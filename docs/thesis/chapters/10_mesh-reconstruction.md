# 10. Mesh Processing & Surface Reconstruction

This chapter covers the repository's mesh-geometry back end: the `Mesh`
ribbon tab and the optional native geometry shims it fronts. Three layers
sit here. The lowest is two native shims, `frahan_cgal` (CGAL Polygon Mesh
Processing) and `frahan_geogram` (Bruno Levy's Geogram), each reached through
a managed P/Invoke front end. Above them sit the managed fallbacks, a BSP CSG
kernel and a Rhino-side weld/heal pipeline, so the plugin runs with no native
DLL present. On top sit the Grasshopper wrappers: a mesh-boolean comparator,
repair, decimation, segmentation, straight-skeleton, remesh, hole-fill, and a
three-backend point-cloud reconstructor.

The repository contributes no new mesh algorithm here. Almost everything in
scope is published computational geometry executed by a vendored library, so
the originality story is honest by construction: the algorithms are CGAL's and
Geogram's, the licences are theirs, and the repository's work is the
marshalling boundary, the managed fallback, the out-of-process crash isolation,
and the numeric conditioning around the call. Every claim is anchored to a
`file:line`, an `[Algorithm]` attribute, or a committed reference key.

The governing engineering decision for the whole chapter is the
**mesh-boolean backend routing rule**, stated in the repository law:
"In-process CGAL/geogram BOOLEAN can crash Rhino: route heavy boolean/recon
through the out-of-process worker" (`AGENTS.md:19`). A native abort inside the
host process (an access violation, or a C++ `abort` 0xC0000409 from an
unmasked floating-point exception) would take down Rhino with the user's
unsaved work. So large slab and block cuts do not run N RhinoCommon booleans
in a loop, and they do not run the native kernel in-process either; they run
it in an isolated worker that can crash without consequence
(`OutOfProcessReconstructor.cs:9-19`).

---

## 10.1 The native shim boundary

Both shims share one contract: a lazy probe on first use, a cached
availability flag, and a transparent fallback when the DLL is absent. The
default install ships **no** native DLL, so the probe normally fails closed and
the managed path runs (`AGENTS.md:19`; `CgalMeshBoolean.cs:66-86`,
`GeogramMesh.cs:103-123`). The probe calls a version export inside a
`try`/`catch` for `DllNotFoundException`, `EntryPointNotFoundException`, and
`BadImageFormatException` (the x86/x64 mismatch case), and never rethrows, so a
missing or wrong-bitness shim degrades to managed rather than faulting
(`CgalMeshBoolean.cs:74-83`).

The marshalling is uniform: managed inputs are flattened to `double[]` vertex
coordinates and `int[]` triangle indices, passed to the native entry point,
which allocates output buffers; the managed side `Marshal.Copy`-es them back
and immediately frees the native buffers through a paired free export
(`CgalMeshBoolean.cs:215-220`, `CgalGeometry.cs:301-311`). There is no GC
pinning and no buffer left dangling on the error path: every failure branch
frees before it throws (`CgalGeometry.cs:293-299`).

> **Originality.** `CgalMeshBoolean`, `CgalGeometry`, `GeogramMesh`, and
> `ReconstructionNative` are **wrapper-of-native**: P/Invoke surfaces over
> `frahan_cgal.dll` and `frahan_geogram.dll`. The algorithms execute inside
> the vendored libraries; only the marshalling, the availability probe, and
> the buffer lifetime are repository code (`CgalMeshBoolean.cs:8-23`,
> `GeogramMesh.cs:9-20`). The libraries themselves are **vendored-library**.
> CGAL is GPL in its open-source distribution and the shim header says so
> verbatim (`CgalGeometry.cs:23-28`); Geogram is BSD-3 throughout
> (`GeogramMesh.cs:18-19`). The licence asymmetry drives the install policy of
> section 10.6.

---

## 10.2 CGAL Polygon Mesh Processing

`frahan_cgal` exposes the CGAL Polygon Mesh Processing (PMP) package plus
neighbouring CGAL packages (Botsch et al. 2010). Six operations are wired.

### 10.2.1 Corefinement booleans

The boolean front end (`CgalMeshBoolean`) routes Union, Intersection, and
Difference through CGAL's `corefine_and_compute_boolean_operations`
(`CgalTestComponents.cs:119`, GUID `F2D000A0-CADC-4F2D-A0A0-7E60CADA15A0`).
Corefinement first computes the exact intersection polylines of the two input
surfaces and **inserts** them as constrained edges into both meshes, so the two
surfaces share a common refined edge set along their intersection. The boolean
is then a face-selection over the corefined arrangement: a triangle survives
the union, intersection, or difference depending on which side of the other
surface it lies on. This is the gold standard for 3D mesh robustness because
the cut geometry is shared exactly rather than reconstructed twice from
floating-point intersections.

Two kernel modes are exposed (`CgalMeshBoolean.cs:37-53`). **Inexact** uses
CGAL's EPICK (exact predicates, inexact constructions): predicate signs are
exact, but the constructed intersection points are rounded doubles. Fast,
default, correct for well-conditioned inputs. **Hybrid** keeps storage in
EPICK for speed but constructs the intersection vertices in EPECK (exact
predicates, exact constructions) and round-trips them through a
`Cartesian_converter`, the COMPAS_CGAL pattern, recommended for near-tangent
contacts and multi-cut chains where inexact constructions accumulate error
(`CgalMeshBoolean.cs:44-52`).

### 10.2.2 Repair

`RepairMesh` runs the PMP repair recipe: `triangulate_faces`,
`stitch_borders`, `remove_degenerate_faces`, `orient_to_bound_a_volume` when
the mesh is closed, then `collect_garbage` (`CgalGeometry.cs:317-326`). The
header states why this is stronger than Rhino's `RebuildNormals` +
`UnifyNormals` + `FillHoles`: CGAL stitches coincident half-edges by exact
adjacency, actually merging the topology, where Rhino's heuristics only align
normal vectors (`CgalGeometry.cs:322-326`). The Sanitize Mesh component
(GUID `F2D05A01-...`) fronts this as the upstream gate for any CGAL cut, which
rejects non-manifold input (`MeshSanitizeComponents.cs:53-67`).

### 10.2.3 Lindstrom-Turk simplification

`DecimateMesh` is CGAL `Surface_mesh_simplification` edge-collapse with the
default Lindstrom-Turk cost and placement policies (Lindstrom and Turk 1998;
`CgalGeometry.cs:381-413`; Decimate (CGAL) component
`CgalTestComponents.cs:507`). Edge-collapse simplification removes one edge at
a time, contracting its two endpoints to a single placed vertex. The
Lindstrom-Turk policy chooses that placement by a **memoryless volume-and-shape
optimisation** rather than by accumulating Garland-Heckbert quadrics. For a
collapse it forms a small linear system from the local one-ring: volume
preservation requires that the signed volume swept by the moved triangles sum
to zero,

$$
\sum_{f\in\mathrm{ring}} \mathbf{n}_f\cdot \bar{\mathbf{v}} = \sum_{f\in\mathrm{ring}} \mathbf{n}_f\cdot \mathbf{c}_f,
$$

with $\mathbf{n}_f$ the area-weighted face normal and $\mathbf{c}_f$ a face
point; the remaining degrees of freedom are fixed by minimising a boundary and
shape energy, giving a $3\times 3$ solve per candidate collapse. Three stop
predicates are offered, a ratio of remaining to initial edges, an absolute
edge-count target, and an edge-length floor that **preserves sharp features**
by refusing to collapse edges shorter than the threshold
(`CgalGeometry.cs:157-166`). Geogram offers a different decimation flavour,
vertex-clustering, for the same problem; section 10.3 contrasts the two.

### 10.2.4 SDF segmentation (original derivation of the segmentation field)

`SegmentMeshBySdf` is CGAL `Surface_mesh_segmentation`, the Shape Diameter
Function graph-cut of Shapira et al. (2008) (`CgalGeometry.cs:460-515`;
component `CgalTestComponents.cs:755`, GUID
`F2D000A6-CADC-4F2D-A0A6-7E60CADA15A0`). It partitions a surface into
volumetric-feature clusters, the natural decomposition for breaking a sculpted
stone form into part-like pieces.

The Shape Diameter Function at a face $f$ measures the local object thickness.
From the face centroid, cast a cone of rays of half-angle $\alpha$ (default
$\tfrac{2}{3}\pi$, `CgalGeometry.cs:472`) inward along $-\mathbf{n}_f$, keep the
rays that hit the opposite surface, and take a robust average of the hit
distances:

$$
\mathrm{SDF}(f)=\frac{\displaystyle\sum_{r\in R(f)} w_r\,\ell_r}{\displaystyle\sum_{r\in R(f)} w_r},
\qquad
w_r=\cos\!\big(\angle(r,-\mathbf{n}_f)\big),
$$

over the inlier ray set $R(f)$ (rays whose hit length falls within one
standard deviation of the median, discarding rays that escape through a
concavity). The cosine weight favours rays near the inward normal. A default of
25 rays per facet is used (`CgalGeometry.cs:473`). The raw field is then
normalised and **soft-clustered** by fitting a $k$-component Gaussian mixture
over the log-SDF values, giving each face a probability of belonging to each
thickness class.

The cluster labels are not assigned directly from the mixture, because that
ignores spatial coherence and produces speckle. Instead the labelling minimises
a Markov-random-field energy by graph-cut $\alpha$-expansion:

$$
E(\mathbf{x})=\sum_{f} -\log \Pr\big(\mathrm{SDF}(f)\mid x_f\big)
\;+\;\lambda \sum_{(f,g)\in \mathcal{E}} \big[x_f\neq x_g\big]\,
\frac{-\log\!\big(\theta_{fg}/\pi\big)}{\,1+\,\mathrm{dist}_{fg}\,},
$$

where the data term is the mixture's negative log-likelihood, the pairwise term
penalises a label change across adjacent faces, and the smoothing weight
$\lambda$ is the user's `smoothingLambda` (default 0.26, the CGAL example value;
`CgalGeometry.cs:471`, `:478`). The dihedral factor $-\log(\theta_{fg}/\pi)$
makes a label boundary **cheap across a concave crease** (small $\theta$) and
expensive across a flat band, so cuts fall in the natural part seams. Higher
$\lambda$ yields fewer, more coherent islands; the component clamps it to
$[0,1]$ (`CgalGeometry.cs:485-486`). The managed side then splits the per-face
segment-id array into one sub-mesh per non-empty cluster, re-indexing vertices
locally (`CgalGeometry.cs:615-659`).

