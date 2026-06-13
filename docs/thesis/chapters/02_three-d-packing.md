# 02. Three-Dimensional Packing & Settling

This chapter covers the repository's volumetric packing subsystem: the
`3D Packing` ribbon tab (9 components) and the `Slab` ribbon tab (4
components). Three distinct engine families live here. The first is a family of
**heightmap** packers (a flat-bed greedy box packer, a mesh-derived top/bottom
pile packer, and an irregular mesh-container variant) that place geometry by a
deepest-bottom-left rule on a discrete grid. The second is the **guillotine /
DLBF** layer: the `Block Pack (Tree)` forest packer and the `HeteroExt`
mixed-size extraction facade that share a single deepest-left-bottom-fill (DLBF)
engine across timber, ceramic, and concrete catalogues. The third is the
**physics settle** layer: a Bullet rigid-body service that turns a proxy
placement into a settled pile of real geometry. The `Slab` tab adds convex
half-space slab cutting by oriented fracture planes.

The repository's own research arc runs straight through this chapter. The
shipped baseline was a heightmap proxy packer; the evolution study replaced it
with a mesh-accurate collision plus drop-settle pipeline that roughly doubles
packing compactness on the ETH1100 dry-stone benchmark while producing a stable,
non-interpenetrating pile (section 2.4). Where the repository evolved the
published math, the derivation is shown. Every originality claim is anchored to
a `file:line`, an `[Algorithm]` attribute, or a measured benchmark.

The packing-quality metrics used throughout are the **fill ratio**

$$
\mathrm{fill}=\frac{\sum_k \mathrm{vol}(P_k)}{\mathrm{vol}(\text{container})}
$$

(placed item volume over container volume; `PackingMetrics.cs:43-45`) and the
evolution-study **compactness**

$$
\mathrm{compactness}=\frac{\sum_k \mathrm{vol}_{\text{mesh}}(P_k)}{A_{\text{floor}}\cdot h_{\text{used}}},
$$

packed true mesh volume over the swept footprint column (floor area times used
height), which rewards interlocking rather than bounding-box stacking
(`outputs/2026-06-03/pack3d_evolution/PLAN.md`, section 5).

---

## 2.1 The heightmap deepest-bottom-left core

### 2.1.1 The flat-bed box heightmap

The simplest packer fills an axis-aligned box container with axis-aligned item
boxes. A `Heightmap` stores one scalar per grid cell: the current top surface
$T(x,y)$, the maximum $z$ already occupied at that cell (`Heightmap.cs:8`,
`:28`). To place an item of footprint $w\times d$ at grid cell $(i,j)$, the
landing height is the highest top under the item's footprint,

$$
z(i,j)=\max_{\substack{i\le x<i+s_w \\ j\le y<j+s_d}} T(x,y),\qquad s_w=\lceil w/c\rceil,\ s_d=\lceil d/c\rceil,
$$

with $c$ the cell size, and the placement is legal only if it stays under the
container ceiling, $z+h\le H+\varepsilon$ (`Heightmap.cs:30-50`). Placement is a
greedy argmin over a score that trades vertical growth against a
back-left compactness bias:

$$
\mathrm{score}(i,j)=w_h\!\!\sum_{\substack{i\le x<i+s_w \\ j\le y<j+s_d}}\!\!\max\bigl(0,\ (z+h)-T(x,y)\bigr)\ +\ w_c\,(i+j),
$$

the first term being the new material lifted above the existing surface (low is
flush), the second a lexicographic-style pull toward the origin corner
(`Heightmap.cs:52-69`). The `GreedyHeightmapPacker` sorts items by volume
descending, evaluates each over $\{0^\circ,90^\circ\}$ yaw, and commits the
lowest-score candidate, bounded by a `MaxCandidatesPerItem` budget
(`GreedyHeightmapPacker.cs:17-19`, `:44-82`). The argmin is exact over the
evaluated candidate set; there is no overlap by construction because the
heightmap is monotone.

This is a textbook deepest-bottom-left / skyline heuristic. The relevant
published anchor is the deepest-left-bottom-fill (DLBF) algorithm of Chehrazad,
Roose and Wauters (2025), which the components cite as the substrate
(`Pack3DIrregularComponent.cs:20-23`).

### 2.1.2 The mesh-pile heightmap (top and bottom surfaces)

