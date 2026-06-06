# 00 - Frahan Project Overview

**Spec version:** 0.1 (overnight refactor 2026-05-03)
**Audience:** any next agent or human contributor opening this folder cold.

## 1. Product definition

Frahan is a **material-aware architectural packing and fabrication-fitting
system** built as a Rhino 8 / Grasshopper / RhinoCommon plugin family in
C# (with optional native C++ backends loaded lazily).

Frahan is not a generic geometry toolbox. Every feature must support one
of the following goals:

1. 2D irregular boundary-aware packing.
2. Trencadis-style fragment packing.
3. Surface Trencadis packing.
4. 3D Ashlar stone packing.
5. Quarry block extraction.
6. Crack-aware cutting.
7. GeoPack and GeoCut workflows.
8. Mesh repair, slicing, remeshing, and validation.
9. Optional native C++ backends loaded lazily.
10. Learning-guided packing only **after** deterministic heuristics are stable.
11. Fabrication reports, yield metrics, visual debugging, and safe validation.

## 2. What ships today (live)

The current shipping artefact is **Frahan StonePack 0.5.6 (Rhino 8)**,
delivered as
`Template-General/outputs/2026-05-01/frahan_stonepack/dist/frahan_stonepack-0.5.6-rh8-win.zip`.
It contains:

- `Frahan.StonePack.Core.dll` (managed, dual-target net48 / netstandard2.0)
- `Frahan.StonePack.gha` (Grasshopper assembly, net48)
- `Frahan.StonePack.Rhino.rhp` (Rhino plugin shell, net48)
- The bundled BFF runtime (`bff-command-line.exe`) plus the SuiteSparse
  / OpenBLAS / LAPACK DLL set (Windows-x64).
- `manifest.yml` and `README.md`.

Live Grasshopper components (the public surface today):

- 2D NFP-based packing (`NfpPack2DComponent`, `NfpTestComponent`,
  `Pack2DBottomLeftComponent`, `Pack2DIrregularSheet*Component`
  family at V1 / V2 / V3 / V506).
- 3D irregular into rectangular and irregular containers
  (`Pack3DIrregularComponent`, `Pack3DIrregularContainerComponent`,
  `Pack3DMeshHeightmapComponent`).
- Surface packing prototype
  (`SurfaceChartComponent`, `PackOnSurfaceComponent`,
  `PackSurfacesComponent`).
- Validation utility (`ValidatePackedTransformComponent`).

See `04_frahan_grasshopper_component_spec.md` for the catalogue.

## 3. What is planned (per runbook + research)

- Frahan **2D Trencadis** family (boundary rail index, fragment
  descriptors, edge match, trim suggestions, residual voids,
  packing report).
- Frahan **Surface Trencadis** family (surface patch, column unwrap,
  surface Trencadis solver, distortion report).
- Frahan **3D Ashlar** family (stone proxy mesh, stone descriptor,
  course segmentation, ashlar volume pack, face match, cut
  suggestions, contact graph, masonry report).
- Frahan **GeoPack** family (point-cloud / mesh / GPR-slice import,
  unit normalisation, crack candidate detection, crack surface fit,
  block-graph, uncertainty tagging, candidate generation).
- Frahan **GeoCut + QuarryCutOpt** family (bench block builder, rift
  ledger, slab forest pack, billet cutter, saw-bed optimiser,
  yield/waste reports).
- Frahan **Mesh + native backend** family (mesh diagnostics, repair,
  simplify, slice, remesh, collision proxy, native backend status).
- Frahan **Reports / Export** family (CSV, JSON, GraphML, DXF, CNC,
  3DM package).
- **Learning-guided** ordering - only after deterministic heuristics
  are stable.

See specs **05–17** for the per-module detail.

## 4. Naming reality check (current vs target)

The runbook target naming is `Frahan.{Core, GH, Geometry2D, Geometry3D,
Surface, Mesh, NativeBridge, Native.*, QuarryCutOpt, GeoPack, GeoCut,
Tests, Benchmarks, Docs}`. The live source uses `Frahan.StonePack.{Core,
GH, Rhino}`. Specs in this folder write the **target name** in prose with
the **current name** in parentheses on first mention. See
`docs/index/frahan_naming_drift_report.md`.

## 5. What this folder contains

| File | Role |
| --- | --- |
| `00_frahan_project_overview.md` (this file) | One-page entry point |
| `01_frahan_software_principles.md` | Core engineering rules |
| `02_frahan_architecture_spec.md` | Assembly / namespace / dependency layout |
| `03_frahan_module_map.md` | Module ↔ source-file map |
| `04_frahan_grasshopper_component_spec.md` | Per-component contract |
| `05_frahan_2d_trencadis_packing_spec.md` | 2D Trencadis spec |
| `06_frahan_surface_trencadis_spec.md` | Surface Trencadis spec |
| `07_frahan_3d_ashlar_packing_spec.md` | 3D Ashlar spec |
| `08_frahan_geopack_spec.md` | GeoPack spec |
| `09_frahan_geocut_spec.md` | GeoCut spec |
| `10_frahan_quarrycutopt_spec.md` | QuarryCutOpt spec |
| `11_frahan_mesh_and_native_backend_spec.md` | Mesh + native backend spec |
| `12_frahan_learning_guided_packing_spec.md` | Learning-guided spec |
| `13_frahan_testing_and_validation_spec.md` | Testing strategy |
| `14_frahan_minimal_pre_factory_test_plan.md` | Pre-factory checklist |
| `15_frahan_agent_implementation_plan.md` | What agents should build, in what order |
| `16_frahan_licensing_and_source_porting_policy.md` | Licensing rules |
| `17_frahan_roadmap_and_timeline.md` | Roadmap |
| `18_frahan_open_questions.md` | Open questions |

## 6. Confidence and limits

- Coverage of live source: **high** - every `.cs` in the canonical
  `Template-General/outputs/2026-05-01/frahan_stonepack/src/` tree
  was inventoried tonight.
- Coverage of proposed modules: **medium** - these are derived from
  research markdowns and the runbook, not from compiled code.
- Coverage of native backends: **low** - starter zips are catalogued
  by name only; not extracted tonight (binary scope).