A cheaper sibling, `SegmentMeshByAngle`, clusters faces by dihedral-angle walls
alone: `detect_sharp_edges` marks edges whose dihedral exceeds a threshold,
then `connected_components` flood-fills faces treating those edges as barriers
(`CgalGeometry.cs:517-564`; Angle component `CgalTestComponents.cs:867`). It is
the planarity-band detector, faster and parameter-light where full SDF is
overkill.

### 10.2.5 Straight skeleton and convex partition (2D)

`StraightSkeleton2D` wraps CGAL `Straight_skeleton_2` (Aichholzer and
Aurenhammer 1996; `CgalGeometry.cs:257-313`; component
`CgalTestComponents.cs:253`, GUID `F2D000A1-CADC-4F2D-A0A1-...`). The straight
skeleton of a polygon is the trace of its vertices under a uniform inward
**offset**: every edge moves inward at unit speed along its normal, vertices
move along angle bisectors, and the skeleton is the locus of bisector
intersections, with split events when a reflex vertex reaches an opposite edge.
The shim returns the skeleton vertices, edges, and the **time-of-arrival**
$t(v)$ per vertex, which equals the inward offset distance at which that
skeleton node forms, so boundary vertices have $t=0$
(`CgalGeometry.cs:81`). The time field is exactly the medial-offset depth, the
quantity a roof or a chamfer toolpath wants.

