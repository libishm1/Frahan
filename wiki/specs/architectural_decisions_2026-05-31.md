# Frahan StonePack — Architectural Decisions, 2026-05-31

**Location:** `wiki/specs/architectural_decisions_2026-05-31.md`
**Status:** canonical · authored 2026-05-31 per Libish directive:
*"Reusable primitives help me make an architectural decision to find
the right balance based on design precedents and interdisciplinary use
for other workflows."*
**Companions:** `wiki/specs/component_decomposition_plan.md` (the
broader proposal); the three research dossiers at
`Template-General/outputs/2026-05-31/research/{structural_circle,vsa_lloyd,cgal_trim_nsga2}/`.

## §0. Why this document exists

Three research dossiers (structuralCircle code dive, VSA Lloyd-iteration,
CGAL trim + NSGA-II) landed 2026-05-31. They show CONCRETE design
patterns from PhDs who have already solved analogous problems. This
document locks ten architectural decisions that the dossiers
collectively justify — so the v1.x build doesn't drift back into
unjustified ad-hoc choices.

Each decision below has the form: **the decision** + **the dossier
evidence** + **the practical consequence for Frahan**.

## §1. Decision 1 — Adopt the PhD class/helper boundary verbatim

**Decision:** Frahan's Stage 4 matching layer is shaped as a
`MatcherContext` typed record (the canonical state) + `MatcherUtils`
static class (free functions) + `ISolver` interface (the seven solver
implementations). One C# triple, mirroring structuralCircle's
Matching/matching.py + helper_methods.py + per-solver method split.

**Evidence (structuralCircle code dive §1):** *"Everything that
operates on paired (demand, supply, constraints, score_fn) state lives
as a method on Matching; everything reusable across instances lives in
helper_methods.py as a free function. That split is the single most
copyable decision in the repo."*

**Consequence:** `MatcherContext` carries Demand[], Supply[],
Constraints, IncidenceMatrix, WeightMatrix, Pairs, Result. Solvers
fill `Pairs`; they don't reset, don't time, don't log. Future contributors
who add a new solver implement `ISolver` only — they don't touch the
context, the helpers, or the registry.

## §2. Decision 2 — Reject structuralCircle's 11-boolean-flag dispatcher

**Decision:** Frahan's matching dispatcher is
`Dictionary<string, ISolver>` keyed by name. Callers pass
`IEnumerable<string> activeSolvers` (e.g.
`["greedy", "hungarian"]`). NEVER an 11-boolean-flag signature.

**Evidence (code dive §2):** *"`run_matching()` is the dispatcher.
Body is a flat sequence of `if greedy_single: matching.match_greedy(...)`.
There is no strategy registry. Eleven boolean flags hardcoded. That is
the single biggest design mistake in the file. The author's own TODO at
line 657 admits it: `#TODO Can **kwargs be used instead of all these
arguments`."*

**Consequence:** Adding a new solver in Frahan adds one entry to the
registry dictionary, not a new boolean parameter. Callers select by
string name. The TODO in structuralCircle is closed in Frahan from
day one.

## §3. Decision 3 — Adopt the @_matching_decorator pattern in C#

**Decision:** `MatcherRegistry.Run(solverName, context)` is the
single entry point. It owns: reset state → start timer → dispatch to
solver → compute final score → log → return paired result. Solvers
implement `ISolver.Match(ctx)` only.

**Evidence (code dive §2):** verbatim Python decorator at lines 226-247:

```python
def _matching_decorator(func):
    def wrapper(self, *args, **kwargs):
        start = time.time()
        self.result = 0
        self.pairs = pd.DataFrame(...)
        func(self, *args, **kwargs)
        self.calculate_result()
        end = time.time()
        self.solution_time = round(end - start, 3)
        return [self.result, self.pairs]
    return wrapper
```

The PhD pattern: every solver gets the same wrapper-managed side
effects. No solver duplicates timing / reset / score logic.

**Consequence:** Frahan's `MatcherRegistry` is the C# equivalent of
this decorator. Every `ISolver` is ~20-50 lines of pure assignment
logic; the wrapper is ~30 lines once.

## §4. Decision 4 — Separate incidence and weight matrices

**Decision:** Frahan computes the **boolean** incidence matrix N
ONCE upstream of the solver (per `ConstraintDictionary`), then the
**float** weight matrix W lazily and ONLY on incidence-True cells.
Two separate typed records: `IncidenceMatrix` (bool[,]) and
`WeightMatrix` (double[,] with NaN sentinels).

