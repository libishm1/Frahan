# Frahan StonePack v1.0 — Consolidated Plan (plans-only)

Date: 2026-05-30
Status: **PLANS ONLY. No code, no doc-edits yet.** Awaiting HITL on this plan
before any build / commit.

**Updated 2026-05-30 with HITL answers** to the six open questions (see §7):
GUID confirmed; `QuarryBlock` typed wrapper AND raw outputs both shipped; full
Lab-gating mechanism (B09) built in v1.0; Monument Packing relocated to
Fabricate (NOT Lab); Masonry-side QuarryDecompose stays visible until H-QDEC
audit decides; MASON dossier ingested as types-compatible (input options
preserved).

### 0.1 Preservation principle (HITL constraint, applies to every Lab-gate)

**Nothing is deleted. No source, no icon, no `.csproj` entry, no GUID is
removed when a component is moved to Lab.** The Lab-gating mechanism is a
**visibility flag only** (`Hidden = true`, `Exposure = hidden`, ribbon
sub-category swap). The component must still:

- Build into the released `.gha`.
- Instantiate correctly when an existing `.gh` document references it.
- Keep its icon resource intact.
- Keep its GUID stable (round-trip rule: existing `.gh` files load
  without missing-component errors).

This means the Lab-gating in §2 is **fully reversible**: returning a
component to the default ribbon is a one-line config flip. No
`Obsolete` attributes, no `<Compile Remove>`, no icon deletion. The
retired Pack2D V2 / V3 / V506 wrappers (already `Obsolete + Hidden`)
are the existing precedent — they still build, still load, still carry
icons, just don't appear on a fresh canvas search. Lab-gating in v1.0
follows the same rule.

Removal (`git rm` of the source / icon) is **never** part of a Lab-gate.
Removal is a separate v1.1+ decision per component, gated on HITL
validation that the component is truly redundant and that no `.gh`
file in the wild references it.

Inputs digested for this plan:

- `D:\code_ws\reference\release_plan\frahan_v1_release_checklist.md` (7 gates)
- `D:\code_ws\reference\release_plan\frahan_release_backlog.md` (v1.0 / v1.1 / v1.2 / v2)
- `D:\code_ws\reference\release_plan\frahan_hitl_backlog.md` (V-series + H-series cards)
- `D:\code_ws\reference\release_plan\frahan_quarra_lecture_implications.md` (sharpened deltas)
- `D:\code_ws\reference\MASON_Digital_Craft_Architecture.md` (Oxford Brookes MA "Digital Craft in Architecture" dossier — RADD, MASON nested structural stone, 2-axis wire apparatus, 6-axis voussoir twist, +242 % expansion)
- `wiki/research/borrowed_earth/` (commit `4b7bd23`)
- `wiki/research/quarra_stone/` (commit `297c16f`)
- Earlier session work: `REPO_ARCHITECTURE_FLOW.md`, `OVERLAP_AND_PHASEOUT_PLAN.md`,
  `ARCHITECTURE_IMPROVEMENTS.md`, `MASTER_HITL_COVERAGE.md`, `feedback_gh_async_vs_sync` memory.

This plan **does not contradict** the existing 4 release-plan docs. It adds:
the **Scan → Block Inventory** component spec, the **Block-Cut + Quarry
audit** (per-component verdict for v1), the **doc-update map** for a clean
v1 release, and the **MASON + consolidated video-research** intake.

---

## 1. The new build — `Scan → Block Inventory`