`PolygonPartition2D` wraps CGAL `Partition_2` with three modes: Hertel-Mehlhorn
approximate convex (fast), Greene optimal convex ($O(n^4)$, minimal piece
count), and Y-monotone (`CgalGeometry.cs:147-155`, `:417-458`; component
`CgalTestComponents.cs:632`).

### 10.2.6 The heat method (geodesic Voronoi)

`SegmentMeshByGeodesicVoronoi` partitions a surface into geodesic Voronoi cells
around seed points, with cell boundaries that **follow surface curvature**
rather than slicing straight through it (`CgalGeometry.cs:566-613`). For each
seed it computes an on-surface distance field by the Heat Method of Crane et
al. (2013), then assigns each face to the nearest seed by geodesic, not
Euclidean, distance.

The Heat Method computes geodesic distance in three linear steps, which is its
whole appeal: distance becomes two sparse linear solves instead of a
front-propagation. Let $L$ be the cotangent Laplacian and $M$ the lumped-mass
(area) matrix of the mesh. First, integrate the heat equation for a short time
$t$ from a unit source $\delta_s$ at the seed using one backward-Euler step:

$$
(M - t\,L)\,u = \delta_s .
$$

Varadhan's result says that for small $t$ the heat kernel decays like
$u \sim e^{-d^2/4t}$, so $-\sqrt{4t\,\ln u}\,$ already approximates geodesic
distance $d$; but its **gradient direction** is far more accurate than its
magnitude. So second, normalise that gradient to a unit vector field pointing
away from the source,