A box top-surface heightmap cannot nest a concave stone into the dimple of the
stone below it: it only knows the highest point of each column, so it stacks
flat tops on flat tops. The `OrientedMeshHeightmap` fixes this by rasterising
**two** surfaces per cell from the item's triangle mesh: a bottom envelope
$b(x,y)$ and a top envelope $t(x,y)$ (`OrientedMeshHeightmap.cs:8-10`,
`:276-292`). Each triangle is scan-converted by barycentric interpolation over
its footprint cells, and vertices, edge samples and the centroid are added so
thin or degenerate triangles still deposit samples (`:195-244`). The cell stores
the per-column min as the bottom and the per-column max as the top.

The `MeshPileHeightmap` then seats one mesh into another. For a candidate at
grid offset $(i,j)$, the landing height is chosen so the item's bottom envelope
rests on the pile's current top surface at every occupied column:

$$
z(i,j)=\max_{(m_x,m_y)\in \mathrm{occ}}\Bigl(\ T_{\text{pile}}(i+m_x,\ j+m_y)\ -\ b_{\text{item}}(m_x,m_y)\ \Bigr),
$$

so the deepest finger of the item drops exactly onto the highest point beneath
it (`MeshPileHeightmap.cs:79-106`). Unlike the box heightmap, the pile keeps a
**list of occupied vertical intervals** per cell, not just a top scalar, so a
later piece can slide under an overhang. A candidate is rejected if its
$[z+b,\,z+t]$ interval at any column overlaps an existing interval there:

$$
\mathrm{WouldCollide}(i,j,z)\iff \exists\,(m_x,m_y),\ \exists\,[\,\beta,\tau\,]\in I(i+m_x,j+m_y):\ (z+b)<\tau-\varepsilon\ \wedge\ \beta<(z+t)-\varepsilon,
$$

a conservative vertical-column non-penetration test on the true mesh envelopes
(`MeshPileHeightmap.cs:248-271`). This is the "conservative vertical-column
collision check" the component name advertises.

### 2.1.3 Orientation and the down-axis rotation

The `GreedyMeshHeightmapPacker` searches up to six discrete orientations per
item: each of the three local axes tilted down to $-Z$ (None / X-down / Y-down)
crossed with $\{0^\circ,90^\circ\}$ yaw, with the $90^\circ$ skipped when the
rotated footprint is square (`GreedyMeshHeightmapPacker.cs:142-169`). The down
rotation is applied to the vertices **before** the yaw, matching the world
transform order $R_{\text{yaw}}R_{\text{down}}$. For the None case the down step
is a literal identity, so the multi-orientation engine is byte-identical to the
legacy yaw-only code on that path (`OrientedMeshHeightmap.cs:115-152`); the
Grasshopper transform reproduces the same order, $T_{\text{yaw}}\cdot
T_{\text{down}}$, so the placed mesh matches the heightmap that scored it
(`Pack3DMeshHeightmapComponent.cs:269-301`).

### 2.1.4 The irregular mesh container

A real container is not a box. `IrregularMeshContainer.FromMesh` ray-casts a
vertical line through the cell centre of every grid cell, collects the sorted
$z$ values where the line crosses a container triangle, and pairs consecutive
crossings into solid intervals (`IrregularMeshContainer.cs:52-97`,
`:133-176`). The crossing-parity pairing $\{z_0,z_1\},\{z_2,z_3\},\dots$ is the
standard ray / closed-surface inside test: an even number of crossings bounds
the interior segments. Cells with fewer than two crossings are outside the
container and marked not-allowed. The pile packer then honours those per-cell
allowed intervals, raising a candidate's $z$ until the item fits inside a
container interval or rejecting it (`MeshPileHeightmap.cs:160-246`). A
surface-sample fallback handles meshes that produce no closed columns
(`IrregularMeshContainer.cs:99-131`).

