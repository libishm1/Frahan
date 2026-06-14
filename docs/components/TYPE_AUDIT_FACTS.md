# Data-structure facts (pre-computed from components.json)

187 components, 2028 ports. Type frequency:

- Number: 580
- Integer: 410
- Text: 253
- Mesh: 169
- Boolean: 161
- Curve: 142
- Generic: 115
- Point: 63
- Plane: 30
- Transform: 29
- Box: 28
- Vector: 21
- Geometry: 15
- Line: 5
- Brep: 3
- Rectangle: 2
- Interval: 1
- Surface: 1

## Concepts that appear as MORE THAN ONE type (40)

These are the canonical-type inconsistency candidates: the same named port modelled as different GH types across components.

### `container` -> Box, Integer, Mesh  (7 ports)
- Mesh     in   CoM In-Container Check :: Container
- Mesh     in   Settle 3D (Physics) :: Container
- Box      in   Pack3D Irregular :: Container
- Box      in   Pack3D Mesh Heightmap :: Container
- Box      in   Block Pack (Tree) :: Containers
- Integer  in   IFC Export (Building) :: Containers
- Integer  in   IFC Export (Stone Assembly) :: Container

### `placed` -> Curve, Integer, Mesh  (5 ports)
- Curve    out  Sheet Nest (Hole-Aware) :: Placed
- Curve    out  EdgeMatch Solve :: Placed
- Integer  out  Live Edge Match :: Placed
- Mesh     out  Stone-Cell Match (Λ) :: Placed
- Mesh     out  Fit In Block :: Placed

### `report` -> Generic, Text  (86 ports)
- Text     out  2D Bottom Left Pack :: Report
- Text     out  2D Freeform Sheet Pack :: Report
- Text     out  2D Freeform Sheet Pack V3 :: Report
- Text     out  2D Irregular Sheet Pack :: Report
- Text     out  2D NFP Pack :: Report
- Text     out  CSV Parts Reader :: Report
- Text     out  Frahan Residual Voids :: Report
- Text     out  Frahan Sheet Pack (Unified Async) :: Report
- Text     out  Frahan Sheet Pack (Unified) :: Report
- Text     out  Freeform Sheet Nest :: Report
- Text     out  Freeform Sheet Nest (Exact NFP) :: Report
- Text     out  Sheet Nest (Hole-Aware) :: Report
- Text     out  Frahan Stone Descriptor :: Report
- Text     out  Settle 3D (Physics) :: Report
- Text     out  Validate Packed Transform :: Report
- Text     out  Frahan Boundary Rail Index :: Report
- Text     out  Frahan Fragment Descriptors :: Report
- Text     out  Frahan Fragment Edge Match :: Report
- Text     out  EdgeMatch Solve :: Report
- Text     out  Live Edge Stagger Layup :: Report
- Text     out  Fabrication Prep Report :: Report
- Text     out  Staggered Block Decompose :: Report
- Text     out  Stone-Aware Cut Export :: Report
- Text     out  GPR Radargram Mesh :: Report
- Text     out  Contact Settle :: Report
- Text     out  Fracture Roughen :: Report
- Text     out  Frahan Fragment Shatter :: Report
- Text     out  Frahan Kintsugi :: Report
- Text     out  Load BB Sample :: Report
- Text     out  Load Scan Fragments :: Report
- Text     out  Synthetic Block :: Report
- Text     out  Download Frahan Data :: Report
- Text     out  Mesh CSG (CGAL) :: Report
- Text     out  Mesh Decimate (Geogram) :: Report
- Text     out  Mesh Decompose (CoACD) :: Report
- Text     out  Mesh Repair (Auto) :: Report
- Text     out  Block Pack (Tree) :: Report
- Text     out  Block Size Distribution :: Report
- Text     out  Build-Order Stability Stream :: Report
- Text     out  Cut Validation :: Report
- Text     out  IFC Export (Building) :: Report
- Text     out  IFC Export (Stone Assembly) :: Report
- Text     out  Masonry Stability (RBE) :: Report
- Text     out  Masonry Stability Check :: Report
- Text     out  Mesh Quality Report :: Report
- Text     out  Polygonal Wall (Generator) :: Report
- Text     out  Stone-Cell Match (Λ) :: Report
- Text     out  Clip Boxes By Mesh :: Report
- Text     out  Cloud ICP :: Report
- Text     out  Estimate Cloud Normals :: Report
- Text     out  Frahan Mesh Diagnostics :: Report
- Text     out  Georeference :: Report
- Text     out  Georeference (Align by Points) :: Report
- Text     out  Sanitize Mesh :: Report
- Text     out  Scan Reconstruct :: Report
- Text     out  Scan Scale Calibrate :: Report
- Text     out  Clean Scan Mesh :: Report
- Text     out  Discontinuity Ingest :: Report
- Text     out  Discontinuity Sets (Async) :: Report
- Text     out  Discontinuity Sets (Cloud) :: Report
- Text     out  Fracture Block Pack :: Report
- Text     out  GPR Bedrock Surface :: Report
- Text     out  GPR Fracture Extract :: Report
- Text     out  GPR Fracture Surfaces 3D :: Report
- Text     out  GPR Fractures on Mesh :: Report
- Text     out  Joint Sets to DFN :: Report
- Text     out  Overburden To Rock Face :: Report
- Text     out  Quarry Decompose By CoACD :: Report
- Text     out  Scan to Block Inventory :: Report
- Text     out  Stereonet + Block Size :: Report
- Text     out  Stochastic DFN (Baecher) :: Report
- Text     out  Frahan Packing Report :: Report
- Text     out  Fit In Block :: Report
- Text     out  Slab Cut By Tool Mesh (CGAL) :: Report
- Text     out  Frahan Chart Flatness Report :: Report
- Text     out  Pack On Surface :: Report
- Text     out  Pack Surfaces :: Report
- Text     out  Surface Chart :: Report
- Text     out  Frahan Trencadís Catalog Pack :: Report
- Text     out  Frahan Trencadís Dynamic Settle :: Report
- Text     out  Frahan Trencadís EdgeMatch :: Report
- Text     out  Frahan Trencadís Pack :: Report
- Text     out  Frahan Trencadís Pipeline :: Report
- Text     out  Arch Voussoirs :: Report
- Text     out  Pendentive Vault Voussoirs :: Report
- Generic  in   Frahan Report / Export :: Reports

