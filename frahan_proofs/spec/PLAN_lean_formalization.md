# Lean formalization plan — Frahan algorithm theorems

Target: mechanize the theorems of `frahan_algorithm_derivations.tex` in **Lean 4 + Mathlib**. This plan gives,
per theorem: a Lean statement sketch, the Mathlib pieces it builds on, a difficulty tier, and a dependency
order. The `.tex` is the spec; this file is the build order.

Scope: ~33 named results across §1–§27 (22 original + 11 from the §20–§27 completeness pass). Every result in
the audit (`AUDIT_coverage.md`) that is a *theorem* (not an excused I/O routine) is listed here.

## 0. Project setup

- `lake new frahan_proofs math`; pin a Mathlib toolchain. One file per section: `Frahan/Nesting.lean`,
  `Frahan/Masonry.lean`, ... ; a `Frahan/Common.lean` for shared defs.
- Convention: each `.tex` Theorem N becomes a Lean `theorem tex_<label>` with a docstring citing the `.tex`
  label, so the two stay in sync.

## 1. Shared formalization (`Common.lean`)

- `abbrev Polygon := List (ℝ × ℝ)` ; `def signedArea`, `def isCCW`, `def IsConvexPoly`, `def reflexAt`.
- `def halfplaneClip : Polygon → Halfplane → Polygon` (Sutherland–Hodgman as a fold over edges).
- Convex sets / hull / Minkowski: reuse `Mathlib.Analysis.Convex.Hull`, `Convex.add` (Minkowski),
  `convexHull_min`. Cones: `Mathlib.Analysis.Convex.Cone.Basic`.
- Rotations/poses: `Matrix.specialOrthogonalGroup`, `Quaternion`, `Mathlib.Geometry.Euclidean.*`.
- Spectral: `Mathlib.Analysis.InnerProductSpace.Spectrum` (`LinearMap.IsSymmetric.eigenvalue...`),
  `InnerProductSpace.rayleigh` (extremes of the Rayleigh quotient = eigenvalues).
- Graphs: `Mathlib.Combinatorics.SimpleGraph.*`, `Quiver`/`Rel` for DAGs, `SimpleGraph.Coloring`.

## 2. Tiers and dependency DAG

- **Tier 1 — combinatorial / induction / order** (cleanest; start here, mostly self-contained):
  Lemma~sh, Thm~trim, Thm~imaiiri, Thm~guillodp, Thm~kplanes, Thm~hm, Thm~kahn, Thm~lpt, FFD, Welsh–Powell,
  Thm~potato (the ordering inequality), Lloyd descent (Thm in §12/§15), Hungarian optimality (cf. Mathlib
  matching).
- **Tier 2 — finite linear algebra / spectral / Fourier** (Mathlib has the tools):
  Lemma~plane, Thm~nugget (SPD), Thm~horn + Prop~kabsch (spectral/SVD), Prop~pca, Thm~qem, Prop~power,
  Thm~nfp (Minkowski), Lemma~clip3d, Thm~phasecorr (Fourier shift), Thm~lambert (Jacobian identity),
  Prop §5 planarity (Chebyshev/Rayleigh).
- **Tier 3 — analysis / convex-duality / measure / PDE** (needs heavier Mathlib or a stated axiom):
  Thm~cra (limit-analysis = LP/SOCP feasibility ↔ Farkas/Gale duality), Thm~blocktheory (cone emptiness),
  Thm~settle (KKT), Thm~cpd (EM monotonicity), Thm~poisson (distributional Euler–Lagrange), Thm~stolt
  (wave-equation dispersion), Thm~heat (Varadhan), CVT/GVF energy descent.

Dependency edges: Thm~horn → Prop~kabsch → Thm~cpd; Lemma~sh → Thm~trim → Thm~potato (ordering);
Thm~cra → Thm~settle (same KKT); rayleigh → {Lemma~plane, Prop~pca, §5, Thm~horn}.

## 3. Per-theorem plan

### Tier 1 (do first)
| .tex | Lean statement (sketch) | builds on | effort |
|---|---|---|---|
| Lemma~sh | `clip p H = {x ∈ p | x ∈ H} ∧ area (clip p H) ≤ area p` | fold, `Convex.inter` | S |
| Thm~trim | greedy clip seq is `⊆`-monotone, terminates, output `IsConvexPoly` | `WellFounded` on reflex count | M |
| Thm~potato | `area (greedyTrim p) ≤ area (convexSkull p) ≤ area p` | Tier1 trim + `csSup` over convex subsets | M |
| Thm~imaiiri | BFS fewest-edges path in admissibility DAG = min #segments | `SimpleGraph` BFS, path↔approx bijection | M |
| Thm~guillodp | Bellman recursion `V(R)=max(...)` = optimum over guillotine tilings | strong induction on subrects | M |
| Thm~kplanes | Lloyd step is cost-non-increasing; terminates (finite assignments) | `Finset`, monovariant | S |
| Thm~hm | Hertel–Mehlhorn convex partition ≤ 4·OPT | reflex-vertex counting | M |
| Thm~kahn | source-removal yields a topo order ↔ DAG; respects edges | `Quiver`/`Rel.acyclic`, induction | S |
| Thm~lpt | `makespan_LPT ≤ (4/3 − 1/3m)·OPT` | load-counting, `Finset.sum` | L |
| (FFD) | `FFD ≤ 11/9·OPT + 6/9` | weight argument | L |
| (Welsh–Powell) | greedy degree-order coloring ≤ `Δ+1` | `SimpleGraph.Coloring`, `colorOrder` | S |

