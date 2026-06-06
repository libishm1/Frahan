# Example 13 - Surfaces from a solid + Trencadís on the surfaces

Split a twisted block into its constituent surfaces by dihedral angle, then clad those surfaces with a
Trencadís mosaic. This is the freeform-cladding entrypoint: take a sculpted form, find its faces, tile
them. Style: short sentences, no em dashes.

![Twisted block split into surfaces](13_surface_segments.png)
![Trencadis mosaic draped on the twisted surface](13_surface_trencadis.png)

*Built and solved live: a twisted block (130 deg over its height) split by CGAL dihedral-angle
segmentation into 6 regions (4 helical side bands + 2 caps), then a 176-shard Trencadís mosaic mapped onto
the twisted surface with grout gaps.*

## What it shows
`Mesh Segmentation by Angle (CGAL)` clusters mesh faces into smoothly-connected regions and cuts at sharp
edges (dihedral angle above the threshold). On a twisted box the 4 corner creases (~90 deg) are detected as
boundaries, so the 4 helical side faces and the 2 flat caps come out as 6 separate surface meshes. Measured
live: `Input 702V/1400F; Angle 35 deg; Segments 6 (CGAL CC count 6); Runtime 10 ms`.

The Trencadís cladding maps a broken-tile mosaic onto the curved surface: shards are laid out in the
unrolled (perimeter, height) domain and lifted to 3D via the surface parametrization, so they follow the
twist and butt edge to edge with a grout gap (the same mosaic logic as example 12, now on a 3D surface).

## Files
- `13_surface_segment.gh` - the canvas (built + solved live): internalized twisted block mesh -> Mesh
  Segmentation by Angle (CGAL) -> Segments + Report. MCP bridge stripped, grouped.
- `13_surface_result.3dm` - 6 segment meshes + grey base + 176 mosaic shards (layers
  `13_Surface_segments`, `13_Surface_base`, `13_Surface_trencadis`).
- `13_surface_segments.png`, `13_surface_trencadis.png` - shaded captures.

## Components
`Mesh Segmentation by Angle (CGAL)` / SegmentAngleCgal (Frahan > Lab). Wraps CGAL detect_sharp_edges. Angle
tuning: 5-15 deg strict planarity, 30-60 deg smooth-band detection, 90+ only orthogonal creases. Requires
the CGAL shim (frahan_cgal.dll, in `install/`). For distortion-free 2D->3D mapping, route each segment
through `Surface Chart` (BFF) -> `Pack On Surface`; the BFF exe path input is OPTIONAL (Surface Chart falls
back without it), and a `bff-command-line.exe` is bundled in `install/`.

## Data
Synthetic twisted block (internalized in the `.gh`, low-poly, KB-1-safe). For a real job, feed your scanned
sculpture mesh; raise Angle to merge fine facets into broad surfaces.

## Run
1. Deploy the `.gha` + native libs from `install/` (Rhino closed). Open the `.gh`.
2. Toggle `Run` true. Read `Segment Count`. Tune `Angle` to merge/split surface bands.
3. For the cladding, send each segment to a chart + Trencadís pack (see example 12 for the 2D mosaic).

## Best practices
Per `../GRASSHOPPER_BEST_PRACTICES.md`: coloured Group, default-false Run gate. Results pre-baked so
reviewers see the surfaces + mosaic without Grasshopper.