### `transform` -> Generic, Transform  (31 ports)
- Generic  out  2D Bottom Left Pack :: Transforms
- Generic  out  2D Freeform Sheet Pack :: Transforms
- Generic  out  2D Freeform Sheet Pack V3 :: Transforms
- Generic  out  2D Irregular Sheet Pack :: Transforms
- Generic  out  2D NFP Pack :: Transforms
- Generic  out  Frahan Sheet Pack (Unified Async) :: Transforms
- Generic  out  Frahan Sheet Pack (Unified) :: Transforms
- Generic  out  Freeform Sheet Nest :: Transforms
- Generic  out  Freeform Sheet Nest (Exact NFP) :: Transforms
- Generic  out  EdgeMatch Solve :: Transforms
- Generic  out  Frahan Trencadís EdgeMatch :: Transforms
- Generic  out  Frahan Trencadís Pack :: Transforms
- Transform out  Sheet Nest (Hole-Aware) :: Transform
- Transform out  Pack3D Irregular :: Transforms
- Transform out  Pack3D Irregular Container :: Transforms
- Transform out  Pack3D Mesh Heightmap :: Transforms
- Transform out  Settle 3D (Physics) :: Transforms
- Transform in   Validate Packed Transform :: Transforms
- Transform out  Block Pair Match 3D :: Transforms
- Transform out  Contact Settle :: Transforms
- Transform out  Frahan Kintsugi :: Transforms
- Transform out  Block Ground Transforms :: Transforms
- Transform out  Block Pack (Tree) :: Transforms
- Transform out  Match Block Transform :: Transforms
- Transform out  Cloud ICP :: Transform
- Transform out  Georeference :: Transform
- Transform out  Georeference (Align by Points) :: Transform
- Transform out  Marker Registration :: Transform
- Transform out  Move to Origin :: Transform
- Transform out  Frahan Trencadís Pipeline :: Transforms
- Transform out  Voussoir Pack Into Block :: Transforms

