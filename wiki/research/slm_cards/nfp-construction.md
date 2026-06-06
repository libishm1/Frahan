---
slug: nfp-construction
algorithm: No-Fit-Polygon / Inner-Fit-Polygon construction via Minkowski difference + Bottom-Left-Fill irregular nesting
fabrication_direction: bottom_up
geometry_type: 2D closed planar polygons (parts, sheet outline, holes), polyline-discretised from RhinoCommon Curves
core_method_class: Burke-Hellier-Kendall-Whitwell 2006 BLF (DOI 10.1287/opre.1060.0293); Bennell-Oliveira 2009 NFP/IFP-via-Minkowski (DOI 10.1057/jors.2008.169); Clipper2 (A. Johnson, BSL-1.0)
big_o_time: convex NFP O(|A||B| log(|A||B|)); concave NFP O(T_A*T_B) capped 2500 pairs; legacy pack O(S*n*r*p*(c+p)); Clipper nester O(n*r*(h+p)*N log N)
big_o_space: O(distinct-pairs * NFP-verts) cached (legacy) / no cache (Clipper); transient O(|A||B|) per Minkowski; vertices capped 128 (NfpRhino) / 200 (NfpBlf)
parallel_model: serial (solver); GH wrapper offloads to one Task.Run, no internal parallelism
gotcha_flags: [G1, G2, G4, T2, T6, T5, M5]
verdict: evolve
source_files:
  - NfpRhino.cs:15-91 (ctor, convex/concave branch)
  - NfpRhino.cs:153-219 (Minkowski diff hull + concave triangulated regions)
  - NfpRhino.cs:438-545 (Andrew hull, IsConvex, SignedArea)
  - NfpCache.cs:8-33 (identity cache key)
  - NfpBottomLeftFillRhino.cs:152-215 (FindBestPlacement)
  - NfpBottomLeftFillRhino.cs:295-372 (feasible space = IFP - union NFP)
  - NfpBottomLeftFillRhino.cs:552-574 (RotatedPart.GeometryKey = index:angle)
  - IrregularSheetFillNfpBlf.cs:37-238 (Clipper2 exact NFP-BLF sibling)
  - Clipper2Adapter.cs:148-199 (MinkowskiSum, boolean, inflate; PathsD double API)
---

## Derived equations (reconstructed from the code, not from memory)

No-fit polygon, translation form. A is stationary, B sliding. The code at NfpRhino.cs:153-165 builds the point set `{ a - b : a in A, b in B }` then takes its convex hull. For convex A,B that hull is exactly the Minkowski difference, i.e. the locus of B reference points p for which B(p) just touches A:

$$ \mathrm{NFP}(A,B) \;=\; A \ominus B \;=\; \{\, a - b : a \in A,\; b \in B \,\} \;=\; A \oplus (-B). $$

B placed at reference point p overlaps A iff p lies in the interior of NFP(A,B); B touches A iff p is on the boundary (used as the placement-feasibility test at NfpBottomLeftFillRhino.cs:464-488 with the local shift `p - origin_A`, encoding translation invariance: the NFP is built once at the origin and translated by the placed part origin at line 324).

Inner-fit polygon. Legacy solver (NfpBottomLeftFillRhino.cs:374-394) treats the sheet as an axis-aligned rectangle W x L and the part by its width/height bbox, so

$$ \mathrm{IFP}_{rect} = [0,\,L - w_B] \times [0,\,W - h_B], $$

a conservative box (it uses the rotated bbox extents, exact only for axis-aligned parts). The Clipper sibling computes the true IFP by eroding the outer loop by every hull vertex of B (IrregularSheetFillNfpBlf.cs:225-238):

$$ \mathrm{IFP}(\partial, B) \;=\; \bigcap_{v \in \mathrm{hull}(B)} (\partial - v), $$

exact for convex B, a safe (under-approximate) inner region otherwise.

Feasible region. Both solvers form

