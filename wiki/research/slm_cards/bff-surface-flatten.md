---
slug: bff-surface-flatten
title: BFF Surface Flattening + Chart-Scale Recovery + Barycentric Inverse Map
tier: 1
algorithm_family: planar parameterization (conformal) + interpolation inverse map
core_method_class: Sawhney & Crane 2017, "Boundary First Flattening", ACM TOG 36(4):109, DOI 10.1145/3072959.3056432 (external binary). Inverse map = standard barycentric interpolation. NO Floater-2003 MVC code present.
fabrication_direction: top_down
geometry_type: triangle mesh -> 2D UV chart (Z=0 plane)
parallel_model: serial
big_o_time: external BFF O(n) sparse-solve (opaque); Frahan code O(V+F) build/scale/distortion, O(P*F) inverse map per curve
big_o_space: O(V+F)
verdict: evolve
source_files:
  - Frahan.StonePack.Core/SurfacePacking/BffCommandLineRunner.cs:32
  - Frahan.StonePack.Core/SurfacePacking/MeshObjIO.cs:22
  - Frahan.StonePack.Core/SurfacePacking/FaceCornerUvTable.cs:56
  - Frahan.StonePack.Core/SurfacePacking/ChartScaleComputer.cs:14
  - Frahan.StonePack.Core/SurfacePacking/ChartDistortionAnalyzer.cs:27
  - Frahan.StonePack.Core/SurfacePacking/BarycentricMapper2DTo3D.cs:102
  - Frahan.StonePack.GH/SurfacePacking/SurfaceChartComponent.cs:217
gotcha_flags: [G2, M1, M3, M5, T1, T2, T5, T7]
---

## What the code actually does (grounded)

The "BFF flatten" feature is a wrapper, not an in-process solver. The conformal
flattening math (Sawhney-Crane 2017) lives entirely inside an external binary
`bff-command-line.exe`; Frahan only shells out to it
(`BffCommandLineRunner.RunAsync`, BffCommandLineRunner.cs:32-99) with args
`"<in.obj>" "<out.obj>" [--nCones=K] [--normalizeUVs]` (BuildArgs, lines 101-108).
The Frahan-owned, derivable math is the surrounding pipeline orchestrated in
`SurfaceChartComponent.ComputeChart` (SurfaceChartComponent.cs:217-312):

1. Clean + triangulate mesh (MeshCleanup.cs:26-31).
2. Write OBJ, splitting quads to 2 triangles (MeshObjIO.cs:41-57).
3. Run BFF (external).
4. Parse OBJ back into a per-(face,corner) UV table (MeshObjIO.cs:171-228).
5. Build an UNWELDED flat mesh: 3 fresh vertices per triangle so UV seams are
   never bridged (FaceCornerUvTable.cs:56-82).
6. Recover a single global scale (ChartScaleComputer.cs:14-59).
7. Scale the flat mesh to real units (SurfaceChartComponent.cs:285-287).
8. Edge-stretch distortion report (ChartDistortionAnalyzer.cs:27-102).
9. Inverse map 2D curves back to 3D by barycentric blend
   (BarycentricMapper2DTo3D.cs:102-180).

So the equations below describe steps 6, 8, 9 (Frahan math), NOT the BFF
flattening kernel, which I did not read (it is not in this repo).

## Derived equations (from the code, method-class cited only)

### Global chart scale (ChartScaleComputer.cs:35-58)
Per triangle face \(i\) the code sums the three 3D edge lengths of the original
surface mesh and the three edge lengths of the flat (UV) mesh, then returns the
ratio of the totals:
$$
s \;=\; \frac{\sum_{i\in F}\big(\lVert s^i_A-s^i_B\rVert+\lVert s^i_B-s^i_C\rVert+\lVert s^i_C-s^i_A\rVert\big)}
{\sum_{i\in F}\big(\lVert f^i_A-f^i_B\rVert+\lVert f^i_B-f^i_C\rVert+\lVert f^i_C-f^i_A\rVert\big)}
$$
guarded by \( \sum f\text{-edges} \ge 10^{-12}\) (line 52), else \(s=1\). This is a
single isotropic scalar for the whole chart.

### Edge-stretch distortion (ChartDistortionAnalyzer.cs:66-100)
Flat verts are first multiplied by \(s\) (lines 66-68), then per edge \(e\):
$$
\sigma_e \;=\; \frac{\lVert s_p-s_q\rVert_{\text{3D}}}{\,s\,\lVert f_p-f_q\rVert_{\text{2D}}\,},\qquad
\sigma_{\max}=\max_e\sigma_e,\ \ \sigma_{\min}=\min_e\sigma_e
$$
edges with either length \(<10^{-8}\) skipped (line 96). Warn if
\(\sigma_{\max}>1.15\) or \(\sigma_{\min}<0.85\) (lines 24-25, 82-88). This is a
conformal (length) distortion metric, not area; for a perfectly conformal BFF map
\(\sigma\) varies smoothly with local area scale, which is exactly why a single
global \(s\) is an approximation.