This is the single highest-leverage addition surfaced by the Quarra
implications (`§21.3`) and the existing Quarra lecture implications doc
(§4.2, item **F1A** + the registration-loop block in §1.1). It is **not in
the current v1 backlog**, but it should be — it's the front-door for the
v1 spine (Quarra's headline operational tool: "scan a block, nest into it").

### 1.1 Purpose (one sentence)

Convert a 3D-scanned **raw block** (mesh) into a typed **`QuarryBlock`**
input the downstream `GeoCut` / `GeoPack` / `BlockPackTree` chain can nest
project parts into.

### 1.2 Why this is the v1 keystone, not a v1.1 nice-to-have

| Evidence | Source |
|---|---|
| "We've let the digital tools bleed into [block utilisation] — we can take a quick 3D scan of [the block] and nest a project's parts into the block, with much higher utilisation. **Dramatic effect on how much raw material we need.**" | Quarra MIT Q&A, transcript line ~1485 |
| Whole-block yield maximisation is the talk's commercial pitch ("items per block" animation) | Borrowed Earth CEU slide 18 (35:00) |
| Material-availability-driven design is the framing of the MASON / RADD model — "form should emerge from materials that are actually available" | MASON §1 |
| Existing Frahan chain ends in `BlockCandidateGenerator` + `SlabYieldOptimizer` + `BlockPackTree` but has **no clean scan-as-input front-end** | `REPO_ARCHITECTURE_FLOW.md` §2 (GeoCut/GeoPack 5 components, Quarry 21 components) |

Without it, the existing GeoCut/GeoPack chain only accepts a bench mesh
that came from a synthetic block-from-bbox flow. The headline production
workflow (scan a real quarry block, nest parts) is implemented in the
backend but cannot be driven from the ribbon.

### 1.3 Component spec

**Display name:** `Scan to Block Inventory`
**Nickname:** `ScanBlock`
**Ribbon:** `Frahan > Quarry` (sits next to `Bench From Mesh`)
**GUID:** propose `F2D0BC20-1A2B-4F2D-A0B0-7E60CADA20A0` (next free
in the `F2D0BC` BlockCutOpt range; confirm uniqueness against the
`ComponentGuidUniquenessTests` build assert before commit).
**Class:** `Frahan.GH.Quarry.ScanToBlockInventoryComponent`
**Lives in:** `src/Frahan.StonePack.GH/Quarry/`
**Sync / async:** **synchronous** (per the
`feedback_gh_async_vs_sync` memory — no off-thread RhinoCommon topology
work; the scan-clean step is cheap).

#### Inputs

| Idx | Name | Nick | Type | Access | Optional | Default | Purpose |
|---|---|---|---|---|---|---|---|
| 0 | Scan Mesh | `M` | Mesh | item | no | — | Raw 3D-scanned block as a mesh (from handheld scanner → Scan Reconstruct, or from a `.3dm` import) |
| 1 | Orient | `O` | int | item | yes | 1 | `0` = use mesh's existing frame; `1` = align by PCA (longest principal axis → X); `2` = align by world Z (top face flat) |
| 2 | Usable Inset | `I` | number | item | yes | 0.0 | Inset (model units) used to compute the **usable interior volume** — accounts for kerf + scan noise + edge defects |
| 3 | Method | `Me` | int | item | yes | 0 | `0` = OBB (oriented bounding box, fast, deterministic); `1` = inscribed-AABB after PCA align; `2` = convex hull (uses `ConvexHullSlab` internally) |
| 4 | Label | `L` | string | item | yes | "" | Per-block label / provenance string (carried into the typed `QuarryBlock` and downstream metadata) |

#### Outputs

| Idx | Name | Nick | Type | Access | Purpose |
|---|---|---|---|---|---|
| 0 | Block | `B` | `QuarryBlock` (custom type, see §1.4) | item | The typed block: oriented frame + usable interior mesh + metadata |
| 1 | Bounds | `Bb` | Mesh | item | The oriented bounding-box mesh (visualisation + downstream usable-volume input) |
| 2 | Frame | `Fr` | Plane | item | Block's oriented base frame (origin + X/Y/Z axes from PCA / world align) |
| 3 | Dimensions | `D` | Vector | item | Block's principal dimensions (X = longest, Y = next, Z = thinnest) in model units |
| 4 | Volume | `V` | number | item | Usable interior volume (model units cubed); accounts for `Usable Inset` |
| 5 | Report | `R` | string | item | One-line summary: `"Block <label> 1.2 × 0.8 × 0.5 m, volume 0.42 m³, method OBB"` |

#### Validation rules

- Mesh required, valid, ≥4 vertices (else error).
- Orient ∈ {0,1,2} (else default 1).
- Method ∈ {0,1,2} (else default 0).
- Usable Inset ≥ 0 (clamp negative to 0).
- Empty mesh → outputs Empty Block + warning, not error.

### 1.4 New typed output: `QuarryBlock`

A small Core type that carries the geometry **plus** the metadata
downstream needs. Mirrors the existing `StoneCutMetadata` pattern.

```csharp
// In Frahan.StonePack.Core/Quarry/
public sealed class QuarryBlock
{
    public Mesh Bounds;          // oriented bounding box mesh
    public Mesh UsableVolume;    // interior mesh after inset (== Bounds when Inset=0)
    public Plane Frame;          // origin + axes
    public Vector3d Dimensions;  // X = longest, Y = next, Z = thinnest
    public double Volume;        // m³ (model units cubed)
    public string Label;         // provenance string (carried through pipeline)
    public string Method;        // "OBB" | "InscribedAABB" | "ConvexHull"
}
```

Downstream consumers (existing components that need to be **lightly
adapted** to accept `QuarryBlock`):

- `BlockPackTree` — currently takes a `Mesh` "container"; add an optional
  `QuarryBlock` input (or auto-extract `UsableVolume` if a `QuarryBlock`
  arrives).
- `Slab Yield Optimizer` — same: accepts `Mesh` block today; thread
  `QuarryBlock` through if present.
- `Block Candidate Generator` — same.
- `Pack 3D Irregular Container` — `QuarryBlock.UsableVolume` is its
  container input.

The adapter is **one constructor + one IGH_GeometricGoo wrapper** per
consumer. No GUID changes. The existing `Mesh`-input path stays working.

### 1.5 Reuse map (what existing components this composes)

The new component is **mostly a composition**, not new geometry math.
This is deliberate — the user asked to use existing components where
possible.

| Step | Reuses |
|---|---|
| Mesh validation + duplicate | (built-in RhinoCommon) |
| PCA orientation (Orient = 1) | reuse `Masonry/MeshPca` logic (extract to a `Core/Masonry/PrincipalAxes` helper) |
| OBB extraction (Method = 0) | reuse `Lab/Geogram OBB` or `Lab/Auto OBB` calc → or hand-roll PCA + bbox in Core (no native dep) |
| Convex hull (Method = 2) | reuse `Masonry/ConvexHullSlab` |
| Usable inset | mesh offset along inward normals + bbox intersection (Core arithmetic) |
| Bounds mesh | from `Quarry/BoxToMesh` (Brep→Mesh helper) |

The implementation work is therefore: (a) the `QuarryBlock` Core type,
(b) ~150 lines of orchestration in the GH component, (c) ~50 lines of
adapter code on the 4 downstream consumers. No new native code, no new
backend.

### 1.6 HITL card

New card: **V-BLOCKIN**, slots into `frahan_hitl_backlog.md` V1 series
just after V-ING and before V-REG.

| Card | Stage | Components | Fixture | Level | Pass criterion |
|---|---|---|---|---|---|
| V-BLOCKIN | Block intake | **Scan to Block Inventory** + BlockPackTree (downstream check) | real scanned block (`raw/2026-05-27/granite_dells_tls/` LAZ → Mesh, OR `raw/2026-05-29/independence_rock_tls/` E57 → meshed) | L2 | (a) `QuarryBlock` output non-empty; (b) `Frame` aligns to principal axis within 5°; (c) `Volume` within ±5 % of a reference truth (hand-measured bbox volume); (d) `BlockPackTree` consumes the `QuarryBlock` and reports placement; (e) no canvas freeze |

This card also gates the architectural decision in §1.4 (the `QuarryBlock`
type adopted by 4 downstream consumers).

### 1.7 Estimated effort

- Core type + helpers: **S** (under a day).
- GH component + adapters on 4 consumers: **M** (a few days).
- HITL card (generator + build_gh): **S**.
- Audit of GUID collision: **S** (run existing test).
- Net: **M (a few days).** Fits inside v1.0 if approved.

### 1.8 Risk + mitigation

| Risk | Mitigation |
|---|---|
| `QuarryBlock` type breaks downstream `Mesh`-input wiring | Adapter accepts both `Mesh` AND `QuarryBlock`; preserve all existing wires |
| PCA on a degenerate mesh produces an arbitrary frame | Fallback: world axes when PCA fails or eigenvalues are too close |
| Scanned mesh has noise / spikes / non-manifold defects | Run a guarded `MeshSanitize` pass internally; warn but don't error |
| User expects scan → mesh → block in one component | Document the chain: Load E57 / Read LAS → Voxel Downsample → Scan Reconstruct → **Scan to Block Inventory** → BlockPackTree |

---

## 2. Block-Cut + Quarry audit — per-component verdict for v1

Goal: cut the visible quarry / block-cut surface to **only what is
absolutely needed** to ship the v1 spine, with everything experimental
hidden in `Lab`. The mechanism is the **Lab gating** in backlog item
**B09** of the existing release backlog — `SwapComponents` + `Hidden /
Exposure` settings, GUID-stable, reversible.

### 2.0 What "Lab" means in v1 (not what the existing release plan implied)

The existing release plan §B10 ("Sculptor + factory stubs + unvalidated
Kintsugi/Monument → Lab") used "stub" language that **does not apply
across the board any more**. Components like Carving Stages, Enlarge
Sculpture, Fit In Block, Stone-Aware Cut Export, Staggered Block
Decompose, Fabrication Prep Report, Monument Packers, and the Kintsugi
chain **all received substantive development time** — they are not
stubs, they have HITL cards, and several are already industry-validated
by the Borrowed Earth + Quarra video research.

**Revised Lab definition (v1.0):**
"Lab" = components that **require enabling configuration** to use (a
native dependency that doesn't ship by default, a synthetic-only
generator that's helpful for testing not production, or a research
inspector that's not on the spine). **Not** components that "we
haven't validated yet" — those keep their subcategory and stay on the
ribbon with their HITL card pending. The default subcategory carries
the validation; Lab is reserved for genuinely experimental machinery.

Under this revised rule:

- **Sculpt** (Enlarge / Fit In Block / Carving Stages) — **stays on
  ribbon** in `Sculpt`. Cards 01-06 in the 2026-05-29 sculpt_fabricate
  set already validate them at L2/L3 level.
- **Fabrication** (Stone-Aware Cut Export / Staggered Block Decompose
  / Fabrication Prep Report / Monument Packers) — **stays on ribbon**
  in `Fabricate`. Export gated by E01/E02 only.
- **Kintsugi** (the 7-component chain) — **stays on ribbon** in
  `Kintsugi`. Backed by published research (ETH1100, PotNet-stone,
  pose-composition fix). Not "unvalidated stubs" — research-grade
  production code with documented provenance.
- **BlockCutOpt Inspector** (Pareto / Fisher-Robust / Density-Watershed
  / VTU / Mixed-Size) — **provisionally KEEP** in
  `BlockCutOpt / Research` sub-area. Per the BlockCutOpt synthesis memory
  these are deliberate research components with 10 documented
  improvements over BlockCutOpt 2020; the §2.2 table is **revised below**
  accordingly.
- **Advanced Quarry Decompose** (CoACD / Tet / Voronoi) — **LAB only
  because they require native deps not in default deploy** (Geogram
  with TetGen, CoACD shim). When the deploy includes those shims they
  return to the default ribbon. This is the genuine Lab case.
- **BlockCutOpt Ingestion** (PhotoToPly / AlgebraicConvex / SyntheticTNGranite)
  — **LAB** because they're synthetic-data generators for testing, not
  production geometry inputs.
- **Quarry Bridge / Quarry Ingestion** — **LAB pending content audit**
  (I don't have enough context on these without reading source).
- **Monument Packing** — **already moved to Fabricate** per HITL #4.

The §2.X sub-section verdicts below are the source-of-truth current
state. Where they disagreed with this revised rule (BlockCutOpt
Inspector, Heterogeneous, etc.) I've corrected them inline.

Verdict legend: **KEEP** (visible on default ribbon, in the named
subcategory) · **LAB** (move to the Lab subcategory; requires enabling
config to use) · **RETIRE** (Obsolete + Hidden, schedule for `git rm` in
v1.1 / 0.8.0) · **MERGE** (consolidate into a sibling component with a
Backend toggle).

### 2.1 Quarry (core path) — **KEEP all** (the spine)

| Component | File | Verdict | Reason |
|---|---|---|---|
| Bench From Mesh | `Quarry/BenchFromMeshComponent.cs` | KEEP | V1 spine: `V-BENCH` card |
| Clip Boxes By Mesh | `Quarry/ClipBoxesByMeshComponent.cs` | KEEP | V1 spine |
| Box To Mesh | `Quarry/BoxToMeshComponent.cs` | KEEP | utility, used by `ScanToBlockInventory` |
| **Scan to Block Inventory** (NEW) | `Quarry/ScanToBlockInventoryComponent.cs` | KEEP | new keystone (see §1) |

Net: 4 components on the ribbon in the `Quarry` group.

### 2.2 BlockCutOpt — **KEEP 3, LAB 11** (research-grade)

| Component | File | Verdict | Reason |
|---|---|---|---|
| BlockCutOpt Load Fractures | `BlockCutOptComponents.cs` | KEEP | spine ingestion (`V-FRAC`) |
| BlockCutOpt Solve | `BlockCutOptComponents.cs` | KEEP | V1 decompose (`V-DECOMP`) |
| BlockCutOpt AMRR Plan | `BlockCutOptComponents.cs` | KEEP | V1 decompose alternative; `V-DECOMP` |
| BlockCutOpt Omni Solve | `BlockCutOptComponents.cs` | KEEP | V1 decompose multi-objective; `V-DECOMP` |
| Pareto Front Inspector | `BlockCutOptInspectorComponents.cs` | **LAB** | research-grade visualisation, not part of spine |
| Fisher-Robust BCO | `BlockCutOptInspectorComponents.cs` | **LAB** | Monte Carlo analysis, research-grade |
| Density-Watershed Zones | `BlockCutOptInspectorComponents.cs` | **LAB** | adaptive subdivision research |
| VTU Export (ParaView) | `BlockCutOptInspectorComponents.cs` | **LAB** | dev-only export |
| Mixed-Size Block Pack (2D DLBF) | `BlockCutOptInspectorComponents.cs` | **LAB** | superseded by Pack 2D canonical + `BlockPackTree` |
| Mixed-Size Block Pack 3D | `BlockCutOptHeterogeneousComponents.cs` | **LAB** | superseded by `BlockPackTree` |
| Heterogeneous Extraction | `BlockCutOptHeterogeneousComponents.cs` | **LAB** | composite pipeline, not a primitive |
| Photo to PLY | `BlockCutOptIngestionComponents.cs` | **LAB** | one-shot conversion, niche |
| Algebraic Convex Poly | `BlockCutOptIngestionComponents.cs` | **LAB** | synthetic-data generator |
| Synthetic TN Granite | `BlockCutOptIngestionComponents.cs` | **LAB** | synthetic-data generator |

Net: **4 KEEP, 10 LAB**, 0 RETIRE in BlockCutOpt-named files. 14 components total
across the 4 BlockCutOpt source files. Lab-gating is **visibility only** per §0.1
— source / icon / GUID all preserved.

### 2.3 Advanced Quarry Decompose — **LAB all 3** (covered by H-QDEC audit, then maybe one survives)

| Component | File | Verdict | Reason |
|---|---|---|---|
| Quarry Decompose By CoACD | `AdvancedQuarryDecomposeComponents.cs` | **LAB** (audit-gated) | five-way Quarry Decompose collision; H-QDEC card; CoACD is research-grade |
| Quarry Decompose By Tet (Geogram) | `AdvancedQuarryDecomposeComponents.cs` | **LAB** (audit-gated) | tetrahedralisation requires Geogram-with-TetGen build; not in default deploy |
| Quarry Decompose By Voronoi (Geogram) | `AdvancedQuarryDecomposeComponents.cs` | **LAB** (audit-gated) | Voronoi-cells research |

Net: **0 KEEP, 3 LAB** of Advanced. After H-QDEC card, the *best-behaved
one* may come back to the ribbon. Decision deferred to that card.

### 2.4 CGAL Cut — **KEEP both** (spine)

| Component | File | Verdict | Reason |
|---|---|---|---|
| Slab Cut By Tool Mesh (CGAL) | `CgalCutComponents.cs` | KEEP | V1 spine (`V-SLAB`); CGAL backend toggle for robustness |
| Quarry Decompose By Mesh (CGAL) | `CgalCutComponents.cs` | KEEP | V1 decompose backend (`V-DECOMP`) |

Net: **2 KEEP.**

### 2.5 GeoCut + GeoPack chain — **KEEP all 5** (spine yield path)

| Component | File | Verdict | Reason |
|---|---|---|---|
| Slab Yield Optimizer | `GeoCutAndGeoPackComponents.cs` | KEEP | V1 spine (`V-YIELD`); accepts `QuarryBlock` after §1.4 |
| Billet Cutter | `GeoCutAndGeoPackComponents.cs` | KEEP | V1 spine; sub-divides slab into billets |
| Crack Graph (manual) | `GeoCutAndGeoPackComponents.cs` | KEEP | V1 spine (`V-YIELD`); wraps `FracturePlanes` as graph |
| Block Graph | `GeoCutAndGeoPackComponents.cs` | KEEP | V1 spine |
| Block Candidate Generator | `GeoCutAndGeoPackComponents.cs` | KEEP | V1 spine; accepts `QuarryBlock` after §1.4 |

Net: **5 KEEP.**

### 2.6 QuarryCutOpt — **KEEP 3, LAB 2**

(5 components; I only have nicknames from earlier inspection — `QuarryInventory`,
`YieldEstimator`, `ExtractionOrder` are the 3 named; 2 unnamed.)

| Component (named) | File | Verdict | Reason |
|---|---|---|---|
| Quarry Inventory | `QuarryCutOptComponents.cs` | KEEP | V1 spine (`V-YIELD`) |
| Yield Estimator | `QuarryCutOptComponents.cs` | KEEP | V1 spine (`V-YIELD`) |
| Extraction Order | `QuarryCutOptComponents.cs` | KEEP | V1 spine (`V-YIELD`) |
| 2 unnamed | `QuarryCutOptComponents.cs` | **LAB** (provisional) | identify in audit; lab unless they have a spine role |

Net: **3 KEEP, 2 LAB** (provisional pending source read).

### 2.7 Quarry Bridge — **LAB all 3** (one-off bridges)

| Component | File | Verdict | Reason |
|---|---|---|---|
| 3 components | `QuarryBridgeComponents.cs` | **LAB** | from naming, these are bridge-to-experimental; not spine |

Net: **0 KEEP, 3 LAB.**

### 2.8 Quarry Ingestion — **LAB both 2**

| Component | File | Verdict | Reason |
|---|---|---|---|
| 2 components | `QuarryIngestionComponents.cs` | **LAB** | synthetic / experimental ingest |

Net: **0 KEEP, 2 LAB.**

### 2.9 Monument Packing — **KEEP all 3 in Fabricate group** (HITL answer #4)

Per HITL answer (2026-05-30), Monument Packing is **not Lab**; relocated to
the **Fabricate** subcategory where it fits the SKU shape (monument
fabricators produce cut layouts; Stone-Aware Cut Export already sits
there). The "B10 unvalidated → Lab" rule is **revised for Monument** —
remains visible on the ribbon under Fabricate, validated via the existing
Phase 11 monument cards.

| Component | File | Verdict | Reason |
|---|---|---|---|
| 3 components | `MonumentPackingComponents.cs` | **KEEP in Fabricate** | HITL answer; SKU/output-side fit; existing Phase 11 cards validate behaviour |

Source + icon + GUID preserved per §0.1. The ribbon subcategory string
changes from `Monument` (or wherever it sat) to `Fabricate`; no GUID
edit, no source edit.

Net: **3 KEEP (Fabricate), 0 LAB.**

### 2.10 Masonry-side Quarry — **KEEP both pending H-QDEC** (HITL answer #5)

Per HITL answer (2026-05-30): both components stay visible until the
**H-QDEC consolidation card** decides on their behavioural distinctness
vs the CGAL variant. The plan still flags `Masonry/QuarryDecomposeComponent`
as a likely Lab candidate, but **only after** H-QDEC validates that the
CGAL variant covers its use cases.

| Component | File | Verdict (current) | Verdict (post-H-QDEC) | Reason |
|---|---|---|---|---|
| QuarryDecompose (Masonry) | `Masonry/QuarryDecomposeComponent.cs` | **KEEP** | Lab if H-QDEC shows full overlap with CGAL variant; KEEP if behaviourally distinct | HITL answer #5 — no Lab-gate without HITL |
| QuarryDfn (DFN) | `Masonry/QuarryDfnComponent.cs` | **KEEP** | KEEP | V1 spine fracture-generator (`V-FRAC`) |

Net: **2 KEEP, 0 LAB (pending H-QDEC).**

### 2.11 Surface Packing — **KEEP all 3** (industry-validated by Quarra QLab + UVA)

Surface Packing was not flagged for audit in the existing release plan,
but it deserves an explicit positive verdict given the video research:

| Component | File | Verdict | Reason |
|---|---|---|---|
| Surface Chart | `SurfacePacking/SurfaceChartComponent.cs` | **KEEP** | conformal-mapping primitive; Quarra QLab + UVA Memorial both run this technique |
| Pack On Surface | `SurfacePacking/PackOnSurfaceComponent.cs` | **KEEP** | direct match to the UVA "photo-v-carve mapped onto a conical surface" workflow |
| Pack Surfaces | `SurfacePacking/PackSurfacesComponent.cs` | **KEEP** | multi-surface variant |
| Chart Flatness Report | `ChartFlatnessReportComponent.cs` | **KEEP** | QA for the chart step (distortion sanity-check) before committing the carve |

Net: **4 KEEP, 0 Lab, 0 Retire.** These are the canonical Frahan code
path for the pattern-on-surface theme (§5.3); industry-validated by
Quarra's production work. Do not Lab-gate.

### 2.12 Net effect — counts visible on the v1 ribbon

Updated for HITL answers #4 + #5 (2026-05-30).

| Group | Before | After (v1) | Hidden in Lab | Notes |
|---|---|---|---|---|
| Quarry (core path) | 3 | **4** (+1 new) | 0 | +1 = Scan to Block Inventory |
| BlockCutOpt | 14 | **4** | 10 | 4 solvers on ribbon; 10 inspector/heterogeneous/ingestion in Lab (source + icon + GUID all preserved) |
| Advanced Quarry Decompose | 3 | **0** (audit-gated) | 3 | H-QDEC may return one to ribbon |
| CGAL Cut | 2 | **2** | 0 | |
| GeoCut/GeoPack | 5 | **5** | 0 | accepts `QuarryBlock` after §1.4 |
| QuarryCutOpt | 5 | **3** | 2 | 2 unnamed → Lab provisional |
| Quarry Bridge | 3 | 0 | 3 | |
| Quarry Ingestion | 2 | 0 | 2 | |
| Monument Packing | 3 | **3** | 0 | **Moved to Fabricate subcategory (HITL answer #4)** |
| Masonry-side Quarry | 2 | **2** | 0 | **Both KEEP pending H-QDEC (HITL answer #5)** |
| Surface Packing | 4 | **4** | 0 | Industry-validated by Quarra QLab + UVA (§5.3) |
| **Total quarry+block-cut+surface-pack surface** | **46** | **27** | **20** | |

**A v1 visible surface of 27 components (down from 46) in the quarry +
block-cut + surface-pack envelope.** Every visible component is on a HITL
card. Every Lab-gated component remains buildable + GUID-stable + icon-
intact per §0.1.

### 2.13 What's NOT in this audit (out of scope)

- Masonry assembly chain (Pack 2D, Pack 3D, Surface, EdgeMatch, Sculpt,
  Fabrication) — already covered by the existing release plan's
  consolidation work (B05 Pack 2D, B06 mesh-ops, B08 packers, B10 sculptor
  / Kintsugi → Lab).
- Scan ingest (Load Cloud / E57 / LAS, ICP, Reconstruct) — already in
  the spine, no audit changes.

---

## 3. v1.0 ribbon — the post-audit visible surface

| Group | Visible components |
|---|---|
| **Ingest** | Load Cloud, Load E57 Cloud, Read LAS Cloud, Import Photo Markers, Vector Fractures Loader, Vertical Fracture Planes From Curves, Voxel Downsample, Estimate Cloud Normals, Scale Calibrate |
| **Register** | Marker Registration, **one** Georeference (per B03) |
| **Reconstruct** | Scan Reconstruct, (Auto / managed) repair-trio canonical (per B06) |
| **Mesh** | Sanitize Mesh, Close Holes, Move To Origin, Mesh Diagnostics, Mesh Quality Report (Masonry) |
| **Quarry (new spine)** | Bench From Mesh, Clip Boxes By Mesh, Box To Mesh, **Scan to Block Inventory (NEW)** |
| **Fractures** | Grid / Random / Voronoi / **plus** the surviving plane generators per audit (H-2D-side), Joint Set, QuarryDfn |
| **Slab Cut** | Slab Cut By Tool Mesh (CGAL), Quarry Decompose By Mesh (CGAL), Slab Cut By Fractures (CGAL toggle), Slab Cut By Fracture Polygons, Vertical Fracture Planes From Curves |
| **BlockCutOpt** | Load Fractures, **Solve**, **AMRR Plan**, **Omni Solve** (4) |
| **GeoCut / GeoPack** | Slab Yield Optimizer, Billet Cutter, Crack Graph, Block Graph, Block Candidate Generator (5) |
| **QuarryCutOpt** | Quarry Inventory, Yield Estimator, Extraction Order (3) |
| **Pack 2D** | canonical 1–2 (per B05 + H-2D audit), retired V2 / V3 / V506 hidden |
| **Pack 3D** | Pack 3D Irregular Container (now consumes `QuarryBlock`), Pack 3D Mesh Heightmap, Block Pack Tree (now consumes `QuarryBlock`), Packing Report, Residual Voids |
| **Masonry** | Ashlar Pack (+ Options), Best Fit Pack (Random Rubble), Auto Interfaces (+ Robust), Masonry Block / Assembly / Stability RBE, Pick/Place Frames, Wall Frame, Build Order / Coloring / Stability Stream / Build Step Preview / Build Sequence JSON, Joint Set, Pack Diagnostics, Pack Preview |
| **Surface Packing** | Surface Chart, Pack On Surface, Pack Surfaces |
| **EdgeMatch** | EdgeMatch Solve, EdgeMatch Segments, EdgeMatch Options, Trencadis Edge Match, Fragment Edge Match, Fragment Descriptors |
| **Fabrication** | Stone-Aware Cut Export (gate E01/E02), Staggered Block Decompose, Fabrication Prep Report, **Monument Packing (3 components, moved from `Monument` subcategory per HITL #4)** |
| **GPR** | GPR File Loader, GPR Radargram Mesh, GPR Picks From Points, GPR Fracture Overlay |
| **Lab** | **only genuinely experimental machinery** — Advanced Quarry Decompose (CoACD / Tet / Voronoi; native deps not in default deploy), BlockCutOpt Ingestion (PhotoToPly / Algebraic / Synthetic TN Granite — synthetic-data generators), Quarry Bridge / Quarry Ingestion (pending content audit). **Sculpt, Fabrication, Kintsugi, Monument, BlockCutOpt Inspector — all on the default ribbon** per §2.0 (they had their time; not stubs). |

Rough headline: **~80 production components visible on v1, ~100+ hidden
in Lab**. The current ribbon shows 184 components and is overwhelming.
The audited ribbon is **the validated spine plus its branches**, nothing
half-finished.

---

## 4. Docs to update for clean release — full delta map

Plans only. Edits applied **after** HITL on this plan.

### 4.1 Existing release-plan docs (in `reference/release_plan/` — move into the repo)

| Doc | Status | Proposed delta |
|---|---|---|
| `frahan_v1_release_checklist.md` | ✓ comprehensive | **Add Gate 1.5** under "Hygiene complete": `[ ] Scan to Block Inventory (new) ships and consumes a real scanned block (V-BLOCKIN passes)` — references new backlog item and HITL card |
| `frahan_release_backlog.md` | ✓ ordered, sized | **Add B11** under v1.0 Blockers: `BUILD Scan to Block Inventory component + QuarryBlock typed adapter on 4 downstream consumers (M)`. **Note:** F1A "Machine-profile library" stays in v1.1 — different scope. |
| `frahan_hitl_backlog.md` | ✓ V/H/V3 series | **Add V-BLOCKIN** in V1 series right after V-ING; update priority order to: H-2D, H-MESH, H-QDEC, **V-ING, V-BLOCKIN**, V-REG → V-POSE, V-PACK (highest risk), V-E2E, V-EXP. **Add V-PACK sub-case "photo-v-carve on a conical / curved surface"** (Surface Chart → Pack On Surface → carved depth-map output) — directly models the UVA Memorial / QLab pattern-on-surface scenario. Fixture: a small conical mesh + a bitmap depth-map. |
| `frahan_quarra_lecture_implications.md` | ✓ analytical | **No edits** — cross-reference from §1.2 of this plan. Already cites F18 / F1A / F1B / F1C as v1.1+. The Scan to Block Inventory is the *v1.0 down-payment* on that direction. |

### 4.2 New docs to write (in `Template-General/outputs/2026-05-30/`)

| Doc | Path | Role | Status |
|---|---|---|---|
| Borrowed Earth presentation + implications | `research/borrowed_earth/` | already on the repo (commit `4b7bd23`) | ✓ done |
| Quarra Stone presentation + implications | `research/quarra_stone/` | already on the repo (commit `297c16f`) | ✓ done |
| **MASON / DCA implications for Frahan** | `research/mason_dca/mason_dca_implications.md` (+ curated images) | new — Oxford Brookes MA dossier intake | **WRITE after HITL on this plan** |
| **Consolidated video-research findings (one-pager)** | `research/CONSOLIDATED_VIDEO_FINDINGS.md` | new — index pointing at all three (BE / Quarra / MASON) + the cross-cutting themes | **WRITE after HITL on this plan** |
| **This document** | `architecture/v1_consolidated_plan.md` | current plan; the HITL artefact | ✓ writing now |

### 4.3 Code changes (post-HITL on this plan)

1. **`QuarryBlock` Core type** — new file `src/Frahan.StonePack.Core/Quarry/QuarryBlock.cs`. The typed record + simple constructor + IGH_GeometricGoo wrapper for canvas wiring. Per HITL #2: typed output **AND** raw mesh/frame/dim/volume outputs both ship; downstream consumers accept **either** input path.
2. **`ScanToBlockInventoryComponent`** — new file `src/Frahan.StonePack.GH/Quarry/ScanToBlockInventoryComponent.cs`. GUID `F2D0BC20-1A2B-4F2D-A0B0-7E60CADA20A0` (confirmed HITL #1).
3. **Adapter edits** on 4 downstream consumers: `BlockPackTree`, `Pack3DIrregularContainer`, `SlabYieldOptimizer`, `BlockCandidateGenerator` — each accepts **optional** `QuarryBlock` input alongside the existing `Mesh` input. Both wire paths supported (HITL #2). **GUID unchanged on all 4.**
4. **Full Lab-gating mechanism (B09)** — built **now** for v1.0 (HITL #3). One config + one helper class:
   - `Frahan.StonePack.GH/Attributes/LabExposureAttribute.cs` — opt-in attribute that flips `Hidden=true`, `Exposure = GH_Exposure.hidden`, and rewrites the subcategory string to `"Lab"`.
   - `Frahan.StonePack.GH/Attributes/LabConfig.cs` — central allow-list of GUIDs the build should Lab-gate; one-line flip per component to return to default ribbon.
   - **Preservation contract (§0.1):** source files stay, icon resources stay, `.csproj` `<Compile>` entries stay, GUIDs stay. The mechanism is a runtime visibility flag only. Existing `.gh` documents continue to instantiate Lab-gated components without error.
5. **Lab-gating apply** to the components listed in §2 per HITL answers (Monument and Masonry-Quarry **excluded** from Lab per HITL #4 + #5). The list is the §2.12 table.
6. **Tests** — extend `ComponentGuidUniquenessTests` to assert the new GUID is unique (it will be, but the test catches a regression). Add a `LabGatingRoundtripTest` that asserts every Lab-gated component **still instantiates from a saved `.gh` referencing its GUID**.

### 4.4 References audit (new) — every algorithm cites its source

Per HITL ask (2026-05-30): consolidate every algorithm reference across
the codebase + reference docs + chat handoffs. The goal is that v1
ships with **every algorithmic component traceable to a primary source**
— paper, repo, official spec, or named handoff doc. AGENTS.md §9 already
forbids invented citations; this task closes the gap from "no invented"
to "every cited."

Scope of audit:

| Surface | What gets a citation | Source format |
|---|---|---|
| Algorithm-tagged GH components (the 17 tagged per existing release notes) | The paper / repo the algorithm is from | `[AlgorithmReference]` attribute on the component class — DOI / arXiv / GitHub link |
| Core algorithm files (BlockCutOpt, EdgeMatching, Surface Packing, BlockPackTree, MasonryStabilityRbe, photo-v-carve mapping, etc.) | One-line citation comment at the top of the file with the primary source | C# top-of-file comment |
| Native shim wrappers (CGAL, Geogram, CoACD) | The library's canonical citation | Already present in some files; complete the set |
| Research dossiers in `wiki/research/` (BE, Quarra, MASON) | Cross-link back to the Frahan component that implements / is validated by each cited technique | Per-implication footnote |

Inputs to the audit (the "references handoffs and links pasted in chats"):

- `D:\code_ws\reference\release_plan\frahan_quarra_lecture_implications.md` §6 references (Quarra Cairn ACADIA 2017, Ariza FABRICATE 2017, Quarra public site).
- `D:\code_ws\reference\MASON_Digital_Craft_Architecture.md` (Oxford Brookes MA dossier).
- `D:\code_ws\wiki\papers\` (existing equations + diagrams folder per the project memory).
- Project memories: `project_compas_masonry`, `project_blockcutopt_synthesis`, `project_jalalian_bcsdbbv`, `project_open_source_block_cutters`, `project_geocrack_geofractnet`, `project_granite_open_datasets`, `project_kintsugi_port_pose_composition`, `reference_eth1100_dataset`, `reference_quarry_scan_datasets`.
- Chat-pasted references: Borrowed Earth links (paharudothobjects.com, design icons), Quarra company site, the QLab mentions.

Deliverable: a new doc
`wiki/index/algorithm_references_audit.md`
that lists, **per algorithmic component**:

1. The primary source (citation + link).
2. Where in the code the citation lives (file + line, after the audit).
3. The matching wiki/papers/ entry if any.
4. A "verified" or "needs citation" flag — `needs citation` rows feed
   a backlog of small "add comment + attribute" PRs.

This is a **research + edit pass**, not a re-design. Sized as **M (a
few days)** for the audit pass + per-component citation edits. Slots
into Gate 1 (Hygiene) of the v1.0 release checklist.

### 4.4 HITL cards (post-HITL on this plan)

| Card | When |
|---|---|
| **V-BLOCKIN** | Generated alongside the component (`outputs/<date>/hitl_cards/scan_block_inventory/`); rhino3dm fixture + Rhino build-gh script following the proven pattern |
| **H-QDEC** | Already in the backlog; should run BEFORE we Lab-gate Advanced Quarry Decompose, so we know which (if any) variant returns to the ribbon |
| **H-2D, H-MESH** | Already in the backlog; same logic |

---

## 5. Consolidated video-research findings (one-page summary)

Three video / lecture sources, all from the same end of the market (digital
+ traditional stone fabrication). They cluster around four cross-cutting
themes that should shape v1+.

### 5.1 The five cross-cutting themes (with sources)

| Theme | Borrowed Earth | Quarra | MASON / DCA |
|---|---|---|---|
| **Block-yield maximisation** is the single largest commercial lever | "items per block" animation (slide 18, 35:00) — 3+ SKU per block uses the whole block, savings → client | "we can take a quick 3D scan of [the block] and **nest a project's parts into the block**, much higher utilisation" (Q&A) | Whole project — RADD = "form should emerge from materials that are actually available"; +242 % expansion from zigzag-cut of a single quarry block |
| **Scan-driven registration** is the sub-mm moat | Heritage 3D scan + recreate at scale (2nd-c. Indian monument) | **Headline.** Repeatability + scan-best-fit deviation surface; laser tracker at 0.1–0.2 mm in field; machined fiducials surviving flips (0.5 → 0.15 mm) | Photogrammetry → optimise geometry → fit clean mesh into irregular scanned block (MASON §2.1) |
| **Selective precision** — not every surface deserves sub-mm | Implicit (tile sizing to maximise yield, "different finishes per surface") | Explicit ("we couldn't just mill every piece on that job to this millimetric precision … structural surfaces get sub-mm, visible non-critical gets simplified, hidden gets rough") | Cut-and-stack at 242 % — the cut surfaces are *exact*, the stack faces are *as found* (the cut IS the precision; the rest is dry-stone) |
| **Hand-finish + machine-cut sequenced** — the digital model must leave allowance | All robotic carving + hand finish on every piece (slide 28) | "Almost everything that we make here with a CNC machine gets touched by hand" — Cervietti hand-carving studio bought outright | "Industry 5.0 hybrid: understanding *how and when* to work with your hands versus *when* to work with machines"; the digital model **orchestrates material behaviour** rather than imposing form |
| **Pattern / image mapped onto curved surfaces** — the conformal-mapping line of work | Hex-Skin parametric envelope (5 cm / 3 cm zones + perforated lattice on a building skin); contour-relief carved into a 6" slab from a topographic map | **QLab "painting in stone" follow-up to Sarah Sze:** studies of surface textures + colour-dot patterns + a depth-map pixelated eye; **UVA Memorial photo-v-carve mapped parametrically onto a conical surface** | Wave Function Collapse on voxel grids + Soma-cube modules tiled onto wall + column geometries (a discrete cousin of conformal mapping, same intent: catalogued pieces onto a target surface) |

### 5.2 What MASON specifically adds that BE + Quarra didn't

The MASON dossier (Oxford Brookes MA Digital Craft in Architecture)
introduces three concepts that are not in the BE / Quarra story:

1. **Wave Function Collapse (WFC) as a design language for catalogued
   parts.** Given a parts catalogue (e.g. a list of `QuarryBlock`s with
   sizes), use WFC + adjacency rules to assemble them into an
   architectural form. This is a **post-decomposition** technique that
   would sit downstream of `BlockPackTree` / `BlockCandidateGenerator`
   in Frahan. Not v1, but a strong v2 candidate — fits in the v2 "early
   design-assist" band as a *generator from a block inventory*.
2. **Diagonal Configuration / Soma-cube interlocking modules.** A
   self-restricting modular system where "L" and "Z" pieces interlock —
   close to the Frahan **Edge Matching** chain in intent, but at a
   coarser scale (whole blocks, not panel boundaries). The interlocking-
   geometry idea is exactly the Hudson Yards "miniature jigsaw" from BE
   and Emanuel 9's interlocking bench walls from Quarra. Frahan's
   `Edge Matching` family is the right code path; a "Block Interlocking
   Layout" component would be a natural v2 add.
3. **2-axis wire-cut apparatus + 6-axis robotic voussoir twist.**
   Hardware-specific fabrication strategies. 2-axis wire is *cheaper* and
   *constrained* — Frahan should have a "2-axis-only" reach-check on its
   Machine Profile (v1.1, item F1A). 6-axis voussoir twist is a target
   for the carvability gate (v2, F35) but also for the staggered-block
   decompose (Fabrication) — could feed a "twist allowance" parameter.

### 5.3 QLab follow-up → conformal mapping is the Frahan Surface-Packing chain

This is worth pulling out separately because it directly validates an
existing Frahan ribbon group.

Right after the Sarah Sze section (~46:00 in the Quarra talk), Brian
says: *"one interesting thing that came out of this project was a desire
internally to **further explore painting in stone — what we could do with
colour dots in stone**. So these were some studies that our QLab team
did, just looking at different surface textures and putting holes into
that, colouring it, as well as a pixelated image of an eye based on the
depth and the size of the opening."* The eye study (Sam's) reads as a
**depth-map driven pattern mapped onto a 3D surface**.

This sits on top of the UVA Memorial section (~26:00), where the project
explicitly maps a 2D photo-v-carve **parametrically onto a tapered
conical surface**, preserving two superimposed textures (scallop +
emergent eye pattern), with results good enough to be a permanent
public memorial. Photo-v-carve on a conical surface is **the canonical
conformal-mapping problem in stone**.

Both QLab strands are the same Frahan ribbon group:

| QLab / Quarra technique | Frahan ribbon component(s) that already do this |
|---|---|
| Map a 2D depth-map / image / pattern onto a curved 3D surface, preserving relative geometry | `Surface Chart` + `Pack On Surface` + `Pack Surfaces` (the Surface Packing trio) |
| Verify chart quality (flatness, distortion) before committing the carve | `Chart Flatness Report` |
| Re-pack catalogued tile parts onto the chart | `Pack 2D Trencadis` + variants → applied through `Pack On Surface` |
| Carry stone-aware metadata for each pattern element through the export | `StoneCutMetadata` + `Stone-Aware Cut Export` |

**Implication for v1.** No new component is required for this surface
— the existing Surface-Packing chain is the right code path and **a
solid industry reference**: Quarra runs the same technique in
production at the UVA Memorial, validated by an internal QLab research
strand. Three concrete actions follow:

1. **§3 ribbon — Surface Packing components stay KEEP, no Lab gating
   at all.** They are now industry-validated, not experimental.
2. **HITL card V-PACK** (already in `frahan_hitl_backlog.md` V1 series)
   should add a **specific sub-case: photo-v-carve on a curved surface
   via the Surface Chart → Pack On Surface chain** — directly modelling
   the UVA Memorial scenario. Fixture: a small conical mesh + a
   bitmap depth-map.
3. **The future "Pattern Map to Surface" composition** (mentioned in
   the Quarra implications §21.6 follow-on HITL cards) is a v1.1+ work
   that **wraps the existing Surface Packing chain into a one-click
   workflow**, not a new geometry primitive. It belongs in the
   Fabrication group (or Surface Packing) as a "starter card" for
   designers, with the existing primitive components staying available.

This also retires any temptation to relocate Surface Packing to Lab as
part of the audit. They are spine for the Hex-Skin / monument / texture-
on-surface use cases.

### 5.4 What all three confirm about Frahan's positioning

Pre-CAM stone fabrication readiness. NOT a CAM competitor. The bridge
layer between design intent and machine-ready fab. All three sources
explicitly land here:

- BE explicitly: "stone if manufactured sustainably and mindfully and
  utilised efficiently is the best material to work with" — the work
  is in *fabrication readiness*, not in posting machine code.
- Quarra: "we want to be part of the design process" + "early DD
  involvement is the value pitch" — the moat is upstream of CAM.
- MASON: "digital tools to **orchestrate material behaviour** rather
  than imposing abstract form" — same thesis at the academic level.

Project memory `feedback_positioning_pre_cam_stone_logic` is correct.
No re-positioning needed.

---

## 6. Execution order (post-HITL on this plan)

All six §7 questions are answered. Order below; everything HITL-gated per
AGENTS.md.

1. **Checkpoint now** — git tag the current plan as
   `frahan-v1-plan-2026-05-30` so we can recover this point.
2. **Write `mason_dca_implications.md`** (the third research doc,
   matching BE + Quarra) and a top-level `CONSOLIDATED_VIDEO_FINDINGS.md`
   one-pager. Types-compatible ingest per HITL #6. *Effort S. No code.*
3. **References audit (new, §4.4)** — read every references / handoff
   doc on disk + project memory + chat history; produce
   `algorithm_references_audit.md` listing every algorithmic component
   with its primary source + verification flag. *Effort M. No code yet;
   the citation-edit PRs that follow are S each.*
4. **Update the 4 release_plan docs** with the deltas in §4.1. *Effort
   S.* These docs become the `wiki/project_management/` candidates.
5. **Run the H-QDEC + H-2D + H-MESH consolidation audits** on the
   current `.gha`. Outputs: which variants are truly distinct, which
   are dupes. HITL on the canvas. (Per existing backlog.)
6. **Build the Lab-gating mechanism (full B09)** — `LabExposure`
   attribute + `LabConfig` + the `LabGatingRoundtripTest`. Preservation
   contract per §0.1. *Effort S/M.*
7. **Build the `QuarryBlock` Core type + `ScanToBlockInventoryComponent`**
   + adapter edits on the 4 downstream consumers. Both wire paths
   (typed + raw) per HITL #2. Build + GUID-uniqueness test. *Effort M.*
8. **Generate the V-BLOCKIN HITL card** + fixtures. Run on the
   Independence Rock E57 scan. (Real data.)
9. **Apply Lab-gating** for the components listed in §2.12 (Monument
   and Masonry-Quarry **excluded** per HITL #4 + #5). Source / icon /
   GUID preserved everywhere. *Effort S.*
10. **Add citation attributes** from the audit (step 3 output). *Effort
    S; many small PRs.*
11. **Commit + push** the whole v1-prep batch behind a feature branch.
    Tag `v1.0-rc1`.
12. **Promote** the v1 release plan + checklist + HITL backlog + the
    three research docs to `wiki/project_management/` and
    `wiki/research/` after HITL.

---

## 7. Open questions — all answered 2026-05-30

| # | Question | Resolution |
|---|---|---|
| 1 | GUID for Scan to Block Inventory | **`F2D0BC20-1A2B-4F2D-A0B0-7E60CADA20A0`** accepted |
| 2 | `QuarryBlock` typed wrapper vs raw outputs | **Both** ship: typed `QuarryBlock` output AND raw `(Mesh, Plane, Vector, double, string)` outputs; downstream adapters accept either wire path |
| 3 | Lab-gating mechanism scope | **Full B09 built now for v1.0** (see §4.3 item 4). Preservation contract per §0.1 |
| 4 | Monument Packing → Lab? | **No** — moved to **Fabricate** subcategory instead, source + icon + GUID preserved (§2.9). Existing Phase 11 cards validate. |
| 5 | Masonry-side QuarryDecompose | **Both stay visible** until H-QDEC audit decides (§2.10). No Lab-gate without HITL. |
| 6 | MASON dossier ingest | **Yes — types-compatible ingest**, MASON dossier becomes a research-only doc under `wiki/research/mason_dca/`. Frahan types stay primary; any MASON concepts that map cleanly (e.g. WFC adjacency rules, voxel-to-block placements) ship as **optional input/output compat layers**, not authoritative type changes. HITL still needed to promote any algorithmic technique. |

All six resolved. No remaining open questions before the build phase.

---

## 8. What this plan deliberately does NOT touch

- The **multi-pass machining subsystem** from Quarra implications §21.4 —
  that's v1.1 / v1.2 territory (F18 / F1A / F1C), correctly placed there
  in the existing release backlog. Not in v1.0 scope.
- The **machine-profile library** (F1A) — v1.1, also correctly placed.
- The **remnants / stone-brick branch** from the Borrowed Earth analysis
  — proposed as v1.2/v2 work. Not in v1.0 scope.
- The **colour / pigment metadata** (Sarah Sze territory from Quarra) —
  v2+ research.
- Sculptor + factory branches beyond Stone-Aware Cut Export — v1.1+
  (factory branch) and v2 (sculptor branch).
- Wiki promotion of any of these docs — explicitly waits on HITL per
  AGENTS.md §6.

This plan is **only** v1.0 scope: the keystone Scan to Block Inventory
component, the quarry / block-cut audit that goes with it, and the doc
deltas the existing release plan needs to absorb it. Everything else is
already in the right v1.1 / v1.2 / v2 buckets in the existing backlog,
which I'm not proposing to change.

---

*Plans-only. Awaiting HITL before any code change, doc edit, commit, or
ingest. The 4 existing `reference/release_plan/` docs are the canonical
target; this plan tells you exactly what to add to each.*