> **Originality.** The heightmap packers are **clean-room**. The deepest-
> bottom-left placement rule and the DLBF substrate are cited to Chehrazad,
> Roose and Wauters (2025) (`Pack3DIrregularComponent.cs:20-23`; GUID
> `E36C3F7D-7E2C-495E-9E2A-59312C5CF990`). The two-surface mesh-pile proxy, the
> per-cell vertical-interval collision test, the six-orientation down-axis
> search and the ray-cast irregular container are Frahan additions over a box
> skyline; the mesh-heightmap component labels its method "Frahan-original
> mesh-pile heightmap" (`Pack3DMeshHeightmapComponent.cs:20-21`; GUID
> `A16D6426-38A8-44B1-AB6A-4BA80EB39730`). No upstream packing source sits in
> the tree. *Citation flag:* the sibling container component attributes the same
> heightmap method to "Park and Han 2024 tree-packing"
> (`Pack3DIrregularContainerComponent.cs:18`, `:22`); that work
> (references `[R8]`) carries no DOI and is a placeholder-grade citation, while
> the mesh-pile component calls the identical method Frahan-original. The two
> attributions should be reconciled before external review (AGENTS.md §9).

---

## 2.2 The guillotine forest packer: Block Pack (Tree)

`BlockPackTreeComponent` (GUID `C2D3E4F5-3001-4F5E-A6B7-C8D9E0F12345`) packs a
set of element cuboids into a set of stone-block containers using only
axis-aligned, saw-cuttable **guillotine** cuts (`BlockPackTreeComponent.cs:37`).
It is the digital-fabrication entrypoint: every placement it returns is
reachable by straight saw passes, so the plan runs directly on a wire or bridge
saw (`examples/11_pack3d/README.md`).

A guillotine partition of a cuboid container is a binary tree: each internal
node is one axis-aligned cut that splits a free region into two children; each
leaf is either a placed element or free residue. The forest packer grows many
such trees under independently seeded randomness and keeps the best by score.
The Kim (2025) score sums packed element value with an all-fit container-price
bonus,

$$
S=\sum_{e\in \text{packed}} v_e\ +\ \mathbf{1}[\text{all packed}]\cdot \frac{1}{1+p_{\text{container}}}\ -\ w_{\text{cut}}\sum_{\text{cuts}} A_{\text{internal}},
$$

where the final term is the Frahan / Jalalian I11 (BCSdbBV) extension that
charges for internal cutting-surface area $A_{\text{internal}}$, so the packer
can be biased toward fewer, cheaper cuts (`BlockPackTreeComponent.cs:30-31`,
`:154-158`, `:102-106`). The component adds three documented capabilities
beyond the paper: a deterministic master seed (forest $k$ uses $\text{seed}+k$,
so parallel forests are bitwise identical to serial), a saw **kerf width** that
each cut consumes along its direction, and per-container **forbidden boxes** so
fracture-intersected cells can be passed in as keep-out regions
(`BlockPackTreeComponent.cs:22-28`). The measured live result on example 11 is a
full pack, 12/12 elements into one container, score 65.11, deterministic at
seed 0 (`examples/11_pack3d/README.md`).

![3D guillotine block packing: 12 element cuboids saw-cut into one container](../../../examples/11_pack3d/11_pack3d_result.png)

> **Originality.** **evolved-fork** of Kim (2025) (Computation 13:211, CC BY
> 4.0). The tree-forest growth and score are the paper's; the deterministic
> seed, kerf, forbidden-box, parallel-forest and memory-budget inputs are the
> stated deltas, and the cut-surface-area term cites Jalalian et al. (2023)
> (`[Algorithm]` `BlockPackTreeComponent.cs:30-31`; synthesis in
> `wiki/papers/kim2025_tree_packing.md`). CC BY 4.0 is permissive with
> attribution, no copyleft. It sits on the Masonry ribbon by design to keep the
> recommended 11-panel layout intact (`:13-21`).

---

## 2.3 The DLBF mixed packer and the HeteroExt facade

### 2.3.1 The 3D deepest-left-bottom-fill engine

`Dlbf3dMixedSizePacker` is the 3D generalisation of the planar DLBF
(`Dlbf3dMixedSizePacker.cs:8-37`). It packs a multi-size catalogue, each entry
carrying its own per-piece height and per-piece revenue, into a tested AABB on a
discrete cubic grid. Pieces are processed in **revenue-per-volume descending**
order,

$$
\text{priority}(p)=\frac{\mathrm{revenue}(p)}{\mathrm{vol}(p)},
$$

and for each piece the chosen cell is the deepest-left-bottom free cell in the
strict order lowest $z$, then lowest $y$, then lowest $x$:

