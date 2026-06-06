# 03 - Frahan Module Map

**Spec version:** 0.1
**Method:** derived from `docs/index/frahan_class_method_component_audit.md`.
Maps every live source file (and every proposed module) onto the target
naming layout from `docs/index/frahan_naming_drift_report.md`.

## 1. Live source ↔ target module map (Frahan.StonePack 0.5.6)

| Target module | Target file path (after future rename) | Live file path today |
| --- | --- | --- |
| `Frahan.Core` | `src/Frahan.Core/Geometry/GeometryPrimitives.cs` | `src/Frahan.StonePack.Core/GeometryPrimitives.cs` |
| `Frahan.Core` | `src/Frahan.Core/Heightmap/Heightmap.cs` | `src/Frahan.StonePack.Core/Heightmap.cs` |
| `Frahan.Core` | `src/Frahan.Core/Heightmap/MeshPileHeightmap.cs` | `src/Frahan.StonePack.Core/MeshPileHeightmap.cs` |
| `Frahan.Core` | `src/Frahan.Core/Heightmap/OrientedMeshHeightmap.cs` | `src/Frahan.StonePack.Core/OrientedMeshHeightmap.cs` |
| `Frahan.Core` | `src/Frahan.Core/Container/IrregularMeshContainer.cs` | `src/Frahan.StonePack.Core/IrregularMeshContainer.cs` |
| `Frahan.Core` | `src/Frahan.Core/Models/PackingModels.cs` | `src/Frahan.StonePack.Core/PackingModels.cs` |
| `Frahan.Core` | `src/Frahan.Core/Models/MeshPackingModels.cs` | `src/Frahan.StonePack.Core/MeshPackingModels.cs` |
| `Frahan.Core` | `src/Frahan.Core/Solvers/GreedyHeightmapPacker.cs` | `src/Frahan.StonePack.Core/GreedyHeightmapPacker.cs` |
| `Frahan.Core` | `src/Frahan.Core/Solvers/GreedyMeshHeightmapPacker.cs` | `src/Frahan.StonePack.Core/GreedyMeshHeightmapPacker.cs` |
| `Frahan.Surface` | `src/Frahan.Surface/BarycentricMapper2DTo3D.cs` | `src/Frahan.StonePack.Core/SurfacePacking/BarycentricMapper2DTo3D.cs` |
| `Frahan.Surface` | `src/Frahan.Surface/BffCommandLineRunner.cs` | `src/Frahan.StonePack.Core/SurfacePacking/BffCommandLineRunner.cs` |
| `Frahan.Surface` | `src/Frahan.Surface/ChartDistortionAnalyzer.cs` | `src/Frahan.StonePack.Core/SurfacePacking/ChartDistortionAnalyzer.cs` |
| `Frahan.Surface` | `src/Frahan.Surface/ChartScaleComputer.cs` | `src/Frahan.StonePack.Core/SurfacePacking/ChartScaleComputer.cs` |
| `Frahan.Surface` | `src/Frahan.Surface/FaceCornerUvTable.cs` | `src/Frahan.StonePack.Core/SurfacePacking/FaceCornerUvTable.cs` |
| `Frahan.Surface` | `src/Frahan.Surface/FrahanSurfaceChart.cs` | `src/Frahan.StonePack.Core/SurfacePacking/FrahanSurfaceChart.cs` |
| `Frahan.Surface` | `src/Frahan.Surface/MeshCleanup.cs` | `src/Frahan.StonePack.Core/SurfacePacking/MeshCleanup.cs` |
| `Frahan.Surface` | `src/Frahan.Surface/MeshObjIO.cs` | `src/Frahan.StonePack.Core/SurfacePacking/MeshObjIO.cs` |
| `Frahan.GH` | `src/Frahan.GH/Components/Pack2D*.cs`, `Pack3D*.cs`, `Nfp*.cs`, `ValidatePackedTransformComponent.cs`, `IconProvider.cs`, `StonePackAssemblyInfo.cs` | `src/Frahan.StonePack.GH/*.cs` |
| `Frahan.GH.Surface` (proposed) | `src/Frahan.GH/Surface/PackOnSurfaceComponent.cs`, `PackSurfacesComponent.cs`, `SurfaceChartComponent.cs` | `src/Frahan.StonePack.GH/SurfacePacking/*.cs` |
| `Frahan.GH.TwoD` | `src/Frahan.GH/TwoD/*.cs` | `src/Frahan.StonePack.GH/TwoD/*.cs` (8 files) |
| `Frahan.Rhino` | `src/Frahan.Rhino/StonePackPlugin.cs`, `FrahanStonePackAboutCommand.cs` | `src/Frahan.StonePack.Rhino/*.cs` |
| `Frahan.Tests` | `tests/Frahan.Tests/Program.cs`, `SurfacePackingTests.cs` | `tests/Frahan.StonePack.Tests/*.cs` |

