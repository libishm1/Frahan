# Frahan verification suite

`Frahan.Verification.Tests` is the permanent Layer-1 code-verification suite.
It turns each machine-checked Lean invariant into a property/oracle test that
runs against the **real shipping C#**, so a green `dotnet test` is continuous
evidence that the implementation still satisfies the algorithms the proofs
guarantee.

It consolidates the six ad-hoc harnesses from
`code_ws/outputs/2026-07-24/*_verification/` into one CI-runnable xUnit
project (P1 of `code_ws/proofs/CODE_VERIFICATION_AUDIT.md`).

## Link, don't copy

The suite never reimplements a kernel. Each `<Compile Include="../../src/...">`
in `Frahan.Verification.Tests.csproj` points at the **actual** source file
under `src/Frahan.StonePack.Core`. A fix landed in `src/` is re-verified here
on the next run; there is no second copy to drift. The only local code is:

- `Stubs/PlyMeshStub.cs`, `Stubs/FacetStub.cs` — tiny stand-ins for return
  types the linked kernels reference but the tests never exercise (they let the
  kernels compile standalone without dragging their whole dependency chain);
- the six `*Tests.cs` classes — generators, independent oracles, assertions.

Paths are **relative**, so the project restores and tests identically on a
developer machine and on the CI runner. (The original harnesses used absolute
`D:\` paths and were not portable.)

Each test checks its invariant with an **independent oracle** — analytic box
volume, Monte-Carlo-derived idempotence, a Clipper2 intersection-area overlap
check, brute-force adjacency, raw double-vector axial angles — never the
kernel's own validity flag.

## Lean ↔ test mapping

| Kernel (shipping source) | Test class · fact(s) | Lean theorem(s) | Origin report |
|---|---|---|---|
| `ConvexPolyhedron.ClipByHalfSpace` / `ClipBothSides` | `ClipTests` — `SingleClip_MeasureLe_Subset_Feasible`, `ClipChain_MeasureLe_NonIncreasing`, `Clip_Idempotent`, `Clip_Idempotence_BreakRate_IsZero`, `ClipBothSides_ConservesMaterial`, `UnclippedVolume_MatchesAnalyticBox` | `clip_measure_le`, `clip_subset`, `clipChain_measure_le`, `clip_idempotent` (`Common.lean`) | `code_ws/outputs/2026-07-24/clip_verification/` |
| `ConvexPolyhedron.To/FromInequalities` | `HrepTests` — `RoundTrip_ExactOnWorkingRange`, `RoundTrip_ExtremeScales_Characterization` | H-rep ↔ V-rep duality behind `clipChain_convex` / `clip_subset` (`Common.lean`) | `code_ws/outputs/2026-07-24/hrep_verification/` |
| `BlockBuildOrderer.Solve` | `KahnTests` — `EmittedOrder_IsValidTopologicalSort` | `kahn_linear_extension` (`Scheduling.lean`), `dag_has_source` (`Common.lean`) | `code_ws/outputs/2026-07-24/kahn_verification/` |
| `BlockGraphColorer.Color` | `ColoringTests` — `RandomGraphs_ProperColouring_AndNeverRefuses`, `CompleteGraphs_PaletteScalesPastEight` | `greedy_coloring_exists` (`Coloring.lean`) | `code_ws/outputs/2026-07-24/coloring_verification/` |
| `ContactNfpHoleNester.Pack` | `NesterTests` — `PlacedParts_ZeroOverlap_AndContained` | `nfp_separation` (`Packing.lean`) | `code_ws/outputs/2026-07-24/nester_verification/` |
| `SetClusterer.Cluster` | `ClusteringTests` — `HardInvariants_Partition_MinSize_MergeSeparation_PointShare` | `mergeKeep_separated` (`Clustering.lean`) | `code_ws/outputs/2026-07-24/clustering_verification/` |

Theorem sources live in `frahan_proofs/FrahanProofs/` and are CI-checked
(no `sorry`) by `.github/workflows/lean-proofs.yml`.

## How to run

```bash
dotnet test verification/Frahan.Verification.Tests -c Release
```

Requires the .NET 8 SDK (the project targets `net8.0`; the .NET 9 SDK builds it
too). NuGet restores `xunit`, `xunit.runner.visualstudio`,
`Microsoft.NET.Test.Sdk`, `CsCheck`, `Clipper2` (2.0.0) and `Rhino3dm` (8.x).
No Rhino install and no native `nfp_kernel.dll` are needed: the nester forces
its managed Clipper2 lane (`FRAHAN_NFP_NATIVE=0`), and Rhino3dm supplies
headless `Vector3d`/`Point3d`.

Runs are deterministic. The CsCheck property loops use a fixed seed and a
single thread; the `Random`-driven batteries seed with `20260724`.

## Failures are regressions

Every assertion here mirrors an invariant that is **proved** in Lean. The code
under test is fixed and passing. A red test therefore means the shipping C# has
diverged from a machine-checked invariant — a regression to investigate, not a
test to weaken. Two of these tests exist because the harness caught a real bug
that was then fixed:

- `Clip_Idempotent` / `Clip_Idempotence_BreakRate_IsZero` guard the
  coincident-plane no-op fix (pre-fix: ~2.8% of repeated cuts inflated volume,
  up to ~8x);
- `ColoringTests.CompleteGraphs_PaletteScalesPastEight` guards the Δ+1 palette
  fix (pre-fix: the palette was hard-capped at 8 and threw on any graph needing
  more; 23.9% of random graphs refused).

Two documented **latent** behaviours are characterized but intentionally not
gated, because their fixes were deliberately deferred (they are on the
watch-list, not regressions):

- H-rep round-trip at sub-mm / huge scales degrades from a hardcoded `1e-6`
  tolerance — `RoundTrip_ExtremeScales_Characterization` runs those scales
  without asserting exactness; the realistic working range (0.01 – 1e6) is
  gated exactly by `RoundTrip_ExactOnWorkingRange`;
- joint-set recovery is bandwidth-limited (sets closer than ~1.8·BandwidthDeg
  fuse in mean-shift) — `ClusteringTests` gates only the hard invariants
  (partition, min-size, merge-separation, point-share), not planted-K recovery.

## Adding a kernel

Follow the recipe in `code_ws/proofs/HARNESS_REGISTRY.md`: find the kernel and
its Lean theorem, add a `<Compile Include>` for the real source (+ a stub only
for an unused return type), generate random valid inputs, and check the
invariant with an **independent** oracle. Cite the Lean theorem and the origin
report in the test's doc comment.
