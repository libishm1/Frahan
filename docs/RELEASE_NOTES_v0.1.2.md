# Frahan StonePack v0.1.2-alpha

Research preview. This release makes the algorithm core **formally verified** and
wires that verification into CI, then ships two correctness fixes that the
verification itself found.

## 1. Formally verified algorithm core

The mathematical derivations behind the shipping algorithms are now
machine-checked in **Lean 4 + Mathlib** ([`frahan_proofs/`](../frahan_proofs/)).

- **Every named result of the derivations spec is proved or explicitly
  documented prose** — zero `sorry`, zero open goals, and exactly **one cited
  axiom** (KKT necessity under LICQ).
- Covers the trim/clip theorems, **no-fit-polygon separation** (the nester's
  zero-overlap guarantee), power-cell convexity, Welsh–Powell `Δ+1` colouring
  (built from scratch — Mathlib has no such bound), QEM, the Kintsugi pose
  algebra, kriging interpolation, the Lambert equal-area law, phase correlation,
  the CRA safe theorem with its Gale/Farkas converse, and the full
  **Graham 1969 LPT `4/3 − 1/3m` scheduling bound** — the last closed by
  replacing the classical exchange induction with a static pigeonhole argument.
- The library was **independently audited** (three adversarial statement reviews
  plus a mechanical `#print axioms` sweep over all public theorems). The audit
  found the single axiom was mis-stated — it omitted feasibility of the
  minimizer, making it false — and that is fixed. No proved result depended on
  it. Audit verdict: PASS after fixes.

## 2. Verification CI (proofs ↔ shipping C#)

Two GitHub Actions workflows now gate every push:

- **`lean-proofs.yml`** — builds the proof library, rejects any real `sorry`,
  and lists declared axioms.
- **`verification.yml`** — runs
  [`verification/Frahan.Verification.Tests`](../verification/), a **40-fact**
  xUnit + CsCheck suite that links the **real shipping sources** (never copies)
  and tests them against the same machine-checked invariants, with independent
  oracles. Covered: clip/trim, H-rep↔V-rep round-trip, Kahn build order, greedy
  colouring, NFP nesting, joint-set clustering, CRA equilibrium certificates
  (independently rebuilt residual + friction cone), Lambert projection, power
  cells, least-squares plane fitting, Soft-ICP monotonicity, and the saw-bed
  scheduler — the last checked against a **brute-force optimum** for the Graham
  bound.

A failure in that suite is a regression against a machine-checked invariant.

## 3. Two kernel fixes (found by the verification)

- **`ConvexPolyhedron.ClipByHalfSpace` (BlockCutOpt) — volume inflation on
  coincident-plane re-clip.** Re-clipping by a plane that coincides with an
  existing cut face inflated the computed volume: 2.81 % of random cuts, up to
  ~8× on thin slivers, confirmed against a Monte-Carlo ground truth. Reachable
  through `ClipBothSides` and staged/recovery re-cuts, so it could overstate
  recovered block yield. Fixed with a scale-aware no-op guard; the general cut
  path is untouched. Idempotence break-rate 2.81 % → 0.00 %.
- **`BlockGraphColorer.Color` — threw on dense contact graphs.** The palette was
  hard-capped at 8 (the header wrongly said 4), so any assembly needing more
  colours raised `InvalidOperationException` — 23.9 % of random graphs, minimum
  trigger a clique of 9 mutually-touching blocks. The Lean theorem both
  certified the properness and prescribed the fix: scale the palette to `Δ+1`.
  Refusals 4788 → 0; graphs of degree ≤ 7 are coloured identically to before.

Both fixes ship in this release's binaries (the install payload was rebuilt for
this tag) and are re-validated live on canvas — see the delta-audit below.

## 4. Nester A/B — the archived-version bug resolved

A head-to-head run of the current and Zenodo-archived nester on canvas
established that **`main`'s hole-aware nester is the correct one**: the archived
build carries a Grasshopper routing bug that the current code does not. This
release supersedes that archived behaviour. (Method note: old tags do not
rebuild in a worktree due to cross-version drift, so the comparison was made
against git-verified deployed binaries.)

## Validation for this release

- Test battery: **1067 PASS / 1 FAIL / 154 SKIP** (baseline 2026-06-14 was
  1034/0/147; the suite has grown).
- Verification suite: **40 / 40**.
- Lean library: builds green, 0 `sorry`, 1 audited axiom.
- Canvas delta-audit on the rebuilt + redeployed plugin: the colouring example
  and the staged-guillotine example both solve with **0 errors and 0 warnings**,
  with block volumes showing no inflation (~20.2 m³ of blocks inside a 38.7 m³
  bench). Evidence and captures are recorded with the release.

## Known issues

- **KB-14 — Cloud ICP centroid pre-alignment is not outlier robust.** The one
  battery failure. A single far outlier in the target cloud drags the
  arithmetic-mean centroid used for the initial guess, and because that runs
  before trimming, `trimFraction` cannot rescue it. Pre-existing since
  2026-07-10 and unrelated to this release's fixes. A robust-centroid fix is
  proposed and queued with its own property test.
- **KB-15 — Masonry Stability (RBE) reports `error` for a primal-infeasible
  QP.** Infeasibility is not a solver malfunction: by the Farkas converse of the
  safe theorem it is the certificate that the assembly is *unstable*, so the
  verdict should read UNSTABLE rather than `error`/`NaN`. Pre-existing;
  solver code unchanged since 2026-07-13.

Both are documented in [`handoffs/KNOWN_BUGS.md`](../handoffs/KNOWN_BUGS.md).

## Install

`git lfs pull`, then run `install/deploy.ps1` (Windows / Rhino 8) with Rhino
closed. See [`install/INSTALL.md`](../install/INSTALL.md).

Licence unchanged: GPL-3.0, bundling a research-only component
(Kintsugi / PuzzleFusion++) — research and educational use, not commercial.
