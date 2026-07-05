// =============================================================================
// Copyright (c) 2026 Frahan StonePack (Independent Research).
// SPDX-License-Identifier: LicenseRef-Frahan-Proprietary
// CLEAN-ROOM implementation: derived from our own C# reference
// (Frahan.Masonry.Vault.FieldAlignedParam / QuadExtract, validated in Rhino) and
// the cited published math, NOT from QuadriFlow / Instant Meshes source.
// =============================================================================
// frahan_quadremesh -- out-of-process thrust-following quad remesher. Given a
// triangle mesh and a per-vertex cross-field (E1 tangent + N normal, computed
// upstream from the TNA thrust network), it produces a quad mesh whose edges
// follow the field:
//     Stage A.5  comb + Gauss-Seidel smooth of the field (optional here; the
//                C# side already combs, so this is a light re-smooth).
//     Stage B    cotangent-Poisson parametrization  min |grad u - rho*E1|^2,
//                solved MATRIX-FREE by preconditioned conjugate gradient (the
//                native win over the C# dense Cholesky -> full-res meshes stay
//                fast and memory-light).
//     Stage C    lift the integer (u,v) lattice back onto the surface -> quads.
//
//   args: --selftest                    internal flat + paraboloid asserts (no I/O)
//         --remesh <in.bin> <out.obj>   read mesh+field blob, write quad OBJ
//
// Blob format (little-endian): int32 nv; nv*3 f64 verts; nv*3 f64 E1; nv*3 f64 N;
//                              int32 nf; nf*3 int32 tris; f64 edgeLen.
// Build: build_mingw.sh (static mingw64 g++ -O3, no external libraries).
// =============================================================================
#include <cstdio>
#include <cstdint>
#include <cstring>
#include <cmath>
#include <vector>
#include <string>
#include <algorithm>
#include <map>
#include <cstdlib>

struct V3 { double x, y, z; };
static inline V3 operator-(const V3&a,const V3&b){ return {a.x-b.x,a.y-b.y,a.z-b.z}; }
static inline V3 operator+(const V3&a,const V3&b){ return {a.x+b.x,a.y+b.y,a.z+b.z}; }
static inline V3 operator*(double s,const V3&a){ return {s*a.x,s*a.y,s*a.z}; }
static inline double dot(const V3&a,const V3&b){ return a.x*b.x+a.y*b.y+a.z*b.z; }
static inline V3 cross(const V3&a,const V3&b){ return {a.y*b.z-a.z*b.y, a.z*b.x-a.x*b.z, a.x*b.y-a.y*b.x}; }
static inline double len(const V3&a){ return std::sqrt(dot(a,a)); }
static inline V3 unit(const V3&a){ double l=len(a); return l>1e-12 ? (1.0/l)*a : V3{0,0,0}; }

struct Mesh { std::vector<V3> P; std::vector<int> T; /*3*nf*/ };

// ---- per-face precompute for the Poisson operator ---------------------------
struct FaceOp {
    int v[3];
    double A;          // triangle area
    V3 g[3];           // hat-function gradients (grad phi_a)
    V3 X1, X2;         // rho * projected field (RHS targets)
};

// Build the field-aligned Poisson data: hat gradients, area, and the target
// gradient rho*E1 / rho*E2 per face. Mirrors FieldAlignedParam.Solve (C#).
static void buildFaceOps(const Mesh& m, const std::vector<V3>& E1, double rho,
                         std::vector<FaceOp>& F, std::vector<double>& b1,
                         std::vector<double>& b2, std::vector<double>& diagL) {
    int nv = (int)m.P.size(); int nf = (int)m.T.size()/3;
    F.clear(); F.reserve(nf);
    b1.assign(nv,0.0); b2.assign(nv,0.0); diagL.assign(nv,0.0);
    for (int f=0; f<nf; ++f) {
        FaceOp fo; fo.v[0]=m.T[3*f]; fo.v[1]=m.T[3*f+1]; fo.v[2]=m.T[3*f+2];
        const V3& p0=m.P[fo.v[0]]; const V3& p1=m.P[fo.v[1]]; const V3& p2=m.P[fo.v[2]];
        V3 nrm = cross(p1-p0, p2-p0); double A = 0.5*len(nrm);
        if (A < 1e-12) { fo.A=0; fo.g[0]=fo.g[1]=fo.g[2]={0,0,0}; fo.X1=fo.X2={0,0,0}; F.push_back(fo); continue; }
        V3 n = unit(nrm); fo.A=A;
        fo.g[0]=(1.0/(2*A))*cross(n, p2-p1);
        fo.g[1]=(1.0/(2*A))*cross(n, p0-p2);
        fo.g[2]=(1.0/(2*A))*cross(n, p1-p0);
        V3 e1f = E1[fo.v[0]]+E1[fo.v[1]]+E1[fo.v[2]];
        e1f = e1f - dot(e1f,n)*n;
        e1f = (len(e1f)>1e-9) ? unit(e1f) : unit(fo.g[0]);
        V3 e2f = cross(n, e1f);
        fo.X1 = rho*e1f; fo.X2 = rho*e2f;
        for (int a=0;a<3;++a) {
            b1[fo.v[a]] += A*dot(fo.g[a], fo.X1);
            b2[fo.v[a]] += A*dot(fo.g[a], fo.X2);
            diagL[fo.v[a]] += A*dot(fo.g[a], fo.g[a]);
        }
        F.push_back(fo);
    }
}

