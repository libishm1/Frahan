// =============================================================================
// csr_normals_bench -- isolate the neighbour-search bottleneck and benchmark the
// evolved CLEAN-ROOM normal estimator against the v1 hash-map kNN.
//
// Evolution (derived from first principles + MATH_DERIVATIONS.md sec.2):
//   * SoA float coords, shifted by p_min (cache + precision).
//   * counting-sort CSR uniform grid (cell = radius r): O(N) build, no comparisons.
//   * points REORDERED into cell order -> a cell's points are contiguous in RAM.
//   * radius-ball covariance over the 27 neighbour cells: one pass, no k-buffer.
//   * OpenMP over points; closed-form 3x3 symmetric eigen (analytic, no Jacobi loop).
// No CloudCompare/GPL code. Built static with mingw64 (see build_mingw.sh).
//
//   args: --in cloud.ply [--r 0.23] [--maxpts N] [--reorder 1]
// =============================================================================
#include <cstdio>
#include <cstdint>
#include <cstring>
#include <cmath>
#include <vector>
#include <string>
#include <algorithm>
#include <chrono>
#ifdef _OPENMP
#include <omp.h>
#endif

using clk = std::chrono::high_resolution_clock;
static double ms(clk::time_point a, clk::time_point b){ return std::chrono::duration_cast<std::chrono::microseconds>(b-a).count()/1000.0; }

// ---- minimal binary-LE PLY reader (float x/y/z) into SoA ----
static bool readPLY(const char*path, std::vector<float>&X, std::vector<float>&Y, std::vector<float>&Z){
    FILE*f=fopen(path,"rb"); if(!f)return false;
    char ch; long nv=0; int stride=0,xo=-1,yo=-1,zo=-1; std::string line;
    auto tsize=[&](const std::string&t)->int{ if(t=="char"||t=="uchar"||t=="int8"||t=="uint8")return 1; if(t=="short"||t=="ushort"||t=="int16"||t=="uint16")return 2; if(t=="int"||t=="uint"||t=="int32"||t=="uint32"||t=="float"||t=="float32")return 4; if(t=="double"||t=="float64")return 8; return 4; };
    while(fread(&ch,1,1,f)==1){ if(ch=='\n'){ if(line.rfind("element vertex",0)==0) nv=atol(line.c_str()+15);
            else if(line.rfind("property",0)==0){ char tp[64],nm[64]; if(sscanf(line.c_str(),"property %63s %63s",tp,nm)==2){ int sz=tsize(tp); if(!strcmp(nm,"x"))xo=stride; else if(!strcmp(nm,"y"))yo=stride; else if(!strcmp(nm,"z"))zo=stride; stride+=sz; } }
            else if(line=="end_header") break; line.clear(); } else if(ch!='\r') line+=ch; }
    if(xo<0||yo<0||zo<0||nv<=0){ fclose(f); return false; }
    std::vector<char> buf(stride); X.reserve(nv);Y.reserve(nv);Z.reserve(nv);
    for(long i=0;i<nv;i++){ if(fread(buf.data(),1,stride,f)!=(size_t)stride) break; float x,y,z; memcpy(&x,buf.data()+xo,4);memcpy(&y,buf.data()+yo,4);memcpy(&z,buf.data()+zo,4); X.push_back(x);Y.push_back(y);Z.push_back(z); }
    fclose(f); return true;
}

