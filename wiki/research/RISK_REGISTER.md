# Risk register (research-preview honesty ledger)

The synthesis and mathematics pages document their own caveats in place. This
page consolidates the load-bearing ones, triaged by severity and **status**,
so the readiness of any given path is legible at a glance. It is the gate list
for turning a path into a service or a headline claim.

Severity = how much harm the risk does if a user relies on the path today.
Status: **open** (unaddressed), **measure-pending** (correct but the C# number
is not yet harness-measured), **mitigated** (addressed; how noted).

## High — gate a public service / a beats-benchmark claim

| ID | Risk | Where | Status |
|----|------|-------|--------|
| H1 | **3D packing density numbers are UNMEASURED in C#.** The 33.8 / 35.7 / 38.7 / 53.3 / 49.3 % figures are Python pybullet+VHACD or Rhino-live-mesh, not the net48 Core engines. CoACD != VHACD. | [SYNTHESIS_3D](packing/SYNTHESIS_3D.md) Open risks; [math/GEOLOGY](math/GEOLOGY_GPR_CUTTING.md) not applicable | **measure-pending** — needs the `--pack3d`/`--packbench` C# rows (partly the MeshAccuratePacker blueprint). The honest-density numerator now exists in Core (`MeshPackItem.MeshVolume`, 2026-07-06). |
| H2 | **The pipeline is only ~74% headless.** 78 of 300 Core files import RhinoCommon: all geology/discontinuity, fabrication (DXF, cut-plan, wire-saw), and much of masonry. A "headless server" would need Rhino.Compute for those, not a plain dotnet service. | grep `using Rhino` in Core | **open** — architectural. The 2D nester (`ContactNfpHoleNester`) and its Core packing path ARE clean and measured. |
| H3 | **The production RBE solver is the weak path.** The closed-form QP is skipped whenever friction inequalities are present, so the with-friction (production) case always takes the Dykstra alternating-projection route, which the code comments admit has convergence trouble on the 6-DOF family (500-iter serial tail). A stability verdict served blind could be wrong on hard assemblies. | [slm_cards/masonry-equilibrium-cra](slm_cards/masonry-equilibrium-cra.md); [math/MASONRY](math/MASONRY_STABILITY.md) | **open** — the formation layer (Aeq, Afr, COM) is sound and reusable; the solver back-end is the gap (evolve to active-set / SOCP via the `IConvexQpSolver` seam). |

## Medium — address before scaling, safe for interactive use with the caveat

| ID | Risk | Where | Status |
|----|------|-------|--------|
| M1 | **Masonry numeric hygiene.** Four uncoordinated absolute epsilons; no assembly recentering (far-from-origin inputs degrade the Cholesky of `Aeq Aeq^T`); undeclared units (a mm model silently mixes mm geometry with SI gravity, weights off by 1e9). | [slm_cards/masonry-equilibrium-cra](slm_cards/masonry-equilibrium-cra.md) numeric stress | **open** — fix: recenter + Ruiz-equilibrate before Cholesky; declare/convert units at the boundary. |
| M2 | **Legacy RBE sign convention still live.** `RbeQpFormulation.Build` yields `f_n = -mg` against `lowerBounds = 0`, making real assemblies infeasible; only `BuildPhysicsCorrected` is correct. Two conventions coexist. | RbeQpFormulation.cs:174-212 | **open** — fix: delete the legacy `Build` RHS path. |
| M3 | **Physics-settle determinism is conditional.** `BulletSettleService` is reproducible only single-thread with a fixed step count; a service under concurrent load may not be. | [packing/EQUATIONS](packing/EQUATIONS.md) 2.6 | **open** — keep settle opt-in; mark determinism P1. |
| M4 | **Type-conversion seam.** Core `Vec3`/`MeshTriangle` vs the `MeshSnapshot` consumed by CoACD/Bullet; world transforms for verify+settle must survive the seam (`world = R*local` with R the transpose of the Bullet basis). | [SYNTHESIS_3D](packing/SYNTHESIS_3D.md) Open risks | **open** — cover with a round-trip transform test. |
| M5 | **Voxel seed is below benchmark alone.** ~32-34 % (Python) vs the 33.8 % skyline; the density win depends on the settle stage. Not a defect, an expectation. | [SYNTHESIS_3D](packing/SYNTHESIS_3D.md) | **open** — state the seed floor as parity/below in any claim. |

## Mitigated (addressed, kept here for provenance)

| ID | Was | Status |
|----|-----|--------|
| X1 | Friction pyramid conservativeness: the raw `FrictionConeBuilder` K=4 exact-coefficient branch is *optimistic* (outer, under-constrains by up to sqrt(2)). | **mitigated** — the shipping `MasonryStabilityChecker` overrides to **K=8 inscribed** (`mu_eff = mu cos(pi/K)`); this session machine-proved the inscribed pyramid is a subset of the true Coulomb cone (Z3, [math/verification](math/verification/verify_instances.py)). The card's K=4 finding describes the builder in isolation, not the shipping path. |
| X2 | Honest density needed Rhino (`VolumeMassProperties`); Core `VolumeEstimate` was bbox only. | **mitigated** — `MeshPackItem.MeshVolume` (signed-tetra) + `MeshPackResult.FillRatioMeshVolume` added 2026-07-06, tested; Core rho no longer needs Rhino. |
| X3 | `Kriging.Predict` header comment claimed `(sill + nugget) - w^T w`. | **mitigated** — corrected in code to the actual latent variance `sill - w^T w`. |
| X4 | Unclear whether any GH path routed the coupled CRA certificate out of process. | **mitigated** — verified: the worker runs only the penalty RBE checker; the coupled certificate has a single in-process call site. |

## Reading this for deployment

The **2D nesting path** (`ContactNfpHoleNester`: exact NFP-BLF, hole-aware,
boundary mode) is the one path that clears every gate above: Rhino-free,
deterministic, exactly validated (0-overlap boolean gate), and benchmarked
against OpenNest 2.89 with a measured C# number ([RESULTS](../../docs/results/RESULTS.md)).
Everything 3D / masonry / geology carries at least one open High or Medium
item and is not yet service-ready.
