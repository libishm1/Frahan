# Card 03 — Generic chain-based wall (paper Fig. 7)

## Component

`Frahan > Masonry > Polygonal Masonry Sequence (2D)`

## Fixture

**Easiest**: open `03_chains_wall.gh`. Geometry is already
internalised and the component is pre-wired. Hit recompute.

**From scratch**: open `03_chains_wall.3dm`. Layers:
- **Wall_Boundary** (black): one rectangle, wire to `Wall` input.
- **Chains** (red): 9 polyline(s), wire all to `Chains` input.

bbox = (0.0, 0.0, 12.0, 6.0)

## Expected

- `Region Count` output ≈ **8** finite stones (the
  two infinite top/bottom bands are included in the `Stones` output
  but do not count as finite stones).
- `Install Order` output: integers 1..n, with 1 at the bottom of
  the wall and n at the top.
- `DAG Edges` output: line segments that point from lower-Z to
  higher-Z stone centroids (in 2D, Y plays the role of Z).
- No runtime errors on the component.

## Reference (Python pipeline)

![reference](../27_03_chains_wall.png)


## Pass / fail

```
Date: ____________
Verdict: PASS / FAIL
Notes:
```

## Notes

Three horizontal chains plus six vertical connectors. The vertical connectors must be purely vertical (x constant) so the component treats them as monotone-y chains.
