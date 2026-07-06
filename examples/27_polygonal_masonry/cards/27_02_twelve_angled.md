# Card 02 — Twelve-angled stone wall (paper Figs. 5-6)

## Component

`Frahan > Masonry > Polygonal Masonry Sequence (2D)`

## Fixture

**Easiest**: open `02_twelve_angled.gh`. Geometry is already
internalised and the component is pre-wired. Hit recompute.

**From scratch**: open `02_twelve_angled.3dm`. Layers:
- **Wall_Boundary** (black): one rectangle, wire to `Wall` input.
- **Chains** (red): 4 polyline(s), wire all to `Chains` input.

bbox = (0.0, 0.0, 10.0, 8.0)

## Expected

- `Region Count` output ≈ **5** finite stones (the
  two infinite top/bottom bands are included in the `Stones` output
  but do not count as finite stones).
- `Install Order` output: integers 1..n, with 1 at the bottom of
  the wall and n at the top.
- `DAG Edges` output: line segments that point from lower-Z to
  higher-Z stone centroids (in 2D, Y plays the role of Z).
- No runtime errors on the component.

## Reference (Python pipeline)

![reference](../27_02_twelve_angled.png)


## Pass / fail

```
Date: ____________
Verdict: PASS / FAIL
Notes:
```

## Notes

Four chains. The 4th chain (top boundary of the twelve-angled stone) shares endpoints with the middle chain at (3, 4.5) and (7, 4.5). The twelve-angled stone is the small region enclosed between those endpoints.