### Flatness classifier (ChartFlatnessReport.cs:93)
Given per-face area ratio \(r_i\), the distortion magnitude is symmetrised:
$$
\rho_i=\max\!\Big(r_i,\tfrac{1}{r_i}\Big),\qquad \text{flag if } \rho_i>\tau
$$
\(r_i\le0 \Rightarrow \rho_i=\infty\) (line 93). Pure-managed, no Rhino dep.

### Inverse map: 2D barycentric containment + 3D blend (BarycentricMapper2DTo3D.cs:147-179)
For query point \(p\) and flat triangle \((a,b,c)\), with
\(v_0=b-a,\;v_1=c-a,\;v_2=p-a\) and Gram entries
\(d_{00}=v_0\!\cdot\!v_0\), \(d_{01}=v_0\!\cdot\!v_1\), \(d_{11}=v_1\!\cdot\!v_1\),
\(d_{20}=v_2\!\cdot\!v_0\), \(d_{21}=v_2\!\cdot\!v_1\),
$$
D=d_{00}d_{11}-d_{01}^2,\quad
w_B=\frac{d_{11}d_{20}-d_{01}d_{21}}{D},\quad
w_C=\frac{d_{00}d_{21}-d_{01}d_{20}}{D},\quad w_A=1-w_B-w_C
$$
reject if \(|D|<10^{-12}\) (degenerate tri, line 158); accept containment if
\(w_A,w_B,w_C\ge-10^{-6}\) (line 164). The 3D point is the SAME weights on the
corresponding surface triangle:
$$
P_{\text{3D}} \;=\; w_A\,A_{\text{3D}} + w_B\,B_{\text{3D}} + w_C\,C_{\text{3D}}
$$
(BlendSurfacePoint, lines 176-179). Fallback for near-boundary misses:
`Mesh.ClosestMeshPoint(p, 5*samplingTol)` (line 124-125), reusing Rhino's
returned barycentric `mp.T`.

## Code sketch / reuse seam

- Reuse seam = `FrahanSurfaceChart` (immutable record: SurfaceMesh3D, scaled
  FlatMesh, ChartScale, FlatOuterBoundary, Distortion). Face i in flat == face i
  in 3D is the load-bearing invariant; every downstream consumer relies on it.
- The forward (flatten) seam is a process boundary (OBJ in / OBJ out). Any
  replacement conformal kernel just has to honour the OBJ face order
  (MeshObjIO.cs:41-57 vs 116-121).
- The inverse map (`BarycentricMapper2DTo3D`) is independent of BFF and reusable
  for ANY triangle-to-triangle chart, not just BFF output.

## Gotchas (G/M/N/P/T)

| Code | Verdict | Reason (file:line) |
|---|---|---|
| G1 exact/adaptive predicates | flag | Barycentric containment uses plain float64 with fixed abs eps 1e-6; no adaptive/exact orientation predicate (BarycentricMapper2DTo3D.cs:158,164). |
| G2 degeneracy handled | flag | Degenerate flat tri caught only by `|D|<1e-12` then SKIPPED (line 158); a point inside a sliver triangle silently falls through to ClosestMeshPoint fallback. CullDegenerateFaces runs pre-BFF only (MeshCleanup.cs:30). |
| G3 predicate/construction separation | pass | Containment test (predicate) and 3D blend (construction) are separate methods (TryBarycentricCoords2D vs BlendSurfacePoint, lines 141,168). |
| G4 robust boolean kernel | na | No boolean ops in this path. |
| M1 manifold output | flag | Flat mesh is intentionally UNWELDED (3 verts/tri, FaceCornerUvTable.cs:73-77): non-manifold by construction. Correct for seam handling but not a manifold surface; GetNakedEdges-based boundary extraction depends on this (FrahanSurfaceChart.cs:62). |
| M2 watertight/Euler | na | Chart is an open disk by design; no watertight claim. |
| M3 winding + self-intersection | flag | No check that BFF preserved triangle orientation; a flipped (foldover) flat triangle still passes edge-stretch (scalar lengths ignore sign) (ChartDistortionAnalyzer.cs:98). No self-intersection test on the flat chart. |
| M4 bounded decimation error | na | No remesh/decimation; cleanup only dedups + culls (MeshCleanup.cs:26-31). |
| M5 hidden data-structure cost | flag | Inverse map is O(P*F) linear scan per curve, author-acknowledged "acceptable for <~2000 faces" (BarycentricMapper2DTo3D.cs:106-107); no RTree. |
| N1-N6 NURBS | na | Mesh + polyline only; BrepFace is meshed first (MeshCleanup.cs:46-71). No spline eval. |
| P1-P5 parallel | na | Serial. The only thread use is moving the whole solve off the GH UI thread (SurfaceChartComponent.cs:146); the algorithm itself is single-threaded, no reductions. |
| T1 recenter far-from-origin | flag | No recenter before compute. OBJ written with raw model coords at G10 (MeshObjIO.cs:37-38); a chart far from origin loses ASCII mantissa precision and barycentric Gram products lose bits. |
| T2 abs vs scale-relative eps | flag | Mixed: containment eps 1e-6 ABSOLUTE (line 164), denom guard 1e-12 absolute (line 158), edge skip 1e-8 absolute (ChartDistortionAnalyzer.cs:96), scale guard 1e-12 (ChartScaleComputer.cs:52). None scale to chart size. |
| T3 float32 vs float64 | pass | All double; OBJ written G10 (~10 sig figs), so disk round-trip is the precision floor, not float32. |
| T4 units declared | flag | No model unit recorded anywhere in FrahanSurfaceChart; chart scale is unitless ratio. Violates the standing scale-invariance constraint (mm vs m). |
| T5 tolerance-system count | flag | At least 4 independent epsilons across 3 files plus Rhino doc tolerance plus samplingTolerance default 0.01 (BarycentricMapper2DTo3D.cs:35) -> none reconciled. Matches the standing "3 simultaneous tolerance systems" flag. |
| T6 int64 overflow (Clipper2) | na | No Clipper2/integer scaling in this path. |
| T7 snap/near-degenerate defined | flag | Near-boundary points handled by a magic 5x sampling tol (line 124) then dropped to Point3d.Unset; no defined snap-rounding rule. |

