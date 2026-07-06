# Frahan StonePack — Scan-to-Mill Architectural Plan

**Location:** `wiki/specs/scan_to_mill_architecture.md`
**Status:** authored 2026-05-31 per Libish directive
*"make a detailed architectural implementation plan that we will use
in support of either sprutcam/kukaprc/robotsplugin/rhinocam and others."*
**Companions:**
- `wiki/specs/frahan_design_philosophy.md` §10
- `wiki/specs/cathedral_scale_stone_fitting_plan.md` (the upstream design discipline)
- `wiki/specs/component_decomposition_plan.md` (the substrate spine)
- `wiki/specs/architectural_decisions_2026-05-31.md` (the 10 design decisions)
- `wiki/research/mrac_workshop_2023/` (MRAC workshop research dossiers — in flight)
- `wiki/research/robot_ingest_pipeline/gcode_to_kukaprc_robots.md` (in flight)

**Audience:** Frahan v1.x fabrication-side architects + AI agents. Reads
in 15 minutes. The wider workflow-level wikis flesh out individual
stages.

## §0. The 7-stage scan-to-mill pipeline

Frahan's end-to-end pipeline, learned from the MRAC 2023 robotic
log-milling workshop + Quarra MIT 2025 lecture + UCL Devadass 2025
limestone arch + Clifford-McGee 2017 Cyclopean Cannibalism, is
**seven stages from photo to stone**:

```
[1] SCAN INGEST
       photogrammetry (Metashape) | LiDAR (HDL-32) | structured light (Creaform)
                |
                v
[2] CLEANUP + ORIENT
       RANSAC plane segmentation + PCA workbench-align + outlier removal
                |
                v
[3] RECONSTRUCT
       Poisson surface reconstruction; mesh sanitize; OBB/PCA frame
                |
                v
[4] DESIGN MATCH (= top-down or bottom-up, per philosophy §10)
       template ↔ stone via Hungarian / NSGA-II / Recipe / Greedy
                |
                v
[5] CUT PLAN
       BlockCutOpt v2; per-stone toolpaths; kerf compensation; sequencing
                |
                v
[6] ROBOT TARGETS
       Plane[] / Target[] for KUKAprc / Robots / SprutCAM / RhinoCAM
                |
                v
[7] DRIVE + VERIFY
       robot drives motion; laser-tracker / deviation loop closes the gap
       (Quarra's sub-mm; MRAC's mm-level; F18 task closes Frahan's gap)
```

Frahan v1.0-rc1 covered stages 1, 3 partly, 4 fully (Voussoir trio + matching
substrate), 5 partly (BlockCutOpt v2 + Stone-Aware Cut Export). Stages
2 + 6 + 7 are the v1.x scope this plan locks in.

## §1. Per-stage component map — existing + gap + v1.x build

### §1.1 Stage 1 — SCAN INGEST

| Modality | Existing Frahan | Gap | Priority |
|---|---|---|---|
| LiDAR / E57 | `LoadE57CloudComponent` (D5F1xxxx) | LAS 1.4 (ASPRS), RD3 (Mala), DT1 (PulseEKKO), DZT (GSSI), .ply | LAS = HIGH (per ribbon plan); GPR formats = MED |
| Photogrammetry (Metashape) | none | MRAC pipeline scripts (`0_orient_ply.py` + `1_clean_ply.py`, local archive) use Open3D + Python; needs Frahan-side wrapper or out-of-process worker | HIGH (per Libish 2026-05-31; MRAC Exercise dossier in flight) |
| Structured light (Creaform) | none | UCL Devadass 2025 §2.2: handheld Creaform + turntable, 0.2 mm resolution | LOW (research lab-grade tool; rare in commercial flow) |
| Sample meshes (.ply / .obj) | `ReadPlyMeshComponent` (existing) | None for v1; full coverage via existing | (covered) |

Proposed new components (v1.x):
- `LoadLasCloudComponent` (LAS 1.4 ASPRS spec; ALL `D5F1xxxx` GUIDs to be assigned).
- `LoadMetashapeProjectComponent` (Agisoft `.psx` + `.files/` project format; routes through an out-of-process Python worker per `[[project_e57_ingest_component]]` pattern).
- `MetashapeAlignAndDenseComponent` (wraps the MRAC orient+clean pipeline; calls Open3D via Python worker).

### §1.2 Stage 2 — CLEANUP + ORIENT

The MRAC pipeline script (`0_orient_ply.py`, local archive) gives the
canonical recipe verbatim:

