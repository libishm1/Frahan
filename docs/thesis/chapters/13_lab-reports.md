# 13. Lab, Analysis & Reporting

This chapter covers the repository's instrumentation surface: the three
ribbon tabs that do not produce primary stone geometry but inspect, measure,
and report on the subsystems that do. They are `Lab` (26 components, the
third-largest subcategory), `Reports` (3 components), and `Analysis` (3
components). None of these is a new solver. Their contribution is access:
exposing a Core primitive for diagnostics, summarising a solver's output as
typed metrics, or formatting those metrics per audience. The interesting
design questions here are governance questions, not mathematics ones. What
keeps the `Lab` tab from becoming a junk drawer of dead probes? What stops a
monolith like the heterogeneous-extraction pipeline from becoming an opaque
black box that hides the published primitives it composes? The repository
answers both with explicit rules that this chapter audits against the source.

Two governance rules from the project's working notes bind every component
below. The **lab-not-an-island** rule requires every `Lab` node to carry a
`[RelatedComponent]` attribute pointing at a real production sibling (a 3D
packing, quarry, or mesh component), so a `Lab` probe is never a dead-end.
The **monster-vs-primitive** rule requires every monolithic facade (the
`HeteroExt` pipeline is the canonical case) to be a thin orchestrator over
published Core primitives, with the same engine also exposed standalone, so
both the composed convenience node and its decomposed primitives ship
together. The honesty convention of `AGENTS.md` §6 governs the whole tab: no
ghost component, every node must produce a real, valid output, and a node
that returns empty is a defect to be flagged, not a feature.

---

## 13.1 The Lab subcategory: what it is and what it is not

`Lab` is the reserved subcategory for diagnostic and research-probe
components: native-shim exercisers, single-objective inspectors over a
production solver, and one or two "answer a specific question" bonus
packers. The central allow-list `LabConfig` documents the design intent
precisely. Lab-gating is a **visibility flag only**: a Lab-gated component
never has its source, icon, GUID, or `.csproj` entry deleted, and removing a
GUID from the gate returns the component to its default ribbon (a reversible
operation). The current gate is intentionally empty:

> The Lab subcategory itself stays reserved for genuinely miscellaneous /
> scratchpad / experimental future components that need an enabling
> configuration to use. None qualify in v1.0.
> (`Attributes/LabConfig.cs:28-35`.)

That comment records a 2026-05-30 reversal: eleven GUIDs were once force-gated
into `Lab`, then released because they "turned out to be real, named
algorithms / production paths, not miscellaneous" (`LabConfig.cs:18-26`). The
`Lab` tab today is therefore populated by components whose **own** base
constructor declares `"Frahan", "Lab"`, not by runtime gating. There are 26
such components across six source files.

| File | Count | Components (short name) |
|---|---|---|
| `CgalTestComponents.cs` | 8 | `MeshCsgCgal`, `SkeletonCgal`, `MeshRepairCgal`, `DecimateCgal`, `PartitionCgal`, `SegmentSdfCgal`, `SegmentAngleCgal`, `GeodesicVoronoiCgal` |
| `GeogramTestComponents.cs` | 8 | `DecimateGeogram`, `RepairGeogram`, `ObbGeogram`, `RemeshGeogram`, `TetGeogram`, `CvtGeogram`, `RvdGeogram`, `FillHolesGeogram` |
| `BlockCutOptInspectorComponents.cs` | 5 | `BCOPareto`, `BCORobust`, `BCOWatershed`, `VtuOut`, `BCOMixedPack` |
| `AutoMeshComponents.cs` | 3 | `RepairAuto` (retired), `DecimateAuto`, `ObbAuto` |
| `CoacdTestComponents.cs` | 1 | `DecomposeCoacd` |
| `DownloadFrahanDataComponent.cs` | 1 | `GetData` |

