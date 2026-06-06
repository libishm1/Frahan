# Frahan StonePack — HITL Cards Master Plan

**Location:** `wiki/specs/hitl_cards_master_plan.md`
**Status:** canonical · authored 2026-05-31 per Libish directive: *"don't
make ghost components, make components that are sensible and grounded in
design principles. Generate all of the HITL cards for Grasshopper for all
the possible design workflows. Use existing meshes from ETH quarry
datasets. I am expecting Grasshopper files to be complete with design
sensibility."*
**Authority:** `wiki/specs/frahan_design_philosophy.md`,
`[[feedback_hitl_cards_design_grounded]]`.
**Companion:** `wiki/specs/release_plan/frahan_hitl_backlog.md`.

---

## §0. The discipline

Every HITL card-set listed below honours four rules per
`[[feedback_hitl_cards_design_grounded]]`:

1. **Design problem statement** with a named precedent (paper, project,
   building, fabricator).
2. **Explicit numeric tolerance** that derives the pass criterion.
3. **Real dataset fixture** — ETH1100, Granite Dells, Tongjiang,
   Bengaluru granite, Loviisa rapakivi, Grimsel, GeoCrack, or the
   reference reference cache. No synthetic-only inputs for
   architect-facing components.
4. **Cross-reference** to the Frahan philosophy doc + the external
   precedent it demonstrates.

The 110 auto-generated cards from 2026-05-24 (per
`[[project_hitl_cards_all]]`) are grandfathered — they remain as
isolation tests. The card-sets below are the **design-demonstrator**
layer that sits on top.

---

## §1. Dataset registry — what each fixture pulls from

| Dataset | Path on disk | Best for | Origin |
|---|---|---|---|
| **ETH1100** dry-stone | `D:/code_ws/reference/eth_drystone/` (3 GB, gitignored) | Bottom-up rubble + Trencadís 2D + 3D | Zenodo 10038881; ETH Gramazio Kohler |
| **Granite Dells** | `D:/code_ws/reference/granite_dells/` (gitignored) | Quarry scan, top-down inventory | Open dataset, airborne + TLS |
| **Tongjiang quarry scan** | `D:/code_ws/reference/tongjiang/` | Top-down quarry-to-block | China granite quarry |
| **Bengaluru granite** | `D:/code_ws/reference/bengaluru_granite/` | TN charnockite analog, top-down | India granite |
| **Loviisa rapakivi** | (Zenodo 3-record) | Fracture / DFN input | Chudasama 2022 |
| **Grimsel granite borehole DFN** | (CC-BY 4.0) | Fracture-aware cutting | Krietsch 2018 |
| **GeoCrack + GeoFractNet** | `D:\dataverse_files.zip` | Fracture digitisation | Panara 2024 |
| **HITL fixtures.3dm** | `Template-General/outputs/2026-05-24/hitl_cards/fixtures.3dm` | Quick synthetic primitives | Auto-generated |

Full inventory: `wiki/index/data_assets_inventory.md` per
`[[reference_large_data_assets]]`.

---

## §2. Top-Down spine — design-driven workflows

### §2.1 TD-VOUSSOIR — Voussoir Ingest → Stone Matcher → Cut Plan

| Field | Value |
|---|---|
| **Design problem** | Architect designs a freeform stone vault as a discretised voussoir set (Voussoir GH plugin output). Find the smallest scanned quarry block per voussoir that contains it with rotation freedom and a 5 mm safety margin. Cite: Rippmann & Block, *Digital Stereotomy* (IABSE-IASS 2011); Block Research Group *Armadillo Vault* (Venice Biennale 2016). |
| **Tolerance** | Voussoir oriented bounding box (OBB) + 5 mm margin ⊆ block convex hull. Rotation: any. Pass = 100 % of voussoirs assigned to a block; reject if any unassignable. |
| **Dataset** | Voussoir set = Frahan's `Stereotomic Vault Mode` output OR Voussoir-plugin sample. Stones = ETH1100 (top-N largest meshes by volume) OR Granite Dells block segments. |
| **Components** | `Voussoir Ingest` (proposed) → `Stone Matcher` (proposed; Hungarian) → `Stone-Aware Cut Export` (existing). |
| **Card-set** | `wiki/research/hitl_cards/td_voussoir/` — three fixtures: 12-voussoir flat arch, 24-voussoir rib, 60-voussoir freeform shell. |
| **Status** | Spec exists at `wiki/research/voussoir_stereotomy_integration.md`; cards proposed. |

### §2.2 TD-TEMPLATE-FLOOR — Designed multi-cell template → inventory match