### `seed` -> Integer, Point  (30 ports)
- Integer  in   2D Bottom Left Pack :: Seed
- Integer  in   2D Freeform Sheet Pack :: Seed
- Integer  in   2D Freeform Sheet Pack V3 :: Seed
- Integer  in   2D Irregular Sheet Pack :: Seed
- Integer  in   2D NFP Pack :: Seed
- Integer  in   Frahan Sheet Pack (Unified Async) :: Seed
- Integer  in   Frahan Sheet Pack (Unified) :: Seed
- Integer  in   Freeform Sheet Nest :: Seed
- Integer  in   Freeform Sheet Nest (Exact NFP) :: Seed
- Integer  in   Pack3D Irregular Container :: Seed
- Integer  in   Pack3D Mesh Heightmap :: Seed
- Integer  in   Live Edge Match :: Seed
- Integer  in   Live Edge Stagger Layup :: Seed
- Integer  in   Jittered Grid Fracture Planes :: Seed
- Integer  in   Random Fracture Planes :: Seed
- Integer  in   Fracture Roughen :: Seed
- Integer  in   Frahan Fragment Shatter :: Seed
- Integer  in   Synthetic Block :: Seed
- Integer  in   Mesh Decompose (CoACD) :: Seed
- Integer  in   Block Pack (Tree) :: Seed
- Integer  in   Polygonal Wall (Generator) :: Seed
- Integer  in   Joint Sets to DFN :: Seed
- Integer  in   Quarry DFN :: Seed
- Integer  in   Quarry Decompose By CoACD :: Seed
- Integer  in   Stochastic DFN (Baecher) :: Seed
- Integer  in   Pack On Surface :: Seed
- Integer  in   Pack Surfaces :: Seed
- Integer  in   Frahan Trencadís Catalog Pack :: Seed
- Integer  in   Frahan Trencadís Pack :: Seed
- Point    in   Voronoi Fracture Planes :: Seeds

### `plan` -> Generic, Plane  (17 ports)
- Plane    out  G-code to Planes :: Planes
- Plane    in   Planes to KUKAprc Commands :: Planes
- Plane    out  Planes to KUKAprc Commands :: Planes
- Plane    in   Planes to Robot Targets :: Planes
- Plane    out  Planes to Robot Targets :: Planes
- Plane    out  Wire-Saw Toolpath :: Planes
- Plane    out  Discontinuity Ingest :: Planes
- Plane    out  Vertical Fracture Planes From Curves :: Planes
- Generic  out  Brick-Pattern Fracture Planes :: Planes
- Generic  in   Fracture Plane Filter :: Planes
- Generic  out  Fracture Plane Filter :: Planes
- Generic  out  Grid Fracture Planes :: Planes
- Generic  out  Jittered Grid Fracture Planes :: Planes
- Generic  out  Layered Fracture Planes :: Planes
- Generic  out  Radial Fracture Planes :: Planes
- Generic  out  Random Fracture Planes :: Planes
- Generic  out  Voronoi Fracture Planes :: Planes

### `slab` -> Generic, Mesh  (16 ports)
- Generic  in   Brick-Pattern Fracture Planes :: Slab
- Generic  in   Fracture Plane Filter :: Slab
- Generic  in   Grid Fracture Planes :: Slab
- Generic  in   Jittered Grid Fracture Planes :: Slab
- Generic  in   Layered Fracture Planes :: Slab
- Generic  in   Random Fracture Planes :: Slab
- Generic  out  Slab Cut By Fracture Polygons :: Slab
- Generic  in   Block Size Distribution :: Slabs
- Generic  in   Fragment Merger :: Slabs
- Generic  out  Convex Hull Slab :: Slab
- Generic  out  Mesh Shell Split :: Slabs
- Generic  out  Quarry DFN :: Slabs
- Generic  out  Quarry Decompose :: Slabs
- Generic  out  Slab Cut By Fractures :: Slab
- Generic  out  Slab From Mesh :: Slab
- Mesh     in   Slab Cut By Tool Mesh (CGAL) :: Slab

### `block` -> Generic, Mesh  (11 ports)
- Mesh     in   Fabrication Prep Report :: Blocks
- Mesh     out  Synthetic Block :: Blocks
- Mesh     out  Fracture Block Pack :: Blocks
- Mesh     in   Frahan Slab Yield Optimizer :: Block
- Mesh     out  Quarry DFN :: Blocks
- Mesh     out  Quarry Decompose By CoACD :: Blocks
- Mesh     in   Carving Stages :: Block
- Mesh     in   Fit In Block :: Block
- Generic  in   Masonry Assembly :: Blocks
- Generic  out  Masonry Block :: Block
- Generic  in   Voussoir Pack Into Block :: Block

### `ston` -> Curve, Mesh  (9 ports)
- Mesh     in   Frahan Stone Descriptor :: Stones
- Mesh     in   IFC Export (Building) :: Stones
- Mesh     in   IFC Export (Stone Assembly) :: Stones
- Mesh     in   Masonry Stability Check :: Stones
- Mesh     out  Polygonal Masonry Sequence 3D :: Stones
- Mesh     out  Polygonal Wall (Generator) :: Stones
- Mesh     in   Rubble Wall Settle :: Stones
- Mesh     in   Stone-Cell Match (Λ) :: Stones
- Curve    out  Polygonal Masonry Sequence :: Stones