These group into three roles. **Native-shim exercisers** (the CGAL, Geogram,
CoACD, and Auto families, 20 of the 26) are Grasshopper surfaces that drive
the out-of-process geometry kernels end-to-end and report which back end
actually ran. **Solver inspectors** (the five `BlockCutOpt` components) wrap a
production quarry solver and surface a single axis or a robustness sweep that
the production node does not expose. **Distribution helper** (`GetData`) is
the on-demand large-asset fetcher. The taxonomy matters for the originality
call-outs: the exercisers are wrappers over vendored or in-tree kernels, the
inspectors are evolved or clean-room math over a cited baseline, and the
helper is a Frahan-original utility.

### 13.1.1 The lab-not-an-island rule, audited

The rule is verifiable by attribute. Every one of the 26 `Lab` components
carries at least one `[RelatedComponent]` pointing at a production sibling.
Sampling the families:

- `MeshCsgCgal` redirects to `Frahan > Masonry > Mesh CSG` and
  `Slab Cut By Fractures` (`CgalTestComponents.cs:120-121`).
- `DecimateGeogram` redirects to `Frahan > Mesh > Mesh Repair`, with the
  honest note "no production decimate component yet"
  (`GeogramTestComponents.cs:26`).
- `BCOPareto` redirects to `BlockCutOpt Solve` and `BlockCutOpt Omni Solve`
  (`BlockCutOptInspectorComponents.cs:39-40`), the production solvers whose
  Pareto front it visualises.
- `DecomposeCoacd` redirects to `Masonry Assembly`, `Auto Interfaces`, and
  `Mesh Diagnostics` (`CoacdTestComponents.cs:29-31`).

No `Lab` node was found without a production cross-reference. The rule holds
across the tab as shipped.

### 13.1.2 Native-shim exercisers (CGAL / Geogram / CoACD / Auto)

These nodes are wrappers, not algorithms. They marshal a Rhino `Mesh` to the
shared `MeshSnapshot` interop record, call the managed wrapper over the native
shim, and marshal back, reporting the back end (`Geogram`, `Cgal`,
`ManagedBsp`, or `None`) and a timing line. The marshalling itself is the only
in-tree work: the conversion welds coincident vertices and drops unreferenced
ones, because Rhino's procedural primitives emit duplicated corner vertices
(24 for a cube instead of 8) and CGAL corefinement then treats every edge as a
boundary and returns hole-ridden output (`CgalConvert.ToSnapshot`,
`CgalTestComponents.cs:29-64`). The `Auto*` family adds one layer: it asks the
`MeshOps` facade to pick the best available back end (Geogram first, CGAL
fallback) and reports which ran (`AutoMeshComponents.cs:55-88`).

> **Originality.** The shim exercisers are **wrapper-of-native** for the call
> path and **clean-room** for the marshalling. The cited kernels are: CGAL
> Polygon Mesh Processing corefinement (`[Algorithm]`
> `CgalTestComponents.cs:119`), Geogram vertex-clustering decimation (Levy,
> Geogram v1.9.9, BSD-3, `GeogramTestComponents.cs:25`), CoACD (Wei et al.
> 2022, SIGGRAPH, `CoacdTestComponents.cs:28`), and the CGAL straight
> skeleton, SDF segmentation, and geodesic-Voronoi probes. **Licensing.** CGAL
> is GPLv3 and CoACD vendors CGAL transitively; both are reached only through
> optional out-of-process shims absent from the default install, with a
> managed BSP fallback. The Geogram `TetGeogram` node wraps Geogram's TetGen
> path, which is AGPL and on by default in the geogram build, gated behind
> `-DFRAHAN_WITH_TETGEN` for an AGPL-free configuration
> (`GeogramTestComponents.cs:547`, `:738`; licensing register flags E3-E6).
> The Clipper2 and Kazhdan-Poisson permissive paths carry attribution only.

### 13.1.3 Solver inspectors (the BlockCutOpt Lab five)

These are the algorithmically interesting `Lab` nodes. Each wraps the quarry
block-cutting solver of Chapter 3 and surfaces information the production node
withholds.

