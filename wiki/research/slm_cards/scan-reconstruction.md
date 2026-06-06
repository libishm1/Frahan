---
slug: scan-reconstruction
title: Scan Reconstruct (point cloud -> mesh; alpha-shape / advancing-front / screened-Poisson)
tier: 1
fabrication_direction: bottom_up
geometry_type: unstructured 3D oriented/unoriented point set -> triangle mesh
core_method_class: >
  Edelsbrunner & Mucke 1994 (3D alpha shapes); Cohen-Steiner & Da 2004
  (advancing-front surface reconstruction); Kazhdan & Hoppe 2013 (screened
  Poisson, Geogram-bundled PoissonRecon); Kazhdan/Bolitho/Hoppe 2006 (CGAL
  Poisson fallback). Normals: Hoppe 1992 PCA tangent + MST orientation.
big_o_time: >
  alpha-shape O(n log n) Delaunay + O(F) facet sweep; advancing-front
  O(n log n); Poisson O(n) octree + O(d^3) multigrid solve (d=octree depth,
  default 8) -- depth-bound not n-bound; estimate_normals O(n log n)+MST.
big_o_space: >
  alpha-shape/AF O(n); Poisson O(2^(3d)) sparse octree + O(V+T) output;
  every backend mallocs flat double[3V]+int[3T] across the P/Invoke seam.
parallel_model: task-parallel (one GH background Task -> one isolated worker process; native libs sequential tag)
verdict: evolve
source_files:
  - ScanReconstructComponent.cs:241-344 (dispatch + UI-thread mesh build)
  - AsyncScanComponent.cs:131-175 (background Task state machine)
  - OutOfProcessReconstructor.cs:26-171 (worker IPC + in-proc fallback)
  - ReconstructionNative.cs:220-290 (P/Invoke wrappers, Poisson geo->cgal fallback)
  - native/recon_worker/Program.cs:103-130 (worker dispatch)
  - geogram_shim/frahan_geogram.cpp:979-1045 (screened Poisson)
  - cgal_shim/frahan_cgal.cpp:1316-1586 (alpha-shape, AF, Poisson, normals)
---

## What the code actually does

A `Mode` enum (0 Auto, 1 AlphaShape, 2 Poisson/Geogram, 3 AdvancingFront,
4 Poisson/CGAL) selects a backend (`ScanReconstructComponent.cs:260-302`).
The cloud is flattened to plain `double[]` on the UI thread
(`ScanReconstructComponent.cs:179-227`) so no RhinoCommon geometry crosses
the thread boundary, run on a background `Task`
(`AsyncScanComponent.cs:131-175`), executed in an isolated child process
`frahan_recon_worker.exe` (`OutOfProcessReconstructor.cs:26-99`) so a native
abort cannot kill Rhino, then the raw `verts/tris` arrays are rebuilt into a
`Mesh` on the UI thread (`ScanReconstructComponent.cs:331-339`). If the worker
exe is absent it falls back to in-process P/Invoke
(`OutOfProcessReconstructor.cs:101-120`). Poisson mode tries Geogram first and
falls back to CGAL (`ReconstructionNative.cs:220-268`).

## Derived equations (from the code, method-class cited)

Let the input be \(P=\{p_i\}_{i=1}^{n}\subset\mathbb{R}^3\), optionally with
unit normals \(\{\hat n_i\}\).

### 1. 3D alpha-shape (Mode 1) -- Edelsbrunner & Mucke 1994
Code builds a Delaunay triangulation \(\mathrm{Del}(P)\) then sets a scale
\(\alpha\) (`frahan_cgal.cpp:1341-1348`). A facet \(f\) (with circumradius
\(\rho_f\) of its smallest empty circumscribing ball) is on the alpha-complex
when, per CGAL's classification used at `frahan_cgal.cpp:1355-1356`:
\[
f\in\partial S_\alpha \iff \mathrm{class}(f)\in\{\text{REGULAR},\text{SINGULAR}\},
\]
which for the underlying alpha-complex means
\[
\rho_f \le \alpha \quad\text{and } f \text{ bounds the exterior.}
\]
When `alpha <= 0` the code requests the optimal value that yields one solid
component (`find_optimal_alpha(1)`, `frahan_cgal.cpp:1343`):
\[
\alpha^\* = \min\{\alpha : S_\alpha \text{ has exactly 1 connected component}\}.
\]
Vertices are welded by exact equality on the coordinate triple
\((x,y,z)\) used as a hash key (`frahan_cgal.cpp:1372-1385`):
\[
\text{key}(p)=\big(x,y,z\big),\qquad p\equiv q \iff \text{key}(p)=\text{key}(q).
\]

