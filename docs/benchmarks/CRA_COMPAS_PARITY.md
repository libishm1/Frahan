# CRA vs compas_cra — parity benchmark (Block 3 item 1, 2026-06-11)

Per BENCHMARK_CRITERIA: parity first, then speed, against the reference implementation
(BlockResearchGroup/compas_cra — Kao et al. 2022, CAD 146:103216; IPOPT via Python/conda).
Fixtures are EXACT ports of their parametric doc examples (geometry re-derived from their sources,
fetched 2026-06-11). Tests: `tests/Frahan.StonePack.Tests/CraCompasParityTests.cs`.

## Results

| compas_cra example | Their result | Our RBE | Our CRA certificate | Ours (ms) |
|---|---|---|---|---|
| 00_simple_cube (2 cubes) | solves (stable) | **STABLE** | **STABLE, CERTIFIED (1 iter)** | ~1 |
| tutorial_cubes (4×2×1 + rotated 1×3×1) | solves (stable) | **STABLE** | **STABLE, CERTIFIED (1 iter)** | ~1 |
| 04_stacks (3 cubes tilted 20° about Y) | solves (stable) | **STABLE** | **STABLE, CERTIFIED (1 iter)** | 2 |
| 06_arch (Arch h5/s10/t0.5/d0.5, n=20, μ=0.7) | solves (stable) | **STABLE** (159 ms) | **STABLE, CERTIFIED (1 iter)** | 261 |
| 07_shelf (H-model) | CRA rejects / RBE accepts | **accepts** (matches their RBE) | **rejects** ✓ | 350 |

**Parity: 5/5** (after the KB-9 root-cause fix, same day — see below).

## THE FINDING — KB-9 (RESOLVED same day)
The benchmark exposed a real product bug, and it was never the solver: **MeshContactDetector under-covers
exact-coplanar contacts** — the detected polygon was a pentagon of triangle centroids / edge midpoints
biting INTO the true contact quad, making statically fine assemblies geometrically infeasible (reported as
ADMM non-convergence). Proof chain: (1) equality system consistent (LS residual ~1e-9); (2) handmade exact
joints -> CERTIFIED 1 iter; (3) the existing-but-default-off coplanar-coincidence resolver -> CERTIFIED.
**Fix: the resolver is now ON by default.** A second, distinct issue remains open as KB-10 (exact-joint
path, 53-interface wall conditioning). En route we documented the fixBelowZ semantics trap (tolerance
above the lowest vertex, not an absolute plane). Diagnostics remain as battery canaries
(Kb9DiagnosticsTests).

## Speed protocol (unfilled by design)
We print our wall-clock in the `[bench]` lines above. The same-machine compas_cra/IPOPT timing requires
their conda env (`environment.yml` in their repo; `cra_solve(..., timer=True)`); run on this machine and
fill the column before quoting any "faster than" claim. We do not quote numbers we did not measure.

## Next (in priority order)
1. Same-machine compas_cra/IPOPT timing -> fill the speed column (their conda env, timer=True).
2. Extend to their mesh-based examples (05_wedge type-a..d, 09_bridge) via their data/*.json.
3. KB-10: exact-joint conditioning at 53 interfaces (LS-first warm start for the RBE pre-step).
