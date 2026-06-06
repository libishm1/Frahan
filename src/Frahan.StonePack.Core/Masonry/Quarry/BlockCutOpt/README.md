# Frahan.BlockCutOpt v2

> 3D brute-force cutting-pattern optimisation for dimension-stone quarries.
> Recreates Elkarmoty, Bondua and Bruno (2020) *BlockCutOpt* with 14
> improvements, plus a Shao 2022 in-block AMRR planner, plus a Grasshopper
> UI, plus a multi-format fracture-input pipeline, in C# / .NET Framework 4.8.

[![tests](https://img.shields.io/badge/tests-63%20%2F%2063%20pass-brightgreen)](#testing)
[![license](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)
[![framework](https://img.shields.io/badge/.NET-Framework%204.8-512BD4)]()

---

## Table of contents

1. [What this is](#1-what-this-is)
2. [Quick start (5 minutes)](#2-quick-start-5-minutes)
3. [Architecture in one diagram](#3-architecture-in-one-diagram)
4. [The 14 improvements over BlockCutOpt 2020](#4-the-14-improvements-over-blockcutopt-2020)
5. [Public API reference](#5-public-api-reference)
6. [Input formats](#6-input-formats)
7. [Output formats](#7-output-formats)
8. [Tolerances and Rhino units](#8-tolerances-and-rhino-units)
9. [Grasshopper components](#9-grasshopper-components)
10. [Extension points](#10-extension-points)
11. [Testing](#11-testing)
12. [Citations](#12-citations)
13. [License](#13-license)

---

## 1. What this is

`Frahan.Masonry.Quarry.BlockCutOpt` is a C# implementation of a 3D
brute-force search that finds the cutting-grid orientation and
displacement that maximises the number of non-fractured commercial
blocks extractable from a quarry bench.

**Input.** A fracture model (PLY mesh, CSV trace endpoints, or
`.lines` ASCII) describing the discontinuities in the rock mass, and
a tested-area axis-aligned bounding box.

**Output.** The optimum `(psi, theta, phi, dx, dy)` cutting pattern,
the count of non-intersected blocks, the recovery percentage, and
optionally a ParaView VTU rendering of the result.

**Why a port.** The original BlockCutOpt (Elkarmoty et al., 2020,
*Resources Policy* 68, 101761) is private C++. Frahan v2 reimplements
its algorithm with 14 architectural improvements documented in
`/wiki/papers/equations_and_diagrams/08_synthesis_and_optimum_algorithm.md`,
under Apache-2.0, as a Grasshopper-native mesh-based pipeline. The
target use case is Tamil Nadu granite production planning.

---

## 2. Quick start (5 minutes)

### 2.1 From C# (any consumer)

```csharp
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Quarry.BlockCutOpt;

// 1. Load a fracture file (PLY, CSV, or .lines auto-detected by extension)
var fractures = FractureInputReader.Load(
    "samples/tn_granite_realistic.csv",
    zMin: 0.0,
    zMax: 6.0);

// 2. Define the bench (40 x 30 x 6 m tested area)
var bench = new BoundingBox3(0, 0, 0, 40.0, 30.0, 6.0);

// 3. Solve with paper-default tolerances
var result = BlockCutOptSolver.Solve(
    bench,
    fractures,
    BlockCutOptOptions.LimestoneStratumA());

Console.WriteLine(result);
// -> BlockCutOptResult(N_ni=83, R=44.21%, psi=84.0, theta=0.0, phi=0.0 deg,
//                     dx=0.50, dy=0.00, evals=2440, elapsed=1820 ms)
```

### 2.2 From Grasshopper

After installing the GH plugin
(see [`samples/INSTALL_GH_PLUGIN.md`](../../../../samples/INSTALL_GH_PLUGIN.md)),
in a new Grasshopper canvas:

1. **BlockCutOpt Load Fractures** -- file path
   `samples/tn_granite_realistic.csv`, zMin `0.0`, zMax `6.0`.
2. **BlockCutOpt Solve** -- connect a Box (40 x 30 x 6 m) + the fracture
   mesh from step 1. Defaults to limestone Stratum a parameters.
3. The seven outputs (count, recovery %, psi, dx, dy, evaluations,
   elapsed) populate immediately.

### 2.3 Full omni-pipeline (sub-divided 4-axis Pareto)

```csharp
var omniOpts = new OmniSolverOptions
{
    Search = BlockCutOptOptions.LimestoneStratumA(),
    SubdivMode = SubdivisionMode.Uniform,
    Mx = 3, My = 2,                           // 3 x 2 sub-zones
};
var omni = BlockCutOptOmniSolver.Solve(bench, fractures, omniOpts);

foreach (var zr in omni.PerZone)
{
    var bestR = zr.Front.BestRecovery();      // recovery-optimal
    var bestRev = zr.Front.BestRevenue();     // revenue-optimal
    var bestCost = zr.Front.BestBcsdbBv();    // cost-optimal (Jalalian 2023)
    Console.WriteLine($"Zone {zr.Zone.Id}: " +
        $"R={bestR.RecoveryCount}, Pi={bestRev.Revenue:0.0}, " +
        $"BCSdbBV={bestCost.BcsdbBv:0.000}");
}
```

---

## 3. Architecture in one diagram

```
                          ┌─────────────────────────┐
                          │  FractureInputReader    │
                          │  (.ply / .csv / .lines) │
                          └────────────┬────────────┘
                                       │ PlyMesh
                                       ▼
   ┌────────────────────────────────────────────────────────┐
   │  JointSetDfnGenerator  ─────▶  JointSetDfnPlyEmitter   │
   │  (synthetic DFN path)          (FracturePlane → PLY)    │
   └────────────────────────────┬────────────────────────────┘
                                │ PlyMesh
                                ▼
   ┌──────────────────────────────────────────────────────┐
   │  TriangleAabbBvh   (Phase 2 inner-loop pruning)      │
   └────────────────┬─────────────────────────────────────┘
                    │ BVH
                    ▼
   ┌────────────────────────────────────────────────────────┐
   │  CuttingGrid + ObbTriangleIntersection                  │
   │  (full 3D rotation: psi + theta + phi, BlockCutOpt I1)  │
   └────────────────┬───────────────────────────────────────┘
                    │ List<OrientedBlock>
        ┌───────────┼───────────┬──────────────┬──────────────┐
        ▼           ▼           ▼              ▼              ▼
  BlockCutOptSolver  ...Pareto  ...CoarseToFine  Subdivision  FisherRobust
  (scalar)           (4-axis)   (12→3→0.5 deg)   (mx, my)     (Monte Carlo)
        │                            │
        └────────────┬───────────────┘
                     ▼
         ┌──────────────────────────────────┐
         │  BlockCutOptOmniSolver           │
         │  (sub-division + Pareto glue)    │
         └─────────────┬────────────────────┘
                       ▼
          ┌────────────────────────────────────┐
          │  AmrrPlanner (Shao 2022 + Minetto) │
          │  in-block plane-sequence cut       │
          └────────────────┬───────────────────┘
                           ▼
                    ┌──────────────┐
                    │  VtuWriter   │
                    │  (ParaView)  │
                    └──────────────┘
```

29 source files in this directory. Categories:

| Category | Files |
|---|---|
| **Geometry + primitives** | `OrientedBlock`, `ConvexPolyhedron`, `CompositeBlock`, `ObbTriangleIntersection`, `EdgeTriangleObbIntersection`, `TriangleAabbBvh`, `SharedEdgeSlicer` |
| **Cutting grid + tolerances** | `CuttingGrid`, `SubdivisionPartition`, `DensityWatershedPartition`, `BlockCutOptTolerances` |
| **Fracture input** | `FractureInputReader`, `TraceVerticalExtruder`, `JointSetDfnPlyEmitter`, `PhotogrammetryContract`, `PythonSubprocessFractureDetector`, `SyntheticTnGraniteGenerator` |
| **Solvers** | `BlockCutOptOptions`, `BlockCutOptResult`, `BlockCutOptSolver`, `BlockCutOptCoarseToFine`, `BlockCutOptParetoSolver`, `BlockCutOptOmniSolver`, `FisherRobustSampler`, `AmrrPlanner` |
| **Output** | `ParetoPoint`, `ParetoFront`, `BlockValueModel`, `DlbfMixedSizePacker`, `VtuWriter`, `BlockCutOptDemo` |

---

## 4. The 14 improvements over BlockCutOpt 2020

| # | Improvement | Citation | Module |
|---|---|---|---|
| **I1** | Full 3D rotation (psi + theta + phi), back-compat with psi-only Phase 1 | -- | `OrientedBlock`, `CuttingGrid.GenerateTilted` |
| **I2** | AABB-tree (BVH) pruning of fracture triangles | -- | `TriangleAabbBvh` |
| **I3** | Coarse-to-fine angular search (12 → 3 → 0.5 deg refinement) | -- | `BlockCutOptCoarseToFine` |
| **I4** | Edge-triangle intersection as an alternative to 13-axis SAT | Möller-Trumbore | `EdgeTriangleObbIntersection` |
| **I5** | Density-watershed adaptive sub-division (replaces forced uniform mx, my) | -- | `DensityWatershedPartition` |
| **I6** | Pareto 4-axis multi-objective (recovery, revenue, kerf-time, BCSdbBV) | -- | `BlockCutOptParetoSolver` + `ParetoFront` |
| **I7** | DLBF semi-discrete 3D mixed-size packing | Chehrazad et al. 2025 | `DlbfMixedSizePacker` |
| **I8** | Fisher Monte Carlo robust optimum over fracture-position uncertainty | -- | `FisherRobustSampler` |
| **I9** | Coupling to Shao 2022 in-block AMRR plane sequence | Shao, Liu, Gao 2022 | `AmrrPlanner` + `ConvexPolyhedron` |
| **I10** | Open-source, Grasshopper-native, mesh-based delivery | -- | `BlockCutOptComponents.cs` (in `Frahan.StonePack.GH`) |
| **I11** | BCSdbBV cost objective (cutting-surface area / block value) | Jalalian et al. 2023 *Sci. Reports* | `ParetoPoint.BcsdbBv` |
| **I12** | Minetto 2017 shared-edge mesh slicing inside the Shao loop | Minetto et al. 2017 *CAD* 92 | `SharedEdgeSlicer` |
| I13 | Multi-model joint generator (Baecher + Veneziano + Lévy-Lee + Priest) | Tian 2025 *Comput. Geotech.* | **proposed**, not yet built |
| **I14** | Composite block (multi-convex-per-block) + algebraic ineq. store + ContainsPoint / SignedGap / ClipBothSides | Zhang Zheng Yang Wang 2024 *Adv. Eng. Softw.* | `CompositeBlock`, `ConvexPolyhedron.FromInequalities` |

12 of 14 shipped. I13 (Tian 2025) is the next natural extension; I14
shipped at commit `c36fcf3` as Tier-1 build-out parity with Zhang
2024 cut-code (`github.com/NingZhangQh/cut-code`).

---

## 5. Public API reference

### 5.1 Solver entry points

#### `BlockCutOptSolver.Solve(testedArea, fractures, options)`

Single-zone brute-force search.

| Param | Type | Notes |
|---|---|---|
| `testedArea` | `BoundingBox3` | Bench AABB in metres. |
| `fractures` | `PlyMesh` | Triangulated fracture mesh. |
| `options` | `BlockCutOptOptions` | Search params + block size. |

**Returns** `BlockCutOptResult` with `NonIntersectedCount`,
`RecoveryPercent`, `BestPsiRad/Deg`, `BestThetaRad/Deg`,
`BestPhiRad/Deg`, `BestDx`, `BestDy`, `TotalEvaluations`, `Elapsed`.

#### `BlockCutOptSolver.SolveSubdivided(testedArea, fractures, options, mx, my)`

Uniform `(mx, my)` sub-division; each sub-zone solved independently.
Returns `IReadOnlyList<(SubZone Zone, BlockCutOptResult Result)>`.

#### `BlockCutOptCoarseToFine.Solve(testedArea, fractures, options, coarseStepRad, mediumStepRad, fineStepRad, topK)`

12° coarse sweep -> top-K seeds -> 3° medium refinement -> 0.5° fine
refinement. Returns a `BlockCutOptResult`.

#### `BlockCutOptParetoSolver.Solve(testedArea, fractures, options, valueModel)`

4-axis Pareto front. Returns `(ParetoFront Front, long Evaluations,
TimeSpan Elapsed)`. Query via `front.BestRecovery()`, `BestRevenue()`,
`BestBcsdbBv()`, `BestKerfTime()`, or iterate `front.Points`.

#### `BlockCutOptOmniSolver.Solve(testedArea, fractures, options, watershedPlanes)`

Sub-division + per-zone Pareto in one call. Returns `OmniSolveResult`
with `PerZone`, `AggregateRecoveryCount`, `AggregateRevenue`.

#### `FisherRobustSampler.Solve(testedArea, jointSets, options, monteCarloSamples, baseSeed)`

Monte Carlo robust optimum. Returns `FisherRobustResult` with `p10`,
`p50`, `p90`, `mean`, `stddev` of recovery, plus the median ψ.

#### `AmrrPlanner.PlanBoundingSphere(blank, centre, radius, options)`

Shao 2022 plane-sequence cut. Returns `AmrrPlanResult` with per-step
plane + removed volume + cutting time.

### 5.2 Input loaders

#### `FractureInputReader.Load(path, zMin, zMax)`

Auto-detects `.ply` / `.csv` / `.lines` / `.txt` by extension; returns
a `PlyMesh`. PLY files ignore `zMin/zMax`; the others vertical-extrude
between them.

#### `JointSetDfnGenerator.Generate(jointSets, box, seed)`

Synthetic DFN via Priest 1993 spacing + Fisher scatter. Returns
`IReadOnlyList<FracturePlane>`.

#### `JointSetDfnPlyEmitter.Emit(planes, bench)`

Clip each infinite plane to the bench AABB and emit a triangulated
`PlyMesh`.

#### `SyntheticTnGraniteGenerator.WriteSampleSet(csvPath, plyPath, bench, seed, jointSets)`

Tamil Nadu granite synthetic dataset. Emits both CSV (mid-Z traces)
and ASCII PLY (3D polygons). Deterministic given the seed.

### 5.3 Geometry types

#### `OrientedBlock`

Immutable struct. Full 3D rotation via `(UX, UY, UZ), (VX, VY, VZ),
(WX, WY, WZ)`. Phase 1 constructor sets `W = (0,0,1)`.

#### `ConvexPolyhedron`

Built via `FromOrientedBlock(in obb)` or `FromInequalities(rows)`
(Zhang cut-code parity). Methods:
- `Volume()` -- signed-tet sum, identical formula to Zhang's
  `Con3_getVolume`.
- `ClipByHalfSpace(p, n)` -- Sutherland-Hodgman, keeps the side
  opposite the normal.
- `ClipBothSides(p, n)` -- returns `(Kept, Discarded)`.
- `ContainsPoint(x, y, z, tol)` -- vectorised point-in-convex.
- `SignedGap(x, y, z)` -- > 0 outside, < 0 inside.
- `ToInequalities()` / `ToPlyMesh()` -- export.

#### `CompositeBlock`

Multi-convex-per-block container with `Id`, `Pieces`, `TotalVolume`,
union `Aabb`, `PieceContaining(x, y, z, tol)`.

### 5.4 Output / visualisation

#### `VtuWriter.Write(path, nonIntersected, intersected)`

ParaView VTU with `cell_status` (1 = non-intersected orange, 0 =
intersected dark red).

#### `VtuWriter.WriteAmrrSequence(path, plan, quadSize)`

VTK_QUAD per AMRR plane with `step_index`, `removed_volume_m3`,
`cutting_time_min` cell data.

#### `VtuWriter.WriteFromGridAndBvh(path, grid, bvh)`

Convenience: split a flat grid via the BVH and write the VTU. Returns
`(NonIntersectedCount, IntersectedCount)`.

### 5.5 End-to-end demo

#### `BlockCutOptDemo.RunCsvDrivenDemo(csvPath, bench, search, mx, my, vtuOutputDir)`

Three-line pipeline: CSV → PLY → omni solve → per-zone VTUs.

#### `BlockCutOptDemo.CoupleToAmrrAtBestBlock(quarryResult, search, targetSphereFraction, amrrOpts, amrrVtuPath)`

Phase 9 handoff: pick the first non-intersected block from the quarry
result and run Shao AMRR against an inscribed sphere.

---

## 6. Input formats

See [`samples/INPUT_FORMATS.md`](../../../../samples/INPUT_FORMATS.md)
for the full spec. Summary:

| Extension | Contents | Best for |
|---|---|---|
| `.ply` | Full 3D triangulated mesh (vertex + face, ASCII or binary LE) | GPR deterministic surfaces, TLS facets, laser-scanner output |
| `.csv` | `x1, y1, x2, y2` per row (header optional) | Photogrammetry digitisation, GeoFractNet CNN output |
| `.lines` / `.txt` | Whitespace- or comma-separated endpoints | Hand-edited regression fixtures |

For 2D-trace formats, the consumer passes `zMin / zMax`; the reader
vertical-extrudes each trace into a kerf-aware fracture rectangle.

All world units are **metres**. For non-metre Rhino documents, scale
on the consumer side via `BlockCutOptTolerances.ToRhinoUnit`.

---

## 7. Output formats

### 7.1 `BlockCutOptResult` (scalar)

```
BlockCutOptResult(N_ni=23, R=7.86%, psi=81.0, theta=0.0, phi=0.0 deg,
                  dx=-0.51, dy=-1.01, evals=2440, elapsed=820 ms)
```

### 7.2 `ParetoFront` (multi-objective)

Each `ParetoPoint` carries `RecoveryCount`, `Revenue`, `KerfTime`,
`BcsdbBv`, plus `(PsiRad, Dx, Dy)`. Query via:

- `front.BestRecovery()` — maximises non-intersected count.
- `front.BestRevenue()` — maximises sum of per-block RMV.
- `front.BestBcsdbBv()` — minimises cutting-surface area / block value.
- `front.BestKerfTime()` — minimises total saw cutting time.

### 7.3 `AmrrPlanResult` (Shao 2022 in-block)

Per-step `AmrrCutStep` with plane `(p, n)`, cut area, removed volume,
cutting time. Aggregate `MaterialRemovalPercent` + `Amrr` (m³/min).

### 7.4 ParaView VTU

Hexahedral cell grid with `cell_status` (1 = non-intersected, 0 =
intersected). Drop into ParaView, colour by `cell_status`, screenshot.

---

## 8. Tolerances and Rhino units

All defaults from BlockCutOpt 2020 + Elkarmoty thesis are catalogued
in [`BlockCutOptTolerances`](BlockCutOptTolerances.cs) and
[`/wiki/papers/equations_and_diagrams/14_tolerances_and_units.md`](../../../../wiki/papers/equations_and_diagrams/14_tolerances_and_units.md).

Key constants:

| Constant | Value | Origin |
|---|---|---|
| `KerfDefaultMetres` | 0.05 | BlockCutOpt 2020 Appendix A + B |
| `PsiStepDefaultRad` | 3° in rad | both case studies |
| `DxyStepLimestoneDefault` | 0.5 | limestone case |
| `SawBladeRadiusMmDefault` | 100 mm | Shao 2022 |
| `FeedSpeedMmPerMinDefault` | 50 mm/min | Shao 2022 |
| `GeometricEps` | 1e-12 | SAT zero-axis test |
| `VertexDedupeTol` | 1e-9 m = 1 nm | PLY emission |

Rhino unit translation:

```csharp
// Rhino model in millimetres
double kerfRhino = BlockCutOptTolerances.ToRhinoUnit(
    metres: BlockCutOptTolerances.KerfDefaultMetres,
    rhinoMetresPerUnit: 1.0e-3);
// kerfRhino = 50.0 (mm units)
```

---

## 9. Grasshopper components

Four components under **Grasshopper → Frahan → Quarry**:

| Component | Inputs | Outputs |
|---|---|---|
| **BlockCutOpt Load Fractures** | path, zMin, zMax | Mesh, triangle count |
| **BlockCutOpt Solve** | tested-area Box, fracture Mesh, block X/Y/Z, kerf, psi step, dx/dy max/step | count, recovery %, ψ, dx, dy, evaluations, elapsed |
| **BlockCutOpt AMRR Plan** | blank Box, target centre, target radius, sawblade radius mm, feed mm/min, max cuts | cut Planes list, per-step volume + time, MRP %, AMRR |
| **BlockCutOpt Omni Solve** | tested-area Box, fracture Mesh, mx, my, block size, kerf, psi step | per-zone id + recovery + revenue + BCSdbBV + ψ, aggregate recovery |

Install instructions:
[`samples/INSTALL_GH_PLUGIN.md`](../../../../samples/INSTALL_GH_PLUGIN.md).

---

## 10. Extension points

### Custom fracture detector (CNN, manual, GPR processing)

Implement `IFractureDetector`:

```csharp
public sealed class MyDetector : IFractureDetector
{
    public string BackendName => "my-detector";
    public IReadOnlyList<FractureTrace> Detect(string imagePath, ImageToWorldMap map)
    {
        // return list of FractureTrace in world metres
    }
}

// Use it
var ply = PhotogrammetryPipeline.DetectAndExtrude(
    new MyDetector(), "image.png",
    new ImageToWorldMap(originX: 0, originY: 0, gsdMetresPerPx: 0.02),
    zMin: 0, zMax: 6);
```

A shipped reference backend is `PythonSubprocessFractureDetector` for
hooking up the GeoFractNet CNN
(`https://github.com/YaqoobAnsari/GeoFractNet`) via a one-line Python
wrapper script.

### Custom block value model

```csharp
var myValueModel = new BlockValueModel(
    rmvPerBlock: 2.5,
    bvPerBlock: 5.0,
    kerfTimeMinPerBlock: 30.0);
var (front, _, _) = BlockCutOptParetoSolver.Solve(
    bench, fractures, options, myValueModel);
```

### Custom sub-division

Implement a function returning `IReadOnlyList<SubZone>` and pass into
the omni solver's `SubdivisionMode.Manual` path (extend
`BlockCutOptOmniSolver` if not already wired).

### Algebraic input (Zhang cut-code parity)

```csharp
var rows = new (double B, double Nx, double Ny, double Nz)[]
{
    (0.5, 1, 0, 0),       // x <= 0.5
    (0.5, -1, 0, 0),      // -x <= 0.5  i.e. x >= -0.5
    // ... (a unit cube needs 6 such inequalities)
};
var cph = ConvexPolyhedron.FromInequalities(rows);
```

---

## 11. Testing

63 tests in `tests/Frahan.StonePack.Tests/BlockCutOptSolverTests.cs`,
registered in `Program.cs`. Run:

```pwsh
cd Template-General/outputs/2026-05-01/frahan_stonepack
$env:FRAHAN_SKIP_NATIVE = "1"
dotnet build tests/Frahan.StonePack.Tests/Frahan.StonePack.Tests.csproj -c Debug
dotnet run --project tests/Frahan.StonePack.Tests/Frahan.StonePack.Tests.csproj -c Debug --no-build
```

Filter to just BlockCutOpt:

```pwsh
dotnet run ... | Select-String "BlockCutOpt"
```

Test counts by phase:

| Phase | # |
|---|---|
| 1.A-1.D core | 9 |
| 2 BVH | 4 |
| 3 sub-division | 2 |
| 4 coarse-to-fine | 2 |
| 6 Pareto + BCSdbBV | 3 |
| 7 density-watershed | 1 |
| 8 Fisher-robust | 2 |
| 9 Shao AMRR | 6 |
| 11.5 Photogrammetry | 4 |
| Tolerances | 2 |
| VtuWriter | 3 |
| OmniSolver | 2 |
| I1 full 3D rotation | 5 |
| I4 edge-triangle | 2 |
| I7 DLBF | 2 |
| I12 Minetto slicing | 1 |
| I14 Zhang parity | 6 |
| FractureInputReader | 3 |
| Demo end-to-end | 2 |
| Phase 1.D regression | 1 |
| Synthetic generator | 3 |

= 63 tests. **63 / 63 PASS, 0 FAIL** as of commit `c36fcf3`.

---

## 12. Citations

If you use Frahan.BlockCutOpt v2 in research, please cite:

1. **Elkarmoty, M., Bondua, S., Bruno, R.** (2020). *A 3D brute-force
   algorithm for the optimum cutting pattern of dimension stone
   quarries.* *Resources Policy* 68, 101761.
   DOI: `10.1016/j.resourpol.2020.101761`. (The base algorithm.)

2. **Shao, H., Liu, Q., Gao, Z.** (2022). *Material Removal
   Optimization Strategy of 3D Block Cutting Based on Geometric
   Computation Method.* *Processes* 10(4), 695.
   DOI: `10.3390/pr10040695`. (The in-block AMRR planner, I9.)

3. **Jalalian, M., Bagherpour, R., Khoshouei, M.** (2023).
   *Environmentally sustainable mining in quarries to reduce waste
   production and loss of resources using the developed optimization
   algorithm.* *Scientific Reports* 13.
   DOI: `10.1038/s41598-023-49633-w`. (BCSdbBV objective, I11.)

4. **Zhang, N., Zheng, H., Yang, M., Wang, N.** (2024).
   *An open-source MATLAB toolbox for 3D block cutting and 3D mesh
   cutting in geotechnical engineering.* *Advances in Engineering
   Software* 197, 103762.
   DOI: `10.1016/j.advengsoft.2024.103762`. (Architectural parity
   for I14; cut-code at https://github.com/NingZhangQh/cut-code.)

5. **Frahan StonePack project** (2026). Frahan.BlockCutOpt v2 source.
   `<repo URL>`, commit `c36fcf3`.

Synthesis of the full 24-paper academic chain is at
[`/wiki/papers/equations_and_diagrams/08_synthesis_and_optimum_algorithm.md`](../../../../wiki/papers/equations_and_diagrams/08_synthesis_and_optimum_algorithm.md).

Forward + backward citation map at
[`/wiki/papers/equations_and_diagrams/17_zhang2024_citation_map.md`](../../../../wiki/papers/equations_and_diagrams/17_zhang2024_citation_map.md).

---

## 13. License

Apache-2.0. The Frahan.StonePack codebase as a whole is governed by
the project's top-level LICENSE; this module follows.

Third-party libraries used at runtime:

- **Clipper2** (Angus Johnson, BSL-1.0) — 2D polygon boolean (used
  elsewhere in `Frahan.StonePack.Core`, not directly by BlockCutOpt).
- **MathNet.Numerics** (MIT) — linear algebra helpers.

Datasets bundled in `/samples/`:

- `tn_granite_traces.csv` and `tn_granite_fractures.lines` are toy
  smoke-test fixtures, generated by Frahan, no third-party data.
- `tn_granite_realistic.csv` and `tn_granite_realistic.ply` are
  Frahan-generated synthetic DFNs from `SyntheticTnGraniteGenerator`,
  deterministic at seed 1234. No third-party data.

External data sources documented but **NOT** bundled:

- **GeoCrack dataset** (Ansari 2024, CC BY 4.0, Harvard Dataverse
  DOI `10.7910/DVN/E4OXHQ`) — 11 outcrop sites, 12,158 patches.
- **Bondua / Tinti 2024 ornamental-stone GPR dataset**
  (CC BY-NC-ND, Mendeley DOI `10.17632/w26n6nftxs.3`) — 4 quarries,
  marble + travertine.
- **Zhang 2024 cut-code** (MIT, `github.com/NingZhangQh/cut-code`,
  commit `19fca42`) — Layer-3 cross-validation target.

---

## See also

- [`/wiki/papers/equations_and_diagrams/INDEX.md`](../../../../wiki/papers/equations_and_diagrams/INDEX.md)
  — full documentation index (24 papers + 23 synthesis docs).
- [`/wiki/papers/equations_and_diagrams/18_progress_log.md`](../../../../wiki/papers/equations_and_diagrams/18_progress_log.md)
  — commit timeline + 14-improvement status + 28-file inventory.
- [`/wiki/papers/equations_and_diagrams/19_gpr_acquisition_sources.md`](../../../../wiki/papers/equations_and_diagrams/19_gpr_acquisition_sources.md)
  — how to obtain GPR data.
- [`/wiki/papers/equations_and_diagrams/20_gpr_documents_and_granite_path.md`](../../../../wiki/papers/equations_and_diagrams/20_gpr_documents_and_granite_path.md)
  — four paths to a viable granite dataset.
- [`/wiki/papers/equations_and_diagrams/23_zhang_vs_frahan_gap_analysis.md`](../../../../wiki/papers/equations_and_diagrams/23_zhang_vs_frahan_gap_analysis.md)
  — feature-by-feature mapping vs cut-code.
- [`/samples/`](../../../../samples/) — sample fracture data + GH install + format reference.

---

*Last updated 2026-05-12 at commit c36fcf3. Test status:
63 / 63 BlockCutOpt PASS, 0 FAIL.*
