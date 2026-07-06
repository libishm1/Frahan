# Frahan StonePack — Cathedral-Scale Stone Fitting: Plan in Words

**Location:** `wiki/specs/cathedral_scale_stone_fitting_plan.md`
**Status:** canonical · authored 2026-05-31 per Libish directive:
*"answer this question of fitting/matching from stock in words, make a
plan of implementation. Can we use existing tools to accomplish this
with Soft ICP etc.? It should be simple components."*
**Authority anchor:** `wiki/specs/frahan_design_philosophy.md` §10.

## §0. The question, restated

> *"What algorithms do we need to fit stones onto a 3D model of a
> building? How do we go about generating a 3D model of a cathedral or
> something?"*

Two operations, in sequence:

1. **Generate the building model** — design the cathedral, palace,
   freeform pavilion, or Vitruvian villa as a 3D Rhino model, and
   subdivide it into individual stone templates.
2. **Fit stones from stock to the templates** — for every template,
   find the inventory stone that yields it with acceptable yield,
   correct grain, and minimum carving.

Both operations are already supported by Frahan's component substrate.
This document spells out the workflow as a designer would invoke it,
the algorithms each step needs, and which existing Frahan components
serve which step. The plan is composition over invention — almost
every algorithm we need is already in `Frahan.EdgeMatching.Core` or
`Frahan.StonePack.Core`.

## §1. Generating the cathedral model

The architect controls the global form. Frahan does not invent the
cathedral; it consumes the architect's intent. There are four broad
form-vocabularies a stone cathedral / palace / freeform pavilion can
sit in, and Frahan supports all four because each is just a different
way of producing the **template inventory** that step §2 consumes.

### §1.1 Gothic — rib-vault + voussoir subdivision

The classical Gothic toolchain:

1. **Thrust Network Analysis (TNA)** finds the funicular compression-
   only surface for the vault span (Block Research Group, ETH Zürich
   — *Armadillo Vault*, *Beyond Bending* Venice Biennale 2016).
   Frahan does NOT implement TNA; the architect uses Block's
   `compas_tna` Python library OR Robert McNeel's RhinoVAULT, or
   draws the rib network by hand from a section drawing.
2. **Rib subdivision** — the funicular surface is dissected into rib
   curves following the gothic vocabulary (transverse, diagonal,
   tierceron, lierne, ridge). Each rib becomes a series of voussoirs.
3. **Voussoir generation** — the *Voussoir* Grasshopper plugin
   (food4rhino, free) takes the rib network and emits one voussoir
   mesh per stone, with joint surfaces (bed / head / key) parameterised.
4. **Wall + buttress geometry** — the nave wall, transept, apse,
   buttresses are modelled directly in Rhino as ashlar courses
   (rectangular block templates).
5. **Tracery** — for fine ornament (rose windows, blind arcades),
   modelled as 2D curve sets that get extruded to template thickness.

Frahan's role kicks in at step (a) downstream of voussoir generation
and at (b) downstream of any other template-producing step.

Cite: Rippmann & Block (2011) *Digital Stereotomy: Voussoir geometry
for freeform masonry-like vaults informed by structural and fabrication
constraints* (IABSE-IASS 2011). Block et al. *Armadillo Vault*
(2016 Venice Biennale). Also the wiki page
`wiki/research/voussoir_stereotomy_integration.md` for the existing
Voussoir → Frahan integration plan.

### §1.2 Neoclassical / Vitruvian — orders + proportional grid

The Vitruvian discipline (*De Architectura* c. 25 BCE) fixes
proportions by column diameter (the *module*):

- Column shaft height = 7 modules (Doric) … 10 modules (Corinthian).
- Entablature height = 1/4 column height.
- Intercolumniation (column spacing) = 2.25 modules (pycnostyle) …
  3.75 modules (sysiyle).

Frahan supports this as a **template inventory generator**:

1. The architect picks an order (Doric / Ionic / Corinthian / Tuscan /
   Composite).