**Pareto front inspector (`BCOPareto`, GUID `F2D0BC10`).** The production
`BlockCutOpt Omni Solve` returns three of four objectives; the inspector runs
`BlockCutOptOmniSolver` and surfaces all four optima per sub-zone in parallel:
recovery-max, revenue-max, kerf-time-min, and the Jalalian BCSdbBV cost-min
(`BlockCutOptInspectorComponents.cs:147-167`). The fourth axis is the
sustainable-mining cost objective

$$
\mathrm{BCSdbBV} = \frac{S}{BV},\qquad S = 2\,(L_xL_y + L_yL_z + L_xL_z),
$$

cutting-surface area $S$ over block value $BV$ (Jalalian et al. 2023): minimise
the sawn surface per unit of recovered value. The inspector exposes the
`Front.BestBcsdbBv()` extremum that the single-best-recovery production output
hides.

**Fisher-robust solver (`BCORobust`, GUID `F2D0BC11`).** A single
deterministic optimum is fragile: the best cutting direction $\psi$ depends on
fracture orientations measured with scatter. `BCORobust` runs the solver $M$
times against $M$ Fisher-perturbed realisations of the same joint sets and
reports the percentile band, not the point estimate
(`BlockCutOptInspectorComponents.cs:270-289`).

**Original framing: the robust optimum is the median direction.** Given $M$
Monte-Carlo samples each returning a recovery $R_k$ and a best direction
$\psi_k$, the deterministic best $\psi^\star$ over the unperturbed mean
orientations can sit on a knife-edge. The robust score is the lower percentile

$$
R_{p10} = \mathrm{percentile}_{10}\{R_1,\dots,R_M\},\qquad
\psi_{\text{robust}} = \mathrm{median}\{\psi_1,\dots,\psi_M\},
$$

so the reported direction is the one that survives orientation noise rather
than the one that maximises a single noiseless realisation (Azarafza et al.
2016 Fisher-scatter reading; synthesis axis I8). The component returns
$R_{p10}/R_{p50}/R_{p90}$, the mean, the standard deviation, and the per-sample
arrays for the caller's own histogram
(`BlockCutOptInspectorComponents.cs:219-228`). The base seed makes the sweep
reproducible (`:216`).

**Density-watershed zones (`BCOWatershed`, GUID `F2D0BC12`).** Replaces the
uniform $(m_x,m_y)$ sub-division with an adaptive partition whose zone
boundaries snap to high-density fracture ridges, so the unavoidable
boundary-cut penalty lands on already-broken rock
(`BlockCutOptInspectorComponents.cs:340-381`). It is the GH front end to the
in-tree `DensityWatershedPartition` (synthesis I5).

**VTU export (`VtuOut`, GUID `F2D0BC13`).** Runs the solver, regenerates the
winning tilted cutting grid, classifies each cell against the triangle-AABB
BVH as intersected or clear, and writes a ParaView `.vtu` with two cell sets,
matching the BlockCutOpt 2020 paper's figure convention
(`BlockCutOptInspectorComponents.cs:463-499`). A default-false `Write` gate
keeps the file I/O off until requested.

> **Originality.** `BCOPareto` is **clean-room** over the cited Jalalian 2023
> BCSdbBV axis (`[Algorithm]` `BlockCutOptInspectorComponents.cs:38`); it adds
> no algorithm, only the fourth-axis surfacing. `BCORobust` is **clean-room**
> Monte-Carlo robustness sampling over the cited Azarafza 2016 Fisher reading
> (`:175`). `BCOWatershed` fronts a **clean-room** Frahan-original partition
> (`:298`, "Core DensityWatershedPartition.cs verified-original"). `VtuOut` is
> a **facade-over-primitives** export node composing the solver, the cutting
> grid, and the BVH. All four are diagnostic surfaces over the Chapter 3
> production solvers, each carrying the `[RelatedComponent]` redirect required
> by the lab-not-an-island rule.

![Uncertainty-safe quarry yield: blocks packed only in fracture-clean rock, the production output the Lab inspectors instrument](../../../examples/09_uncertainty_safe_yield/uncertainty_safe_yield_3d.png)