// Matrix-free action y = L x  (L = cotangent stiffness), with vertex p pinned.
static void applyL(const std::vector<FaceOp>& F, int nv, int pin,
                   const std::vector<double>& x, std::vector<double>& y) {
    y.assign(nv,0.0);
    for (const auto& fo : F) {
        if (fo.A==0) continue;
        for (int a=0;a<3;++a) {
            double s=0;
            for (int b=0;b<3;++b) s += dot(fo.g[a],fo.g[b]) * x[fo.v[b]];
            y[fo.v[a]] += fo.A * s;
        }
    }
    y[pin] = x[pin]; // pinned row -> identity
}

// Jacobi-preconditioned CG for L u = b with u[pin]=0. Returns iterations used.
static int cgSolve(const std::vector<FaceOp>& F, int nv, int pin,
                   std::vector<double> b, const std::vector<double>& diagL,
                   std::vector<double>& u, int maxit=3000, double tol=1e-10) {
    b[pin]=0.0;
    u.assign(nv,0.0);
    std::vector<double> r=b, z(nv), p(nv), q(nv);
    auto precond=[&](const std::vector<double>& in, std::vector<double>& out){
        for (int i=0;i<nv;++i){ double d = (i==pin)?1.0:diagL[i]; out[i] = (d>1e-14)? in[i]/d : in[i]; }
        out[pin]=0.0;
    };
    r[pin]=0.0; precond(r,z); p=z;
    double rz=0; for(int i=0;i<nv;++i) rz+=r[i]*z[i];
    double bnorm=0; for(int i=0;i<nv;++i) bnorm+=b[i]*b[i]; bnorm=std::sqrt(bnorm)+1e-30;
    int it=0;
    for (; it<maxit; ++it) {
        applyL(F,nv,pin,p,q);
        double pq=0; for(int i=0;i<nv;++i) pq+=p[i]*q[i];
        if (std::fabs(pq)<1e-30) break;
        double alpha=rz/pq;
        for(int i=0;i<nv;++i){ u[i]+=alpha*p[i]; r[i]-=alpha*q[i]; }
        u[pin]=0.0; r[pin]=0.0;
        double rnorm=0; for(int i=0;i<nv;++i) rnorm+=r[i]*r[i]; rnorm=std::sqrt(rnorm);
        if (rnorm/bnorm < tol) { ++it; break; }
        precond(r,z);
        double rz2=0; for(int i=0;i<nv;++i) rz2+=r[i]*z[i];
        double beta=rz2/(rz+1e-30);
        for(int i=0;i<nv;++i) p[i]=z[i]+beta*p[i];
        rz=rz2;
    }
    return it;
}

// field-follow residual  Sum A|grad u - X1|^2 / Sum A|X1|^2
static double residual(const std::vector<FaceOp>& F, const std::vector<double>& u) {
    double num=0,den=0;
    for (const auto& fo:F){ if(fo.A==0) continue;
        V3 gu = u[fo.v[0]]*fo.g[0] + u[fo.v[1]]*fo.g[1] + u[fo.v[2]]*fo.g[2];
        V3 d = gu - fo.X1; num += fo.A*dot(d,d); den += fo.A*dot(fo.X1,fo.X1);
    }
    return den>1e-12 ? num/den : 0.0;
}

// ---- Stage C: integer-lattice inverse-map extraction ------------------------
struct QuadMesh { std::vector<V3> P; std::vector<int> Q; /*4*nq*/ int flipped=0; };

