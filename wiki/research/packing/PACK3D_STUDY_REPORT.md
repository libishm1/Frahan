# 3D packing study: volumetric ratios + Dlbf evolution + keep/hide

Date: 2026-06-06. Style: short sentences, no em dashes. Measured headless via `Harness --packbench` (3D
section) and `--pack3d` (mesh container on ETH stones). Grounded in `SYNTHESIS_3D.md` (the 3D SLM review).

## 1. The metric: stock packing volumetric ratio
`vol_ratio = sum(placed piece/stone volume) / container volume`. For the box/mixed-size packers the volume
is the exact AABB volume; for the mesh container packer it is the true mesh volume (VolumeMassProperties).
The ratio is comparable WITHIN a truth domain, NOT across domains (a guillotine-separable packer sacrifices
fill for saw-cuttability; a masonry course packer fills a wall frame, not a solid box). Always read the
ratio next to the domain + the constraint it honors.

## 2. Measured (Harness --packbench, 20 ETH stones / synthetic catalogues)

| Packer | Domain / constraint | vol_ratio (fill) | Placed | Note |
|---|---|---|---|---|
| **Dlbf3D + best-of-orientation** | AABB mixed-size, stackable, revenue | **70.4%** | 28 pc | **evolved leader** (+4.0pp, +1 pc, +6 revenue vs baseline) |
| Dlbf3D baseline (no orient) | AABB mixed-size, stackable | 66.4% | 27 pc | pre-evolution |
| BestFitInventoryPacker | masonry coursed-rubble wall | 65.2% | 20/20 | wall-frame coverage, not solid-box fill |
| AshlarLayoutEngine | masonry coursed ashlar | 60.8% | 20/20 | height-binned courses |
| TreePackForest | 3D box GUILLOTINE (saw-separable) | 37.2% | 19/20 | 100% wire-saw separable -- fill traded for cuttability |
| GreedyMeshHeightmap / V2-skyline (--pack3d) | irregular MESH container | ~23.7% (mesh-honest) | 30/30 | true-mesh density; proxy heightmap, no interlock (SYNTHESIS_3D) |
| RecoveryCascade (block+slab) | fracture recovery | 15.2% recovered | 11 blk | recovery %, different domain (intact blocks from a fractured mass) |
| BlockCutOptSolver | fracture pose-search | 12.5% recovered | 4 blk | single-scale inner loop |

## 3. The Dlbf evolution (this session)
`Dlbf3dMixedSizePacker` gained a best-of-orientation Pack overload: each piece is tried in its up to 6
distinct axis-permutations and placed in the orientation whose best free cell is lowest (z, then y, then x);
volume + revenue are permutation-invariant so the placed piece records its oriented dims. The default
overload delegates with orientation OFF, so the 6 existing tests stay byte-identical. Measured on the AABB
4x3x2 mixed catalogue: **66.4% -> 70.4% volumetric fill, 27 -> 28 pieces, revenue 146 -> 152**, 0 overlap
(AABB), deterministic, ~46 ms. This is the safe, Rhino-free Core lever from SYNTHESIS_3D Priority 1.

Not done this pass (staged, per SYNTHESIS_3D, HITL-gated / native-dep): the mesh-accurate voxel packer
(MeshAccuratePacker + managed GJK/SAT + Bullet settle) that would lift the irregular-mesh 23.7% toward the
~32-38% the Python prototype reached; the sub-cell half-open reservation (carries an AABB-overlap risk
without a verify, so deferred); the RBE load-path stability metric (depends on the masonry RBE work).

## 4. Canvas KEEP / HIDE for 3D (with measured justification)
| Component | Domain | Verdict |
|---|---|---|
| `BlockPackTreeComponent` (TreePackForest) | box guillotine | **KEEP** -- only saw-separable 3D packer (100% guillotine), the manufacturable choice |
| `Dlbf3D` mixed-size (inside HeteroExt / BCO Mixed Pack) | AABB revenue | **KEEP** -- evolved leader for mixed-size box yield (70.4%); expose best-of-orientation |
| `Pack3DIrregularContainerComponent` (mesh heightmap) | irregular mesh | **KEEP** (canonical) -- the scan-to-container path; staged mesh-accurate upgrade |
| `Pack3DIrregularComponent` (bbox proxy MVP) | box proxy | **HIDE** -- superseded by the container packer; bbox over-count |
| `Pack3DMeshHeightmapComponent` | mesh | already hidden -- folded into the container component |
| masonry BestFit / Ashlar / BenchMonument | masonry | **KEEP** -- distinct wall/course domain (decided in MASONRY_QUARRY) |

## 5. Honesty notes
- vol_ratio is NOT comparable across domains: TreePackForest 37.2% (guillotine) is not "worse" than Dlbf
  70.4% (free stacking) -- it pays fill for full saw-separability, which the others do not provide.
- The mesh-container 23.7% is a C# mesh-honest number (V2-skyline port); the Python prototype's 32-38% used
  pybullet + VHACD and is NOT a C# result. No mesh-accurate gain is claimed until built + measured in C#.
- The Dlbf AABB number (70.4%) is for axis-aligned boxes; it does NOT transfer to the mesh-voxel
  FractureBlockPack domain (53.3% on marble was a different IsPointInside truth domain).
