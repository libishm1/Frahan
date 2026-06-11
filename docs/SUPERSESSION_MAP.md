# Supersession map

Legacy component -> evolved form -> the benchmark that justified the move.

**Nothing here is deleted.** Per the compatibility policy every legacy
component keeps its GUID, class, and input/output order, so old canvases
and the shipped examples stay loadable. Legacy components are deprecated
in place (hover text states the supersession, `[RelatedComponent]` points
at the evolved form), following the `AutoMeshRepairComponent` house
pattern. The originals remain credited as the baselines that the evolved
forms were measured against.

| Superseded / legacy | Evolved form | Benchmark that justified it |
|---|---|---|
| 2D V1/V2/V3/V506 + Pack2DBottomLeft (hidden) | "Freeform Sheet Nest (Exact NFP) FreeNestX" + the unified facade | mean 53.9% wasted-area cut vs V506 at strict 0-overlap; only 0-overlap packer crossing 80% util_stock: 82.0/84.7/89.6% on the three study fixtures |
| Pack3D heightmap family (Pack3DIrregular E36C3F7D, Pack3DIrregularContainer B3E8A42F, Pack3DMeshHeightmap hidden) | "Settle 3D (Physics)" volume packing, or "Block Pack (Tree)" for saw-cuttable subdivision | CoACD+Bullet settle beats the heightmap baseline on ETH1100 compactness (honest record: 1.05-1.15x, NOT 2x) |
| RubbleWallSettle v1 | settle v2 objective | +97% mean support clearance, 23/24 stable, live 24 ETH stones |
| AutoInterfaces detector path | exact-joint generator Assembly | 40-stone verify 284s -> 10.4s |
| MasonryStabilityRbe (permissive) | Masonry Stability Check + CRA | H-model: RBE accepts / CRA rejects |
| FrahanGprRadargramReader (legacy) | GPR File Loader + GPR Fracture Extract | 11 headless tests + 5 example grids, cross-validated against RGPR |
| FractureBlockPack voxel-DLBF mode | mode 5 staged wire-saw | 49.3% yield @100% separable vs 53.3% @0% |

## Reading the table

- "Superseded" means: do not use in new canvases. It does NOT mean
  removed. Old `.gh` files that reference these components still open
  and solve.
- The evolved forms are the entry points the persona map
  (`PERSONA_MAP.md`) and the examples point at.
- Benchmarks are recorded as measured. Where an earlier claim was
  corrected (the Pack3D 2x claim), the honest figure is kept here.