**Evidence (code dive §4 + Tomczak 2023 paper §4.3):** Listing 2's
constraint dict drives `evaluate_incidence()` → bool matrix. Then
`evaluate_weights()` only fills the True cells:
`weights = np.full(self.incidence.shape, np.nan); ... weights[incidence] = ...`.

The dual `weights / weights_transport` pattern (transport cost
alongside GWP cost) shows how Frahan can add SECONDARY objectives
(e.g. carving cost alongside yield cost) by carrying parallel
weight matrices.

**Consequence:** Solvers consume `(N, W)` together. MILP especially
benefits — it fixes `x_ij` upper-bound = 0 where N_ij = False
(paper §4.7), bypassing infeasible cells entirely. The greedy path
skips cells where N_ij = False before computing scores.

## §5. Decision 5 — Numeric vs categorical constraints split up front

**Decision:** `ConstraintDictionary` distinguishes numeric and
categorical constraints at the TYPE LEVEL. Numeric: `Dictionary<string,
Func<double, double, bool>>` keyed by operator string. Categorical:
`Dictionary<string, Func<string, string, bool>>`.

**Evidence (code dive §3):** structuralCircle's
`evaluate_incidence()` lines 178-184:

```python
if isinstance(demand_array[0], str):
    bool_col = np.array(eval(f"['{var}' {compare} x for x in demand_array]"))
else:
    bool_col = ne.evaluate(f'{var} {compare} demand_array')
```

The runtime isinstance branch is a code smell — type-level distinction
is cleaner and safer.

**Consequence:** Frahan stone constraints split cleanly:
`{Volume: ">=", MaxDimension: ">=", Weight: "<="}` (numeric) +
`{ColorFamily: "==", LithologyClass: "=="}` (categorical). Each
operator is a typed delegate; no `eval(string)` invocations
(also rejected as anti-pattern §10).

## §6. Decision 6 — REJECT structuralCircle's monolithic GH UX, INVERT it

**Decision:** Frahan's Grasshopper UX is MANY SMALL components, each
~150-300 LoC, with explicit upstream / downstream typed-record wiring.
The algorithm lives in C# via `Google.OrTools` NuGet, NOT shelled out
to Python.

**Evidence (code dive §6, verbatim):** *"Verdict on prompt's
component-decomposition question: the production Grasshopper UX is
ONE monolithic component (`MappingBeta`) that JSON-serialises
everything and shells out to Python. C# does pure transport. That
is a design mistake — Frahan inverts it (algorithm in C# via
`Google.OrTools` NuGet, GH UX as many small components per pipeline
stage)."*

**Consequence:** This is the **strongest single dossier finding**.
structuralCircle's `MappingBeta.cs` C# component is pure transport
+ JSON serialisation; the actual matching runs in a Python subprocess.
That breaks debuggability, breaks GH determinism, ties UX to a Python
install. Frahan inverts. The MatcherRegistry + ISolver lives in
`Frahan.StonePack.Core` (C#). Each pipeline stage gets its own
GH component. Users debug on the canvas without leaving Rhino.

This is the architectural decision the user's "interdisciplinary
use for other workflows" question demands: timber reuse, concrete
rubble, ceramic mosaic, stone fabrication can all be authored on
the SAME canvas using the same primitives wired differently —
which they cannot do if every workflow shells out to Python.

## §7. Decision 7 — The number-of-primitives balance (the central call)

**Decision:** **~30-35 primitive components, 5 stages, with the 19
monolithic solvers retained as "convenience compositions"** that
internally call the primitives. Specifically:

| Stage | Primitive count | Existing monoliths |
|---|---|---|
| 1 Ingest | 5 | Scan to Block Inventory, EdgeMatch Segments, Voussoir Ingest, VsaSegmenter wrapper, Mesh to Template |
| 2 Incidence | 5 | Constraint Dictionary, Incidence Matrix Builder, OBB / Fracture / Grain filters |
| 3 Weight | 6 | Cost Matrix Builder + 4 cost-term primitives + Custom Cost script |
| 4 Match | 5 | Match Greedy / Hungarian / Bipartite / MILP / NSGA-II |
| 5 Refine + Export | 8 | Soft ICP 3D (✓ shipped), Constrained ICP 3D, Apply Assignment, Assembly to Mesh, Carving Plan, Build Order Sequencer, Stone-Aware Cut Export (✓), Fab Prep Report (✓) |
| Cross-cut | 2 | Run Matching driver, Strategy Switch |
| **Total primitives** | **31** | |

**Plus** 19 monolithic solvers retained — `EdgeMatch Solve`, all
Pack2DTrencadis variants, `AshlarPack`, `BestFitPack`, `KintsugiAssembly`,
`Pack3DIrregular*`, `RubbleWallSettle`, `BlockCutOpt Solve`, etc.

**Evidence:** structuralCircle has 7 solver methods + ~13 free
functions = ~20 algorithmic units (code dive §1). Their GH layer is
1 monolithic component (the mistake §6). Frahan's count (31 primitives
+ 19 monolithic compositions) lets users pick either composition style
per workflow — small components for novel workflows, big components
for quick prototyping.

