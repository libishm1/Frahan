# 01. Two-Dimensional Nesting & Trencadís

This chapter covers the repository's two-dimensional geometry-packing
subsystem: the `2D Packing` ribbon tab (13 components) and the `Trencadis`
ribbon tab (5 components). The shared engine is exact No-Fit-Polygon
Bottom-Left-Fill on a Clipper2 integer-snapped back end. Three nesters are in
scope: the shipped `IrregularSheetFillV506` (FreeNest), its exact-NFP sibling
`IrregularSheetFillNfpBlf` (FreeNestX), and the hole-aware
`ContactNfpHoleNester` / CNH (HoleNest). The Trencadís family adds a
centroidal-Voronoi cell partition plus optimal one-to-one shard assignment.

The mathematics here is real plane geometry: Minkowski sums, the no-fit and
inner-fit polygons, and a distance-based penetration certificate. Where the
repository evolved the published math we show the derivation, not only the
final formula. Every originality claim is anchored to a `file:line`, an
`[Algorithm]` attribute, or a measured benchmark.

The packing-quality metric used throughout is **stock utilisation**

$$
\mathrm{util\_stock}=\frac{\sum_k \mathrm{area}(P_k)}{\mathrm{area}(S)-\sum_l \mathrm{area}(H_l)}
$$

placed part material area over net sheet area (sheet minus its holes). It is
computed by boolean area, not bounding boxes, and it is **reported, not
gated** in the components; the gate is the zero-overlap validity certificate
of section 1.6. (Definition: `examples/10_pack2d/README.md`.)

---

## 1. The No-Fit-Polygon nesting core

### 1.1 The no-fit polygon (NFP)

Let two polygons $A$ and $B$ be given with $A$ fixed and $B$ free to
translate by $t\in\mathbb{R}^2$. Write $B(t)=\{b+t : b\in B\}$. The no-fit
polygon is the set of translations for which the interiors of $A$ and $B$
overlap:

$$
\mathrm{NFP}(A,B)=\{\,t\in\mathbb{R}^2 : \operatorname{int}A\cap\operatorname{int}B(t)\neq\varnothing\,\}.
$$

The standard result (Bennell and Oliveira 2009) is that this set is exactly a
Minkowski sum of $A$ with the point-reflection of $B$:

$$
\mathrm{NFP}(A,B)=A\oplus(-B),\qquad A\oplus C=\{a+c : a\in A,\ c\in C\},\quad -B=\{-b:b\in B\}.
$$

**Derivation.** The interiors meet iff there exist $a\in A$, $b\in B$ with
$a=b+t$, i.e. $t=a-b=a+(-b)$. The set of all such $t$ is exactly
$\{a+(-b)\}=A\oplus(-B)$. Placing $B$ at any $t$ on the *boundary* of this set
makes $A$ and $B$ touch without interior overlap; any $t$ outside the set is
collision-free. This is the geometric primitive every component in this
chapter uses for collision. In code the reflection is `Reflect` and the sum is
`Clipper2Adapter.MinkowskiSum`
(`ContactNfpHoleNester.cs:1484`, `:962`; the engine contract is documented at
`ContactNfpHoleNester.cs:28-31`).

### 1.2 The inner-fit polygon (IFP)

The container constraint is the dual. The inner-fit polygon is the set of
translations that keep $B$ **inside** a container $C$:

$$
\mathrm{IFP}(B,C)=\{\,t : B(t)\subseteq C\,\}.
$$

For a convex container this is an erosion (Minkowski difference)
$C\ominus B$. The repository realises it as an exact intersection over the
vertices of the convex hull of $B$:

$$
\mathrm{IFP}(B,C)=\bigcap_{v\in\operatorname{hull}(B)}\bigl(C-v\bigr),\qquad C-v=\{c-v:c\in C\}.
$$

