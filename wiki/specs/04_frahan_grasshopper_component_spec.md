# 04 - Frahan Grasshopper Component Spec

**Spec version:** 0.1
**Sources:** runbook § 16, live `src/Frahan.StonePack.GH/*.cs`,
`docs/index/frahan_class_method_component_audit.md`.

This spec catalogues both **live** components and **proposed** ones.
Live components have their `Frahan.StonePack.GH.*` identity called out;
proposed components are flagged `proposed only` and have no GUID yet.

## 0. Per-component contract template

Each entry below uses this template. For brevity only field values are
listed; the labels are implied:

```
- name             : (display name in GH ribbon)
- nickname         : (short label)
- category         : ribbon tab → group
- inputs           : (param name : type)
- outputs          : (param name : type)
- expected behavior: …
- validation rules : …
- failure cases    : …
- preview behavior : …
- performance notes: …
- backend type     : RhinoCommon | managed C# | native optional | future ML
- status           : implemented | implemented (versioned-collapse pending) | proposed only
```

---

## 1. Live components (Frahan StonePack 0.5.6)

### 1.1 Frahan StonePack assembly information

```
- name             : Frahan StonePack assembly metadata
- live class       : StonePackAssemblyInfo (Frahan.StonePack.GH)
- behavior         : reports plugin name, version, author, description to Grasshopper
- backend          : managed C#
- status           : implemented
```

### 1.2 2D NFP family

#### NfpPack2DComponent

```
- name      : Frahan NFP Pack 2D
- nickname  : NFP Pack 2D
- category  : Frahan StonePack → 2D
- inputs    : Sheet : Curve, Parts : List<Curve>, Settings : PackSettings (or split scalars)
- outputs   : Placements : List<Transform>, Result : PackingResult, Diagnostics : List<string>
- expected  : packs Parts into Sheet using NFP-based bottom-left placement
- validation: Sheet must be closed planar curve; each Part must be closed planar curve
- failure   : non-planar input, zero-area part, sheet smaller than smallest part
- preview   : packed parts in sheet plane
- backend   : managed C# (RhinoCommon Curve)
- status    : implemented
```

#### NfpTestComponent

```
- name      : Frahan NFP Test
- nickname  : NFP Test
- category  : Frahan StonePack → 2D → Diagnostics
- inputs    : PartA : Curve, PartB : Curve
- outputs   : NFP : Curve, Diagnostics : List<string>
- expected  : computes the No-Fit Polygon between two parts
- backend   : managed C#
- status    : implemented
```

### 1.3 2D bottom-left family

#### Pack2DBottomLeftComponent

```
- name      : Frahan Pack 2D Bottom-Left
- inputs    : Sheet : Rectangle, Parts : List<Curve>, SortMode : PackingSortMode
- outputs   : Placements : List<Transform>, Result : PackingResult
- backend   : managed C# via Frahan.StonePack.GH.TwoD.BottomLeftFillRhino
- status    : implemented
```

### 1.4 2D irregular sheet family (versioned)

#### Pack2DIrregularSheetComponent (V1)
#### Pack2DIrregularSheetV2Component
#### Pack2DIrregularSheetV3Component
#### Pack2DIrregularSheetV506Component

```
- inputs    : Sheets : List<Curve>, Parts : List<Curve>, AllowYaw : bool,
              MinDistance : double, EnableHoles : bool, Variant : enum (proposed)
- outputs   : Placements : DataTree<Transform>, Sheets-out : List<Curve>,
              Failures : List<string>, Yield : double, Diagnostics : List<string>
- expected  : packs irregular parts into one or more irregular sheets
              with collision-free placement; respects optional holes
- validation: each sheet planar; parts closed planar curves; tolerance > 0
- failure   : non-planar sheet, degenerate part, cancelled task
- preview   : transformed parts in WorldXY (re-projected to sheet plane)
- performance: V506 uses adaptive ToPolyline (2°/chord, ≤256 verts),
              cached MinDistPolys, parallel candidate evaluation;
              GH_TaskCapableComponent so Rhino stays responsive
- backend   : managed C# via IrregularSheetFillV{2,3,506}
- status    : implemented (versioned-collapse pending - see refactor plan R3)
```

### 1.5 3D irregular family

#### Pack3DIrregularComponent

```
- name      : Frahan Pack 3D Irregular
- inputs    : Container : Box, Items : List<Mesh>, Settings : PackSettings
- outputs   : Placements : List<Transform>, Result : PackResult, Heightmap : Mesh-preview
- backend   : managed C# via Frahan.StonePack.Core.GreedyHeightmapPacker
- status    : implemented
```

#### Pack3DIrregularContainerComponent

```
- name      : Frahan Pack 3D into Irregular Container
- inputs    : Container : Mesh (closed, manifold), Items : List<Mesh>, Settings
- outputs   : Placements per container : DataTree<Transform>, Result : per-container ContainerResult
- backend   : managed C# via IrregularMeshContainer + GreedyMeshHeightmapPacker
- preview   : packed items in their container
- status    : implemented
```

#### Pack3DMeshHeightmapComponent

```
- name      : Frahan Pack 3D (Mesh Heightmap)
- inputs    : Container : Mesh, Items : List<Mesh>, Settings
- outputs   : Placements : List<Transform>, Heightmap : Mesh-preview, Result
- backend   : managed C# via MeshPileHeightmap + OrientedMeshHeightmap
- status    : implemented
```