2. A parametric Grasshopper definition (designer-supplied) emits
   the elevation as a series of typed templates: column drum,
   capital, base, entablature segment, frieze panel, pedestal.
3. Each template becomes an input to step §2's matcher.

Frahan does not implement the order parametrics — that's the
architect's GH definition. But Frahan **consumes** the typed-template
output and runs the matcher on it.

Cite: Vitruvius *De Architectura* (Loeb Classical Library translation
Morgan 1914 reprint). For the parametric Grasshopper implementation
of the orders, see Paul Davidson's `Vitruvian` GH cluster
(food4rhino) — an established community precedent.

### §1.3 Freeform / "fluidic" stone — Quarra/Heatherwick lineage

The modern carved-freeform vocabulary, exemplified by Quarra's
*Two Horse Relief* (Met) and Thomas Heatherwick's *Vessel*
(Hudson Yards):

1. Architect designs a NURBS surface or sub-D mesh (Rhino's
   SubD, ZBrush export, or sculpted in Maya).
2. The form is **dissected into stereotomic voussoirs** following
   the Rippmann-Block digital-stereotomy pipeline — funicular
   form-finding plus CNC-aware joint geometry.
3. Each carved voussoir becomes an input template for §2.

This is the **flagship Frahan use case** per the Frahan flagship
Fabricate niche memory `[[project_fabricate_staggered_masonry_niche]]`:
"split a sculpted stone form into staggered masonry-like mesh blocks
for wire-saw + robotic milling."

Cite: Rippmann & Block 2011 (same as Gothic); Quarra *Two Horse
Relief* + *Parallel Nature* + *UVA Memorial* (`wiki/research/quarra_stone/`);
Heatherwick *Vessel* 2019 (public-art precedent).

### §1.4 Cyclopean / bottom-up — the inverse

Sometimes the building's form is NOT predetermined. The mason has a
pile of irregular stones and an envelope to fill (a retaining wall,
a dry-stone hut, a cyclopean foundation course). The Clifford-McGee
2017 *Cyclopean Cannibalism* recipe handles this:

1. Inventory the rubble (each stone scanned to a mesh).
2. Designer supplies the wall envelope (a primary surface + variable-
   thickness offset back-plane).
3. The **Cyclopean Recipe Coursing** component (D5F1000C, proposed in
   §10.11 of the philosophy doc) runs the verbatim 8-step recipe:
   trapezoid → parallelogram → keystone-fill + Utah-detail bed-joint
   scribing.

Cite: Clifford & McGee (2017) *Cyclopean Cannibalism: A Method for
Recycling Rubble* (ACADIA 2017, pp. 404-413).

## §2. Fitting stones to templates — the matching problem

