# Frahan StonePack - component catalog (inputs / outputs)

Auto-generated from source by `extract_components.py`. 240 components on the `Frahan` ribbon tab.
Each entry lists its GUID, algorithm citation, inputs, outputs, and related components.
Source of truth = the component source; regenerate after any component change.

## Subcategories

- [2D Packing](#2d-packing) (14)
- [3D Packing](#3d-packing) (9)
- [Analysis](#analysis) (3)
- [EdgeMatch](#edgematch) (15)
- [Fabricate](#fabricate) (11)
- [Fracture](#fracture) (10)
- [Ingest](#ingest) (5)
- [Kintsugi](#kintsugi) (7)
- [Lab](#lab) (26)
- [Masonry](#masonry) (38)
- [Mesh](#mesh) (25)
- [Quarry](#quarry) (52)
- [Reports](#reports) (3)
- [Sculpt](#sculpt) (3)
- [Slab](#slab) (4)
- [Surface Packing](#surface-packing) (5)
- [Trencadis](#trencadis) (5)
- [Voussoir](#voussoir) (5)


## 2D Packing

### 2D Bottom Left Pack  (`BL Pack`)

- GUID: `6E63E716-84E5-4E1B-9673-8D9C12C4D8B1`  |  icon: `BottomLeftPacker.png`  |  exposure: `hidden`  |  source: `src/Frahan.StonePack.GH/Pack2DBottomLeftComponent.cs`
- Algorithm: **Bottom-left-fill placement heuristic** - Baker, B.S., Coffman, E.G., Rivest, R.L. (1980). "Orthogonal packings in two dimensions." SIAM J. Comput. 9(4):846-855
- PHASED OUT: superseded by Freeform Sheet Nest (Exact NFP) 'FreeNestX' — mean 53.9% waste-cut vs V506 at strict 0-overlap. Kept loadable for old canvases.  Greedy 2D irregular packing using RhinoCommon curves. Implements bottom-left fill (Baker, Coffman & Rivest 1980).

| in | type | access | description |
|---|---|---|---|
| Parts (`P`) | Curve | list | Closed planar curves to pack. |
| Sheet Width (`W`) | Number | item | Sheet width in Y direction. |
| Sheet Length (`L`) | Number | item | Sheet length in X direction. |
| Spacing (`S`) | Number | item | Clearance between packed parts. |
| Rotations (`R`) | Number | list | Allowed rotations in degrees. Example: 0, 90, 180, 270. |
| Sort Mode (`M`) | Integer | item | 0 UserOrder, 1 Area, 2 Width, 3 Height, 4 MaxDimension. |
| Simplify (`Si`) | Boolean | item | Simplify curves before packing. |
| Simplify Tolerance (`St`) | Number | item | Curve simplification tolerance. |
| Tolerance (`T`) | Number | item | Collision tolerance. |
| Seed (`Seed`) | Integer | item | 0 keeps the original deterministic source behavior. Nonzero values explore alternate tie/order options. |
| Corner Mode (`Cnr`) | Integer | item | 0 BottomLeft, 1 BottomRight, 2 TopLeft, 3 TopRight. |
| Run (`Run`) | Boolean | item | Execute packing. |

| out | type | access | description |
|---|---|---|---|
| Packed Curves (`C`) | Curve | list | Placed curves. |
| Transforms (`X`) | Transform | list | Placement transforms applied to source curves. |
| Unplaced (`U`) | Curve | list | Curves that could not be placed. |
| Sheet Preview (`B`) | Curve | item | Preview rectangle for the sheet. |
| Used Length (`L`) | Number | item | Used sheet length. |
| Utilization (`A`) | Number | item | Area utilization inside used sheet length. |
| Report (`R`) | Text | item | Packing report. |
| Source Indices (`Src`) | Integer | list | Original input curve index for each packed curve and transform. |

Related:
- Frahan > 2D Packing > Freeform Sheet Nest (Exact NFP) - SUPERSEDED BY: Freeform Sheet Nest (Exact NFP) 'FreeNestX' — mean 53.9% waste-cut vs V506 at strict 0-overlap.

### 2D Freeform Sheet Pack  (`Freeform Pack`)

- GUID: `A7F52C1D-3E84-4B09-9CF1-85D74A2E0B3F`  |  icon: `IrregularSheet.png`  |  exposure: `hidden`  |  source: `src/Frahan.StonePack.GH/Pack2DIrregularSheetV2Component.cs`
- Algorithm: **NFP-assisted bottom-left irregular nesting** - Burke, E., Hellier, R., Kendall, G., Whitwell, G. (2007). "Complete and robust no-fit polygon generation for the irregular stock cutting problem." Eur. J. Oper. Res.
- PHASED OUT: superseded by Freeform Sheet Nest (Exact NFP) 'FreeNestX' — mean 53.9% waste-cut vs V506 at strict 0-overlap. Kept loadable for old canvases.  Pack any closed planar curves (freeform arcs, splines, polygons) into freeform  sheet outlines with optional holes. Non-blocking async solve. Implements NFP-assisted bottom-left nesting (Burke et al. 2007).

| in | type | access | description |
|---|---|---|---|
| Parts (`P`) | Curve | list | Closed planar part curves to pack. Any curve type accepted — freeform, arc, polyline. |
| Sheet Outlines (`S`) | Curve | list | Closed planar sheet boundary curves. Any curve type accepted. |
| Sheet Holes (`H`) | Curve | tree | Hole curves as a tree. Branch {0} = sheet 0, {1} = sheet 1, etc. |
| Spacing (`Gap`) | Number | item | Clearance between packed parts and between parts and boundaries. Minimum enforced: 0.1. |
| Rotations (`R`) | Number | list | Allowed rotation angles in degrees (e.g. 0, 90, 180, 270). |
| Sort Mode (`M`) | Integer | item | 0 UserOrder, 1 Area↓, 2 Width↓, 3 Height↓, 4 MaxDim↓. |
| Tolerance (`T`) | Number | item | Geometric tolerance for containment and collision checks. |
| Seed (`Seed`) | Integer | item | 0 = deterministic. Non-zero changes tie-breaking randomisation. |
| Run (`Run`) | Boolean | item | Set to true to execute packing. |
| Max Candidates (`Max`) | Integer | item | Candidate budget per part per rotation. 0 = default (300). |
| Corner Mode (`Cnr`) | Integer | item | 0 BottomLeft, 1 BottomRight, 2 TopLeft, 3 TopRight. |

| out | type | access | description |
|---|---|---|---|
| Packed Curves (`C`) | Curve | list | Placed part curves. |
| Transforms (`X`) | Transform | list | Placement transforms applied to each source curve. |
| Source Indices (`Src`) | Integer | list | Original input curve index for each packed curve. |
| Sheet Indices (`Sh`) | Integer | list | Sheet index used for each packed curve. |
| Unplaced (`U`) | Curve | list | Curves that could not be placed. |
| Failure Reasons (`Why`) | Text | list | Reason for each unplaced curve. |
| Sheet Preview (`B`) | Curve | list | Outer sheet and hole preview curves. |
| Report (`R`) | Text | item | Packing report. |

Related:
- Frahan > 2D Packing > Freeform Sheet Nest (Exact NFP) - SUPERSEDED BY: Freeform Sheet Nest (Exact NFP) 'FreeNestX' — mean 53.9% waste-cut vs V506 at strict 0-overlap.

### 2D Freeform Sheet Pack V3  (`Freeform V3`)

- GUID: `C9D74E3F-5A06-4D2B-BEF3-A7F96C4E2D5A`  |  icon: `IrregularSheet.png`  |  exposure: `hidden`  |  source: `src/Frahan.StonePack.GH/Pack2DIrregularSheetV3Component.cs`
- Algorithm: **NFP-assisted bottom-left irregular nesting** - Burke, E., Hellier, R., Kendall, G., Whitwell, G. (2007). "Complete and robust no-fit polygon generation for the irregular stock cutting problem." Eur. J. Oper. Res.
- PHASED OUT: superseded by Freeform Sheet Nest (Exact NFP) 'FreeNestX' — mean 53.9% waste-cut vs V506 at strict 0-overlap. Kept loadable for old canvases.  Pack any closed planar curves into freeform sheet outlines.  Converts all inputs to polyline polygons for robust containment on organic shapes.  Non-blocking async solve. Implements NFP-assisted bottom-left nesting (Burke et al. 2007).

| in | type | access | description |
|---|---|---|---|
| Parts (`P`) | Curve | list | Closed planar part curves to pack. Any curve type — freeform, arc, polyline. |
| Sheet Outlines (`S`) | Curve | list | Closed planar sheet boundary curves. Any curve type, including organic freeform. |
| Sheet Holes (`H`) | Curve | tree | Hole curves as a tree. Branch {0} = sheet 0, {1} = sheet 1, etc. |
| Spacing (`Gap`) | Number | item | Clearance between parts and between parts and boundaries. Minimum enforced: 0.1. |
| Rotations (`R`) | Number | list | Allowed rotation angles in degrees (e.g. 0, 90, 180, 270). |
| Sort Mode (`M`) | Integer | item | 0 UserOrder, 1 Area↓, 2 Width↓, 3 Height↓, 4 MaxDim↓. |
| Tolerance (`T`) | Number | item | Geometric tolerance for containment and collision checks. |
| Seed (`Seed`) | Integer | item | 0 = deterministic. Non-zero changes tie-breaking randomisation. |
| Run (`Run`) | Boolean | item | Set to true to execute packing. |
| Max Candidates (`Max`) | Integer | item | Candidate budget per part per rotation. 0 = default (300). |
| Corner Mode (`Cnr`) | Integer | item | 0 BottomLeft, 1 BottomRight, 2 TopLeft, 3 TopRight. |

| out | type | access | description |
|---|---|---|---|
| Packed Curves (`C`) | Curve | list | Placed part curves. |
| Transforms (`X`) | Transform | list | Placement transforms applied to each source curve. |
| Source Indices (`Src`) | Integer | list | Original input curve index for each packed curve. |
| Sheet Indices (`Sh`) | Integer | list | Sheet index used for each packed curve. |
| Unplaced (`U`) | Curve | list | Curves that could not be placed. |
| Failure Reasons (`Why`) | Text | list | Reason for each unplaced curve. |
| Sheet Preview (`B`) | Curve | list | Outer sheet and hole preview curves. |
| Report (`R`) | Text | item | Packing report. |

Related:
- Frahan > 2D Packing > Freeform Sheet Nest (Exact NFP) - SUPERSEDED BY: Freeform Sheet Nest (Exact NFP) 'FreeNestX' — mean 53.9% waste-cut vs V506 at strict 0-overlap.

### 2D Irregular Sheet Pack  (`Sheet Pack`)

- GUID: `8233FA3B-12F7-4D37-BBE5-6D3ECAB0FAE1`  |  icon: `Pack2D.png`  |  exposure: `hidden`  |  source: `src/Frahan.StonePack.GH/Pack2DIrregularSheetComponent.cs`
- Algorithm: **NFP-assisted bottom-left irregular nesting** - Burke, E., Hellier, R., Kendall, G., Whitwell, G. (2007). "Complete and robust no-fit polygon generation for the irregular stock cutting problem." Eur. J. Oper. Res.
- PHASED OUT: superseded by Freeform Sheet Nest (Exact NFP) 'FreeNestX' — mean 53.9% waste-cut vs V506 at strict 0-overlap. Kept loadable for old canvases.  Pack closed planar parts into irregular sheet outlines with optional per-sheet hole curves. Implements NFP-assisted bottom-left nesting (Burke et al. 2007).

| in | type | access | description |
|---|---|---|---|
| Parts (`P`) | Curve | list | Closed planar part curves to pack. |
| Sheet Outlines (`S`) | Curve | list | Closed planar outer sheet curves. |
| Sheet Holes (`H`) | Curve | tree | Hole curves as a tree. Branch {0} belongs to sheet 0, {1} to sheet 1, and so on. |
| Spacing (`Gap`) | Number | item | Clearance between packed parts and sheet/hole boundaries. |
| Rotations (`R`) | Number | list | Allowed rotations in degrees. Example: 0, 90, 180, 270. |
| Sort Mode (`M`) | Integer | item | 0 UserOrder, 1 Area, 2 Width, 3 Height, 4 MaxDimension. |
| Simplify (`Si`) | Boolean | item | Simplify curves before packing. |
| Simplify Tolerance (`St`) | Number | item | Curve simplification tolerance. |
| Tolerance (`T`) | Number | item | Collision and containment tolerance. |
| Seed (`Seed`) | Integer | item | 0 is deterministic. Nonzero values change tie-breaking. |
| Run (`Run`) | Boolean | item | Execute packing. |
| Max Candidates (`Max`) | Integer | item | Candidate budget per part/rotation/sheet. Use 0 for default. |
| Corner Mode (`Cnr`) | Integer | item | 0 BottomLeft, 1 BottomRight, 2 TopLeft, 3 TopRight. |

| out | type | access | description |
|---|---|---|---|
| Packed Curves (`C`) | Curve | list | Placed part curves. |
| Transforms (`X`) | Transform | list | Placement transforms applied to source curves. |
| Source Indices (`Src`) | Integer | list | Original input curve index for each packed curve and transform. |
| Sheet Indices (`Sh`) | Integer | list | Sheet index used for each packed curve. |
| Unplaced (`U`) | Curve | list | Curves that could not be placed. |
| Failure Reasons (`Why`) | Text | list | Reason for each unplaced curve. |
| Sheet Preview (`B`) | Curve | list | Outer sheet and hole preview curves. |
| Report (`R`) | Text | item | Packing report. |

Related:
- Frahan > 2D Packing > Freeform Sheet Nest (Exact NFP) - SUPERSEDED BY: Freeform Sheet Nest (Exact NFP) 'FreeNestX' — mean 53.9% waste-cut vs V506 at strict 0-overlap.

### 2D NFP Pack  (`NFP Pack`)

- GUID: `0B164F89-A199-4264-88FD-A91E508DBEC3`  |  icon: `NoFitPolygon.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/NfpPack2DComponent.cs`
- Algorithm: **No-fit polygon construction** - Burke, Hellier, Kendall, Whitwell 2007, European Journal of Operational Research 179(1):27-49 Complete and robust no-fit polygon generation for the irregular stock cutting problem
- NFP-assisted 2D irregular packing with diagnostics and optional sequence optimization. [Burke et al. 2007]

| in | type | access | description |
|---|---|---|---|
| Parts (`P`) | Curve | list | Closed planar curves to pack. |
| Sheet Width (`W`) | Number | item | Sheet width in Y direction. |
| Sheet Length (`L`) | Number | item | Sheet length in X direction. |
| Spacing (`S`) | Number | item | Clearance between packed parts. |
| Rotations (`R`) | Number | list | Allowed rotations in degrees. Example: 0, 90, 180, 270. |
| Sort Mode (`M`) | Integer | item | 0 UserOrder, 1 Area, 2 Width, 3 Height, 4 MaxDimension. |
| Simplify (`Si`) | Boolean | item | Simplify curves before packing. |
| Simplify Tolerance (`St`) | Number | item | Curve simplification tolerance. |
| Tolerance (`T`) | Number | item | Collision and NFP tolerance. |
| NFP Max Iterations (`NI`) | Integer | item | Budget for triangulated concave NFP construction. Higher values allow more concave detail but solve slower. |
| Optimizer Mode (`OM`) | Integer | item | 0 None, 1 sort variants, 2 sort variants plus reverse, 3 deterministic swap search. |
| Optimizer Iterations (`OI`) | Integer | item | Additional deterministic swap-search iterations used when Optimizer Mode is 3. |
| Seed (`Seed`) | Integer | item | 0 keeps the original deterministic source behavior. Nonzero values explore alternate rotation and swap-search options. |
| Corner Mode (`Cnr`) | Integer | item | 0 BottomLeft, 1 BottomRight, 2 TopLeft, 3 TopRight. |
| Run (`Run`) | Boolean | item | Execute packing. |

| out | type | access | description |
|---|---|---|---|
| Packed Curves (`C`) | Curve | list | Placed curves. |
| Transforms (`X`) | Transform | list | Placement transforms applied to source curves. |
| Unplaced (`U`) | Curve | list | Curves that could not be placed. |
| Sheet Preview (`B`) | Curve | item | Preview rectangle for the sheet. |
| NFP Preview (`N`) | Curve | list | Diagnostic no-fit regions used during placement. Capped to keep Grasshopper responsive. |
| Used Length (`L`) | Number | item | Used sheet length. |
| Utilization (`A`) | Number | item | Area utilization inside used sheet length. |
| Report (`R`) | Text | item | Packing report. |
| Source Indices (`Src`) | Integer | list | Original input curve index for each packed curve and transform. |

### CSV Parts Reader  (`CSVParts`)

- GUID: `F2D00C5F-CADC-4F2D-9C5F-7E60CADA15A0`  |  icon: `CurveToPolygon.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/TwoD/CsvPartsReaderComponent.cs`
- Read an Albano-format 2D packing benchmark CSV  (num,polygon rows where polygon is a JSON-ish [[x,y], ...])  and emit one closed PolylineCurve per part (with the row's  multiplicity respected).

| in | type | access | description |
|---|---|---|---|
| CSV Path (`Csv`) | Text | item | Absolute path to a benchmark CSV (Albano/Blaz/Dagli/Jakobs  format: header 'num,polygon', then rows with an integer  multiplicity and a JSON-ish polygon vertex list). |
| Scale (`S`) | Number | item | Per-coordinate scale factor (e.g. set 0.001 to convert  millimetre Albano coordinates to metres). |
| Expand Multiplicity (`E`) | Boolean | item | If True, each row is emitted `num` times. If False, the  row is emitted once and the multiplicity is exposed  verbatim in the Counts output. |

| out | type | access | description |
|---|---|---|---|
| Parts (`P`) | Curve | list | One closed PolylineCurve per emitted part. |
| Counts (`N`) | Integer | list | Per-row multiplicity from the CSV. |
| Row Indices (`R`) | Integer | list | 0-based source row index per emitted part (lines up with  the canonical benchmark numbering). |
| Total Parts (`T`) | Integer | item | Total number of curves emitted. |
| Report (`Rep`) | Text | item | One-line summary of the read. |

### Floor Tile (Boundary-Trimmed)  (`FloorTile`)

- GUID: `D5F1001A-7C3B-4E29-B1A6-0F2D9E4C8B57`  |  icon: `FloorTile.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/TwoD/FloorTileComponent.cs`
- Algorithm: **Floor setting-out: balanced/centred layout and the ANSI half-tile no-sliver rule** - ANSI A108.02 4.3.2 (centre and balance tile, no cuts smaller than half size); CTEF/TCNA tile layout practice
- Divide a floor boundary into standard stone tiles on a module grid (tile face + grout joint) by  straight full-span (guillotine) lines, trimming the perimeter tiles to the boundary. Choose the  start: a corner, a picked point, or a centred/symmetric layout that balances the border cuts  equally on opposite walls. The ANSI half-tile no-sliver rule is enforced by auto-centring (the  grid shifts by half a module to split a thin sliver into two larger border cuts). Each tile  carries a GRAIN DIRECTION, output both as a direction line (the feature) and as a texture-mapping  frame: feed the tile meshes to a Custom Preview Material with a scanned stone image and the grain  follows. Set Continuous for a slip-match (the floor reads as one slab). Deterministic.

| in | type | access | description |
|---|---|---|---|
| Boundary (`B`) | Curve | item | Closed planar floor outline curve (the room edge), in a WorldXY-parallel plane. |
| Holes (`H`) | Curve | list | Optional closed obstacle/hole curves inside the floor (columns, openings); tiles are trimmed around them. |
| TileX (`Lx`) | Number | item | Tile face width (model units). |
| TileY (`Ly`) | Number | item | Tile face height (model units). |
| Joint (`Gr`) | Number | item | Grout joint width; module pitch = tile + joint. Stone 2-6, rectified 3. |
| Start (`St`) | Integer | item | Start mode: 0 = corner (full tile at the boundary min corner), 1 = picked point (Anchor),  2 = centred/symmetric (equal border cuts on opposite walls). |
| Anchor (`Pt`) | Point | item | Lattice origin for Start = 1 (picked point); the corner of one full tile. |
| Grain (`Ga`) | Number | item | Grain/vein direction in DEGREES (the grain feature; also rotates the texture mapping). |
| GrainField (`Gf`) | Integer | item | Grain pattern: 0 = monolithic (all one way), 1 = quarter-turn (checkerboard 0/90), 2 = random. |
| Sliver (`Sf`) | Number | item | No-sliver acceptance: perimeter cuts must be >= this fraction of the tile (0.5 ANSI, 0.333 fallback). |
| Match (`Mt`) | Integer | item | Texture continuity: 0 = per-tile (each tile shows the whole image, rotated to its grain),  1 = slip-match (UVs flow across the floor so it reads as one slab), 2 = book-match (adjacent  tiles mirror so the veins meet at the joints). |
| Stagger (`Off`) | Integer | item | Running-bond row offset: 0 = stack bond, 1 = 1/3 offset, 2 = 1/2 offset. Large-format tiles  (a side > 380) auto-cap at 1/3 to control lippage. |
| Image (`Img`) | Text | item | Optional stone-texture image file path. When supplied, the floor is DRAWN on screen with the  image mapped to the grain (no extra wiring); also emitted as Material for Custom Preview / baking. |
| Rates (`$`) | Number | list | Optional cost rates (defaults kept where omitted). Order: material/m2, overage frac, cut/tile,  set-out stack, set-out matched, lay/m2, lay/tile, large-format/m2, premium[per,slip,book],  matchLabour[per,slip,book]/m2. Drives the Cost/m2 and Costing outputs. |

| out | type | access | description |
|---|---|---|---|
| Tiles (`T`) | Curve | list | Trimmed tile boundary polylines (full tiles + cut perimeter tiles). |
| Direction (`Dir`) | Line | list | Per-tile grain direction line from the tile centre (the grain feature; draw as arrows). |
| Full (`F`) | Boolean | list | True for a full module tile, false for a cut perimeter tile. |
| TexMesh (`M`) | Mesh | list | Per-tile mesh carrying grain-aligned texture coordinates. Feed a Custom Preview Material with a  scanned stone image and the texture maps per the grain direction. |
| MapFrame (`Pl`) | Plane | list | Per-tile texture-mapping plane (origin = tile centre, X rotated by the grain). Use with Rhino's  planar TextureMapping (CreatePlanarMapping + SetTextureMapping) for object-level mapping. |
| Report (`R`) | Text | item | Tile counts (full/cut), coverage, smallest perimeter cut vs the no-sliver threshold, grain field,  match mode and row offset. |
| Cost/m2 (`$/m2`) | Number | item | Estimated installed cost per square metre of floor for the current config (material + cutting +  set-out + laying + matching, on the Rates). Illustrative; override Rates with local prices. |
| Costing (`$R`) | Text | item | Cost report: the material vs operation breakdown and the total $/m2 for the current config, plus a  match sweep (PerTile/Slip/Book at this layout) and a size sweep (re-packed tile-size ladder). |

Related:
- Frahan > 2D Packing > Sheet Nest (Hole-Aware) - Irregular-part nesting on a sheet; the floor tiler is its regular-grid, boundary-trimmed sibling.

### Frahan Residual Voids  (`ResVoid`)

- GUID: `AB12C002-1A2B-4C3D-9E4F-5A6B7C8D9E02`  |  icon: `PackMetrics.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/ResidualVoidsComponent.cs`
- Algorithm: **Grid sampling + connected-component void detection** - Frahan-original
- Detect 2D residual voids inside a sheet polygon not covered by any  placed part. Uses cell-grid sampling + 4-neighbour connected-component  labelling. Reports each void's bounding rectangle and approximate area;  small voids below MinArea are filtered. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Sheet (`S`) | Curve | item | Closed planar curve representing the sheet outline. |
| Placed Parts (`P`) | Curve | list | Closed planar curves representing already-placed parts. |
| Cell Size (`C`) | Number | item | Sampling cell size in model units. Smaller = more accurate, slower. |
| Min Void Area (`M`) | Number | item | Minimum reportable void area in model-unit-squared.  Smaller voids are filtered. |
| Discretisation Tolerance (`T`) | Number | item | Tolerance used when discretising the sheet and parts to polylines. |

| out | type | access | description |
|---|---|---|---|
| Void Bounds (`B`) | Rectangle | list | Axis-aligned bounding rectangle of each detected void. |
| Void Areas (`A`) | Number | list | Approximate area of each detected void. |
| Cell Counts (`N`) | Integer | list | Cells per detected void. |
| Total Void Area (`Tv`) | Number | item | Sum of all reported void areas. |
| Report (`R`) | Text | item | Human-readable summary. |

### Frahan Sheet Pack (Unified Async)  (`FreeNestUA`)

- GUID: `AB12C00C-1A2B-4C3D-9E4F-5A6B7C8D9E0C`  |  icon: `IrregularSheet.png`  |  exposure: `hidden`  |  source: `src/Frahan.StonePack.GH/IrregularSheetFillComponentAsync.cs`
- Algorithm: **NFP-assisted bottom-left irregular nesting** - Burke, E., Hellier, R., Kendall, G., Whitwell, G. (2007). "Complete and robust no-fit polygon generation for the irregular stock cutting problem." Eur. J. Oper. Res.
- Async variant of Frahan Sheet Pack (Unified). Same Variant routing  as the sync version but runs on a background thread so Grasshopper  stays responsive during long packs. Pick the variant with the Variant  input; default is V506. Implements NFP-assisted bottom-left nesting (Burke et al. 2007).

| in | type | access | description |
|---|---|---|---|
| Parts (`P`) | Curve | list | Closed planar part curves to pack. |
| Sheet Outlines (`S`) | Curve | list | Closed planar sheet boundary curves. |
| Sheet Holes (`H`) | Curve | tree | Hole curves as a tree. Branch {0} = sheet 0, {1} = sheet 1, etc. |
| Spacing (`Gap`) | Number | item | Clearance between parts and between parts and boundaries. |
| Rotations (`R`) | Number | list | Allowed rotation angles in degrees (default 0, 90, 180, 270). |
| Sort Mode (`M`) | Integer | item | 0 UserOrder, 1 Area↓, 2 Width↓, 3 Height↓, 4 MaxDim↓. |
| Tolerance (`T`) | Number | item | Geometric tolerance for containment and collision. |
| Seed (`Seed`) | Integer | item | 0 = deterministic; non-zero changes tie-breaking. |
| Run (`Run`) | Boolean | item | Set to true to execute packing. |
| Max Candidates (`Max`) | Integer | item | Candidate budget per part per rotation. |
| Corner Mode (`Cnr`) | Integer | item | 0 BottomLeft, 1 BottomRight, 2 TopLeft, 3 TopRight. |
| Variant (`V`) | Integer | item | 0 V506 (default), 1 V1 polyline, 2 V2 freeform, 3 V3 adaptive non-convex. |

| out | type | access | description |
|---|---|---|---|
| Packed Curves (`C`) | Curve | list | Placed part curves. |
| Transforms (`X`) | Transform | list | Placement transforms applied to each source curve. |
| Source Indices (`Src`) | Integer | list | Original input curve index for each packed curve. |
| Sheet Indices (`Sh`) | Integer | list | Sheet index used for each packed curve. |
| Unplaced (`U`) | Curve | list | Curves that could not be placed. |
| Failure Reasons (`Why`) | Text | list | Reason for each unplaced curve. |
| Sheet Preview (`B`) | Curve | list | Outer sheet and hole preview curves. |
| Report (`R`) | Text | item | Packing report. |
| Variant Used (`Vu`) | Text | item | Which variant actually ran (echoes the requested Variant input). |

### Frahan Sheet Pack (Unified)  (`FreeNestU`)

- GUID: `AB12C00B-1A2B-4C3D-9E4F-5A6B7C8D9E0B`  |  icon: `IrregularSheet.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/IrregularSheetFillComponent.cs`
- Algorithm: **No-fit polygon construction** - Burke, Hellier, Kendall, Whitwell 2007, European Journal of Operational Research 179(1):27-49 Complete and robust no-fit polygon generation for the irregular stock cutting problem
- Unified entry point for Frahan's four 2D irregular-sheet solver  variants (V1 / V2 / V3 / V506). Pick the variant with the Variant  input; default is V506. Synchronous solve only - for the async  variant, use 'Frahan Sheet Pack (Unified Async)' / FreeNestUA. [Burke et al. 2007]

| in | type | access | description |
|---|---|---|---|
| Parts (`P`) | Curve | list | Closed planar part curves to pack. |
| Sheet Outlines (`S`) | Curve | list | Closed planar sheet boundary curves. |
| Sheet Holes (`H`) | Curve | tree | Hole curves as a tree. Branch {0} = sheet 0, {1} = sheet 1, etc. |
| Spacing (`Gap`) | Number | item | Clearance between parts and between parts and boundaries. |
| Rotations (`R`) | Number | list | Allowed rotation angles in degrees (default 0, 90, 180, 270). |
| Sort Mode (`M`) | Integer | item | 0 UserOrder, 1 Area↓, 2 Width↓, 3 Height↓, 4 MaxDim↓. |
| Tolerance (`T`) | Number | item | Geometric tolerance for containment and collision. 0 (default) =  AUTO: use the active document's absolute tolerance, so a millimetre  document gets a millimetre tolerance and a metre document a metre  tolerance (no manual per-scale tuning). Set a positive value to override. |
| Seed (`Seed`) | Integer | item | 0 = deterministic; non-zero changes tie-breaking. |
| Run (`Run`) | Boolean | item | Set to true to execute packing. |
| Max Candidates (`Max`) | Integer | item | Candidate budget per part per rotation. |
| Corner Mode (`Cnr`) | Integer | item | 0 BottomLeft, 1 BottomRight, 2 TopLeft, 3 TopRight. |
| Variant (`V`) | Integer | item | 0 V506 (default, recommended), 2 V2 freeform (recommended; V506  delegates to this engine). 1 V1 polyline and 3 V3 adaptive  non-convex are RETAINED FOR REPRODUCIBILITY ONLY: the 2026-06-05  --packbench benchmark measured V1 at 44.6% fill with 9 overlap  pairs and 3166 ms, and V3 at 21/24 placed (dominated by V2's  24/24 at the same fill). Prefer 0 or 2 for new work. See  outputs/2026-06-05/keep_or_cut/PACKING_BENCHMARK.md. |
| Boundary Mode (`BMode`) | Integer | item | 0 = off (geometric only).  1 = boundary-aware bias: parts with edges matching the sheet  outline / hole edges are placed first AND auto-rotated to align  with the matched boundary tangent; all candidate sources (boundary  anchors + interior grid) used.  2 = strict two-phase ring/interior: boundary-worthy parts use only  boundary-anchor candidates (true ring), then non-boundary parts  fill the interior. Falls back to all candidates if a phase is  saturated.  3 = uniform curve division: divide each boundary curve by arc  length, place each part at its assigned position with longest  edge tangent to the curve. Most predictable ring layout. Min  Boundary Affinity is ignored in this mode.  V506 only — other variants ignore. |
| Min Boundary Affinity (`BAff`) | Number | item | Edge-match score at or above which an edge is considered  boundary-worthy. Range [0, 1]; default 0.5. Only applies when  Boundary Mode > 0. |
| Discretization Tolerance (`DTol`) | Number | item | ToPolyline tolerance for both sheet boundaries and part curves.  Set to a positive value to control polyline density independently  of the geometric Tolerance. Default -1 (means: use Tolerance).  Lower = finer polylines, more detail captured but more matching  work. Higher = coarser, faster, but may miss small features. |
| Trim Tolerance (`TrimT`) | Number | item | Maximum part-to-part overlap depth (in document units) allowed  during placement. After all parts are placed, overlapping pairs  are boolean-differenced — the EARLIER-placed part wins, the  later-placed part loses material at the contact. Sheet outline  and holes are NEVER trimmed (only part-to-part collisions).  0 = trim off (strict no-overlap; THIS IS THE DEFAULT, so packed  parts never overlap out of the box). Set > 0 to allow  overlap-then-trim (the earlier-placed part wins); for meter-scale  shared-contact masonry coursing try 0.003–0.01. Most useful with  Boundary Mode > 0 where parts get pushed close together along the  boundary; the trim cleans the contacts. |

| out | type | access | description |
|---|---|---|---|
| Packed Curves (`C`) | Curve | list | Placed part curves. |
| Transforms (`X`) | Transform | list | Placement transforms applied to each source curve. |
| Source Indices (`Src`) | Integer | list | Original input curve index for each packed curve. |
| Sheet Indices (`Sh`) | Integer | list | Sheet index used for each packed curve. |
| Unplaced (`U`) | Curve | list | Curves that could not be placed. |
| Failure Reasons (`Why`) | Text | list | Reason for each unplaced curve. |
| Sheet Preview (`B`) | Curve | list | Outer sheet and hole preview curves. |
| Report (`R`) | Text | item | Packing report. |
| Variant Used (`Vu`) | Text | item | Which variant actually ran (echoes the requested Variant input). |
| Trimmed Curves (`Tc`) | Curve | list | Per-part post-trim curves. Same length as Packed Curves. When  Trim Tolerance == 0, this output is empty. When > 0, each  entry is either the original packed curve (no trim happened)  or the boolean-difference result from being trimmed by an  earlier-placed neighbor. |
| Trim Adjacency (`Ta`) | Integer | tree | DataTree per packed part: branch i lists the SOURCE indices  of earlier-placed parts that trimmed Trimmed Curves[i]. Empty  branches indicate parts that were not trimmed. |

### Freeform Sheet Nest  (`FreeNest`)

- GUID: `D5E7A2B1-8C34-4F1E-A096-3B7F5D2E8A4C`  |  icon: `Pack2D.png`  |  exposure: `hidden`  |  source: `src/Frahan.StonePack.GH/Pack2DIrregularSheetV506Component.cs`
- Algorithm: **NFP-assisted bottom-left irregular nesting** - Burke, E., Hellier, R., Kendall, G., Whitwell, G. (2007). "Complete and robust no-fit polygon generation for the irregular stock cutting problem." Eur. J. Oper. Res.
- PHASED OUT: superseded by Freeform Sheet Nest (Exact NFP) 'FreeNestX' — mean 53.9% waste-cut vs V506 at strict 0-overlap. Kept loadable for old canvases.  Packs closed planar parts into freeform sheet boundaries with holes using Frahan's V5.0.6 polygon-based nesting solver.  Supports organic sheet outlines, hole avoidance, spacing, rotation search, and non-blocking solve execution. Implements NFP-assisted bottom-left nesting (Burke et al. 2007).

| in | type | access | description |
|---|---|---|---|
| Parts (`P`) | Curve | list | Closed planar part curves to pack. Any curve type — freeform, arc, polyline. |
| Sheet Outlines (`S`) | Curve | list | Closed planar sheet boundary curves. Any curve type, including organic freeform. |
| Sheet Holes (`H`) | Curve | tree | Hole curves as a tree. Branch {0} = sheet 0, {1} = sheet 1, etc. |
| Spacing (`Gap`) | Number | item | Clearance between parts and between parts and boundaries. Minimum enforced: 0.1. |
| Rotations (`R`) | Number | list | Allowed rotation angles in degrees (e.g. 0, 90, 180, 270). |
| Sort Mode (`M`) | Integer | item | 0 UserOrder, 1 Area↓, 2 Width↓, 3 Height↓, 4 MaxDim↓. |
| Tolerance (`T`) | Number | item | Geometric tolerance for containment and collision checks. |
| Seed (`Seed`) | Integer | item | 0 = deterministic. Non-zero changes tie-breaking randomisation. |
| Run (`Run`) | Boolean | item | Set to true to execute packing. |
| Max Candidates (`Max`) | Integer | item | Candidate budget per part per rotation. 0 = default (300). |
| Corner Mode (`Cnr`) | Integer | item | 0 BottomLeft, 1 BottomRight, 2 TopLeft, 3 TopRight. |

| out | type | access | description |
|---|---|---|---|
| Packed Curves (`C`) | Curve | list | Placed part curves. |
| Transforms (`X`) | Transform | list | Placement transforms applied to each source curve. |
| Source Indices (`Src`) | Integer | list | Original input curve index for each packed curve. |
| Sheet Indices (`Sh`) | Integer | list | Sheet index used for each packed curve. |
| Unplaced (`U`) | Curve | list | Curves that could not be placed. |
| Failure Reasons (`Why`) | Text | list | Reason for each unplaced curve. |
| Sheet Preview (`B`) | Curve | list | Outer sheet and hole preview curves. |
| Report (`R`) | Text | item | Packing report. |

Related:
- Frahan > 2D Packing > Freeform Sheet Nest (Exact NFP) - SUPERSEDED BY: Freeform Sheet Nest (Exact NFP) 'FreeNestX' — mean 53.9% waste-cut vs V506 at strict 0-overlap.

### Freeform Sheet Nest (Exact NFP)  (`FreeNestX`)

- GUID: `2D351646-2CB0-402A-BBD8-3950B5BB1FBC`  |  icon: `Pack2D.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/IrregularSheetFillNfpBlfComponent.cs`
- Algorithm: **Exact No-Fit-Polygon Bottom-Left-Fill (hard non-overlap by construction)** - Burke, E.K., Hellier, R., Kendall, G., Whitwell, G. (2006). "A New Bottom-Left-Fill Heuristic Algorithm for the Two-Dimensional Irregular Packing Problem." Operations Research 54(3):587-601
- Packs closed planar parts into freeform sheets using an exact No-Fit-Polygon  Bottom-Left-Fill solver. The feasible region for each part is the inner-fit polygon  minus the union of no-fit polygons of placed parts and holes, so parts never overlap  by construction (a hard constraint, not a trim). Implements bottom-left-fill  (Burke et al. 2006) over Minkowski-sum NFP/IFP (Bennell & Oliveira 2009) on a Clipper2  back-end. Sibling of the V506 nester; V506 is unchanged.

| in | type | access | description |
|---|---|---|---|
| Parts (`P`) | Curve | list | Closed planar part curves to pack. |
| Sheet Outlines (`S`) | Curve | list | Closed planar sheet boundary curves. |
| Sheet Holes (`H`) | Curve | tree | Hole curves as a tree. Branch {i} = sheet i. |
| Spacing (`Gap`) | Number | item | Clearance between parts and boundaries. |
| Rotations (`R`) | Number | list | Allowed rotation angles in degrees. |
| Sort Mode (`M`) | Integer | item | 0 UserOrder, 1 Area↓, 2 Width↓, 3 Height↓, 4 MaxDim↓. |
| Tolerance (`T`) | Number | item | Geometric tolerance. 0 (default) = AUTO: use the active document's absolute tolerance (mm doc -> mm tol, m doc -> m tol). Set a positive value to override. |
| Seed (`Seed`) | Integer | item | 0 = deterministic. Non-zero changes tie-break. |
| Run (`Run`) | Boolean | item | Set true to execute packing. |

| out | type | access | description |
|---|---|---|---|
| Packed Curves (`C`) | Curve | list | Placed part curves. |
| Transforms (`X`) | Transform | list | Placement transforms per source curve. |
| Source Indices (`Src`) | Integer | list | Original input index for each packed curve. |
| Sheet Indices (`Sh`) | Integer | list | Sheet index for each packed curve. |
| Unplaced (`U`) | Curve | list | Curves that could not be placed. |
| Failure Reasons (`Why`) | Text | list | Reason for each unplaced curve. |
| Sheet Preview (`B`) | Curve | list | Outer sheet and hole preview curves. |
| Report (`R`) | Text | item | Packing report. |

### NFP Test  (`NFP`)

- GUID: `915FB7AF-425E-4F5B-9F57-7CE8F5C8A301`  |  icon: `NoFitPolygon.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/NfpTestComponent.cs`
- Algorithm: **No-fit polygon construction (orbital / boundary slide)** - Burke, E., Hellier, R., Kendall, G., Whitwell, G. (2007). "Complete and robust no-fit polygon generation for the irregular stock cutting problem." Eur. J. Oper. Res.
- Generate a diagnostic no-fit polygon from two closed planar polylines. Implements no-fit polygon construction (Burke et al. 2007).

| in | type | access | description |
|---|---|---|---|
| Stationary (`A`) | Curve | item | Stationary closed polygon. |
| Sliding (`B`) | Curve | item | Sliding closed polygon. |
| Tolerance (`T`) | Number | item | Geometric tolerance. |
| Max Iterations (`I`) | Integer | item | Reserved for future full concave NFP implementation. |
| Rectangle Shortcut (`R`) | Boolean | item | Reserved for future rectangle-specific NFP implementation. |

| out | type | access | description |
|---|---|---|---|
| NFP (`N`) | Curve | item | No-fit polygon curve. |
| Error Code (`E`) | Integer | item | 1 convex OK, 2 approximation, 0 failed. |
| Message (`M`) | Text | item | NFP status message. |

### Sheet Nest (Hole-Aware)  (`HoleNest`)

- GUID: `D5F10019-8A3C-4D17-B5E2-6C90F2A47D31`  |  icon: `NoFitPolygon.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/TwoD/HoleNestComponent.cs`
- Algorithm: **No-fit-polygon / inner-fit-polygon via Minkowski sum** - Bennell, J.A. & Oliveira, J.F. (2009). "A tutorial in irregular shape packing problems." J. Oper. Res. Soc. 60(S1):S93-S105
- Deterministic hole-aware 2D nester: parts are placed on a sheet with defects (holes) by  exact no-fit-polygon bottom-left-fill, and smaller parts are nested INSIDE the holes of  larger placed parts via the inner-fit region. No-fit and inner-fit polygons are built  exactly as Clipper2 Minkowski sums/erosions (Bennell & Oliveira 2009) and placement is  bottom-left-fill (Burke et al. 2006), so layouts are 0-overlap by construction.  Rotations are contact-adaptive: the uniform base set is extended with edge-alignment  angles against the sheet, the latest neighbour, and host holes so parts seat flush.  Returns valid hole-aware layouts where hole-blind nesters fail; an exact rectangle  shelf fast-path accelerates all-rectangle instances. Deterministic: the same inputs  always reproduce the same cut layout.

| in | type | access | description |
|---|---|---|---|
| Sheets (`S`) | Curve | list | Closed planar sheet boundary curve(s). Multiple sheets nest by greedy overflow: sheet 0  fills first, unplaced parts carry to sheet 1, and so on. Sheets stay at their drawn  positions. |
| Sheet Holes (`SH`) | Curve | tree | Closed sheet defect/hole curves (flat list or tree). Each hole is routed to whichever sheet  geometrically CONTAINS it (tree path {s} is only the fallback) — no tree matching or  grafting required; sheets without holes need nothing. |
| Parts (`P`) | Curve | list | Closed planar part outline curves to nest. |
| Part Holes (`PH`) | Curve | tree | Part hole curves (flat list or tree). Each hole is routed to the SMALLEST part outline that  geometrically CONTAINS it (tree path {i} -> Parts[i] is only the fallback) — no tree  matching or grafting required; parts without holes need nothing. Parts with holes are  placed first as hosts, then smaller parts nest into their holes via the inner-fit region. |
| Spacing (`Gap`) | Number | item | Clearance between parts and boundaries. |
| BaseRotations (`BR`) | Integer | item | Uniform base rotation count (4 = 0/90/180/270 degrees). |
| ContactRotations (`CR`) | Integer | item | Longest-edge count per polygon used to build contact (edge-alignment) rotation angles. |
| Resolution (`Res`) | Integer | item | SOLVER sampling resolution for smooth curves: uniform-by-length vertices per closed curve  (16..200, default 24). This ONLY sets the collision proxy — the Placed output is always the  exact ORIGINAL curve, transformed — so there is no output-quality reason to raise it. Solve  time grows ~QUADRATICALLY with this while packing density is nearly flat (benchmark: 48 verts  was ~10-20x slower than 24 for <2% density gain). Raise it ONLY when small parts must seat  into tight CONCAVE notches; otherwise leave it low for fast nesting. |
| MultiStart (`MS`) | Integer | item | Number of deterministic part orders the general engine tries per sheet, keeping the densest  valid layout (1..4; default 4). Orders: area / max-dimension / width / height, all descending.  1 = the original single largest-first pass. Higher values raise irregular-outline density at a  near-linear wall-time cost (4 orders is ~4x the solve time of 1) and never reduce placements or  validity. The exact rectangle fast-path ignores this (it is already optimal). Output stays  deterministic: identical inputs always reproduce the same layout. |

| out | type | access | description |
|---|---|---|---|
| Placed (`C`) | Curve | list | The ORIGINAL part curves at full resolution, moved to their placed positions (placement  order). The solver works on coarse collision proxies internally; output geometry stays  exact for fabrication. |
| Source (`I`) | Integer | list | For each placed curve, the index of the source curve in the Parts input (labeling/etching map). |
| Transform (`X`) | Transform | list | For each placed curve, the rigid placement transform (rotation about the world Z origin,  then translation). Apply it to the original part curve, its holes, or any decoration. |
| Nested (`N`) | Boolean | list | True where the corresponding placed part was nested into a host part's hole. |
| Report (`R`) | Text | item | Placed count, part-holes filled, density, engine note, elapsed ms, valid flag. |
| Density (`D`) | Number | item | Placed part material area / net sheet area (sheet minus its holes). |
| Valid (`V`) | Boolean | item | True when the final layout passed the independent boolean (path-free) validation. |
| Placed Holes (`CH`) | Curve | tree | The placed parts' own hole curves at full resolution, moved with their parts: branch path  {i} holds the hole curves of Placed[i]. Subtract them from Placed[i] for the true cut profile. |
| Sheet (`Sh`) | Integer | list | For each placed curve, the index of the sheet it landed on (greedy overflow order). |

Related:
- Frahan > 2D Packing > Freeform Sheet Nest (Exact NFP) - Multi-sheet exact NFP-BLF production sibling without part-in-part-hole nesting; use it when parts have no usable holes.


## 3D Packing

### CoM In-Container Check  (`PackComCheck`)

- GUID: `B1C2D3A4-2004-4F5E-A6B7-C8D9E0F12345`  |  icon: `StabilityCheck.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/ScanIngest/PackComCheckComponent.cs`
- Algorithm: **Limit-state CoM-over-support** - Heyman, J. (1966), The Stone Skeleton, Int. J. Solids Struct. 2(2):249-279
- For each placed stone, report whether its centre of mass  (vertex centroid) lies inside the container. Stones with  CoM outside the container are flagged as marginal — they  are likely to tip out of the pack. Stability per Heyman 1966 limit state.

| in | type | access | description |
|---|---|---|---|
| Placed Meshes (`M`) | Mesh | list | Placed stones after a 3D pack. |
| Container (`C`) | Mesh | item | Closed container mesh. |
| Tolerance (`T`) | Number | item | Inside / outside testing tolerance in model units. |

| out | type | access | description |
|---|---|---|---|
| Inside (`In`) | Boolean | list | Per-stone bool: true if CoM is inside the container. |
| Centres of Mass (`CoM`) | Point | list | Per-stone vertex-centroid points. |
| Marginal Ids (`Mr`) | Integer | list | Indices of stones whose CoM lies outside the container. |

### Frahan Stone Descriptor  (`StoneDesc`)

- GUID: `AB12C009-1A2B-4C3D-9E4F-5A6B7C8D9E09`  |  icon: `PackQuality.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/StoneDescriptorComponent.cs`
- Algorithm: **Stone shape-feature descriptor extractor** - Frahan-original
- Convert Rhino meshes into StoneDescriptors. Output is consumable  by Frahan Pack3D and other 3D-packing tools. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Stones (`S`) | Mesh | list | Stone meshes (one descriptor per mesh). |
| Ids (`I`) | Text | list | Per-stone id. Defaults to "stone-{index}" if omitted or shorter than the stone list. |

| out | type | access | description |
|---|---|---|---|
| Descriptors (`D`) | Generic | list | StoneDescriptor per stone (opaque). |
| Mesh Volumes (`Vm`) | Number | list | Per-stone mesh volume (0 if mesh is open). |
| AABB Volumes (`Va`) | Number | list | Per-stone axis-aligned bounding-box volume. |
| Surface Areas (`A`) | Number | list | Per-stone surface area. |
| Aspect Ratios (`Ar`) | Number | list | Per-stone aspect ratio (max/min AABB dimension; >= 1). |
| Compactness (`C`) | Number | list | Per-stone compactness (MeshVol / AabbVol; range (0, 1]). |
| Triangle Counts (`T`) | Integer | list | Per-stone triangle count. |
| Is Closed (`Cl`) | Boolean | list | Per-stone closed-mesh flag. |
| Is Manifold (`Mf`) | Boolean | list | Per-stone manifold flag. |
| Skipped (`Sk`) | Integer | item | Number of stones skipped (null mesh or builder threw). |
| Report (`R`) | Text | item | Summary. |

### Pack3D Irregular  (`Pack3D`)

- GUID: `E36C3F7D-7E2C-495E-9E2A-59312C5CF990`  |  icon: `Pack3D.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Pack3DIrregularComponent.cs`
- Algorithm: **Heightmap-greedy 3D bin packing (deepest-bottom-left family)** - Chehrazad, R., Roose, D., Wauters, T. (2025). "A fast and scalable deepest-left-bottom-fill algorithm." Int. J. Production Research 63:6606-6629
- EVOLVED PATH: for volume packing use Settle 3D (Physics); for saw-cuttable subdivision use Block Pack (Tree). This heightmap packer remains the validated baseline.  Deterministic heightmap packer for early irregular 3D packing workflows. Implements deepest-left-bottom-fill packing (Chehrazad et al. 2025).

| in | type | access | description |
|---|---|---|---|
| Meshes (`M`) | Mesh | list | Meshes to pack. MVP uses each mesh bounding box as the packing proxy. |
| Container (`C`) | Box | item | Container box. |
| Cell Size (`Grid`) | Number | item | Solver grid resolution in model units. 0 (default) = AUTO: derived from the smallest element (min bounding-box edge / 8), so the packer works at any unit/scale. Set a positive value to override. |
| Clearance (`Gap`) | Number | item | Extra XY gap added around each packing proxy in model units. |
| Yaw 90 (`Y90`) | Boolean | item | Try 90 degree yaw rotations. |
| Run (`Run`) | Boolean | item | Run the packer. |

| out | type | access | description |
|---|---|---|---|
| Placed Meshes (`P`) | Mesh | list | Packed mesh duplicates. |
| Transforms (`T`) | Transform | list | Placement transforms. |
| Sequence (`Seq`) | Integer | list | Placement sequence by input index. |
| Info (`Info`) | Text | item | Packing report and failures. |
| Heightmap (`H`) | Mesh | item | Heightmap debug mesh. |
| Pack Result (`PR`) | Generic | item | Opaque PackResult for downstream Frahan Packing Report. |

Related:
- Frahan > 3D Packing > Settle 3D (Physics) - EVOLVED PATH: the canonical volume packer; physically settles real geometry into contact.
- Frahan > Masonry > Block Pack (Tree) - EVOLVED PATH: saw-cuttable guillotine subdivision (Kim 2025).

### Pack3D Irregular Container  (`Pack3DContainer`)

- GUID: `B3E8A42F-F67E-42B5-B3C3-1D1A5A1195C7`  |  icon: `PackIntoBlock.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Pack3DIrregularContainerComponent.cs`
- Algorithm: **Heightmap-greedy 3D bin packing** - Park and Han 2024 tree-packing for 3D-BPP / orthogonal-block packing
- EVOLVED PATH: for volume packing use Settle 3D (Physics); for saw-cuttable subdivision use Block Pack (Tree). This heightmap packer remains the validated baseline.  Mesh-heightmap packer inside a mesh-derived irregular container footprint and height volume. [Park & Han 2024]

| in | type | access | description |
|---|---|---|---|
| Meshes (`M`) | Mesh | list | Meshes to pack using mesh-derived footprint and heightmap proxies. |
| Container Meshes (`C`) | Mesh | list | One or more irregular container meshes. Each top-down footprint and per-cell height defines an allowed packing volume. |
| Cell Size (`Grid`) | Number | item | Solver grid resolution in model units. 0 (default) = AUTO: derived from the smallest element (min bounding-box edge / 8), so the packer works at any unit/scale. Set a positive value to override. |
| Clearance (`Gap`) | Number | item | Extra XY gap added around each mesh footprint in model units. Larger values leave more space between packed parts. |
| Yaw 90 (`Y90`) | Boolean | item | Try 90 degree yaw rotations. |
| Max Candidates (`N`) | Integer | item | Maximum XY/orientation candidates evaluated per mesh. |
| Seed (`Seed`) | Integer | item | 0 is deterministic. Nonzero seeds explore alternative candidate orders. |
| Random Tie (`Rnd`) | Number | item | Small score jitter for seed-driven alternatives. Use 0 for no jitter. |
| Run (`Run`) | Boolean | item | Run the irregular-container packer. |

| out | type | access | description |
|---|---|---|---|
| Placed Meshes (`P`) | Mesh | list | Packed mesh duplicates. |
| Transforms (`T`) | Transform | list | Placement transforms. |
| Sequence (`Seq`) | Integer | list | Placement sequence by input index. |
| Failed Meshes (`Fail`) | Mesh | list | Meshes that could not be placed. |
| Failure Reasons (`Why`) | Text | list | Failure reason for each failed mesh. |
| Info (`Info`) | Text | item | Packing report. |
| Heightmaps (`H`) | Mesh | list | Final pile heightmap debug mesh for each container. |
| Container Cells (`Cells`) | Mesh | list | Allowed container cells for each container shown at ceiling heights. |
| Source Indices (`Src`) | Integer | list | Original input mesh index for each placed mesh and transform. |
| Container Indices (`Con`) | Integer | list | Input container mesh index for each placed mesh and transform. |
| Pack Result (`PR`) | Generic | item | Opaque PackResult for downstream Frahan Packing Report. |

Related:
- Frahan > 3D Packing > Settle 3D (Physics) - EVOLVED PATH: the canonical volume packer; physically settles real geometry into contact.
- Frahan > Masonry > Block Pack (Tree) - EVOLVED PATH: saw-cuttable guillotine subdivision (Kim 2025).

### Pack3D Mesh Heightmap  (`Pack3DMesh`)

- GUID: `A16D6426-38A8-44B1-AB6A-4BA80EB39730`  |  icon: `LayeredPack.png`  |  exposure: `hidden`  |  source: `src/Frahan.StonePack.GH/Pack3DMeshHeightmapComponent.cs`
- Algorithm: **Mesh top/bottom heightmap greedy packing** - Frahan-original
- EVOLVED PATH: for volume packing use Settle 3D (Physics); for saw-cuttable subdivision use Block Pack (Tree). This heightmap packer remains the validated baseline.  Mesh-derived top/bottom heightmap packer with conservative vertical-column collision checks. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Meshes (`M`) | Mesh | list | Meshes to pack using mesh-derived footprint and heightmap proxies. |
| Container (`C`) | Box | item | Container box. |
| Cell Size (`Grid`) | Number | item | Solver grid resolution in model units. 0 (default) = AUTO: derived from the smallest element (min bounding-box edge / 8), so the packer works at any unit/scale without manual tuning. Set a positive value to override (smaller = more detailed but slower). |
| Clearance (`Gap`) | Number | item | Extra XY gap added around each mesh footprint in model units. Larger values leave more space between packed parts. |
| Yaw 90 (`Y90`) | Boolean | item | Try 90 degree yaw rotations. |
| Max Candidates (`N`) | Integer | item | Maximum XY/orientation candidates evaluated per mesh. |
| Seed (`Seed`) | Integer | item | 0 is deterministic. Nonzero seeds explore alternative candidate orders. |
| Random Tie (`Rnd`) | Number | item | Small score jitter for seed-driven alternatives. Use 0 for no jitter. |
| Run (`Run`) | Boolean | item | Run the mesh-heightmap packer. |

| out | type | access | description |
|---|---|---|---|
| Placed Meshes (`P`) | Mesh | list | Packed mesh duplicates. |
| Transforms (`T`) | Transform | list | Placement transforms. |
| Sequence (`Seq`) | Integer | list | Placement sequence by input index. |
| Failed Meshes (`Fail`) | Mesh | list | Meshes that could not be placed. |
| Failure Reasons (`Why`) | Text | list | Failure reason for each failed mesh. |
| Info (`Info`) | Text | item | Packing report. |
| Heightmap (`H`) | Mesh | item | Final pile heightmap debug mesh. |
| Source Indices (`Src`) | Integer | list | Original input mesh index for each placed mesh and transform. |
| Pack Result (`PR`) | Generic | item | Opaque PackResult for downstream Frahan Packing Report. |

Related:
- Frahan > 3D Packing > Settle 3D (Physics) - EVOLVED PATH: the canonical volume packer; physically settles real geometry into contact.
- Frahan > Masonry > Block Pack (Tree) - EVOLVED PATH: saw-cuttable guillotine subdivision (Kim 2025).

### Packed-Pile Stability  (`PackStability`)

- GUID: `B1C2D3A4-2005-4F5E-A6B7-C8D9E0F12345`  |  icon: `StabilityCheck.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/ScanIngest/PackStabilityComponent.cs`
- Algorithm: **Limit-state CoM-over-support** - Heyman, J. (1966), The Stone Skeleton, Int. J. Solids Struct. 2(2):249-279
- Geometric stability proxy for a 3D packed pile. A stone is  marked stable when its centre of mass either rests inside  its own footprint on the floor, or lies inside the union of  the XY footprints of the stones it rests on. Quick check;  for full RBE physics use Frahan Masonry Stability (RBE). Stability per Heyman 1966 limit state.

| in | type | access | description |
|---|---|---|---|
| Placed Meshes (`M`) | Mesh | list | Placed stones after a 3D pack. |
| Up (`U`) | Vector | item | World up vector. Default world Z+. |
| Floor Z (`Z0`) | Number | item | Z coordinate of the floor plane. |
| Z Tolerance (`Tz`) | Number | item | How close (in model units) a candidate supporter's top must  be to the supported stone's bottom for contact to count. |

| out | type | access | description |
|---|---|---|---|
| Stable (`S`) | Boolean | list | Per-stone stability verdict. |
| Falling Ids (`F`) | Integer | list | Indices of stones flagged unstable (CoM outside all supports). |
| All Stable (`OK`) | Boolean | item | True iff every stone passes. |

### Per-Stone Overlap  (`PackOverlap`)

- GUID: `B1C2D3A4-2003-4F5E-A6B7-C8D9E0F12345`  |  icon: `Pack3DNfp.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/ScanIngest/PackOverlapComponent.cs`
- For each placed stone, report the fraction of its vertices  that lie strictly inside another placed stone. Useful as a  cheap penetration check after a 3D pack — anything > ~1%  indicates real overlap (mis-placement or solver bug).

| in | type | access | description |
|---|---|---|---|
| Placed Meshes (`M`) | Mesh | list | Placed stones after a 3D pack. Open meshes are skipped (no  inside / outside distinction). |
| Tolerance (`T`) | Number | item | Inside / outside testing tolerance in model units. |

| out | type | access | description |
|---|---|---|---|
| Overlap Fractions (`O`) | Number | list | Per-stone fraction of vertices inside another stone, in [0, 1]. |
| Penetrating Ids (`P`) | Integer | list | Indices of stones whose overlap fraction exceeds the warning  threshold (1% of vertices). |
| Max Overlap (`Mx`) | Number | item | Worst-case per-stone overlap fraction. |

### Settle 3D (Physics)  (`Settle3D`)

- GUID: `134785AC-19CB-4F14-85F8-E2F666BD14F6`  |  icon: `PackIntoBlock.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/PackSettle3DComponent.cs`
- Algorithm: **Rigid-body physics settle of irregular stone piles** - Zhuang, Q., Chen, Z., He, K., Cao, J., Wang, W. (2024). "Dynamics Simulation-Based Packing of Irregular 3D Objects." Computers and Graphics 123:103996
- The canonical Frahan volume packer (evolved path; the heightmap Pack3D components remain the validated baseline).  Physically settles an already-placed pack of stone meshes into real 3D contact  with a Bullet rigid-body simulation (convex-decomposition collision, gravity,  friction). Compose after any 3D packer to turn a heightmap/proxy placement into a  settled, stable, non-interpenetrating pile of real geometry. Bullet backend  (better than Kangaroo for stacking); needs libbulletc.dll beside the .gha. [Zhuang et al. 2024]

| in | type | access | description |
|---|---|---|---|
| Meshes (`M`) | Mesh | list | Already-placed stone meshes to settle. |
| Container (`C`) | Mesh | item | Container mesh; its bounding box is the settle box (floor + walls). Optional; defaults to the meshes' bounds. |
| Friction (`Fr`) | Number | item | Coulomb friction. |
| Settle Steps (`St`) | Integer | item | Physics steps after the gravity ramp. |
| Tamp (`Tp`) | Integer | item | Vertical tamp rounds (densify). |
| CoACD (`Cx`) | Boolean | item | Convex-decompose each stone (else convex hull). |
| Run (`Run`) | Boolean | item | Run the settle. |

| out | type | access | description |
|---|---|---|---|
| Settled Meshes (`S`) | Mesh | list | Meshes after physics settle. |
| Transforms (`X`) | Transform | list | Settle transform per input mesh. |
| Source Indices (`Src`) | Integer | list | Input mesh index per settled mesh. |
| Report (`R`) | Text | item | Settle report. |

### Validate Packed Transform  (`PackXformCheck`)

- GUID: `2AE8987D-83E5-471C-B82F-8A19EC57492A`  |  icon: `StabilityCheck.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/ValidatePackedTransformComponent.cs`
- Debugs StonePack transforms by comparing source mesh + transform against placed mesh output.

| in | type | access | description |
|---|---|---|---|
| Source Meshes (`S`) | Mesh | list | Original source meshes sent into the packer. |
| Placed Meshes (`P`) | Mesh | list | Placed meshes output by the packer. |
| Transforms (`T`) | Transform | list | Transforms output by the packer. |
| Source Indices (`Src`) | Integer | list | Source index output by the packer. |
| Tolerance (`Tol`) | Number | item | Validation tolerance in model units. |

| out | type | access | description |
|---|---|---|---|
| Transformed Sources (`TS`) | Mesh | list | Source meshes after applying the supplied transforms. |
| Max Vertex Error (`Err`) | Number | list | Maximum same-index vertex distance between transformed source and placed mesh. |
| Bounding Box Error (`BBox`) | Number | list | Maximum min/max bounding-box corner distance. |
| Valid (`OK`) | Boolean | list | True when vertex and bounding-box errors are within tolerance. |
| Report (`Info`) | Text | item | Validation summary. |


## Analysis

### Frahan Boundary Rail Index  (`RailIdx`)

- GUID: `AB12C001-1A2B-4C3D-9E4F-5A6B7C8D9E01`  |  icon: `BoundarySegmenter.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/BoundaryRailIndexComponent.cs`
- Algorithm: **Boundary-rail affinity bucketing** - Frahan-original
- Diagnostic component. Build a boundary-rail index from one or  more boundary curves; each curve is sliding-window-sampled into  (length, tangent angle, curvature) buckets and stored as a  BoundaryIntervalInfo. The unified Frahan Sheet Pack now builds  this index internally when Boundary Mode is on; this standalone  component is kept for index inspection and ad-hoc analysis. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Boundaries (`B`) | Curve | list | One or more boundary curves to index. |
| Outer Flags (`O`) | Boolean | list | Per-boundary flag: true = outer outline, false = hole.  If shorter than the boundary list, the last value is repeated. |
| Zone Buckets (`Z`) | Integer | list | Per-boundary zone bucket. Used to group boundaries (sheets, regions).  If shorter than the boundary list, the last value is repeated.  Defaults to all-zeros if omitted. |
| Window Length (`W`) | Number | item | Sliding-window length along each curve (model units). |
| Step Length (`S`) | Number | item | Sliding-window step (model units). |
| Length Bucket Size (`Lb`) | Number | item | EdgeKey length bucket size (model units). |
| Angle Bucket Size (`Ab`) | Number | item | EdgeKey angle bucket size (degrees). |
| Curvature Bucket Size (`Cb`) | Number | item | EdgeKey curvature bucket size (1 / radius). |

| out | type | access | description |
|---|---|---|---|
| Index (`I`) | Generic | item | Populated BoundaryRailIndex&lt;BoundaryIntervalInfo&gt; (opaque). |
| Interval Count (`N`) | Integer | item | Total intervals added to the index. |
| Key Count (`K`) | Integer | item | Distinct EdgeKey buckets in the index. |
| Known Zones (`Zk`) | Integer | list | Distinct zone buckets observed. |
| Report (`R`) | Text | item | Human-readable summary. |

### Frahan Fragment Descriptors  (`FragDesc`)

- GUID: `AB12C007-1A2B-4C3D-9E4F-5A6B7C8D9E07`  |  icon: `FragmentCluster.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/FragmentDescriptorsComponent.cs`
- Algorithm: **Fragment shape descriptor extraction** - Frahan-original
- Diagnostic component. Convert closed planar Rhino curves into  FragmentDescriptors with per-edge EdgeDescriptors. The unified  Frahan Sheet Pack now builds these internally when Boundary Mode  is on; use this standalone component to inspect descriptors for  ad-hoc analysis. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Fragments (`F`) | Curve | list | Closed planar fragment curves. |
| Zone Buckets (`Z`) | Integer | list | Per-fragment zone bucket. Defaults to all-zeros if omitted. |
| Discretisation Tolerance (`T`) | Number | item | Tolerance for ToPolyline conversion. |

| out | type | access | description |
|---|---|---|---|
| Descriptors (`D`) | Generic | list | FragmentDescriptor per fragment (opaque). |
| Areas (`A`) | Number | list | Polygon area per fragment. |
| Perimeters (`P`) | Number | list | Perimeter per fragment. |
| Aspect Ratios (`Ar`) | Number | list | Aspect ratio per fragment. |
| Edge Counts (`E`) | Integer | list | Edge count per fragment. |
| Skipped (`Sk`) | Integer | item | Number of fragments skipped (degenerate). |
| Report (`R`) | Text | item | Summary. |

### Frahan Fragment Edge Match  (`FragMatch`)

- GUID: `AB12C003-1A2B-4C3D-9E4F-5A6B7C8D9E03`  |  icon: `EdgeMatchSolve.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/FragmentEdgeMatchComponent.cs`
- Algorithm: **Boundary-rail edge affinity scoring** - Frahan-original
- Diagnostic component. Match each fragment curve's polyline edges  against a populated BoundaryRailIndex; returns ranked affinity  scores per fragment per edge. The unified Frahan Sheet Pack now  matches internally when Boundary Mode is on; use this component  to inspect scores externally. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Index (`I`) | Generic | item | Populated BoundaryRailIndex<BoundaryIntervalInfo> from Frahan Boundary Rail Index. |
| Fragments (`F`) | Curve | list | Closed planar fragment curves to query. |
| Zone Buckets (`Z`) | Integer | list | Per-fragment zone bucket. Defaults to all-zeros if omitted. |
| Length Bucket Size (`Lb`) | Number | item | Must match the source index's bucket size. |
| Angle Bucket Size (`Ab`) | Number | item | Must match the source index's bucket size (degrees). |
| Curvature Bucket Size (`Cb`) | Number | item | Must match the source index's bucket size. |
| Length Radius (`Lr`) | Integer | item | How many length-buckets to widen on each side. |
| Angle Radius (`Ar`) | Integer | item | How many angle-buckets to widen on each side. |
| Preserve Zone (`Pz`) | Boolean | item | If true, only match within each fragment's zone. |
| Top K (`K`) | Integer | item | Maximum matches per edge (0 = unlimited). |
| Min Affinity Score (`M`) | Number | item | Filter out matches with score below this threshold. |
| Discretisation Tolerance (`T`) | Number | item | Tolerance for fragment ToPolyline conversion. |

| out | type | access | description |
|---|---|---|---|
| Top Score Per Edge (`S`) | Number | tree | DataTree: branch per fragment, one number per fragment edge = best affinity score. |
| Match Count Per Edge (`N`) | Integer | tree | DataTree: branch per fragment, one int per fragment edge = number of matches kept. |
| Edge Counts (`E`) | Integer | list | Number of edges per fragment. |
| Total Matches (`Tm`) | Integer | item | Total matches summed across every fragment edge. |
| Report (`R`) | Text | item | Summary. |


## EdgeMatch

### Adaptive Block Match 3D  (`AdaptBlk3D`)

- GUID: `D5F1000A-ED9E-4ED9-A00A-ED9EED9E000A`  |  icon: `EdgeMatchSolve.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/EdgeMatch3D/AdaptiveBlockMatch3DComponent.cs`
- Algorithm: **Block Pair Match 3D** - See BlockPairMatch3DComponent for B3D pipeline references
- 3D sibling of Component C. Given two scanned stone blocks where  one is oversized for its slot, find the best mating pose via  Block Pair Match 3D, then carve a minimum volume from the candidate  (CGAL/Geogram boolean diff) to make it fit. Mirrors the Clifford- McGee 2017 Cyclopean Cannibalism overlap-then-carve discipline and  the UCL Devadass 2025 minimum-machining principle.

| in | type | access | description |
|---|---|---|---|
| Slot (`Sl`) | Mesh | item | Target slot mesh (the neighbour the candidate must mate against). |
| Candidate (`Ca`) | Mesh | item | Oversized candidate mesh to trim. |
| Trim Style (`Ts`) | Integer | item | 0=Planar cut (single plane; saw / stone-friendly, default per UCL SS2.7).  1=Polyline / free-form sculpt (router / wood / swarf-machining). |
| Max Trim Volume Ratio (`Mtv`) | Number | item | Trim volume budget as a fraction of candidate volume.  Component rejects placement if trim would exceed this. Default 0.1 (10 %). |
| Match Tolerance (`Mt`) | Number | item | Post-trim joint Hausdorff tolerance (mm). |

| out | type | access | description |
|---|---|---|---|
| Placed Block (`Pb`) | Mesh | item | Trimmed and placed candidate mesh. |
| Trim Diff (`Td`) | Mesh | item | The carved-away material (CGAL boolean difference result). |
| Trim Volume (`Tv`) | Number | item | Volume carved away (cubic mm). |
| Trim Ratio (`Tr`) | Number | item | Trim volume as fraction of source candidate volume. |
| Joint Residual (`Jr`) | Number | item | Post-trim Hausdorff residual at the matched face pair (mm). |
| Remarks (`Rm`) | Text | list | Diagnostic notes -- which face-pair matched, whether the trim  stayed inside the volume budget, etc. |

### Block Chain Along Thrust Line  (`BlkChain3D`)

- GUID: `D5F10009-ED9E-4ED9-A009-ED9EED9E0009`  |  icon: `EdgeMatchSolve.png`  |  exposure: `hidden`  |  source: `src/Frahan.StonePack.GH/EdgeMatch3D/BlockChainAlongThrustLine3DComponent.cs`
- Algorithm: **Bidirectional rail walker** - Frahan-original sequential placement state machine
- Bidirectional 3D walker placing scanned stones along a designer- supplied thrust line (catenary, parabola, spline). One stone per  station; Block Pair Match 3D is the per-station atomic call.  Strategy=Pareto runs NSGA-II on three UCL-paper objectives  (angle deviation / Cg deviation / endpoint deviation). The  canonical implementation of the UCL Bartlett 18-stone arch  workflow (em_3d_chain_ucl_bartlett HITL card-set).

| in | type | access | description |
|---|---|---|---|
| Stone Inventory (`I`) | Mesh | list | Filtered list of scanned-stone meshes. Apply area + internal-angle  filters upstream (UCL Devadass 2025 SS2.4) before wiring here. |
| Thrust Curve (`Tc`) | Curve | item | Designer-supplied thrust line (catenary, parabola, spline). The walker  places one stone per station evaluated along this curve. |
| Strategy (`St`) | Integer | item | 0=Greedy, 1=Beam (default), 2=Pareto (NSGA-II three-objective). |
| Direction (`Dr`) | Integer | item | 0=Forward, 1=Backward, 2=Bidirectional (default, OQ1-locked 2026-05-31). |
| Match Tolerance (`Mt`) | Number | item | Per-station Hausdorff match tolerance (mm). |
| Beam Width (`Bw`) | Integer | item | Beam width (Strategy=Beam) or population size (Strategy=Pareto). |

| out | type | access | description |
|---|---|---|---|
| Placed Stones (`Ps`) | Mesh | list | Per-station placed stone meshes (transformed). |
| Stone Indices (`Si`) | Integer | list | Per-station inventory index used. |
| Per-Station Residual (`Rs`) | Number | list | Per-station match residual (mm). |
| Pareto Front (`Pf`) | Point | list | Strategy=Pareto only: 3D points where (x=angle deviation,  y=Cg deviation, z=endpoint deviation) per solution. |
| Endpoint Deviation (`Ed`) | Number | item | Final placed-chain endpoint distance from designed thrust-line endpoint (mm). |
| Remarks (`Rm`) | Text | list | Per-station diagnostic notes + strategy convergence flags. |

### Block Pair Match 3D  (`BlkMatch3D`)

- GUID: `D5F10008-ED9E-4ED9-A008-ED9EED9E0008`  |  icon: `EdgeMatchSolve.png`  |  exposure: `hidden`  |  source: `src/Frahan.StonePack.GH/EdgeMatch3D/BlockPairMatch3DComponent.cs`
- Algorithm: **Phase correlator FFT (3D)** - Classical cross-correlation lag estimation
- First-cut matcher: VSA segmentation + plane-to-plane mating scored by sampled  Hausdorff distance. The full exhaustive face-pair search is a planned refinement.  For a practically-tested matcher use Stone-Cell Match (Λ)  (ETH1100 Lambda=0.194, card 27_07).  Atomic 3D edge-matching primitive: given two scanned stone meshes,  find the rigid 3D pose where their planar face patches mate.  VsaSegmenter -> face filtering -> per-pair PhaseCorrelator +  ConstrainedIcp3D refinement -> top-N candidates ranked by  patch-pair Hausdorff residual + match-length. Foundational  primitive for the 3D EdgeMatch family (Block Chain, Adaptive  Block Match, Template Block Match, Cyclopean Recipe Coursing). [Cohen-Steiner et al. 2004]

| in | type | access | description |
|---|---|---|---|
| Block A (`A`) | Mesh | item | First scanned-stone mesh. Closed manifold preferred; algorithm tolerates  open meshes but face-pair coverage may suffer. |
| Block B (`B`) | Mesh | item | Second scanned-stone mesh. Same constraints as Block A. |
| Min Face Area (`Mfa`) | Number | item | Minimum face-patch area (mm^2) below which patches are dropped.  Default 15,000 mm^2 per UCL Devadass 2025 SS2.4.1 (stability filter). |
| Normal Merge Angle (`Nma`) | Number | item | VSA segmenter's adjacent-normal merge angle threshold (radians).  Coarser = fewer larger patches. Default 0.35 rad (~20 deg). |
| Max Candidates (`Mc`) | Integer | item | Maximum number of MatchResult candidates to emit (sorted by residual ascending). |
| Match Tolerance (`Mt`) | Number | item | Match residual cutoff (mm). Candidates with residual > this are rejected. |

| out | type | access | description |
|---|---|---|---|
| Transforms (`T`) | Transform | list | Per-candidate rigid 3D transform that places Block B against Block A. |
| Residuals (`R`) | Number | list | Per-candidate Hausdorff residual on the matched patch pair (mm). |
| Match Areas (`Ma`) | Number | list | Per-candidate area of the matched patch (square mm). |
| Remarks (`Rm`) | Text | list | Per-candidate diagnostic notes (which face-pair matched, etc.)  plus rejection reasons if no candidate exceeds the tolerance. |

Related:
- Frahan > Masonry > Stone-Cell Match (Λ) - The practically-tested matcher: Hungarian stone-to-cell assignment, ETH1100 Lambda=0.194 (card 27_07). Use it for real stone-to-target matching today.

### Cyclopean Recipe Coursing  (`CycRecipe`)

- GUID: `D5F1000C-ED9E-4ED9-A00C-ED9EED9E000C`  |  icon: `EdgeMatchSolve.png`  |  exposure: `hidden`  |  source: `src/Frahan.StonePack.GH/EdgeMatch3D/CyclopeanRecipeCoursingComponent.cs`
- Algorithm: **Largest 4-sided inscribed polygon** - Clifford 2017 The Cannibal's Cookbook pp. 118-120 recursive algorithm
- The bottom-up 3D peer with no 2D analog. Encodes the Clifford- McGee 2017 Cyclopean Cannibalism 8-step recipe verbatim. Inputs:  scanned rubble inventory + wall envelope + variable-thickness  back-plane + course height. Outputs: placed stones with  trapezoid/parallelogram/keystone recipe-step tags + Utah-detail  scribe curves per bed joint + dowel insertion vectors. The 3D  flagship bottom-up component, mirroring cyclopean masonry  principles per Libish 2026-05-31 directive.

| in | type | access | description |
|---|---|---|---|
| Rubble Inventory (`RI`) | Mesh | list | List of scanned-stone meshes (demolition rubble, quarry off-cuts). |
| Wall Envelope (`WE`) | Brep | item | Primary wall Brep (front face). The form the recipe builds against. |
| Back Plane (`BP`) | Brep | item | Variable-thickness back-plane offset Brep. The recipe's stones  extend from Wall Envelope to Back Plane; thickness varies by location. |
| Course Height (`CH`) | Number | item | Average course height (mm). Drives the number of horizontal rows. Default 400 mm. |
| Min Face Area (`MFA`) | Number | item | VsaSegmenter min-face-area threshold (mm^2) for shape classification. Default 15,000 mm^2. |
| Strategy (`St`) | Integer | item | 0=Greedy recipe, 1=Pareto (NSGA-II three-objective: coverage / stability / variable-thickness fit). Default 0. |
| Allow Trim (`AT`) | Boolean | item | If true, calls Component C3D (Adaptive Block Match 3D) for the  overlap-then-carve recipe step 7. If false, stones placed without trim. |

| out | type | access | description |
|---|---|---|---|
| Placed Stones (`PS`) | Mesh | list | Per-stone placed and trimmed mesh (transformed into wall position). |
| Recipe Step (`RS`) | Text | list | Per-stone recipe-step tag: 'trapezoid_seed' / 'parallelogram_fill' / 'keystone_inverted_trapezoid'. |
| Utah Curves (`UC`) | Curve | list | Per-joint Utah-detail scribe curves (the bed-joint signatures  carved into the course below). Empty if Allow Trim = false. |
| Dowel Positions (`DP`) | Point | list | Stainless-steel structural alignment dowel insertion points  (3-inch cord-drilled holes per Quarra Emanuel 9 + Cyclopean Cannibalism). |
| Dowel Vectors (`DV`) | Vector | list | Per-stone dowel insertion vectors (vertical setting direction). |
| Stone Indices (`SI`) | Integer | list | Per-stone inventory-index used (parallels Placed Stones). |
| Coverage (`Cv`) | Number | item | Fraction of Wall Envelope's projected area covered by placed stones (0-1). |
| Remarks (`R`) | Text | list | Per-stone diagnostic notes + recipe-rule-violation flags +  stability-check results. |

### EdgeMatch Options  (`EMOpts`)

- GUID: `D5F10003-ED9E-4ED9-A003-ED9EED9E0003`  |  icon: `EdgeMatchOptions.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/EdgeMatchOptionsComponent.cs`
- Algorithm: **Agglomerative pair-graph assembly** - Frahan-original minimum-residual spanning tree
- Bundle the EdgeMatch solver's advanced AssemblyOptions flags  (assembly mode, scale-relative gates, partial sub-segment  matching, overlap resolve, Soft-ICP rim-contact refine, and the  WIP projection bootstrap) into one AssemblyOptions DTO. Wire into  EdgeMatch Solve's optional Opt input. Every input is optional and  defaults to the Core default, so an empty component emits the  default options (unchanged behaviour).

| in | type | access | description |
|---|---|---|---|
| Agglomerative (`Ag`) | Boolean | item | Assembly mode. FALSE (default) = FrameAnchored beam (2D Trencadís;  every existing canvas + test path). TRUE = Agglomerative pairwise  spanning-tree assembly for free 3D fragment reassembly. |
| Non-Crossing Max Gap (`Ng`) | Integer | item | Index-band bound for the monotone non-crossing DP. 0 (default) =  unbounded. Only consulted when Non-Crossing is on at the solver. |
| Phase Score Threshold (`Ps`) | Number | item | Minimum phase-correlator similarity to accept a candidate pair.  Default 0.5 (the original hardcoded gate). |
| Residual Threshold Factor (`Rf`) | Number | item | When > 0 the residual gate becomes factor * objectScale (bbox  diagonal), making acceptance scale-relative. 0 (default) keeps the  absolute Residual Threshold from the solver. Suggested ~0.01. |
| Emit Partials (`Ep`) | Boolean | item | When TRUE the segmenters also emit shorter sub-windows so a long  edge can mate a short complementary edge. FALSE (default) =  candidate generation identical to before. |
| Partial Fractions (`Pf`) | Number | list | Partial window lengths as fractions of each base segment span.  Default {0.5, 0.25}. Only consulted when Emit Partials is on. |
| Partial Stride Fraction (`Pst`) | Number | item | Stride between consecutive partial windows as a fraction of the  window length. Default 1.0 (non-overlapping tiling). Only consulted  when Emit Partials is on. |
| Overlap Penalty (`Op`) | Number | item | When > 0 a candidate's score is penalised by penalty * overlap  area fraction, lowering overlapping placements. 0 (default) = no  penalty. A working value is ~1.0. |
| Edge Exclusivity (`Ex`) | Boolean | item | When TRUE a placed panel's matched segment is consumed so two  pieces cannot snap to the same placed edge. FALSE (default) = a  segment can be reused (existing behaviour). |
| Resolve Overlap (`Ro`) | Boolean | item | When TRUE the caller runs a post-solve 2D rigid depenetration  polish (translation only, anchor-locked) until pairwise overlap is  within tolerance. FALSE (default) = no polish. |
| Resolve Overlap Tolerance (`Rot`) | Number | item | Target max pairwise overlap area as a fraction of the smaller  contour, for the Resolve Overlap polish. Default 0.001 (0.1%). |
| Resolve Overlap Iterations (`Roi`) | Integer | item | Max relaxation iterations for the Resolve Overlap polish.  Default 50. |
| Resolve Overlap Relaxation (`Ror`) | Number | item | Per-iteration step factor in (0,1] for the Resolve Overlap polish.  Lower = stabler but slower. Default 0.5. |
| Soft-ICP Refine (`Si`) | Boolean | item | When TRUE the caller runs the Soft-ICP refiner after the solve to  pull open-mesh rims into contact with a non-penetration hinge.  FALSE (default) = no refine. Only the keys below are exposed; other  SoftIcpOptions fields keep their Core defaults. |
| Soft-ICP Tau0 Factor (`Si0`) | Number | item | Initial CPD temperature tau0 = factor * (median rim spacing)^2.  Larger = softer / wider start. Default 4.0. |
| Soft-ICP Tau Anneal (`SiA`) | Number | item | Geometric anneal factor applied to tau each iteration, in (0,1).  Default 0.8. |
| Soft-ICP Correspondence Radius Factor (`SiR`) | Number | item | Contact correspondence radius = factor * (median rim spacing).  Neighbours beyond contribute zero weight. Default 3.0. 0 = no cutoff. |
| Soft-ICP Contact Weight (`SiC`) | Number | item | Weight w_contact of the contact term. Default 1.0. |
| Soft-ICP Penetration Weight (`SiP`) | Number | item | Hinge weight w_pen / lambda for the non-penetration term.  Default 1.0. |
| Soft-ICP Max Iterations (`SiI`) | Integer | item | Max outer EM iterations for the Soft-ICP refiner. Default 40. |
| Projection Bootstrap (`Pb`) | Boolean | item | WIP / UNVERIFIED. When TRUE the caller bootstraps 3D candidate  pairs by per-facet 2D projection + lift (agglomerative 3D path  only). FALSE (default) = no projection bootstrap. Needs its own  HITL before any visual-correctness claim. |
| Projection Sample Spacing Factor (`Pbs`) | Number | item | Resample spacing for the projected 2D rim as a fraction of the  loop bbox diagonal. Default 0.02. |
| Projection Planarity Factor (`Pbp`) | Number | item | Planarity flag threshold for a projected rim as a fraction of the  loop bbox diagonal. Default 0.05. |
| Projection Verify Factor (`Pbv`) | Number | item | 3D verification gate for a lifted pair as a fraction of the  projected-rim scale. Default 0.12. |

| out | type | access | description |
|---|---|---|---|
| Options (`O`) | Generic | item | AssemblyOptions DTO bundling the advanced EdgeMatch flags. Wire  into EdgeMatch Solve's optional Opt input. When wired, the solver  copies these advanced fields onto the options it builds from its  simple inputs; the simple inputs keep owning the basic fields. |

Related:
- Frahan > EdgeMatch > EdgeMatch Solve - Consumes this Options DTO on its optional Opt input to override the advanced fields
- Frahan > Kintsugi > Frahan Kintsugi - 3D fragment reassembly that shares the agglomerative + Soft-ICP refine machinery these knobs tune
- Frahan > EdgeMatch > Trencadis EdgeMatch - 2D Trencadís edge-matching that runs the same FrameAnchored beam these options tune

### EdgeMatch Segments  (`EMSegs`)

- GUID: `D5F10002-ED9E-4ED9-A002-ED9EED9E0002`  |  icon: `BoundarySegmenter.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/EdgeMatchSegmentsComponent.cs`
- Algorithm: **Boundary segmenter** - Frahan-original arc-length curvature/torsion signature
- Run the EdgeMatch boundary segmenter on one curve and expose  the per-segment polylines and signatures. Auto-dispatches between  the 2D planar segmenter and the 3D Frenet-invariant segmenter  based on the curve's best-fit planarity.

| in | type | access | description |
|---|---|---|---|
| Curve (`C`) | Curve | item | Closed planar or spatial polyline-convertible curve. |
| Planarity Tolerance (`Pt`) | Number | item | RMS planarity threshold (mm) deciding the 2D vs 3D path. |
| Sample Spacing (`Sp`) | Number | item | Arc-length sample spacing (mm). |
| Break Angle (`Ba`) | Number | item | Curvature break threshold in degrees per window. |
| Min Segment Length (`Ms`) | Number | item | Below this chord length a segment is treated as noise. |
| Signature Bins (`Sb`) | Integer | item | Resampled signature length (power of 2 recommended). |

| out | type | access | description |
|---|---|---|---|
| Segments (`Sg`) | Curve | list | One polyline per detected segment. |
| Chord Lengths (`L`) | Number | list | Per-segment chord length. |
| Total Turning (`T`) | Number | list | Per-segment signed turning integral. |
| Sign (`Sn`) | Integer | list | +1 convex (relative to panel interior), -1 concave. |
| Turning Signatures (`Tg`) | Number | tree | One branch per segment; resampled signed-turning signal. |
| Curvature Signatures (`Kg`) | Number | tree | One branch per segment; |turning| for the planar path or discrete Frenet curvature for the 3D path. |
| Torsion Signatures (`Wg`) | Number | tree | One branch per segment; populated only when the curve takes the 3D path. |
| Mode (`Md`) | Text | item | Detected panel mode: Planar2D or Spatial3D. |
| Planarity RMS (`Rm`) | Number | item | Best-fit plane RMS (mm) for the input curve. |

### EdgeMatch Solve  (`EMSolve`)

- GUID: `D5F10001-ED9E-4ED9-A001-ED9EED9E0001`  |  icon: `EdgeMatchSolve.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/EdgeMatchSolveComponent.cs`
- Algorithm: **Boundary segmenter** - Frahan-original arc-length curvature/torsion signature
- Edge-matching beam search for Trencadís shards or live-edge planks.  Anchors against a frame curve, places each candidate using ICP-refined  complementary-edge matches, and emits the placement transform set.

| in | type | access | description |
|---|---|---|---|
| Frame (`Fr`) | Curve | item | Closed boundary curve. Anchored at the identity transform; shards match against it first. |
| Shards (`S`) | Curve | list | Closed shard / plank curves to place. |
| Substrate (`Sb`) | Brep | item | Optional curved substrate Brep. Only consulted by the 3D ICP path; pass nothing for flat assemblies. |
| Planarity Tolerance (`Pt`) | Number | item | RMS planarity threshold (mm). Below this, panels take the 2D path. |
| Sample Spacing (`Sp`) | Number | item | Arc-length sample spacing along each contour (mm). |
| Break Angle (`Ba`) | Number | item | Curvature break-point threshold in degrees per window. |
| Min Segment Length (`Ms`) | Number | item | Below this chord length, a segment is treated as noise and discarded. |
| Residual Threshold (`Rt`) | Number | item | Maximum mean point-to-point ICP residual for an accepted match (mm). |
| Beam Width (`Bw`) | Integer | item | Number of concurrent beam states retained between iterations. |
| Max Iterations (`Mi`) | Integer | item | Maximum outer-loop iterations. |
| Run (`R`) | Boolean | item | Execute the solver. |
| Non-Crossing (`Nc`) | Boolean | item | Order-preserving rim correspondence. FALSE (default) = free  nearest-point ICP (unchanged behaviour). TRUE = monotone,  non-crossing point pairing between rims (OrderedBoundaryMatcher);  more robust on wiggly / noisy rims where free matching tangles. |
| Options (`Opt`) | Generic | item | Optional AssemblyOptions DTO from EdgeMatch Options. When wired,  its advanced flags (Mode, scale-relative gates, partial sub-segment  matching, overlap resolve, Soft-ICP refine, projection bootstrap)  override the defaults; the simple inputs above keep owning the basic  fields. Leave disconnected for unchanged behaviour. |

| out | type | access | description |
|---|---|---|---|
| Placed (`P`) | Curve | list | Shard contours transformed by their solved placements. Frame is included as Identity. |
| Transforms (`X`) | Transform | list | Per-panel rigid transform. |
| Ids (`Id`) | Text | list | Panel ids matching the Placed and Transforms order. |
| Modes (`Md`) | Text | list | Per-panel mode: Planar2D or Spatial3D. |
| Planarity RMS (`Rm`) | Number | list | Per-panel best-fit plane RMS (mm). |
| Residuals (`Re`) | Number | list | Per-placement ICP residual. Length = placed shard count (frame excluded). |
| Total Residual (`Tr`) | Number | item | Sum of per-placement residuals. |
| Report (`Rp`) | Text | item | Human-readable summary of the solve. |

### Live Edge Classify  (`LEClassify`)

- GUID: `D5F10043-ED9E-4ED9-A043-ED9EED9E0043`  |  icon: `LiveEdgeClassify.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/LiveEdgeClassifyComponent.cs`
- Algorithm: **Live/sawn straight-run classifier** - Frahan-original
- Classify a wood-offcut outline into LIVE (curvy, natural) edges and SAWN (straight, machine-cut) ends,  for live-edge flooring 2D edge matching. Robust to live-edge wiggles: the two longest straight runs are  taken as the sawn ends and the two arcs between them as the live edges.

| in | type | access | description |
|---|---|---|---|
| Outline (`O`) | Curve | item | Closed offcut outline (one board). |

| out | type | access | description |
|---|---|---|---|
| Live edges (`L`) | Curve | list | The two LIVE (curvy) edges. |
| Sawn edges (`S`) | Curve | list | The two SAWN (straight) ends. |
| Corners (`C`) | Point | list | The four detected corners. |
| Straightness (`St`) | Number | list | Per-edge chord/arc-length (~1 = straight). |

Related:
- Frahan > EdgeMatch > Live Edge Stagger Layup - End-to-end floor that consumes classified offcuts.

### Live Edge Match  (`LEMatch`)

- GUID: `D5F10044-ED9E-4ED9-A044-ED9EED9E0044`  |  icon: `LiveEdgeMatch.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/LiveEdgeMatchComponent.cs`
- Algorithm: **Scribe-trim cost matcher** - Frahan-original
- Assign a pool of offcuts to the staggered live-edge course slots and report the assignment plus per-board  scribe trim. Mode 0 = greedy, Mode 1 = Hungarian (global minimum total trim). No Outlines -> a demo pool.

| in | type | access | description |
|---|---|---|---|
| Outlines (`O`) | Curve | list | Offcut outline pool (closed curves). Empty -> demo pool. |
| Floor width (`W`) | Number | item | Floor width along the courses. |
| Courses (`C`) | Integer | item | Number of courses (rows). |
| Course height (`H`) | Number | item | Nominal course height. |
| Seed (`S`) | Integer | item | Deterministic seed. |
| Mode (`M`) | Integer | item | 0 = Greedy, 1 = Hungarian (global min-trim). |
| Run (`R`) | Boolean | item | Set true to solve the assignment. |

| out | type | access | description |
|---|---|---|---|
| Pool index (`I`) | Integer | list | Offcut pool index assigned to each placed slot (placement order). |
| Course (`C`) | Integer | list | Course (row) of each placed slot. |
| Trim (`T`) | Number | list | Mean scribe trim per placed board. |
| Mean trim (`Mt`) | Number | item | Mean scribe trim over the floor. |
| Max trim (`Mx`) | Number | item | Max scribe deviation over the floor. |
| Placed (`P`) | Integer | item | Number of boards placed. |
| Rivers (`Rv`) | Curve | list | The live-edge seams between courses. |

Related:
- Frahan > EdgeMatch > Live Edge Stagger Layup - Builds the floor geometry from this assignment.
- Frahan > Voussoir > Template Panel Match - Same HungarianAssigner, 3D top-down version.

### Live Edge Stagger Layup  (`LEStagger`)

- GUID: `D5F10046-ED9E-4ED9-A046-ED9EED9E0046`  |  icon: `LiveEdgeStagger.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/LiveEdgeStaggerComponent.cs`
- Algorithm: **Brick-bond scribe layup** - Frahan-original
- Lay a pool of wood offcuts into a staggered live-edge floor: live edges matched along the course as  continuous wavy seams, short sawn butt joints staggered brick-bond, each board scribe-trimmed to fit.  Mode 0 = greedy, Mode 1 = Hungarian (global min-trim). No Outlines -> a demo pool is synthesised.

| in | type | access | description |
|---|---|---|---|
| Outlines (`O`) | Curve | list | Offcut outline pool (closed curves). Empty -> demo pool. |
| Floor width (`W`) | Number | item | Floor width along the courses. |
| Courses (`C`) | Integer | item | Number of courses (rows). |
| Course height (`H`) | Number | item | Nominal course height. |
| Seed (`S`) | Integer | item | Deterministic seed (river shapes + demo pool). |
| Mode (`M`) | Integer | item | 0 = Greedy, 1 = Hungarian (global min-trim). |
| Run (`R`) | Boolean | item | Set true to lay the floor. |

| out | type | access | description |
|---|---|---|---|
| Boards (`B`) | Mesh | list | Placed boards (vertex-coloured meshes). |
| Rivers (`Rv`) | Curve | list | The live-edge seams between courses. |
| Butt joints (`J`) | Line | list | Staggered sawn butt joints. |
| Trim slivers (`T`) | Curve | list | Scribe-and-fill strips removed from each board. |
| Report (`Re`) | Text | item | Layup summary. |

Related:
- Frahan > EdgeMatch > Live Edge Classify - Produces the live/sawn split each offcut is laid by.
- Frahan > Voussoir > Template Panel Match - Same HungarianAssigner, 3D top-down stone-to-slot assignment.

### Live Edge Trim  (`LETrim`)

- GUID: `D5F10045-ED9E-4ED9-A045-ED9EED9E0045`  |  icon: `LiveEdgeTrim.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/LiveEdgeTrimComponent.cs`
- Algorithm: **Scribe-and-fill trim** - Frahan-original; live-edge river gap-fill practice
- Scribe a board's two live edges onto target seam curves (lower + upper rivers). Returns the trimmed  outline, the scribe-and-fill slivers, and the max trim depth. Leave a seam unconnected to keep that edge.

| in | type | access | description |
|---|---|---|---|
| Board (`B`) | Curve | item | Closed board outline (laid horizontally). |
| Lower seam (`L`) | Curve | item | Target seam for the bottom live edge. |
| Upper seam (`U`) | Curve | item | Target seam for the top live edge. |

| out | type | access | description |
|---|---|---|---|
| Trimmed (`T`) | Curve | item | The scribed (trimmed) board outline. |
| Slivers (`S`) | Curve | list | The scribe-and-fill strips removed (bottom + top). |
| Depth (`D`) | Number | item | Max trim depth. |

Related:
- Frahan > EdgeMatch > Live Edge Classify - Provides the live/sawn split this trims by.
- Frahan > EdgeMatch > Live Edge Stagger Layup - Applies this trim across a whole staggered floor.

### Mesh Template Match  (`MTM`)

- GUID: `D5F1000D-ED9E-4ED9-A00D-ED9EED9E000D`  |  icon: `EdgeMatchSolve.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/EdgeMatch3D/MeshTemplateMatchComponent.cs`
- Algorithm: **OBB containment + best-fit alignment** - PrincipalAxes3d via covariance / Jacobi eigendecomposition
- Simple-component template matcher: given stock meshes + one  target template, find the stock whose OBB contains the template  with the lowest waste. Inspired by PolytopeSolutions'  MatchMeshTransformation but generalised for scanned-stone-vs- designed-template where the topology doesn't match. The simple  first-cut matcher for the cathedral / Vitruvian / fluidic  stone workflows -- reach for Template Block Match 3D when  production cost matters.

| in | type | access | description |
|---|---|---|---|
| Stock Meshes (`SM`) | Mesh | list | List of stock stone meshes to search (scanned quarry blocks, off-cuts, etc.). |
| Template Mesh (`TM`) | Mesh | item | The designed template mesh to fit (e.g. one voussoir, one column drum, one wall block). |
| Margin (`M`) | Number | item | Safety margin (mm) the template's OBB must clear within the stock's OBB. Default 5 mm. |
| Min Yield (`Y`) | Number | item | Minimum yield ratio (template_vol / stock_vol) for a feasible match. Default 0.4 (40 %). |

| out | type | access | description |
|---|---|---|---|
| Matched Index (`MI`) | Integer | item | Index of the picked stock mesh (-1 if none feasible). |
| Transformation (`T`) | Transform | item | Rigid transform that aligns the template into the picked stock's OBB  (Plane.PlaneToPlane from template OBB to stock OBB). |
| Yield Ratio (`Y`) | Number | item | Achieved yield ratio (template_vol / stock_vol) for the picked stock. 0 if no match. |
| Carving Volume (`CV`) | Number | item | Estimated material to carve away (stock_vol - template_vol) in mm^3. |
| Remarks (`R`) | Text | list | Per-candidate diagnostic notes -- feasible / infeasible reasons. |

### Soft ICP 3D  (`SoftICP3D`)

- GUID: `D5F1000E-ED9E-4ED9-A00E-ED9EED9E000E`  |  icon: `EdgeMatchSolve.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/EdgeMatch3D/SoftIcp3DComponent.cs`
- Algorithm: **Penetration hinge (smooth non-penetration)** - Mesh.IsPointInside inside-test + smooth quadratic hinge per SettleContactComponent / OverlapResolver2D
- Refine the poses of 3D fragment meshes so their rims come into  CONTACT while their solids do not interpenetrate. EM weighted- Kabsch over CPD soft correspondence + smooth penetration hinge.  The standalone primitive that EdgeMatch Solve / Kintsugi / Trencadis  / Cyclopean Recipe / Voussoir Match all invoke internally; surface  on the canvas to chain into any custom workflow. [Myronenko & Song 2010]

| in | type | access | description |
|---|---|---|---|
| Fragments (`F`) | Mesh | list | Placed fragment meshes at their CURRENT pose. Rim samples are taken  from each mesh's naked-edge / boundary loops automatically. |
| Anchor Index (`Ai`) | Integer | item | Index of the fragment to PIN (its pose stays fixed; others move  relative to it). Default 0. Set -1 to anchor none (free-floating refine). |
| Tau0 Factor (`T0`) | Number | item | Initial CPD temperature factor (tau0 = T0 * (median rim spacing)^2).  Larger = softer / wider correspondence. Default 4.0. |
| Tau Anneal (`Ta`) | Number | item | Geometric anneal factor (0,1) applied to tau each outer iter. Default 0.8. |
| Outlier Weight (`Ow`) | Number | item | Uniform-outlier pseudo-weight in the softmax denominator.  Default 0.01. |
| Max Iterations (`Mi`) | Integer | item | Maximum EM outer iterations. Default 40. |
| Sample Spacing (`Ss`) | Number | item | Arc-length sample spacing along naked edges (mm). Default 1.0 mm;  0 = auto from the assembly bbox. |

| out | type | access | description |
|---|---|---|---|
| Delta (`D`) | Transform | list | Per-fragment pose increment (left-composed onto the input pose).  Anchored fragments emit Transform.Identity. |
| Refined Fragments (`RF`) | Mesh | list | Per-fragment refined mesh (input mesh with Delta applied). |
| Mean Rim Gap (`MRG`) | Number | item | Mean nearest-neighbour rim gap across all matched neighbour rims  (global Report metric). |
| Max Penetration (`MP`) | Number | item | Maximum penetration depth across all fragment pairs  (should be <= 0 or very small after a successful refine). |
| Contact Samples (`CS`) | Integer | item | Count of rim samples on mating interfaces (drives the contact term). |
| Iterations (`I`) | Integer | item | Outer EM iterations actually run. |
| Remarks (`R`) | Text | list | Per-fragment diagnostic notes + convergence flags. |

### Template Block Match 3D  (`TmplBlk3D`)

- GUID: `D5F1000B-ED9E-4ED9-A00B-ED9EED9E000B`  |  icon: `EdgeMatchSolve.png`  |  exposure: `hidden`  |  source: `src/Frahan.StonePack.GH/EdgeMatch3D/TemplateBlockMatch3DComponent.cs`
- Algorithm: **Hungarian assignment** - H.W. Kuhn 1955 Hungarian Method for the Assignment Problem; Jonker-Volgenant pivot
- 3D sibling of Component D. Designer supplies an N-cell 3D template  (voussoir layout). Inventory is a list of scanned stones.  Hungarian bipartite assignment solves the optimal one-to-one  mapping (stone -> cell) minimising total trim volume + post-trim  residual. Cost matrix per-cell evaluated via Component C3D in  dry-run mode. Same algorithm as Voussoir Stone Matcher (shared  HungarianAssigner.cs). [Kuhn 1955]

| in | type | access | description |
|---|---|---|---|
| Template Cells (`Tc`) | Mesh | list | List of designed cell meshes (voussoir layout). One mesh per cell. |
| Stone Inventory (`I`) | Mesh | list | List of scanned-stone meshes. |
| Strategy (`St`) | Integer | item | 0=Greedy, 1=Hungarian (default; globally optimal), 2=Pareto (NSGA-II fallback for M*N > 40k). |
| Allow Trim (`At`) | Boolean | item | If true, calls Component C3D to evaluate cost with minimal trim.  If false, only no-trim Block Pair Match 3D matches are considered. |
| Max Trim Volume Ratio (`Mtv`) | Number | item | Per-pair trim volume budget. Per-pair cost = infinity if exceeded. |
| Allow Empty (`Ae`) | Boolean | item | If true, cells with no feasible inventory match remain unassigned (reported).  If false, fail loudly when any cell would remain empty. |

| out | type | access | description |
|---|---|---|---|
| Placed Stones (`Ps`) | Mesh | list | Per-cell placed (and trimmed if applicable) stone meshes. |
| Cell Indices (`Ci`) | Integer | list | Per-cell index in the template (parallels Placed Stones). |
| Stone Indices (`Si`) | Integer | list | Per-cell inventory index assigned (parallels Cell Indices). |
| Unassigned Cells (`Uc`) | Integer | list | Cell indices with no feasible inventory match (when Allow Empty=true). |
| Unused Stones (`Us`) | Integer | list | Inventory indices not consumed in the assignment. |
| Total Cost (`Tc`) | Number | item | Sum of per-cell costs across the assignment (Hungarian objective value). |
| Remarks (`Rm`) | Text | list | Diagnostic notes -- strategy used, cost matrix size, rejected stones, etc. |

### Whole-Side Assemble  (`WSAssemble`)

- GUID: `D5F10021-ED9E-4ED9-A021-ED9EED9E0021`  |  icon: `WholeSideAssemble.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/WholeSideAssembleComponent.cs`
- Algorithm: **Whole-side corner/side extraction** - Minimum-area bounding rectangle (rotating calipers) corners + flat-border (is_edge) exclusion
- Reassembles scattered/rotated coplanar parts by matching WHOLE contour  sides (corner-to-corner) and growing best-first from an anchor part.  Outputs placed contours and per-part transforms. 2D (world XY) only.

| in | type | access | description |
|---|---|---|---|
| Anchor (`A`) | Curve | item | Anchor part (placed first at its current position; the assembly grows from it). |
| Parts (`P`) | Curve | list | Closed part contours to reassemble (scattered / rotated, coplanar in world XY). |
| Fit Gate (`G`) | Number | item | Maximum length-normalized side-fit cost admitted to the search.  Default 2.5 (must exceed the highest TRUE seam cost; too low orphans far parts). |
| Run (`R`) | Boolean | item | Execute the assembler. |

| out | type | access | description |
|---|---|---|---|
| Placed (`Pl`) | Curve | list | Part contours transformed by their solved placements (anchor included). |
| Transforms (`X`) | Transform | list | Per-part rigid transform, matching the Placed / Ids order. |
| Ids (`Id`) | Text | list | Part ids in placement order. |
| Total Residual (`Tr`) | Number | item | Sum of accepted side-fit costs. |
| Report (`Rp`) | Text | item | Human-readable solve summary. |

Related:
- Frahan > EdgeMatch > EdgeMatch Solve - ALTERNATIVE SOLVER: segment/ICP/beam pipeline for frame-anchored Trencadís; 


## Fabricate

### Fabrication Prep Report  (`FabPrep`)

- GUID: `F2D07A04-1A2B-4C3D-9E4F-5A6B7C8D9E04`  |  icon: `StockpileManager.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Fabrication/FabricationPrepReportComponent.cs`
- Per-block weight (volume x density), centroid, and lift class  (hand <25 kg / two-person <50 / mechanical <2000 / crane) for the  crate + hoist plan. Assumes model units are metres. Default  density = 2700 kg/m3 (granite). Wire block meshes from Staggered  Block Decompose / Slab Cut.

| in | type | access | description |
|---|---|---|---|
| Blocks (`B`) | Mesh | list | Closed block meshes (e.g. from Staggered Block Decompose). |
| Density (`D`) | Number | item | Stone density kg/m3 (default granite 2700). |
| Ids (`Id`) | Text | list | Optional per-block ids (parallel to Blocks). |

| out | type | access | description |
|---|---|---|---|
| Weights (`W`) | Number | list | Per-block weight (kg). |
| Volumes (`V`) | Number | list | Per-block volume (m^3). |
| Centroids (`C`) | Point | list | Per-block centroid. |
| Lift Class (`L`) | Text | list | Per-block lift class. |
| Total Weight (`T`) | Number | item | Sum of block weights (kg). |
| Report (`R`) | Text | item | Summary + per-class counts. |

### Frahan Bench Monument Pack  (`MonPack`)

- GUID: `F7A16002-0001-4F2D-A0B0-7E60CADA17F2`  |  icon: `BinPack.png`  |  exposure: `quinary`  |  source: `src/Frahan.StonePack.GH/MonumentPackingComponents.cs`
- Algorithm: **24-orientation SO(3) sampling + greedy AABB packing** - Frahan-original
- Pack a MonumentInventory inside a fractured bench (BlockGraph)  using 24-rotation SO(3) sampling and greedy AABB placement  per cell. Monuments stay inside one cell — no fracture crossings. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Block Graph (`Bg`) | Generic | item | BlockGraph from Frahan Block Graph. |
| Inventory (`Inv`) | Generic | item | MonumentInventory. |
| Grid Stride (m) (`Gs`) | Number | item | Candidate-origin sweep step. |

| out | type | access | description |
|---|---|---|---|
| Plan (`P`) | Generic | item | BenchMonumentPlan. |
| Placed Boxes (`B`) | Box | list | Axis-aligned box per placement (visual). |
| Placed Ids (`I`) | Text | list | Monument ids in placement order. |
| Orientation Index (`R`) | Integer | list | Rotation index (0..23) per placement. |
| Cell Ids (`C`) | Text | list | Parent cell id per placement. |
| Unplaced Ids (`U`) | Text | list | Monuments that did not fit. |
| Fill Ratio (`Fr`) | Number | item | TotalPlacedVolume / BenchAabbVolume. |

### Frahan Monument Inventory  (`MonInv`)

- GUID: `F7A16001-0001-4F2D-A0B0-7E60CADA17F1`  |  icon: `StockpileManager.png`  |  exposure: `quinary`  |  source: `src/Frahan.StonePack.GH/MonumentPackingComponents.cs`
- Bundle Rhino meshes as a MonumentInventory consumable by  the Frahan Bench Monument Pack components. Each mesh becomes  one Monument; ids are optional and auto-generated when blank.

| in | type | access | description |
|---|---|---|---|
| Meshes (`M`) | Mesh | list | One mesh per monument. |
| Ids (`I`) | Text | list | Optional ids; auto-generated when blank. |
| Density (kg/m^3) (`D`) | Number | item | Material density. |

| out | type | access | description |
|---|---|---|---|
| Inventory (`Inv`) | Generic | item | MonumentInventory. |
| Count (`N`) | Integer | item | Number of monuments. |
| Total AABB Volume (m^3) (`V`) | Number | item | Sum of monument AABB volumes. |

### Frahan Pack Monuments In Cell  (`MonInCell`)

- GUID: `F7A16003-0001-4F2D-A0B0-7E60CADA17F3`  |  icon: `PackIntoBlock.png`  |  exposure: `quinary`  |  source: `src/Frahan.StonePack.GH/MonumentPackingComponents.cs`
- Algorithm: **24-orientation SO(3) sampling + greedy AABB packing** - Frahan-original
- Pack a MonumentInventory inside ONE BlockCell. Useful when  you want to assign specific monuments to specific cells  rather than letting the bench-wide packer order them. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Block Graph (`Bg`) | Generic | item | BlockGraph (the cell is selected by index). |
| Cell Index (`Ci`) | Integer | item | Index of the cell within Bg.Cells. |
| Inventory (`Inv`) | Generic | item | MonumentInventory. |
| Grid Stride (m) (`Gs`) | Number | item | Candidate-origin sweep step. |

| out | type | access | description |
|---|---|---|---|
| Placed Boxes (`B`) | Box | list | Axis-aligned box per placement. |
| Placed Ids (`I`) | Text | list | Monument ids placed. |
| Orientation Index (`R`) | Integer | list | Rotation index per placement. |
| Placed Count (`N`) | Integer | item | Total placements in this cell. |

### G-code Parser  (`GCode`)

- GUID: `D5F10030-ED9E-4ED9-A030-ED9EED9E0030`  |  icon: `StoneCutExport.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Fabrication/GCodeParserComponent.cs`
- Algorithm: **ISO 6983-1 G-code tokenizer + modal state machine** - Frahan-original; ISO 6983-1:2009 standard for CNC numerical control
- Parse an ISO 6983-1-subset G-code file (.nc / .gcode / .cnc) into  a typed CutPath record. Phase B Stage 1 of the scan-to-mill  architecture per wiki/specs/scan_to_mill_architecture.md §1.6.  First production component bridging Stone-Aware Cut Export ->  KUKAprc / Robots / SprutCAM / RhinoCAM. Supports the RhinoCAM  3-axis dialect observed in the MRAC 2023 workshop.

| in | type | access | description |
|---|---|---|---|
| File Path (`F`) | Text | item | Absolute or relative path to a G-code .nc / .gcode / .cnc file. |
| Initial Position (`I0`) | Point | item | Optional initial tool position before the first G-code line.  Default (0,0,0). Used as the Start of the first segment if the  file does not begin with an explicit G00 / G01 rapid. |
| Skip Rapids (`Sr`) | Boolean | item | If true, G00 rapid-traverse segments are dropped from the output  (only G01-cut + G02/G03-arc segments remain). Default false --  preserves the full toolpath for visualisation. |

| out | type | access | description |
|---|---|---|---|
| Cut Path (`CP`) | Generic | item | The typed CutPath record. Wire into GCodeToPlanesComponent or  WireSawToolpathAdapterComponent downstream. |
| Segment Count (`N`) | Integer | item | Total segments parsed (informational; matches CutPath.Segments.Count). |
| Total Length (`L`) | Number | item | Sum of segment lengths (linear approximation for arcs) in the  file's units. Useful for time estimates: time ≈ length / feed. |
| Segment Endpoints (`EP`) | Point | list | Per-segment end points (one per CutSegment). For canvas preview. |
| Remarks (`R`) | Text | list | Parser diagnostics: line count, modal-mode transitions, comment  count, file-level F + S defaults, encountered unknown G-codes. |

### G-code to Planes  (`GCodeToPlanes`)

- GUID: `D5F10031-ED9E-4ED9-A031-ED9EED9E0031`  |  icon: `StoneCutExport.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Fabrication/GCodeToPlanesComponent.cs`
- Algorithm: **CutPath -> Plane[] tool-axis frame construction** - Frahan-original; standard milling-frame convention (tool axis = -Z by default)
- Translate a parsed CutPath into a Plane[] consumable by KUKAprc  (via its Plane->Command components) or visose/Robots (via  CreateTarget). Phase B Stage 2 of the scan-to-mill architecture.  Arc segments are discretised at Arc Step intervals; linear  segments emit one Plane per segment endpoint. Tool axis defaults  to -Z (downward milling).

| in | type | access | description |
|---|---|---|---|
| Cut Path (`CP`) | Generic | item | The typed CutPath from GCodeParserComponent (D5F10030). |
| Tool Axis (`Ta`) | Vector | item | Tool axis vector in WORLD coordinates. Default -Z (downward  milling, the dominant case). The emitted Plane's Z-axis aligns  with this vector; rotation about Z is determined by the segment  direction. |
| Arc Step (`As`) | Number | item | Chord step (mm) for arc discretisation. Smaller = more Planes /  tighter chord. Default 2.0 mm (sub-mm-spec friendly). Set 0 to  emit only segment endpoints (chord-only mode). |

| out | type | access | description |
|---|---|---|---|
| Planes (`P`) | Plane | list | Per-segment + per-arc-sample Planes (KUKAprc + Robots consumable). |
| Feed Rates (`F`) | Number | list | Per-Plane feed rate (mm/min); parallels Planes list. |
| Spindle Speeds (`S`) | Number | list | Per-Plane spindle speed (RPM); parallels Planes list. |
| Segment Indices (`Si`) | Integer | list | Per-Plane source segment index (so the user can trace each Plane  back to a CutPath.Segments entry). |
| Remarks (`R`) | Text | list | Per-pipeline diagnostics: Plane count, arc-sample count, tool- axis quality flags. |

### Planes to KUKAprc Commands  (`Pl2KUKAprc`)

- GUID: `D5F10032-ED9E-4ED9-A032-ED9EED9E0032`  |  icon: `StoneCutExport.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Fabrication/PlanesToKukaPrcCommandsComponent.cs`
- Algorithm: **CutSegmentKind -> KUKAprc Motion mapping** - Frahan-original; standard CAM-to-KRL motion-type convention (Rapid->PTP, Linear/Arc->LIN)
- Tag a Plane[] (from GCodeToPlanes or WireSawToolpath) with  KUKAprc-compatible motion-type (LIN / PTP) and feed metadata.  Thin wrapper -- Frahan stops here; KUKAprc Pro owns the final  Plane->LIN/PTP/CIRC command construction + KRL code generation.  This wrapper IS the first FREE open-source G-code path into  KUKAprc (paid Generic NC Import is the only alternative).

| in | type | access | description |
|---|---|---|---|
| Planes (`P`) | Plane | list | Per-pose tool-axis frames (from GCodeToPlanes D5F10031 or  WireSawToolpath D5F10034). Each plane = one KUKAprc target. |
| Feed Rates (`F`) | Number | list | Per-plane feed rate (mm/min); parallels Planes list. The Frahan  convention is mm/min; KUKAprc consumes this as the LIN/PTP velocity  argument after the user maps it to their unit-system in KUKAprc. |
| Spindle Speeds (`S`) | Number | list | Per-plane spindle speed (RPM); parallels Planes list. KUKAprc has  no native spindle command; the user wires this into a KUKAprc  ENV-variable assignment or vendor-specific tool-trigger. |
| Cut Path (optional) (`CP`) | Generic | item | Optional: the source CutPath typed record (from GCodeParser  D5F10030). If wired, the wrapper distinguishes Rapid (PTP) from  Linear/Arc (LIN) per-plane via the Segment Indices lookup. |
| Segment Indices (optional) (`Si`) | Integer | list | Optional: per-plane source segment index (from GCodeToPlanes).  Required when Cut Path is wired so the wrapper can look up the  CutSegmentKind for motion-type mapping. |

| out | type | access | description |
|---|---|---|---|
| Planes (`P`) | Plane | list | Plane[] passthrough; wire into KUKAprc Pro's Plane->LIN/PTP  command components. |
| Motion Types (`M`) | Text | list | Per-plane motion type: "LIN" (linear cut) or "PTP"  (point-to-point rapid). Wire into KUKAprc's command branch  selector. |
| Feed Rates (`F`) | Number | list | Per-plane feed rate passthrough (mm/min). |
| Spindle Speeds (`S`) | Number | list | Per-plane spindle RPM passthrough. |
| Remarks (`R`) | Text | list | KUKAprc Pro version + paid-tier dependency note + motion-type  histogram. |

### Planes to Robot Targets  (`Pl2Robots`)

- GUID: `D5F10033-ED9E-4ED9-A033-ED9EED9E0033`  |  icon: `StoneCutExport.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Fabrication/PlanesToRobotTargetsComponent.cs`
- Algorithm: **CutSegmentKind -> visose/Robots Motion mapping** - Frahan-original; standard CAM-to-robot-target motion convention (Rapid->Joint, Linear/Arc->Linear)
- Tag a Plane[] (from GCodeToPlanes or WireSawToolpath) with  visose/Robots-compatible motion (Linear / Joint), speed (mm/s),  and zone (mm blending) metadata. Thin wrapper -- Frahan stops  here; visose/Robots owns the Plane->CreateTarget construction +  kinematic simulation. This wrapper + Frahan's GCodeParser is the  only path from G-code into visose/Robots (the plugin has zero  native G-code ingest).

| in | type | access | description |
|---|---|---|---|
| Planes (`P`) | Plane | list | Per-pose tool-axis frames (from GCodeToPlanes D5F10031 or  WireSawToolpath D5F10034). Each Plane = one visose/Robots Target. |
| Feed Rates (`F`) | Number | list | Per-plane feed rate (mm/min); parallels Planes. Converted to  mm/s (factor 1/60) on the way to visose/Robots' Speed convention. |
| Spindle Speeds (`S`) | Number | list | Per-plane spindle RPM passthrough. visose/Robots does not consume  spindle directly; the user wires this into a Robots `Command`  tool-trigger if needed. |
| Default Zone (`Z`) | Number | item | Blending zone radius (mm) applied to all targets when CutPath  is not wired. Default 1.0 mm (visose/Robots fine zone). |
| Cut Path (optional) (`CP`) | Generic | item | Optional CutPath typed record (from GCodeParser D5F10030). If  wired, the wrapper distinguishes Rapid (Joint motion) from  Linear / Arc (Linear motion). |
| Segment Indices (optional) (`Si`) | Integer | list | Optional: per-plane source segment index (from GCodeToPlanes);  required when Cut Path is wired. |

| out | type | access | description |
|---|---|---|---|
| Planes (`P`) | Plane | list | Plane[] passthrough; wire into visose/Robots `Create Target`.Plane. |
| Motions (`M`) | Text | list | Per-plane motion: "Linear" or "Joint". Wire into visose/Robots  `Create Target`.Motion. |
| Speeds (`Sp`) | Number | list | Per-plane speed (mm/s); fed into visose/Robots' Speed parameter. |
| Zones (`Z`) | Number | list | Per-plane blending zone (mm); fed into visose/Robots' Zone parameter. |
| Remarks (`R`) | Text | list | visose/Robots version + packaging note + motion-type histogram. |

### Staggered Block Decompose  (`StaggerBlocks`)

- GUID: `F2D07A02-1A2B-4C3D-9E4F-5A6B7C8D9E02`  |  icon: `BondPattern.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Fabrication/StaggeredBlockDecomposeComponent.cs`
- Algorithm: **Running-bond staggered cell layout** - Frahan-original
- Lay out staggered (running-bond) blocks over a sculpted form's  bounding box for wire-saw + robotic-mill fabrication. Emits the  staggered cells (boxes + box meshes) + per-cell course index  (ascending = build order). Pipe Cell Meshes into Quarry Decompose  By Mesh (CGAL) / Mesh CSG (CGAL) for form-fitted blocks — this  component does NOT fire many RhinoCommon booleans (the HITL  large-slab failure mode). Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Form (`M`) | Mesh | item | Sculpted / freeform stone mesh to decompose. |
| Course Height (`Hc`) | Number | item | Course (layer) height along the up axis. |
| Block Length (`Lb`) | Number | item | Block length along the bond axis. |
| Stagger (`St`) | Number | item | Odd-course shift as a fraction of block length (0..1). Default 0.5 = running bond. |
| Up Axis (`Up`) | Integer | item | Course-stacking axis: 0=X, 1=Y, 2=Z (default). |

| out | type | access | description |
|---|---|---|---|
| Cells (`C`) | Box | list | Staggered cell boxes. |
| Cell Meshes (`Cm`) | Mesh | list | Cell boxes as meshes (feed into CGAL/geogram decompose). |
| Course (`Cr`) | Integer | list | Per-cell course index (ascending = build order). |
| Count (`N`) | Integer | item | Number of cells. |
| Report (`R`) | Text | item | Layout summary + min/max cell size (wire-saw / mill feasibility). |

### Stone-Aware Cut Export  (`CutExport`)

- GUID: `F2D07A01-1A2B-4C3D-9E4F-5A6B7C8D9E01`  |  icon: `GcodeExport.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Fabrication/StoneCutExportComponent.cs`
- Write cut pieces (mesh / brep / curve / surface) to a .3dm with  stone metadata (bed direction, finish, weight, kerf, provenance)  attached per piece as namespaced user-strings + one layer per  piece, so CAM (EasySTONE, Alphacam, Breton, Lantek) keeps the  stone intelligence. Set Write = true to write the file.

| in | type | access | description |
|---|---|---|---|
| Geometry (`G`) | Geometry | list | Cut pieces: mesh / brep / curve / surface. |
| Piece Ids (`Id`) | Text | list | Per-piece id (parallel to Geometry). Auto S001.. if absent. |
| Stone (`St`) | Text | item | Stone / source applied to all pieces (e.g. 'TN Black Granite'). |
| Finish (`Fi`) | Text | item | Finish applied to all (polished / honed / flamed / sandblasted). |
| Bed Direction (`Bd`) | Vector | item | Bed / grain direction (unit vector) applied to all. |
| Weight kg (`W`) | Number | list | Per-piece weight in kg (parallel to Geometry). |
| Kerf mm (`K`) | Number | item | Saw kerf in mm applied to all. |
| File Path (`Fp`) | Text | item | Output .3dm path. |
| Write (`Wr`) | Boolean | item | Set true to write the .3dm. False = dry run (reports only). |

| out | type | access | description |
|---|---|---|---|
| File Path (`Fp`) | Text | item | Path written (empty on dry run / failure). |
| Piece Count (`N`) | Integer | item | Number of pieces exported. |
| Report (`R`) | Text | item | Export summary. |

### Wire-Saw Toolpath  (`WireSaw`)

- GUID: `D5F10034-ED9E-4ED9-A034-ED9EED9E0034`  |  icon: `StoneCutExport.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Fabrication/WireSawToolpathAdapterComponent.cs`
- Algorithm: **Moult2018PortableWireBandsaw** - Moult, Weir, Fernando 2018 University of Sydney KUKA + portable diamond-wire bandsaw end-effector
- Generate a Plane[] toolpath for a robot-mounted diamond-wire saw  to cut stone along a designed curve. Frahan-original component  closing the toolchain gap left by Zhang 2024 + Moult 2018 (neither  has KUKAprc / Robots plugin integration). v1 supports planar cuts;  v1.x adds ruled-surface decomposition + variable wire tension.  Outputs feed directly into KUKAprc / Robots downstream.

| in | type | access | description |
|---|---|---|---|
| Cut Curve (`C`) | Curve | item | The designed cut path the wire traces. Closed or open.  v1 supports planar curves; v1.x adds ruled-surface paths. |
| Wire Axis (`Wa`) | Vector | item | Wire-axis vector in WORLD coordinates -- the direction the  wire is tensioned. Perpendicular to the cut direction at  every sample. Default world-Y (cuts in XZ plane). |
| Kerf Width (`Kw`) | Number | item | Diamond-wire kerf width (mm). Default 4.0 mm (mid-range for  brazed diamond wires; Zhang 2024 reports Δ = 1.75 mm half-kerf). |
| Sample Count (`N`) | Integer | item | Number of Planes to emit along the cut curve. Higher = smoother  robot motion + more program lines. Default 32. |
| Feed Rate (`F`) | Number | item | Wire feed rate (mm/min) at each sample. Zhang 2024 reports  wire surface speeds in the 30-50 m/s range; conservative GH  feed default = 300 mm/min linear advance. |
| Apply Kerf Compensation (`Kc`) | Boolean | item | If true, offsets the cut curve by Kerf Width / 2 so the FINISHED  cut surface matches the design. Default true. |

| out | type | access | description |
|---|---|---|---|
| Planes (`P`) | Plane | list | Per-sample Plane[]: origin = curve sample, X = tangent, Z = wire axis.  Wire into KUKAprc / Robots downstream. |
| Feed Rates (`F`) | Number | list | Per-Plane feed rate (mm/min); parallels Planes list. |
| Compensated Curve (`Cc`) | Curve | item | The kerf-compensated cut curve (offset by Kerf Width / 2).  Returned even when Apply Kerf Compensation = false (= input curve). |
| Remarks (`R`) | Text | list | Per-pipeline diagnostics + Zhang 2024 / Moult 2018 reference notes. |


## Fracture

### Brick-Pattern Fracture Planes  (`BrickFx`)

- GUID: `BADBECFD-AEBF-4567-89AB-CDEF01234567`  |  icon: `BondPattern.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Fracture/BrickPatternFracturePlanesComponent.cs`
- Algorithm: **Running-bond brick-pattern fracture set** - Frahan-original
- Orthogonal fracture set emulating a running-bond brick  layout. nX = vertical planes per course, nZ = horizontal  course separators. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Slab (`S`) | Generic | item | Slab DTO whose bounding box seeds the brick grid. |
| nX (`nX`) | Integer | item | Vertical fracture planes per course. >= 0. |
| nZ (`nZ`) | Integer | item | Horizontal course separators. >= 0. |

| out | type | access | description |
|---|---|---|---|
| Planes (`P`) | Generic | list | FracturePlane DTOs in a running-bond pattern. |

### Fracture Plane Filter  (`FxFilter`)

- GUID: `DCFDAEBF-CADB-4789-ABCD-EF0123456789`  |  icon: `DefectMap.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Fracture/FracturePlaneFilterComponent.cs`
- Drops planes that miss the target slab's bounding box.  Pre-cutting optimisation; SlabCutter handles non-intersecting  planes gracefully on its own.

| in | type | access | description |
|---|---|---|---|
| Planes (`P`) | Generic | list | Input FracturePlane DTOs (any source). |
| Slab (`S`) | Generic | item | Target Slab whose bounding box drives the filter. |

| out | type | access | description |
|---|---|---|---|
| Planes (`P`) | Generic | list | Filtered FracturePlane DTOs (only those intersecting the slab AABB). |

### Fracture Polygon From Curve  (`FracPoly`)

- GUID: `D3C4E5F6-7B8A-49AC-BD2E-3F4A5B6C7D8E`  |  icon: `DefectMap.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Fracture/FracturePolygonFromCurveComponent.cs`
- Wraps a closed planar polyline into a FracturePolygon DTO.  The curve must convert to a polyline with at least 4 points  (closing duplicate dropped). The polygon must be convex and  planar; the FracturePolygon constructor enforces both.

| in | type | access | description |
|---|---|---|---|
| Curve (`C`) | Curve | item | Closed polyline (or PolylineCurve) describing a finite convex  fracture polygon. The curve must yield a polyline with at least  4 points and be closed. Planarity required unless ForceProject  is true. |
| ForceProject (`FP`) | Boolean | item | When true, near-planar curves are projected onto their best-fit  plane before constructing the polygon. Default false (strict). |

| out | type | access | description |
|---|---|---|---|
| FracturePolygon (`F`) | Generic | item | FracturePolygon DTO. Wire into Slab Cut By Fracture Polygons. |

### Grid Fracture Planes  (`GridFx`)

- GUID: `E6F7A8B9-CADB-4CDE-F012-345678901234`  |  icon: `QuarryCutOpt.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Fracture/GridFracturePlanesComponent.cs`
- Algorithm: **Orthogonal grid fracture set** - Frahan-original
- Produces an orthogonal grid of FracturePlanes inside the  bounding box of the input Slab. nX/nY/nZ control how many  evenly-spaced planes are emitted along each axis. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Slab (`S`) | Generic | item | Slab DTO whose bounding box seeds the grid. |
| nX (`nX`) | Integer | item | Number of planes perpendicular to +X. Must be >= 0. |
| nY (`nY`) | Integer | item | Number of planes perpendicular to +Y. Must be >= 0. |
| nZ (`nZ`) | Integer | item | Number of planes perpendicular to +Z. Must be >= 0. |

| out | type | access | description |
|---|---|---|---|
| Planes (`P`) | Generic | list | FracturePlane DTOs. Wire into Slab Cut By Fractures or Quarry Decompose. |

### Jittered Grid Fracture Planes  (`JitGridFx`)

- GUID: `CBECFDAE-BFCA-4678-9ABC-DEF012345678`  |  icon: `QuarryCutOpt.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Fracture/JitteredGridFracturePlanesComponent.cs`
- Algorithm: **Grid with per-plane offset jitter** - Frahan-original
- Orthogonal grid of FracturePlanes with each plane jittered  by up to (jitter * cellStep) along its normal. Deterministic  for a given Seed. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Slab (`S`) | Generic | item | Slab DTO whose bounding box seeds the grid. |
| nX (`nX`) | Integer | item | Planes perpendicular to +X (>= 0). |
| nY (`nY`) | Integer | item | Planes perpendicular to +Y (>= 0). |
| nZ (`nZ`) | Integer | item | Planes perpendicular to +Z (>= 0). |
| Jitter (`J`) | Number | item | Per-plane offset jitter as a fraction of the cell step. In [0, 0.5). |
| Seed (`Seed`) | Integer | item | Random seed. |

| out | type | access | description |
|---|---|---|---|
| Planes (`P`) | Generic | list | FracturePlane DTOs. |

### Layered Fracture Planes  (`LayerFx`)

- GUID: `F8A9CADB-ECFD-4345-6789-012345678ABC`  |  icon: `Stratigraphy.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Fracture/LayeredFracturePlanesComponent.cs`
- Algorithm: **Parallel layered fracture set** - Frahan-original
- Parallel planes equally spaced along the chosen axis.  Use Axis = 0 (X), 1 (Y), or 2 (Z). Common pattern for  sedimentary rocks. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Slab (`S`) | Generic | item | Slab DTO whose bounding box seeds the layers. |
| Axis (`A`) | Integer | item | Layer normal direction: 0 = X, 1 = Y, 2 = Z. Default 2 (Z). |
| Count (`N`) | Integer | item | Number of layers (interior cuts). Must be >= 0. |

| out | type | access | description |
|---|---|---|---|
| Planes (`P`) | Generic | list | FracturePlane DTOs (parallel, evenly spaced). |

### Radial Fracture Planes  (`RadialFx`)

- GUID: `A9CADBEC-FDAE-4456-789A-012345678BCD`  |  icon: `CompressionDesign.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Fracture/RadialFracturePlanesComponent.cs`
- Algorithm: **Radial / fan fracture set** - Frahan-original
- N planes that share a common axis line, rotated by  180/N degrees per plane. Pie-wedge cut pattern; common  for log-like or cylindrical stones. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Center (`C`) | Point | item | Point on the rotation axis. |
| Axis (`A`) | Vector | item | Axis direction. |
| Count (`N`) | Integer | item | Number of radial planes. Must be >= 0. |

| out | type | access | description |
|---|---|---|---|
| Planes (`P`) | Generic | list | FracturePlane DTOs around the rotation axis. |

### Random Fracture Planes  (`RandFx`)

- GUID: `F7A8B9CA-DBEC-4DEF-0123-456789012345`  |  icon: `DefectMap.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Fracture/RandomFracturePlanesComponent.cs`
- Algorithm: **Random plane placement** - Frahan-original
- Produces N FracturePlanes with points inside the slab's  bounding box and normals uniform on the sphere. Deterministic  for a given Seed. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Slab (`S`) | Generic | item | Slab DTO whose bounding box seeds the random plane points. |
| Count (`N`) | Integer | item | Number of random planes. Must be >= 0. |
| Seed (`Seed`) | Integer | item | Random seed for reproducibility. |

| out | type | access | description |
|---|---|---|---|
| Planes (`P`) | Generic | list | FracturePlane DTOs. |

### Slab Cut By Fracture Polygons  (`SlabCutFP`)

- GUID: `E4D5F607-8C9B-40BD-CE3F-405162738491`  |  icon: `BlockCutOpt.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Fracture/SlabCutByFracturePolygonsComponent.cs`
- Cuts a list of Slabs by a list of finite FracturePolygons.  A polygon that fully contains the slab cross-section produces  two pieces; a polygon that misses the slab is a passthrough;  a polygon that only partially overlaps the cross-section is a  passthrough unless ExtendPartial is set, in which case the  polygon's supporting plane is used as an infinite cut.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | list | Convex meshes to cut. Standard Rhino mesh wires; the cutter  converts to its internal Slab DTO automatically. |
| FracturePolygon (`F`) | Generic | list | FracturePolygon DTOs (from Fracture Polygon From Curve). |
| ExtendPartial (`X`) | Boolean | item | When true, a fracture polygon that only partially covers the  slab cross-section is treated as if it were an infinite plane.  Default: false (partial fractures pass through untouched). |
| Eps (`E`) | Number | item | Vertex-classification epsilon for the underlying SlabCutter.  Default 1e-9. |

| out | type | access | description |
|---|---|---|---|
| Slab (`S`) | Generic | list | Output Slabs after cutting. |
| Count (`N`) | Integer | item | Number of resulting Slabs. |
| TotalVolume (`V`) | Number | item | Sum of signed volumes of all output Slabs (sanity check). |
| Mesh (`M`) | Mesh | list | Output Slabs as Rhino Meshes (parallel to the Slab list). |

### Voronoi Fracture Planes  (`VoroFx`)

- GUID: `A8B9CADB-ECFD-4EF0-1234-567890123456`  |  icon: `Voronoi.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Fracture/VoronoiFracturePlanesComponent.cs`
- Algorithm: **Voronoi perpendicular-bisector cell construction** - Aurenhammer, F. (1991). "Voronoi diagrams—a survey." ACM Computing Surveys 23(3):345-405
- Emits the perpendicular bisector plane between every pair of  input seed Points. Cutting a slab with these planes  approximates Voronoi-cell decomposition. Implements Voronoi partition (Aurenhammer 1991).

| in | type | access | description |
|---|---|---|---|
| Seeds (`S`) | Point | list | Voronoi seed points. At least 2 required. |

| out | type | access | description |
|---|---|---|---|
| Planes (`P`) | Generic | list | FracturePlane DTOs (one per distinct seed pair). |


## Ingest

### GPR File Loader  (`GprLoad`)

- GUID: `F2D00BEC-2026-4523-B0B0-2ABE15A0DEAD`  |  icon: `GprIngest.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/GprFileLoaderComponent.cs`
- Algorithm: **Multi-format GPR ingest dispatcher** - Frahan-original; routes .csv / .sgy / .segy / .rd3 / .dt1 to matching reader
- Load a ground-penetrating-radar file by extension: CSV / SEG-Y / MALA RD3 / pulseEKKO DT1.  Emits trace-start points + count + sample spacing. Sample amplitudes are not piped to the  canvas (too large for GH data trees); use the Core reader for per-sample access.  Workflows cross-checked against RGPR (the open R GPR-processing package) in the companion paper.

| in | type | access | description |
|---|---|---|---|
| File Path (`F`) | Text | item | Absolute path to a .csv / .sgy / .segy / .rd3 / .dt1 file.  .rd3 expects a companion .rad alongside; .dt1 expects a companion .HD. |
| Id (`Id`) | Text | item | Optional radargram identifier label. Defaults to the file's basename. |

| out | type | access | description |
|---|---|---|---|
| Trace Count (`N`) | Integer | item | Number of traces in the radargram. |
| Trace Origins (`P`) | Point | list | One Point3d per trace, at (sourceX, sourceY, 0) in source CRS units. |
| Sample Spacing m (`dz`) | Number | item | Sample spacing (metres) of the first trace; uniform within one radargram. |
| Sample Count (`Ns`) | Integer | item | Number of samples per trace (first trace). |
| Source File (`Src`) | Text | item | The source file path echoed verbatim. |

### GPR Picks From Points  (`GprPicks`)

- GUID: `F2D05A07-1A2B-4C3D-9E4F-5A6B7C8D9E07`  |  icon: `Downsample.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/GprPicksFromPointsComponent.cs`
- Algorithm: **Interactive reflector picking** - Frahan-original: viewport points on the radargram section -> reflector picks (depth = -z / DepthScale)
- Turn points picked on a GPR Radargram Mesh section into reflector  picks for GPR Fractures on Mesh. Recovers true depth by undoing the  Depth Scale, tags label + confidence, and optionally writes a picks  CSV (x_m,y_m,depth_m,confidence_01,label) that reloads via GPR  Radargram Mesh's Picks CSV input.

| in | type | access | description |
|---|---|---|---|
| Points (`P`) | Point | list | Points picked on the radargram section (snap to the section mesh). |
| Depth Scale (`Z`) | Number | item | The Depth Scale used on GPR Radargram Mesh (to recover true depth = -z / Z).  Match it. Default 1. |
| Label (`L`) | Text | list | Fracture label(s). One value = applied to all picks; a list of the same  length = per-pick (group picks into distinct fractures). Default 'pick'. |
| Confidence (`Cf`) | Number | item | Confidence 0..1 for the picks. Default 1. |
| CSV Out (`F`) | Text | item | OPTIONAL path to write a picks CSV (reloadable via GPR Radargram Mesh Picks CSV). |

| out | type | access | description |
|---|---|---|---|
| Pick Points (`Pd`) | Point | list | The picks (section frame) — wire into GPR Fractures on Mesh 'Picks'. |
| Labels (`L`) | Text | list | Resolved label per pick. |
| Confidence (`Cf`) | Number | list | Confidence per pick. |
| Picks CSV (`Csv`) | Text | item | The picks CSV content (also written if CSV Out is set). |

Related:
- Frahan > Ingest > GPR Radargram Mesh - Snap points onto its section, then convert them here; the CSV out reloads via its Picks CSV input.
- Frahan > Quarry > GPR Fractures on Mesh - Feed Pick Points + Labels straight into the overlay.

### GPR Radargram Mesh  (`GprMesh`)

- GUID: `F2D05A04-1A2B-4C3D-9E4F-5A6B7C8D9E04`  |  icon: `Downsample.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/GprRadargramMeshComponent.cs`
- Algorithm: **GPR amplitude section visualisation** - Frahan-original: traces x samples amplitude grid -> vertex-coloured vertical-section mesh
- Read a GPR file and draw the radargram as a vertex-coloured vertical  section mesh: the curtain follows the survey line (trace X,Y) and  goes down by sample depth; vertex colour = reflection amplitude  (blue low, white mid, red high). Reflector picks come out as points.  Use instead of GPR File Loader when you want to SEE the radargram.

| in | type | access | description |
|---|---|---|---|
| File Path (`F`) | Text | item | Path to a GPR file (CSV radargram; see GPR File Loader for formats). |
| Id (`Id`) | Text | item | Optional radargram id (defaults to file name). |
| Depth Scale (`Z`) | Number | item | Vertical exaggeration of the depth axis. 1 = true scale. |
| Trace Spacing (`Dx`) | Number | item | Fallback in-plane spacing between traces when traces share the same  X,Y (no survey geometry). 0 = use the trace X,Y as-is. |
| Contrast (`C`) | Number | item | Amplitude contrast (gamma on the normalized amplitude). 1 = linear;  >1 boosts faint reflectors. |
| Picks CSV (`Pk`) | Text | item | OPTIONAL path to an interpreted-reflector picks CSV  (x_m,y_m,depth_m,confidence_01,label). Most GPR files carry NO picks,  so without this the Pick Points output is empty. Supply picks here to  drive GPR Fractures on Mesh. |

| out | type | access | description |
|---|---|---|---|
| Radargram (`M`) | Mesh | item | Vertex-coloured radargram section mesh. |
| Pick Points (`P`) | Point | list | Interpreted reflector picks at depth. |
| Pick Labels (`L`) | Text | list | Label per pick. |
| Pick Confidence (`Cf`) | Number | list | 0..1 confidence per pick. |
| Amplitude Range (`A`) | Interval | item | Min/max amplitude used for the colour map. |
| Report (`R`) | Text | item | Summary. |

Related:
- Frahan > Ingest > GPR File Loader - Same GPR file; this draws the radargram instead of just trace origins.
- Frahan > Quarry > BlockCutOpt Load Fractures - Picked reflectors become fracture inputs for block-cut optimisation.

### Import Photo Markers  (`PhotoMarkers`)

- GUID: `F2D07A03-1A2B-4C3D-9E4F-5A6B7C8D9E03`  |  icon: `GeoreferenceMarker.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/ScanIngest/ImportPhotoMarkersComponent.cs`
- Read photogrammetry markers / GCPs from a CSV (Metashape / COLMAP /  RealityCapture export or a plain GCP file):  'label, worldX,Y,Z [, modelX,Y,Z]'. Outputs World points (base  frame) + Model points (scan frame, if present) + labels. Feed  World -> Target and Model -> Source of Georeference (Align by  Points), Scale = true, to position the scan on its base.

| in | type | access | description |
|---|---|---|---|
| File (`F`) | Text | item | Path to a marker / GCP CSV. |

| out | type | access | description |
|---|---|---|---|
| Labels (`L`) | Text | list | Per-marker label. |
| World (`W`) | Point | list | Marker positions in the base / world frame (-> Georeference Target). |
| Model (`Mo`) | Point | list | Marker positions in the scan / model frame, if present (-> Georeference Source). |
| Has Model (`Hm`) | Boolean | item | True if the file carried model-frame positions. |
| Count (`N`) | Integer | item | Number of markers read. |

### Vector Fractures Loader  (`VecFrac`)

- GUID: `F2D00BEC-2026-4522-B0B0-1ABE15A0DEAD`  |  icon: `ShapefileImport.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/VectorFracturesLoaderComponent.cs`
- Algorithm: **Vector fracture import** - ESRI Shapefile / OGC Simple Features (standard)
- Load fracture traces from a Shapefile (.shp) or GeoJSON (.geojson) into Rhino  as open PolylineCurves, plus their attributes and source CRS WKT. Uses  NetTopologySuite under the hood; format dispatched by file extension.  Reads ESRI Shapefile / OGC Simple Features (industry standard, not a published algorithm).

| in | type | access | description |
|---|---|---|---|
| File Path (`F`) | Text | item | Absolute path to a .shp or .geojson file. For Shapefiles,  the companion .dbf + .shx + .prj must sit alongside. |

| out | type | access | description |
|---|---|---|---|
| Traces (`T`) | Curve | list | One open PolylineCurve per fracture trace, in the source CRS units. |
| Count (`N`) | Integer | item | Number of traces returned. |
| CRS WKT (`Crs`) | Text | item | Coordinate reference system as WKT (Shapefile .prj). Empty for GeoJSON. |
| Attribute Keys (`Ak`) | Text | tree | Per-trace attribute keys as a {trace_index;0} data tree. |
| Attribute Values (`Av`) | Text | tree | Per-trace attribute values parallel to Attribute Keys. |
| Source File (`Src`) | Text | item | The source file path echoed verbatim. |


## Kintsugi

### Contact Settle  (`Settle`)

- GUID: `F2D00507-2026-4522-B0B0-1ABE15A0CAFE`  |  icon: `ContactSettle.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Kintsugi/SettleContactComponent.cs`
- Algorithm: **Rigid depenetration (contact settle)** - Iterative rigid relaxation that pushes placed fragments apart until solids 
- Push placed fragment meshes apart with rigid translations until  they touch at fracture faces but do not interpenetrate. Closes  open meshes for the solid inside test. Run after Frahan Kintsugi.

| in | type | access | description |
|---|---|---|---|
| Fragments (`F`) | Mesh | list | Placed fragment meshes to de-penetrate (e.g. Frahan Kintsugi output). |
| Iterations (`It`) | Integer | item | Max relaxation iterations. Stops early once max penetration <=  Penetration Tol. Default 25. |
| Penetration Tol (`Pt`) | Number | item | Target max penetration depth (model units). 0 = settle to just  touching; a small positive value tolerates slight overlap.  Default 0.0. |
| Relaxation (`Rx`) | Number | item | Per-iteration step factor 0..1. Lower = stabler but slower; higher  = faster but can oscillate. Default 0.5. |
| Close Open Meshes (`Cl`) | Boolean | item | FillHoles each fragment to a watertight solid for the inside test  (output keeps the original open mesh, translated). Default true. |
| Lock First (`Lk`) | Boolean | item | Keep fragment 0 fixed as the anchor so the whole assembly does not  drift; all corrections go to the other piece. Default true. |
| Run (`R`) | Boolean | item | Execute the settle. |

| out | type | access | description |
|---|---|---|---|
| Settled Fragments (`F`) | Mesh | list | Fragments translated so solids no longer interpenetrate. |
| Transforms (`X`) | Transform | list | Net rigid translation applied to each fragment (input order). |
| Max Penetration (`Mp`) | Number | item | Final maximum pairwise penetration depth (should be <= Pt). |
| Report (`Rp`) | Text | item | Per-iteration max penetration + close/fallback diagnostics. |

Related:
- Frahan > Kintsugi > Frahan Kintsugi - Produces the placed fragments this pass de-penetrates
- Frahan > Kintsugi > Load Scan Fragments - Source of real (open) scan shards that need closing before the inside test

### Fracture Roughen  (`Roughen`)

- GUID: `F2D00504-2026-4522-B0B0-1ABE15A0CAFE`  |  icon: `DiffusionDenoiser.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Kintsugi/FractureRoughenComponent.cs`
- Algorithm: **Fracture surface roughen (shared fractal field)** - Displaces cut-region vertices by a single world-position fractal noise 
- Give Voronoi shatter fragments worn, irregular fracture surfaces  using a shared world-position fractal field, so the pieces still  fit together. Wire between Frahan Fragment Shatter and Frahan  Kintsugi.

| in | type | access | description |
|---|---|---|---|
| Fragments (`F`) | Mesh | list | List of fragments to roughen (typically from Frahan Fragment Shatter). |
| Amplitude (`A`) | Number | item | Displacement amplitude as a FRACTION of the bounding box diagonal.  Default 0.02 (2% of bbox). Larger = deeper worn relief. |
| Roughness (`R`) | Number | item | Per-octave amplitude falloff (persistence). 0.5 = balanced.  Higher = rougher/grittier; lower = smoother. Default 0.5. |
| Seed (`S`) | Integer | item | RNG seed for the shared noise field. SAME seed = SAME field for  every fragment (required for mating). Default 42. |
| Run (`Run`) | Boolean | item | Apply. |
| Frequency (`Fq`) | Number | item | Noise frequency as cycles across the bounding box diagonal.  Lower = broad gentle waves; higher = fine pitting. Default 3.0. |
| Octaves (`Oc`) | Integer | item | Fractal octaves summed (each doubles frequency, halves amplitude).  1 = smooth, 4-5 = rich worn detail. Default 4. |
| Cap Cuts (`Cap`) | Boolean | item | TRUE = FillHoles first so the cut becomes a worn SURFACE (closed  fragment). FALSE = displace only the open rim. Default TRUE. |

| out | type | access | description |
|---|---|---|---|
| Roughened Fragments (`Fo`) | Mesh | list | Fragments with worn, irregular fracture surfaces (still mating). |
| Displaced Count (`Dc`) | Integer | item | Total cut-region vertices displaced (across all fragments). |
| Report (`Rp`) | Text | item | Per-fragment displacement count. |

### Frahan Fragment Shatter  (`Shatter`)

- GUID: `F2D00502-2026-4522-B0B0-1ABE15A0CAFE`  |  icon: `SyntheticBlock.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Kintsugi/FragmentShatterComponent.cs`
- Algorithm: **Voronoi shatter for fracture test-beds** - Frahan-original: deterministic seed -> pairwise bisector planes -> 
- Voronoi-shatter a solid input mesh into N fragments suitable  for round-trip testing of Frahan Kintsugi.  Outputs each Voronoi cell as a separate mesh with the original  outer surface plus fresh fracture rims on the cut surfaces.

| in | type | access | description |
|---|---|---|---|
| Solid (`M`) | Mesh | item | Input mesh to shatter. Pot, sphere, sculpture, etc. Should be  closed-ish; tiny gaps are tolerated. |
| Fragment Count (`N`) | Integer | item | Number of Voronoi cells (= output fragments). Practical range 2..30.  Higher = slower (O(N^2) plane clips). |
| Seed (`S`) | Integer | item | Deterministic random seed. Re-running with the same value  produces the same shatter pattern. Default 42. |
| Jitter (`J`) | Number | item | Voronoi seed positional noise relative to the bbox diagonal.  0 = grid layout, 1 = full random in the bbox. Default 0.6  (mostly random with a touch of regularity for predictable demos). |
| Min Fragment Volume (`Vmin`) | Number | item | Drop any cell whose volume is below this fraction of the  input volume (0 to disable). Default 0.005 = 0.5% drops  slivers from edge cells. |
| Run (`R`) | Boolean | item | Execute the shatter. |

| out | type | access | description |
|---|---|---|---|
| Fragments (`F`) | Mesh | list | Voronoi-shattered fragments. Wire directly into Kintsugi. |
| Seed Points (`Sp`) | Point | list | Voronoi seed points used (one per fragment). Diagnostic. |
| Drop Count (`Dc`) | Integer | item | Number of cells dropped under Min Fragment Volume. |
| Report (`Rp`) | Text | item | Per-cell volume / face / vertex / naked-edge counts. |

### Frahan Kintsugi  (`Kintsugi`)

- GUID: `F2D00501-2026-4522-B0B0-1ABE15A0CAFE`  |  icon: `KintsugiAssemble.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/KintsugiAssemblyComponent.cs`
- Algorithm: **Auto-agglomerative outer loop** - Clean-room translation of PuzzleFusion++ Section 3 outer schedule
- 3D mesh fracture-assembly via naked-edge rim matching.  Each fragment's open-boundary loops are treated as 3D  panels and joined by the same deterministic 5-stage  edge-matching pipeline used by Frahan Trencadís EdgeMatch.  Inspired by PuzzleFusion++ but no learned model; runs  entirely in-process. Best when fracture rims are clean  and well-defined. [Wang et al. 2025]

| in | type | access | description |
|---|---|---|---|
| Fragments (`F`) | Mesh | list | List of mesh fragments to reassemble. First fragment is  anchored at the identity transform; all others are placed  relative to it. Each mesh must have at least one open  boundary loop (naked edges) for the GEOMETRIC path.  For Mode=Port: OPTIONAL when Point Clouds (PC) is wired -- the  port path derives output meshes from the point clouds directly  if Fragments is unwired. |
| Joint Width (`J`) | Number | item | Edge-match residual tolerance (document units). Larger =  more forgiving rim alignment. Default 1.0. |
| Sample Spacing (`Sp`) | Number | item | Arc-length spacing along each naked-edge loop. Match the  mesh's edge length. Default 1.0. |
| Break Angle Deg (`Ba`) | Number | item | Curvature peak threshold for segment break (degrees).  Lower = more sensitive (more segments per rim). Default 8  (was 25 -- pottery rims are usually smoother than wood,  needs lower threshold to find the curvature peaks). |
| Min Segment Length (`Ms`) | Number | item | Below this chord length a rim segment is treated as noise.  Lower = preserves shorter notch features. Default 1.0  (was 5 -- too aggressive for dense rim meshes; lots of  real notches got filtered out as noise). |
| Beam Width (`Bw`) | Integer | item | Beam-search concurrent states. Pottery fragments have more  ambiguous matches than wood; 32 recommended. |
| Max Iterations (`Mi`) | Integer | item | AssemblySolver inner-loop iteration cap (per round). |
| Min Loop Length (`Ll`) | Number | item | Naked-edge loops shorter than this are ignored as noise  (e.g. tiny holes from mesh artifacts). Default 10. |
| Max Rounds (`Mr`) | Integer | item | Auto-agglomerative outer-loop cap. After each round,  successfully-placed fragments merge into the anchor cluster  and unplaced fragments retry against the larger cluster.  Mirrors PuzzleFusion++'s up-to-6-iteration outer schedule. |
| Verifier Penetration Tol (`Vp`) | Number | item | Geometric verifier: reject placements whose transformed  mesh penetrates an already-placed mesh by more than this  distance (document units). 0 = disable the verifier.  Replaces PuzzleFusion++'s learned binary verifier. Default 0.5. |
| Diffusion Steps (`T`) | Integer | item | Mode=Port only. Number of diffusion sampling steps. Higher =  better assembly quality, slower. Paper default is 20; for  interactive prototyping use 5-10. Cost scales linearly  (re-encoder runs at each step). |
| Use Port Mode (`Port`) | Boolean | item | FALSE (default) = geometric path via Frahan.EdgeMatching.Core;  no GPL code linked at runtime; in-process; deterministic.  TRUE = GPL-3.0 PuzzleFusion++ learned path via Frahan.Kintsugi.Port;  requires kintsugi.bin weight file in the .gha deploy folder. |
| Run (`R`) | Boolean | item | Execute the solver. |
| Use TorchSharp (`Torch`) | Boolean | item | Mode=Port only. FALSE (default) = manual C# port denoiser  (~3-5% per-layer drift vs paper). TRUE = TorchSharp/libtorch  denoiser using PyTorch's exact kernels for paper-quality  inference. Requires libtorch DLLs in the .gha deploy folder.  Falls back to manual port if TorchSharp init fails. |
| Point Clouds (`PC`) | Point | tree | OPTIONAL Mode=Port override. Per-fragment point cloud as a  Grasshopper tree: one BRANCH per fragment, N=1000 points per  branch. When wired, the Port-mode pipeline uses these points  DIRECTLY for the encoder instead of sampling N=1000 points from  the Fragments meshes. Useful when you have authoritative point  data (e.g. from Load BB Sample) and don't want sampling noise.\n If wired branch-count mismatches Fragments count, this input is  ignored and the mesh sampler runs. Leave unwired for the default  mesh-sampling path. |
| Verifier Accept Threshold (`Vt`) | Number | item | Mode=Port only. Minimum verifier pair-score for a fragment to be  PLACED via the network pose. Fragments whose best pair-score is  below this stay at their INPUT world position and are listed as  Unplaced. Default 0.5 (matches the 'STRONG' tag in the report).  Lower it (e.g. 0.45) to accept the network's near-miss pairs on  hard multi-fragment samples; raise it to demand higher confidence. |
| Non-Crossing (`Nc`) | Boolean | item | Geometric path only. Order-preserving rim correspondence.  FALSE (default) = free nearest-point ICP (unchanged behaviour).  TRUE = monotone, non-crossing point pairing between fracture rims  (OrderedBoundaryMatcher); more robust on wiggly / noisy rims where  free matching tangles. Ignored in Mode=Port. |

| out | type | access | description |
|---|---|---|---|
| Assembled Fragments (`M`) | Mesh | list | Input fragments transformed into their joined placement.  First fragment is at identity; others composed. |
| Transforms (`X`) | Transform | list | Per-fragment rigid SE(3) Transform. Parallel to Fragments. |
| Placed Indices (`Pi`) | Integer | list | Source-list indices of fragments that the solver placed. |
| Unplaced Indices (`Ui`) | Integer | list | Source-list indices of fragments left unjoined (no rim match found). |
| Residuals (`Re`) | Number | list | Per-rim-match ICP residual for diagnostics. |
| Total Residual (`Tr`) | Number | item | Sum of per-rim residuals. |
| Rim Polylines (`Rim`) | Curve | list | Extracted naked-edge rim polylines per fragment (placed  frame). Diagnostic for tuning Sample Spacing / Break Angle. |
| Report (`Rp`) | Text | item | Human-readable assembly summary. |

### Load BB Sample  (`BBLoad`)

- GUID: `F2D00503-2026-4522-B0B0-1ABE15A0CAFE`  |  icon: `LoadScanFragments.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Kintsugi/BreakingBadSampleLoaderComponent.cs`
- Algorithm: **Breaking Bad fragment loader** - Loads upstream PuzzleFusion++ training-distribution fragments from 
- Load a Breaking Bad sample (.bin from extract_breaking_bad_sample.py)  and output per-fragment point clouds + convex-hull meshes ready for  Frahan Kintsugi.

| in | type | access | description |
|---|---|---|---|
| Sample File (`F`) | Text | item | Path to a FRKINTSU .bin produced by extract_breaking_bad_sample.py.  Defaults to the bb_sample_00697.bin generated during the parity work. D:\code_ws\Template-General\outputs\2026-05-22\reference\bb_sample_00697.bin |
| Mesh Style (`MS`) | Integer | item | How to convert the loaded point cloud into a Mesh for  downstream Frahan Kintsugi consumption:\n   0 = point-cloud vertices (DEFAULT, accuracy-preferred).  Builds a 12^3 bbox-subdivided mesh and pulls each vertex  to its nearest input point. Rhino may flag the mesh as  invalid (overlapping verts), but Kintsugi's surface sampler  lands closer to the ORIGINAL point cloud, giving the encoder  more accurate features (better verifier scores).\n   1 = convex hull (display-preferred). Clean manifold via  FPS-subsample + QuickHull; valid mesh but loses interior  fragment shape and fragments at curved fracture interfaces  will visibly interpenetrate when assembled.\n   2 = bbox cube. Always valid, simplest, but Kintsugi sees  a cube and the encoder produces ~unrelated features.\n   3 = high-resolution bbox-pulled (24^3). Use when style 0  still leaves visible interpenetration between assembled  fragments at curved fracture surfaces. ~3750 surface  vertices per fragment; slower to display but tracks the  actual cloud surface closely. |
| Run (`R`) | Boolean | item | Load. |

| out | type | access | description |
|---|---|---|---|
| Fragment Points (`P`) | Point | tree | Per-fragment 3D points (1000 per fragment, flattened across fragments).  Use the Branches output for per-fragment grouping. |
| Fragments (`Frag`) | Mesh | list | Per-fragment convex-hull Mesh (coarse approximation suitable for  Frahan Kintsugi -- it re-samples points from the mesh surface anyway).  Wire this directly into Frahan Kintsugi's Fragments input. |
| Fragment Count (`N`) | Integer | item | Number of fragments in the sample. |
| Report (`Rp`) | Text | item | Sample loader diagnostic. |

### Load Scan Fragments  (`ScanFrags`)

- GUID: `F2D00505-2026-4522-B0B0-1ABE15A0CAFE`  |  icon: `LoadScanFragments.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Kintsugi/LoadScanFragmentsComponent.cs`
- Algorithm: **Load scanned PLY fragments for Kintsugi inference** - Reads scattered scanned fragment .ply files into per-fragment point 
- Load scanned fragment .ply files (mesh or point cloud) into  per-fragment point clouds + meshes for Frahan Kintsugi Port mode.  Wire Points -> Point Clouds and Fragments -> Fragments.

| in | type | access | description |
|---|---|---|---|
| Folder (`D`) | Text | item | Optional folder to scan for *.ply (each file = one fragment).  Used when File Paths is empty. |
| File Paths (`F`) | Text | list | Optional explicit list of .ply file paths (one fragment each).  Takes priority over Folder. |
| Sample Count (`N`) | Integer | item | Points emitted per fragment for the Point Clouds tree.  Upstream convention is 1000. Dense scans are subsampled; sparse  ones are repeated up to N. |
| Split Disjoint (`Sp`) | Boolean | item | TRUE = a single .ply containing many shards is SPLIT into separate  fragments (disjoint mesh pieces, or proximity-clustered points).  Use this for one-file-many-shards scans. FALSE = each .ply is one  fragment. Default TRUE. |
| Cluster Tol (`Ct`) | Number | item | Point-cloud clustering distance (document units) when Split  Disjoint is on and the .ply is a raw cloud (no faces). 0 = auto  (bbox diagonal / 80). Ignored for mesh PLYs (uses disjoint faces). |
| Remove Floor (`Rf`) | Boolean | item | TRUE = RANSAC-detect the dominant plane (the scan floor/ground)  and strip points near it BEFORE splitting, so resting shards  separate into individual fragments. Works at the point level  (shards become point clusters). Default FALSE. |
| Floor Tol (`Ft`) | Number | item | Distance band (document units) around the detected floor plane to  remove. 0 = auto (bbox diagonal / 150). Raise if the floor isn't  fully removed; lower if shard bottoms get clipped. |
| Run (`R`) | Boolean | item | Load. |

| out | type | access | description |
|---|---|---|---|
| Points (`P`) | Point | tree | Per-fragment points as a tree: one BRANCH per fragment, N points  per branch. Wire into Frahan Kintsugi -> Point Clouds. |
| Fragments (`Frag`) | Mesh | list | Per-fragment mesh (the PLY mesh if present, else a coarse  point-pulled mesh). Wire into Frahan Kintsugi -> Fragments. |
| Fragment Count (`Nf`) | Integer | item | Number of fragments loaded. |
| Report (`Rp`) | Text | item | Per-file load diagnostic. |

### Synthetic Block  (`SynBlock`)

- GUID: `F2D00506-2026-4522-B0B0-1ABE15A0CAFE`  |  icon: `SyntheticBlock.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Kintsugi/SyntheticBlockComponent.cs`
- Generate parametric closed stone-block meshes (10 shapes) as  targets for synthetic fracture-assembly training. Wire into  Frahan Fragment Shatter or bake to .3dm for the PotNet-stone  exporter. Distinctive shapes train better than featureless ones.

| in | type | access | description |
|---|---|---|---|
| Shape (`S`) | Integer | item | Block shape index 0..9: 0 Irregular Boulder, 1 Slab, 2 Tapered  Wedge, 3 Pyramidal Frustum, 4 Fluted Drum, 5 Faceted Gem,  6 Bossed Block, 7 Ridged Block, 8 Sculpted Relief, 9 Stepped  Block. For per-object 6-DoF training prefer 6/8 (asymmetric  features); 4/7/9 are periodic/symmetric and 0/1/5 featureless ->  ambiguous. All are fine for shatter / round-trip testing. |
| Size (`Sz`) | Number | item | Longest block dimension in model units. Default 100. |
| Seed (`Sd`) | Integer | item | Deterministic seed (jitter / sculpt / boss placement). Default 42. |
| Feature (`Fa`) | Number | item | Feature strength 0..1: flute depth, boss height, ridge depth,  sculpt amplitude, taper, step height. 0 = near-flat, 1 = bold.  Default 0.5. |
| Resolution (`Rs`) | Integer | item | Tessellation density for featured shapes (grid / around count).  Higher = finer surface detail, more faces. Default 24. |
| Variations (`V`) | Integer | item | Number of seed-varied copies, laid out along +X (1.6*Size apart).  Use to compare shapes by eye or build a multi-object set. Default 1. |

| out | type | access | description |
|---|---|---|---|
| Blocks (`B`) | Mesh | list | Closed block meshes. Wire into Frahan Fragment Shatter or bake  to .3dm for the PotNet-stone exporter. |
| Shape Name (`Sn`) | Text | item | Name of the selected shape. |
| Report (`Rp`) | Text | item | Per-block vertex / face / closed / volume diagnostics. |

Related:
- Frahan > Kintsugi > Frahan Fragment Shatter - Shatter the generated block into fragments for training / round-trip
- Frahan > Kintsugi > Fracture Roughen - Give the shards worn fracture surfaces before assembly
- Frahan > Kintsugi > Frahan Kintsugi - Reassemble the shards (round-trip test of the target)


## Lab

### CVT Seeds (Geogram)  (`CvtGeogram`)

- GUID: `F2D000C5-6E06-4F2D-A0C5-7E60660C0AC1`  |  icon: `Voronoi.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/GeogramTestComponents.cs`
- Algorithm: **Centroidal Voronoi tessellation (Lloyd relaxation)** - Lloyd, S. (1982). Least squares quantization in PCM. IEEE Trans. Inf. Theory IT-28:129-137
- Compute optimized seed positions on a surface via  centroidal Voronoi tessellation (Lloyd + Newton-Lloyd).  Output feeds directly into Voronoi Block Partition.  Implements CVT (Lloyd 1982) via Geogram.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Input surface. |
| Points (`N`) | Integer | item | Seed count (50..500 typical for masonry blocks). |
| Lloyd Iters (`L`) | Integer | item | Lloyd relaxation iterations. |
| Newton Iters (`Nw`) | Integer | item | Newton iterations after Lloyd. |
| Run (`Run`) | Boolean | item | Set true to compute. |

| out | type | access | description |
|---|---|---|---|
| Seeds (`S`) | Point | list | CVT seed positions. |
| Available (`Av`) | Boolean | item | True iff Geogram shim loaded. |
| Report (`R`) | Text | item | Diagnostic report. |

Related:
- Frahan > Quarry > Quarry DFN - Production DFN generator consumes CVT seeds.
- Frahan > Quarry > Joint Set - Joint-set parameters drive CVT seed distribution.
- Frahan > 2D > Pack 2D Trencadis Catalog - CVD-Lloyd seeds are also used for Trencadis catalog placement.

### Download Frahan Data  (`GetData`)

- GUID: `F2D05A08-1A2B-4C3D-9E4F-5A6B7C8D9E08`  |  icon: `Downsample.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/DownloadFrahanDataComponent.cs`
- Algorithm: **On-demand data fetch + SHA-256 verify** - Frahan-original distribution helper
- Download the optional large plugin data (Kintsugi Mode=Port weights +  torch/CUDA runtime, and/or examples) from a release manifest into the  folder beside the .gha, with SHA-256 verification. Runs on a background  thread; the canvas stays responsive. Files already present and verified  are skipped.

| in | type | access | description |
|---|---|---|---|
| Manifest URL (`U`) | Text | item | URL of the data manifest (plain text; see component help). Usually a  manifest.txt attached to the GitHub Release. |
| What (`W`) | Integer | item | 0 = Port (kintsugi.bin + torch/CUDA runtime), 1 = Examples, 2 = All. Default 0. |
| Run (`R`) | Boolean | item | Set true to download. |

| out | type | access | description |
|---|---|---|---|
| Port Ready (`Ok`) | Boolean | item | True if kintsugi.bin is present beside the .gha (Mode=Port can run). |
| Installed (`I`) | Text | list | Files installed / skipped this run. |
| Report (`R`) | Text | item | Status summary. |

Related:
- Frahan > Kintsugi > Frahan Kintsugi - Mode=Port needs kintsugi.bin + the torch/CUDA runtime this fetches.

### Frahan Density-Watershed Zones  (`BCOWatershed`)

- GUID: `F2D0BC12-1234-4F2D-A0B0-7E60CADA15B2`  |  icon: `Voronoi.png`  |  exposure: `tertiary`  |  source: `src/Frahan.StonePack.GH/BlockCutOptInspectorComponents.cs`
- Algorithm: **Density-watershed partition (BlockCutOpt I5)** - Frahan-original
- Adaptive sub-division of the tested area by 2D fracture- density watershed (synthesis I5). Each zone boundary snaps  to high-density ridges so the unavoidable boundary penalty  lands on already-broken rock. Feed FracturePlanes from  Mesh2FxPl or any other planes-producing component.  Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Tested Area (`A`) | Box | item | Bench bounding box (m). |
| Fracture Planes (`F`) | Generic | list | List<FracturePlane> (e.g. from Mesh2FxPl). |
| Bandwidth (m) (`H`) | Number | item | Gaussian KDE bandwidth. |
| Raster Cell (m) (`Rc`) | Number | item | Density-raster cell size; 0 = bandwidth/4. |

| out | type | access | description |
|---|---|---|---|
| Zone Boxes (`B`) | Box | list | One axis-aligned Box per watershed basin. |
| Zone Ids (`Z`) | Text | list | Synthetic id per zone. |
| Zone Count (`N`) | Integer | item | Total number of zones. |

Related:
- Frahan > Quarry > BlockCutOpt Solve - Density-watershed sub-zones feed the production solver's I10 sub-division pass.
- Frahan > Mesh > Bench From Mesh - Bench mesh source for the tested area whose density is partitioned here.

### Frahan Fisher-Robust BCO  (`BCORobust`)

- GUID: `F2D0BC11-1234-4F2D-A0B0-7E60CADA15B1`  |  icon: `BlockCutOpt.png`  |  exposure: `tertiary`  |  source: `src/Frahan.StonePack.GH/BlockCutOptInspectorComponents.cs`
- Algorithm: **Fisher-distribution joint-scatter robustness sampling** - Azarafza et al. (2016) granite block-cut + Fisher-distribution joint scatter
- Run BlockCutOpt M times against M Fisher-perturbed DFN  realisations of the same joint sets; return p10 / p50 / p90  recovery percent and the median psi. The robust optimum  direction is the median psi, not the single deterministic  best (Azarafza 2016 / synthesis I8). Parallel joint-set  lists must all be the same length.  Implements Fisher-scatter robustness sampling (Azarafza 2016).

| in | type | access | description |
|---|---|---|---|
| Tested Area (`A`) | Box | item | Bench bounding box (m). |
| Dip Directions (deg) (`Dd`) | Number | list | Per joint set, in [0,360). |
| Dips (deg) (`Dp`) | Number | list | Per joint set, in [0,90]. |
| Mean Spacings (m) (`Sp`) | Number | list | Per joint set. |
| Scatters (deg) (`Sc`) | Number | list | Fisher scatter per joint set. |
| Block X (`Lx`) | Number | item | Block length (m). |
| Block Y (`Ly`) | Number | item | Block width (m). |
| Block Z (`Lz`) | Number | item | Block height (m). |
| Kerf (`K`) | Number | item | Material-lost-by-quarrying (m). |
| Psi Step (deg) (`Pdeg`) | Number | item | Angular search step. |
| MC Samples (`M`) | Integer | item | Monte Carlo sample count. |
| Base Seed (`S`) | Integer | item | Reproducibility seed. |

| out | type | access | description |
|---|---|---|---|
| Recovery p10 % (`R10`) | Number | item | 10th-percentile recovery (robust score). |
| Recovery p50 % (`R50`) | Number | item | Median recovery. |
| Recovery p90 % (`R90`) | Number | item | 90th-percentile recovery. |
| Recovery Mean % (`Rm`) | Number | item | Mean recovery. |
| Recovery StdDev % (`Rs`) | Number | item | Sample standard deviation. |
| Median Psi (deg) (`Psi`) | Number | item | Median psi across MC samples. |
| Per-Sample Recovery % (`Rk`) | Number | list | All M recovery values. |
| Per-Sample Psi (deg) (`Pk`) | Number | list | All M psi values. |

Related:
- Frahan > Quarry > BlockCutOpt Solve - Production single-best-fit solver; Fisher-robust extension reports stability of that optimum under fracture-orientation noise.
- Frahan > Quarry > Joint Set - Source of fracture-orientation distribution used here.

### Frahan Mixed-Size Block Pack  (`BCOMixedPack`)

- GUID: `F2D0BC17-1234-4F2D-A0B0-7E60CADA15B7`  |  icon: `BinPack.png`  |  exposure: `quinary`  |  source: `src/Frahan.StonePack.GH/BlockCutOptInspectorComponents.cs`
- Algorithm: **Deepest-left-bottom-fill (DLBF) mixed-size packing** - Chehrazad, R., Roose, D., Wauters, T. (2025). A fast and scalable deepest-left-bottom-fill algorithm. Int. J. Production Research 63:6606-6629
- Pack a catalogue of mixed-size blocks (multiple Width x  Depth pairs each with its own revenue) into the tested  area using the DLBF greedy heuristic (Chehrazad 2025,  synthesis I7). Forbidden boxes mark fracture-intersected  regions that must stay empty. Returns one Box per placed  piece.  Implements DLBF (Chehrazad 2025).

| in | type | access | description |
|---|---|---|---|
| Tested Area (`A`) | Box | item | Bench bounding box (m). |
| Piece Ids (`Id`) | Text | list | One id per catalogue entry. |
| Piece Widths (m) (`W`) | Number | list | Width per entry (X). |
| Piece Depths (m) (`D`) | Number | list | Depth per entry (Y). |
| Piece Revenues (`Rev`) | Number | list | RMV per entry. |
| Block Height (m) (`Lz`) | Number | item | Common Z extrusion height for output Boxes. |
| Forbidden Boxes (`X`) | Box | list | Optional forbidden regions (e.g. fracture-intersected cells). |
| Grid Cell (m) (`Gc`) | Number | item | Discretisation cell; 0 = min(W,D)/4. |

| out | type | access | description |
|---|---|---|---|
| Placed Boxes (`B`) | Box | list | One Box per placed piece. |
| Placed Ids (`I`) | Text | list | Id of each placed piece (multiplicity preserved). |
| Total Revenue (`Pi`) | Number | item | Sum of placed-piece revenues. |
| Covered Area (m^2) (`Ar`) | Number | item | Sum of placed-piece footprint areas. |
| Placed Count (`N`) | Integer | item | Number of placements. |

Related:
- Frahan > Masonry > Ashlar Pack - Production 3D packer; this mixed-size variant is the heterogeneous-block research path.
- Frahan > Masonry > Best Fit Pack - Production rubble packer for varied-height inputs.
- Frahan > Quarry > BlockCutOpt Solve - Upstream source of the block inventory this packer consumes.

### Frahan Pareto Front Inspector  (`BCOPareto`)

- GUID: `F2D0BC10-1234-4F2D-A0B0-7E60CADA15B0`  |  icon: `YieldEstimator.png`  |  exposure: `tertiary`  |  source: `src/Frahan.StonePack.GH/BlockCutOptInspectorComponents.cs`
- Algorithm: **Pareto multi-objective front (BCSdbBV cost axis)** - Jalalian (2023) BCSdbBV cost objective = cutting-surface area / block value
- Run BlockCutOpt with 4-axis Pareto optimisation and emit the  recovery-max, revenue-max, kerf-time-min and BCSdbBV-min  points side-by-side, per sub-zone. Use when the BCOOmni  single best-recovery output is not enough and you need to  compare trade-offs explicitly.  Implements BCSdbBV cost axis (Jalalian 2023).

| in | type | access | description |
|---|---|---|---|
| Tested Area (`A`) | Box | item | Bench bounding box (m). |
| Fractures (`F`) | Mesh | item | Fracture mesh. |
| Mx (`Mx`) | Integer | item | Uniform sub-divisions in X. |
| My (`My`) | Integer | item | Uniform sub-divisions in Y. |
| Block X (`Lx`) | Number | item | Block length (m). |
| Block Y (`Ly`) | Number | item | Block width (m). |
| Block Z (`Lz`) | Number | item | Block height (m). |
| Kerf (`K`) | Number | item | Material-lost-by-quarrying (m). |
| Psi Step (deg) (`Pdeg`) | Number | item | Angular search step. |
| RMV per Block (`Rmv`) | Number | item | Jalalian relative money value per block (BCSdbBV denominator factor). |
| BV per Block (`Bv`) | Number | item | Jalalian block value per block. |
| Kerf Time / Block (min) (`Kt`) | Number | item | Saw kerf time per block (min). |

| out | type | access | description |
|---|---|---|---|
| Zone Id (`Z`) | Text | list | Sub-zone id per row. |
| Recovery Max -- Count (`Nr`) | Integer | list | Best-recovery non-intersected count per zone. |
| Recovery Max -- Psi (deg) (`Pr`) | Number | list | Best-recovery psi per zone. |
| Revenue Max -- Pi (`Pi`) | Number | list | Best-revenue Pi per zone. |
| Revenue Max -- Psi (deg) (`Ppi`) | Number | list | Best-revenue psi per zone. |
| Kerf Time Min -- tau (`Tau`) | Number | list | Min kerf-time tau per zone. |
| Kerf Time Min -- Psi (deg) (`Ptau`) | Number | list | Min-kerf-time psi per zone. |
| BCSdbBV Min (`BCS`) | Number | list | Min BCSdbBV cost (Jalalian) per zone. |
| BCSdbBV Min -- Psi (deg) (`Pbcs`) | Number | list | Min-BCSdbBV psi per zone. |
| Pareto Front Size (`Fz`) | Integer | list | Number of non-dominated points per zone. |
| Total Evaluations (`Ev`) | Integer | item | Sum of (psi, dx, dy) samples evaluated. |
| Elapsed (ms) (`T`) | Number | item | Wall-clock duration. |

Related:
- Frahan > Quarry > BlockCutOpt Solve - Production solver; this inspector visualises the Pareto front of a solver run.
- Frahan > Quarry > BlockCutOpt Omni Solve - Multi-objective production solver.

### Frahan VTU Export  (`VtuOut`)

- GUID: `F2D0BC13-1234-4F2D-A0B0-7E60CADA15B3`  |  icon: `GcodeExport.png`  |  exposure: `tertiary`  |  source: `src/Frahan.StonePack.GH/BlockCutOptInspectorComponents.cs`
- Run BlockCutOpt then dump the optimal cutting grid to a  ParaView .vtu file. Two cell sets: cell_status=1 (non- intersected, ready-to-quarry), cell_status=0 (intersected,  discarded). Matches BlockCutOpt 2020 Figures 3 and 6.

| in | type | access | description |
|---|---|---|---|
| Tested Area (`A`) | Box | item | Bench bounding box (m). |
| Fractures (`F`) | Mesh | item | Fracture mesh. |
| VTU Path (`Path`) | Text | item | Output .vtu file path. |
| Block X (`Lx`) | Number | item | Block length (m). |
| Block Y (`Ly`) | Number | item | Block width (m). |
| Block Z (`Lz`) | Number | item | Block height (m). |
| Kerf (`K`) | Number | item | Material-lost-by-quarrying (m). |
| Psi Step (deg) (`Pdeg`) | Number | item | Angular search step. |
| Write (`W`) | Boolean | item | Trigger; set true to write the file. |

| out | type | access | description |
|---|---|---|---|
| Non-Intersected (`NI`) | Integer | item | Cell count tagged status=1. |
| Intersected (`I`) | Integer | item | Cell count tagged status=0. |
| Recovery % (`R`) | Number | item | Recovery percent at the winning (psi, dx, dy). |
| Written Path (`Out`) | Text | item | Path written, or empty when Write=false. |

Related:
- Frahan > Quarry > BlockCutOpt Solve - Source of the optimised cutting grid being exported to VTU for external visualisation.

### Geodesic Voronoi (CGAL)  (`GeodesicVoronoiCgal`)

- GUID: `F2D000A8-CADC-4F2D-A0A8-7E60CADA15A0`  |  icon: `GeodesicPath.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/CgalTestComponents.cs`
- Algorithm: **Heat-method geodesic distance** - CGAL Heat_method_3 (Crane-Weischedel-Wardetzky heat method)
- Split a mesh surface into Voronoi cells driven by  geodesic distance from user-supplied seed points (Crane  et al. Heat Method 2013). Each seed snaps to its nearest  vertex; each face joins the cell of the seed with the  shortest on-surface distance. Cuts follow surface  curvature - neat boundaries on curved meshes where  Euclidean Voronoi would slice through the form.  Wraps CGAL Heat_method_3.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Input surface (2-manifold gives a stable cotangent Laplacian). |
| Seeds (`S`) | Point | list | Seed points - each is snapped to the nearest mesh vertex.  Place 5-50 for a useful tessellation. |
| Run (`Run`) | Boolean | item | Set true to compute. |

| out | type | access | description |
|---|---|---|---|
| Cells (`C`) | Mesh | list | One mesh per geodesic Voronoi cell. |
| Cell Count (`N`) | Integer | item | Number of non-empty cells (== seed count when input is a single connected component). |
| Available (`Av`) | Boolean | item | True iff CGAL shim loaded. |
| Report (`R`) | Text | item | Diagnostic report. |

Related:
- Frahan > Quarry > Quarry DFN - Production Voronoi-based DFN generator; this geodesic variant is the research path.
- Frahan > Quarry > Joint Set - Joint-set seeds for DFN generation.

### Mesh CSG (CGAL)  (`MeshCsgCgal`)

- GUID: `F2D000A0-CADC-4F2D-A0A0-7E60CADA15A0`  |  icon: `CoacdDecompose.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/CgalTestComponents.cs`
- Algorithm: **Mesh corefinement boolean** - CGAL Polygon Mesh Processing (corefine_and_compute_boolean_operations)
- Boolean operation between two meshes via the CGAL native  shim. Falls back transparently to in-tree BSP CSG when the  shim is absent. Reports which back-end actually ran.  Wraps CGAL corefine_and_compute_boolean_operations.

| in | type | access | description |
|---|---|---|---|
| Mesh A (`A`) | Mesh | item | First operand. |
| Mesh B (`B`) | Mesh | item | Second operand. |
| Operation (`Op`) | Integer | item | 0 = Union, 1 = Intersection, 2 = Difference (A − B). |
| Use Hybrid Kernel (`Hybrid`) | Boolean | item | False = EPICK only (default, fast). True = HYBRID — EPICK  storage + EPECK intersection construction. Use Hybrid when  inputs may be numerically fragile (multi-cut chains,  near-tangent contacts). |
| Run (`Run`) | Boolean | item | Set true to compute. |

| out | type | access | description |
|---|---|---|---|
| Result (`M`) | Mesh | item | Result mesh. |
| Backend (`B`) | Text | item | Which kernel ran: 'CGAL' or 'ManagedBsp'. |
| Available (`Av`) | Boolean | item | True iff the CGAL native shim is loadable. |
| Version (`V`) | Text | item | Reported version string from the shim. |
| Report (`R`) | Text | item | Diagnostic report (timing, kernel, fallback note). |

Related:
- Frahan > Masonry > Mesh CSG - Production-grade boolean mesh CSG; this CGAL component is the research probe.
- Frahan > Masonry > Slab Cut By Fractures - Production cutting pipeline for slab + fracture inputs.

### Mesh Decimate (Auto)  (`DecimateAuto`)

- GUID: `F2D000D1-A070-4F2D-A0D1-7E60A07000D1`  |  icon: `Downsample.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/AutoMeshComponents.cs`
- Mesh decimation via the best available backend (Geogram  vertex-clustering preferred; CGAL edge-collapse fallback).  Single ratio in (0,1) is mapped to backend-specific params.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Input mesh. |
| Target Ratio (`R`) | Number | item | In (0, 1). Higher = more detail kept. Mapped to bin count  (Geogram) or edge-count ratio (CGAL). |
| Run (`Run`) | Boolean | item | Set true to compute. |

| out | type | access | description |
|---|---|---|---|
| Decimated (`M`) | Mesh | item | Decimated mesh. |
| Backend (`B`) | Text | item | Which backend ran. |
| Diagnostics (`D`) | Text | item | Loaded shim versions. |
| Report (`R`) | Text | item | Diagnostic report. |

Related:
- Frahan > Mesh > Mesh Repair - Mesh-quality production path; no production decimate yet.

### Mesh Decimate (CGAL)  (`DecimateCgal`)

- GUID: `F2D000A5-CADC-4F2D-A0A5-7E60CADA15A0`  |  icon: `Downsample.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/CgalTestComponents.cs`
- Algorithm: **Quadric edge-collapse simplification** - CGAL Surface_mesh_simplification (Lindstrom-Turk edge-collapse policies)
- Mesh simplification via CGAL Surface_mesh_simplification  (quadric-error edge collapse, Lindstrom-Turk policies).  Three stop modes: count ratio, target edge count, edge  length. Run before CoACD to speed up decomposition on  scanned statue input.  Wraps CGAL Surface_mesh_simplification (Lindstrom-Turk policies).

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Input mesh. Should be a valid 2-manifold for stable results. |
| Stop Kind (`K`) | Integer | item | 0 = count ratio (remaining/initial, value in (0, 1)). Most common.\n 1 = target edge count (>= 1).\n 2 = minimum edge length (> 0); preserves edges shorter than the  threshold (good for keeping sharp features). |
| Stop Value (`V`) | Number | item | Threshold meaning depends on Stop Kind:\n   Kind 0: 0.5 = halve edge count.\n   Kind 1: 5000 = stop at 5000 edges.\n   Kind 2: 0.05 = stop when next edge to collapse is >= 0.05. |
| Run (`Run`) | Boolean | item | Set true to compute. |

| out | type | access | description |
|---|---|---|---|
| Decimated (`M`) | Mesh | item | Decimated mesh. |
| Available (`Av`) | Boolean | item | True iff the CGAL native shim is loadable. |
| Report (`R`) | Text | item | Diagnostic report (V/F counts in/out, runtime). |

Related:
- Frahan > Mesh > Mesh Repair - Mesh-quality production sibling; no dedicated decimate component on the production side yet.

### Mesh Decimate (Geogram)  (`DecimateGeogram`)

- GUID: `F2D000C0-6E06-4F2D-A0C0-7E60660C0AC1`  |  icon: `Downsample.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/GeogramTestComponents.cs`
- Algorithm: **Vertex-clustering decimation** - Levy, B., INRIA/ALICE. Geogram v1.9.9. BSD-3. https://github.com/BrunoLevy/geogram
- Vertex-clustering decimation via Geogram  (GEO::mesh_decimate_vertex_clustering). Voxel-bin algorithm:  higher Bins = more detail. Different from CGAL's edge-collapse  decimation - use this for very high-poly scans where you want  controlled spatial sampling, and CGAL's for precise count  targeting.  Wraps Geogram mesh_decimate_vertex_clustering.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Input mesh. |
| Bins (`B`) | Integer | item | Voxel grid resolution per bbox dimension. Higher = more  detail (less aggressive decimation). Typical 50..300;  default 100. Minimum 2. |
| Mode (`Mo`) | Integer | item | Bitwise OR of mode flags:\n   0 = FAST (no extra cleanup)\n   1 = REMOVE_DUPLICATES\n   2 = REMOVE_DEGREE_3\n   4 = KEEP_BORDERS\n   7 = DEFAULT (1|2|4) |
| Run (`Run`) | Boolean | item | Set true to compute. |

| out | type | access | description |
|---|---|---|---|
| Decimated (`M`) | Mesh | item | Decimated mesh. |
| Backend (`B`) | Text | item | Reported version from the loaded shim. |
| Available (`Av`) | Boolean | item | True iff the Geogram native shim is loadable. |
| Report (`R`) | Text | item | Diagnostic report (V/F counts in/out, runtime). |

Related:
- Frahan > Mesh > Mesh Repair - Mesh-quality production path; no production decimate component yet.

### Mesh Decompose (CoACD)  (`DecomposeCoacd`)

- GUID: `F2D000B0-C0AC-4F2D-A0B0-7E60C0AC1DB0`  |  icon: `CoacdDecompose.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/CoacdTestComponents.cs`
- Algorithm: **Collision-aware approximate convex decomposition** - Wei, J., Liu, M., Wang, J. et al. (2022). Approximate Convex Decomposition for 3D Meshes with Collision-Aware Concavity and Tree Search. SIGGRAPH 2022
- Approximate convex decomposition via the CoACD native shim.  Input must be 2-manifold for the lightweight build (no  manifold preprocess); pre-clean with Mesh Repair (CGAL) if  input is non-manifold and the OpenVDB-equipped build is not  loaded.  Wraps CoACD (Wei et al. 2022).

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Input mesh. Must be 2-manifold when running against the  lightweight (WITH_3RD_PARTY_LIBS=OFF) shim build. The full  build accepts non-manifold input via OpenVDB preprocessing. |
| Threshold (`T`) | Number | item | Concavity threshold. Lower = more pieces, finer fit.  Default 0.05 (normalized [0..1]) or 0.05 metres if Real  Metric is true. |
| Max Hulls (`N`) | Integer | item | Cap on output piece count. -1 = unlimited. |
| Preprocess (`P`) | Integer | item | 0 = auto, 1 = on, 2 = off. Auto runs OpenVDB-based  manifold-isation only when input is non-manifold (requires  WITH_3RD_PARTY_LIBS=ON build). |
| Real Metric (`RM`) | Boolean | item | When true, Threshold is interpreted as metres rather than  CoACD's normalized [0..1] units. Use for statue-scale input. |
| Run (`Run`) | Boolean | item | Set true to compute. |
| MCTS Iters (`mi`) | Integer | item | MCTS iterations per cut (default 150). |
| MCTS Depth (`md`) | Integer | item | MCTS tree depth (default 3). |
| MCTS Nodes (`mn`) | Integer | item | MCTS nodes per cut (default 20). |
| Seed (`S`) | Integer | item | RNG seed for reproducibility (default 0). |
| PCA (`pca`) | Boolean | item | Align cuts to PCA frame (default false). World-axis cuts  are usually better for architectural input. |

| out | type | access | description |
|---|---|---|---|
| Convex Hulls (`H`) | Mesh | list | List of convex pieces approximating the input. |
| Count (`N`) | Integer | item | Number of hulls produced. |
| Runtime (`T`) | Number | item | Runtime in milliseconds. |
| Backend (`B`) | Text | item | Reported version + build-flag status from the loaded shim.  Use this to confirm whether OpenVDB-based manifold  preprocessing is available. |
| Available (`Av`) | Boolean | item | True iff the CoACD native shim is loadable. |
| Report (`R`) | Text | item | Diagnostic report (input/output sizes, runtime, parameters). |

Related:
- Frahan > Masonry > Masonry Assembly - Convex decomposition feeds collision-detection for masonry block assembly.
- Frahan > Masonry > Auto Interfaces - Convex parts simplify interface auto-detection between non-convex blocks.
- Frahan > Mesh > Mesh Diagnostics - Diagnose mesh convexity before decomposing.

### Mesh Fill Holes (Geogram)  (`FillHolesGeogram`)

- GUID: `F2D000C7-6E06-4F2D-A0C7-7E60660C0AC1`  |  icon: `PoissonReconstruct.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/GeogramTestComponents.cs`
- Algorithm: **Boundary-loop hole filling** - Levy, B., INRIA/ALICE. Geogram v1.9.9. BSD-3. https://github.com/BrunoLevy/geogram
- Triangulate open boundary loops smaller than a size threshold.  Use it to close sliver-holes in a Voronoi cell sub-mesh while  keeping the main outer boundary open - exactly what BFF needs  to flatten without self-overlap. BSD-3 (GEO::fill_holes).  Wraps Geogram fill_holes.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Input mesh (open patch). |
| Max Area (`A`) | Number | item | Maximum hole AREA (input units squared) to fill. 0 = fill  nothing. A very large value (1e30) fills every hole. |
| Max Edges (`E`) | Integer | item | Maximum boundary edges per hole. 0 = no edge limit (size  governed by area alone). Set to ~30 to target only  sliver-style holes that have few edges. |
| Repair After (`R`) | Boolean | item | Run mesh_repair (DEFAULT mode) after filling to clean up  duplicate vertices / facets the hole triangulator may  leave behind. |
| Run (`Run`) | Boolean | item | Set true to compute. |

| out | type | access | description |
|---|---|---|---|
| Filled (`M`) | Mesh | item | Mesh with small holes triangulated. |
| Available (`Av`) | Boolean | item | True iff Geogram shim loaded. |
| Report (`R`) | Text | item | Diagnostic report. |

Related:
- Frahan > Mesh > Mesh Repair - Production mesh-repair production path; hole-fill is a research sub-operation.

### Mesh Remesh (Geogram)  (`RemeshGeogram`)

- GUID: `F2D000C3-6E06-4F2D-A0C3-7E60660C0AC1`  |  icon: `SurfaceTile.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/GeogramTestComponents.cs`
- Algorithm: **Centroidal-Voronoi remeshing** - Levy, B., INRIA/ALICE. Geogram v1.9.9. BSD-3. https://github.com/BrunoLevy/geogram
- Uniform surface remeshing via centroidal-Voronoi-driven  Lloyd + Newton optimization (GEO::remesh_smooth). Accepts a direct  Mesh OR a File Path (.ply / .obj / .stl / .wrl; takes precedence).  Runs on a background thread (Run gate) so the canvas never freezes.  Wraps Geogram remesh_smooth.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Input mesh (optional if File Path is given). |
| Points (`N`) | Integer | item | Desired vertex count in output (5000..50000 typical). |
| Lloyd Iters (`L`) | Integer | item | Lloyd relaxation iterations (default 5). |
| Newton Iters (`Nw`) | Integer | item | Newton iterations after Lloyd (default 30). |
| Run (`Run`) | Boolean | item | Set true to compute. |
| File Path (`F`) | Text | item | Optional mesh file to remesh directly (.ply / .obj / .stl / .wrl).  Takes precedence over the Mesh input. Empty = use the Mesh input. |

| out | type | access | description |
|---|---|---|---|
| Remeshed (`M`) | Mesh | item | Remeshed surface. |
| Available (`Av`) | Boolean | item | True iff Geogram shim loaded. |
| Report (`R`) | Text | item | Diagnostic report. |

Related:
- Frahan > Mesh > Mesh Repair - Mesh-quality production path; remesh is the research variant for adaptive density.
- Frahan > Mesh > Mesh Diagnostics - Pre-remesh diagnostic.

### Mesh Repair (Auto)  (`RepairAuto`)

- GUID: `F2D000D0-A070-4F2D-A0D0-7E60A07000D0`  |  icon: `PoissonReconstruct.png`  |  exposure: `hidden`  |  source: `src/Frahan.StonePack.GH/AutoMeshComponents.cs`
- Topology-aware mesh repair via the best available backend  (Geogram first, CGAL fallback). Backend output reports  which one ran.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Input mesh. |
| Run (`Run`) | Boolean | item | Set true to compute. |

| out | type | access | description |
|---|---|---|---|
| Repaired (`M`) | Mesh | item | Repaired mesh. |
| Backend (`B`) | Text | item | Which backend ran: Geogram, Cgal, or None. |
| Diagnostics (`D`) | Text | item | Loaded shim versions. |
| Report (`R`) | Text | item | Diagnostic report. |

Related:
- Frahan > Mesh > Sanitize Mesh - SUPERSEDED BY: Sanitize Mesh (Backend = Auto) does the same Geogram->CGAL repair plus a CGAL-Ready verdict.
- Frahan > Mesh > Mesh Diagnostics - Diagnose mesh before repairing.

### Mesh Repair (CGAL)  (`MeshRepairCgal`)

- GUID: `F2D000A4-CADC-4F2D-A0A4-7E60CADA15A0`  |  icon: `PoissonReconstruct.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/CgalTestComponents.cs`
- Algorithm: **CGAL PMP mesh repair** - CGAL Polygon Mesh Processing (repair package)
- Robust mesh repair via CGAL Polygon Mesh Processing.  Triangulates non-triangle faces, stitches coincident  borders, removes degenerate triangles, and orients faces  outward when the mesh is closed.  Wraps CGAL PMP repair routines.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Input mesh to repair. |
| Run (`Run`) | Boolean | item | Set true to compute. |

| out | type | access | description |
|---|---|---|---|
| Repaired (`M`) | Mesh | item | Repaired mesh. |
| Available (`Av`) | Boolean | item | True iff the CGAL native shim is loadable. |
| Report (`R`) | Text | item | Repair report (vertex/face deltas, runtime). |

Related:
- Frahan > Mesh > Mesh Repair - Production mesh-repair component; this CGAL variant is the research path.
- Frahan > Mesh > Mesh Quality Report - Diagnose mesh quality before deciding to repair.

### Mesh Repair (Geogram)  (`RepairGeogram`)

- GUID: `F2D000C1-6E06-4F2D-A0C1-7E60660C0AC1`  |  icon: `PoissonReconstruct.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/GeogramTestComponents.cs`
- Algorithm: **Geogram mesh repair** - Levy, B., INRIA/ALICE. Geogram v1.9.9. BSD-3. https://github.com/BrunoLevy/geogram
- Topology-aware mesh repair via GEO::mesh_repair  (colocate + remove duplicate facets + triangulate). BSD-3.  Wraps Geogram mesh_repair.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Input mesh. |
| Mode (`Mo`) | Integer | item | Bitwise OR of GeogramRepairMode flags:\n   0 = TopologyOnly   (always done; dissociate non-manifold)\n   1 = Colocate       (merge identical vertices)\n   2 = RemoveDupFacets\n   4 = Triangulate    (force triangulation)\n   7 = Default = 1|2|4 |
| Colocate Eps (`Eps`) | Number | item | Tolerance for COLOCATE merge (0 = exact only). Match scene units. |
| Run (`Run`) | Boolean | item | Set true to compute. |

| out | type | access | description |
|---|---|---|---|
| Repaired (`M`) | Mesh | item | Repaired mesh. |
| Available (`Av`) | Boolean | item | True iff Geogram shim loaded. |
| Report (`R`) | Text | item | Diagnostic report. |

Related:
- Frahan > Mesh > Mesh Repair - Production mesh-repair component; this Geogram variant is the research probe.
- Frahan > Mesh > Mesh Diagnostics - Diagnose before repairing.

### Mesh Segmentation (CGAL SDF)  (`SegmentSdfCgal`)

- GUID: `F2D000A6-CADC-4F2D-A0A6-7E60CADA15A0`  |  icon: `CoacdDecompose.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/CgalTestComponents.cs`
- Algorithm: **SDF-based mesh segmentation** - CGAL Surface_mesh_segmentation (Shape Diameter Function graph-cut)
- Surface mesh segmentation via Shape Diameter Function.  Cuts at concave features (deep folds, narrow necks); the  tried-and-tested CGAL Surface_mesh_segmentation pipeline.  Returns one mesh per segment. NOT a Voronoi-style spatial  split: convex inputs collapse to one segment.  Wraps CGAL Surface_mesh_segmentation (SDF).

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Input surface (2-manifold gives best results). |
| Clusters (`K`) | Integer | item | Target number of segments (>= 2). CGAL's example uses 5. |
| Smoothing (`Lam`) | Number | item | Graph-cut smoothness penalty in [0, 1]. Higher = more  spatially coherent / fewer islands. CGAL default 0.26. |
| Cone Angle (`Cone`) | Number | item | SDF inward cone half-angle (radians). 0 = CGAL default  (2/3 * pi, ~120 degrees). |
| Rays (`Ry`) | Integer | item | Rays per facet for SDF estimation. 0 = CGAL default (25).  More rays = smoother SDF, slower compute. |
| Postprocess (`Pp`) | Boolean | item | Run CGAL's SDF postprocess (smoothing + connected-component  cleanup). Recommended. |
| Run (`Run`) | Boolean | item | Set true to compute. |

| out | type | access | description |
|---|---|---|---|
| Segments (`S`) | Mesh | list | One mesh per non-empty segment. |
| Segment Count (`N`) | Integer | item | Number of non-empty segments produced. |
| Available (`Av`) | Boolean | item | True iff CGAL shim loaded. |
| Report (`R`) | Text | item | Diagnostic report. |

Related:
- Frahan > Mesh > Mesh Quality Report - Production mesh analysis; SDF segmentation is a research probe.
- Frahan > Quarry > BlockCutOpt Solve - Segmented mesh regions can feed the BlockCutOpt sub-zone partition (I10).

### Mesh Segmentation by Angle (CGAL)  (`SegmentAngleCgal`)

- GUID: `F2D000A7-CADC-4F2D-A0A7-7E60CADA15A0`  |  icon: `CoacdDecompose.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/CgalTestComponents.cs`
- Algorithm: **Sharp-edge dihedral segmentation** - CGAL Polygon Mesh Processing (detect_sharp_edges + connected components)
- Cluster mesh faces by dihedral-angle change. Detects  sharp edges (where adjacent face normals deviate by more  than the threshold) and flood-fills the rest into smooth  regions. Returns one mesh per region.  Tuning: 5-15 deg = strict planarity, 30-60 deg = smooth- band detection, 90+ = only orthogonal-ish creases.  Wraps CGAL detect_sharp_edges.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Input surface (2-manifold gives stable dihedral computation). |
| Angle (`A`) | Number | item | Dihedral angle threshold in DEGREES, in (0, 180). Edges  whose dihedral angle exceeds this become segment boundaries.  Try 30-45 for smooth bands on curved forms. |
| Run (`Run`) | Boolean | item | Set true to compute. |

| out | type | access | description |
|---|---|---|---|
| Segments (`S`) | Mesh | list | One mesh per smoothly-connected region. |
| Segment Count (`N`) | Integer | item | Number of non-empty segments produced. |
| Available (`Av`) | Boolean | item | True iff CGAL shim loaded. |
| Report (`R`) | Text | item | Diagnostic report. |

Related:
- Frahan > Mesh > Mesh Quality Report - Production mesh analysis; angle-based segmentation is a research probe.
- Frahan > Masonry > Mesh Planar Polygon Extractor - Planar-face extraction shares the angle-clustering primitive.

### OBB (Auto)  (`ObbAuto`)

- GUID: `F2D000D2-A070-4F2D-A0D2-7E60A07000D2`  |  icon: `MeshBvh.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/AutoMeshComponents.cs`
- Oriented bounding box via the best available backend  (Geogram preferred - lighter, no Eigen). CGAL fallback  requires the shim to be built with Eigen.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Input mesh. |
| Run (`Run`) | Boolean | item | Set true to compute. |

| out | type | access | description |
|---|---|---|---|
| OBB (`B`) | Box | item | Oriented bounding box. |
| Plane (`P`) | Plane | item | Box origin frame. |
| Backend (`Bk`) | Text | item | Which backend ran. |
| Diagnostics (`D`) | Text | item | Loaded shim versions. |
| Report (`R`) | Text | item | Diagnostic report. |

Related:
- Frahan > Quarry > BlockCutOpt Solve - OBB feeds BlockCutOpt I2 BVH pruning; this auto-dispatcher selects CGAL or Geogram.
- Frahan > Mesh > Bench From Mesh - Quarry-bench mesh OBB analysis.

### OBB (Geogram)  (`ObbGeogram`)

- GUID: `F2D000C2-6E06-4F2D-A0C2-7E60660C0AC1`  |  icon: `MeshBvh.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/GeogramTestComponents.cs`
- Algorithm: **PCA oriented bounding box** - Frahan-original
- Oriented bounding box via PrincipalAxes3d (PCA).  BSD-3 parallel to OBB (CGAL); no Eigen dependency.  Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Input mesh (triangles ignored; uses vertex point cloud). |
| Run (`Run`) | Boolean | item | Set true to compute. |

| out | type | access | description |
|---|---|---|---|
| OBB (`B`) | Box | item | Oriented bounding box. |
| Plane (`P`) | Plane | item | Box origin frame. |
| Available (`Av`) | Boolean | item | True iff Geogram shim loaded. |
| Report (`R`) | Text | item | Diagnostic report. |

Related:
- Frahan > Quarry > BlockCutOpt Solve - OBB primitive is used inside the BlockCutOpt solver inner loop (I2 BVH pruning).
- Frahan > Mesh > Bench From Mesh - Quarry-bench mesh inputs benefit from OBB analysis.

### Polygon Partition (CGAL)  (`PartitionCgal`)

- GUID: `F2D000A2-CADC-4F2D-A0A2-7E60CADA15A0`  |  icon: `Voronoi.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/CgalTestComponents.cs`
- Algorithm: **Convex polygon partition** - CGAL Partition_2 (Hertel-Mehlhorn approximate + Greene optimal convex decomposition)
- Decompose a 2D simple polygon into convex sub-polygons or  y-monotone pieces via CGAL Partition_2.  Wraps CGAL Partition_2 (Hertel-Mehlhorn and Greene).

| in | type | access | description |
|---|---|---|---|
| Polygon (`P`) | Curve | item | Closed planar simple polygon (no holes). |
| Kind (`K`) | Integer | item | 0 = approximate convex (Hertel-Mehlhorn, fast).  1 = optimal convex (Greene, O(n^4) but minimal pieces).  2 = y-monotone partition. |
| Tolerance (`T`) | Number | item | Curve-to-polyline tolerance. |
| Run (`Run`) | Boolean | item | Set true to compute. |

| out | type | access | description |
|---|---|---|---|
| Pieces (`C`) | Curve | list | Sub-polygons as closed polylines. |
| Piece Count (`N`) | Integer | item | Number of pieces. |
| Available (`Av`) | Boolean | item | True iff the CGAL native shim is loadable. |
| Report (`R`) | Text | item | Diagnostic report. |

Related:
- Frahan > Masonry > Slab Cut By Fracture Polygons - Production polygon-based slab cutting; uses partitioned polygons as input.
- Frahan > Masonry > Polygon Sanitize - Pre-clean polygons before partitioning.

### Straight Skeleton (CGAL)  (`SkeletonCgal`)

- GUID: `F2D000A1-CADC-4F2D-A0A1-7E60CADA15A0`  |  icon: `PolygonSimplify.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/CgalTestComponents.cs`
- Algorithm: **Straight skeleton** - CGAL Straight_skeleton_2 (Aichholzer-Aurenhammer straight skeleton)
- Interior straight skeleton of a 2D polygon (with optional  holes) via CGAL Straight_skeleton_2. Outer ring CCW, holes  CW; the shim auto-reverses if winding is wrong.  Wraps CGAL Straight_skeleton_2.

| in | type | access | description |
|---|---|---|---|
| Outer (`O`) | Curve | item | Closed planar polyline / curve. The outer ring of the polygon. |
| Holes (`H`) | Curve | list | Optional closed planar curves treated as holes. |
| Tolerance (`T`) | Number | item | Curve-to-polyline tolerance. |
| Run (`Run`) | Boolean | item | Set true to compute. |

| out | type | access | description |
|---|---|---|---|
| Edges (`E`) | Line | list | Skeleton edges as 2D lines (Z = 0). |
| Vertices (`V`) | Point | list | Skeleton + boundary vertex positions. |
| Times (`Time`) | Number | list | Time-of-arrival per vertex (boundary = 0; interior > 0). |
| Available (`Av`) | Boolean | item | True iff the CGAL native shim is loadable. |
| Report (`R`) | Text | item | Diagnostic report. |

Related:
- Frahan > Masonry > Slab Cut By Fractures - Skeleton-based cutting paths feed into the production fracture-cutting pipeline.
- Frahan > Masonry > Fracture Polygon From Curve - Polygon ingest sibling for fracture inputs.

### Tetrahedralize (Geogram)  (`TetGeogram`)

- GUID: `F2D000C4-6E06-4F2D-A0C4-7E60660C0AC1`  |  icon: `CoacdDecompose.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/GeogramTestComponents.cs`
- Algorithm: **Constrained Delaunay tetrahedralisation** - Levy, B., INRIA/ALICE. Geogram v1.9.9. BSD-3. https://github.com/BrunoLevy/geogram
- Volumetric tetrahedral mesh of a closed surface via  GEO::mesh_tetrahedralize. NOTE: requires the shim to be  built with GEOGRAM_WITH_TETGEN=ON. Default build has it  OFF for BSD-3 license cleanliness; in that mode this  component returns a clear error pointing at the rebuild.  Wraps Geogram mesh_tetrahedralize (TetGen).

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Closed input surface. |
| Preprocess (`Pre`) | Boolean | item | Clean input first. |
| Refine (`Re`) | Boolean | item | Insert Steiner points to improve quality. |
| Quality (`Q`) | Number | item | Element quality target [1.0..2.0]; 1.0 = max. |
| Keep Regions (`Kr`) | Boolean | item | Keep all internal regions (else outermost only). |
| Run (`Run`) | Boolean | item | Set true to compute. |

| out | type | access | description |
|---|---|---|---|
| Tet Cells (`T`) | Mesh | list | One mesh per tet (4 boundary triangles each). |
| Tet Count (`N`) | Integer | item | Number of tetrahedra. |
| Available (`Av`) | Boolean | item | True iff Geogram shim loaded. |
| Report (`R`) | Text | item | Diagnostic report. |

Related:
- Frahan > Mesh > Mesh Diagnostics - Tet-mesh quality reports feed mesh-diagnostics workflows.
- Frahan > Masonry > Masonry Stability RBE - Tetrahedralisation can support FE-volume stability analysis downstream of the RBE solver.

### Voronoi Block Partition (Geogram)  (`RvdGeogram`)

- GUID: `F2D000C6-6E06-4F2D-A0C6-7E60660C0AC1`  |  icon: `Voronoi.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/GeogramTestComponents.cs`
- Algorithm: **Restricted Voronoi diagram partition** - Levy, B., INRIA/ALICE. Geogram v1.9.9. BSD-3. https://github.com/BrunoLevy/geogram
- Partition a surface mesh into N Voronoi cells given seed  points (use CVT Seeds upstream for uniform-area cells).  Output is one Mesh per cell. Quarry-pipeline use:  Statue → Decimate → Repair → Remesh → CVT → this  → BlockGraph → GeoCut → QuarryCutOpt.  Wraps Geogram restricted Voronoi diagram.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Input surface. |
| Seeds (`S`) | Point | list | Seed points (use CVT Seeds for optimized seeds). |
| Run (`Run`) | Boolean | item | Set true to compute. |
| Smooth Pts (`SP`) | Integer | item | Pre-RVD uniform remesh target vertex count (0 = off,  5_000..50_000 typical). Smooths cell-boundary sawtooth. |
| Closed (`Cl`) | Boolean | item | If true, return CLOSED Voronoi blocks (volumetric mode,  input must be a closed solid). If false, surface partition. |

| out | type | access | description |
|---|---|---|---|
| Cells (`C`) | Mesh | list | One mesh per Voronoi cell. |
| Cell Count (`N`) | Integer | item | Number of non-empty cells produced. |
| Available (`Av`) | Boolean | item | True iff Geogram shim loaded. |
| Report (`R`) | Text | item | Diagnostic report. |

Related:
- Frahan > Quarry > Quarry DFN - Production DFN partitioner; this Geogram Voronoi is the research-grade alternative.
- Frahan > Quarry > Joint Set - Joint-set fracture statistics feed Voronoi partitioning.


## Masonry

### Ashlar Pack  (`AshlarPack`)

- GUID: `F1A2B3C4-D5E6-4789-9ABC-DEF012345678`  |  icon: `CourseGenerator.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/AshlarPackComponent.cs`
- Algorithm: **Ashlar coursed wall layout** - Frahan-original 3D grid stacking with running bond
- Lays convex Slabs into a coursed-ashlar wall (running bond).  Emits a MasonryAssembly with bottom-row blocks fixed and an  AshlarPackResult carrying coverage / leftovers / notes.

| in | type | access | description |
|---|---|---|---|
| Wall Width (`W`) | Number | item | Wall length along +X (units of the active Rhino document,  typically meters). Must be > 0. Recommended: wire a Wall  Frame instead and leave this at default. |
| Wall Height (`H`) | Number | item | Wall height along +Z. Must be > 0. Default 1.0. |
| Wall Thickness (`T`) | Number | item | Wall thickness along +Y. Must be > 0. Default 0.20 (typical  single-leaf masonry). |
| Meshes (`M`) | Mesh | list | Block inventory as Rhino meshes (e.g. from Quarry DFN, Slab  Cut By Fractures, or hand-authored). Each mesh must be convex  with at least 4 vertices and 4 faces. The packer auto-converts  to its internal Slab DTO. |
| Course Mode (`M`) | Integer | item | Layout strategy. 0 = CoursedAshlar (uniform course height;  one block size). 1 = CoursedRubble (multi-bin: each course  uses one block-height bin; mixes block sizes across courses). |
| Target Course Height (`Ch`) | Number | item | Course height for CoursedAshlar. For CoursedRubble it seeds  the first bin height. Should match the Z-extent of your  blocks within Height Tolerance. Default 0.15. |
| Bed Joint (`Bj`) | Number | item | Vertical mortar gap between courses (units of the document).  >= 0. Default 0.001 (1 mm typical lime-mortar gap). |
| Head Joint (`Hj`) | Number | item | Horizontal mortar gap between adjacent blocks in a course.  >= 0. Default 0.001 (1 mm). |
| Stagger Offset (`So`) | Number | item | Running-bond shift on odd courses, as a fraction of the  average block width. In [0, 1]. Default 0.5 (half-bond). |
| Density (`D`) | Number | item | Material density in kg/m³ (or any consistent mass-per-volume  unit). > 0. Default 2400 (typical limestone). Used by the  downstream stability solver to compute self-weight. |
| Height Tolerance (`Tol`) | Number | item | Block-height tolerance for inventory filtering. >= 0. Default  0.05 (5 cm — accommodates rough-cut quarry blocks). |
| Wall Frame (`Wf`) | Generic | item | Optional WallFrame DTO from Wall Frame component. When wired,  this overrides the primitive Wall Width / Height / Thickness  inputs above. Recommended for clean canvas wiring. |
| Options (`Op`) | Generic | item | Optional AshlarPackOptions DTO from Ashlar Pack Options  component. When wired, overrides Course Mode / Course Height  / joints / stagger / density / tolerance. Recommended for  clean canvas wiring. |
| Start Plane (`Sp`) | Plane | item | Optional start plane. Engine stays in world XY (correct for  the stability solver, which assumes gravity = -Z). The  component emits a Display Transform that maps world XY into  this plane; wire it into AssemblyPreview / mesh Transform  to re-orient the wall visually. |

| out | type | access | description |
|---|---|---|---|
| Assembly (`A`) | Generic | item | MasonryAssembly with bottom-course blocks fixed. Wire into  Masonry Stability (RBE). |
| Result (`R`) | Generic | item | AshlarPackResult carrying coverage / leftovers / notes / placed  blocks. Wire into Pack Diagnostics (Stage 3). |
| Display Transform (`T`) | Transform | item | World-XY → Start Plane transform. Identity when Start Plane  is not wired. Wire into mesh / preview transforms. |

### Ashlar Pack Options  (`AshOpts`)

- GUID: `B3C4D5E6-F7A8-49AB-CDEF-012345678901`  |  icon: `BondPattern.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/AshlarPackOptionsComponent.cs`
- Bundles ashlar packer knobs (course mode, joints, stagger,  density, height tolerance) into an AshlarPackOptions DTO.  Wire into Ashlar Pack's optional Options input.

| in | type | access | description |
|---|---|---|---|
| Course Mode (`M`) | Integer | item | Layout strategy. 0 = CoursedAshlar (single block-height bin,  uniform courses). 1 = CoursedRubble (multi-bin, mixes block  sizes across courses). Default 0. |
| Course Height (`Ch`) | Number | item | Target course height in document units (typically meters).  Must match the Z-extent of your blocks within Height Tolerance.  Default 0.15. |
| Bed Joint (`Bj`) | Number | item | Vertical mortar gap between courses, in document units. >= 0.  Default 0.001 (1 mm typical). |
| Head Joint (`Hj`) | Number | item | Horizontal mortar gap between adjacent blocks, in document  units. >= 0. Default 0.001. |
| Stagger Offset (`So`) | Number | item | Running-bond shift on odd courses, as a fraction of the  average block width. In [0, 1]. Default 0.5 (half-bond,  the standard). |
| Density (`D`) | Number | item | Material density in kg/m³ (or consistent mass-per-volume  unit). > 0. Default 2400 (typical limestone). Used by the  downstream stability solver. |
| Height Tolerance (`Tol`) | Number | item | Block-height tolerance for inventory filtering and rubble  binning, in document units. >= 0. Default 0.05 (5 cm —  accommodates rough-cut quarry blocks). |

| out | type | access | description |
|---|---|---|---|
| Options (`O`) | Generic | item | AshlarPackOptions DTO bundling all algorithmic knobs. Wire  into Ashlar Pack's Options input — when wired, it overrides  the equivalent primitive inputs on the packer. |

### Assembly Preview  (`AsmPrev`)

- GUID: `12345678-9ABC-DEF0-1234-56789ABCDEF0`  |  icon: `AssemblyState.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/AssemblyPreviewComponent.cs`
- Visualizes a MasonryAssembly. Outputs one Mesh per block  (separated into free + fixed lists), the IDs of each, and  per-interface contact polylines + centroids + normals for  debugging.

| in | type | access | description |
|---|---|---|---|
| Assembly (`A`) | Generic | item | MasonryAssembly DTO (from Masonry Assembly or Ashlar Pack). |

| out | type | access | description |
|---|---|---|---|
| Free Meshes (`Mf`) | Mesh | list | Block meshes for blocks that are NOT fixed by boundary conditions. |
| Fixed Meshes (`Mfix`) | Mesh | list | Block meshes for blocks that ARE fixed (grounded). |
| Free Block Ids (`If`) | Text | list | IDs of the free blocks (parallel to Free Meshes). |
| Fixed Block Ids (`Ifix`) | Text | list | IDs of the fixed blocks (parallel to Fixed Meshes). |
| Contact Polylines (`C`) | Curve | list | One closed polyline per MasonryInterface — the contact  polygon as drawn on the canvas. |
| Contact Centroids (`Cc`) | Point | list | Centroid of each contact polygon. Use to anchor labels  or to draw normals. |
| Contact Normals (`Cn`) | Vector | list | Surface normal at each contact, pointing from block A to  block B (Frahan convention). |
| Contact Pairs (`Cp`) | Text | list | Per-interface 'aId -> bId' string for hover-debugging. |

### Auto Interfaces  (`AutoIf`)

- GUID: `CADBECFD-AEBF-4012-3456-789012345678`  |  icon: `ContactDetector.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/AutoInterfacesComponent.cs`
- Algorithm: **Interface auto-detector** - Frahan-original proximity-based pairwise contact detection
- Detects face-face contacts between a list of placed Slabs  and emits the corresponding MasonryInterfaces. Wire output  into Masonry Assembly's Interfaces input. NOTE: generated  walls carry exact joints via the Assembly output — detection  is only needed for imported geometry; for scan-derived meshes  use Robust Auto Interfaces.

| in | type | access | description |
|---|---|---|---|
| Meshes (`M`) | Mesh | list | Block meshes in their final placed positions. Standard Rhino  mesh wires; this component finds face-face contacts between them. |
| Block Ids (`Ids`) | Text | list | One ID per slab in the same order. Must match the IDs used  to construct MasonryBlocks. |
| Distance Tolerance (`Dtol`) | Number | item | Max distance between coplanar faces (>= 0). |
| Angle Tolerance Deg (`Atol`) | Number | item | Max angle between antiparallel face normals, in degrees (>= 0, < 90). |

| out | type | access | description |
|---|---|---|---|
| Interfaces (`I`) | Generic | list | Detected MasonryInterfaces. Wire into Masonry Assembly. |

Related:
- Frahan > Masonry > Robust Auto Interfaces - Vertex-proximity detection for scan-derived meshes (slight gaps, non-planar contacts); use it when this polygon-based detector misses contacts.

### Best Fit Pack  (`BestFit`)

- GUID: `01234567-89AB-CDEF-0123-456789ABCDEF`  |  icon: `CourseGenerator.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/BestFitPackComponent.cs`
- Algorithm: **Best-fit rubble inventory placement** - Gramazio Kohler Eichenhofer 2017 NCCR Digital Fabrication
- Inventory-aware ashlar packing. For each placement slot,  scores every remaining stone by width / depth / height /  aspect-ratio fit and picks the highest-scoring candidate.  Companion to Ashlar Pack (which uses first-fit). Recommended  for heterogeneous quarry inventories where stone sizes vary. [Gramazio et al. 2017]

| in | type | access | description |
|---|---|---|---|
| Wall Width (`W`) | Number | item | Wall length along +X. Must be > 0. Recommended: wire a  Wall Frame instead. |
| Wall Height (`H`) | Number | item | Wall height along +Z. Must be > 0. |
| Wall Thickness (`T`) | Number | item | Wall thickness along +Y. Must be > 0. |
| Inventory (`I`) | Mesh | list | Block inventory as Rhino meshes. Each mesh becomes a  candidate stone for best-fit selection. |
| Course Height (`Ch`) | Number | item | Target course height. Must match the Z-extent of your  blocks within Height Tolerance. Default 0.15. |
| Bed Joint (`Bj`) | Number | item | Vertical mortar gap between courses. Default 0.001. |
| Head Joint (`Hj`) | Number | item | Horizontal mortar gap between blocks. Default 0.001. |
| Stagger Offset (`So`) | Number | item | Running-bond shift on odd courses, fraction of average  block width. Default 0.5. |
| Density (`D`) | Number | item | Material density (kg/m³). Default 2400. |
| Height Tolerance (`Tol`) | Number | item | Block-height tolerance for inventory filtering. Default 0.05. |
| Wall Frame (`Wf`) | Generic | item | Optional WallFrame DTO. Overrides primitive Wall W/H/T. |
| Options (`Op`) | Generic | item | Optional AshlarPackOptions DTO. Overrides primitive  algorithmic inputs. |
| Start Plane (`Sp`) | Plane | item | Optional start plane. Engine stays in world XY (correct for  the stability solver). Component emits a Display Transform  that maps world XY into this plane for visual re-orientation. |

| out | type | access | description |
|---|---|---|---|
| Assembly (`A`) | Generic | item | MasonryAssembly with bottom-course blocks fixed. |
| Result (`R`) | Generic | item | AshlarPackResult — coverage / leftovers / notes / placed blocks. |
| Display Transform (`T`) | Transform | item | World-XY → Start Plane transform. Identity when unset. |

### Block Build Order  (`BuildOrd`)

- GUID: `3456789A-BCDE-F012-3456-789ABCDEF012`  |  icon: `AssemblySolver.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/BlockBuildOrderComponent.cs`
- Algorithm: **Support-DAG topological install order** - Kim et al. 2024, ASME IDETC/CIE DETC2024-142563 Polygonal masonry install-order DAG
- Computes a physically valid build order for a masonry  assembly. A block is placed only after every block it  rests on is already placed. Layer = course number  (longest support path from ground). [Kim et al. 2024]

| in | type | access | description |
|---|---|---|---|
| Assembly (`A`) | Generic | item | MasonryAssembly DTO. |
| Up Vector (`Up`) | Vector | item | Direction the courses stack along. Default world Z. |
| Up Tolerance Deg (`Tol`) | Number | item | An interface counts as a bed joint when its normal is  within this many degrees of the up axis. Head joints /  vertical contacts beyond this tolerance contribute no  support constraint. Default 30°. |

| out | type | access | description |
|---|---|---|---|
| Ordered Block Ids (`Id`) | Text | list | Block ids in build order (lowest course first). |
| Ordered Meshes (`M`) | Mesh | list | Block meshes in build order, parallel to Ordered Block Ids. |
| Order Index (`i`) | Integer | list | 0-based placement index per block, parallel to Ordered  Block Ids. Equals the list index — exposed for downstream  components that rebuild the order. |
| Layer (`L`) | Integer | list | Course number (longest support path). 0 = ground course.  Useful for colour-by-course visualisation. |

### Block Graph Coloring  (`BlockColor`)

- GUID: `F2D000B0-CADC-4F2D-A0B0-7E60CADA15A0`  |  icon: `BeamConfig.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/BlockGraphColoringComponent.cs`
- Algorithm: **Welsh-Powell graph coloring** - Welsh and Powell 1967, Computer Journal 10(1):85-86
- 4-colours the contact graph of a MasonryAssembly: no two  blocks sharing an interface get the same colour. Output  is one integer per block (0-3 typically; up to 7 for  non-planar topologies). Wire into native colour-mapping  to drive visualization or material assignment. [Welsh & Powell 1967]

| in | type | access | description |
|---|---|---|---|
| Assembly (`A`) | Generic | item | MasonryAssembly with blocks + interfaces. |

| out | type | access | description |
|---|---|---|---|
| Block Ids (`Id`) | Text | list | Block identifiers, in iteration order. |
| Colour (`C`) | Integer | list | Per-block colour index (0-based). Same length and order as  the Block Ids output. |
| Colours Used (`N`) | Integer | item | Total number of distinct colours used. Should be <= 4 for  planar contact graphs (4-Colour Theorem). |

### Block Ground Transforms  (`BlkXform`)

- GUID: `23456789-ABCD-EF01-2345-6789ABCDEF01`  |  icon: `RigidTransform.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/BlockGroundTransformsComponent.cs`
- Algorithm: **Closed-form absolute orientation (Horn QAO)** - Horn 1987, Closed-form solution of absolute orientation using unit quaternions, JOSA A 4(4):629-642
- Recovers the rigid transform per placed block. Wire  Source Meshes (canonical) and Placed Meshes (post-  assembly) for vertex-paired Horn QAO recovery, OR wire  Existing Transforms for direct pass-through. Output  transforms are expressed in the Ground Plane's frame  (default: world XY). Implements Horn QAO (Horn 1987).

| in | type | access | description |
|---|---|---|---|
| Placed Meshes (`P`) | Mesh | list | Meshes after assembly (one per block). The recovered  transform takes the matching Source Mesh to this pose. |
| Source Meshes (`S`) | Mesh | list | Canonical / pre-placement meshes (one per block, parallel  to Placed). Required when Existing Transforms is empty.  Vertex count and order must match the placed mesh. |
| Existing Transforms (`Tx`) | Transform | list | Optional pre-known transforms (parallel to Placed). When  supplied at index i, this transform is passed through  verbatim and Horn QAO is skipped for that block. |
| Ground Plane (`G`) | Plane | item | Reference frame. Output transforms are re-expressed in  this plane's local basis. Default: world XY. |

| out | type | access | description |
|---|---|---|---|
| Transforms (`T`) | Transform | list | Per-block rigid transform (canonical → placed) in the  ground-plane frame. |
| Origins (`O`) | Point | list | Where each block's local origin lands. Useful for  anchoring labels on the canvas. |
| RMS (`E`) | Number | list | Per-block Horn QAO residual (root-mean-square of  vertex-pair distances after fit). 0.0 for pass-through. |
| Status (`St`) | Text | list | 'passthrough' / 'kabsch (V=…)' / 'failed: …' per block. |

### Block Pack (Tree)  (`BlockPackTree`)

- GUID: `C2D3E4F5-3001-4F5E-A6B7-C8D9E0F12345`  |  icon: `TreePack.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Packing/BlockPackTreeComponent.cs`
- Algorithm: **Tree-forest guillotine pack** - Kim 2025 Computation 13:211
- Pack sculpture/element cuboids into stone-block containers  with axis-aligned guillotine cuts. Frahan port of Kim 2025  (Computation 13:211, CC BY 4.0). Picks the cheapest subset  of containers that fits all elements; falls back to highest  packed-value when full packing is infeasible. Three  extensions beyond the paper: deterministic seed, saw kerf  width, and Forbidden Boxes per container.

| in | type | access | description |
|---|---|---|---|
| Elements (`E`) | Box | list | Element AABBs (sculpture / final-piece bounding boxes).  Only the box dimensions are used for the fit test; the  Box.Plane defines the element's source pose. |
| Element Values (`Pe`) | Number | list | Per-element value (e.g. piece price). Must match the  element count. |
| Containers (`C`) | Box | list | Container AABBs (stone-block bounding boxes). |
| Container Prices (`Pc`) | Number | list | Per-container price (e.g. stone-block material cost).  Must match the container count. |
| Rotation Mode (`Rot`) | Integer | item | 0 = None (identity only), 1 = OneAxis (vein-aligned),  2 = ThreeAxis (six 90° rotations). |
| Forests (`F`) | Integer | item | Number of independent randomised forests to grow. Score  plateaus by f ≈ 50–1000 on small instances; large jobs  may need 10⁴–10⁶ forests (see paper §4). |
| Seed (`S`) | Integer | item | Master seed (Frahan extension beyond Kim 2025). Forest k  uses (seed + k) internally; setting the same seed gives  the same result. Default 0 is deterministic. |
| Kerf Width (`K`) | Number | item | Saw kerf width in model units (Frahan extension). Each  axis-aligned cut consumes this much material along its  direction. Real values: 5–10 mm for diamond wire saws,  1–3 mm for thin blades. Default 0. |
| Forbidden Boxes (`X`) | Box | list | Optional flat list of forbidden Box regions inside any  container (Frahan extension; closes Kim §8.2 gap on  fracture-aware containers). Elements that overlap a  forbidden region in their target container are rejected.  A forbidden box outside all containers has no effect. |
| Cut Surface Weight (`Cw`) | Number | item | K2 / Jalalian I11 (BCSdbBV) extension. Score subtracts  weight × Σ(internal-face area) across placements. Default 0  preserves the original Kim 2025 score. |
| Max Parallelism (`Mp`) | Integer | item | K2 parallel-forest extension. 0 = auto (Environment. ProcessorCount). 1 forces serial. Parallel results are  bitwise identical to serial because each forest's RNG is  seeded independently. |
| Memory Budget MB (`Mb`) | Number | item | K2 memory-cap extension. When > 0, Forests is  automatically reduced so f × ~1.4 KB × element-count ≤ budget.  0 = unlimited. |

| out | type | access | description |
|---|---|---|---|
| Placed Boxes (`Pb`) | Box | list | Placed element AABBs in world-frame coordinates. |
| Transforms (`Xf`) | Transform | list | World-frame transform per placed element (apply to the  source element Box to recover the placed pose, including  any rotation). |
| Placed Element Ids (`Ei`) | Integer | list | Index into the input element list for each placement, in  placement order. Compare against the input element count  to find unpacked elements. |
| Placed Container Ids (`Ci`) | Integer | list | Index into the input container list for each placement. |
| Used Containers (`Uc`) | Integer | list | Sorted unique indices of containers that hold at least one  placed element. |
| Score (`Sc`) | Number | item | Score of the winning forest (Kim 2025 §2.4): sum of packed  element values, plus 1/(1+containerPrice) bonus when all  elements fit. |
| All Packed (`All`) | Boolean | item | True iff every input element landed in a container. |
| Best Forest (`Bf`) | Integer | item | Index of the winning forest (0 ≤ index < Forests). |
| Report (`R`) | Text | item | Human-readable summary. |

### Block Size Distribution  (`BlkSize`)

- GUID: `EF012345-6789-ABCD-EF01-23456789ABCD`  |  icon: `YieldEstimator.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/BlockSizeDistributionComponent.cs`
- Algorithm: **Descriptive statistics + Tukey-fence outlier rule** - Tukey 1977, Exploratory Data Analysis, Addison-Wesley
- Diagnostic stats over a list of slab volumes. Use to QA  quarry decomposition: high CV (> 1) signals the joint-set  parameters need retuning. Outlier fence per Tukey 1977 (EDA).

| in | type | access | description |
|---|---|---|---|
| Slabs (`S`) | Generic | list | Slab DTOs. |
| Bins (`B`) | Integer | item | Histogram bin count. 0 = ceil(sqrt(N)). Default 0. |

| out | type | access | description |
|---|---|---|---|
| Count (`N`) | Integer | item | Total piece count. |
| Total Volume (`V`) | Number | item | Sum of all volumes. |
| Min (`Min`) | Number | item |  |
| Max (`Max`) | Number | item |  |
| Mean (`Mean`) | Number | item |  |
| Median (`P50`) | Number | item |  |
| StdDev (`SD`) | Number | item |  |
| CV (`CV`) | Number | item | Coefficient of variation (StdDev/Mean). |
| Percentiles (`P`) | Number | list | [P10, P25, P50, P75, P90]. |
| Outlier Indices (`Out`) | Integer | list | Indices outside the Tukey fence (Q1−1.5·IQR, Q3+1.5·IQR). |
| Bin Counts (`Hist`) | Integer | list | Histogram counts (length = Bins). |
| Bin Width (`Bw`) | Number | item | Width of each histogram bin. |
| Report (`R`) | Text | item | One-line summary. |

### Build Sequence JSON  (`BuildJson`)

- GUID: `6789ABCD-EF01-2345-6789-ABCDEF012345`  |  icon: `GcodeExport.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/BuildSequenceJsonComponent.cs`
- Encodes the masonry build sequence as a JSON string.  Wire Block Ids, Place Planes (or Place Transforms via  Pick Place Frames), and Layers in matching list order.  Output is plain text — pipe to a file-writing component  if you need disk persistence.

| in | type | access | description |
|---|---|---|---|
| Block Ids (`Id`) | Text | list | Per-block identifier. Required. |
| Place Planes (`Pl`) | Plane | list | Per-block placement plane (parallel to Block Ids).  Required. |
| Layers (`L`) | Integer | list | Per-block course number. Optional; if absent, all  layers are reported as 0. |
| Pretty (`P`) | Boolean | item | Indent the JSON for human reading. Default true. Set  false for compact single-line output. |

| out | type | access | description |
|---|---|---|---|
| Json (`J`) | Text | item | JSON text encoding the build sequence (schema 1.0). |

### Build Step Preview  (`BuildStep`)

- GUID: `56789ABC-DEF0-1234-5678-9ABCDEF01234`  |  icon: `AssemblyState.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/BuildStepPreviewComponent.cs`
- Slider-driven animation of a masonry build sequence. Wire  the ordered meshes from Block Build Order and a Step  integer slider. Returns Built (0..step), Pending  (step..N), and Current (mesh at step).

| in | type | access | description |
|---|---|---|---|
| Ordered Meshes (`M`) | Mesh | list | Block meshes in build order. Pipe in the Ordered Meshes  output from Block Build Order. |
| Step (`S`) | Integer | item | Current step. 0 = nothing built yet; N = everything  built. Values outside [0, N] are clamped. |

| out | type | access | description |
|---|---|---|---|
| Built (`Mb`) | Mesh | list | Meshes placed at or before Step (indices 0..step-1). |
| Pending (`Mp`) | Mesh | list | Meshes still to place (indices step..N-1). |
| Current (`Mc`) | Mesh | item | The mesh placed at this step (index step-1). Empty when  Step == 0 (nothing built yet). |
| Total (`N`) | Integer | item | Total mesh count, for sizing the slider. |

### Build-Order Stability Stream  (`StabStream`)

- GUID: `F2D000B3-CADC-4F2D-A0B3-7E60CADA15A0`  |  icon: `EquilibriumRBE.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/BuildOrderStabilityStreamComponent.cs`
- Walks a masonry build order and runs the RBE convex-QP  stability check on each partial assembly. Reports the  first step at which the in-progress wall becomes unstable.

| in | type | access | description |
|---|---|---|---|
| Assembly (`A`) | Generic | item | Full MasonryAssembly DTO. |
| Ordered Block Ids (`Id`) | Text | list | Block ids in build order. Output of Block Build Order. |
| Mu (`Mu`) | Number | item | Coulomb friction coefficient. Default 0.84 (~40°). |
| Faces (`F`) | Integer | item | Pyramidal friction-cone face count. Default 4. |
| Penalty (`P`) | Boolean | item | Penalty form (split f_n+/f_n-). Default false. |
| Stop On First Infeasible (`Stop`) | Boolean | item | Stop streaming as soon as one step is infeasible. Default true. |
| Max Steps (`Max`) | Integer | item | Cap on steps to evaluate. -1 = no cap (default). |
| Run (`R`) | Boolean | item | Set true to run. |

| out | type | access | description |
|---|---|---|---|
| First Unstable Step (`Step*`) | Integer | item | 0-based index of the first infeasible step. -1 if every  evaluated step was stable. |
| First Unstable Block Id (`Id*`) | Text | item | Block id placed at the first unstable step. Empty when  all evaluated steps are stable. |
| Verdict Per Step (`V`) | Text | list | Per-step verdict (parallel to Ordered Block Ids up to the  evaluated count). |
| Objective Per Step (`Obj`) | Number | list | Per-step QP objective value. |
| Report (`R`) | Text | item | Multi-line summary log. |

### Cut Validation  (`CutVal`)

- GUID: `DEF01234-5678-9ABC-DEF0-123456789ABC`  |  icon: `StereotomyJoint.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/CutValidationComponent.cs`
- Validates a cut: sum(post-piece volumes) ≈ sum(pre-slab  volumes) within tolerance. Flags slivers and dropouts.

| in | type | access | description |
|---|---|---|---|
| Pre Slabs (`Pre`) | Generic | list | Slab DTOs before the cut. |
| Post Slabs (`Post`) | Generic | list | Slab DTOs after the cut. |
| Relative Tolerance (`Tr`) | Number | item | Acceptable relative volume mismatch. Default 1e-6. |
| Sliver Fraction (`Ts`) | Number | item | Pieces whose volume is below this fraction of the total  are flagged as slivers. Default 1e-4. |
| Dropout Volume (`Td`) | Number | item | Pieces below this absolute volume are flagged as  dropouts (likely lost geometry). Default 1e-12. |
| Drop Slivers (`DropS`) | Boolean | item | If true, also output a sliver-free subset of post slabs.  Default false. |

| out | type | access | description |
|---|---|---|---|
| Conserved (`OK`) | Boolean | item | True iff |postVol − preVol| / preVol ≤ Relative Tolerance. |
| Pre Volume (`Vpre`) | Number | item | Sum |signed volume| of pre slabs. |
| Post Volume (`Vpost`) | Number | item | Sum |signed volume| of post slabs. |
| Absolute Error (`AbsErr`) | Number | item | |Vpre − Vpost|. |
| Relative Error (`RelErr`) | Number | item | AbsErr / Vpre. |
| Sliver Indices (`Sli`) | Integer | list | 0-based indices of post slabs flagged as slivers. |
| Dropout Indices (`Dro`) | Integer | list | 0-based indices of post slabs with effectively zero volume. |
| Cleaned Slabs (`Clean`) | Generic | list | Sliver-free subset of post slabs. Empty when Drop  Slivers is false. |
| Report (`R`) | Text | item | One-line summary. |

### Dry-Stone Wall (NBO)  (`NBOWall`)

- GUID: `D5F10030-0BA0-4ED9-A030-0BA00BA00030`  |  icon: `DryStoneWallNbo.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Nbo/DryStoneWallNboComponent.cs`
- Fill a dry-stone wall from a stone inventory with the Next-Best-Object planner  (hybrid orient -> drop-to-contact -> analytic stability gate -> lowest-cost pick).  Outputs the ordered, gated placement sequence. Optional target envelope, physical Seat  validation (settle each placement onto the fixed wall), and Bullet settle / CRA confirmation.  Ref: Furrer 2017 / Johns 2020.

| in | type | access | description |
|---|---|---|---|
| Inventory (`I`) | Mesh | list | Stone meshes to draw from. |
| Wall Length (`L`) | Number | item | Wall length to fill along +X (m). Overridden by Envelope if supplied. |
| Target Height (`H`) | Number | item | Fill until the wall top reaches this height (m). Overridden by Envelope. |
| Course Offset (`O`) | Number | item | Running-bond offset applied on alternating courses (m). |
| Gap (`G`) | Number | item | Minimum gap between stones along a course (m). |
| Envelope (`E`) | Brep | item | Optional closed target envelope (Brep): bounds the wall and rejects stones whose CoM falls outside it. |
| Spine (`Sp`) | Curve | item | Optional plan-rim spine curve: the wall follows it (front advances along arc length, long axis  into the wall along the local normal). A straight line reproduces the straight-X wall. |
| Confirm (`C`) | Boolean | item | Run a Bullet physics settle confirmation of the produced wall. |
| CRA (`Cra`) | Boolean | item | Run the compas-CRA rigid-block-equilibrium wall-gate (the strongest stability tier) on the produced wall. |
| Run (`R`) | Boolean | item | Execute the fill. |
| Seat (`Se`) | Boolean | item | Physically VALIDATE each placement: drop every candidate (in its top stable orientations) onto  the fixed as-built and keep only the one that beds firmly, committed at its settled pose. Builds  a wall that holds (fewer stones, no slips) -- the robot-ready mode. Needs the Bullet backend. |

| out | type | access | description |
|---|---|---|---|
| Placed (`Pl`) | Mesh | list | Placed stone meshes in placement order. |
| Transforms (`X`) | Transform | list | Per-stone placement transform (matches Placed order). |
| Course (`Cr`) | Integer | list | Per-stone course index. |
| Stable (`St`) | Boolean | list | Per-stone analytic stability verdict. |
| Cost (`Co`) | Number | list | Per-stone selection cost (lower is better). |
| Report (`Rp`) | Text | item | Solve summary. |

### Force-Seat (URScript)  (`FSeat`)

- GUID: `D5F10032-0BA0-4ED9-A032-0BA00BA00032`  |  icon: `ForceSeatUrScript.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Nbo/ForceSeatUrScriptComponent.cs`
- Emit UR URScript to place + force-seat a stone at each place TCP frame  (approach -> descend -> force_mode press -> retract). TEXT ONLY (code-gen, no  hardware send); validate in URSim. Force-seating is the irregular-stone enabler.

| in | type | access | description |
|---|---|---|---|
| Place Frames (`F`) | Plane | list | Seat TCP frames (from Next-Best-Object Pose -> Robot Frame). Frame Z is the press direction. |
| Robot Base (`B`) | Plane | item | Robot base frame in world coords; poses are emitted in it. |
| Seat Force (`Fz`) | Number | item | Downward press force to seat the stone (N). |
| Approach (`A`) | Number | item | Approach/retract clearance above the seat (m). |
| Descend Speed (`V`) | Number | item | Compliant descent speed during seating (m/s). |

| out | type | access | description |
|---|---|---|---|
| URScript (`U`) | Text | list | One place + force-seat URScript program per place frame (text; validate in URSim before any hardware). |

### Fragment Merger  (`FragMerge`)

- GUID: `F0123456-789A-BCDE-F012-3456789ABCDE`  |  icon: `KintsugiAssemble.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/FragmentMergerComponent.cs`
- Algorithm: **Greedy smallest-first agglomeration over a contact-adjacency graph** - Frahan-original
- Agglomerates small fragments into their largest adjacent  host using upstream contact adjacency. Returns a merge  mapping (HostOf per piece + per-host accumulated volume);  geometry is NOT remeshed at this stage. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Slabs (`S`) | Generic | list | Slab DTOs (the candidate pieces). |
| Adjacency I (`Ai`) | Integer | list | First index of each adjacency pair. |
| Adjacency J (`Aj`) | Integer | list | Second index of each adjacency pair (parallel to Ai). |
| Threshold Fraction (`Th`) | Number | item | Fragments below threshold·meanVolume are merged. Default  1e-3 (0.1% of mean). |

| out | type | access | description |
|---|---|---|---|
| Host Of (`H`) | Integer | list | Per-input-piece, the index it ultimately merged into.  Self if it's a host. |
| Merged Volume (`Vm`) | Number | list | Per-input-piece volume after merge (host accumulates;  non-host entries are 0). |
| Host Indices (`Hi`) | Integer | list | Indices of pieces that remained hosts. |
| Merged Count (`Mc`) | Integer | item | Number of fragments that got merged into a different host. |

### IFC Export (Building)  (`IfcBuilding`)

- GUID: `D5F10018-5C7B-4E2D-9A41-8B36F1D07C54`  |  icon: `IfcBuilding.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/IfcBuildingExportComponent.cs`
- Write SEVERAL stone containers (walls, arches, vaults, columns) into one IFC4 building.  Stones come as a tree: one branch per container; Names and Containers list-match the branches.  Each stone becomes an IfcBuildingElementPart (or voussoir IfcMember) with a tessellated body.

| in | type | access | description |
|---|---|---|---|
| Stones (`St`) | Mesh | tree | Stone meshes, ONE BRANCH PER CONTAINER |
| Names (`N`) | Text | list | Container names (one per branch) |
| Containers (`C`) | Integer | list | Per branch: 0 Wall | 1 Cladding | 2 Arch | 3 Vault | 4 Column |
| Path (`P`) | Text | item | Output .ifc path |
| Project (`Pr`) | Text | item | IfcProject name Frahan Stone Building |
| Run (`R`) | Boolean | item | Write the file |

| out | type | access | description |
|---|---|---|---|
| Path (`P`) | Text | item | Written file |
| Report (`R`) | Text | item | Export report |
| OK (`OK`) | Boolean | item | True if the file was written |

### IFC Export (Stone Assembly)  (`IfcStones`)

- GUID: `D5F10017-3E5A-4B9C-8D26-1F70A4C85E93`  |  icon: `IfcExport.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/IfcExportComponent.cs`
- Write the stone assembly as IFC4: container element (wall / cladding / arch / vault / column)  with one building-element part per stone (tessellated body + Frahan_Stone property set).  xBIM Essentials, SI metres.

| in | type | access | description |
|---|---|---|---|
| Stones (`St`) | Mesh | list | Closed stone meshes in their placed positions |
| Name (`N`) | Text | item | Container element name StoneWall |
| Path (`P`) | Text | item | Output .ifc path |
| Container (`C`) | Integer | item | 0 Wall | 1 Cladding | 2 Arch | 3 Vault | 4 Column |
| Carve (`Cr`) | Number | list | Per-stone carve ratio lambda (optional) |
| Stability (`Sm`) | Number | item | Stability margin for the assembly (optional) |
| InterlockJ (`J`) | Number | item | Interlock score J of the pattern (optional) |
| Run (`R`) | Boolean | item | Write the file |

| out | type | access | description |
|---|---|---|---|
| Path (`P`) | Text | item | Written file |
| Report (`R`) | Text | item | Export report |
| OK (`OK`) | Boolean | item | True if the file was written |

### Masonry Assembly  (`MasAsm`)

- GUID: `E5A9B2C3-3D4E-4F60-AB2C-4D5E6F7A8B9C`  |  icon: `Voussoir.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/MasonryAssemblyComponent.cs`
- Composes MasonryBlocks, MasonryInterfaces, and fixed-block boundary  conditions into a MasonryAssembly DTO. Interfaces must be supplied  explicitly; auto-detection is a future task.

| in | type | access | description |
|---|---|---|---|
| Blocks (`B`) | Generic | list | MasonryBlock DTOs from Masonry Block. |
| Interfaces (`I`) | Generic | list | MasonryInterface DTOs. May be empty; auto-detection is a future task. |
| Fixed Block Ids (`F`) | Text | list | Identifiers of blocks that are grounded (boundary conditions).  Empty list means all blocks are free. |

| out | type | access | description |
|---|---|---|---|
| Assembly (`A`) | Generic | item | MasonryAssembly DTO. Wire into Masonry Stability (RBE). |

### Masonry Block  (`MasBlk`)

- GUID: `D4F8A1B2-2C3D-4E5F-9A1B-3C4D5E6F7A8B`  |  icon: `MeshBvh.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/MasonryBlockComponent.cs`
- Wraps a Rhino mesh into a MasonryBlock DTO. Quads are triangulated;  the mesh must have at least 3 vertices and at least one face.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Rhino mesh defining the block geometry. Quads are auto-triangulated. |
| Id (`I`) | Text | item | Optional stable identifier. If blank, a fresh GUID is assigned. |
| Density (`D`) | Number | item | Material density (kg/m^3 or any consistent unit). Must be > 0. |

| out | type | access | description |
|---|---|---|---|
| Block (`B`) | Generic | item | MasonryBlock DTO. Wire into Masonry Assembly. |
| Id (`Id`) | Text | item | Block identifier (the value passed in, or the auto-generated  GUID if Id was blank). Wire into Auto Interfaces' Block Ids  input or Masonry Assembly's Fixed Block Ids input — keeps  block identity consistent across the canvas. |

### Masonry Stability (RBE)  (`MasRBE`)

- GUID: `F6BAC3D4-4E5F-4071-BC3D-5E6F7A8B9CAD`  |  icon: `EquilibriumRBE.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/MasonryStabilityRbeComponent.cs`
- Algorithm: **Rigid-Block Equilibrium QP** - Kao et al. 2022, Computer-Aided Design 146:103216 Coupled Rigid-Block Analysis
- Convex-QP rigid-block-equilibrium stability check for a MasonryAssembly.  NOTE: RBE is the permissive check; CRA (Kao 2022) rejects self-stressed  states RBE accepts (H-model). Certify via Masonry Stability Check + CRA.  Asynchronous: assembles the equilibrium + friction QP and solves on a  pool thread.

| in | type | access | description |
|---|---|---|---|
| Assembly (`A`) | Generic | item | MasonryAssembly from Masonry Assembly. |
| Mu (`Mu`) | Number | item | Coulomb friction coefficient (default 0.84, ~40 deg). |
| Faces (`F`) | Integer | item | Number of pyramidal faces used to linearise the friction cone  (>= 3, default 4). |
| Penalty (`P`) | Boolean | item | If true, use the penalty form (split normals f_n+/f_n-) for the  equilibrium matrix. |
| Run (`R`) | Boolean | item | Set true to execute the solve. |

| out | type | access | description |
|---|---|---|---|
| Verdict (`V`) | Text | item | Short verdict: 'stable', 'infeasible', 'no solver registered', or an  error class. |
| Objective (`O`) | Number | item | QP objective value at the returned x (NaN when not Optimal). |
| ResidualNorm (`R`) | Number | item | L2 norm of the equilibrium residual ||Aeq x - beq||. |
| SolverName (`S`) | Text | item | Identifier of the solver used (e.g. 'ManagedQpSolver'). |
| Report (`Rpt`) | Text | item | Human-readable diagnostic. |

Related:
- Frahan > Masonry > Masonry Stability Check - Certification path: CRA (Kao 2022) rejects self-stressed states that RBE accepts (H-model); certify via Masonry Stability Check + CRA.

### Masonry Stability Check  (`MasonStable`)

- GUID: `D5F10015-2B43-4E8A-A1C7-9D0F4B6E2A91`  |  icon: `StabilityCheck.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Masonry/MasonryStabilityCheckComponent.cs`
- Rigid-block equilibrium (RBE) stability check for a stone assembly: contacts are  auto-detected, the Kao 2021/2022 compression-only + Coulomb-friction QP is solved,  and the verdict + per-interface friction utilization are reported. Friction uses a  conservative INSCRIBED K-face pyramid (mu_eff = mu*cos(pi/K)).  Refs: Kao et al. 2021 (J Mech Des) / 2022 (CAD 146:103216, compas_cra).

| in | type | access | description |
|---|---|---|---|
| Stones (`St`) | Mesh | list | Closed stone meshes in their placed positions (optional when an Assembly is supplied) |
| Mu (`Mu`) | Number | item | Coulomb friction coefficient (0.84 ~ dry stone, 40 deg) |
| Faces (`K`) | Integer | item | Friction pyramid face count (>= 3; 8 recommended) |
| FixBelowZ (`Fz`) | Number | item | Blocks whose lowest vertex is within this of the global min Z are fixed (ground) |
| Density (`Rho`) | Number | item | Stone density (kg/m^3) |
| ContactTol (`Ct`) | Number | item | Contact detection distance tolerance (model units) |
| AngleTol (`At`) | Number | item | Contact detection face-angle tolerance (degrees). Raise to ~12-20 for stones on CURVED surfaces, where adjacent stones extrude along different normals and joint faces tilt apart. |
| Assembly (`A`) | Generic | item | OPTIONAL: a pre-built assembly (e.g. the Polygonal Wall Generator's Assembly output, with  exact generator-adjacency joints). When supplied, Stones/tolerances are ignored and the  check runs directly on it - much faster and tolerance-free. |
| CRA (`Cr`) | Boolean | item | Use the COUPLED rigid-block analysis (Kao 2022 Eqs 8-14, alternating convex certificate)  instead of force-only RBE. CRA also checks that a kinematically consistent virtual motion  exists, rejecting self-stressed states RBE wrongly accepts (the H-model). |

| out | type | access | description |
|---|---|---|---|
| Stable (`OK`) | Boolean | item | True when an admissible compressive/friction-consistent force state exists (RBE-stable) |
| Report (`R`) | Text | item | Verdict, counts, max compression, worst friction utilization, weakest interface |
| Utilization (`U`) | Number | list | Per-interface max friction utilization (1.0 = cone saturated) |

### Match Block Transform  (`MatchBlk`)

- GUID: `89ABCDEF-0123-4567-89AB-CDEF01234567`  |  icon: `RigidTransform.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/MatchBlockTransformComponent.cs`
- Algorithm: **Closed-form absolute orientation (Horn QAO)** - Horn 1987, Closed-form solution of absolute orientation using unit quaternions, JOSA A 4(4):629-642
- Library-based auto-match: given a list of canonical  library meshes and a list of placed (target) meshes,  find which library entry each target was transformed  from and recover the placement transform via Horn QAO.  Strictly more accurate than 3-random-vertex matching  because the fit is least-squares over all N vertex pairs.  Implements Horn QAO (Horn 1987).

| in | type | access | description |
|---|---|---|---|
| Library Meshes (`Lib`) | Mesh | list | Canonical mesh shapes (the 'pick' representations). |
| Target Meshes (`T`) | Mesh | list | Placed meshes to match against the library. |
| RMS Threshold (`Rms`) | Number | item | Maximum acceptable per-fit RMS for a high-confidence  match. Targets whose best library fit exceeds this  threshold are still emitted but tagged 'low confidence'.  Default 1e-3. |

| out | type | access | description |
|---|---|---|---|
| Transforms (`T`) | Transform | list | Per-target transform: library[matched] → target. |
| Matched Library Index (`Idx`) | Integer | list | Per-target library index. -1 when no candidate had a  matching vertex count. |
| RMS (`E`) | Number | list | Per-target Horn QAO residual at the chosen library entry. |
| Status (`St`) | Text | list | 'matched (rms=…)' / 'low confidence (rms=…)' / 'no match'. |

### Mesh Planar Polygon Extractor  (`MeshPlnPoly`)

- GUID: `F2D000B2-CADC-4F2D-A0B2-7E60CADA15A0`  |  icon: `MortarJoint.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/MeshPlanarPolygonExtractorComponent.cs`
- Algorithm: **Boundary-loop extraction + signed-area outer/hole classification** - Frahan-original
- Extracts the outer + hole loops from a mesh's boundary  and projects them into a 2D plane. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Mesh whose boundary loops are extracted. |
| Plane (`Pl`) | Plane | item | Projection plane (origin + axes). Default world XY. |

| out | type | access | description |
|---|---|---|---|
| Outer (`O`) | Curve | item | Closed CCW polyline curve for the outermost loop. |
| Holes (`H`) | Curve | list | Closed CW polyline curves for each hole (parallel to  Hole Areas). |
| Outer Area (`Ao`) | Number | item | Signed area of the outer loop in plane units². |
| Hole Areas (`Ah`) | Number | list | Signed area of each hole. |

### Mesh Quality Report  (`MQ`)

- GUID: `9ABCDEF0-1234-5678-9ABC-DEF012345678`  |  icon: `PackDiagnostics.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/MeshQualityReportComponent.cs`
- Algorithm: **Mesh-quality metrics** - Frey & Borouchaki 1999, Surface mesh quality evaluation, Int. J. Numer. Methods Eng. 45(1):101-118
- Topology + geometry diagnostics for a Rhino mesh. Use as  a precondition for contact detection, packing, or cutting.  Metrics per Frey & Borouchaki 1999.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Mesh to analyse. |
| Dedup Tolerance (`Td`) | Number | item | Vertex-merge tolerance for duplicate detection. Default 1e-9. |
| Degenerate Area Tol (`Ta`) | Number | item | Triangle-area threshold below which a triangle counts as  degenerate. Default 1e-12. |

| out | type | access | description |
|---|---|---|---|
| Is Clean Solid (`OK`) | Boolean | item | True iff closed AND manifold AND consistent normals AND no  degenerate triangles AND no duplicate vertices. |
| Manifold (`Mf`) | Boolean | item | Every edge is incident to 1 or 2 triangles. |
| Closed (`Cl`) | Boolean | item | Every edge is incident to exactly 2 triangles. |
| Consistent Normals (`Nm`) | Boolean | item | No two triangles share an edge in the same winding direction. |
| Duplicate Vertices (`DupV`) | Integer | item | Vertices closer than the dedup tolerance to an earlier one. |
| Degenerate Triangles (`DegT`) | Integer | item | Triangles below the area threshold. |
| Boundary Edges (`Be`) | Integer | item | Edges incident to exactly one triangle (open boundaries). |
| Non-manifold Edges (`Nme`) | Integer | item | Edges incident to three or more triangles. |
| Median Edge Length (`MedE`) | Number | item | Median triangle-edge length. Useful as an adaptive-tolerance  scale factor. |
| Surface Area (`A`) | Number | item | Sum of triangle areas. |
| Signed Volume (`V`) | Number | item | Divergence-theorem volume. Negative means normals are  inward-facing on a closed mesh. |
| Report (`R`) | Text | item | Single-line human-readable summary. |

### Next-Best-Object Pose → Robot Frame  (`NBORobot`)

- GUID: `D5F10031-0BA0-4ED9-A031-0BA00BA00031`  |  icon: `NboPoseRobotFrame.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Nbo/NboPoseToRobotFrameComponent.cs`
- Turn NBO placements into robot TCP frames + UR poses via a top-pick grasp model.  Outputs pick / place / approach frames, the place pose as UR p[...] in the robot base,  and the grip width/length. The live robot stays downstream (Robots/visose,  UnderAutomation, compas_fab); this is the planner->robot handoff only.

| in | type | access | description |
|---|---|---|---|
| Stones (`S`) | Mesh | list | Source stone meshes (where they currently sit, for the pick frame). |
| Placements (`X`) | Transform | list | NBO placement transforms, matching the Stones order. |
| Robot Base (`B`) | Plane | item | Robot base frame in world coords; the place pose is expressed in it. |
| Approach (`A`) | Number | item | Approach/retract clearance above each place frame (m). |

| out | type | access | description |
|---|---|---|---|
| Pick Frames (`Pk`) | Plane | list | TCP frame to grab each stone where it sits. |
| Place Frames (`Pl`) | Plane | list | TCP frame to place each stone. |
| Approach Frames (`Ap`) | Plane | list | Pre-place / retract waypoint above each place frame. |
| Place Poses (`Ps`) | Text | list | Place TCP as a UR p[x,y,z,rx,ry,rz] (m, axis-angle) in the robot base. |
| Grip Width (`Gw`) | Number | list | Stone extent across the jaw axis (gripper opening). |
| Grip Length (`Gl`) | Number | list | Stone extent along the jaw-open axis. |

### Pack Diagnostics  (`PackDiag`)

- GUID: `C4D5E6F7-A8B9-4ABC-DEF0-123456789012`  |  icon: `PackDiagnostics.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/PackDiagnosticsComponent.cs`
- Splits an AshlarPackResult into Coverage / Course Count /  Leftovers / Notes / Placed Blocks for inspection.

| in | type | access | description |
|---|---|---|---|
| Result (`R`) | Generic | item | AshlarPackResult from Ashlar Pack. |

| out | type | access | description |
|---|---|---|---|
| Coverage (`Cov`) | Number | item | Wall area filled by placed blocks divided by total wall area, in [0, 1]. |
| Course Count (`N`) | Integer | item | Number of courses laid. |
| Leftovers (`L`) | Generic | list | Slabs from the input inventory that were not placed. |
| Notes (`Notes`) | Text | list | Diagnostic messages emitted by the layout engine. |
| Placed Blocks (`B`) | Generic | list | MasonryBlocks in the order they were laid. |

### Pack Preview  (`PackPrev`)

- GUID: `D5E6F7A8-B9CA-4BCD-EF01-234567890123`  |  icon: `AssemblyState.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/PackPreviewComponent.cs`
- Builds one Rhino mesh per placed block in an AshlarPackResult  for visual preview on the canvas.

| in | type | access | description |
|---|---|---|---|
| Result (`R`) | Generic | item | AshlarPackResult from Ashlar Pack. |

| out | type | access | description |
|---|---|---|---|
| Meshes (`M`) | Mesh | list | One Rhino mesh per placed MasonryBlock. |

### Pick Place Frames  (`PickPlc`)

- GUID: `456789AB-CDEF-0123-4567-89ABCDEF0123`  |  icon: `FrameBuilder.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/PickPlaceFramesComponent.cs`
- Per-block pick-and-place planes for a robot consumer.  Wire Place Transforms from Block Ground Transforms; the  component returns pick + approach-pick (shared across  all blocks) and place / approach-place / retract-place  (one per block).

| in | type | access | description |
|---|---|---|---|
| Place Transforms (`T`) | Transform | list | Per-block placement transform (typically the output of  Block Ground Transforms, ordered by Build Order). Each  transform takes the canonical pose at Pick Plane to  the placed pose. |
| Pick Plane (`Pp`) | Plane | item | Where each canonical block is picked up. Default world XY. |
| Approach Vector (`Av`) | Vector | item | World-frame direction the robot approaches FROM (i.e.,  moves opposite to when descending). Default world +Z so  the gripper hovers above pick / place poses. |
| Approach Distance (`Ad`) | Number | item | Distance the gripper hovers above pick / place poses  before descending. Default 0.05 (5 cm in metres, or  5 mm in millimetres — match your unit system). |
| Retract Distance (`Rd`) | Number | item | Distance the gripper retracts after release. Default 0.05. |

| out | type | access | description |
|---|---|---|---|
| Pick (`Pi`) | Plane | item | Shared pick pose (same as input Pick Plane). |
| Approach Pick (`ApPi`) | Plane | item | Pick + approach offset. Hover here before descending to pick. |
| Place (`Pl`) | Plane | list | Per-block place pose = Pick · transform[i]. |
| Approach Place (`ApPl`) | Plane | list | Per-block approach-place = place + approach offset. |
| Retract Place (`RtPl`) | Plane | list | Per-block retract-place = place + retract offset. |

### Polygon Sanitize  (`PolySan`)

- GUID: `F2D000B1-CADC-4F2D-A0B1-7E60CADA15A0`  |  icon: `PolygonSimplify.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/PolygonSanitizeComponent.cs`
- Algorithm: **Polygon vertex cleanup (dedup + collinearity drop)** - Frahan-original
- Drops duplicate / collinear vertices and sliver edges  from a closed polyline. Operates in 2D — points are  projected onto the supplied plane (default world XY).  Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Polyline (`P`) | Curve | item | Closed polyline curve. |
| Plane (`Pl`) | Plane | item | 2D projection plane. Default world XY. |
| Dedup Tolerance (`Td`) | Number | item | Adjacent-vertex dedup tolerance. Default 1e-6. |
| Collinear Tolerance (`Tc`) | Number | item | Triangle area threshold for collinear-chain dropping.  Default 1e-6. |

| out | type | access | description |
|---|---|---|---|
| Sanitized (`S`) | Curve | item | Cleaned closed polyline. |
| Verts Dropped (`Vd`) | Integer | item | Number of vertices removed. |
| Area (`A`) | Number | item | Signed area of the sanitized polygon (in plane units²). |

### Polygonal Masonry Sequence  (`PolyMasonrySeq`)

- GUID: `B4E07A3C-7F4D-4E5B-9C71-0EAF21C9B6A1`  |  icon: `CourseGenerator.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/Sequencing/PolygonalMasonrySequenceComponent.cs`
- Algorithm: **Polygonal masonry install sequence** - Kim 2024 ASME DETC2024-142563 Finding Installation Sequence of Polygonal Masonry through Design and Depth Search of a DAG
- Installation-order DAG for a polygonal-masonry wall  (Kim 2024). Inputs are 2D chains and a wall rectangle.  Each chain must be monotone in x or a vertical connector.  Output is one closed polyline per stone, parallel install  order, depth from Code 1, and DAG edge line segments.

| in | type | access | description |
|---|---|---|---|
| Chains (`C`) | Curve | list | Polylines or curves defining the wall partition. Each must  be monotone in x or a purely vertical connector. Chains  may share endpoints at meetings; they must not cross. |
| Wall (`W`) | Rectangle | item | Axis-aligned wall rectangle. Defines the bbox and the  wall boundary (paper sec. 5.3). |
| Hole Probes (`H`) | Point | list | Optional. Each probe point marks the region containing it  as a hole; that region is removed before depth search  (paper sec. 5.4). |
| Epsilon (`e`) | Number | item | Tolerance for vertex deduplication and predicates. |

| out | type | access | description |
|---|---|---|---|
| Stones (`S`) | Curve | list | One closed polyline per stone region (finite, non-hole).  Includes the two infinite top/bottom bands. |
| Install Order (`i`) | Integer | list | 1-based install index per stone, parallel to Stones.  1 = installed first (bottom), max = installed last. |
| Depth (`d`) | Integer | list | Reversed-Kahn depth per stone. Higher = installed earlier.  Sinks (last-installed) have depth 0. |
| DAG Edges (`E`) | Line | list | One line segment per DAG edge from lower-order centroid to  higher-order centroid. Visualises the install constraint  graph (paper Figs. 5, 13, 14). |
| Region Count (`n`) | Integer | item | Number of finite stone regions (excludes the bbox  surroundings and any hole-marked regions). |

### Polygonal Masonry Sequence 3D  (`PolyMasonrySeq3D`)

- GUID: `C5F18B4D-8A6F-4E72-AC83-1FBD32D8C7B2`  |  icon: `StereotomyGenerate.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/Sequencing/PolygonalMasonrySequence3DComponent.cs`
- Algorithm: **3D polygonal masonry install sequence** - Kim 2024 DETC2024-142563 section 8 3D extension
- Install-order DAG for a 3D polyhedral-stone wall. Each  input Mesh is one stone; adjacency is detected from  shared mesh faces. Returns 1-based install order,  reversed-Kahn depth, and DAG edges as line segments  between cell centroids (Kim 2024 sec. 8 extension).

| in | type | access | description |
|---|---|---|---|
| Cells (`M`) | Mesh | list | Closed polyhedral meshes; one per stone. |
| Hole Probes (`H`) | Point | list | Optional. Each probe point marks the cell whose centroid  is closest as a hole; that cell is removed before the  depth search (sec. 5.4 analogue). |
| Face Tolerance (`Tf`) | Number | item | Two cells count as adjacent when at least one of their  mesh faces matches within this Euclidean tolerance. |
| Z Threshold (`Tz`) | Number | item | Adjacent cells whose representative-Z difference is  below this value are treated as side neighbours  (no ordering constraint). |

| out | type | access | description |
|---|---|---|---|
| Stones (`S`) | Mesh | list | Cells in install order, parallel to Order / Depth. |
| Install Order (`i`) | Integer | list | 1-based install index per cell, parallel to Stones. |
| Depth (`d`) | Integer | list | Reversed-Kahn depth per cell. |
| DAG Edges (`E`) | Line | list | Line segments from lower-Z cell centroid to higher-Z  cell centroid for every DAG edge. |
| Cell Count (`n`) | Integer | item | Number of stones included in the install plan  (excludes hole-marked cells). |

### Polygonal Wall (Generator)  (`PolyWall`)

- GUID: `D5F10014-7A11-4C0E-9B22-3F6A1E2C4D80`  |  icon: `CourseGenerator.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Masonry/Sequencing/PolygonalWallGeneratorComponent.cs`
- Tile a surface (flat / curved / double-curved) with interlocking polygonal masonry stones  (power-diagram cells + Lloyd relaxation + Coursing slider + sliver cull). Outputs closed,  manifold, unified-normal stone meshes and an interlock score J.  Pattern math: Frahan.Masonry.Sequencing.PolygonalWallGenerator (Rhino-free Core).  Refs: Kim 2024 (ASME DETC2024-142563) sequencing substrate; Clifford & McGee 2018  (ACADIA, Cyclopean Cannibalism) interlock reading; Lloyd 1982 relaxation.

| in | type | access | description |
|---|---|---|---|
| Surface (`S`) | Surface | item | Surface to tile (flat / curved / double-curved). If empty, a flat W x H panel in the XZ plane is used. |
| Width (`W`) | Number | item | Panel width (m) when no Surface is supplied |
| Height (`H`) | Number | item | Panel height (m) when no Surface is supplied |
| Coursing (`C`) | Number | item | 0 = irregular (Inca)  ->  1 = coursed rubble |
| Courses (`Cr`) | Integer | item | Number of courses the coursing pulls toward |
| GridX (`Gx`) | Integer | item | Seed columns (stones along width) |
| GridY (`Gy`) | Integer | item | Seed rows (stones along height) |
| Depth (`D`) | Number | item | Stone depth (m), extruded along the surface normal |
| Mortar (`M`) | Number | item | Mortar joint fraction (cell shrink toward centroid, 0..0.45) |
| Seed (`Sd`) | Integer | item | Random seed |
| Lloyd (`L`) | Integer | item | Lloyd relaxation iterations (evens stone size/shape; 0 disables) |
| SizeGrade (`Sg`) | Number | item | Size-grading strength 0..~0.6 (power-diagram weights) |

| out | type | access | description |
|---|---|---|---|
| Stones (`St`) | Mesh | list | Closed, manifold, unified-outward-normal stone meshes |
| Count (`N`) | Integer | item | Number of stones |
| Interlock (`J`) | Number | item | Interlock score in [0,1]: 1 - runningJoints/headJoints - 0.5*crossVertices/cells. Higher = better staggering. |
| Report (`R`) | Text | item | Pattern metrics (coverage, area CV, slivers culled, joints) |
| Assembly (`A`) | Generic | item | The wall as a structural assembly with EXACT joint interfaces from the generator's own cell  adjacency (no contact re-detection; correct on any curvature). Feed straight into Masonry  Stability Check's Assembly input. Models the dry (mortarless), uniform-depth wall. |

### Robust Auto Interfaces  (`RAutoIf`)

- GUID: `F2D000B4-CADC-4F2D-A0B4-7E60CADA15A0`  |  icon: `ContactDetector.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/RobustAutoInterfacesComponent.cs`
- Algorithm: **Auto interface detection** - Frahan-original
- Detects block-to-block contacts via mesh-vertex proximity.  Robust to slight gaps, non-planar contact regions, and  irregular triangulation (scan-derived meshes). Use this  when 'Auto Interfaces' (polygon-based) misses contacts.  Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Meshes (`M`) | Mesh | list | Block meshes in their final placed positions. Standard Rhino  mesh wires; this component finds proximity-based contacts. |
| Block Ids (`Ids`) | Text | list | One ID per mesh in the same order. Must match the IDs used  to construct MasonryBlocks. |
| Distance Tolerance (`Dtol`) | Number | item | Max distance between two surfaces to count as contact  (document units). Default 0.001 (1 mm). Raise for noisy  scan data; lower for exact-coord meshes. |
| Angle Tolerance Deg (`Atol`) | Number | item | Contact points are grouped when their surface normals agree  within this angle. Default 5° — accommodates mild surface  curvature; tighten to 1° for sharply-faceted blocks. |
| Min Contact Points (`MinN`) | Integer | item | Minimum contact points required to emit a MasonryInterface.  Default 3 (= polygon triangle minimum). Raise to filter  out spurious single-vertex grazes. |

| out | type | access | description |
|---|---|---|---|
| Interfaces (`I`) | Generic | list | Detected MasonryInterfaces. Wire into Masonry Assembly. |
| Count (`N`) | Integer | item | Number of detected interfaces. |

### Rubble Wall Settle  (`RubbleSettle`)

- GUID: `6514A1BB-FE82-4919-9419-141A07D2358A`  |  icon: `RubbleWallSettle.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Masonry/RubbleWallSettleComponent.cs`
- Algorithm: **COM-over-support stability** - Heyman 1966 limit-state masonry (centre of thrust within the support)
- NOTE: the settle v2 objective reaches +97% clearance, 23/24 stable  (see examples/27 cards); this component remains the validated v1.  Settles stone meshes into an upright Z-up rubble wall. Each  stone is PCA-oriented so its broad/flat face beds DOWN, then  dropped (gravity = -Z) into the per-(x,y)-cell dimples of the  course below, trying 4 orientation flips and a small X-slot  shift. Non-penetrating by construction. Reports a per-stone  COM-over-support stability flag and signed support clearance.  Apply each output mesh as-is; transforms are already baked in.

| in | type | access | description |
|---|---|---|---|
| Stones (`S`) | Mesh | list | Stone inventory as Rhino meshes (e.g. ETH1100 dry-stone scans,  Quarry blocks, or hand-authored). Each is PCA-oriented for flat  bedding; the input meshes are not modified. Order is preserved  in the outputs. |
| Width (`W`) | Number | item | Wall length along +X, in units of the mean stone X-extent.  > 0. Default 7.0 (the signed-off proportion). Larger spreads  stones into more, shorter courses; smaller piles them taller. |
| Stability Aware (`St`) | Boolean | item | When true, each stone prefers the first seat whose COM projects  inside its contact support polygon (won't topple), then the  deepest. When false, always takes the deepest (densest) seat.  Default true. |
| Margin (`M`) | Number | item | Required COM-over-support clearance (document units) for a seat  to count as stable. >= 0. Default 0.0 (COM merely inside the  support polygon). |

| out | type | access | description |
|---|---|---|---|
| Settled (`S`) | Mesh | list | Placed stones, upright in the Z-up wall, one per input mesh in  input order. The PCA flat-bed orientation, flip, and settle  offsets are already applied. |
| Stable (`St`) | Boolean | list | Per-stone COM-over-support flag: true if the projected COM lies  inside the contact support polygon by at least Margin. |
| Clearance (`C`) | Number | list | Per-stone signed support clearance. > 0 = COM inside the support  polygon (distance to the nearest edge); <= 0 = would topple;  -1 = degenerate support (< 3 non-collinear contacts). |

Related:
- Frahan > Masonry > Ashlar Pack - production coursed layout; this settle drops rough rubble into the dimples instead of a regular grid
- Frahan > Masonry > Best Fit Pack - inventory-aware ashlar packer for the same stone inventory
- Frahan > Masonry > Masonry Stability (RBE) - full rigid-block equilibrium; this component does only the per-stone COM-over-support gate

### Stone-Cell Match (Λ)  (`StoneMatchL`)

- GUID: `D5F10016-6C2D-4F1B-B3E8-7A95D0C41F62`  |  icon: `MatchCandidate.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Masonry/StoneCellMatchComponent.cs`
- Hungarian assignment of a stone inventory to target wall cells, minimising carved material.  Reports per-stone carve ratio λ, the workflow imposition index Λ (0 = as-found … 1 = pure  stock; Cyclopean Cannibalism datum ≈0.27) and gap ratios, and outputs the stones placed  into their cells. Refs: Clifford & McGee 2018 (ACADIA); Kuhn 1955 (Hungarian);  Frahan SLM+ROSES masonry review 2026-06-10.

| in | type | access | description |
|---|---|---|---|
| Stones (`St`) | Mesh | list | Stone inventory (closed meshes, found/scanned) |
| Cells (`Ce`) | Mesh | list | Target cells (closed meshes, e.g. the Polygonal Wall Generator's stones at Mortar = 0) |
| TopK (`K`) | Integer | item | Prefilter candidates per cell that get the voxel cost |
| CostRes (`Cr`) | Integer | item | Voxel resolution for the assignment cost |
| RefineRes (`Rr`) | Integer | item | Voxel resolution for the final per-pair metrics |

| out | type | access | description |
|---|---|---|---|
| Placed (`P`) | Mesh | list | Stones transformed into their assigned cells (cell order) |
| Carve (`L`) | Number | list | Per-placement carve ratio lambda_i = carved/found volume (cell order) |
| Gap (`G`) | Number | list | Per-placement gap ratio (cell volume the stone fails to fill) |
| Lambda (`LL`) | Number | item | Workflow imposition index: volume-weighted carve fraction (0..1) |
| StoneIndex (`Si`) | Integer | list | Assigned stone index per placement (cell order) |
| Unused (`U`) | Integer | list | Inventory indices that were not used |
| Report (`R`) | Text | item | Summary |

### Wall Frame  (`WallFrame`)

- GUID: `A2B3C4D5-E6F7-489A-BCDE-F01234567890`  |  icon: `CourseGenerator.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Masonry/WallFrameComponent.cs`
- Bundles wall width / height / thickness into a WallFrame DTO.  Wire into the optional WallFrame input on Ashlar Pack so the  envelope can be reused across multiple packers.

| in | type | access | description |
|---|---|---|---|
| Width (`W`) | Number | item | Wall length along +X (Rhino-document units, typically meters).  Must be > 0. Example: 1.5 for a 1.5 m long wall. |
| Height (`H`) | Number | item | Wall height along +Z. Must be > 0. Example: 1.0 for 1 m tall. |
| Thickness (`T`) | Number | item | Wall thickness along +Y. Must be > 0. Default 0.20 — typical  single-leaf masonry. Use 0.40 for double-leaf. |

| out | type | access | description |
|---|---|---|---|
| Wall Frame (`F`) | Generic | item | WallFrame DTO. Wire into Ashlar Pack's Wall Frame input to  reuse the same envelope across multiple packers. |


## Mesh

### Bench From Mesh  (`BenchFromMesh`)

- GUID: `D3E4F5A6-3002-4F5E-A6B7-C8D9E0F12345`  |  icon: `QuarryBlock.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Quarry/BenchFromMeshComponent.cs`
- Derive an axis-aligned Box bench + carry the original Mesh  for use with the existing 11 BCO components (which take Box  inputs) and with ClipBoxesByMesh (which filters their Box[]  outputs). Closes the §7.8 mesh-bench gap without editing any  existing BCO component. Designed for non-rectangular quarry  benches: trapezoidal, stepped, polygonal, surveyed from a  DXF + bench-height extrusion, or produced by a Phase H  scan reconstruction.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Bench mesh (any closed or open shape). The AABB is derived  from this for the Box output; the mesh itself is preserved  for downstream clipping. |
| Bench Height (`H`) | Number | item | Optional override: if > 0, the Box output's Z height is  extended to this value (e.g. when the user wants the bench  AABB to span the full block height even though the mesh  only covers the working face). Default 0 = use mesh AABB Z  as-is. |

| out | type | access | description |
|---|---|---|---|
| Box (`B`) | Box | item | Axis-aligned bounding Box of the mesh (with optional  Bench Height override applied to Z). Wire to the existing  BCO components' `Tested Area` or `Bench` input. |
| Mesh (`M`) | Mesh | item | Pass-through of the input mesh, for downstream clipping. |
| Bench Boundary (`BB`) | Generic | item | Opaque BenchBoundary value (Box + Mesh combined). Future  BCO-v2 components consume this directly. |
| Vertex Count (`V`) | Integer | item | Mesh vertex count (sanity check). |

### Clip Boxes By Mesh  (`ClipBoxesByMesh`)

- GUID: `D3E4F5A6-3003-4F5E-A6B7-C8D9E0F12345`  |  icon: `QuarryCutOpt.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Quarry/ClipBoxesByMeshComponent.cs`
- Algorithm: **Mesh-containment box filter** - Frahan-original
- Filter a Box[] grid from BCO output by mesh-boundary  containment. Drops cells that lie outside the actual bench  (the cells the AABB algorithm wrongly claimed as winnable).  Use after BCOExtract / HeteroExt / BCOMixedPack to get the  true recovery on a non-rectangular bench.  Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Boxes (`B`) | Box | list | Box[] from a BCO output (Prime Boxes, Mixed Boxes, Zone  Boxes, etc.). |
| Mesh Bench (`M`) | Mesh | item | Closed mesh of the actual bench geometry. Wire from  BenchFromMesh.Mesh. |
| Inside Fraction Threshold (`Tf`) | Number | item | A box is kept when at least this fraction of its 8 corners  lie inside the mesh. 0 = keep all (back-compat); 0.5 =  majority must be inside; 1.0 = entire box must be inside. |
| Tolerance (`T`) | Number | item | Inside/outside testing tolerance in model units. |

| out | type | access | description |
|---|---|---|---|
| Inside Boxes (`In`) | Box | list | Cells whose containment fraction meets or exceeds the  threshold. |
| Outside Boxes (`Out`) | Box | list | Cells that fall below the threshold (the AABB algorithm  wrongly claimed these). |
| Inside Count (`Ni`) | Integer | item | Number of cells inside the mesh. |
| Outside Count (`No`) | Integer | item | Number of cells dropped. |
| Corrected Recovery (`Rc`) | Number | item | Inside / total fraction in [0, 1]. Apply this multiplicatively  to recovery numbers reported by the BCO components when they  ran on the same AABB grid. |
| Report (`R`) | Text | item | Human-readable summary. |

### Close Holes  (`CloseHoles`)

- GUID: `F2D05A02-1A2B-4C3D-9E4F-5A6B7C8D9E02`  |  icon: `PoissonReconstruct.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/MeshSanitizeComponents.cs`
- Algorithm: **Geogram hole filling** - GEO::fill_holes — triangulate open boundary loops up to an area / edge-count threshold
- Fill open boundary loops to make a watertight mesh. Backend: Managed  (RhinoCommon Mesh.FillHoles, fast on clean meshes), Geogram  (GEO::fill_holes, robust on dirty / scan meshes), or Auto (managed  first, geogram fallback if still open). Max Hole Area / Edges apply  to the Geogram path. Runs on a background thread (Run gate) so the  canvas never freezes.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Mesh with holes (open boundary loops). |
| Max Hole Area (`A`) | Number | item | Geogram path: largest hole AREA to fill (model units squared). 0 fills  nothing; a very large value (default 1e30) fills every hole. |
| Max Hole Edges (`E`) | Integer | item | Geogram path: max boundary edges per hole. 0 = no limit (area governs). |
| Repair After (`Rp`) | Boolean | item | Geogram path: run a repair pass after filling. Default true. |
| Run (`R`) | Boolean | item | Set true to close holes (background thread). |
| Backend (`Bk`) | Integer | item | 0 = Auto (managed first, geogram fallback); 1 = Managed (RhinoCommon,  fast); 2 = Geogram (robust on dirty meshes). |

| out | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Hole-filled mesh. |
| Closed (`Cl`) | Boolean | item | True if the output mesh is closed (watertight). |
| Report (`R`) | Text | item | Before/after validity summary + backend used. |

Related:
- Frahan > Mesh > Sanitize Mesh - Sanitize first (weld/triangulate), then close holes.
- Frahan > Mesh > Scan Reconstruct - Alpha-Shape output is open; close holes to get a watertight tool mesh.

### Cloud ICP  (`CloudIcp`)

- GUID: `E4F5A6B7-3201-4F5E-A6B7-C8D9E0F12345`  |  icon: `PointCloudIcp.png`  |  exposure: `quarternary`  |  source: `src/Frahan.StonePack.GH/ScanIngest/CloudIcpComponent.cs`
- Algorithm: **Trimmed ICP (coarse-to-fine)** - Besl & McKay 1992 (Iterative Closest Point); Chetverikov et al. 2002 (Trimmed ICP)
- Register a source point cloud onto a target via coarse-to- fine trimmed ICP. Uses Geogram KD-tree + voxel downsample  (native shim, Phase I) when available; falls back to  managed brute-force / hash-grid otherwise. Scales to 10M+  points with the native shim. [Besl & McKay 1992]

| in | type | access | description |
|---|---|---|---|
| Source Cloud (`S`) | Point | list | Source point cloud to register. |
| Target Cloud (`T`) | Point | list | Target point cloud (registration goal). |
| Initial Guess (`X0`) | Transform | item | Optional initial transform. Identity if not wired. |
| Voxel Scales (`Vs`) | Number | list | Coarse-to-fine voxel sizes (model units). Default {0.5, 0.1,  0.02} → 50 cm → 10 cm → 2 cm for metre-scale benches. |
| Max Iterations (`Mi`) | Integer | item | Max ICP iterations per voxel scale. |
| Trim Fraction (`Tf`) | Number | item | Drop this fraction of worst-residual pairs each iteration.  0.2 = standard robust ICP. 0 keeps all. |

| out | type | access | description |
|---|---|---|---|
| Transform (`X`) | Transform | item | Cumulative source→target rigid transform. |
| Final RMS (`RMS`) | Number | item | Final RMS distance between corresponding source-target pairs. |
| Iterations (`It`) | Integer | item | Total iterations across all voxel scales. |
| Converged (`Cv`) | Boolean | item | True when the last iteration met the tolerance. |
| Correspondences (`Cn`) | Integer | item | Number of correspondences used in the final iteration  (after trim). |
| Report (`R`) | Text | item | Summary line. |

### Estimate Cloud Normals  (`EstNormals`)

- GUID: `E4F5A6B7-3203-4F5E-A6B7-C8D9E0F12345`  |  icon: `NormalEstimation.png`  |  exposure: `quarternary`  |  source: `src/Frahan.StonePack.GH/ScanIngest/EstimateCloudNormalsComponent.cs`
- Algorithm: **PCA normal estimation + MST orientation** - Hoppe et al. 1992, surface reconstruction from unorganized points (PCA tangent planes + MST sign propagation)
- PCA + MST-oriented normals on an unstructured point cloud.  Wire upstream of Poisson reconstruction (ScanReconstruct  Mode = 2) or point-to-plane Cloud ICP. Runs on a background  thread (Run gate). Requires the Phase H/I rebuild of  frahan_cgal.dll; falls back to a Warning bubble if the shim  isn't built. [Hoppe et al. 1992]

| in | type | access | description |
|---|---|---|---|
| Points (`P`) | Point | list | Input cloud as a point list. Optional if Cloud is wired. |
| K Neighbours (`K`) | Integer | item | k for PCA fit (CGAL recommends 18-24 for dense clouds).  0 uses 18. |
| Cloud (`C`) | Geometry | item | Input as a single native PointCloud (lag-free; preferred over the  Points list for large scans). If wired, the Points list is ignored. |
| Run (`R`) | Boolean | item | Set true to estimate normals (on a background thread).  False = idle; the canvas never freezes. |

| out | type | access | description |
|---|---|---|---|
| Normals (`N`) | Vector | list | Per-point oriented normals; same order as input. |
| Report (`R`) | Text | item | Summary. |
| Cloud (`C`) | Geometry | item | The input points as a single native PointCloud WITH the estimated  normals baked in. Wire into Scan Reconstruct (Cloud) for a lag-free  Poisson path - no million-point list crosses the canvas. |

### Frahan Mesh Diagnostics  (`MeshDiag`)

- GUID: `AB12C005-1A2B-4C3D-9E4F-5A6B7C8D9E05`  |  icon: `PackDiagnostics.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/MeshDiagnosticsComponent.cs`
- Algorithm: **Mesh quality diagnostics** - Frahan-original
- Read a Rhino Mesh and report vertex/face/triangle/quad counts,  IsClosed, IsManifold, HasConsistentWinding, AverageEdgeLength,  BoundingBoxVolume.  Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Mesh to diagnose. |

| out | type | access | description |
|---|---|---|---|
| Vertex Count (`V`) | Integer | item | Number of vertices. |
| Face Count (`F`) | Integer | item | Number of faces. |
| Triangle Count (`T`) | Integer | item | Number of triangular faces. |
| Quad Count (`Q`) | Integer | item | Number of quad faces. |
| Is Closed (`Ic`) | Boolean | item | True if the mesh is closed (watertight). |
| Is Manifold (`Im`) | Boolean | item | True if the mesh is manifold. |
| Has Consistent Winding (`Cw`) | Boolean | item | True if face windings are consistent. |
| Average Edge Length (`Ae`) | Number | item | Mean of all unique edge lengths. |
| Bounding Box Volume (`Bv`) | Number | item | Volume of axis-aligned bounding box. |
| Report (`R`) | Text | item | Single-line summary. |

### Frahan Mesh Repair  (`MeshFix`)

- GUID: `AB12C00A-1A2B-4C3D-9E4F-5A6B7C8D9E0A`  |  icon: `PoissonReconstruct.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/MeshRepairComponent.cs`
- Algorithm: **Mesh-repair recipe** - Botsch, Kobbelt, Pauly, Alliez, Levy 2010 Polygon Mesh Processing (AK Peters / CRC Press), ISBN 978-1568814261
- Run the Frahan mesh-repair pipeline (cull degenerate / weld / cull  unused / heal naked edges / unify normals / recompute normals) and  return the repaired mesh plus a per-step trace. [Botsch et al. 2010]

| in | type | access | description |
|---|---|---|---|
| Meshes (`M`) | Mesh | list | Mesh(es) to repair. Originals are not mutated. |
| Weld Angle (`Wa`) | Number | item | Weld vertices whose face normals fall within this angle (radians).  Default = pi/8 (~22.5 deg). |
| Heal Distance (`Hd`) | Number | item | Maximum naked-edge gap to heal (model units). Default = 0.001. |

| out | type | access | description |
|---|---|---|---|
| Repaired (`R`) | Mesh | list | Repaired mesh per input. |
| Trace (`T`) | Text | list | Per-mesh repair trace (one multi-line string per input mesh). |
| Skipped (`Sk`) | Integer | item | Number of meshes skipped (null input or pipeline threw). |
| Summary (`S`) | Text | item | One-line summary. |

### Georeference  (`GeorefCRS`)

- GUID: `B1C2D3A4-1112-4F5E-A6B7-C8D9E0F12345`  |  icon: `GeoreferenceMarker.png`  |  exposure: `quarternary`  |  source: `src/Frahan.StonePack.GH/Registration/GeoreferenceComponent.cs`
- Algorithm: **Absolute orientation + UTM/EPSG transform** - Horn, B.K.P. (1987). Closed-form solution of absolute orientation using unit quaternions. J. Opt. Soc. Am. A 4(4):629-642
- Rigid scan→world transform from N≥3 control-point pairs in a  global coordinate system. Supports WGS84 LLH degrees, UTM,  and pre-converted ENU metres. World points are converted to  ENU about the first control point's origin before solving.  Implements absolute orientation (Horn 1987).  Sibling: GeorefCRS handles the WGS84/UTM/ENU datum;  'GeorefPts' (Georeference (Align by Points)) is the local fit  via Horn when both datasets share a frame.

| in | type | access | description |
|---|---|---|---|
| World Control Points (`W`) | Point | list | World-frame control points in the chosen Coord System.  LLH-WGS84-degrees: pack as (X=lon°, Y=lat°, Z=height-m).  UTM: pack as (X=easting-m, Y=northing-m, Z=elevation-m).  Local-ENU: pack as (X=east-m, Y=north-m, Z=up-m). |
| Scan-Frame Points (`S`) | Point | list | Scan-frame points paired by INDEX with World Control Points. |
| Coord System (`C`) | Integer | item | 0 = LLH-WGS84-degrees, 1 = UTM, 2 = Local-ENU. |
| UTM Zone (`Z`) | Integer | item | Optional UTM zone override (1..60). Ignored unless Coord  System = 1. Default 0 means auto-pick from origin. |
| UTM Northern Hemisphere (`NH`) | Boolean | item | True = northern hemisphere, false = southern. Ignored  unless Coord System = 1. |

| out | type | access | description |
|---|---|---|---|
| Transform (`X`) | Transform | item | Rigid transform mapping scan-frame onto the ENU frame  centred at the first control point. Apply to your scan to  place it in world-relative coordinates. |
| RMS Error (`RMS`) | Number | item | Root-mean-square per-pair residual after the transform  (ENU metres). |
| ENU Origin (LLH) (`O`) | Point | item | The LLH origin used for ENU conversion (X=lon°, Y=lat°, Z=h-m).  Wire this into a Panel to record the projection origin alongside  the .gh file. |
| Report (`R`) | Text | item | Human-readable summary of the solve. |
| Per-Pair Residuals (`Res`) | Number | list | Per-pair residual distances after applying Transform (m). |

### Georeference (Align by Points)  (`GeorefPts`)

- GUID: `F2D05A06-1A2B-4C3D-9E4F-5A6B7C8D9E06`  |  icon: `Downsample.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/GeoreferenceComponent.cs`
- Algorithm: **Absolute orientation (Horn 1987)** - Horn, 'Closed-form solution of absolute orientation using unit quaternions', JOSA A 4(4) 1987
- Best-fit transform from 3+ corresponding control points (Horn's  absolute orientation). Aligns GPR / scan / quarry geometry into one  georeferenced frame: Source = control points in the frame you move,  Target = matching points in the reference frame. Rigid by default;  enable Scale for similarity. Feed the Transform into Cloud ICP's  Initial Guess for fine registration.  Sibling: GeorefPts is the local fit via Horn from matched points;  'GeorefCRS' (Georeference) handles WGS84/UTM/ENU datum conversion.

| in | type | access | description |
|---|---|---|---|
| Geometry (`G`) | Geometry | list | Geometry to align (moved by the fitted transform). |
| Source (`S`) | Point | list | Control points in the SOURCE frame (>= 3). |
| Target (`T`) | Point | list | Matching control points in the TARGET / reference frame (>= 3, same order). |
| Scale (`Sc`) | Boolean | item | Allow uniform scale (similarity transform). Default false = rigid. |

| out | type | access | description |
|---|---|---|---|
| Geometry (`G`) | Geometry | list | Geometry mapped into the target frame. |
| Transform (`X`) | Transform | item | Fitted source -> target transform. |
| Inverse (`Xi`) | Transform | item | Inverse transform (target -> source). |
| RMS (`RMS`) | Number | item | Root-mean-square control-point residual after the fit (model units). |
| Report (`R`) | Text | item | Fit summary. |

Related:
- Frahan > Mesh > Cloud ICP - Feed this Transform into Cloud ICP's Initial Guess for coarse-georef then fine-ICP.
- Frahan > Quarry > GPR Fractures on Mesh - Georeference GPR picks into the scan/bench frame before overlaying.
- Frahan > Mesh > Move to Origin - Move to Origin recenters; this aligns to another dataset via control points.

### Load Cloud  (`LoadCloud`)

- GUID: `E4F5A6B7-3210-4F5E-A6B7-C8D9E0F12345`  |  icon: `Downsample.png`  |  exposure: `quarternary`  |  source: `src/Frahan.StonePack.GH/ScanIngest/LoadCloudComponent.cs`
- Algorithm: **Streaming cloud read + voxel-grid downsample** - Voxel-grid filter (one centroid per occupied cell)
- Stream a point cloud from a file and voxel-downsample on the  fly. Supports PLY (binary_little_endian + ascii; points-only  and mesh-vertex clouds) and plain ASCII XYZ / PTS. Memory is  bounded by occupied voxels, not the input point count, so very  large clouds (28M+ points) load without materialising the full  set. Pure-managed; no native dependency. Runs on a background  thread (Run gate) so the canvas stays responsive. Use upstream of  Cloud ICP / Scale Calibrate.

| in | type | access | description |
|---|---|---|---|
| File Path (`F`) | Text | item | Path to a .ply / .xyz / .pts / .asc / .txt point-cloud file. |
| Voxel Size (`V`) | Number | item | Edge length of the cubic downsample voxel in model units.  Default 0.05. If <= 0, no downsample (warns for huge files). |
| Run (`R`) | Boolean | item | Set true to read the file (on a background thread). False = idle;  nothing is read, the canvas never freezes. |

| out | type | access | description |
|---|---|---|---|
| Points (`P`) | Point | list | One centroid per occupied voxel (or all points when Voxel Size <= 0). |
| Input Count (`Ni`) | Integer | item | Total points read from the file. |
| Output Count (`No`) | Integer | item | Number of output points (occupied voxels). |
| Bounding Box (`B`) | Box | item | Axis-aligned bounding box of the input cloud. |
| Cloud (`C`) | Geometry | item | Downsampled cloud as a single native PointCloud - fast viewport  display and bake-ready (far cheaper than the Points list for big  clouds). Wire into a Point param to explode it back into points. |

### Load E57 Cloud  (`LoadE57`)

- GUID: `E4F5A6B7-3230-4F5E-A6B7-C8D9E0F12345`  |  icon: `Downsample.png`  |  exposure: `quarternary`  |  source: `src/Frahan.StonePack.GH/ScanIngest/LoadE57CloudComponent.cs`
- Algorithm: **Out-of-process E57 read (Python worker) + voxel downsample, chunked into one PointCloud** - pye57 worker -> binary PLY -> chunked PointCloud assembly
- Read a registered terrestrial-LiDAR .e57 via an out-of-process  Python worker (pye57), voxel-downsample, and ingest the result in  chunks as a single PointCloud. The heavy parse runs in a subprocess  so a crash never takes down Rhino. Coordinates are shifted to the  origin (add the Shift output to georeference back). Runs on a  background thread (Run gate); needs python + pye57 + numpy on PATH  and frahan_e57_worker.py deployed beside the .gha.

| in | type | access | description |
|---|---|---|---|
| E57 File (`F`) | Text | item | Path to a .e57 registered point-cloud file. |
| Voxel Size (`V`) | Number | item | Edge length of the cubic downsample voxel in model units (metres).  Default 0.05. If <= 0, no downsample (warns; can be very large). |
| Python Exe (`Py`) | Text | item | Optional python interpreter (full path or bare name). Empty =  'python' on PATH. |
| Run (`R`) | Boolean | item | Set true to run the worker + ingest (on a background thread).  False = idle; nothing runs, the canvas never freezes. |

| out | type | access | description |
|---|---|---|---|
| Cloud (`C`) | Geometry | item | The downsampled scan as a single PointCloud (shifted to the origin).  Wire into a Point param to explode into points if needed. |
| Input Count (`Ni`) | Integer | item | Total points in the E57 (all scans). |
| Output Count (`No`) | Integer | item | Number of points after voxel downsample. |
| Bounding Box (`B`) | Box | item | Axis-aligned box of the output cloud (shifted frame; bounds the Cloud). |
| Shift (`S`) | Vector | item | Global offset subtracted from the original coordinates. Add it back  to the Cloud (e.g. via Move) to restore the georeferenced position. |
| PLY Path (`Pf`) | Text | item | Path to the voxel-downsampled binary PLY the worker wrote (reusable). |

### Load Metashape Dense Cloud  (`LoadOc3`)

- GUID: `D5F10040-ED9E-4ED9-A040-ED9EED9E0040`  |  icon: `Downsample.png`  |  exposure: `quarternary`  |  source: `src/Frahan.StonePack.GH/ScanIngest/LoadMetashapeDenseCloudComponent.cs`
- Algorithm: **File-extension recognition + conversion-guidance emission** - Frahan-original; stub pattern mirrors E57 / GSF bridge components
- Recognise a Metashape .oc3 dense-cloud file and emit conversion  guidance. v1 does NOT parse the binary format; the user must  export the .oc3 to PLY in Metashape first, then load with the  Load Cloud component. v2 will add a Metashape Python worker  following the E57 out-of-process pattern.

| in | type | access | description |
|---|---|---|---|
| Oc3 File (`F`) | Text | item | Path to an Agisoft Metashape .oc3 dense-cloud file. v1 recognises  the format and emits conversion guidance; no point data is  extracted. |

| out | type | access | description |
|---|---|---|---|
| Recognised (`R`) | Boolean | item | True when the file exists and has a .oc3 extension. |
| File Size MB (`Sz`) | Number | item | Size of the .oc3 in megabytes (informational). |
| Guidance (`G`) | Text | list | Conversion guidance: open the .psx in Metashape, File > Export >  Export Dense Cloud to PLY, then load with the Load Cloud component. |

### Load Photo Set  (`LoadPhotoSet`)

- GUID: `D5F10041-ED9E-4ED9-A041-ED9EED9E0041`  |  icon: `Downsample.png`  |  exposure: `quarternary`  |  source: `src/Frahan.StonePack.GH/ScanIngest/LoadPhotoSetComponent.cs`
- Algorithm: **Folder enumeration + bucket classification + filesystem metadata aggregate** - Frahan-original; trimmed-scope v1 deliberately skips EXIF (ExifTool / CloudCompare covers that)
- Inventory a folder of photogrammetry photos into a typed PhotoSet.  v1 SCOPE: filesystem listing + bucket classification (used /  skipped / raw) + aggregate summary. No EXIF parsing (use  ExifTool externally if needed). The typed PhotoSet is what  downstream Frahan components consume.

| in | type | access | description |
|---|---|---|---|
| Photo Folder (`F`) | Text | item | Root folder containing photogrammetry photos. Subfolders named  'used', 'skipped', 'raw' (case-insensitive) bucket-classify the  entries. |
| Recurse Subfolders (`R`) | Boolean | item | If true, recurse into subfolders. Default true (the MRAC convention). |

| out | type | access | description |
|---|---|---|---|
| Photo Set (`PS`) | Generic | item | Typed PhotoSet record (Frahan.Core.ScanIngest.PhotoSet). Wire  into downstream Frahan ingest components. |
| Photo Count (`N`) | Integer | item | Total photo count. |
| Total Size MB (`Sz`) | Number | item | Total size in megabytes. |
| Remarks (`Rm`) | Text | list | Per-bucket counts + date range + extensions seen. |

### Marker Registration  (`MarkerReg`)

- GUID: `B1C2D3A4-1111-4F5E-A6B7-C8D9E0F12345`  |  icon: `MarkerDetect.png`  |  exposure: `quarternary`  |  source: `src/Frahan.StonePack.GH/Registration/MarkerRegistrationComponent.cs`
- Algorithm: **Absolute orientation (Horn 1987)** - Horn, B.K.P. (1987). Closed-form solution of absolute orientation using unit quaternions. J. Opt. Soc. Am. A 4(4):629-642
- Closed-form rigid alignment of N≥3 source/target point pairs  (Horn 1987 quaternion absolute orientation). Use for marker-  or reference-object-based scan-to-world registration.  Implements absolute orientation (Horn 1987).

| in | type | access | description |
|---|---|---|---|
| Source Points (`S`) | Point | list | Source-frame (e.g. scan) marker positions.  Must have N≥3 points paired by INDEX with Target Points. |
| Target Points (`T`) | Point | list | Target-frame (e.g. world) marker positions.  Same count as Source Points; pairing is by index. |

| out | type | access | description |
|---|---|---|---|
| Transform (`X`) | Transform | item | Rigid transform mapping source onto target (apply to scan). |
| RMS Error (`RMS`) | Number | item | Root-mean-square per-pair residual after applying Transform  (model-unit distance). |
| Per-Pair Residuals (`R`) | Number | list | Distance from R·sᵢ+t to tᵢ for each input pair. Long-tail  values indicate bad markers — drop or re-survey them. |
| Pair Count (`N`) | Integer | item | Number of input pairs used. |

### Mesh AABB  (`AABB`)

- GUID: `ABCDEF01-2345-6789-ABCD-EF0123456789`  |  icon: `MeshBvh.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Mesh/MeshAabbComponent.cs`
- Axis-aligned bounding box of a mesh. Outputs the box, its  X/Y/Z extents, and the centre point. Useful for verifying  block dimensions match a wall's expected course height.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Input mesh. |

| out | type | access | description |
|---|---|---|---|
| Box (`B`) | Box | item | Axis-aligned bounding box. |
| Width (`W`) | Number | item | Extent along +X (document units). |
| Depth (`D`) | Number | item | Extent along +Y (document units). |
| Height (`H`) | Number | item | Extent along +Z (document units). |
| Centre (`C`) | Point | item | Centre of the bounding box. |

### Mesh PCA  (`PCA`)

- GUID: `BCDEF012-3456-789A-BCDE-F0123456789A`  |  icon: `FrameBuilder.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Mesh/MeshPcaComponent.cs`
- Algorithm: **Principal component analysis (covariance eigendecomposition)** - Frahan-original
- Principal-component analysis of a mesh's vertex cloud. Returns  a Plane aligned to the natural axes (PC1 = longest, PC2 =  second, PC3 = shortest = plane normal), plus the three extent  lengths along each axis. Use to align rough quarry blocks.  Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Input mesh. |

| out | type | access | description |
|---|---|---|---|
| Frame (`F`) | Plane | item | Plane at the centroid, X-axis = PC1 (longest), Y-axis = PC2,  Z-axis = PC3 (shortest, = plane normal). |
| Length 1 (`L1`) | Number | item | Extent along PC1 (longest principal axis). |
| Length 2 (`L2`) | Number | item | Extent along PC2. |
| Length 3 (`L3`) | Number | item | Extent along PC3 (shortest, = thickness through the plane normal). |
| Centroid (`C`) | Point | item | Centroid of the vertex cloud (unweighted average). |

### Move to Origin  (`ToOrigin`)

- GUID: `F2D05A03-1A2B-4C3D-9E4F-5A6B7C8D9E03`  |  icon: `Downsample.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/UtilityComponents.cs`
- Recenter geometry (mesh / cloud / curves / blocks) to the world  origin as a group. Fixes geometry built from UTM-coordinate scans  that lands far from the origin. Emits the applied Transform and its  inverse so you can map the result back into world space.

| in | type | access | description |
|---|---|---|---|
| Geometry (`G`) | Geometry | list | Geometry to recenter (any type; recentered together as one group). |
| Anchor (`A`) | Integer | item | Which point maps to the target: 0 = bounding-box center,  1 = base (center XY, min Z) [good for 'set on the ground'],  2 = bounding-box min corner. Default 1. |
| Target (`T`) | Point | item | Where the anchor lands. Default world origin (0,0,0). |

| out | type | access | description |
|---|---|---|---|
| Geometry (`G`) | Geometry | list | Recentered geometry. |
| Transform (`X`) | Transform | item | The translation applied (world -> origin). |
| Inverse (`Xi`) | Transform | item | Inverse translation (origin -> world); maps results back. |
| Anchor Point (`P`) | Point | item | The source anchor point (in world coords). |

Related:
- Frahan > Mesh > Bench From Mesh - Bench built from a UTM scan lands far from origin; recenter it here.
- Frahan > Mesh > Read LAS Cloud - LAS/LAZ clouds are in real-world (UTM) coordinates.

### Read LAS Cloud  (`ReadLAS`)

- GUID: `E4F5A6B7-3220-4F5E-A6B7-C8D9E0F12345`  |  icon: `Downsample.png`  |  exposure: `quarternary`  |  source: `src/Frahan.StonePack.GH/ScanIngest/ReadLasCloudComponent.cs`
- Algorithm: **LASzip LAS/LAZ decode** - Isenburg 2013 (LASzip lossless LiDAR compression)
- Read a .las / .laz LiDAR or TLS point cloud and voxel-downsample  on the fly. Handles both uncompressed .las and compressed .laz.  Memory is bounded by occupied voxels, not the input point count,  so very large clouds (100M+ points) load without materialising  the full set. The LAS scale + offset are applied, so points are  in real-world coordinates. Runs on a background thread (Run gate);  the canvas stays responsive. Use upstream of Cloud ICP / Scale  Calibrate. [Isenburg 2013]

| in | type | access | description |
|---|---|---|---|
| File Path (`F`) | Text | item | Path to a .las (uncompressed) or .laz (compressed) point-cloud file. |
| Voxel Size (`V`) | Number | item | Edge length of the cubic downsample voxel in model units.  Default 0.05. If <= 0, no downsample (warns for huge files). |
| Run (`R`) | Boolean | item | Set true to read the file (on a background thread). False = idle;  nothing is read, the canvas never freezes. |

| out | type | access | description |
|---|---|---|---|
| Points (`P`) | Point | list | One centroid per occupied voxel (or all points when Voxel Size <= 0).  Real-world coordinates (LAS scale + offset applied). |
| Input Count (`Ni`) | Integer | item | Total points read from the file. |
| Output Count (`No`) | Integer | item | Number of output points (occupied voxels). |
| Bounding Box (`B`) | Box | item | Axis-aligned bounding box of the input cloud. |
| Cloud (`C`) | Geometry | item | Downsampled cloud as a single native PointCloud - fast viewport  display and bake-ready (far cheaper than the Points list for big  clouds). Wire into a Point param to explode it back into points. |

### Read Metashape Project  (`ReadPsx`)

- GUID: `D5F10042-ED9E-4ED9-A042-ED9EED9E0042`  |  icon: `Downsample.png`  |  exposure: `quarternary`  |  source: `src/Frahan.StonePack.GH/ScanIngest/ReadMetashapeProjectComponent.cs`
- Algorithm: **XML + nested-zip walk: .psx -> .files/project.zip -> 0/chunk.zip** - Frahan-original; tolerant XML parser handles version skew
- Read an Agisoft Metashape .psx project into a typed  MetashapeProject record: sensor calibration, chunk transform,  camera + marker counts, and the resolved mesh.ply path. With  Extract Mesh true, the component unzips mesh.ply to a temp  dir and returns the on-disk path so Frahan's Load PLY Mesh  component can consume it directly.

| in | type | access | description |
|---|---|---|---|
| Psx File (`F`) | Text | item | Path to an Agisoft Metashape .psx project descriptor. The  sibling .files/ directory must be present (Metashape's standard  save convention). |
| Chunk Id (`Ci`) | Integer | item | Which chunk to return. Default 0 (the active chunk in most  projects). Use -1 to return the project's active chunk per the  .psx active_id. |
| Extract Mesh (`Em`) | Boolean | item | If true, extract the chunk's mesh.ply to a temp dir and return  the on-disk path via Resolved Ply. Default true. |

| out | type | access | description |
|---|---|---|---|
| Metashape Project (`MP`) | Generic | item | Typed MetashapeProject record (Frahan.Core.ScanIngest.MetashapeProject). |
| Doc Version (`Dv`) | Text | item | Document schema version (e.g. 1.2.0). |
| Metashape Version (`Mv`) | Text | item | Metashape application version if recoverable. |
| Chunk Count (`Nc`) | Integer | item | Number of chunks in the project. |
| Chunk Plane (`Cp`) | Plane | item | Chunk transform represented as a Rhino Plane (origin + axes). |
| Chunk Scale (`Cs`) | Number | item | Chunk scale factor (Metashape internal units -> world units). |
| Camera Count (`Nc2`) | Integer | item | Camera count in the selected chunk. |
| Marker World Positions (`Mp`) | Point | list | Reference (world) marker positions, one per marker with reference data. |
| Marker Labels (`Ml`) | Text | list | Marker labels parallel to Marker World Positions. |
| Resolved Ply (`Ply`) | Text | item | On-disk path to mesh.ply (when Extract Mesh = true). Wire into  Frahan's Load PLY Mesh. |
| Remarks (`R`) | Text | list | Per-pipeline diagnostics + parser flags. |

### Sanitize Mesh  (`Sanitize`)

- GUID: `F2D05A01-1A2B-4C3D-9E4F-5A6B7C8D9E01`  |  icon: `PoissonReconstruct.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/MeshSanitizeComponents.cs`
- Algorithm: **CGAL PMP mesh repair** - CGAL Polygon Mesh Processing: triangulate_faces + stitch_borders + remove_degenerate_faces + orient_to_bound_a_volume
- Make a mesh valid so CGAL ops accept it: triangulate non-tri faces,  stitch coincident borders, remove degenerate faces, orient/unify  normals, drop unused vertices. Use upstream of CGAL boolean / cut  components and on Alpha-Shape / scan-reconstruction output that  comes out non-manifold or unwelded.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Mesh to sanitize. |
| Backend (`B`) | Integer | item | 0 = CGAL (strict; what CGAL ops need), 1 = Geogram (robust repair),  2 = Auto (Geogram then CGAL). Default 0. |
| Run (`R`) | Boolean | item | Set true to sanitize. |

| out | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Sanitized mesh. |
| CGAL Ready (`Ok`) | Boolean | item | True if the output is closed + manifold (CGAL booleans will accept it). |
| Report (`R`) | Text | item | Before/after validity summary. |

Related:
- Frahan > Mesh > Close Holes - Pair sanitation with hole-closing to reach a watertight surface.
- Frahan > Cut > Cut By Fractures (CGAL) - CGAL boolean cutters require a sanitized, closed, manifold mesh.
- Frahan > Mesh > Scan Reconstruct - Alpha-Shape / reconstruction output usually needs sanitation before use.

### Scan Read  (`ReadPLY`)

- GUID: `789ABCDE-F012-3456-789A-BCDEF0123456`  |  icon: `PlyReader.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Mesh/ReadPlyMeshComponent.cs`
- Algorithm: **PLY parse** - Turk 1994 (PLY Polygon File Format)
- Loads a mesh from a .ply, .obj, or .stl file via pure-managed  parsers (no third-party native code). PLY: ASCII + binary_LE;  OBJ: v + f with vertex/tex/normal triplet syntax, multi-group  files emit one mesh per group; STL: ASCII + binary, vertex  welding at 1e-7 model units. Vertex colours preserved on PLY. [Turk 1994]

| in | type | access | description |
|---|---|---|---|
| File Path (`F`) | Text | item | Absolute path to a .ply / .obj / .stl file. |
| Format (`Fmt`) | Integer | item | 0 = Auto (detect from extension and magic bytes),  1 = PLY, 2 = OBJ, 3 = STL. |

| out | type | access | description |
|---|---|---|---|
| Meshes (`M`) | Mesh | list | One mesh per group/object in the source file. PLY and STL  always produce a single mesh; OBJ may produce many. |
| Names (`N`) | Text | list | Per-mesh name (OBJ group/object name, PLY/STL file stem). |
| Vertex Counts (`V`) | Integer | list | Per-mesh vertex count. |
| Triangle Counts (`T`) | Integer | list | Per-mesh triangle count. |
| Detected Format (`D`) | Text | item | Format the dispatcher actually used (PLY / OBJ / STL). |

### Scan Reconstruct  (`ScanRecon`)

- GUID: `E4F5A6B7-3101-4F5E-A6B7-C8D9E0F12345`  |  icon: `PoissonReconstruct.png`  |  exposure: `tertiary`  |  source: `src/Frahan.StonePack.GH/ScanIngest/ScanReconstructComponent.cs`
- Algorithm: **3D Alpha Shapes** - Edelsbrunner & Mücke 1994 (three-dimensional alpha shapes)
- Reconstruct a closed mesh from a point cloud. Three backends:  Alpha Shape (CGAL; tight; preserves edges), Poisson (Geogram- bundled PoissonRecon, CGAL fallback; smooth; needs oriented  normals), and Advancing-Front (CGAL; BPA-equivalent; tolerant of  unoriented input). Runs on a background thread (Run gate) so the  canvas never freezes. Requires the Phase H rebuild of  frahan_cgal.dll / frahan_geogram.dll. [Edelsbrunner & Mücke 1994]

| in | type | access | description |
|---|---|---|---|
| Points (`P`) | Point | list | Input point cloud as a point list. Optional if Cloud is wired. |
| Normals (`N`) | Vector | list | Optional per-point oriented normals (required for Poisson;  ignored by Alpha Shape and Advancing-Front). |
| Mode (`M`) | Integer | item | 0 = Auto, 1 = AlphaShape, 2 = Poisson (Geogram), 3 = AdvancingFront,  4 = Poisson (CGAL). Auto picks AlphaShape with find_optimal_alpha(1)  and falls back to Advancing-Front. All run in an isolated worker  process, so a backend crash cannot take down Rhino. |
| Alpha (`A`) | Number | item | Alpha value for AlphaShape mode. <= 0 uses CGAL's  find_optimal_alpha(1). |
| Poisson Depth (`D`) | Integer | item | Octree depth for Poisson mode. Typical 7-9. <= 0 uses 8. |
| Samples Per Node (`Sn`) | Number | item | Poisson samples-per-leaf-node. <= 0 uses 1.5. |
| Radius Ratio (`Rr`) | Number | item | Advancing-Front radius ratio. <= 0 uses CGAL default 5.0. |
| AF Beta (`Bt`) | Number | item | Advancing-Front sharp-edge parameter. <= 0 uses 0.52. |
| Cloud (`C`) | Geometry | item | Input as a single native PointCloud (lag-free; preferred for large  scans). If it carries normals (from Estimate Cloud Normals' Cloud  output), Poisson uses them. If wired, the Points / Normals lists are ignored. |
| Run (`R`) | Boolean | item | Set true to reconstruct (on a background thread). False = idle;  nothing runs, the canvas never freezes. |

| out | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Reconstructed mesh. |
| Used Mode (`U`) | Text | item | Which backend actually ran (AlphaShape / Poisson /  AdvancingFront / None). |
| Report (`R`) | Text | item | One-line summary: input count, output verts / tris, mode. |

### Scan Scale Calibrate  (`ScaleCal`)

- GUID: `B1C2D3A4-2001-4F5E-A6B7-C8D9E0F12345`  |  icon: `CalibrationBoard.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/ScanIngest/ScaleCalibrateComponent.cs`
- Algorithm: **Known-distance scale calibration** - Frahan-original
- Derive a uniform scale Transform from a measured reference  curve in the scan and the curve's real-world length.  Optionally apply the transform to a list of input meshes.  Closes the unit-ambiguity gap in photogrammetry / scan workflows.  Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Measured Curve (`C`) | Curve | item | A curve in the scan frame whose real-world length is known  (e.g. picked between two corners of a printed scale bar). |
| Reference Length (`L`) | Number | item | The real-world length the curve should represent, in the  target unit system (Z output). |
| Meshes (`M`) | Mesh | list | Optional scan meshes to scale. When wired, the Scaled Meshes  output carries the transformed copies; otherwise that output  is empty. |
| Units (`U`) | Text | item | Free-form unit label for the report output ("m", "mm",  "ft", etc.). Math is unit-agnostic; this is display only. m |

| out | type | access | description |
|---|---|---|---|
| Scale Transform (`X`) | Transform | item | Uniform scale transform centred at the world origin. Apply  to any scan-frame geometry to bring it into the target frame. |
| Scale Factor (`F`) | Number | item | Uniform scale factor = Reference Length / Measured Length. |
| Measured Length (`Lm`) | Number | item | Length of the measured curve in source-frame units. |
| Scaled Meshes (`Ms`) | Mesh | list | Input meshes after applying the scale transform. Empty when  no Meshes input is wired. |
| Report (`R`) | Text | item | Human-readable summary line. |

### Stone Prep (Scan)  (`StonePrep`)

- GUID: `B1C2D3A4-2002-4F5E-A6B7-C8D9E0F12345`  |  icon: `OutlierRemoval.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/ScanIngest/StonePrepComponent.cs`
- One-button cleanup pipeline for scanned stones:  Repair → optional Decimate → StoneDescriptor. Wraps the  existing Frahan repair + Rhino quadric decimation + Stone  Descriptor builder. Pure managed.

| in | type | access | description |
|---|---|---|---|
| Meshes (`M`) | Mesh | list | Scanned stone meshes to clean. |
| Ids (`I`) | Text | list | Per-stone id. Missing entries default to "stone-{index}". |
| Repair (`Rep`) | Boolean | item | Run the Frahan MeshRepair pipeline (cull degenerate, weld,  fill small holes). |
| Decimate (`Dec`) | Boolean | item | Run quadric edge-collapse decimation to reach Target  Triangle Count (via RhinoCommon's managed Mesh.Reduce). |
| Target Triangle Count (`T`) | Integer | item | Target triangle count for the Decimate stage. 0 disables  decimation regardless of the Decimate toggle. |

| out | type | access | description |
|---|---|---|---|
| Cleaned Meshes (`Mc`) | Mesh | list | Per-stone cleaned mesh after Repair (+ optional Decimate). |
| Descriptors (`D`) | Generic | list | StoneDescriptor per stone (opaque; consumable by Pack3D and  downstream Frahan 3D-packing tools). |
| Mesh Volumes (`Vm`) | Number | list | Per-stone signed mesh volume. |
| Compactness (`C`) | Number | list | Per-stone compactness (MeshVolume / AabbVolume). |
| Triangle Counts (`Tc`) | Integer | list | Per-stone triangle count after the pipeline. |
| Trace (`R`) | Text | list | Multi-line per-stone trace of every stage. |
| Skipped (`Sk`) | Integer | item | Number of stones skipped (null mesh, builder threw, etc.). |

### Voxel Downsample  (`VoxelDown`)

- GUID: `E4F5A6B7-3202-4F5E-A6B7-C8D9E0F12345`  |  icon: `Downsample.png`  |  exposure: `quarternary`  |  source: `src/Frahan.StonePack.GH/ScanIngest/VoxelDownsampleComponent.cs`
- Algorithm: **Voxel-grid centroid downsample** - Voxel-grid filter: one centroid per occupied cubic cell
- Reduce a point cloud by averaging points within each voxel.  Native Geogram path (Phase I shim) when available; managed  hash-grid fallback otherwise. Use upstream of Cloud ICP for  interactive ~10M+-point clouds.

| in | type | access | description |
|---|---|---|---|
| Points (`P`) | Point | list | Input cloud. |
| Voxel Size (`V`) | Number | item | Edge length of the cubic voxel in model units. |

| out | type | access | description |
|---|---|---|---|
| Downsampled (`D`) | Point | list | One centroid per non-empty voxel. |
| Reduction Factor (`Rf`) | Number | item | Output count / input count. |
| Output Count (`N`) | Integer | item | Number of centroids. |


## Quarry

### Bed Block Layout  (`BedBlocks`)

- GUID: `A7E0B0F5-0C0F-4A16-9E3D-0FACE0FACE06`  |  icon: `BlockPackTree.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Quarry/BedBlockLayoutComponent.cs`
- Algorithm: **Cost/volume dimension-block catalogue layout, per bed-bounded layer, exact guillotine tiling** - Elkarmoty et al. 2020 (block recovery on bedded stone); guillotine cutting stock (Gilmore & Gomory 1965)
- Lay marketable dimension blocks (catalogue) into the intact layers between fracture beds,  cut along the beds. Inputs the bench box + the kriged bed surfaces; builds one layer per  inter-bed gap (Oblique = full bed spacing / bed-following; off = flat dip-safe envelope) and  tiles each layer with the block catalogue under a cost-to-volume objective. Volume Weight W  sweeps the plan: 0 = max cost (fewer big high-value blocks), ~500 = balanced, large = max  volume (fill). Outputs the blocks + net value + recovered volume. Reproduces the example-08  Botticino marble study. Facade over Core CatalogueBlockLayout.

| in | type | access | description |
|---|---|---|---|
| Bench (`A`) | Box | item | Bench bounding box (m). The XY footprint + Z range to lay blocks in. |
| Bed Surfaces (`F`) | Mesh | list | Fracture bed surfaces (from GPR Fracture Surfaces 3D). One layer is built per gap between  consecutive beds (and bench top/bottom). |
| Volume Weight (`W`) | Number | item | Cost-to-volume objective weight ($/m3 added to each block's price). 0 = max COST (fewer big  high-value blocks, lower volume, higher net); ~500 = balanced; large (e.g. 3000) = max VOLUME  (fill the layers). Default 0. |
| Oblique (`Ob`) | Boolean | item | TRUE (default) = bed-following: each layer is as thick as the full bed spacing (recovers the  dip wedge; needs georeferenced sloped cuts to execute). FALSE = flat dip-safe envelope (top =  deepest point of the upper bed, bottom = shallowest of the lower bed; fabricable on any gangsaw  today, but the wedges are waste). |
| Cut Cost (`Cut`) | Number | item | Diamond-saw cost (USD/m2 of sawn block face). Default 200. |
| Keep-out (`K`) | Number | item | Inward margin (m) kept from each bed (the GPR position keep-out). Default 0.05. |
| Catalogue (`Cat`) | Number | list | OPTIONAL block catalogue as flat triples [footLength, footWidth, pricePerM3, ...]. Omit for the  default A 3.0x1.5 $2200 / B 2.0x1.5 $1800 / C 1.5x1.0 $1400 / D 1.0x1.0 $1100. |

| out | type | access | description |
|---|---|---|---|
| Blocks (`B`) | Mesh | list | Placed dimension blocks. With Oblique on these are bed-bounded HEXAHEDRA: each block's top  face rides the upper bed and its bottom face rides the lower bed (sheared to the dip), so no  block crosses a fracture and the layout follows the real bed dip. Oblique off = flat boxes. |
| Class (`C`) | Text | list | Catalogue class (A/B/C/D...) of each block, aligned to Blocks. |
| Volume (`V`) | Number | item | Total recovered block volume (m3). |
| Net Value (`Net`) | Number | item | Net value (USD) = block sale price - diamond-saw cut cost. |
| Count (`N`) | Integer | item | Number of blocks placed. |
| Report (`Rpt`) | Text | item | Mix + economics + per-layer summary. |

Related:
- Frahan > Quarry > GPR Fracture Surfaces 3D - Source of the bed surfaces this lays blocks between.
- Frahan > Quarry > Fracture Block Pack - Uniform-block guillotine packer; this one is the priced multi-size CATALOGUE layout.

### BlockCutOpt AMRR Plan  (`BCOAmrr`)

- GUID: `F2D0BC03-1234-4F2D-A0B0-7E60CADA15A3`  |  icon: `QuarryCutOpt.png`  |  exposure: `tertiary`  |  source: `src/Frahan.StonePack.GH/BlockCutOptComponents.cs`
- Algorithm: **AMRR in-block plane-sequence cutting** - Shao, Liu, Gao 2022, AMRR in-block plane-sequence cutting strategy, Processes (MDPI)
- Plan a sequence of plane cuts (Shao 2022) that reduces the  starting block toward a target bounding sphere. Maximises  the average material removal rate. Implements AMRR in-block plane-sequence cutting (Shao 2022).

| in | type | access | description |
|---|---|---|---|
| Blank Block (`B`) | Box | item | Starting block (m). |
| Target Center (`C`) | Point | item | Target sphere centre. |
| Target Radius (`R`) | Number | item | Target sphere radius (m). |
| Sawblade Radius (mm) (`SBR`) | Number | item | Sawblade radius in mm. |
| Feed Speed (mm/min) (`FS`) | Number | item | Feed speed in mm/min. |
| Max Cuts (`MC`) | Integer | item | Iteration cap. |

| out | type | access | description |
|---|---|---|---|
| Cut Planes (`P`) | Plane | list | Sequence of cutting planes. |
| Removed Volume (m^3) (`V`) | Number | list | Removed volume per step. |
| Cutting Time (min) (`T`) | Number | list | Cutting time per step. |
| Material Removal % (`MRP`) | Number | item | Overall material removal percentage. |
| AMRR (mm^3/min) (`AMRR`) | Number | item | Average material removal rate. |

### BlockCutOpt Extract Grid  (`BCOExtract`)

- GUID: `F7A13001-0001-4F2D-A0B0-7E60CADA17C1`  |  icon: `BlockCutOpt.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/QuarryBridgeComponents.cs`
- Brute-force search + extract the winning OrientedBlock grid.  Outputs the non-intersected blocks as Rhino Boxes plus the  BlockCutOptResult headline numbers.

| in | type | access | description |
|---|---|---|---|
| Tested Area (`A`) | Box | item | Bench bounding box (m). |
| Fractures (`F`) | Mesh | item | Fracture mesh. |
| Block X (`Lx`) | Number | item | Block length (m). |
| Block Y (`Ly`) | Number | item | Block width (m). |
| Block Z (`Lz`) | Number | item | Block height (m). |
| Kerf (`K`) | Number | item | Material-lost-by-quarrying (m). |
| Psi Step (deg) (`Pdeg`) | Number | item | Angular search step. |
| Dx Max (`Dx`) | Number | item | Half-range of dx (m). |
| Dx Step (`DxS`) | Number | item | Dx step (m). |
| Dy Max (`Dy`) | Number | item | Half-range of dy (m). |
| Dy Step (`DyS`) | Number | item | Dy step (m). |

| out | type | access | description |
|---|---|---|---|
| Boxes (`B`) | Box | list | Non-intersected blocks as Rhino Boxes. |
| Count (`N`) | Integer | item | Number of non-intersected blocks. |
| Recovery % (`R`) | Number | item | Recovery percentage. |
| Best Psi (deg) (`Psi`) | Number | item | Optimum cutting direction. |
| Best Dx (m) (`Dx`) | Number | item | Optimum dx. |
| Best Dy (m) (`Dy`) | Number | item | Optimum dy. |
| Elapsed (ms) (`T`) | Number | item | Wall-clock duration. |

### BlockCutOpt Load Fractures  (`BCOLoadFx`)

- GUID: `F2D0BC01-1234-4F2D-A0B0-7E60CADA15A1`  |  icon: `DefectMap.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/BlockCutOptComponents.cs`
- Load fractures from disk (PLY, CSV, .lines, .txt). World  coordinates in metres. For 2D-trace formats, zMin / zMax  define the vertical extrusion range. Output is a Rhino  Mesh consumable by BlockCutOpt Solve.

| in | type | access | description |
|---|---|---|---|
| Path (`P`) | Text | item | File path. .ply / .csv / .lines / .txt |
| Z Min (`Zmin`) | Number | item | Bottom of vertical extrusion (m). Ignored for PLY. |
| Z Max (`Zmax`) | Number | item | Top of vertical extrusion (m). Ignored for PLY. |

| out | type | access | description |
|---|---|---|---|
| Fractures (`F`) | Mesh | item | Rhino Mesh of fracture triangles. |
| Triangle Count (`N`) | Integer | item | Number of fracture triangles. |

### BlockCutOpt Omni Solve  (`BCOOmni`)

- GUID: `F2D0BC04-1234-4F2D-A0B0-7E60CADA15A4`  |  icon: `BlockCutOpt.png`  |  exposure: `tertiary`  |  source: `src/Frahan.StonePack.GH/BlockCutOptComponents.cs`
- Algorithm: **BlockCutOpt omni-solve (Pareto over recovery/revenue/risk/cut-area)** - Elkarmoty Bondua Bruno 2020, Resources Policy 68:101761
- Run the omni-solver: uniform (mx, my) sub-division per zone,  4-axis Pareto multi-objective (recovery, revenue, kerf-time,  BCSdbBV). Returns one row per zone. Implements BlockCutOpt omni-solve (Elkarmoty 2020; Jalalian 2023).

| in | type | access | description |
|---|---|---|---|
| Tested Area (`A`) | Box | item | Bench bounding box (m). |
| Fractures (`F`) | Mesh | item | Fracture mesh. |
| Mx (`Mx`) | Integer | item | Sub-divisions in X. |
| My (`My`) | Integer | item | Sub-divisions in Y. |
| Block X (`Lx`) | Number | item | Block length (m). |
| Block Y (`Ly`) | Number | item | Block width (m). |
| Block Z (`Lz`) | Number | item | Block height (m). |
| Kerf (`K`) | Number | item | Material-lost-by-quarrying (m). |
| Psi Step (deg) (`Pdeg`) | Number | item | Angular search step. |
| Run (`R`) | Boolean | item | Execute the solve (the search is expensive; bound it before running) |

| out | type | access | description |
|---|---|---|---|
| Zone Id (`Z`) | Text | list | Sub-zone identifier (i, j). |
| Best Recovery Count (`N`) | Integer | list | Best recovery count per zone. |
| Best Revenue (`Pi`) | Number | list | Best revenue per zone. |
| Best BCSdbBV (`BCS`) | Number | list | Best BCSdbBV cost per zone. |
| Best Psi (deg) (`Psi`) | Number | list | Recovery-optimal psi per zone. |
| Aggregate Recovery (`R`) | Integer | item | Sum of recovery counts. |

### BlockCutOpt Solve  (`BCOSolve`)

- GUID: `F2D0BC02-1234-4F2D-A0B0-7E60CADA15A2`  |  icon: `BlockCutOpt.png`  |  exposure: `tertiary`  |  source: `src/Frahan.StonePack.GH/BlockCutOptComponents.cs`
- Algorithm: **BlockCutOpt brute-force search** - Elkarmoty Bondua Bruno 2020, Resources Policy 68:101761
- Brute-force search for the optimum cutting direction +  displacement that maximises the count of non-intersected  blocks. All units in metres. [Elkarmoty et al. 2020]

| in | type | access | description |
|---|---|---|---|
| Tested Area (`A`) | Box | item | Bench bounding box (m). |
| Fractures (`F`) | Mesh | item | Fracture mesh. |
| Block X (`Lx`) | Number | item | Block length (m). |
| Block Y (`Ly`) | Number | item | Block width (m). |
| Block Z (`Lz`) | Number | item | Block height (m). |
| Kerf (`K`) | Number | item | Material-lost-by-quarrying (m). |
| Psi Step (deg) (`Pdeg`) | Number | item | Angular search step. |
| Dx Max (`Dx`) | Number | item | Half-range of dx search (m). |
| Dx Step (`DxS`) | Number | item | Dx step (m). |
| Dy Max (`Dy`) | Number | item | Half-range of dy search (m). |
| Dy Step (`DyS`) | Number | item | Dy step (m). |
| Run (`R`) | Boolean | item | Execute the solve (the search is expensive; bound it before running) |

| out | type | access | description |
|---|---|---|---|
| Non-Intersected Count (`N`) | Integer | item | Best non-intersected block count. |
| Recovery % (`R`) | Number | item | Recovery percentage. |
| Best Psi (deg) (`Psi`) | Number | item | Optimum cutting direction. |
| Best Dx (m) (`Dx`) | Number | item | Optimum dx. |
| Best Dy (m) (`Dy`) | Number | item | Optimum dy. |
| Evaluations (`E`) | Integer | item | Total (psi, dx, dy) samples evaluated. |
| Elapsed (ms) (`T`) | Number | item | Wall-clock duration. |

### Box To Mesh  (`Box2Mesh`)

- GUID: `D3E4F5A6-3004-4F5E-A6B7-C8D9E0F12345`  |  icon: `QuarryBlock.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Quarry/BoxToMeshComponent.cs`
- Convert a Box (e.g. a BlockCutOpt output) into a closed  Mesh (8 vertices, 12 triangles). Bridges the Box->Mesh  adapter gap between BlockCutOpt and SlabFromMesh /  SlabCutByFractures / AshlarPack.

| in | type | access | description |
|---|---|---|---|
| Box (`B`) | Box | item | Input Box. Typically a single Box from BlockCutOpt's  Boxes output (graft, list-item, or as a single item). |

| out | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Closed mesh of the box. 8 vertices, 12 triangles. |

### Clean Scan Mesh  (`CleanTIN`)

- GUID: `A7E0B0F1-0C0F-4A16-9E3D-0FACE0FACE03`  |  icon: `QuarryBlock.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Quarry/CleanScanMeshComponent.cs`
- Algorithm: **TIN border-peel terrain cleanup (median-edge / verticality / cap-angle + component size)** - Fade2D land-survey peelOffIf; scale-relative thresholds (GeometryNumerics T2)
- Peel long 'cap' triangles, near-vertical gap webs and slivers from a reconstructed  scan mesh, then drop tiny disconnected islands. Thresholds are relative to the median  edge length, so it works at any survey scale. Wraps Core TinPeelFilter (card A2).

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Reconstructed scan mesh to clean. |
| Long Edge k (`k`) | Number | item | Remove border triangles whose longest edge exceeds k * median edge (3 = aggressive, 10 = careful). |
| Max Tilt (`T`) | Number | item | Remove near-vertical border facets steeper than this (deg). Default 85. |
| Max Cap Angle (`A`) | Number | item | Remove border facets whose angle opposite the border edge exceeds this (deg). Default 140. |
| Min Component (`N`) | Integer | item | Drop connected components smaller than this many triangles. Default 50. |

| out | type | access | description |
|---|---|---|---|
| Clean Mesh (`M`) | Mesh | item | Peeled mesh (kept triangles only). |
| Peeled (`P`) | Integer | item | Triangles removed by the peel predicate. |
| Size Dropped (`S`) | Integer | item | Triangles dropped as tiny components. |
| Report (`Rpt`) | Text | item | Summary. |

Related:
- Frahan > Quarry > Overburden To Rock Face - Cleaned ground TIN feeds the overburden volume.
- Frahan > Ingest > Scan Reconstruct - Cleans the over-triangulated reconstruction output.

### Construct GPR Preset  (`GprPreset`)

- GUID: `A7E0B0F6-0C0F-4A16-9E3D-0FACE0FACE07`  |  icon: `GprIngest.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Quarry/GprPresetComponents.cs`
- Algorithm: **Constructs a GPR preset: velocity (or eps_r), frequency, energy + continuity gates** - EM velocity v = c/sqrt(eps_r), c = 0.2998 m/ns; depth = v*t/2. Continuity gate per USGS Mirror Lake WRIR 99-4018C.
- Build a custom GPR ingestion preset for ANY stone / antenna (the library ships only two  empirically tuned presets, marble_600 and granite_160). Set the EM velocity (or a relative  permittivity to derive it), the antenna frequency, and the reflector detection + continuity  gates. Wire the output into GPR Survey Grid > Custom Preset to override the named preset.  Tip: a stone sold as 'marble' may be a compact limestone - match the velocity/frequency to the  survey, not the trade name.

| in | type | access | description |
|---|---|---|---|
| Stone (`St`) | Text | item | Stone / material label (e.g. limestone, marble, granite, travertine). custom |
| Frequency (`f`) | Integer | item | Antenna centre frequency (MHz). Sets the lambda/4 resolution. |
| Velocity (`v`) | Number | item | EM velocity (m/ns); depth = v*t/2. If <= 0 it is derived from Eps_r.  Marble/limestone ~0.10, granite ~0.12, travertine ~0.11. |
| Eps_r (`Er`) | Number | item | Relative permittivity. Used to derive Velocity when Velocity <= 0  (v = 0.2998/sqrt(Eps_r)); otherwise Eps_r is recomputed from Velocity for consistency. |
| Energy Quantile (`Q`) | Number | item | Reflector detection threshold (0..1) on the Hilbert energy;  higher keeps only the strongest reflectors. Marble/granite empirical ~0.985. |
| Continuity Traces (`Ct`) | Integer | item | Reflector continuity gate in traces: a reflector must  persist this many traces to be kept. Marble veins are short (~27 traces ~0.65 m); granite shear zones  longer (~41 traces ~1 m). |
| Migrate (`Mig`) | Boolean | item | f-k (Stolt) migration on each line (repositions dipping reflectors). |

| out | type | access | description |
|---|---|---|---|
| Preset (`Pr`) | Generic | item | Constructed GPR preset. Wire into GPR Survey Grid > Custom Preset. |

Related:
- Frahan > Quarry > GPR Survey Grid - Wire the constructed preset into Custom Preset to ingest a stone the two built-in presets do not cover.

### Convex Hull Slab  (`HullSlab`)

- GUID: `ECFDAEBF-CADB-4234-5678-9012345678AB`  |  icon: `ConvexHull2D.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Quarry/ConvexHullSlabComponent.cs`
- Algorithm: **QuickHull convex hull** - Barber, Dobkin, Huhdanpaa 1996, The Quickhull algorithm for convex hulls, ACM TOMS 22(4):469-483
- Builds the convex hull of a Rhino mesh's vertices and emits  the hull as a Slab. Loses concavity by definition; opt in  for fast Mesh -> Slab on roughly-convex inputs. Implements QuickHull (Barber-Dobkin-Huhdanpaa 1996).

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Rhino mesh whose vertices seed the hull. At least 4 non-coplanar vertices required. |

| out | type | access | description |
|---|---|---|---|
| Slab (`S`) | Generic | item | Convex-hull Slab. |
| Mesh (`M`) | Mesh | item | Convex hull as a Rhino Mesh (same geometry, fan-triangulated). |

### Discontinuity Ingest  (`DiscIn`)

- GUID: `D5F10049-ED9E-4ED9-A049-ED9EED9E0049`  |  icon: `DiscontinuitySets.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Quarry/DiscontinuityIngestComponent.cs`
- Algorithm: **Discontinuity ingest** - ISRM Suggested Methods (Brown 1981) dip/dip-direction; trace->plane TLS PCA
- Read mapped structural discontinuities (joints / faults / bedding / measured planes / digitised  traces) from a vector file (.csv / .geojson / .dxf / .shp) into Rhino planes + trace curves +  per-feature dip / dip-direction / set id. The ingest twin of Discontinuity Sets (Async).  Bad rows are skipped with a warning.

| in | type | access | description |
|---|---|---|---|
| File (`F`) | Text | item | Path to a discontinuity vector file: .csv (dip,dipdir[,x,y,z] | nx,ny,nz[,x,y,z] | a,b,c,d),  .geojson, .dxf (LINE / LWPOLYLINE / POLYLINE / 3DFACE), or .shp. |
| Origin (`O`) | Point | item | Optional local offset added to every parsed coordinate (e.g. to bring a UTM survey back to a  Rhino-friendly origin). Default (0,0,0). |

| out | type | access | description |
|---|---|---|---|
| Planes (`P`) | Plane | list | One plane per oriented feature (origin = centroid, normal = lower-hemisphere pole). |
| Traces (`T`) | Curve | list | Digitised trace polylines (DXF / GeoJSON / SHP lines). |
| Dip (`D`) | Number | list | Per-oriented-feature dip (deg, 0..90). |
| Dip dir (`Dd`) | Number | list | Per-oriented-feature dip-direction (deg, 0..360). |
| Set id (`S`) | Integer | list | Per-oriented-feature set id (-1 if the file did not classify it). |
| Report (`Re`) | Text | item | Counts, CRS, and any skipped-row warnings. |

Related:
- Frahan > Quarry > Discontinuity Sets (Async) - Discovers joint sets from a scan; this ingests measured ones.
- Frahan > Quarry > Joint Set - Author a single set by hand instead of reading a file.

### Discontinuity Sets (Async)  (`DiscSetsA`)

- GUID: `D5F10048-ED9E-4ED9-A048-ED9EED9E0048`  |  icon: `DiscontinuitySets.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/DiscontinuitySetsAsyncComponent.cs`
- Algorithm: **Planar-facet extraction** - FACETS (Dewez et al. 2016); Frahan clean-room C++ worker
- Lag-free, 10M-capable point-cloud -> joint sets. Runs a clean-room out-of-process worker on a  background task (canvas never blocks). Feed a PointCloud or a .ply path. Outputs the cloud coloured  by joint set + per-set dip / dip-direction / spacing.

| in | type | access | description |
|---|---|---|---|
| Cloud (`C`) | Geometry | item | Point cloud (PointCloud / mesh vertices). Optional if File is given. |
| File (`F`) | Text | item | Path to a .ply cloud (use for very large clouds instead of Cloud). |
| K (`K`) | Integer | item | Neighbours for PCA normals. |
| Max angle (`A`) | Number | item | Region-grow normal agreement (deg). |
| Min facet pts (`Mp`) | Integer | item | Minimum points per facet. |
| Bandwidth (`Bw`) | Number | item | Mean-shift angular bandwidth (deg). |
| Min set facets (`Ms`) | Integer | item | Minimum facets per joint set. |
| Max points (`Mx`) | Integer | item | Work budget; clouds larger than this are stride-downsampled. Runs off-process so the canvas never blocks -- higher resolves more joint sets (6M ~ 10 s, full 8M ~ 15 s). |
| Run (`R`) | Boolean | item | Set true to segment (runs off-process, async). |
| Keep facets (`Kf`) | Boolean | item | Copy the worker's facets.csv (per-facet pole + set id) to a stable path and expose it on the 'Facets path' output, for the Stereonet + Block Size card. |

| out | type | access | description |
|---|---|---|---|
| Segmented (`S`) | Geometry | item | Cloud coloured by joint set (unassigned = grey). |
| Set poles (`P`) | Line | list | A pole line per joint set through the cloud centroid. |
| Dip (`D`) | Number | list | Per-set dip (deg). |
| Dip dir (`Dd`) | Number | list | Per-set dip-direction (deg). |
| Spacing (`Sp`) | Number | list | Per-set mean normal spacing. |
| Facets/set (`Nf`) | Integer | list | Per-set facet count. |
| Report (`Re`) | Text | item | Summary + timings. |
| Share (`Sh`) | Number | list | Per-set fraction of facet points (set dominance). |
| Facets path (`Fp`) | Text | item | Path to the copied facets.csv (empty unless 'Keep facets' is true). Feed the Stereonet + Block Size card. |

Related:
- Frahan > Quarry > Discontinuity Sets (Cloud) - The in-process managed twin (small clouds).
- Frahan > Quarry > BlockCutOpt Solve - Consumes the discontinuity model.

### Discontinuity Sets (Cloud)  (`DiscSets`)

- GUID: `D5F10047-ED9E-4ED9-A047-ED9EED9E0047`  |  icon: `DiscontinuitySets.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/DiscontinuitySetsComponent.cs`
- Algorithm: **Planar-facet extraction** - FACETS (Dewez, Girardeau-Montaut et al. 2016); Frahan managed port
- Extract planar facets from a rock-face point cloud and cluster their poles into joint sets  (managed FACETS + DSE). Outputs the cloud coloured by joint set plus per-set dip / dip-direction /  spacing. Subsample very large clouds first.

| in | type | access | description |
|---|---|---|---|
| Cloud (`C`) | Geometry | item | Rock-face point cloud (PointCloud, or points/mesh vertices). |
| K (`K`) | Integer | item | Neighbours for PCA normals. |
| Max angle (`A`) | Number | item | Region-grow normal agreement (deg). |
| Min facet pts (`Mp`) | Integer | item | Minimum points per facet. |
| Bandwidth (`Bw`) | Number | item | Mean-shift angular bandwidth for joint sets (deg). |
| Min set facets (`Ms`) | Integer | item | Minimum facets per joint set. |
| Run (`R`) | Boolean | item | Set true to segment. |

| out | type | access | description |
|---|---|---|---|
| Segmented (`S`) | Geometry | item | Cloud coloured by joint set (unassigned = grey). |
| Set poles (`P`) | Line | list | A pole line per joint set through the cloud centroid. |
| Dip (`D`) | Number | list | Per-set dip (deg). |
| Dip dir (`Dd`) | Number | list | Per-set dip-direction (deg). |
| Spacing (`Sp`) | Number | list | Per-set mean normal spacing. |
| Facets/set (`Nf`) | Integer | list | Per-set facet count. |
| Report (`Re`) | Text | item | Summary. |

Related:
- Frahan > Quarry > BlockCutOpt Solve - Consumes the discontinuity model this produces.
- Frahan > Ingest > Load E57 Cloud - Produces the point cloud this segments.

### Fracture Block Pack  (`FracBlockPack`)

- GUID: `A7E0B0F3-0C0F-4A16-9E3D-0FACE0FACE04`  |  icon: `BlockCutOpt.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Quarry/FractureBlockPackComponent.cs`
- Pack fixed-size dimension blocks into each fracture-bounded slab (bin): tree-pack  coarse subdivision of the AABB + irregular-boundary fit to the slab mesh. Reports  per-bin yield. Managed.

| in | type | access | description |
|---|---|---|---|
| Container Meshes (`C`) | Mesh | list | Fracture-bounded slab meshes (closed). Each is one BIN. From the split mesh bench /  fracture surfaces. |
| Block Length (`Lx`) | Number | item | Dimension-block length (m). |
| Block Width (`Ly`) | Number | item | Dimension-block width (m). |
| Block Height (`Lz`) | Number | item | Dimension-block height (m). |
| Kerf (`K`) | Number | item | Saw-cut gap between blocks (m). |
| Fracture Clearance (`Cl`) | Number | item | Extra inward margin (m) every block must keep from the fracture boundary. Set it to the  fracture position sigma (GPR Fracture Surfaces 3D) for uncertainty-safe blocks. Default 0. |
| Run (`R`) | Boolean | item | Compute the packing. |
| Uncertainty Safe (`US`) | Boolean | item | Toggle the deep-fracture safety allowance. FALSE = geometric yield (clearance ignored, the  optimistic number). TRUE = enforce the Fracture Clearance (wire it to the fracture sigma  from GPR Fracture Surfaces 3D) so no block sits within the measured GPR uncertainty of a  fracture -> uncertainty-safe yield. Default false. |
| Packer (`Pk`) | Integer | item | Packing strategy. 0 = fixed axis grid. 1 = best-of (6 orientations x grid phase). 2 =  combined multi-size on a global grid. 3 = VOXEL-DLBF: per-block deepest-bottom-left-first  placement on a lattice (each block lands independently, conforming to the wavy boundary --  adopted after a head-to-head where a Mosch-style voxel greedy beat the global grid). 4 =  VOXEL-DLBF + multi-size (max-yield mesh-bench algorithm, DEFAULT): per-block placement plus  the 1.0/0.66/0.5 marketable fill ladder, strict 8-corner irregular fit + kerf. Tops the  head-to-head vs Kim forest and the Mosch-style greedy, but is NOT guaranteed saw-separable.  5 = GUILLOTINE multi-size (MANUFACTURABLE): recursive full-span 3D guillotine so every block  is separable by edge-to-edge saw cuts; reports cutting-surface-area + cut count (Jalalian  I11 / saw-path cost). Trades a little yield for full manufacturability. |

| out | type | access | description |
|---|---|---|---|
| Blocks (`B`) | Mesh | list | Placed dimension-block meshes (closed boxes). |
| Bin Index (`Bi`) | Integer | list | Container/bin index of each placed block. |
| Block Count (`N`) | Integer | list | Blocks placed per bin. |
| Recovered Volume (`V`) | Number | list | Recovered block volume per bin (m^3). |
| Yield (`Y`) | Number | list | Recovered / intact volume per bin (0..1). |
| Report (`Rpt`) | Text | item | Per-bin yield summary. |

### Fracture Bounded Slabs  (`BedSlabs`)

- GUID: `A7E0B0F4-0C0F-4A16-9E3D-0FACE0FACE05`  |  icon: `Box2Mesh.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Quarry/FractureBoundedSlabsComponent.cs`
- Algorithm: **Fracture-bounded slabs by height-field stitch between single-valued kriged bed surfaces** - ordinary-kriging bed surfaces (Cressie 1993); guillotine bed-cut sequence (Gilmore and Gomory 1965)
- Cut a bench box into the closed inter-bed SLABS that FOLLOW the kriged fracture surfaces.  The beds are single-valued depth surfaces, so each slab is built by stitching the sampled  height fields of two consecutive beds (height-field stitch, no CGAL boolean): one slab per  gap between consecutive beds and the bench top/bottom. Each slab follows the wavy beds, so a  block packed inside it never crosses a fracture. Feed the slabs into Fracture Block Pack  (packer 5, staged guillotine) -> the paper's manufacturable bed-following layout.

| in | type | access | description |
|---|---|---|---|
| Bench (`A`) | Box | item | Bench bounding box (m). XY footprint + the Z range to slab. |
| Bed Surfaces (`F`) | Mesh | list | Kriged fracture bed surfaces (from GPR Fracture Surfaces 3D). Single-valued depth surfaces;  one slab is built per gap between consecutive beds. |
| Grid Res (`G`) | Integer | item | Stitch grid resolution along the longer footprint axis (the other axis scales to keep cells  near-square). Higher = finer wavy-bed fidelity. Default 26. |
| Keep-out (`K`) | Number | item | Inward Z margin (m) kept from each bed (the GPR position keep-out). Default 0. |

| out | type | access | description |
|---|---|---|---|
| Slabs (`S`) | Mesh | list | The closed fracture-bounded slab meshes, one per inter-bed layer (shallow -> deep). Feed into  Fracture Block Pack > Container Meshes. |
| Thickness (`T`) | Number | list | Mean thickness (m) of each slab, aligned to Slabs. |
| Report (`Rpt`) | Text | item | Per-slab summary. |

Related:
- Frahan > Quarry > GPR Fracture Surfaces 3D - Source of the kriged bed surfaces this slabs the bench by.
- Frahan > Quarry > Fracture Block Pack - Pack each fracture-bounded slab with the staged guillotine (mode 5).
- Frahan > Slab > Slab Cut By Tool Mesh (CGAL) - The CGAL boolean alternative for arbitrary (non-height-field) curved cutters.

### Frahan Algebraic Convex Polyhedron  (`AlgConv`)

- GUID: `F2D0BC15-1234-4F2D-A0B0-7E60CADA15B5`  |  icon: `CoacdDecompose.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/BlockCutOptIngestionComponents.cs`
- Algorithm: **Half-space intersection convex polyhedron** - Frahan-original
- Build a convex polyhedron from N half-space inequalities  Nx*x + Ny*y + Nz*z <= b (Zhang 2024 parity, synthesis I14).  Each parallel-list row defines one face's outward normal  and offset. Returns a triangulated Rhino Mesh. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| B (`B`) | Number | list | Right-hand side b per inequality. |
| Nx (`Nx`) | Number | list | Outward-normal X per inequality. |
| Ny (`Ny`) | Number | list | Outward-normal Y per inequality. |
| Nz (`Nz`) | Number | list | Outward-normal Z per inequality. |

| out | type | access | description |
|---|---|---|---|
| Polyhedron (`P`) | Mesh | item | Triangulated CPH as Rhino Mesh. |
| Vertex Count (`V`) | Integer | item | Vertex count. |
| Face Count (`F`) | Integer | item | Face count. |
| Volume (m^3) (`Vol`) | Number | item | Polyhedron volume. |

### Frahan BenchBlock Cut → Slabs  (`QCut`)

- GUID: `F7A13002-0001-4F2D-A0B0-7E60CADA17C2`  |  icon: `QuarryCutOpt.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/QuarryBridgeComponents.cs`
- Run BlockCutOpt per BenchBlock in the ExtractionPlan order  and emit the winning cut-grid as Slabs (Mesh form).  Closes the Layer 7 → Layer 5 / 6 handoff.

| in | type | access | description |
|---|---|---|---|
| Inventory (`Inv`) | Generic | item | QuarryInventory. |
| Plan (`P`) | Generic | item | ExtractionPlan (accepted blocks are cut in plan order). |
| Fractures (`F`) | Mesh | item | Fracture mesh. |
| Product X (m) (`Lx`) | Number | item | Dimension-block target X. |
| Product Y (m) (`Ly`) | Number | item | Dimension-block target Y. |
| Product Z (m) (`Lz`) | Number | item | Dimension-block target Z. |
| Kerf (m) (`K`) | Number | item | Saw kerf. |
| Psi Step (deg) (`Pdeg`) | Number | item | Angular search step. |
| Dx Max (`Dx`) | Number | item | Half-range of dx (m). |
| Dx Step (`DxS`) | Number | item | Dx step (m). |
| Dy Max (`Dy`) | Number | item | Half-range of dy (m). |
| Dy Step (`DyS`) | Number | item | Dy step (m). |

| out | type | access | description |
|---|---|---|---|
| Slabs (`S`) | Mesh | list | Per-BenchBlock cut slabs concatenated in plan order. Wire into Ashlar Pack. |
| Block Ids (`I`) | Text | list | BenchBlock id parallel to each slab. |
| Counts (`N`) | Integer | list | Slab count per BenchBlock (parallel to ExtractionPlan.Accepted). |
| Cut Results (`C`) | Generic | list | List of BenchBlockCutResult objects. |

### Frahan Billet Cutter  (`Billets`)

- GUID: `F7A14002-0001-4F2D-A0B0-7E60CADA17D2`  |  icon: `QuarryCutOpt.png`  |  exposure: `quarternary`  |  source: `src/Frahan.StonePack.GH/GeoCutAndGeoPackComponents.cs`
- Algorithm: **Axis-parallel kerf-aware slab sub-division** - Frahan-original
- Sub-divide slabs into billets along an axis at a target  billet width. Kerf-aware. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Slabs (`S`) | Mesh | list | Slab inventory (one mesh per slab). |
| Axis (`A`) | Integer | item | Billet axis: 0=X, 1=Y, 2=Z. |
| Billet Width (m) (`W`) | Number | item | Target billet width. |
| Kerf (m) (`K`) | Number | item | Saw kerf. |

| out | type | access | description |
|---|---|---|---|
| Billets (`B`) | Mesh | list | Billet meshes (one per cut piece). |
| Count (`N`) | Integer | item | Total billet count. |

### Frahan Block Candidate Generator  (`BCand`)

- GUID: `F7A15003-0001-4F2D-A0B0-7E60CADA17E3`  |  icon: `BlockCutOpt.png`  |  exposure: `quarternary`  |  source: `src/Frahan.StonePack.GH/GeoCutAndGeoPackComponents.cs`
- Algorithm: **Per-cell AABB block-candidate generator** - Frahan-original
- Emit one BlockCandidate per BlockCell using the cell's AABB  as the BenchBlock footprint. Also returns a QuarryInventory  ready for the Layer 7 Quarry Yield Estimator. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Block Graph (`Bg`) | Generic | item | BlockGraph from Frahan Block Graph. |
| Bench Id (`B`) | Text | item | Bench identifier for the QuarryInventory. bench-1 |
| Geology Grade (`G`) | Number | item | Per-cell geology grade 0..1. |
| Uncertainty Buffer (m) (`U`) | Number | item | Buffer applied to each candidate. |

| out | type | access | description |
|---|---|---|---|
| Inventory (`Inv`) | Generic | item | QuarryInventory for the Layer 7 pipeline. |
| Candidates (`C`) | Generic | list | List<BlockCandidate>. |
| Candidate Boxes (`Bx`) | Box | list | Footprint of each candidate as a Rhino Box. |
| Count (`N`) | Integer | item | Number of candidates. |

### Frahan Block Graph  (`BlkGraph`)

- GUID: `F7A15002-0001-4F2D-A0B0-7E60CADA17E2`  |  icon: `Voronoi.png`  |  exposure: `quarternary`  |  source: `src/Frahan.StonePack.GH/GeoCutAndGeoPackComponents.cs`
- Algorithm: **CrackGraph to BlockGraph partition** - Frahan-original
- Partition a bench (Box or Mesh) into BlockCells using a  CrackGraph. Each cell is a convex Slab; small cells are  dropped under Min Cell Volume. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Bench Mesh (`B`) | Mesh | item | Bench geometry (convex mesh). |
| Crack Graph (`G`) | Generic | item | CrackGraph from Frahan Crack Graph. |
| Min Cell Volume (m^3) (`Mv`) | Number | item | Cells below this volume are dropped. |

| out | type | access | description |
|---|---|---|---|
| Block Graph (`Bg`) | Generic | item | BlockGraph object. |
| Cells (`C`) | Mesh | list | One mesh per BlockCell. |
| Count (`N`) | Integer | item | Number of cells. |
| Total Volume (m^3) (`V`) | Number | item | Sum of cell volumes. |

### Frahan Crack Graph (manual)  (`CrkGraph`)

- GUID: `F7A15001-0001-4F2D-A0B0-7E60CADA17E1`  |  icon: `DefectMap.png`  |  exposure: `quarternary`  |  source: `src/Frahan.StonePack.GH/GeoCutAndGeoPackComponents.cs`
- Algorithm: **Crack-graph DTO builder** - Frahan-original
- Wrap a user-supplied list of FracturePlanes (and optional  confidences) as a CrackGraph for spec-08 downstream consumers. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Fracture Planes (`F`) | Generic | list | List<FracturePlane>. |
| Confidences (`C`) | Number | list | Per-plane confidence 0..1 (optional). |
| Ids (`I`) | Text | list | Per-plane ids (optional). |

| out | type | access | description |
|---|---|---|---|
| Crack Graph (`G`) | Generic | item | CrackGraph object. |
| Count (`N`) | Integer | item | Number of cracks. |

### Frahan Extraction Order Optimizer  (`QOrder`)

- GUID: `F7A11003-0001-4F2D-A0B0-7E60CADA17A3`  |  icon: `QuarryCutOpt.png`  |  exposure: `quarternary`  |  source: `src/Frahan.StonePack.GH/QuarryCutOptComponents.cs`
- Algorithm: **Weighted-sum greedy extraction-order sort** - Frahan-original
- Order BenchBlocks by score = w_yield*yield - w_risk*risk -  w_access*access. Blocks under min yield are skipped. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Inventory (`Inv`) | Generic | item | QuarryInventory. |
| Estimates (`E`) | Generic | list | BlockYieldEstimate list. |
| Yield Weight (`Wy`) | Number | item | Score weight on yield fraction. |
| Risk Weight (`Wr`) | Number | item | Score weight on fracture risk. |
| Access Weight (`Wa`) | Number | item | Score weight on access cost. |
| Min Yield (`My`) | Number | item | Yield fraction 0..1 below which a block is skipped. |
| Access Normaliser (`An`) | Number | item | Divisor for access cost. |

| out | type | access | description |
|---|---|---|---|
| Plan (`P`) | Generic | item | ExtractionPlan object. |
| Order Ids (`I`) | Text | list | Block ids in extraction order. |
| Scores (`S`) | Number | list | Score for each accepted block. |
| Skipped Ids (`Sk`) | Text | list | Block ids skipped (low yield). |
| Total Recoverable (m^3) (`Vr`) | Number | item | Sum of recoverable volumes. |
| Total Waste (m^3) (`Vw`) | Number | item | Sum of waste volumes. |

### Frahan GPR Radargram Reader  (`GprRead`)

- GUID: `F7A12001-0001-4F2D-A0B0-7E60CADA17B1`  |  icon: `GprIngest.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/QuarryIngestionComponents.cs`
- SUPERSEDED BY: GPR File Loader + GPR Fracture Extract, which read  vendor formats natively and run the validated processing chain.  Kept loadable for old canvases.  Read a Frahan-format GPR radargram (traces CSV + optional  picks CSV). Coordinates in metres. SEG-Y / DZT / RD3 must  be converted externally (RGPR).

| in | type | access | description |
|---|---|---|---|
| Id (`I`) | Text | item | Radargram identifier. scan-1 |
| Traces CSV (`T`) | Text | item | Path to traces CSV (x,y,dz,a0,a1,...). |
| Picks CSV (`P`) | Text | item | Path to picks CSV (x,y,depth,conf,label). Empty = none. |

| out | type | access | description |
|---|---|---|---|
| Radargram (`R`) | Generic | item | GprRadargram object. |
| Trace XY (`TXY`) | Point | list | One point per trace at (x, y, 0). |
| Pick Points (`Pk`) | Point | list | One point per pick at (x, y, -depth). |
| Pick Confidence (`C`) | Number | list | Confidence per pick (0..1). |
| Trace Count (`Nt`) | Integer | item | Number of traces. |
| Pick Count (`Np`) | Integer | item | Number of picks. |

Related:
- Frahan > Ingest > GPR File Loader - SUPERSEDED BY: GPR File Loader — native multi-format ingest (CSV / SEG-Y / RD3 / DT1 / DZT / IDS .dt), no external conversion needed.
- Frahan > Quarry > GPR Fracture Extract - SUPERSEDED BY: GPR Fracture Extract — full processing chain (f-k migration + Hilbert energy + continuity) with stone/frequency presets.

### Frahan GeoFractNet Inference  (`GFNInfer`)

- GUID: `F7A12002-0001-4F2D-A0B0-7E60CADA17B2`  |  icon: `DefectMap.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/QuarryIngestionComponents.cs`
- Load pre-computed GeoFractNet fracture predictions from CSV  and emit a BlockCutOpt-ready fracture Mesh clipped to a bench  AABB. Inference itself runs externally (net48 cannot host  PyTorch).

| in | type | access | description |
|---|---|---|---|
| CSV Path (`P`) | Text | item | Path to GeoFractNet predictions CSV. |
| Bench AABB (`B`) | Box | item | Bounding box to clip fracture planes to. |
| Min Confidence (`C`) | Number | item | Drop predictions below this confidence (0..1). |

| out | type | access | description |
|---|---|---|---|
| Fractures (`F`) | Mesh | item | Fracture mesh ready for BlockCutOpt. |
| Planes (`Pl`) | Plane | list | One Rhino Plane per fracture. |
| Confidence (`C`) | Number | list | Per-fracture confidence. |
| Set Id (`S`) | Integer | list | Per-fracture set id. |
| Triangle Count (`Nt`) | Integer | item | Triangles in the fracture mesh. |

### Frahan Heterogeneous Quarry Extraction  (`HeteroExt`)

- GUID: `F2D0BC19-1234-4F2D-A0B0-7E60CADA15B9`  |  icon: `QuarryBlock.png`  |  exposure: `quinary`  |  source: `src/Frahan.StonePack.GH/BlockCutOptHeterogeneousComponents.cs`
- Algorithm: **Heterogeneous quarry extraction pipeline** - Frahan-original
- Composite 4-step extraction pipeline: BlockCutOpt to find  the fracture-clean regions, then 3D DLBF mixed-size pack  (monuments + dimension stones + slabs) avoiding fractured  regions, plus optional MonumentInventory placement on a  fracture-derived BlockGraph. One component, four outputs  per stage. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Bench (`B`) | Box | item | Bench bounding box (m). |
| Fractures (`Fx`) | Mesh | item | Fracture mesh. |
| Prime Block X (`Plx`) | Number | item | Prime (max) block length (m) for BCO stage 1. |
| Prime Block Y (`Ply`) | Number | item | Prime block width (m). |
| Prime Block Z (`Plz`) | Number | item | Prime block height (m). |
| Kerf (`K`) | Number | item | Saw kerf (m). |
| Psi Step (deg) (`Pdeg`) | Number | item | Angular search step. |
| Catalogue Ids (`Cid`) | Text | list | DLBF catalogue ids. |
| Catalogue Widths (m) (`Cw`) | Number | list | DLBF widths. |
| Catalogue Depths (m) (`Cd`) | Number | list | DLBF depths. |
| Catalogue Heights (m) (`Ch`) | Number | list | DLBF heights. |
| Catalogue Revenues (`Cr`) | Number | list | DLBF revenues. |
| Grid Cell (m) (`Gc`) | Number | item | DLBF discretisation cell; 0 = min(W,D,H)/4. |
| Floor Only (`Fl`) | Boolean | item | True = pieces on bench floor (no stacking). |
| Monument Inventory (`Mon`) | Generic | item | Optional MonumentInventory (from MonInv) for stage 4. |
| Monument Grid (m) (`Mg`) | Number | item | Monument-placement grid stride. |

| out | type | access | description |
|---|---|---|---|
| Prime Boxes (`Pb`) | Box | list | Non-intersected cells at the prime block dim. |
| Prime Count (`Pn`) | Integer | item | Count of fracture-clean prime cells. |
| Prime Recovery % (`Pr`) | Number | item | BlockCutOpt recovery at the prime dim. |
| Best Psi (deg) (`Psi`) | Number | item | Optimal cutting direction. |
| Forbidden Boxes (`Fb`) | Box | list | Fracture-intersected cells (forbidden for DLBF). |
| Mixed Boxes (`Mb`) | Box | list | DLBF-placed mixed-size piece boxes. |
| Mixed Ids (`Mi`) | Text | list | Id of each DLBF piece. |
| Mixed Revenue (`Mr`) | Number | item | DLBF total revenue. |
| Mixed Volume (`Mv`) | Number | item | DLBF occupied volume (m^3). |
| Monument Boxes (`Mo`) | Box | list | Monument-placement AABBs (empty if no inventory). |
| Monument Ids (`Moi`) | Text | list | Monument ids in placement order. |
| Monument Count (`Mon`) | Integer | item | Total monuments placed. |
| Unplaced Monuments (`Mou`) | Text | list | Monuments that did not fit anywhere. |

Related:
- Frahan > Lab > Frahan Mixed-Size Block Pack - Standalone 2D DLBF mixed-size packer (F2D0BC17); the same engine this facade composes.
- Frahan > Quarry > Frahan Mixed-Size Block Pack 3D - Standalone 3D DLBF mixed-size packer (F2D0BC18); the same engine this facade composes.
- Frahan > Quarry > BlockCutOpt Solve - Standalone stage-1 solver: optimum cutting direction + displacement (Elkarmoty 2020).

### Frahan Mesh → Fracture Planes  (`Mesh2FxPl`)

- GUID: `F7A13003-0001-4F2D-A0B0-7E60CADA17C3`  |  icon: `DefectMap.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/QuarryBridgeComponents.cs`
- Algorithm: **Mesh-face to fracture-plane conversion** - Frahan-original
- Convert a hand-drawn Rhino Mesh into a List<FracturePlane>  consumable by Slab Cut By Fractures. One plane per face  (centroid + face normal). Lets you author fractures on the  Rhino canvas without going through a PLY file. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Rhino mesh whose faces become fracture planes. |
| Unitize Normals (`U`) | Boolean | item | Re-normalise face normals (recommended). |

| out | type | access | description |
|---|---|---|---|
| Fracture Planes (`F`) | Generic | list | List<FracturePlane> for Slab Cut By Fractures. |
| Rhino Planes (`Pl`) | Plane | list | Same fractures as Rhino Planes for preview. |
| Count (`N`) | Integer | item | Number of fracture planes. |

### Frahan Mixed-Size Block Pack 3D  (`BCOMixedPack3D`)

- GUID: `F2D0BC18-1234-4F2D-A0B0-7E60CADA15B8`  |  icon: `BinPack.png`  |  exposure: `quinary`  |  source: `src/Frahan.StonePack.GH/BlockCutOptHeterogeneousComponents.cs`
- Algorithm: **Deepest-Left-Bottom-Fill (3D)** - Chehrazad, Roose, Wauters 2025, Int. J. Production Research 63:6606-6629
- 3D generalisation of DLBF (Chehrazad 2025). Each piece has  its own (Width, Depth, Height); pieces sort by revenue-per- volume. Floor-only mode (default) places every piece at  z = bench.MinZ, matching quarry extraction where blocks are  cut OUT of solid rock (no stacking). Disable Floor-Only for  monument storage / slab racking / container loading. Implements Deepest-Left-Bottom-Fill 3D (Chehrazad 2025).

| in | type | access | description |
|---|---|---|---|
| Tested Area (`A`) | Box | item | Bench bounding box (m). |
| Piece Ids (`Id`) | Text | list | One id per catalogue entry. |
| Piece Widths (m) (`W`) | Number | list | Width per entry (X). |
| Piece Depths (m) (`D`) | Number | list | Depth per entry (Y). |
| Piece Heights (m) (`H`) | Number | list | Height per entry (Z). |
| Piece Revenues (`Rev`) | Number | list | RMV per entry. |
| Forbidden Boxes (`X`) | Box | list | Optional forbidden regions (e.g. fracture-intersected cells). |
| Grid Cell (m) (`Gc`) | Number | item | Discretisation cell; 0 = min(W,D,H)/4. |
| Floor Only (`Fl`) | Boolean | item | True = pieces sit on bench floor (no stacking). |

| out | type | access | description |
|---|---|---|---|
| Placed Boxes (`B`) | Box | list | One Box per placed piece. |
| Placed Ids (`I`) | Text | list | Id of each placed piece (multiplicity preserved). |
| Total Revenue (`Pi`) | Number | item | Sum of placed-piece revenues. |
| Occupied Volume (m^3) (`Vol`) | Number | item | Sum of placed-piece volumes. |
| Placed Count (`N`) | Integer | item | Number of placements. |

### Frahan Photo Detect → PLY  (`Photo2Ply`)

- GUID: `F2D0BC14-1234-4F2D-A0B0-7E60CADA15B4`  |  icon: `PlyReader.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/BlockCutOptIngestionComponents.cs`
- v1 reads pre-detected fracture TRACES from a CSV (x1, y1, x2, y2  in world metres) and emits the vertical-extruded PLY consumable by  BlockCutOpt. The on-image fracture detector is not yet wired (the  Origin/GSD/Flip-Y inputs are placeholders for it). Pair with GFNInfer  to write the CSV from a GeoFractNet run, or hand-author the CSV from  QGIS / AutoCAD digitisation.

| in | type | access | description |
|---|---|---|---|
| CSV Path (`Csv`) | Text | item | Trace CSV file (x1, y1, x2, y2 in metres). |
| Origin X (m) (`Ox`) | Number | item | World X of pixel (0, 0). Unused by CSV backend. |
| Origin Y (m) (`Oy`) | Number | item | World Y of pixel (0, 0). Unused by CSV backend. |
| GSD (m/px) (`Gsd`) | Number | item | Ground sampling distance. Unused by CSV backend. |
| Z Min (m) (`Zmin`) | Number | item | Bottom of vertical extrusion. |
| Z Max (m) (`Zmax`) | Number | item | Top of vertical extrusion. |
| Flip Y (`Fy`) | Boolean | item | Pixel Y points down. Unused by CSV backend. |

| out | type | access | description |
|---|---|---|---|
| Fractures (`F`) | Mesh | item | Rhino Mesh of vertically-extruded fracture triangles. |
| Trace Count (`Tc`) | Integer | item | Number of traces parsed. |
| Triangle Count (`Tri`) | Integer | item | Number of triangles emitted. |
| Backend (`Bk`) | Text | item | Detector backend used. |

### Frahan Quarry Inventory  (`QInv`)

- GUID: `F7A11001-0001-4F2D-A0B0-7E60CADA17A1`  |  icon: `StockpileManager.png`  |  exposure: `quarternary`  |  source: `src/Frahan.StonePack.GH/QuarryCutOptComponents.cs`
- Aggregate a list of bench-block AABBs into a QuarryInventory.  All units in metres.

| in | type | access | description |
|---|---|---|---|
| Bench Id (`B`) | Text | item | Bench identifier. bench-1 |
| Block Boxes (`X`) | Box | list | Axis-aligned bench-block footprints (m). |
| Block Ids (`I`) | Text | list | Optional block ids. If empty, auto-generated. |
| Geology Grade (`G`) | Number | list | Per-block geology grade 0..1 (default 1.0). |
| Access Cost (`A`) | Number | list | Per-block access cost (default 0). |

| out | type | access | description |
|---|---|---|---|
| Inventory (`Inv`) | Generic | item | QuarryInventory object. |
| Count (`N`) | Integer | item | Number of blocks. |
| Total Volume (m^3) (`V`) | Number | item | Sum of gross volumes. |
| Avg Grade (`G`) | Number | item | Volume-weighted average geology grade. |

### Frahan Quarry Report  (`QRep`)

- GUID: `F7A11005-0001-4F2D-A0B0-7E60CADA17A5`  |  icon: `PackDiagnostics.png`  |  exposure: `quarternary`  |  source: `src/Frahan.StonePack.GH/QuarryCutOptComponents.cs`
- Aggregate Inventory + ExtractionPlan + SawBedSchedule into  a Markdown summary plus headline numbers.

| in | type | access | description |
|---|---|---|---|
| Inventory (`Inv`) | Generic | item | QuarryInventory. |
| Plan (`P`) | Generic | item | ExtractionPlan. |
| Schedule (`Sc`) | Generic | item | SawBedSchedule. |

| out | type | access | description |
|---|---|---|---|
| Report (`R`) | Generic | item | QuarryReport object. |
| Markdown (`MD`) | Text | item | Report rendered as Markdown. |
| Yield (m^3) (`Vy`) | Number | item | Total recoverable yield. |
| Waste (m^3) (`Vw`) | Number | item | Total waste. |
| Recovery % (`R%`) | Number | item | Overall recovery percent. |
| Makespan (min) (`M`) | Number | item | Schedule makespan. |

### Frahan Quarry Yield Estimator  (`QYield`)

- GUID: `F7A11002-0001-4F2D-A0B0-7E60CADA17A2`  |  icon: `YieldEstimator.png`  |  exposure: `quarternary`  |  source: `src/Frahan.StonePack.GH/QuarryCutOptComponents.cs`
- Per-block yield estimate via BlockCutOpt as a sub-routine.  Returns one BlockYieldEstimate per BenchBlock.

| in | type | access | description |
|---|---|---|---|
| Inventory (`Inv`) | Generic | item | QuarryInventory from Frahan Quarry Inventory. |
| Fractures (`F`) | Mesh | item | Fracture mesh (BlockCutOpt format). |
| Product X (m) (`Lx`) | Number | item | Dimension-block target X. |
| Product Y (m) (`Ly`) | Number | item | Dimension-block target Y. |
| Product Z (m) (`Lz`) | Number | item | Dimension-block target Z. |
| Kerf (m) (`K`) | Number | item | Saw kerf. |
| Psi Step (deg) (`Pdeg`) | Number | item | Angular search step. |
| Dx Max (`Dx`) | Number | item | Half-range of dx (m). |
| Dx Step (`DxS`) | Number | item | Dx step (m). |
| Dy Max (`Dy`) | Number | item | Half-range of dy (m). |
| Dy Step (`DyS`) | Number | item | Dy step (m). |
| Risk Normaliser (`Rn`) | Number | item | Fracture-triangle count divisor for risk 0..1. |

| out | type | access | description |
|---|---|---|---|
| Estimates (`E`) | Generic | list | BlockYieldEstimate per BenchBlock. |
| Block Ids (`I`) | Text | list | Block ids matching the estimates list. |
| Recovery % (`R`) | Number | list | Per-block recovery percent. |
| Fracture Risk (`Rf`) | Number | list | Per-block fracture risk 0..1. |
| Cutting Time (min) (`T`) | Number | list | Per-block estimated cutting time. |

### Frahan Saw-Bed Schedule  (`QSched`)

- GUID: `F7A11004-0001-4F2D-A0B0-7E60CADA17A4`  |  icon: `CncRoughing.png`  |  exposure: `quarternary`  |  source: `src/Frahan.StonePack.GH/QuarryCutOptComponents.cs`
- Algorithm: **Greedy LPT list scheduling** - Graham 1969, Bounds on multiprocessing timing anomalies, SIAM J. Appl. Math. 17(2):416-429
- Greedy LPT schedule of accepted blocks onto N saw beds.  Returns per-bed timelines and the total makespan. Implements greedy LPT scheduling (Graham 1969).

| in | type | access | description |
|---|---|---|---|
| Plan (`P`) | Generic | item | ExtractionPlan. |
| Bed Count (`N`) | Integer | item | Number of saw beds (>= 1). |
| Setup (min) (`S`) | Number | item | Fixed inter-block setup time per bed. |

| out | type | access | description |
|---|---|---|---|
| Schedule (`Sc`) | Generic | item | SawBedSchedule object. |
| Bed Summary (`BS`) | Text | list | One line per bed. |
| Makespan (min) (`M`) | Number | item | Schedule makespan. |
| Slot Count (`K`) | Integer | item | Total scheduled slots. |

### Frahan Slab Yield Optimizer  (`SlabYield`)

- GUID: `F7A14001-0001-4F2D-A0B0-7E60CADA17D1`  |  icon: `YieldEstimator.png`  |  exposure: `quarternary`  |  source: `src/Frahan.StonePack.GH/GeoCutAndGeoPackComponents.cs`
- Algorithm: **Per-block slab-plan yield maximisation** - Frahan-original spec 09 section 2 conflict-penalised yield score
- Pick the best SlabPlan (axis + thickness) for one block.  Enumerates three axis-aligned candidates at the given  thickness; score = yield - conflictPenalty * crackConflicts.

| in | type | access | description |
|---|---|---|---|
| Block (`B`) | Mesh | item | Convex block mesh. |
| Fracture Planes (`F`) | Generic | list | Optional List<FracturePlane>. Wire from Frahan Mesh → Fracture Planes. |
| Thickness (m) (`T`) | Number | item | Target slab thickness. |
| Kerf (m) (`K`) | Number | item | Saw kerf. |
| Conflict Penalty (`Cp`) | Number | item | Score penalty per aligned fracture inside the block. |
| Alignment Tol (deg) (`At`) | Number | item | Normal-axis alignment tolerance for conflict detection. |

| out | type | access | description |
|---|---|---|---|
| Best Plan (`P`) | Generic | item | SlabPlan with the highest score. |
| Axis (`A`) | Integer | item | 0=X, 1=Y, 2=Z. |
| Slab Count (`N`) | Integer | item | Slabs the block produces under this plan. |
| Yield Fraction (`Y`) | Number | item | slab_total_volume / block_volume. |
| Conflicts (`C`) | Integer | item | Crack conflicts counted. |
| Score (`S`) | Number | item | Yield − penalty × conflicts. |
| Cut Planes (`Cp`) | Generic | list | FracturePlanes that materialise the winning plan (feed Slab Cut By Fractures). |

### Frahan Synthetic TN Granite  (`TnGran`)

- GUID: `F2D0BC16-1234-4F2D-A0B0-7E60CADA15B6`  |  icon: `Stratigraphy.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/BlockCutOptIngestionComponents.cs`
- Algorithm: **Synthetic joint-set DFN generator** - ISRM Suggested Methods + Priest 1993 joint-set DFN
- Generate a deterministic synthetic discrete fracture network  for Tamil Nadu granite (three joint sets: NE-SW, NW-SE,  sub-horizontal bedding). Outputs a CSV of 2D traces at  z=midheight + a PLY of 3D fracture polygons + the fracture  Mesh in-process. Lets you regression-test BlockCutOpt  without a field dataset. Implements synthetic joint-set DFN generation (ISRM/Priest 1993; Goodman & Shi 1985).

| in | type | access | description |
|---|---|---|---|
| Bench (`B`) | Box | item | Bench bounding box (m). |
| Seed (`S`) | Integer | item | Reproducibility seed. |
| CSV Path (`Csv`) | Text | item | Output trace CSV path. |
| PLY Path (`Ply`) | Text | item | Output fracture-polygon PLY path. |
| Write Files (`W`) | Boolean | item | False = compute in memory only. |

| out | type | access | description |
|---|---|---|---|
| Fractures (`F`) | Mesh | item | In-process fracture Mesh (consumable by BCO components). |
| Plane Count (`Np`) | Integer | item | Number of fracture planes generated. |
| Trace Count (`Nt`) | Integer | item | Number of 2D traces at z=midheight. |
| Triangle Count (`Ntri`) | Integer | item | Number of triangles in the PLY. |
| CSV Written (`Co`) | Text | item | CSV file path actually written (empty when W=false). |
| PLY Written (`Po`) | Text | item | PLY file path actually written (empty when W=false). |

### GPR Bedrock Surface  (`GprBedrock`)

- GUID: `A7E0B0F1-0C0F-4A16-9E3D-0FACE0FACE04`  |  icon: `GprIngest.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Quarry/GprBedrockSurfaceComponent.cs`
- Algorithm: **GPR-to-bedrock surface: deepest continuous reflector + k-NN IDW resample onto a ground TIN** - Porsani 2006 / Isakova 2021 (top-of-fresh-rock reflector); Shepard 1968 (IDW); scale-relative radius (GeometryNumerics T2)
- Build a bedrock / rock-face-top surface mesh from GPR reflector picks. Takes the  deepest continuous reflector per column as bedrock, resamples its depth onto a ground  mesh's vertices (k-NN IDW), and outputs a bedrock mesh (ground topology, z = ground z -  depth) for Overburden To Rock Face. Wraps Core BedrockSurface + TinMerge (A9 + A3). [Porsani 2006]

| in | type | access | description |
|---|---|---|---|
| Ground (`G`) | Mesh | item | Ground / topographic TIN (supplies the (x,y) sample set + datum). |
| Picks (`P`) | Point | list | GPR reflector pick points. Depth is taken from -Z unless Depths is supplied. |
| Depths (`D`) | Number | list | Optional explicit depth (m) per pick (overrides -Z). Empty = use -Z. |
| Min Depth (`Dmin`) | Number | item | Ignore picks shallower than this (m) -- skip the weathered cover. Default 0. |
| Column Cell (`Cc`) | Number | item | Bin (x,y) to this cell (m) when reducing to one bedrock pick per column. 0 = exact. Default 0.25. |
| Neighbors (`k`) | Integer | item | k nearest picks for the IDW resample. Default 6. |

| out | type | access | description |
|---|---|---|---|
| Bedrock Mesh (`B`) | Mesh | item | Bedrock surface mesh (ground topology, z = ground z - interpolated depth) for Overburden To Rock Face. |
| Bedrock Points (`Bp`) | Point | list | Scattered bedrock points (deepest reflector per column). |
| Unresolved (`U`) | Integer | item | Ground vertices with no pick within range (clipped). |
| Report (`Rpt`) | Text | item | Summary. |

Related:
- Frahan > Quarry > GPR Fracture Extract - Source of the reflector picks (deepest = bedrock).
- Frahan > Quarry > Overburden To Rock Face - This bedrock mesh is its Bedrock input.
- Frahan > Quarry > Clean Scan Mesh - Clean the ground TIN first.

### GPR Fracture Extract  (`GprFracture`)

- GUID: `A7E0B0F1-0C0F-4A16-9E3D-0FACE0FACE02`  |  icon: `GprIngest.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Quarry/GprFractureExtractComponent.cs`
- Algorithm: **GPR fracture extraction: f-k (Stolt) migration + Hilbert instantaneous energy + USGS continuity** - Stolt 1978 (f-k migration); Taner 1979 (instantaneous attributes via Hilbert); USGS Mirror Lake WRIR 99-4018C (>=40-trace continuity); Porsani 2006 + Isakova 2021 (high-energy = fracture)
- Process a GPR radargram and extract fracture reflectors. Reads IDS .dt /  MALA .rd3 / GSSI .dzt / pulseEKKO .dt1 / SEG-Y / CSV. Runs dewow -> background  removal -> time-zero mute -> gain -> f-k (Stolt) migration -> Hilbert energy ->  USGS >=40-trace continuity extraction. Choose a STONE x FREQUENCY preset for  tuned defaults (marble_600, granite_160, ...); override any knob (set < 0 to use  the preset). Outputs fracture picks, depths, confidence, and a depth-converted  energy mesh. Reads Geoscanners .gsf natively (GsfReader).  Workflows cross-checked against RGPR (the open R GPR-processing package) in the companion paper.

| in | type | access | description |
|---|---|---|---|
| File (`F`) | Text | item | Path to the GPR radargram file (.dt / .rd3 / .dzt / .dt1 / .sgy / .csv). |
| Preset (`Pr`) | Text | item | Stone x frequency preset for tuned defaults:  marble_600, granite_160, travertine_390, andesite_390, limestone_200. granite_160 |
| Velocity (`v`) | Number | item | EM velocity (m/ns), depth = v*t/2. < 0 = use the preset value. Override with a  WARR/CMP-measured velocity when available (highest-leverage parameter). |
| Migrate (`Mig`) | Boolean | item | f-k (Stolt) migration to reposition dipping reflectors / collapse diffractions.  Leave unset to use the preset. |
| Depth Equalize (`Eq`) | Boolean | item | Per-depth energy normalisation so deep weak reflectors surface. Preset default. |
| Energy Quantile (`Q`) | Number | item | Energy quantile (0..1) above which a sample is a fracture candidate. < 0 = preset  (typically 0.985; lower it for broad CAVITY anomalies). |
| Continuity Traces (`C`) | Integer | item | USGS lateral-continuity window in traces (>= 40 keeps only continuous reflectors).  < 0 = preset (41). |
| Min Support (`S`) | Integer | item | Minimum like-picks within the continuity window to keep a pick. < 0 = preset (12). |
| Max Dip (`Dip`) | Number | item | USGS dip gate (deg): continuity is followed along reflector dips up to this angle;  steeper events are rejected. 45 = the USGS crystalline-rock standard. < 0 = default 45.  Raise toward 60 to keep steeper shear zones; lower toward 20 for sub-horizontal only. |
| Trace Mode (`Tm`) | Integer | item | How discrete picks are grouped into continuous fracture lines: 0 = connected-components  (simple, merges crossings), 1 = orientation-gated (separates crossing fractures by local  dip). Default 0. |
| Perm Uncertainty (`dEr`) | Number | item | Absolute uncertainty of the relative permittivity eps_r (e.g. 1.0 for eps_r 9+-1). Drives  the depth velocity error sigma_v/v = 0.5*dEr/eps_r, the dominant deep-fracture deviation.  Lower it (toward 0.3) when you have a CMP/WARR velocity calibration. Default 1.0. |
| Tolerance (`T`) | Number | item | Target tolerance T (m) for the confidence metric = probability each pick's depth is within  +-T of the truth (Gaussian, 1-sigma). Default 0.02 (2 cm precision-cutting). |

| out | type | access | description |
|---|---|---|---|
| Fracture Picks (`P`) | Point | list | Extracted fracture pick points at (distance, 0, -depth) in metres. |
| Depths (`D`) | Number | list | Depth (m) of each pick. |
| Confidence (`Cf`) | Number | list | Normalised energy (0..1) of each pick. |
| Energy Mesh (`E`) | Mesh | item | Depth-converted energy section as a mesh (x=distance, z=-depth), vertex-coloured  by instantaneous energy (blue=intact -> red=fracture). |
| Bedrock Depth (`Z`) | Number | item | Depth (m) of the deepest continuous reflector = candidate bedrock / rock-face top  (feeds Overburden To Rock Face). |
| Fracture Id (`Fid`) | Integer | list | Continuous-fracture id per pick (aligned to Fracture Picks; 0 = unassigned). Feed into  'GPR Fractures on Mesh' Labels to drape each fracture onto a bench/block mesh. |
| Fracture Lines (`L`) | Curve | list | Continuous fracture trace polylines in the section plane (x, 0, -depth), one per  reflector (FractureTracer). Extrude / loft these into fracture surfaces. |
| Report (`Rpt`) | Text | item | Parameters used + extraction summary. |
| Depth Sigma (`Ds`) | Number | list | Per-pick 1-sigma depth uncertainty (m), aligned to Fracture Picks: the GPR time->depth  deviation sqrt((depth*sigma_v/v)^2 + (lambda/4)^2). Grows with depth (velocity error)  off a lambda/4 resolution floor. Stage 1 of the GPR->fracture->mesh tolerance ladder. |
| Confidence within T (`Cf%`) | Number | item | OPTIMISATION METRIC: mean over the picks of P(|depth deviation| <= T) = erf(T/(sigma*sqrt2)).  0..1 (= the fraction of the fracture trace within +-T of truth). Raise it by calibrating  velocity (Perm Uncertainty down) or higher frequency. Section-level (no inter-line term). |

### GPR Fracture Surfaces 3D  (`GprFrac3D`)

- GUID: `A7E0B0F2-0C0F-4A16-9E3D-0FACE0FACE03`  |  icon: `Stratigraphy.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Quarry/GprFractureSurface3DComponent.cs`
- Cluster a multi-line GPR pick cloud into fractures, krige each into a 3D surface,  and colour it by the GPR->fracture->mesh deviation-from-truth (tolerance ladder).  Outputs the confidence-within-tolerance metric. Managed (C# kriging; no Python).

| in | type | access | description |
|---|---|---|---|
| Fracture Picks (`P`) | Point | list | 3D fracture-pick cloud across the survey (x = distance, y = line offset, z = -depth).  Combine the picks of several GPR section lines (e.g. from GPR Fracture Extract). |
| Num Fractures (`k`) | Integer | item | Number of fractures to cluster the picks into by depth. < 0 = auto (depth-gap split). |
| Grid Res (`G`) | Integer | item | Surface grid resolution per axis. Default 36. |
| Velocity (`v`) | Number | item | EM velocity (m/ns); depth = v*t/2. Default 0.1. |
| Frequency (`f`) | Number | item | Antenna centre frequency (MHz); sets lambda/4  vertical resolution. Default 600. |
| Eps_r (`Er`) | Number | item | Relative permittivity. Default 9. |
| Perm Uncertainty (`dEr`) | Number | item | Absolute eps_r uncertainty (e.g. 1.0 for 9+-1) -> velocity error sigma_v/v=0.5*dEr/eps_r  (the dominant deep-fracture deviation). Lower with a CMP/WARR calibration. Default 1.0. |
| Tolerance (`T`) | Number | item | Target tolerance T (m) for the confidence metric P(|deviation| <= T). Default 0.02. |
| Assume Open (`Op`) | Boolean | item | Treat the fractures as OPEN (fluid/air-filled) when scoring detectability. Surface GPR  mainly images OPEN fractures; sealed ones are largely missed (Molron 2020). Default true. |
| Time-Zero (`t0`) | Number | item | Direct-wave time-zero pick window (ns) -> rectangular sigma_t0=((t0)/2)/sqrt3 added to  sigma_recon (Xie 2021; dominant near the surface). 0 = off. Default 0. |
| Detect Base (`Pe`) | Number | item | STONE-SPECIFIC base imaging efficiency for detectability (0..1): the detected fraction  for ideal open sub-horizontal fractures. Crystalline/granite ~0.80-0.91 (Molron 2020 /  Dorn 2012, low loss); attenuating/clay-prone stone (marble, limestone) is lower. Default  0.80 (granite, MEASURED Molron 2020). Per-stone (GprDetectionCalibration): limestone 0.90,  sandstone 0.80, marble/travertine 0.75, andesite 0.50, tuff 0.38. ONLY stone-specific detection  knob; velocity/eps_r/frequency still set sigma_recon + the (now depth-aware) size floor. |
| Through Picks (`Xp`) | Boolean | item | EXACT interpolation: collapse each fracture's picks to one PEAK pick per cell (keep the  highest-energy reflector) and krige with a near-zero nugget so the surface passes THROUGH  every peak pick (posterior sigma ~0 at picks) and spans the full survey footprint as one  continuous dipping sheet. False = smoothing fit (the old behaviour). Default true. |
| Pick Energy (`En`) | Number | list | OPTIONAL per-pick energy/confidence (0..1), aligned to Fracture Picks (wire the Confidence  output of GPR Survey Grid / GPR Fracture Extract). With Through Picks on, the PEAK (highest-  energy) pick is kept per cell. Omit to keep the pick nearest the local trend. |
| Peak Dedup (`Dd`) | Boolean | item | K2 (default true): collapse each cell to its single PEAK reflector before kriging -> smoothest  sheet, lowest residual, rides the strong reflectors. False = K1: keep EVERY pick as a hard  constraint -> maximum fidelity to the raw cloud, marginally lower posterior sigma, but the  surface buckles where near-coincident picks disagree. Only applies when Through Picks is on. |

| out | type | access | description |
|---|---|---|---|
| Fracture Surfaces (`S`) | Mesh | list | One kriged 3D fracture surface mesh per fracture, vertex-coloured by the total  deviation-from-truth sigma (green <= T -> red). |
| Confidence (`Cf`) | Number | list | OPTIMISATION METRIC per fracture: mean over the surface of P(|deviation| <= T)  (0..1). |
| Mean Sigma (`Ds`) | Number | list | Mean total deviation sigma (m) per fracture surface. |
| Overall Confidence (`Cf*`) | Number | item | Area-mean confidence across all fractures (0..1) -- the single number to optimise. |
| Report (`Rpt`) | Text | item | Per-fracture tolerance-ladder summary. |
| Detectability (`Pd`) | Number | list | DETECTION rung per fracture (0..1): probability surface GPR images it, from its mean dip,  openness and area (Molron 2020 / Dorn 2012). Low = a fracture that may be MISSED. |
| Effective Confidence (`Ce*`) | Number | item | Detection-adjusted overall confidence = Overall Confidence x detection completeness.  Accounts for fractures that may be missed, not just mislocated. The honest yield-safety number. |

### GPR Fractures on Mesh  (`GprOverlay`)

- GUID: `F2D05A05-1A2B-4C3D-9E4F-5A6B7C8D9E05`  |  icon: `Downsample.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/GprFractureOverlayComponent.cs`
- Algorithm: **GPR reflector drape + fracture-sheet build** - Frahan-original: project reflector picks onto a target mesh; loft surface-to-reflector ribbons per fracture
- Overlay GPR reflector picks onto a target bench/block mesh: drape  each pick onto the surface, connect picks into per-fracture trace  curves, and (optionally) build fracture sheets from the surface down  to the reflector depth for use with Cut By Fractures / BlockCutOpt.  Feed Pick Points from GPR Radargram Mesh; put both in the same frame  with Move to Origin first.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Target bench / block mesh (same coordinate frame as the picks). |
| Picks (`P`) | Point | list | GPR reflector pick points (from GPR Radargram Mesh). |
| Labels (`L`) | Text | list | Optional fracture label per pick (groups picks into distinct fractures).  If absent or mismatched, all picks form one fracture. |
| Project (`X`) | Integer | item | How a pick maps to the mesh: 0 = closest point on mesh, 1 = drop along -Z,  2 = raise along +Z. Default 0. |
| Make Sheets (`S`) | Boolean | item | Also build fracture sheets (ribbons from the draped surface point down to the  reflector pick) as meshes, for Cut By Fractures / BlockCutOpt. Default false. |

| out | type | access | description |
|---|---|---|---|
| Draped (`Pd`) | Point | list | Picks projected onto the mesh surface. |
| Fracture Curves (`C`) | Curve | list | One polyline per fracture, on the mesh surface. |
| Fracture Sheets (`F`) | Mesh | list | Surface->reflector ribbon mesh per fracture (when Make Sheets). |
| Report (`R`) | Text | item | Summary. |

Related:
- Frahan > Ingest > GPR Radargram Mesh - Source of the reflector picks this overlays.
- Frahan > Cut > Cut By Fractures (CGAL) - Fracture-sheet output feeds the CGAL fracture cutter.
- Frahan > Quarry > BlockCutOpt Load Fractures - Fracture meshes are the BlockCutOpt fracture input.
- Frahan > Mesh > Move to Origin - Bring GPR picks + the bench mesh into one coordinate frame first.

### GPR Survey Grid  (`GprGrid`)

- GUID: `A7E0B0F0-0C0F-4A16-9E3D-0FACE0FACE01`  |  icon: `GprIngest.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Quarry/GprSurveyGridComponent.cs`
- Algorithm: **GPR survey-grid ingest: per-line f-k migration + Hilbert energy + USGS continuity, laid out by line offset** - Stolt 1978 (f-k migration); Taner 1979 (instantaneous attributes); USGS Mirror Lake WRIR 99-4018C (>=40-trace continuity)
- Ingest a whole GPR survey GRID in ONE component. Give it the LIST of scan-line files  (.dt / .rd3 / .dzt / .dt1 / .sgy / .csv) and a Line Spacing (or explicit Line Positions);  it runs the validated GPR chain (dewow -> background -> time-zero -> gain -> f-k migration  -> Hilbert energy -> USGS continuity) on each line and lays line i at y = position[i]  (default i*spacing). Outputs one 3D fracture-pick cloud (x=distance, y=line offset, z=-depth)  plus per-pick energy -- feed Picks -> 'GPR Fracture Surfaces 3D' Fracture Picks and Confidence  -> its Pick Energy. Replaces N single-line components + Merge.

| in | type | access | description |
|---|---|---|---|
| Files (`F`) | Text | list | The GRID of GPR scan-line files (one per survey line). .dt / .rd3 / .dzt / .dt1 / .sgy / .csv /  .gsf (Geoscanners Akula -- now read natively). |
| Preset (`Pr`) | Text | item | Stone x frequency preset for tuned defaults applied to every line:  marble_600, granite_160, travertine_390, andesite_390, limestone_200. granite_160 |
| Line Spacing (`Sp`) | Number | item | Distance (m) between consecutive parallel scan lines. Line i is placed at y = i * spacing.  Ignored where Line Positions supplies an explicit y. Default 2.0. |
| Line Positions (`Y`) | Number | list | OPTIONAL explicit y offset (m) per line, aligned to Files (use real survey line coordinates  when you have them). Overrides Line Spacing when its count matches the file count. |
| Velocity (`v`) | Number | item | EM velocity (m/ns), depth = v*t/2. < 0 = use the preset value. Override with a WARR/CMP- measured velocity when available (highest-leverage parameter). |
| Energy Quantile (`Q`) | Number | item | Energy quantile (0..1) above which a sample is a fracture candidate. < 0 = preset (~0.985). |
| Max Dip (`Dip`) | Number | item | USGS dip gate (deg). < 0 = default 45 (crystalline-rock standard). |
| Migrate (`Mig`) | Boolean | item | f-k (Stolt) migration on every line. Default true. |
| Orientation (`Ax`) | Integer | list | OPTIONAL per-line axis for a BIDIRECTIONAL grid: 0 = longitudinal (line runs along X, lines  stacked in Y), 1 = transverse / cross-line (runs along Y, stacked in X). Empty = auto-detect  from the filename (contains 'TA' -> transverse, else longitudinal); a single value applies to  all. With BOTH axes present the picks form a true crossing grid and each axis is spaced to fit  the other axis' extent (the cross-lines MEASURE the perpendicular dip instead of interpolating  it); with one axis it falls back to Line Spacing (parallel lines). |
| Custom Preset (`CPr`) | Generic | item | OPTIONAL constructed GPR preset (from 'Construct GPR Preset'). If provided, it OVERRIDES the named  Preset string -- use it for any stone/antenna the two built-in empirical presets do not cover. |

| out | type | access | description |
|---|---|---|---|
| Fracture Picks (`P`) | Point | list | One 3D fracture-pick cloud across the whole survey: (distance, line offset, -depth) in metres.  Wire into GPR Fracture Surfaces 3D > Fracture Picks. |
| Line Id (`Lid`) | Integer | list | Survey line index (0-based) of each pick. |
| Confidence (`Cf`) | Number | list | Normalised energy (0..1) of each pick. Wire into GPR Fracture Surfaces 3D > Pick Energy so the  PEAK reflector is kept per cell. |
| Depths (`D`) | Number | list | Depth (m) of each pick. |
| Energy Sections (`E`) | Mesh | list | Per-line depth-converted energy section meshes, each laid at its survey y (x=distance,  y=line offset, z=-depth), vertex-coloured by instantaneous energy. |
| Bedrock Depth (`Z`) | Number | list | Deepest continuous reflector (m) per line = candidate bedrock / rock-face top. |
| Report (`Rpt`) | Text | item | Per-line ingest summary. |

Related:
- Frahan > Quarry > GPR Fracture Surfaces 3D - Krige this multi-line pick cloud into 3D dipping bed surfaces.
- Frahan > Quarry > GPR Fracture Extract - Single-section twin; this one batches a whole survey grid.

### Joint Set  (`Joint`)

- GUID: `ECFDAEBF-CBDC-4345-6789-012345678BCD`  |  icon: `Stratigraphy.png`  |  exposure: `quinary`  |  source: `src/Frahan.StonePack.GH/Quarry/JointSetComponent.cs`
- Algorithm: **Joint-set DFN authoring** - ISRM Suggested Methods + Priest 1993 joint-set DFN
- Authors a structural-geology joint set: dip direction (azimuth  of steepest descent, 0 = North), dip angle, mean spacing along  the normal, optional orientation scatter. Wire into Quarry DFN. Implements joint-set DFN authoring (ISRM/Priest 1993).

| in | type | access | description |
|---|---|---|---|
| Dip Direction (`DD`) | Number | item | Azimuth of the steepest descent line, clockwise from North (+Y), in [0, 360). |
| Dip (`D`) | Number | item | Dip angle from horizontal, in [0, 90]. 0 = horizontal joint, 90 = vertical. |
| Spacing (`S`) | Number | item | Mean spacing along the normal (same units as the quarry block). > 0. |

| out | type | access | description |
|---|---|---|---|
| Joint Set (`J`) | Generic | item | JointSet DTO. Wire into Quarry DFN. |

### Joint Sets to DFN  (`Sets2DFN`)

- GUID: `D5F1004B-ED9E-4ED9-A04B-ED9EED9E004B`  |  icon: `DiscontinuitySets.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Quarry/JointSetsToDfnComponent.cs`
- Algorithm: **Joint-set DFN** - Priest 1993 discrete fracture network from joint-set statistics
- Bridge joint sets (dip / dip-direction / spacing) into a discrete fracture network mesh clipped  to a bench box, ready for the Block Cut Optimiser. Uses only the joint-set statistics, not the  scan mesh, so an incomplete scan still works. Deterministic by seed.

| in | type | access | description |
|---|---|---|---|
| Dip (`D`) | Number | list | Per-set dip (deg, 0..90). |
| Dip dir (`Dd`) | Number | list | Per-set dip-direction (deg, 0..360). |
| Spacing (`Sp`) | Number | list | Per-set mean normal spacing (cloud units). Sets with spacing<=0 are skipped. |
| Spacing scale (`Ss`) | Number | item | Multiplies spacing into the bench's units (e.g. 100 to take a cm-scale detail scan to bench metres). Default 1. |
| Bench (`B`) | Box | item | Bench / blank bounding box the DFN is clipped to (and that you also feed to the Block Cut Optimiser as Tested Area). |
| Scatter (`Sc`) | Number | list | Per-set orientation scatter (deg, Fisher dispersion). One value applies to all sets. Default 0 = planar. |
| Seed (`S`) | Integer | item | Random seed (deterministic given the same inputs). |
| Exp spacing (`E`) | Boolean | item | Negative-exponential spacing (Priest) instead of constant. |

| out | type | access | description |
|---|---|---|---|
| DFN (`F`) | Mesh | item | Fracture-network mesh (triangulated planes clipped to the bench). Feed to Block Cut Optimiser 'Fractures'. |
| Tested area (`A`) | Box | item | The bench box, passed through. Feed to Block Cut Optimiser 'Tested Area'. |
| Fractures (`N`) | Integer | item | Number of fracture planes clipped into the bench. |
| Sets used (`Su`) | Integer | item | Joint sets actually used (spacing>0, valid orientation). |
| Report (`Re`) | Text | item | Per-set summary + DFN stats + any skipped sets. |

Related:
- Frahan > Quarry > Discontinuity Sets (Async) - Upstream: discovers dip/dipdir/spacing from a scan.
- Frahan > Quarry > Discontinuity Ingest - Upstream: ingests measured dip/dipdir orientations.
- Frahan > Quarry > BlockCutOpt Omni Solve - Downstream (evolved): sub-division + coarse-to-fine + Pareto recovery on this DFN.
- Frahan > Quarry > Fracture Block Pack - Downstream: wire-saw staged guillotine packing against this DFN.

### Mesh Shell Split  (`ShellSplit`)

- GUID: `DBECFDAE-BFCA-4123-4567-89012345678A`  |  icon: `CoacdDecompose.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Quarry/MeshShellSplitComponent.cs`
- Algorithm: **Connected-components labelling** - Frahan-original
- Separates a multi-shell Rhino mesh into one Slab per  connected shell. Each output shell is assumed convex  (Slab's input requirement). Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Multi-shell Rhino mesh. |

| out | type | access | description |
|---|---|---|---|
| Slabs (`S`) | Generic | list | One Slab per connected shell. |
| Mesh (`M`) | Mesh | list | One Rhino Mesh per shell (parallel to the Slabs list). |

### Overburden To Rock Face  (`Overburden`)

- GUID: `A7E0B0F1-0C0F-4A16-9E3D-0FACE0FACE01`  |  icon: `QuarryBlock.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Quarry/OverburdenToRockFaceComponent.cs`
- Algorithm: **Cut-and-fill volume by TIN prism differencing** - Route-surveying prismoidal volume; difference triangulation (geom.at / Fade2D land survey)
- Soil volume to strip to reach the rock face: the volume between a GROUND  surface mesh and a BEDROCK surface mesh. Bedrock z is sampled vertically  under each ground vertex (common-TIN bridge); volume by exact TIN-prism  differencing (Core OverburdenVolume). Cut = soil to remove; Loose = swell- adjusted haul volume. 2.5D volume only -- get the 3D exposed face from Scan  Reconstruct for block extraction.

| in | type | access | description |
|---|---|---|---|
| Ground (`G`) | Mesh | item | Ground / topographic surface mesh (e.g. from Scan Reconstruct on a LiDAR /  photogrammetry cloud). Its triangulation is used as the common TIN. |
| Bedrock (`R`) | Mesh | item | Bedrock / rock-face surface mesh (e.g. reconstructed from GPR / ERT / seismic  depth picks). Sampled vertically under each ground vertex. |
| Swell (`Sw`) | Number | item | Swell fraction for the loose/haul volume (e.g. 0.25 = +25%). 0 = report bank volume only. |

| out | type | access | description |
|---|---|---|---|
| Overburden (bank) (`V`) | Number | item | Cut volume = soil above the bedrock surface, in model units^3 (bank / in-situ). |
| Loose (haul) (`L`) | Number | item | Swell-adjusted volume to haul = V*(1+Swell). |
| Fill (`F`) | Number | item | Volume where bedrock is ABOVE ground (rock already exposed / above the surface). |
| Net (`N`) | Number | item | Cut - Fill (signed). |
| Plan Area (`A`) | Number | item | Total projected (x,y) area covered by the common TIN. |
| Depth Mesh (`D`) | Mesh | item | Ground mesh vertex-coloured by overburden depth (blue=thin -> red=deep) for the visual pass. |
| Report (`Rpt`) | Text | item | Human-readable summary. |

### Quarry DFN  (`QuarryDFN`)

- GUID: `FDAEBFCA-DCED-4456-789A-CDEF01234567`  |  icon: `DefectMap.png`  |  exposure: `quinary`  |  source: `src/Frahan.StonePack.GH/Quarry/QuarryDfnComponent.cs`
- Algorithm: **Discrete Fracture Network block extraction** - ISRM Suggested Methods + Priest 1993 joint-set DFN
- Extracts dimension-stone blocks from a quarry mesh following  a Discrete Fracture Network defined by joint sets.  Geomechanically faithful (Priest 1993 / ISRM Suggested Methods). Implements DFN block extraction (ISRM/Priest 1993; Azarafza 2016).

| in | type | access | description |
|---|---|---|---|
| Quarry (`Q`) | Mesh | item | Convex quarry mesh (e.g. a cube of rock). |
| Joint Sets (`J`) | Generic | list | JointSet DTOs (from Joint Set component). |
| Seed (`Seed`) | Integer | item | Random seed (controls spacing offset within meanSpacing). |

| out | type | access | description |
|---|---|---|---|
| Blocks (`B`) | Mesh | list | Extracted block meshes. |
| Slabs (`S`) | Generic | list | Same blocks as Slab DTOs (for downstream Frahan plumbing). |

### Quarry Decompose  (`QuarryDc`)

- GUID: `B9CADBEC-FDAE-4F01-2345-678901234567`  |  icon: `QuarryCutOpt.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Quarry/QuarryDecomposeComponent.cs`
- Algorithm: **Orthogonal-grid slab decomposition** - Frahan-original
- Cuts a convex quarry Slab into a list of smaller convex Slabs  by an orthogonal grid of fracture planes. Output flows into  Ashlar Pack. Frahan-original method. Selection: convex pieces  -> By CoACD; plane-bounded cuts -> By Mesh (CGAL); cell  partition -> By Voronoi.

| in | type | access | description |
|---|---|---|---|
| Quarry (`Q`) | Generic | item | Convex quarry. Accepts a Frahan Slab DTO (from Slab From Mesh)  OR a Rhino Mesh (auto-converted). |
| nX (`nX`) | Integer | item | Grid count along +X (>= 0). |
| nY (`nY`) | Integer | item | Grid count along +Y (>= 0). |
| nZ (`nZ`) | Integer | item | Grid count along +Z (>= 0). |
| Eps (`eps`) | Number | item | Cutter floating-point tolerance. Must be >= 0. |

| out | type | access | description |
|---|---|---|---|
| Slabs (`S`) | Generic | list | Output Slab DTOs. Wire into Ashlar Pack. |
| Parents (`Pi`) | Integer | list | Per-output index back into the input list (always 0 for a  single-quarry call). |
| Mesh (`M`) | Mesh | list | Output Slabs as Rhino Meshes (parallel to the Slab list). |

### Quarry Decompose By CoACD  (`QuarryDcCoacd`)

- GUID: `F2D000E0-CADC-4F2D-A0E0-7E60CADA15A0`  |  icon: `CoacdDecompose.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/AdvancedQuarryDecomposeComponents.cs`
- Algorithm: **Collision-Aware Approximate Convex Decomposition** - Wei, Liu, Wang et al. 2022, Approximate Convex Decomposition for 3D Meshes with Collision-Aware Concavity and Tree Search, SIGGRAPH 2022
- Decomposes a quarry mesh into nearly-convex blocks via  CoACD (Wei et al, SIGGRAPH 2022). Concavity-driven — block  count and shape come from the input geometry, not a user  grid. Use when the goal is approximate convex pieces for  downstream packing or collision physics. Implements Collision-Aware Approximate Convex Decomposition (Wei 2022).  Selection: convex pieces -> By CoACD; plane-bounded cuts -> By Mesh (CGAL); cell partition -> By Voronoi.

| in | type | access | description |
|---|---|---|---|
| Quarry (`Q`) | Mesh | item | Quarry mesh. Must be 2-manifold for the lightweight CoACD  build (no OpenVDB preprocess); pre-clean with Mesh Repair  (CGAL) if needed. |
| Threshold (`Th`) | Number | item | Concavity threshold. Lower = more pieces, tighter fit.  Default 0.05. |
| Real Metric (`RM`) | Boolean | item | True = treat Threshold as metres rather than normalized  [0..1] units. Recommended for statue-scale input. |
| Max Pieces (`Mx`) | Integer | item | Cap on output piece count. -1 = unlimited. |
| Seed (`Sd`) | Integer | item | RNG seed for reproducibility. |
| Run (`Run`) | Boolean | item | Set true to compute. Decomposition is heavy. |

| out | type | access | description |
|---|---|---|---|
| Blocks (`B`) | Mesh | list | One nearly-convex mesh per piece. |
| Count (`N`) | Integer | item | Number of pieces. |
| Available (`Av`) | Boolean | item | True iff frahan_coacd.dll is loadable. |
| Report (`R`) | Text | item | Diagnostic report. |

### Quarry Decompose By Mesh (CGAL)  (`QuarryDcCgal`)

- GUID: `F2D000C1-CADC-4F2D-A0C1-7E60CADA15A0`  |  icon: `CoacdDecompose.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/CgalCutComponents.cs`
- Algorithm: **CGAL Polygon Mesh Processing corefinement** - CGAL Polygon Mesh Processing (corefine_and_compute, EPECK/EPICK hybrid kernel)
- Decomposes a (possibly non-convex) quarry mesh into blocks  by intersecting it against a 3D grid of box cells via CGAL.  Empty cells are dropped automatically. Use this when the  plane-based Quarry Decompose does not apply because the  quarry mesh is not convex. Implements CGAL PMP corefinement.  Selection: convex pieces -> By CoACD; plane-bounded cuts -> By Mesh (CGAL); cell partition -> By Voronoi.

| in | type | access | description |
|---|---|---|---|
| Quarry (`Q`) | Mesh | item | Quarry mesh. Must be closed and manifold (run Mesh Repair  (CGAL) upstream if in doubt). Need not be convex. |
| Grid Box (`Gb`) | Box | item | Oriented box that defines the grid extent + orientation.  If empty (Box.Empty / Box.Unset), the world-aligned bounding  box of the Quarry mesh is used. |
| nX (`nX`) | Integer | item | Grid divisions along the box's local +X axis (>= 1). |
| nY (`nY`) | Integer | item | Grid divisions along the box's local +Y axis (>= 1). |
| nZ (`nZ`) | Integer | item | Grid divisions along the box's local +Z axis (>= 1). |
| Hybrid Kernel (`Hy`) | Boolean | item | True (default) = HYBRID kernel for robustness on every  cell intersection. False = EPICK only (fastest). |
| Run (`Run`) | Boolean | item | Set true to compute. Cost scales with nX*nY*nZ CGAL calls. |

| out | type | access | description |
|---|---|---|---|
| Blocks (`B`) | Mesh | list | One mesh per non-empty grid cell intersection (Quarry ∩ cell). |
| Cell Index (`Ci`) | Integer | list | Flat (i + j*nX + k*nX*nY) cell index for each output block,  parallel to the Blocks list. Lets the caller correlate  outputs with their originating cell. |
| Backend (`B`) | Text | item | Which kernel ran on the most recent cell. |
| Available (`Av`) | Boolean | item | True iff the CGAL native shim is loadable. |
| Report (`R`) | Text | item | Diagnostic report (cells visited / kept / dropped, runtime). |

### Quarry Decompose By Tet  (`QuarryDcTet`)

- GUID: `F2D000E1-CADC-4F2D-A0E1-7E60CADA15A0`  |  icon: `CoacdDecompose.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/AdvancedQuarryDecomposeComponents.cs`
- Algorithm: **Geogram tetrahedralisation** - Lévy, B. Geogram v1.9.9 (GEO::mesh_tetrahedralize), BSD-3
- Decomposes a quarry mesh into tetrahedra via Geogram.  Fine-grained, fracture-pattern style. Requires the  Geogram shim to be built with GEOGRAM_WITH_TETGEN=ON  (off by default — TetGen is non-commercial-use). When  off, the component surfaces a clear error and produces no  blocks; use Quarry Decompose By CoACD instead. Implements Geogram tetrahedralisation (Lévy, Geogram v1.9.9).

| in | type | access | description |
|---|---|---|---|
| Quarry (`Q`) | Mesh | item | Closed manifold quarry mesh. |
| Preprocess (`Pp`) | Boolean | item | Run mesh preprocess (manifold-isation, hole fill) inside  Geogram before tetrahedralizing. |
| Refine (`Rf`) | Boolean | item | Refine the tet mesh via Delaunay refinement after the  initial tetrahedralization. Increases tet count. |
| Quality (`Qu`) | Number | item | Tet quality bound for refinement (radius-edge ratio).  Default 1.4. Lower is stricter / more tets. |
| Run (`Run`) | Boolean | item | Set true to compute. |

| out | type | access | description |
|---|---|---|---|
| Tets (`T`) | Mesh | list | One closed tetrahedron mesh per output cell. |
| Count (`N`) | Integer | item | Number of tets. |
| Available (`Av`) | Boolean | item | True iff frahan_geogram.dll is loadable. |
| Report (`R`) | Text | item | Diagnostic report. Reports the TetGen-disabled state when  applicable. |

### Quarry Decompose By Voronoi  (`QuarryDcVoro`)

- GUID: `F2D000E2-CADC-4F2D-A0E2-7E60CADA15A0`  |  icon: `Voronoi.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/AdvancedQuarryDecomposeComponents.cs`
- Algorithm: **Restricted Voronoi diagram** - Lévy, B. Geogram v1.9.9 restricted Voronoi, BSD-3
- Decomposes a (possibly non-convex) quarry mesh into solid  Voronoi blocks. Seeds are sampled inside the quarry and  Lloyd-relaxed for a more uniform cell-area distribution.  Each cell is then CGAL-intersected against the quarry  for the final block geometry. Realistic stone-fracturing  look; seed count + relaxation iterations are user dials. Implements restricted Voronoi + Lloyd relaxation (Geogram; Lloyd 1982).  Selection: convex pieces -> By CoACD; plane-bounded cuts -> By Mesh (CGAL); cell partition -> By Voronoi.

| in | type | access | description |
|---|---|---|---|
| Quarry (`Q`) | Mesh | item | Closed manifold quarry mesh. |
| Seed Count (`Ns`) | Integer | item | Number of Voronoi seeds = number of output blocks.  Default 30. Typical 20–200 for masonry-scale work. |
| Lloyd Iters (`Li`) | Integer | item | Lloyd-relaxation iterations on the interior seeds. 0 =  raw rejection-sampled seeds; 5–10 = visibly more uniform. |
| Seed (`Sd`) | Integer | item | RNG seed for reproducibility. Default 1. |
| Hybrid Kernel (`Hy`) | Boolean | item | True (default) = CGAL HYBRID kernel for the cell ×  quarry intersection. False = EPICK only (faster, less robust). |
| Run (`Run`) | Boolean | item | Set true to compute. |

| out | type | access | description |
|---|---|---|---|
| Blocks (`B`) | Mesh | list | One mesh per non-empty Voronoi cell ∩ quarry intersection. |
| Seeds (`S`) | Point | list | The relaxed seed positions actually used (parallel to Blocks). |
| Available (`Av`) | Boolean | item | True iff CGAL shim is loadable. |
| Report (`R`) | Text | item | Diagnostic report. |

### Scan to Block Inventory  (`ScanBlock`)

- GUID: `F2D0BC20-1A2B-4F2D-A0B0-7E60CADA20A0`  |  icon: `QuarryBlock.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Quarry/ScanToBlockInventoryComponent.cs`
- Convert a 3D-scanned raw block mesh into a typed QuarryBlock  (bounds + usable volume + frame + dimensions + label) the  downstream GeoCut / GeoPack / BlockPackTree chain can nest  project parts into. Orientation: 0 = mesh frame, 1 = PCA  (longest principal axis → X), 2 = world Z. Method: 0 = OBB,  1 = inscribed AABB after PCA align, 2 = ConvexHull.

| in | type | access | description |
|---|---|---|---|
| Scan Mesh (`M`) | Mesh | item | Raw 3D-scanned block as a mesh (from a handheld scanner via  Scan Reconstruct, or a .3dm import). |
| Orient (`O`) | Integer | item | Orientation policy. 0 = keep the mesh's existing frame.  1 = align by PCA (longest principal axis → X). 2 = align by  world Z (top face flat). |
| Usable Inset (`I`) | Number | item | Inset (model units) used to compute the usable interior  volume — accounts for kerf + scan noise + edge defects.  Negative values are clamped to 0. |
| Method (`Me`) | Integer | item | Block-extraction method. 0 = OBB (oriented bounding box,  fast, deterministic). 1 = inscribed AABB after PCA align.  2 = ConvexHull. |
| Label (`L`) | Text | item | Per-block label / provenance string carried into the typed  QuarryBlock and downstream metadata. |

| out | type | access | description |
|---|---|---|---|
| Bounds (`Bb`) | Mesh | item | Oriented bounding-box mesh (visualisation + downstream  usable-volume input for the raw-Mesh wire path). |
| Frame (`Fr`) | Plane | item | Block's oriented base frame (origin + X/Y/Z axes from PCA /  world align). |
| Dimensions (`D`) | Vector | item | Block's principal dimensions (X = longest, Y = next,  Z = thinnest) in model units. |
| Volume (`V`) | Number | item | Usable interior volume (model units cubed); accounts for  Usable Inset. |
| Report (`R`) | Text | item | One-line summary: "Block <label> X x Y x Z units, volume V  units^3, method M". |

### Stereonet + Block Size  (`Stereonet`)

- GUID: `D5F1004A-ED9E-4ED9-A04A-ED9EED9E004A`  |  icon: `DiscontinuitySets.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Quarry/StereonetBlockSizeComponent.cs`
- Algorithm: **Equal-area stereonet** - Schmidt/Lambert lower-hemisphere projection; Wulff toggle
- Self-presenting card: equal-area lower-hemisphere stereonet (great circles + set poles + facet-pole  density) plus an in-situ block-size readout (Jv, Palmstrom Vb, RQD, Deq). Feed the per-set Dip / Dip dir /  Spacing / Share (and optional Facets path) from Discontinuity Sets (Async). Set Unit scale to convert  spacing to metres; block-size numbers are a proxy.

| in | type | access | description |
|---|---|---|---|
| Dip (`D`) | Number | list | Per-set dip (deg). |
| Dip dir (`Dd`) | Number | list | Per-set dip-direction (deg). |
| Spacing (`Sp`) | Number | list | Per-set mean normal spacing (cloud units). |
| Share (`Sh`) | Number | list | Per-set point share (optional; picks the 3 dominant sets). |
| Facets path (`Fp`) | Text | item | Optional facets.csv path (from 'Keep facets') for the pole-density cloud. |
| Plane (`Pl`) | Plane | item | Base plane for the net (origin + X=East, Y=North). Default World XY. |
| Radius (`R`) | Number | item | Net radius (model units). |
| Unit scale (`U`) | Number | item | Multiplies spacing into metres for the block-size math (e.g. 1 if already metres). |
| Equal area (`Ea`) | Boolean | item | True = equal-area (Schmidt); false = equal-angle (Wulff). |

| out | type | access | description |
|---|---|---|---|
| Net (`N`) | Curve | list | Primitive circle + N/E/S/W ticks. |
| Great circles (`Gc`) | Curve | list | Cyclographic trace per set. |
| Set poles (`P`) | Point | list | Projected pole per set. |
| Facet poles (`Fp`) | Point | list | Projected facet-pole density cloud (if Facets path given). |
| Jv (`Jv`) | Number | item | Volumetric joint count (joints/m^3 proxy). |
| Vb (`Vb`) | Number | item | Palmstrom block volume (m^3; NaN if < 3 sets). |
| RQD (`Rq`) | Number | item | RQD proxy (0..100). |
| Deq (`De`) | Number | item | Equivalent block diameter (m). |
| Report (`Re`) | Text | item | Per-set table + block-size readout + unit notes. |

Related:
- Frahan > Quarry > Discontinuity Sets (Async) - Upstream source of dip/dipdir/spacing/share + facets.csv.

### Stochastic DFN (Baecher)  (`BaecherDFN`)

- GUID: `D5F1004C-ED9E-4ED9-A04C-ED9EED9E004C`  |  icon: `DiscontinuitySets.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Quarry/StochasticDfnComponent.cs`
- Algorithm: **Baecher stochastic DFN** - Baecher et al. 1977 finite-disc DFN; Fisher (1953) orientation; lognormal persistence
- Stochastic finite-persistence discrete fracture network (Baecher disc model): Poisson centres,  Fisher-sampled poles (dispersion kappa), lognormal persistence. Intensity from spacing (P10=1/sp).  Deterministic by seed; vary the seed for a Monte-Carlo block-yield distribution. Output DFN mesh +  bench feed the BlockCutOpt packers.

| in | type | access | description |
|---|---|---|---|
| Dip (`D`) | Number | list | Per-set dip (deg). |
| Dip dir (`Dd`) | Number | list | Per-set dip-direction (deg). |
| Spacing (`Sp`) | Number | list | Per-set normal spacing (m) -> intensity P10 = 1/spacing. |
| Kappa (`K`) | Number | list | Per-set Fisher dispersion (one value applies to all). Higher = tighter. |
| Persistence (`P`) | Number | list | Per-set mean fracture diameter (m) (one value applies to all). |
| Persistence CV (`Cv`) | Number | item | Lognormal coefficient of variation of persistence. |
| Bench (`B`) | Box | item | Domain box the fractures are generated in (also feed to BlockCutOpt Tested Area). |
| Seed (`S`) | Integer | item | Realisation seed (vary for Monte-Carlo). |

| out | type | access | description |
|---|---|---|---|
| DFN (`F`) | Mesh | item | Stochastic finite-disc fracture mesh. Feed to BlockCutOpt 'Fractures'. |
| Tested area (`A`) | Box | item | The bench box, passed through to BlockCutOpt 'Tested Area'. |
| Fractures (`N`) | Integer | item | Number of fracture discs generated. |
| P32 (`P32`) | Number | item | Fracture area per unit volume (1/m). |
| Report (`Re`) | Text | item | Per-set disc counts + intensity + notes. |

Related:
- Frahan > Quarry > Discontinuity Sets (Async) - Upstream: dip/dipdir/spacing per set.
- Frahan > Quarry > Joint Sets to DFN - The infinite-plane (deterministic) sibling; this is finite-persistence + stochastic.
- Frahan > Quarry > BlockCutOpt Omni Solve - Downstream: block-cut yield per realisation (Monte-Carlo over seeds).


## Reports

### Frahan Packing Plan Report  (`PackPlanRpt`)

- GUID: `AB12C008-1A2B-4C3D-9E4F-5A6B7C8D9E08`  |  icon: `PackDiagnostics.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/PackingPlanReportComponent.cs`
- Aggregate PackingMetricsReport + residual voids + edge-match scores  into one PackingPlanReport. All inputs come from upstream Frahan  components (Pack3D, Residual Voids, Fragment Edge Match).

| in | type | access | description |
|---|---|---|---|
| Packing Metrics (`M`) | Generic | item | PackingMetricsReport (opaque) from Frahan Pack3D / Frahan Packing Metrics. |
| Residual Voids (`V`) | Generic | list | ResidualVoid list (opaque) from Frahan Residual Voids component.  Optional; defaults to empty. |
| Edge Match Scores (`E`) | Generic | item | Per-fragment-per-edge best match scores as a nested list  (IReadOnlyList<IReadOnlyList<double>>) wrapped opaque, or a flat  list of doubles (one entry per fragment-edge). Optional; defaults to empty. |
| Edge Match Tree (`Et`) | Number | tree | Per-fragment-per-edge best match scores as a DataTree<Number>.  One branch per fragment, items in each branch are that fragment's  per-edge scores. If both Edge Match Scores (E) and Edge Match Tree  (Et) are wired, the tree takes precedence. Optional. |

| out | type | access | description |
|---|---|---|---|
| Plan Report (`R`) | Generic | item | PackingPlanReport (opaque) for downstream serialisation / further reporting. |
| Total Residual Void Area (`Va`) | Number | item | Sum of approximate areas across all residual voids. |
| Avg Best Edge Match Score (`Es`) | Number | item | Mean of best-match scores across all fragment edges. Zero if no edges supplied. |
| Summary (`S`) | Text | item | One-line human-readable summary (PackingPlanReport.ToString()). |

### Frahan Packing Report  (`PackRpt`)

- GUID: `AB12C004-1A2B-4C3D-9E4F-5A6B7C8D9E04`  |  icon: `PackDiagnostics.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/PackingReportComponent.cs`
- Compute summary metrics for a 3D PackResult: placements, failures,  fill ratio, average placement score, item-volume stats, per-reason  failure counts.

| in | type | access | description |
|---|---|---|---|
| Pack Result (`R`) | Generic | item | PackResult from a 3D pack solver (opaque). |

| out | type | access | description |
|---|---|---|---|
| Placement Count (`P`) | Integer | item | Number of placed items. |
| Failure Count (`F`) | Integer | item | Number of failed items. |
| Failure Ratio (`Fr`) | Number | item | Failures / (Placements + Failures). |
| Packed Volume (`Vp`) | Number | item | Sum of placed item volumes. |
| Container Volume (`Vc`) | Number | item | Container volume. |
| Fill Ratio (`Fl`) | Number | item | Packed / Container. |
| Average Score (`S`) | Number | item | Average placement score. |
| Max Item Height (`H`) | Number | item | Max top-of-item Z across placements. |
| Item Volume Min/Max/Avg (`Vi`) | Number | list | Three-element list: [min, max, avg]. |
| Failure Reasons (`Rs`) | Text | list | One '<reason>: <count>' line per distinct failure reason. |
| Report (`R`) | Text | item | Single-line summary. |

### Frahan Report / Export  (`Report`)

- GUID: `AB12C010-1A2B-4C3D-9E4F-5A6B7C8D9E10`  |  icon: `PackDiagnostics.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/AudienceReportComponent.cs`
- Audience-tailored report terminal. Pick Audience (0 engineer, 1 artist,  2 geologist). Consumes Frahan report records + optional pipe-delimited  Sections; emits Markdown + CSV. Engineer release is refused without a  declared CRS/datum. With Run + File Path it writes the .md / .csv files.

| in | type | access | description |
|---|---|---|---|
| Reports (`R`) | Generic | list | Frahan report records (PackingReport, MeshDiagnostics, FabricationPrep,  BlockCutOpt, ChartFlatness). Optional; wire any number. |
| Sections (`S`) | Text | list | Optional extra rows, one per line, pipe-delimited:  'Section|Label|Value|Unit|Flag' for a value row, or 'Section|note|Text'  for a note. Use this to add a block schedule, fracture sets, etc. |
| Audience (`A`) | Integer | item | 0 = Engineer (mining-plan), 1 = Artist (carving-guide), 2 = Geologist (brief). |
| Format (`Fmt`) | Integer | item | 0 = Markdown, 1 = CSV, 2 = Both. |
| Site (`St`) | Text | item | Site name (provenance). |
| Scan File (`Sf`) | Text | item | Source scan file (provenance). |
| Date (`Dt`) | Text | item | Report date (provenance). |
| CRS / Datum (`Crs`) | Text | item | Coordinate reference system + datum. REQUIRED for an engineer release. |
| Units (`U`) | Text | item | Document units (provenance). m |
| Solver Version (`Sv`) | Text | item | Solver / mesh version (provenance). |
| File Path (`Fp`) | Text | item | Optional output base path WITHOUT extension (e.g. C:\out\mining_plan).  Writes <path>.md and/or <path>.csv when Run is true. |
| Run (`Run`) | Boolean | item | Set true to write the report file(s) to File Path. |

| out | type | access | description |
|---|---|---|---|
| Markdown (`Md`) | Text | item | Audience-tailored Markdown report. |
| CSV (`Csv`) | Text | item | Audience-tailored CSV report. |
| Refused (`X`) | Boolean | item | True when the release was refused (engineer without a CRS). |
| Warnings (`W`) | Text | list | Warnings + surfaced confidence flags. |
| Files Written (`Fw`) | Text | list | Paths written when Run is true. |


## Sculpt

### Carving Stages  (`CarveStages`)

- GUID: `F2D06A03-1A2B-4C3D-9E4F-5A6B7C8D9E03`  |  icon: `CncRoughing.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Sculpt/CarvingStagesComponent.cs`
- Algorithm: **Staged offset-shell roughing** - Frahan-original
- Roughing-pass shells from a rough block / flat top down to the  finished sculpture (digital pointing machine). Mode 0 Radial  (smoothed normals), 1 Push-In (Front Direction), 2 Flat Top (bbox  face; best for reliefs, no Block needed); a Block input clamps stages  to an arbitrary block mesh. CACHED + Run-gated: recomputes only when  its inputs change and re-emits the cached result otherwise, so editing  a List Item index or other components never re-runs it or freezes the  canvas. Synchronous; preview off (pick a stage downstream).  Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Target (`M`) | Mesh | item | Finished sculpture / relief mesh (the final surface). |
| Stages (`N`) | Integer | item | Number of roughing passes (>= 1). |
| Max Offset (`Mx`) | Number | item | Free-offset modes (0/1, no Block): outward offset of the roughest shell. |
| Finish Allowance (`Fa`) | Number | item | Free-offset modes: offset left on the final pass (0 = exact surface). |
| Feature Boost (`Fb`) | Number | item | Free-offset modes: extra stock at the strongest protrusion (ears/noses), x the offset. 0 = uniform. |
| Mode (`Md`) | Integer | item | 0 = Radial (smoothed normals); 1 = Push-In (along Front Direction); 2 = Flat Top (bbox face along Front Direction - best for reliefs, no Block needed). |
| Front Direction (`Fd`) | Vector | item | Push-In / Flat-Top direction (e.g. +Z for a flat top); also the axis a Block sits along. |
| Block (`B`) | Mesh | item | Raw stone block (optional). When given, stages are clamped to the block surface (roughest at the block, finish at the target). |
| Run (`R`) | Boolean | item | Compute (when inputs change). False = keep showing the cached result. Recompute only fires when an input actually changes. |

| out | type | access | description |
|---|---|---|---|
| Stages (`S`) | Mesh | list | Roughing shells, roughest first -> finish last. |
| Offsets (`O`) | Number | list | Per-stage offset distance (free modes) or reach fraction 1..0 (Block / Flat Top). |
| Feature Weight (`Fw`) | Number | list | Per-vertex protrusion weight 0..1. |
| Count (`N`) | Integer | item | Number of stage meshes produced. |

### Enlarge Sculpture  (`Enlarge`)

- GUID: `F2D06A01-1A2B-4C3D-9E4F-5A6B7C8D9E01`  |  icon: `MorphCorrect.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Sculpt/EnlargeSculptureComponent.cs`
- Algorithm: **Parametric enlargement** - Frahan-original
- Digital pointing-machine scaling: enlarge a scanned maquette mesh to  a target size (Mode 0 factor, 1 target-longest, 2 target-height, 3  non-uniform XYZ). Scales from the base centre by default so a plinth  stays grounded. Wire the output into Fit In Block. Frahan-original method (digital pointing-machine; affine scale-from-base).

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Scanned maquette mesh. |
| Mode (`Mo`) | Integer | item | 0 = Factor, 1 = Target Longest, 2 = Target Height (Z), 3 = Non-uniform XYZ. |
| Value (`V`) | Number | item | Mode 0: scale factor. Mode 1: target longest dimension. Mode 2: target  height. Ignored in Mode 3. |
| Target XYZ (`T`) | Vector | item | Mode 3 only: target size on each axis (model units). |
| Anchor (`A`) | Point | item | Scale origin. Default: base centre (bbox centre X/Y at min Z). |

| out | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Enlarged mesh. |
| Scale Factors (`F`) | Vector | item | Per-axis scale factors applied. |
| Final Size (`S`) | Vector | item | Bounding size of the enlarged mesh (x,y,z). |
| Volume (`Vol`) | Number | item | Volume of the enlarged mesh (0 if not closed). |

### Fit In Block  (`FitBlock`)

- GUID: `F2D06A02-1A2B-4C3D-9E4F-5A6B7C8D9E02`  |  icon: `PackIntoBlock.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Sculpt/FitInBlockComponent.cs`
- Algorithm: **Bounding-extents containment + max-scale fit** - Frahan-original
- Check whether a raw block can hold a (enlarged) sculpture, allowing a  kerf/roughing margin. Reports fit, per-axis clearance, and the max  scale that still fits. v1 uses bounding extents matched largest-to- largest; optionally centres the piece in the block. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Sculpture (`S`) | Mesh | item | Sculpture mesh (e.g. from Enlarge Sculpture). |
| Block (`B`) | Mesh | item | Raw block mesh (available stock). |
| Margin (`Mg`) | Number | item | Clearance per side subtracted from the block (kerf + roughing allowance + handling). |
| Place (`P`) | Boolean | item | Centre the sculpture inside the block (translation only in v1). |

| out | type | access | description |
|---|---|---|---|
| Fits (`F`) | Boolean | item | True if the block holds the sculpture (with margin). |
| Clearance (`C`) | Vector | item | Per sorted-axis slack (block - sculpture), largest axis first. Negative = overflow. |
| Max Scale To Fit (`Sf`) | Number | item | Largest uniform scale of the sculpture that still fits (>=1 means it already fits). |
| Placed (`M`) | Mesh | item | Sculpture centred in the block (if Place = true). |
| Report (`R`) | Text | item | Human-readable fit summary. |


## Slab

### Slab Cut By Fractures  (`SlabCut`)

- GUID: `C2B3D4E5-6F7A-489B-AC1D-2E3F4A5B6C7D`  |  icon: `BlockCutOpt.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Slab/SlabCutByFracturesComponent.cs`
- Algorithm: **Convex polyhedron half-space clipping (managed)** - Frahan-original
- Cuts a list of Slabs by a list of oriented fracture planes.  Each Rhino Plane is interpreted as an infinite plane (Origin, Normal).  Output Slabs carry the input-list parent index so callers can  track 'this fragment came from quarry block #N'.  Managed path is Frahan-original; opt-in CGAL backend uses CGAL PMP booleans (CGAL_PMP).

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | list | Convex meshes to cut. Standard Rhino mesh wires; the cutter  converts to its internal Slab DTO automatically. Multi-shell  meshes should be split with Mesh Shell Split first. |
| Plane (`P`) | Generic | list | Oriented infinite fracture planes. Accepts the Frahan  FracturePlane DTO (from any *Fracture Planes generator) OR a  Rhino Plane (origin + normal). The two are interchangeable. |
| Eps (`E`) | Number | item | Vertex-classification tolerance. Default 1e-9 works for most  metric inputs; raise to 1e-6 for non-metric or noisy meshes. |
| Use CGAL (`Cg`) | Boolean | item | Backend. False (default) = managed convex SlabCutter (fast, but  convex-only and explodes combinatorially on large slabs with many  planes). True = route the cut through the CGAL boolean kernel  (CgalMeshBoolean): robust on non-convex / large slabs. CGAL path  returns meshes (the Slab output is empty). Falls back to managed  if the CGAL shim is not loaded. |

| out | type | access | description |
|---|---|---|---|
| Slab (`S`) | Generic | list | Output Slabs after cutting. |
| Parent (`P`) | Integer | list | Per-output parent index (0-based) into the input Slab list. |
| TotalVolume (`V`) | Number | item | Sum of signed volumes of all output Slabs (sanity check). |
| Count (`N`) | Integer | item | Number of resulting Slabs. |
| Mesh (`M`) | Mesh | list | Output Slabs as Rhino Meshes (parallel to the Slab list). Wire  into native components (Move, Bake, Boolean, Volume, etc.). |

### Slab Cut By Tool Mesh (CGAL)  (`SlabCutCgal`)

- GUID: `F2D000C0-CADC-4F2D-A0C0-7E60CADA15A0`  |  icon: `BlockCutOpt.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/CgalCutComponents.cs`
- Algorithm: **Exact-predicate mesh-mesh CSG via corefinement** - CGAL Polygon Mesh Processing corefine_and_compute_difference/intersection (EPICK+EPECK Hybrid kernel)
- Cuts a slab/block mesh by an arbitrary tool mesh via CGAL  exact-predicate booleans. Outputs the outside half  (slab − tool), the inside half (slab ∩ tool), or both.  Use this for non-convex slabs or curved/sculpted fracture  tools where the plane-based cutter does not apply.  Implements CGAL PMP corefinement booleans (CGAL_PMP).

| in | type | access | description |
|---|---|---|---|
| Slab (`S`) | Mesh | item | Slab/block mesh to cut. Must be closed and manifold for  predictable output (run Mesh Repair (CGAL) upstream if in  doubt). |
| Tool (`T`) | Mesh | item | Tool mesh used as the cutter. Closed manifold mesh. |
| Mode (`M`) | Integer | item | 0 = Outside only (slab − tool).  1 = Inside only  (slab ∩ tool).  2 = Both halves (default). |
| Hybrid Kernel (`Hy`) | Boolean | item | True (default) = HYBRID — EPICK storage + EPECK intersection  construction. Robust on near-tangent contacts and multi-cut  chains at a 2–5x speed cost.  False = EPICK only — fastest, fine for well-conditioned inputs. |
| Run (`Run`) | Boolean | item | Set true to compute. Heavy operation on large inputs. |

| out | type | access | description |
|---|---|---|---|
| Outside (`O`) | Mesh | item | Slab − Tool. The portion of the slab outside the tool.  Empty mesh when the tool fully contains the slab. |
| Inside (`I`) | Mesh | item | Slab ∩ Tool. The portion of the slab inside the tool.  Empty mesh when the tool misses the slab entirely. |
| Backend (`B`) | Text | item | Which kernel ran: 'CGAL' or 'ManagedBsp' (BSP fallback). |
| Available (`Av`) | Boolean | item | True iff the CGAL native shim is loadable. |
| Report (`R`) | Text | item | Diagnostic report (mode, kernel, vertex/face counts, runtime). |

### Slab From Mesh  (`Slab`)

- GUID: `B1A2C3D4-5E6F-4789-9ABC-1D2E3F4A5B6C`  |  icon: `QuarryBlock.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Slab/SlabFromMeshComponent.cs`
- Wraps a Rhino mesh into a Slab DTO. Quads stay as quads.  Mesh must have at least 4 vertices and 4 faces. Slab assumes  the input is CONVEX; convexity is not verified here.

| in | type | access | description |
|---|---|---|---|
| Mesh (`M`) | Mesh | item | Rhino mesh defining a CONVEX polyhedral slab. Quads are  preserved as quads; triangles stay as triangles. |

| out | type | access | description |
|---|---|---|---|
| Slab (`S`) | Generic | item | Slab DTO. Wire into Slab Cut By Fractures or downstream masonry. |
| Mesh (`M`) | Mesh | item | Slab as a Rhino Mesh (re-emitted). Identical geometry to the  input Mesh but fan-triangulated from each polygonal face. |

### Vertical Fracture Planes From Curves  (`FracPlanes`)

- GUID: `F2D05A09-1A2B-4C3D-9E4F-5A6B7C8D9E09`  |  icon: `PoissonReconstruct.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/VerticalFracturePlanesFromCurvesComponent.cs`
- Algorithm: **Vertical plane per fracture trace** - Frahan-original: plan-view trace -> vertical cutting plane (contains the trace direction + Z)
- Turn plan-view fracture trace curves (e.g. from Vector Fractures  Loader on a real fracture shapefile) into VERTICAL cutting planes  for Slab Cut By Fractures. Per Segment = a plane per polyline segment  (faithful to wiggly traces); off = one best-fit vertical plane per curve.

| in | type | access | description |
|---|---|---|---|
| Curves (`T`) | Curve | list | Fracture trace curves (plan-view). |
| Per Segment (`Sg`) | Boolean | item | TRUE = one vertical plane per polyline segment (faithful to curved traces);  FALSE (default) = one best-fit vertical plane per curve (start->end direction). |

| out | type | access | description |
|---|---|---|---|
| Planes (`P`) | Plane | list | Vertical fracture planes (feed Slab Cut By Fractures 'Plane'). |
| Count (`N`) | Integer | item | Number of planes produced. |

Related:
- Frahan > Ingest > Vector Fractures Loader - Source of the real fracture trace curves (.shp / .geojson).
- Frahan > Slab > Slab Cut By Fractures - Consumes these vertical planes to cut a block into slabs.


## Surface Packing

### Frahan Chart Flatness Report  (`ChartFlat`)

- GUID: `AB12C006-1A2B-4C3D-9E4F-5A6B7C8D9E06`  |  icon: `DistortionMap.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/ChartFlatnessReportComponent.cs`
- Algorithm: **Per-face area-ratio distortion classification** - Frahan-original
- Classify per-face area ratios against a flatness threshold.  Threshold is interpreted as max(ratio, 1/ratio); 0.5 and 2.0 are  equally distorted from 1.0. Frahan-original method.

| in | type | access | description |
|---|---|---|---|
| Per-Face Area Ratios (`A`) | Number | list | List of per-face area ratios from ChartDistortionAnalyzer. |
| Threshold (`T`) | Number | item | Flatness threshold (1.0 = no distortion allowed; 1.5 = 50% allowed; 2.0 = 2x). |

| out | type | access | description |
|---|---|---|---|
| Total Face Count (`N`) | Integer | item | Total faces classified. |
| Above Threshold Count (`Nx`) | Integer | item | Faces above the threshold. |
| Above Threshold Ratio (`Rx`) | Number | item | Above / Total. |
| Worst Face Index (`Wi`) | Integer | item | Index of the most-distorted face. |
| Worst Area Ratio (`Wr`) | Number | item | Normalised distortion of the worst face. |
| Per-Face Above Flag (`Bf`) | Boolean | list | Per-face boolean: true if above threshold. |
| Report (`R`) | Text | item | Single-line summary. |

### Pack On Surface  (`PackSurf`)

- GUID: `B7E4D9C1-3F8A-4B2E-91C6-5D7F3A8B2E1D`  |  icon: `SurfaceTile.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/SurfacePacking/PackOnSurfaceComponent.cs`
- Algorithm: **Barycentric 2D-to-3D mapping** - Floater 2003, Computer Aided Geometric Design 20(1):19-27 Mean value coordinates
- Packs 2D shapes onto a surface chart with the deterministic hole-aware nester (exact NFP  bottom-left-fill, multi-start, 0-overlap), then lifts packed curves to the 3D surface via  barycentric mapping. Runs async: the canvas stays live. [Floater 2003]

| in | type | access | description |
|---|---|---|---|
| Surface Map (`Map`) | Generic | item | FrahanSurfaceChart from the Surface Chart component. |
| Parts (`P`) | Curve | list | Closed planar 2D part curves to pack (in the flat chart XY plane). |
| Spacing (`Gap`) | Number | item | Clearance between parts and between parts and the sheet boundary (model units). |
| Rotations (`R`) | Number | list | Allowed rotation angles in degrees. The hole-aware engine uses the COUNT of angles as its  uniform base rotation count (default list 0/90/180/270 -> 4), extended with contact angles. |
| Tolerance (`T`) | Number | item | Geometric tolerance for the 3D barycentric mapping and containment checks. |
| Sort Mode (`M`) | Integer | item | IGNORED by the hole-aware engine (kept for compatibility). It multi-starts over  area/max-dim/width/height orders automatically - see MultiStart. |
| Corner Mode (`Cnr`) | Integer | item | IGNORED by the hole-aware engine (kept for compatibility). Placement is always bottom-left-fill. |
| Seed (`Seed`) | Integer | item | IGNORED by the hole-aware engine (kept for compatibility). The engine is deterministic. |
| Max Candidates (`Max`) | Integer | item | IGNORED by the hole-aware engine (kept for compatibility). The exact NFP enumerates feasible  placements directly. |
| Run (`Run`) | Boolean | item | Set to True to execute packing. False shows the idle message and cancels any running solve. |
| ContactRotations (`CR`) | Integer | item | Longest-edge count per polygon used to build contact (edge-alignment) rotation angles. Default 6. |
| Resolution (`Res`) | Integer | item | Solver sampling resolution for smooth part curves (16..200, default 24). Collision proxy only;  packed output is the exact original curve. Solve time grows ~quadratically. |
| MultiStart (`MS`) | Integer | item | Deterministic part orders the engine tries, keeping the densest valid layout (1..4, default 4).  1 = single largest-first pass. Higher raises density at ~linear cost, never reduces placements. |

| out | type | access | description |
|---|---|---|---|
| Packed 3D (`C3`) | Curve | list | Packed part curves lifted to the 3D surface. |
| Packed 2D (`C2`) | Curve | list | Packed part curves in the flat chart plane (real units). |
| Unplaced (`U`) | Curve | list | Curves that could not be placed in the chart. |
| Failed 3D (`F`) | Integer | item | Number of packed curves that failed 3D barycentric mapping (likely cross a UV seam). |
| Report (`R`) | Text | item | Packing and mapping report. |

### Pack Surfaces  (`PackSurfs`)

- GUID: `C4A8D2E1-7F3B-4C5D-9A2E-6B8D4F1E3C7A`  |  icon: `SurfaceUnroll.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/SurfacePacking/PackSurfacesComponent.cs`
- Packs 2D shapes across one or more surface charts with the deterministic hole-aware  nester (exact NFP bottom-left-fill, multi-start, 0-overlap), then maps them onto the 3D  surfaces. Runs async: the canvas stays live and the result pops in when ready. Outputs  Full Transform to place original flat parts on the surface without distortion.

| in | type | access | description |
|---|---|---|---|
| Surface Maps (`Maps`) | Generic | list | One or more FrahanSurfaceChart objects from the Surface Chart component. Each becomes one  sheet (greedy overflow chart 0 -> chart 1 -> ...). |
| Parts (`P`) | Curve | list | Closed planar 2D part curves to pack. |
| Spacing (`Gap`) | Number | item | Clearance between parts and chart boundaries (model units). |
| Rotations (`R`) | Number | list | Allowed rotation angles in degrees. The hole-aware engine uses the COUNT of angles as its  uniform base rotation count (default list 0/90/180/270 -> 4) and extends it with  contact (edge-alignment) angles. Pass more angles to raise the base count. |
| Tolerance (`T`) | Number | item | Geometric tolerance for the 3D barycentric mapping and containment checks. |
| Sort Mode (`M`) | Integer | item | IGNORED by the hole-aware engine (kept for compatibility). The engine multi-starts over  area/max-dim/width/height orders automatically and keeps the best - see MultiStart. |
| Corner Mode (`Cnr`) | Integer | item | IGNORED by the hole-aware engine (kept for compatibility). Placement is always bottom-left-fill. |
| Seed (`Seed`) | Integer | item | IGNORED by the hole-aware engine (kept for compatibility). The engine is deterministic:  identical inputs always reproduce the same layout. |
| Max Candidates (`Max`) | Integer | item | IGNORED by the hole-aware engine (kept for compatibility). The exact NFP enumerates feasible  placements directly. |
| Run (`Run`) | Boolean | item | Set to True to execute packing. False shows the idle message and cancels any running solve. |
| ContactRotations (`CR`) | Integer | item | Longest-edge count per polygon used to build contact (edge-alignment) rotation angles so  parts seat flush. Default 6. |
| Resolution (`Res`) | Integer | item | Solver sampling resolution for smooth part curves (16..200, default 24). This only sets the  collision proxy - packed output is always the exact original curve. Solve time grows  ~quadratically; raise only for tight concave notches. |
| MultiStart (`MS`) | Integer | item | Deterministic part orders the engine tries per chart, keeping the densest valid layout  (1..4, default 4). 1 = single largest-first pass. Higher raises density at ~linear cost and  never reduces placements or validity. |

| out | type | access | description |
|---|---|---|---|
| Packed 3D (`C3`) | Curve | list | Packed curves lifted to the 3D surface via barycentric mapping (shape follows surface). |
| Placement Planes (`Pl`) | Plane | list | Rigid placement frame on the 3D surface per packed part.  Origin = centroid on surface, X/Y = surface tangent axes, Z = surface normal. |
| Transforms 3D (`T3`) | Transform | list | Transform from PACKED 2D position to the 3D surface placement frame.  Apply to Packed 2D curves to get rigid (non-deformed) parts on the surface. |
| Full Transform (`FT`) | Transform | list | Composed transform: original flat part -> 3D surface in one step.  Apply to the ORIGINAL part geometry (before packing) using Part Index to select it. |
| Max Deviation (`Dev`) | Number | list | Maximum gap (model units) between the flat part and the curved surface  at the four bounding-box corners. Small = nearly flat. Large = needs shimming. |
| Packed 2D (`C2`) | Curve | list | Packed curves in each chart's native coordinate space. |
| Chart Index (`CI`) | Integer | list | Which Surface Map (0-based) each packed part was placed on. |
| Part Index (`PI`) | Integer | list | 0-based index into the original Parts input list for each packed part.  Use with List Item to select the matching original part, then apply Full Transform. |
| Unplaced (`U`) | Curve | list | Curves that could not be placed on any chart. |
| Report (`R`) | Text | item | Packing and mapping report. |

### Panel Tile Surface  (`PanelTile`)

- GUID: `A7E0B0F7-0C0F-4A16-9E3D-0FACE0FACE08`  |  icon: `QuarryBlock.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Quarry/PanelTileSurfaceComponent.cs`
- Algorithm: **Planar-panel discretization of a surface for stone cladding (planarized U x V quads + flat cut outlines)** - Each UV quad is projected to its best-fit plane (Plane.FitPlaneToPoints); the flat outline mapped to World XY is the cut tile.
- Discretize a freeform facade surface into PLANAR stone-cladding panels: divide the surface  U x V, project each quad to its best-fit plane (stone cannot bend), and output BOTH the 3D  panels on the surface AND their flat cut outlines (laid in World XY) ready to nest on slabs  with Sheet Nest (Hole-Aware). Reports per-panel planarity (corner deviation from the panel  plane) and area - raise U/V where planarity is too high for the curvature.

| in | type | access | description |
|---|---|---|---|
| Surface (`S`) | Surface | item | Facade surface to panelize (a single surface; a Brep face is coerced). |
| U Count (`U`) | Integer | item | Number of panels across the surface U direction. |
| V Count (`V`) | Integer | item | Number of panels across the surface V direction. |
| Joint (`J`) | Number | item | Grout / joint gap: each panel is inset by this toward its  centre (m). Default 0.005. |
| Planarize (`Pl`) | Boolean | item | Project each quad to its best-fit plane so every panel  is a FLAT cuttable tile (stone cannot bend). False = leave the warped surface quad. Default true. |

| out | type | access | description |
|---|---|---|---|
| Panels (`P`) | Mesh | list | The 3D (planarized) cladding panels positioned on the surface. |
| Cut Tiles (`T`) | Curve | list | Flat closed outline per panel, mapped to the World XY plane,  ready to nest on slabs (wire into Sheet Nest (Hole-Aware) > Parts). |
| Planarity (`Pl`) | Number | list | Max corner deviation from the panel plane (m) per panel.  High = the surface is too curved for that panel size; raise U / V. |
| Area (`A`) | Number | list | Panel area (m2) per panel. |
| Report (`R`) | Text | item | Panel count, total area, and worst planarity. |

Related:
- Frahan > 2D Packing > Sheet Nest (Hole-Aware) - Nest the flat Cut Tiles onto slab sheets to cut them from stock.
- Frahan > Quarry > Fracture Bounded Slabs - Produces the slabs the cut tiles are nested onto.

### Surface Chart  (`SurfChart`)

- GUID: `A3F1C8B2-74D9-4E2A-8F5B-1C3D9E7A2B4F`  |  icon: `BffChartPack.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/SurfacePacking/SurfaceChartComponent.cs`
- Algorithm: **BFF boundary-first flattening** - Sawhney and Crane 2017, ACM TOG 36(4):109
- Unwraps a 3D mesh to a 2D UV chart using Boundary-First Flattening (BFF).  BFF must be downloaded separately and the exe path provided as input. [Sawhney & Crane 2017]

| in | type | access | description |
|---|---|---|---|
| Surface (`S`) | Mesh | item | Mesh to unwrap. Accepts any Rhino mesh — cleaned automatically before BFF. |
| BFF Exe Path (`BFF`) | Text | item | Optional. Path to bff-command-line.exe.  Leave unconnected to auto-detect next to the .gha file. |
| Cones (`K`) | Integer | item | Number of cone singularities (0 = auto, 1–8 for complex surfaces). |
| Normalize UVs (`N`) | Boolean | item | Normalize output UVs to [0,1]. Required for chart scale computation. |
| Timeout (s) (`T`) | Number | item | Maximum seconds to wait for BFF before aborting. |
| Run (`Run`) | Boolean | item | Set to True to execute unwrapping. |

| out | type | access | description |
|---|---|---|---|
| Flat Mesh (`FM`) | Mesh | item | 2D unwrapped mesh in UV space (Z=0 plane). Scale by ChartScale for real dimensions. |
| Surface Map (`Map`) | Generic | item | FrahanSurfaceChart object. Wire into the Pack On Surface component. |
| Boundary (`B`) | Curve | item | Outer boundary polyline of the flat chart scaled to real units. |
| Distortion (`D`) | Text | item | Max/min edge stretch ratio. Values far from 1.0 indicate mapping distortion. |
| Report (`R`) | Text | item | Quality metrics, warnings, and timing. |


## Trencadis

### Frahan Trencadís Catalog Pack  (`TrencadisCat`)

- GUID: `F2D00007-CADC-4F2D-9007-7E60CADA15A0`  |  icon: `Trencadis.png`  |  exposure: `tertiary`  |  source: `src/Frahan.StonePack.GH/Pack2DTrencadisCatalogComponent.cs`
- Algorithm: **CVD-Lloyd interior seeding** - Lloyd 1982 centroidal Voronoi diagram relaxation
- Trencadís catalog packer: partition each sheet into CVD-Lloyd  cells, then optimally assign catalog parts to cells via the  Hungarian algorithm. Best when piece count matches target  coverage and you want each piece placed exactly once.

| in | type | access | description |
|---|---|---|---|
| Parts (`P`) | Curve | list | Catalog of irregular shard curves to place. Each piece will be  placed exactly once. |
| Sheet Outlines (`S`) | Curve | list | Closed planar sheet boundary curves. |
| Sheet Holes (`H`) | Curve | tree | Hole curves as a tree. Branch {0} = sheet 0, etc. |
| Tolerance (`T`) | Number | item | Geometric tolerance. |
| Seed (`Seed`) | Integer | item | 0 = deterministic. |
| Run (`Run`) | Boolean | item | Set true to run the catalog assignment. |
| Lloyd Iterations (`Iter`) | Integer | item | CVD-Lloyd relaxation iterations. Higher = more uniform cells. |
| Grout (`Gr`) | Number | item | Inward offset applied to each piece AFTER trim, to leave the  characteristic trencadís mortar gap. 0 = no grout. Default 0.02. |

| out | type | access | description |
|---|---|---|---|
| Placed Pieces (`C`) | Curve | list | Catalog parts placed at their assigned cell centroids. |
| Cell Seeds (`X`) | Point | list | CVD-Lloyd seed centroids — one per assigned cell. |
| Source Indices (`Src`) | Integer | list | Catalog index for each placed piece. |
| Sheet Indices (`Sh`) | Integer | list | Sheet index for each placed piece. |
| Cell Areas (`A`) | Number | list | Approximate area of each assigned cell. Useful to spot  outliers where the piece is much smaller / bigger than the  cell. |
| Report (`R`) | Text | item | Catalog packing report. |

### Frahan Trencadís Dynamic Settle  (`TrencadisDyn`)

- GUID: `F2D00008-CADC-4F2D-9008-7E60CADA15A0`  |  icon: `ContactSettle.png`  |  exposure: `tertiary`  |  source: `src/Frahan.StonePack.GH/Pack2DTrencadisDynamicComponent.cs`
- Algorithm: **Trencadis dynamic settle** - F-2D-002.F8 Frahan-original
- Light Kangaroo 2 settle for trimmed trencadís packing.  Each piece is one centroid particle. SphereCollide pushes  overlapping centroids apart, Anchor pulls back to post-  packing centroid, OnCurve sticks boundary-adjacent pieces  to the sheet edge. Pieces translate rigidly so shape and  edge lengths are exactly preserved.

| in | type | access | description |
|---|---|---|---|
| Pieces (`C`) | Curve | list | Trimmed pieces from a trencadís packer. Centroid of each  input curve becomes the anchor target — i.e. the answer  point produced by the boundary packing algorithm upstream. |
| Sheet Outlines (`S`) | Curve | list | Closed planar sheet outlines. OnCurve targets. |
| Sheet Holes (`H`) | Curve | tree | Hole curves as a tree. OnCurve targets too. |
| Apply Physics (`Phys`) | Boolean | item | Master toggle. False = pure pass-through (output = input).  True = solve. |
| Iterations (`Iter`) | Integer | item | Maximum solver step count. Solver early-exits when kinetic  energy drops below 1e-6. Default 100. |
| Anchor Strength (`Anc`) | Number | item | Pull strength on each centroid back toward its post-packing  position. 0 = disabled. Default 0.05. |
| Collide Strength (`Col`) | Number | item | SphereCollide goal strength. Default 1.0. |
| Boundary Pull (`Bp`) | Number | item | OnCurve goal strength for centroids within 1.5× mean radius  of any boundary curve. 0 = disabled. Default 0.5. |
| Collide Radius Factor (`RF`) | Number | item | Multiplier on the mean per-piece bounding radius used as  the SphereCollide radius. >1 = more space between pieces.  <1 = pieces can overlap (centroid distance allowed to be  less than mean radius). Default 1.0. |
| Strict Containment (`Cont`) | Boolean | item | Hard per-vertex boundary collider. When True, after each  Kangaroo step the proposed centroid translation is binary- searched to find the largest fraction that keeps EVERY  vertex of the piece inside at least one Sheet Outline and  outside every Sheet Hole. Pieces whose initial vertices  are already outside the boundary won't move (fraction = 0).  When False, only the soft OnCurve boundary pull on centroids  is applied (legacy behaviour). Default True. |

| out | type | access | description |
|---|---|---|---|
| Settled Pieces (`C`) | Curve | list | Pieces translated by (settledCentroid − originalCentroid).  Shape and edge lengths preserved exactly via rigid translation. |
| Translations (`V`) | Vector | list | Per-piece translation vector applied during settle. |
| Final Centroids (`X`) | Point | list | Per-piece centroid after settling. |
| Final vSum (`v`) | Number | item | Final kinetic-energy sum. < 1e-6 indicates well converged. |
| Residual Overlap (`Res`) | Number | item | Sum of polygon-pair intersection areas after settle. This  is the dynamic tolerance — how much overlap remains in the  tolerance-based packing once Kangaroo has resolved as much  as it can. 0 = clean fit. |
| Report (`R`) | Text | item | Settle report. |

### Frahan Trencadís EdgeMatch  (`TrencEM`)

- GUID: `F2D0000A-CADC-4F2D-900A-7E60CADA15A0`  |  icon: `EdgeMatchSolve.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/TrencadisEdgeMatchComponent.cs`
- Algorithm: **EdgeMatch-powered Trencadis pack** - Frahan-original alternative to Battiato 2013 CVD+GVF stack
- Trencadís packer driven by the EdgeMatch beam-search solver.  Each sheet outline becomes an anchored frame; parts are placed  by their complementary edges against the frame and against  previously-placed parts. Output is deterministic for fixed  input order.

| in | type | access | description |
|---|---|---|---|
| Parts (`P`) | Curve | list | Closed planar shard curves to pack. |
| Sheet Outlines (`S`) | Curve | list | Closed sheet boundary curves. Each becomes an anchored frame  for one independent EdgeMatch run. |
| Joint Width (`J`) | Number | item | Allowed mean edge-to-edge gap (document units). Mapped onto  EdgeMatch's residual threshold: matches further than this are  rejected. Default 0.5. |
| Sample Spacing (`Sp`) | Number | item | Arc-length sample spacing along each contour. Match scanner  resolution. |
| Break Angle (`Ba`) | Number | item | Curvature break-point threshold in degrees per window. |
| Min Segment Length (`Ms`) | Number | item | Below this chord length a segment is treated as noise. |
| Beam Width (`Bw`) | Integer | item | Concurrent beam states retained per iteration. 16 recommended  for Trencadís (more local minima than wood). |
| Max Iterations (`Mi`) | Integer | item | Outer-loop iteration cap. |
| Run (`R`) | Boolean | item | Execute the solver. |
| Non-Crossing (`Nc`) | Boolean | item | Order-preserving rim correspondence. FALSE (default) = free  nearest-point ICP (unchanged behaviour). TRUE = monotone,  non-crossing point pairing between shard edges; more robust on  wiggly / noisy fracture edges where free matching tangles. |

| out | type | access | description |
|---|---|---|---|
| Placed Pieces (`C`) | Curve | list | Shard contours transformed into their solved placements.  Sheet outlines are not included (they are the identity frame). |
| Transforms (`X`) | Transform | list | Per-piece rigid transform. |
| Source Indices (`Src`) | Integer | list | Original Parts list index for each placed piece. |
| Sheet Indices (`Sh`) | Integer | list | Sheet list index this piece was placed onto. |
| Unplaced (`U`) | Curve | list | Source-curve copies of parts that did not find a match on any sheet. |
| Residuals (`Re`) | Number | list | Per-placement ICP residual. |
| Total Residual (`Tr`) | Number | item | Sum of all per-placement residuals across all sheets. |
| Report (`Rp`) | Text | item | Human-readable per-sheet summary. |

### Frahan Trencadís Pack  (`Trencadis`)

- GUID: `F2D00002-CADC-4F2D-9001-7E60CADA15A0`  |  icon: `Trencadis.png`  |  exposure: `secondary`  |  source: `src/Frahan.StonePack.GH/Pack2DTrencadisComponent.cs`
- Algorithm: **Trencadis greedy pack basic** - Gaudi Park Guell broken-tile mosaic technique
- Trencadís ('broken-tile') 2D mosaic packer. Places irregular  pieces with bounded overlap, then boolean-differences  the overlapping bits so pieces butt edge-to-edge with  characteristic chipped fits. Optional grout offset leaves  the mortar gap. Run-gated (set Run=true to pack).

| in | type | access | description |
|---|---|---|---|
| Parts (`P`) | Curve | list | Closed planar shard curves to pack. Irregular shapes welcome —  trencadís is at its best with non-uniform pieces. |
| Sheet Outlines (`S`) | Curve | list | Closed planar sheet boundary curves. The mosaic is built  inside these. |
| Sheet Holes (`H`) | Curve | tree | Hole curves as a tree. Branch {0} = sheet 0, {1} = sheet 1, etc. |
| Spacing (`Gap`) | Number | item | Pre-trim part-to-part clearance. The trim post-pass removes  everything inside this clearance, so think of it as the  MAXIMUM grout gap (actual gap = Grout, see below). |
| Rotations (`R`) | Number | list | Allowed rotation angles in degrees. Default 0/45/90/135 to  encourage varied edge orientation typical of trencadís work. |
| Tolerance (`T`) | Number | item | Geometric tolerance for containment / collision / boolean  difference. |
| Seed (`Seed`) | Integer | item | 0 = deterministic; non-zero changes tie-breaking randomisation  of placement order. |
| Run (`Run`) | Boolean | item | Set to true to execute packing. |
| Max Candidates (`Max`) | Integer | item | Candidate budget per part per rotation. Trencadís typically  needs MORE candidates than V506 because trim acceptance  expands the feasible set. 0 = default (600). |
| Trim Tolerance (`TrimT`) | Number | item | Maximum allowed part-to-part overlap depth (document units)  during placement. Larger = more aggressive chipping, denser  pack. Default 0.2; for meter-scale 0.1–1.0. |
| Grout (`Gr`) | Number | item | Inward offset applied to each piece AFTER trim, to leave the  characteristic trencadís mortar gap. 0 = no grout (raw  edge-to-edge). Default 0.02. |
| Boundary Mode (`BMode`) | Integer | item | 0 = off (interior fill only).  1 = boundary-aware bias: shards with edges matching the sheet  or hole edges are placed first AND auto-rotated to align with  the matched boundary tangent. All candidate sources used.  2 = strict two-phase ring/interior — boundary-worthy shards  use only boundary-anchor candidates first (true ring), then  non-boundary shards fill the interior. Falls back to all  candidates if a phase is saturated.  3 = uniform curve division — divide each boundary curve by  arc length; place each shard with longest edge tangent to  the curve at its assigned position. |
| Min Boundary Affinity (`BAff`) | Number | item | Edge-match score at or above which a shard is considered  boundary-worthy. Range [0, 1]. Only applies when Boundary  Mode > 0. |
| Cut Budget (`Cut`) | Number | item | Battiato 2013 §4 cumulative-cut cap as a fraction of each  shard's area. T_N = budget on a NEW shard's total chipping  across all neighbours; T_P = budget on a PLACED shard  (derived as Cut/2); single-cut caps S_N (Cut/2) and S_P  (Cut/4) cap any one chip. Default 0.35 matches Battiato's  recommended T_N. Lower → less aggressive chipping, more  shard-shape preservation. 0 → no cuts allowed (strict  no-overlap; defeats the trencadís technique). |
| Use CVD Seeds (`CVD`) | Boolean | item | Initialize per-sheet placement using CVD-Lloyd seed points  (blue-noise distribution). Improves coverage uniformity vs  the bbox-corner default starting point. |
| Use GVF Orientation (`GVF`) | Boolean | item | Compute Gradient Vector Flow over each sheet to bias shard  rotation toward the local boundary tangent. Pieces follow  curves like Gaudí's columns. Slower than the discrete  rotation list alone but produces the flow-line look. |
| GVF Smoothness (`GMu`) | Number | item | GVF μ (smoothness regularizer). Lower (0.05–0.15) → field  follows boundary closely, sharper. Higher (0.3–0.5) →  smoother propagation into interior. Default 0.2 (Battiato  2008). |

| out | type | access | description |
|---|---|---|---|
| Trencadís Pieces (`C`) | Curve | list | Final shard curves after trim + grout. Use these for  downstream rendering / fabrication. |
| Pre-Trim Pieces (`C0`) | Curve | list | Placed shards BEFORE the trim post-pass. Useful for  debugging which piece chipped which. |
| Transforms (`X`) | Transform | list | Placement transforms (per source curve). |
| Source Indices (`Src`) | Integer | list | Original input curve index for each placed shard. |
| Sheet Indices (`Sh`) | Integer | list | Sheet index used for each placed shard. |
| Trim Adjacency (`Ta`) | Integer | tree | DataTree per shard: branch i lists the SOURCE indices of  earlier-placed shards that chipped Trencadís Pieces[i]. |
| Unplaced (`U`) | Curve | list | Shards that could not be placed even with trim tolerance. |
| Failure Reasons (`Why`) | Text | list | Reason for each unplaced shard. |
| Sheet Preview (`B`) | Curve | list | Outer sheet and hole preview curves. |
| Report (`R`) | Text | item | Trencadís packing report (counts, timings, trim events). |

### Frahan Trencadís Pipeline  (`TrencadisPipe`)

- GUID: `F2D00009-CADC-4F2D-9009-7E60CADA15A0`  |  icon: `Trencadis.png`  |  exposure: `tertiary`  |  source: `src/Frahan.StonePack.GH/Pack2DTrencadisPipelineComponent.cs`
- Algorithm: **Trencadis greedy pack** - Gaudi Park Guell broken-tile mosaic technique
- All-in-one trencadís pipeline. Deterministic boundary  pack first; if residual overlap remains, Kangaroo 2  settle fills the gaps. Exposes solver controls (kinetic  energy threshold, momentum) for cases where the  deterministic pass alone is insufficient.

| in | type | access | description |
|---|---|---|---|
| Parts (`P`) | Curve | list | Closed planar shard curves to pack. |
| Sheet Outlines (`S`) | Curve | list | Closed planar sheet outlines. |
| Sheet Holes (`H`) | Curve | tree | Hole curves as a tree. |
| Run (`Run`) | Boolean | item | Master toggle. False = no output. |
| Apply Physics (`Phys`) | Boolean | item | Toggle for the dynamic settle stage. Right-click → Show  Physics Tuning to expose strength / convergence / momentum  controls. |
| Live Animate (`Live`) | Boolean | item | Step-by-step animated settle with viewport overlay. Right- click → Show Animation Tuning to expose frame controls. |

| out | type | access | description |
|---|---|---|---|
| Settled Pieces (`C`) | Curve | list | Final pieces. |
| Packed Pieces (`Cp`) | Curve | list | Boundary-packed pieces BEFORE physics. |
| Translations (`V`) | Vector | list | Per-piece translation applied during settle. |
| Final Centroids (`X`) | Point | list | Per-piece centroid after the pipeline. |
| Source Indices (`Src`) | Integer | list | Original input curve index per placed shard. |
| Final vSum (`v`) | Number | item | Final kinetic-energy sum. |
| Residual Overlap (`Res`) | Number | item | Sum of polygon-pair intersection areas after pipeline. |
| Report (`R`) | Text | item | Pipeline report. |
| Pre-Trim Pieces (`Pt`) | Curve | list | Pre-trim placed curves directly out of the boundary  packer (TrencadisFill.PackedCurves). One curve per  placed source. Compare against Packed Pieces (Cp) to see  what was trimmed off near the sheet edge / hole edges. |
| Transforms (`T`) | Transform | list | Per-placed-piece rigid transform from the source curve's  frame to its world placement. One Transform per piece,  parallel to Packed Pieces (Cp) and Source Indices (Src).  Apply to any source-frame geometry (drill points, hatch  patterns) to bring it into the placed frame. |
| Trim Adjacency (`TA`) | Integer | tree | Per-piece tree where branch {i} holds the source-indices  of OTHER pieces that trimmed piece i during the deterministic  boundary pass. Empty branch = piece i was not trimmed  against any other piece. Useful for auditing chain-cut  relationships in trencadís layouts. |


## Voussoir

### Arch Voussoirs  (`ArchVous`)

- GUID: `D5F10012-ED9E-4ED9-A012-ED9EED9E0012`  |  icon: `StereotomyGenerate.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Voussoir/ArchVoussoirsComponent.cs`
- Algorithm: **RadialVoussoirCells** - Frahan-original: intrados curve -> arc-length stations -> 8-vertex wedge solids with radial bed joints (extrados = intrados offset by ring thickness along the outward normal)
- Generate a stereotomic arch as N radial voussoir cells (8-vertex  wedge solids; bed joints normal to the intrados). Profiles:  Semicircular / Segmental / Pointed / Catenary. Outputs the cut-stone  cells plus a typed VoussoirAssembly for Voussoir Stone Matcher + the  rubble match-and-trim (example 21). Grounded in  wiki/research/stereotomy_voussoir_from_rubble.md.

| in | type | access | description |
|---|---|---|---|
| Profile (`Pf`) | Integer | item | Intrados profile: 0 = Semicircular, 1 = Segmental, 2 = Pointed  (equilateral), 3 = Catenary. Default 0. |
| Intrados Radius (`R`) | Number | item | Intrados radius (m). For Catenary this is the span. Default 2.0. |
| Ring Thickness (`t`) | Number | item | Radial thickness intrados->extrados (m). Default 0.55. |
| Width (`w`) | Number | item | Out-of-plane voussoir width (m). Default 0.6. |
| Count (`N`) | Integer | item | Number of voussoirs. Default 11. |
| Included Angle (`A`) | Number | item | Included angle for the Segmental profile (deg, 0..180). Ignored by  the other profiles. Default 120. |
| Rise (`Ri`) | Number | item | Apex rise for the Catenary profile (m). 0 = use Intrados Radius.  Ignored by the other profiles. Default 0. |
| Base Point (`P`) | Point | item | Origin to translate the arch to (built in world XZ, width along Y,  springers on z=0). Default world origin. |

| out | type | access | description |
|---|---|---|---|
| Cells (`C`) | Mesh | list | The voussoir cut-stone cells (closed wedge solids), in install order. |
| Assembly (`VA`) | Generic | item | Typed VoussoirAssembly. Wire into Voussoir Stone Matcher (D5F10010). |
| Bed Planes (`Bp`) | Plane | list | Per-voussoir lower bed-joint plane (radial). The springer's is the springing plane. |
| Centroids (`Ct`) | Point | list | Per-voussoir centroid. |
| Volumes (`V`) | Number | list | Per-voussoir volume (m^3). |
| Keystone (`K`) | Integer | item | Index of the keystone voussoir (nearest the apex). |
| Intrados (`I`) | Curve | item | The intrados (soffit) curve. |
| Report (`R`) | Text | item | Build summary (profile, span, rise, total volume, closedness). |

### Pendentive Vault Voussoirs  (`VaultVous`)

- GUID: `D5F10013-ED9E-4ED9-A013-ED9EED9E0013`  |  icon: `Voussoir.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Voussoir/PendentiveVaultVoussoirsComponent.cs`
- Algorithm: **PendentiveDomeCells** - Frahan-original: square plan grid lifted onto a sphere (z=sqrt(R^2-x^2-y^2)) then extruded radially by the shell thickness -> 8-vertex cells along lines of curvature
- Generate a pendentive (sail) dome (sphere over a square) tessellated  on a grid into voussoir cells along the sphere's lines of curvature,  each extruded radially by the shell thickness. Outputs the cut-stone  cells plus a typed VoussoirAssembly for Voussoir Stone Matcher + the  rubble match-and-trim (example 22). Grounded in  wiki/research/stereotomy_voussoir_from_rubble.md.

| in | type | access | description |
|---|---|---|---|
| Sphere Radius (`R`) | Number | item | Sphere radius (m). Default 2.5. |
| Square Half Width (`h`) | Number | item | Half the side of the square plan (m). Must satisfy 2*h^2 < R^2 so the  corners lie on the sphere. Default 1.6. |
| Shell Thickness (`t`) | Number | item | Radial shell thickness intrados->extrados (m). Default 0.4. |
| Grid U (`U`) | Integer | item | Cells across the plan in U. Default 6. |
| Grid V (`V`) | Integer | item | Cells across the plan in V. Default 6. |
| Drop To Ground (`D`) | Boolean | item | Translate so the springing corners rest on z=0. Default true. |
| Base Point (`P`) | Point | item | Origin to translate the vault to. Default world origin. |

| out | type | access | description |
|---|---|---|---|
| Cells (`C`) | Mesh | list | The voussoir cut-stone cells (closed solids), one per grid cell. |
| Assembly (`VA`) | Generic | item | Typed VoussoirAssembly. Wire into Voussoir Stone Matcher (D5F10010). |
| Bed Planes (`Bp`) | Plane | list | Per-cell intrados (bed) plane: centre + outward sphere radial. |
| Centroids (`Ct`) | Point | list | Per-cell centroid. |
| Volumes (`V`) | Number | list | Per-cell volume (m^3). |
| Report (`R`) | Text | item | Build summary (R, span, thickness, grid, springing/apex, total volume). |

### Voussoir Ingest  (`VousIngest`)

- GUID: `D5F1000F-ED9E-4ED9-A00F-ED9EED9E000F`  |  icon: `EdgeMatchSolve.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Voussoir/VoussoirIngestComponent.cs`
- Algorithm: **Bed-Head plane detection via largest-face heuristic** - Frahan-original: sort voussoir faces by area; bed = largest, head = second-largest
- Read a list of voussoir meshes (from the Voussoir GH plugin or  Frahan Stereotomic Vault Mode) as a typed VoussoirAssembly.  Per-voussoir record carries OBB + volume + centroid + bed/head  planes + load axis + joint class. Emits MatchItem[] for downstream  MatcherContextBuilder (the substrate spine). First step of the  top-down voussoir-to-stone workflow per philosophy doc §10.6.

| in | type | access | description |
|---|---|---|---|
| Voussoirs (`V`) | Generic | list | List of voussoir geometries representing the designed stereotomic  assembly. Accepts EITHER Mesh OR Brep. The Voussoir GH plugin by  Varela (FAUP Porto STBIM) emits BREP per voussoir as a GH data  tree -- this component handles both natively. Closed solids  preferred; Breps are meshed via Mesh.CreateFromBrep at default  MeshingParameters quality. |
| Joint Classes (`JC`) | Text | list | Optional per-voussoir position-role tags: 'bed' / 'head' / 'key' /  'ground' / 'void' (default 'void'). Same count as Voussoirs (or  empty / single-value for default). |
| Thrust Curve (`Tc`) | Curve | item | Optional funicular thrust curve (from TNA form-finding or hand  drafting). Drives LoadAxis per voussoir via closest-point-tangent. |
| Lithology Hints (`Lh`) | Text | list | Optional per-voussoir lithology constraint (e.g. 'Vermont Marble').  Used as a categorical constraint in the matcher. |
| Ground Anchor Indices (`Ga`) | Integer | list | Optional indices of springer / abutment voussoirs (start points  of the install DAG). Empty = auto-detect via lowest centroid Z. |
| Adjacency Threshold (`Ad`) | Number | item | Fraction of face-diagonal for adjacency detection. Default 0.05  (5% of object span). Faces within this distance count as a shared joint. |
| Provenance (`Pr`) | Text | item | Optional provenance string for the assembly (e.g. 'Voussoir plugin v2.3 output'). |

| out | type | access | description |
|---|---|---|---|
| Assembly (`VA`) | Generic | item | The typed VoussoirAssembly. Wire into VoussoirStoneMatcher +  VoussoirPackIntoBlock downstream. |
| Match Items (`MI`) | Generic | list | List of MatchItem (substrate-compatible). Wire into MatcherContextBuilder  as the Demand side. Numeric props: Volume, MaxDim, MinDim, Height.  Categorical: JointClass, LithologyHint. |
| OBBs (`B`) | Box | list | Per-voussoir oriented bounding boxes (AABB v1). |
| Volumes (`Vo`) | Number | list | Per-voussoir mesh volume (absolute). |
| Centroids (`C`) | Point | list | Per-voussoir geometric centroid. |
| Bed Planes (`Bp`) | Plane | list | Per-voussoir bed-joint plane (largest-area face heuristic v1). |
| Head Planes (`Hp`) | Plane | list | Per-voussoir head-joint plane (second-largest-area face heuristic v1). |
| Load Axes (`La`) | Vector | list | Per-voussoir compressive-load direction (thrust-curve tangent if  supplied, else OBB longest-axis). |
| Adjacency Pairs (`Ap`) | Integer | list | Pairs of voussoir indices that share a joint face (flat list:  [i0, j0, i1, j1, ...]). |
| Remarks (`R`) | Text | list | Per-voussoir + assembly-level diagnostic notes. |

### Voussoir Pack Into Block  (`VousPack`)

- GUID: `D5F10011-ED9E-4ED9-A011-ED9EED9E0011`  |  icon: `EdgeMatchSolve.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Voussoir/VoussoirPackIntoBlockComponent.cs`
- Algorithm: **Greedy first-fit-decreasing 3D bin-pack** - Frahan-original v1 packing; foundational reference: Garey-Johnson 1979 FFD
- Pack as many voussoirs as possible into a single quarried block.  Greedy first-fit-decreasing on AABB extents (v1; v2 routes through  BlockPackTree DLBF + CGAL exact-shape fit). Outputs: placed  voussoir indices + per-voussoir transforms + cut-plane plan +  achieved yield ratio. Use case: extract all voussoirs of a vault  from one large quarry block (Quarra Two Horse Relief pattern).

| in | type | access | description |
|---|---|---|---|
| Assembly (`VA`) | Generic | item | VoussoirAssembly from VoussoirIngestComponent (D5F1000F). |
| Block (`B`) | Generic | item | A single quarried block (accepts QuarryBlock typed record from  ScanToBlockInventoryComponent F2D0BC20, OR a raw Mesh). |
| Spacing (`S`) | Number | item | Gap between adjacent voussoirs (mm), used as the saw-kerf +  carving allowance. Default 5.0 mm. |
| Grid Step (`G`) | Number | item | Grid step for the candidate-position scan (mm). Smaller = denser  search = slower. Default 25.0 mm. |
| Allow Skip (`As`) | Boolean | item | If true, voussoirs that cannot be placed are skipped + reported.  If false, fail loudly when any voussoir cannot be placed. Default true. |

| out | type | access | description |
|---|---|---|---|
| Placed Voussoirs (`PV`) | Mesh | list | Per-voussoir transformed mesh (placed in the block's local frame).  Null where the voussoir could not be placed. |
| Transforms (`T`) | Transform | list | Per-voussoir placement transform (identity where unplaced). |
| Fit Voussoir Indices (`Fi`) | Integer | list | Indices of voussoirs that were successfully placed (sorted by  placement order = volume descending). |
| Skip Voussoir Indices (`Si`) | Integer | list | Indices of voussoirs that could NOT be placed (under-provisioned). |
| Yield Ratio (`Y`) | Number | item | Sum-of-placed-volumes / block-volume. >= 0.4 is typically  production-acceptable per UCL Devadass 2025 §2.7. |
| Cut Planes (`Cp`) | Plane | list | Cut-plan: one plane per adjacent-voussoir-pair joint inside the  block. The cutting sequence is implied by placement order. |
| Remarks (`R`) | Text | list | Diagnostic notes -- voussoir count placed/skipped, yield,  block fill rate. |

### Voussoir Stone Matcher  (`VousMatch`)

- GUID: `D5F10010-ED9E-4ED9-A010-ED9EED9E0010`  |  icon: `EdgeMatchSolve.png`  |  exposure: `primary`  |  source: `src/Frahan.StonePack.GH/Voussoir/VoussoirStoneMatcherComponent.cs`
- Algorithm: **Tomczak2023Matching** - Tomczak/Haakonsen/Luczkowski 2023 Environ. Res. Infrastruct. Sustain. 3:035005 DOI 10.1088/2634-4505/acf341 -- Figure 2 5-stage matching pipeline
- Assign each voussoir to a quarry stone via Kuhn 1955 Hungarian  bipartite assignment. Voussoirs are demand; stones are supply;  feasibility = stone OBB contains voussoir OBB + safety margin +  yield_ratio >= MinYield; cost = w_yield * (1 - yield_ratio) +  w_carving * (carving_vol / voussoir_vol). The canonical top-down  voussoir-to-stone matcher per wiki/research/voussoir_stereotomy_integration.md  Phase 2 + philosophy doc §10.6. First production use of the  MatcherRegistry substrate.

| in | type | access | description |
|---|---|---|---|
| Assembly (`VA`) | Generic | item | VoussoirAssembly from VoussoirIngestComponent (D5F1000F). |
| Quarry Stones (`QS`) | Generic | list | List of quarry-block candidates. Accepts either:  (a) QuarryBlock typed records from ScanToBlockInventoryComponent  (F2D0BC20), or (b) raw Mesh inputs (in which case AABB+volume  are computed inline). Mixed lists are accepted. |
| Min Yield (`MY`) | Number | item | Minimum yield ratio (voussoir_vol / stone_vol) for a feasible pair.  Default 0.4 (40%). Stones below this are excluded as wasteful. |
| Safety Margin (`SM`) | Number | item | Safety margin added to voussoir OBB extent before containment test  (mm). Default 5.0. |
| Yield Weight (`Wy`) | Number | item | Cost weight for the yield term `1 - yield_ratio`. Default 1.0. |
| Carving Weight (`Wc`) | Number | item | Cost weight for the carving term `(stone_vol - voussoir_vol) / voussoir_vol`.  Default 0.5. |
| Allow Empty (`Ae`) | Boolean | item | If true, unassigned voussoirs are reported (under-provisioned case).  If false, fail loudly when any voussoir would remain unassigned.  Default true. |

| out | type | access | description |
|---|---|---|---|
| Assignment (`A`) | Integer | list | Per-voussoir stone index (-1 = unassigned). |
| Placed Stones (`PS`) | Mesh | list | Per-voussoir assigned stone mesh (null where unassigned). |
| Yield Ratios (`Y`) | Number | list | Per-voussoir yield ratio (voussoir_vol / stone_vol). 0 if unassigned. |
| Carving Volumes (`Cv`) | Number | list | Per-voussoir carving volume (stone_vol - voussoir_vol). 0 if unassigned. |
| Per-Pair Cost (`Pc`) | Number | list | Per-voussoir total cost (yield + carving weighted sum).  +Inf where unassigned / infeasible. |
| Unassigned Voussoirs (`Uv`) | Integer | list | Indices of voussoirs that received no stone (under-provisioned). |
| Unused Stones (`Us`) | Integer | list | Indices of stones that were not consumed. |
| Total Cost (`Tc`) | Number | item | Sum of per-pair costs across the assignment. |
| Remarks (`R`) | Text | list | Diagnostic notes -- strategy, infeasibility reasons, M*N matrix size. |
