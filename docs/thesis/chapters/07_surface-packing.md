# 07. Surface Packing & Conformal Unwrapping

This chapter covers the **Surface Packing** ribbon tab: the pipeline that takes a
curved 3D stone surface, flattens it to a planar chart, packs parts into that flat
chart, and lifts the packed parts back onto the curved surface. It is the
freeform-cladding spine of the toolkit: a Trencadis mosaic, a tile field, or any
2D part set can be draped onto a sculpted face so the pieces follow the surface and
butt edge to edge in 3D.

The subsystem is deliberately split across a process boundary. The hard
mathematics, conformal planar parameterization, is done by an external solver,
Boundary First Flattening (Sawhney and Crane 2017), shipped as a single static
executable. Everything Frahan owns sits **around** that solver: a seam-correct flat
mesh build, a global chart-scale recovery, an edge-stretch distortion metric, and a
barycentric inverse map that pulls packed UV curves back to 3D. The 2D packing
itself was recently re-pointed from the legacy V506 nester onto the deterministic
hole-aware engine (`ContactNfpHoleNester`), so the surface components inherit
0-overlap-by-construction layouts and a live async canvas.

The Core lives at `src/Frahan.StonePack.Core/SurfacePacking` (Rhino-free where it
can be: the inverse map and the chart record still use `Rhino.Geometry`). The
Grasshopper front end lives at `src/Frahan.StonePack.GH/SurfacePacking`.

![Twisted block split into surfaces by dihedral angle](../examples/13_surface_mapping/13_surface_segments.png)

![Trencadis mosaic draped on the twisted surface via a BFF chart](../examples/13_surface_mapping/13_surface_trencadis.png)

*Example 13: a twisted monument (130 deg over its height) is split by CGAL dihedral
segmentation into 6 regions, then a 176-shard Trencadis mosaic is mapped onto the
curved surface through the BFF chart and the barycentric inverse map.*

---

## 7.1 The pipeline at a glance

The forward chart is built by `SurfaceChartComponent.ComputeChart`
(`SurfaceChartComponent.cs:217`). The ten steps are:

1. Clean and triangulate the input mesh (`MeshCleanup`).
2. Write an OBJ, splitting quads into two triangles (`MeshObjIO`).
3. Run the external BFF binary (`BffCommandLineRunner`).
4. Parse the output OBJ into a per-(face, corner) UV table (`FaceCornerUvTable`).
5. Build an **unwelded** flat mesh: three fresh vertices per triangle so UV seams
   are never bridged.
6. Recover a single global scale (`ChartScaleComputer`).
7. Scale the flat mesh to real units.
8. Compute the edge-stretch distortion report (`ChartDistortionAnalyzer`).
9. (Downstream) pack parts into the flat chart with `ContactNfpHoleNester`.
10. (Downstream) inverse-map packed 2D curves to 3D by barycentric blend
    (`BarycentricMapper2DTo3D`).

The load-bearing invariant across the whole pipeline is that **face `i` in the flat
mesh corresponds exactly to face `i` in the 3D surface mesh**. Every chart-scale,
distortion, and inverse-map computation relies on this index alignment. The
`FrahanSurfaceChart` record (`FrahanSurfaceChart.cs:12`) carries the immutable
result: the original 3D mesh, the scaled flat mesh, the scalar chart scale, the
flat outer boundary polyline, and the distortion report.

---

## 7.2 Conformal flattening (BFF, external)

### 7.2.1 The mathematics BFF solves

Boundary First Flattening (Sawhney and Crane 2017) computes a discrete conformal
map from a triangle mesh with disk topology to the plane. A conformal map preserves
angles, so a flattening is conformal when its differential is, locally, a rotation
composed with a single positive scaling. Equivalently the map distorts lengths by a
scalar **conformal factor** $e^{u}$ that varies over the surface but applies
isotropically at each point:

$$
\lVert d\Phi(X) \rVert \;=\; e^{u}\,\lVert X \rVert \quad\text{for every tangent vector } X .
$$

The factor $u$ is governed by the Yamabe equation, which relates the change in
Gaussian curvature to the Laplacian of $u$:

$$
\Delta u \;=\; K - e^{2u}\,\tilde{K},
$$

where $K$ is the original Gaussian curvature and $\tilde{K}$ the target. For a flat
chart $\tilde{K}=0$ in the interior, so the interior curvature must be absorbed and
all the cone defect pushed to the boundary or to a small set of cone singularities.
The Sawhney-Crane contribution is to pose this as a **boundary** problem: prescribe
either the boundary scale factor $u|_{\partial M}$ or the geodesic curvature
$\tilde{k}|_{\partial M}$, then recover the other via a Dirichlet-to-Neumann map
built from a Cherrier-type linear system, and finally integrate the curve and
extend it to the interior with two harmonic conjugate functions. The dense work is
a sparse Cholesky factorization (SuiteSparse) of the cotangent-Laplace operator;
the boundary solve is cheap once that factorization exists.

The chapter does not reimplement these equations. The BFF kernel is not in this
repository. Frahan calls it as a process: `bff-command-line.exe "<in.obj>"
"<out.obj>" [--nCones=K] [--normalizeUVs]` (`BffCommandLineRunner.BuildArgs`,
`BffCommandLineRunner.cs:101`). The cone count $K$ exposes the cone-singularity
extension, and `--normalizeUVs` rescales the output UVs to a unit box, which is why
Frahan must then recover the real-world scale itself (Section 7.3).

### 7.2.2 Static single-exe build

The shipped artifact is a **static** `bff-command-line.exe`: 17 third-party DLLs
folded into one 38 MB executable with only `KERNEL32` and `msvcrt` imports
(`install/tools/BFF-BUILD-STATIC.md`). The build links SuiteSparse (CHOLMOD, UMFPACK,
AMD/COLAMD), OpenBLAS with its bundled static LAPACK, and GFortran statically with a
single `g++` invocation under MSYS2 mingw64, skipping the GUI app and the
non-essential submodules. The `-Wl,--start-group ... --end-group` link group
resolves the circular SuiteSparse-to-BLAS static references. The build recipe
records that the output UV is byte-identical to the upstream dynamic build, which is
the parity check that lets the static exe be treated as the same tool. This swap was
made because the multi-DLL deploy was breaking; the commit is
`d1b5c5b` ("static single-exe BFF — 17 DLLs -> 1 self-contained exe").

### 7.2.3 Async wrapper and pipe-deadlock handling

`BffCommandLineRunner.RunAsync` (`BffCommandLineRunner.cs:32`) runs the process with
stdout and stderr consumed asynchronously on separate handlers, which is the correct
way to avoid the classic pipe deadlock where a child fills its OS pipe buffer while
the parent blocks on `WaitForExit`. A `CancellationTokenSource` enforces the timeout
and kills the process on overrun. The component (`SurfaceChartComponent`) is a
`GH_TaskCapableComponent`: the whole solve runs on a pool thread so the BFF process
launch never blocks the Grasshopper UI thread.

**Originality.** The BFF flattening kernel is third-party. The component that wraps
it is a **wrapper-of-native** with a small **clean-room** orchestration shell around
it.

- Evidence (kernel): `[Algorithm("BFF boundary-first flattening", "Sawhney and
  Crane 2017, ACM TOG 36(4):109", Doi="10.1145/3072959.3056432", Note="External BFF
  command-line exe; Frahan wraps the binary")]` at `SurfaceChartComponent.cs:43`.
- Evidence (process boundary): `BffCommandLineRunner.RunAsync` shells out only;
  `wiki/research/slm_cards/bff-surface-flatten.md` states "the conformal flattening
  math (Sawhney-Crane 2017) lives entirely inside an external binary."