### 13.1.4 The bonus mixed-size packer and the monster-vs-primitive balance

`BCOMixedPack` (GUID `F2D0BC17`) is the one `Lab` node that is not a probe but
a standalone packer. It answers a direct question the project owner asked:
"can we pack multiple sizes instead of one size for the entire quarry?". It
exposes the **2D Deepest-Left-Bottom-Fill** packer `DlbfMixedSizePacker`
directly: a multi-size catalogue with per-size revenue, pieces sorted by
revenue-per-area, placed at the deepest-then-leftmost free grid cell, with
optional forbidden boxes encoding fracture-intersected regions
(`BlockCutOptInspectorComponents.cs:559-620`; engine
`DlbfMixedSizePacker.cs:8-25`). Pieces sort by

$$
\text{order key} = \frac{\mathrm{Revenue}}{\mathrm{Width}\cdot\mathrm{Depth}}
\quad(\text{descending}),
$$

the revenue-per-area density (`DlbfMixedSizePacker.cs:46`), and the placement
rule scans the discrete grid for the deepest-left-bottom feasible cell
(Chehrazad, Roose, Wauters 2025).

This is the load-bearing example of the **monster-vs-primitive** rule. The
same DLBF engine is the core of the monolithic `HeteroExt` pipeline
(`FrahanHeterogeneousExtractionComponent`, GUID `F2D0BC19`, on the `Quarry`
tab), a four-stage facade that runs BlockCutOpt to find fracture-clean
regions, marks intersected cells forbidden, runs 3D DLBF mixed-size packing,
and optionally places monuments
(`BlockCutOptHeterogeneousComponents.cs:179-191`). The monolith's
`[Algorithm]` names itself "Frahan-original" but immediately declares that it
"Composes Elkarmoty 2020 (BlockCutOpt) and Chehrazad 2025 (DLBF), both
interpreted and reimplemented in managed code; the composition and the
heterogeneity model are the contribution"
(`BlockCutOptHeterogeneousComponents.cs:169`). Crucially the monolith carries
explicit `[RelatedComponent]` back-pointers to the standalone primitives it
composes: to `Frahan > Lab > Frahan Mixed-Size Block Pack` (the 2D `F2D0BC17`)
and to `Frahan > Quarry > Frahan Mixed-Size Block Pack 3D` (the 3D `F2D0BC18`),
each annotated "the same engine this facade composes"
(`:170-173`). Both the composed convenience and the decomposed primitive ship,
the seams are exposed, and the engine is never a black box. This is the rule
satisfied, with the `Lab` tab holding the 2D primitive seam.

> **Originality.** `BCOMixedPack` is **clean-room** over the cited DLBF
> (Chehrazad, Roose, Wauters 2025, DOI 10.1080/00207543.2025.2478434,
> `BlockCutOptInspectorComponents.cs:509`). `HeteroExt` is
> **facade-over-primitives**: a Frahan-original composition with no new
> algorithm, its two cited sub-algorithms each exposed standalone
> (`BlockCutOptHeterogeneousComponents.cs:169-173`). The pairing is the
> monster-vs-primitive rule made executable.

### 13.1.5 The distribution helper

`GetData` (GUID `F2D05A08`) fetches optional large assets (the Kintsugi
`kintsugi.bin` weights plus the Torch/CUDA runtime, or the examples bundle)
from a release manifest into the deploy folder, SHA-256-verifying each file,
on a background thread so the canvas stays responsive
(`DownloadFrahanDataComponent.cs:36-58`). It keeps the install lean by moving
the non-redistributable and the heavy off the `.gha`.

> **Originality.** **original-research** is overstated for a utility; this is a
> **Frahan-original distribution helper** (`[Algorithm]`
> `DownloadFrahanDataComponent.cs:36-38`), an engineering utility, not a
> research contribution. Its licensing relevance is real: it is the mechanism
> by which the GPL-3.0 / non-commercial Kintsugi weights stay out of the
> default permissive install until the user explicitly fetches them.

---

