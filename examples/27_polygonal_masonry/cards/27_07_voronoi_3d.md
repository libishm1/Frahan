# Card 07 — 3D Voronoi wall (paper Fig. 15)

## Component

`Frahan > Masonry > Polygonal Masonry Sequence 3D`

## Fixture

**Easiest**: open `07_voronoi_3d.gh`. The 50 cell meshes are already
internalised and the 3D component is pre-wired. Hit recompute.

**From scratch**: open `07_voronoi_3d.3dm`. Layers:
- **Cells_3D** (green): 50 closed meshes, one per Voronoi
  cell. Select all and wire to the `Cells` input.
- **Reference_Text** (grey): seed points for reference only; do
  NOT wire these into the component.
- **Wall_Boundary** (black): a single rectangle on the floor of
  the bbox for visual orientation.

3D bbox = (0.0, 0.0, 0.0, 10.0, 10.0, 6.0)

## Expected

- `Cell Count` output: **50** stones.
- `Install Order` output: integers 1..50, gradient bottom
  (low z) to top (high z).
- `Depth` output: max depth observed in the Python pipeline ~ 9.
- `DAG Edges` output: line segments from lower-Z cell centroid to
  higher-Z cell centroid.
- No runtime errors.

## Reference (Python pipeline)

![reference](./07_voronoi_3d.png)


## Pass / fail

```
Date: ____________
Verdict: PASS / FAIL
Notes:
```

## Notes

Fifty 3D Voronoi cells inside a 10 x 10 x 6 box. Each polyhedral cell is one stone. The 3D component auto-detects shared-face adjacency and orders by z. The Cell Count output should match the bounded cell count printed above.
