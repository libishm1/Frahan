# Mathematics of Frahan StonePack

The complete mathematical layer behind the plugin: what each subsystem
computes, derived from the *shipping code* (not from paper figures), with the
proofs, the verification machinery, and the mechanization roadmap. Everything
renders natively on GitHub and on the docs site (MathJax).

## Code-bound equation pages (statement + derivation + code provenance + citation)

| Page | Covers |
|---|---|
| [Packing (2D + 3D)](../packing/EQUATIONS.md) | NFP/IFP feasibility, BLF, boundary mode, per-column interval heightmap seed, CoACD+GJK, Bullet settle, honest density |
| [Masonry & stability](MASONRY_STABILITY.md) | RBE contact forces, friction pyramid (inscribed, `mu_eff = mu cos(pi/K)`), penalty QP, CRA certificate, ADMM/OSQP form, COM-over-support |
| [Edge matching & surfaces](EDGE_MATCHING_SURFACE.md) | turning signatures, phase/lag correlation, constrained ICP + Kabsch, discrete Fréchet, Hungarian, Soft-ICP/CPD, BFF interface, chart scale + edge-stretch, barycentric lift, Pack Surfaces transform composition |
| [Geology, GPR & cutting](GEOLOGY_GPR_CUTTING.md) | Terzaghi weighting, P10/P32, Palmström V_b, Monte-Carlo IBSD, Watson mean-shift sets, kinematic tests, DFN generators, Stolt migration chain, kriging, detection probability, BlockCutOpt / SlabYieldOpt / wire-saw / RecoveryCascade |

House rule on honesty: every equation traces to a `Class.Method` that was
actually read; **where the code deviates from the textbook formulation, the
page documents what the code does and flags the deviation.** Ten-plus such
deviations are documented across the pages (e.g. the inscribed friction
pyramid; the "phase correlator" that is a brute-force circular L1, not an
FFT; the Bullet settle that runs a fixed step budget rather than an energy
test; the depth-aware Fresnel detectability that superseded the depth-free
form).

## Theorem corpus (Definition → Theorem → Proof)

- [`frahan_algorithm_derivations.pdf`](frahan_algorithm_derivations.pdf) — the
  typeset corpus: ~33 named results across 27 sections (nesting, packing,
  masonry limit analysis, decomposition, registration, reconstruction,
  discontinuities, GPR, yield). Source:
  [`frahan_algorithm_derivations.tex`](frahan_algorithm_derivations.tex).
- [`AUDIT_coverage.md`](AUDIT_coverage.md) — the completeness audit: every
  distinct `[Algorithm]` title in the codebase (249) classified
  covered / gap / no-derivation-needed.

## Verification ladder (how the math is checked)

Four layers, weakest to strongest; each equation page states which layers it
has passed.

1. **Code-bound extraction.** The equations are read off the implementation
   with file/method provenance, by independent extraction passes that are
   instructed to document code-vs-literature deviations rather than assume
   the paper. (This is a propose-and-verify agent pipeline in the spirit of
   problem-evolution frameworks: extract, then adversarially cross-check
   against source.)
2. **Executable oracles (continuous, in the test battery).** The claims the
   equations make are enforced at runtime by exact checkers: the boolean
   0-overlap layout validator (2D), the interval/GJK interpenetration checks
   (3D), the RBE/CRA equilibrium certificates (masonry), residual gates
   (matching), and determinism pins. ~1,050 tests run per merge.
3. **SMT instance proofs (Z3).** Decidable instances of the published
   theorems are machine-proved by encoding the negation and obtaining
   `unsat`. Currently proved (all four in
   [`verification/verify_instances.py`](verification/verify_instances.py),
   reproducible with `pip install z3-solver`): the NFP unit-square instance,
   the IFP erosion instance, the **BLF lexicographic minimum attained at a box
   vertex**, and the **inscribed friction pyramid being a subset of the
   Coulomb cone** (K=4, the masonry conservativeness claim). This is the
   recommended complement wherever full mechanization is pending: exact, fast,
   and it covers real-arithmetic instances Lean would need Mathlib analysis
   for.
4. **Lean 4 + Mathlib mechanization (roadmap).** The full plan lives in
   [`LEAN_PLAN.md`](LEAN_PLAN.md): per-theorem Lean statement sketches, the
   Mathlib pieces they build on, three difficulty tiers, and the dependency
   DAG (Tier 1: combinatorial/induction results like Sutherland-Hodgman
   subset, guillotine DP optimality, Lloyd monotonicity; Tier 2: spectral /
   Fourier results like Horn/Kabsch, PCA, the Minkowski NFP theorem; Tier 3:
   convex duality / PDE results like CRA-as-SOCP-feasibility via Farkas,
   Poisson reconstruction). Mechanizing Tier 1 is an open contributor
   blueprint node.

**Where Lean is NOT the right tool** (and what is): floating-point behavior
of the shipping kernels (use exact integer arithmetic on the Clipper2 lane +
property-based tests against exact oracles); physics-engine outcomes (use the
interpenetration oracle + statistical repeatability runs); performance claims
(use the measured benchmark protocol in
[`docs/results/RESULTS.md`](../../../docs/results/RESULTS.md)); geometric
results on real scan data (use the visual truth criterion: validated on the
Rhino canvas).

## Corrections log (found by this verification pass, 2026-07-06)

- The 3D voxel/FFT seed equations describe the **evolution reference**, not
  the shipping Core packer; the shipping seed is the per-column interval
  heightmap, now derived in the packing page §2.1.
- `BulletSettleService` terminates on a fixed step budget, not the kinetic
  energy criterion; the packing page §2.6 now says so.
- The friction pyramid ships **inscribed** (`mu_eff = mu cos(pi/K)`, K=8),
  deliberately conservative vs the circumscribed K=4 reference
  implementation; documented in the masonry page.
- `Kriging.Predict` returns the latent variance `sill - w^T w`, not
  `(sill + nugget) - w^T w` as its own header comment claimed; the header
  comment was **corrected in code** 2026-07-06 to match `Predict`.
- The honest density numerator is now computed in Core without Rhino:
  `MeshPackItem.MeshVolume` (signed-tetra) + `MeshPackResult.FillRatioMeshVolume`
  were added 2026-07-06 (test: `mesh signed-tetra volume is honest vs bbox`),
  closing the "VolumeEstimate is bbox only" gap the packing page flagged.