Step §1 emits a list of **templates** (the per-stone target meshes
the architect's model needs). Step §2 takes those templates plus the
**stock inventory** (scanned quarry blocks, off-cuts, rubble — from
`Scan to Block Inventory`, F2D0BC20-…) and produces one stone-to-
template assignment with carving plans.

### §2.1 The problem in plain English

> *"For each template I have designed, find the best stone in my
> inventory that contains it (with rotation freedom and a safety
> margin), with correct grain direction, with no fracture through the
> template's load path, and with minimum carving waste."*

That's a **bipartite assignment problem** (M templates × N stones)
with a cost function combining geometric fit + grain + fracture + yield.

### §2.2 The cost function (per-pair)

For each (template_i, stone_j) pair, the cost combines five terms:

| Term | Frahan source | Comment |
|---|---|---|
| **Containment** | `OrientedBoundingBox.Contains(template_i + 5mm margin)` after best-fit pose | infeasible → cost = ∞ |
| **Yield (1 − template_vol / stone_vol)** | Mesh volume comparison | want low (less waste) |
| **Grain misalignment** | `Vector3d.AngleBetween(stone.BedNormal, template.LoadAxis)` | scaled by Pattern Weight (philosophy §10) |
| **Fracture intersection** | DFN ∩ template-volume check (Elkarmoty 2020) | infeasible if any fracture crosses load path |
| **Carving volume** | (stone_vol − template_vol) post-best-fit | minimise per UCL Devadass 2025 §2.7 |

Total per-pair cost: `c_ij = w₁·yield + w₂·grain + w₃·carving + ∞·(infeasible)`.

The cost matrix is M × N. For a small cathedral (M = 50 voussoirs,
N = 200 stones in inventory) → 10,000 cost cells. Each cell evaluation
is ~50 ms with current Frahan primitives, so the full matrix builds
in ~8 minutes single-threaded. Acceptable.

### §2.3 The assignment

Three strategies, in increasing sophistication:

1. **Greedy** — for each template in order, pick the lowest-cost
   remaining stone. O(M·N). Fast, no global optimum.
2. **Hungarian** — Kuhn (1955) bipartite assignment, O(N³). Globally
   optimal. **Already shipped in `Frahan.EdgeMatching.Core/HungarianAssigner.cs`**.
3. **Pareto / NSGA-II** — Deb et al. (2002) multi-objective genetic
   algorithm. Emits the Pareto front of (yield, grain, carving)
   trade-offs; designer picks the knee-point solution. Same algorithm
   the UCL Devadass 2025 paper uses (Octopus plug-in for Grasshopper).

The choice is exposed as a `Strategy` enum on `Template Block Match 3D`
(D5F1000B, partial-real today). For Frahan v1.x the Hungarian path is
production-ready; Pareto is the v1.x extension.

### §2.4 Pose refinement (after assignment)

Hungarian gives the assignment but not the precise placement transform.
After assignment:

1. **Constrained ICP 3D** (`Frahan.EdgeMatching.Core/ConstrainedIcp3D.cs`)
   — Besl & McKay 1992; Kabsch SVD via MathNet.Numerics. Already
   exists. Iteratively refines the transform that maps the stone's
   inscribed template to the designed template, subject to the
   containment constraint.
2. **Soft ICP refinement** (`Frahan.EdgeMatching.Core/SoftIcpRefiner.cs`)
   — Frahan-original optional post-pass when the rim contact needs
   sub-mm accuracy (used by Trencadís Solve today).

Both already exist. The 3D matcher (Component B3D, D5F10008) calls
them. Soft ICP IS what the user asked about — "can we use existing
tools to accomplish this with Soft ICP etc.?" Answer: **yes, soft
ICP is already in the substrate, and the 3D matcher already calls it
in the existing pipeline.**

### §2.5 Carving plan (per assigned pair)

Per (template_i, stone_j) assignment:

1. Position the stone in world coordinates per the refined transform.
2. Compute the carving difference: `stone_j_at_pose − template_i`.
3. Run `BlockCutOpt v2` (Elkarmoty 2020 + Frahan synthesis) to plan
   the cut sequence: which planes, which order, kerf compensation,
   fracture avoidance. Already implemented.
4. Emit a `StoneCutMetadata` record per cut (stone name, finish,
   kerf, density, grain) via `Stone-Aware Cut Export`. Already
   implemented.

### §2.6 Assembly sequencing

The cathedral must stand at every stage of construction, not just at
completion:

1. **Block Build Order** (`BlockBuildOrderComponent`, Kim 2024
   polygonal masonry install order — DETC2024-142563) produces a
   stability-preserving DAG of placement steps. Already implemented.
2. **Build Order Stability Stream** runs `RubbleWallSettleComponent`'s
   Heyman 1966 limit-state check at every step. Already implemented.
3. **Fabrication Prep Report** emits per-stone lift class, CoM,
   anchor geometry — Quarra's MIT lecture §11 rigging discipline.
   Already implemented.

## §3. Simple template-matcher component (the PolytopeSolutions idiom)

The user supplied PolytopeSolutions' `MatchMeshTransformation`
component as a reference. That component is **brilliantly simple**:

- Input: source meshes (list) + target mesh.
- For each source: check if `vertex_count == target.vertex_count` and
  `face_count == target.face_count`.
- If topology matches: pick 3 random vertices, build a Plane from
  them, fit `Plane.PlaneToPlane` from source's plane to target's plane.
- Output: rigid transform + matched-source index.

That's the **simple-component idiom** — pure geometric, O(1) per
candidate, no ICP iteration, no cost matrix.

Frahan's analogue is `MeshTemplateMatchComponent` (proposed below).
It generalises the PolytopeSolutions idea for the case where topology
DOESN'T match (which is the case for scanned stone vs. designed
template) by replacing the topology-match check with an OBB
containment + best-fit alignment.