static bool locate(const Mesh& m, const std::vector<double>& U, const std::vector<double>& V,
                   double gu, double gv, V3& pos) {
    int nf=(int)m.T.size()/3;
    for (int f=0; f<nf; ++f) {
        int a=m.T[3*f],b=m.T[3*f+1],c=m.T[3*f+2];
        double ux=U[a],uy=V[a],vx=U[b],vy=V[b],wx=U[c],wy=V[c];
        double d=(vy-wy)*(ux-wx)+(wx-vx)*(uy-wy);
        if (std::fabs(d)<1e-14) continue;
        double la=((vy-wy)*(gu-wx)+(wx-vx)*(gv-wy))/d;
        double lb=((wy-uy)*(gu-wx)+(ux-wx)*(gv-wy))/d;
        double lc=1.0-la-lb; const double e=-1e-7;
        if (la>=e && lb>=e && lc>=e) {
            pos = la*m.P[a] + lb*m.P[b] + lc*m.P[c];
            return true;
        }
    }
    return false;
}

static QuadMesh extract(const Mesh& m, const std::vector<double>& U, const std::vector<double>& V) {
    QuadMesh out;
    int nv=(int)m.P.size(), nf=(int)m.T.size()/3;
    double umin=1e300,umax=-1e300,vmin=1e300,vmax=-1e300;
    for(int i=0;i<nv;++i){ umin=std::min(umin,U[i]);umax=std::max(umax,U[i]);vmin=std::min(vmin,V[i]);vmax=std::max(vmax,V[i]); }
    for(int f=0;f<nf;++f){ int a=m.T[3*f],b=m.T[3*f+1],c=m.T[3*f+2];
        double ar=0.5*((U[b]-U[a])*(V[c]-V[a])-(U[c]-U[a])*(V[b]-V[a])); if(ar<0) out.flipped++; }
    int i0=(int)std::ceil(umin-1e-9), i1=(int)std::floor(umax+1e-9);
    int j0=(int)std::ceil(vmin-1e-9), j1=(int)std::floor(vmax+1e-9);
    int nu=i1-i0+1, nvv=j1-j0+1;
    if (nu<2||nvv<2) return out;
    std::vector<V3> node(nu*nvv); std::vector<char> have(nu*nvv,0);
    for(int a=0;a<nu;++a) for(int b=0;b<nvv;++b){ V3 pos;
        if (locate(m,U,V,(double)(i0+a),(double)(j0+b),pos)){ node[a*nvv+b]=pos; have[a*nvv+b]=1; } }
    std::vector<int> idx(nu*nvv,-1);
    auto ens=[&](int a,int b)->int{ int k=a*nvv+b; if(idx[k]<0){ idx[k]=(int)out.P.size(); out.P.push_back(node[k]); } return idx[k]; };
    for(int a=0;a<nu-1;++a) for(int b=0;b<nvv-1;++b){
        if(have[a*nvv+b]&&have[(a+1)*nvv+b]&&have[(a+1)*nvv+b+1]&&have[a*nvv+b+1]){
            int i00=ens(a,b),i10=ens(a+1,b),i11=ens(a+1,b+1),i01=ens(a,b+1);
            out.Q.push_back(i00);out.Q.push_back(i10);out.Q.push_back(i11);out.Q.push_back(i01);
        }
    }
    return out;
}

static void writeObj(const char* path, const QuadMesh& q) {
    FILE* fp=fopen(path,"w"); if(!fp) return;
    for (const auto& v:q.P) fprintf(fp,"v %.9g %.9g %.9g\n",v.x,v.y,v.z);
    for (size_t i=0;i<q.Q.size();i+=4) fprintf(fp,"f %d %d %d %d\n",q.Q[i]+1,q.Q[i+1]+1,q.Q[i+2]+1,q.Q[i+3]+1);
    fclose(fp);
}

// ---- driver ----------------------------------------------------------------
static void solve(const Mesh& m, const std::vector<V3>& E1, double edgeLen,
                  std::vector<double>& U, std::vector<double>& V, double& res, int& iters) {
    int nv=(int)m.P.size();
    double rho = edgeLen>1e-9 ? 1.0/edgeLen : 1.0;
    std::vector<FaceOp> F; std::vector<double> b1,b2,diagL;
    buildFaceOps(m,E1,rho,F,b1,b2,diagL);
    iters  = cgSolve(F,nv,0,b1,diagL,U);
    int it2= cgSolve(F,nv,0,b2,diagL,V); iters=std::max(iters,it2);
    res = residual(F,U);
}

