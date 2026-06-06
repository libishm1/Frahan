# Example 16 - Rubble masonry wall (real ETH1100 dry-stone)

Settle a lot of irregular scanned stones into an upright, staggered dry-stone rubble wall. Bottom-up
(material-first) masonry: the form emerges from the stones. Units: meters. Style: short sentences, no
em dashes.

![Rubble wall](16_rubble_wall.png)

## What it shows
40 real ETH1100 dry-stone scans (`0000-0039.obj`) settled by `Rubble Wall Settle`. Each stone is
PCA-oriented to bed its broad flat face down, then dropped (gravity -Z) into the per-(x,y)-cell dimples
of the course below, trying orientation flips and a small slot shift. The running-bond stagger emerges
from that dimple settling, not from a fixed grid. Non-penetrating by construction.

Measured (this run): **40 stones placed, 36 stable (90%)** by COM-over-support; wall ~10.2 m wide x
3.9 m tall (~5 courses). Width = 8 (units of mean stone X-extent); larger Width spreads stones into
more, shorter courses, smaller piles them taller. Metrics in `16_rubble_metrics.json`.

## Files
- `16_rubble_wall.gh` - the canvas. The 40 ETH stones are internalized in the Mesh param, so the .gh
  is self-contained (no external data needed to open it).
- `16_rubble_wall.3dm` - the baked settled wall (one mesh per stone, coloured by course band).
- `16_rubble_wall.png` - front-elevation shaded capture.
- `16_rubble_metrics.json` - stones, stable count, stable %, width.

## Component
`Rubble Wall Settle` (Frahan > Masonry). Inputs: Stones (mesh list), Width, Stability Aware, Margin.
Outputs: Settled meshes, per-stone Stable flag, signed support Clearance.

## Data
ETH1100 dry-stone meshes (Zenodo 10038881). Internalized here; the full lot is on the Drive master
folder in `../../data/DATA_ACCESS.md`.