**Consequence:** A designer asking "build me a Trencadís floor"
grabs ONE `EdgeMatch Solve` component (the monolithic shortcut). A
designer asking "I want a Trencadís floor with vein-flow weighting
AND a Hungarian-not-Beam solver AND export to a specific CAM" composes
~8 small primitives. Both authoring styles supported.

The right "balance" is **not 30 OR 19 — it's BOTH**. Primitives serve
power users + custom workflows + interdisciplinary reuse. Monoliths
serve novice users + common workflows + fast prototyping.

## §8. Decision 8 — Reports are asymmetric — typed records IN + raw outputs OUT

**Decision:** Every Report component CONSUMES a typed record (the
discipline). Inside, it can recompute statistics or visualisations
from raw inputs as needed. Outside, it emits BOTH a text panel
(human-readable) AND a typed-record output (machine-consumable
downstream).

**Evidence (code dive §5):** structuralCircle's PDF report
(`helper_methods_PDF.py`) consumes structured `Match.result / .pairs
/ .demand / .supply` records. The plotting layer
(`helper_methods_plotting.py`) recomputes from raw DataFrames. The
PhDs deliberately KEPT this asymmetric — reports lock to the
canonical match-result shape; plots stay flexible to ad-hoc analyses.

**Consequence:** Frahan's 8 orphan reports
(`PackingReport`, `PackingPlanReport`, `MeshDiagnostics`,
`PackDiagnostics`, `MeshQualityReport`, `ChartFlatnessReport`,
`FabricationPrepReport`, `BlockCutOptInspector`) each gain a typed-
record output. Downstream `Stone-Aware Cut Export`, `Apply
Assignment`, future `Final Assembly Validator` consume them. No
orphan-out reports remain after the v1.x sweep. **Plotting / preview
components still recompute from raw inputs** — that's the documented
asymmetry, not a design failure.

## §9. Decision 9 — Interdisciplinary use: the four canonical compositions

**Decision:** Frahan ships sample `.gh` files (Libish-supplied) that
demonstrate the SAME 31 primitives wired for FOUR canonical
interdisciplinary use cases. Other disciplines plug in by editing
the constraint dict + the cost terms — the matching engine stays
unchanged.

**Tolerance-citation discipline:** Per AGENTS.md §9, every tolerance
in §9.1-§9.4 below traces to a verifiable source OR is explicitly
flagged "Frahan-internal." The full audit lives at
`wiki/research/tolerance_discipline/per_discipline_tolerances.md`
(2026-05-31 sub-agent dossier).

**The four canonical compositions:**

### §9.1 Stone fabrication (Frahan's primary)

Scanned quarry blocks → voussoir templates → Hungarian match →
BlockCutOpt → Stone-Aware Cut Export.

- Numeric constraints: `Volume >=, MaxDimension >=`. Categorical:
  `LithologyClass ==, ColorFamily ==`. Cost: yield + grain + carving.
- **Cited tolerances**: UCL Devadass 2025 §2.3 reports 0.2 mm scan
  resolution + 15,000 mm² face threshold + 13.4 mm tool. Quarra MIT
  Out of Frame 2025: 0.5 → 0.15 mm flip-registration (62 %
  reduction, City Jeff §10), 0.1-0.2 mm field tolerance via Leica
  laser tracker (Emanuel 9 §15.6), 20 µm/400 ft instrument spec,
  1/16 inch anchor tolerance.
- **Frahan-internal (NOT directly cited; flag honestly)**:
  - "50 mm endpoint deviation at 2.5 m span" — UCL Devadass treats
    endpoint deviation as a MOO OBJECTIVE without a fixed threshold;
    Frahan picks 50 mm as a 2 % engineering tolerance based on the
    built arch's reported endpoint behaviour.
  - "Cg ≤ 30 mm per stone" — same gap; Frahan-internal.
  - "Joint Hausdorff ≤ 2 mm" — Frahan-internal; defensible as 100×
    UR10e repeatability margin but not directly cited in any
    precedent paper.
  - "Carving ≤ 30 % of inventory" — derived by inversion from
    Clifford-McGee 2017 reported 73 % yield; cross-discipline
    transfer flagged.

### §9.2 Timber reuse (structuralCircle's primary)

