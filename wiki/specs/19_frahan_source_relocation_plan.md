# 19 — Frahan source relocation plan

## Purpose

Active StonePack source code currently lives at
`Template-General/outputs/2026-05-01/frahan_stonepack/{src,tests}/`,
inside a date-dated `outputs/` folder. The `2026-05-01` prefix is the
project START date, not last-modified, which creates two cognitive
problems:

1. A session on a later date has artifacts split across two date
   dirs: `outputs/<session-date>/` for the report, `outputs/2026-05-01/`
   for the actual code change.
2. Active code is buried in a "dated artifact" folder, mentally
   conflicting with `outputs/<date>/<task>/` = session-dated
   artifacts elsewhere in the same tree.

This spec captures the planned move and the trigger for executing it.

## Confirmed facts

- Current active source path:
  `Template-General/outputs/2026-05-01/frahan_stonepack/{src,tests}/`
- Current `.csproj` Version: `0.7.0` (per `Frahan.StonePack.GH.csproj`).
- AGENTS.md §3 build/test/deploy commands embed the long path
  literally; they will need updating.
- AGENTS.md §7 file routing references include the long path; same.
- Wiki cross-references using the path live in
  `wiki/algorithms/packing_2D/trencadis_pipeline.md`,
  `wiki/algorithms/packing_2D/known_failures.md`, and a few others
  identifiable by `grep "outputs/2026-05-01/frahan_stonepack" wiki/`.
- Solution file `code_ws.sln` references `RhinoCommonDualPlugin`
  (an unrelated demo .csproj) but does NOT reference the active
  StonePack .csproj files. Move does not affect solution loading.

## Current working assumptions

- The move is invasive but well-bounded: ~10 files need path edits
  outside the source tree itself, plus the bulk `git mv` of the
  source tree (~600 file renames, one git operation, history
  preserved).