$$
\mathbf{X}=-\frac{\nabla u}{\lVert\nabla u\rVert}.
$$

Third, recover the distance as the scalar field whose gradient best matches
$\mathbf{X}$, a Poisson solve against the same Laplacian:

$$
L\,\phi=\nabla\!\cdot\mathbf{X}.
$$

$\phi$ is the geodesic distance up to an additive constant fixed by $\phi(s)=0$.
Both solves share the factorisation of $L$, so multiple seeds reuse it. The
cell of seed $i$ is the set of faces where $\phi_i$ is smallest, and because
$\phi$ respects the surface metric, the cell walls bend with the geometry
(`CgalGeometry.cs:566-574`). The cotangent Laplacian requires a clean
2-manifold, which is why repair runs upstream (`CgalGeometry.cs:575`).

---

## 10.3 Geogram

`frahan_geogram` wraps Bruno Levy's Geogram (Levy, INRIA/ALICE, v1.9.9, BSD-3;
`GeogramMesh.cs:9-20`; reference key `[R124]`). It is the licence-clean sibling
of CGAL: BSD-3 throughout, so it ships inside a binary plugin without the GPL
ceremony. Seven operations are wired (`GeogramMesh.cs`).

**Vertex-clustering decimation** (`DecimateMesh`, `GeogramMesh.cs:154-194`;
component GUID `F2D000C0-6E06-...`) snaps vertices to a voxel grid of
`nbBins`$^3$ cells and collapses each occupied cell to one representative. It is
fast and gives a controlled spatial resolution, in contrast to CGAL's
edge-collapse, which is topology-preserving and count-targeted. The component
hover states the trade explicitly: Geogram's for very high-poly scans where you
want a fixed resolution, CGAL's for precise count targeting
(`GeogramMesh.cs:143-148`).

**Repair** (`GeogramMesh.cs:203-233`) wraps `GEO::mesh_repair`: colocate
near-coincident vertices, remove duplicate facets, triangulate. **FillHoles**
(`GeogramMesh.cs:244-276`; Close Holes component GUID `F2D05A02-...`,
`MeshSanitizeComponents.cs:180`) triangulates open boundary loops below an area
and edge-count threshold, the operation that closes a raw scan's spurious
sliver holes while leaving the true outer boundary open. The documented
clean-scan recipe for a raw 2.2M-vertex temple scan is exactly
FillHoles to RemeshUniform to FillHoles, which makes the soup a clean
2-manifold so `IsPointInside` and CGAL booleans work
(memory: example 15 statue-to-blocks).