- Licence note: BFF ships under its upstream MIT-style licence; SuiteSparse,
  OpenBLAS, and GFortran notices are owed in the dist `NOTICE` (see the build recipe).

---

## 7.3 Chart-scale recovery (Frahan, clean-room)

BFF run with `--normalizeUVs` returns UVs in a unit box. To turn the chart into a
fabrication template, the flat mesh must be rescaled to real-world surface
distances. `ChartScaleComputer.ComputeGlobalScale` (`ChartScaleComputer.cs:14`)
recovers a **single isotropic scalar** as the ratio of total 3D edge length to
total flat edge length, summed over all triangular faces. For the triangle set $F$,
with surface-triangle vertices $s^i_A, s^i_B, s^i_C$ and flat-triangle vertices
$f^i_A, f^i_B, f^i_C$:

$$
s \;=\;
\frac{\displaystyle\sum_{i \in F}\big(\lVert s^i_A - s^i_B\rVert + \lVert s^i_B - s^i_C\rVert + \lVert s^i_C - s^i_A\rVert\big)}
{\displaystyle\sum_{i \in F}\big(\lVert f^i_A - f^i_B\rVert + \lVert f^i_B - f^i_C\rVert + \lVert f^i_C - f^i_A\rVert\big)} .
$$

The denominator is guarded against degeneracy: if $\sum f\text{-edges} < 10^{-12}$ or
no valid triangle was found, the method returns $s = 1$ with a warning
(`ChartScaleComputer.cs:52`). The scale is then applied to the flat mesh by a
uniform `Transform.Scale` (`SurfaceChartComponent.cs:285`), so `FrahanSurfaceChart.FlatMesh`
is stored **post-scale** in real units. Downstream packing consumes it directly with
no further scaling.

**Derivation and its limitation.** Why a length ratio rather than an area ratio? On
a conformal map the local length scale is $e^{u}$ and the local area scale is
$e^{2u}$. Taking the ratio of total perimeters rather than total areas keeps the
recovered $s$ a length scale, so it composes directly with edge-length distortion in
Section 7.4. But $u$ is **not** constant over a curved surface (that is the whole
point of a conformal map: it trades angle preservation for spatially varying area
scale). A single global $s$ is therefore the perimeter-weighted average of $e^{u}$
over the chart, and it mis-sizes parts wherever the local conformal factor departs
from that average. This is the central accuracy compromise of the subsystem and is
documented as an evolution target (per-face or per-cone-patch scale) in the SLM
card.

**Originality.** `ChartScaleComputer` is **clean-room** Frahan math: a global
scale recovery derived from the conformal-factor structure, with no upstream code.

- Evidence: `[Algorithm("Conformal chart-scale recovery", "Frahan-original
  barycentric UV-to-real-world scaling")]` at `SurfaceChartComponent.cs:44`; full
  derivation in `wiki/research/slm_cards/bff-surface-flatten.md`.
- Tier: B (faithful, derivable; the single-global-scale approximation is the
  known limitation, not a novelty claim).

---

## 7.4 Edge-stretch distortion metric (Frahan, clean-room)

A fabrication template is only trustworthy if its distortion is bounded.
`ChartDistortionAnalyzer.Analyze` (`ChartDistortionAnalyzer.cs:27`) measures
**edge-scale** distortion (not area) because cut-path accuracy depends on length,
not on enclosed area. The flat vertices are first scaled by $s$, then for each
triangle edge $e$ joining endpoints $p, q$ the stretch ratio is:

$$
\sigma_e \;=\; \frac{\lVert s_p - s_q \rVert_{\text{3D}}}{\,s \,\lVert f_p - f_q \rVert_{\text{2D}}\,},
\qquad
\sigma_{\max} = \max_e \sigma_e,
\qquad
\sigma_{\min} = \min_e \sigma_e .
$$

