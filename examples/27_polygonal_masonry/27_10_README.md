# 27_10 — Castle Keep: the P7 composer (one IFC4 building from bottom-up stone workflows)

A complete stone building composed ON CANVAS and exported as ONE IFC4 file, live-validated 2026-06-11.

## Composition (architecturally explicit, no decorative filler)
- **4 polygonal-masonry walls** from ONE Polygonal Wall (Generator) driven by LIST inputs (W/GridX/Seed
  lists -> GH iterates the solver; one component, four walls) + one Rotate/Move (grafted) for placement.
- **Integrated portal** (HITL iteration): the front wall is generated SOLID, then an arched opening is
  CGAL-DIFFERENCED through its stones using the arch's exact profile — door box UNION extrados cylinder
  (r = 1.15 = intrados 0.8 + ring 0.35; Cap Holes closes the stock cylinder, else CGAL corefinement fails
  rc=-12 on open inputs). The 9-voussoir arch (D5F10012) then fills the cut exactly, bearing on the
  trimmed stones — wall masonry continues over the extrados like a real portal.
- **Tympanum infill**: 4 generated stone panels CGAL-INTERSECTED stone-by-stone with the dome sphere
  (Mesh CSG (CGAL), native backend, ~234 ms) — irregular trimmed blocks filling between the vault edge and
  the masonry, the Byzantine lunette practice. Empty boolean results are culled by the exporter
  (degenerate-mesh guard).
- **Pendentive dome**: Pendentive Vault Voussoirs D5F10013 (R=4.2, square half-width 2.6, 6x3 grid),
  springing from the wall tops; pendentive corners land on the wall corners.

## Per-element verification (sidesteps the ADMM ~50-interface ceiling)
| Element | Verdict |
|---|---|
| 4 walls (exact-joint Assembly + CRA, pre-cut patterns) | **4/4 STABLE** (list-driven: one checker iterates all) |
| Portal arch (9 voussoirs, local frame) | STABLE |
| Pendentive dome (26 shells, local frame) | STABLE |

## The .ifc (STEP-verified)
7 containers / 125 stone parts (309 KB): 5 IfcWall (4 keep walls incl. the portal-cut front + tympanum
infill) +
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