**RemeshUniform** (`GeogramMesh.cs:316-352`) is centroidal-Voronoi-driven Lloyd
plus Newton optimisation (`GEO::remesh_smooth`), the uniform retriangulation
that regularises a scan. **OBB** uses Geogram `PrincipalAxes3d`, a PCA box with
no Eigen dependency, lighter than CGAL's optimal box (`GeogramMesh.cs:285-314`;
the GH wrapper is correctly self-labelled "Frahan-original" only for the
PCA-OBB assembly, not the eigensolver, `GeogramTestComponents.cs:238`).

**CVT seeds, RVD, and volumetric Voronoi blocks** (`GeogramMesh.cs:396-565`)
compute optimised seed positions, restricted-Voronoi surface partitions, and
closed polyhedral Voronoi blocks. The block path needs the shim built with
TetGen, which is AGPL and therefore OFF by default for a BSD-clean build
(`GeogramMesh.cs:354-361`, `:526-540`); this is the chapter's sharpest licence
edge and is tracked in section 10.6.

> **Originality.** Every Geogram operation above is **wrapper-of-native** over a
> **vendored-library** (Geogram, BSD-3). The `[Algorithm]` attributes credit
> Bruno Levy's Geogram by name and version with the BSD-3 licence and repo URL
> (`GeogramTestComponents.cs:25`, `:163`, `:311`, `:531`); the Lloyd relaxation
> inside CVT/RVD additionally cites Lloyd 1982 `[R87]`
> (`GeogramTestComponents.cs:624-625`). No Geogram source sits in the managed
> tree.

---

## 10.4 Surface reconstruction (modes 1/2/3/4)

The Scan Reconstruct component (GUID
`E4F5A6B7-3101-4F5E-A6B7-C8D9E0F12345`, `ScanReconstructComponent.cs:54`) turns
a point cloud into a closed mesh. It carries three `[Algorithm]` attributes,
one per backend (`ScanReconstructComponent.cs:32-37`), and dispatches by a Mode
enum (`OutOfProcessReconstructor.cs:25`):

- **Mode 1, Alpha Shape (CGAL).** Edelsbrunner and Mucke (1994), reference key
  `[R88]`. A 3D alpha shape carves the Delaunay tetrahedralisation by an
  $\alpha$ radius: a simplex survives iff an empty ball of radius
  $\sqrt{\alpha}$ circumscribes it. Tight, edge-preserving, tolerant of
  unoriented input. `Alpha <= 0` uses CGAL `find_optimal_alpha(1)`
  (`ScanReconstructComponent.cs:74-77`).
- **Mode 2, screened Poisson (Geogram).** Kazhdan and Hoppe (2013),
  reference key `[R91]`; primary backend is Geogram's bundled Kazhdan
  PoissonRecon (`GEO::PoissonReconstruction`), with CGAL Poisson as fallback
  (`ReconstructionNative.cs:220-268`). Requires oriented normals.
- **Mode 3, advancing-front (CGAL).** Cohen-Steiner and Da, BPA-equivalent,
  tolerant of unoriented input (`ScanReconstructComponent.cs:36`).
- **Mode 4, Poisson (CGAL only)** (`ReconstructionNative.cs:271-290`), plus
  **Mode 0 Auto**, which tries alpha-shape then falls back to advancing-front
  (`ScanReconstructComponent.cs:268-285`).

![Poisson-reconstructed quarry bench from a LAS LiDAR cloud (example 04)](../examples/04_scan_to_bench_engineer/04_card_poisson_bench.png)

### 10.4.1 Screened Poisson reconstruction (original derivation)

