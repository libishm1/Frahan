import FrahanProofs.Common
import FrahanProofs.Scheduling
import FrahanProofs.Power
import FrahanProofs.Clustering
import FrahanProofs.Coloring

/-!
Frahan StonePack — Lean formalization roadmap.

This file is the build order for mechanizing ALL named results of
`spec/frahan_algorithm_derivations.tex` (~33 theorems across §1–§27),
following `spec/PLAN_lean_formalization.md`. Statements appear here as
`proof_wanted` (exact statement, proof pending) or as structured TODO
comments when the statement itself still needs supporting definitions.
Nothing in this repository uses `sorry`: a result is either proved or
explicitly marked wanted here.

Status key: [P] proved (file) · [W] proof_wanted below ·
[D] needs definitions first · [A] Tier 3, will import a named classical
result as an axiom with citation when stated.

Tier 1 — combinatorial / induction / order
  [P] lem:sh        clip = intersection; subset + measure monotone (Common)
  [P] thm:trim      chain identity, subset, measure, convexity (Common)
  [P] thm:kahn      source-existence (Common) + linear extension (Scheduling)
  [D] thm:kahn      loop-correctness of the literal emit loop; stuck ⇒ cycle
  [P] thm:kplanes   descent inequality + finite termination (Clustering)
  [P] (WelshPowell) greedy Δ+1 coloring, any insertion order (Coloring) —
                    built from scratch; Mathlib has no such bound
  [D] thm:imaiiri   BFS fewest-edges path = min-# approximation
  [D] thm:guillodp  Bellman recursion optimal over guillotine tilings
  [D] thm:hm        Hertel–Mehlhorn ≤ 4·OPT convex partition
  [D] thm:lpt       Graham LPT (4/3 − 1/3m) bound
  [D] (FFD)         first-fit-decreasing 11/9·OPT + 6/9
  [P] thm:potato    greedy trim ≤ convex skull ≤ area(P) (Common) —
                    Chang–Yap exactness/complexity stays prose

Tier 2 — linear algebra / spectral / Fourier
  [P] prop:power    power cell convex (Power)
  [P] lem:clip3d    subsumed: the development is dimension-generic (Common)
  [D] lem:plane     non-collinear ⇒ PosDef normal equations ⇒ unique LS plane
  [D] thm:nugget    nugget=0 interpolates; nugget>0 strictly smooths
  [D] thm:horn      optimal rotation = top eigenvector of N(M)
  [D] prop:kabsch   weighted Kabsch SVD = Horn (det-sign correction)
  [D] prop:pca      normal = least eigenvector; OBB in eigenbasis
  [P] thm:qem       vᵀKₚv = squared plane dist; QEM convex; ∇=0 ⇒ global
                    min (Registration) — matrix-block Q̄v̄=−b stays basis form
  [D] thm:phasecorr Fourier shift ⇒ δ at the translation
  [D] thm:lambert   r = √2 sin(θ/2) is area-preserving (Jacobian identity)
  [D] §5 planarity  best-fit plane = least eigvec; Chebyshev deviation

Tier 3 — analysis / duality / PDE (state exactly; axiomatize the named
classical ingredient with a citation; discharge as Mathlib grows)
  [A] thm:cra          static/safe theorem = SOC cone feasibility (Farkas)
  [A] thm:blocktheory  removability = polyhedral cone emptiness (Shi)
  [A] thm:settle       rest = KKT of constrained energy minimum
  [A] thm:cpd          EM round is likelihood-non-decreasing
  [A] thm:poisson      argmin ∫‖∇χ−V‖² solves Δχ = ∇·V (weak E–L)
  [A] thm:stolt        constant-v dispersion remap + Jacobian
  [A] thm:heat         Varadhan limit recovers geodesic distance
  [P] thm:kintsugi     world-pose composition + faithful uniqueness +
                    g-conjugation equivariance (Registration)
  [D] thm:surfpack     surface-packing transfer (chart scale bound)
  [D] thm:guillotine   wire-saw separability ⇔ guillotine, staged φ=1
-/

-- No `proof_wanted` is currently open: every remaining [D] item needs
-- its supporting definitions (graphs-with-paths, tilings, machine
-- schedules, …) stated first, and every [A] item awaits its Tier-3
-- axiomatization pass. New exact statements land here before proofs.
