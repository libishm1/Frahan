# Example 05 — Artist carving / pointing machine

The artist master-spine: a scanned sculpture mesh -> carving stages -> pointing-machine guide for hand or
robotic carving. Style: short sentences, no em dashes.

## Files
- `stone_carving_simulation_LIGHT.gh` — the carving-stages simulation (KB-1-safe: the scan is DECIMATED /
  referenced, not internalized).
- `06_carving_stages.gh` — the standalone Carving Stages component card (synchronous + cached).

## KB-1 (read this)
NEVER use the 51 MB `..._FIXED.gh` that internalized the full 2.2M-vertex temple scan: Grasshopper autosave
rewrites the whole file on every edit and crashes the canvas (confirmed by GH's author). This folder ships
the LIGHT version instead. For your own scan: decimate it to ~150k verts (voxel-cluster) or reference the
mesh externally (bake to `.3dm` + reference). See `../../handoffs/KNOWN_BUGS.md` KB-1 / KB-2.

## Data
- A scanned sculpture mesh. Use one of `../../data/stanford_scans/` (e.g. armadillo, dragon) as a stand-in,
  or your own temple scan DECIMATED first. Reference it externally; do not internalize a multi-million-vertex
  mesh in the saved `.gh`.

## Run
1. Deploy the `.gha` (Rhino closed). Open the `.gh`.
2. Reference the (decimated) scan mesh. Set the Carving Stages inputs (Target, Stages, MaxOffset,
   FinishAllowance, FeatureBoost, Mode, FrontDirection, Block) and toggle `Run` true for a single solve.
3. The staged carving meshes + pointing-machine coordinates appear. Carving Stages is synchronous + cached;
   do not reorder its inputs (breaks saved files).

## Best practices
Per `../GRASSHOPPER_BEST_PRACTICES.md`: COVER panel, default-false Run gate, externally-referenced scan,
coloured stage groups + scribbles.
