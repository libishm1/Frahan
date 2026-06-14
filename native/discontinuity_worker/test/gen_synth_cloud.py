import numpy as np, struct, math
rng = np.random.default_rng(7)
# three known joint sets: (dip, dipdir, spacing) in metres
sets = [(8.0, 0.0, 0.6), (88.0, 90.0, 0.5), (85.0, 0.0, 0.7)]
def lowerhemi_normal(dip, dd):
    d=math.radians(dip); a=math.radians(dd); s=math.sin(d)
    return np.array([s*math.sin(a), s*math.cos(a), -math.cos(d)])
L=4.0  # block half-extent
pts=[]
for dip,dd,sp in sets:
    n=lowerhemi_normal(dip,dd); n/=np.linalg.norm(n)
    # in-plane axes
    ref=np.array([0,0,1.0]) if abs(n[2])<0.9 else np.array([1.0,0,0])
    u=np.cross(n,ref); u/=np.linalg.norm(u); v=np.cross(n,u)
    g=np.linspace(-L,L,46)
    uu,vv=np.meshgrid(g,g); uu=uu.ravel(); vv=vv.ravel()
    k0=int(-L/sp)-1; k1=int(L/sp)+1
    for k in range(k0,k1+1):
        off=k*sp
        P = off*n[None,:] + uu[:,None]*u[None,:] + vv[:,None]*v[None,:]
        # keep within a cube, add small normal noise (2 mm)
        P = P + (rng.normal(0,0.002,size=(P.shape[0],1)))*n[None,:]
        m=(np.abs(P[:,0])<=L)&(np.abs(P[:,1])<=L)&(np.abs(P[:,2])<=L*0.75)
        pts.append(P[m])
P=np.vstack(pts).astype(np.float32)
print("points:",P.shape[0])
with open("synth.ply","wb") as f:
    f.write(b"ply\nformat binary_little_endian 1.0\n")
    f.write(("element vertex %d\n"%P.shape[0]).encode())
    f.write(b"property float x\nproperty float y\nproperty float z\nend_header\n")
    f.write(P.tobytes())
print("expected sets (dip/dipdir/spacing):", sets)
