# Deep repo review - v0.1.0-alpha (2026-06-15)

A pre-release review across 6 dimensions (docs/citations, licensing/secrets, build+test+code,
benchmark honesty, examples, public-readiness), each finding adversarially verified. Verified
live against `main` at the time of review. Style: short sentences, no em dashes.

## Verdict

Code is sound; documentation reconciliation gates the public tag. **0 critical, 0 high, 11
medium, 9 low.** Every blocker is a find-replace or reword, not a rewrite. The build is green
(0 errors), the battery reproduces 1034 PASS / 0 FAIL / 147 SKIP from a clean clone, no secrets
leak, the LICENSE is full GPL-3.0, THIRD_PARTY_NOTICES exists, and the corrected benchmark
framings (discontinuity ~1.4x vs Open3D, CRA wall-time parity, NFP-BLF 53.9%, Lambda 0.18-0.23)
reproduce. The remaining work is documentation, not code.

## Must fix before public tag (medium)

1. **Dangling `outputs/2026-06-*` citations** in the thesis, hole-packer benchmark, and 3
   discontinuity validation cards point at dev-side study folders not bundled in the repo.
   Repoint to in-repo homes (`docs/benchmarks/`, `wiki/research/packing/`) or mark "not bundled".
2. **Out-of-repo links** in `examples/29_liveedge_floor/README.md` escape into `../../../code_ws/`.
   Remove; state the study is dev-side.
3. **Validation-card "Reproduce" footers** cite non-existent `outputs/2026-06-14/...` artifacts.
   Reword to cite the shipped worker + `data/DATA_ACCESS.md`.
4. **Marble GPR mislabel:** a stale `_SOURCE.md` + ~10 docs call the bundled marble GPR
   CC-BY-NC-ND; upstream is CC BY 4.0 (per `data/ATTRIBUTION.md`, Mendeley 10.17632/w26n6nftxs.3).
   The error over-restricts (safe direction) but misleads. Reconcile to CC BY 4.0.
5. **Stale BLOCKER rows:** `90_originality.md` + `91_roadmap.md` still list the LICENSE as a
   "placeholder" and THIRD_PARTY_NOTICES as "missing"; both are now resolved. Mark RESOLVED. The
   `frahan_reference_register.md` item is genuinely still open.
6. **`Block Pair Match 3D`** is `GH_Exposure.primary` but its hover reads "SKELETON ... not
   implemented yet" (and an inaccurate "AABB-containment" phrase - it uses Hausdorff). Hide it to
   match its two sibling stubs, or fix the wording. It is functional, just first-cut.
7. **Hole-packer headline multiplier:** docs/thesis state 0.148 ms / 146x native / ~22,000x Sparrow;
   the shipped bench test reproduces 0.347 ms / ~62x / ~9,400x on the review machine. Use the
   reproducible figure with machine/date provenance; add an `ElapsedMs` floor to the bench test.
8. **`docs/STONEPACK_THESIS.md` TOC:** all 20 internal links are broken (paths relative to
   `docs/thesis/`, file is at `docs/`). Prefix `thesis/` or use in-page anchors. Content is
   self-contained, so this is navigation only.
9. **Example 28 (hole_nest)** ships only a `.gh` (no README, no result image). Add both.
10. **Index gallery vs README drift:** `examples/README.md` Trencadis captions (100 shards;
    408 shards) disagree with `12`/`13` READMEs (28; 176). Reconcile to the committed PNGs
    (ex12 ~100 is right; ex13 176 is right, the index 408 is wrong).
11. **THIRD_PARTY_NOTICES** omits SuiteSparse / OpenBLAS / libgfortran owed by the statically-
    linked BFF exe. Add those rows with the GCC Runtime Library Exception note.

## Should fix / polish (medium-low)

- `CRA_COMPAS_PARITY.md` cites an absolute `...` timing harness; vendor or note unbundled.
- Component-count drift: 187 (components) vs 109 (families) vs 112/184/206 (old specs) never
  reconciled; add a one-line "components vs families" note, date-stamp the stale spec figures.
- Stale per-card test counts (995, 968, "150 PASS / 16 SKIP") alongside the current 1034; fix the
  two reviewer-facing files (`RUN_TESTS.md`, `27_06_README.md`), label historical logs as dated.
- `HOLE_PACKER...:149` final verdict still says "on all-rect instances" for the 146x figure; it is
  the true-hole bench instance. Last surviving "all-rect" mislabel.
- Examples 21-25 ship no `.gh`/README-reason; ex05 result image missing + `.gh` mis-named
  `06_carving_stages.gh`; ex03_gpr PNG is an input radargram with Status "Pending"; ex32 README
  contradicts itself (18 planes/2 blocks vs 24 planes/4 blocks).
- `README.md`/`REVIEWER_SUMMARY.md` overclaim "every example ships a README"; four lack one.

## Judgment calls for the maintainer (not auto-fixed)

- **Kintsugi in the default install:** `90_originality.md` claims the non-commercial weights are
  "absent from the default install," but `install/deploy.ps1` copies the plugin dir + `kintsugi.bin`.
  Not a copyleft violation (whole dist is GPLv3 + research-only), but the "commercial subset
  excludes Kintsugi by default" story is false. Either correct the register, or gate Kintsugi out
  of the default deploy.
- **Granite Dells `.laz` in-repo** ships with upstream license "Not Provided" (not public domain).
  Consider moving to the download tier (like the Stanford scans) or confirming terms with OpenTopography.
- **`handoffs/` working notes** (`COWORK_VS_CLAUDECODE_PLAN.md`, punch-lists, TODO files) ship in the
  release tree; consider a `dev/` subfolder. `wiki/research/slm_cards/*` carry "TBD"/"TODO" scaffolding.

## Checked and clean (no action)

Secrets/credentials clean (1900+ files); restrictive datasets correctly gitignored; core licensing
posture sound (full GPLv3, Kintsugi correctly non-commercial-labelled); build green + battery exact;
resilience + transform migrations complete (173 sync components on FrahanComponentBase, 0 generic
Transform output ports); corrected benchmark framings hold; the static site is self-contained
(data.js matches components.json 1:1, all icon fields resolve).
