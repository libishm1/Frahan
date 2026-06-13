# What Is Left: Roadmap

Sole author: Independent Research. Open data, open source.

This is the consolidated, deduplicated, and prioritised list of open work,
merged from the per-chapter "Status and what's left" sections of all fifteen
chapters and the licensing register. Items are graded blocker / high / medium
/ low and tagged by subsystem. Each carries a single-line action. The grade
reflects what blocks a public or commercial release, not difficulty.

Honesty note. Several "what's left" items are honesty constraints, not
defects: a stated boundary on a claim (for example, the outline-only density
boundary against the reference physics nester). These are listed so the claim
boundary is preserved, not because the code is wrong.

---

## Blockers — must resolve before a public or commercial release

| Subsystem | Item | Action |
|---|---|---|
| Licensing (E1) | Kintsugi / PuzzleFusion++ is NON-COMMERCIAL, not plain GPL; covers the ported C# and the ~255 MB `kintsugi.bin` weights. | Keep Kintsugi as a separately distributed, optional, research-only package outside the default install; verify root LICENSE, port README, and any repo-root statement say research-only non-commercial, not plain GPL. |
| Licensing | Root LICENSE is a placeholder header, not full GPL text, while the dist links Kintsugi.Port. | Replace root LICENSE with canonical `gpl-3.0.txt`; isolate Kintsugi.Port behind a separate build so the rest can be relicensed if desired. |
| Licensing (spec 16) | No `THIRD_PARTY_NOTICES.md` and no `frahan_reference_register.md` at audit time; BFF + SuiteSparse + OpenBLAS + GFortran, csg.js, NTS, laszip.net, pye57, and any copied source lack attribution rows; per-file SPDX headers missing on copied source. | Create `THIRD_PARTY_NOTICES.md` (one row per dependency) and `docs/index/frahan_reference_register.md`, with per-file SPDX headers on copied source, before external review. |

---

## High — correctness or canvas-reachability defects

| Subsystem | Item | Action |
|---|---|---|
| Quarry / GPR | `RecoveryCascade` has no GH consumer; `FractureBlockPack` ships a duplicate self-contained recovery engine, so the validated Core cascade is unreachable on canvas (silent-disagreement risk). | Refactor `FractureBlockPack` to call the validated Core `RecoveryCascade` (facade-not-fork); retire the duplicate engine and add a shared-call-path regression. |
| Masonry / CRA | `AdmmQpSolver` cold-start degrades steeply past ~50 contact interfaces (54-iface 5.4 s, 147-iface 86 s), so wall-scale equilibrium does not converge in interactive time. | Keep the LS-first KKT certificate in `MasonryStabilityChecker`, add warm-start / per-element verification for large mixed assemblies, and benchmark conditioning to 300 interfaces; document per-element as the wall-scale pattern. |
| Edge-Matching | Independently tessellated shard rims yield zero cross-panel hash hits (self 172, cross 0, `ProjectionPairFinder.cs:16-22`); the geometric 3D path assembles only via the projection bootstrap, which leaves some MST interfaces loose (2 of 5 fully in contact). | Treat the learned Kintsugi Port as the production 3D path; for the geometric engine, replace independent tessellation with a shared-rim resampling so cross-panel hashes hit. |
| Mesh | No native DLL in the default install: every CGAL and Geogram operation (boolean, segment, skeleton, remesh, reconstruct) is unavailable until the user builds `frahan_cgal` / `frahan_geogram` from `native/`; the default experience is managed BSP CSG plus Rhino-side repair only. | Ship a build or fetch step for the native shims (the licence mitigation stays, but document the capability gap and fetch path clearly). |
| Mesh / Lab (E6) | `TetGeogram` and volumetric Voronoi blocks wrap Geogram's TetGen path, AGPL and ON by default in the geogram build. | Build the geogram shim with `-DFRAHAN_WITH_TETGEN=OFF` for any AGPL-free packaging; a packager turning on the shims must honour the flag. |
| Fabrication / Quarry | The georeferenced bed-following recovery of example 08 has no shipped physical-marking component: the math (`GeoreferenceMath`) closes the scan-to-world transform but no GH node turns the oblique cut planes into a georeferenced marking output. | Ship the physical-marking GH component that consumes the oblique cut planes and the Horn fit to mark the real block. |

