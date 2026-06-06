# Frahan StonePack — session log

Append-only. Newest entries at the top. One block per meaningful
work session. Use the format below.

```
## YYYY-MM-DD — short subject

Agent: claude_cloud | codex_cloud | qwen_local | …
Branch: <git branch>
Commits: <short SHA>, <short SHA>
Tests: PASS / FAIL / SKIP

Done:
- bullet
- bullet

Files touched (top-level):
- path/

Next:
- bullet
```

---

## 2026-05-31 — Fabrication-bridge checkpoint (G-code + wire-saw)

Agent: claude_cloud (Opus 4.7, 1M ctx)
Branch: docs/frahan-autonomous-nightshift
Commits: not yet committed (HITL gate per AGENTS.md §6; 7 in-scope files)
Tests: not re-run this window; last green 19/19 PASS on MatcherRegistryTests.
Build: Frahan.StonePack.GH Debug net48 -> Build succeeded.

Done — 3 of 5 Stage F (Robot Targets) components shipped + 3 dossiers:

**Components (in `src/Frahan.StonePack.GH/Fabrication/`):**
- D5F10030 `GCodeParserComponent` — ISO 6983 parser, CutPath typed-record emitter.
- D5F10031 `GCodeToPlanesComponent` — CutPath -> Plane[] with arc discretisation + tool-axis frame.
- D5F10034 `WireSawToolpathAdapterComponent` — Frahan-original; robot-mounted diamond-wire saw toolpath; cites Zhang 2024 JCDE 11(6) DOI 10.1093/jcde/qwae094 + Moult 2018 USyd.

**Typed record (in `src/Frahan.StonePack.Core/Fabrication/`):**
- `CutPath.cs` — Segments + Bounds + Units + Dialect + per-segment FeedRate / SpindleSpeed.

**Dossiers landed (in `wiki/research/`):**
- `robot_ingest_pipeline/gcode_to_kukaprc_robots.md` — KUKAprc + visose Robots + wire-saw survey.
- `mrac_workshop_2023/exercise_dossier.md` — Metashape exercise (92 photos, 5 markers).
- `mrac_workshop_2023/submissions_dossier.md` — Submissions inventory (Pink Joinery + Yellow Joinery groups, Speckle 2.11.1 bridge, Open3D pipeline).

**In flight (sub-agent):**
- `wiki/research/fabrication_bridge_gap_map.md` — avoid-double-work map.

Files touched (top-level):
- `Template-General/outputs/2026-05-01/frahan_stonepack/src/Frahan.StonePack.Core/Fabrication/`
- `Template-General/outputs/2026-05-01/frahan_stonepack/src/Frahan.StonePack.GH/Fabrication/`
- `wiki/research/robot_ingest_pipeline/`
- `wiki/research/mrac_workshop_2023/`
- `outputs/2026-05-31/future_work/frahan_handoff_compact.md`

