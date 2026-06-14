// =============================================================================
// frahan_discontinuity_worker -- out-of-process point-cloud discontinuity
// extraction (planar facets + joint sets) for the Frahan "Discontinuity Sets
// (Async)" Grasshopper component.
//
// CLEAN-ROOM. Implemented from the published math (Pauly 2002 surface
// variation; Dewez et al. 2016 FACETS; Riquelme et al. 2014/2015 DSE) with NO
// dependency on CloudCompare / CCCoreLib / qFACETS (GPL-3.0) source. No external
// libraries: hand-rolled grid kNN, 3x3 Jacobi eigensolver, binary-PLY read/write
// and JSON write. Free of GPL; usable under Frahan's own licence.
//
//   args: --in cloud.ply --out <dir> [--k 24] [--angle 12] [--band 2.5]
//         [--seedeta 0.06] [--minfacet 40] [--bw 15] [--merge 8] [--minset 4]
//         [--voxel 0] [--maxpts 1200000] [--segply]
//   writes: <dir>/discontinuity.json  (+ <dir>/segmented.ply if --segply)
// Build: see build_mingw.sh (static mingw64 g++ -O3 -fopenmp).
// =============================================================================
#include <cstdio>
#include <cstdint>
#include <cstring>
#include <cmath>
#include <vector>
#include <string>
#include <algorithm>
#include <unordered_map>
#include <chrono>
#ifdef _OPENMP
#include <omp.h>
#endif

struct V3 { double x, y, z; };
static inline V3 sub(const V3&a,const V3&b){ return {a.x-b.x,a.y-b.y,a.z-b.z}; }
static inline double dot(const V3&a,const V3&b){ return a.x*b.x+a.y*b.y+a.z*b.z; }
static inline double d2(const V3&a,const V3&b){ double dx=a.x-b.x,dy=a.y-b.y,dz=a.z-b.z; return dx*dx+dy*dy+dz*dz; }