$$
(i^\star,j^\star,k^\star)=\operatorname*{arg\,min}_{(i,j,k)\,:\,\mathrm{RegionFree}}\ (k,\ j,\ i)\quad\text{(lexicographic)},
$$

scanning $k$ as the outermost loop so the bench floor fills first
(`Dlbf3dMixedSizePacker.cs:186-220`). A region is free only if every cubic cell
it spans is unblocked, where blocked cells come from forbidden boxes (e.g.
fracture-intersected regions) rasterised into the grid (`:166-184`, `:268-278`).
Two operating modes split the use cases: `FloorOnly = true` clamps every piece
to $z=z_{\min}$ for monoliths cut out of solid rock (no stacking), while
`FloorOnly = false` allows full 3D stacking for racking and container loading
(`:24-29`, `kStop` at `:215`). A 2026-06-06 evolution adds optional
best-of-orientation: each piece is tried in its up-to-six distinct
axis-permutations and placed in the orientation whose best free cell is lowest;
volume and revenue are permutation-invariant, and the default overload keeps
`tryOrientations = false` so the six existing tests stay byte-identical
(`:127-143`, `:201-231`, `:250-266`).

The outer `while (anyPlaced)` loop re-passes the catalogue until no further
piece fits, so a multi-instance catalogue packs greedily to exhaustion
(`:194-245`).

### 2.3.2 The HeteroExt facade: one engine, three materials

`FrahanHeterogeneousExtractionComponent` (HeteroExt, GUID
`F2D0BC19-1234-4F2D-A0B0-7E60CADA15B9`) is a four-stage extraction pipeline:
BlockCutOpt locates the fracture-clean regions of a bench, the 3D DLBF packer
fills those regions with a mixed catalogue of monuments, dimension stones and
slabs while avoiding the fractured cells, and an optional monument-inventory
stage places found stones on a fracture-derived block graph
(`BlockCutOptHeterogeneousComponents.cs:169`). The same `Dlbf3dMixedSizePacker`
that HeteroExt composes is also exposed standalone as `Frahan Mixed-Size Block
Pack 3D` (GUID `F2D0BC18`), satisfying the monster-vs-primitive rule that a
monolith must be a facade over a published, standalone primitive, never a black
box (`:170-176`, `[RelatedComponent]` cross-links). Because the engine packs a
revenue-weighted multi-size catalogue, it is the same mixed packer for timber,
ceramic and concrete instances; only the catalogue changes.

> **Originality.** The DLBF 3D engine is **clean-room** from Chehrazad, Roose
> and Wauters (2025), following the paper's Section 5 generalisation; the
> standalone exposure carries the citation with DOI
> (`[Algorithm]` `BlockCutOptHeterogeneousComponents.cs:42`, DOI
> 10.1080/00207543.2025.2478434). The HeteroExt monolith is
> **facade-over-primitives**: its `[Algorithm]` is "Frahan-original" with the
> explicit note that it composes Elkarmoty (2020) and Chehrazad (2025), both
> reimplemented in managed code, and that "the composition and the heterogeneity
> model are the contribution" (`:169`). No new algorithm; the integration and
> the seam-exposed standalone primitives are the work.

---

## 2.4 The physics settle: Bullet rigid-body

### 2.4.1 From proxy placement to settled geometry

A heightmap or DLBF placement is a proxy: it positions bounding-box or
height-column approximations, not the real stone in real contact.
`Settle 3D (Physics)` (`PackSettle3DComponent`, GUID
`134785ac-19cb-4f14-85f8-e2f666bd14f6`) is the finishing pass that composes
after any 3D packer and settles the meshes into a stable,
non-interpenetrating pile under gravity and friction
(`PackSettle3DComponent.cs:16-28`). Each stone is convex-decomposed (CoACD, with
a convex-hull fallback) into pieces wrapped as `ConvexHullShape` children of a
per-stone `CompoundShape`; bodies are seeded at their already-placed positions,
lifted slightly to clear convex-proxy overlap, then dropped
(`BulletSettleService.cs:9-30`, `:111-133`).

