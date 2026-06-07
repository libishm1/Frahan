# COPY_MANIFEST - Example 27 Polygonal masonry (Voronoi 2D/3D)

Migration prep only. Nothing is copied yet. This maps every source file to its
target path under the live Frahan example. Style: short sentences, no em dashes.

- Source dir: `D:/code_ws/Template-General/outputs/2026-05-21/polygonal_masonry_hitl_cards/cards/`
- Target dir: `D:/frahan-stonepack/examples/27_polygonal_masonry/`
- Source file count (all, incl. .3dmbak): 31. Files to copy: 28 (8 .gh + 8 .3dm + 8 .md + 4 .png reference figs). Skipped: 3 (.3dmbak).

## Conventions

- The 8 source cards (01-08) are 8 distinct fixtures of the SAME two components
  (`Polygonal Masonry Sequence` 2D, GUID B4E07A3C-..., and `Polygonal Masonry
  Sequence 3D`, GUID C5F18B4D-..., both already shipped in the live Frahan src).
  All 8 are kept as one example folder, named `27_<NN>_<slug>.<ext>`.
- The source `.gh` files are binary Grasshopper, all geometry internalised, zero
  external file-path references (verified: 0 backslash-path hits in any .gh).
  So the .gh + .3dm are copied verbatim; only the file NAME changes.
- The source `.png` files are PYTHON-REFERENCE renders (paper-figure
  reproductions), not Rhino captures. They are migrated as `*_pyref.png` so the
  example-scoped `27_<NN>_*.png` names stay reserved for the live Rhino shaded
  captures produced in the live step (see README_DRAFT liveStepsNeeded).
- Cards 02-07 are png-backed (6). Cards 01 and 08 have no reference png.
- `.3dmbak` are Rhino autosave backups. SKIP per item note and migration recipe.

## Per-file mapping

| # | Source file | Target file (under examples/27_polygonal_masonry/) | Action |
|---|---|---|---|
| 1 | `01_three_band_wall.gh` | `27_01_three_band_wall.gh` | copy + rename |
| 2 | `01_three_band_wall.3dm` | `27_01_three_band_wall.3dm` | copy + rename (LFS) |
| 3 | `01_three_band_wall.md` | `cards/27_01_three_band_wall.md` | copy + rename |
| 4 | `01_three_band_wall.3dmbak` | - | SKIP (autosave backup) |
| 5 | `02_twelve_angled.gh` | `27_02_twelve_angled.gh` | copy + rename |
| 6 | `02_twelve_angled.3dm` | `27_02_twelve_angled.3dm` | copy + rename (LFS) |
| 7 | `02_twelve_angled.md` | `cards/27_02_twelve_angled.md` | copy + rename |
| 8 | `02_twelve_angled.png` | `27_02_twelve_angled_pyref.png` | copy + rename (reference fig) |
| 9 | `03_chains_wall.gh` | `27_03_chains_wall.gh` | copy + rename |
| 10 | `03_chains_wall.3dm` | `27_03_chains_wall.3dm` | copy + rename (LFS) |
| 11 | `03_chains_wall.md` | `cards/27_03_chains_wall.md` | copy + rename |
| 12 | `03_chains_wall.png` | `27_03_chains_wall_pyref.png` | copy + rename (reference fig) |
| 13 | `04_wall_with_holes.gh` | `27_04_wall_with_holes.gh` | copy + rename |
| 14 | `04_wall_with_holes.3dm` | `27_04_wall_with_holes.3dm` | copy + rename (LFS) |
| 15 | `04_wall_with_holes.md` | `cards/27_04_wall_with_holes.md` | copy + rename |
| 16 | `04_wall_with_holes.png` | `27_04_wall_with_holes_pyref.png` | copy + rename (reference fig) |
| 17 | `05_wavy_perlin.gh` | `27_05_wavy_perlin.gh` | copy + rename |
| 18 | `05_wavy_perlin.3dm` | `27_05_wavy_perlin.3dm` | copy + rename (LFS) |
| 19 | `05_wavy_perlin.md` | `cards/27_05_wavy_perlin.md` | copy + rename |
| 20 | `05_wavy_perlin.png` | `27_05_wavy_perlin_pyref.png` | copy + rename (reference fig) |
| 21 | `06_voronoi_2d.gh` | `27_06_voronoi_2d.gh` | copy + rename |
| 22 | `06_voronoi_2d.3dm` | `27_06_voronoi_2d.3dm` | copy + rename (LFS) |
| 23 | `06_voronoi_2d.md` | `cards/27_06_voronoi_2d.md` | copy + rename |
| 24 | `06_voronoi_2d.png` | `27_06_voronoi_2d_pyref.png` | copy + rename (reference fig) |
| 25 | `07_voronoi_3d.gh` | `27_07_voronoi_3d.gh` | copy + rename |
| 26 | `07_voronoi_3d.3dm` | `27_07_voronoi_3d.3dm` | copy + rename (LFS) |
| 27 | `07_voronoi_3d.md` | `cards/27_07_voronoi_3d.md` | copy + rename |
| 28 | `07_voronoi_3d.png` | `27_07_voronoi_3d_pyref.png` | copy + rename (reference fig) |
| 29 | `08_negative_cases.gh` | `27_08_negative_cases.gh` | copy + rename |
| 30 | `08_negative_cases.3dm` | `27_08_negative_cases.3dm` | copy + rename (LFS) |
| 31 | `08_negative_cases.md` | `cards/27_08_negative_cases.md` | copy + rename |

## To be authored fresh in the example (not copied)

| Target file | Source | Action |
|---|---|---|
| `README.md` | `README_DRAFT.md` in this dir | finalise from draft after live step |
| `27_01_three_band_wall.png` ... `27_08_negative_cases.png` | live Rhino captures | produce in live step (hero + 7 more) |

## Notes

- The eight `.md` cards are HITL pass/fail cards, not READMEs. They go in a
  `cards/` subfolder so the single `README.md` is the example entry point (the
  house pattern: one README per example folder). Renaming with `27_` prefix keeps
  them example-scoped.
- LFS: all `.3dm` and `.gh` are already covered by `.gitattributes`. Largest
  binary is `07_voronoi_3d.3dm` (~101 KB) and `07_voronoi_3d.gh` (~56 KB). All
  small; no large-data subset needed (see DATA section in README_DRAFT).
- Total copied bytes are small (largest single file under 870 KB png). No
  large-data policy trigger.
</content>
</invoke>