## Numeric stress findings

- Coordinate magnitude: OBJ uses raw world coords at G10 (MeshObjIO.cs:38). A
  quarry-scale chart at, say, UTM-style 10^6 coords loses ~4 decimal places of
  mantissa on disk, then the Gram products \(d_{ij}\) in the containment test lose
  further bits. No recenter (T1).
- Epsilon kind: all absolute (1e-6 / 1e-8 / 1e-12). At mm-scale fractures these
  are far too loose; at m-scale blocks the 1e-6 containment eps is far too tight,
  causing boundary curve points to drop to Unset and the whole curve to return
  null (BarycentricMapper2DTo3D.cs:58). Scale-relative epsilon needed (T2/T7).
- Units: undeclared (T4). Chart scale is a dimensionless ratio, so the same chart
  silently means different physical sizes in a mm vs m document.
- Overflow: none; no integer scaling here (T6 na).
- Determinism: fully deterministic (serial, no parallel reduction). The only
  non-determinism risk is the external BFF binary, which is out of scope.

## Verdict: evolve

The wrapper architecture and the seam-correct UNWELDED-flat-mesh + face-corner UV
table are sound and worth keeping (the seam handling at FaceCornerUvTable.cs:56-82
is genuinely correct and non-trivial). But three things block reuse at fabrication
accuracy: (1) single global isotropic scale on a conformal (area-varying) map
mis-sizes parts; (2) O(P*F) inverse map will not scale past the author's own ~2000
-face ceiling; (3) the tolerance/units story is unresolved (T2/T4/T5/T7) against
the standing scale-invariance constraint. None are fatal; all are bounded edits.

## Evolution plan (tied to flags/bottlenecks)

PERFORMANCE
- Add an RhinoCommon RTree over flat-face bounding boxes to replace the linear
  triangle scan (BarycentricMapper2DTo3D.cs:107, M5). Removes the author-stated
  ~2000-face ceiling; turns O(P*F) into O(P log F).
- Cache the BFF result by mesh hash so an unchanged Surface input skips the
  external process + OBJ disk round-trip entirely (SurfaceChartComponent.cs:242-255,
  named OBJ-I/O bottleneck).

ACCURACY
- Replace the single global scale with per-face / per-cone-patch scale
  (ChartScaleComputer.cs:58) and feed the ChartFlatnessReport max(r,1/r) metric
  (ChartFlatnessReport.cs:93) to drive adaptive subdivision/re-cut. Resolves the
  conformal-vs-global-scale mismatch (T2, M3).
- Add a signed-area sign test per flat triangle to detect BFF foldovers that the
  scalar edge-stretch metric cannot see (ChartDistortionAnalyzer.cs:98, M3 flag).
- Record the Rhino model unit in FrahanSurfaceChart and propagate it downstream so
  mm fractures cannot mix with m blocks (T4 flag, scale-invariance constraint).

SPEED / ROBUSTNESS
- Make the barycentric containment epsilon and the ClosestMeshPoint fallback
  tolerance scale-relative to chart size instead of the hardcoded 1e-6 / 5x
  (BarycentricMapper2DTo3D.cs:124,164). Reconciles the >=4 competing epsilons
  (T2/T5/T7) and stops valid boundary points dropping to Point3d.Unset.
- Recenter the mesh to its bounding-box centroid before OBJ write and undo after
  the inverse map (T1), to keep far-from-origin charts within float64 precision.