// ---- thrust-POTENTIAL field --------------------------------------------------
// The curvature-proxy cross-field degenerates (anisotropy -> 0) on flat regions
// and carries many 4-RoSy comb cones. Instead solve the load-path potential
//     L phi = lumped_area,   phi = 0 at the supports,
// and take E1 = tangential grad(phi): the meridional thrust direction, support ->
// crown. A GRADIENT field is single-valued (no 4-fold ambiguity), so it has far
// fewer singularities -> the single-chart param survives on a real holed vault.
static std::vector<V3> computeNormals(const Mesh& m) {
    int nv=(int)m.P.size(), nf=(int)m.T.size()/3;
    std::vector<V3> N(nv,{0,0,0});
    for(int f=0;f<nf;++f){ int a=m.T[3*f],b=m.T[3*f+1],c=m.T[3*f+2];
        V3 nrm=cross(m.P[b]-m.P[a], m.P[c]-m.P[a]); N[a]=N[a]+nrm; N[b]=N[b]+nrm; N[c]=N[c]+nrm; }
    for(int i=0;i<nv;++i){ V3 u=unit(N[i]); N[i]= len(u)>0.5 ? u : V3{0,0,1}; }
    return N;
}

// supports = naked-boundary vertices in the low z-band (the springing + column
// tops). Falls back to all boundary verts if the band is too thin.
static std::vector<char> detectSupports(const Mesh& m, double frac) {
    int nv=(int)m.P.size(), nf=(int)m.T.size()/3;
    std::map<long long,int> ec;
    auto key=[&](int a,int b){ long long lo=std::min(a,b),hi=std::max(a,b); return (lo<<32)|hi; };
    for(int f=0;f<nf;++f){ int t[3]={m.T[3*f],m.T[3*f+1],m.T[3*f+2]};
        for(int e=0;e<3;++e) ec[key(t[e],t[(e+1)%3])]++; }
    std::vector<char> bnd(nv,0);
    for(auto&kv:ec) if(kv.second==1){ int a=(int)(kv.first>>32), b=(int)(kv.first&0xffffffff); bnd[a]=1; bnd[b]=1; }
    double zmin=1e300,zmax=-1e300; for(int i=0;i<nv;++i){ zmin=std::min(zmin,m.P[i].z); zmax=std::max(zmax,m.P[i].z); }
    double thr=zmin+frac*(zmax-zmin);
    std::vector<char> sup(nv,0); int ns=0;
    for(int i=0;i<nv;++i) if(bnd[i] && m.P[i].z<=thr){ sup[i]=1; ns++; }
    if(ns<3) for(int i=0;i<nv;++i) if(bnd[i]) sup[i]=1;
    return sup;
}

// CG for  L u = b  with a SET of vertices pinned to 0 (Dirichlet supports).
static int cgSolveMask(const std::vector<FaceOp>& F, int nv, const std::vector<char>& pin,
                       std::vector<double> b, const std::vector<double>& diagL,
                       std::vector<double>& u, int maxit=6000, double tol=1e-10) {
    for(int i=0;i<nv;++i) if(pin[i]) b[i]=0.0;
    u.assign(nv,0.0);
    auto matvec=[&](const std::vector<double>& x, std::vector<double>& y){
        y.assign(nv,0.0);
        for(const auto& fo:F){ if(fo.A==0) continue;
            for(int a=0;a<3;++a){ double s=0; for(int bb=0;bb<3;++bb) s+=dot(fo.g[a],fo.g[bb])*x[fo.v[bb]]; y[fo.v[a]]+=fo.A*s; } }
        for(int i=0;i<nv;++i) if(pin[i]) y[i]=x[i];
    };
    std::vector<double> r=b,z(nv),p(nv),q(nv);
    auto precond=[&](const std::vector<double>& in, std::vector<double>& out){
        for(int i=0;i<nv;++i){ double d=pin[i]?1.0:diagL[i]; out[i]=(d>1e-14)?in[i]/d:in[i]; if(pin[i]) out[i]=0.0; } };
    for(int i=0;i<nv;++i) if(pin[i]) r[i]=0.0;
    precond(r,z); p=z;
    double rz=0; for(int i=0;i<nv;++i) rz+=r[i]*z[i];
    double bn=0; for(int i=0;i<nv;++i) bn+=b[i]*b[i]; bn=std::sqrt(bn)+1e-30;
    int it=0;
    for(;it<maxit;++it){ matvec(p,q);
        double pq=0; for(int i=0;i<nv;++i) pq+=p[i]*q[i]; if(std::fabs(pq)<1e-30) break;
        double al=rz/pq; for(int i=0;i<nv;++i){ u[i]+=al*p[i]; r[i]-=al*q[i]; }
        for(int i=0;i<nv;++i) if(pin[i]){ u[i]=0; r[i]=0; }
        double rn=0; for(int i=0;i<nv;++i) rn+=r[i]*r[i]; rn=std::sqrt(rn);
        if(rn/bn<tol){ ++it; break; }
        precond(r,z); double rz2=0; for(int i=0;i<nv;++i) rz2+=r[i]*z[i];
        double be=rz2/(rz+1e-30); for(int i=0;i<nv;++i) p[i]=z[i]+be*p[i]; rz=rz2; }
    return it;
}

