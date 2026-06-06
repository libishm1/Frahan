# Results at a glance (no Grasshopper required)

Measured benchmark + process results so you can see what the plugin does without opening a single `.gh`.
All numbers are machine-measured (headless harness / test suite); figures regenerate from the studies in
`../../wiki/research/packing/`. Style: short sentences, no em dashes.

## Hero: fracture modelling -> block packing
![fracture block packing](hero_fracture_block_packing.png)

Staged wire-saw guillotine recovery of intact blocks from a fractured bench: blue/green blocks are the
recovered, saw-separable stock; the green surfaces are the mapped fracture planes the cut avoids; orange
marks the kerf/edge blocks. Mode 5 (staged guillotine) = 49.3% yield at 100% saw-separable; mode 4
(voxel-DLBF) = 53.3% yield, not saw-cuttable. See `03_quarry_to_slabs` + `03_gpr_fracture_granite`.

## 2D packing (stock utilization, 80% bar)
![selection](../../wiki/research/packing/figures/study_selection_scatter.png)
Green = valid (0-overlap), red = invalid (overlaps). The evolved exact NFP-BLF is the only 0-overlap packer
crossing 80% stock-utilization with holes: 82.0% oversub, 84.7% L+hole, 89.6% on a hard 3-hole fixture.

![oversub bars](../../wiki/research/packing/figures/study_bars_oversub.png)
![hard bars](../../wiki/research/packing/figures/study_bars_hard.png)

Live Rhino shaded captures of the corrected example outputs (per-application physical scale, on the z=0
ground plane; see `../../examples/` and `../../wiki/research/tolerances_dimensions_slm_roses.md`):
![2D slab nest](../../examples/10_pack2d/10_pack2d_result.png)
![3D quarry block](../../examples/11_pack3d/11_pack3d_result.png)
2D slab nest 3.2x2.0 m (exact NFP-BLF, 18 parts, 0-overlap) and 3D quarry block 3.0x1.5x1.5 m
(Block Pack Tree, 12/12 packed, base on the ground).

## 3D packing (volumetric ratio)
![3d volumetric](../../wiki/research/packing/figures/pack3d_volumetric.png)
Dlbf best-of-orientation 70.4% vol-fill (vs 66.4% baseline); TreePackForest 37.2% (100% guillotine);
masonry BestFit 65.2% / Ashlar 60.8%. Domains are not cross-comparable.

## Packing benchmark overview + masonry/quarry decision
![packbench](../../wiki/research/packing/figures/packbench_overview.png)
![masonry quarry](../../wiki/research/packing/figures/masonry_quarry_decision.png)

## Fracture recovery + GPR
![packer comparison](../../wiki/research/packing/figures/fig_packer_comparison.png)
![blockpack capture](../../wiki/research/packing/figures/evolved_blockpack_capture.png)
RecoveryCascade recovers +21% over single-scale BlockCutOpt by re-cutting cracked blocks at finer scales.

## Digital-fabrication entrypoint examples (10-14, corrected scale + position)
Live-built, solved, and captured at correct per-application physical scale (meters for slab/block/monument,
millimetres for mosaic/vessel), on the z=0 ground plane. Full set + numbers in `../../examples/`.
![Trencadis mosaic](../../examples/12_trencadis/12_trencadis_result.png)
![surfaces from a solid](../../examples/13_surface_mapping/13_surface_segments.png)
![Trencadis on the twist](../../examples/13_surface_mapping/13_surface_trencadis.png)
![Kintsugi reassembly](../../examples/14_kintsugi/14_kintsugi_result.png)
Trencadis mosaic 1100 mm panel / 100 shards / 5 mm grout; twisted monument 1.2x1.2x3.5 m split into 6
surfaces by CGAL dihedral angle then clad (408 shards, 4 mm grout); Kintsugi vessel reassembled on
Breaking Bad parity data (Port mode, verifier 0.71 STRONG). Component defaults now self-scale (auto
tolerance, auto cell size, strict no-overlap) so these open correct on the first try.

## Test + build health
983 tests pass (0 fail) from a clean clone; all projects build green. See `../INSTALL.md`.

## Where the numbers come from
`../../wiki/research/packing/`: PACK2D_STUDY_REPORT, PACK3D_STUDY_REPORT, ROSES_2D_PACKER_GUIDE,
MASONRY_QUARRY_DECISION, SYNTHESIS_2D/3D/BEYOND_BLF, pack2d_study_metrics.csv. Regenerate with
`tools/Frahan.StonePack.Harness --packbench` / `--pack2dstudy` + `plot_pack2d_study.py`.
