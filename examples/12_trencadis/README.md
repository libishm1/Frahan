# Example 12 - Trencadís mosaic (catalog pack)

Place a catalog of irregular shards into a sheet as a Trencadís ("broken-tile") mosaic. Each shard is
placed exactly once, assigned to a CVD-Lloyd cell, with a grout (mortar) gap. This is the mosaic /
cladding entrypoint: turn a pile of offcut shards into a coherent surface. Style: short sentences, no em
dashes.

![Trencadis mosaic result](12_trencadis_result.png)

*Built and solved live: 28 shards placed into 28 CVD-Lloyd cells, grout 0.4, 0 warnings.*

## What it shows
The catalog packer partitions the sheet into Centroidal-Voronoi (Lloyd-relaxed) cells, then assigns each
catalog shard to a cell via the Hungarian algorithm (optimal one-to-one assignment), placing each piece at
its cell centroid. A grout offset leaves the characteristic mortar gap. Best when piece count matches the
target coverage and you want each piece used exactly once.

Measured live result (this definition): `Trencadis catalog pack: 28/28 placed; Cells (CVD): 28;
Lloyd iter: 12; Grout: 0.40; Runtime: 53 ms`.

## Files
- `12_trencadis_catalog.gh` - the canvas (built + solved live). Internalized shard catalog -> Frahan
  Trencadís Catalog Pack -> Placed Pieces + Report. MCP bridge stripped, grouped.
- `12_trencadis_result.3dm` - baked mosaic (`12_Trencadis_mosaic`) + sheet (`12_Trencadis_sheet`).
- `12_trencadis_result.png` - shaded top-view capture.

## Component
`Frahan Trencadís Catalog Pack` / TrencadisCat (Frahan > Trencadis). CVD-Lloyd cell partition + Hungarian
assignment. Inputs: `Lloyd Iterations` (cell uniformity), `Grout` (mortar gap, default 0.02), `Seed`,
`Tolerance`, `Run`. The sibling `Frahan Trencadís Pipeline` adds a Kangaroo 2 settle pass to fill residual
gaps (headless: 55.1% cov with physics on vs 52.7% greedy). The plain `Frahan Trencadís Pack` is a
skeleton (returns empty); do not use it.

## Data
Synthetic irregular shards (internalized in the `.gh`). For a real job, replace with your offcut/shard
curves and the sheet with your cladding panel outline; add hole curves to route around fixings.

## Run
1. Deploy the `.gha` (Rhino closed). Open the `.gh`.
2. Toggle `Run` true. Read the `Report` panel (placed / cells / runtime).
3. Raise `Lloyd Iterations` for more uniform cells; raise `Grout` for a wider mortar gap.

## Best practices
Per `../GRASSHOPPER_BEST_PRACTICES.md`: coloured Group, default-false Run gate, deterministic Seed.
Results pre-baked so reviewers see the mosaic without Grasshopper. Headless-validated logic.
