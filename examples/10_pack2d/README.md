# Example 10 - 2D irregular nesting (exact NFP, evolved V506)

Nest closed planar parts into a sheet (with optional holes), zero overlap, maximising stock utilisation.
This is the slab/sheet-cutting entrypoint: cut the most parts from the least stone. Style: short sentences,
no em dashes.

![2D nest result](10_pack2d_result.png)

*Validated evolved-packer output: ~22 varied parts (rectangles + L-shapes) nested around a hole (red),
zero overlap. Captured from the headless harness (current source). See the caveat below.*

## What it shows
The evolved exact No-Fit-Polygon Bottom-Left-Fill nester. The feasible region for each part is the
inner-fit polygon minus the union of the no-fit polygons of already-placed parts and holes, so parts never
overlap by construction. Parts route around sheet holes. Honest yield is measured as
`util_stock = placed part area / (sheet area - hole area)`.

Headless-validated result (`tools/Frahan.StonePack.Harness --pack2dstudy`, current source):
the evolved NFP-BLF is the only 0-overlap packer crossing the 80% bar with holes: 82.0% oversub,
84.7% L+hole, 89.6% on a hard 3-hole fixture. See `wiki/research/packing/PACK2D_STUDY_REPORT.md` and
`10_pack2d_util_bars.png`.

## Files
- `10_pack2d_v506_nest.gh` - the canvas (built live): convex part curves (internalized) -> Frahan Sheet
  Pack (Unified) V506 -> Packed Curves + Report. MCP bridge stripped, grouped.
- `10_pack2d_result.png` - validated nest capture (from `wiki/research/packing/figures/rhino_v506.png`).
- `10_pack2d_result.3dm` - validated nest geometry (the oversub L+hole evolved case).
- `10_pack2d_util_bars.png` - util_stock bar chart across packers.

## Component
`Frahan Sheet Pack (Unified)` / FreeNestU (Frahan > 2D Packing), Variant 0 = V506. Bottom-left-fill
(Burke et al. 2006) over Minkowski-sum NFP/IFP (Bennell & Oliveira 2009) on a Clipper2 back-end. The
sibling `Freeform Sheet Nest (Exact NFP)` / FreeNestX is the pure exact-NFP engine. Inputs of note:
`Variant` (0 V506), `Boundary Mode` (0 off; 1 bias; 2 ring; 3 curve-division), `Trim Tolerance`
(0 = strict no-overlap; >0 boolean-trims part-to-part contacts), `Rotations` (default 0/90/180/270).

## IMPORTANT caveat (read this)
The `result.png` / `result.3dm` here are from the HEADLESS harness (current source), which is validated
0-overlap. The `.gha` currently deployed in a given Rhino may be an OLDER build whose 2D NFP packers
overlap parts (see `handoffs/KNOWN_BUGS.md` KB-7). Rebuild + redeploy the `.gha` from current source
(`docs/INSTALL.md`, Rhino closed) before trusting a live 2D solve. The `.gh` wiring is correct; only the
deployed binary can lag.

## Data
Synthetic varied parts (internalized convex polygons in the `.gh`; the validated capture uses the study's
oversub L+hole part set). For a real job, replace the parts with your cut-list curves and the sheet with
your slab outline; add hole curves for defects/veins to route around.

## Run
1. Build + deploy the CURRENT `.gha` (Rhino closed). Open the `.gh`.
2. Toggle `Run` true. Read the `Report` panel (Placed / Unplaced / Invalid).
3. For maximum density use spacing 0 with `Freeform Sheet Nest (Exact NFP)`; use V506 for holes + boundary
   modes (V506 has a 0.1 spacing floor, KB-6).

## Best practices
Per `../GRASSHOPPER_BEST_PRACTICES.md`: coloured Group, default-false Run gate, deterministic Seed.
Results pre-baked so reviewers see the outcome without Grasshopper.
