# Rhino dependency map (headless phase-out plan)

The plan for risk-register **H2** (the pipeline is only ~74% headless). This
maps every RhinoCommon-bound Core file by *what it uses Rhino for* and *how
hard it is to phase out*, so the conversion can proceed worst-effort-last.

Generated from the codebase 2026-07-07 (`grep 'using Rhino'` over
`src/Frahan.StonePack.Core`). **77 of 300 Core files** import RhinoCommon.

## Summary

| Subsystem | Easy | Hard | Total |
|---|---|---|---|
| Masonry | 2 | 31 | 33 |
| SurfacePacking | 0 | 13 | 13 |
| Discontinuity (geology) | 11 | 0 | 11 |
| Fabrication | 3 | 3 | 6 |
| ScanIngest | 2 | 4 | 6 |
| Voussoir | 0 | 3 | 3 |
| Packing | 2 | 0 | 2 |
| Quarry | 0 | 2 | 2 |
| Registration | 1 | 0 | 1 |
| **Total** | **21** | **56** | **77** |

- **Easy (21 files)**: use only Rhino *value types* (`Point3d`, `Vector3d`,
  `Transform`, `Plane`, `BoundingBox`, `Interval`, `Line`). These are pure
  arithmetic and convert to the Core primitives (`Vec3`, `Box3`) the same way
  `KinematicAnalysis` did (inline the math, drop `using Rhino.Geometry`). No
  geometry kernel needed.
- **Hard (56 files)**: use `Mesh` / `Brep` / `Curve` / `SubD` /
  `VolumeMassProperties` / `Intersection` — real geometry-kernel operations.
  These cannot go headless by inlining; they need either a headless mesh kernel
  (the out-of-process CGAL/geogram workers already exist for some) or they stay
  Rhino-bound and run under Rhino.Compute.

## Phase-out order (easy + high-value first)

### Track 1 — geology stack (11 files, all Easy) — highest value
The entire discontinuity/geology feasibility pipeline uses only vector math.
Converting it makes GPR-to-block-yield analysis headless (a real service
target). Order by size:

- `Discontinuity/BlockSizeMath.cs` (Vector3d:6)
- `Discontinuity/FractureIntensity.cs` (Vector3d:6)
- `Discontinuity/OrientationMath.cs` (Vector3d:7) — the shared normal/dip helper; convert first, it unblocks the rest
- `Discontinuity/DiscontinuitySetClusterer.cs` (Vector3d:8)
- `Discontinuity/PointCloudFacetExtractor.cs` (Point3d:3, Vector3d:4)
- `Discontinuity/InSituBlockSize.cs` (Vector3d:14)
- `Discontinuity/TerzaghiCorrection.cs` (Vector3d:14)
- `Discontinuity/CloudMath.cs` (Point3d:9, Vector3d:6)
- `Discontinuity/StereonetProjection.cs` (Point3d:3, Vector3d:10, Plane:3)
- `Discontinuity/Ingest/Discontinuity.cs` (Point3d:15, Vector3d:7)
- `Discontinuity/Ingest/DiscontinuityReader.cs` (Point3d:25, Vector3d:5)
- (`KinematicAnalysis.cs` — done 2026-07-06)

### Track 2 — packing + fabrication (5 Easy files)
- `Packing/GuillotinePackResult.cs` (Transform:5)
- `Packing/TreePackForest.cs` (Point3d:20, Vector3d:9, Transform:14) — the 3D
  guillotine forest; voluminous but all value types. Unblocks a Rhino-free
  Core guillotine path (noted in SYNTHESIS_3D as a blocker).
- `Fabrication/CutPath.cs` (Point3d:3, Plane:1)
- `Fabrication/BlockYieldOptimizer.cs` (Point3d:7, Plane:4)
- `Fabrication/CutOrientationOptimizer.cs` (Vector3d:22)

### Track 3 — registration + scan value-math (3 Easy files)
- `Registration/RegistrationApi.cs` (Transform:9) — ICP transforms
- `ScanIngest/PointCloudIcp.cs` (Transform:14)
- `ScanIngest/MetashapeProject.cs` (Point3d:1, Transform:1)

### Track 4 — the Hard 56 (defer / keep Rhino-bound)
Mesh/Brep/Curve-heavy: all of `SurfacePacking` (BFF charts, barycentric
mapping, mesh cleanup), `Masonry/Vault` + `Masonry/Nbo` (mesh stone fitting,
form-finding), `Voussoir`, `Quarry/BenchBoundary`, `DxfCutPlanExporter`
(Polyline), and the mesh readers. Options, per file:
1. Route through the existing out-of-process CGAL/geogram/CoACD workers where
   the operation is boolean/reconstruction (already headless there).
2. Introduce a minimal headless mesh/polyline type in Core (Vec3 + int[] tris,
   which `MeshPackItem` already is) and port the arithmetic-only parts.
3. Accept they stay Rhino-bound and run under Rhino.Compute for the full
   pipeline; only the Easy tracks reach a plain dotnet service.

## What this unblocks

Clearing Tracks 1–3 (29 files) would make the **geology feasibility**,
**2D/3D packing**, and **registration** paths fully headless — enough for a
service that does GPR-to-block-yield and nesting without Rhino. The Hard tier
(mesh/surface/vault) is the long tail and is lower priority for deployment.

See the [risk register](RISK_REGISTER.md) H2 for context and GitHub issue #14
for the tracking item.