#### ValidatePackedTransformComponent

```
- name      : Frahan Validate Packed Transforms
- inputs    : Items : List<Mesh>, Transforms : List<Transform>, Container
- outputs   : Valid : bool, Failures : List<string>, OverlapVolume : double
- backend   : managed C#
- status    : implemented
```

### 1.6 Surface packing (live prototype)

#### SurfaceChartComponent

```
- name      : Frahan Surface Chart
- inputs    : Source : Surface | Brep | Mesh, BffPath : string (optional)
- outputs   : Chart : FrahanSurfaceChart, FlatMesh : Mesh, Distortion : ChartDistortionReport
- backend   : managed C# wrapping out-of-process bff-command-line.exe
- status    : implemented (prototype)
```

#### PackOnSurfaceComponent

```
- name      : Frahan Pack On Surface
- inputs    : Chart : FrahanSurfaceChart, Parts2D : List<Curve>, Settings
- outputs   : Placements3D : List<Transform>, Curves3D : List<Curve>
- backend   : managed C# via BarycentricMapper2DTo3D
- status    : implemented (prototype)
```

#### PackSurfacesComponent

```
- name      : Frahan Pack Surfaces
- inputs    : Surfaces : List<Surface>, Parts2D : List<Curve>, Settings
- outputs   : Placements3D-per-surface : DataTree<Transform>
- backend   : managed C#
- status    : implemented (prototype)
```

---

## 2. Proposed components (per runbook § 16)

The runbook lists six families of proposed components. Live components
that already exist are cross-referenced; everything else is `proposed only`.

### 2.1 2D Packing family (runbook 16.1)

| Component | Status | Live equivalent (if any) |
| --- | --- | --- |
| Frahan Trencadis 2D | proposed only | partially served by `Pack2DIrregularSheetV506Component` |
| Frahan Boundary Rail Index | proposed only | none |
| Frahan Fragment Descriptors | proposed only | none |
| Frahan Edge Match | proposed only | none |
| Frahan Trim Suggestions 2D | proposed only | none |
| Frahan Residual Voids | proposed only | none |
| Frahan Packing Report | proposed only | reports are inside `PackingResult` today |

### 2.2 Surface family (runbook 16.2)

| Component | Status | Live equivalent |
| --- | --- | --- |
| Frahan Surface Patch | proposed only | partial: `SurfaceChartComponent` |
| Frahan Column Unwrap | proposed only | none |
| Frahan Surface Trencadis | proposed only | partial: `PackOnSurfaceComponent` |
| Frahan Map Fragments To Surface | proposed only | partial: `PackOnSurfaceComponent` |
| Frahan Distortion Report | proposed only | covered by `ChartDistortionReport` output |

### 2.3 3D Ashlar family (runbook 16.3)

| Component | Status | Live equivalent |
| --- | --- | --- |
| Frahan Stone Proxy Mesh | proposed only | none |
| Frahan Stone Descriptor | proposed only | none |
| Frahan Course Segmentation | proposed only | none |
| Frahan Ashlar Volume Pack | proposed only | partial: `Pack3DIrregularComponent` |
| Frahan Ashlar Face Match | proposed only | none |
| Frahan Cut Suggestions 3D | proposed only | none |
| Frahan Contact Graph | proposed only | none |
| Frahan Masonry Report | proposed only | none |

### 2.4 GeoPack family (runbook 16.4)

All `proposed only`. See `08_frahan_geopack_spec.md`.

### 2.5 GeoCut and QuarryCutOpt family (runbook 16.5)

All `proposed only`. See `09_frahan_geocut_spec.md` and
`10_frahan_quarrycutopt_spec.md`.

### 2.6 Mesh and Backend family (runbook 16.6)

| Component | Status |
| --- | --- |
| Frahan Mesh Diagnostics | proposed only |
| Frahan Mesh Repair | proposed only |
| Frahan Mesh Simplify | proposed only |
| Frahan Mesh Slice | proposed only |
| Frahan Remesh | proposed only |
| Frahan Collision Proxy | proposed only |
| Frahan Native Backend Status | proposed only |

### 2.7 Reports and Export family (runbook 16.7)

| Component | Status |
| --- | --- |
| Frahan Export CSV Report | proposed only |
| Frahan Export JSON Plan | proposed only |
| Frahan Export GraphML | proposed only |
| Frahan Export DXF Slab Outlines | proposed only |
| Frahan Export CNC Prep Curves | proposed only |
| Frahan Export 3DM Package | proposed only |

---

## 3. Cross-cutting requirements for every component

- `GH_TaskCapableComponent` for any solver expected to take > 200 ms.
- Cancellation passed through to the solver via `CancellationToken`.
- Progress reporting updates via Grasshopper canvas message, not via
  `Console.WriteLine`.
- `AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, …)` for any
  recoverable issue; `Error` only when the solver cannot produce
  output; `Remark` for diagnostics.
- `ComponentGuid` is **stable across renames** - never regenerate the
  GUID for an existing component, even when the namespace or class
  name changes.
- `Exposure` set per family (`primary` for `Pack2DIrregularSheetV506`
  and `Pack3DIrregular*`; `secondary` for the older versioned
  variants once the V-collapse refactor lands).