---

## Medium — partial implementations, attribution, and scale-invariance

| Subsystem | Item | Action |
|---|---|---|
| 2D Nesting | Standalone greedy Trencadis box (`F2D00002`) is a skeleton returning empty; a ghost on the primary ribbon violates `AGENTS.md` §6. | Either implement the greedy pack or move the box off the primary ribbon; route users to Catalog (`F2D00007`) / Pipeline (`F2D00009`). |
| 2D Nesting | Deployed `.gha` can lag current source on live 2D solves (KB-7); an old build may overlap parts where current source does not. | Rebuild and redeploy the `.gha` before trusting any live 2D result; gate release on a live zero-overlap check. |
| 2D Nesting | The fold-FreeNestX-into-HoleNest port (eight UI features, then hide) is decided but not landed; the plugin ships two NFP nesters and FreeNestX's shipped path runs no concave-overlap verify (KB-4). | Port FreeNestX's eight unique UI features into HoleNest, then mark FreeNestX `[Obsolete]` + `Exposure=hidden` with its GUID preserved. |
| 3D Packing | Heightmap citation inconsistency: `Pack3DMeshHeightmapComponent` labels the mesh-pile method "Frahan-original" while `Pack3DIrregularContainerComponent` attributes the same method to "Park and Han 2024" (`[R8]`, no DOI, placeholder). | Reconcile the two attributions before external review. |
| 3D Packing | `Settle 3D` needs `libbulletc.dll` beside the `.gha`; it ships in `install/plugin/` but is absent from a source-only build (component warns and does nothing without it). | Document the native dependency and bundle/fetch `libbulletc.dll` with the deploy. |
| 3D Packing | The compactness gain over the heightmap baseline is ~1.05 to 1.15x, not the original 2x target. | Keep the mesh-accurate-port item open to push past ~35% with active void-insertion; report the delta as modest, not 2x. |
| 3D Packing / Slab | The default `SlabCutByFractures` path is exact for convex slabs only and explodes combinatorially on large slabs with many planes; non-convex or large work needs the opt-in CGAL backend. | Document the convex-only limit and route non-convex / large cuts through the CGAL backend. |
| Quarry | `BlockCutOptOmniSolver` coarse-to-fine is a stub: both `UseCoarseToFine` branches run the fine-step uniform sweep (worst-case wall clock). | Implement the true coarse-to-fine Pareto sweep; until then, document the flag as a no-op. |
| Quarry | I13 (Tian 2025 multi-model joint generator) is proposed only; I14 (Zhang et al. 2024 composite multi-convex block) is partial. 12 of 14 improvements shipped. | Ship I13 and complete I14, or mark both explicitly as future work in the README. |
| Quarry | Bed-bounded hexahedra and the flat/oblique cost/volume/balanced frontier live in example generators, REPORTED not gated. | Promote the bed-bounded hexahedra fix and the frontier metric into a Core class with a unit-test gate. |
| GPR | `.gsf` (Geoscanners AKULA) is read-only via conversion; any dataset shipping only `.gsf` cannot enter the pipeline without a manual GPRSoft/RGPR export to SEG-Y. | Keep the bridge-not-guess dead-stop; document the conversion path (blocks a real Tamil Nadu charnockite path). |
| GPR | Literature-default presets (granite frequency family, travertine/andesite/limestone) carry paper velocities but extrapolated filter windows; only `marble_600` and `granite_160` are `IsEmpirical=true`. | Validate the remaining presets end-to-end, or keep the `IsEmpirical` warning prominent. |
| GPR | Multi-channel GSSI `.dzt` (`rh_nchan > 1`) is read as a single concatenated stream; companion MALA `.cor`/`.mrk` GPS markers are parsed but not written to `GprTrace.X/Y` (trace geometry is a straight line). | De-interleave multi-channel DZT and apply MALA marker positions when a multi-channel / marked file appears. |
| Surface Packing | `PackOnSurfaceComponent.cs:41` mis-credits Floater 2003 MVC; the lift is plain barycentric (Cramer's rule), confirmed by the SLM card. | Soften the attribution to the mean-value-coordinate family (after Floater 2003) or replace with a classical barycentric citation; the shipped math is correct, only the attribution is wrong. |
| Surface Packing | A single global chart scale `s` mis-sizes parts on conformal charts where the local conformal factor e^u departs from the perimeter-weighted average. | Add per-face / per-cone-patch scale plus adaptive re-cut driven by `ChartFlatnessReport`. |
| Surface Packing | `BarycentricMapper2DTo3D` inverse map is an O(P·F) linear scan, author-stated ceiling ~2000 faces. | Add an RTree over flat-face bounding boxes to make the lift O(P log F). |
| Surface Packing | `ChartDistortionAnalyzer` edge-stretch metric cannot detect BFF foldovers (scalar lengths ignore orientation sign). | Add a per-face signed-area sign test (flag M3). |
| Surface Packing | Tolerance system unreconciled and units undeclared: four absolute epsilons plus a 0.01 sampling tolerance, none scaled to chart size; at m-scale the 1e-6 containment eps drops valid boundary points and returns a null curve. | Route all surface-packing tolerances through the scale-relative budget; record the model unit in `FrahanSurfaceChart`. |
| Masonry / CRA | The alternating-convex CRA certificate is sound only in the certifying direction; "not certified" can be a false negative on the non-convex CRA NLP (`CraStabilityChecker.cs:45-49`). | Document the verdict as conservative: stable claims are sound, unstable claims are conservative; do not assert sharpness. |
| Masonry / CRA | The J interlock metric, the Coursing-morph continuum, and the Lambda imposition formalisation are A-candidate originality claims; `AGENTS.md` §9 forbids "novel" without a completed sweep (Legakis et al. 2001 is the closest known prior). | Run the targeted prior-art sweep before any external novelty claim. |
| Edge-Matching | `BlockPairMatch3DComponent.cs:43-45` declares the VSA face partitioner a Frahan stub; real stone-to-cell work routes to the Hungarian Stone-Cell Match via `[RelatedComponent]` (:41). | Either implement the VSA partitioner (Cohen-Steiner 2004) or keep the honest stub-plus-redirect and mark it future work in the README. |
| Edge-Matching | The per-facet projection bootstrap, antiparallel SE(3) lift composition, and 3D-disposes verification gate are original-research A-candidate; the prior-art sweep has not been run. | Run the targeted prior-art sweep per `AGENTS.md` §9 before asserting novelty. |
| Kintsugi | `AutoAgglomerate` outer loop is a skeleton: the per-round merge body is stubbed; the shipped path is the single-round denoise-then-verify, not the iterative paper schedule. | Implement the per-round merge / point-match-deletion / FPS resample, or document the single-round path as the shipped behaviour. |
| Kintsugi | Manual C# denoiser drifts ~3-5% from the libtorch kernels; the TorchSharp path removes it but needs `LibTorchSharp.dll` + a working libtorch, with a documented silent-fallback. | Surface the fallback prominently; bundle/fetch the libtorch path for paper-exact runs. |
| Kintsugi | Port mode reassembles reliably only on Breaking Bad-like fractured-scan fragments; synthetic primitives and smooth rims under-place (honesty bound). | State the distribution-only generalisation; keep the geometric path the safe default on clean rims. |
| Mesh | No managed fallback for OBB / skeleton / partition / segmentation: these are CGAL-only and throw when the shim is absent. | Add a managed fallback or document the CGAL-only requirement for these operations. |
| Licensing (E2) | xBIM is CDDL-1.0, distribution-incompatible with GPL-3.0. | Move to the licence-clean GeometryGymIFC path (HITL ruling), or use an out-of-process IFC writer. |
| Licensing (E5) | `BestFitInventoryPacker.cs:26-29` and the Masonry facade `BestFitPackComponent.cs:30` cite a non-existent "Gramazio/Kohler/Eichenhofer 2017 CAD paper"; the Core lineage is correctly Furrer 2017 / Johns 2020. | Correct both `[Algorithm]` attributes to Furrer/Johns before external review. |
| Licensing | Stale Kintsugi `[Algorithm]` attribute still reads "Full GPL-3.0 honest port ... underway / NO learned model" (`KintsugiAssemblyComponent.cs:62-68`), describing a pre-port state. | Correct the attribute to match the ledger (learned port landed, licence non-commercial) before academic review. |
| Fabrication | Wire-saw v1 is planar only: kerf compensation is skipped on a non-planar cut curve (warned); curved-surface, variable-tension, bidirectional are backlog. The robot-mounted diamond-wire workflow remains research-grade. | Add curved-surface ruled-decomposition planning; keep the research-grade caveat. |
| Fabrication | Carving Stages input order is load-bearing: reordering the inputs breaks canvases saved against the proven layout (the v2 regression); heavy scans must be decimated before carving (KB-1/KB-2). | Keep the input order frozen and the synchronous-cached design; document decimate-first. |
| Ingestion | E57 worker is an external dependency (python + pye57 + numpy + `frahan_e57_worker.py` beside the `.gha`); if any is missing the component reports the failure but cannot read the file. | Document the runtime dependency and a fallback (convert to PLY/LAZ) for installs without python. |

---

## Low — perf limitations, bounded residuals, and honesty boundaries

| Subsystem | Item | Action |
|---|---|---|
| 2D Nesting | Example 28 (hole nest) ships no rendered figure and no README; the CNH renders borrow examples 10 and 12. | Add a HoleNest-specific capture and README so the hole-aware lane is shown directly. |
| 2D Nesting | Rect shelf fast-path only activates at `spacing == 0`; `spacing > 0` defers to the general engine. | Add exact rect-dilation bookkeeping for `spacing > 0`; perf limitation, correct fallback already exists. |
| 2D Nesting | Residual penetration band (~2e-5 caller units) can be accepted after the compound gate; deeper ones cannot on any path. | Document the bounded residual in the claim; it is far inside the fabrication budget but is not a zero guarantee. |
| 2D Nesting | Outline-only strip density still trails the reference physics nester by 6-10%; CNH's win is the hole-aware lane only. | Preserve this boundary in any external claim (honesty constraint, not a defect). |
| 3D Packing | Bullet settle is non-deterministic (a physics simulation, not a search); re-runs can differ, one stone may hang mid-drop. | Document the non-deterministic settle as expected behaviour. |
| 3D Packing | Heightmap proxy is a conservative vertical-column test on envelopes, retained as the validated baseline; the components route users to Settle 3D and Block Pack (Tree) as the evolved paths. | Keep the proxy as baseline; no fix needed, by design. |
| Quarry | Kerf volume is a film approximation `A_xy·k/2`, not exact inter-cell kerf; recovery denominator is approximate. | Refine to exact inter-cell kerf alongside the sub-division work (documented Phase-1). |
| Quarry | `RecoveryCascade` header originality wording (E9): formerly self-labelled "novel", now softened to the Murugean 2026 BoEGE cite. | Complete the prior-art sweep per `AGENTS.md` §9 to confirm the A-candidate status. |
| Quarry | `RecoveryCascade` `AabbOf` child region is exact only for psi-only oriented blocks; a fully tilted pose feeds a slightly loose axis-aligned bound (conservative, never drops a real block). | Tighten the child bound for tilted poses, or keep the conservative bound documented. |
| Quarry | Example 08 marble GPR data is CC-BY-NC-ND (research/testing only). | Do not use the flagship marble study in commercial product demos; swap to a CC-BY dataset for any commercial demo. |
| Quarry | Extraction Order Optimizer is self-declared Frahan-original without a prior-art sweep. | Run the prior-art sweep (A-candidate) per the originality framework. |
| GPR | Example 3 ships a radargram PNG plus the `.gh` canvases but no rendered fracture-pick / 3-D surface figure; marked pending live regeneration. | Regenerate the example with a shaded fracture-surface capture. |
| GPR | Reconstruction path needs native shims (geogram / CGAL); the default install has no reconstruction and falls back to loft-only surfaces. | Keep the managed loft path as the default; document the shim requirement for cloud reconstruction. |
| Voussoir | Funicular form-finding is external (the arch supports a true catenary intrados, but the pendentive generator is the closed-form sphere only); the `ThrustCurve` field is unpopulated. | Route a general form-found shell through the compas-RV reference pipeline; document the scope boundary. |
| Voussoir | Faceted cells by construction (straight-chord facets, O(1/N²) error); no equilibrium check inside the tab; adjacency graph not auto-built by the factory; example READMEs cite Sakarovitch / Galletti / Hooke not yet keyed (Hooke now keyed in `99_references.md`). | Raise count for accuracy; wire the assembly into Masonry Stability for a verdict; emit station/grid adjacency losslessly; finish keying the remaining stereotomy-history cites. |
| Surface Packing | Far-from-origin (UTM-scale) charts lose ~4 mantissa decimals because OBJ is written at raw world coords G10 with no recenter (flag T1). | Recenter to the bbox centroid before OBJ write and undo after the inverse map. |
| Masonry / CRA | Stale audit note E4: a prior digest claimed the shipped RBE verdict still wires the sign-buggy `RbeQpFormulation.Build`; current source uses `BuildPhysicsCorrected` (`MasonryStabilityRbeComponent.cs:305`). Legacy `Build` survives only for sign-pinning unit tests. | Mark the legacy `Build` `[Obsolete]` to prevent future mis-wiring; the note is stale, not an active bug. |
| Masonry / CRA | `PolygonalMasonrySequence3DComponent` (`C5F18B4D`) overlaps `BlockBuildOrderer` (3D contact-support DAG); two sequencers for one job. | Merge to a single 3D sequencer (documented architecture candidate). |
| Masonry / CRA | `examples/02_masonry_assembly` ships `.gh` + `.3dm` only, no PNG; the assembly colour/order sequencing figure cannot be embedded. | Add a rendered PNG capture so the assembly-sequencing figure is embeddable. |
| Edge-Matching | Three duplicate absolute-orientation kernels (Horn quaternion in `RigidTransformRecovery`, the Georeference private Horn, and SVD-Kabsch in `ConstrainedIcp3D`/`SoftIcpRefiner`) solve the same rigid fit by two routes. | Unify on one MathNet-SVD kernel; both routes are correct, so this is a refactor not a bug. |
| Edge-Matching | The `[Algorithm]` at `EdgeMatchSolveComponent.cs:25` names "Phase correlator FFT" while `PhaseCorrelator.cs:29-34` is direct O(n²) circular L1 correlation, not an FFT. | Reword the attribute to "direct cross-correlation" to avoid implying a frequency-domain implementation; the code is correct and deterministic. |
| Mesh | CGAL/geogram shim wrappers live on the `Lab` subcategory while Repair/Diagnostics/Sanitize/Close Holes/Scan Reconstruct are on `Mesh`; a UX inconsistency, not a code fault. | Reconcile the tab split, or document the Lab-vs-Mesh placement rationale. |
| Mesh / Lab | `DecimateGeogram` / `DecimateCgal` redirect to `Mesh Repair` because there is no production decimate component yet (the lab-not-an-island redirect is satisfied but a production node is missing). | Promote a production `Mesh Decimate` out of `Lab`. |
| Lab | Retired `RepairAuto` (`F2D000D0`) is `[Obsolete]` + `Exposure=hidden`, superseded by `Sanitize Mesh (Backend=Auto)`, GUID preserved (hide-not-delete done correctly). | No fix; documented as correct supersession. |
| Reports / Analysis | `ChartFlatnessReport` feeds only the audience terminal today; the Reports and Analysis tabs ship no dedicated example folder (figures borrow example 09). | Drive an adaptive per-face surface re-cut from the flatness classifier; add a Reports/Analysis example capture. |
| Fabrication | G-code parser subset: v1 supports `G00/G01/G02/G03/G17/G20/G21/G90` plus `F S M N`; `G91` incremental and `G18/G19` non-XY arc planes are parsed but flagged, not solved. | Extend the solved subset, or keep the graceful warnings (not failures). |
| Fabrication | Robot adapters depend on third-party plugins (KUKAprc paid-tier; visose/Robots installed separately, `Robots.dll` not bundled). | Decide the packaging; document the external-plugin dependency. |
| Fabrication | Fit-in-block is axis-aligned: v1 matches sorted extents largest-to-largest; a sculpture fitting only in a tilted orientation reads as not fitting. | Add an OBB-exact orientation search. |
| Fabrication | Example 05 (artist pointing machine) ships the carving `.gh` + light `.3dm` only, no PNG; figures borrow examples 04/24/25/08. | Add a rendered carving-stages capture. |
| Ingestion | Vector readers do not reproject: output curves stay in source-CRS units (Loviisa `EUREF_FIN_TM35FIN`), GeoJSON carries no CRS. Correct (no silent datum error) but pushes reprojection onto the user. | Document the no-reproject posture; offer an opt-in reprojection helper. |
| Ingestion | No `.las`/`.laz` canvas reader validated end-to-end in Rhino on the 357M-point tile; `.laz` ingest is routed through the harness `laszip.net.dll`. | Run a live in-Rhino validation of the LAS/LAZ component on the large tile (the remaining truth-criterion step). |
| Architecture | Four README-less canvases (examples 01, 02, 03b, 28) ship `.gh` + `.3dm` with no README and no rendered PNG; their pipeline graphs are read from the canvas. | Author READMEs and render PNGs for the four canvases. |

---

## Priority order (top to bottom)

1. **Resolve the Kintsugi non-commercial split and the root LICENSE (E1, blocker).** This is the single item that gates any public or commercial release; everything ships under the wrong terms until it is correct.
2. **Add `THIRD_PARTY_NOTICES.md` and the reference register (spec 16, blocker).** Required before external review; cheap to author, mandatory to ship; covers BFF, csg.js, NTS, laszip.net, pye57, and the numeric stack.
3. **Make `RecoveryCascade` the on-canvas engine; retire the `FractureBlockPack` duplicate (high).** Removes a silent-disagreement risk and makes the validated cascade reachable.
4. **Hold the line on the two 3D-path boundaries (high).** CRA equilibrium does not converge interactively past ~50 contact interfaces (warm-start / per-element verification needed); the geometric 3D reassembler only assembles via the projection bootstrap because independent tessellation kills cross-panel hashes (the learned Kintsugi Port is the production 3D path). Both must be stated, not papered over.
5. **Remove the ghost greedy Trencadis box and rebuild/redeploy the `.gha` (medium).** Two §6 canvas-honesty fixes: no empty-output node on the primary ribbon, no stale build overlapping live parts.
6. **Correct the fabricated and mislabelled citations (E5 / Floater / FFT / stale Kintsugi attribute, medium).** Fix the BestFit "Gramazio 2017" attribute to Furrer/Johns, soften the Floater-2003 MVC credit on the barycentric lift to the mean-value-coordinate family, reword the "Phase correlator FFT" attribute to direct cross-correlation, and correct the pre-port Kintsugi attribute; all required before any external review under §9.

---

## Research-led future directions (added 2026-06-14)

From the field-to-factory site-visit package and the computational-gemology
synthesis (research wiki: `krishnagiri_granite_field_to_factory/` and
`computational_gemology_robotic_granite_fabrication.md`). These are new
capability directions, not defects in shipped code.

### Near-term — the photogrammetry-cloud -> discontinuity bridge

Frahan already has the Load E57/LAS/PLY cloud readers, the NTS vector reader, and
the GPR `FractureExtractor`. The gap is turning a UAV/TLS point cloud into joint
sets. The field report's pipeline is Metashape/DJI Terra/Pix4D -> CloudCompare +
FACETS/Compass/DSE -> fracture traces (DXF/GeoJSON/CSV) -> Frahan.

| Subsystem | Item | Action |
|---|---|---|
| Ingestion | No reader for CloudCompare structural output (FACETS facets, Compass traces/planes, DSE sets). Cheapest win. | Add a fracture-trace ingest that reads DXF/GeoJSON/CSV planes + traces into the fracture model, beside the Shapefile reader + GPR picks. |
| GPR / Mesh | ~~No in-Frahan facet extraction~~ **LANDED 2026-06-14**: `Discontinuity Sets (Cloud)` (D5F10047) region-grows planar facets (FACETS, Dewez 2016) with dip/dip-direction. | Optionally expose the facet primitive separately and add the convex facet polygon (alpha-shape) contour. |
| Quarry | ~~No discontinuity-set + spacing summary~~ **LANDED 2026-06-14**: same component clusters poles into joint sets by antipodal mean-shift (DSE, Riquelme 2014) + reports normal spacing. Validated on the Tongjiang quarry-face cloud (4 sets, 16.6 s/140k). | Add the Jv / wJd block-size proxy and a stereonet report card. |
| Reports / Analysis | No structural-geology summary card. | Add a stereonet + joint-set / spacing report card (Lab/Reports tab). |
| Ingestion | The manual-mapping-calibrates-the-cloud audit step is unmodelled. | Add a scale/registration QA that aligns scanline measurements to the cloud (manual-vs-digital audit). |

Prerequisite already tracked in **Low**: the LAS/LAZ canvas reader live-in-Rhino
validation on the large tile.

### Frontier — robotic granite fabrication (diamond-planning analogue)

Scientifically grounded but production-unproven; treat as R&D, not v1.x.

| Theme | Item | Evidence posture |
|---|---|---|
| Sensing | Nonlinear-ultrasonic cleavage-plane (rift / grain / hardway) mapping — augment linear US with the acoustic nonlinearity parameter beta. | Granite study shows beta more sensitive than velocity/attenuation; production-scale tomography not yet routine. |
| Closed loop | AE + spindle power/current + force supervisory control for wire-saw / robotic cutting (feed-speed adaptation, workpiece protection). | Strong in machining; ready for supervisory use before autonomous interior-defect avoidance. |
| Closed loop | Live ahead-of-tool tomographic replan (cut-as-acoustic-probe). | Speculative; enabling pieces (sonic tomography, scan-plan-rescan) exist separately, integration not found in production. |
| End-effector | Laser-spalling / laser-preconditioned hybrid saw-or-waterjet granite end-effector. | Laser rock-spallation is real; a closed-loop stone-production cell is unproven. |
| Optimisation | Structural-aesthetic co-optimisation: predict 3D vein trajectories through the block and co-optimise against the fracture field. | Today's vein matching is 2D slab-image + nesting only; the volumetric solver is a genuine research opportunity. |
