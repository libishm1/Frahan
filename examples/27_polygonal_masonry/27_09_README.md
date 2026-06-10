# 27_09 — IFC Export (Stone Assembly): the BIM terminal

The P6 terminal: any Frahan stone workflow now ends in BIM with one node. Canvas chain, live-validated
2026-06-11:

    sliders -> Polygonal Wall (Generator) -> Masonry Stability Check (CRA=true)   [verdict panel]
                        |                                                          STABLE | CRA-CERTIFIED (0.48e)
                        +-> IFC Export (Stone Assembly) -> 27_09_polygonal_wall.ifc  [report panel]

## What the .ifc contains (verified at STEP level)
- FILE_SCHEMA IFC4; length unit SI METRE (unprefixed).
- IfcProject -> IfcSite -> IfcBuilding -> IfcBuildingStorey; the IfcWall is storey-contained.
- 1 IfcWall(SOLIDWALL) aggregating **15 IfcBuildingElementPart** (USERDEFINED / "NaturalStone") — parts
  decompose the wall and are NOT storey-contained (IFC4 rule).
- 15 closed IfcTriangulatedFaceSet bodies (1-based CoordIndex), one per stone.
- 15 "Frahan_Stone" property sets: BuildOrder / CarveRatio / StabilityMargin / InterlockJ (the metric trail
  from generator + matcher + verifier survives into BIM).

## Container modes
The component writes Wall / Cladding (IfcCovering CLADDING, aggregated — IfcRelCoversBldgElements is
deprecated in IFC4) / Arch (IfcElementAssembly ARCH with voussoir IfcMembers) / Vault (USERDEFINED) / Column.
Headless round-trip tests cover wall + arch + cladding (battery).

## Engineering notes
- xBIM Essentials 6.0.587 (managed write path, no native geometry engine); ~13 MB closure deploys beside the
  .gha. Spec: outputs/2026-06-10/masonry_evolution/P6_IFC_EXPORT_SPEC.md.
- Meshes are quad-triangulated, welded, unified, flipped-if-inverted, and scaled doc-units -> metres before
  writing.
- FINDING (queued): the penalty-RBE ADMM hits a convergence ceiling near ~50 interfaces (6x4 wall = 53 ifaces
  failed with SolverError; 5x3 = 30 ifaces certifies in 1 iter). Conditioning work is the open perf item.
