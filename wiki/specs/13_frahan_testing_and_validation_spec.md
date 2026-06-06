# 13 - Frahan Testing and Validation Spec

**Spec version:** 0.1
**Sources:** live `tests/Frahan.StonePack.Tests/{Program.cs, SurfacePackingTests.cs}`,
`Template-General/AGENTS.md` ("Task completion verification"),
runbook §§ 19, 22, 25.

## 1. Test pyramid

```
           ┌────────────┐
           │  V3 - human │  Rhino-in-the-loop, fabrication review
           ├────────────┤
           │  V2 - runtime │  unit + integration tests, sample-fixture runs
           ├────────────┤
           │  V1 - static │  build, lint, type, compile checks
           └────────────┘
```

Per `Frahan.GH` component:

- **V1**: `dotnet build` of `Frahan.StonePack.sln` succeeds.
- **V2**: at least one unit test exercises the underlying solver.
- **V3**: open at least one of the
  `outputs/.../frahan_stonepack/share/Frahan.StonePack_Rhino8_*.zip`
  bundles in Rhino 8 SR8+ and confirm the component appears in the
  ribbon.

## 2. Live test inventory

| File | Purpose | Status |
| --- | --- | --- |
| `tests/Frahan.StonePack.Tests/Program.cs` | console-runner + `MeshPackPlacementExtensions` helpers | implemented |
| `tests/Frahan.StonePack.Tests/SurfacePackingTests.cs` | `SurfacePackingTests` covering the FaceCornerUvTable + BarycentricMapper round-trips | implemented |

The test project does not declare a namespace today (parser reports
`(global)`). Adding a `Frahan.Tests` namespace is a small fix
deferred to the next agent (see naming drift § 7 item A4).

## 3. Test framework choice

The live `Frahan.StonePack.Tests.csproj` (per archive snapshot) uses a
**custom console runner**, not xUnit / NUnit / MSTest. This is a
deliberate choice for net48 + net6.0 dual-target without taking a
dependency on a heavyweight test framework. Spec recommendation:
**keep the console runner for v1**; revisit when the test count
exceeds ~50.

## 4. Required test categories

| Category | Examples | Where |
| --- | --- | --- |
| Geometry primitives | `Vec3`, `Size3`, `Box3` invariants; `Size3` ctor argument validation | unit |
| Heightmap | `Heightmap.PlaceFootprint` monotonicity | unit |
| Packing | `GreedyHeightmapPacker.Pack` on a 5-stone fixture | unit |
| Mesh packing | `GreedyMeshHeightmapPacker.Pack` on a 5-mesh fixture | unit |
| Surface chart round-trip | `FaceCornerUvTable` → flat mesh → `BarycentricMapper2DTo3D` round-trip | unit |
| BFF integration | `BffCommandLineRunner` with a fixture mesh; assert `ExitCode == 0` and a non-empty output OBJ | integration; Windows-only |
| 2D packing | `IrregularSheetFillV506.Solve` on a 10-part fixture | unit |
| GH wrappers | open a fixture `.gh` and confirm component output | V3 (Rhino-in-loop) |
| Naming drift fix verification | after refactor R1, confirm `using Frahan.Core;` works | regression |
| Bug B1 fix | after `preserveZone` ternary fix, confirm widening behaviour | regression |

## 5. Fixture management

- Test fixtures live under `tests/Frahan.Tests/Fixtures/`.
- Fixture files larger than 100 KB go through Git LFS (see
  `.gitattributes`).
- Each fixture has a `README.md` describing its construction.

## 6. Continuous-integration recommendation

- v1: developer-machine `dotnet build` + `dotnet test` only (no CI
  yet).
- v2: GitHub Actions workflow running `dotnet build` and the unit
  test pass on Windows-latest.
- BFF integration tests are tagged `[Category("Windows")]` and skipped
  on non-Windows runners.

## 7. Verification levels per common change

| Change | Required levels | Evidence |
| --- | --- | --- |
| README edit | V0 | diff |
| Markdown spec edit | V0 | diff |
| Snippet bug fix in markdown | V0 + (V1 if the snippet is meant to compile) | diff (+ build) |
| Source code typo / single-line fix (≤ 20 lines, single file) | V1 | `dotnet build` |
| New solver class | V1 + V2 | build + unit test |
| New GH component | V1 + V2 + V3 | build + unit test + open in Rhino |
| Release zip | V3 | human approval, install in fresh Rhino, smoke-test all components |
| Native backend swap | V1 + V2 | build + integration test on Windows |
| Robot / fabrication output | V3 | human sign-off |

## 8. Performance benchmarks (`Frahan.Benchmarks` proposed)

A separate `Frahan.Benchmarks` project (proposed; not yet on disk)
hosts BenchmarkDotNet runs for:

- 2D irregular sheet packing - 100 parts.
- 3D mesh-heightmap packing - 50 stones.
- BFF round-trip - fixture mesh of 10 k faces.

Benchmarks are **not** part of the V2 acceptance set; they ride on
the v2 GitHub Actions workflow and post-results to a manual
dashboard.

## 9. Test coverage targets

- v1: cover every `*Result` DTO constructor and every public method
  on `Frahan.Core.GreedyHeightmapPacker`,
  `Frahan.Core.GreedyMeshHeightmapPacker`,
  `Frahan.Surface.FaceCornerUvTable`,
  `Frahan.Surface.BarycentricMapper2DTo3D`.
- v2: ≥ 60 % line coverage on `Frahan.Core` and `Frahan.Surface`.
- v3: ≥ 80 % line coverage on every released assembly.

## 10. Known gaps

- No coverage of `IrregularSheetFillV2`, `…V3`, `…V506` today.
- No coverage of any `Pack3D*Component` (only the underlying
  packers).
- No coverage of `ValidatePackedTransformComponent`.

These gaps are the primary content of `14_frahan_minimal_pre_factory_test_plan.md`.
