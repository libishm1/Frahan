# COPY_MANIFEST - item 30 BlockCutOpt validation (A4)

Migration prep for `examples/30_blockcutopt_validation`. PREP ONLY. Nothing copied into `examples/` and
nothing committed by this pass. This manifest maps every source file to its target path. Style: short
sentences, no em dashes.

Source dir: `D:/code_ws/Template-General/outputs/2026-06-04/hitl_cards/validation_pack/cards`
Target dir: `D:/frahan-stonepack/examples/30_blockcutopt_validation`

Scope per `handoffs/MIGRATION_GAP_TODO.md` item 30 and the item note: A4 ONLY. A1/A2/A3/A5 carry the
1000x unit bug and are NOT migrated. A4 is the one validation-pack card recorded PASS (N=163,
deterministic Parallel.For pose-grid search; memory project_validation_pack_units_a4).

## Files to copy (renamed to example-scoped names)

| # | Source file | Target path | LFS? | Rename rationale |
|---|---|---|---|---|
| 1 | `A4_blockcutopt.gh` | `examples/30_blockcutopt_validation/30_blockcutopt.gh` | gh (see risks) | drop the `A4_` validation-pack prefix; scope to example 30 |
| 2 | `A4_blockcutopt.md` | `examples/30_blockcutopt_validation/30_blockcutopt_CARD.md` | no | original HITL card kept beside the README as the provenance card; README is the house-format doc |
| 3 | `a4_bench.3dm` | `examples/30_blockcutopt_validation/30_bench_input.3dm` | 3dm (27 KB, small) | this is the LIVE input (bench fracture mesh on layer `Bench_Mesh` + tested-area bbox); name it as the input |
| 4 | `a4_blocks_in_fractures.3dm` | `examples/30_blockcutopt_validation/30_blocks_in_fractures.3dm` | YES (19.4 MB) | baked Loviisa-window result; large -> LFS |
| 5 | `tn_heavy_dfn.3dm` | `examples/30_blockcutopt_validation/30_tn_heavy_dfn_input.3dm` | 3dm (84 KB, small) | the heavy Tamil-Nadu DFN INPUT that produced tn_heavy_result; ships so the heavy run reproduces |
| 6 | `tn_heavy_result.3dm` | `examples/30_blockcutopt_validation/30_tn_heavy_result.3dm` | YES (44.7 MB) | baked heavy-DFN result; large -> LFS |
| 7 | `png/A4_canvas.png` | `examples/30_blockcutopt_validation/30_canvas.png` | no (59 KB) | the GH canvas capture |
| 8 | `png/a4_blocks_in_fractures.png` | `examples/30_blockcutopt_validation/30_blocks_in_fractures.png` | no (207 KB) | the headline result (blocks placed inside the fracture mesh) |
| 9 | `png/a4_blocks_stacked.png` | `examples/30_blockcutopt_validation/30_blocks_stacked.png` | no (185 KB) | the recovered intact blocks, stacked |
| 10 | `png/tn_heavy_result.png` | `examples/30_blockcutopt_validation/30_tn_heavy_result.png` | no (150 KB) | the heavy-DFN solve capture |

## Explicitly NOT copied

- `A1_nest_nfp.md`, `A2_settle_physics.md`, `A3_softicp.md`, `A5_match_hungarian.md` and their
  `a1_nest.3dm` / `a2_settle.3dm` / `a3_icp.3dm` / `a5_match.3dm` - other validation-pack cards, out of
  scope (A4 only).
- `a4_blocks_in_fractures.3dmbak` - `.3dmbak` backup, skipped per recipe.
- `png/_smoke.png` - internal smoke-test capture, not a deliverable.
- `stone_carving_simualtion.gh` (52 MB), `stone_carving_simulation_LIGHT.{gh,3dm}`,
  `stone_carving_simulation_LIGHT.3dm.rhl` - unrelated carving files that landed in the same source
  folder; belong to the carving examples, not item 30.

## Decision: tn_heavy_dfn.3dm added beyond the item-note list

The item note lists `tn_heavy_result.3dm` (the baked heavy result) but not its input. I add
`tn_heavy_dfn.3dm` (84 KB) so the heavy Tamil-Nadu run is reproducible, not just a frozen render. It is
tiny and matches the example-09/24 pattern of shipping the input that produced the baked result. If the
reviewer wants strict item-note scope, drop row 5; the result PNG + .3dm still document the run.

## Target folder, after migration (10 files)

```
examples/30_blockcutopt_validation/
  README.md                       (from README_DRAFT.md in this prep folder)
  30_blockcutopt_CARD.md          (= A4_blockcutopt.md, provenance card)
  30_blockcutopt.gh               (= A4_blockcutopt.gh)
  30_bench_input.3dm              (= a4_bench.3dm)               LIVE INPUT
  30_tn_heavy_dfn_input.3dm       (= tn_heavy_dfn.3dm)          heavy input
  30_blocks_in_fractures.3dm      (= a4_blocks_in_fractures.3dm) LFS, baked
  30_tn_heavy_result.3dm          (= tn_heavy_result.3dm)        LFS, baked
  30_canvas.png                   (= png/A4_canvas.png)
  30_blocks_in_fractures.png      (= png/a4_blocks_in_fractures.png)
  30_blocks_stacked.png           (= png/a4_blocks_stacked.png)
  30_tn_heavy_result.png          (= png/tn_heavy_result.png)
```
