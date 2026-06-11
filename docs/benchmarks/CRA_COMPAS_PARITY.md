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
| 04_stacks (3 cubes tilted 20° about Y) | solves (stable) | **SolverError** | blocked by RBE pre-step | 49 (diverged) |
| 06_arch (Arch h5/s10/t0.5/d0.5, n=20, μ=0.7) | solves (stable) | **SolverError** | blocked by RBE pre-step | ~2500 (diverged) |
| 07_shelf (H-model) | CRA rejects / RBE accepts | **accepts** (matches their RBE) | **rejects** ✓ | 350 |

**Parity: 3/5** (the two horizontal-bed cases + the H-model counterexample in both directions).

## THE FINDING — KB-9
Both failures are **inclined-contact** systems; the smallest repro is now **3 unit cubes tilted 20°**
(2 free blocks, 2 interfaces). Density 1 vs 2400 makes no difference. This **subsumes the earlier
"~50-interface ceiling"** diagnosis: the failure axis is contact inclination via the detector path, not
system size. Note the exact-joint generator path HAS certified inclined joints (card 27_06 double-curved),
so prime suspects are the detector-path interface frames/vertex ordering on inclined planes feeding the
equality rows, then penalty conditioning. Full notes: `handoffs/KNOWN_BUGS.md` KB-9. The gap fixtures run
in every battery and SKIP loudly as "KNOWN PARITY GAP KB-9" — they flip to hard assertions when fixed.

## Speed protocol (unfilled by design)
We print our wall-clock in the `[bench]` lines above. The same-machine compas_cra/IPOPT timing requires
their conda env (`environment.yml` in their repo; `cra_solve(..., timer=True)`); run on this machine and
fill the column before quoting any "faster than" claim. We do not quote numbers we did not measure.

## Next (in priority order)
1. Fix KB-9 using the 3-cube repro (single-stepping the equality residual tells frame bug vs conditioning).
2. Re-run this table → expect 5/5; then install their env and fill the IPOPT timing column.
3. Extend to their mesh-based examples (05_wedge type-a..d, 09_bridge) via their data/*.json.
