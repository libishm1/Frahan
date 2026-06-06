# Frahan StonePack — Component Decomposition Plan

**Location:** `wiki/specs/component_decomposition_plan.md`
**Status:** canonical · authored 2026-05-31 per Libish directive:
*"How do we split this component silos (instead of having 9 different
complete solver components) into parts that can be used across
workflows instead of making large components? Don't make stub
components with no upstream or downstream workflow connections,
especially don't make report components with no upstream connections,
and make the reports usable by other downstream components."*
**Authority anchors:** `wiki/specs/frahan_design_philosophy.md` §10;
`wiki/specs/cathedral_scale_stone_fitting_plan.md` §4; structuralCircle
dossier at `Template-General/outputs/2026-05-31/research/structural_circle/findings.md`.

## §0. The thesis in one breath

Frahan's monolithic "complete" solver components (EdgeMatch Solve,
Pack2DTrencadis variants, Pack3DIrregular, Ashlar Pack, BlockCutOpt
Solve, BlockPackTree, KintsugiAssembly, Voussoir Pack, etc.) are
black boxes. Each subsumes 4-6 algorithmic steps that exist as
internal methods, not as canvas-wireable components. Users cannot
recompose the steps for a different workflow (e.g. use BlockCutOpt's
key-block backtrack with EdgeMatch's Hungarian assignment), cannot
swap the matching algorithm (Greedy → Hungarian → MILP), and cannot
inject custom cost functions.

**The structuralCircle / Tomczak 2023 paper (DOI 10.1088/2634-4505/acf341,
Figure 2) shows a better pattern**: decompose each "matching solver" into
five wireable stages — Inputs → Pre-process (Incidence matrix N) →
Weighted matrix C → Matching algorithm (Greedy / MaxBM / MIP / GA) →
Output pairs. Different workflows reuse the same primitives in different
configurations.

This document proposes the equivalent decomposition for Frahan: ~30-40
**primitives** that compose the existing ~19 monolithic solvers. Each
primitive is a small GH component with explicit upstream + downstream
wiring. Sample `.gh` files (Libish-supplied) demonstrate the
compositions; users freely recombine for novel workflows.

## §1. The structuralCircle canonical pattern (the reference)

Tomczak, Haakonsen, Luczkowski 2023 ("Matching algorithms to assist in
designing with reclaimed building elements," *Environ. Res.: Infrastruct.
Sustain.* 3:035005) describe the FIVE-STAGE pattern (their Figure 2):

```
INPUTS                  PRE-PROCESS                  MATCH                  OUTPUT
- Demand DataFrame     - Incidence matrix N_ij      - Greedy single         - Per-pair list
- Supply DataFrame     - Weighted matrix C_ij       - Greedy plural          - Total objective
- Constraint dict      (independent steps)          - MaxBM (bipartite)      - Assignment x_ij
- Objective (GWP fn)                                - MIP (HiGHS / scipy)
                                                    - Genetic (PyGAD)
```

Key design decisions in the paper that Frahan inherits:

- **Constraint dict is `{property: operator}` data**, not code. Listing 2
  of the paper: `constraint = {'Area':'>=', 'Length':'>=', 'Moment of inertia':'>='}`.
- **Incidence N is computed ONCE upstream** of any solver. All solvers
  consume N; none recompute it.
- **Weighted matrix C is separate from N**. C carries the cost (GWP
  saving = `c_ij = w_i - w_ij`, eq. 3). Solvers minimise sum(C_ij·x_ij)
  subject to N_ij = True.
- **MIP uses fixed upper-bounds (lb=0, ub=N_ij) to encode
  feasibility** — solvers don't iterate over infeasible cells.
- **`run_matching()` is a SINGLE driver** dispatching to algorithms
  by boolean flag (greedy=True / milp=True / bipartite=True). Callers
  swap algorithms without changing the rest of the pipeline.
- **Greedy + plural-assign gets within 2.5 % of MIP optimum at 100×
  the speed**. Production default = greedy-plural; MIP is the oracle.

Frahan's analogue per the cathedral plan §3 and `findings.md` §8:

| structuralCircle | Frahan analogue (status) |
|---|---|
| Demand DataFrame | List of `Template` records (proposed v1.x typed record) |
| Supply DataFrame | List of `QuarryBlock` (✓ shipped v1.0-rc1) |
| Constraint dict `{property: operator}` | `ConstraintDictionary` typed record (proposed v1.x) |
| Objective function | `IScoreFunction` interface + plug-in implementations |
| Incidence matrix N | `IncidenceMatrixComponent` (proposed) |
| Weighted matrix C | `CostMatrixComponent` (proposed) |
| `run_matching()` driver | `MatcherRegistry` + `RunMatchingComponent` (proposed) |
| Greedy / MaxBM / MIP / GA | one component per strategy (4 components proposed) |
| Output pairs DataFrame | `AssignmentResult` typed record + downstream consumers |

## §2. The monolithic-solver inventory — what to decompose

Frahan currently has ~19 monolithic solvers. Each subsumes 4-6 stages:

| # | Solver | Domain | Internal stages |
|---|---|---|---|
| 1 | `EdgeMatchSolveComponent` (D5F10001) | 2D Trencadís | (1) segment → (2) hash index → (3) phase correlate → (4) ICP → (5) beam assembly |
| 2 | `TrencadisEdgeMatchComponent` | 2D Trencadís | preprocess → CVD → match → assemble |
| 3 | `Pack2DTrencadisComponent` | 2D mosaic | CVD → matching → packing |
| 4 | `Pack2DTrencadisCatalogComponent` | 2D mosaic | catalog + CVD |
| 5 | `Pack2DTrencadisDynamicComponent` | 2D mosaic | dynamic tile + CVD |
| 6 | `Pack2DTrencadisPipelineComponent` | 2D mosaic | pipeline composition |
| 7 | `Pack2DIrregularSheetComponent` | 2D nesting | NFP → cost → BL-fill |
| 8 | `Pack2DBottomLeftComponent` | 2D nesting | BL heuristic |
| 9 | `NfpPack2DComponent` | 2D nesting | NFP-pack composition |
| 10 | `IrregularSheetFillComponent` | 2D nesting | NFP + Bennell-Oliveira pipeline |
| 11 | `KintsugiAssemblyComponent` | 3D reassembly | PuzzleFusion++ pipeline |
| 12 | `AshlarPackComponent` | 3D masonry | course-by-course running-bond |
| 13 | `BestFitPackComponent` | 3D masonry | inventory-aware ashlar |
| 14 | `Pack3DIrregularComponent` | 3D packing | DLBF pipeline |
| 15 | `Pack3DIrregularContainerComponent` | 3D packing | TreePack pipeline |
| 16 | `Pack3DMeshHeightmapComponent` | 3D packing | heightmap pipeline |
| 17 | `BlockPackTreeComponent` | 3D packing | TreePack standalone |
| 18 | `RubbleWallSettleComponent` | 3D physics | drop → settle → equilibrium |
| 19 | `BlockCutOptSolveComponent` | Block cut | Elkarmoty + Goodman key-block pipeline |

Plus the **3D EdgeMatch family added 2026-05-31** (B3D / A3D / C3D / D3D)
which is ALREADY split into 4 components — this is the *correct* pattern
the rest of the codebase should evolve toward.

## §3. The decomposition pattern — 5 universal stages

Every Frahan workflow decomposes into the same 5 stages
(mirroring structuralCircle Figure 2 + Frahan EdgeMatching §1):

### §3.1 Stage 1 — INGEST / SEGMENT

Convert raw input into typed records the matcher can consume.
Currently:

- `Scan to Block Inventory` (F2D0BC20-…) — Mesh → `QuarryBlock`. ✓
- `EdgeMatch Segments` (`D5F10002`) — Curve → `Segment[]`. ✓
- `VsaSegmenter` (proposed Core utility) — Mesh → `Patch[]`. Stub today.
- `Voussoir Ingest` (proposed, D5F1000E) — Voussoir-plugin output → `Template[]`.

### §3.2 Stage 2 — INCIDENCE (feasibility filter, N_ij)

For each (template, candidate) pair, decide if the candidate is
GEOMETRICALLY / MATERIALLY feasible. Boolean matrix output.

NEW components proposed:

- `Incidence Matrix Builder` — generic; takes demand/supply lists + a
  `ConstraintDictionary` and emits N_ij as an `IncidenceMatrix` typed
  record.
- `OBB Containment Filter` — per-pair test for "stone contains
  template + margin." Implementation already in
  `MeshTemplateMatchComponent` (D5F1000D, shipped); extract to a
  primitive.
- `Fracture-Aware Filter` — per-pair test for "no fracture crosses
  template load path." Uses BlockCutOpt v2 primitives.
- `Grain-Aware Filter` — per-pair test for "stone bed-plane aligns
  with template load axis."

Each is a small component with one boolean output per (i, j) pair.

### §3.3 Stage 3 — WEIGHT (cost matrix, C_ij)

For each (template, candidate) pair that PASSED Stage 2, compute the
scalar cost. Multiple cost terms can be weighted-summed into C.

NEW components proposed:

- `Cost Matrix Builder` — generic; consumes a feasibility matrix N +
  one or more cost-term components and emits the weighted matrix C.
- `Yield Cost Term` — `1 − template_vol / stock_vol`.
- `Grain Alignment Cost Term` — `angle(stone.bed_normal, template.load_axis) / π`.
- `Carving Cost Term` — `(stock_vol − template_vol) / template_vol`.
- `Hausdorff Cost Term` — geometric residual after best-fit.

Each cost-term component is small + standalone. Users wire many or few.

### §3.4 Stage 4 — MATCH (the actual algorithm)

The MatcherRegistry pattern. ONE driver, multiple plug-in algorithms:

- `Match Greedy` — sorts cost matrix, walks demand assigning best
  remaining supply. Already in `HungarianAssigner.cs` (greedy fallback
  path); extract to its own component.
- `Match Hungarian` — Kuhn 1955 O(N³). Already in `HungarianAssigner.cs`;
  extract to its own component.
- `Match Bipartite (igraph)` — `maximum_bipartite_matching` analogue
  (port from structuralCircle if needed; or use C# graph lib).
- `Match MILP` — proposed v1.x via `Google.OrTools` NuGet.
- `Match NSGA-II Pareto` — proposed v1.x for multi-objective.

Inputs: `IncidenceMatrix N + CostMatrix C + Strategy`. Outputs:
`AssignmentResult` typed record with `Pair[]`, `TotalCost`,
`Unassigned[]`, `Unused[]`.

### §3.5 Stage 5 — REFINE + EXPORT

Post-assignment refinement + per-pair geometric output:

- `Soft ICP 3D` (proposed today, D5F1000E) — STANDALONE component.
- `Constrained ICP 3D` — wrapper around existing `ConstrainedIcp3D`.
- `Apply Assignment` — consumes AssignmentResult + Stock list + Template
  list; emits `PlacedAssembly` typed record.
- `Assembly to Mesh` — flatten PlacedAssembly → Mesh list for GH output.
- `Carving Plan` — wrapper around BlockCutOpt for per-pair cuts.
- `Build Order Sequencer` — wrapper around Kim 2024 sequencing.
- `Stone-Aware Cut Export` (existing) — consumes PlacedAssembly + metadata.
- `Fabrication Prep Report` (existing) — consumes PlacedAssembly + weights.

**Critical:** Stage 5 reports CONSUME upstream typed records. No orphan
reports (see §6).

## §4. Sample workflow compositions

Three example workflows showing the SAME primitives wired differently:

### §4.1 Workflow A — Voussoir → Stone (top-down)

```
[Architect Rhino Model: voussoir set]
            |
            v
[Voussoir Ingest]                                  [Scan to Block Inventory] (existing)
   |                                                       |
   v                                                       v
[Templates: Template[]]                            [Stock: QuarryBlock[]]
            \\                                           //
             v                                          v
            [ConstraintDictionary: Volume>=, MaxDim>=, FracTrue==None]
                              |
                              v
                  [Incidence Matrix Builder] -> N_ij
                              |
                  +-----------+-----------+
                  v                       v
        [OBB Contain Filter]      [Fracture-Aware Filter]
                  |                       |
                  +-----------+-----------+
                              v
                            N_ij (combined)
                              |
                              v
                    [Cost Matrix Builder]
                       /     |       \\
                      v      v        v
              [Yield Cost] [Grain Cost] [Carving Cost]
                       \\     |       //
                              v
                            C_ij
                              |
                              v
                [Match Hungarian]   <- Strategy = Hungarian (default)
                              |
                              v
                      [AssignmentResult]
                              |
                              v
                    [Soft ICP 3D]            <- per-pair refinement
                              |
                              v
                     [Apply Assignment]
                              |
                              v
                    [PlacedAssembly]
                       /      |       \\
                      v       v        v
              [Carving Plan]  [Stone-Aware Cut Export]   [Fab Prep Report]
              (BlockCutOpt)
```

Every block above is a SMALL component. The same `Incidence Matrix
Builder` + `Cost Matrix Builder` + `Match Hungarian` chain appears in
workflows B and C below.

### §4.2 Workflow B — Trencadís 2D (bottom-up)

```
[Boundary curve]   [Shard curves]
        |             |
        v             v
[EdgeMatch Segments] [EdgeMatch Segments]
        |             |
        v             v
       Segs_A         Segs_B (per shard)
                       |
                       v
            [Constraint: PlanarMatch == True]
                       |
                       v
            [Incidence Matrix Builder] -> N_ij
                       |
                       v
            [Cost Matrix Builder w/ Hausdorff cost]
                       |
                       v
            [Match Greedy]    <- Strategy = Greedy (Trencadís default)
                       |
                       v
              [AssignmentResult]
                       |
                       v
            [Apply Assignment 2D]
                       |
                       v
            [Trencadis Assembly Output]
```

Note this is THE SAME pipeline as Workflow A, but with 2D segments
instead of 3D blocks and Greedy instead of Hungarian. The
Incidence/Cost/Match nodes are unchanged.

### §4.3 Workflow C — Cyclopean Cannibalism (recipe-driven bottom-up)

```
[Rubble inventory: Mesh[]]
        |
        v
[VsaSegmenter] (per stone) -> Patch[]
        |
        v
[Shape Classifier] -> {trapezoids, parallelograms} bins
        |
        v
[Wall Envelope: Brep]
        |
        v
[Cyclopean Recipe Coursing] -> course-by-course state machine
        |
        v
   (per course)
        |
        v
[Adaptive Block Match 3D] -> overlap-then-carve trim (CGAL diff)
        |
        v
[Soft ICP 3D] -> per-stone final pose refinement
        |
        v
[PlacedAssembly]
   /      |       \\
  v       v        v
[Build Order]  [Stone-Aware Cut Export]  [Fab Prep Report]
(Kim 2024)
```

The recipe-driven workflow doesn't run Hungarian — the recipe's
trapezoid → parallelogram → keystone sequence IS the assignment. But
it consumes the same `VsaSegmenter` primitive and the same downstream
`Build Order` / `Cut Export` / `Fab Prep` primitives as Workflow A.

## §5. Proposed primitives — the full list

~35 new + ~10 existing-that-need-extraction. Implementation phases per
the structuralCircle "greedy-first MILP-as-oracle" cadence:

### §5.1 Stage 1 INGEST (5 components)

| Component | Status | GUID (proposed) |
|---|---|---|
| Scan to Block Inventory | ✓ shipped | F2D0BC20-… |
| EdgeMatch Segments | ✓ shipped | D5F10002 |
| VsaSegmenter (GH wrapper) | proposed v1.x | D5F10011 |
| Voussoir Ingest | proposed v1.x | D5F1000F |
| Mesh to Template | proposed v1.x | D5F10012 |

### §5.2 Stage 2 INCIDENCE (5 components)

| Component | Status | GUID |
|---|---|---|
| Constraint Dictionary | proposed v1.x | D5F10013 |
| Incidence Matrix Builder | proposed v1.x | D5F10014 |
| OBB Containment Filter | proposed v1.x | D5F10015 |
| Fracture-Aware Filter | proposed v1.x | D5F10016 |
| Grain-Aware Filter | proposed v1.x | D5F10017 |

### §5.3 Stage 3 WEIGHT (6 components)

| Component | Status | GUID |
|---|---|---|
| Cost Matrix Builder | proposed v1.x | D5F10018 |
| Yield Cost Term | proposed v1.x | D5F10019 |
| Grain Alignment Cost Term | proposed v1.x | D5F1001A |
| Carving Cost Term | proposed v1.x | D5F1001B |
| Hausdorff Cost Term | proposed v1.x | D5F1001C |
| Custom Cost (script) | proposed v1.x | D5F1001D |

### §5.4 Stage 4 MATCH (5 components — the MatcherRegistry)

| Component | Status | GUID |
|---|---|---|
| Match Greedy | proposed v1.x (Greedy single + plural) | D5F1001E |
| Match Hungarian | proposed v1.x (extract from HungarianAssigner) | D5F1001F |
| Match Bipartite | proposed v1.x | D5F10020 |
| Match MILP | proposed v1.x (Google.OrTools NuGet) | D5F10021 |
| Match NSGA-II Pareto | proposed v1.x (Deb 2002) | D5F10022 |

### §5.5 Stage 5 REFINE + EXPORT (8 components)

| Component | Status | GUID |
|---|---|---|
| **Soft ICP 3D** | **shipping today** | **D5F1000E** |
| Constrained ICP 3D | proposed v1.x | D5F10023 |
| Apply Assignment | proposed v1.x | D5F10024 |
| Assembly to Mesh | proposed v1.x | D5F10025 |
| Carving Plan | wrapper around BlockCutOpt v2 (existing) | D5F10026 |
| Build Order Sequencer | wrapper around Kim 2024 (existing inside BlockBuildOrder) | D5F10027 |
| Stone-Aware Cut Export | ✓ shipped (existing) | StoneCutExport GUID |
| Fabrication Prep Report | ✓ shipped (existing) | FabPrep GUID |

### §5.6 Cross-cutting (2 components)

| Component | Status | GUID |
|---|---|---|
| Run Matching (driver) | proposed v1.x — calls Match*; mirrors structuralCircle `run_matching()` | D5F10028 |
| Strategy Switch | proposed v1.x — picks algorithm based on M*N size + time budget | D5F10029 |

### §5.7 Monolithic solvers — KEEP as workflow conveniences

Per Libish 2026-05-31: *"maybe the full solvers is a good choice in
some cases to avoid component bloat."* The 19 existing monolithic
solvers stay in the codebase as **convenience compositions** for
common workflows. Internally they call the new primitives. Users who
want a one-component Trencadís solve grab EdgeMatch Solve; users who
want to inject a custom cost function build their own canvas from the
primitives.

## §6. Orphan reports — the discipline

Per Libish 2026-05-31: *"don't make report components with no upstream
connections and make the reports usable by other downstream components."*

### §6.1 Audit (current state, 2026-05-31)

| Report component | Upstream consumer | Downstream consumer | Status |
|---|---|---|---|
| `PackingReportComponent` | `PackResult` from Pack3D (wired step 45) | none directly; emits text panel | ⚠ has upstream, no downstream |
| `PackingPlanReportComponent` | wired into 3D pack workflows | none | ⚠ orphan-out |
| `MeshDiagnosticsComponent` | Mesh inputs | none | ⚠ orphan-out |
| `PackDiagnosticsComponent` | MasonryAssembly | none | ⚠ orphan-out |
| `MeshQualityReportComponent` | Mesh inputs | none | ⚠ orphan-out |
| `ChartFlatnessReportComponent` | Surface Chart output | none | ⚠ orphan-out |
| `FabricationPrepReportComponent` | PlacedAssembly (typed) | downstream Stone-Aware Cut Export reads same input independently | ⚠ duplication |
| `BlockCutOptInspectorComponent` | BlockCutOpt output | none | ⚠ orphan-out |

### §6.2 Discipline

**New rule** (extends `[[feedback_hitl_cards_design_grounded]]`):

> *Every Report component must emit a TYPED RECORD output that at
> least one downstream component consumes. If the report is purely
> visual (text panel for human eyes), it must also emit the underlying
> typed record so other components can chain.*

Concrete pattern: each Report component gets a SECOND output —
the typed `Report` record — alongside its existing text-panel output.
Downstream components (`Stone-Aware Cut Export`, `Fab Prep`, future
`Final Assembly Validator`) consume the typed record.

### §6.3 Fix plan (v1.x)

For each of the 8 orphan reports:

1. Add a second output of type `<Report-specific> Report` typed record.
2. Audit downstream — identify or create one consumer per record.
3. Lab-gate any report that genuinely has no downstream after audit.

Concrete first fix shipping today: `FabricationPrepReportComponent`
gets a `FabricationPrepReport` typed-record output so the proposed
`Apply Assignment` and `Stone-Aware Cut Export` can chain off it.

## §7. Implementation order

Per the structuralCircle "greedy-first MILP-as-oracle" cadence + Libish
2026-05-31 *"start implementing in order, use subagents to delegate
research."*

### §7.1 Phase 1 — substrate (this nightshift + next session)

1. ✓ `MeshTemplateMatchComponent` (D5F1000D) — simple PolytopeSolutions
   idiom, shipped 2026-05-31.
2. ✓ `HungarianAssigner.cs` — Kuhn 1955 real implementation, shipped
   2026-05-31.
3. **Soft ICP 3D component** (D5F1000E) — wrap existing
   `SoftIcpRefiner.Refine3D`. Shipping today.
4. **ConstraintDictionary + IncidenceMatrix + CostMatrix typed records**
   in `Frahan.EdgeMatching.Core`. Spec lands today; impl follows.

### §7.2 Phase 2 — Stage 1 + 2 + 3 components (Q3 2026)

5. `Constrained ICP 3D` component (wrap existing `ConstrainedIcp3D`).
6. `OBB Containment Filter`, `Fracture-Aware Filter`, `Grain-Aware Filter`.
7. `Yield Cost`, `Grain Cost`, `Carving Cost`, `Hausdorff Cost`.
8. `Incidence Matrix Builder`, `Cost Matrix Builder`.

### §7.3 Phase 3 — Stage 4 matcher registry (Q4 2026)

9. `Match Greedy` + `Match Hungarian` + `Match Bipartite` (port
   structuralCircle algorithm structure).
10. `Run Matching` driver (mirrors structuralCircle `run_matching()`).
11. `Match MILP` (Google.OrTools NuGet).
12. `Match NSGA-II Pareto` (Deb 2002).

### §7.4 Phase 4 — algorithm-deep STUB flesh-out

13. VsaSegmenter Lloyd-iteration body (per Cohen-Steiner 2004 dossier — agent in flight).
14. B3D full ConstrainedIcp3D pipeline.
15. A3D bidirectional walker + NSGA-II Pareto strategy.
16. C3D CGAL/Geogram trim path (per dossier — agent in flight).
17. Cyclopean Recipe Coursing body (8-step state machine).
18. Voussoir Ingest + Stone Matcher.

### §7.5 Phase 5 — orphan-report fix sweep

19. Per §6.3, add typed-record output to each of the 8 orphan reports,
    wire one downstream consumer each, Lab-gate any genuine dead-ends.

### §7.6 Phase 6 — 16 P1+P2 HITL card-sets

20. Per `wiki/specs/hitl_cards_master_plan.md` §5.1. Each card-set
    validates a primitive + a composition.

## §8. References

- Tomczak, Haakonsen, Luczkowski (2023). *"Matching algorithms to
  assist in designing with reclaimed building elements."* Environ.
  Res.: Infrastruct. Sustain. 3:035005. DOI 10.1088/2634-4505/acf341.
  Open access. **The canonical reference for this plan**; Figure 2 is the
  five-stage pattern Frahan inherits.
- structuralCircle code dive (in flight 2026-05-31, subagent):
  `Template-General/outputs/2026-05-31/research/structural_circle/code_dive.md`
  (forthcoming).
- VSA Lloyd-iteration research (in flight, subagent):
  `Template-General/outputs/2026-05-31/research/vsa_lloyd/vsa_implementation_plan.md`.
- CGAL trim + NSGA-II dossier (in flight, subagent):
  `Template-General/outputs/2026-05-31/research/cgal_trim_nsga2/dossier.md`.
- PolytopeSolutions `MatchMeshTransformation` (the simple-component
  inspiration), DLL at `D:/code_ws/PolytopeSolutions_GrasshopperTools.dll`.
- `wiki/specs/cathedral_scale_stone_fitting_plan.md` (the
  workflow-level companion).
- `wiki/specs/frahan_design_philosophy.md` §10 (the architectural anchor).
- Memory anchors: `[[feedback_hitl_cards_design_grounded]]`,
  `[[feedback_reuse_dont_duplicate_components]]`,
  `[[feedback_lab_not_an_island]]`.

## §9. Last updated

2026-05-31 — initial authorship. Soft ICP 3D component (D5F1000E)
shipping today as the first new primitive. Phase 1 substrate continues
through next session.