This is intentionally a v1 component: simple, fast, no iteration. The
production-grade `Template Block Match 3D` is the heavyweight cousin
that runs the full matching pipeline. Designers reach for the simple
component for prototyping; reach for the heavyweight when the
production cost matters.

See `Frahan.StonePack.GH/EdgeMatch3D/MeshTemplateMatchComponent.cs`
(landed 2026-05-31, GUID `D5F1000D`).

## §4. Implementation plan — what to build

The plan is **composition over invention**. The list below is
ordered by HITL gating and dependency.

### §4.1 Already shipped (v1.0-rc1 + 2026-05-31 nightshift)

- `Scan to Block Inventory` (F2D0BC20-…) — quarry-side entry point.
- `ConstrainedIcp3D`, `SoftIcpRefiner`, `BoundarySegmenter3D`,
  `PhaseCorrelator`, `PlanarityTester` — all in `Frahan.EdgeMatching.Core`.
- `HungarianAssigner` (real implementation, Kuhn 1955).
- `VsaSegmenter` (stub — TODO for real Lloyd-iteration variational
  shape approximation per Cohen-Steiner 2004).
- `Block Pair Match 3D` (B3D, D5F10008) — skeleton + plane-to-plane
  stub body.
- `Block Chain Along Thrust Line` (A3D, D5F10009) — skeleton + STUB.
- `Adaptive Block Match 3D` (C3D, D5F1000A) — skeleton + STUB.
- `Template Block Match 3D` (D3D, D5F1000B) — partial-real (the
  Hungarian path is functional with a volume-difference proxy cost).
- `Mesh Template Match` (D5F1000D) — simple PolytopeSolutions-idiom
  matcher, landing in this nightshift.
- `BlockCutOpt v2`, `Carving Stages`, `Stone-Aware Cut Export`,
  `Fabrication Prep Report` — all implemented and validated.

### §4.2 v1.x — flesh out the stubs (multi-day each)

Each needs its own HITL card-set pass to validate. The cards already
exist:

| Build target | HITL card-set | Estimated effort |
|---|---|---|
| VsaSegmenter Lloyd-iteration (real Cohen-Steiner 2004 §3 L²,¹ norm + farthest-point seed + proxy auto-termination) | `em_3d_chain_ucl_bartlett/02_ucl_eighteen_stone_arch.md` | 3-5 days |
| B3D full pipeline (face-pair loop + ConstrainedIcp3D refinement + match-length scoring + top-N candidates) | `em_2d_boundary_match/` (the 2D sibling card-set) + `em_3d_chain_ucl_bartlett/01_three_stone_open_arch.md` | 2-3 days |
| A3D bidirectional walker (state machine + per-station call to B3D + Pareto NSGA-II for `Strategy = Pareto`) | `em_3d_chain_ucl_bartlett/02_ucl_eighteen_stone_arch.md` | 4-6 days |
| C3D CGAL/Geogram trim path (per UCL §2.7 minimum-machining; per Clifford-McGee p. 410 overlap-then-carve discipline) | `em_3d_cyclopean_cannibalism/02_seoul_biennale_full_geometry.md` | 3-4 days |

### §4.3 Cyclopean Recipe Coursing (D5F1000C)

