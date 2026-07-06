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

## Speed — same-machine compas_cra/IPOPT timing (MEASURED 2026-06-11)
Environment: pip venv (no conda), Python 3.9.13 win64, compas 2.15.1, compas_assembly 0.7.1,
compas_cra 0.4.0 with its pinned pyomo==6.4.2, official COIN-OR Ipopt 3.14.19 win64 binary on PATH
(cyipopt has no win64 PyPI wheel, but compas_cra drives IPOPT through pyomo's executable interface,
so cyipopt is not needed). Script: `outputs/2026-06-11/cra_timing/time_examples.py` —
fixtures reproduce their docs/examples verbatim (cra_view removed), fresh assembly per run,
`time.perf_counter` around the solve call, best of 3 after 1 warmup. "solve" is compas_cra's own
`timer=True` phase (pyomo NL write + ipopt.exe + parse), which is what a compas_cra user pays per call.

| compas_cra example | their solver | their total (ms) | their solve (ms) | termination | ours (ms) |
|---|---|---|---|---|---|
| 00_simple_cube | cra_solve | 52.6 | 48.9 | optimal | ~1 (CRA cert) |
| tutorial_cubes | cra_solve | 50.5 | 47.5 | optimal | ~1 (CRA cert) |
| 04_stacks | cra_solve | 933.1 | 914.2 | optimal | 2 (CRA cert) |
| 06_arch | rbe_solve | 123.4 | 101.8 | optimal | 159 (RBE) |
| 06_arch | cra_solve | 50523.9 | 50434.9 | **maxIterations** | 261 (CRA cert) |

Honest caveats, in both directions:
- **Their RBE beats ours on the arch** (102-123 ms vs our 159 ms). No "faster than" claim for RBE.
- CRA on the cubes/stacks fixtures: ours is ~50x (cubes) and ~470x (stacks) faster in **wall-time
  per call** -- NOT a solver-algorithm gap. Their per-call floor (~50 ms) includes ipopt.exe process
  spawn + NL file I/O, inherent to their out-of-process architecture; subtract that floor and the gap
  is far smaller. The honest reading: our in-process CRA cert is ~1-2 ms; theirs is ~50 ms + spawn.
- **06_arch cra_solve did NOT converge here**: IPOPT 3.14.19 exits at max_iter=3000 (50.4 s) with
  constraint violation 3.8e-8 (physically solved) but dual infeasibility stalled at 2.33 against
  their hardcoded tol=1e-10. This is their pinned stack on win64; their docs env (conda-forge
  ipopt) may converge. The 50.4 s figure is a time-to-exit, NOT a converged solve time. Do not
  quote it as a speedup denominator; the honest arch-CRA comparison is "ours certifies in 261 ms,
  theirs did not terminate optimally on this machine".

## Next (in priority order)
1. ~~Same-machine compas_cra/IPOPT timing~~ DONE 2026-06-11 (table above). Optional follow-up:
   retry 06_arch cra_solve under their conda-forge ipopt build to check the maxIterations exit.
2. Extend to their mesh-based examples (05_wedge type-a..d, 09_bridge) via their data/*.json.
3. KB-10: exact-joint conditioning at 53 interfaces (LS-first warm start for the RBE pre-step).