Poisson reconstruction (Kazhdan, Bolitho, Hoppe 2006, `[R90]`; screened
variant Kazhdan and Hoppe 2013, `[R91]`) recovers a watertight surface from
oriented points by solving a single global Poisson equation. The insight is
that the oriented point samples are samples of the **gradient** of the model's
indicator function $\chi$, which is 1 inside the solid and 0 outside. The
gradient of a step function is a surface delta carrying the inward normal, so
the point normals $\mathbf{N}$, smeared into a vector field $\vec V$ over an
adaptive octree, approximate $\nabla\chi$. Recovering $\chi$ is then the
variational problem of finding the scalar field whose gradient best matches
$\vec V$:

$$
\min_{\chi}\ \big\lVert \nabla\chi - \vec V \big\rVert^2 ,
$$

whose Euler-Lagrange condition is the Poisson equation

$$
\Delta\chi = \nabla\!\cdot \vec V .
$$

The surface is then the isolevel of $\chi$ at the average value it takes at the
samples. The 2013 **screened** extension adds a positional data term so the
surface is pulled back onto the points, not just made normal-consistent,
turning the energy into

$$
\min_{\chi}\ \big\lVert \nabla\chi - \vec V \big\rVert^2
\;+\;\beta \sum_{p\in P} \big(\chi(p)-\tfrac12\big)^2 ,
$$

with the screening weight $\beta$ tying the isosurface to the samples; this
removes the over-smoothing of the unscreened solve and sharpens detail at no
extra asymptotic cost. The octree depth controls resolution: the implementation
defaults to depth 8, typical range 7 to 9, with samples-per-node defaulting to
1.5 (`ScanReconstructComponent.cs:78-83`). The native call passes depth and
samples-per-node straight to PoissonRecon (`ReconstructionNative.cs:85-92`,
`:236`).

### 10.4.2 Numeric conditioning and crash isolation

Two repository deltas wrap the native call. First, **recentering**: before
reconstruction the cloud is translated to its centroid so the Delaunay and
alpha predicates evaluate near the origin, recovering mantissa digits at quarry
and UTM scale; normals are directions and are not translated; the centroid is
added back to the output vertices (`ScanReconstructComponent.cs:250-256`,
`:319-320`). This is the V3 numeric-hygiene rule applied at the reconstruction
boundary, and it is Rhino-free (`GeometryNumerics` is in Core).

Second, **out-of-process isolation**. `OutOfProcessReconstructor` writes the
cloud to a temp file, launches `frahan_recon_worker.exe` with the native shim
DLLs alongside it, and reads back a length-prefixed binary result guarded by a
`'FREC'` magic word (`OutOfProcessReconstructor.cs:22`, `:122-171`). A native
abort kills only the worker; the host detects it from the missing output magic
or a non-zero exit code and surfaces a clean managed error,
"the reconstruction backend faulted; Rhino is unaffected"
(`OutOfProcessReconstructor.cs:75-80`). If the worker exe is not deployed, it
falls back to the in-process (still FP-guarded) path with a note
(`OutOfProcessReconstructor.cs:43-49`). The component runs all this on a
background thread behind a default-false `Run` gate, so opening a definition
never triggers a reconstruction and the canvas never freezes
(`ScanReconstructComponent.cs:25-30`, `:97-100`). Finally the raw soup is
cleaned: the largest edge-connected component is kept and dangling alpha-shape
facets dropped (`ScanReconstructComponent.cs:318-322`).

![Photogrammetric scan reconstructed to a closed mesh (example 07)](../examples/07_scan_ingest_full/07_scan_to_mesh.png)

> **Originality.** Scan Reconstruct and `ReconstructionNative` are
> **wrapper-of-native** over **vendored-library** backends: CGAL (alpha-shape,
> advancing-front, CGAL Poisson; GPL) and Geogram-bundled Kazhdan PoissonRecon
> (MIT, BSD-clean). The repository contributions are the recenter conditioning,
> the out-of-process crash isolation, the binary IPC, the async Run-gated
> wrapper, and the soup cleanup, none an algorithm. The `[Algorithm]`
> attributes cite Edelsbrunner-Mucke 1994, Kazhdan-Hoppe 2013, and
> Cohen-Steiner-Da 2004 correctly (`ScanReconstructComponent.cs:32-37`).

