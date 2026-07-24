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
  [P] thm:kahn      emit-loop converse stuck ⇒ cycle (Scheduling,
                    stuck_implies_cycle) — the loop sticks only on a non-DAG
  [P] thm:kplanes   descent inequality + finite termination (Clustering)
  [P] mode-merge    greedy mode-merge keeps pairwise-separated joint-set poles
                    (Clustering, mergeKeep_separated) — the clusterer merge step;
                    CODE-VERIFIED (0/19274 pole pairs within MergeDeg; code_ws
                    outputs/2026-07-24/clustering_verification)
  [P] (WelshPowell) greedy Δ+1 coloring, any insertion order (Coloring) —
                    built from scratch; Mathlib has no such bound
  [P] thm:imaiiri   min segments = G.dist, achieved by a shortest walk +
                    lower bound for every approximation (Paths); walk=path form
  [P] thm:guillodp  DP optimal substructure V=max(place,cut) (Guillotine,
                    guillotine_dp_recursion via Finset.sup'_union)
  [P] thm:guillotine wire-saw separability ⇔ guillotine tree + staged φ=1
                    (Guillotine, GuillotineTree + stagedThreeStage)
  [P] thm:hm        Hertel–Mehlhorn 4·OPT counting core (Approx,
                    hertelMehlhorn_four_opt; geometric facts = hypotheses)
  [P] thm:lpt       OPT bounds + (2−1/m) bound + tight 4/3 arith core +
                    execution-trace realization (Machines, list_schedule_
                    decomposition PROVED); [W] full Graham 4/3 lpt_tight_bound —
                    needs the Case-B pigeonhole optimality (not reducible to the
                    conditional arith core; concrete counterexample in-file)
  [P] (FFD)         first-fit 2·OPT core (Approx, firstFit_lt_two_opt);
                    [W] tight 11/9·OPT+6/9 (Dósa)
  [P] thm:potato    greedy trim ≤ convex skull ≤ area(P) (Common) —
                    Chang–Yap exactness/complexity stays prose
  [P] nfp-sep       no-fit-polygon placement ⇒ disjoint parts (Packing) —
                    overlap ⟺ t ∈ A−B, and t ∉ A−B ⇒ disjoint. Matches the
                    code-verified nester (0 overlap / 34832 pairs; code_ws
                    outputs/2026-07-24/nester_verification). Gap CLOSED: proof
                    and code now agree.

Tier 2 — linear algebra / spectral / Fourier
  [P] prop:power    power cell convex (Power)
  [P] lem:clip3d    subsumed: the development is dimension-generic (Common)
  [P] lem:plane     non-collinear ⇒ Gram PosDef ⇔ picks span + LS-fit
                    UNIQUENESS (Fitting, ls_fit_unique proved); matrix θ=M⁻¹b prose
  [P] thm:nugget    τ=0 interpolation kⱼᵀK⁻¹=eⱼ ⇒ d̂(pⱼ)=dⱼ + τ>0 strict-
                    smoother contraction PROVED (Kriging, both; elementary r=K₀s+τs)
  [P] thm:horn      centroid-alignment reduction: optimal translation = centroid
                    difference (Registration, horn_optimal_translation); rotation
                    = top eigenvector of N(M) is prose (quaternion, research-scale)
  [P] prop:kabsch   shares horn_optimal_translation; SVD=Horn det-sign is prose
  [P] prop:pca      least-eigenvalue direction minimizes variance = surface
                    normal + eigenvalue identification PROVED (Spectral,
                    least_eigenvalue_lowerBound); OBB-in-eigenbasis prose
  [P] thm:qem       vᵀKₚv = squared plane dist; QEM convex; ∇=0 ⇒ global
                    min (Registration) — matrix-block Q̄v̄=−b stays basis form
  [P] thm:phasecorr DFT shift + normalized cross-power = unit phase + inverse-
                    DFT of phase = shifted delta PROVED (Fourier, all three)
  [P] thm:lambert   r = √2 sin(θ/2): r·r' = ½ sinθ area-element identity
                    (Projection)
  [P] §5 planarity  best-fit plane = least eigenvector: subsumed by prop:pca
                    (Spectral) + lem:plane (Fitting); Chebyshev (min-max) dev prose

Tier 3 — analysis / duality / PDE (state exactly; axiomatize the named
classical ingredient with a citation; discharge as Mathlib grows)
  [P] thm:cpd          EM/MM monotonicity mm_monotone (TierThree)
  [P] thm:poisson      least-squares E–L normal equation poisson_normal_eq_min
                       (TierThree, T=∇ ⇒ Δχ=∇·V); operator-level QEM too
  [P] thm:cra          convex-feasibility admissibleSet_convex + Gale/Farkas
                       duality converse cra_farkas (TierThree, via Hahn–Banach)
  [P] thm:blocktheory  Removable def + not_removable half (TierThree, Shi)
  [P] thm:settle       convex-optimality core settle_convex_optimality +
                       [A] KKT necessity settle_kkt (cited axiom, LICQ) — TierThree
  [P] thm:stolt        amplitude Jacobian ∂ω/∂k_z = c·k_z/√ (TierThree,
                       stolt_dispersion_jacobian); full Fourier remap = prose
  [~] thm:heat         heat-method geodesics: the Poisson integration step
                       Δφ=∇·X IS proved (poisson_normal_eq_min); the Varadhan
                       asymptotic −4t·log uₜ→φ² is PROSE — stating it soundly
                       needs heat-kernel + geodesic machinery Mathlib lacks (a
                       free-parameter axiom relating two arbitrary functions
                       would be unsound). The one deliberately-prose residue.
  NOTE: Tier-3 policy — prove the sound abstract core, never axiomatize an
  equivalence to a free predicate (unsound). See TierThree.lean.
  [P] thm:kintsugi     world-pose composition + faithful uniqueness +
                    g-conjugation equivariance (Registration)
  [P] thm:surfpack     non-overlap transfer: injective chart ⇒ disjoint UV
                       placements stay disjoint on the surface (Surface,
                       single + pairwise); area-scale e^{2u} = conformal
                       Jacobian, prose
  (thm:guillotine moved to Tier-1: proved via GuillotineTree + staged φ=1)
-/

-- No `proof_wanted` is currently open: every remaining [D] item needs
-- its supporting definitions (graphs-with-paths, tilings, machine
-- schedules, …) stated first, and every [A] item awaits its Tier-3
-- axiomatization pass. New exact statements land here before proofs.
