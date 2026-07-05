# 45 - Cut-and-Fill / soil excavation to the rock face - GhPython demo.
#
# Computes the OVERBURDEN to strip to reach a bedrock / rock-face surface: the volume between a
# GROUND (topo) surface and a BEDROCK surface, by exact TIN-prism differencing (the same Core the
# 'Overburden To Rock Face' component fronts). Cut = soil above bedrock (excavate); Fill = where
# bedrock breaks the surface; Loose = swell-adjusted haul volume.
#
# Outputs:  ground  = ground mesh vertex-coloured by overburden DEPTH (blue thin -> red deep; grey = fill)
#           bedrock = the bedrock / rock-face mesh
#           cut, fill, net, area, loose = volumes (m3) and plan area (m2)
#           report  = text summary
# Wire `ground` and `bedrock` -> Custom Preview for the self-presenting site.
#
# This is the no-plugin path. The 'Overburden To Rock Face' component (Frahan > Quarry) does the same
# from two arbitrary scan/GPR meshes (it bridges them onto a common TIN). Set CORE_DIR to your build.

import clr, System, math
import Rhino.Geometry as rg
from System.Collections.Generic import List

CORE_DIR = r"d:\frahan-stonepack\src\Frahan.StonePack.Core\bin\Debug\net48"
clr.AddReferenceToFileAndPath(CORE_DIR + r"\Frahan.StonePack.Core.dll")
from Frahan.Core.Earthworks import OverburdenVolume

# --- synthetic site (metres): a rolling ground over an undulating bedrock + a knob that surfaces ---
nx, ny, W, H = 44, 30, 30.0, 20.0
try:
    swell
except NameError:
    swell = 0.25   # 25% bulking for the loose/haul volume

def gZ(x, y):
    return 6.0 + 1.4 * math.sin(0.25 * x) * math.cos(0.30 * y) + 0.6 * math.sin(0.12 * x + 0.5)

def rZ(x, y):
    return (1.6 + 0.9 * math.sin(0.18 * x + 1.0) * math.cos(0.22 * y) + 0.04 * x
            + 6.5 * math.exp(-(((x - 24) ** 2 + (y - 14) ** 2) / 7.0)))   # bedrock knob -> fill

gxyz = List[float]()
bz = List[float]()
tris = List[int]()
for j in range(ny):
    for i in range(nx):
        x = W * i / (nx - 1)
        y = H * j / (ny - 1)
        gxyz.Add(x); gxyz.Add(y); gxyz.Add(gZ(x, y))
        bz.Add(rZ(x, y))
id = lambda i, j: j * nx + i
for j in range(ny - 1):
    for i in range(nx - 1):
        a, b, c, d = id(i, j), id(i + 1, j), id(i + 1, j + 1), id(i, j + 1)
        tris.Add(a); tris.Add(b); tris.Add(c)
        tris.Add(a); tris.Add(c); tris.Add(d)

res = OverburdenVolume.Compute(gxyz, bz, tris)
cut = res.CutVolume
fill = res.FillVolume
net = res.NetVolume
area = res.PlanArea
loose = cut * (1.0 + swell)

# --- display meshes ---
ground = rg.Mesh()
bedrock = rg.Mesh()
for k in range(0, gxyz.Count, 3):
    ground.Vertices.Add(gxyz[k], gxyz[k + 1], gxyz[k + 2])
    bedrock.Vertices.Add(gxyz[k], gxyz[k + 1], bz[k // 3])
for t in range(0, tris.Count, 3):
    ground.Faces.AddFace(tris[t], tris[t + 1], tris[t + 2])
    bedrock.Faces.AddFace(tris[t], tris[t + 1], tris[t + 2])

dmax = 0.0
dd = []
for i in range(ground.Vertices.Count):
    di = gxyz[3 * i + 2] - bz[i]
    dd.append(di)
    if di > dmax:
        dmax = di
ground.VertexColors.Clear()
for di in dd:
    if di < 0:
        ground.VertexColors.Add(System.Drawing.Color.FromArgb(150, 150, 160))   # fill = grey
    else:
        t = max(0.0, min(1.0, di / max(0.1, dmax)))
        r = min(255, int(40 + 215 * t))
        g = min(255, int(120 + 60 * (1 - abs(t - 0.5) * 2)))
        b = max(0, int(210 * (1 - t)))
        ground.VertexColors.Add(System.Drawing.Color.FromArgb(r, g, b))
ground.Normals.ComputeNormals()
bedrock.Normals.ComputeNormals()

report = ("Cut-and-fill to rock face: CUT (overburden) = %.0f m3, FILL (bedrock above) = %.0f m3, "
          "net = %.0f m3, plan area = %.0f m2, mean depth = %.2f m, loose haul (+%.0f%%) = %.0f m3."
          % (cut, fill, net, area, cut / area if area else 0, swell * 100, loose))
