---
slug: coacd-mesh-boolean
title: CoACD / CGAL / BSP mesh-boolean + Greiner-Hormann 2D clipper cluster
tier: 1
schema: V3-SLM
fabrication_direction: top_down
geometry_type: triangle mesh (float64 MeshSnapshot) + 2D polygon loops
core_method_class: Wei 2022 CoACD; Naylor 1990 BSP-CSG (Wallace csg.js); Greiner-Hormann 1998 + Foster-Hormann 2008; CGAL PMP corefinement; Clipper2 (Vatti)
big_o_time: BSP O(n log n) typ / O(n^2) worst; Greiner-Hormann O(n*m); CoACD+CGAL native (opaque)
big_o_space: BSP O(n) + recursion-depth stack; GH O(n+m+k)
parallel_model: serial
verdict: evolve
gotcha_flags: [G1,G2,G4,M1,M2,M3,T1,T2,T3,T6,T7]
source_files:
  - CoacdMeshDecompose.cs:149-207
  - CgalMeshBoolean.cs:144-237
  - MeshCsg.cs:107-162,363-426
  - GreinerHormannClipper.cs:267-309
  - Clipper2Adapter.cs:96-165
  - Interfaces/MeshSnapshot.cs:13-72
---

## What the code actually is

This "mesh boolean" slug is a CLUSTER of four cooperating layers, all in
`Frahan.StonePack.Core/Masonry/Geometry/`. Read in full:

1. **CoacdMeshDecompose.cs** - a pure P/Invoke marshaller around the optional
   native `frahan_coacd` shim (SarahWeiii/CoACD, SIGGRAPH 2022). It flattens
   `MeshSnapshot` to `double[]`/`int[]`, calls `frahan_coacd_decompose`
   (CoacdMeshDecompose.cs:168-185), and slices per-part output via N+1 start
   arrays (CoacdMeshDecompose.cs:218-249). No managed fallback; throws when the
   DLL is absent (CoacdMeshDecompose.cs:154-157). Contains NO geometry math
   itself - the decomposition lives in native code.
2. **CgalMeshBoolean.cs** - Union/Intersection/Difference. When `frahan_cgal`
   loads it calls CGAL PMP corefinement, Inexact (EPICK) or Hybrid (EPICK store +
   EPECK construct, COMPAS_CGAL pattern, CgalMeshBoolean.cs:44-53,166-179). When
   the DLL is ABSENT it transparently falls back to the managed BSP kernel
   `MeshCsg` (CgalMeshBoolean.cs:150-153,226-237).
3. **MeshCsg.cs** - the pure-managed 3D boolean that actually runs in the
   no-native-DLL configuration. Port of Wallace csg.js (BSP trees). THIS is the
   numeric-risk centre of the cluster.
4. **GreinerHormannClipper.cs** / **Clipper2Adapter.cs** - the 2D analogue
   (planar polygon booleans), GH being the in-tree reference and Clipper2 the
   production back-end.

All 3D coordinates are float64 (`MeshSnapshot.VertexCoordsXyz` is
`IReadOnlyList<double>`, Interfaces/MeshSnapshot.cs:50).

## Derived equations (from the code)

**(1) BSP vertex classification** (MeshCsg.cs:119-125). For plane (n, W) and
vertex v the signed distance is

$$ d(v) = n \cdot v - W,\qquad
\text{class}(v)=\begin{cases}\text{BACK}& d<-\varepsilon\\ \text{FRONT}& d>\varepsilon\\ \text{COPLANAR}&|d|\le\varepsilon\end{cases}$$

with the hardcoded ABSOLUTE epsilon $\varepsilon = 10^{-5}$ (`SplitEps`,
MeshCsg.cs:52). Plane from a triangle: $n=\widehat{(b-a)\times(c-a)}$,
$W=n\cdot a$ (MeshCsg.cs:85-88).

**(2) Spanning-edge split parameter** (MeshCsg.cs:152). For a SPANNING edge
$v_i\to v_j$ the cut point is $v_i + t(v_j-v_i)$ with

$$ t = \frac{W - n\cdot v_i}{\,n\cdot(v_j-v_i)\,} . $$

No guard on the denominator: an edge nearly parallel to the plane (denominator
-> 0) yields $t$ far outside [0,1] and an off-edge cut vertex.

**(3) Boolean ops as BSP clip/invert sequences** (MeshCsg.cs:380-426),
reconstructed from the call order:

$$ A\cup B:\ \text{clip}(A,B);\ \text{clip}(B,A);\ \overline{B};\ \text{clip}(B,A);\ \overline{B};\ \text{build}(A,B) $$
$$ A\cap B = \overline{(\overline A \cup \overline B)}\ \text{(De Morgan, realised by the invert-clip-invert chain, MeshCsg.cs:401-408)} $$
$$ A\setminus B:\ \overline A;\ \text{clip}(A,B);\ \text{clip}(B,A);\ \overline B;\ \text{clip}(B,A);\ \overline B;\ \text{build};\ \overline A $$

