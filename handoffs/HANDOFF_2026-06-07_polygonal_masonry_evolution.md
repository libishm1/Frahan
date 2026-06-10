# Handoff 2026-06-07 - Polygonal masonry shipped + evolution directive

Read AGENTS.md first (every section applies). Truth criterion (c) visual validation in live Rhino, HITL
gates in section 6 are mandatory stops. This handoff covers the voussoir + polygonal-masonry session and
the open work. Style: short sentences, no em dashes.

## Current state (one line)
Frahan `main` is at `5d87c4b`, fully pushed. All voussoir + migration-27 + clean-mesh work is in. The next
big task is the polygonal-masonry EVOLUTION, but it is NOT started. Migrations 28/29/30 and the heavy
example regens are paused under an explicit user "stop here".

## What shipped this session (pushed to Frahan main 02706ee..5d87c4b)

### Voussoir geometry components (commit 02706ee)
The missing voussoir front end. Two new GH components over `Core/Voussoir/VoussoirCellFactory.cs`:
- **Arch Voussoirs** (GUID D5F10012): semicircular / segmental / pointed / catenary arch profiles ->
  wedge voussoir cells with the radial bed-joint rule (Frezier 1737, Monge 1798).
- **Pendentive Vault Voussoirs** (GUID D5F10013): sail dome (sphere over a square) -> voussoir cells.
- Critical fix in `MakeHexahedron`: `if (m.Volume() < 0) m.Flip(true, true, true);`. Inward-oriented
  cells silently broke CGAL booleans (coverage read > 1.0). 10 tests in `VoussoirCellFactoryTests.cs`.
- Examples 21 (rubble arch) and 22 (pendentive vault) regenerated from the components + ETH1100 rubble +
  CGAL trim. No more raw oversized boulders. 11/11 at 94.9% and 36/36 at 98.3%.

### Migration 27 polygonal masonry (commit f407127)
`examples/27_polygonal_masonry/`. 7 of 8 cards live-validated in Rhino 8:
- 27_01..05 spanning-chain walls (3 / 7 / 10 / 8 / 20 stones), 27_07 3D Voronoi (50 cells), 27_08 negative
  cases. Each card: `.gh` + `.3dm` + `cards/*.md` + a live result PNG (colored by install order) + README.
- Card 06 (2D Voronoi) is HELD BACK. See KB-8.
- **Rule-8 robustness fix** (`Core/Masonry/Sequencing/Wall.cs`): mutual above/below ambiguity in
  `DirectEdge` no longer throws. It resolves by length-weighted votes, then by centroid height (a global
  potential, so acyclic). `ReversedKahnDepths` now breaks gracefully on a cycle instead of throwing. This
  was the bug that aborted card 06 with 0 stones. Cards 01-05/07/08 verified unchanged.

### Clean-mesh output (commit 18898f7) - user-flagged, important
`Polygonal Masonry Sequence 3D` now emits clean closed cells NATIVELY. The user requirement was explicit:
components must not hand the user degenerate meshes to clean up themselves. New `CleanCell` runs
CombineIdentical -> CullDegenerateFaces -> FillHoles (if open) -> RebuildNormals -> UnifyNormals -> flip
if signed volume < 0 -> Compact, before output. Verified live: trimmed 3D-Voronoi areas now close, and the
user confirmed "now it looks great actually after you unified normals". Example 07 re-rendered.

### Evolution TODO (commit 5d87c4b)
`handoffs/POLYGONAL_MASONRY_EVOLUTION_TODO.md`. The phased roadmap (below).

## The directive that frames the next task
Libish: the shipped polygonal masonry is "very basic". It SEQUENCES a given joint pattern (Kim 2024); it
does not DESIGN one. It must evolve to:
- architectural scale (meters; walls, facades, openings, corners),
- aesthetically convincing real-masonry patterns (named precedent),
- a controllable block count (~10 for a panel, ~30+ for a wall/facade),
- design-not-just-sequence, end-to-end quarry-to-wall workflows.

Roadmap phases (full detail + acceptance criteria in `POLYGONAL_MASONRY_EVOLUTION_TODO.md`):
- **A** Architectural pattern GENERATORS: polygonal wall (Inca / opus incertum / Cyclopean), ashlar/coursed
  bonds with grading, size-graded Voronoi + Lloyd, openings + corners + quoins.
