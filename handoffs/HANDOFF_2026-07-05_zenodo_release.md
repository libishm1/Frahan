# Handoff — v0.1.0-alpha Zenodo release execution (2026-07-05 evening)

Everything is staged. The ONLY remaining actions are the operator steps in §1.
Written at main = tag `v0.1.0-alpha` = `9c51086`. (Docs commits after this
handoff may move main past the tag; that is fine — the release archives the
TAG, and the tag must NOT move once the DOI exists.)

## 1. Release steps (in this exact order)

1. **Link Zenodo FIRST**: zenodo.org → account → GitHub → flip the toggle for
   `libishm1/Frahan`. The webhook only fires for releases created AFTER the
   integration is enabled. (If a release is accidentally created first:
   delete the GitHub release and recreate it to re-fire the webhook.)
2. **Create the release** (this triggers the archive + DOI):
   ```
   gh release create v0.1.0-alpha --repo libishm1/Frahan \
     --title "v0.1.0-alpha" --notes-file RELEASE_NOTES_v0.1.0-alpha.md
   ```
3. **Verify on Zenodo** (~minutes): a new deposition appears; metadata comes
   from `.zenodo.json` (version 0.1.0-alpha, GPL-3.0, ORCID
   0009-0004-3238-4202). Publish it in the Zenodo UI if it lands as a draft.
4. **Yak publish** (independent of Zenodo):
   ```
   yak login
   yak push install/yak_build/frahanstonepack-0.1.0-rh8_30-win.yak
   ```
5. **Post-DOI polish** (a normal follow-up commit; do NOT retag):
   add the DOI badge to README.md, and the concept-DOI to CITATION.cff.

## 2. What the snapshot (tag 9c51086) contains

- All engines working: CGAL native fixed (mpfr-6.dll bundled — it was
  silently dead before 2026-07-05), workers at 0.1.0, native lanes verified
  (osqp, geogram, CGAL, quadwild, discontinuity, coacd).
- 2D-nest consolidation: `Sheet Nest (Hole-Aware)` + `Sheet Nest (Live)`
  (truly async, progressive live preview, boundary mode) on the single
  `ContactNfpHoleNester` core; FreeNestX + Sheet Pack (Unified) hidden.
  Boundary mode = measured rim-contact at verified NFP poses + arc-interval
  occupancy (rotation-invariant), rim-full early-out, >120-part multi-start
  clamp. Field-validated: 240 parts, irregular sheet, 7.3 s, live preview.
- OpenNest 2.89 head-to-head recorded in docs/results/RESULTS.md (valid vs
  invalid layouts, +8 pp utilization, 6-5000x faster).
- Pack Surfaces: boundary inputs + progressive live preview.
- Edge matching: discrete Frechet primitive + `Edge Gap (Fréchet)` component;
  theory-vs-implementation study in wiki/research/.
- Fabricate: 16 components, 0 stubs; robot-handoff optional-input bug fixed;
  G-code -> Planes -> Robots chain verified INCLUDING real visose/Robots
  `Create Target` ingestion (KUKA|prc contract-correct, plugin untested —
  30-second check when it is installed).
- Examples: 8/8 families load 0-unresolved on the shipped .gha; 49/50
  promoted; dup 51 removed; index covers 01-50 + vaults.
- Docs: GitHub Pages LIVE at <https://libishm1.github.io/Frahan/> (MkDocs
  Material via Actions, auto-deploys on main); 274-component reference
  regenerated; wiki cleaned of internal migration docs; security scan of the
  full git history clean (no keys/creds anywhere).
- Contributor model (Tao blueprint): CONTRIBUTING.md (automated judge /
  blueprint nodes / invariants / CRediT), CONTRIBUTORS.md, and blueprint
  issues #5-11 seeded (CI-unlock #5+#6 is the keystone pair).
- Repo hygiene: only `main` branch remains; PR #3 closed as superseded;
  battery 1056 PASS / 0 FAIL / 154 SKIP (`FRAHAN_SKIP_NATIVE=1`).

## 3. Artifacts on disk (match the tag)

- Yak package: `install/yak_build/frahanstonepack-0.1.0-rh8_30-win.yak`
  (12.2 MB) — verified: gha v0.1.0 incl. all consolidation fixes, workers
  0.1.0, mpfr-6.dll present, no stray files.
- Deployed .gha in `%APPDATA%\Grasshopper\Libraries` = same build.

## 4. Post-release backlog (priority order)

1. DOI badge -> README (§1.5).
2. Blueprint #5 + #6 (PackageReference -> CI): makes the battery an automatic
   PR judge; everything else scales from there.
3. UX audit P1 remainder (issues #9, #10) + 17 dangling [RelatedComponent]
   links + icon pass for shared-icon clusters.
4. Frechet gate integration into the block matchers (#7, re-bake examples
   42/48 with it), MILP solver (#8).
5. Wiki 2D-packing figure refresh (defer: default path is byte-identical and
   test-pinned, so the June figures remain accurate; new results are recorded
   as text rows in RESULTS.md; optional add = the boundary-mode A/B capture).
6. GitHub Pages polish (nav depth, examples gallery), KUKA|prc live check.
7. Trencadís family: audited 2026-07-05 — all five are genuinely distinct
   engines/problems (fill / Hungarian catalog / Kangaroo settle / facade /
   AssemblySolver edge-match), already exposure-tiered. NO consolidation.

## 5. Gotchas (hard-won, do not relearn)

- Zenodo webhook ordering (§1.1). Tag is frozen post-DOI.
- Deploy needs ALL Rhino closed (the .gha file-locks); kill stray
  `frahan_*worker` first; MCP slots count as Rhino.
- `GH_TaskCapableComponent` is NOT async (branch-parallel only) — the real
  pattern is `AsyncScanComponent` (Run gate + background Task +
  ScheduleSolution).
- The rect fast-path bypasses `PackGeneral`: any new placement feature living
  in PackGeneral must disable the fast path when active (as boundaryMode does).
- GH inputs named "(optional)" must ALSO set `.Optional = true` — otherwise
  the component silently never solves (no error, empty outputs).
- Local `main` can go stale while working on task branches pushed directly to
  origin/main: `git branch -f main origin/main` before switching.