**(4) Vertex-dedup spatial hash** (MeshCsg.cs:363-376). Cell index per axis
$i_x=\lfloor x/c\rfloor$, $c=\max(2\,\text{tol},10^{-12})$, packed into 63 bits as

$$ \text{key} = (u_x \ll 42)\,|\,(u_y\ll 21)\,|\,u_z,\quad u_a=(i_a+2^{20})\,\&\,(2^{21}-1). $$

This is correct ONLY while $|i_a|<2^{20}$, i.e. $|x|<2^{20}c\approx 2\times10^9\,c$.
With tol $=10^{-9}$ that is $|x|\lesssim 2\,$mm-scale * 2e9 = a few metres before
the mask aliases distinct vertices into one bucket (T6).

**(5) Greiner-Hormann segment intersection** (GreinerHormannClipper.cs:278-291).
For edges $p_1p_2$ and $q_1q_2$, with $D=\Delta x_1\Delta y_2-\Delta y_1\Delta x_2$,

$$ s=\frac{(x_3-x_1)\Delta y_2-(y_3-y_1)\Delta x_2}{D},\quad
   t=\frac{(x_3-x_1)\Delta y_1-(y_3-y_1)\Delta x_1}{D}, $$

accepted iff $|D|\ge10^{-20}$ and $s,t\in(\text{tol},1-\text{tol})$ (endpoints
excluded). Entry/exit labels alternate from a point-in-polygon seed
(GreinerHormannClipper.cs:296-309): $\text{entry}_0=\neg\text{PIP}(p_0,\text{other})$,
flipping at each intersection. Union swaps both lists, Difference swaps the clip
list (GreinerHormannClipper.cs:119-120).

**(6) CoACD threshold semantics** (CoacdMeshDecompose.cs:33,82). Concavity
threshold is unitless normalized [0..1] unless `RealMetric=true`, in which case
it is interpreted in METRES (`-rm`). This is the only place units enter the
native call.

## Code sketch / reuse seam

- Reuse seam is `CgalMeshBoolean.Run(...)` (CgalMeshBoolean.cs:144): one entry
  point, `IsAvailable` probe selects native vs managed, `out CsgBackend` reports
  which kernel actually ran. Wedge any improved kernel here.
- `MeshSnapshot` (double-only, Rhino-free, Interfaces/MeshSnapshot.cs) is the
  shared currency; it precomputes the AABB, which the recenter lever can exploit.
- 2D seam: `Clipper2Adapter` already exposes tuple-only signatures
  (Clipper2Adapter.cs:148-199) so the GH project can call it without a Clipper2Lib
  reference; GreinerHormann is the transparency/reference fallback only.

## Gotchas (G/M/N/P/T)

| Code | Verdict | Reason (file:line) |
|------|---------|--------------------|
| G1 exact/adaptive predicates | flag | Managed BSP uses raw float dot-products, no adaptive predicates (MeshCsg.cs:119-125). CGAL Hybrid path DOES use EPECK (CgalMeshBoolean.cs:166) but only when the native DLL is present. |
| G2 degeneracy handled | flag | GH excludes endpoint/collinear hits (GreinerHormannClipper.cs:285-286) and header admits coincident edges -> undefined (GreinerHormannClipper.cs:42-46); BSP split has no parallel-edge guard (MeshCsg.cs:152). |
| G3 predicate/construction separation | flag | BSP mixes classification and cut-point construction in one float pass (MeshCsg.cs:113-160); no separated robust predicate layer. |
| G4 robust boolean kernel vs home-rolled float | flag | Default-config kernel is home-rolled float BSP CSG (MeshCsg.cs:9-14); robust kernel (CGAL) is optional and may be absent. |
| M1 manifold output | flag | BSP makes no manifold guarantee; header warns open inputs give "topologically inconsistent results" (MeshCsg.cs:37-39). |
| M2 watertight/Euler stated | flag | No Euler check; output validity deferred to external CutResultValidator (MeshCsg.cs:42). VoronoiBlocks claims watertight but only via native TetGen (GeogramMesh.cs:530-537). |
| M3 consistent winding + self-intersection | flag | Fan-triangulation assumes convex split pieces (MeshCsg.cs:347-358); no self-intersection test; sliver risk acknowledged (MeshCsg.cs:40-42). |
| M4 bounded decimation/remesh error | na | This cluster is booleans/decomposition, not decimation. |
| M5 hidden data-structure cost | flag | `AllPolygons()` rebuilds full lists by recursive AddRange (MeshCsg.cs:257-263) and `ClipPolygons` is stack-recursive (MeshCsg.cs:236-248) - hidden O(n) copies per node and stack-depth risk on dense meshes (MeshCsg.cs:43-46). |
| N1-N6 nurbs | na | No NURBS; all triangle mesh / polygon loops. |
| P1-P5 parallel | na | Entirely serial; no threads, no GPU. Native CoACD/CGAL may thread internally but that is opaque to this code. |
| T1 recenter before far-from-origin compute | flag | No recentering anywhere; raw world coords go straight into dot-products (MeshCsg.cs:119) and the dedup hash (MeshCsg.cs:365). |
| T2 absolute vs scale-relative epsilon | flag | `SplitEps=1e-5` absolute, not bbox-relative (MeshCsg.cs:52); GH `DefaultIntersectionTol=1e-9` absolute (GreinerHormannClipper.cs:58). |
| T3 float32 vs float64 | pass-with-note | All float64 (MeshSnapshot.cs:50) - correct choice; flagged only because the absolute eps still under-resolves at metre+ scale. |
| T4 units declared+consistent | flag | CoACD threshold flips unitless<->metres on RealMetric (CoacdMeshDecompose.cs:82) but the managed/CGAL paths declare no units; caller must keep them consistent. |
| T5 tolerance-system count | flag | At least three coexisting eps: BSP 1e-5 (MeshCsg.cs:52), dedup 1e-9 (MeshCsg.cs:314), GH 1e-9 + denom 1e-20 (GreinerHormannClipper.cs:58,281). Not reconciled. |
| T6 integer-scaling overflow | flag | 21-bit-per-axis dedup hash overflows / aliases for far-from-origin coords (MeshCsg.cs:363-376). Frahan standing flag confirmed here (analogue of the Clipper2 int64 concern). |
| T7 snap-rounding / near-degenerate defined | flag | Degenerate triangles dropped post-hoc (MeshCsg.cs:356) and near-zero-alpha GH cases only approximated by tolerance perturbation (GreinerHormannClipper.cs:31-35); no principled snap-rounding. |