1. RANSAC plane segmentation (downsample 0.05, distance 0.010).
2. Statistical outlier removal (`remove_statistical_outlier(nb=50, std_ratio=1)`).
3. Transformation 1: translate to origin (subtract mean), rotate plane
   normal to align with Z-axis.
4. Re-segment after T1.
5. Transformation 2: PCA on segmented inliers; PCA axes → world axes.
6. Optional 90° rotation along X or Y.

The MRAC `1_clean_ply.py` adds:
7. Downsample voxel (0.015).
8. Two passes of statistical outlier removal.
9. Z-mask (cut workbench out: `z > z_table AND z < z_log`).
10. DBSCAN clustering (eps=0.06, min_points=55).
11. Pick largest log cluster.
12. Radius-outlier final clean.
13. Poisson reconstruction (depth=10).
14. Save .ply and .obj.

Frahan v1.x components needed:
- `RansacPlaneSegmentComponent` — wraps RANSAC plane fit (CGAL has equivalent).
- `PcaAlignToPlaneComponent` — the MRAC T1+T2 transformation.
- `StatisticalOutlierRemovalComponent` — `nb_neighbors + std_ratio` knobs.
- `RadiusOutlierRemovalComponent` — `nb_points + radius` knobs.
- `DbscanClusterComponent` — `eps + min_points` knobs; pick top-N clusters.
- `PoissonReconstructComponent` — depth + scale; wraps existing Geogram-bundled Kazhdan 2006 per `[[project_reconstruction_backends]]`.

Existing Frahan `MeshSanitize` + `MeshRepair` cover the post-reconstruction
hygiene (Botsch 2010 PMP). The MRAC-specific composition is the new value-add.

### §1.3 Stage 3 — RECONSTRUCT

- `Poisson Reconstruct` (above) emits the triangle mesh.
- `MeshSanitize` (existing): close holes, weld vertices, drop unreferenced.
- `MeshPca` (existing): principal axes for OBB orientation.
- `ScanToBlockInventoryComponent` (F2D0BC20, ✓ shipped v1.0-rc1): emits typed `QuarryBlock` with bounds + usable volume + frame + dimensions.
- (Optional) `MeshDecimate` to reduce vertex count for downstream perf.

This stage is largely covered. The MRAC pipeline confirms the Poisson
reconstruction depth=10 default; Frahan can adopt that.

### §1.4 Stage 4 — DESIGN MATCH

Already shipped via the Voussoir trio (`VoussoirIngest` / `VoussoirStoneMatcher` / `VoussoirPackIntoBlock`) plus the `MatcherRegistry` substrate plus the 3D EdgeMatch family (B3D / A3D / C3D / D3D) plus the Soft-ICP 3D component.

Per the architectural decisions doc §9 the four canonical workflows
(stone fab / timber reuse / Cyclopean / Trencadís) all compose the same
~31 primitives differently. **The MRAC log-milling workflow is the
*wood* analogue of the stone fab discipline** — it uses the same matching
substrate (find scanned log → designed segment) but with timber-style
constraints (length / area / inertia per Tomczak 2023).

### §1.5 Stage 5 — CUT PLAN

Existing:
- `BlockCutOpt v2` per `[[project_blockcutopt_synthesis]]` — quarry-side
  cut planning (Elkarmoty 2020 + Goodman 1985 key-block + Frahan synthesis).
- `Stone-Aware Cut Export` — emits per-piece typed `StoneCutMetadata`.
- `Fabrication Prep Report` — lift class, CoM, anchor geometry.
- `Carving Stages` — multi-pass machining with smoothed normals + AABB clamp.

Gap: no per-stone toolpath. Stone-Aware Cut Export emits geometry +
metadata, not motion. Stage 6 closes this.

### §1.6 Stage 6 — ROBOT TARGETS (the v1.x KUKAprc / Robots / SprutCAM bridge)

The headline v1.x gap per Libish 2026-05-31. Three downstream targets:

**6a. KUKAprc bridge** (Robots in Architecture, Vienna; Brell-Cokcan + Braumann):
- Native KUKA Robot Language (KRL `.src` + `.dat`) output.
- Takes Frame[] / Plane[] target lists from GH.
- Existing `NC IMPORT.spm` config in the MRAC Sample Files shows
  RhinoCAM 3-axis G-code being imported via KUKAprc's `NC IMPORT`
  component.

