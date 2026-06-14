# Frahan StonePack - data-structure audit (port type contract)

v0.1.0-alpha. Audited every Grasshopper component port in `Frahan.StonePack.GH` for type
consistency before the public release. Method: deterministic pre-compute of every port type
(`audit_types.py` -> `type_audit.json`) then a multi-agent judge + adversarial-verify pass over
every concept that appears as more than one type. READ-ONLY audit; the verdicts are below.
Style: short sentences, no em dashes.

> **UPDATE 2026-06-15: the one defect is FIXED.** The 14 `transform` output ports were retyped
> Generic -> Transform (commit `0a29468`), build + battery green, and the bundled `.gha` rebuilt.
> No bundled example wires those ports (verified live), so nothing broke. The numbers below are
> refreshed: Generic 115 -> 101, Transform 29 -> 43, multi-type concepts 40 -> 39, defects 0.

Corpus: 187 components, 2028 ports, 0 custom GH types, **101** Generic ports, **39** concepts that
appear as more than one GH type (was 115 / 40 before the transform fix).

## TL;DR

The type system is clean. Of the 40 multi-type concepts audited, 39 were correctly typed and
exactly **1 was a genuine defect**: the `transform` concept was `Generic` on 12 output ports
(+ `Transforms 3D` and `Full Transform`, 14 in all) where the plugin already used `Transform`
for the identical concept. **That defect is now fixed** (all 14 retyped to `Transform`).
Everything else is a name collision (one English word, two genuinely different concepts, each
correctly typed) or a deliberate DTO-vs-geometry split. No conversion is forced anywhere.

The brief expected several big problems (report Generic->Text across ~86 ports, stone/slab/block
geometry mis-typed, enums Integer-vs-Text). The source refutes all three. They are documented
below so the reader sees they were checked and dismissed.

Type frequency (2028 ports, after the fix): Number 580, Integer 410, Text 253, Mesh 169,
Boolean 161, Curve 142, Generic 101, Point 63, Transform 43, Plane 30, Box 28, Vector 21,
Geometry 15, Brep 3, Surface 1.

## The one defect (FIXED 2026-06-15): `transform` should be `Transform`, not `Generic` (14 ports)

12 output ports emit a per-element rigid placement transform but register as
`AddGenericParameter`. The same plugin already establishes `Transform` as canonical for this
exact concept (the 3D family, Sheet Nest (Hole-Aware), Trencadis Pipeline). The split is even
intra-pipeline: Trencadis Pipeline uses `Transform` but its siblings Trencadis Pack /
Trencadis EdgeMatch use Generic. Proof the payload is a real transform: the packers
`SetDataList(result.Transforms)` where `PackingResult.Transforms` is `List<Transform>`
(`TwoD/Packing2DModels.cs:39`); EdgeMatch builds `List<GH_Transform>`
(`EdgeMatchSolveComponent.cs:273`).

| Component | Port | File:line |
|---|---|---|
| 2D Bottom Left Pack | Transforms | Pack2DBottomLeftComponent.cs:68 |
| 2D Freeform Sheet Pack | Transforms | Pack2DIrregularSheetComponent.cs:69 |
| 2D Freeform Sheet Pack V3 | Transforms | Pack2DIrregularSheetV3Component.cs:92 |
| 2D Irregular Sheet Pack | Transforms | Pack2DIrregularSheetV2Component.cs:93 |
| 2D NFP Pack | Transforms | NfpPack2DComponent.cs:52 |
| Frahan Sheet Pack (Unified Async) | Transforms | IrregularSheetFillComponentAsync.cs:109 |
| Frahan Sheet Pack (Unified) | Transforms | IrregularSheetFillComponent.cs:146 |
| Freeform Sheet Nest | Transforms | (sheet-nest sibling) |
| Freeform Sheet Nest (Exact NFP) | Transforms | IrregularSheetFillNfpBlfComponent.cs:68 |
| EdgeMatch Solve | Transforms | EdgeMatchSolveComponent.cs:109 |
| Frahan Trencadis EdgeMatch | Transforms | TrencadisEdgeMatchComponent.cs:97 |
| Frahan Trencadis Pack | Transforms | Pack2DTrencadisComponent.cs:159 |

Fix: change `AddGenericParameter("Transforms", ...)` to `AddTransformParameter("Transforms", ...)`
on these 12 ports. The GUID does not change; only the registered param type flips.
`Param_Transform.SetDataList` accepts both `Transform` and `GH_Transform`, so no `SetData`
change is needed.