// ---- analytic symmetric 3x3 eigenvalues (Smith 1961) + smallest eigenvector ----
// returns smallest eigenvalue l0, its eigenvector n, and trace for surface variation.
static inline void eigSmallest(double a,double b,double c,double d,double e,double f,
                               double&l0,double n[3],double&tr){
    // matrix [[a,d,e],[d,b,f],[e,f,c]]
    double p1=d*d+e*e+f*f; tr=a+b+c;
    if(p1<1e-30){ // diagonal
        double ev[3]={a,b,c}; int mi=0; if(ev[1]<ev[mi])mi=1; if(ev[2]<ev[mi])mi=2;
        l0=ev[mi]; n[0]=mi==0;n[1]=mi==1;n[2]=mi==2; return; }
    double q=tr/3.0;
    double p2=(a-q)*(a-q)+(b-q)*(b-q)+(c-q)*(c-q)+2*p1; double p=std::sqrt(p2/6.0);
    double Ba=(a-q)/p,Bb=(b-q)/p,Bc=(c-q)/p,Bd=d/p,Be=e/p,Bf=f/p;
    double detB=Ba*(Bb*Bc-Bf*Bf)-Bd*(Bd*Bc-Bf*Be)+Be*(Bd*Bf-Bb*Be);
    double r=detB/2.0; r=r<-1?-1:(r>1?1:r);
    double phi=std::acos(r)/3.0;
    double e0=q+2*p*std::cos(phi+2.0*M_PI/3.0); // smallest
    l0=e0;
    // eigenvector for e0: two rows of (C - e0 I) cross product
    double m00=a-e0,m11=b-e0,m22=c-e0;
    double r0[3]={m00,d,e}, r1[3]={d,m11,f}, r2[3]={e,f,m22};
    auto cross=[&](const double*u,const double*v,double*o){ o[0]=u[1]*v[2]-u[2]*v[1]; o[1]=u[2]*v[0]-u[0]*v[2]; o[2]=u[0]*v[1]-u[1]*v[0]; };
    double c01[3],c02[3],c12[3]; cross(r0,r1,c01); cross(r0,r2,c02); cross(r1,r2,c12);
    double n01=c01[0]*c01[0]+c01[1]*c01[1]+c01[2]*c01[2];
    double n02=c02[0]*c02[0]+c02[1]*c02[1]+c02[2]*c02[2];
    double n12=c12[0]*c12[0]+c12[1]*c12[1]+c12[2]*c12[2];
    const double*best=c01; double bn=n01; if(n02>bn){best=c02;bn=n02;} if(n12>bn){best=c12;bn=n12;}
    if(bn<1e-30){ n[0]=0;n[1]=0;n[2]=1; return; }
    double inv=1.0/std::sqrt(bn); n[0]=best[0]*inv;n[1]=best[1]*inv;n[2]=best[2]*inv;
}