### 2. Advancing-front (Mode 3) -- Cohen-Steiner & Da 2004
Greedy front growth; the priority for adding candidate vertex \(v\) to a
front edge with neighbour ball radius \(r\) uses radius ratio
\(\beta_{\text{rr}}\) (code default 5.0) and sharpness \(\beta\) (default
0.52), `frahan_cgal.cpp:1431-1436`. The plausibility of a candidate triangle
\(t\) is, in the method class,
\[
\text{accept}(t) \iff \frac{r_{\text{circ}}(t)}{\delta_{\text{local}}}
  \le \beta_{\text{rr}} \;\land\; \angle(\hat n_t,\hat n_{\text{front}})\ge \beta\pi,
\]
where \(\delta_{\text{local}}\) is local sample spacing. Output reuses the
input points verbatim as vertices (`frahan_cgal.cpp:1440-1445`), so the vertex
set is exactly \(P\) and only the index triples are new.

### 3. Screened Poisson (Mode 2 primary, Mode 4 / fallback) -- Kazhdan & Hoppe 2013
Given oriented samples, solve for indicator-like field \(\chi\) whose gradient
matches the smoothed normal field \(\vec V\). The screened energy minimised
(method class; the code calls `GEO::PoissonReconstruction::reconstruct` at
`frahan_geogram.cpp:1024-1029` and `poisson_surface_reconstruction_delaunay`
at `frahan_cgal.cpp:1493-1497`) is
\[
E(\chi)=\int_\Omega \lVert \nabla\chi(x)-\vec V(x)\rVert^2\,dx
        \;+\;\lambda\sum_{i=1}^{n}\big(\chi(p_i)-\tfrac12\big)^2,
\]
with octree depth \(d\) (`set_depth`, `frahan_geogram.cpp:1025`; default 8 at
`frahan_geogram.cpp:998`). The surface is the isolevel at the mean field value
\[
\mathcal{S}=\{x : \chi(x)=\bar\chi\},\qquad
\bar\chi=\frac1n\sum_i\chi(p_i).
\]
Geogram fixes samples-per-node at 1.5 internally and DISCARDS the input
`samples_per_node` (`(void)samples_per_node`, `frahan_geogram.cpp:1026`).
The CGAL fallback sizes its Delaunay refinement from the average spacing
(`frahan_cgal.cpp:1485-1497`):
\[
\bar s=\frac1n\sum_i \frac1{6}\sum_{q\in\mathrm{kNN}_6(p_i)}\lVert p_i-q\rVert,
\quad d_{\text{refine}}=0.375\,\bar s,\;\; \theta=20^\circ,\;\; r=30\bar s.
\]

### 4. Normal estimation (upstream of Poisson) -- Hoppe 1992
`pca_estimate_normals` then `mst_orient_normals` (`frahan_cgal.cpp:1563-1569`):
the normal at \(p_i\) is the smallest-eigenvalue eigenvector of the local
covariance over its \(k\) neighbours (code default \(k=18\),
`frahan_cgal.cpp:1562`),
\[
C_i=\frac1k\sum_{q\in\mathrm{kNN}_k(p_i)}(q-\bar p_i)(q-\bar p_i)^{\!\top},
\quad \hat n_i=\arg\min_{\lVert v\rVert=1} v^\top C_i v,
\]
then globally oriented by minimum spanning tree over the Riemannian graph
weighted \(w_{ij}=1-|\hat n_i\cdot\hat n_j|\).