**Gate (saved-file compatibility).** Changing a registered output param TYPE on a shipped
component can drop wires in saved `.gh` files authored against the old Generic port. This is a
HITL change spanning >5 files and needs a canvas re-validation pass (truth criterion c). Best
window: do it BEFORE the public release, while there is no installed base of `.gh` files to
break. After release, it belongs in v0.2 with a migration note ("Transforms outputs on the
2D/Trencadis/EdgeMatch packers changed from Generic to Transform; re-connect the wire").

## Checked and refuted (not defects)

- `report` Generic-vs-Text: 85 Report ports are already `Text`. The lone Generic is an INPUT
  named `Reports` that consumes opaque `FrahanReport` DTO records; typing it Text would discard
  them. No text report ever flows into the record input. Not a collision.
- `stone` / `slab` / `block` Generic/Mesh/Curve: every spread is a DTO-vs-mesh or 2D-vs-3D split.
  Generic carries the typed Core DTO, Mesh the renderable geometry, Curve the 2D outline. Each
  producer pairs its DTO with a separate Mesh render port. Correct.
- enums `mode` / `backend` / `project` Integer-vs-Text: enum SELECTOR inputs are uniformly
  Integer; the Text occurrences are detected-LABEL outputs (e.g. EdgeMatch Segments :: Mode =
  "Planar2D or Spatial3D"). Different role, different direction. The enum-input convention is
  already consistent.
- `plan` / `cut plan` Generic-vs-Plane: the fracture-plane generators emit
  `Frahan.Masonry.Cutting.FracturePlane`, a Rhino-free Core DTO with no IGH_Goo wrapper, which
  cannot sit in an `AddPlaneParameter` output and carries LESS than a Rhino Plane (no in-plane
  frame). Generic is correct and required by the Rhino-free Core boundary. The Slab Yield
  Optimizer Cut Planes port belongs to the 8-sibling FracturePlane family; retyping would split
  it from its siblings and degrade a typed cutting DTO to a bare plane.

## The Generic ports (115 -> 101 after the fix)

| Disposition | Count | Detail |
|---|---|---|
| Retyped Generic -> `Transform` | 14 | DONE (commit 0a29468): the 12 `Transforms` ports + `Transforms 3D` + `Full Transform` |
| Legitimately Generic (typed DTO carriers) | 101 | correct; carry Core/Masonry DTO records with no IGH_Goo wrapper |

The 103 are Slab / MasonryBlock / QuarryBlock DTOs, AshlarPackResult DTOs, inventory container
DTOs, the FracturePlane family, the FrahanReport input, and the GprRadargram object. Typing any
of them concretely would discard the record. The 0-custom-type, Generic-as-DTO-carrier pattern
is deliberate and defensible given the Rhino-free Core boundary.

## Canonical type per concept (the contract)

The plugin already has ONE canonical type per concept once `transform` is fixed. Where a word
carries two genuinely different concepts, both are listed because they are different things;
collapsing them would force a lossy or wrong conversion.

| Concept | Canonical type | Note |
|---|---|---|
| placement transform | `Transform` | the one fix: 12 Generic ports -> Transform |
| report (summary) | `Text` | already canonical (85 ports) |
| report bundle (records) | `Generic` | opaque FrahanReport DTO |
| enum selector (mode/backend/axis/anchor) | `Integer` | input selectors + counts/indices |
| detected label (mode/backend) | `Text` | output labels |
| stone/slab/block DTO | `Generic` | typed Core DTO carrier |
| stone/slab/block geometry | `Mesh` | renderable; pairs with the DTO port |
| 2D outline (stone/fragment/board) | `Curve` | 2D variant |
| fracture / cut plane (DTO) | `Generic` | Rhino-free Core FracturePlane |
| fabrication / geometric / pose plane | `Plane` | CNC/robot/section/pick-place frames |
| pick point / control points / Voronoi seeds | `Point` | geometric points |
| RNG seed | `Integer` | deterministic seed |
| container AABB | `Box` | fast-path packing volume |
| irregular container / cell polyhedron | `Mesh` | closed solid |
| grid cell | `Box` | axis-aligned running-bond cell |
| depth (metric) | `Number` | physical extent |
| depth (DAG level) | `Integer` | topological-sort level |
| scale toggle | `Boolean` | rigid/similarity flag |
| scale / clearance / uniform factor | `Number` | scalar knobs |
| per-axis factors / slack / rotation axis | `Vector` | anisotropic / direction |
| pole line (3D) | `Line` | cloud-space pole |
| projected pole (2D) | `Point` | stereonet pole |
| NURBS surface (tile) | `Surface` | wall-generator input |
| radargram data | `Generic` | GprRadargram DTO |

Standardization guidance: the ONLY concept needing standardization is `transform` (-> `Transform`).
Every other "two-type" concept is two concepts under one word; do not collapse them.

## Optional cosmetic polish (non-blocking, no wiring impact)

Nickname renames could soften the name collisions without any type change (safe in saved files):
IFC `Container` enum -> `Element Type`; `Sheet` output index -> `Sheet Index`; GPR `Picks` ->
`Pick Points`; Carving Stages `Target` -> `Target Mesh`.

Reproduce: `audit_types.py` (facts) + the audit workflow. Raw facts: `type_audit.json`,
`TYPE_AUDIT_FACTS.md`.
