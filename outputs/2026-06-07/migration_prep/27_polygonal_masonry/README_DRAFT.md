# Example 27 - Polygonal masonry install order (Voronoi 2D/3D)

Recover the bottom-to-top installation sequence of a polygonal stone wall from
its joint pattern. Give the component a wall rectangle and a set of joint chains
(2D) or a set of polyhedral cells (3D); it returns one stone per region, a
1..n install order, a per-stone depth, and the install-constraint DAG as line
segments. This is the masonry-sequencing entrypoint: turn a static stone
arrangement into a buildable order. Units: dimensionless model units (procedural,
no real-world scale). Style: short sentences, no em dashes.

> DRAFT. The hero image below is produced in the live step (see liveStepsNeeded
> in COPY_MANIFEST and the live recipe at the bottom). Until a Rhino capture
> exists, the Python-reference figures `27_<NN>_*_pyref.png` stand in.

![Polygonal masonry install order](27_06_voronoi_2d.png)
*Placeholder for the live Rhino capture. Voronoi-tessellation wall: 28 seeds,
ridges clipped to a 16 x 12 wall, ~26 finite stones, coloured by install order
(blue = installed first at the base, red = installed last at the top); DAG edges
drawn from lower-stone centroid to higher-stone centroid.*

## Design problem and named precedent

A polygonal (rubble or ashlar) wall has no regular courses, so the build order is
not obvious. A stone can only be set once every stone it leans on is in place.
The precedent is Kim, T. (2024), *Finding Installation Sequence of Polygonal
Masonry through Design and Depth Search of a Directed Acyclic Graph*, ASME
IDETC-CIE 2024, DETC2024-142563 (paper PDF in the repo at
`Template-General/raw/references/computation-13-00211.pdf`). Kim builds a planar
arrangement from joint chains, derives a region-adjacency "above" relation
(rules 5-8), and recovers the install order by a reversed-Kahn depth search on
the resulting DAG ("Code 1"). This example reproduces the paper figures: card 02
= Figs. 5-6 (twelve-angled stone), card 03 = Fig. 7, card 05 = Fig. 13 (wavy),
card 06 = Fig. 14 (2D Voronoi), card 07 = Fig. 15 (3D Voronoi). The 3D Voronoi
case is distinct from the existing 3D packing examples (11, 15): here each
polyhedral cell is a pre-cut stone and the output is a build sequence, not a
packing.

## Component

- `Frahan Polygonal Masonry Sequence` / PolyMasonrySeq (Frahan > Masonry), GUID
  `B4E07A3C-7F4D-4E5B-9C71-0EAF21C9B6A1`. Inputs: `Chains` (list of curves,
  each monotone in x or a vertical connector), `Wall` (axis-aligned rectangle),
  `Hole Probes` (optional points, mark a region as a removed opening), `Epsilon`
  (tolerance, default 1e-7). Outputs: `Stones` (one closed polyline per region,
  incl. the two infinite top/bottom bands), `Install Order` (1-based), `Depth`
  (reversed-Kahn), `DAG Edges` (line segments).
- `Frahan Polygonal Masonry Sequence 3D` / PolyMasonrySeq3D (Frahan > Masonry),
  GUID `C5F18B4D-8A6F-4E72-AC83-1FBD32D8C7B2`. Inputs: closed `Cells` meshes
  (one per stone); adjacency is auto-detected from shared faces by face-centroid
  quantisation. Outputs: `Cell Count`, `Install Order`, `Depth`, `DAG Edges`.

Both ship in the live `Frahan.StonePack.gha`. No new code is needed for this
example; it is a workflow demonstrator over shipped components.

## Numeric tolerance

`Epsilon` default 1e-7 model units (vertex deduplication and geometric
predicates). The 3D component quantises face centroids to detect shared faces at
the same tolerance scale. Geometry is procedural and unitless; the wall bboxes
range from 4 x 4 (card 01) to 16 x 12 (card 06), and the 3D box is
10 x 10 x 6 (card 07). Because the model is small-integer scale, the default
1e-7 epsilon is far below any joint length and engages cleanly. There is no kerf
or grout in this example; the joints are mathematical, not sawn.

## Dataset