Reclaimed timber stock → designed structural members → MILP
(Google.OrTools) → GWP minimisation.

- Numeric: `Length >=, Area >=, Inertia >=` (Tomczak 2023 Listing 2
  verbatim). Categorical: `Species ==, MoistureClass ==`.
- **Cited tolerances** (Tomczak 2023, all verbatim):
  - `k_new = 28.9 kgCO2eq/m³` and `k_reuse = 2.25 kgCO2eq/m³`
    (eq. 1).
  - Reference: 56.1 kgCO2eq for new-only baseline (§4.3).
  - MIP optimum: 14.01 kgCO2eq = 75 % GWP reduction vs new (§4.7).
  - Greedy-plural: within 2.5 % of MIP at 100× speed (§5).
- **Frahan-internal**:
  - "MILP timeout 120 s" — Tomczak 2023 paper uses
    `solution_limit=120` as default in `run_matching()` (verified in
    structuralCircle code-dive §2); honest cite.

### §9.3 Cyclopean concrete-rubble (Clifford-McGee 2017 lineage)

Demolition concrete → variable-thickness wall envelope → recipe-
driven greedy coursing → BlockCutOpt v2 trim.

- Numeric: `Mass <=`. Categorical: shape-class (trapezoid /
  parallelogram). Cost: recipe-rule satisfaction.
- **Cited tolerances** (Clifford-McGee 2017 ACADIA pp. 404-413,
  verbatim):
  - Wall dimensions 6.6 m × 2.3 m × 6,896 kg.
  - Thickness range 100-312 mm (variable).
  - 73 % material yield reported.
  - 3-inch (76 mm) dowel-hole diameter.
- **Frahan-internal**:
  - "Wall coverage ≥ 90 %" — Frahan-internal target; not a
    Clifford-McGee number.
  - "Utah-scribe Hausdorff ≤ 2 mm" — Frahan-internal; mirrors §9.1
    joint tolerance.

### §9.4 Ceramic mosaic / Trencadís (Gaudí lineage)

Ceramic-shard inventory → designed boundary → Greedy match → 2D NFP
nesting.

- Numeric: `Area >=`. Categorical: `ColorPalette ==`. Cost:
  Hausdorff joint residual.
- **Cited tolerances**: Gaudí Park Güell (1900-1914) is industry
  vernacular with no numeric tolerances. IAAC MRAC RoboMosaic
  (`blog.iaac.net/robomosaic/`) reports no numeric placement
  tolerances — verified by sub-agent (page silent on numerics).
- **Frahan-internal** (all of them):
  - "Mean joint Hausdorff ≤ 5 mm" — Frahan-internal target;
    defensible as approximate Gaudí Park Güell joint scale but not
    cited.
  - "Coverage ≥ 95 %" — Frahan-internal target.
  - "Zero overlap" — physical / structural constraint, not a
    tolerance.
  - "Beam width 16" — discrepancy flagged by sub-agent:
    `AssemblyOptions.cs:30` default is BeamWidth = 8, not 16.
    Frahan-internal; the canonical value is `AssemblyOptions`
    default (8), not the 16 mentioned in earlier drafts.

### §9.5 Cross-discipline observation: scale matters

Per `[[project_edgematch_scale_invariance]]` memory + the tolerance
dossier §5: absolute mm tolerances are NOT comparable across the
four disciplines. The four span ~7 orders of magnitude in object
size (ceramic shards 30 mm → cyclopean walls 6.6 m). A "2-5 mm
Hausdorff" is 0.03 % of object span at cyclopean scale and ~10 %
at Trencadís scale — wildly different design semantics for the
same absolute number.

Frahan's `AssemblySolver.cs:198-200` already implements scale-
relative gates at runtime; the HITL card labels currently list
tolerances in absolute mm only. **Action item**: add a "% of
object span" column to the HITL card §1 tolerance tables when
authoring the next batch of sample workflows. This makes the
discipline auditable across scales.

All four use the SAME `MatcherRegistry` + `ConstraintDictionary` +
`IncidenceMatrix` + `WeightMatrix` substrate. The disciplines differ
only in WHICH `ISolver` they pick, which COST TERMS they wire, and
which CONSTRAINTS they declare. Frahan ships components, users compose
disciplines.

**Evidence:** structuralCircle solved (2) for timber. Cathedral plan
§1 covers (1) for stone. Cyclopean Cannibalism + UCL Devadass cover
(3) for concrete rubble. RoboMosaic + Gaudí cover (4) for ceramic.
All four problems decompose to the SAME 5-stage pipeline (Tomczak
2023 Figure 2). The dossiers are the proof.

