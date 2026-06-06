# Example 18 - 3D pack then Settle 3D (Bullet rigid-body physics)

Drop a set of real scanned stones into a container and physically settle them into a stable,
non-interpenetrating pile with a Bullet rigid-body simulation. This is the physics densify stage that
composes after any 3D packer (heightmap / proxy placement) to turn it into real contact. Units: meters.
Style: short sentences, no em dashes.

![Bullet settle pile](18_settle_bullet.png)

## What it shows
12 real ETH1100 dry-stone scans, placed loosely above a container, then settled by `Settle 3D
(Physics)` (Bullet backend: gravity, friction, convex-hull collision). The stones fall and nestle into
a dense, stable, non-interpenetrating pile. One stone hung up mid-drop (a normal settle artifact);
11 settled into the cluster shown.

Measured (this run): 12 stones, friction 0.6, 400 settle steps + 5 tamp rounds, convex-hull collision.
Settled cluster ~1.95 m tall. Metrics in `18_settle_metrics.json`.

## Bullet dependency
`Settle 3D (Physics)` needs `libbulletc.dll` beside the `.gha`. It is shipped in `install/plugin/`
(libbulletc.dll + BulletSharp.dll) and must be in `%APPDATA%/Grasshopper/Libraries/` for the live
component. Without it the component warns and does nothing.

## Files
- `18_pack_settle_bullet.gh` - the canvas (stones + container internalized; self-contained).
- `18_pack_settle_bullet.3dm` - the baked settled pile (one mesh per stone, coloured) + container.
- `18_settle_bullet.png` - shaded capture of the settled pile (GH preview hidden).
- `18_settle_metrics.json` - stone count, friction, steps, pile height.

## Component
`Settle 3D (Physics)` (Frahan > 3D Packing). Inputs: Meshes, Container, Friction, Settle Steps, Tamp,
CoACD (convex-decompose vs convex hull), Run. Outputs: Settled Meshes, Transforms, Source Indices,
Report. Compose it after `Pack3D Irregular Container` / `Block Pack (Tree)` for pack-then-settle.

## Data
Real ETH1100 dry-stone scans (Zenodo 10038881), internalized. Full lot on the Drive master folder in
`../../data/DATA_ACCESS.md`.