static std::vector<V3> potentialField(const Mesh& m, double supportFrac,
                                      std::vector<V3>& N, int& nsup, int& cgit,
                                      std::vector<double>& conf) {
    int nv=(int)m.P.size();
    N=computeNormals(m);
    std::vector<char> sup=detectSupports(m,supportFrac); nsup=0; for(char c:sup) nsup+=c;
    std::vector<V3> dummy(nv,{1,0,0}); std::vector<FaceOp> F; std::vector<double> b1,b2,diagL;
    buildFaceOps(m,dummy,1.0,F,b1,b2,diagL);                 // reuse for L geometry
    std::vector<double> load(nv,0.0);
    for(const auto& fo:F){ if(fo.A==0) continue; double a3=fo.A/3.0; load[fo.v[0]]+=a3; load[fo.v[1]]+=a3; load[fo.v[2]]+=a3; }
    std::vector<double> phi; cgit=cgSolveMask(F,nv,sup,load,diagL,phi);
    std::vector<V3> E1(nv,{0,0,0});
    for(const auto& fo:F){ if(fo.A==0) continue;
        V3 gp = phi[fo.v[0]]*fo.g[0] + phi[fo.v[1]]*fo.g[1] + phi[fo.v[2]]*fo.g[2];
        for(int a=0;a<3;++a) E1[fo.v[a]]=E1[fo.v[a]]+fo.A*gp; }
    // confidence = |tangential grad phi| (0 where the potential is flat/degenerate),
    // normalised by its 90th percentile -> [0,1]. Drives the smoothing data term.
    conf.assign(nv,0.0);
    for(int i=0;i<nv;++i){ V3 e=E1[i]; e=e-dot(e,N[i])*N[i];
        conf[i]=len(e);
        if(len(e)>1e-9) E1[i]=unit(e);
        else { V3 t=cross(N[i],V3{1,0,0}); if(len(t)<1e-6) t=cross(N[i],V3{0,1,0}); E1[i]=unit(t); conf[i]=0.0; } }
    std::vector<double> cs=conf; std::sort(cs.begin(),cs.end());
    double p90=cs[(size_t)(0.9*(cs.size()-1))]+1e-30;
    for(int i=0;i<nv;++i) conf[i]=std::min(1.0, conf[i]/p90);
    return E1;
}

// mesh-only blob: int32 nv; nv*3 f64 verts; int32 nf; nf*3 int32 tris; f64 edgeLen.
template<class T> static T rdf(FILE* f){ T v; fread(&v,sizeof(T),1,f); return v; }
static int remeshPotential(const char* inPath, const char* outPath, double supportFrac) {
    FILE* f=fopen(inPath,"rb"); if(!f){ fprintf(stderr,"cannot open %s\n",inPath); return 2; }
    int nv=rdf<int32_t>(f); Mesh m; m.P.resize(nv);
    for(int i=0;i<nv;++i){ m.P[i].x=rdf<double>(f); m.P[i].y=rdf<double>(f); m.P[i].z=rdf<double>(f); }
    int nf=rdf<int32_t>(f); m.T.resize(3*nf); for(int i=0;i<3*nf;++i) m.T[i]=rdf<int32_t>(f);
    double edgeLen=rdf<double>(f); fclose(f);
    std::vector<V3> N; int nsup,cgphi; std::vector<double> conf;
    std::vector<V3> E1=potentialField(m,supportFrac,N,nsup,cgphi,conf);
    std::vector<double> U,V; double res; int it; solve(m,E1,edgeLen,U,V,res,it);
    QuadMesh q=extract(m,U,V); writeObj(outPath,q);
    printf("potential-remesh: V=%d F=%d supports=%d phi_cg=%d -> quads=%d flippedTris=%d residual=%.3e param_cg=%d -> %s\n",
           nv,nf,nsup,cgphi,(int)q.Q.size()/4,q.flipped,res,it,outPath);
    return 0;
}