### Tier 2
| .tex | Lean statement (sketch) | builds on | effort |
|---|---|---|---|
| Lemma~plane | non-collinear ⇒ `M.PosDef` ⇒ unique LS plane | `Matrix.PosDef`, normal eqns | S |
| Thm~nugget | `nugget=0 ∧ distinct ⇒ interp`; `nugget>0 ⇒ ‖correction‖<‖r‖` | `PosDef`, operator norm | M |
| Thm~horn | optimal `R` = top eigenvector of `N(M)` (quaternion) | spectrum + `rayleigh` | L |
| Prop~kabsch | weighted Kabsch SVD form `R=V·diag(1,1,det)·Uᵀ` = Horn | polar/SVD, `det` sign | M |
| Prop~pca | normal = least eigenvector; OBB in eigenbasis | spectral thm | S |
| Thm~qem | `vᵀ K_p v = dist(v,plane)²`; optimum solves `Q̄ v̄ = −b` | inner product, `Matrix.inv` | S |
| Prop~power | power cell = `⋂` half-spaces ⇒ convex polytope | `Convex.iInter`, affine | S |
| Thm~nfp | `int(A) ∩ int(B+t) = ∅ ↔ t ∉ int(A ⊕ −B)` | `Convex.add`, Minkowski | M |
| Lemma~clip3d | `Q ∩ H` convex `⊆ Q` (3D Sutherland–Hodgman) | `Convex.inter` | S |
| Thm~phasecorr | shift `f₂=f₁(·−t) ⇒ ℱ⁻¹[normalized cross-power]=δ(·−t)` | `Mathlib.Analysis.Fourier`, shift thm | M |
| Thm~lambert | `r=√2 sin(θ/2)` ⇒ `r dr dφ = ½ sinθ dθ dφ` (area-preserving) | `deriv`, trig identities | S |
| §5 planarity | best-fit plane = least eigvec; `δ` = Chebyshev dev | `rayleigh` | S |

### Tier 3 (state precisely; discharge later or axiomatize with a literature citation)
| .tex | Lean statement (sketch) | gap / approach |
|---|---|---|
| Thm~cra | stable ↔ `∃ f ∈ 𝒦 (SOC cone), A f = g` | convex-cone feasibility; Farkas/`Mathlib...Cone.Dual`; SOCP is heavy — start with the LP (frictionless) case |
| Thm~blocktheory | removable ↔ `JP ≠ ∅ ∧ JP ∩ EP = ∅` | polyhedral-cone emptiness (LP feasibility); cite Shi for the kinematics |
| Thm~settle | rest ↔ KKT of `min PE s.t. φ≥0` = §9 equilibrium | `Mathlib` KKT not full; state as cone condition, reuse cra |
| Thm~cpd | EM round is likelihood-non-decreasing | measure-theoretic EM not in Mathlib; axiomatize the Q-bound or prove finite-mixture case |
| Thm~poisson | `argmin ∫‖∇χ−V‖² solves Δχ=∇·V` | needs Sobolev/calc-of-variations; state E–L weakly, cite |
| Thm~stolt | dispersion `ω²=c²(k²)` ⇒ remap + Jacobian | needs Fourier-integral PDE; `Mathlib.Analysis.Fourier` partial; state Jacobian lemma, axiomatize wave-eq |
| Thm~heat | Varadhan `lim −4t log uₜ = φ²` ⇒ method recovers `φ` | Riemannian heat kernel not in Mathlib; axiomatize Varadhan, prove the linear-solve steps |

**S/M/L** = small/medium/large; Tier-3 rows are intentionally "state-and-cite": the Lean *statement* is exact,
the proof imports a named classical lemma as an axiom until Mathlib has the analysis.

## 4. Milestone order

1. `Common.lean` + Lemma~sh + Thm~trim + Thm~kahn + Welsh–Powell (warm-up, pure combinatorics).
2. Thm~imaiiri + Thm~guillodp + Thm~kplanes + Thm~hm (the discrete optimality core).
3. Spectral block: `rayleigh` wrappers → Lemma~plane, Prop~pca, §5, Thm~qem, Thm~horn, Prop~kabsch.
4. Convex block: Prop~power, Lemma~clip3d, Thm~nfp, Thm~potato.
5. Fourier/trig: Thm~phasecorr, Thm~lambert.
6. Approx bounds: Thm~lpt, FFD (longest, do when momentum is high).
7. Tier 3 statements stubbed with `axiom`/`sorry` + citations; discharge opportunistically as Mathlib grows.

## 5. Honest gaps

- Mathlib **has**: spectral theorem, Rayleigh quotient, convex hull/Minkowski/cones + duality, SimpleGraph +
  colorings + matchings, Fourier transform, `Matrix.PosDef`, `SpecialOrthogonalGroup`, quaternions, measure
  theory + Jacobian/change-of-variables.
- Mathlib **lacks** (→ Tier 3 axiomatize): Riemannian heat kernel / Varadhan, distributional Euler–Lagrange
  for Poisson, wave-equation/Fourier-integral operators for Stolt, measure-theoretic EM convergence, a general
  KKT theorem for inequality-constrained NLPs. For each, the plan states the exact theorem and imports the
  classical result as a named axiom, so the formal development is honest about what is proved vs cited.
- SVD in Mathlib is partial; Horn/Kabsch can instead go via the **polar decomposition** + spectral theorem
  (the `det`-sign correction is the only fiddly part).

## 6. Resume / status

Plan only — **no Lean code written yet**. First action on resume: `lake new`, then Milestone 1.
Cross-references: `AUDIT_coverage.md` (what's covered), `frahan_algorithm_derivations.tex` (the proofs),
`PROGRESS_lean_derivations.md` (the .tex build log).
