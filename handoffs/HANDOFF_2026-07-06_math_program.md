# Handoff — verified mathematics program (2026-07-06)

State: SHIPPED to main (`819182d`) and LIVE on <https://libishm1.github.io/Frahan/>
(Mathematics nav section). Release tag `v0.1.0-alpha` = `9c51086` stays frozen
(DOI 10.5281/zenodo.21209690); everything here is post-DOI docs work on main.

## What exists now

`wiki/research/math/` — the maths tab:

- `INDEX.md` — overview + the four-layer **verification ladder**:
  (1) code-bound extraction with Class.Method provenance and mandatory
  deviation-flagging; (2) executable oracles in the battery (0-overlap
  validator, RBE/CRA certificates, residual gates, determinism pins);
  (3) **SMT instance proofs** — Z3, quantified LRA, negation-unsat
  (`verification/verify_instances.py`, 2 instances proved: NFP unit-square,
  IFP erosion); (4) **Lean 4 + Mathlib roadmap** (`LEAN_PLAN.md`, 3 tiers +
  dependency DAG, ~33 named theorems). Where Lean is not the tool the INDEX
  names the complement (exact-integer property tests, benchmark protocol,
  interpenetration oracles, canvas truth criterion).
- `MASONRY_STABILITY.md` (~670 lines) — RBE per-vertex contact forces,
  penalty QP actually minimized, **inscribed** friction pyramid
  `mu_eff = mu*cos(pi/K)` K=8 (verified at FrictionConeBuilder.cs:130),
  CRA alternating convex certificate (NOT IPOPT), OSQP-form ADMM,
  COM-over-support gates.
- `EDGE_MATCHING_SURFACE.md` (~910 lines) — signatures, phase/lag
  correlation, constrained ICP + Kabsch, discrete Fréchet (verified
  symbol-for-symbol vs FrechetDistance.cs:72), Hungarian/JV, Soft-ICP/CPD
  deviations, BFF interface, chart scale + edge-stretch, barycentric lift,
  Pack Surfaces transform composition.
- `GEOLOGY_GPR_CUTTING.md` (~1060 lines) — Terzaghi (15° blind-zone cap),
  P10/P32, Palmström V_b, Monte-Carlo IBSD, Watson mean-shift sets,
  kinematic tests as coded, DFN generators, the full GPR chain (dewow,
  gain, Stolt f-k with Jacobian, Hilbert envelope), kriging, detection
  probability, BlockCutOpt/SlabYieldOpt/wire-saw/RecoveryCascade.
- `frahan_algorithm_derivations.pdf` + `.tex` — the Definition→Theorem→Proof
  corpus (27 sections; compiled with tectonic after fixing a text-mode \R
  bug, also fixed in the code_ws source).
- `AUDIT_coverage.md` — 249 algorithms classified covered/gap/no-derivation.

`wiki/research/packing/EQUATIONS.md` — CORRECTED: new §2.1 derives the
SHIPPING per-column interval heightmap seed (TryGetLowestZ / ScorePlacement /
Add); the voxel/FFT trio is explicitly labeled evolution-reference; §2.6
states the settle terminates on a fixed step budget (not the KE criterion);
Z3-proved instances tagged in §1.2/§1.3.

## Corrections found by this pass (the honesty ledger)

1. Voxel/FFT 3D equations ≠ shipping packer (heightmap interval model added).
2. Bullet settle: step budget, not energy convergence.
3. Friction pyramid ships inscribed (deliberate, conservative; deviation
   from compas_cra's circumscribed K=4 documented).
4. Kriging.Predict returns latent variance `sill - w^T w`, contradicting its
   own header comment (code documented as authoritative).
5. "Phase correlator FFT" attribute vs brute-force O(n²) circular L1 reality.
6. PackOnSurface cites Floater MVC; code is plain triangle barycentric.
7. 22+ total documented code-vs-literature deviations across the pages;
   3 items carry explicit **UNVERIFIED:** flags (grep for them).

## How it was verified (method)

Propose-and-verify pipeline (Code2Math-style architecture, manual agents):
three parallel extraction agents (masonry / edgematch+surface /
geology+GPR+cutting) instructed to trace every equation to source and flag
deviations; main-loop spot-checks re-derived key formulas directly from code
(boundary score `contact - 2.0*overlap` at ContactNfpHoleNester.cs:1382,
threshold :1320, OrderedVertices (y,x) sort :1981, mu_eff :130, Fréchet :72,
friction utilization `ft/(muEff*fnPos)`); Z3 machine-proofs for the decidable
instances; the theorem corpus provides the proof layer.

## Open items (priority order)

1. **SYNTHESIS_3D.md body typesetting** — the typesetter agent completed the
   top ~25 lines of SYNTHESIS_2D (shipped) then died on a session limit;
   SYNTHESIS_3D body is still ASCII (its typeset headline + link to
   EQUATIONS.md is in). Re-run the same conversion task (rules: only
   notation → LaTeX, no semantic changes, code names stay code spans, no
   $ in table cells).
2. **Lean Tier-1 mechanization** — open a `blueprint` GitHub issue pointing
   at wiki/research/math/LEAN_PLAN.md (Sutherland-Hodgman subset, guillotine
   DP, Lloyd monotonicity are the recommended starters). Toolchain not on
   this machine (elan/lake + Mathlib build required).
3. Resolve the 3 UNVERIFIED flags (grep the math pages).
4. More Z3 instances: friction-pyramid-inscribed-in-cone (QF_NRA) and
   BLF-vertex-attainment on a fixture polygon are natural next targets;
   extend verify_instances.py.
5. The corrections log names shipping-code gaps worth issues: Core
   signed-tetra true volume (rho numerator), kriging header comment fix,
   phase-correlator attribute wording.

## Gotchas (this session, hard-won)

- The Bash tool's command param decodes JSON escapes: single backslash + t/r/f/b/n
  in heredocs becomes control characters. Write LaTeX via the Write/Edit
  tools (they preserve backslashes) or build strings with chr(92) in Python.
- GitHub renders $$...$$ natively; the site needs pymdownx.arithmatex +
  MathJax (wired in mkdocs.yml + the workflow-generated bootstrap). Images
  of equations are the wrong plan; xhub is extension-only and obsolete.
- tectonic (portable, %LOCALAPPDATA%\claude-tools) compiles the corpus;
  \R-style macros die in \texttt (text mode).
- Site workflow copies md/images/pdf/tex/py from docs|wiki|examples +
  a top-level whitelist; anything else 404s on Pages even if on GitHub.