Edges with either endpoint distance below $10^{-8}$ are skipped
(`ChartDistortionAnalyzer.cs:96`). The report warns when
$\sigma_{\max} > 1.15$ (high stretch, increase clearance) or
$\sigma_{\min} < 0.85$ (high compression, parts under-scaled after mapping); the
thresholds are constants at `ChartDistortionAnalyzer.cs:24`.

For a perfectly conformal BFF map $\sigma_e \to e^{u}/s$ varies smoothly with the
local conformal factor, which is exactly why $\sigma_{\max}$ and $\sigma_{\min}$
bracket the spread of $e^{u}$ around the global average $s$. The metric therefore
serves two roles: it is the fabrication-trust gauge, and it is the diagnostic that a
single global scale is or is not adequate for a given chart.

A complementary symmetrized **flatness** classifier lives in `ChartFlatnessReport`:
given a per-face area ratio $r_i$ it forms

$$
\rho_i = \max\!\Big(r_i, \tfrac{1}{r_i}\Big), \qquad \text{flag if } \rho_i > \tau,
$$

with $r_i \le 0 \Rightarrow \rho_i = \infty$ (`ChartFlatnessReport.cs:93`). This is
a pure-managed area-distortion symmetrization with no Rhino dependency.

**A known blind spot.** Because $\sigma_e$ uses scalar lengths, it cannot see a
**foldover**: a flat triangle whose orientation BFF flipped still passes the
edge-stretch test (lengths ignore sign). Detecting foldovers needs a signed-area
sign test per flat triangle, which is logged as an evolution item (flag M3).

**Originality.** `ChartDistortionAnalyzer` and `ChartFlatnessReport` are
**clean-room** Frahan metrics derived from the conformal-factor structure.

- Evidence: derivation at `wiki/research/slm_cards/bff-surface-flatten.md`
  (sections "Edge-stretch distortion" and "Flatness classifier"); no upstream
  code, both are pure managed.
- Tier: B.

---

## 7.5 Seam-correct flat mesh (Frahan, clean-room)

A conformal chart of a non-trivial surface needs **seam cuts** to open the surface
into a disk. Across a seam, one shared 3D vertex maps to two **different** UV
positions. A naive welded mesh would average those, bridging the seam and corrupting
the chart. `FaceCornerUvTable` (`FaceCornerUvTable.cs:37`) solves this by keying UVs
on `(faceIndex, cornerIndex)` rather than on vertex index, so each face corner
carries its own UV. `ToFlatUnweldedMesh` (`FaceCornerUvTable.cs:56`) then emits
three fresh vertices per triangle:

- For triangle $i$ it reads the three corner UVs $(u_A, v_A), (u_B, v_B), (u_C, v_C)$.
- It adds three new flat vertices at those UV positions in the $Z=0$ plane.
- It adds one face on the new vertices, preserving face index $i$.
- A missing UV throws `InvalidOperationException` rather than defaulting to $(0,0)$,
  so a parse bug surfaces immediately instead of silently corrupting one corner.

The output is intentionally **non-manifold** (three vertices per triangle), which is
correct for seam handling but means boundary extraction must use naked-edge tracing.
`FrahanSurfaceChart.ExtractOuterBoundary` (`FrahanSurfaceChart.cs:58`) calls
`GetNakedEdges` and takes the **longest** loop as the outer boundary; the shorter
loops are sheet holes. The custom `FaceCornerKey.GetHashCode` uses
`(FaceIndex * 397) ^ CornerIndex` to avoid boxing on net48, consistent with the
repo build constraints.

**Originality.** **Clean-room** Frahan engineering. The SLM card flags the
unwelded-flat + face-corner-UV table as "genuinely correct and non-trivial". No
upstream code.

- Evidence: `FaceCornerUvTable.cs:56`; `wiki/research/slm_cards/bff-surface-flatten.md`
  step 5.
- Tier: C-to-B (correct, non-trivial seam engineering).

