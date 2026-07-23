import FrahanProofs.Common

/-!
Frahan StonePack — Lean formalization roadmap.

This file is the build order for mechanizing ALL named results of
`spec/frahan_algorithm_derivations.tex` (~33 theorems across §1–§27),
following `spec/PLAN_lean_formalization.md`. Statements appear here as
`proof_wanted` (exact statement, proof pending) or as structured TODO
comments when the statement itself still needs supporting definitions.
Nothing in this repository uses `sorry`: a result is either proved (see
`Common.lean`) or explicitly marked wanted here.

Status key: [P] proved in Common.lean · [W] proof_wanted below ·
[D] needs definitions first · [A] Tier 3, will import a named classical
result as an axiom with citation when stated.

Tier 1 — combinatorial / induction / order
  [P] lem:sh        clip = intersection; subset + measure monotone
  [P] thm:trim      chain identity, subset, measure, convexity (convex input)
  [P] thm:kahn      source-existence half (dag_has_source)
  [W] thm:kahn      full loop correctness + stuck ⇒ cycle
  [D] thm:imaiiri   BFS fewest-edges path = min-# approximation
  [D] thm:guillodp  Bellman recursion optimal over guillotine tilings
  [D] thm:kplanes   Lloyd step cost-non-increasing, finite termination
  [D] thm:hm        Hertel–Mehlhorn ≤ 4·OPT convex partition
  [D] thm:lpt       Graham LPT (4/3 − 1/3m) bound
  [D] (FFD)         first-fit-decreasing 11/9·OPT + 6/9
  [W] (WelshPowell) greedy coloring ≤ Δ+1  (via Mathlib SimpleGraph)
  [D] thm:potato    greedy trim ≤ convex skull ≤ area(P)

Tier 2 — linear algebra / spectral / Fourier
  [D] lem:plane     non-collinear ⇒ PosDef normal equations ⇒ unique LS plane
  [D] thm:nugget    nugget=0 interpolates; nugget>0 strictly smooths
  [D] thm:horn      optimal rotation = top eigenvector of N(M)
  [D] prop:kabsch   weighted Kabsch SVD = Horn (det-sign correction)
  [D] prop:pca      normal = least eigenvector; OBB in eigenbasis
  [D] thm:qem       vᵀKₚv = squared plane distance; optimum solves Q̄v̄ = −b
  [W] prop:power    power cell = intersection of half-spaces ⇒ convex
  [P] lem:clip3d    (subsumed: Common.lean is dimension-generic)
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
  [D] thm:kintsugi     world-pose composition T_world = T_unnorm·T_net·T_norm
  [D] thm:surfpack     surface-packing transfer (chart scale bound)
  [D] thm:guillotine   wire-saw separability ⇔ guillotine, staged φ=1
-/

namespace Frahan

open MeasureTheory

variable {E : Type*} [NormedAddCommGroup E] [InnerProductSpace ℝ E]

/-- tex Proposition `prop:power` (statement): the power cell of site `p`
with weight `w` among sites `q ∈ Q` with weights `wq` is convex — it is an
intersection of half-spaces because the quadratic terms cancel in
`‖x − p‖² − w ≤ ‖x − q‖² − wq`. -/
proof_wanted powerCell_convex (p : E) (w : ℝ) (Q : List (E × ℝ)) :
    Convex ℝ {x : E | ∀ qw ∈ Q, ‖x - p‖ ^ 2 - w ≤ ‖x - qw.1‖ ^ 2 - qw.2}

/-- tex Theorem `thm:kahn` (full statement, forward half): if the strict
precedence relation is acyclic (transitive + irreflexive on a finite
type), there exists a linear install order in which every part follows
all of its predecessors. (Kahn's emit-loop realises one such order;
`Frahan.dag_has_source` proves the loop never sticks.) -/
proof_wanted kahn_topological_order {α : Type*} [Finite α]
    (r : α → α → Prop) [IsTrans α r] [IsIrrefl α r] :
    ∃ L : LinearOrder α, ∀ a b, r a b → L.lt a b

end Frahan