## 13.2 The Reports tab: typed metrics from solver output

The `Reports` tab holds three components, each a thin Grasshopper marshaller
over a Rhino-free Core report type. The math is elementary; the contribution
is a single canonical metrics surface so every solver reports the same way.

### 13.2.1 Packing metrics

`PackRpt` (GUID `AB12C004`) consumes an opaque `PackResult` from any 3D pack
solver and surfaces `Frahan.Core.PackingMetrics`. The Core computes placement
and failure counts, the failure ratio, packed and container volumes, the fill
ratio, the average placement score, item-volume statistics, and a per-reason
failure breakdown (`PackingMetrics.cs:72-133`). The two headline ratios are

$$
\mathrm{FailureRatio} = \frac{N_{\text{fail}}}{N_{\text{placed}} + N_{\text{fail}}},
\qquad
\mathrm{FillRatio} = \frac{V_{\text{packed}}}{V_{\text{container}}},
$$

and the per-reason histogram is built by counting `Failure.Reason` strings
(`PackingMetrics.cs:112-118`). The component orders the failure reasons by
descending count and emits a single-line summary
(`PackingReportComponent.cs:101-106`). The Core is static, side-effect-free,
and allocates only the returned report dictionary
(`PackingMetrics.cs:64-67`).

### 13.2.2 Packing-plan report

`PackPlanRpt` (GUID `AB12C008`) aggregates three pieces into one composite
`PackingPlanReport`: the `PackingMetricsReport`, a residual-void list, and the
per-fragment-per-edge match scores from the edge-matcher
(`PackingPlanReportComponent.cs:164-182`; builder `PackingPlanReport.cs:51-91`).
The two scalars it derives are the total residual-void area (a sum of
per-void approximate areas) and the mean best-edge-match score (a flatten-then-
average over the nested per-fragment score lists):

$$
A_{\text{void}} = \sum_v \mathrm{area}(v),\qquad
\bar{s}_{\text{edge}} = \frac{1}{|E|}\sum_{e\in E} s_e .
$$

The pieces are decoupled, so a caller can pass any subset and `null` for the
rest (`PackingPlanReport.cs:46-48`). The component accepts edge scores three
ways: a nested opaque list, a flat opaque list, or a Grasshopper
`DataTree<Number>` with one branch per fragment, the tree taking precedence
when both are wired (`PackingPlanReportComponent.cs:114-162`). That tree path
is the natural Grasshopper UX; the opaque path stays for code that already
produces nested lists.

### 13.2.3 Audience report

`Report` (GUID `AB12C010`) is the single report/export terminal driven by an
`Audience` enum (engineer / artist / geologist). It consumes the typed
`FrahanReport` records the kept solvers emit (Packing, MeshDiagnostics,
FabricationPrep, BlockCutOpt, ChartFlatness) plus optional pipe-delimited
section rows, then orders, routes, flags, and formats per audience, applying
the spec's audience rules: the engineer release is **refused without a
declared CRS/datum**, the artist flags grain/vein UNKNOWN, the geologist flags
rock-mass needing a worksheet (`AudienceReportComponent.cs:27-46`). Output is
Markdown plus CSV; with `Run` and a path it writes the files
(`AudienceReportComponent.cs:147-169`). The composition logic is Rhino-free and
unit-tested in `Frahan.Core.Reports.AudienceReportComposer`; the component is a
thin marshaller (`:24-26`). The CRS refusal is the only "gate" in the tab: a
correctness guard against shipping an unreferenced mining plan
(`Tolerance = "Engineer release refused without a declared CRS/datum"`,
`:31`).

> **Originality.** All three report components are **facade-over-primitives**:
> Frahan-original report generators over the cited spec sections (spec 5 §5,
> spec 7 §5), composing pure-data Core DTOs. The `[DesignApplication]`
> precedents name them "Frahan-original packing-report generator"
> (`PackingReportComponent.cs:25`), "Frahan-original packing-plan report"
> (`PackingPlanReportComponent.cs:24`), and the "SAMPLE_GH_SPEC.md
> three-audience report terminal" (`AudienceReportComponent.cs:30`). No new
> algorithm; the contribution is one canonical, audience-aware metrics surface.

