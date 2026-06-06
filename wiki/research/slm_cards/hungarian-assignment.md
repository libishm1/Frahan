---
slug: hungarian-assignment
title: Hungarian / Kuhn-Munkres Minimum-Cost Bipartite Assignment
tier: 1
schema: RESEARCH_WORKFLOW_V3 SS6
algorithm_class: combinatorial optimization (assignment problem)
core_method_class: Kuhn 1955 Hungarian Method / Munkres 1957; shortest-augmenting-path dual-potential form (Jonker-Volgenant 1987; rectangular extension cf. Bourgeois-Lassalle 1971)
fabrication_direction: top_down
geometry_type: none-direct (solves over a precomputed scalar cost matrix; geometry enters only via upstream OBB extents and volumes)
big_o_time: O(n^3), n = max(rows, cols)
big_o_space: O(n^2)
parallel_model: serial
verdict: evolve
gotcha_flags: [T2, T3, M5]
source_files:
  - Frahan.EdgeMatching.Core/HungarianAssigner.cs:48-132   # canonical rectangular solver, double[] row-major
  - Frahan.StonePack.GH/TwoD/HungarianAssignment.cs:23-92  # divergent square-only copy, double[,]
  - Frahan.StonePack.GH/Voussoir/VoussoirStoneMatcherComponent.cs:227-315  # cost-matrix construction (3D top-down)
  - Frahan.StonePack.GH/EdgeMatch3D/TemplateBlockMatch3DComponent.cs:137-147  # volume-proxy cost (stub)
  - tests/Frahan.StonePack.Tests/MatcherRegistryTests.cs:29-177  # HungarianAssignerTests
citation: H.W. Kuhn, "The Hungarian Method for the Assignment Problem," Naval Research Logistics Quarterly 2 (1955) 83-97. Method-class origin only; equations below derived from the Frahan code.
---

# Hungarian / Kuhn-Munkres Assignment (Frahan SLM card)

## What the code actually does

`HungarianAssigner.Solve(double[] cost, int rows, int cols)`
(`HungarianAssigner.cs:48`) takes a row-major cost matrix
`cost[i*cols + j] = c(i,j)`, pads it to a square `n x n` matrix with
`n = max(rows,cols)` filling absent cells with the sentinel
`Infeasible = 1e18` (`HungarianAssigner.cs:56-66`), then runs a single
shortest-augmenting-path Hungarian with row/column dual potentials
`u`, `v`. Output `result[i]` is the column assigned to row i, or
`Unassigned = -1` when row i lands on a padded/infeasible cell
(`HungarianAssigner.cs:122-131`).

It is used as the SAME engine for three top-down "fit a form to
available stock" tasks: 2D Trencadis catalogue matching, 3D template
block match (`TemplateBlockMatch3DComponent.cs:147`), and voussoir to
quarry-stone assignment (`VoussoirStoneMatcherComponent.cs:308`).

A second, divergent copy `HungarianAssignment.Solve(double[,])`
(`TwoD/HungarianAssignment.cs:23`) implements the same recurrence on a
square `double[,]`, but uses `double.PositiveInfinity` for `minv`
(line 45) and returns an all `-1` array instead of throwing when no
augmenting path is found (line 60). Behavioural drift between the two
is real.

## Derived equations (from the code, not from memory)

Let the padded square cost be `C in R^{n x n}`, with
`C[i][j] = cost[i*cols+j]` for `i<rows, j<cols` and `C[i][j] = S`
(S = 1e18) otherwise (`HungarianAssigner.cs:65`).

The solver maintains dual potentials `u_i` (rows) and `v_j` (cols).
The reduced cost evaluated in the inner loop (`HungarianAssigner.cs:90`)
is exactly:

$$ \bar{c}(i,j) = C[i][j] - u_i - v_j. $$

It seeks a perfect matching minimizing total cost subject to the
complementary-slackness / dual-feasibility invariant the loop
maintains:

$$ \min_{\sigma \in S_n} \sum_{i=1}^{n} C[i][\sigma(i)]
   \quad\text{s.t.}\quad u_i + v_j \le C[i][j]\ \forall i,j,\ \
   u_i + v_{\sigma(i)} = C[i][\sigma(i)]. $$

Each outer iteration i (`HungarianAssigner.cs:76`) grows an alternating
tree from a free row. `minv[j]` holds the smallest reduced cost from
the current tree to unscanned column j (`HungarianAssigner.cs:91-95`):

$$ \mathrm{minv}[j] = \min_{i_0 \in T}\big(C[i_0][j] - u_{i_0} - v_j\big). $$

The phase step `delta` is the tree-expansion slack
(`HungarianAssigner.cs:96-100`):

$$ \delta = \min_{j \notin \text{used}} \mathrm{minv}[j]. $$

