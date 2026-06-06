# Known bugs / traps (Frahan StonePack) — canonical registry

Concrete, reproducible issues with their root cause, fix, and status. Style: short sentences, no em dashes.
Each entry: Symptom / Trigger / Root cause / Fix / Status.

---

## KB-1 — Saving a .gh that internalises a large mesh stalls / crashes the canvas (CONFIRMED, fixed by data)

- **Symptom:** the canvas freezes or Rhino crashes on a small edit (e.g. inserting a List Item on a
  component output), and it feels like "two processes running in parallel."
- **Trigger:** the bug appears ONLY once the .gh is SAVED (named). The exact same graph in an UNNAMED /
  unsaved file works smoothly. The save LOCATION/name does not matter.
- **Root cause (confirmed):** the file internalises a multi-million-vertex mesh (e.g. the 2.2M-vertex
  temple scan = 52 MB .gh). Grasshopper autosave writes a backup on every wire/value change, but only
  once the document has a filename. So every edit re-serialises the whole 52 MB file, and that collides
  with any async component (here Mesh Remesh (Geogram) + Close Holes both spawn background tasks) and the
  display = the "two parallel processes." Confirmed by GH's author David Rutten on the McNeel forum
  ("every new wire will cause an autosave update ... you have a lot of internalized data, this has to be
  re-written many times over"). This is NOT a component-code bug. The Carving Stages component itself is
  synchronous + cached and runs fine standalone on a fresh canvas.
- **Fix (RULE):** NEVER internalise a multi-million-vertex scan in a .gh. Either
  (a) DECIMATE it first (voxel-cluster down to ~100-200k verts: 2.2M -> 146k drops the file 52 MB ->
  4 MB and autosave becomes instant), or
  (b) REFERENCE it externally (bake to a .3dm and reference, or use Data Output -> Data Input, Params >
  Util) so the mesh is not in the saved file at all.
  ALSO turn off Grasshopper "Create backup file when a new connection is made" (File > Preferences >
  Grasshopper > Solver/Autosave) as belt-and-braces.
- **Status:** RESOLVED for the carving file. `stone_carving_simulation_LIGHT.gh` (4.06 MB, scan
  decimated 2.2M -> 146,437 verts) opens in 0.2 s vs 12 s and does not stall. The rule above is the
  general fix. See [[feedback_gh_carving_stages_reorder_bug]].

## KB-2 — Mesh.Reduce on a multi-million-vertex mesh blows the MCP 300 s cap and blocks the slot (TRAP)

- **Symptom:** a headless `run_python` / `run_csharp` that calls `Mesh.Reduce` on a >1M-vertex mesh
  never returns; the MCP HttpClient times out at 300 s and the canceled work keeps running server-side
  (14-core, several GB), blocking the Rhino slot for many minutes. Forced two Rhino kills/restarts.
- **Trigger:** `Mesh.Reduce(targetFaces, ...)` on a 2.2M-vertex mesh (even once; ~4x in a loop is far
  worse).
- **Root cause:** quadric edge-collapse is pathologically slow at multi-million scale; the MCP call cap
  is 300 s.
- **Fix:** do NOT use `Mesh.Reduce` on multi-million meshes inline. Use O(n) voxel-cluster decimation
  (hash verts to a grid, keep one per cell, remap faces) via bulk arrays (`Vertices.ToPoint3fArray()`,
  `Faces.ToIntArray(true)`); 2.2M decimates in ~15-20 s. Or decimate ONCE upstream via the geogram
  vertex-clustering node. Transforms and bakes on multi-million meshes are fine; only Reduce is the trap.
- **Status:** documented; voxel-cluster path used for the LIGHT file. See [[feedback_gh_carving_stages_reorder_bug]].

## KB-3 — Masonry RBE stability verdict is wired to the sign-buggy formulation (FLAGGED, not yet fixed)

- **Symptom:** the shipped masonry stability verdict can be wrong; it verifies pure equilibrium, not
  frictional stability.
- **Root cause:** `MasonryStabilityRbeComponent` (and `BuildOrderStabilityStream`) call
  `RbeQpFormulation.Build`, which the code's own XML doc says makes f_n >= 0 infeasible for any real
  assembly; `BuildPhysicsCorrected` is the correct overload but is unwired. The only friction-capable
  solver path (`ManagedQpSolver` Dykstra) returns NotImplemented for the non-uniform H that RBE emits.
- **Fix (planned, W4):** test whether the sign flop was deliberate (stable + unstable wall, Build vs
  BuildPhysicsCorrected), then make BuildPhysicsCorrected the single Build, migrate the two GH callers,
  label the verdict equilibrium-only until the friction solver lands, add an end-to-end test.
- **Status:** OPEN (task #29 / W4). User APPROVED the sign fix pending the intentionality test. See the
  master-spine audit AUDIT_00/AUDIT_04.

## KB-4 — Exact NFP-BLF admits a small overlap on CONCAVE parts (FIXED in the evolved path)

- **Symptom:** `IrregularSheetFillNfpBlf` (the exact Clipper2 NFP-BLF engine) produces a small part-to-part
  overlap when the parts are concave (e.g. L-shapes). Measured via `--pack2dstudy`: greedy path 3 overlap
  pairs / max 0.49 area on the 24-part saturated fixture, 4 on the oversub fixture.
- **Trigger:** concave input parts at 0 spacing.
- **Root cause:** the no-fit polygon of a concave part is NOT captured by a single Minkowski sum; the
  feasible-region difference then admits a position where one part pokes into the other's concave pocket.
- **Fix:** the 2026-06-06 evolution adds a real polygon-intersection VERIFY (`OverlapsPlaced`, Clipper
  intersect-area) on the evolved path, so a candidate that actually overlaps a placed part is rejected ->
  0-overlap by construction even on concave parts (measured: evolved = 0 overlap on both fixtures). The
  legacy greedy path (all evolution flags off) STILL has the overlap; use the evolved flags or convex parts.
- **Status:** RESOLVED for the evolved path (multi-start/compaction/reinsertion enable the verify). See
  outputs/2026-06-06/packing_slm_evolution/PACK2D_STUDY_REPORT.md.

## KB-5 — `cov = union/full-sheet` cannot rank 2D packers; exact engines inflate emitted geometry

- **Symptom:** every full 0-overlap pack of the same parts reads the same `cov` (~60%) regardless of layout;
  and the exact NFP engines read `cov`/`covUsed` slightly above 100% on the oversub fixture.
- **Root cause:** on a saturated fixed-area sheet union = total part area, so cov is invariant. The exact
  engines emit geometry inflated ~3-4% by the x1000 integer-scale Clipper round-trip (covUsed > 100% is
  physically impossible, so it is the inflation). The W2 "NFP-BLF 65.2%" was a different engine
  (rectangle-strip `NfpBottomLeftFillRhino`) and was partly this artifact.
- **Fix (RULE):** rank 2D packers by **covUsed = union/used-bbox** and by **placedCount on an
  oversubscribed fixture**, and ALWAYS gate a result on **overlap == 0**. Never compare packers on `cov`
  on a saturated fixture, and never trust a higher placedCount that comes with overlaps.
- **Status:** documented; the `--pack2dstudy` harness reports covUsed + placed + overlap for exactly this.

## KB-6 — V506 quality mode is capped by V506's 0.1 spacing floor

- **Symptom:** V506-quality (the evolved engine routed through V506) reads covUsed 68.0% on a tight
  saturated pack, below the raw evolved engine's 87.7% at spacing 0.
- **Root cause:** V506 clamps `_spacing = max(0.1, spacing)`, so the quality route always packs with >= 0.1
  clearance, which spreads parts on a tight pack.
- **Fix:** for maximum density use the standalone `IrregularSheetFillNfpBlf` (FreeNestX) with spacing 0 and
  the evolution flags. Use V506-quality when you need V506 holes/boundary WITH the spacing floor. Changing
  the floor is a separate behaviour change requiring its own test + HITL (do not change it inside the
  density evolution).
- **Status:** documented; behaviour is intended (the floor is a V506 contract).

---

## KB-7 — Loaded .gha can be STALE; 2D NFP packers overlap live until redeployed

- **Symptom:** Building the 2D nest live on the MCP slot (2026-06-06), both `Freeform Sheet Nest (Exact NFP)`
  (FreeNestX) and `Frahan Sheet Pack (Unified)` V506 placed parts that visually + measurably OVERLAP (e.g.
  bottom-left pentagon/hexagon/heptagon overlapping by 200-540 sq units) while the component Report said
  `Invalid: 0`. Overlap persisted even at 42% utilisation, with convex AND concave parts.
- **Root cause:** the `Frahan.StonePack.gha` loaded in the running Rhino is an OLDER build that predates the
  0-overlap evolution in source. The current SOURCE (`IrregularSheetFillNfpBlf.cs`, `IrregularSheetFillV506.cs`)
  is validated 0-overlap by the headless harness (`--pack2dstudy`: 82-89% util_stock, Invalid 0). The
  discrepancy is the deployed binary, not the algorithm.
- **Fix:** rebuild + redeploy the `.gha` from current source (Rhino CLOSED, file-copy per `docs/INSTALL.md`)
  before trusting live 2D packs. The harness output and the bundled `examples/10_pack2d/` result artifacts
  are from current source and ARE 0-overlap.
- **Implication for examples:** `examples/10_pack2d/` ships the correct `.gh` wiring plus the
  headless-validated result `.png`/`.3dm` (from `wiki/research/packing/figures/`), NOT a live solve. Re-render
  the live capture only after redeploying the fixed `.gha`.
- **Status:** SOURCE FIX BUILT + BUNDLED 2026-06-06. Rebuilt `Frahan.StonePack.gha` from current source
  (`dotnet build ... -c Release`, 0 errors) and refreshed `install/plugin/` with it (Jun 6 15:52 build).
  The bundled `.gha` now contains the 0-overlap 2D nesters. LIVE REDEPLOY still pending: the running Rhino
  locks the old `.gha`, so loading the fresh one needs Rhino CLOSED -> `install/deploy.ps1` -> reopen ->
  `MCPStart`. After that, re-capture the live 2D example. Live-build handoff: `LIVE_EXAMPLE_BUILD_HANDOFF.md`.

---

## Standing rules distilled
- No multi-million-vertex data internalised in a .gh (KB-1). Decimate or reference externally.
- No `Mesh.Reduce` on multi-million meshes inline; voxel-cluster instead (KB-2).
- Carving Stages must stay synchronous + cached; do not reorder its inputs (breaks saved files).
- In-process CGAL/geogram BOOLEAN can crash Rhino; route heavy boolean/recon through the out-of-process worker.