Next:
- Build PlanesToKukaPrcCommands (D5F10032) + PlanesToRobotTargets (D5F10033). Thin wrappers, ~1 day each.
- Phase A scan-side components per MRAC submissions dossier §6.
- ETH1100 tuning sweep (task #62).

---

## 2026-05-21 — Kim 2024 polygonal-masonry install-order DAG (Phases 1-4)

Agent: claude_cloud (Opus 4.7, 1M ctx)
Branch: docs/frahan-autonomous-nightshift
Commits: 64333b6, 13b994b, b59dbdc (+ today's docs commit)
Tests: 756 PASS / 0 FAIL / 91 SKIP (was 747 / 0 / 91 at session start).
Build: 0 errors, 0 warnings.

Done — full implementation of DETC2024-142563 (Kim 2024) shipped:

**Phase 1 — Python reference:**
- outputs/2026-05-20/polygonal_masonry_sequence/ — 5 modules + 7
  examples covering paper Figs. 4-7, sec. 5.4 holes, Fig. 13
  (Perlin), Fig. 14 (Voronoi + bunny), Fig. 15 (3D Voronoi, 50
  stones). 15/15 unit tests pass; 7 PNGs saved.

**Phase 2 — C# Core port:**
- Frahan.StonePack.Core/Masonry/Sequencing/{Geom2D, Pslg, Wall}.cs
  in namespace Frahan.Masonry.Sequencing.
- Half-edge PSLG with T-junction pre-splitting; chains+bbox -> DAG
  via rules (5)-(8); reversed Kahn's per Code 1. 14 unit tests.

**Phase 3 — Grasshopper component (2D):**
- PolygonalMasonrySequenceComponent on Frahan > Masonry ribbon,
  GUID B4E07A3C-7F4D-4E5B-9C71-0EAF21C9B6A1. Curves + Rectangle in
  -> stone polylines + install order + DAG edges out. Metadata test.

**Phase 4 — Wall3D + 3D Grasshopper component:**
- Wall3D.cs (structural, geometry-kernel-free): cells + adjacency
  in, install plan out. 7 tests covering tower, side-by-side no-edge,
  pyramid, holes, dedup, unbounded skip, duplicate-id throw.
- PolygonalMasonrySequence3DComponent: closed Meshes in, install
  order + DAG edges out. Auto-detects adjacency via face-centroid
  quantisation. GUID C5F18B4D-8A6F-4E72-AC83-1FBD32D8C7B2. Metadata
  test.

**Deploy:**
- Release build 2026-05-20 21:38. .gha + Core.dll + EdgeMatching.Core.dll
  copied to %APPDATA%/Grasshopper/Libraries/Frahan.StonePack.MeshHeightmap/.

Files touched (top-level):
- Template-General/outputs/2026-05-20/ (Python reference + figures
  + NEXT_STEPS)
- Template-General/outputs/2026-05-01/frahan_stonepack/src/
  Frahan.StonePack.Core/Masonry/Sequencing/ (4 .cs files)
- Template-General/outputs/2026-05-01/frahan_stonepack/src/
  Frahan.StonePack.GH/Masonry/Sequencing/ (2 .cs files)
- Template-General/outputs/2026-05-01/frahan_stonepack/tests/
  Frahan.StonePack.Tests/ (4 new .cs files + Program.cs edits)
- Template-General/raw/references/computation-13-00211.pdf
- wiki/algorithms/polygonal_masonry/ (this session, both pages)
- wiki/index.md, wiki/log.md (this entry), wiki/audit_trail.jsonl

Next:
- Visual canvas validation pass — drop Polygonal Masonry Sequence
  on the canvas, confirm install order and DAG arrows match
  expectation. That's the truth-criterion-(c) gate before
  promotion. Detailed recipe in
  Template-General/outputs/2026-05-20/future_work/NEXT_STEPS.md
  and wiki/algorithms/polygonal_masonry/validation_log.md.
- Phase 5 (turnkey "seeds in, plan out" C# pipeline): MIConvexHull
  NuGet (HITL gate) OR hand-rolled Bowyer-Watson 3D Voronoi.
  Deferred pending Libish's call.

---

## 2026-05-19 (final) — K2 + Phase G + Phase H + Phase I in one push

Agent: claude_cloud (Opus 4.7, 1M ctx)
Branch: docs/frahan-autonomous-nightshift
Tests: 733 PASS / 0 FAIL / 91 SKIP (was 724 / 0 / 82).
Build: 0 errors, 0 warnings.

Done — four phases shipped in this session:

**K2 (Kim 2025 extensions beyond the paper):**
- CutSurfaceWeight option subtracts Jalalian I11 surface cost from score.
- Parallel.For forest growth (bitwise identical to serial via
  per-forest seed).
- MemoryBudgetBytes option auto-caps ForestCount.
- Async GH wrapper deliberately skipped per Frahan's own empirical
  evidence (IrregularSheetFillComponentAsync marked Obsolete).
- 4 new tests; commit 5767e8a.

**Phase G (mesh-bench support):**
- Frahan.Core.Quarry.BenchBoundary value type with Aabb + optional
  Mesh + ContainsBoxCentre / ContainsBox helpers.
- BenchFromMesh GH component (Mesh → Box + Mesh + BenchBoundary).
- ClipBoxesByMesh GH component (filter Box[] BCO outputs by mesh
  containment + recompute recovery fraction).
- Design choice: composition over invasive edits to the 11 BCO
  components — preserves all GUIDs and tests.
- 8 new tests; commit not yet pushed at point of writing this log.

**Phase H (reconstruction primitives):**
- frahan_cgal.cpp + .h gain alpha_shape_3 + advancing_front_reconstruct
  + estimate_normals exports (build-gated by
  FRAHAN_CGAL_ENABLE_RECONSTRUCTION).
- frahan_geogram.cpp + .h gain poisson_reconstruct stub
  (build-gated; PoissonRecon library wiring TODO documented).
- ReconstructionNative.cs: 6 Try* safe wrappers that catch
  DllNotFoundException + EntryPointNotFoundException so the .gha
  loads cleanly even before the native rebuild.
- ScanReconstruct + EstimateCloudNormals GH components.

**Phase I (cloud-cloud ICP at 10M+ scale):**
- frahan_geogram.cpp gains voxel_downsample (pure C++ hash grid) +
  kdtree_query (GEO::NearestNeighborSearch).
- Frahan.Core.ScanIngest.PointCloudIcp: coarse-to-fine trimmed
  point-to-point ICP. Native KD-tree + voxel downsample when
  available; managed brute-force + hash-grid fallback otherwise.
  Reuses RigidTransformRecovery (Horn 1987) for per-iteration solves.
- VoxelDownsample + CloudIcp GH components.
- 6 new tests; all PASS via managed fallback paths.

Files touched: ~17 across native shim sources, Core, GH, and tests.

Pending HITL:
- Native rebuild with -DFRAHAN_CGAL_ENABLE_RECONSTRUCTION + a
  PoissonRecon link target to enable Phase H exports.
- 10M-point Cloud ICP canvas validation per UX report §7.7.F gate
  (target wall-clock 30-40 s with native KD-tree).
- Visual validation of all 11 new GH components on a Rhino 8 canvas.

Phase F8 (HITL canvas validation) and the K2 + G + H + I HITL gates
remain user-side work.

---

## 2026-05-19 (last) — Kim 2025 K1 port + 3 extensions

Agent: claude_cloud (Opus 4.7, 1M ctx)
Branch: docs/frahan-autonomous-nightshift
Tests: 724 PASS / 0 FAIL / 82 SKIP (was 719 / 0 / 74).
Build: 0 errors, 0 warnings.

Done:
- **Kim 2025 K1 port** — implements the forest+slab+score algorithm
  from `wiki/papers/kim2025_tree_packing.md` (CC BY 4.0 paper synthesis).
  Solves multi-container guillotine cuboid packing with element values
  and container prices — a problem none of the existing Frahan packers
  cover.
- **Frahan.Core.Packing namespace** (pure managed, no MathNet/no Rhino
  native needed by the algorithm itself):
  - `GuillotineRotationMode` enum: None / OneAxis / ThreeAxis.
  - `GuillotinePackOptions`: ForestCount, Seed, RotationMode, KerfWidth.
  - `GuillotinePackResult`: BestForestIndex + Score + Placements[]
    (each with element/container indices + PlacedBox + Transform) +
    UsedContainerIndices + AllElementsPacked + ForestsRun + SeedUsed.
  - `GuillotinePlacement` struct.
  - `TreePackForest.Pack(...)` — main entry. Per forest: shuffle elements
    + containers + rotations, grow forest using leaf-and-slab queue,
    split slab into 3 sub-slabs along one of 6 axis-orderings on each
    placement, compute score per Kim 2025 §2.4. Best forest wins.
- **Three extensions beyond the paper** (close documented gaps):
  1. **Deterministic seed.** Master seed input; forest k uses
     (seed + k). SplitMix64 RNG. Same seed → same placement order.
  2. **Saw kerf width.** Each axis-aligned cut consumes this much
     material along its direction. Slabs that collapse to <=0 size
     after kerf are dropped from the leaf queue. Closes paper's
     silent zero-kerf assumption.
  3. **Per-container Forbidden Boxes** (e.g. fracture-intersected
     cells from `HeteroExt`). Elements that would overlap any
     forbidden box in their target container are rejected during
     placement. Closes Kim §8.2 gap (fracture-aware containers).
- **`BlockPackTreeComponent`** (`C2D3E4F5-3001-...`) on Masonry /
  secondary row. 9 inputs (Elements + Element Values + Containers +
  Container Prices + Rotation Mode + Forests + Seed + Kerf + optional
  Forbidden Boxes flat list, auto-bucketed by container centre).
  9 outputs (Placed Boxes + Transforms + Element Ids + Container Ids +
  Used Containers + Score + All Packed + Best Forest + Report).
- **13 new tests** (TreePackForestTests): input validation (null,
  mismatched, empty), single-fit, oversized rejection, cheap-container
  preference, deterministic seed reproducibility, seed exploration
  diversity, kerf reduces packed count, forbidden box blocks single
  fit, forbidden box redirects to free container, 3-axis rotation
  fits elongated element, all-packed score formula.

Deferred to a future K2 batch:
- Jalalian I11 cutting-surface cost as an optional score term.
- Parallel forests via Parallel.For / PLINQ.
- Memory-budget cap (auto-reduce forests when budget exceeded).
- Async via GH_TaskCapableComponent for large f.

Files touched:
- `Template-General/outputs/2026-05-01/frahan_stonepack/src/Frahan.StonePack.Core/Packing/GuillotinePackOptions.cs` (new, ~65 LOC)
- `Template-General/outputs/2026-05-01/frahan_stonepack/src/Frahan.StonePack.Core/Packing/GuillotinePackResult.cs` (new, ~80 LOC)
- `Template-General/outputs/2026-05-01/frahan_stonepack/src/Frahan.StonePack.Core/Packing/TreePackForest.cs` (new, ~440 LOC)
- `Template-General/outputs/2026-05-01/frahan_stonepack/src/Frahan.StonePack.GH/Packing/BlockPackTreeComponent.cs` (new, ~220 LOC)
- `Template-General/outputs/2026-05-01/frahan_stonepack/tests/Frahan.StonePack.Tests/TreePackForestTests.cs` (new, ~235 LOC)
- `Template-General/outputs/2026-05-01/frahan_stonepack/tests/Frahan.StonePack.Tests/Program.cs` (+13 test registrations)

Next:
- HITL canvas validation: open Rhino 8, drop BlockPackTree on canvas
  with the Inst_01 / Inst_02 instances from the paper (Tables 2/3 of
  the synthesis at wiki/papers/kim2025_tree_packing.md) and confirm
  the score converges to 1245.52 / 1087.36 within ~50-1000 forests.
- K2 batch — adaptive-MC + parallel + async (deferred items above).

---

## 2026-05-19 (late) — Phase F6/F7 duplicate component cleanup

Agent: claude_cloud (Opus 4.7, 1M ctx)
Branch: docs/frahan-autonomous-nightshift
Tests: 719 PASS / 0 FAIL / 74 SKIP (unchanged from F5).
Build: 0 errors, 0 warnings.

Done:
- **Phase F6 — MeshFix dup removal** (UX architecture report §5.2):
  Dropped `Template-General/.../Masonry/MeshRepairComponent.cs` (GUID
  `F2D000B5-CADC-4F2D-A0B5-7E60CADA15A0`, committed 2026-05-08).
  The older root-level `MeshRepairComponent.cs` (GUID
  `AB12C00A-1A2B-4C3D-9E4F-5A6B7C8D9E0A`, committed 2026-05-04)
  survives per AGENTS.md §8 stability rule (longer commit lineage).
  Both shared nickname `MeshFix`; only one survived a `.gha` reload,
  so the dropped declaration was already unreachable from canvas
  search.
- **Phase F7 — MeshDiag dup removal** (UX architecture report §5.3):
  Dropped `Template-General/.../Masonry/MeshDiagnosticsComponent.cs`
  (GUID `CDEF0123-4567-89AB-CDEF-0123456789AB`, committed 2026-05-08).
  Root `MeshDiagnosticsComponent.cs` (GUID
  `AB12C005-1A2B-4C3D-9E4F-5A6B7C8D9E05`, committed 2026-05-04)
  survives. `MQ Mesh Quality Report` stays as the richer alternative
  in the Masonry subcategory.
- Updated `Gh_MeshRepairComponent_Metadata` (BvhAndBestFitTests.cs)
  and `Gh_MeshDiagnosticsComponent_Metadata` (MeshSolverTests.cs) to
  assert against the surviving root-variant GUIDs and port counts
  (Repair: 3 in / 4 out; Diag: 1 in / 10 out). Both tests now PASS
  cleanly.
- Added `using Frahan.GH;` to both test files so the simple class
  names resolve to the root variants now that the Masonry dups are
  gone.

Files touched:
- DELETED: `Template-General/outputs/2026-05-01/frahan_stonepack/src/Frahan.StonePack.GH/Masonry/MeshRepairComponent.cs`
- DELETED: `Template-General/outputs/2026-05-01/frahan_stonepack/src/Frahan.StonePack.GH/Masonry/MeshDiagnosticsComponent.cs`
- MODIFIED: `Template-General/outputs/2026-05-01/frahan_stonepack/tests/Frahan.StonePack.Tests/BvhAndBestFitTests.cs` (metadata expectations + using directive)
- MODIFIED: `Template-General/outputs/2026-05-01/frahan_stonepack/tests/Frahan.StonePack.Tests/MeshSolverTests.cs` (metadata expectations + using directive)

Next:
- Phase F8: HITL canvas validation gate (user-side, Rhino 8). Save
  `.gh` files into `outputs/<date>/canvas_validation/scan_pack/`.
- PackRpt duplicate (UX report §A.12) — needs deliberation; both
  PackingReportComponent and PackingPlanReportComponent have
  functional code with same Name + Nickname. NEXT_STEPS says drop
  PackingReportComponent but it has the older lineage. Defer until
  a clear policy call on Name disambiguation vs deletion.

---

## 2026-05-19 (later still) — Phase F5 3D-pack diagnostics

Agent: claude_cloud (Opus 4.7, 1M ctx)
Branch: docs/frahan-autonomous-nightshift
Tests: 719 PASS / 0 FAIL / 74 SKIP (was 717 / 0 / 66 → +2 PASS, +8 SKIP).
Build: 0 errors, 0 warnings (Core, EdgeMatching.Core, GH, Tests).

Done:
- **Phase F5 — 3D Pack diagnostics** (UX architecture report §7.7.C):
  Three pragmatic diagnostics that catch obvious 3D-pack failures
  without the cost of a full RBE solve.
- `Frahan.Core.ScanIngest.PackDiagnostics` (pure managed):
  - `PerStoneOverlap(meshes, tol)` — fraction of each stone's
    vertices inside any other closed mesh. Cheap volumetric-overlap
    stand-in.
  - `CentreOfMassInContainer(meshes, container, tol)` — projects
    each stone's vertex centroid and tests inside the container.
  - `PileStability(meshes, up, floorZ, zTol)` — two-test geometric
    proxy: (a) stone is grounded and CoM lies in its own XY footprint,
    or (b) CoM lies in the XY footprint of a stone whose top is within
    zTol of this stone's bottom.
  - `VertexCentroid(mesh)` helper.
- GH components on 3D Packing / secondary row:
  - `PackOverlap` (`B1C2D3A4-2003-...`) — per-stone overlap
    fractions + penetrating-ids list + max overlap + warning bubble
    above 1 % vertex-inside threshold.
  - `PackComCheck` (`B1C2D3A4-2004-...`) — per-stone inside-bools +
    CoM points + marginal-ids + warning bubble.
  - `PackStability` (`B1C2D3A4-2005-...`) — per-stone stable bool +
    falling-ids + all-stable verdict + warning bubble.
- **10 new tests** (PackDiagnosticsTests):
  - 4 PerStoneOverlap (null guard, single-stone-zero, disjoint-zero,
    fully-contained-1.0).
  - 3 ComCheck (null guard, stone-inside-passes, stone-outside-fails).
  - 3 PileStability (grounded-stable, stack-of-two-stable,
    floating-falling).
  - 2 PASS pure managed; 8 SKIP requiring Rhino native
    (Mesh.CreateFromBox + Mesh.IsPointInside paths) per existing
    FRAHAN_SKIP_NATIVE convention.

Files touched:
- `Template-General/outputs/2026-05-01/frahan_stonepack/src/Frahan.StonePack.Core/ScanIngest/PackDiagnostics.cs` (new, ~180 LOC)
- `Template-General/outputs/2026-05-01/frahan_stonepack/src/Frahan.StonePack.GH/ScanIngest/PackOverlapComponent.cs` (new, ~95 LOC)
- `Template-General/outputs/2026-05-01/frahan_stonepack/src/Frahan.StonePack.GH/ScanIngest/PackComCheckComponent.cs` (new, ~105 LOC)
- `Template-General/outputs/2026-05-01/frahan_stonepack/src/Frahan.StonePack.GH/ScanIngest/PackStabilityComponent.cs` (new, ~110 LOC)
- `Template-General/outputs/2026-05-01/frahan_stonepack/tests/Frahan.StonePack.Tests/PackDiagnosticsTests.cs` (new, ~160 LOC)
- `Template-General/outputs/2026-05-01/frahan_stonepack/tests/Frahan.StonePack.Tests/Program.cs` (+10 test registrations)

Next:
- Phase F6/F7: drop the duplicate `MeshRepairComponent` and
  `MeshDiagnosticsComponent` declarations (§5.2 / §5.3 collisions in
  the UX architecture report). Keep the GUID with the longer commit
  lineage per AGENTS.md §8.
- Phase F8: HITL canvas validation gate.

---

## 2026-05-19 (still later) — Phase F3+F4 scan-prep components

Agent: claude_cloud (Opus 4.7, 1M ctx)
Branch: docs/frahan-autonomous-nightshift
Tests: 717 PASS / 0 FAIL / 66 SKIP (was 713 / 0 / 57 → +4 PASS, +9 SKIP).
Build: 0 errors, 0 warnings (Core, EdgeMatching.Core, GH, Tests).

Done:
- **Phase F3 — ScaleCal** (UX architecture report §7.7.A scan ingest):
  `Frahan.Core.ScanIngest.ScaleCalibration` — pure-managed
  `Solve(measuredLength, referenceLength)` and `SolveFromCurve(curve, ref)`
  static façade. Returns a `ScaleCalibrationResult` with the uniform
  scale factor, a Rhino `Transform.Scale` ready to apply, and a
  human-readable summary. `ScaleCalibrateComponent` GH wrapper
  (`B1C2D3A4-2001-...`) on Mesh / secondary row: input the measured
  Curve + reference length + optional Meshes; output transform +
  factor + scaled meshes + report. Sanity warning for scale factors
  outside [1e-4, 1e4].
- **Phase F4 — StonePrep** (UX architecture report §7.7.A one-button
  scan cleanup): `Frahan.Core.ScanIngest.StonePreparation` wraps the
  existing `Frahan.Surface.MeshRepair.RepairAll` (cull degenerate →
  weld → fill holes) + RhinoCommon's managed `Mesh.Reduce` (quadric
  edge collapse) + `Frahan.Surface.StoneDescriptorBuilder.BuildFromMesh`.
  Per-stage toggles (Repair, Decimate, Target Triangle Count), per-
  stone trace, batch + single APIs. `StonePrepComponent` GH wrapper
  (`B1C2D3A4-2002-...`) on Mesh / secondary row.
- **13 new tests:** 8 for ScaleCal (identity, mm-to-m, km-to-m,
  transform geometry, negative/zero/null guards, LineCurve length),
  5 for StonePrep (null guard, box mesh round-trip, repair-disabled
  trace, decimate triangle-count drop, batch with nulls preserving
  positions). 4 PASS pure managed; 9 SKIP requiring Rhino native
  (Mesh.CreateFromBox + Curve.GetLength + Transform.Scale paths) —
  appropriate skip per the existing FRAHAN_SKIP_NATIVE convention.

Files touched:
- `Template-General/outputs/2026-05-01/frahan_stonepack/src/Frahan.StonePack.Core/ScanIngest/ScaleCalibration.cs` (new, ~115 LOC)
- `Template-General/outputs/2026-05-01/frahan_stonepack/src/Frahan.StonePack.Core/ScanIngest/StonePreparation.cs` (new, ~175 LOC)
- `Template-General/outputs/2026-05-01/frahan_stonepack/src/Frahan.StonePack.GH/ScanIngest/ScaleCalibrateComponent.cs` (new, ~115 LOC)
- `Template-General/outputs/2026-05-01/frahan_stonepack/src/Frahan.StonePack.GH/ScanIngest/StonePrepComponent.cs` (new, ~150 LOC)
- `Template-General/outputs/2026-05-01/frahan_stonepack/tests/Frahan.StonePack.Tests/ScanPrepTests.cs` (new, ~200 LOC)
- `Template-General/outputs/2026-05-01/frahan_stonepack/tests/Frahan.StonePack.Tests/Program.cs` (+13 test registrations)

Next:
- Phase F5: 3D Pack diagnostics (PackOverlap, PackComCheck,
  PackStability) on 3D Packing / secondary row.
- Phase F6/F7: drop MeshFix + MeshDiag duplicate declarations
  (§5.2/§5.3 collisions).
- Phase F8: HITL canvas validation gate — Rhino-side.

---

## 2026-05-19 (later) — Phase F1+F2 scan ingest + Kim 2025 paper synthesis

Agent: claude_cloud (Opus 4.7, 1M ctx)
Branch: docs/frahan-autonomous-nightshift
Tests: 713 PASS / 0 FAIL / 57 SKIP (was 702 → +11 new tests).
Build: 0 errors, 0 warnings (Core, EdgeMatching.Core, GH, Tests — Debug).

Done:
- **Kim 2025 paper synthesis** (`wiki/papers/kim2025_tree_packing.md`):
  Engineering summary of Kim, T. "Packing and Cutting Stone Blocks
  Based on the Nonlinear Programming of Tree Cases", Computation 13:211
  (CC BY 4.0). Algorithm (forest growth + slab-split guillotine cuts +
  randomised parallel scoring), comparison to current Frahan packers,
  adoption recommendation. **Verdict:** Kim 2025 solves a problem none
  of the current Frahan packers cover (multi-container guillotine
  cuboid packing with prices). Add as a new component, do not replace
  anything. Defer to a self-contained ~1-2 week mini-phase between
  Phase G and Phase H of the UX architecture rollout.
- **Phase F1+F2 multi-format scan reader** (UX report §7.7.A-B):
  - `Frahan.Core.ScanIngest` namespace with three pure-managed parsers:
    `ObjMeshReader` (multi-group OBJ with v/f, vertex/tex/normal triplet
    syntax, negative indices, fan-triangulation), `StlMeshReader`
    (ASCII + binary, vertex welding via quantised hash at 1e-7
    tolerance), and `MultiFormatMeshReader` dispatcher (extension +
    magic-byte detection).
  - `ScanMesh` value type (Name + flat vertex/index arrays + optional
    RGB) shared across the scan path.
  - **Rescue-extend of `ReadPlyMeshComponent`** per UX report §9.14:
    same GUID (`789ABCDE-F012-3456-789A-BCDEF0123456`), subcategory
    moved Masonry → Mesh, inputs grew 1 → 2 (File Path + Format enum),
    outputs grew 3 → 5 (Meshes[] + Names[] + V[] + T[] + Detected
    Format). Multi-group OBJ files surface as parallel Mesh + Name
    lists.
  - 11 new tests covering OBJ (single tri, quad fan-triangulate,
    triplet face syntax, two-group split, negative indices, empty),
    STL (ASCII tetra welding, binary single tri), and dispatcher
    (extension detect, forced format override, missing file).
  - 2 existing ReadPlyMesh metadata tests updated to assert the new
    SubCategory ("Mesh") and port counts (2 in / 5 out).

Files touched:
- `wiki/papers/kim2025_tree_packing.md` (new)
- `Template-General/outputs/2026-05-01/frahan_stonepack/src/Frahan.StonePack.Core/ScanIngest/` (new dir; 3 files, ~565 LOC)
- `Template-General/outputs/2026-05-01/frahan_stonepack/src/Frahan.StonePack.GH/Masonry/ReadPlyMeshComponent.cs` (rewritten in-place; GUID stable)
- `Template-General/outputs/2026-05-01/frahan_stonepack/tests/Frahan.StonePack.Tests/ScanIngestTests.cs` (new, ~210 LOC)
- `Template-General/outputs/2026-05-01/frahan_stonepack/tests/Frahan.StonePack.Tests/MasonryGhComponentTests.cs` (Phase F metadata expectations)
- `Template-General/outputs/2026-05-01/frahan_stonepack/tests/Frahan.StonePack.Tests/Program.cs` (+11 test registrations)

Next:
- Phase F3 ScaleCal (scan scale calibration from reference length +
  measured Curve).
- Phase F4 StonePrep (one-button Repair→Decimate→OBB→StoneDesc wrapper).
- Phase F5 3D Pack diagnostics (PackOverlap, PackComCheck, PackStability).
- Phase F6/F7 MeshFix + MeshDiag dup removals (§5.2/§5.3 collisions).
- Phase F8 HITL canvas validation: scan two real stones, run full
  ScanRead → StonePrep → Pack3DContainer chain in Rhino 8.

---

## 2026-05-19 — UX architecture report + Phase I1-I5 registration shipped

Agent: claude_cloud (Opus 4.7, 1M ctx)
Branch: docs/frahan-autonomous-nightshift
Tests: 702 PASS / 0 FAIL / 57 SKIP (was 694 → +8 new tests, +1 SKIP requires Rhino native)
Build: 0 errors, 0 warnings (Core, EdgeMatching.Core, GH, Tests — Debug)

Done:
- **UX architecture report** (`outputs/2026-05-18/UX_ARCHITECTURE_REPORT.md`,
  2734 lines, 5 appendices) — full ribbon-consolidation analysis with
  three iterations:
  - **Initial pass:** 17 panels / ~131 visible components → 11 / ~70.
    §1-§8 + Appendix A/B + §9 open questions framed for claude.ai
    refinement.
  - **2026-05-19 morning:** folded in scan ingest (§3.8) and quarry
    mesh-boundary input (§3.9) workflows; added §6.6 / §6.7 friction
    items; §7.7.A-D + §7.8 layout impact; Appendix C BCO bench-input
    port audit (11 components Box-only, 1 Mesh).
  - **2026-05-19 afternoon:** added §3.8.1 reconstruction primitives
    (Alpha Shape / Poisson / Advancing-Front) via existing CGAL +
    Geogram shim extension — no new native deps; key finding is
    Geogram already bundles Misha Kazhdan's PoissonRecon per
    NOTICE.md line 33. Appendix D library comparison
    (Geogram / CGAL / Open3D / MeshLab).
  - **2026-05-19 evening:** added §3.8.0 registration upstream
    (marker / reference / georeferenced + cloud-cloud ICP at 10M+
    point quarry-bench scale); §7.7.F components; Appendix E
    registration / cloud-cloud ICP library matrix.
- **NEXT_STEPS.md Phase F-L rollout** (`outputs/2026-05-15/NEXT_STEPS.md`,
  +226 lines): 47 numbered checkpoints across four workstreams (scan
  ingest, mesh-bench, reconstruction, registration) + 5-week phasing
  table + cross-phase blockers + open-question gate.
- **Phase I1-I5 implementation shipped** (UX report §7.7.F easy-80%,
  pure managed, zero new deps, zero native code):
  - `Frahan.Core.Registration.RegistrationApi` — public Rhino-friendly
    façade over existing `RigidTransformRecovery` (Horn 1987 QAO).
    Returns Rhino `Transform` + RMS + per-pair residuals from N≥3
    `Point3d` pairs.
  - `Frahan.Core.Registration.GeoreferenceMath` — WGS84 + Bowring
    LLH↔ECEF (closed-form non-iterative) + ENU rotation about a
    chosen origin + Karney UTM↔LLH (series order 6). Round-trip
    verified at 4 latitudes (equator, mid-lat, southern, near-pole).
  - GH components on `Mesh / quarternary` row: `MarkerReg`
    (`B1C2D3A4-1111-...`), `Georef` (`B1C2D3A4-1112-...`) with
    Coord System enum (LLH-WGS84 / UTM / Local-ENU). Mis-picked
    marker warning bubble when any residual > 5× RMS.
  - 9 new tests covering identity / translation / rotation /
    combined / noisy / mismatched-count / N<3 + LLH↔ECEF +
    ENU↔ECEF + UTM↔LLH round-trips.

Files touched:
- `outputs/2026-05-18/UX_ARCHITECTURE_REPORT.md` (new, 2734 lines)
- `outputs/2026-05-15/NEXT_STEPS.md` (modified, +226 lines)
- `Template-General/outputs/2026-05-01/frahan_stonepack/src/Frahan.StonePack.Core/Registration/` (new dir, 2 files, ~395 LOC)
- `Template-General/outputs/2026-05-01/frahan_stonepack/src/Frahan.StonePack.GH/Registration/` (new dir, 2 files, ~380 LOC)
- `Template-General/outputs/2026-05-01/frahan_stonepack/tests/Frahan.StonePack.Tests/RegistrationApiTests.cs` (new, 200 LOC)
- `Template-General/outputs/2026-05-01/frahan_stonepack/tests/Frahan.StonePack.Tests/Program.cs` (+9 test registrations)

Next:
- HITL canvas validation for Phase I5 gate: register a synthetic
  3-marker case + a synthetic UTM-control-point case in Rhino,
  verify RMS < 1 mm on noise-free input. Save `.gh` files into
  `outputs/<date>/canvas_validation/registration/`.
- Phase F (scan ingest workflow), Phase G (mesh-bench), Phase H
  (reconstruction primitives via shim extension), and Phase I6-I15
  (hard-20% Cloud ICP with KD-tree / voxel / normal shim exports)
  per NEXT_STEPS.md §F-§I.
- Architecture report hand-off to claude.ai for §9.1-§9.14
  refinement pass before Phase F begins.

---

## 2026-05-15 (later) — Monument packing + BCO inspector / ingestion / heterogeneous track

Agent: claude_cloud (Opus 4.7, 1M ctx)
Branch: docs/frahan-autonomous-nightshift
Tests: 694 PASS / 0 FAIL / 56 SKIP (was 680 → +14 new tests)
Build: 0 errors, 0 warnings (4 projects, Debug + Release)

Done:
- **Monument packing** (3 GH + 4 Core + 1 test file):
  - `Frahan.Masonry.Quarry.Monuments` namespace: `Monument`,
    `MonumentInventory`, `MonumentPlacement`,
    `MonumentOrientationSampler` (24-rotation SO(3) axis-aligned group,
    det=+1), `BenchMonumentPacker` + `BenchMonumentPlan` (per-cell
    largest-first AABB-greedy, 8-corner half-space containment,
    fracture-respecting).
  - GH components: `MonInv`, `MonPack`, `MonInCell`.
  - 7 tests covering 24-rotation invariants, single-cell place,
    oversized → unplaced, fracture-split → one-cell only.
- **BlockCutOpt inspector / extension components** (gaps 1–7 of the
  2026-05-15 coverage audit; 7 GH components in 2 new GH files):
  - `BCOPareto` — 4-axis Pareto extrema per sub-zone (recovery, revenue,
    kerf-time, BCSdbBV — the fourth was hidden in `BCOOmni`).
  - `BCORobust` — Fisher-robust Monte Carlo wrapper (synthesis I8).
  - `BCOWatershed` — density-watershed adaptive sub-division (I5).
  - `VtuOut` — ParaView .vtu export of the optimal grid.
  - `Photo2Ply` — CSV-driven photogrammetry → PLY (Phase 11.5).
  - `AlgConv` — Zhang 2024 algebraic input → ConvexPolyhedron (I14).
  - `TnGran` — deterministic synthetic Tamil Nadu granite DFN.
- **3D DLBF generalisation + heterogeneous-extraction composite**:
  - `Dlbf3dMixedSizePacker` in Core (Chehrazad 2025 §5; variable-height
    pieces with optional `floorOnly` mode — default true for quarry).
  - 6 tests covering floor-only fill, stacked fill, heterogeneous
    heights, revenue-per-volume preference, forbidden columns, oversized
    pieces.
  - GH: `BCOMixedPack3D` (direct exposure) and `HeteroExt` (4-step
    composite: BCO at prime dim → forbidden = intersected cells →
    3D DLBF on the full catalogue → optional MonPack with monument
    meshes on a derived BlockGraph).
- Docs: `outputs/2026-05-14/connection_map/GRASSHOPPER_CONNECTIONS.md`
  updated with §2.1.8 (Monument) + §2.1.9 (BCO extensions) + §3.7
  (heterogeneous workflow) + §6.1 (new GUIDs).

Files touched (top-level):
- Template-General/.../src/Frahan.StonePack.Core/Masonry/Quarry/Monuments/
- Template-General/.../src/Frahan.StonePack.Core/Masonry/Quarry/BlockCutOpt/Dlbf3dMixedSizePacker.cs
- Template-General/.../src/Frahan.StonePack.GH/MonumentPackingComponents.cs
- Template-General/.../src/Frahan.StonePack.GH/BlockCutOptInspectorComponents.cs
- Template-General/.../src/Frahan.StonePack.GH/BlockCutOptIngestionComponents.cs
- Template-General/.../src/Frahan.StonePack.GH/BlockCutOptHeterogeneousComponents.cs
- Template-General/.../tests/Frahan.StonePack.Tests/MonumentPackingTests.cs
- Template-General/.../tests/Frahan.StonePack.Tests/Dlbf3dMixedSizePackerTests.cs
- Template-General/.../tests/Frahan.StonePack.Tests/Program.cs (test registrations)
- outputs/2026-05-14/connection_map/GRASSHOPPER_CONNECTIONS.md
- wiki/log.md, wiki/index.md

Pending:
- **HUMAN**: Rhino canvas smoke test for the 14 new components (3
  Monument + 11 BCO-extension). Drop each on a Rhino 8 canvas, walk
  through the heterogeneous workflow in §3.7 of the connection map,
  log the result in `wiki/algorithms/<topic>/validation_log.md`.

---

## 2026-05-15 — Health check + 7-item improvements punchlist

Agent: claude_cloud (Opus 4.7, 1M ctx)
Branch: docs/frahan-autonomous-nightshift
Commits: `add473a`, `f34e418`, `6fb604a`, `c1fa4c0`, `bb5d57f`,
         `a81ce4b`, `478d6b9`, `7b5ce0a` (8 commits this session)
Tests: 681 PASS / 0 FAIL / 56 SKIP (requires Rhino runtime)
Build: 0 errors, 0 code warnings

Done (all agent-doable items from the post-health-sweep punchlist):
- **Stale GH-metadata test expectations** refreshed against actual
  component GUIDs (commit `7e709ae` earlier same day; suite 665→680).
- **Health sweep**: build clean, tests green, LFS clean, branch in
  sync. Identified 8 improvement items.
- **Wiki orchestration files** (`index.md`, `health.md`, `log.md`,
  `audit_trail.jsonl`) landed at the wiki root (`f34e418`).
- **Stray untracked items resolved**: `wiki/papers/equations_and_diagrams/24_granite_scan_citation_network.md`
  committed (`6fb604a`); local-only `tasks/` duplicate at repo root
  removed (canonical copy already tracked under
  `outputs/2026-05-05/tasks/`).
- **Quarry → Masonry integration test** added; suite 680 → 681 PASS
  (`c1fa4c0`).
- **CI workflow** `.github/workflows/build-and-test.yml` lands;
  builds Core + EdgeMatching.Core on Windows runners (`bb5d57f`).
- **62 nullable warnings → 0** by adding `#nullable disable` to 2
  production files + 13 test files per project convention, and
  removing the unused `bool any` in `PlyMeshReader.cs` (`a81ce4b`).
- **Component icon design brief** written; raster art deferred to
  human pass (`478d6b9`).
- **Stability solver Phase B closed**: defensive
  `EnsureDefaultSolver()` call in GH component + stale TODOs cleared
  (`7b5ce0a`).
- Refreshed `outputs/2026-05-14/connection_map/GRASSHOPPER_CONNECTIONS.md`
  with the post-fix state (`add473a`).

Files touched (top-level):
- wiki/ (4 new orchestration files + updated health.md, log.md, audit_trail.jsonl)
- .github/workflows/ (new)
- outputs/2026-05-14/connection_map/, outputs/2026-05-15/icon_design/
- Template-General/.../src/Frahan.StonePack.Core/, src/Frahan.StonePack.GH/,
  tests/Frahan.StonePack.Tests/

Pending (last item on the punchlist):
- **HUMAN**: Rhino canvas smoke test for the 15 new components — drop
  each on a Rhino 8 canvas, walk through the four canonical workflows
  in `outputs/2026-05-14/connection_map/GRASSHOPPER_CONNECTIONS.md` §3,
  append the result to a `wiki/algorithms/<topic>/validation_log.md`
  entry, tag the commit, snapshot to `checkpoints/<tag>/`. Tracked as
  P1 in `wiki/health.md`.

Open (post-session improvements):
- P2 component icons (PNG drop, code path documented).
- P2 IPOPT P/Invoke (Phase C) for the stability solver.
- P2 Full-solution CI (requires RhinoCommon NuGet migration).

---

## 2026-05-14 — Layer 7 + GeoCut + GeoPack v0 + Quarry→Masonry bridge

Agent: claude_cloud (Opus 4.7, 1M ctx)
Branch: docs/frahan-autonomous-nightshift
Commits: `7e709ae`

Done:
- New `Frahan.Masonry.Quarry.CutOpt` namespace: BenchBlock,
  QuarryInventory, BlockYieldEstimator, ExtractionOrderOptimizer,
  SawBedScheduler, QuarryReportBuilder, BenchBlockSlabBuilder.
- New `Frahan.Masonry.Quarry.Ingestion`: GprRadargram + reader,
  GeoFractNetFracture + reader.
- New `Frahan.Masonry.Quarry.GeoCut`: SlabPlan, SlabYieldOptimizer,
  BilletCutter (spec 09 slice).
- New `Frahan.Masonry.Quarry.GeoPack`: CrackGraph, BlockGraph,
  BlockCandidate, builders (spec 08 v0, manual input).
- `BlockCutOptSolver.SolveAndExtract` — returns the winning OBB list.
- BestFitInventoryPacker rubble path fixed (per-course height bins).
- AshlarPack / BestFitPack: optional Start Plane + Display Transform.
- 15 new GH components, stable new GUIDs.
- 17 new tests; suite from 656 → 673 PASS at end of day.
- Docs: `FRAHAN_PIPELINE_MAP.md`, `GRASSHOPPER_CONNECTIONS.md`.

Files touched (top-level):
- Template-General/outputs/2026-05-01/frahan_stonepack/src/
- Template-General/outputs/2026-05-01/frahan_stonepack/tests/
- outputs/2026-05-14/connection_map/

Next:
- Refresh stale GH-metadata tests (deferred to 2026-05-15).
- Visual canvas validation (truth criterion (c)) on the new components.

---

## 2026-05-08 — Zhang 2024 parity + module README

Agent: claude_cloud (assumed; recorded from git log)
Branch: docs/frahan-autonomous-nightshift
Commits: `c36fcf3`, `2e165b6`, `8869ab3`, `5d8351a`, `5d7e4dd`

Done:
- BlockCutOpt I14 + Zhang cut-code parity: CompositeBlock + algebraic
  store + helpers.
- BlockCutOpt module README at module root.
- Zhang 2024 vs Frahan v2 gap analysis + build-out plan.
- Zhang 2024 cut-code distillation + cross-validation plan.
- Email draft for Porsani 2006 granite data.

---

(Earlier history available via `git log --oneline` and the spec
section "Roadmap and timeline" in `wiki/specs/17_…`.)

## 2026-06-04 (autonomous nightshift + morning)
Promoted nightshift deliverables to wiki (HITL-approved mapping): workflow+architecture
study -> specs/03_frahan_workflows_spec.md; 15 workflow SLM spines -> research/slm_spines/;
10 algorithm gap-spines -> research/slm_cards/ (now 20 cards, full workflow coverage, unified
_INDEX); plugin-wide interdisciplinary ROSES -> research/roses_synthesis/ROSES_plugin_wide_review.md;
quarry->monument/building evolved workflow -> fabrication_workflows/quarry_to_monument.md.
Reconciled an R4 duplicate (a new RecenterTransform was reverted; GeometryNumerics already
existed + committed 9418bd6). Validated GeometryNumerics in the real Rhino runtime via MCP
(run_csharp on slot armadillo): 5/5 PASS incl. the T6 int64-overflow guard. A1-A5 validation
fixtures re-baked in metres. Checkpoint/handoff automation (hooks) installed in settings.local.json.
Drafts + generators remain under outputs/2026-06-04/nightshift/.