---

## 7.6 Barycentric inverse map (Frahan, clean-room) — and a citation correction

After parts are packed into the flat chart, their 2D curves must be lifted back to
the 3D surface. `BarycentricMapper2DTo3D` (`BarycentricMapper2DTo3D.cs:25`) samples
each packed curve to a polyline, then maps each sample point.

For a query point $p$ and a flat triangle $(a, b, c)$, with
$v_0 = b - a$, $v_1 = c - a$, $v_2 = p - a$ and the 2D Gram matrix entries
$d_{00} = v_0\!\cdot\!v_0$, $d_{01} = v_0\!\cdot\!v_1$, $d_{11} = v_1\!\cdot\!v_1$,
$d_{20} = v_2\!\cdot\!v_0$, $d_{21} = v_2\!\cdot\!v_1$, Cramer's rule on the normal
equations gives the barycentric weights:

$$
D = d_{00}d_{11} - d_{01}^2,
\qquad
w_B = \frac{d_{11}d_{20} - d_{01}d_{21}}{D},
\qquad
w_C = \frac{d_{00}d_{21} - d_{01}d_{20}}{D},
\qquad
w_A = 1 - w_B - w_C .
$$

The triangle is rejected as degenerate when $|D| < 10^{-12}$
(`BarycentricMapper2DTo3D.cs:158`); containment is accepted when
$w_A, w_B, w_C \ge -10^{-6}$ (`BarycentricMapper2DTo3D.cs:164`). The 3D position
applies the **same** weights to the corresponding surface triangle
(`BlendSurfacePoint`, `BarycentricMapper2DTo3D.cs:168`):

$$
P_{\text{3D}} \;=\; w_A\,A_{\text{3D}} + w_B\,B_{\text{3D}} + w_C\,C_{\text{3D}} .
$$

This is correct precisely because of the face-index invariant: weights computed on
flat face $i$ are valid on surface face $i$. For sample points that fall just
outside the chart boundary (a common artefact of sampling a curve that runs along
the boundary), a fallback uses `Mesh.ClosestMeshPoint(p, 5 \times \text{samplingTol})`
and reuses Rhino's returned barycentric weights `mp.T`
(`BarycentricMapper2DTo3D.cs:124`). The search is a linear $O(P \cdot F)$ scan over
all flat faces, which the author flags as "acceptable for mesh densities < ~2000
faces"; an RTree is the documented scale-up.

### A misattributed citation