None external. Every fixture is procedural and fully internalised in its `.gh`
(verified: zero external file-path references in all 8 .gh files). The companion
`.3dm` carries the same geometry on named layers (`Wall_Boundary`, `Chains`, and
for card 07 `Cells_3D` / `Reference_Text`) for the from-scratch path. No data
colocation, no `_SOURCE.md`, no large-data subset is required. The 2D Voronoi
(card 06) and 3D Voronoi (card 07) seed patterns were generated in the Python
reference (`scipy.spatial.Voronoi`) and baked; the GH side consumes only the
resulting curves/meshes.

## The eight cards (fixtures)

| Card | Fixture | Wall bbox | Expected finite stones | Tests |
|---|---|---|---|---|
| 01 | Three-band wall (paper minimal) | 4 x 4 | 1 | smallest case, 2 horizontal chains |
| 02 | Twelve-angled stone (Figs. 5-6) | 10 x 8 | 5 | shared-endpoint chains |
| 03 | Generic chain wall (Fig. 7) | 12 x 6 | 8 | horizontal + vertical connectors |
| 04 | Wall with holes (sec. 5.4) | 12 x 6 | 6 | 2 hole probes remove 2 stones |
| 05 | Wavy chain wall (Fig. 13) | 16 x 10 | 20 | denser, 21 chains |
| 06 | Voronoi 2D (Fig. 14) | 16 x 12 | 26 | 28 seeds, ridges as 2-pt chains |
| 07 | Voronoi 3D (Fig. 15) | 10 x 10 x 6 | 50 cells | 3D component, shared-face adjacency |
| 08 | Negative cases | 10 x 8 / 12-22 x 8 | error | 8a non-monotone chain, 8b crossing chains |

Card 08 is the guard case. 8a must raise "chain is not monotone in x or y"; 8b
must either raise a rule-8 cycle error or visibly fail. The component must not
silently emit a plausible-but-wrong order. The full pass/fail cards are in
`cards/`.

## Why this matters

Install order is the bridge between a designed stone wall and a built one. The
same DAG that orders a 2D Voronoi facade also orders a 3D Voronoi block wall,
and it feeds the cathedral-scale `Block Build Order` sequencer (wiki spec
`cathedral_scale_stone_fitting_plan.md`). Pairing this with the fracture-aware
block packers (examples 15-17) gives a quarry-to-wall chain: cut the stones,
then know the order to set them.

## Validation status (honest)

Headless-validated: 21/21 Core algorithm tests + 2/2 component metadata tests
pass; workspace gate 756 PASS / 0 FAIL / 91 SKIP at build time (2026-05-20).
Per the Frahan truth criterion (c), visual canvas validation is the remaining
gate. These eight cards ARE that gate. The live step below is what flips the
wiki `validation_log.md` entry from "implementation landed" to "validated."

## Files (after migration)

- `27_01_three_band_wall.gh` ... `27_08_negative_cases.gh` - 8 pre-wired canvases
  (geometry internalised, hit recompute).
- `27_01_three_band_wall.3dm` ... `27_08_negative_cases.3dm` - the same geometry
  on named layers for the from-scratch path (LFS).
- `27_<NN>_*_pyref.png` (cards 02-07) - the Python-reference paper-figure renders.
- `27_<NN>_*.png` - the live Rhino shaded captures (produced in the live step).
- `cards/27_<NN>_*.md` - the eight HITL pass/fail cards.

## Wiki cross-reference

- `wiki/algorithms/polygonal_masonry/kim_2024_install_order.md` - the validated
  state, paper scope, module map, GUIDs. (Currently in code_ws at
  `D:/code_ws/wiki/algorithms/polygonal_masonry/`; the Frahan `wiki/index.md`
  lists `algorithms/polygonal_masonry/` but the dir is not yet materialised in
  the Frahan wiki. Migrate the wiki page alongside this example, or update the
  index entry.)
- `wiki/algorithms/polygonal_masonry/validation_log.md` - the HITL log this
  example's live step writes into.
- `wiki/specs/cathedral_scale_stone_fitting_plan.md` - downstream `Block Build
  Order` consumer (Kim 2024 sequencing).
- Card-grounding policy: `feedback_hitl_cards_design_grounded` (design problem +
  named precedent + numeric tolerance + dataset + wiki cross-ref, all present
  above).
</content>
