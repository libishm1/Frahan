# Example 10 - 2D irregular nesting (Sheet Nest, Hole-Aware)

> **Scale, units, position:** METERS. Slab 3.2 x 2.0 m (jumbo gangsaw), cut parts 0.3-1.2 m. Lies flat on
> the XY plane at z=0, sheet lower-left at the origin. Spacing = saw kerf. Packer is **Sheet Nest
> (Hole-Aware) / HoleNest** (contact-NFP, hole-aware, multi-start), strictly valid by boolean check. See
> `../../wiki/research/tolerances_dimensions_slm_roses.md`.

Nest closed planar parts into a sheet (with optional holes), zero overlap, maximising stock utilisation.
This is the slab/sheet-cutting entrypoint: cut the most parts from the least stone. Style: short sentences,
no em dashes.

![2D nest result](10_pack2d_result.png)

*Live-validated Sheet Nest (Hole-Aware) output: 18/18 parts placed (rectangles + a pentagon + triangles),
wood-coloured by part, density 0.563, Valid=True, 256 ms (general-NFP + multi-start K=4 + native-NFP
fast-path). Captured from the live canvas solve.*

## What it shows
The hole-aware contact-NFP nester. Each part is placed by a no-fit-polygon bottom-left fill with
contact-adaptive rotations; parts route around sheet holes and part-in-part-hole nesting is supported, and
the layout is boolean-validated (no overlap by construction, confirmed by the `Valid` output). A
multi-start keep-best pass (K=4 part orders) takes the densest valid layout. Yield is the `Density` output
(placed part area / sheet area).

## Files
- `10_pack2d_nfp_nest.gh` - the canvas: internalized part curves + sheet rectangle -> Sheet Nest
  (Hole-Aware) -> Placed curves + Report, with a coloured Custom Preview. MCP bridge stripped, grouped.
- `10_pack2d_result.png` - the live nest capture (this swap).
- `10_pack2d_result.3dm` - baked nest geometry.
- `10_pack2d_util_bars.png` - util_stock bar chart across packers (historical).

## Component
`Sheet Nest (Hole-Aware)` / HoleNest (Frahan > 2D Packing, GUID `d5f10019-8a3c-4d17-b5e2-6c90f2a47d31`).
Contact-NFP bottom-left fill (Burke et al. 2006 BLF over Bennell & Oliveira 2009 Minkowski NFP/IFP) on a
Clipper2 back-end, with part-in-part-hole IFP and a boolean validity gate. Inputs: `Sheets` (S), `Sheet
Holes` (SH), `Parts` (P), `Part Holes` (PH), `Spacing` (Gap), `BaseRotations` (BR), `ContactRotations`
(CR), `Resolution` (Res), `MultiStart` (MS). Outputs: `Placed` (C), `Source` (I), `Transform` (X),
`Nested` (N), `Report` (R), `Density` (D), `Valid` (V), `Placed Holes` (CH), `Sheet` (Sh). The component is
async: it nests in the background and the result pops in when ready (no Run toggle).

The sibling `Sheet Nest (Live)` / NestLive is the same solver behind a Run gate: it nests on a
background thread (the canvas never freezes on big jobs), draws a live per-sheet colored preview, and
adds `Boundary Mode` (rim-hug placement scored by measured sheet-boundary contact — for parts that
should seat against the slab edge). The older `Freeform Sheet Nest (Exact NFP)` / FreeNestX and
`Sheet Pack (Unified)` are superseded and hidden (old canvases still load).

## Data
Internalized varied parts (rectangles + a pentagon + triangles) in the `.gh`. For a real job, replace the
parts with your cut-list curves and the sheet with your slab outline; add `Sheet Holes` for defects/veins
to route around, and `Part Holes` for parts that themselves carry voids to nest into.

## Run
Open the `.gh` (deploy the current `.gha` first, Rhino closed). HoleNest solves on open and the result
appears when the background nest finishes; read the `Report` / `Density` / `Valid` outputs.

## Best practices
Per `../GRASSHOPPER_BEST_PRACTICES.md`: coloured Group, deterministic, results pre-baked so reviewers see
the outcome without Grasshopper.