A note on scope: the `ChartFlatnessReport` named in this subsystem lives in
Core under `Frahan.Surface` (`ChartFlatnessReport.cs`), but its Grasshopper
front end `ChartFlat` (GUID `AB12C006`) is filed on the **Surface Packing**
tab, not `Reports` (`ChartFlatnessReportComponent.cs:32`). Its math is a
per-face area-ratio distortion test using $\max(r, 1/r)$ so $0.5\times$ and
$2\times$ count as equally distorted (`ChartFlatnessReport.cs:90-101`); it is
**clean-room** Frahan-original, not the BFF algorithm
(`ChartFlatnessReportComponent.cs:23-24`). It is reported here because it is
one of the five `FrahanReport` record types the audience terminal consumes,
and the roadmap proposes driving an adaptive surface re-cut from it.

---

## 13.3 The Analysis tab: edge-matching diagnostics

The `Analysis` tab holds three components, all moved there on 2026-05-05 from
`2D Packing` to make their diagnostic role explicit. Each was once wired into
the irregular-sheet packer; the unified solver now folds boundary scoring in
internally, so these standalone nodes survive only to inspect the index the
solver builds, debug affinity scores, and export for ad-hoc analysis. None
needs to be wired into the solver any more
(`BoundaryRailIndexComponent.cs:19-25`).

- **`RailIdx`** (GUID `AB12C001`) builds a `BoundaryRailIndex` from boundary
  curves: each curve is sliding-window-sampled into (length, tangent angle,
  curvature) buckets and stored as a `BoundaryIntervalInfo`
  (`BoundaryRailIndexComponent.cs:38-52`).
- **`FragDesc`** (GUID `AB12C007`) converts closed planar curves into
  `FragmentDescriptor`s with per-edge `EdgeDescriptor`s and surfaces area,
  perimeter, aspect ratio, and edge counts
  (`FragmentDescriptorsComponent.cs:34-47`).
- **`FragMatch`** (GUID `AB12C003`) matches each fragment edge against a
  populated index and returns ranked affinity scores as one
  `DataTree<Number>` branch per fragment
  (`FragmentEdgeMatchComponent.cs:38-51`).

The three compose a pipeline: `RailIdx` builds the index, `FragMatch` queries
it with `FragDesc` descriptors. The bucketing is arc-length affinity over a
turning-function representation (Arkin et al. 1991 is the turning-function
shape-metric precedent).

> **Originality.** All three are **clean-room** Frahan-original diagnostics:
> the geometric descriptors are textbook quantities but the descriptor schema
> and the affinity-bucket index are Frahan-original (`[Algorithm]` notes
> "geometric descriptors are textbook quantities but the descriptor schema is
> Frahan-original", `FragmentDescriptorsComponent.cs:29-31`; "arc-length
> affinity bucketing, not a published algorithm",
> `BoundaryRailIndexComponent.cs:33-35`). They carry no production
> `[RelatedComponent]` redirect because they are not `Lab`-gated; their
> production home is the unified `Frahan Sheet Pack`, which absorbed their
> function (`FragmentEdgeMatchComponent.cs:20-24`).

---

## 13.4 Status & what's left

- **Retired Lab ghost (`RepairAuto`).** `AutoMeshRepairComponent`
  (GUID `F2D000D0`) is marked `Obsolete` and `Exposure=hidden`, superseded by
  `Sanitize Mesh (Backend=Auto)` which runs the same `MeshOps.Repair` plus a
  CGAL-Ready verdict; the GUID is preserved so existing canvases keep loading
  (`AutoMeshComponents.cs:34-40`). This is the hide-not-delete pattern done
  correctly, not a defect. *Low.*
- **Lab decimate has no production sibling.** `DecimateGeogram` and
  `DecimateCgal` redirect to `Mesh Repair` because there is no production
  decimate component yet (`GeogramTestComponents.cs:26`). The lab-not-an-island
  rule is satisfied by the redirect, but the honest implication is a missing
  production node: a `Mesh Decimate` should be promoted out of `Lab`. *Medium.*