| Field | Value |
|---|---|
| **Design problem** | Designer draws an opus-sectile floor template — 20 closed planar cells of irregular shape on the XY plane. Quarry yields 25 slab tiles of varied shape. Assign one tile per cell minimising total Hausdorff residual; report unassigned cells + unused tiles. Cite: opus sectile (Roman/Byzantine art history); parquetry (industry standard); Quarra *Parallel Nature* off-cut matching workflow. |
| **Tolerance** | Per-cell Hausdorff residual ≤ 2 mm for a 1 m cell; pass if ≥ 90 % of cells assigned within tolerance. |
| **Dataset** | Template = synthetic 20-cell layout from Frahan `Random Rubble Pack` output (a real prior packing run). Tiles = ETH1100 subset (50 mesh tiles, scaled to slab dimensions). |
| **Components** | `Template Panel Match` (Component D, `D5F10007` proposed) using Hungarian strategy. |
| **Card-set** | `wiki/research/hitl_cards/td_template_floor/` — three fixtures per `edge_matching_redesign.md` §11.7: 5-slot live-edge plank floor, 8-slot opus sectile (under-provisioned), 4-slot with trim. |
| **Status** | Component D spec in `edge_matching_redesign.md` §11; cards proposed. |

### §2.3 TD-DEVIATION — Scan to Compensated Surface (F18)