The `[Algorithm]` attribute on `PackOnSurfaceComponent.cs:41` cites this lift as
"Floater 2003 ... Mean value coordinates" (`Doi=10.1016/S0167-8396(03)00002-5`).
**The shipped code does not implement mean-value coordinates.** It implements plain
triangle barycentric interpolation (Cramer's rule on the 2D Gram system above),
which is the classical piecewise-linear inverse map, not Floater's generalized
barycentric coordinates for polygons. The audit ground truth is explicit:
"Inverse map = standard barycentric interpolation. NO Floater-2003 MVC code present"
(`wiki/research/slm_cards/bff-surface-flatten.md:6`). The correct attribution for
the implemented method is classical barycentric interpolation over a triangle mesh;
Floater (2003) stands here only as **related background** for the mean-value-coordinate
family, not as the implemented method. This is a **citation-correctness** flag, not a
code defect: the math that ships is correct, it is simply not the cited method.

**Originality.** **Clean-room** classical barycentric interpolation. The method is
textbook; the contribution is the seam-correct, face-index-aligned plumbing that
makes it a reliable inverse for a BFF chart. It is reusable for **any**
triangle-to-triangle chart, not only BFF output.

- Evidence: `BarycentricMapper2DTo3D.cs:141-179`; citation flag at
  `wiki/research/slm_cards/bff-surface-flatten.md:6`.
- Tier: C (commodity math), with a B-grade reuse seam.

---

## 7.7 The packing engine swap: V506 → HoleNest

The two packing components, **Pack On Surface** and **Pack Surfaces**, were
recently rewritten to drop the legacy V506 nester and call the deterministic
hole-aware Core engine `ContactNfpHoleNester` (the CNH family). The commit is
`05c7e82` ("feat(surface): Pack Surfaces + Pack On Surface onto HoleNest engine +
self-trigger async"). Both components kept their `ComponentGuid` so existing
canvases keep resolving them: `B7E4D9C1-3F8A-4B2E-91C6-5D7F3A8B2E1D` (Pack On
Surface) and `C4A8D2E1-7F3B-4C5D-9A2E-6B8D4F1E3C7A` (Pack Surfaces).

### What the swap buys

The hole-aware engine builds no-fit and inner-fit polygons exactly as Clipper2
Minkowski operations and places by bottom-left-fill. For a part and an obstacle the
no-fit polygon is the Minkowski sum of the obstacle with the reflected part:

$$
\mathrm{NFP}(\text{part}, \text{obstacle}) \;=\; \text{obstacle} \,\oplus\, (-\text{part}),
$$

the inner-fit polygon is the Minkowski erosion of the container by the part:

$$
\mathrm{IFP}(\text{part}, \text{container}) \;=\; \text{container} \,\ominus\, \text{part},
$$

and the feasible placement region for a part on the flat chart sheet is the
inner-fit region minus the union of no-fit regions against every placed part and
every sheet hole:

$$
\mathrm{feasible}(\text{part}) \;=\;
\mathrm{IFP}(\text{part}, \text{sheet})
\;\setminus\; \bigcup_{j} \mathrm{NFP}(\text{part}, \text{placed}_j)
\;\setminus\; \bigcup_{l} \mathrm{NFP}(\text{part}, \text{sheetHole}_l) .
$$

The placement is the bottom-left vertex of the feasible region; every accepted move
is re-checked by a compound boolean-area-plus-penetration-depth gate, so the layout
is 0-overlap by construction (`ContactNfpHoleNester.cs:27-72`). For the surface use
case this matters directly: a chart's inner naked edges become **sheet holes**, and
parts route around them rather than being placed on top of a chart gap. The previous
V506 path did not do hole-aware nesting on the chart.

The surface components feed the engine through a single shared bridge,
`SurfaceHoleNestBridge` (`SurfaceHoleNestBridge.cs:21`), so the curve-to-loop logic
lives in exactly one place. The bridge does uniform-by-length sampling for smooth
curves (curvature-adaptive sampling makes degenerate tiny edges that slow the
Minkowski NFPs), measures the sampling **proxy deviation** so the engine spacing can
be inflated to keep full-resolution output non-overlapping, enforces CCW orientation
(the Core nester expects CCW loops), and rejects any curve that is not
WorldXY-parallel (a tilted curve would nest a foreshortened projection).

### Distortion-aware spacing

Because the chart is conformal (area-varying), parts that just clear in the flat
chart can butt closer on the 3D surface where the local scale is larger. **Pack On
Surface** compensates by inflating the user clearance by the chart's max edge
stretch (`PackOnSurfaceComponent.cs:400`):

$$
\text{spacing}_{\text{adj}} \;=\; \text{spacing}_{\text{user}} \cdot \max\!\big(1,\, \sigma_{\max}\big),
$$

then adds the proxy-deviation term before handing the engine its spacing:

$$
\text{spacing}_{\text{engine}} \;=\; \text{spacing}_{\text{adj}} + 2\,\delta_{\text{part}} + \delta_{\text{sheet}},
$$

where $\delta_{\text{part}}$ and $\delta_{\text{sheet}}$ are the worst sampled
deviations of the part and sheet proxies (`PackOnSurfaceComponent.cs:424`). The part
term enters with factor 2 (part-pair clearance needs both parts' deviation); the
sheet term enters once. **Pack Surfaces** uses the same proxy-deviation compensation
without the stretch inflation (`PackSurfacesComponent.cs:539`), because it emits a
rigid placement frame per part and reports the surface gap directly (below).

### Fabrication outputs

**Pack Surfaces** is the multi-chart, fabrication-grade component. It packs across
**one or more** charts with greedy overflow (chart 0 fills first, unplaced parts
carry to chart 1), and per placed part it emits a rigid placement frame computed by
sampling the surface at the part centroid and at small $x$ and $y$ offsets to build
local tangent axes (`ComputePlacementFrame`, `PackSurfacesComponent.cs:613`). The
frame yields three fabrication transforms:

- **Transforms 3D (T3):** from the packed 2D position to the 3D placement frame.
- **Full Transform (FT):** composed `T3 \cdot packTx`, mapping the original flat
  part straight to its surface position in one step (`PackSurfacesComponent.cs:425`).
- **Max Deviation:** the largest gap between the flat part and the curved surface,
  measured at the four bounding-box corners against the placement plane
  (`PackSurfacesComponent.cs:655`). Small means nearly flat; large means the piece
  needs shimming or sub-tiling.

The rigid-frame path is the honest fabrication answer: a flat-cut stone part cannot
deform, so the FT transform places it rigidly and Max Deviation quantifies the
residual gap, rather than pretending the part bends to the surface.

### Self-trigger async

Both components run the solver and the 3D mapping on a background `Task` so the
canvas never freezes; the previous result stays visible while a new layout computes,
progress text ticks live, and the finished result pops in via `ScheduleSolution`. A
`_selfTrigger` flag distinguishes the component's own scheduled re-solves (emit only)
from real GH input changes (rebuild and restart), and a sampling-free bounding-box
input hash guards against redundant recomputes (`PackSurfacesComponent.cs:541`,
`PackOnSurfaceComponent.cs:426`). The pattern follows the repo async-source rule and
the AsyncScanComponent gate: if `Run` flips false inside the +10 ms schedule window,
the component clears rather than repainting a stale layout
(`PackSurfacesComponent.cs:235`). Several legacy V506-only inputs (Sort Mode, Corner
Mode, Seed, Max Candidates) are kept in their original slots for wiring compatibility
but are now **inert** under the deterministic engine and documented as ignored.

**Originality (the engine).** `ContactNfpHoleNester` is **clean-room** from
published NFP and BLF mathematics, with a measured **evolved-fork** increment
(contact-adaptive rotations and part-in-part-hole inner-fit nesting) over plain
bottom-left-fill.

- Evidence: `[Algorithm("Exact No-Fit-Polygon Bottom-Left-Fill with
  part-in-part-hole nesting", "Burke ... 2006 ...", Doi="10.1287/opre.1060.0293")]`
  and `[Algorithm("No-fit-polygon / inner-fit-polygon via Minkowski sum",
  "Bennell & Oliveira 2009 ...", Doi="10.1057/jors.2008.169")]` at
  `HoleNestComponent.cs:25-31`; the Clipper2 back end
  (`HoleNestComponent.cs:32`, BSL-1.0, no copyleft); the contact-rotation increment
  `[Algorithm("Contact-adaptive rotations ... Frahan ContactNfpHoleNester
  evolution study, outputs/2026-06-12/hole_packer_evolution", Note="Frahan-original
  ...")]` at `HoleNestComponent.cs:34`. The engine and the contact/IFP delta are
  covered in their own 2D-packing chapter; here it is a consumed dependency.

**Originality (the two surface components).** Both are
**facade-over-primitives**: they compose the BFF chart, the
`SurfaceHoleNestBridge` curve-to-loop, the `ContactNfpHoleNester` engine, and the
`BarycentricMapper2DTo3D` lift behind one canvas box. They add no new algorithm; the
distortion-aware spacing and the rigid placement frame are derivable plumbing over
those primitives.

- Evidence: `PackOnSurfaceComponent.cs:43` (`GH_Component` composing
  `ContactNfpHoleNester.PackSheets` + `BarycentricMapper2DTo3D`),
  `PackSurfacesComponent.cs:56`; the swap commit `05c7e82`.

---

## 7.8 Status & what's left

The subsystem is **live-validated** in example 13 (segmentation plus surface-conformal
Trencadis, captured and pre-baked). The engine swap onto HoleNest is committed
(`05c7e82`) and the static BFF exe is committed (`d1b5c5b`). The open items, ordered
by severity, are:

1. **Floater 2003 citation is wrong (medium).** `PackOnSurfaceComponent.cs:41`
   credits "Floater 2003 mean value coordinates", but the code is plain triangle
   barycentric interpolation; no MVC code exists. Soften to related-work or replace
   with a classical barycentric citation. The math that ships is correct.

2. **Single global chart scale mis-sizes parts (medium).** A conformal map has a
   spatially varying area scale; one isotropic $s$ is the perimeter-weighted average
   and is wrong wherever the local conformal factor departs from it. The fix is
   per-face or per-cone-patch scale plus adaptive re-cut driven by the flatness
   metric (`ChartScaleComputer.cs:58`).

3. **Inverse map is $O(P \cdot F)$ (medium).** The linear triangle scan caps at the
   author-stated ~2000 faces (`BarycentricMapper2DTo3D.cs:107`); an RTree over flat
   face bounding boxes turns it into $O(P \log F)$ and removes the ceiling.

4. **Foldover blind spot (medium).** Edge-stretch uses scalar lengths and cannot see
   a BFF triangle flip (`ChartDistortionAnalyzer.cs:98`); add a per-face signed-area
   sign test.

5. **Tolerance and units unresolved (medium).** At least four absolute epsilons
   ($10^{-6}$ containment, $10^{-8}$ edge skip, $10^{-12}$ denominator and scale
   guards) plus a default sampling tolerance of $0.01$, none scaled to chart size,
   and no model unit recorded in `FrahanSurfaceChart` (flags T1, T2, T4, T5, T7 in
   the SLM card). This violates the standing mm-versus-m scale-invariance constraint:
   the same chart silently means a different physical size in a mm versus an m
   document.

6. **Far-from-origin precision (low).** The OBJ is written with raw world coords at
   G10; a UTM-scale chart loses mantissa bits on disk and in the Gram products. A
   recenter-before-write and undo-after-map is the fix (flag T1).

None of these are fatal: the pipeline produces correct, pre-baked output today. They
are the bounded edits between "works on the example" and "fabrication-grade at
arbitrary scale and units".

---

## References

- Sawhney, R. and Crane, K. (2017). Boundary First Flattening. ACM Transactions on
  Graphics 36(4):109. DOI 10.1145/3072959.3056432.
- Floater, M.S. (2003). Mean value coordinates. Computer Aided Geometric Design
  20(1):19-27. DOI 10.1016/S0167-8396(03)00002-5. *(Related background for the
  mean-value-coordinate family; the shipped surface lift is plain triangle
  barycentric interpolation, not Floater's polygon MVC. See Section 7.6.)*
- Burke, E.K., Hellier, R., Kendall, G. and Whitwell, G. (2006). A New Bottom-Left-Fill
  Heuristic Algorithm for the Two-Dimensional Irregular Packing Problem. Operations
  Research 54(3):587-601. DOI 10.1287/opre.1060.0293.
- Bennell, J.A. and Oliveira, J.F. (2009). A tutorial in irregular shape packing
  problems. Journal of the Operational Research Society 60(S1):S93-S105.
  DOI 10.1057/jors.2008.169.
- Johnson, A. Clipper2. Boost Software License 1.0. (Minkowski sum + NonZero Boolean
  back end for the NFP/IFP construction.)
