# 07 - Frahan 3D Ashlar Packing Spec

**Spec version:** 0.1
**Sources:** live `Frahan.StonePack.Core.{PackingModels, MeshPackingModels,
Heightmap, MeshPileHeightmap, OrientedMeshHeightmap, IrregularMeshContainer,
GreedyHeightmapPacker, GreedyMeshHeightmapPacker}`, plus
`Template-General/raw/fabrication_workflows/3DIrregularPacking.md`,
runbook § 16.3.

## 1. Goal

Pack irregular 3D stones into a container (rectangular box, irregular
mesh container, or open ashlar wall course-by-course) such that:

- placements are collision-free at a configurable clearance;
- placements minimise wasted volume above the heightmap;
- candidate orientations are pre-pruned (yaw-90 today; full rotation
  set proposed);
- the result is a `PackResult` / `MeshPackResult` with placements,
  failures, fill ratio, and a heightmap preview.

## 2. Computational grammar instantiation

```
Stone meshes + container
  → validate (closed, manifold, non-degenerate)
  → simplify or proxy (oriented bounding box → mesh heightmap proxy)
  → extract descriptors (size3, volume, oriented mesh footprint)
  → match compatible features (footprint vs current heightmap window)
  → generate candidates (yaw-90 today; full SO(3) discretisation proposed)
  → validate with original mesh (mesh-vs-mesh collision via voxel proxy)
  → refine or rank (compactness, height, fabrication-aware face match)
  → suggest cuts (3D trim suggestions; proposed)
  → report fabrication metrics (fill ratio, course count, contact graph)
```

## 3. Live implementation today

### 3.1 Box container

`Pack3DIrregularComponent` → `GreedyHeightmapPacker` → `Heightmap` (box
container; size given by `Box3`).

### 3.2 Irregular mesh container

`Pack3DIrregularContainerComponent` → `IrregularMeshContainer`
(stores per-cell `ContainerHeightInterval`s) →
`GreedyMeshHeightmapPacker`.

### 3.3 Mesh-pile heightmap (proxy collision)

`Pack3DMeshHeightmapComponent` → `MeshPileHeightmap` (per-cell
`HeightInterval`) → `OrientedMeshHeightmap` (per-orientation footprint
for a stone mesh).

### 3.4 DTOs

- `Frahan.StonePack.Core.PackingModels`: `PackItem`, `PackContainer`,
  `PackSettings`, `PackPlacement`, `PackFailure`, `PackResult`.
- `Frahan.StonePack.Core.MeshPackingModels`: `MeshTriangle`,
  `MeshPackItem`, `MeshPackSettings`, `MeshPackPlacement`,
  `MeshPackFailure`, `MeshPackResult`.

### 3.5 Validation

`ValidatePackedTransformComponent` checks every output transform for
overlap volume against the container and other items.

## 4. Proposed components (runbook § 16.3)

| Component | Purpose | Notes |
| --- | --- | --- |
| Frahan Stone Proxy Mesh | build a low-poly proxy mesh per stone for fast collision | future; can wrap VHACD or CoACD via `Frahan.NativeBridge` |
| Frahan Stone Descriptor | extract size, volume, footprint, signature | future |
| Frahan Course Segmentation | segment a wall mesh into masonry courses | future |
| Frahan Ashlar Volume Pack | course-by-course packing into a 3D wall | future; builds on `GreedyMeshHeightmapPacker` |
| Frahan Ashlar Face Match | match face-to-face contacts between adjacent stones | future; needs face descriptors |
| Frahan Cut Suggestions 3D | propose cuts that improve yield without increasing waste | future |
| Frahan Contact Graph | output a graph of stone-to-stone face contacts | future |
| Frahan Masonry Report | yield, average bed thickness, per-course stats | future |

## 5. Acceptance contract (live)

A valid `Pack3DIrregular*` run produces:

- `PackResult.Placements` - list of `PackPlacement(item, box, yaw_deg, score, sequence)`.
- `PackResult.Failures` - list of `PackFailure(item, reason)`.
- `PackResult.Heightmap` - visualisation mesh.
- `PackResult.Container` - back-reference to the container.
- `PackResult.PackedVolume` - sum of placed item volumes.
- `PackResult.FillRatio` - `PackedVolume / Container.Volume`.

For mesh-heightmap variants, `MeshPackResult` is the analogous DTO.

## 6. Validation rules

- Stone meshes must be closed manifold (or `MeshCleanup`-fixable).
- Container mesh must be closed manifold.
- `Settings.CellSize > 0`, `Settings.Clearance >= 0`,
  `Settings.MaxCandidatesPerItem > 0`.
- `Settings.AllowYaw90` toggle restricts rotation to 0° / 90° (default
  `true`).
- Cancellation honoured at every per-item placement loop.

## 7. Failure modes

- **Non-manifold stone** → recorded in failures; pack continues.
- **Container too small** → all items fail with reason
  `container_too_small`.
- **Heightmap exhausted** → remaining items fail with
  `no_feasible_candidate`.
- **Cancelled** → partial result returned.

## 8. Performance targets

- 50 stones, container 1 m³, cell size 10 mm: ≤ 10 s.
- 500 stones, container 10 m³, cell size 20 mm: ≤ 5 min.
- Memory bounded by heightmap resolution (cells × 8 B for the
  height value).

## 9. Tests required

- Unit: `Heightmap.PlaceFootprint` updates the cell heights
  monotonically.
- Unit: `GreedyHeightmapPacker.Pack` on a 5-stone fixture matches a
  hand-computed expected `FillRatio` within 1 %.
- Integration: open `outputs/.../frahan_stonepack/share/.../*.gh`
  fixtures and confirm `Pack3DIrregularComponent` produces the
  expected number of placements.

## 10. Out of scope for v1

- Full SO(3) rotation set (still yaw-only).
- Stone-on-stone face match (proposed component).
- Saw-bed cut planning (covered by GeoCut spec, **09**).
