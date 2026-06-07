# Card 01 — Three-band wall (paper minimal)

## Component

`Frahan > Masonry > Polygonal Masonry Sequence (2D)`

## Fixture

**Easiest**: open `01_three_band_wall.gh`. Geometry is already
internalised and the component is pre-wired. Hit recompute.

**From scratch**: open `01_three_band_wall.3dm`. Layers:
- **Wall_Boundary** (black): one rectangle, wire to `Wall` input.
- **Chains** (red): 2 polyline(s), wire all to `Chains` input.

bbox = (0.0, 0.0, 4.0, 4.0)

## Expected

- `Region Count` output ≈ **1** finite stones (the
  two infinite top/bottom bands are included in the `Stones` output
  but do not count as finite stones).
- `Install Order` output: integers 1..n, with 1 at the bottom of
  the wall and n at the top.
- `DAG Edges` output: line segments that point from lower-Z to
  higher-Z stone centroids (in 2D, Y plays the role of Z).
- No runtime errors on the component.

## Reference (Python pipeline)

_(no reference PNG available)_


## Pass / fail

```
Date: ____________
Verdict: PASS / FAIL
Notes:
```

## Notes

Smallest test. Two horizontal chains divide a 4x4 square into 3 horizontal bands. Install order must go 1 -> 2 -> 3 from bottom to top.
