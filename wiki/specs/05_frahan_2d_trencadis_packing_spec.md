# 05 - Frahan 2D Trencadis Packing Spec

**Spec version:** 0.1
**Sources:** `frahan/frahan_boundary_aware_packing_consolidated_research.md`,
the `Frahan.StonePack.GH.TwoD.*` live solver tree, and runbook § 16.1.

## 1. Goal

Pack irregular fragments inside one or more irregular 2D sheet
boundaries so that:

- placements are collision-free at a configurable tolerance;
- placements respect zero or more interior holes per sheet;
- the boundary's local rail (curvature + edge length + edge angle +
  zone) is preferred when matching fragment edges;
- residual voids are reported, not hidden;
- the result is a fabrication-ready `PackingResult` with placements,
  failures, yield, and diagnostics.

## 2. Computational grammar (runbook § 15.2 instantiation)

```
Input curves (sheet outline + holes + parts)
  → validate (closed, planar, non-degenerate)
  → simplify (adaptive ToPolyline; 2° / chord-height; ≤ 256 verts)
  → extract descriptors (boundary rail index, fragment edge buckets)
  → match compatible features (zone-aware lookup, B1 caveat)
  → generate candidates (rotated and reflected variants)
  → validate with original curve (PointInPoly + MinDistPolys)
  → refine or rank (yield, edge-affinity, residual-void score)
  → suggest trims (2D trim curves at fragment boundaries)
  → report fabrication metrics (yield, perimeter, kerf, residual voids)
```

## 3. Live implementation today

| Pipeline stage | Live class / file |
| --- | --- |
| input projection + simplification | `IrregularSheetFillV506.ToPolyline` |
| sheet polygon storage | `IrregularSheetFillV506.SheetData` (private nested) |
| part polygon storage | `IrregularSheetFillV506.PartData` (private nested) |
| candidate grid generation | `IrregularSheetFillV506.Grid2d` (private nested) |
| containment test | `IrregularSheetFillV506.ContainedInSheet` |
| collision test | `MinDistPolys` (private static) |
| placement loop | `IrregularSheetFillV506.Solve(...)` |
| GH wrapper | `Pack2DIrregularSheetV506Component` |
| 2D enums | `Frahan.StonePack.GH.TwoD.PackingSortMode`, `PackingCornerMode` |
| 2D result DTO | `Frahan.StonePack.GH.TwoD.PackingResult` |

## 4. Boundary rail index (proposed)

The research consolidated boundary-rail concept builds an index
keyed by `EdgeKey(LengthBucket, AngleBucket, CurvatureBucket,
ZoneBucket)`. Lookups use a `BoundaryIntervalInfo` with start/end
parameters, normal, and a sliding-window tag. Neighbour query widens
the search by ±N buckets in length and angle, and (when
`preserveZone == false`) widens across all zones.

**Bug B1 reminder.** The current research-snippet implementation of
`QueryNeighbors` returns `key.ZoneBucket` on both ternary arms, so
`preserveZone` has no effect. The Frahan implementation must replace
the ternary with a sentinel "any-zone" value, or restructure the
index to widen via a separate code path. See
`docs/future_work/frahan_code_bug_register.md` entry **B1**.

## 5. Acceptance contract

A valid 2D Trencadis solver run returns:

- `PackingResult.Placements` - list of `(partId, sheetId, transform,
  rotation_deg, mirrored)`.
- `PackingResult.Failures` - list of `(partId, reason)`.
- `PackingResult.Yield` - `total_packed_area / total_sheet_area`.
- `PackingResult.PerimeterUsed` - total perimeter in mm.
- `PackingResult.ResidualVoids` - list of polygons.
- `PackingResult.EdgeAffinityScore` - sum of matched edge-bucket
  affinities (proposed).

## 6. Validation rules

- Sheet must be a closed planar curve at tolerance ≤ 1e-3.
- Each hole must be a closed planar curve fully inside the sheet
  outline.
- Each part must be a closed planar curve at tolerance ≤ 1e-3.
- `MinDistance` ≥ 0; default 0 mm.
- Cancellation token honoured at every outer loop iteration.

## 7. Failure modes

- **Non-planar input** → component error, no output.
- **Degenerate part** (zero area, < 3 vertices after polyline
  conversion) → individual failure recorded, other parts continue.
- **Cancelled token** → partial result returned with current
  placements and a `Cancelled` flag.
- **No feasible candidate after N iterations** → part recorded as
  failure with reason `no_feasible_candidate`.

## 8. Performance targets

- 100 parts into 1 sheet, 5 rotations each: ≤ 5 s on a 4-core CPU.
- 1,000 parts into 1 sheet, 1 rotation each: ≤ 60 s on a 4-core CPU.
- Memory ≤ 200 MB for 1,000 parts.
- GH UI never blocked > 100 ms (every long path goes through
  `GH_TaskCapableComponent`).

## 9. Tests required

- Unit: `IrregularSheetFillV506` returns correct yield on a 10-part
  fixture with known optimum.
- Unit: `MinDistPolys` returns correct minimum distance on three
  hand-checked polygon pairs.
- Integration: open `outputs/.../frahan_stonepack/share/.../*.gh`
  fixtures in Rhino 8 and confirm component executes without error.
- Regression: B1 fix verified by feeding a multi-zone boundary and
  confirming `preserveZone == false` widens the lookup correctly.

## 10. Out of scope for v1

- Trim-suggestion generation (proposed for v2).
- Fragment edge-affinity scoring (proposed for v2).
- ML-guided ordering (covered in `12_frahan_learning_guided_packing_spec.md`).