## Numeric stress findings

- **Coordinate magnitude:** No recenter (T1). The dot-product classification
  $n\cdot v - W$ (MeshCsg.cs:119) loses bits as $|v|$ grows; at quarry/architectural
  scale (>10 m, and worse if georeferenced) the $10^{-5}$ comparison is below the
  ULP of $n\cdot v$, so FRONT/BACK/COPLANAR misclassifies and produces slivers.
- **Epsilon kind:** All epsilons are ABSOLUTE (T2): BSP $10^{-5}$, dedup $10^{-9}$,
  GH $10^{-9}$, denom $10^{-20}$. None scale with model size; five values total
  across the cluster, unreconciled (T5).
- **Units:** Only CoACD declares units (metres iff RealMetric, CoacdMeshDecompose.cs:82);
  managed BSP/GH/CGAL are unit-agnostic and trust the caller (T4).
- **Overflow:** The dedup spatial hash (MeshCsg.cs:363-376) packs cell indices into
  21 bits/axis with a $+2^{20}$ bias and a $(2^{21}-1)$ mask. Distinct vertices
  beyond roughly $\pm 2^{20}\cdot c$ from origin collide into one bucket, welding
  unrelated vertices and silently corrupting topology (T6). This is the same class
  of int-range hazard as the Clipper2 int64 standing flag, here in managed code.
- **float32/64:** Correctly float64 throughout (T3 pass); the residual risk is the
  absolute-eps choice, not the storage type.

## Evolution plan

PERFORMANCE / robustness (G4, M3): make CGAL Hybrid (EPECK) the default for any
real slab/block cut and demote `MeshCsg` to a "no native DLL" escape hatch only.
The Hybrid entry already exists (CgalMeshBoolean.cs:166-179); flip the public
Union/Intersection/Difference defaults from `CsgKernelMode.Inexact` (CgalMeshBoolean.cs:102-108)
to Hybrid for masonry inputs. Tied to the sliver bottleneck the code itself flags
(MeshCsg.cs:40-42).

ACCURACY (T1, T2): recenter each `MeshSnapshot` to its precomputed bbox centroid
(Interfaces/MeshSnapshot.cs:31-47) before BSP classification, and replace the
fixed `SplitEps=1e-5` (MeshCsg.cs:52) with $\varepsilon = k\cdot\text{diag(bbox)}$,
$k\approx10^{-7}$. Removes scale-dependent misclassification at architectural
coordinates.

ACCURACY (T6, T7): replace the 21-bit `HashKey` (MeshCsg.cs:363-376) with a full
64-bit hash on recentered coordinates, or delegate dedup to Geogram colocate
(GeogramMesh.RepairMesh, GeogramMesh.cs:203-233) which is BSD-3 and already wired.
Eliminates the far-from-origin vertex-welding overflow.

SPEED (O(n*m)): GreinerHormannClipper.Compute runs a full nested edge-pair scan
(GreinerHormannClipper.cs:82-105). Add a bbox/sweep prefilter, or make Clipper2
(Clipper2Adapter.cs) mandatory above a small loop-count threshold and keep GH only
as a 2-loop reference. Clipper2 already handles the vertex-on-edge / coincident
cases GH cannot (Clipper2Adapter.cs:16-23).