The settle is governed by Newton-Euler rigid-body dynamics integrated by
Bullet's sequential-impulse solver. The contact model is Coulomb friction with
a default coefficient $\mu=0.85$ (`SettleOptions.Friction`,
`BulletSettleService.cs:52`). The load-bearing implementation detail is a
**gravity ramp**: seeded near-contacts are resolved softly by stepping
$g_z$ through $-0.5,-2.0,-5.0$ to full $-9.81$ before the main settle loop, so
the pile does not explode from convex-proxy interpenetration at $t=0$ (the
lesson recorded from the dev run; `:135-141`). Optional tamp rounds then apply
strong-gravity bursts to densify (`:142-148`). The component is heavy and
single-threaded, so it runs on a Run-gated background `Task`
(`GH_TaskCapableComponent`), never on the Grasshopper UI thread
(`PackSettle3DComponent.cs:82-101`).

The returned per-stone rigid delta is decoded carefully: Bullet stores its basis
row-wise for row-vector math, so the column-vector rotation applied to the mesh
is its transpose, and the world update is $v'=R\,(v-\mathbf{c})+\mathbf{t}$ about
the piece centroid $\mathbf{c}$ (`BulletSettleService.cs:62-70`, `:152-164`).
Example 18 settles 12 ETH1100 dry-stone scans into a roughly 1.95 m cluster
(`examples/18_pack_settle_bullet/README.md`).

![Bullet rigid-body settle of 12 ETH1100 dry-stone scans into a stable pile](../../../examples/18_pack_settle_bullet/18_settle_bullet.png)

### 2.4.2 COM-over-support stability (Heyman limit state)

Stability is the Heyman (1966) limit-state criterion shared with the masonry
chapter: a settled stone is stable when its centre of mass, projected down to
the bed, lies inside the convex hull of its contact footprint. Writing the
support polygon $\mathcal{S}=\mathrm{hull}\{\mathbf{p}_k\}$ over the contact
points $\mathbf{p}_k$, the gate is

$$
\Pi_{xy}(\mathbf{c}_{\text{COM}})\in \mathcal{S},
$$

so the resultant of self-weight passes through the support and no overturning
moment is unbalanced (`[Algorithm]` `PackSettle3DComponent.cs:34-35`). The
physics solver enforces this dynamically: a stone whose COM falls outside its
support topples and re-settles until it rests, which is exactly the equilibrium
the limit-state test certifies.

### 2.4.3 The heightmap-to-settle evolution (measured 2x)

The evolution study set the baseline as the shipping heightmap packer (GUID
`B3E8A42F`) and targeted a 2x improvement in wasted true-volume fraction at
zero interpenetration and full COM-over-support stability
(`outputs/2026-06-03/pack3d_evolution/PLAN.md`, sections 1, 3, 5). The measured
arc on the metric of section 2 is: v1 AABB greedy reached 17.0% compactness and
placed 19 of 30 stones; the mesh-accurate plus drop-settle v2 reached 33.2%
compactness, placed 30 of 30, and held 80% of stones COM-stable, rising to 100%
COM-stable with the stability-seat pass (`PLAN.md`, section 5). The
near-doubling of compactness from 17.0% to 33.2%, on real geometry and at full
placement, is the recorded heightmap-to-mesh-accurate-plus-settle 2x. The
heightmap path is retained as the validated baseline (the components route users
to Settle 3D and Block Pack (Tree) as the evolved paths;
`Pack3DMeshHeightmapComponent.cs:12-15`).

> **Originality.** The settle service is **clean-room** physics integration
> over a **vendored** engine. The rigid-body dynamics packing framing cites
> Zhuang et al. (2024) (DOI 10.1016/j.cag.2024.103996); the engine is Bullet
> (Coumans et al.) via BulletSharp under zlib, and the convex pieces come from
> CoACD (Wei et al. 2022) (`[Algorithm]` `PackSettle3DComponent.cs:29-35`).
> Bullet and BulletSharp are vendored unmodified (zlib, permissive); only the
> marshalling, the gravity-ramp seeding schedule and the centroid-relative
> transform decode are ours. The COM-over-support gate is Heyman (1966). CoACD
> is MIT but transitively vendors CGAL (GPL); per the licensing register it is
> reached only through the optional out-of-process shim with a convex-hull
> fallback, so the default install links no copyleft
> (`docs/thesis/90_originality.md`, register row 5).

---

## 2.5 The Slab tab: convex fracture cutting