### IPC marshalling (the seam)
Worker I/O is a flat little-endian binary blob (`OutOfProcessReconstructor.cs:122-171`,
`Program.cs:82-130`): header magic `0x46524543`, status, then `V`, `double[3V]`,
`T`, `int[3T]`. The Rhino mesh is
\(\text{Mesh}=\{(v_{3i},v_{3i+1},v_{3i+2})\}\cup\{\text{face}(t_{3j},t_{3j+1},t_{3j+2})\}\)
(`ScanReconstructComponent.cs:331-339`).

## Code sketch / reuse seam

- Backend selection + auto-fallback: `ScanReconstructComponent.Compute` switch
  (`ScanReconstructComponent.cs:260-302`). Auto = alpha-shape, then a SECOND
  worker for advancing-front on failure.
- Stable IPC contract = the binary header. New backends slot into
  `Program.cs:104` switch + a new `mode` int; no managed signature change.
- The native shims already expose `repair_mesh`, `fill_holes`, `remesh_uniform`,
  `decimate_mesh`, `voxel_downsample`, `estimate_normals` -- all reusable as a
  post/pre pipeline without new native code.

## Gotchas (G / M / N / P / T)

| Code | Verdict | Reason (file:line) |
|---|---|---|
| G1 exact/adaptive predicates | pass | CGAL EPICK = exact predicates, inexact constructions for Delaunay/alpha (`frahan_cgal.cpp:112,1328-1333`). |
| G2 degeneracy handled | flag | <4 / <8 pt guards exist (`frahan_cgal.cpp:1324,1469`), but alpha-shape weld is exact-double equality (`frahan_cgal.cpp:1372`) -- coincident/near-coincident scan points neither merged nor split deterministically. |
| G3 predicate/construction separation | pass | EPICK separates them; reconstruction never feeds constructions back into predicates (no corefinement here). |
| G4 robust boolean kernel | na | reconstruction path does no booleans (the hybrid EPECK kernel at `frahan_cgal.cpp:316-443` is for the boolean entry points, not used here). |
| M1 manifold output | flag | alpha-shape REGULAR+SINGULAR facets (`frahan_cgal.cpp:1356`) can yield non-manifold edges; advancing-front can leave boundary; no manifold check before output. |
| M2 watertight/Euler | flag | only Poisson is closed by construction; AF/alpha may be open; code never tests IsClosed or Euler, just ComputeNormals+Compact (`ScanReconstructComponent.cs:338-339`). |
| M3 consistent winding | flag | AF orients per method; alpha-shape facet corners taken from cell vertex order (`frahan_cgal.cpp:1360-1364`) with no global orientation pass; Rhino ComputeNormals will not fix inconsistent face winding. |
| M4 bounded decimate/remesh error | na | no decimation/remesh on this path (those are separate entry points). |
| M5 hidden data-structure cost | pass | alpha weld + partition use std::map (O(log) per op) but linear in output; documented. |
| N1-N6 NURBS | na | output is a triangle mesh; no NURBS/spline anywhere in the path. |
| P1 deterministic reductions | flag | Poisson multigrid + Delaunay are run sequential-tag (`CGAL::Sequential_tag`, `frahan_cgal.cpp:1485,1563`) so deterministic, BUT auto-mode's two-worker race is not reduction-deterministic across runs if alpha is borderline. |
| P2 thread safety on shared geometry | flag | async state is per-instance and guarded by `_gate` (`AsyncScanComponent.cs:46,92-118`); however a cancelled-then-restarted Task can have a native call still running in the old worker (cooperative cancel only, `AsyncScanComponent.cs:154-165`) -- result is discarded but the process lingers until exit. |
| P3 GPU memory pattern | na | CPU-only; no GPU. |
| P4 load balance | na | single worker, single backend per call. |
| P5 Amdahl serial tail | pass | named: UI-thread flatten + UI-thread Mesh build are the serial tail (`ScanReconstructComponent.cs:179-227,331-339`). |
| T1 recenter before far-from-origin | flag | NO recenter; raw coords go straight to native (`ScanReconstructComponent.cs:179-189`). Georeferenced quarry scans at 1e5-1e6 m lose precision in EPICK Delaunay + the exact-double weld key. |
| T2 absolute vs scale-relative epsilon | flag | mixed: alpha weld = absolute exact equality (`frahan_cgal.cpp:1372`); CGAL Poisson = scale-relative 0.375*spacing (`frahan_cgal.cpp:1490`); not unified. |
| T3 float32 vs float64 | pass | double throughout managed + native + IPC (`OutOfProcessReconstructor.cs:135-142`). |
| T4 units declared+consistent | flag | component never reads ModelUnitSystem/AbsoluteTolerance; treats all coords as unitless doubles; Report omits units (`ScanReconstructComponent.cs:316`). |
| T5 tolerance-system count reconciled | flag | THREE unreconciled systems: alpha weld equality, Poisson spacing-relative sm_distance, downstream Rhino mesh tolerance -- none derived from a common spacing. |
| T6 int64 overflow (Clipper2) | na | no integer-scaled coordinates / Clipper2 on this path. |
| T7 snap-rounding / near-degenerate | flag | alpha weld is bit-exact (`frahan_cgal.cpp:1372`); no snap radius, so two points differing in the last ULP stay distinct, producing sliver triangles. |