$$ \mathrm{feasible}(B) \;=\; \mathrm{IFP}(\partial,B) \;\setminus\; \Big( \bigcup_k \mathrm{NFP}(A_k,B) \;\cup\; \bigcup_j \mathrm{NFP}(H_j,B) \Big), $$

A_k placed parts, H_j holes (NfpBottomLeftFillRhino.cs:341-372 via RhinoCommon CreateBooleanDifference; IrregularSheetFillNfpBlf.cs:189-201 via Clipper2 DifferenceLoops). Spacing g is applied by inflating the blocked union by +g and eroding the IFP by -g (Clipper InflateLoops, IrregularSheetFillNfpBlf.cs:166,197):

$$ \mathrm{feasible}_g(B) = \mathrm{erode}_g(\mathrm{IFP}) \setminus \mathrm{inflate}_g\!\Big(\bigcup \mathrm{NFP}\Big). $$

Placement objective (Bottom-Left-Fill). Pick the reference point minimising (y, then x):

$$ p^\* = \arg\min_{p \in \mathrm{feasible}(B)} \; (\,p_y,\; p_x\,), $$

evaluated over a candidate vertex set in the legacy path (corner + placed-bbox + NFP-vertex candidates, NfpBottomLeftFillRhino.cs:217-282) and over the exact feasible-region vertices in the Clipper path (BottomLeftVertex, IrregularSheetFillNfpBlf.cs:249-256).

Signed area / orientation (degeneracy + winding control), NfpRhino.cs:526-542:
$$ 2\,\mathcal{A}(P) = \sum_{i} (x_i y_{i+1} - x_{i+1} y_i),\qquad \mathcal{A} < 0 \Rightarrow \text{reverse to CCW}. $$

Convexity test, NfpRhino.cs:486-516: all consecutive edge cross-products share one sign,
$$ \mathrm{sign}\big((b-a)\times(c-b)\big)\ \text{constant}\ \forall\ \text{triples}. $$

Transform composition (placement emission), IrregularSheetFillNfpBlf.cs:262-275:
$$ T = T_{work\to sheet}\cdot T_{move}\cdot T_{unnorm}\cdot R(\theta)\cdot T_{normalize}\cdot T_{plane\to work}, $$
applied in that order to the source curve; this is the documented root-cause-fixed order, mirroring the V506 nester.

## Code sketch / reuse seam

Two parallel implementations exist; they do NOT share an NFP kernel.

1. Legacy RhinoCommon path: `NfpRhino` (geometry kernel) -> `NfpCache` (memo) -> `NfpBottomLeftFillRhino` (search). Home-rolled hull (Andrew monotone chain, NfpRhino.cs:438-484), ear-clip triangulation (NfpRhino.cs:261-318), RhinoCommon `Curve.CreateBooleanUnion/Difference/Intersection` for region algebra and collision. Reuse seam = `NfpRhino.CurveToPolygon` (static, NfpRhino.cs:124) and the cache `GetOrCreate(stationaryKey, stationary, slidingKey, sliding, tol, iters, rectShortcut)` (NfpCache.cs:13).

2. Exact Clipper path: `IrregularSheetFillNfpBlf` builds NFP/IFP entirely through `Clipper2Adapter.MinkowskiSum / IntersectLoops / UnionLoops / DifferenceLoops / InflateLoops` (Clipper2Adapter.cs:148-199). Reuse seam = `Clipper2Adapter` tuple-only API (no Clipper2Lib types leak; the GH project does not reference Clipper2Lib). This is the better seam for new work.

Both feed the shared `PackingResult` (Packing2DModels.cs) and are exposed as GH `GH_TaskCapableComponent<PackingResult>` (IrregularSheetFillNfpBlfComponent.cs).

## Gotchas (G/M/N/P/T)