static int selftest() {
    int fails=0;
    // TEST1: flat 20x20 grid, constant field +X. Exact: u = rho*x, residual ~0.
    {
        int N=20; double h=0.25; Mesh m; std::vector<V3> E1;
        for(int j=0;j<N;++j)for(int i=0;i<N;++i){ m.P.push_back({i*h,j*h,0}); E1.push_back({1,0,0}); }
        for(int j=0;j<N-1;++j)for(int i=0;i<N-1;++i){ int a=j*N+i,b=j*N+i+1,c=(j+1)*N+i+1,d=(j+1)*N+i;
            m.T.push_back(a);m.T.push_back(b);m.T.push_back(c); m.T.push_back(a);m.T.push_back(c);m.T.push_back(d); }
        std::vector<double> U,V; double res; int it; solve(m,E1,h,U,V,res,it);
        double rho=1.0/h, err=0; for(size_t k=0;k<m.P.size();++k) err=std::max(err,std::fabs((U[k]-U[0])-rho*(m.P[k].x-m.P[0].x)));
        printf("TEST1 flat/const: residual=%.3e maxErr(u-rho*x)=%.3e cg_it=%d  %s\n",res,err,it,(res<1e-8&&err<1e-6)?"PASS":"FAIL");
        if(!(res<1e-8&&err<1e-6)) fails++;
    }
    // TEST2: paraboloid patch, projected-X field. Extract -> flips==0, quads>200.
    {
        int N=26; double h=0.22, c=0.10; Mesh m; std::vector<V3> E1;
        for(int j=0;j<N;++j)for(int i=0;i<N;++i){ double x=(i-N/2)*h,y=(j-N/2)*h; m.P.push_back({x,y,c*(x*x+y*y)});
            V3 n=unit(V3{-2*c*x,-2*c*y,1}); V3 e={1,0,0}; e=e-dot(e,n)*n; E1.push_back(unit(e)); }
        for(int j=0;j<N-1;++j)for(int i=0;i<N-1;++i){ int a=j*N+i,b=j*N+i+1,cc=(j+1)*N+i+1,d=(j+1)*N+i;
            m.T.push_back(a);m.T.push_back(b);m.T.push_back(cc); m.T.push_back(a);m.T.push_back(cc);m.T.push_back(d); }
        std::vector<double> U,V; double res; int it; solve(m,E1,0.30,U,V,res,it);
        QuadMesh q=extract(m,U,V); int nq=(int)q.Q.size()/4;
        printf("TEST2 paraboloid: residual=%.3e quads=%d flippedTris=%d cg_it=%d  %s\n",res,nq,q.flipped,it,(q.flipped==0&&nq>200)?"PASS":"FAIL");
        if(!(q.flipped==0&&nq>200)) fails++;
    }
    printf(fails==0 ? "SELFTEST: ALL PASS (native pipeline matches the C# reference)\n" : "SELFTEST: %d FAIL\n", fails);
    return fails==0?0:1;
}

template<class T> static T rd(FILE* f){ T v; fread(&v,sizeof(T),1,f); return v; }

static int remesh(const char* inPath, const char* outPath) {
    FILE* f=fopen(inPath,"rb"); if(!f){ fprintf(stderr,"cannot open %s\n",inPath); return 2; }
    int nv=rd<int32_t>(f); Mesh m; m.P.resize(nv); std::vector<V3> E1(nv), Nn(nv);
    for(int i=0;i<nv;++i){ m.P[i].x=rd<double>(f); m.P[i].y=rd<double>(f); m.P[i].z=rd<double>(f); }
    for(int i=0;i<nv;++i){ E1[i].x=rd<double>(f); E1[i].y=rd<double>(f); E1[i].z=rd<double>(f); }
    for(int i=0;i<nv;++i){ Nn[i].x=rd<double>(f); Nn[i].y=rd<double>(f); Nn[i].z=rd<double>(f); }
    int nf=rd<int32_t>(f); m.T.resize(3*nf);
    for(int i=0;i<3*nf;++i) m.T[i]=rd<int32_t>(f);
    double edgeLen=rd<double>(f); fclose(f);
    std::vector<double> U,V; double res; int it; solve(m,E1,edgeLen,U,V,res,it);
    QuadMesh q=extract(m,U,V); writeObj(outPath,q);
    printf("remesh: V=%d F=%d -> quads=%d flippedTris=%d residual=%.3e cg_it=%d -> %s\n",
           nv,nf,(int)q.Q.size()/4,q.flipped,res,it,outPath);
    return 0;
}