## Numeric stress findings

- Coordinate magnitude: unbounded; no recenter. At quarry/UTM scale
  (1e5-1e6 m) doubles keep ~9-10 significant digits -> mm features near the
  edge of representable precision; the alpha-shape exact-double weld key
  (`frahan_cgal.cpp:1372`) becomes unstable (T1).
- Epsilon kind: NOT uniform. Exact equality (alpha weld), scale-relative
  0.375*average-spacing (CGAL Poisson, `frahan_cgal.cpp:1490`), and an
  unused/implicit downstream Rhino tolerance (T2/T5).
- Units: undeclared; the component does not consult the Rhino document unit
  system (T4).
- Overflow: none on this path -- all float64, indices are int32 (V,T well under
  2^31 for any reconstructable cloud); IPC counts are int32 (`Program.cs:151,154`),
  fine for realistic meshes.
- Determinism: sequential-tag native calls are deterministic; auto-mode's
  alpha-then-AF two-process retry is the only non-determinism risk (P1).

## Evolution plan

PERFORMANCE
- Recenter to centroid in `TryRead` (`ScanReconstructComponent.cs:179-227`),
  reconstruct near origin, translate back in `EmitResult`. Removes the T1/T2
  precision loss that dominates accuracy on georeferenced scans, near-zero cost.
- Add optional `VoxelSize` calling `frahan_geogram_voxel_downsample`
  (`frahan_geogram.cpp:1051-1090`, O(n)) before reconstruction; at Poisson
  depth 8 the octree caps useful density so this is near-lossless and cuts
  Delaunay/octree input size by an order of magnitude.

ACCURACY
- Thread ONE ScanIngest tolerance from average point spacing into all three
  tolerance systems (alpha snap-weld radius replacing exact equality at
  `frahan_cgal.cpp:1372`; Poisson sm_distance; downstream mesh weld) and emit it
  in the Report -- closes T5/T7.
- Run the existing `frahan_cgal_repair_mesh` (stitch + orient_to_bound_a_volume,
  `frahan_cgal.cpp:507-519`) after AF/alpha output and report IsClosed +
  edge/Euler counts; auto-pick on watertightness not just non-empty -- closes
  M1/M2/M3.

SPEED
- Collapse auto-mode's two separate worker spawns
  (`ScanReconstructComponent.cs:260-277`) into a single worker that tries
  alpha then advancing-front in-memory (extend `Program.cs:104` to accept a
  fallback list), saving a full process spawn + GEO::initialize + temp-file
  re-read on every borderline cloud.
- Fix the dead `samples_per_node` knob: Geogram discards it
  (`frahan_geogram.cpp:1026`); either wire the real PoissonRecon setter or drop
  the input from the Geogram path so the UX matches the math.