### `fragment` -> Curve, Mesh  (9 ports)
- Curve    in   Frahan Fragment Descriptors :: Fragments
- Curve    in   Frahan Fragment Edge Match :: Fragments
- Mesh     in   Soft ICP 3D :: Fragments
- Mesh     in   Contact Settle :: Fragments
- Mesh     in   Fracture Roughen :: Fragments
- Mesh     out  Frahan Fragment Shatter :: Fragments
- Mesh     in   Frahan Kintsugi :: Fragments
- Mesh     out  Load BB Sample :: Fragments
- Mesh     out  Load Scan Fragments :: Fragments

### `mode` -> Integer, Text  (8 ports)
- Text     out  EdgeMatch Segments :: Mode
- Integer  in   Live Edge Match :: Mode
- Integer  in   Live Edge Stagger Layup :: Mode
- Integer  in   Mesh Decimate (Geogram) :: Mode
- Integer  in   Scan Reconstruct :: Mode
- Integer  in   Carving Stages :: Mode
- Integer  in   Enlarge Sculpture :: Mode
- Integer  in   Slab Cut By Tool Mesh (CGAL) :: Mode

### `depth` -> Integer, Number  (7 ports)
- Number   out  Live Edge Trim :: Depth
- Number   in   Polygonal Wall (Generator) :: Depth
- Number   out  Mesh AABB :: Depth
- Number   in   GPR Bedrock Surface :: Depths
- Number   out  GPR Fracture Extract :: Depths
- Integer  out  Polygonal Masonry Sequence :: Depth
- Integer  out  Polygonal Masonry Sequence 3D :: Depth

### `backend` -> Integer, Text  (7 ports)
- Text     out  Mesh CSG (CGAL) :: Backend
- Text     out  Mesh Decimate (Geogram) :: Backend
- Text     out  Mesh Decompose (CoACD) :: Backend
- Text     out  Mesh Repair (Auto) :: Backend
- Text     out  Frahan Photo Detect → PLY :: Backend
- Text     out  Slab Cut By Tool Mesh (CGAL) :: Backend
- Integer  in   Sanitize Mesh :: Backend

### `fractur` -> Integer, Mesh  (6 ports)
- Mesh     in   Frahan Pareto Front Inspector :: Fractures
- Mesh     in   BlockCutOpt Extract Grid :: Fractures
- Mesh     out  BlockCutOpt Load Fractures :: Fractures
- Mesh     out  Frahan Photo Detect → PLY :: Fractures
- Integer  out  Joint Sets to DFN :: Fractures
- Integer  out  Stochastic DFN (Baecher) :: Fractures

### `clearance` -> Number, Vector  (5 ports)
- Number   in   Pack3D Irregular :: Clearance
- Number   in   Pack3D Irregular Container :: Clearance
- Number   in   Pack3D Mesh Heightmap :: Clearance
- Number   out  Rubble Wall Settle :: Clearance
- Vector   out  Fit In Block :: Clearance

### `cell` -> Box, Mesh  (5 ports)
- Box      out  Staggered Block Decompose :: Cells
- Mesh     in   Polygonal Masonry Sequence 3D :: Cells
- Mesh     in   Stone-Cell Match (Λ) :: Cells
- Mesh     out  Arch Voussoirs :: Cells
- Mesh     out  Pendentive Vault Voussoirs :: Cells

### `result` -> Generic, Mesh  (5 ports)
- Mesh     out  Mesh CSG (CGAL) :: Result
- Generic  out  Ashlar Pack :: Result
- Generic  out  Best Fit Pack :: Result
- Generic  in   Pack Diagnostics :: Result
- Generic  in   Pack Preview :: Result

### `plane` -> Generic, Plane  (4 ports)
- Plane    in   Mesh Planar Polygon Extractor :: Plane
- Plane    in   Polygon Sanitize :: Plane
- Plane    in   Stereonet + Block Size :: Plane
- Generic  in   Slab Cut By Fractures :: Plane

### `sheet` -> Curve, Integer  (3 ports)
- Curve    in   Frahan Residual Voids :: Sheet
- Curve    in   Sheet Nest (Hole-Aware) :: Sheets
- Integer  out  Sheet Nest (Hole-Aware) :: Sheet