Potential update preserving dual feasibility
(`HungarianAssigner.cs:105-109`):

$$ u_{p[j]} \mathrel{+}= \delta\ (j\in\text{used}),\quad
   v_j \mathrel{-}= \delta\ (j\in\text{used}),\quad
   \mathrm{minv}[j] \mathrel{-}= \delta\ (j\notin\text{used}). $$

Augmentation walks the `way[]` predecessors to flip the matching
(`HungarianAssigner.cs:113-118`), giving the standard alternating-path
update `p[j0] = p[way[j0]]`.

### Cost functions the matrix actually carries

Voussoir matcher (`VoussoirStoneMatcherComponent.cs:283-285`),
with yield `y = V_voussoir / V_stone`:

$$ C[i][j] = w_y\,(1 - y) + w_c\,\frac{V_{stone} - V_{voussoir}}
   {\max(V_{voussoir},\,10^{-9})}, $$

with hard gates writing `S = 1e18` when the stone OBB does not contain
the voussoir OBB+margin (`:270-274`) or when `y < minYield` (`:277-279`).

3D template stub (`TemplateBlockMatch3DComponent.cs:145`):

$$ C[i][j] = \frac{|V_{cell} - V_{stone}|}{\max(V_{cell},\,1)}. $$

Both cost forms are dimensionless ratios in roughly O(1)-O(10); the
sentinel S = 1e18 is 17-18 orders of magnitude larger. That gap is the
headline numeric risk below.

## Code sketch / reuse seam

Reuse seam is the single static entry
`int[] HungarianAssigner.Solve(double[] cost, int rows, int cols)`.
It is pure-managed, net48-safe, allocation-bounded, no RhinoCommon
dependency, no `Contains(StringComparison)` / `HashCode.Combine`.
Callers build a `double[M*N]`, write `HungarianAssigner.Infeasible`
into forbidden cells, and read back per-row column indices. This is a
clean, well-tested seam (`MatcherRegistryTests.cs:29-177` covers
empty / 1x1 / identity / anti-diagonal / 2x4 / 4x2 / all-infeasible /
partial-feasibility / null / bad-dims / determinism).

## Gotchas (G/M/N/P/T instrument)

| Code | Verdict | Reason (grounded in file:line) |
|------|---------|--------------------------------|
| G1 exact/adaptive predicates | na | No geometric predicates; pure scalar comparisons on `double` reduced costs (`HungarianAssigner.cs:90-100`). |
| G2 degeneracy | na | No collinear/coincident geometry inside the solver; ties between equal reduced costs resolve deterministically by scan order (`:96-100`). |
| G3 predicate/construction split | na | No construction of geometry. |
| G4 robust boolean kernel | na | No boolean ops. |
| M1 manifold output | na | No mesh produced; output is an int[] assignment. |
| M2 watertight/Euler | na | No mesh. |
| M3 winding/self-intersect | na | No mesh. |
| M4 bounded decimation error | na | No remesh. |
| M5 data-structure hidden cost | FLAG | Square padding allocates `n*n` doubles even for very rectangular inputs (`HungarianAssigner.cs:56-66`); M=2000 cells x N=50 stones allocates 4M doubles (~32MB) vs the ~100k truly needed. Hidden O(max(M,N)^2) memory and O(max^3) time independent of the small side. |
| N1-N6 nurbs | na | No NURBS/spline anywhere in the solver. |
| P1 deterministic reductions | na (pass-by-design) | Fully serial; output is deterministic and a test asserts it (`MatcherRegistryTests.cs:169-170`). |
| P2 thread safety | na | Static method, no shared mutable state across calls; all working arrays are call-local (`HungarianAssigner.cs:61-74`). |
| P3 GPU pattern | na | CPU only. |
| P4 load balance | na | Serial. |
| P5 Amdahl serial tail | na | Whole algorithm is the serial tail; not currently parallel. |
| T1 recenter before far-from-origin | pass | Solver consumes precomputed scalar costs, not coordinates; recentering is an upstream OBB/volume concern (`VoussoirStoneMatcherComponent.cs:243-247`), not this code's job. |
| T2 absolute vs scale-relative epsilon | FLAG | The infeasibility marker is a hard-coded absolute `1e18` (`HungarianAssigner.cs:34`), not scaled to the feasible cost range. Tested only against costs of O(1) (`MatcherRegistryTests.cs:131-140`); no relative-epsilon comparison exists. |
| T3 float32 vs float64 at scale | FLAG | All math is float64, good, BUT 1e18 sentinels are summed into `u`/`v` potentials (`:90,107`). Once a potential reaches ~1e18, real costs ~O(1) sit below the float64 relative ulp (2.2e-16 * 1e18 ~= 220) and are lost; with many infeasible cells the optimum among feasible cells can be mis-ranked. |
| T4 units declared+consistent | pass | Costs are explicitly dimensionless ratios (yield 1-y, normalized volume diff) (`VoussoirStoneMatcherComponent.cs:283-285`, `TemplateBlockMatch3DComponent.cs:145`); units cancel by construction. |
| T5 tolerance-system reconciliation | na | No Rhino doc tolerance, Clipper scale, or model-unit interaction inside the solver; it never touches the 3 Frahan tolerance systems. |
| T6 integer-scaling overflow (Clipper2 int64) | pass | No integer scaling, no Clipper2, no int64 coordinates; all costs are double (`HungarianAssigner.cs:48`). The standing Frahan int64 flag does not apply here. |
| T7 snap-rounding/near-degenerate | na | No coordinate snapping. |

