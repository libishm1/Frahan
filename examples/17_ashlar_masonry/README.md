# Example 17 - Ashlar masonry wall (coursed, running bond)

Lay dressed rectangular blocks into a coursed-ashlar wall with half-bond stagger. Top-down
(form-first) masonry: a regular wall envelope filled with cut stone. Units: meters. Style: short
sentences, no em dashes.

![Ashlar wall](17_ashlar_wall.png)

## What it shows
A 60-block dressed inventory (depth 0.18 m, height 0.15 m, widths 0.30-0.60 m) laid by `Ashlar Pack`
into a 3.0 x 1.2 x 0.3 m wall, course height 0.15 m, 5 mm bed + head joints, half-bond stagger.
`Pack Preview` turns the result into meshes (coloured by course).

Measured (this run): **45 blocks placed** across ~7 courses; wall fills 2.98 x 1.08 m of the 3.0 x 1.2 m
envelope. Metrics in `17_ashlar_metrics.json`.

## Sizing note (defend the dimensions so blocks fit)
The wall must be dimensioned so the inventory fits: **wall thickness (0.3 m) > block depth (0.18 m)**,
wall width >> block width, and **course height (0.15 m) = block height** within Height Tolerance. With
thickness equal to block depth the packer places nothing ("No blocks were placed"); give the wall
headroom over the block dimensions.

## Files
- `17_ashlar_wall.gh` - the canvas. Block inventory internalized; self-contained.
- `17_ashlar_wall.3dm` - the baked coursed wall (one mesh per placed block, coloured by course).
- `17_ashlar_wall.png` - front-elevation shaded capture.
- `17_ashlar_metrics.json` - inventory, placed count, wall + course dimensions.

## Components
`Ashlar Pack` (Frahan > Masonry; running-bond coursed layout) + `Pack Preview` (placed-block meshes).
Wire `Ashlar Pack` Result -> `Pack Preview`. For stability analysis, wire the Assembly output into
`Masonry Stability (RBE)`.