int main(int argc,char**argv){
    std::string inPath; double r=0.23; long maxPts=100000000; int reorder=1; int knn=0;
    for(int i=1;i<argc;i++){ std::string a=argv[i];
        if(a=="--in"&&i+1<argc)inPath=argv[++i]; else if(a=="--r"&&i+1<argc)r=atof(argv[++i]);
        else if(a=="--maxpts"&&i+1<argc)maxPts=atol(argv[++i]); else if(a=="--reorder"&&i+1<argc)reorder=atoi(argv[++i]);
        else if(a=="--knn"&&i+1<argc)knn=atoi(argv[++i]); }
    if(inPath.empty()){ fprintf(stderr,"need --in\n"); return 2; }
    auto t0=clk::now();
    std::vector<float> RX,RY,RZ; if(!readPLY(inPath.c_str(),RX,RY,RZ)){ fprintf(stderr,"read failed\n"); return 3; }
    long nRaw=(long)RX.size();
    // optional uniform stride to budget
    std::vector<float> X,Y,Z;
    if(nRaw>maxPts){ long st=(nRaw+maxPts-1)/maxPts; for(long i=0;i<nRaw;i+=st){X.push_back(RX[i]);Y.push_back(RY[i]);Z.push_back(RZ[i]);} }
    else { X=std::move(RX);Y=std::move(RY);Z=std::move(RZ); }
    int N=(int)X.size();
    auto tRead=clk::now();

    // bbox + shift to local origin
    float mnx=1e30f,mny=1e30f,mnz=1e30f,mxx=-1e30f,mxy=-1e30f,mxz=-1e30f;
    for(int i=0;i<N;i++){ mnx=std::min(mnx,X[i]);mny=std::min(mny,Y[i]);mnz=std::min(mnz,Z[i]); mxx=std::max(mxx,X[i]);mxy=std::max(mxy,Y[i]);mxz=std::max(mxz,Z[i]); }
    for(int i=0;i<N;i++){ X[i]-=mnx;Y[i]-=mny;Z[i]-=mnz; }
    double inv_r=1.0/r;
    int nx=std::max(1,(int)((mxx-mnx)*inv_r)+1), ny=std::max(1,(int)((mxy-mny)*inv_r)+1), nz=std::max(1,(int)((mxz-mnz)*inv_r)+1);
    long long ncell=(long long)nx*ny*nz;
    auto cellOf=[&](int i)->long long{ int cx=(int)(X[i]*inv_r),cy=(int)(Y[i]*inv_r),cz=(int)(Z[i]*inv_r);
        if(cx>=nx)cx=nx-1; if(cy>=ny)cy=ny-1; if(cz>=nz)cz=nz-1; return ((long long)cx*ny+cy)*nz+cz; };

    // counting-sort CSR
    std::vector<int> cellStart(ncell+1,0);
    std::vector<long long> ci(N);
    for(int i=0;i<N;i++){ ci[i]=cellOf(i); cellStart[ci[i]+1]++; }
    for(long long c=0;c<ncell;c++) cellStart[c+1]+=cellStart[c];
    std::vector<int> sorted(N); { std::vector<int> cur(cellStart.begin(),cellStart.end()-1);
        for(int i=0;i<N;i++){ sorted[cur[ci[i]]++]=i; } }
    // reorder coords into cell order for cache coherence
    std::vector<float> Xs,Ys,Zs;
    const float *px,*py,*pz;
    if(reorder){ Xs.resize(N);Ys.resize(N);Zs.resize(N);
        for(int s=0;s<N;s++){ int i=sorted[s]; Xs[s]=X[i];Ys[s]=Y[i];Zs[s]=Z[i]; }
        px=Xs.data();py=Ys.data();pz=Zs.data(); }
    else { px=X.data();py=Y.data();pz=Z.data(); }
    auto tBuild=clk::now();

    std::vector<float> NX(N),NY(N),NZ(N),SIG(N); double r2=r*r;
    long long avgNb=0;
    if(knn<=0){
    // radius normals: for each point, scan 27 cells, accumulate covariance within r^2
    #pragma omp parallel for schedule(dynamic,4096) reduction(+:avgNb)
    for(int s=0;s<N;s++){
        double qx = reorder? Xs[s]:X[s], qy = reorder? Ys[s]:Y[s], qz = reorder? Zs[s]:Z[s];
        int cx=(int)(qx*inv_r),cy=(int)(qy*inv_r),cz=(int)(qz*inv_r);
        if(cx>=nx)cx=nx-1; if(cy>=ny)cy=ny-1; if(cz>=nz)cz=nz-1;
        double sx=0,sy=0,sz=0; int cnt=0;
        int x0=cx>0?cx-1:0,x1=cx<nx-1?cx+1:nx-1,y0=cy>0?cy-1:0,y1=cy<ny-1?cy+1:ny-1,z0=cz>0?cz-1:0,z1=cz<nz-1?cz+1:nz-1;
        for(int ax=x0;ax<=x1;ax++)for(int ay=y0;ay<=y1;ay++)for(int az=z0;az<=z1;az++){
            long long cc=((long long)ax*ny+ay)*nz+az; int b=cellStart[cc],e=cellStart[cc+1];
            for(int t=b;t<e;t++){ double dx=px[t]-qx,dy=py[t]-qy,dz=pz[t]-qz; if(dx*dx+dy*dy+dz*dz<=r2){ sx+=px[t];sy+=py[t];sz+=pz[t];cnt++; } } }
        if(cnt<3){ NX[s]=0;NY[s]=0;NZ[s]=1;SIG[s]=1.0/3; continue; }
        double cxm=sx/cnt,cym=sy/cnt,czm=sz/cnt;
        double cxx=0,cyy=0,czz=0,cxy=0,cxz=0,cyz=0;
        for(int ax=x0;ax<=x1;ax++)for(int ay=y0;ay<=y1;ay++)for(int az=z0;az<=z1;az++){
            long long cc=((long long)ax*ny+ay)*nz+az; int b=cellStart[cc],e=cellStart[cc+1];
            for(int t=b;t<e;t++){ double dx=px[t]-qx,dy=py[t]-qy,dz=pz[t]-qz; if(dx*dx+dy*dy+dz*dz<=r2){ double ux=px[t]-cxm,uy=py[t]-cym,uz=pz[t]-czm; cxx+=ux*ux;cyy+=uy*uy;czz+=uz*uz;cxy+=ux*uy;cxz+=ux*uz;cyz+=uy*uz; } } }
        double l0,nv[3],tr; eigSmallest(cxx,cyy,czz,cxy,cxz,cyz,l0,nv,tr);
        if(nv[2]>0){nv[0]=-nv[0];nv[1]=-nv[1];nv[2]=-nv[2];}
        NX[s]=(float)nv[0];NY[s]=(float)nv[1];NZ[s]=(float)nv[2]; SIG[s]=(float)(tr>1e-20? l0/tr:0); avgNb+=cnt;
    }
    } else {
    // kNN normals over the CSR grid: expanding Chebyshev shells + sorted k-buffer.
    // Consistent scale on non-uniform clouds (the right semantics here).
    int K=knn; int maxRing=std::max(nx,std::max(ny,nz))+1;
    #pragma omp parallel for schedule(dynamic,2048) reduction(+:avgNb)
    for(int s=0;s<N;s++){
        double qx = reorder? Xs[s]:X[s], qy = reorder? Ys[s]:Y[s], qz = reorder? Zs[s]:Z[s];
        int cx=(int)(qx*inv_r),cy=(int)(qy*inv_r),cz=(int)(qz*inv_r);
        if(cx>=nx)cx=nx-1; if(cy>=ny)cy=ny-1; if(cz>=nz)cz=nz-1;
        std::vector<double> bd(K,1e300); std::vector<int> bi(K,-1); int found=0;
        for(int ring=0;ring<=maxRing;ring++){
            int ax0=cx-ring<0?0:cx-ring, ax1=cx+ring>=nx?nx-1:cx+ring;
            int ay0=cy-ring<0?0:cy-ring, ay1=cy+ring>=ny?ny-1:cy+ring;
            int az0=cz-ring<0?0:cz-ring, az1=cz+ring>=nz?nz-1:cz+ring;
            for(int ax=ax0;ax<=ax1;ax++)for(int ay=ay0;ay<=ay1;ay++)for(int az=az0;az<=az1;az++){
                if(std::max(std::abs(ax-cx),std::max(std::abs(ay-cy),std::abs(az-cz)))!=ring) continue;
                long long cc=((long long)ax*ny+ay)*nz+az; int b=cellStart[cc],e=cellStart[cc+1];
                for(int t=b;t<e;t++){ double dx=px[t]-qx,dy=py[t]-qy,dz=pz[t]-qz; double d=dx*dx+dy*dy+dz*dz;
                    if(d<bd[K-1] && d>1e-18){ int pos=K-1; while(pos>0&&bd[pos-1]>d){bd[pos]=bd[pos-1];bi[pos]=bi[pos-1];pos--;} bd[pos]=d;bi[pos]=t; if(found<K)found++; } } }
            double ringMin=(double)ring*r; if(found>=K && ringMin*ringMin>=bd[K-1]) break;
        }
        if(found<3){ NX[s]=0;NY[s]=0;NZ[s]=1;SIG[s]=1.0/3; continue; }
        double cxm=0,cym=0,czm=0; for(int j=0;j<found;j++){int t=bi[j];cxm+=px[t];cym+=py[t];czm+=pz[t];} cxm/=found;cym/=found;czm/=found;
        double cxx=0,cyy=0,czz=0,cxy=0,cxz=0,cyz=0;
        for(int j=0;j<found;j++){int t=bi[j]; double ux=px[t]-cxm,uy=py[t]-cym,uz=pz[t]-czm; cxx+=ux*ux;cyy+=uy*uy;czz+=uz*uz;cxy+=ux*uy;cxz+=ux*uz;cyz+=uy*uz;}
        double l0,nv[3],tr; eigSmallest(cxx,cyy,czz,cxy,cxz,cyz,l0,nv,tr);
        if(nv[2]>0){nv[0]=-nv[0];nv[1]=-nv[1];nv[2]=-nv[2];}
        NX[s]=(float)nv[0];NY[s]=(float)nv[1];NZ[s]=(float)nv[2]; SIG[s]=(float)(tr>1e-20? l0/tr:0); avgNb+=found;
    }
    }
    auto tNorm=clk::now();

    fprintf(stderr,"{\n  \"engine\": \"%s\", \"points\": %d, \"radius\": %.4f, \"knn\": %d, \"reorder\": %d, \"cells\": %lld, \"avg_neighbours\": %.1f,\n",knn>0?"csr_knn":"csr_radius",N,r,knn,reorder,ncell,(double)avgNb/std::max(1,N));
    fprintf(stderr,"  \"ms_read\": %.1f, \"ms_build\": %.1f, \"ms_normals\": %.1f, \"ms_total\": %.1f\n}\n",ms(t0,tRead),ms(tRead,tBuild),ms(tBuild,tNorm),ms(t0,tNorm));
    return 0;
}