| Code | Verdict | Reason (file:line) |
|------|---------|--------------------|
| G1 exact/adaptive predicates | flag | All orientation/containment via float cross-product against RhinoMath.ZeroTolerance, no exact/adaptive predicates (NfpRhino.cs:291,360,429,461,500). |
| G2 degeneracy handled | flag | Zero-area + <3-vertex guarded (NfpRhino.cs:25-36); collinear removed (407-436); BUT concave NFP collapses to convex hull when triangle-pairs >2500 or union throws (179-181, 202-216), over-blocking placement silently. |
| G3 predicate/construction separation | pass | Hull/area/convexity are read-only predicates; Minkowski/boolean are separate construction calls; no mixing (NfpRhino.cs:153-259). |
| G4 robust boolean kernel vs home-rolled float | flag | Legacy path uses home-rolled float Minkowski+hull and RhinoCommon booleans wrapped in bare try/catch that swallow to empty/fallback (NfpRhino.cs:202-218; NfpBottomLeftFillRhino.cs:342-371). Clipper sibling DOES use a robust kernel (Clipper2Adapter) -- mixed maturity. |
| M1-M4 mesh | na | Pure 2D curve/polygon problem, no mesh output. |
| M5 data-structure hidden cost | flag | NfpBottomLeftFillRhino re-extracts polylines and recomputes bboxes per candidate (GetCurveVertices, ToPolyline at 885-907; GetBoundingBox in collision loop 758-788) -> hidden O(p) RhinoCommon work inside the inner candidate loop. |
| N1-N6 nurbs/spline | na | Curves are discretised to polylines before any NFP math (NfpRhino.cs:124-151; IrregularSheetFillNfpBlf.cs:376-418); no spline math survives into the kernel. |
| P1-P5 parallel | na | Solver is fully serial; the only concurrency is the GH wrapper's single Task.Run offload (IrregularSheetFillNfpBlfComponent.cs:124-129), no internal threading or reductions. |
| T1 recenter far-from-origin | flag | No recentre; parts normalised to their own bbox-min (good) but the SHEET keeps world coordinates and is scaled x1000 in place (IrregularSheetFillNfpBlf.cs:294-325,420-425) -> far-from-origin sheets lose precision. |
| T2 absolute vs scale-relative epsilon | flag | Mix of absolute RhinoMath.ZeroTolerance, user _tol, and hardcoded 1e-8 (NfpRhino.cs:429) / 1e-6 (NfpBottomLeftFillRhino.cs:277); none scale with model size. |
| T3 float32 vs float64 | pass | All double (Point2d, PathsD double API, tuples); no float32 at architectural scale. |
| T4 units declared+consistent | flag | Tolerances and spacing are unitless numbers; no read of RhinoDoc.ModelAbsoluteTolerance / unit system anywhere in the kernel. |
| T5 tolerance-system count reconciled | flag | Three unreconciled systems: RhinoMath.ZeroTolerance (degeneracy), user _tol (discretisation+containment), implicit Clipper 1/Scale snap. Never tied together. |
| T6 integer-scaling overflow (Clipper int64) | flag | IrregularSheetFillNfpBlf scales x1000 (line 40) to dodge the named Clipper int64 overflow, BUT Clipper2Adapter uses the DOUBLE PathsD API (Clipper2Adapter.cs:113-117), which snaps to int64 internally at the scaled magnitude -- the guard is partial; metre-scale blocks far from origin can still under-resolve. |
| T7 snap-rounding / near-degenerate defined | flag | BottomLeftVertex uses exact == on y and x with no epsilon (IrregularSheetFillNfpBlf.cs:254); near-tie vertices chosen by raw float equality. |

## Numeric stress findings