The `Slab` tab turns a quarry block into fabricable slabs by cutting it along
oriented fracture planes. `SlabFromMesh` (GUID
`B1A2C3D4-5E6F-4789-9ABC-1D2E3F4A5B6C`) wraps a Rhino mesh into the internal
`Slab` DTO. `SlabCutByFractures` (GUID
`C2B3D4E5-6F7A-489B-AC1D-2E3F4A5B6C7D`) splits a list of slabs by a list of
planes (`SlabCutByFracturesComponent.cs:44-60`). Each plane, given as origin and
normal, classifies every slab vertex by signed distance $d=\mathbf{n}\cdot
(\mathbf{v}-\mathbf{o})$ into above / on / below up to a tolerance $\varepsilon$;
each face is walked once, and every directed edge that strictly straddles
contributes a linearly interpolated intersection point to both half-faces. The
cut cap is reconstructed by sorting the unique on-plane and intersection
vertices CCW about $+\mathbf{n}$ in an in-plane 2D frame
(`SlabCutter.cs:7-34`). Cutting a slab by $m$ ordered planes grows the piece
list by up to $2\times$ per plane, since each plane splits every existing piece
(`:55-70`).

The managed path is exact for **convex** input only; non-convex slabs can
produce non-simple caps, so the component exposes an opt-in `Use CGAL` backend
that routes the cut through the corefinement boolean kernel for robust
non-convex or large-slab cutting, with a managed fallback if the CGAL shim is
absent (`SlabCutByFracturesComponent.cs:84-92`). The two further Slab-tab
members are `Slab Cut By Tool Mesh (CGAL)` and `Vertical Fracture Planes From
Curves` (GUID `F2D05A09-1A2B-4C3D-9E4F-5A6B7C8D9E09`), which lifts 2D fracture
traces into vertical cutting planes.

Example 23 runs the full quarry-to-slab chain on a fracture-prone 3.0 x 1.5 x
1.5 m block (6.75 m3): fractures with a 40 mm keep-out leave 6.05 m3 of intact
rock, fracture-aware block packing recovers 2.98 m3 (60 blocks, 49.3% of intact,
100% guillotine-separable), and gangsaw slabbing yields 2.52 m3 of finished 20
mm slab, a 37.3% overall volume yield and 126 m2 of slab face
(`examples/23_quarry_to_slab/README.md`).

![Quarry-to-slab: fracture-prone block split into slabs](../../../examples/23_quarry_to_slab/23c_slabs.png)

> **Originality.** The slab cutter is **clean-room** convex-polyhedron
> half-space clipping (Sutherland-Hodgman family), labelled "Frahan-original"
> with the optional CGAL boolean backend named separately
> (`[Algorithm]` `SlabCutByFracturesComponent.cs:33-39`). The successive-plane
> block-decomposition framing cites Goodman and Shi (1985) block theory
> (`SlabCutter.cs:29-33`). The CGAL Polygon Mesh Processing path is GPL and is
> reached only through the optional shim with a managed fallback (licensing
> register row 4).

---

## 2.6 Example: statue to fabricable blocks

Example 15 is the volumetric counterpart that feeds the packers: it decomposes a
sculpture into roughly 0.5 m brick blocks whose boundary blocks carry the
**real** statue surface, not bounding boxes. A 0.5 m grid covers the bounding
box; each cell box is intersected (CGAL boolean) with the closed statue solid,
so interior cells return a full cube and boundary cells keep the real mesh face
on the outside and clean planar cut faces on the grid sides. On the Stanford
bunny scaled to 3.0 m, the run produced 7 interior cubes, 106 real-face boundary
blocks, 67 empty cells dropped, 113 closed blocks total, with a recovered volume
ratio of exactly 1.0000 (5.4009 m3 statue = 5.4009 m3 of blocks, measured by
`VolumeMassProperties`), in 3.6 s over 173 CGAL booleans with zero failures
(`examples/15_statue_to_blocks/README.md`). The enabling fix is a Geogram
`FillHoles -> RemeshUniform -> FillHoles` pass that rebuilds the raw scan into a
clean 2-manifold so both the interior point test and the CGAL corefinement work.
These closed blocks are exactly the input the Block Pack (Tree) and DLBF packers
of this chapter consume.

![Exploded real-face block decomposition of a scanned form](../../../examples/15_statue_to_blocks/15_step2_blocks_exploded.png)

