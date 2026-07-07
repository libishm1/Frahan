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
| H1 | **3D packing density numbers were UNMEASURED in C#.** The 33.8 / 35.7 / 38.7 / 53.3 / 49.3 % figures are Python pybullet+VHACD or Rhino-live-mesh, not the net48 Core engines. CoACD != VHACD. | [SYNTHESIS_3D](packing/SYNTHESIS_3D.md) Open risks | **partially addressed 2026-07-06** — the measurement is now Rhino-free and shipped: `GreedyMeshHeightmapPacker` + signed-tetra `MeshVolume` + `ObjMeshReader` measure honest density on the real ETH subset headless (16/16 placed, rho_honest 0.073 vs rho_bbox 0.195, **2.68x bbox over-report**; test `ETH C# honest 3D density headless bench`). Remaining: container-tuned per-packer comparison at verified-zero interpenetration (issue #13). |
| H2 | **The pipeline is only ~74% headless.** Core files import RhinoCommon: geology/discontinuity, fabrication (DXF, cut-plan, wire-saw), and much of masonry. A "headless server" would need Rhino.Compute for those, not a plain dotnet service. | grep `using Rhino` in Core | **in progress** — 77 of 300 Core files remain Rhino-bound (was 78). The 2D nester (`ContactNfpHoleNester`) is clean and measured, and the client-side [nest demo](../../web/README.md) proves the pattern (net9 WASM, no backend). `KinematicAnalysis` (geology feasibility) was made Rhino-free 2026-07-06 by inlining a cross product. Remaining paths tracked in issue #14. |
| H3 | **The production RBE solver is the weak path.** The closed-form QP is skipped whenever friction inequalities are present, so the with-friction (production) case always takes the Dykstra alternating-projection route, which the code comments admit has convergence trouble on the 6-DOF family (500-iter serial tail). A stability verdict served blind could be wrong on hard assemblies. | [slm_cards/masonry-equilibrium-cra](slm_cards/masonry-equilibrium-cra.md); [math/MASONRY](math/MASONRY_STABILITY.md) | **fail-loud since 2026-07-07** — the checker now runs an INDEPENDENT equality-residual audit after every solve (relative ||Aeq f − b||inf gate 1e-3, arithmetic independent of all solver lanes) and refuses a stable verdict on an unconverged iterate; a wrong-stable can no longer ship silently. Also verified: production routes through native OSQP / warm-started ADMM (non-Optimal already fail-loud), not the Dykstra path the audit card described. Remaining (lower urgency): solver back-end upgrade via the `IConvexQpSolver` seam. |

## Medium — address before scaling, safe for interactive use with the caveat

| ID | Risk | Where | Status |
|----|------|-------|--------|
| M1 | **Masonry numeric hygiene.** Four uncoordinated absolute epsilons; no assembly recentering (far-from-origin inputs degrade the Cholesky of `Aeq Aeq^T`); undeclared units (a mm model silently mixes mm geometry with SI gravity, weights off by 1e9). | [slm_cards/masonry-equilibrium-cra](slm_cards/masonry-equilibrium-cra.md) numeric stress | **partially addressed 2026-07-07** — the worst precision hole is fixed: `BlockCenterOfMass` now integrates the signed-tetra volume/centroid relative to the first vertex (translation-invariant recentering), so far-from-origin models no longer lose ~10 digits (test: COM at 1e6 offset matches near-origin to 1e-6). Remaining: Ruiz equilibration before the Cholesky, declared units at the boundary, scale-relative epsilons. |
| M2 | **Legacy RBE sign convention still live.** `RbeQpFormulation.Build` yields `f_n = -mg` against `lowerBounds = 0`, making real assemblies infeasible; only `BuildPhysicsCorrected` is correct. Two conventions coexist. | RbeQpFormulation.cs:174-212 | **defused 2026-07-07** — `RbeQpFormulation.Build` is now `[Obsolete]` with the failure mode spelled out in the message, so accidental use warns at compile time; production uses `BuildPhysicsCorrected` (verified: no production callers of the legacy path). The sign-pinning tests suppress deliberately. Full deletion remains as cleanup once those pins are reconciled. |
| M3 | **Physics-settle determinism is conditional.** `BulletSettleService` is reproducible only single-thread with a fixed step count; a service under concurrent load may not be. | [packing/EQUATIONS](packing/EQUATIONS.md) 2.6 | **open** — keep settle opt-in; mark determinism P1. |
| M4 | **Type-conversion seam.** Core `Vec3`/`MeshTriangle` vs the `MeshSnapshot` consumed by CoACD/Bullet; world transforms for verify+settle must survive the seam (`world = R*local` with R the transpose of the Bullet basis). | [SYNTHESIS_3D](packing/SYNTHESIS_3D.md) Open risks | **covered 2026-07-07** — headless convention-pinning tests: the Bullet row-vector basis packing (producer) composed with the NboSettle row-major application (consumer) round-trips a known rotation + rigid motion, and MeshSnapshot round-trips vertex/triangle arrays exactly. Either end flipping the transpose now fails the battery. |
| M5 | **Voxel seed is below benchmark alone.** ~32-34 % (Python) vs the 33.8 % skyline; the density win depends on the settle stage. Not a defect, an expectation. | [SYNTHESIS_3D](packing/SYNTHESIS_3D.md) | **open** — state the seed floor as parity/below in any claim. |
| M6 | **Masonry model is no-tension only (dry-stone).** The stability checker is strictly frictional Heyman/CRA: `FrictionConeBuilder` cone anchored at `f_n >= 0`, tension penalized to zero, NO cohesion / tensile-bond / surcharge term. It cannot represent MORTARED masonry (lime mortar, cementitious rubble-core, Roman concrete) — a whole structural class. A mortared structure analysed with it gets a **conservative lower bound** (safe: stable-without-mortar implies stable), which can read UNSTABLE for a real structure that stands on its mortar bond (e.g. a steep Maya corbel vault). | [FrictionConeBuilder](../../src/Frahan.StonePack.Core/Masonry/Equilibrium/FrictionConeBuilder.cs); [RbeQpFormulation](../../src/Frahan.StonePack.Core/Masonry/Solvers/RbeQpFormulation.cs) | **open, boundary documented 2026-07-07** — safe to use as a conservative bound if the caveat is stated. Fix = Mohr-Coulomb cohesion extension (`\|f_t\| <= c*A + mu*f_n`, tension cutoff `f_n >= -sigma_t*A`); Opus-tier, HITL + re-validate battery + re-prove conservativeness + calibrate c/sigma_t. Tracked in the private development map. |

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
