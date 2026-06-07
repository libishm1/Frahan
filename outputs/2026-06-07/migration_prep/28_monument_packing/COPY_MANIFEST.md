# COPY_MANIFEST - Example 28 Monument packing

Migration prep for item 28 from `handoffs/MIGRATION_GAP_TODO.md`. This manifest lists every source file
mapped to its target path. No file is copied here. Copying and live verification happen in the migration
step. Style: short sentences, no em dashes.

Source dir: `D:/code_ws/Template-General/outputs/2026-05-22/hitl_cards/monument_packing/cards`
Target dir: `D:/frahan-stonepack/examples/28_monument_packing/`

## Source listing (verified)

```
01_monument_inventory.3dm   20716 B
01_monument_inventory.gh     4756 B
01_monument_inventory.md      496 B
02_pack_in_block.3dm        20706 B
02_pack_in_block.gh          5070 B
02_pack_in_block.md           885 B
03_pack_on_bench.3dm        20706 B
03_pack_on_bench.gh          5157 B
03_pack_on_bench.md           742 B
```

No `.3dmbak` files present. No PNGs present (renders are produced in the live step). 9 source files, 6 to
copy after renaming the 3 `.md` cards into the README. Total source size ~83 KB; all small, no large-data
subset needed (see Data section).

## File map (source -> target)

Naming kept close to source. The `01_/02_/03_` prefixes are retained so the three cards stay ordered and
self-describing inside the example. The 3 per-card `.md` files are NOT copied verbatim; their content is
folded into the single example `README.md` (house format), matching examples 11/12/15/26 which carry one
README, not per-card markdown.

| # | Source file | Target path | Action |
|---|---|---|---|
| 1 | `01_monument_inventory.gh`  | `examples/28_monument_packing/28a_monument_inventory.gh`  | copy + rename (example-scoped) |
| 2 | `01_monument_inventory.3dm` | `examples/28_monument_packing/28a_monument_inventory.3dm` | copy + rename (LFS) |
| 3 | `02_pack_in_block.gh`       | `examples/28_monument_packing/28b_pack_in_block.gh`       | copy + rename (example-scoped) |
| 4 | `02_pack_in_block.3dm`      | `examples/28_monument_packing/28b_pack_in_block.3dm`      | copy + rename (LFS) |
| 5 | `03_pack_on_bench.gh`       | `examples/28_monument_packing/28c_pack_on_bench.gh`       | copy + rename (example-scoped) |
| 6 | `03_pack_on_bench.3dm`      | `examples/28_monument_packing/28c_pack_on_bench.3dm`      | copy + rename (LFS) |
| - | `01_monument_inventory.md`  | folded into `examples/28_monument_packing/README.md`      | do not copy verbatim |
| - | `02_pack_in_block.md`       | folded into `examples/28_monument_packing/README.md`      | do not copy verbatim |
| - | `03_pack_on_bench.md`       | folded into `examples/28_monument_packing/README.md`      | do not copy verbatim |

PNGs to be created in the live step (not present in source), one per card:

| Target PNG | Capture |
|---|---|
| `examples/28_monument_packing/28a_monument_inventory.png` | shaded top/iso of the 12 baked monument boxes + inventory panel |
| `examples/28_monument_packing/28b_pack_in_block.png`       | placed boxes inside the single bench block (per-cell pack) |
| `examples/28_monument_packing/28c_pack_on_bench.png`       | placed boxes across the quarry bench + fill-ratio panel |

## Rename rationale

- Prefix `28a/28b/28c` ties each artifact to example 28 and preserves the source card order
  (inventory -> pack-in-block -> pack-on-bench). This follows the in-example multi-card pattern already
  used (example 15 uses `15A_/15B_/15C_`, example 26 uses a single example-scoped stem).
- Keeping three `.gh` + three `.3dm` is correct: these are three distinct cards (inventory, single-block
  pack, bench pack), not redundant snapshots. They are not merged.

## LFS

`.3dm` and `.gh` are LFS-tracked per the repo `.gitattributes` (AGENTS.md binary list). All 6 copied
binaries inherit existing LFS rules. No new `.gitattributes` entry needed. PNGs are also small binaries;
confirm `.gitattributes` covers `*.png` before commit (out of scope here; commit is a later HITL step).

## Notes / flags

- The `.gh` files are GH binary archives whose payload is not plain gzip and could not be string-scanned
  headless. The internal wiring is read from the source `.md` cards: card 02/03 wire ONLY the
  inventory side. The Crack Graph + Block Graph + Cell/Bench Monument Pack chain is left to be wired by
  hand on the canvas. This is a live-step task, captured in README_DRAFT liveStepsNeeded.
- Source `.3dm` files are ~20 KB each: the fixtures (12 axis-aligned monument boxes on `Bench_Mesh`) are
  internalized, so the example is self-contained. No external dataset file is referenced.
