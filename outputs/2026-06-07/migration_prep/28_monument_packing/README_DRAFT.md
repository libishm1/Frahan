# Example 28 - Monument packing (Tamil Nadu monument application)

> **Scale, units, position:** METERS. Monument boxes width 0.4-0.8 m, depth 0.3-0.6 m, height 0.6-1.2 m,
> axis-aligned on the `Bench_Mesh` layer. Geometric tolerance 0.05 m (5 cm), well below the smallest
> monument dimension and matching the bench-scale tolerance budget in
> `../../wiki/research/tolerances_dimensions_slm_roses.md` (site/quarry/masonry work is in meters).

Pack a fixed catalog of monument blanks into quarried stone. This is the top-down, form-first flow: the
monument forms are sovereign, and the question is which quarry block, or which region of a quarry bench,
can yield each monument with usable, non-overlapping placements. Three cards run the full chain end to
end: take inventory of the monuments, pack them inside one bench block, then pack them across a whole
quarry bench. Style: short sentences, no em dashes.

This is distinct from the generic 3D packing example (`../11_pack3d`): there the bin is an abstract box.
Here the bin is a quarried bench whose usable volume is bounded by its crack graph, and the items are
named monument blanks for the Tamil Nadu granite-monument application.

## Design problem and named precedent

Tamil Nadu granite is quarried for monuments, dimension stones, and slabs. A monument workshop holds an
order book of fixed monument forms (statue blanks, lingams, pillars, plinths) at known sizes. The quarry
delivers benches and blocks whose intact volume is broken up by joints and cracks. The matching problem
is top-down: given the monument forms, find placements inside the fracture-bounded stone that yield every
monument without crossing a crack and without overlap.

This sits in the top-down branch of the Frahan design philosophy
(`../../wiki/specs/frahan_design_philosophy.md`, sections 1-2): "form-first ... subdivide it into parts,
then find or cut stone to fit each part." The named precedents in that doc are the voussoir tradition
(Block Research Group Armadillo Vault, Rippmann and Block "Digital Stereotomy") and Quarra's form-sovereign
carved reliefs. Monument packing is the same machine pointed at discrete monument blanks instead of
voussoirs: monument template -> available stone.

## Cards (the three-stage chain)

### 28a Monument inventory
Component: `Frahan > Quarry > Frahan Monument Inventory`. Reads the 12 monument box meshes and returns an
`Inventory` (count + per-box AABB). Expected from the card: Count = 12, TotalAabbVolume ~5-7 m^3. The
inventory is the shared input both packers consume.

### 28b Pack monuments in one bench block
Chain: `monument meshes -> Frahan Monument Inventory -> Inventory`; `convex block mesh -> Frahan Crack
Graph -> CrackGraph`; `CrackGraph + block -> Frahan Block Graph -> BlockGraph`; `BlockGraph + Inventory ->
Frahan Cell Monument Pack -> Placed Boxes`. Per-cell packing places monuments into the crack-bounded cells
of a single bench block. Suggested Grid Stride (Gs) 0.05 m at this scale; raise to 0.1-0.2 m for speed on
larger monuments.

### 28c Pack monuments across a quarry bench
Chain: `monument meshes -> Frahan Monument Inventory -> Inventory`; `bench geometry -> Frahan Crack/Block
Graph -> BlockGraph`; `BlockGraph + Inventory -> Frahan Bench Monument Pack -> Placed Boxes + Fill Ratio`.
Bench-scale packing reports a Fill Ratio: > 0.4 is a well-packed bench; < 0.2 means the bench is too small
for the inventory. Same 12 monuments as 28a/28b.

The shipped `.gh` for 28b and 28c wires ONLY the inventory side (monument meshes -> Monument Inventory).
The Crack Graph + Block Graph + Cell/Bench Monument Pack chain needs the bench mesh + fracture planes
wired by hand on the canvas (this is by design in the source cards; it is the reviewer's hands-on step).

## Numeric tolerance

- Geometric tolerance: 0.05 m (5 cm), bench/masonry scale, below the smallest monument edge (0.3 m).
- Grid Stride (Gs): 0.05 m default; 0.1-0.2 m for larger monuments or faster solves.
- Fill Ratio target (28c): > 0.4 good, < 0.2 means bench too small for the inventory.
- Inventory expectation (28a): Count = 12, TotalAabbVolume ~5-7 m^3.

## Dataset

Synthetic fixture, colocated and self-contained. 12 axis-aligned monument boxes (width 0.4-0.8 m, depth
0.3-0.6 m, height 0.6-1.2 m) baked on the `Bench_Mesh` layer inside each card `.3dm`, internalized in the
`.gh`. No external dataset file is referenced, so the example runs with no download. The boxes stand in for
a Tamil Nadu monument order book at realistic monument sizes; the LiDAR/photogrammetry-scanned bench is a
separate upstream branch (examples 04/07 and the scan-ingest cards) and is not required here.

When applying to a real job, replace the 12 boxes with the actual monument blanks and replace the bench
block with a scanned quarry bench plus its mapped fracture planes (a surface fracture map like
`../26_loviisa_surface_fractures` or a GPR survey like `../08_gpr_marble` supplies the crack graph).

## Wiki cross-reference

- `../../wiki/specs/frahan_design_philosophy.md` - top-down / form-first / voussoir logic; sections 1-2
  name the precedents and define the template-to-stone matching this example performs.
- `../../wiki/research/tolerances_dimensions_slm_roses.md` - per-application units and tolerance budget
  (meters at site/quarry/masonry scale; the 0.05 m tolerance above is from this doc).

## Files (after migration)

- `28a_monument_inventory.gh` / `.3dm` (+ `.png`) - the inventory card.
- `28b_pack_in_block.gh` / `.3dm` (+ `.png`) - per-cell pack inside one bench block.
- `28c_pack_on_bench.gh` / `.3dm` (+ `.png`) - pack across a quarry bench, with fill ratio.

## Run

1. Deploy the `.gha` (Rhino closed). Open a card `.gh`.
2. 28a: read the inventory panel (Count = 12, TotalAabbVolume ~5-7 m^3).
3. 28b/28c: wire the bench/block mesh + fracture planes into Frahan Crack Graph -> Block Graph -> the
   matching monument packer, set Grid Stride, solve, read Placed Boxes (and Fill Ratio for 28c).