---

## 10.5 The managed fallbacks

When no native DLL is present, two managed kernels keep the plugin working.

**MeshCsg** is a pure-managed BSP-tree CSG: build a binary space partition per
input mesh, then implement Union, Intersection, and Difference as De Morgan
sequences of `ClipTo` and `Invert` on the two trees
(`MeshCsg.cs:8-40`). It is the silent fallback under `CgalMeshBoolean` when the
shim is absent (`CgalMeshBoolean.cs:150-153`, `:226-236`). It is a **port of
Evan Wallace's csg.js (MIT)** and the header says so (`MeshCsg.cs:9-10`). That
is a **direct-port** under a permissive licence and owes a THIRD_PARTY_NOTICES
attribution row, but no copyleft.

**MeshRepair / Frahan Mesh Repair** (GUID `AB12C00A-...`,
`MeshRepairComponent.cs:37`) is the Rhino-side weld / cull-degenerate /
heal-naked-edges / unify-normals pipeline, cited to the standard PMP reference
(Botsch et al. 2010, `[R81]`; `MeshRepairComponent.cs:19`). **Mesh
Diagnostics** (GUID `AB12C005-...`) is a read-only inspector over the same
reference (`MeshDiagnosticsComponent.cs:18`). Sanitize Mesh and Close Holes
front the CGAL and Geogram repair paths with a Geogram repair fallback
(`MeshSanitizeComponents.cs:61-63`).

> **Originality.** `MeshCsg` is **direct-port** (csg.js, MIT,
> `MeshCsg.cs:9-10`). `MeshRepairComponent` and `MeshDiagnosticsComponent` are
> **facade-over-primitives** composing RhinoCommon mesh operations behind a
> cited recipe (Botsch et al. 2010); they add no new algorithm, only the
> orchestration and the diagnostic readout (`MeshRepairComponent.cs:19`,
> `MeshDiagnosticsComponent.cs:18`).

---

## 10.6 Licensing posture (the load-bearing decision)

The whole-chapter mitigation is architectural: the default install ships no
native DLL and links no GPL, AGPL, or non-commercial code (originality matrix,
licensing register, flags E3/E5/E6). CGAL's PMP, simplification,
reconstruction, straight-skeleton, and partition packages are **GPL** and
depend transitively on GMP; they are reached only through the optional
`frahan_cgal` shim, with the managed BSP CSG (csg.js, MIT) as the in-tree
fallback. Geogram is **BSD-3** and stays clean, but its TetGen path (needed for
volumetric Voronoi blocks) is **AGPL** and is OFF by default
(`-DFRAHAN_WITH_TETGEN=OFF`; `GeogramMesh.cs:354-361`). The Kazhdan PoissonRecon
bundled in Geogram is **MIT** and stays in the default path with attribution.
A commercial release would buy the CGAL commercial packages or stay on the
Geogram and managed paths only.

---

## 10.7 Status & what's left

- **No native DLL in the default install.** Every CGAL and Geogram operation in
  this chapter is unavailable until the user builds `frahan_cgal` /
  `frahan_geogram` from `native/`. The default experience is the managed BSP
  CSG plus the Rhino-side repair only. This is the licence mitigation, not a
  defect, but it is the single biggest gap between the documented capability and
  the out-of-box behaviour (`AGENTS.md:19`, `CgalGeometry.cs:23-28`). Severity:
  high.
- **CGAL/geogram components live on the `Lab` subcategory, not `Mesh`.** The
  native shim wrappers (Mesh CSG (CGAL), Decimate (CGAL/Geogram), Segmentation,
  Skeleton, Remesh, Tetrahedralize) are filed under `Frahan > Lab`, while
  Repair, Diagnostics, Sanitize, Close Holes, and Scan Reconstruct are on
  `Mesh` (`CgalTestComponents.cs:133`, `MeshRepairComponent.cs:33`). The tab
  split is a UX inconsistency, not a code fault. Severity: low.
