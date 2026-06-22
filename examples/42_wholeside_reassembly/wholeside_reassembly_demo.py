# Whole-Side Reassembly -- GhPython demo (paste into a GhPython / Python 3 Script component).
#
# Self-contained: regenerates the validated 3x3 wavy jigsaw, scatters+rotates it, then
# calls the Core BestFirstAssembler and previews the reassembly.
#   Outputs:  a = Placed (the reassembled contours)   b = Input (the scattered contours)
#
# Requires the built Core DLL. Set CORE_DIR to your Frahan.EdgeMatching.Core build output.
# (The shipped "Whole-Side Assemble" component, Frahan > EdgeMatch, does the same natively
#  once the plugin is installed -- this script is the no-plugin live demo.)

import clr, math, System
import Rhino.Geometry as rg
from System import Random, Array
from System.Collections.Generic import List

CORE_DIR = r"d:\frahan-stonepack\src\Frahan.EdgeMatching.Core\bin\Debug\net48"
clr.AddReferenceToFileAndPath(CORE_DIR + r"\MathNet.Numerics.dll")
clr.AddReferenceToFileAndPath(CORE_DIR + r"\Frahan.EdgeMatching.Core.dll")
from Frahan.EdgeMatching import Panel, PanelKind, AssemblyOptions, BestFirstAssembler

# --- generate the validated 3x3 wavy jigsaw (deterministic, seed 7) ---------
W, Hh, R, C, ampFrac, nE = 320.0, 220.0, 3, 3, 0.16, 20
rng = Random(7)
cache = {}

def node(r, c):
    return rg.Point3d(c * W / C, r * Hh / R, 0)

def edge(rA, cA, rB, cB):
    ka, kb = rA * 100 + cA, rB * 100 + cB
    key = (min(ka, kb), max(ka, kb)); fwd = ka < kb
    if key in cache:
        pts = cache[key]; return list(pts) if fwd else list(reversed(pts))
    p0, p1 = node(rA, cA), node(rB, cB)
    horiz = rA == rB
    inner = (rA > 0 and rA < R) if horiz else (cA > 0 and cA < C)
    d = p1 - p0; ln = d.Length; d.Unitize(); perp = rg.Vector3d(-d.Y, d.X, 0)
    amp = ampFrac * (Hh / R if horiz else W / C)
    f1 = 1.5 + rng.NextDouble() * 2.5; ph1 = rng.NextDouble() * math.pi * 2; a1 = 0.6 + rng.NextDouble() * 0.4
    f2 = 2.5 + rng.NextDouble() * 3.0; ph2 = rng.NextDouble() * math.pi * 2; a2 = 0.3 + rng.NextDouble() * 0.4
    sgn = 1 if rng.Next(2) == 0 else -1
    seg = []
    for i in range(nE + 1):
        t = float(i) / nE; b = p0 + d * (ln * t); off = 0.0
        if inner:
            env = math.sin(math.pi * t)
            wig = a1 * math.sin(2 * math.pi * f1 * t + ph1) + a2 * math.sin(2 * math.pi * f2 * t + ph2)
            off = sgn * amp * env * wig / (a1 + a2)
        seg.append(b + perp * off)
    cache[key] = seg if fwd else list(reversed(seg))
    return seg

truth = []
for r in range(R):
    for c in range(C):
        loop = []
        def add(e, first):
            for i in range(0 if first else 1, len(e)):
                loop.append(e[i])
        add(edge(r, c, r, c + 1), True)
        add(edge(r, c + 1, r + 1, c + 1), False)
        add(edge(r + 1, c + 1, r + 1, c), False)
        add(edge(r + 1, c, r, c), False)
        if loop[0].DistanceTo(loop[-1]) > 1e-9:
            loop.append(loop[0])
        truth.append(rg.PolylineCurve(loop))

N = len(truth); centre = 4
pitch = max(W / C, Hh / R) * 1.6
scattered = []
for i in range(N):
    ctr = truth[i].GetBoundingBox(True).Center
    crv = truth[i].DuplicateCurve()
    crv.Transform(rg.Transform.Translation((i % C) * pitch - ctr.X - 700, (i // C) * pitch - ctr.Y, 0))
    crv.Rotate((i * 1.7) % 6.28, rg.Vector3d.ZAxis, crv.GetBoundingBox(True).Center)
    scattered.append(crv)

# --- reassemble with the Core whole-side best-first solver -------------------
anchor = Panel("p%d" % centre, truth[centre], PanelKind.Shard)
pool = List[Panel]()
for i in range(N):
    if i != centre:
        pool.Add(Panel("p%d" % i, scattered[i], PanelKind.Shard))
opt = AssemblyOptions(); opt.WholeSideFitGate = 2.5
state = BestFirstAssembler(opt).Solve(Array[Panel]([anchor]), pool)

placed = []
for pn in state.PlacedPanels:
    idx = int(pn.Id[1:])
    src = truth[idx] if idx == centre else scattered[idx]
    crv = src.DuplicateCurve(); crv.Transform(state.AppliedTransforms[pn.Id])
    placed.append(crv)

a = placed          # reassembled contours (9/9, ~5 mm)
b = scattered       # the scattered input
