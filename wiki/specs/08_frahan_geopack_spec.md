# 08 - Frahan GeoPack Spec

**Spec version:** 0.1 (proposed-only; no live source)
**Sources:** `frahan/frahan_geopack_geocut_final_merged_landscape.md`,
runbook § 16.4, plus the nested `frahan_quarry_blockcutopt_codebase.zip`,
`frahan_quarry_blockcutopt_3d_bvh_codebase.zip`,
`frahan_geocut_crack_aware_codebase.zip`, and
`frahan_quarrysuite_integrated_v0_1_20260503.zip` archives inside the
research bundle (catalogued, not extracted).

## 1. Goal

GeoPack is the **input pipeline** for the quarry stack. Its job is to
turn raw site data (point clouds, meshes, GPR slices, photogrammetry)
into a structured **block graph** that downstream GeoCut and
QuarryCutOpt can plan against.

## 2. Scope

GeoPack covers:

- ingestion of 3D scans (PLY, PTS, OBJ, PLY) and GPR slices (image
  stacks);
- unit normalisation (mm vs m vs ft);
- crack candidate detection (geometric heuristics; ML-assisted in v2);
- crack surface fit (planar / quadric / mesh);
- crack-graph editing (an editable DAG of through-cracks, partial
  cracks, and uncertainty buffers);
- block-graph construction (per-bench, per-rift volume cells);
- per-cell uncertainty tagging;
- generation of block candidates ready for GeoCut.

GeoPack does **not** cut, slab, or saw - those live in GeoCut and
QuarryCutOpt.

## 3. Proposed components (runbook § 16.4)

| Component | Purpose | Inputs | Outputs |
| --- | --- | --- | --- |
| Frahan Import PointCloud | load PLY / PTS / XYZ | path, units | `PointCloud` (Frahan DTO) |
| Frahan Import Mesh | load OBJ / PLY / STL | path, units | `Mesh` |
| Frahan Import GPR Slice | load image-stack GPR | folder, slice spacing | `GprVolume` |
| Frahan Normalize Units | convert units | object, source unit, target unit | object in target unit |
| Frahan Detect Crack Candidates | geometric crack detection | mesh / point cloud, sensitivity | `List<CrackCandidate>` |
| Frahan Fit Crack Surfaces | fit planar / quadric / mesh to candidates | `List<CrackCandidate>` | `List<CrackSurface>` |
| Frahan Edit Crack Graph | manual edits to the crack DAG | `CrackGraph`, edits | `CrackGraph` |
| Frahan Build Block Graph | partition the volume into cells separated by crack surfaces | mesh, `CrackGraph` | `BlockGraph` |
| Frahan Tag Uncertainty | annotate cells with uncertainty buffers | `BlockGraph`, source quality | `BlockGraph` |
| Frahan Generate Block Candidates | enumerate block candidates per cell | `BlockGraph`, target block size | `List<BlockCandidate>` |

## 4. Frahan-owned DTOs (proposed)

```csharp
public sealed class CrackCandidate
{
    public IReadOnlyList<Point3d> SamplePoints { get; }
    public double Confidence { get; }      // 0..1 from the detector
    public string Source { get; }          // "geometric", "ml", "manual"
}

public sealed class CrackSurface
{
    public CrackSurfaceKind Kind { get; }  // Plane | Quadric | Mesh
    public Plane FitPlane { get; }         // populated when Kind == Plane
    public Brep FitQuadric { get; }        // populated when Kind == Quadric
    public Mesh FitMesh { get; }           // populated when Kind == Mesh
    public double RmsError { get; }
}

public sealed class CrackGraph
{
    public IReadOnlyList<CrackSurface> Cracks { get; }
    public IReadOnlyList<Edge> Adjacency { get; } // crack-to-crack intersections
}

public sealed class BlockGraph
{
    public IReadOnlyList<BlockCell> Cells { get; }
    public IReadOnlyList<Contact> Contacts { get; } // cell-to-cell faces
}

public sealed class BlockCandidate
{
    public BlockCell Cell { get; }
    public Box3 OrientedBoundingBox { get; }
    public double Volume { get; }
    public double UncertaintyBuffer { get; } // mm
}
```

## 5. Dependencies (proposed)

- Geometric crack detection: pure managed (`Frahan.Mesh`).
- Surface fit: native via `Frahan.Native.GeometryCore` (proposed)
  for least-squares quadric fits; managed fallback for plane fits.
- Block-graph construction: native via `Frahan.Native.CGAL` (proposed)
  for boolean / arrangement operations, **license-gated** (see
  reference register entry 1).

## 6. Acceptance contract

A GeoPack run produces:

- `BlockGraph` with `Cells.Count > 0`.
- Per-cell `UncertaintyBuffer >= 0`.
- `List<BlockCandidate>` with at least one candidate per cell.
- A round-trip JSON export so external tools can consume the graph.

## 7. Validation rules

- All crack surfaces must be fully inside the input volume's AABB.
- Crack-to-crack intersections must be tagged in `CrackGraph.Adjacency`.
- No cell may have zero volume after the partition.

## 8. Failure modes

- Bad input units (m vs mm) → component error with explicit unit
  message.
- Crack-detector confidence below threshold → empty `CrackGraph`,
  warning emitted.
- CGAL unavailable → fall back to managed quadric fits with a warning.

## 9. Performance targets (rough)

- Mesh ≤ 1M faces: crack detection ≤ 30 s on a 4-core CPU.
- 1k crack candidates: graph construction ≤ 2 min.
- GPR slice stack 100 slices @ 1k × 1k: ingestion ≤ 1 min.

## 10. Tests required

- Unit: `Detect Crack Candidates` returns ≥ 1 candidate on a
  hand-prepared cracked-cube fixture.
- Unit: `Fit Crack Surfaces` returns RMS < 0.5 mm on a planar fixture.
- Integration: open a sample GPR slice stack and confirm
  `BuildBlockGraph` produces the expected number of cells.

## 11. Out of scope for v1

- ML-assisted crack detection (covered in **12**).
- Saw-bed planning (covered in **09**).
- Real-time on-machine slice ingestion.
