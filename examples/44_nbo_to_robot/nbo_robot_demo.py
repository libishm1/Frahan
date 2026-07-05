# 44 - NBO -> Robot frames - GhPython demo (paste into a GhPython / Python Script component).
#
# Builds the NBO wall (example 43), then for each placed stone computes the top-pick grasp and the
# robot TCP target frames: PLACE (where the gripper holds the stone to place it), PICK (where it
# grabs the stone where it sits) and APPROACH (the waypoint above the place frame). It also emits the
# UR pose p[x,y,z,rx,ry,rz] (metres + axis-angle) in the robot base frame. This is the planner ->
# robot HANDOFF; the live robot stays dormant (no command is sent).
#
# Feed `place_planes` into nbo_to_compas_robots.py (the COMPAS FK/IK sim) or into a UR component
# (Robots/visose, UnderAutomation). Frame Z points DOWN into the stone (the tool approach).
#
# Outputs:  wall            = placed meshes (context)
#           place_planes    = TCP frame to place each stone
#           pick_planes     = TCP frame to grab each stone where it sits
#           approach_planes = pre-place waypoint above each place frame
#           ur_poses        = UR p[...] per stone (in the robot base)
#           report          = text summary
# Wire wall -> Custom Preview; place_planes -> a Plane display for the robot targets.
#
# Optional inputs: robot_base = Plane (default World XY), approach = number metres (default 0.15).

import clr, System
import Rhino.Geometry as rg
from System.Collections.Generic import List

CORE_DIR = r"d:\frahan-stonepack\src\Frahan.StonePack.Core\bin\Debug\net48"
INVENTORY_DIR = r"d:\code_ws\Data\eth1100\closed\1100 Closed Stone Meshes"
N = 24

clr.AddReferenceToFileAndPath(CORE_DIR + r"\Frahan.StonePack.Core.dll")
from Frahan.Masonry.Nbo import NboPlanner, NboFillOptions, StoneShapeAnalyzer, NboGrasp

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
opt.TargetHeight = 1.3
opt.Gap = 0.02
seq = NboPlanner.FillWall(inv, opt)

try:
    robot_base
except NameError:
    robot_base = rg.Plane.WorldXY
try:
    approach
except NameError:
    approach = 0.15

wall = []
place_planes = []
pick_planes = []
approach_planes = []
ur_poses = []
for s in seq.Steps:
    src = inv[s.StoneIndex]
    pm = src.DuplicateMesh()
    pm.Transform(s.Placement)
    pm.Normals.ComputeNormals()
    wall.append(pm)

    shape = StoneShapeAnalyzer.Analyze(src)
    rest = StoneShapeAnalyzer.BestRestingFace(shape)
    grasp = NboGrasp.TopPick(shape, rest)

    pw = NboGrasp.PlaceFrame(grasp, s.Placement)
    place_planes.append(pw)
    pick_planes.append(NboGrasp.PickFrame(grasp, rg.Transform.Identity))
    approach_planes.append(NboGrasp.WithApproach(pw, approach))
    ur_poses.append(NboGrasp.ToUrPose(NboGrasp.InBase(pw, robot_base)).ToString())

report = ("NBO -> robot: %d place frames + UR poses (robot base). Frame Z = tool-down. "
          "Feed place_planes -> nbo_to_compas_robots.py for the FK/IK sim. Hardware dormant." % len(place_planes))
