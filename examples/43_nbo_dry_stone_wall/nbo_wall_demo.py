# 43 - NBO Dry-Stone Wall - GhPython demo (paste into a GhPython / Python Script component).
#
# Builds a coursed dry-stone wall from a stone inventory with the Next-Best-Object planner:
# per placement front it orients each stone by the hybrid rule (rest on the hull stable face +
# yaw the long axis into the wall), drops it to contact on the as-built, gates stability
# (CoM-over-support + d/h >= 0.5 + seating), and commits the lowest-cost stone.
#
# Outputs:  a = placed meshes (placement order)
#           b = per-stone colour (by course; red = gate-rejected)
#           report = text summary
# Wire a -> Custom Preview (Geometry) and b -> Custom Preview (Material) for the self-presenting wall.
#
# Optional inputs (add on the component): spine = a horizontal Curve (the wall follows it as a
# plan-rim); a straight wall is built when no spine is given.
#
# Set CORE_DIR to your Frahan.StonePack.Core build folder and INVENTORY_DIR to a folder of stone
# OBJ meshes (the shipped capture uses the ETH1100 dry-stone dataset).

import clr, System
import Rhino.Geometry as rg
from System.Collections.Generic import List

CORE_DIR = r"d:\frahan-stonepack\src\Frahan.StonePack.Core\bin\Debug\net48"
INVENTORY_DIR = r"d:\code_ws\Data\eth1100\closed\1100 Closed Stone Meshes"
N = 40

clr.AddReferenceToFileAndPath(CORE_DIR + r"\Frahan.StonePack.Core.dll")
from Frahan.Masonry.Nbo import NboPlanner, NboFillOptions

def load_obj(path):
    m = rg.Mesh()
    for line in System.IO.File.ReadLines(path):
        if len(line) < 2:
            continue
        if line[0] == 'v' and line[1] == ' ':
            p = line.split()
            m.Vertices.Add(float(p[1]), float(p[2]), float(p[3]))
        elif line[0] == 'f' and line[1] == ' ':
            p = line.split()
            def vi(t):
                s = t.find('/')
                return int(t[:s] if s >= 0 else t) - 1
            if len(p) == 4:
                m.Faces.AddFace(vi(p[1]), vi(p[2]), vi(p[3]))
            elif len(p) >= 5:
                m.Faces.AddFace(vi(p[1]), vi(p[2]), vi(p[3]), vi(p[4]))
    m.Compact()
    m.Normals.ComputeNormals()
    return m

inv = List[rg.Mesh]()
for i in range(N):
    inv.Add(load_obj(INVENTORY_DIR + ("\\%04d.obj" % i)))

opt = NboFillOptions()
opt.WallLength = 3.0
opt.TargetHeight = 1.6
opt.Gap = 0.02
opt.CourseOffset = 0.25

try:
    spine
except NameError:
    spine = None

seq = NboPlanner.FillSpine(inv, spine, opt) if spine is not None else NboPlanner.FillWall(inv, opt)

palette = [System.Drawing.Color.FromArgb(95, 160, 210), System.Drawing.Color.FromArgb(120, 190, 110),
           System.Drawing.Color.FromArgb(225, 180, 90), System.Drawing.Color.FromArgb(205, 120, 170),
           System.Drawing.Color.FromArgb(150, 150, 225), System.Drawing.Color.FromArgb(225, 150, 95)]

a = []
b = []
for s in seq.Steps:
    pm = inv[s.StoneIndex].DuplicateMesh()
    pm.Transform(s.Placement)
    pm.Normals.ComputeNormals()
    a.append(pm)
    b.append(palette[s.Course % len(palette)] if s.Verdict.Stable
             else System.Drawing.Color.FromArgb(210, 70, 60))

report = "NBO wall: placed %d, stable %d/%d, %d courses, top %.2f m (%s)." % (
    seq.Placed, seq.StableCount, seq.Placed, seq.Courses, seq.TopHeight,
    "spine plan-rim" if spine is not None else "straight")
