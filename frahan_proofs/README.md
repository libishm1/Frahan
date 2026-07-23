# Frahan StonePack — formal derivations (Lean 4 + Mathlib)

Machine-checked derivations for the algorithms shipped in this
repository. The mathematical spec is
[`spec/frahan_algorithm_derivations.tex`](spec/frahan_algorithm_derivations.tex)
(~33 named results across 27 algorithm families, from kriging and slab
trimming to nesting, masonry equilibrium and registration); the build
order is [`spec/PLAN_lean_formalization.md`](spec/PLAN_lean_formalization.md).

## Honesty contract

- Every Lean result carries a docstring citing its `.tex` label, so the
  informal proof and the mechanized one stay in sync.
- **No `sorry` anywhere.** A result is either proved, or stated as
  `proof_wanted` in `FrahanProofs/Roadmap.lean`, or listed there as a
  TODO awaiting supporting definitions. Tier-3 analysis results
  (Varadhan, Stolt, EM convergence, KKT) will import their classical
  ingredient as a *named axiom with a literature citation* when stated —
  the development is explicit about what is proved versus cited.

## Status (Milestone 1)

Proved in `FrahanProofs/Common.lean`, dimension-generic over any real
inner-product space:

| tex label | result |
|---|---|
| `lem:sh` | a half-plane clip is exactly intersection: subset + measure-monotone ("every clip only removes material") |
| `thm:trim` | the greedy-trim chain equals `P ∩ ⋂ Hₜ`, stays inside `P`, never gains measure, and is convex for convex input |
| `lem:clip3d` | subsumed — the formalization is dimension-generic |
| `thm:kahn` | source-existence half: a finite acyclic precedence order always offers a next installable part (Kahn's loop never sticks) |

`FrahanProofs/Roadmap.lean` holds the exact-statement `proof_wanted`
queue and the full tier map for the remaining ~29 results.

## Build

```
cd frahan_proofs
lake exe cache get   # fetch Mathlib olean cache (first time)
lake build
```

Toolchain is pinned by `lean-toolchain`; Mathlib is pinned in
`lakefile.toml` / `lake-manifest.json`.