---

## 2.7 Status & what's left

- **Heightmap citation inconsistency.** `Pack3DMeshHeightmapComponent` labels
  the mesh-pile method "Frahan-original" while
  `Pack3DIrregularContainerComponent` attributes the identical heightmap method
  to "Park and Han 2024 tree-packing" (`[R8]`, no DOI, placeholder-grade). The
  two should be reconciled before external review
  (`Pack3DIrregularContainerComponent.cs:18`,
  `Pack3DMeshHeightmapComponent.cs:20-21`). Severity: medium (provenance).
- **Bullet native dependency.** `Settle 3D` needs `libbulletc.dll` beside the
  `.gha`; without it the component warns and does nothing
  (`BulletSettleService.cs:27`, `:80-87`). It ships in `install/plugin/` but is
  absent from a source-only build. Severity: medium (deployment).
- **Settle is non-deterministic.** The Bullet solve is a physics simulation, not
  a deterministic search; example 18 notes one stone hung up mid-drop as a
  normal settle artifact (11 of 12 clustered). Re-runs can differ. Severity: low
  (documented behaviour).
- **Convex-only managed slab cutter.** The default `SlabCutByFractures` path is
  exact for convex slabs only and "explodes combinatorially on large slabs with
  many planes"; non-convex or large work needs the opt-in CGAL backend
  (`SlabCutByFracturesComponent.cs:84-92`, `SlabCutter.cs:23-27`). Severity:
  medium.
- **Heightmap proxy is a baseline, not the recommended path.** The mesh-pile
  collision is a conservative vertical-column test on envelopes, not a true 3D
  contact solve; the components themselves route users to Settle 3D and Block
  Pack (Tree) as the evolved paths
  (`Pack3DMeshHeightmapComponent.cs:12-15`). Severity: low (by design).
- **CoACD transitive copyleft.** Convex decomposition pulls CoACD, which
  vendors CGAL (GPL); the default install must reach it only through the
  optional out-of-process shim with the convex-hull fallback
  (`docs/thesis/90_originality.md`, register row 5). Severity: medium
  (licensing, mitigated by quarantine).

---

## References (this chapter)

- Chehrazad, R., Roose, D., Wauters, T. (2025). A fast and scalable
  deepest-left-bottom-fill algorithm for the 3D bin packing problem.
  International Journal of Production Research 63:6606-6629. DOI
  10.1080/00207543.2025.2478434.
- Kim, S. (2025). Tree-forest guillotine packing. Computation 13(9):211. DOI
  10.3390/computation13090211. (CC BY 4.0.)
- Jalalian, A. et al. (2023). Block-cutting-surface-defined block value
  (BCSdbBV) cost objective. Scientific Reports. DOI 10.1038/s41598-023-49633-w.
- Park, J., Han, S. (2024). Tree-packing for irregular 3D containers
  (tree-search 3D-BPP / orthogonal-block packing). (Cited in source; no DOI,
  placeholder-grade attribution flagged in this chapter.)
- Zhuang, Q., Chen, Z., He, K., Cao, J., Wang, W. (2024). Dynamics
  simulation-based packing of irregular 3D objects. Computers and Graphics
  123:103996. DOI 10.1016/j.cag.2024.103996.
- Wei, J., Liu, M., Wang, J. et al. (2022). Approximate convex decomposition
  for 3D meshes with collision-aware concavity and tree search (CoACD). ACM
  Transactions on Graphics (SIGGRAPH 2022) 41(4):42. DOI
  10.1145/3528223.3530103.
- Coumans, E. et al. Bullet Physics SDK (rigid-body dynamics, sequential-impulse
  solver). zlib License. Via BulletSharp.
- Heyman, J. (1966). The stone skeleton. International Journal of Solids and
  Structures 2(2):249-279. DOI 10.1016/0020-7683(66)90018-7.
- Goodman, R.E., Shi, G. (1985). Block Theory and its Application to Rock
  Engineering. Prentice-Hall.
- Elkarmoty, M., Bondua, S., Bruno, R. (2020). A combinatorial optimization
  method for the block-cutting problem. Resources Policy 68:101761. DOI
  10.1016/j.resourpol.2020.101761. (Cited by HeteroExt stage 1.)