- **AGPL exposure via `TetGeogram`.** The tetrahedralise probe wraps Geogram's
  TetGen path, which is AGPL and on by default in the geogram build; an
  AGPL-free configuration requires `-DFRAHAN_WITH_TETGEN=OFF`, which disables
  volumetric Voronoi blocks (`GeogramTestComponents.cs:547`, `:738`; register
  flag E6). The native shims are absent from the default install, so the
  default path is clean, but a packager who turns on the shims must honour the
  flag. *High.*
- **Greedy Trencadís ghost (cross-subsystem).** The named-but-empty
  `Frahan Trencadís Pack` box (GUID `F2D00002`, 2D Nesting tab) returns empty
  output on the primary ribbon, an `AGENTS.md` §6 violation; it is not a `Lab`
  node, but it is the canonical ghost the Lab governance rules exist to
  prevent. The fix is to implement the greedy pack or move the box off the
  primary ribbon and route users to Catalog / Pipeline (roadmap item 5,
  `91_roadmap.md:42`, `:88`). *Medium.*
- **`ChartFlatnessReport` is under-used.** The Core flatness classifier feeds
  only the audience terminal today. The roadmap proposes driving an adaptive
  per-face surface re-cut from it (`91_roadmap.md:48`), which would make it a
  control input rather than a report-only leaf. *Low.*
- **Reports tab has no rendered figure.** The `Reports` and `Analysis` tabs
  ship no dedicated example folder; the embedded figure borrows example 09
  (the uncertainty-safe yield), the production output the Lab BlockCutOpt
  inspectors instrument. *Low (documentation gap, not a code gap).*

---

## References (this chapter)

- Chehrazad, R., Roose, D., Wauters, T. (2025). A fast and scalable
  deepest-left-bottom-fill algorithm for the 3D bin packing problem.
  International Journal of Production Research 63:6606-6629. DOI
  10.1080/00207543.2025.2478434.
- Elkarmoty, M., Bondua, S., Bruno, R. (2020). Mechanized in-situ
  determination of joint-related and yield-related rock-mass parameters
  during dimension stone block extraction. Resources Policy 68:101761. DOI
  10.1016/j.resourpol.2020.101761.
- Jalalian, M.H., Bagherpour, R., Khoshouei, M. (2023). Environmentally
  sustainable mining in quarries to reduce waste production and loss of
  resources using the developed optimization algorithm (BCSdbBV). Scientific
  Reports 13:22183. DOI 10.1038/s41598-023-49633-w.
- Azarafza, M. et al. (2016). Granite block-cut analysis with Fisher-
  distribution joint-orientation scatter.
- Wei, J., Liu, M., Wang, J. et al. (2022). Approximate convex decomposition
  for 3D meshes with collision-aware concavity and tree search (CoACD). ACM
  Transactions on Graphics (SIGGRAPH 2022) 41(4):42. DOI
  10.1145/3528223.3530103.
- Levy, B. (INRIA/ALICE). Geogram: a programming library of geometric
  algorithms (v1.9.9). BSD-3.
- The CGAL Project (2023). CGAL user and reference manual. CGAL Editorial
  Board. GPLv3 / commercial.
- Lloyd, S.P. (1982). Least squares quantization in PCM. IEEE Transactions on
  Information Theory 28(2):129-137. DOI 10.1109/TIT.1982.1056489.
- Arkin, E.M., Chew, L.P., Huttenlocher, D.P., Kedem, K., Mitchell, J.S.B.
  (1991). An efficiently computable metric for comparing polygonal shapes.
  IEEE Transactions on Pattern Analysis and Machine Intelligence 13(3):209-216.
  DOI 10.1109/34.75509.
- Sawhney, R., Crane, K. (2017). Boundary first flattening. ACM Transactions
  on Graphics 36(4):109. DOI 10.1145/3072959.3056432.