// ---- analytic symmetric 3x3 eigen (Smith 1961): smallest eigenvalue, its
// eigenvector, and trace. No iteration -- closed-form, the evolution over Jacobi
// (MATH_DERIVATIONS.md sec.1). Matrix = [[a,d,e],[d,b,f],[e,f,c]].
static inline void eigSmallest(double a,double b,double c,double d,double e,double f,
                               double&l0,double n[3],double&tr){
    double p1=d*d+e*e+f*f; tr=a+b+c;
    if(p1<1e-30){ double ev[3]={a,b,c}; int mi=0; if(ev[1]<ev[mi])mi=1; if(ev[2]<ev[mi])mi=2;
        l0=ev[mi]; n[0]=(mi==0);n[1]=(mi==1);n[2]=(mi==2); return; }
    double q=tr/3.0;
    double p2=(a-q)*(a-q)+(b-q)*(b-q)+(c-q)*(c-q)+2*p1; double p=std::sqrt(p2/6.0);
    double Ba=(a-q)/p,Bb=(b-q)/p,Bc=(c-q)/p,Bd=d/p,Be=e/p,Bf=f/p;
    double detB=Ba*(Bb*Bc-Bf*Bf)-Bd*(Bd*Bc-Bf*Be)+Be*(Bd*Bf-Bb*Be);
    double r=detB/2.0; r=r<-1?-1:(r>1?1:r);
    double phi=std::acos(r)/3.0;
    l0=q+2*p*std::cos(phi+2.0*M_PI/3.0); // smallest of the three
    double e0=l0, m00=a-e0,m11=b-e0,m22=c-e0;
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
static inline V3 lowerHemi(V3 n){ double L=std::sqrt(dot(n,n)); if(L<1e-20)return {0,0,-1}; n.x/=L;n.y/=L;n.z/=L;
    if(n.z>1e-12){n.x=-n.x;n.y=-n.y;n.z=-n.z;} else if(std::fabs(n.z)<=1e-12){ if(n.x<-1e-12||(std::fabs(n.x)<=1e-12&&n.y<0)){n.x=-n.x;n.y=-n.y;n.z=-n.z;} } return n; }
static void dipDir(const V3&nn,double&dip,double&dd){ V3 n=lowerHemi(nn); dip=std::acos(std::min(1.0,std::fabs(n.z)))*180.0/M_PI;
    if(std::fabs(n.x)<1e-12&&std::fabs(n.y)<1e-12) dd=0; else { dd=std::atan2(n.x,n.y)*180.0/M_PI; dd=std::fmod(dd+360.0,360.0);} }
static inline double axialDeg(const V3&a,const V3&b){ return std::acos(std::min(1.0,std::fabs(dot(a,b))))*180.0/M_PI; }

// ---- counting-sort CSR uniform grid (Hoetzlein 2014 FRNN; MATH_DERIVATIONS sec.2)
// O(N) build, no comparisons; REORDERS the caller's point array into cell order so
// a cell's points are contiguous in RAM (cache-coherent kNN). Beats the hash-map
// grid AND the KD-trees of Open3D/PDAL/CloudCompare on the k=24 normal op.
struct CsrGrid {
    std::vector<V3>& P; double cell, minx,miny,minz; int nx,ny,nz; long long ncell;
    std::vector<int> cellStart; // size ncell+1, prefix-summed
    CsrGrid(std::vector<V3>&pts,double cellWanted,long long maxCells=200000000LL):P(pts){
        int N=(int)P.size();
        double maxx=-1e300,maxy=-1e300,maxz=-1e300; minx=miny=minz=1e300;
        for(auto&q:P){ minx=std::min(minx,q.x);miny=std::min(miny,q.y);minz=std::min(minz,q.z);
                       maxx=std::max(maxx,q.x);maxy=std::max(maxy,q.y);maxz=std::max(maxz,q.z); }
        cell=cellWanted<1e-9?1e-9:cellWanted;
        for(;;){ nx=std::max(1,(int)((maxx-minx)/cell)+1); ny=std::max(1,(int)((maxy-miny)/cell)+1); nz=std::max(1,(int)((maxz-minz)/cell)+1);
            if((double)nx*ny*nz<=(double)maxCells) break; cell*=1.26; } // coarsen to bound memory
        ncell=(long long)nx*ny*nz;
        std::vector<long long> ci(N); cellStart.assign(ncell+1,0);
        for(int i=0;i<N;i++){ ci[i]=cellOf(P[i]); cellStart[ci[i]+1]++; }
        for(long long c=0;c<ncell;c++) cellStart[c+1]+=cellStart[c];
        std::vector<int> cur(cellStart.begin(),cellStart.end()-1);
        std::vector<V3> tmp(N);
        for(int i=0;i<N;i++){ int pos=cur[ci[i]]++; tmp[pos]=P[i]; }
        P.swap(tmp); // P now in cell order; cellStart indexes it directly
    }
    inline void clampc(int&cx,int&cy,int&cz)const{ if(cx<0)cx=0; if(cy<0)cy=0; if(cz<0)cz=0; if(cx>=nx)cx=nx-1; if(cy>=ny)cy=ny-1; if(cz>=nz)cz=nz-1; }
    inline long long cellOf(const V3&p)const{ int cx=(int)((p.x-minx)/cell),cy=(int)((p.y-miny)/cell),cz=(int)((p.z-minz)/cell); clampc(cx,cy,cz); return ((long long)cx*ny+cy)*nz+cz; }
    void knn(int s,int K,std::vector<int>&out)const{
        const V3&q=P[s]; int cx=(int)((q.x-minx)/cell),cy=(int)((q.y-miny)/cell),cz=(int)((q.z-minz)/cell); clampc(cx,cy,cz);
        std::vector<double> bd(K,1e300); std::vector<int> bi(K,-1); int found=0;
        int maxRing=std::max(nx,std::max(ny,nz));
        for(int ring=0;ring<=maxRing;ring++){
            int ax0=cx-ring<0?0:cx-ring, ax1=cx+ring>=nx?nx-1:cx+ring;
            int ay0=cy-ring<0?0:cy-ring, ay1=cy+ring>=ny?ny-1:cy+ring;
            int az0=cz-ring<0?0:cz-ring, az1=cz+ring>=nz?nz-1:cz+ring;
            for(int ax=ax0;ax<=ax1;ax++)for(int ay=ay0;ay<=ay1;ay++)for(int az=az0;az<=az1;az++){
                if(std::max(std::abs(ax-cx),std::max(std::abs(ay-cy),std::abs(az-cz)))!=ring) continue;
                long long cc=((long long)ax*ny+ay)*nz+az; int b=cellStart[cc],e=cellStart[cc+1];
                for(int t=b;t<e;t++){ if(t==s)continue; double d=d2(q,P[t]);
                    if(d<bd[K-1]){ int pos=K-1; while(pos>0&&bd[pos-1]>d){bd[pos]=bd[pos-1];bi[pos]=bi[pos-1];pos--;} bd[pos]=d;bi[pos]=t; if(found<K)found++; } } }
            double rmin=(double)ring*cell; if(found>=K && rmin*rmin>=bd[K-1]) break;
        }
        out.clear(); for(int i=0;i<K&&bi[i]>=0;i++) out.push_back(bi[i]);
    }
};

// PCA over indices -> normal(lower hemi), surface variation eta, centroid
static void pca(const std::vector<V3>&P,const std::vector<int>&idx,V3&nrm,double&eta,V3&ctr){
    int m=(int)idx.size(); double cx=0,cy=0,cz=0; for(int i=0;i<m;i++){auto&q=P[idx[i]];cx+=q.x;cy+=q.y;cz+=q.z;} cx/=m;cy/=m;cz/=m;
    double xx=0,xy=0,xz=0,yy=0,yz=0,zz=0; for(int i=0;i<m;i++){auto&q=P[idx[i]];double ax=q.x-cx,ay=q.y-cy,az=q.z-cz;xx+=ax*ax;xy+=ax*ay;xz+=ax*az;yy+=ay*ay;yz+=ay*az;zz+=az*az;}
    double l0,nv[3],tr; eigSmallest(xx,yy,zz,xy,xz,yz,l0,nv,tr); nrm=lowerHemi({nv[0],nv[1],nv[2]});
    eta=tr>1e-20?l0/tr:0; ctr={cx,cy,cz};
}

// ---- minimal binary-LE PLY reader (float x/y/z) ----
static bool readPLY(const char*path,std::vector<V3>&out){
    FILE*f=fopen(path,"rb"); if(!f)return false;
    std::string hdr; char ch; long nv=0; int stride=0,xo=-1,yo=-1,zo=-1; std::string line;
    auto tsize=[&](const std::string&t)->int{ if(t=="char"||t=="uchar"||t=="int8"||t=="uint8")return 1; if(t=="short"||t=="ushort"||t=="int16"||t=="uint16")return 2; if(t=="int"||t=="uint"||t=="int32"||t=="uint32"||t=="float"||t=="float32")return 4; if(t=="double"||t=="float64")return 8; return 4; };
    while(fread(&ch,1,1,f)==1){ if(ch=='\n'){ if(line.rfind("element vertex",0)==0) nv=atol(line.c_str()+15);
            else if(line.rfind("property",0)==0){ char tp[64],nm[64]; if(sscanf(line.c_str(),"property %63s %63s",tp,nm)==2){ int sz=tsize(tp); if(!strcmp(nm,"x"))xo=stride; else if(!strcmp(nm,"y"))yo=stride; else if(!strcmp(nm,"z"))zo=stride; stride+=sz; } }
            else if(line=="end_header") break; line.clear(); } else if(ch!='\r') line+=ch; }
    if(xo<0||yo<0||zo<0||nv<=0){ fclose(f); return false; }
    std::vector<char> buf(stride);
    out.reserve(nv);
    for(long i=0;i<nv;i++){ if(fread(buf.data(),1,stride,f)!=(size_t)stride) break; float x,y,z; memcpy(&x,buf.data()+xo,4);memcpy(&y,buf.data()+yo,4);memcpy(&z,buf.data()+zo,4); out.push_back({x,y,z}); }
    fclose(f); return true;
}
static void writeSegPLY(const char*path,const std::vector<V3>&P,const std::vector<unsigned char>&rgb,V3 origin){
    FILE*f=fopen(path,"wb"); if(!f)return; fprintf(f,"ply\nformat binary_little_endian 1.0\nelement vertex %zu\nproperty float x\nproperty float y\nproperty float z\nproperty uchar red\nproperty uchar green\nproperty uchar blue\nend_header\n",P.size());
    for(size_t i=0;i<P.size();i++){ float x=(float)(P[i].x+origin.x),y=(float)(P[i].y+origin.y),z=(float)(P[i].z+origin.z); fwrite(&x,4,1,f);fwrite(&y,4,1,f);fwrite(&z,4,1,f); fwrite(&rgb[i*3],1,3,f); } fclose(f);
}

int main(int argc,char**argv){
    std::string inPath,outDir; int K=24; double angle=12,bandF=2.5,seedEta=0.06,bw=15,merge=8,voxel=0,minShare=0.02; int minFacet=40,minSet=4; long maxPts=1200000; bool segply=false;
    for(int i=1;i<argc;i++){ std::string a=argv[i]; auto nx=[&](double&v){ if(i+1<argc)v=atof(argv[++i]); }; auto ni=[&](int&v){ if(i+1<argc)v=atoi(argv[++i]); };
        if(a=="--in"&&i+1<argc)inPath=argv[++i]; else if(a=="--out"&&i+1<argc)outDir=argv[++i]; else if(a=="--k")ni(K); else if(a=="--angle")nx(angle); else if(a=="--band")nx(bandF); else if(a=="--seedeta")nx(seedEta); else if(a=="--minfacet")ni(minFacet); else if(a=="--bw")nx(bw); else if(a=="--merge")nx(merge); else if(a=="--minset")ni(minSet); else if(a=="--minshare")nx(minShare); else if(a=="--voxel")nx(voxel); else if(a=="--maxpts"){ if(i+1<argc)maxPts=atol(argv[++i]); } else if(a=="--segply")segply=true; }
    if(inPath.empty()||outDir.empty()){ fprintf(stderr,"need --in and --out\n"); return 2; }
    auto t0=std::chrono::high_resolution_clock::now();
    std::vector<V3> raw; if(!readPLY(inPath.c_str(),raw)){ fprintf(stderr,"read failed\n"); return 3; }
    long nRaw=(long)raw.size();
    // downsample to the work budget: explicit voxel grid, else uniform stride to ~maxPts.
    // (volume-based auto-voxel is wrong for a thin surface -> over-decimates.)
    std::vector<V3> P;
    if(voxel>0){ std::unordered_map<long long,int> vh; vh.reserve(nRaw); double mnx=1e300,mny=1e300,mnz=1e300; for(auto&q:raw){mnx=std::min(mnx,q.x);mny=std::min(mny,q.y);mnz=std::min(mnz,q.z);} for(auto&q:raw){ long cx=(long)((q.x-mnx)/voxel),cy=(long)((q.y-mny)/voxel),cz=(long)((q.z-mnz)/voxel); long long k=((long long)(cx&0x1FFFFF)<<42)|((long long)(cy&0x1FFFFF)<<21)|(long long)(cz&0x1FFFFF); if(vh.find(k)==vh.end()){ vh[k]=1; P.push_back(q);} } }
    else if(nRaw>maxPts){ long st=(nRaw+maxPts-1)/maxPts; for(long i=0;i<nRaw;i+=st) P.push_back(raw[i]); }
    else P=raw;
    raw.clear(); raw.shrink_to_fit();
    int N=(int)P.size(); if(N<K+1){ fprintf(stderr,"too few points\n"); return 4; }
    auto tRead=std::chrono::high_resolution_clock::now();

    // Coordinates are kept absolute. Translation-invariance + good conditioning come
    // from the region-grow accumulating SEED-RELATIVE covariance and the normals PCA
    // centring each neighbourhood -- so no global shift is needed (and a global shift
    // changed nothing but added an output add-back). origin stays zero. NOTE: a UTM
    // cloud stored as float32 loses sub-decimetre precision in the FILE itself (an
    // upstream issue); ingest such clouds with a local offset or from LAZ scale+offset.
    V3 origin={0,0,0};

    // spacing + grid
    double bbd; { double maxx=-1e300,maxy=-1e300,maxz=-1e300,mnx=1e300,mny=1e300,mnz=1e300; for(auto&q:P){mnx=std::min(mnx,q.x);mny=std::min(mny,q.y);mnz=std::min(mnz,q.z);maxx=std::max(maxx,q.x);maxy=std::max(maxy,q.y);maxz=std::max(maxz,q.z);} bbd=std::sqrt((maxx-mnx)*(maxx-mnx)+(maxy-mny)*(maxy-mny)+(maxz-mnz)*(maxz-mnz)); }
    double coarse=std::max(1e-6,bbd/std::cbrt((double)N)); double spacing=coarse;
    { CsrGrid g0(P,coarse); std::vector<double> sd; for(int i=0;i<N;i+=std::max(1,N/800)){ std::vector<int> nb; g0.knn(i,1,nb); if(!nb.empty()) sd.push_back(std::sqrt(d2(P[i],P[nb[0]]))); }
      std::sort(sd.begin(),sd.end()); spacing=sd.empty()?coarse:std::max(1e-9,sd[sd.size()/2]); }
    // fine grid: size the cell so the home + first ring already hold a few * K
    // points (kNN terminates in ~2 rings). For a ~surface cloud occupancy ~
    // (cell/spacing)^2, so cell = spacing*sqrt(6K) puts ~6K in the home cell --
    // the benchmarked U-shaped optimum (too fine -> empty-cell shell traversal;
    // too coarse -> candidate over-scan). ~12*spacing at K=24.
    double cellMul=std::min(16.0,std::max(4.0,std::sqrt(6.0*K)));
    CsrGrid grid(P,std::max(1e-6,cellMul*spacing),300000000LL);

    // normals + eta (parallel); cache the kNN adjacency for reuse in region-grow.
    std::vector<V3> nrm(N); std::vector<double> eta(N);
    std::vector<int> knnFlat((size_t)N*K,-1);
    #pragma omp parallel for schedule(dynamic,2048)
    for(int i=0;i<N;i++){ std::vector<int> nb; grid.knn(i,K,nb); int kc=(int)std::min((size_t)K,nb.size()); for(int t=0;t<kc;t++) knnFlat[(size_t)i*K+t]=nb[t]; nb.push_back(i); V3 nn; double e; V3 c; pca(P,nb,nn,e,c); nrm[i]=nn; eta[i]=e; }
    auto tNorm=std::chrono::high_resolution_clock::now();

    // region-grow facets
    double cosMax=std::cos(angle*M_PI/180), band=bandF*spacing;
    std::vector<int> ord(N); for(int i=0;i<N;i++)ord[i]=i; std::sort(ord.begin(),ord.end(),[&](int a,int b){return eta[a]<eta[b];});
    std::vector<char> vis(N,0); std::vector<V3> fN,fC; std::vector<std::vector<int>> fI;
    std::vector<int> q; q.reserve(1024);
    for(int oi=0;oi<N;oi++){ int seed=ord[oi]; if(vis[seed]||eta[seed]>seedEta)continue;
        std::vector<int> mem; V3 fn=nrm[seed],fc=P[seed]; q.clear(); q.push_back(seed); vis[seed]=1; size_t head=0; int sf=0;
        // incremental covariance accumulated RELATIVE TO THE SEED point so a refit
        // is O(1) AND exactly translation-invariant + well-conditioned (the diffs
        // are facet-scale, not absolute position -> no catastrophic cancellation).
        const V3 ref=P[seed];
        double Sx=0,Sy=0,Sz=0,Sxx=0,Syy=0,Szz=0,Sxy=0,Sxz=0,Syz=0; long mc=0;
        auto refit=[&](){ if(mc<3)return; double cx=Sx/mc,cy=Sy/mc,cz=Sz/mc;
            double xx=Sxx-mc*cx*cx,yy=Syy-mc*cy*cy,zz=Szz-mc*cz*cz,xy=Sxy-mc*cx*cy,xz=Sxz-mc*cx*cz,yz=Syz-mc*cy*cz;
            double l0,nv[3],tr; eigSmallest(xx,yy,zz,xy,xz,yz,l0,nv,tr); fn=lowerHemi({nv[0],nv[1],nv[2]}); fc={ref.x+cx,ref.y+cy,ref.z+cz}; };
        while(head<q.size()){ int j=q[head++]; mem.push_back(j); double dx=P[j].x-ref.x,dy=P[j].y-ref.y,dz=P[j].z-ref.z;
            Sx+=dx;Sy+=dy;Sz+=dz; Sxx+=dx*dx;Syy+=dy*dy;Szz+=dz*dz; Sxy+=dx*dy;Sxz+=dx*dz;Syz+=dy*dz; mc++;
            if(++sf>=64&&mc>=16){ refit(); sf=0; }
            const int* nbp=&knnFlat[(size_t)j*K]; for(int t=0;t<K;t++){ int x=nbp[t]; if(x<0)break; if(vis[x])continue; if(std::fabs(dot(nrm[x],fn))<cosMax)continue; V3 v=sub(P[x],fc); if(std::fabs(dot(v,fn))>band)continue; vis[x]=1; q.push_back(x); } }
        if((int)mem.size()<minFacet)continue; refit(); fN.push_back(fn); fC.push_back(fc); fI.push_back(mem); }
    int F=(int)fN.size(); auto tFacet=std::chrono::high_resolution_clock::now();

    // Watson axial mean-shift on facet poles (parallel over seeds; each seed
    // hill-climbs independently to a mode, so write into a pre-sized array).
    std::vector<double> wt(F); for(int i=0;i<F;i++) wt[i]=std::max((size_t)1,fI[i].size());
    double sinbw=std::max(1e-4,std::sin(bw*M_PI/180)), kappa=1.0/(sinbw*sinbw);
    // cap seeds: a few hundred is plenty to discover every mode of a handful of
    // joint sets; the cost is O(seeds * iters * F) so this is the main lever.
    int maxSeeds=256; int stride2=std::max(1,(F+maxSeeds-1)/maxSeeds);
    int nSeed=(F+stride2-1)/stride2; std::vector<V3> seedModes(nSeed);
    #pragma omp parallel for schedule(dynamic,4)
    for(int si=0;si<nSeed;si++){ int s=si*stride2; V3 m=fN[s];
        for(int it=0;it<60;it++){ double sx=0,sy=0,sz=0; for(int j=0;j<F;j++){ double d=dot(m,fN[j]); double w=wt[j]*std::exp(kappa*(d*d-1)); double sg=d>=0?1:-1; sx+=w*sg*fN[j].x;sy+=w*sg*fN[j].y;sz+=w*sg*fN[j].z; } double L=std::sqrt(sx*sx+sy*sy+sz*sz); if(L<1e-12)break; V3 mn={sx/L,sy/L,sz/L}; if(axialDeg(mn,m)<0.02){m=mn;break;} m=mn; }
        seedModes[si]=lowerHemi(m); }
    std::vector<V3> modes(seedModes.begin(),seedModes.end());
    std::vector<V3> sp; for(auto&md:modes){ bool mg=false; for(auto&s:sp) if(axialDeg(md,s)<merge){mg=true;break;} if(!mg)sp.push_back(md); }
    std::vector<int> asg(F,-1); for(int i=0;i<F;i++){ int b=-1; double ba=1e9; for(int k=0;k<(int)sp.size();k++){ double a=axialDeg(fN[i],sp[k]); if(a<ba){ba=a;b=k;} } asg[i]=b; }
    std::vector<int> sc(sp.size(),0),spt(sp.size(),0); long facetPts=0; for(int i=0;i<F;i++){ sc[asg[i]]++; spt[asg[i]]+=(int)fI[i].size(); facetPts+=(long)fI[i].size(); }
    // keep a set only if it has >= minSet facets AND >= minShare of the facet
    // points (drops spurious micro-sets; stabilises the count across densities).
    std::vector<int> keep; for(int k=0;k<(int)sp.size();k++) if(sc[k]>=minSet && (facetPts<=0 || (double)spt[k]/facetPts>=minShare)) keep.push_back(k);
    std::sort(keep.begin(),keep.end(),[&](int a,int b){return spt[a]>spt[b];});
    auto tAll=std::chrono::high_resolution_clock::now();
    auto ms=[&](std::chrono::high_resolution_clock::time_point a,std::chrono::high_resolution_clock::time_point b){ return std::chrono::duration_cast<std::chrono::milliseconds>(b-a).count(); };

    // per-set pole/dip/dipdir/spacing
    std::string js="{\n"; char buf[512];
    snprintf(buf,sizeof buf,"  \"points_in\": %ld, \"points_used\": %d, \"voxel\": %.4f, \"spacing\": %.5f, \"facets\": %d, \"sets\": %d,\n",nRaw,N,voxel,spacing,F,(int)keep.size()); js+=buf;
    snprintf(buf,sizeof buf,"  \"ms_read\": %lld, \"ms_normals\": %lld, \"ms_facets\": %lld, \"ms_sets\": %lld, \"ms_total\": %lld,\n",(long long)ms(t0,tRead),(long long)ms(tRead,tNorm),(long long)ms(tNorm,tFacet),(long long)ms(tFacet,tAll),(long long)ms(t0,tAll)); js+=buf;
    js+="  \"sets\": [\n";
    for(size_t ki=0;ki<keep.size();ki++){ int k=keep[ki];
        double sx=0,sy=0,sz=0; for(int i=0;i<F;i++) if(asg[i]==k){ double d=dot(sp[k],fN[i]); double sg=d>=0?1:-1; sx+=wt[i]*sg*fN[i].x;sy+=wt[i]*sg*fN[i].y;sz+=wt[i]*sg*fN[i].z; }
        V3 pole=lowerHemi({sx,sy,sz}); double dip,dd; dipDir(pole,dip,dd);
        // family-constrained normal spacing
        std::vector<double> offs; for(int i=0;i<F;i++) if(asg[i]==k) offs.push_back(dot(fC[i],pole)); std::sort(offs.begin(),offs.end());
        double sp_mean=0; int gaps=0; for(size_t i=1;i<offs.size();i++){ double gp=offs[i]-offs[i-1]; if(gp>1e-9){sp_mean+=gp;gaps++;} } sp_mean=gaps?sp_mean/gaps:0;
        snprintf(buf,sizeof buf,"    {\"id\": %zu, \"dip\": %.2f, \"dipdir\": %.2f, \"pole\": [%.5f,%.5f,%.5f], \"facets\": %d, \"point_share\": %.4f, \"spacing\": %.4f}%s\n",ki+1,dip,dd,pole.x,pole.y,pole.z,sc[k],facetPts? (double)spt[k]/facetPts:0,sp_mean,ki+1<keep.size()?",":""); js+=buf; }
    js+="  ]\n}\n";
    std::string jp=outDir+"/discontinuity.json"; FILE*jf=fopen(jp.c_str(),"wb"); if(jf){fwrite(js.data(),1,js.size(),jf);fclose(jf);}

    std::vector<int> kmap(sp.size(),-1); for(size_t ki=0;ki<keep.size();ki++) kmap[keep[ki]]=(int)ki;
    // per-facet table (centroid, lower-hemi pole, 1-based kept set id or 0, point count)
    // -> density stereonet + facet-level visualisation.
    { std::string fp=outDir+"/facets.csv"; FILE*ff=fopen(fp.c_str(),"wb");
      if(ff){ fprintf(ff,"cx,cy,cz,nx,ny,nz,set,npts\n");
        for(int f=0;f<F;f++){ int ki=kmap[asg[f]]; V3 nn=lowerHemi(fN[f]);
            fprintf(ff,"%.4f,%.4f,%.4f,%.5f,%.5f,%.5f,%d,%d\n",fC[f].x+origin.x,fC[f].y+origin.y,fC[f].z+origin.z,nn.x,nn.y,nn.z,ki<0?0:ki+1,(int)fI[f].size()); }
        fclose(ff); } }

    if(segply){
        const unsigned char pal[10][3]={{220,50,47},{38,139,210},{133,153,0},{181,137,0},{211,54,130},{42,161,152},{203,75,22},{108,113,196},{0,160,90},{150,100,40}};
        std::vector<unsigned char> rgb(N*3,105); for(int f=0;f<F;f++){ int ki=kmap[asg[f]]; if(ki<0)continue; const unsigned char*c=pal[ki%10]; for(int pi:fI[f]){ rgb[pi*3]=c[0];rgb[pi*3+1]=c[1];rgb[pi*3+2]=c[2]; } }
        std::string pp=outDir+"/segmented.ply"; writeSegPLY(pp.c_str(),P,rgb,origin); }
    fprintf(stderr,"%s",js.c_str());
    return 0;
}
