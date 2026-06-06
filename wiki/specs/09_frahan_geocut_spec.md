# 09 - Frahan GeoCut Spec

**Spec version:** 0.1 (proposed-only; no live source)
**Sources:** `frahan/frahan_geopack_geocut_final_merged_landscape.md`,
runbook § 16.5, the
`frahan_geocut_crack_aware_codebase.zip` (research bundle, catalogued
not extracted).

## 1. Goal

Given a `BlockGraph` (from GeoPack) and a target output (slabs,
billets, or finished tiles), GeoCut produces:

- a sequence of saw cuts that yields the target outputs;
- a yield report (waste vs product);
- per-cut crack-awareness flags so cuts that cross known cracks are
  flagged, re-routed, or rejected;
- a saw-bed layout plan for fabrication.

## 2. Proposed components (runbook § 16.5)

| Component | Purpose |
| --- | --- |
| Frahan Bench Block Builder | turn a quarry bench into a sequence of `BenchBlock`s |
| Frahan Rift Ledger | record the natural rift orientation per block |
| Frahan Rift Strike Mapper | map the strike (rift azimuth) to a saw-bed orientation |
| Frahan Crack Graph Extractor | extract the crack subgraph relevant to one block |
| Frahan Block Section Preview | show a planned section through the block |
| Frahan Slab Candidate Generator | enumerate slab thickness × orientation options |
| Frahan Crack-Slab Intersector | mark which slab candidates intersect known cracks |
| Frahan Slab Yield Optimizer | pick the slab plan that maximises yield |
| Frahan Billet Cutter | sub-divide slabs into billets |
| Frahan Saw Bed Optimizer | pack billets onto the saw bed for fabrication |
| Frahan GeoCut Report | yield, waste, cut count, crack-conflict list |
| Frahan Waste Report | per-cut waste polygons |

## 3. Quarry / fracture terminology (runbook § 17)

Frahan-defined terms (UI labels):

- **Frahan Slab Ledger** - book-keeping record per slab.
- **Frahan Rift Ledger** - book-keeping record per natural rift.
- **Frahan Rift Dresser** - operation that aligns a saw-bed cut to the
  observed rift.
- **Frahan Bench Block** - the largest contiguous block extractable
  from a quarry bench.
- **Frahan Saw Bed** - the working surface where slabs are cut.
- **Frahan Rift Strike** - the strike (compass orientation) of the
  rift surface.
- **Frahan Bed Slicer** - the kinematic model of the bed-slicing saw.

Scientific fracture terms used in the spec but **never** redefined:
joint, fault, vein, bedding plane, foliation, cleavage, fracture
aperture, fracture persistence, fracture spacing, fracture roughness,
fracture orientation, dip, strike, discontinuity set, rock bridge,
damage zone, uncertainty buffer, through-crack, crack-slab
intersection. Each is used per its standard structural-geology
definition; the Frahan spec re-states the definition the first time it
is used in any document.

## 4. Pipeline

```
BlockGraph + target product
  → BenchBlockBuilder       (cut the bench into bench blocks)
  → RiftLedger              (record rift orientation per block)
  → SlabCandidateGenerator  (enumerate thickness × orientation × kerf)
  → CrackSlabIntersector    (mark conflicts with the crack graph)
  → SlabYieldOptimizer      (pick the highest-yield non-conflicting plan)
  → BilletCutter            (sub-divide slabs into billets)
  → SawBedOptimizer         (pack billets onto the saw bed)
  → GeoCutReport            (yield, waste, conflicts)
```

## 5. Frahan-owned DTOs (proposed)

```csharp
public sealed class BenchBlock { /* OBB + parent BlockCell + Rift */ }
public sealed class Rift       { /* azimuth, dip, confidence */ }
public sealed class SlabPlan   { /* thickness, orientation, kerf, count */ }
public sealed class SlabConflict { /* crack ID, intersection length */ }
public sealed class SlabYield  { /* yield, waste, ConflictCount */ }
public sealed class Billet     { /* parent slab + dimensions */ }
public sealed class SawBedLayout { /* billet placements + sequence */ }
public sealed class GeoCutReport { /* yields + cut sequence + conflicts */ }
```

## 6. Acceptance contract

A GeoCut run produces:

- `GeoCutReport.YieldRatio` - `total_billet_volume /
  total_bench_block_volume`.
- `GeoCutReport.CutCount` - number of saw cuts.
- `GeoCutReport.Conflicts` - list of `SlabConflict`s the user
  accepted (otherwise the optimiser would have rejected them).
- `GeoCutReport.SawBedLayout` - final billet placements.

## 7. Validation rules

- Every slab must be inside its parent bench block.
- Every billet must be inside its parent slab.
- No saw bed cut may cross a through-crack with confidence > 0.8
  unless the user explicitly approved it.

## 8. Performance targets

- 1 bench block, 100 slab candidates, 10 conflicts: optimiser
  ≤ 5 s.
- 100 billets onto a 2 m × 1 m saw bed: layout ≤ 30 s.

## 9. Dependencies

- BlockGraph from GeoPack (spec **08**).
- Slab and billet geometry - pure managed; uses `Frahan.Geometry3D`.
- Saw-bed layout - uses the same 2D irregular-sheet packing solver
  family (`Frahan.GH.TwoD.IrregularSheetFillV506`) once that is
  generalised.

## 10. Tests required

- Unit: `SlabYieldOptimizer` returns the higher-yield plan on a
  two-candidate fixture.
- Unit: `CrackSlabIntersector` flags the correct intersections on a
  hand-built cracked-block fixture.
- Integration: end-to-end GeoPack → GeoCut on a small sample bench
  produces non-zero `YieldRatio` and `CutCount`.

## 11. Out of scope for v1

- Real-time crack adjustment during sawing.
- Multi-machine / multi-bed scheduling.
- Force / vibration prediction on the saw blade.