- The `archive/Frahan.StonePack_0.5.{2,3,4}_before_*/` historical
  snapshots inside the current tree should move with the active
  source as one unit (they are conceptually part of the same
  project's history).
- HITL gate per AGENTS.md §6 ("before introducing a new module or
  reorganising the module map") fires; user has pre-approved the
  move at the v0.7.x → v0.8.0 boundary.

## Implementation notes

### Target layout

```
Template-General/
├── projects/                         ← NEW. Active development.
│   └── frahan_stonepack/             ← was outputs/2026-05-01/frahan_stonepack/
│       ├── src/{Frahan.StonePack.Core, .GH, .Rhino}/
│       ├── tests/Frahan.StonePack.Tests/
│       ├── archive/                  (historical snapshots stay here)
│       └── references/               (vendored prior art stays here)
├── outputs/                          ← session artifacts ONLY going forward
│   └── <date>/<task>/
├── samples/                          ← user fixtures (.gh, .3dm)
└── raw/                              ← debug captures
```

### Trigger condition

Execute at the next `<Version>` bump in
`src/Frahan.StonePack.GH/Frahan.StonePack.GH.csproj`.

Current: `0.7.0`. Trigger: change to `0.8.0` (or any other
semantic bump). Patch bumps within `0.7.x` do NOT trigger the move
on their own; the move sits with the next minor or major version
boundary.

### Pre-move checklist

1. Tag `v0.7.x` (whatever the last patch in 0.7 line is) on the
   commit immediately before the move.
2. Snapshot the active source as `checkpoints/v0.7.x/frahan_stonepack.zip`.
3. Confirm test gate green: `425 PASS / 0 FAIL / 39 SKIP` (or the
   then-current count). Move halts if tests fail.
4. Bump `<Version>` in all 4 active csproj files to `0.8.0`.
5. Commit version bump as a separate commit before the move.

### Move procedure (single focused commit)

```bash
cd /d/code_ws

# 1. Create projects/ dir.
mkdir -p Template-General/projects

# 2. Bulk rename. One git operation.
git mv Template-General/outputs/2026-05-01/frahan_stonepack \
       Template-General/projects/frahan_stonepack

# 3. Update path references in 7 files (search-and-replace):
#    - AGENTS.md (§3 build commands, §7 file routing rows that use
#      the long path)
#    - CLAUDE.md (none expected, but grep to confirm)
#    - wiki/algorithms/packing_2D/trencadis_pipeline.md
#    - wiki/algorithms/packing_2D/known_failures.md
#    - wiki/algorithms/packing_2D/validation_log.md
#    - README.md (canonical layout tree)
#    - any other file containing
#      "outputs/2026-05-01/frahan_stonepack" — grep -rn
#
# Pattern: "Template-General/outputs/2026-05-01/frahan_stonepack/"
#       → "Template-General/projects/frahan_stonepack/"

# 4. Sanity-build.
dotnet build Template-General/projects/frahan_stonepack/src/Frahan.StonePack.GH/Frahan.StonePack.GH.csproj -c Release -nologo

# 5. Run test gate.
cd Template-General/projects/frahan_stonepack/tests/Frahan.StonePack.Tests
RUN_TESTS=1 dotnet run -c Debug --no-build

# 6. File-copy deploy and HitL-validate in Rhino.

# 7. Commit (single focused commit).
git commit -m "chore(repo): move active StonePack source out of date-dated outputs/

Source tree relocated from Template-General/outputs/2026-05-01/frahan_stonepack/
to Template-General/projects/frahan_stonepack/ at the v0.7.x → v0.8.0
semantic boundary, per spec wiki/specs/19_frahan_source_relocation_plan.md.

Path references updated in AGENTS.md §3 + §7, README.md, and three
wiki pages. Tests pass: 425 PASS / 0 FAIL / 39 SKIP. Snapshot of
v0.7.x at checkpoints/v0.7.x/frahan_stonepack.zip.

Rollback: git revert <this-hash>; or git checkout v0.7.x; or
extract the checkpoints zip."
```

### Post-move

- AGENTS.md §2 module map paths updated.
- AGENTS.md §3 build commands updated.
- AGENTS.md §7 routing table row "Demo Grasshopper / Rhino file"
  destination unchanged (`Template-General/outputs/samples/`).
- Wiki cross-references: re-grep to confirm zero stale paths.
- Append entry to `wiki/algorithms/packing_2D/validation_log.md`
  marking the structural reorg.

## Code or command patterns

Identify all stale path references before the move:

```bash
grep -rn "outputs/2026-05-01/frahan_stonepack" \
  --include="*.md" --include="*.cs" --include="*.csproj" \
  /d/code_ws | grep -v "/raw/" | grep -v "/archive/"
```

The grep should hit AGENTS.md, CLAUDE.md (zero hits expected),
README.md, the three wiki pages above, and possibly any
script/config under .claude/.

## Risks

- **Path-reference miss.** A file containing the long path that I
  miss in step 3 will silently break (build error, deploy script
  failing, wiki link rotting). Mitigation: grep before commit, run
  full build + test gate before commit, commit then run the gate
  again to confirm post-commit reality matches.
- **Wired Grasshopper canvases.** If any `.gh` document has a
  baked-in path reference (rare but possible for file-link
  components), it will need re-wiring in Rhino. Mitigation: list
  user-known canvases and check before move.
- **Concurrent agent.** Another agent working in the source tree
  during the move would conflict. Mitigation: announce the move
  before executing; complete in one focused commit.
- **`code_ws.sln` references.** Currently the .sln references only
  `RhinoCommonDualPlugin` and the immutable `Agent-orchestration-main`
  tree, not the active StonePack csproj files. Move does not break
  solution loading. Mitigation: re-verify just before move.

## Open questions

- Should `archive/` and `references/` move with the active source
  (cleanest), stay at the old `outputs/2026-05-01/...` path
  (preserves historical context), or split — `archive/` moves with
  active source, `references/` stays as immutable historical
  artifact? Default plan: move both with active source.
- Should the legacy `Template-General/outputs/2026-05-01/`
  directory be left as an empty placeholder (with a README pointing
  to the new location) or deleted? Default: delete (post-move). The
  empty dir adds nothing once paths are updated.
- Does the move date warrant promotion to a checkpoint event in
  `wiki/audit_trail.jsonl` (when present)? Yes — append a
  `checkpoint` event with the new tag.

## Related raw files

None.

## Related wiki pages

- ``trencadis_pipeline.md`` (internal log, not published)
- ``known_failures.md`` (internal log, not published)
- ``validation_log.md`` (internal log, not published)

## Last updated

2026-05-08