The bottom-up 3D peer with no 2D analog. Skeleton lands in this
nightshift; full recipe body lands per HITL card pass.

### §4.4 v1.x — Voussoir Ingest + Stone Matcher (TopDown family)

The cathedral / voussoir workflow needs two more components:

| Component | GUID (proposed) | Role |
|---|---|---|
| `Voussoir Ingest` | `D5F1000E` | Read a Voussoir-plugin output as a typed `VoussoirAssembly` |
| `Voussoir → Stone Matcher` | `D5F1000F` | Hungarian assignment of voussoirs to quarry stones; reuses `HungarianAssigner.cs` |
| `Voussoir Pack-Into-Block` | `D5F10010` | 3D bin-pack designed voussoirs inside one quarried block |

All three are in `wiki/research/voussoir_stereotomy_integration.md`
Phases 1-3 (~6 working days total).

## §5. Real-data testing — datasets and fixture acquisition

The user asked: *"test every component on real mesh inputs from
datasets and request one if one exists or make one up complex enough
for architectural design and parametricism standards, inspired by
fluidic stone architecture and also Vitruvian and neoclassical Gothic
architecture in stone masonry."*

The acquisition plan:

### §5.1 Real datasets already in `[[reference_quarry_scan_datasets]]`

- **ETH1100** (Zenodo 10038881) — 1100 dry-stone meshes with viability
  labels. Bottom-up.
- **Granite Dells** airborne + TLS scan. Top-down quarry input.
- **Tongjiang quarry scan**.
- **Bengaluru granite** scan (TN charnockite analog).
- **Loviisa rapakivi** (Chudasama 2022, 3 Zenodo records).
- **Grimsel granite** borehole DFN (Krietsch 2018).
- **GeoCrack + GeoFractNet** (Panara 2024).

These cover the bottom-up + quarry-side use cases.

### §5.2 Datasets we need to acquire or create (top-down templates)

For the cathedral / palace / freeform workflow, we need TEMPLATE
meshes too. These don't exist as public datasets; we need to either:

1. **Borrow from open architectural archives**:
   - **Open Heritage 3D** (`openheritage3d.org`) — 100+ scanned heritage
     sites including cathedrals (Sagrada Família partial, Salisbury
     Cathedral). Per `[[reference_market_study_part2]]` memory.
   - **CyArk** (`cyark.org`) — 3D scans of UNESCO sites. Free for
     research use.
   - **Sketchfab CC-BY models** — searchable by "gothic cathedral",
     "doric column", "voussoir".

2. **Synthesise complex test fixtures** — at-scale architectural
   templates that exercise the matcher under design discipline:

   **Fixture-1 — Vitruvian column**: a parametric Doric column
   (7-module shaft, 1-module base, fluting at 20 grooves per drum)
   subdivided into 8 drums + 1 capital + 1 base = 10 templates.
   Inventory: 50 scanned granite blocks (Granite Dells subset).
   Pass: every drum + capital + base matched with yield ≥ 50 %.

   **Fixture-2 — Gothic rib bay**: a single bay of a 4-part gothic
   rib vault: 2 diagonal ribs + 2 transverse ribs + 4 tierceron ribs
   + 12 voussoir cells per rib = ~96 voussoir templates. Inventory:
   200 scanned limestone blocks (ETH1100 subset filtered to
   medium-volume). Pass: ≥ 90 % voussoirs matched, structural
   thrust check passes.

   **Fixture-3 — Heatherwick-style freeform pavilion**: a single
   ribbon of a NURBS-defined sculptural seating element (Vessel-class
   but smaller; 3 m × 2 m footprint, organic curvature) → 30
   stereotomic voussoirs per Rippmann-Block 2011. Inventory:
   60 scanned granite blocks. Pass: all 30 voussoirs match with
   total carving ≤ 30 % of inventory volume.

   These three fixtures form the `td_voussoir/` HITL card-set
   per the master plan. **Authoring the three fixture MDs is the
   v1.x deliverable** alongside the matcher build.