**6b. Robots plugin bridge** (Vicente Soler):
- Open-source, cross-vendor (KUKA / ABB / FANUC / UR).
- `github.com/visose/Robots`. GH Target API.
- Same Plane-based target list.

**6c. SprutCAM bridge**:
- Commercial CAM with its own post-processors per robot vendor.
- Frahan exports geometry → SprutCAM ingests via standard CAD/CAM
  pipeline (no plugin needed).

Proposed Frahan components (v1.x, per task #66):

| Component | Role | Input | Output |
|---|---|---|---|
| `GCodeIngestComponent` | Parse .nc / .gcode → typed `CutPath` (segments, speeds, spindle) | text file path | `CutPath` typed record |
| `GCodeToPlanesComponent` | Translate `CutPath` G01/G02/G03 → `Plane[]` with tool axis + feed rate | `CutPath` | `Plane[]`, speeds `double[]` |
| `KukaPrcTargetsComponent` | Wrap KUKAprc's target-from-plane idiom | `Plane[]` | `KukaPrcTarget[]` |
| `RobotsTargetsComponent` | Wrap Robots plugin's `Target.FromPlane` | `Plane[]` | `Robots.Target[]` |
| `WireSawEndEffectorComponent` | Frahan-original: wire-saw-mounted end effector per Libish exploratory direction | tool-axis vector + cut length | Path geometry + feed schedule |
| `CarveDeviationLoopComponent` | F18 per `[[reference_quarra_lecture]]`: scan + best-fit + deviation map + corrective pass | placed mesh + design mesh | corrected toolpath |

The `WireSawEndEffectorComponent` is the Frahan-original differentiator
the user flagged: *"explore wire saw mounted on the robot end-effector
for cutting."* Quarra rented a quarry wire saw for the Two Horse Relief
block splitting; Frahan's contribution is to bring that capability onto
a robot end-effector for finer cuts than blade milling can give.

### §1.7 Stage 7 — DRIVE + VERIFY

This is the precision-tier choice point:

| Tier | Method | Precision | Frahan support |
|---|---|---|---|
| **MRAC tier** | KUKA robot + GH-supplied toolpath; no in-process verification | mm-level | covered by Stage 6 outputs |
| **Quarra tier** | Robot + Leica laser tracker (20 µm / 400 ft); scan after each pass; deviation map; corrective surface | sub-mm | needs F18 build (task per `[[reference_quarra_lecture]]` §4.2) |

Frahan v1.x targets **both**. MRAC tier is straight Stage 6 output. Quarra
tier is the F18 deviation loop sketched in §1.6 row 6.

## §2. MRAC vs Quarra — the precision-tier comparison

Per the user's 2026-05-31 framing: *"study how this was only a millimeter
level workflow while the ones in Quarra went to sub-millimeter accuracy,
also they use pointer devices along with the robots, and laser pointing
systems for accurate sub-millimeter digital to stock processes."*

| Property | MRAC (this workshop) | Quarra | Frahan strategy |
|---|---|---|---|
| Scan source | Photogrammetry (Metashape) | 3D scan + laser tracker | Both via per-modality Stage 1 components |
| Workbench alignment | RANSAC plane + PCA (mm) | Laser tracker to model world (µm) | F18 deviation loop ports Quarra's discipline |
| Toolpath generation | RhinoCAM 3-axis G-code | In-house Rhino scripts | G-code Ingest family bridges both |
| Robot motion | KUKA + KUKAprc | KUKA KR480 MT + custom rigs | Stage 6 components are KUKA-agnostic |
| Verification | None in process | Scan-best-fit-deviation-map per pass | F18 closes Frahan's gap |
| Final tolerance | mm-level | 0.1-0.2 mm field, 0.15 mm shop | Frahan ships BOTH tiers; user picks |

**Quarra's wire saw mention**: from `wiki/research/quarra_stone/quarra_stone_presentation.md`
§12.3: Quarra **rented a quarry wire saw** to split 80,000 lb blocks
in half. The wire-saw-on-robot-end-effector direction Libish proposes
extends this from quarry-scale to mid-scale block cutting.

## §3. KUKAprc vs Robots plugin vs SprutCAM vs RhinoCAM — the four downstream targets

(Research dossier `wiki/research/robot_ingest_pipeline/gcode_to_kukaprc_robots.md`
is in flight from agent `a1e35970a8ce3ee38`. This section will deepen
when it lands.)

| Tool | Author / vendor | License | Robot vendors | Input format | Best for |
|---|---|---|---|---|---|
| **KUKAprc** | Robots in Architecture (Brell-Cokcan + Braumann, Vienna) | Commercial; education licence available | KUKA only | Frame[] / Plane[] + own `NC IMPORT.spm` for G-code | KUKA shops; mature Vienna lineage |
| **Robots** | Vicente Soler (UCL Bartlett RC4 + AUAR alumnus) | Open-source MIT (github.com/visose/Robots) | KUKA / ABB / FANUC / UR | Target[] (Plane-based) | Cross-vendor; open contributions |
| **SprutCAM** | SprutCAM Tech (commercial) | Commercial | All major | STEP / IGES geometry; own toolpath gen | Production shops with existing CAM |
| **RhinoCAM** | MecSoft (commercial) | Commercial | All major | Rhino geometry; .nc G-code output | Rhino-native CAM shops (the MRAC workshop tool) |

Frahan's bridge components (§1.6) emit the lowest common denominator
(typed `CutPath` + `Plane[]`) so all four downstream tools can consume
it.

## §4. Common-vs-specific components — fabrication side

Per Libish 2026-05-31 *"make all the component study for necessary
common components and also special components specific to the workflow
for fabrication."*

### §4.1 Common components (reusable across milling / sawing / wire-saw / robotic-pick-and-place)

- `GCodeIngestComponent` (§1.6)
- `GCodeToPlanesComponent` (§1.6)
- `KukaPrcTargetsComponent` + `RobotsTargetsComponent` (§1.6) — the two
  downstream-plugin wrappers
- `Stone-Aware Cut Export` (existing) — typed metadata carrier
- `Fabrication Prep Report` (existing) — lift class, CoM
- `Build Order Sequencer` (existing) — Kim 2024 install DAG

### §4.2 Specific components — by fabrication mode

| Mode | Specific component | Notes |
|---|---|---|
| **3-axis end-mill** (MRAC workshop) | `EndMillToolpathComponent` (proposed) | Wraps the RhinoCAM-style G-code generation Frahan-side; alternative to handing geometry to RhinoCAM directly |
| **5-axis end-mill** (Quarra Breton 5-axis) | `FiveAxisToolpathComponent` (proposed) | Tool-axis sweep; needs additional MultiToolOrientation knob |
| **Robot wire-saw** (Libish exploratory) | `WireSawEndEffectorComponent` (proposed; Frahan-original) | The new direction — see §1.6 |
| **Robot blade saw** (Quarra blade-machining of granite) | `BladeSawEndEffectorComponent` (proposed) | Horizontal-blade variant; Quarra's granite-finishing tradition |
| **Robot pick-and-place** (MRAC + Quarra Emanuel 9) | `PickPlaceTargetsComponent` (existing) | Already in Frahan as `PickPlaceFramesComponent` |
| **Posi-turner + flip** (Quarra City Jeff) | `FlipRegistrationComponent` (proposed; F18-related) | Machined-recess fiducials for cross-flip registration |
| **Laser tracker setting** (Quarra Emanuel 9) | `LaserTrackerSetComponent` (proposed; F18-related) | Bridges laser tracker readings → Rhino world coordinates |

Each "Specific" component is small (~150-300 LoC, mostly a wrapper) but
encodes the fabrication-mode-specific knobs. Composition: **Stone-Aware Cut Export
→ (one Specific component per mode) → (one Common Robot-Targets component) →
(robot plugin of choice)**.

## §5. Wire-saw end-effector — the Frahan-original differentiator

Libish 2026-05-31: *"i would also like to explore wire saw mounted on the
robot end-effector for cutting."*

**Precedent landscape** (research dossier in flight; preliminary):
- Quarra rented a quarry wire saw (free-standing, not robot-mounted) for the
  Two Horse Relief 80,000 lb block split. Quarra MIT 2025 §12.3.
- Industry standard wire saws for stone are free-standing or gantry-mounted; robot-mounted is research-grade.
- ETH Gramazio Kohler robotic stone wall work uses pick-and-place, not wire-saw end-effectors.
- Search target: any published robot-mounted-diamond-wire-saw papers (research dossier #65 in flight).

**Frahan opportunity**: if robot-mounted wire saws are research-grade, Frahan can be the FIRST commercial GH plugin to support the workflow. The `WireSawEndEffectorComponent` design has:

- **Inputs**: stock mesh, design mesh, tool-axis vector (the wire direction), wire length, feed rate, kerf width (diamond-wire kerfs are ~3-8 mm; tighter than blade saws ~10-15 mm).
- **Outputs**: cut path (Plane[] sequence + tool-axis sweep), tension envelope, expected kerf compensation.
- **Composition**: replaces the end-mill toolpath at Stage 6 for stock-roughing OR fine-curve cuts that a blade saw can't reach.

This is the v1.x Frahan-original deliverable that brings Frahan parallel
to Quarra's wire-saw + robot-sawing-for-roughing + milling-for-depth
workflow Libish flagged.

## §6. Implementation order

Per the structuralCircle "greedy-first MILP-as-oracle" cadence + the
decomposition plan §7:

### §6.1 Phase A — Scan-side (the MRAC pipeline) — Q3 2026

1. `LoadLasCloudComponent` (LAS 1.4 ASPRS) — HIGH priority.
2. `LoadMetashapeProjectComponent` (Agisoft .psx + .files/) — HIGH priority.
3. `RansacPlaneSegmentComponent` + `PcaAlignToPlaneComponent` — the MRAC orient pipeline.
4. `StatisticalOutlierRemovalComponent` + `RadiusOutlierRemovalComponent` + `DbscanClusterComponent` — the MRAC clean pipeline.
5. `PoissonReconstructComponent` — wraps Geogram-bundled Kazhdan.

### §6.2 Phase B — Fabrication-side (the KUKAprc / Robots bridge) — Q4 2026

6. `GCodeIngestComponent` — .nc → typed `CutPath`.
7. `GCodeToPlanesComponent` — `CutPath` → `Plane[]`.
8. `KukaPrcTargetsComponent` + `RobotsTargetsComponent` — downstream-plugin wrappers.
9. `Stone-Aware Cut Export` already exists; verify the typed record IS consumable by 7 above.

### §6.3 Phase C — The Frahan-original components — Q1 2027

10. `WireSawEndEffectorComponent` — the differentiator.
11. `BladeSawEndEffectorComponent` — Quarra-style granite finishing.
12. `FlipRegistrationComponent` + `LaserTrackerSetComponent` — F18 deviation loop pieces.
13. `CarveDeviationLoopComponent` — the full F18 sub-mm tier.

### §6.4 Phase D — Sample workflow .gh files — Q2 2027

14. `Stone Fab Cathedral` (Workflow A from cathedral plan §3-§5).
15. `Cyclopean Cannibalism Wall` (Workflow C from philosophy doc §10.11).
16. `MRAC-Style Log Mill` — direct port of the MRAC LogMilling_RhinoCam2KukaPrc.gh.
17. `Quarra Sub-mm Tier` — Voussoir + Laser Tracker + F18 deviation loop.

Each sample .gh ships with `tolerances.csv` + `README.md` + a HITL card-set
per the discipline established in `wiki/research/hitl_cards/`.

## §7. References (real, verifiable per AGENTS.md §9)

- MRAC IAAC Barcelona robotic-milling-of-non-standard-logs workshop, 2023 (the workshop archive at `reference\mrac_workshop_2023\`).
- Quarra MIT Out of Frame lecture 2025 (`wiki/research/quarra_stone/quarra_stone_presentation.md`).
- UCL Devadass 2025 limestone arch preprint (DOI `10.21203/rs.3.rs-8019586/v1`).
- Clifford-McGee 2017 Cyclopean Cannibalism ACADIA pp. 404-413.
- Tomczak-Haakonsen-Luczkowski 2023 structuralCircle (DOI `10.1088/2634-4505/acf341`).
- Open3D 0.16 (the MRAC pipeline runtime); Kazhdan-Bolitho-Hoppe 2006 Poisson reconstruction (bundled inside Geogram).
- KUKAprc plugin (Robots in Architecture; Brell-Cokcan + Braumann, Vienna; URL `robotsinarchitecture.org/kukaprc`).
- Robots plugin (Vicente Soler; github.com/visose/Robots; MIT licence).
- RhinoCAM (MecSoft); SprutCAM (commercial CAM); KUKA KR-150-2 KR-C4 (sample files include manuals at `reference\mrac_workshop_2023\sample_files\Sample Files\Documentation\`).
- Three research dossiers in flight at `wiki/research/mrac_workshop_2023/`
  + `wiki/research/robot_ingest_pipeline/` per tasks #63, #64, #65 (2026-05-31).

## §8. Last updated

2026-05-31 — initial canonical authorship. Three background dossiers
(MRAC SUBMISSIONS, MRAC Exercise, G-code → KUKAprc/Robots) in flight;
this plan will be revised as they land + as the v1.x Phase A/B/C
components ship.