- **B** Fix and strengthen the substrate: fix KB-8 2D arrangement (or add a 2D-cells input), stone-shape
  quality controls (no slivers), interlock/bond scoring (no continuous vertical joints), structural-aware
  sequencing tied to RBE stability + voussoirs.
- **C** Architectural scale + block-count targets (panel ~10, wall ~30+, facade ~50+; count is an input).
- **D** End-to-end workflows: pattern -> cut from quarry stock -> install order -> stability -> assembly;
  couple with the voussoir/arch/vault generators; grain/vein alignment.

## Open work (PAUSED under explicit "stop here")
Full list: `handoffs/EXAMPLES_PUNCHLIST_2026-06-07.md`. Do not resume without user go-ahead.
- **Migration 28** monument packing -> `examples/28_monument_packing`. Comps live (Frahan Monument
  Inventory f7a16001 / Bench Monument Pack f7a16002 / Pack Monuments In Cell f7a16003). 3 cards, synthetic
  boxes internalised. Solve 3 canvases, hand-wire inventory -> pack for 28b/28c, capture 3 PNGs.
- **Migration 29** edge matching v2 -> `examples/29_edge_matching`. EdgeMatch Solve d5f10001 live. 3 cards.
  CAVEAT: set MinSegmentLength=0.3 on the no-match card (default 8.0 mm filters all mm-scale segments).
- **Migration 30** blockcutopt A4 -> `examples/30_blockcutopt_validation`. BlockCutOpt Solve f2d0bc02 live,
  A4 PASS (N=163). BLOCKER: live `.gitattributes` does NOT LFS-track `*.3dm`; the 19.4 + 44.7 MB result
  `.3dm` would commit as raw blobs. Decide an LFS rule (HITL) before committing item 30.
- **Heavy regens** (need the live Rhino slot): 01 stub, 03_gpr, 07 (not self-reproducible), 23/24 (no
  `.3dm`/`.gh`), 02 (no README/PNG). Drift: 12 (.3dm=100 shards vs README 28), 18 (height 1.95 vs 3.06 +
  degenerate settle), 21/22 (no `.gh` canvas - feed from the new voussoir comps), 26 (.gh stale abs path),
  06 (KB-8 PSLG fix, then re-enable card 06).

## Known bugs touched
- **KB-8** (`handoffs/KNOWN_BUGS.md`): the 2D Polygonal Masonry component UNDER-EXTRACTS a Voronoi ridge
  NETWORK (~10 of ~26 cells) because `Pslg.FromSegments` is built for SPANNING chains, not a ridge soup.
  This is why card 06 is held back. The fix is Phase B of the evolution TODO.

## Process facts (carry forward)
- Commits to Frahan main need EXPLICIT per-task push authorization. The auto-mode classifier blocks
  otherwise; a "commit + push" auth is scoped to that one batch.
- HITL: the user validates result PNGs before approving any push. Show the local PNG paths first.
- Truth criterion (c): the headless harness cannot init `rhcommon_c` here (HRESULT 0x8007045A). Geometry
  regen and captures need the live Rhino MCP slot. Deploy is file-copy with Rhino CLOSED.
- run_csharp/run_python: param is `script` (not `code`); warm RhinoCode via `_ScriptEditor`; add
  `#r "...Frahan.StonePack.Core.dll"` for Core types; File3dm `AllLayers.Add` returns void.
- code_ws working branch: `docs/frahan-autonomous-nightshift`. Nightshift checkpoint log:
  `outputs/2026-06-04/nightshift/checkpoint_log.jsonl`.

## Suggested next step (await user)
Three open paths, user picks: (a) begin the polygonal-masonry evolution (Phase A pattern generators), (b)
resume migrations 28/29/30, (c) work the heavy-regen punch-list. The evolution is the largest and the most
recently emphasised. Nothing should start without confirmation.

See also: `POLYGONAL_MASONRY_EVOLUTION_TODO.md`, `EXAMPLES_PUNCHLIST_2026-06-07.md`, `KNOWN_BUGS.md`,
and memory `project_frahan_examples_audit_migration` + `project_voussoir_geometry_components`.