### `frame` -> Curve, Plane  (3 ports)
- Curve    in   EdgeMatch Solve :: Frame
- Plane    out  Mesh PCA :: Frame
- Plane    out  Scan to Block Inventory :: Frame

### `inventory` -> Generic, Mesh  (3 ports)
- Generic  out  Frahan Monument Inventory :: Inventory
- Generic  out  Frahan Quarry Inventory :: Inventory
- Mesh     in   Best Fit Pack :: Inventory

### `axi` -> Integer, Vector  (3 ports)
- Integer  in   Layered Fracture Planes :: Axis
- Integer  out  Frahan Slab Yield Optimizer :: Axis
- Vector   in   Radial Fracture Planes :: Axis

### `pick` -> Plane, Point  (3 ports)
- Plane    out  Pick Place Frames :: Pick
- Point    in   GPR Bedrock Surface :: Picks
- Point    in   GPR Fractures on Mesh :: Picks

### `target` -> Mesh, Point  (3 ports)
- Point    in   Georeference (Align by Points) :: Target
- Point    in   Move to Origin :: Target
- Mesh     in   Carving Stages :: Target

### `set pol` -> Line, Point  (3 ports)
- Line     out  Discontinuity Sets (Async) :: Set poles
- Line     out  Discontinuity Sets (Cloud) :: Set poles
- Point    out  Stereonet + Block Size :: Set poles

### `quarry` -> Generic, Mesh  (3 ports)
- Mesh     in   Quarry DFN :: Quarry
- Mesh     in   Quarry Decompose By CoACD :: Quarry
- Generic  in   Quarry Decompose :: Quarry

### `scale` -> Boolean, Number  (2 ports)
- Number   in   CSV Parts Reader :: Scale
- Boolean  in   Georeference (Align by Points) :: Scale

### `source` -> Integer, Point  (2 ports)
- Integer  out  Sheet Nest (Hole-Aware) :: Source
- Point    in   Georeference (Align by Points) :: Source

### `inside` -> Boolean, Mesh  (2 ports)
- Boolean  out  CoM In-Container Check :: Inside
- Mesh     out  Slab Cut By Tool Mesh (CGAL) :: Inside

### `placed block` -> Generic, Mesh  (2 ports)
- Mesh     out  Adaptive Block Match 3D :: Placed Block
- Generic  out  Pack Diagnostics :: Placed Blocks

### `board` -> Curve, Mesh  (2 ports)
- Mesh     out  Live Edge Stagger Layup :: Boards
- Curve    in   Live Edge Trim :: Board

### `radargram` -> Generic, Mesh  (2 ports)
- Mesh     out  GPR Radargram Mesh :: Radargram
- Generic  out  Frahan GPR Radargram Reader :: Radargram

### `project` -> Integer, Text  (2 ports)
- Text     in   IFC Export (Building) :: Project
- Integer  in   GPR Fractures on Mesh :: Project

### `place` -> Boolean, Plane  (2 ports)
- Plane    out  Pick Place Frames :: Place
- Boolean  in   Fit In Block :: Place

### `surface` -> Mesh, Surface  (2 ports)
- Surface  in   Polygonal Wall (Generator) :: Surface
- Mesh     in   Surface Chart :: Surface

### `anchor` -> Integer, Point  (2 ports)
- Integer  in   Move to Origin :: Anchor
- Point    in   Enlarge Sculpture :: Anchor

### `scale factor` -> Number, Vector  (2 ports)
- Number   out  Scan Scale Calibrate :: Scale Factor
- Vector   out  Enlarge Sculpture :: Scale Factors

### `cut plan` -> Generic, Plane  (2 ports)
- Generic  out  Frahan Slab Yield Optimizer :: Cut Planes
- Plane    out  Voussoir Pack Into Block :: Cut Planes

### `net` -> Curve, Number  (2 ports)
- Number   out  Overburden To Rock Face :: Net
- Curve    out  Stereonet + Block Size :: Net

### `stag` -> Integer, Mesh  (2 ports)
- Integer  in   Carving Stages :: Stages
- Mesh     out  Carving Stages :: Stages

## Generic / untyped ports (115)

