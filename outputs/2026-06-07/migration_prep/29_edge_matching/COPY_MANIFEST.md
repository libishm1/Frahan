# COPY_MANIFEST - migration prep for example 29_edge_matching

Migration item 29 (Edge matching v2) from the audited MIGRATION_GAP_TODO.md.
Source batch: `Template-General/outputs/2026-05-30/hitl_cards/edge_matching_v2/cards/`.
Migrate the v2 batch ONLY (the 2026-05-22 and 2026-05-25 edge cards are superseded).
Style: short sentences, no em dashes.

## Source directory (listed, verified 2026-06-07)
`D:/code_ws/Template-General/outputs/2026-05-30/hitl_cards/edge_matching_v2/cards/`

9 files, 3 cards x (.gh + .3dm + .md). No `.3dmbak` present, so nothing to skip on that count.

```
three_pieces_chain.3dm     9305 B
three_pieces_chain.gh     22793 B
three_pieces_chain.md      4444 B
two_pieces_clean.3dm       8765 B
two_pieces_clean.gh        8732 B
two_pieces_clean.md        5425 B
two_pieces_no_match.3dm    8597 B
two_pieces_no_match.gh     7267 B
two_pieces_no_match.md     4267 B
```

## Component under test (all 3 cards)
- EdgeMatch Solve     GUID `D5F10001-ED9E-4ED9-A001-ED9EED9E0001`
- EdgeMatch Segments  GUID `D5F10002-ED9E-4ED9-A002-ED9EED9E0002`
- EdgeMatch Options   GUID `D5F10003-ED9E-4ED9-A003-ED9EED9E0003`
These ship from `Frahan.StonePack.GH` (wraps `Frahan.EdgeMatching.Core`). Do NOT reuse or change these
GUIDs (HITL gate). The Frahan example must run against the shipped `install/` .gha, which already carries
`Frahan.EdgeMatching.Core.dll` + `MathNet.Numerics.dll` (per code_ws AGENTS.md section 3 deploy notes).

## File map (source -> target)
Target example root: `D:/frahan-stonepack/examples/29_edge_matching/`.
Rename to clean example-scoped names (NN_ prefix + descriptive slug), matching the house pattern used by
examples 10-14/26 (e.g. `12_trencadis_catalog.gh`, `26_loviisa_vector_load.gh`).

| # | Source file | Target path | Notes |
|---|---|---|---|
| 1 | `cards/two_pieces_clean.gh`     | `examples/29_edge_matching/29a_two_pieces_clean.gh`      | LFS (.gh). Clean single-shard match. |
| 2 | `cards/two_pieces_clean.3dm`    | `examples/29_edge_matching/29a_two_pieces_clean.3dm`     | LFS (.3dm). Fixture A + B polylines. |
| 3 | `cards/two_pieces_no_match.gh`  | `examples/29_edge_matching/29b_two_pieces_no_match.gh`   | LFS (.gh). Negative test. |
| 4 | `cards/two_pieces_no_match.3dm` | `examples/29_edge_matching/29b_two_pieces_no_match.3dm`  | LFS (.3dm). Two non-mating rectangles. |
| 5 | `cards/three_pieces_chain.gh`   | `examples/29_edge_matching/29c_three_pieces_chain.gh`   | LFS (.gh). Beam chain A->B->C. |
| 6 | `cards/three_pieces_chain.3dm`  | `examples/29_edge_matching/29c_three_pieces_chain.3dm`  | LFS (.3dm). Three fixture polylines. |
| 7 | `cards/two_pieces_clean.md`     | `examples/29_edge_matching/cards/29a_two_pieces_clean.md`    | HITL card (per-stage diagnostics). |
| 8 | `cards/two_pieces_no_match.md`  | `examples/29_edge_matching/cards/29b_two_pieces_no_match.md` | HITL card. |
| 9 | `cards/three_pieces_chain.md`   | `examples/29_edge_matching/cards/29c_three_pieces_chain.md`  | HITL card. |
| - | (authored at migrate step)      | `examples/29_edge_matching/README.md`                   | from README_DRAFT.md in this folder. |
| - | (captured live in Rhino)        | `examples/29_edge_matching/29a_two_pieces_clean.png`    | top-view shaded capture. |
| - | (captured live in Rhino)        | `examples/29_edge_matching/29b_two_pieces_no_match.png` | top-view shaded capture. |
| - | (captured live in Rhino)        | `examples/29_edge_matching/29c_three_pieces_chain.png`  | top-view shaded capture. |

Naming choice: 29a/29b/29c keeps the three fixtures sortable and example-scoped while preserving the
clean / no-match / chain semantics from the source slugs. The three .md HITL cards keep their detailed
per-stage diagnostics and go under a `cards/` subfolder so the example root stays a clean .gh + .3dm + .png
+ README set, the same shape as the other examples.

## Skip list
- No `.3dmbak` files in source. Nothing skipped on that count.
- No other extraneous files (no `.gh.bak`, no temp). The 9 files above are the full v2 batch.
- Superseded batches NOT migrated (out of scope per item note):
  `2026-05-22/.../edge_matching` and `2026-05-25/.../edge_matching` cards.

## LFS
`.gh` and `.3dm` are LFS-tracked per `.gitattributes`. Confirm the patterns exist before staging; the
binaries must land as LFS pointers, not blobs (code_ws AGENTS.md "Binary assets and Git LFS"). The .png
captures are also LFS (image rule in the Frahan repo, same as the other example PNGs).

## DO NOT (this prep step)
Do not copy into `examples/`. Do not commit. This folder holds the plan + drafts only.