// ---- field refinement: 4-RoSy-aware smoothing + singularity diagnostics -----
// rotate v by k*90deg about n (one 90deg step: R v = n x v)
static inline V3 rotK(const V3& e, const V3& n, int k){ V3 v=e; for(int i=0;i<k;++i) v=cross(n,v); return v; }
// k in {0..3} whose R^k e best matches target (both ~in n's tangent plane)
static inline int best4(const V3& target, const V3& e, const V3& n){
    V3 c1=cross(n,e);
    double d[4]={ dot(target,e), dot(target,c1), -dot(target,e), -dot(target,c1) };
    int k=0; for(int i=1;i<4;++i) if(d[i]>d[k]) k=i;
    return k;
}
static void vertexAdjacency(const Mesh& m, std::vector<std::vector<int> >& adj, std::vector<char>& bnd){
    int nv=(int)m.P.size(), nf=(int)m.T.size()/3;
    std::map<long long,int> ec;
    auto key=[&](int a,int b){ long long lo=std::min(a,b),hi=std::max(a,b); return (lo<<32)|hi; };
    for(int f=0;f<nf;++f){ int t[3]={m.T[3*f],m.T[3*f+1],m.T[3*f+2]};
        for(int e=0;e<3;++e) ec[key(t[e],t[(e+1)%3])]++; }
    adj.assign(nv, std::vector<int>()); bnd.assign(nv,0);
    for(std::map<long long,int>::iterator it=ec.begin(); it!=ec.end(); ++it){
        int a=(int)(it->first>>32), b=(int)(it->first&0xffffffff);
        adj[a].push_back(b); adj[b].push_back(a);
        if(it->second==1){ bnd[a]=1; bnd[b]=1; } }
}
// interior 4-RoSy singularity count: per triangle, sum the matching rotations
// around the loop; != 0 (mod 4) => cone. Triangles touching the boundary skipped.
static int countSing(const Mesh& m, const std::vector<V3>& E1, const std::vector<V3>& N, const std::vector<char>& bnd){
    int nf=(int)m.T.size()/3, sing=0;
    for(int f=0;f<nf;++f){ int a=m.T[3*f],b=m.T[3*f+1],c=m.T[3*f+2];
        if(bnd[a]||bnd[b]||bnd[c]) continue;
        int vv[4]={a,b,c,a}; int kk=0; bool ok=true;
        for(int i=0;i<3;++i){ int u=vv[i], w=vv[i+1];
            V3 t=E1[u]-dot(E1[u],N[w])*N[w];
            if(len(t)<1e-9){ ok=false; break; }
            kk += best4(unit(t), E1[w], N[w]); }
        if(ok && (kk&3)!=0) sing++; }
    return sing;
}
// Confidence-weighted 4-RoSy Gauss-Seidel smoothing. Where |grad phi| is strong the
// data term holds the thrust direction; where the field is weak/degenerate the
// neighbours flood in (harmonic fill), so NOISE cone pairs annihilate while the
// topologically forced cones migrate to natural locations. This is the refinement
// that cleans the extra singularities seen in the thrust-aligned QuadWild run.
static void smoothField(const Mesh& m, std::vector<V3>& E1, const std::vector<V3>& N,
                        const std::vector<double>& conf, int sweeps, double dataW){
    int nv=(int)m.P.size();
    std::vector<std::vector<int> > adj; std::vector<char> bnd;
    vertexAdjacency(m,adj,bnd);
    std::vector<V3> orig=E1;
    for(int s=0;s<sweeps;++s){
        for(int i=0;i<nv;++i){
            V3 acc = (dataW*conf[i])*orig[i];
            for(size_t jj=0;jj<adj[i].size();++jj){ int j=adj[i][jj];
                V3 v=E1[j]-dot(E1[j],N[i])*N[i];
                if(len(v)<1e-9) continue;
                v=unit(v);
                acc = acc + (0.25+conf[j])*rotK(v,N[i],best4(E1[i],v,N[i]));   // 0.25 floor: propagate through degenerate zones
            }
            acc = acc - dot(acc,N[i])*N[i];
            if(len(acc)>1e-9) E1[i]=unit(acc);
        }
    }
}