Standing Frahan flags checked: NFP cache-key -- na (no NFP);
Clipper2 int64 overflow -- pass (no Clipper); transform composition
drift -- na (no transforms in solver); 3-tolerance-system confusion --
na (solver is tolerance-free, consumes pre-built costs).

## Numeric stress findings

- Coordinate magnitude: solver sees only scalar costs (O(1)-O(10) in
  both real cost builders), so it is itself coordinate-agnostic.
  Architectural-scale OBB extents and volumes are reduced to ratios
  upstream (`VoussoirStoneMatcherComponent.cs:283-285`) before they
  reach the matrix, which is the right design.
- Epsilon kind: the only epsilon-like constant is the absolute
  sentinel `Infeasible = 1e18` and a `max(.,1e-9)` divide guard
  (`VoussoirStoneMatcherComponent.cs:284`). No scale-relative tolerance.
- Sentinel arithmetic risk (the real finding): the augmenting loop
  computes reduced costs against and accumulates deltas drawn from
  1e18 cells (`HungarianAssigner.cs:90,96-100,107`). float64 holds 1e18
  exactly enough for comparison, but mixing 1e18 with O(1) feasible
  costs in the same `u`/`v`/`minv` arithmetic risks absorbing the
  meaningful O(1) differences. Safe today only because the matcher
  early-exits when `feasibleCount == 0`
  (`VoussoirStoneMatcherComponent.cs:295-302`) and Frahan-scale
  matrices stay small; not safe as a general property.
- Overflow: 1e18 + 1e18 = 2e18 is far below double max (~1.8e308), so
  no overflow to Inf in practice; the danger is precision loss, not
  overflow.
- Units: dimensionless throughout; consistent. No unit defect found.

## Verdict: EVOLVE

The solver is correct on the tested feasible regime, deterministic,
net48-clean, dependency-free, and well covered by unit + end-to-end
tests (`MatcherRegistryTests.cs:29-177`,
`TrencadisFillTests.cs:288-339`). It is worth keeping, not rewriting.
But three concrete issues keep it from "reuse": (1) the absolute 1e18
sentinel summed into potentials (T2/T3) is a latent correctness trap on
dense-infeasible inputs; (2) square padding wastes O(max^2) memory and
time on rectangular real inputs (M5); (3) two divergent copies of the
same algorithm with different no-path behaviour create drift risk.

## Evolution plan (each lever tied to a measured flag)

PERFORMANCE / memory (M5 flag, `HungarianAssigner.cs:56-66`):
adopt a rectangular-native LAPJVsp so a 200x10 or 2000x50 problem uses
O(M*N) storage and O(min(M,N) * M*N) time instead of square
O(max^2)/O(max^3). Bottleneck: 4M-double pad for M=2000,N=50.

ACCURACY (T2/T3 flags, `HungarianAssigner.cs:34,90,107`): never sum the
sentinel into duals. Either (a) solve on the feasible subgraph only
and mark dropped rows Unassigned post-hoc, or (b) set the sentinel to
`maxFeasibleCost * (n + 1)` computed per call so it dominates any
feasible matching without exceeding it by 18 orders of magnitude.
Add a regression test with O(1) feasible costs surrounded by sentinel
cells and assert the optimum among feasible cells is exact.

SPEED (M5, inner scan `HungarianAssigner.cs:87-101`): the per-augment
delta search is a linear O(n) scan with no priority structure. Real
voussoir matrices are mostly Infeasible (gates at
`VoussoirStoneMatcherComponent.cs:240-279`), i.e. sparse-feasible.
Replace the linear minv scan with a d-ary heap keyed on `minv[j]` to
get O(E log V) on sparse inputs, plus skip rows that are entirely
sentinel before entering the augment loop.

MAINTAINABILITY: delete `TwoD/HungarianAssignment.cs:23-92` and route
2D Trencadis through the canonical `HungarianAssigner.Solve`, removing
the PositiveInfinity-vs-MaxValue and return-vs-throw divergence
(`HungarianAssignment.cs:45,60` vs `HungarianAssigner.cs:80,103`).