**Consequence:** Frahan's marketing (and the philosophy doc's "beat
the SoA" track 2 per §10.10) gains a concrete claim: *"one component
substrate, four disciplines, configurable per project."* That's the
**interdisciplinary balance** the user asked about.

## §10. Decision 10 — Eight anti-patterns NOT to copy

**Evidence (code dive §6):** structuralCircle's mistakes documented
verbatim. Frahan explicitly rejects each.

| Anti-pattern in structuralCircle | Frahan rejection |
|---|---|
| `eval(run_string)` — string evaluation of solver names | `Dictionary<string, ISolver>` registry; no `Type.GetType()` from string |
| Hardcoded `%APPDATA%` paths | All Frahan paths via `Frahan.Core.Paths` or relative-to-`Frahan_DLL.Location` |
| 11-boolean-flag explosion on `run_matching` | One `IEnumerable<string>` `activeSolvers` parameter |
| Double `Score.calculate_score` in `__init__` | `MatcherContext` builder runs scoring exactly once |
| Mutable default `constraints={}` | C#'s defaulted parameter on a reference type already excludes this; require `ConstraintDictionary` argument explicit |
| Commented-out fixture-generation code | Fixtures land in `hitl_cards/`; no in-source dead code |
| Retained dead code `add_graph_plural` | Lab-gate or delete; no orphan code |
| Bare `except Exception` | Specific catches per `Frahan.Core` exception hierarchy |

## §11. Decision summary table

| # | Decision | Source dossier evidence |
|---|---|---|
| 1 | Class/helper boundary verbatim | structuralCircle code dive §1 |
| 2 | Reject 11-bool dispatcher; use registry dict | structuralCircle §2 |
| 3 | `@_matching_decorator` pattern in C# | structuralCircle §2 |
| 4 | Separate Incidence (bool) + Weight (NaN-sparse) matrices | structuralCircle §4; Tomczak 2023 §4.3 |
| 5 | Type-level numeric vs categorical constraints | structuralCircle §3 |
| 6 | **Reject monolithic GH UX; invert to many small C# components** | structuralCircle §6 (strongest finding) |
| 7 | **31 primitives + 19 retained monolithic = both-and balance** | All three dossiers |
| 8 | Reports CONSUME typed records; plots recompute from raw | structuralCircle §5 |
| 9 | Four canonical interdisciplinary compositions | All three dossiers + Tomczak 2023 + cathedral plan |
| 10 | Eight named anti-patterns | structuralCircle §6 |

## §12. Implementation impact — what changes vs the decomposition plan

The decomposition plan (§5) already proposes the 31 primitives. This
decisions doc tightens the architecture for the v1.x build:

- **Phase 1 substrate** (decomposition plan §7.1) now adds:
  `MatcherContext` typed record + `MatcherUtils` static + `ISolver`
  interface + `MatcherRegistry` (mirrors structuralCircle precisely).
- **Phase 2** (Stage 1+2+3 components) inherits the type-level
  numeric/categorical constraint split from Decision 5.
- **Phase 3** (Stage 4 MatcherRegistry) is now spec-locked to
  Decisions 1+2+3+4. Pure C#, no Python subprocess (Decision 6).
- **Phase 5** orphan-report fix inherits Decision 8.

**Effort estimate adjustment**: the `MatcherRegistry` (was 3-5 days
in decomposition plan §7) is now 5-6 days with the
`ISolver` interface + 4 solver implementations + the registry
dictionary. The remaining estimates hold.

## §13. References

- structuralCircle code dive — `Template-General/outputs/2026-05-31/research/structural_circle/code_dive.md`
- structuralCircle high-level — `wiki/research/stone_cutting_optimization_soa/` (no — that's MaringReed-Bondua) — sorry, the structuralCircle high-level is `Template-General/outputs/2026-05-31/research/structural_circle/findings.md`
- VSA Lloyd-iteration — `Template-General/outputs/2026-05-31/research/vsa_lloyd/vsa_implementation_plan.md`
- CGAL trim + NSGA-II — `Template-General/outputs/2026-05-31/research/cgal_trim_nsga2/dossier.md`
- Component decomposition plan — `wiki/specs/component_decomposition_plan.md`
- Cathedral plan — `wiki/specs/cathedral_scale_stone_fitting_plan.md`
- Philosophy doc — `wiki/specs/frahan_design_philosophy.md` §10
- Tomczak 2023 paper PDF — `D:/code_ws/reference/Tomczak_2023_Environ._Res.__Infrastruct._Sustain._3_035005.pdf`

## §14. Last updated

2026-05-31 — initial authorship; locks ten architectural decisions
informed by three research dossiers.
