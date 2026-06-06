# Frahan StonePack — math derivations (research-level context)

The SLM-tier (algorithm math + code) derivations behind the kept algorithms, so a contributor develops at
the same depth as the maintainer. Style: short sentences, no em dashes. Cross-references the measured
studies in `../wiki/research/packing/`.

## 1. 2D irregular nesting (the exact NFP-BLF engine)
NOTATION. Sheet S (polygon, may be concave / holed), holes H_j, placed parts A_k, candidate part B at pose
x=(theta, t). Work in a x1000 integer-scaled space throughout (Clipper int64-safe).

NO-FIT POLYGON. NFP(A,B) = A (+) (-B) (Minkowski sum of A with the reflected B). Then B@t overlaps A iff t
is in the interior of NFP(A,B). Code: `Clipper2Adapter.MinkowskiSum(A, rot.Refl)` with Refl = {(-x,-y)}.
Exact for convex B; for CONCAVE B a single Minkowski sum can miss a pocket (KB-4) -> a real
polygon-intersection verify rejects the residual overlap.

INNER-FIT POLYGON (containment). IFP(S,B) = { t : B@t subset S } = intersect over b in B of (S - b). For a
CONVEX sheet this equals the intersection over the hull vertices of B (reflex vertices do not bind), so the
shipped hull-vertex IFP is exact. For a CONCAVE sheet the true erosion is S \ (complement(S) (+) (-B)).

FEASIBLE REGION (hard non-overlap by construction). F(B,theta) = (IFP erode_sigma) \ inflate_sigma( union_k
NFP(A_k,B) union union_j NFP(H_j,B) ). Any t in F gives B@t inside S, clear of every part and hole, with
spacing sigma. Non-overlap is a CONSTRAINT, not a penalty.

PLACEMENT. A linear/lexicographic objective over a polygon is minimized at a VERTEX, so bottom-left = the
min (y, x) vertex of F. This is why plain compaction is a NO-OP after bottom-left greedy: each part is
already at the BL-optimal vertex of the region it saw; re-dropping against a superset of obstacles cannot
strictly lower its key. The lever that moves density is ORDER (multi-start): run the greedy over several
sort orders and keep the best by placed-count then tightest used-bbox (monotone keep-best).

GUIDED LOCAL SEARCH (beyond BLF, the SOTA add-on). Place all parts allowing overlap; minimize the penetration
energy Phi = sum_{i<j} w_ij * area(B_i cap B_j) by separation steps (translate a part to the nearest point on
the boundary of the union of NFPs it violates), with Voudouris-Tsang weight updates w_ij += 1 on
argmax a_ij/(1+w_ij), and an outer width-compress loop. Output is emitted only at Phi=0 (0-overlap by
construction). Net48-reimplementable with the shipped Clipper2 primitives; the single-part variant is sound
but cannot open gaps needing whole-layout rearrangement (measured: 0 extra on our fixtures).

## 2. The yield metric (and why cov is wrong)
STOCK UTILIZATION util_stock = ( sum_{i placed} A_i^true ) / ( area(S) - sum_j area(H_j) ). The numerator is
the TRUE input part area (not the x1000-inflated emitted geometry). 80% is the bar for good 2D irregular
packing. INVARIANCE LEMMA: on a saturated fixed-area sheet, any 0-overlap pack of all parts has union =
sum A_i, so cov = union/sheet is pinned and cannot rank packers; util_stock moves only on an
oversubscribed fixture. Always gate a result on overlap == 0 (a higher placed-count with overlaps is
invalid, not better).

## 3. 3D packing (volumetric)
vol_ratio = sum(placed volume) / container volume. DLBF: pieces by revenue-per-volume descending, each at
the lowest (z, y, x) free cell of a voxel grid. Best-of-orientation: try the up-to-6 distinct axis
permutations of each piece, place in the orientation whose best free cell is lowest; volume + revenue are
permutation-invariant. Guillotine (TreePackForest) trades fill for full saw-separability (every block
removable by a straight full-span cut). Domains are NOT cross-comparable (guillotine vs free-stack vs mesh
vs recovery). Honest numerator for meshes is the signed-tetra true volume, not the bbox.

## 4. Block-cut optimization (quarry)
BlockCutOptSolver: max-cover pose search (psi/tilt + dx/dy grid) of a fixed block against a fracture PlyMesh,
via 13-axis SAT for the intact/intersected test. RecoveryCascade: multi-scale -- reject coarse cracked
blocks, re-cut them at finer scales (block -> slab -> tile); reduces to BlockCutOpt at one scale; measured
+21% recovery over single-scale. Jalalian I11 objective: minimise cutting-surface-area / block-value.

## 5. Masonry equilibrium (RBE)
Rigid-block equilibrium: per-block 6-row sum F = 0, sum tau = 0 with non-tension lambda >= 0 and a
linearized Coulomb friction cone. SIGN (KB-3): the builder writes b[F_z] = -m g; the FEASIBLE form is
`BuildPhysicsCorrected` (so f_n >= 0 means compression). The shipped `Build` set equalityRhs = -b making
f_n = -m g infeasible -- fixed by migrating both GH callers to BuildPhysicsCorrected. The verdict is
equilibrium-feasibility; full frictional stability needs the Dykstra non-uniform-H or an IPOPT/active-set
path (a documented limitation).

## 6. Where the measured numbers live
`../wiki/research/packing/`: PACK2D_STUDY_REPORT, PACK3D_STUDY_REPORT, SYNTHESIS_2D/3D, SYNTHESIS_BEYOND_BLF,
ROSES_2D_PACKER_GUIDE, MASONRY_QUARRY_DECISION, pack2d_study_metrics.csv + figures. Every claim there is a
harness/test number; none is project-truth until visually validated in Rhino (truth criterion c).