- **TetGen AGPL gate.** Volumetric Voronoi blocks (`VoronoiBlocks`) and
  tetrahedralisation throw with a clear message when the shim is built BSD-clean
  (`GeogramMesh.cs:354-361`, `:526-540`). The volumetric-block pipeline is
  documented but unavailable in the default build. Severity: medium.
- **THIRD_PARTY_NOTICES owed.** The csg.js port (`MeshCsg.cs:9-10`), Geogram,
  and the bundled Kazhdan PoissonRecon all require attribution rows; no
  `THIRD_PARTY_NOTICES.md` was at repo root at audit time (licensing register,
  flag 10). Severity: medium (provenance, not copyleft).
- **Figures are borrowed.** This chapter's renders come from example 04
  (Poisson bench), example 07 (scan-to-mesh), and example 15 (clean remesh);
  there is no dedicated CGAL-segmentation or straight-skeleton example render.
  Severity: low (documentation gap).
- **No managed fallback for OBB / skeleton / partition / segmentation.** These
  are CGAL-only and throw when the shim is absent, by design
  (`CgalGeometry.cs:14-16`). A user without the shim cannot segment or skeleton
  a mesh at all. Severity: medium.

![Raw scan cleaned and uniformly remeshed before block decomposition (example 15)](../examples/15_statue_to_blocks/15_step1_clean_bunny_remesh.png)

---

## References (this chapter)

- Botsch, M., Kobbelt, L., Pauly, M., Alliez, P., Levy, B. (2010). Polygon mesh
  processing. AK Peters / CRC Press. ISBN 978-1568814261. [R81]
- Lloyd, S.P. (1982). Least squares quantization in PCM. IEEE Transactions on
  Information Theory 28(2):129-137. DOI 10.1109/TIT.1982.1056489. [R87]
- Edelsbrunner, H., Mucke, E.P. (1994). Three-dimensional alpha shapes. ACM
  Transactions on Graphics 13(1):43-72. DOI 10.1145/174462.156635. [R88]
- Kazhdan, M., Bolitho, M., Hoppe, H. (2006). Poisson surface reconstruction.
  Eurographics Symposium on Geometry Processing, pp 61-70. [R90]
- Kazhdan, M., Hoppe, H. (2013). Screened Poisson surface reconstruction. ACM
  Transactions on Graphics 32(3):29:1-29:13. DOI 10.1145/2487228.2487237. [R91]
- Cohen-Steiner, D., Alliez, P., Desbrun, M. (2004). Variational shape
  approximation. ACM Transactions on Graphics (SIGGRAPH 2004) 23(3):905-914.
  DOI 10.1145/1015706.1015817. [R92]
- Crane, K., Weischedel, C., Wardetzky, M. (2013). Geodesics in heat: a new
  approach to computing distance based on heat flow. ACM Transactions on
  Graphics 32(5):152. DOI 10.1145/2516971.2516977. [R95]
- Aichholzer, O., Aurenhammer, F. (1996). Straight skeletons for general
  polygonal figures in the plane. COCOON 1996, LNCS 1090, pp 117-126. [R96]
- Lindstrom, P., Turk, G. (1998). Fast and memory efficient polygonal
  simplification (quadric edge-collapse). IEEE Visualization '98, pp 279-286.
  [R97]
- Shapira, L., Shamir, A., Cohen-Or, D. (2008). Consistent mesh partitioning
  and skeletonisation using the shape diameter function. The Visual Computer
  24(4):249-259. DOI 10.1007/s00371-007-0197-5. [R98]
- Levy, B. (INRIA/ALICE). Geogram: a programming library of geometric
  algorithms (v1.9.9). BSD-3. https://github.com/BrunoLevy/geogram. [R124]
- Wallace, E. csg.js (MIT). Constructive solid geometry via BSP trees. Ported
  as the managed `MeshCsg` boolean fallback.