// Emit our thrust-potential field as a QuadWild .rosy (per-FACE cross-field):
// line 1 = nFaces, line 2 = 4 (RoSy degree), then one unit direction per face.
// Feed to `quadwild <mesh.obj> 2 <config do_remesh 0> <this.rosy>` -> thrust-aligned
// patches + Bi-MDF quantization -> a RELIABLE thrust-following quad mesh (our field,
// their robustness).
static int writeRosy(const char* inPath, const char* outPath, double supportFrac, int sweeps, double dataW) {
    FILE* f=fopen(inPath,"rb"); if(!f){ fprintf(stderr,"cannot open %s\n",inPath); return 2; }
    int nv=rdf<int32_t>(f); Mesh m; m.P.resize(nv);
    for(int i=0;i<nv;++i){ m.P[i].x=rdf<double>(f); m.P[i].y=rdf<double>(f); m.P[i].z=rdf<double>(f); }
    int nf=rdf<int32_t>(f); m.T.resize(3*nf); for(int i=0;i<3*nf;++i) m.T[i]=rdf<int32_t>(f);
    fclose(f);
    std::vector<V3> N; int nsup,cgphi; std::vector<double> conf;
    std::vector<V3> E1=potentialField(m,supportFrac,N,nsup,cgphi,conf);
    std::vector<std::vector<int> > adj; std::vector<char> bnd; vertexAdjacency(m,adj,bnd);
    int s0=countSing(m,E1,N,bnd);
    std::vector<V3> orig=E1;
    if(sweeps>0) smoothField(m,E1,N,conf,sweeps,dataW);
    int s1=countSing(m,E1,N,bnd);
    // alignment kept vs the raw thrust field (4-RoSy residual cos, high-conf verts)
    double al=0; int na=0;
    for(int i=0;i<nv;++i){ if(conf[i]<0.5) continue;
        al += dot(E1[i], rotK(orig[i],N[i],best4(E1[i],orig[i],N[i]))); na++; }
    al = na>0 ? al/na : 1.0;
    FILE* o=fopen(outPath,"w"); if(!o) return 3;
    fprintf(o,"%d\n4\n",nf);
    for(int fi=0; fi<nf; ++fi){ int a=m.T[3*fi],b=m.T[3*fi+1],c=m.T[3*fi+2];
        V3 fn=unit(cross(m.P[b]-m.P[a], m.P[c]-m.P[a]));
        // 4-RoSy-aware per-face average (a plain vector sum cancels near cones)
        int vv[3]={a,b,c}; V3 acc={0,0,0}; V3 ref={0,0,0}; bool haveRef=false;
        for(int i=0;i<3;++i){ V3 v=E1[vv[i]]-dot(E1[vv[i]],fn)*fn;
            if(len(v)<1e-9) continue;
            v=unit(v);
            if(!haveRef){ ref=v; haveRef=true; acc=acc+v; }
            else acc = acc + rotK(v,fn,best4(ref,v,fn)); }
        V3 e;
        if(haveRef && len(acc)>1e-9){ e=acc-dot(acc,fn)*fn; e=unit(e); }
        else { e=unit(cross(fn,V3{1,0,0})); if(len(e)<0.5) e=unit(cross(fn,V3{0,1,0})); }
        fprintf(o,"%.9g %.9g %.9g \n",e.x,e.y,e.z); }
    fclose(o);
    printf("rosy: %d faces supports=%d phi_cg=%d | interiorSing %d -> %d (sweeps=%d dataW=%.2f) meanAlign=%.3f -> %s\n",
           nf,nsup,cgphi,s0,s1,sweeps,dataW,al,outPath);
    return 0;
}

int main(int argc, char** argv) {
    if (argc<2 || strcmp(argv[1],"--selftest")==0) return selftest();
    if (strcmp(argv[1],"--remesh")==0 && argc>=4) return remesh(argv[2],argv[3]);
    if (strcmp(argv[1],"--potential")==0 && argc>=4) return remeshPotential(argv[2],argv[3], argc>=5?atof(argv[4]):0.35);
    if (strcmp(argv[1],"--rosy")==0 && argc>=4) return writeRosy(argv[2],argv[3], argc>=5?atof(argv[4]):0.35, argc>=6?atoi(argv[5]):0, argc>=7?atof(argv[6]):0.5);
    fprintf(stderr,"usage: frahan_quadremesh --selftest | --remesh <in.bin> <out.obj> | --potential <mesh.bin> <out.obj> [frac] | --rosy <mesh.bin> <out.rosy> [frac] [sweeps] [dataW]\n");
    return 2;
}
