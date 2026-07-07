# Frahan proofs — machine-verified theorems (Lean 4)

Lean 4 formalizations of the combinatorial results behind the algorithm
derivations ([`frahan_algorithm_derivations.tex`](../wiki/research/math/frahan_algorithm_derivations.tex),
plan in [`LEAN_PLAN.md`](../wiki/research/math/LEAN_PLAN.md)). This is the
collaboration node for blueprint issue #12.

**No Mathlib dependency** — everything here is provable in core Lean 4, so
`lake build` is fast for any contributor (no multi-GB Mathlib cache). The
analysis / linear-algebra theorems that *do* need Mathlib are the open tranche
below.

## Build + verify

```
cd lean
lake build            # compiles all proofs; a green build IS the verification
```

Every theorem is complete (no `sorry`). Axiom-checked clean — each depends only
on Lean's standard axioms (`propext`, `Classical.choice`, `Quot.sound`), never
`sorryAx`:

```
lake env lean -Dweak.linter.all=false <<< 'import FrahanProofs
#print axioms FrahanProofs.nfp_interval'
```

## Verified theorems (7)

| Theorem | Statement | Derivation formalized |
|---|---|---|
| `Nesting.nfp_interval` | `[a₁,a₂]+p` overlaps `[b₁,b₂]` iff `p ∈ [b₁-a₂, b₂-a₁]` | NFP = A ⊕ (−B), the 1D no-fit interval (2D NFP = per-axis conjunction) |
| `Nesting.first_verified_is_lexmin` | on a lex-sorted candidate list, the FIRST verification-gate survivor is the lex-minimum of ALL survivors | **the shipping selection rule**: `OrderedVertices` (y,x)-sort + first `TryVerifiedCandidate` survivor in `ContactNfpHoleNester` loses nothing |
| `Nesting.blf_argmin_mem` | a nonempty candidate list has a lexicographic `(y,x)` minimum that is a member and ≤ all | bottom-left placement rule (linear objective attains min at a vertex) |
| `Nesting.clip_subset` | `filter p xs ⊆ xs` | Sutherland–Hodgman: a greedy convex trim only shrinks the blank |
| `Packing.cascade_ge_mem` | `max`-cascade ≥ every element | RecoveryCascade multi-scale monotonicity |
| `Packing.guillotine_no_loss` | every item is on exactly one side of a full-span cut | guillotine separability by recursive full-span splits |
| `Packing.orientation_count` | `|6 up-faces × 4 quarter-turns| = 24` | the 24-orientation cube rotation group (0 axioms) |

## Open tranche (needs Mathlib — pick one up)

The spectral / convex / analysis theorems in the plan need Mathlib's
`Matrix`, `InnerProductSpace`, `Convex`, and measure/PDE machinery. Each is a
self-contained node — add a `[[require]] mathlib` and a `FrahanProofsMathlib/`
library, then formalize one:

- **Horn / Kabsch** absolute orientation = top quaternion eigenvector (spectral).
- **CRA masonry** static theorem = SOCP feasibility ↔ Farkas/Gale duality.
- **Poisson reconstruction** = distributional Euler–Lagrange (`Δχ = ∇·V`).
- **Lloyd / k-means** energy descent over `ℝ` (the mean minimizes SSE).
- **Minkowski NFP** in full 2D over `Convex` sets.
- **PCA / OBB** least-eigenvector planarity (Rayleigh quotient extremes).

Tiers, Mathlib dependencies, and the dependency DAG are in
[`LEAN_PLAN.md`](../wiki/research/math/LEAN_PLAN.md). The convention: each `.tex`
Theorem N becomes `theorem tex_<label>` with a docstring citing the label.
