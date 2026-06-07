# Example 27 - Polygonal masonry install order

Recover the bottom-to-top installation sequence of a polygonal stone wall from its joint pattern. Give the
component a wall rectangle and a set of joint chains (2D) or a set of polyhedral cells (3D); it returns one
stone per region, a 1..n install order, a per-stone depth, and the install-constraint DAG as line segments.
This is the masonry-sequencing entrypoint: turn a static stone arrangement into a buildable order. Units:
dimensionless model units (procedural, no real-world scale). Style: short sentences, no em dashes.

![3D Voronoi block wall, coloured by install order](27_07_voronoi_3d.png)
*Live Rhino capture. 3D Voronoi block wall: 50 polyhedral cells in a 10 x 10 x 6 box, each a pre-cut stone,
coloured by install order (blue = set first at the base, red = set last at the top). The component
auto-detects shared-face adjacency and recovers the sequence.*

## Design problem and named precedent
A polygonal (rubble or ashlar) wall has no regular courses, so the build order is not obvious. A stone can
only be set once every stone it leans on is in place. The precedent is Kim, T. (2024), *Finding
Installation Sequence of Polygonal Masonry through Design and Depth Search of a Directed Acyclic Graph*
(ASME IDETC-CIE 2024, DETC2024-142563; paper in code_ws at
`Template-General/raw/references/computation-13-00211.pdf`). Kim builds a planar arrangement from joint
chains, derives a region-adjacency "above" relation (rules 5-8), and recovers the order by a reversed-Kahn
depth search on the resulting DAG.

## Components (both already shipped in `Frahan.StonePack.gha`)
- `Frahan Polygonal Masonry Sequence` (Frahan > Masonry), GUID `B4E07A3C-7F4D-4E5B-9C71-0EAF21C9B6A1`.
  Inputs: `Chains` (curves, each monotone in x or a vertical connector), `Wall` (axis-aligned rectangle),
  `Hole Probes` (optional points marking removed openings), `Epsilon` (default 1e-7). Outputs: `Stones`
  (one closed polyline per region, incl. the two infinite top/bottom bands), `Install Order` (1-based),
  `Depth`, `DAG Edges`, `Region Count`.
- `Frahan Polygonal Masonry Sequence 3D` (Frahan > Masonry), GUID `C5F18B4D-8A6F-4E72-AC83-1FBD32D8C7B2`.
  Inputs: closed `Cells` meshes; adjacency auto-detected from shared faces. Outputs: `Cell Count`,
  `Install Order`, `Depth`, `DAG Edges`.

No new code; this is a workflow demonstrator over shipped components.

## Measured (live in Rhino 8, slot armadillo, 2026-06-07)
Every card below was solved on the live canvas and the result baked + captured (truth criterion c). The
`Stones` count includes the two infinite top/bottom bands; subtract 2 for finite stones.

| Card | Fixture | Wall bbox | Stones (live) | Result |
|---|---|---|---|---|
| 01 | Three-band wall (paper minimal) | 4 x 4 | 3 | PASS, no errors |
| 02 | Twelve-angled stone (Figs. 5-6) | 10 x 8 | 7 | PASS |
| 03 | Generic chain wall (Fig. 7) | 12 x 6 | 10 | PASS |
| 04 | Wall with holes (sec. 5.4) | 12 x 6 | 8 | PASS |
| 05 | Wavy chain wall (Fig. 13) | 16 x 10 | 20 | PASS |
| 07 | Voronoi 3D (Fig. 15) | 10 x 10 x 6 | 50 cells | PASS |
| 08 | Negative cases | 10 x 8 / 12-22 x 8 | error / 1 | PASS (guard fires as designed) |

Card 08 is the guard case: 8a raises "chain is not monotone in x or y"; 8b does not silently emit a
plausible-but-wrong order. Both behave as designed.

## Known limitation: 2D Voronoi (card 06, held back)
The 2D Voronoi card (Kim Fig. 14, 28 seeds -> 63 ridge chains -> ~26 cells) is NOT shipped here. On the live
component the planar arrangement (`Pslg.FromSegments`) under-extracts a dense ridge *network*, yielding ~10
of the ~26 cells, because the 2D sequencer is built for *spanning* joint chains (cards 01-05), not a Voronoi
ridge soup. The ~26 figure is the Python (`scipy.spatial.Voronoi`) reference, which the C# arrangement does
not reproduce. Tracked in `handoffs/KNOWN_BUGS.md` (KB-8). Use the 3D Voronoi (card 07), which works, as the
Voronoi demonstrator until the arrangement is fixed. A related robustness fix shipped with this example:
rule (8) ambiguity (a region above another on some shared sub-segments and below on others, common for
irregular tessellations) is now resolved by centroid height instead of throwing, so the sequencer degrades
gracefully rather than aborting.

## Numeric tolerance
`Epsilon` default 1e-7 model units (vertex dedup + geometric predicates; the 3D component quantises face
centroids at the same scale). Geometry is procedural and unitless. No kerf or grout; the joints are
mathematical, not sawn.

## Dataset
None external. Every fixture is procedural and fully internalised in its `.gh`. The companion `.3dm` carries
the same geometry on named layers (`Wall_Boundary`, `Chains`, and for card 07 `Cells_3D`) for the
from-scratch path.

## Files
- `27_01_three_band_wall.gh` ... `27_08_negative_cases.gh` - the pre-wired canvases (geometry internalised).
- `27_01_*.3dm` ... `27_08_*.3dm` - the same geometry on named layers (LFS).
- `27_01_three_band_wall.png` ... `27_05_wavy_perlin.png`, `27_07_voronoi_3d.png` - live Rhino captures,
  coloured by install order.
- `27_<NN>_*_pyref.png` (cards 02-05, 07) - the Python-reference paper-figure renders.
- `cards/27_<NN>_*.md` - the seven HITL pass/fail cards.

## Why this matters
Install order is the bridge between a designed stone wall and a built one. The same DAG that orders a 2D
chain facade also orders a 3D Voronoi block wall, and it feeds the cathedral-scale `Block Build Order`
sequencer. Paired with the fracture-aware block packers (examples 15-17) it gives a quarry-to-wall chain:
cut the stones, then know the order to set them.

## Wiki cross-reference
- `wiki/algorithms/polygonal_masonry/` (Kim 2024 install order; currently in code_ws, migrate alongside).
- `handoffs/KNOWN_BUGS.md` KB-8 (2D Voronoi arrangement under-extraction).
- Card-grounding policy: `feedback_hitl_cards_design_grounded`.