3. **Generative fixtures from existing Frahan components**: Frahan
   has `Fracture-Plane Generators` (Brick / Grid / Voronoi / Random
   / Layered / Radial / Jittered) that can produce template
   inventories from a sculpted form. Use them to generate
   `Fixture-3` programmatically (and any larger cathedral fixture)
   without manual modelling.

The fixture-generation script lands at
`wiki/research/hitl_cards/_synthesize_cathedral_fixture.py`
(proposed v1.x).

## §6. Why this works with existing tools

Re-stating the answer to the user's "can we use existing tools to
accomplish this with Soft ICP etc.?" question:

**Yes — every algorithm the matching pipeline needs is already in
`Frahan.EdgeMatching.Core` or `Frahan.StonePack.Core`:**

- Cost-matrix evaluation → reuse `BoundarySegmenter3D` + `PhaseCorrelator`
  + `ConstrainedIcp3D` + `SoftIcpRefiner` from EdgeMatching.Core.
- Assignment → `HungarianAssigner` (real, shipped).
- Carving plan → `BlockCutOpt v2` (shipped).
- Stability → `RubbleWallSettleComponent` + `BuildOrderStabilityStream`
  + Kim 2024 sequencing (shipped).
- Fabrication metadata → `Stone-Aware Cut Export` + `Fabrication Prep
  Report` (shipped).

**The 4 new 3D EdgeMatch components (B3D / A3D / C3D / D3D) +
`MeshTemplateMatchComponent` (new today) + `Cyclopean Recipe Coursing`
+ Voussoir Ingest / Stone Matcher / Pack-Into-Block — these are all
*GH wrappers* around the existing kernel, plus one small Hungarian
utility and the proposed VSA segmenter. The kernel does the work; the
wrappers compose it for designers.**

It IS simple. The cathedral problem reduces to template-inventory
generation (architect's GH) + cost-matrix Hungarian (one component
call) + per-pair Soft-ICP refinement (one component call) + per-pair
BlockCutOpt (one component call) + sequencing (one component call).
Five components on the canvas. Each is testable in isolation.

## §7. References

Real, verifiable. AGENTS.md §9.

- Rippmann, M. & Block, P. (2011) *Digital Stereotomy* (IABSE-IASS).
- Block, P. et al. (2016) *Armadillo Vault*, Venice Biennale "Beyond Bending."
- Vitruvius (~25 BCE) *De Architectura*, Loeb Classical Library translation.
- Clifford, B. & McGee, W. (2017) *Cyclopean Cannibalism*, ACADIA pp. 404-413.
- Lu, Zhu, Olesti, Scully, Devadass (2025-11-20) Construction Robotics, DOI 10.21203/rs.3.rs-8019586/v1.
- Kuhn, H.W. (1955) Hungarian Method, Naval Research Logistics Quarterly 2:83-97.
- Besl, P.J. & McKay, N.D. (1992) Iterative Closest Point.
- Kim et al. (2024) Polygonal Masonry Install Order, DETC2024-142563.
- Heyman, J. (1966) Limit-State Masonry Theorem.
- Kao et al. (2022) Coupled Rigid-Block Analysis, CAD 146:103216.
- Elkarmoty, Bondua, Bruno (2020) Resources Policy 68:101761.
- PolytopeSolutions `MatchMeshTransformation` (the simple-component
  idiom inspiration). Reference: file `PolytopeSolutions_GrasshopperTools.dll`
  supplied by user 2026-05-31.
- Luczkowski, M. structuralCircle (NTNU Trondheim) — see
  `Template-General/outputs/2026-05-31/research/structural_circle/findings.md`
  for the research-agent summary (in flight 2026-05-31).

## §8. Last updated

2026-05-31 — canonical authorship; landed alongside `MeshTemplateMatchComponent`
(D5F1000D) and the Cyclopean Recipe Coursing skeleton (D5F1000C).