**Derivation.** $B(t)\subseteq C$ holds iff every vertex $b_i+t\in C$. Because
$C$ is intersected against the **convex hull** of $B$, containing the hull
vertices is sufficient to contain all of $B$ (a convex combination of points
in a convex set stays in the set). Each constraint $b_i+t\in C$ rearranges to
$t\in C-b_i$, and the feasible $t$ is the intersection over all hull vertices.
This is exact for a convex $C$ and conservative (never admits an
out-of-bounds placement) for a concave $C$ (`ContactNfpHoleNester.cs:1332-1352`;
hull by Andrew's monotone chain, `:1355-1368`). The hull pass is the
cost saver: it replaces a full-resolution erosion with at most
$|\operatorname{hull}(B)|$ Clipper intersections.

### 1.3 The feasible region and Bottom-Left-Fill

Combining the two, the set of legal placements of part $B$ at a fixed rotation,
given a sheet $S$ with holes $\{H_l\}$ and already-placed parts $\{A_k\}$, is

$$
\mathrm{feasible}(B)=\mathrm{IFP}(B,S)\ \setminus\ \Bigl(\bigcup_k \mathrm{NFP}(A_k,B)\Bigr)\ \setminus\ \Bigl(\bigcup_l \mathrm{NFP}(H_l,B)\Bigr).
$$

This is stated verbatim in the engine header
(`IrregularSheetFillNfpBlf.cs:18-27`) and in the CNH header
(`ContactNfpHoleNester.cs:30-31`). Non-overlap is therefore a **hard
constraint of construction**, not a post-hoc trim.

The Bottom-Left-Fill placement rule (Burke et al. 2006) selects, among all
feasible translations, the one that is lowest, then leftmost:

$$
t^\star=\operatorname*{arg\,min}_{t\in\mathrm{feasible}(B)}\ (t_y,\ t_x)\quad\text{(lexicographic)}.
$$

The optimum of a linear lexicographic objective over a polygonal region lies at
a **vertex** of that region, so the search reduces to scanning the
feasible-region vertices in $(y,x)$ order and taking the first that survives
verification (`ContactNfpHoleNester.cs:980-1002`,
`OrderedVertices` `:1625-1634`; `IrregularSheetFillNfpBlf.cs:314-315`).

> **Originality.** The NFP-BLF nesting family is **clean-room**. The published
> mathematics is cited in source by `[Algorithm]` attributes: Bottom-Left-Fill
> to Burke, Hellier, Kendall, Whitwell (2006), Operations Research
> 54(3):587-601, DOI `10.1287/opre.1060.0293`; NFP/IFP via Minkowski sum to
> Bennell and Oliveira (2009), JORS 60(S1):S93-S105, DOI
> `10.1057/jors.2008.169`; the boolean back end is the vendored Clipper2
> (Johnson, BSL-1.0) with **no copyleft** (evidence:
> `IrregularSheetFillNfpBlfComponent.cs:24-32`,
> `HoleNestComponent.cs:25-33`). No upstream nesting source sits in the tree.
> Tier B (faithful implementation of published work), with the V506 to
> NFP-BLF delta and the CNH increments below carrying the research weight.

![2D nest result: 22 parts nested around a sheet hole, zero overlap](../../../examples/10_pack2d/10_pack2d_result.png)

### 1.4 V506 to NFP-BLF: the measured delta

The originally shipped nester is `IrregularSheetFillV506` (FreeNest, GUID
`D5E7A2B1-8C34-4F1E-A096-3B7F5D2E8A4C`). It is an NFP-assisted bottom-left
placement that, by design, permits a bounded overlap controlled by a
**Trim Tolerance** and then boolean-trims the contact. With the default
Trim Tolerance of 0.1 it produces apparent overlaps; this is the documented
behaviour, not a fault (KB-6/KB-7; `examples/10_pack2d/README.md`).

`IrregularSheetFillNfpBlf` (FreeNestX, GUID
`2d351646-2cb0-402a-bbd8-3950b5bb1fbc`) re-derives the placement so that the
feasible region is the **complete** IFP-minus-NFP set, making zero overlap a
hard constraint. The measured improvement is recorded in source: a mean wasted
area cut of **53.9%** against V506 at zero overlap, validated against a Python
reference (`IrregularSheetFillNfpBlf.cs:21-22`). FreeNestX is the only
zero-overlap packer in the study to cross the 80% utilisation bar with holes:
82.0% oversub, 84.7% on the L-plus-hole fixture, 89.6% on a hard 3-hole
fixture (`examples/10_pack2d/README.md`). The four legacy V1/V2/V3/V506
wrappers are marked `[Obsolete]` and `Exposure=hidden` per the 2D-V-solver
phase-out (`Pack2DIrregularSheetV506Component.cs:56-57`).

> **Originality.** FreeNestX is an **evolved-fork** of V506: same NFP/BLF
> lineage, but a re-derived complete feasible region replaces the
> overlap-then-trim contract, with the 53.9% waste-cut measured delta
> (`IrregularSheetFillNfpBlf.cs:18-27`, commit lineage at
> `outputs/2026-06-03/pack2d_nfp_evolution`).

### 1.5 The Unified dispatcher

`IrregularSheetFillComponent` (FreeNestU, GUID
`AB12C00B-1A2B-4C3D-9E4F-5A6B7C8D9E0B`) is a strategy selector that dispatches
to the V1/V2/V3/V506 variants behind one canvas box. It adds no new algorithm;
every internal step resolves to an in-repo nesting variant.

> **Originality.** **facade-over-primitives** (variant dispatcher), with the
> `[Algorithm]` note explicitly calling it a "Frahan-original strategy
> selector" over Burke 2007 and Bennell and Oliveira 2008
> (`IrregularSheetFillComponent.cs:32-33`, `:42`).

---

## 1.6 The hole-aware nester (CNH / ContactNfpHoleNester)

The `ContactNfpHoleNester` (HoleNest component, GUID
`D5F10019-8A3C-4D17-B5E2-6C90F2A47D31`) evolves FreeNestX with two capabilities
it lacks, both validated in `outputs/2026-06-12/hole_packer_evolution`
(`ContactNfpHoleNester.cs:10-33`).

### 1.6.1 Contact-adaptive rotations

A fixed rotation list cannot seat a part flush against a wall, a neighbour, or
a hole edge. CNH augments the uniform base set $\{0,\tfrac{\pi}{2},\pi,\tfrac{3\pi}{2}\}$
with **edge-alignment** angles. For a host edge with direction angle
$\alpha_h$ and a part edge with direction angle $\alpha_p$, the rotation that
makes the part edge parallel to the host edge is

$$
\theta=\alpha_h-\alpha_p \pmod{2\pi},\qquad\text{and its flip } \theta+\pi,
$$

evaluated over the longest edges of the part against the longest edges of the
sheet and the most recently placed neighbour (`ContactNfpHoleNester.cs:1304-1330`,
`RotationSet` `:1281-1302`). The total angle budget is bounded by
$\max(4,\ \mathrm{base}+4+2\,\mathrm{contact})$, and symmetric rotations are
collapsed by a translation-invariant shape signature (`RotSignature`,
`:1917-1929`) so each distinct rotated shape evaluates its NFP once.

### 1.6.2 Part-in-part-hole nesting (holes-first)

A small part can be nested **inside the hole of a larger placed part**. The
hole acts as a container, so the legal translations of filler $B$ into host
hole $G$ are exactly the inner-fit region $\mathrm{IFP}(B,G)$ of section 1.2.
The schedule is holes-first: parts that have holes are placed as hosts (area
descending), the smallest remaining parts are nested into the open host holes,
then the remaining outers are placed by NFP-BLF
(`ContactNfpHoleNester.cs:528-592`). This is the GPR-defect-as-sheet-hole
differentiator for stone: pack saw-cuttable parts only in intact rock,
nesting offcuts into the voids of larger remnants
(`outputs/2026-06-12/hole_packer_evolution/HOLE_PACKER_MATH_AND_BENCHMARK.md`,
section 4).

### 1.6.3 The penetration-depth certificate (original derivation)

The NFP and IFP are built on simplified inputs: a Ramer-Douglas-Peucker pass
with tolerance $\tau=2\times10^{-3}\,\mathrm{diag}(p)$ carves the no-fit
region slightly inward (`NfpSimplifyTol`, `:1619-1623`), and the hull-based IFP
is anti-conservative on concave sheets. Area-relative gates alone admit
**needle overlaps**: a tiny intersection area (order $10^{-5}$ of part area)
but a penetration depth up to roughly 0.2 caller units. The repository's answer
is a compound gate on the **true, unsimplified** geometry; the NFP/IFP are
treated as pruning devices only (`ContactNfpHoleNester.cs:35-72`).

For loops $A$ and $B$, penetration depth is the maximum distance any probe of
one loop that lies strictly inside the other travels to that other loop's
boundary, augmented by an edge-crossing term:

$$
d(A,B)=\max\!\Bigl(
\max_{\substack{p\in\Pi(A)\\ p\in \operatorname{int}B}} \operatorname{dist}(p,\partial B),\
\max_{\substack{q\in\Pi(B)\\ q\in \operatorname{int}A}} \operatorname{dist}(q,\partial A),\
d_{\times}(A,B)
\Bigr),
$$

where the probe set $\Pi(\cdot)$ is the loop's vertices, edge midpoints, and
area centroid (the centroid closes the full-containment blind spot), and
$d_{\times}$ is the maximum proper edge-crossing perpendicular depth
(`PenetrationDepth` `:1777-1798`, `DepthProbes` `:1822-1839`,
`MaxProperCross` `:1879-1908`). Containment uses the analogous
`OutsideDepth`.

A candidate is accepted only if its depth stays under a **scale-relative**
tolerance instantiating the project epsilon budget at the verification level:

$$
\varepsilon_{\text{depth}}=\max\bigl(\varepsilon_{\text{floor}},\ 10^{-6}\,L\bigr),\quad L=\sqrt{\mathrm{area}(B)},\quad \varepsilon_{\text{floor}}=2\,\mathrm{SnapGrid},
$$

with $\mathrm{SnapGrid}=0.01$ scaled units $=10^{-5}$ caller units at the
internal scale $1000$ (`DepthTolFor` `:1646-1647`, constants `:1640-1643`).
Engine math runs in scaled space because Clipper snaps to a fixed decimal
precision; small-unit (metre) geometry would otherwise build sliver-noisy NFPs
(`Pack` conditioning `:121-154`).

**The micro-retreat.** Rather than discard a contact-tight candidate that only
violates through NFP simplification, the gate nudges it **once** along the
measured penetration vector $\mathbf{p}=(p_x,p_y)$ (length $=d$) by
$d+\mathrm{SnapGrid}$ and re-verifies:

$$
t'=t+\frac{d+\mathrm{RetreatSlack}}{\lVert\mathbf{p}\rVert}\,\mathbf{p},\qquad \mathrm{RetreatSlack}=\mathrm{SnapGrid}.
$$

If the nudged placement passes, it is accepted; otherwise the candidate is
rejected (`TryVerifiedCandidate` `:1728-1748`). This keeps the layout from
starving while never accepting a deeper-than-floor penetration. The same
compound gate runs path-independently in `Validate` (`:1373-1449`), so
`Valid==true` certifies the layout on **every** engine path, including the
fast path below.

### 1.6.4 The rect shelf fast-path

When every loop in the instance is an axis-aligned rectangle, the NFP and IFP
degenerate to rectangles and the whole solve reduces to interval arithmetic
with no Clipper calls. Rotations collapse to $\{0,\tfrac{\pi}{2}\}$ (squares to
$\{0\}$) using the exact integer map $(x,y)\mapsto(-y,x)$ so there is no
trigonometric round-off (`TryRectFastPath` `:635-738`, detection `:742-767`,
exact transform `:883-895`). A **completeness fallback** guards it: if the
sparser shelf candidate set strands any part (about 1 in 4000 in fuzzing), the
fast-path result is discarded and the general engine runs, so speed never
trades away a placement (`Pack` `:173-189`).

### 1.6.5 Multi-start keep-best

The general engine optionally runs $K$ deterministic part orders (area,
max-dimension, width, height; all descending) and keeps the densest **valid**
layout, breaking ties by placed count, then density, then smallest used
bounding-box diagonal (`BuildOrder` `:424-438`, keep-best `:243-292`). The
ordering keys form a total permutation, so the winner is reproducible.
Determinism is enforced at the cache level: each pass allocates its **own**
NFP cache keyed on (obstacle index, rotation signature), and the index is into
the per-pass placement order, so a cache shared across passes would return the
wrong NFP. The header documents this as the critical correctness point
(`ContactNfpHoleNester.cs:199-210`). $K=1$ reproduces the original single pass
byte-for-byte; `FRAHAN_MULTISTART=0` is the kill switch.

### 1.6.6 Deviation-compensated spacing (original derivation)

The solver collides on sampled **proxies** of smooth curves, but the output is
the exact original curve. A chord cuts inside the true curve by the sampling
**sagitta**, so two proxies that merely touch can let the true full-resolution
curves cross by up to that deviation on each side. The Grasshopper wrapper
measures the worst proxy deviation (max distance from a chord midpoint back to
the true curve) and inflates the engine spacing to compensate
(`HoleNestComponent.cs:758-790`, `:532-542`):

$$
\mathrm{spacing}_{\text{engine}}=\mathrm{spacing}_{\text{user}}+2\,\delta_{\text{part}}+\delta_{\text{sheet}}.
$$

**Derivation of the asymmetric coefficients.** A part-to-part clearance must
absorb the deviation of **both** colliding part proxies, hence $2\delta_{\text{part}}$.
A part-to-sheet clearance absorbs the part deviation through the same term, but
the sheet term enters **once**: a part can poke past the true boundary by at
most the sheet proxy's own deviation, and the sheet samples at high resolution
(192 verts) so $\delta_{\text{sheet}}$ stays tiny. The earlier shared formula
used $2\,\max_{\text{all loops}}\delta$, which let a large freeform sheet's
deviation inflate every part (a reported +27 units on the user's S-sheet,
collapsing a fill from 200 to 21); the asymmetric split is the fix
(`HoleNestComponent.cs:45-48`, `:532-542`). The compensation is surfaced to the
user as `ProxyDevComp: +{2*maxDev}` in the report (`:653`).

> **Originality.** CNH is **clean-room** at its NFP/BLF/IFP base (same citations
> as FreeNestX, `HoleNestComponent.cs:25-33`) with three **evolved-fork**
> increments that carry the research weight, each measured or fuzz-verified:
> the contact-adaptive rotation set, the part-in-part-hole IFP nesting, and the
> distance-based penetration certificate with micro-retreat. The
> `[Algorithm]` attribute names these as "Frahan ContactNfpHoleNester
> evolution study" and the head-to-head protocol is documented, not asserted
> (`HoleNestComponent.cs:34-36`). The component is the CNH facade over the Core
> engine. The native batched-NFP path wraps `nfp_kernel.dll`, which vendors
> official Clipper2 (BSL-1.0) unmodified (`NativeNfpKernel.cs:10-22`); that is
> **vendored-library**, with only the marshalling ours.

### 1.6.7 Head-to-head: V506 vs NFP-BLF vs CNH vs the reference physics nester

The 2026-06-12 hole-packer study benchmarked CNH against the OpenNest-lineage
**reference physics nester** (the jagua-rs guided-local-search strip packer,
"Sparrow") and an MIT-licensed C++ NFP-GLS native nester
(`HOLE_PACKER_MATH_AND_BENCHMARK.md`). On the true-hole lane (1 sheet, 1
sheet-hole, 4 host parts with slanted holes, 8 fillers):

| Packer | time | placed | holes filled | valid | deterministic |
|---|---|---|---|---|---|
| CNH v1 (general exact-NFP) | 60.7 ms | 12/12 | 4 | **true** | **yes** |
| CNH v2 (rect fast-path) | **0.148 ms** | 12/12 | 4 | true | yes |
| MIT native nester (shelf) | 21.6 ms | 12/12 | 4 | true | no (stochastic) |
| Reference physics nester (Sparrow) | 3255 ms | 12/12 outlines | 0 | **false** | no |

CNH is **valid where the reference physics nester is invalid**: Sparrow ignores
holes (4 hole-ignore warnings, 953.7 overlap loss) and produces no usable
hole-aware layout at any time budget. Against Sparrow, CNH v1 is about
**54x faster and valid** on the same parts; the rect fast-path is about
22,000x faster and valid. Against the strongest **valid** baseline (the native
shelf) CNH v1 is 2.8x slower because it runs the general exact-NFP construction
rather than an axis-aligned shortcut, but it is the only deterministic engine
and its v2 fast-path beats the native shelf by 146x on all-rectangle instances.
The honesty boundary is held in source: on the **outline-only** strip lane the
reference physics nester still wins density by 6 to 10 percent, and no
universal "2x better" claim is made there
(`HOLE_PACKER_MATH_AND_BENCHMARK.md`, sections 2-3).

---

## 2. Trencadís mosaics

Trencadís is the Gaudí "broken-tile" technique: irregular shards placed close
together with a grout gap. The repository ships two distinct solver
philosophies under the `Trencadis` tab, plus an EdgeMatch variant.

### 2.1 Catalog mode: CVD-Lloyd cells + Hungarian assignment

`Pack2DTrencadisCatalogComponent` (TrencadisCat, GUID
`F2D00007-CADC-4F2D-9007-7E60CADA15A0`) is the example-12 engine. It partitions
the sheet into blue-noise cells then assigns shards to cells one-to-one.

**Centroidal Voronoi cells (Lloyd 1982).** A Voronoi diagram of $K$ seeds is
centroidal when each seed coincides with the centroid of its own cell. Lloyd's
algorithm reaches this fixed point by iterated relaxation: assign each domain
point to its nearest seed, then move each seed to the centroid of its cell:

$$
s_i^{(n+1)}=\frac{\displaystyle\int_{V_i} x\,\rho(x)\,dx}{\displaystyle\int_{V_i}\rho(x)\,dx},
\qquad V_i=\{x : \lVert x-s_i\rVert\le\lVert x-s_j\rVert\ \forall j\}.
$$

The repository discretises the domain (outer polygon minus holes) on an
$N\times N$ grid with uniform density $\rho\equiv 1$, assigns each grid cell to
its nearest seed, recentres, and stops when the largest seed move drops below
half a grid step (`CvdLloyd2d.cs:30-108`). The result is a blue-noise seed
field, far better than placing the first piece at a bbox corner.

**Optimal assignment (Kuhn-Munkres).** With $n$ shards and $n$ cells, let the
cost $c_{ij}$ be the area mismatch of placing shard $i$ in cell $j$. The
minimising one-to-one assignment solves

$$
\min_{\pi\in S_n}\ \sum_{i=1}^{n} c_{i,\pi(i)},
$$

via the $O(n^3)$ shortest-augmenting-path Hungarian method, with row and
column potentials $u_i,v_j$ maintained so reduced costs
$c_{ij}-u_i-v_j\ge 0$ stay non-negative (`HungarianAssignment.cs:23-85`). Each
placed shard is moved to its cell centroid and inset by the grout offset to
leave the mortar gap. Example 12 places 28 shards into 28 cells in 53 ms with
zero warnings (`examples/12_trencadis/README.md`).

![Trencadis catalog mosaic: 28 shards in 28 CVD-Lloyd cells with grout](../../../examples/12_trencadis/12_trencadis_result.png)

> **Originality.** CVD-Lloyd is **clean-room** from Lloyd (1982) centroidal
> Voronoi relaxation (`CvdLloyd2d.cs:14-22`, `[Algorithm]` at
> `Pack2DTrencadisCatalogComponent.cs:37`). The Hungarian solver is a
> **clean-room** textbook Kuhn-Munkres / Bourgeois-Lassalle (1971)
> shortest-augmenting-path implementation, allocation-bounded and
> net48-compatible (`HungarianAssignment.cs:11-15`). The catalog placement
> wrapper that couples them is **facade-over-primitives**: its `[Algorithm]`
> credits "Slab-partitioned Voronoi catalog; Frahan-original Trencadis
> extension" over a Battiato 2013 synthesis precedent
> (`Pack2DTrencadisCatalogComponent.cs:38`, `:42`). Tier C/B.

### 2.2 Physics-settle variants vs the greedy/EdgeMatch variants

The `Trencadis` tab is intentionally **not** one engine. The variants differ in
how a piece reaches its final pose, and the repository keeps them separate
rather than blind-merging them (the "the 2D solvers are real, validate don't
merge" ruling):

- **Greedy NFP-slide** (`Pack2DTrencadisComponent`, GUID
  `F2D00002-...`). Pieces slide along a Minkowski-difference arc-length sampler
  until they butt against placed neighbours, with a bounded-overlap trim up to
  the Trim Tolerance and the Battiato 2013 §4 cumulative-cut budget ($T_N$ for
  the new piece, $T_P$ for placed) (`TrencadisFill.cs:13-27`,
  `Pack2DTrencadisComponent.cs:37-38`). The standalone greedy box is a skeleton
  returning empty; the working entrypoint is the catalog or pipeline component
  (`examples/12_trencadis/README.md`).
- **Dynamic settle** (`Pack2DTrencadisDynamicComponent`, GUID
  `F2D00008-...`). After a greedy pack, a Kangaroo 2 goal-based dynamic
  relaxation pass settles the pieces to fill residual gaps (physics, not
  geometry). Headless coverage 55.1% with physics on vs 52.7% greedy
  (`Pack2DTrencadisDynamicComponent.cs:61-62`,
  `examples/12_trencadis/README.md`).
- **EdgeMatch** (`TrencadisEdgeMatchComponent`, GUID `F2D0000A-...`). Drives
  placement through the 5-stage EdgeMatch pipeline plus a deterministic
  beam-search assembly solver, an alternative to the Battiato 2013 CVD+GVF
  stack (`TrencadisEdgeMatchComponent.cs:28-29`).
- **Pipeline** (`Pack2DTrencadisPipelineComponent`, GUID `F2D00009-...`)
  composes greedy pack + NFP slide + CVD-Lloyd seeding + an optional Kangaroo 2
  settle (`Pack2DTrencadisPipelineComponent.cs:59-62`).

> **Originality.** The greedy NFP-slide and the dynamic/EdgeMatch/pipeline
> wrappers are **facade-over-primitives** that compose published Frahan
> primitives (CVD-Lloyd, NFP slide, Hungarian, EdgeMatch, the Kangaroo 2
> relaxation). The physics step explicitly credits Daniel Piker's Kangaroo 2
> (`Pack2DTrencadisDynamicComponent.cs:62`); the Trencadís domain framing is
> credited to Gaudí Park Güell and the Battiato 2013 synthesis. None claims a
> new algorithm; the contribution is the integration. Tier C/D.

---

## 3. Status & what's left

- **Example 28 (hole nest) has no rendered figure.** The folder ships the `.gh`
  demo only (`examples/28_hole_nest/`), no PNG and no README. The CNH renders
  in this chapter borrow from example 10 (nesting) and example 12 (Trencadís).
  Severity: low (documentation gap, not a code gap).
- **Deployed `.gha` lag (KB-7).** The example-10 validity figures are from the
  headless harness on current source. An older deployed `.gha` may still
  overlap parts on a live 2D solve; rebuild and redeploy before trusting a live
  result (`examples/10_pack2d/README.md`). Severity: medium.
- **Standalone greedy Trencadís box returns empty.** `Frahan Trencadís Pack`
  is a skeleton; the working entrypoints are the Catalog and Pipeline
  components (`examples/12_trencadis/README.md`). Severity: medium (a ghost on
  the ribbon, against AGENTS.md section 6).
- **Rect fast-path spacing limitation.** The exact rectangle shelf path only
  activates at `spacing == 0`; spacing > 0 needs exact rect-dilation
  bookkeeping and is deferred to the general engine
  (`ContactNfpHoleNester.cs:611-616`). Severity: low (correct fallback exists).
- **Residual penetration band.** After the compound gate, penetrations inside
  the snap band (about $2\times10^{-5}$ caller units) can be accepted; deeper
  ones cannot on any path. This is a stated, bounded residual, far inside any
  fabrication budget (`ContactNfpHoleNester.cs:69-72`). Severity: low.
- **Outline-density gap to the reference physics nester.** On the no-hole
  outline strip lane the reference physics nester (Sparrow) still wins density
  by 6 to 10 percent; CNH's claimed win is the hole-aware lane only. This
  boundary must stay in any external claim
  (`HOLE_PACKER_MATH_AND_BENCHMARK.md`, section 2c). Severity: low (honesty,
  not a defect).

---

## References (this chapter)

- Burke, E.K., Hellier, R., Kendall, G., Whitwell, G. (2006). A New
  Bottom-Left-Fill Heuristic Algorithm for the Two-Dimensional Irregular
  Packing Problem. Operations Research 54(3):587-601. DOI
  10.1287/opre.1060.0293.
- Burke, E.K., Hellier, R., Kendall, G., Whitwell, G. (2007). Complete and
  robust no-fit polygon generation for the irregular stock cutting problem.
  European Journal of Operational Research 179(1):27-49. DOI
  10.1016/j.ejor.2006.03.011.
- Bennell, J.A., Oliveira, J.F. (2009). A tutorial in irregular shape packing
  problems. Journal of the Operational Research Society 60(S1):S93-S105. DOI
  10.1057/jors.2008.169.
- Lloyd, S.P. (1982). Least squares quantization in PCM. IEEE Transactions on
  Information Theory 28(2):129-137. DOI 10.1109/TIT.1982.1056489.
- Kuhn, H.W. (1955). The Hungarian method for the assignment problem. Naval
  Research Logistics Quarterly 2(1-2):83-97. DOI 10.1002/nav.3800020109.
- Battiato, S., Di Blasi, G., Gallo, G., Guarnera, G.C., Puglisi, G. (2013).
  Artificial mosaic generation: a survey and synthesis. (Trencadís synthesis
  precedent, cited in source.)
- Johnson, A. Clipper2 (Boost Software License 1.0). Polygon Minkowski sum and
  NonZero boolean back end.
