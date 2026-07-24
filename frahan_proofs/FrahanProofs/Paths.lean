import FrahanProofs.Common

/-!
Frahan StonePack — fewest-edges path optimality (kerf-follow cut count).

tex Theorem `thm:imaiiri`: a breadth-first / fewest-edges shortest path in the
admissibility graph `G` yields the MINIMUM number of ε-admissible straight
segments approximating the chain — so the kerf-follow cut count is OPTIMAL, not
merely greedy. The tex correspondence: a valid ε-approximation is a chain
`0 = i₀ < i₁ < ⋯ < i_m = n−1` with each segment `v_{iₜ}v_{iₜ₊₁}` admissible, i.e.
a WALK of `m` edges in `G` (vertices = chain vertices, edges = admissible
segments); and conversely any `G`-walk is a valid approximation. Hence the
minimum segment count is exactly the graph distance `dist(start, goal)`.

Under that correspondence the theorem is Mathlib's shortest-path facts on the
admissibility graph: a shortest walk of length `dist` EXISTS (BFS finds it), and
EVERY walk (every valid approximation) has length `≥ dist` (none is shorter).
-/

namespace Frahan

open SimpleGraph

/-- tex Theorem `thm:imaiiri`: in the admissibility graph `G` (walks = valid
ε-approximations, walk length = number of straight segments), the minimum
segment count between the endpoints is the graph distance `G.dist s t` —
ACHIEVED by a shortest walk (BFS) and a LOWER BOUND for every approximation.
So the fewest-edges path is optimal, not merely greedy. -/
theorem imaiiri_min_segments {V : Type*} (G : SimpleGraph V) (s t : V)
    (hr : G.Reachable s t) :
    (∃ p : G.Walk s t, p.length = G.dist s t) ∧
      (∀ p : G.Walk s t, G.dist s t ≤ p.length) :=
  ⟨hr.exists_walk_length_eq_dist, fun p => G.dist_le p⟩

/-- tex Theorem `thm:imaiiri`, refinement: the optimal approximation can be taken
to be a SIMPLE path (no repeated vertices) — a shortest walk with length
`G.dist s t` that is a path. (The optimal cut sequence never revisits a chain
vertex.) -/
theorem imaiiri_min_isPath {V : Type*} (G : SimpleGraph V) {s t : V}
    (hr : G.Reachable s t) :
    ∃ p : G.Walk s t, p.IsPath ∧ p.length = G.dist s t :=
  hr.exists_path_of_dist

end Frahan