## 2. Live → target name change checklist

For each live `.cs` file, the rename involves:

1. Update `namespace Frahan.StonePack.X` → `namespace Frahan.X` (or
   the target sub-namespace).
2. Update `using Frahan.StonePack.X;` directives in importing files.
3. Update each `.csproj` `<RootNamespace>` and `<AssemblyName>`.
4. Update every Yak `manifest.yml` and release-zip name.
5. Update every `.gha` `[Guid("…")]` attribute consistency check (the
   GUID itself stays stable to preserve `.gh` document compatibility;
   only the displayed namespace changes).
6. Update every spec, README, and CHECKPOINT reference.

This is multi-file and far above the ≤ 20-line rule. **Not done
tonight.** Tracked in
`docs/future_work/frahan_major_refactor_plan.md` as Refactor R1.

## 3. Proposed-only modules (`status: proposed only`)

Per `docs/index/frahan_class_method_component_audit.md` § 6:

| Target module | Live file? | Notes |
| --- | --- | --- |
| `Frahan.NativeBridge` | none | proposed loader for lazy native backends |
| `Frahan.Native.GeometryCore` | none on disk | starter zip in research bundle |
| `Frahan.Native.Geogram` | none | research-mention only |
| `Frahan.Native.CGAL` | none on disk | starter zip in research bundle |
| `Frahan.Native.Packing` | none on disk | starter zip |
| `Frahan.GeoPack` | none | spec **08** describes |
| `Frahan.GeoCut` | none on disk | spec **09** describes; codebase zip exists in research bundle |
| `Frahan.QuarryCutOpt` | none on disk | spec **10** describes; codebase zip exists |
| `Frahan.Mesh` | none yet (proposed split from `Frahan.Core` mesh helpers) | spec **11** describes |
| `Frahan.Geometry2D`, `Frahan.Geometry3D` | none yet | proposed split from `Frahan.Core` geometry primitives |
| `Frahan.Benchmarks` | none | spec **13** mentions |
| `Frahan.Docs` | none | this folder is the seed |

## 4. External assemblies (already loaded by the live tree)

| Reference | Used by |
| --- | --- |
| `RhinoCommon` (Rhino.Geometry) | `Frahan.StonePack.Core` (SurfacePacking subfolder), `Frahan.StonePack.GH`, `Frahan.StonePack.Rhino` |
| `Grasshopper` | `Frahan.StonePack.GH` |
| `System.*` (mscorlib, System.Linq, System.Threading, System.Threading.Tasks) | every assembly |
| `bff-command-line.exe` (out-of-process) | invoked via `BffCommandLineRunner` (in-process is **not** an option until BFF gets a managed binding) |
| SuiteSparse / OpenBLAS / LAPACK / GFortran DLLs | loaded transitively by `bff-command-line.exe` |

## 5. Tree at a glance (target)

```
src/
  Frahan.Core/
    Geometry/         GeometryPrimitives.cs
    Heightmap/        Heightmap.cs MeshPileHeightmap.cs OrientedMeshHeightmap.cs
    Container/        IrregularMeshContainer.cs
    Models/           PackingModels.cs MeshPackingModels.cs
    Solvers/          GreedyHeightmapPacker.cs GreedyMeshHeightmapPacker.cs
  Frahan.Surface/     BarycentricMapper2DTo3D.cs BffCommandLineRunner.cs
                      ChartDistortionAnalyzer.cs ChartScaleComputer.cs
                      FaceCornerUvTable.cs FrahanSurfaceChart.cs
                      MeshCleanup.cs MeshObjIO.cs
  Frahan.GH/
    Components/       Pack2D*.cs Pack3D*.cs Nfp*.cs Validate*.cs IconProvider.cs
                      StonePackAssemblyInfo.cs (-> FrahanGHAssemblyInfo.cs)
    Surface/          PackOnSurfaceComponent.cs PackSurfacesComponent.cs
                      SurfaceChartComponent.cs
    TwoD/             BottomLeftFillRhino.cs IrregularSheetFillRhino.cs
                      IrregularSheetFillV2.cs IrregularSheetFillV3.cs
                      IrregularSheetFillV506.cs NfpBottomLeftFillRhino.cs
                      NfpCache.cs NfpRhino.cs Packing2DModels.cs
  Frahan.Rhino/       FrahanRhinoPlugin.cs FrahanAboutCommand.cs
tests/
  Frahan.Tests/       Program.cs SurfacePackingTests.cs
docs/
  final_specs/        (this folder)
  index/              (audits)
  future_work/        (bug register, refactor plan, next-agent tasks)
```