- out 2D Bottom Left Pack :: Transforms (X) - Placement transforms applied to source curves.
- out 2D Freeform Sheet Pack :: Transforms (X) - Placement transforms applied to each source curve.
- out 2D Freeform Sheet Pack V3 :: Transforms (X) - Placement transforms applied to each source curve.
- out 2D Irregular Sheet Pack :: Transforms (X) - Placement transforms applied to source curves.
- out 2D NFP Pack :: Transforms (X) - Placement transforms applied to source curves.
- out Frahan Sheet Pack (Unified Async) :: Transforms (X) - Placement transforms applied to each source curve.
- out Frahan Sheet Pack (Unified) :: Transforms (X) - Placement transforms applied to each source curve.
- out Freeform Sheet Nest :: Transforms (X) - Placement transforms applied to each source curve.
- out Freeform Sheet Nest (Exact NFP) :: Transforms (X) - Placement transforms per source curve.
- out Frahan Stone Descriptor :: Descriptors (D) - StoneDescriptor per stone (opaque).
- out Pack3D Irregular :: Pack Result (PR) - Opaque PackResult for downstream Frahan Packing Report.
- out Pack3D Irregular Container :: Pack Result (PR) - Opaque PackResult for downstream Frahan Packing Report.
- out Pack3D Mesh Heightmap :: Pack Result (PR) - Opaque PackResult for downstream Frahan Packing Report.
- out Frahan Boundary Rail Index :: Index (I) - Populated BoundaryRailIndex&lt;BoundaryIntervalInfo&gt; (opaque).
- out Frahan Fragment Descriptors :: Descriptors (D) - FragmentDescriptor per fragment (opaque).
- in  Frahan Fragment Edge Match :: Index (I) - Populated BoundaryRailIndex<BoundaryIntervalInfo> from Frahan Boundary Rail Inde
- out EdgeMatch Options :: Options (O) - AssemblyOptions DTO bundling the advanced EdgeMatch flags. Wire  into EdgeMatch 
- in  EdgeMatch Solve :: Options (Opt) - Optional AssemblyOptions DTO from EdgeMatch Options. When wired,  its advanced f
- out EdgeMatch Solve :: Transforms (X) - Per-panel rigid transform.
- out Frahan Monument Inventory :: Inventory (Inv) - MonumentInventory.
- out G-code Parser :: Cut Path (CP) - The typed CutPath record. Wire into GCodeToPlanesComponent or  WireSawToolpathAd
- in  G-code to Planes :: Cut Path (CP) - The typed CutPath from GCodeParserComponent (D5F10030).
- in  Planes to KUKAprc Commands :: Cut Path (optional) (CP) - Optional: the source CutPath typed record (from GCodeParser  D5F10030). If wired
- in  Planes to Robot Targets :: Cut Path (optional) (CP) - Optional CutPath typed record (from GCodeParser D5F10030). If  wired, the wrappe
- in  Brick-Pattern Fracture Planes :: Slab (S) - Slab DTO whose bounding box seeds the brick grid.
- out Brick-Pattern Fracture Planes :: Planes (P) - FracturePlane DTOs in a running-bond pattern.
- in  Fracture Plane Filter :: Planes (P) - Input FracturePlane DTOs (any source).
- in  Fracture Plane Filter :: Slab (S) - Target Slab whose bounding box drives the filter.
- out Fracture Plane Filter :: Planes (P) - Filtered FracturePlane DTOs (only those intersecting the slab AABB).
- out Fracture Polygon From Curve :: FracturePolygon (F) - FracturePolygon DTO. Wire into Slab Cut By Fracture Polygons.
- in  Grid Fracture Planes :: Slab (S) - Slab DTO whose bounding box seeds the grid.
- out Grid Fracture Planes :: Planes (P) - FracturePlane DTOs. Wire into Slab Cut By Fractures or Quarry Decompose.
- in  Jittered Grid Fracture Planes :: Slab (S) - Slab DTO whose bounding box seeds the grid.
- out Jittered Grid Fracture Planes :: Planes (P) - FracturePlane DTOs.
- in  Layered Fracture Planes :: Slab (S) - Slab DTO whose bounding box seeds the layers.
- out Layered Fracture Planes :: Planes (P) - FracturePlane DTOs (parallel, evenly spaced).
- out Radial Fracture Planes :: Planes (P) - FracturePlane DTOs around the rotation axis.
- in  Random Fracture Planes :: Slab (S) - Slab DTO whose bounding box seeds the random plane points.
- out Random Fracture Planes :: Planes (P) - FracturePlane DTOs.
- in  Slab Cut By Fracture Polygons :: FracturePolygon (F) - FracturePolygon DTOs (from Fracture Polygon From Curve).
- out Slab Cut By Fracture Polygons :: Slab (S) - Output Slabs after cutting.
- out Voronoi Fracture Planes :: Planes (P) - FracturePlane DTOs (one per distinct seed pair).
- in  Ashlar Pack :: Wall Frame (Wf) - Optional WallFrame DTO from Wall Frame component. When wired,  this overrides th
- in  Ashlar Pack :: Options (Op) - Optional AshlarPackOptions DTO from Ashlar Pack Options  component. When wired, 
- out Ashlar Pack :: Assembly (A) - MasonryAssembly with bottom-course blocks fixed. Wire into  Masonry Stability (R
- out Ashlar Pack :: Result (R) - AshlarPackResult carrying coverage / leftovers / notes / placed  blocks. Wire in
- out Ashlar Pack Options :: Options (O) - AshlarPackOptions DTO bundling all algorithmic knobs. Wire  into Ashlar Pack's O
- in  Assembly Preview :: Assembly (A) - MasonryAssembly DTO (from Masonry Assembly or Ashlar Pack).
- out Auto Interfaces :: Interfaces (I) - Detected MasonryInterfaces. Wire into Masonry Assembly.
- in  Best Fit Pack :: Wall Frame (Wf) - Optional WallFrame DTO. Overrides primitive Wall W/H/T.
- in  Best Fit Pack :: Options (Op) - Optional AshlarPackOptions DTO. Overrides primitive  algorithmic inputs.
- out Best Fit Pack :: Assembly (A) - MasonryAssembly with bottom-course blocks fixed.
- out Best Fit Pack :: Result (R) - AshlarPackResult — coverage / leftovers / notes / placed blocks.
- in  Block Build Order :: Assembly (A) - MasonryAssembly DTO.
- in  Block Graph Coloring :: Assembly (A) - MasonryAssembly with blocks + interfaces.
- in  Block Size Distribution :: Slabs (S) - Slab DTOs.
- in  Build-Order Stability Stream :: Assembly (A) - Full MasonryAssembly DTO.
- in  Cut Validation :: Pre Slabs (Pre) - Slab DTOs before the cut.
- in  Cut Validation :: Post Slabs (Post) - Slab DTOs after the cut.
- out Cut Validation :: Cleaned Slabs (Clean) - Sliver-free subset of post slabs. Empty when Drop  Slivers is false.
- in  Fragment Merger :: Slabs (S) - Slab DTOs (the candidate pieces).
- in  Masonry Assembly :: Blocks (B) - MasonryBlock DTOs from Masonry Block.
- in  Masonry Assembly :: Interfaces (I) - MasonryInterface DTOs. May be empty; auto-detection is a future task.
- out Masonry Assembly :: Assembly (A) - MasonryAssembly DTO. Wire into Masonry Stability (RBE).
- out Masonry Block :: Block (B) - MasonryBlock DTO. Wire into Masonry Assembly.
- in  Masonry Stability (RBE) :: Assembly (A) - MasonryAssembly from Masonry Assembly.
- in  Masonry Stability Check :: Assembly (A) - OPTIONAL: a pre-built assembly (e.g. the Polygonal Wall Generator's Assembly out
- in  Pack Diagnostics :: Result (R) - AshlarPackResult from Ashlar Pack.
- out Pack Diagnostics :: Leftovers (L) - Slabs from the input inventory that were not placed.
- out Pack Diagnostics :: Placed Blocks (B) - MasonryBlocks in the order they were laid.
- in  Pack Preview :: Result (R) - AshlarPackResult from Ashlar Pack.
- out Polygonal Wall (Generator) :: Assembly (A) - The wall as a structural assembly with EXACT joint interfaces from the generator
- out Robust Auto Interfaces :: Interfaces (I) - Detected MasonryInterfaces. Wire into Masonry Assembly.
- out Wall Frame :: Wall Frame (F) - WallFrame DTO. Wire into Ashlar Pack's Wall Frame input to  reuse the same envel
- out Bench From Mesh :: Bench Boundary (BB) - Opaque BenchBoundary value (Box + Mesh combined). Future  BCO-v2 components cons
- out Load Photo Set :: Photo Set (PS) - Typed PhotoSet record (Frahan.Core.ScanIngest.PhotoSet). Wire  into downstream F
- out Read Metashape Project :: Metashape Project (MP) - Typed MetashapeProject record (Frahan.Core.ScanIngest.MetashapeProject).
- out Stone Prep (Scan) :: Descriptors (D) - StoneDescriptor per stone (opaque; consumable by Pack3D and  downstream Frahan 3
- out Convex Hull Slab :: Slab (S) - Convex-hull Slab.
- out Frahan GPR Radargram Reader :: Radargram (R) - GprRadargram object.
- out Frahan Quarry Inventory :: Inventory (Inv) - QuarryInventory object.
- in  Frahan Slab Yield Optimizer :: Fracture Planes (F) - Optional List<FracturePlane>. Wire from Frahan Mesh → Fracture Planes.
- out Frahan Slab Yield Optimizer :: Best Plan (P) - SlabPlan with the highest score.
- out Frahan Slab Yield Optimizer :: Cut Planes (Cp) - FracturePlanes that materialise the winning plan (feed Slab Cut By Fractures).
- out Joint Set :: Joint Set (J) - JointSet DTO. Wire into Quarry DFN.
- out Mesh Shell Split :: Slabs (S) - One Slab per connected shell.
- in  Quarry DFN :: Joint Sets (J) - JointSet DTOs (from Joint Set component).
- out Quarry DFN :: Slabs (S) - Same blocks as Slab DTOs (for downstream Frahan plumbing).
- in  Quarry Decompose :: Quarry (Q) - Convex quarry. Accepts a Frahan Slab DTO (from Slab From Mesh)  OR a Rhino Mesh 
- out Quarry Decompose :: Slabs (S) - Output Slab DTOs. Wire into Ashlar Pack.
- in  Frahan Packing Plan Report :: Packing Metrics (M) - PackingMetricsReport (opaque) from Frahan Pack3D / Frahan Packing Metrics.
- in  Frahan Packing Plan Report :: Residual Voids (V) - ResidualVoid list (opaque) from Frahan Residual Voids component.  Optional; defa
- in  Frahan Packing Plan Report :: Edge Match Scores (E) - Per-fragment-per-edge best match scores as a nested list  (IReadOnlyList<IReadOn
- out Frahan Packing Plan Report :: Plan Report (R) - PackingPlanReport (opaque) for downstream serialisation / further reporting.
- in  Frahan Packing Report :: Pack Result (R) - PackResult from a 3D pack solver (opaque).
- in  Frahan Report / Export :: Reports (R) - Frahan report records (PackingReport, MeshDiagnostics, FabricationPrep,  BlockCu
- in  Slab Cut By Fractures :: Plane (P) - Oriented infinite fracture planes. Accepts the Frahan  FracturePlane DTO (from a
- out Slab Cut By Fractures :: Slab (S) - Output Slabs after cutting.
- out Slab From Mesh :: Slab (S) - Slab DTO. Wire into Slab Cut By Fractures or downstream masonry.
- in  Pack On Surface :: Surface Map (Map) - FrahanSurfaceChart from the Surface Chart component.
- in  Pack Surfaces :: Surface Maps (Maps) - One or more FrahanSurfaceChart objects from the Surface Chart component. Each be
- out Pack Surfaces :: Transforms 3D (T3) - Transform from PACKED 2D position to the 3D surface placement frame.  Apply to P
- out Pack Surfaces :: Full Transform (FT) - Composed transform: original flat part -> 3D surface in one step.  Apply to the 
- out Surface Chart :: Surface Map (Map) - FrahanSurfaceChart object. Wire into the Pack On Surface component.
- out Frahan Trencadís EdgeMatch :: Transforms (X) - Per-piece rigid transform.
- out Frahan Trencadís Pack :: Transforms (X) - Placement transforms (per source curve).
- out Arch Voussoirs :: Assembly (VA) - Typed VoussoirAssembly. Wire into Voussoir Stone Matcher (D5F10010).
- out Pendentive Vault Voussoirs :: Assembly (VA) - Typed VoussoirAssembly. Wire into Voussoir Stone Matcher (D5F10010).
- in  Voussoir Ingest :: Voussoirs (V) - List of voussoir geometries representing the designed stereotomic  assembly. Acc
- out Voussoir Ingest :: Assembly (VA) - The typed VoussoirAssembly. Wire into VoussoirStoneMatcher +  VoussoirPackIntoBl
- out Voussoir Ingest :: Match Items (MI) - List of MatchItem (substrate-compatible). Wire into MatcherContextBuilder  as th
- in  Voussoir Pack Into Block :: Assembly (VA) - VoussoirAssembly from VoussoirIngestComponent (D5F1000F).
- in  Voussoir Pack Into Block :: Block (B) - A single quarried block (accepts QuarryBlock typed record from  ScanToBlockInven
- in  Voussoir Stone Matcher :: Assembly (VA) - VoussoirAssembly from VoussoirIngestComponent (D5F1000F).
- in  Voussoir Stone Matcher :: Quarry Stones (QS) - List of quarry-block candidates. Accepts either:  (a) QuarryBlock typed records 