| Field | Value |
|---|---|
| **Design problem** | Quarry block cut to a designed bas-relief; first machining pass leaves measured deviation from the design model. Scan, best-fit, compute deviation map, reverse to a compensated surface, run a corrective pass. Reduce average point-alignment error from ~0.5 mm to ~0.15 mm (Quarra's reported 62 % reduction). Cite: Quarra *City Jeff* + *Emanuel 9 Memorial* (MIT *Out of Frame* lecture 2025-10-24, `wiki/research/quarra_stone/quarra_stone_presentation.md` §10, §15.3). |
| **Tolerance** | Mean deviation reduction ≥ 50 % between pre- and post-correction passes. Pass = headline Quarra benchmark met or beaten on synthetic deviation field. |
| **Dataset** | Reference mesh = Granite Dells boulder OR Bengaluru granite scan; synthetic deviation = random Gaussian perturbation of vertices at σ = 1 mm. |
| **Components** | NEW (F18 row in `wiki/specs/release_plan/frahan_quarra_lecture_implications.md` §4.2): `Scan Import` → `Best-Fit to Model` → `Deviation Map` → `Reverse to Compensated Surface` → `Corrective Path Geometry`. |
| **Card-set** | `wiki/research/hitl_cards/td_deviation_loop/` — three fixtures: low-deviation (σ=0.5 mm), high-deviation (σ=2 mm), localised hotspot (single mesh region offset 5 mm). |
| **Status** | F18 unbuilt; this card-set is the v1.1 build target. |

### §2.4 TD-BLOCKCUTOPT — Pareto multi-objective quarry cut

| Field | Value |
|---|---|
| **Design problem** | Quarry operator has a fractured granite block (DFN from GPR / scanline). Find cut planes that yield max volume of fracture-free commercial-sized blocks, minimise cutting-surface area (Jalalian BCSdbBV cost), and respect bed/grain direction. Cite: Mutlu 2007 BlockCutOpt; Jalalian 2024 BCSdbBV; Marvie Reed & Bondua 2025 SLR §3.2 (`wiki/research/stone_cutting_optimization_soa/soa_survey_dossier.md`). |
| **Tolerance** | Achieve Pareto front of solutions; manual selection. Per-axis: volume yield ≥ 30 %, fracture intersections = 0 in any selected block, cutting surface area within 80 % of theoretical minimum. |
| **Dataset** | Fractured block = Loviisa rapakivi granite (Chudasama 2022, 3 Zenodo records) OR synthetic DFN per `[[project_granite_open_datasets]]`. |
| **Components** | `Frahan.BlockCutOpt` v2 (existing, per `[[project_blockcutopt_synthesis]]`) with Pareto solver. |
| **Card-set** | `wiki/research/hitl_cards/td_blockcutopt_pareto/` — three fixtures: low-fracture density (10 fractures), medium (30), high (60). |
| **Status** | BlockCutOpt v2 implemented; Pareto fourth-axis (I11 / Jalalian) per memory. Cards proposed. |

### §2.5 TD-CARVE — Carving Stages on designed sculptural form

| Field | Value |
|---|---|
| **Design problem** | Artist supplies a sculptural mesh (designed in Rhino, ZBrush, or Maya). Subdivide carving into stages (rough → semi-finish → finish), with smoothed normals + per-vertex offset cap + AABB-clamp per Quarra's *Two Horse Relief* multi-pass methodology. Cite: Quarra *Two Horse Relief* (Met) — `wiki/research/quarra_stone/quarra_stone_presentation.md` §12.5; Borrowed Earth *Wood Ridge* contour sculpture — `wiki/research/borrowed_earth/borrowed_earth_collective_presentation.md` §5. |
| **Tolerance** | Final-stage Hausdorff to design ≤ 0.5 mm on flat regions, ≤ 2 mm on high-curvature regions. Per-vertex offset ≤ 0.5 × shortest-edge to prevent self-intersection. |
| **Dataset** | Designed mesh = Quarra *City Jeff* / *Two Horse Relief* reference geometry (synthetic stand-in); Block AABB derived from Granite Dells. |
| **Components** | `Carving Stages` (existing, sync per `[[feedback_gh_async_vs_sync]]`); Block AABB-clamp + smoothed normals (2 Laplacian iters) + per-vertex cap. |
| **Card-set** | `wiki/research/hitl_cards/td_carve_stages/` — three fixtures: simple block-to-bas-relief, mid-complexity figurative, high-curvature freeform. |
| **Status** | Implemented v1.0-rc1; cards proposed. |

### §2.6 TD-SLABCUT — SlabCutOpt slab-yield optimisation

| Field | Value |
|---|---|
| **Design problem** | Quarry block yields N parallel slabs of thickness T with kerf K; maximise slab count while avoiding fracture planes. Cite: Marvie Reed & Bondua 2025 SLR §3.2 (SlabCutOpt named tool); Bondua's earlier SlabCutOpt papers (Bologna lineage). |
| **Tolerance** | Slab count within 95 % of fracture-free theoretical max. Pass if at least one fracture-aware slab plan is emitted with valid kerf compensation. |
| **Dataset** | Block = Granite Dells single quarry segment OR Bengaluru granite scan; fractures = synthetic plane set. |
| **Components** | `FrahanSlabYieldOptimizer` (existing) + `Stone-Aware Cut Export`. |
| **Card-set** | `wiki/research/hitl_cards/td_slabcut/` — three fixtures: no-fracture, single-plane, multi-plane. |
| **Status** | Implemented; cards proposed. |

### §2.7 TD-3D-CHAIN — UCL Bartlett 18-stone arch reproduction

| Field | Value |
|---|---|
| **Design problem** | Reproduce the UCL Bartlett three-legged limestone arch end-to-end inside Frahan. Designer-supplied parabolic thrust line + 50 scanned limestone fragments → VSA segmentation + face-library evaluation + MOO/Pareto generative aggregation + minimum machined-joint modifications → 18-stone arch standing. Cite: Lu, Zhu, Olesti, Scully, Devadass, *Construction Robotics* / ResearchSquare preprint 2025-11-20, DOI `10.21203/rs.3.rs-8019586/v1` — see `wiki/specs/frahan_design_philosophy.md` §10.9 + `wiki/research/design_sensibility/external_precedents.md` §5.5. |
| **Tolerance** | Endpoint deviation from designed thrust-line endpoint ≤ 50 mm at 2.5 m span; Cg deviation from thrust line ≤ 30 mm per stone; cumulative angle deviation ≤ 5°. (UCL's reported tolerances.) |
| **Dataset** | 50 limestone fragments = ETH1100 subset filtered by volume (~50-200 L per piece). Thrust line = synthetic parabolic curve. |
| **Components** | `Scan to Block Inventory` → `VsaSegmenter` (NEW utility) → `Block Pair Match 3D` (Component B3D, NEW) → `Block Chain Along Thrust Line` (Component A3D, NEW with Pareto AssemblySolver strategy) → `Adaptive Block Match w/ minimal-cut trim` (Component C3D, NEW). |
| **Card-set** | `wiki/research/hitl_cards/td_3d_chain_ucl/` — three fixtures: 3-stone open arch, 18-stone three-legged arch (UCL replica), mixed inventory with rejects. |
| **Status** | Spec in `frahan_design_philosophy.md` §10.9; components unbuilt (v1.x backlog EM-3D-A/B/C + EM-3D-MOO + EM-3D-VSA). |

---

## §3. Bottom-Up spine — material-driven workflows

### §3.1 BU-TRENCADIS — EdgeMatch Solve fragment reassembly

| Field | Value |
|---|---|
| **Design problem** | Designer has a closed boundary (room outline, table top, façade panel) and a stock of irregular ceramic / stone shards. Place every shard inside the boundary with no overlap and minimum joint residual; let the aesthetic emerge from the shard fits. Cite: Gaudí *Park Güell Trencadís* (1900-1914); IAAC MRAC RoboMosaic (2022-23) — `wiki/research/design_sensibility/external_precedents.md` §4.2. |
| **Tolerance** | Total inter-shard joint residual (Hausdorff) ≤ 5 mm average; boundary coverage ≥ 95 %. |
| **Dataset** | Boundary = synthetic 2 × 1 m rectangle OR ETH1100-derived planar projection. Shards = 30-50 irregular planar polygons from ETH1100 silhouettes. |
| **Components** | `EdgeMatch Solve` → renaming `Trencadis Assembly Solve` (`D5F10001`). |
| **Card-set** | `wiki/research/hitl_cards/bu_trencadis/` — three fixtures: open boundary (15 shards), closed rectangle (30 shards), irregular boundary (50 shards). Plus existing `outputs/2026-05-30/hitl_cards/edge_matching_v2/` as the isolation harness. |
| **Status** | Implemented; v2 isolation cards exist; design-grounded cards proposed. |

### §3.2 BU-PANEL-RAIL — Panel Match Along Rail (re(al)form lineage)

| Field | Value |
|---|---|
| **Design problem** | Carpenter / architect has 12 live-edge wood planks (or stone tiles) with natural meandering edges. Lay them along a 5 m rail to make a floor or façade where seams flow with the natural-edge irregularities. Cite: IAAC MRAC re(al)form (2022-23) by Gottschild / Cobanoglu / Naguib / Siebenaler, faculty Papandreou / Huyghe — `blog.iaac.net/realform/`. |
| **Tolerance** | Per-station edge-match Hausdorff ≤ 3 mm; rail length covered ≥ 95 %; no unplaced inventory if inventory length ≥ rail length. Bidirectional walker per OQ1 lock. |
| **Dataset** | Rail = 5 m straight line. Planks = ETH1100 subset projected to 2D outlines (~12 pieces, length 30-60 cm each). |
| **Components** | `Panel Match Along Rail` (Component A, `D5F10004` proposed). |
| **Card-set** | `wiki/research/hitl_cards/bu_panel_rail/` — three fixtures per `edge_matching_redesign.md` §3.7: simple strip, mixed widths, notch rail. |
| **Status** | Spec in `edge_matching_redesign.md` §3; component proposed (backlog `EM02`). |

### §3.3 BU-BOUNDARY-MATCH — Atomic two-piece match

| Field | Value |
|---|---|
| **Design problem** | Two scanned stone fragments. Find the relative pose where their edges best mate (longest matching contour, lowest Hausdorff). Foundational primitive for all assembly workflows. Cite: Kintsugi reassembly tradition; UCL Devadass §5.5 §2.4 face-library evaluation. |
| **Tolerance** | Match Hausdorff residual ≤ 5 mm on a 50 cm fragment; correct match identified for ≥ 80 % of test pairs. |
| **Dataset** | Pairs = ETH1100 fragment pairs from known-good assemblies (viability label = TRUE per dataset). |
| **Components** | `Boundary Match` (Component B, `D5F10005` proposed); atomic pair-matcher. |
| **Card-set** | `wiki/research/hitl_cards/bu_boundary_match/` — three fixtures per `edge_matching_redesign.md` §4.7: notch & bump, no-match (negative case), multi-match (closed rims). |
| **Status** | Spec in `edge_matching_redesign.md` §4; proposed (backlog `EM01`). |

### §3.4 BU-ADAPTIVE-TRIM — Adaptive Panel Match with minimal trim

| Field | Value |
|---|---|
| **Design problem** | Live-edge wood floor or off-cut stone fit: pieces are slightly oversized for their slot. Trim minimally (straight cut for stone/saw; polyline edit for wood/router) so the piece fits. Cite: Clifford & McGee 2017 *Cyclopean Cannibalism* p. 410: *"doesn't minimize the space between parts, but has to remove it entirely"* — see `wiki/specs/frahan_design_philosophy.md` §10.11. Industry: live-edge wood flooring. |
| **Tolerance** | Trim area ≤ 10 % of piece area; post-trim joint residual ≤ 2 mm; trim type respected (straight = single straight line; polyline ≤ 6 vertices). |
| **Dataset** | Slot = closed curve (1 m × 0.3 m rectangle). Pieces = 10 ETH1100 fragments scaled to slightly oversized. |
| **Components** | `Adaptive Panel Match` (Component C, `D5F10006` proposed). |
| **Card-set** | `wiki/research/hitl_cards/bu_adaptive_trim/` — three fixtures per `edge_matching_redesign.md` §5.7: slight-oversize, too-oversize (rejection), notched-match-plus-trim. |
| **Status** | Spec in `edge_matching_redesign.md` §5; proposed (backlog `EM03`). |

### §3.5 BU-RUBBLE-PACK — Random Rubble Pack inside a boundary

| Field | Value |
|---|---|
| **Design problem** | Mason has a pile of rough rubble and a wall boundary. Pack rubble inside the boundary with no overlap and stable contact, no aesthetic target (purely structural). Cite: dry-stone wall vernacular (UK/Ireland); Gramazio Kohler *Autonomous Dry Stone* (Johns et al. 2020-2022) `wiki/research/design_sensibility/external_precedents.md` §1.3. |
| **Tolerance** | Packing density ≥ 70 % volume fill within boundary; no piece overlapping another; per-piece stability via CoM-over-support-poly check. |
| **Dataset** | Boundary = 2 × 2 m wall front-face. Rubble = ETH1100 200-piece subset. |
| **Components** | `Random Rubble Pack` (existing). |
| **Card-set** | `wiki/research/hitl_cards/bu_rubble_pack/` — three fixtures: small (50 pieces), medium (200 pieces), large (500 pieces). |
| **Status** | Implemented (per `[[project_masonry_workflow_status]]`); cards proposed. |

### §3.6 BU-DROP-SETTLE — Rubble Drop-Settle physics

| Field | Value |
|---|---|
| **Design problem** | Mason drops irregular boulders one at a time onto a flat bed; gravity + friction settle them into stable contact. Top boulders rest on lower ones; emergent course-like structure forms. Cite: Furrer et al. 2017 IROS *Autonomous Robotic Stone Stacking*; ETH Autonomous Dry Stone (§1.3 external_precedents). |
| **Tolerance** | All boulders rest with CoM over support polygon; settled mesh max penetration ≤ 1 mm; stability score (per `[[project_rubble_drop_settle]]`) ≥ 0.8. |
| **Dataset** | Boulders = ETH1100 25-piece subset of medium size. Bed = flat 3 × 3 m XY plane. |
| **Components** | `Rubble Drop-Settle` (existing, signed-off 2026-05-25 per memory). |
| **Card-set** | `wiki/research/hitl_cards/bu_drop_settle/` — three fixtures: 5 boulders (validation), 25 boulders (medium load), 100 boulders (stress test). |
| **Status** | C# port in flight per `[[project_rubble_drop_settle]]`; cards proposed. |

### §3.7 BU-KINTSUGI — Fracture-line reassembly

| Field | Value |
|---|---|
| **Design problem** | Broken sculpture (or fractured slab) into N fragments. Reassemble them into the original object using surface-feature matching + verifier scoring. Cite: Kintsugi (Japanese repair tradition); PuzzleFusion++ (PotNet 2024); `[[project_kintsugi_port_pose_composition]]`. |
| **Tolerance** | Reassembled pose Hausdorff to ground truth ≤ 2 mm; verifier score ≥ 0.5 (threshold per memory). |
| **Dataset** | Fractures = ETH1100 (curated subset with known reassembly) OR synthetic mesh-cut into 5-15 pieces. |
| **Components** | `Kintsugi Settle Contact` + PotNet pose pipeline + Verifier. |
| **Card-set** | `wiki/research/hitl_cards/bu_kintsugi/` — three fixtures: 5-piece clean break, 10-piece with rotation, 15-piece with missing piece. |
| **Status** | Implemented per `[[project_kintsugi_port_pose_composition]]`; cards proposed. |

### §3.8 BU-SURFACE-VEIN — Surface Packing with vein-direction flow

| Field | Value |
|---|---|
| **Design problem** | Stone tile facade where veins must continuously flow across joint lines (like a slab cut into book-matched pieces). Designer supplies surface + tile inventory with per-tile vein directions; component places tiles so vein direction continuity is preserved at every joint. Cite: Quarra *Sarah Sze pixelated boulders* + bookmatched marble facade tradition. |
| **Tolerance** | Average angular misalignment of vein directions at joints ≤ 10°; coverage ≥ 95 %. |
| **Dataset** | Surface = 3 × 2 m flat panel. Tiles = ETH1100 subset (20 pieces) with synthetic vein directions (single per-piece Vector3d). |
| **Components** | `Surface Chart` + `Pack On Surface` + v1.x pattern-matching hook per `edge_matching_redesign.md` §10. |
| **Card-set** | `wiki/research/hitl_cards/bu_surface_vein/` — three fixtures: vein-aligned (easy), vein-mixed (medium), bookmatched (hard). |
| **Status** | Surface packing implemented; vein hook is v1.x extension. Cards proposed. |

### §3.9 BU-ASHLAR — Ashlar Pack (the only validated v1.0 packer per memory)

| Field | Value |
|---|---|
| **Design problem** | Ashlar masonry: rectangular cut blocks in horizontal courses with vertical joints staggered, varying block lengths. The most rule-bound bottom-up mode; aesthetic = strict orderliness. Cite: ashlar masonry vernacular; `[[project_masonry_workflow_status]]` confirms this is one of the two v1.0-validated packers. |
| **Tolerance** | Course-line horizontality ≤ 1° per row; vertical joints offset ≥ 30 % of block length between adjacent rows; no overlap; ≥ 90 % wall coverage. |
| **Dataset** | Wall = 4 × 2.5 m rectangle. Block inventory = synthetic rectangles in 3 sizes (mimicking quarry-cut). |
| **Components** | `Ashlar Pack` (existing). |
| **Card-set** | `wiki/research/hitl_cards/bu_ashlar/` — three fixtures: simple wall, with opening (window/door), corner condition. |
| **Status** | Implemented + validated per memory; cards proposed. |

### §3.10 BU-CYCLOPEAN — Cyclopean Cannibalism recipe reproduction

| Field | Value |
|---|---|
| **Design problem** | Reproduce the Clifford-McGee 2017 Cyclopean Cannibalism wall (Seoul Biennale; 6.6 × 2.3 m, 6896 kg). Sort scanned rubble into trapezoids + parallelograms by VSA-segmentation + face-pair angle; recursive virtual-set per the recipe: large stones first, parallelograms nest into the gaps, upside-down trapezoid as keystone-like fill, Utah-detail bed-joint scribing. Cite: Clifford & McGee 2017 ACADIA pp. 404-413; `wiki/specs/frahan_design_philosophy.md` §10.11. |
| **Tolerance** | Wall coverage ≥ 90 %; stability (CoM-over-support) for every stone; recipe rules satisfied per row (trapezoid → parallelogram → keystone alternation). |
| **Dataset** | Demolition rubble = ETH1100 + synthetic concrete fragments. Wall envelope = 6.6 × 2.3 m. |
| **Components** | NEW (proposed v1.x): `Cyclopean Recipe Coursing` component encoding the rule-set (shape-grammar primitive). Composes `Random Rubble Pack` + `Rubble Drop-Settle` + new `PolygonInscriber` utility. |
| **Card-set** | `wiki/research/hitl_cards/bu_cyclopean/` — three fixtures: 2-course small wall, full Seoul Biennale geometry, irregular form with curved top. |
| **Status** | Proposed v1.x; spec landing in `frahan_design_philosophy.md` §10.11 mapping. |

### §3.11 BU-REMNANT — Borrowed Earth remnant inventory → stone brick

| Field | Value |
|---|---|
| **Design problem** | Quarry / fabricator block-edge offcuts: trim from squaring a slab block. Catalogue offcuts as a typed inventory; convert into stone-brick units in British / European / American standard sizes (dual-finish allowed). Cite: Borrowed Earth Collective CEU `wiki/research/borrowed_earth/borrowed_earth_collective_presentation.md` §7 + §24. |
| **Tolerance** | Stone-brick fit inside parent offcut with ≤ 5 % waste; offcut catalogue accuracy: min-edge / volume / dimensions all within 1 % of ground truth. |
| **Dataset** | Offcuts = ETH1100 small-volume subset (~50 pieces) representing block-edge waste. Brick size table per Borrowed Earth talk. |
| **Components** | NEW (P1 per `wiki/research/borrowed_earth/frahan_implications.md`): `Remnant Inventory` + `Stone Brick From Remnant`. |
| **Card-set** | `wiki/research/hitl_cards/bu_remnant/` — three fixtures: small inventory (10 offcuts), medium (50), full Borrowed Earth-scale (200). |
| **Status** | Proposed v1.1 per Borrowed Earth implications doc. |

---

## §4. Bridges-both layer — workflows that serve both directions

### §4.1 BR-STONE-AWARE-EXPORT — Carry metadata through to CAM

| Field | Value |
|---|---|
| **Design problem** | Fabricator receives a Frahan-generated assembly (top-down OR bottom-up). They need per-piece metadata for CAM: stone name, finish, kerf compensation, bed direction, grain, tool family (carbide / diamond), expected mill time, hand-finish allowance. Cite: Quarra MIT *Out of Frame* lecture 2025-10-24 §1.2 *"selective precision"*; Borrowed Earth §11 *"hand finishing"*. |
| **Tolerance** | All required `StoneCutMetadata` fields populated for every piece; export round-trips through CAM software without metadata loss. |
| **Dataset** | Any prior assembly output (any TD or BU card-set above). |
| **Components** | `Stone-Aware Cut Export` (existing) + D3 metadata-schema extension per `frahan_design_philosophy.md` §10.2. |
| **Card-set** | `wiki/research/hitl_cards/br_stone_aware_export/` — three fixtures: 5 pieces, 50 pieces, 500 pieces with mixed stones / finishes. |
| **Status** | Export implemented; D3 schema extension v1.1. Cards proposed. |

### §4.2 BR-FAB-PREP — Fabrication Prep Report (lift / CoM / anchor)

| Field | Value |
|---|---|
| **Design problem** | Per-piece weight + centroid + lift class + anchor geometry for the fabricator-to-installer handoff. Quarra's *Two Horse Relief* sphere-CoM rigging discipline. Cite: Quarra *City Jeff* + *Two Horse Relief* §11 + §12 of presentation. |
| **Tolerance** | Weight estimate within 5 % of computed volume × density; CoM within 1 % of bounding-box span; lift class (sub-50t / 50-100t / over-100t) correctly tagged. |
| **Dataset** | Any TD or BU card-set output passes through. |
| **Components** | `Fabrication Prep Report` (existing). |
| **Card-set** | `wiki/research/hitl_cards/br_fab_prep/` — three fixtures: small (50 kg pieces), medium (500 kg), large (10 ton Quarra-class). |
| **Status** | Implemented; cards proposed. |

### §4.3 BR-GEO-IMPORT — Georeferenced fracture / quarry-map import

| Field | Value |
|---|---|
| **Design problem** | Geologist supplies shapefile / GeoPackage / GeoTIFF of quarry boundaries + GPR fracture picks + bedding planes with real-world coordinates. Import into Frahan with CRS preserved, attributes intact. Cite: Quarra MIT lecture §3 (quarry maps + provenance tracking); UCL Bartlett geology partnership lineage. |
| **Tolerance** | CRS round-trip preserves geometry within ≤ 1 mm at quarry scale; all attributes present in output; documented local-origin transform to Rhino world. |
| **Dataset** | Loviisa rapakivi (3 Zenodo shapefiles); Grimsel borehole DFN; GeoCrack georeferenced fractures. |
| **Components** | `Frahan Geo Import` (NEW per `frahan_quarra_lecture_implications.md` §4.2 F19). |
| **Card-set** | `wiki/research/hitl_cards/br_geo_import/` — three fixtures: shapefile, GeoPackage, GeoTIFF. |
| **Status** | F19 unbuilt; v1.1 backlog. Cards proposed. |

### §4.4 BR-MESH-SANITIZE — Pre-processing chain

| Field | Value |
|---|---|
| **Design problem** | Raw 3D scan (E57 / LAS / PLY) is dirty: holes, non-manifold edges, flipped normals, duplicate vertices, oversize triangle soup. Sanitize for downstream use (any TD or BU workflow). Cite: standard mesh repair pipeline (Botsch et al. 2010 *Polygon Mesh Processing*); Quarra's "every piece scanned" discipline. |
| **Tolerance** | Output mesh: closed (Euler χ correct), watertight, manifold; vertex count within 10 % of input; principal axes computed correctly per OBB. |
| **Dataset** | Raw scans = E57 / LAS test files at `D:/code_ws/reference/` (gitignored); fallback to ETH1100 pre-cleaned meshes. |
| **Components** | `Mesh Sanitize` + `Load E57 Cloud` + `Scan Reconstruct` (all existing). |
| **Card-set** | `wiki/research/hitl_cards/br_mesh_sanitize/` — three fixtures: clean (validation), hole-y, non-manifold + flipped normals. |
| **Status** | Implemented; cards proposed. |

### §4.5 BR-VOUSSOIR-RAW — Voussoir → Raw Stone match (the user's specific architectural use case)

| Field | Value |
|---|---|
| **Design problem** | The flagship architectural workflow Libish described 2026-05-31: design a 3D building from Platonic-solid geometry → decompose into voussoirs → find the right quarry stones to fit each voussoir → cut. Top-down at form scale; bridges-both at block scale (designed voussoir + scanned raw stone). Cite: Frahan Architecture-Mode workflow (`wiki/specs/frahan_design_philosophy.md` §7.6 and §4); Block Research Group *Armadillo Vault*; UCL Bartlett 18-stone arch. |
| **Tolerance** | Every voussoir matched to a stone with rotation freedom and ≥ 5 mm margin; cutting waste ≤ 30 %; max-cut-surface-area per Jalalian I11 axis minimised. |
| **Dataset** | Voussoirs = Platonic-solid building subdivision OR Frahan Stereotomic Vault Mode output. Stones = Granite Dells + Tongjiang + Bengaluru granite (multi-quarry inventory). |
| **Components** | Architecture-Mode chain: `Platonic Form` (NEW) → `Voussoir Subdivide` → `Scan to Block Inventory` → `Stone Matcher` (Hungarian) → `BlockCutOpt v2` → `Stone-Aware Cut Export` → `Fabrication Prep Report`. |
| **Card-set** | `wiki/research/hitl_cards/br_voussoir_raw_stone/` — three fixtures: 6-voussoir tetrahedron, 30-voussoir dome, 100-voussoir freeform building section. |
| **Status** | Architecture-Mode is the v1.x flagship; multiple sub-components proposed. The unifying card-set. |

---

## §5. Build sequence

### §5.1 Priority tier

**P0 — already built, design-grounded cards CAN be written today:**
TD-CARVE (§2.5), TD-BLOCKCUTOPT (§2.4), TD-SLABCUT (§2.6), BU-TRENCADIS
(§3.1), BU-RUBBLE-PACK (§3.5), BU-DROP-SETTLE (§3.6), BU-KINTSUGI
(§3.7), BU-ASHLAR (§3.9), BR-STONE-AWARE-EXPORT (§4.1), BR-FAB-PREP
(§4.2), BR-MESH-SANITIZE (§4.4). **11 card-sets.**

**P1 — component spec exists, build pending, card MD can be written ahead:**
TD-VOUSSOIR (§2.1), TD-TEMPLATE-FLOOR (§2.2), BU-PANEL-RAIL (§3.2),
BU-BOUNDARY-MATCH (§3.3), BU-ADAPTIVE-TRIM (§3.4), BU-SURFACE-VEIN
(§3.8), BR-VOUSSOIR-RAW (§4.5). **7 card-sets.**

**P2 — fully proposed, both spec + cards new:**
TD-DEVIATION (§2.3), TD-3D-CHAIN (§2.7), BU-CYCLOPEAN (§3.10),
BU-REMNANT (§3.11), BR-GEO-IMPORT (§4.3). **5 card-sets.**

### §5.2 Cadence

For each card-set:

1. Author `index.md` = the design-problem MD with all four required
   elements (problem, tolerance, dataset, components + cross-ref).
2. Author one `.md` per fixture, each:
   - Restates the fixture's specific input + expected output.
   - Links to the dataset file/directory on disk.
   - Includes the expected post-card-pass screenshot description (the
     `.gh` and `.3dm` come from Libish's Rhino session).
3. Author `generate_fixtures.py` (rhino3dm headless) where the inputs
   are derived programmatically from the ETH1100 / quarry datasets.
4. The `.gh` canvas file is built by Libish on the Rhino side (we
   can't launch Rhino per `[[feedback_frahan_workflow]]`). The MD spec
   is the contract.

### §5.3 Total card volume

23 card-sets × ~3 fixtures × (1 spec MD + 1 generator + design-problem
notes) ≈ 23 × 4 files = **92 files** at design-grounded full quality.
Realistic overnight scope: the P0 set (11 card-sets, ~44 files) gets
landed tonight; P1 + P2 follow over subsequent sessions as Libish
validates the P0 batch.

---

## §6. Cross-references

- `wiki/specs/frahan_design_philosophy.md` (the philosophy doc; all
  sections referenced from this plan).
- `wiki/specs/release_plan/frahan_hitl_backlog.md` (the existing HITL
  backlog; this master plan extends, doesn't replace).
- `wiki/specs/release_plan/frahan_release_backlog.md` (component build
  rows for EM-3D-* / Voussoir / etc.).
- `wiki/research/voussoir_stereotomy_integration.md` (top-down voussoir
  spec).
- `Template-General/outputs/2026-05-30/design/edge_matching_redesign.md`
  (EdgeMatch family redesign with Components A/B/C/D).
- `wiki/research/quarra_stone/quarra_stone_presentation.md` + `borrowed_earth/`
  + `mason_dca/` (the research dossiers card precedents pull from).
- `wiki/research/design_sensibility/quarra_design_sensibility.md` +
  `external_precedents.md` (the canonical precedent surveys).
- `wiki/research/stone_cutting_optimization_soa/soa_survey_dossier.md`
  (Marvie Reed & Bondua 2025 SLR; the gap analysis behind the priority
  ordering).
- `wiki/index/algorithm_references_audit.md` (every algorithm in every
  component cited).
- Memory anchors: `[[feedback_hitl_cards_design_grounded]]`,
  `[[feedback_top_down_bottom_up_design]]`, `[[project_hitl_cards_all]]`,
  `[[reference_eth1100_dataset]]`, `[[reference_quarry_scan_datasets]]`,
  `[[reference_large_data_assets]]`.

---

## §7. Last updated

2026-05-31 — initial canonical authorship; 23 card-sets across top-down
(7) / bottom-up (11) / bridges-both (5). P0 tier (11 sets) targeted for
overnight authorship; P1 + P2 over subsequent sessions.
