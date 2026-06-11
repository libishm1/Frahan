# 27_10 — Castle Keep: the P7 composer (one IFC4 building from bottom-up stone workflows)

A complete stone building composed ON CANVAS and exported as ONE IFC4 file, live-validated 2026-06-11.

## Composition (architecturally explicit, no decorative filler)
- **5 polygonal-masonry walls** from ONE Polygonal Wall (Generator) driven by LIST inputs (W/GridX/Seed
  lists -> GH iterates the solver; one component, five walls) + one Rotate/Move (grafted) for placement.
  Front wall = two jambs framing a 1.6 m portal.
- **Portal arch**: 9 voussoirs (Arch Voussoirs D5F10012), springing z=2.0, embedded in the jambs.
- **Tympanum infill**: 4 generated stone panels CGAL-INTERSECTED stone-by-stone with the dome sphere
  (Mesh CSG (CGAL), native backend, ~234 ms) — irregular trimmed blocks filling between the vault edge and
  the masonry, the Byzantine lunette practice. Empty boolean results are culled by the exporter
  (degenerate-mesh guard).
- **Pendentive dome**: Pendentive Vault Voussoirs D5F10013 (R=4.2, square half-width 2.6, 6x3 grid),
  springing from the wall tops; pendentive corners land on the wall corners.

## Per-element verification (sidesteps the ADMM ~50-interface ceiling)
| Element | Verdict |
|---|---|
| 5 walls (exact-joint Assembly + CRA) | **5/5 STABLE** (list-driven: one checker iterates all five) |
| Portal arch (9 voussoirs, local frame) | STABLE |
| Pendentive dome (26 shells, local frame) | STABLE |

## The .ifc (STEP-verified)
8 containers / 123 stone parts (288 KB): 6 IfcWall (5 keep walls + tympanum-infill container) +
IfcElementAssembly(ARCH) with 9 voussoir IfcMembers + IfcElementAssembly(USERDEFINED "PendentiveVault")
with 18+8 IfcMembers; 123 IfcTriangulatedFaceSet bodies + 123 Frahan_Stone psets; SI metres.

## BIM importability
- **IFC4 Reference View tessellation** (IfcTriangulatedFaceSet, Body/Tessellation) is the
  vendor-interchange geometry: **Revit** (IFC4 link/open), **ArchiCAD** (IFC4 RV certified import),
  **Solibri / BIMcollab / ODA Open IFC Viewer / FZKViewer**, and web viewers (That Open/IFC.js-based,
  usBIM) all consume it.
- ifcopenshell note: strict base-IFC4 schema validation flags `IfcCartesianPointList3D` arity — xBIM
  writes the **IFC4 ADD2 TC1** dialect (TagList attribute). Vendor IFC4 implementations target ADD2 TC1;
  no action needed.

## Future work (P7.1)
Vault patterning from **compas-RV (RhinoVAULT)** form-found patterns (Block Research Group) — ingest an
RV pattern as the voussoir layout for funicular vaults instead of the analytic sphere grid.