- Coordinate magnitude: parts are normalised to bbox-min at origin (good, T1-friendly for parts) but sheets/holes stay in world coordinates and are multiplied by Scale=1000 (IrregularSheetFillNfpBlf.cs:294-325). A sheet placed at e.g. (50000, 20000) mm becomes 5e7 scaled units; Clipper2's internal int64 snap then operates at that magnitude, eroding sub-millimetre fidelity. No recentre is performed.
- Epsilon kind: heterogeneous. Degeneracy/orientation use absolute RhinoMath.ZeroTolerance (~1e-12); discretisation and containment use user _tol (default 0.01); literals 1e-8 (NfpRhino.cs:429) and 1e-6 (NfpBottomLeftFillRhino.cs:277) appear. None is scale-relative, so behaviour drifts with model size.
- Units: undeclared. _spacing, _tol, sheet dims are bare doubles; the kernel never consults RhinoDoc units. A model in metres vs millimetres silently changes degeneracy outcomes.
- Overflow: the Clipper int64 risk (T6) is only partially mitigated. Scale=1000 raises precision near origin but, combined with double->int64 snapping inside Clipper2 and unbounded world coordinates, large-coordinate inputs remain exposed. No bbox-size check or adaptive scale exists.
- Cache key (named Frahan standing flag): the legacy key is `index:angle | index:angle | tol:R | iters` (NfpCache.cs:22 + NfpBottomLeftFillRhino.cs:572). It is identity-based, NOT geometry-value based. Because NFP is translation-invariant and the build is per (shape-identity, rotation), this is correct WITHIN one pack run (no wrong-hits). The defect is missed dedup: K congruent parts with distinct source indices build and store K identical NFPs. There is also a latent stale-hit risk if a part's geometry were mutated under a reused index, but the current call sites do not do that. The Clipper sibling has NO cache and rebuilds every NFP per attempt.

## Evolution plan

PERFORMANCE
1. Remove the redundant post-hoc collision pass in the legacy solver: CollidesWithAny + HasInteriorOverlap (NfpBottomLeftFillRhino.cs:758-832) re-checks overlap with RhinoCommon CurveCurve and CreateBooleanIntersection even though IsInsideBlockedNfp (464-488) already guarantees non-overlap from the NFP. This double-check is the slowest inner loop (O(p) boolean ops per candidate). Drop it, or replace the RhinoCommon kernel with the Clipper2 membership test already proven in the sibling.
2. Make the cache value-based (the named cache-key gotcha): key on a quantised geometry signature (rounded vertex hash of the normalised polygon + angle + tol) instead of source-index (NfpCache.cs:22, NfpBottomLeftFillRhino.cs:572) so congruent parts share one NFP. Today it is a missed-hit (wasted build + memory), not a wrong-hit.

ACCURACY
3. Route the legacy NfpRhino Minkowski difference through Clipper2Adapter.MinkowskiSum (Clipper2Adapter.cs:154) so concave NFPs are exact, and delete the convex-hull fallback (NfpRhino.cs:179-181,202-218) that silently loses concavity and over-blocks placement (G4/G2).
4. Fix the scale/overflow flags (T2/T6/T1): make Scale adaptive to model bbox and read RhinoDoc unit (T4), recentre the sheet to its own bbox before scaling (IrregularSheetFillNfpBlf.cs:294-325), and replace the exact == in BottomLeftVertex (line 254) with a tolerance-aware (y,x) compare (T7).

SPEED
5. Parallelise the per-rotation feasible-region build in FindBestPlacement over _rotDeg (IrregularSheetFillNfpBlf.cs:158-221) with a deterministic reduction (gather all rotation candidates, then pick min(y,x) single-threaded) to preserve determinism (P1) while cutting wall-clock by about |rotations|. The GH wrapper already runs the solver off the UI thread (IrregularSheetFillNfpBlfComponent.cs:124-129), so this only adds intra-solver parallelism.

TOLERANCE
6. Reconcile the three tolerance systems (T5): derive degeneracy (RhinoMath.ZeroTolerance uses), discretisation chord error (_tol), and the Clipper snap scale from one budget anchored to RhinoDoc.ModelAbsoluteTolerance, so the degeneracy threshold and the discretisation error cannot disagree.