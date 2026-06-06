# 06 - Frahan Surface Trencadis Spec

**Spec version:** 0.1
**Sources:** live `Frahan.StonePack.Core.SurfacePacking.*` classes,
`Template-General/wiki/fabrication_workflows/surface_packing_implementation.md`,
`Template-General/wiki/fabrication_workflows/surface_packing_unwrapping.md`,
`frahan/Frahan_MASTER_RESEARCH_KNOWLEDGE_BASE_v0_2_20260503.md` (surface chapter),
runbook § 16.2.

## 1. Goal

Pack irregular fragments onto an arbitrary 3D surface (column,
freeform skin) by:

1. unwrapping the surface to a 2D chart with controlled distortion;
2. running the 2D Trencadis solver in chart space;
3. mapping the resulting placements back onto the 3D surface via
   per-face barycentric coordinates;
4. reporting per-fragment distortion so out-of-tolerance pieces can
   be re-cut or re-positioned.

## 2. Pipeline (target = live)

| Step | Class | Status |
| --- | --- | --- |
| 1. Mesh cleanup | `MeshCleanup` | implemented |
| 2. OBJ round-trip | `MeshObjIO` | implemented |
| 3. BFF unwrap (out-of-process) | `BffCommandLineRunner` | implemented |
| 4. Per-face-corner UV table | `FaceCornerUvTable` | implemented (intentionally net48-friendly: manual `GetHashCode`) |
| 5. Surface chart wrapper | `FrahanSurfaceChart` | implemented |
| 6. Chart scale calibration | `ChartScaleComputer` | implemented |
| 7. Distortion analysis | `ChartDistortionAnalyzer` + `ChartDistortionReport` | implemented |
| 8. 2D-flat → 3D mapping | `BarycentricMapper2DTo3D` | implemented |
| 9. GH front-ends | `SurfaceChartComponent`, `PackOnSurfaceComponent`, `PackSurfacesComponent` | implemented (prototype) |

## 3. Why per-face-corner UVs

A naive vertex-keyed UV table loses information at BFF seam cuts: the
*same* 3D vertex may receive two different UVs on either side of the
seam. The Frahan implementation keys UVs on `(faceIndex, cornerIndex)`
so seams are preserved. This is a non-obvious but load-bearing design
decision; do not "simplify" it back to a vertex map.

`FaceCornerUvTable.ToFlatUnweldedMesh(Mesh original3D)` builds a 2D
mesh in `Z = 0` where each face corner becomes its own vertex. Any
missing UV throws `InvalidOperationException` immediately - never
defaults to `(0, 0)`.

## 4. Chart scale and distortion

- `ChartScaleComputer` computes a scale factor so that an average
  3D triangle area maps onto an equivalent 2D area in the flat
  chart.
- `ChartDistortionAnalyzer` produces a `ChartDistortionReport`
  containing per-face area-ratio min, max, mean, std, and a list
  of high-distortion faces.
- The 2D Trencadis solver runs on the **scaled** flat chart, so
  fragments designed in mm map back to the surface at the right
  size.

## 5. BFF integration constraints

- BFF runs as `bff-command-line.exe` (Windows-x64) and reads/writes
  `.obj` files only. No managed binding exists today.
- The bundled BFF runtime (`bff-command-line.exe` + SuiteSparse +
  OpenBLAS + LAPACK + GFortran DLLs) ships inside
  `dist/frahan_stonepack-0.5.6-rh8-win.zip`. Licensing is documented
  in `docs/index/frahan_reference_register.md` (entries 8–11).
- `BffCommandLineRunner` captures `ExitCode`, stdout, stderr; the
  caller must check `ExitCode == 0` before reading the output OBJ.
- Surface inputs may be `Brep`, `Surface`, or `Mesh`. Brep / Surface
  inputs are converted to `Mesh` via `Mesh.CreateFromBrep` /
  `Mesh.CreateFromSurface` with project-default `MeshingParameters`.

## 6. Mapping 2D → 3D

`BarycentricMapper2DTo3D.MapPoint2DTo3D(Point2d uv, FaceCornerUvTable
table, Mesh original3D)`:

1. Locate the chart face that contains `uv` via a 2D point-in-triangle
   sweep (a per-face AABB index speeds this up; today the live
   implementation is linear).
2. Compute barycentric coordinates `(u, v, w)` of the point inside
   that 2D triangle.
3. Apply the same barycentrics to the **3D** triangle of the same
   face index in `original3D`.

`BarycentricMapper2DTo3D.MapPolyline2DTo3D` walks each polyline
vertex through the same routine; the result is a polyline whose
straight 2D segments become geodesic-ish 3D paths only if subdivided
finely enough.

## 7. Acceptance contract

A surface-Trencadis solver run produces:

- `PackOnSurfaceResult.Placements3D` - list of `Transform` + `Curve3D`
  per placed fragment.
- `PackOnSurfaceResult.Distortion` - `ChartDistortionReport`.
- `PackOnSurfaceResult.UnplacedParts` - list of fragments that did
  not fit on the chart.
- `PackOnSurfaceResult.YieldOnSurface` - total 3D fragment area divided
  by total 3D surface area (after distortion correction).

## 8. Validation rules

- Input mesh must be manifold (non-manifold inputs are auto-cleaned
  by `MeshCleanup` if possible; otherwise component fails).
- BFF must produce a chart with no overlapping triangles in 2D; if
  it does (rare), the user must subdivide the surface.
- Distortion report must be inspected before fabrication; fragments
  on faces with area-ratio > 1.5 are flagged for manual review.

## 9. Performance targets

- Mesh ≤ 50,000 faces, 100 fragments, 5 rotations each: ≤ 30 s end
  to end (BFF dominates).
- BFF call captured in `BffCommandLineRunner` with a configurable
  timeout (default 300 s).

## 10. Tests required

- Unit: `FaceCornerUvTable` round-trip on a known mesh keeps
  `EntryCount == 3 * faceCount` for a triangulated mesh.
- Unit: `BarycentricMapper2DTo3D` maps the chart centroid of each
  face back to the 3D centroid of the same face within 1e-6.
- Integration: a curved column unwrap → pack 50 mm × 50 mm tile
  fragments → distortion report shows max area-ratio < 1.2 for a
  well-developable surface.

## 11. Out of scope for v1

- Multi-chart unwrap (more than one 2D chart per surface).
- Geodesic remesh on the original surface (would replace the
  barycentric step with a true geodesic walk).
- Edge-affinity matching of fragment edges across surface faces.
