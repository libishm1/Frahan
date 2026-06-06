# RESTART HANDOFF (system froze 2026-06-06)

The machine display/mouse/keyboard froze; work continued via remote control, then this handoff was written
to survive a restart. Everything below is committed + pushed to GitHub (github.com/libishm1/Frahan, main).
On restart, `git pull` and resume from the OPEN ITEMS. Style: short sentences, no em dashes.

## State at freeze
- Branch main, HEAD pushed to origin. Last commits: galleries (3e63b75), self-scaling defaults (c0eb30c),
  scale/position/tolerance fixes (a1b8fb3), .gha refresh (028fe2c), examples+install (dd0f6c2).
- The fresh self-scaling `.gha` is deployed to `%APPDATA%/Grasshopper/Libraries/` AND bundled in
  `install/plugin/` (LFS). install/weights/kintsugi.bin (255 MB, LFS) is deployed + bundled.
- Live Rhino MCP was BLOCKED at freeze: router `spawn_slot` returns startup_timeout (Rhino launches but no
  main window; likely a Rhino license/update dialog or lost interactive-desktop access). Stuck Rhino.exe
  processes persisted through kills. ON RESTART: launch Rhino manually, dismiss any dialog, run `MCPStart`
  (Enter for default port) so the router adopts the slot; then live work resumes. Recipe:
  `LIVE_EXAMPLE_BUILD_HANDOFF.md`.

## OPEN ITEMS (user-requested, NOT yet done - do these on restart)
1. KINTSUGI: turn OFF auto-scale. The bake currently uniformly scales the assembled result to vessel size;
   the user reports the smaller fragments get scaled and STACK ON TOP of each other instead of matching at
   the crack/fracture interface. FIX: do not rescale per-fragment; keep the Port-mode network output poses
   exactly (the network places fragments relative to fragment 0). If a display size is wanted, apply ONE
   uniform transform to the whole assembled GROUP, never per fragment. Re-verify the two fragments meet at
   the fracture rim (not interpenetrating / not stacked). File: example 14 bake step + 14_kintsugi_result.png.
2. KINTSUGI VALIDATION: open the pushed `examples/14_kintsugi/14_kintsugi_result.png` on GitHub and confirm
   the reassembly is actually correct (fragments joined at the crack interface), not stacked. Port-mode parity
   was 2/2 placed, verifier 0.71 STRONG on bb_sample_00697 - but the DISPLAY may misrepresent it.
3. 2D PACKING OVERLAPS: the user sees overlaps in the PNGs. Determine: are the result IMAGES old, or is the
   ALGORITHM overlapping? FreeNestX was verified 0.0 overlap live (14/14, 16/16) in the rebuilt .gha; the
   10_pack2d_result.png is the FreeNestX live render (should be 0-overlap). Re-check the actual on-disk PNG +
   re-measure overlap on the example .gh output. If the PNG is stale, regenerate; if the algorithm overlaps,
   debug FreeNestX. Note: V506 overlap is BY DESIGN (Trim Tolerance default was 0.1, now 0).
4. 2D BOUNDARY CONTAINMENT: verify packed parts stay INSIDE the sheet boundary (no part crossing the slab
   edge) in the 2D nest. Measure containment on the example output.

## How to re-enable live Rhino (needed for items 1-4 re-render)
1. Open Rhino 8 manually. Dismiss any license/update dialog.
2. Run `MCPStart`, press Enter for default port. This drops the listener the router adopts.
3. `list_slots` should show an adopted slot. Then use g1_* + run_python (see LIVE_EXAMPLE_BUILD_HANDOFF.md).
4. The deployed `.gha` is current (self-scaling defaults); examples open correct.

## Key references
- Tolerance/scale study + applied source fixes: `wiki/research/tolerances_dimensions_slm_roses.md`.
- Known bugs (KB-1..KB-7, KB-7 resolved): `handoffs/KNOWN_BUGS.md`.
- Live-build recipe + MCP quirks: `handoffs/LIVE_EXAMPLE_BUILD_HANDOFF.md`.
- Checkpoint log: `handoffs/CHECKPOINT_LOG.jsonl`.
