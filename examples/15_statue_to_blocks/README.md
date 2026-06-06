# Example 15 - Statue / monument to brick blocks (factory workflow)

> **Scale, units, position:** METERS. Stanford bunny scaled to a 3.0 m max extent (envelope
> 3.0 x 2.957 x 2.32 m), base on the z=0 bed plane, centred in XY. Brick grid 0.5 m. Geometric
> tolerance 1 mm (scale-relative). Saw kerf 5-10 mm at the cut stage (Branch A). See
> `../../wiki/research/tolerances_dimensions_slm_roses.md`.

Decompose a sculpture into ~0.5 m brick-like blocks where the boundary blocks carry the REAL statue
surface, not bounding boxes. The output is a set of clean closed block meshes ready for 3D packing into
a quarry block / gangsaw and for matching against a rubble lot. This is the factory-level entrypoint:
form-first (top-down) sculpture goes in, fabricable stone blocks come out. Style: short sentences, no
em dashes.

![Exploded block decomposition](15_step2_blocks_exploded.png)

## The real-face guarantee (the core idea)
A 0.5 m brick grid covers the statue bounding box. Each cell box is INTERSECTED (CGAL boolean) with the
closed statue solid. Interior cells return a full 0.5 m cube. Boundary cells return the cell clipped by
the statue surface, so they keep the REAL mesh faces on the outside and clean planar cut faces on the
grid sides. Empty cells (outside the statue) are dropped. The single-block detail below shows it: the
curved densely-triangulated face is the real bunny surface; the flat faces are the grid cuts.

![Single boundary block - real face vs cut face](15_step2_block_detail.png)

## Measured live result (this run, verified)
- Source: `data/stanford_scans/.../bun_zipper_res2.ply` (8171 v / 16301 f, ASCII).
- Sanitize: Geogram `FillHoles` -> `RemeshUniform(5000)` -> `FillHoles`. Result is a clean watertight
  solid: **closed = true, manifold = true, 9996 faces**. The remesh both rebuilds valid topology (so
  `IsPointInside` works for interior-skip) and caps face count for speed.
- Grid 6 x 6 x 5 = 180 cells, block 0.5 m. Classification: **7 interior cubes, 106 real-face boundary
  blocks, 67 empty cells dropped**. 173 CGAL booleans, 0 failures, **3.6 s** in-process.
- **113 blocks total, all closed (113/113), boundary blocks all closed (106/106).**
- **Recovered volume ratio = 1.0000** (5.4009 m3 statue = 5.4009 m3 of blocks; a complete partition,
  measured with RhinoCommon `VolumeMassProperties`, which is orientation-robust). Block volumes range
  0 to 0.125 m3 (a full cube), median 0.036 m3.

## Why the remesh matters (the fix)
The raw scan has open base holes and non-manifold edges. After `FillHoles` + `Weld` alone the mesh was
closed but still non-manifold, and `Mesh.IsPointInside` returned false everywhere, so interior-skip
classification failed and CGAL corefinement rejected the inputs (rc -12, "inputs not closed / manifold
/ consistently oriented"). Geogram `RemeshUniform` rebuilds a fresh 2-manifold surface from a uniform
point sampling. That is what makes both the interior test and the CGAL boolean work. A welded 8-vertex
box (not Rhino's 24-vertex `CreateFromBox`, which is unwelded and non-manifold) is required on the cutter
side too.

## Pipeline
1. SOURCE: real Stanford bunny scan (`.ply`, ASCII), parsed directly (fast, no UI importer hang).
2. SCALE + PLACE: uniform scale so max extent = 3.0 m; base to z=0; centre XY.
3. SANITIZE: Geogram FillHoles -> RemeshUniform -> FillHoles -> closed 2-manifold solid.
4. DECOMPOSE: 0.5 m grid; per cell, interior-skip (cube, no boolean) or CGAL `Intersection` (real-face
   block). Drop empties. Tag interior vs boundary; per-block VolumeMassProperties + AABB.
5. METRICS: block count, interior/boundary split, recovered-volume ratio, block-size distribution.
6. BRANCH A: pack the blocks into a quarry block / gangsaw (saw-cuttable). See below.
7. BRANCH B: match each block to a rubble stone from an ETH1100 lot. See below.

## Files
- `PLAN.md` - the full connection plan and out-of-process spec.
- `15_blocks.3dm` - the result: clean bunny (`15_bunny_remesh`), interior cubes
  (`15_blocks_interior`), real-face boundary blocks (`15_blocks_boundary`).
- `15_blocks_metrics.json` - per-block tag, volume, AABB + the aggregate metrics above.
- `15_step1_clean_bunny_remesh.png` - sanitized 3 m bunny on the bed.
- `15_step2_blocks_exploded.png` - exploded brick decomposition, interior (blue) vs boundary (red).
- `15_step2_block_detail.png` - one boundary block: real face vs flat grid cuts.

## Components / engine
- `Quarry Decompose By Mesh (CGAL)` (Frahan > Quarry) is the canvas component for this; here it was
  driven directly through the Core API (`CgalMeshBoolean.Intersection`, `GeogramMesh.RemeshUniform`,
  `GeogramMesh.FillHoles`) for headless control and per-cell timing.
- Interior-skip optimization: cells with no surface vertex and all 8 corners + centre inside emit a
  cube with no boolean. This cuts the boolean count to the boundary shell.

## Tolerances (METERS doc)
- Geometric eps scale-relative; CGAL recenters internally. Absolute tol ~1 mm for a 3 m / 0.5 m model.
- Remesh target ~ block / 20 (~25 mm edge); 5000 points gives ~25 mm spacing on this envelope.
- Saw kerf 5-10 mm (diamond wire) applied at the pack/cut stage, not the decompose.

## Branches (next)
- BRANCH A (pack into gangsaw / quarry block): block AABBs -> `Block Pack (Tree)` (guillotine, Kim
  2025) into a stone-block container; or closed block meshes -> `Pack3D Irregular Container` ->
  `Settle 3D` -> `Frahan Packing Report`.
- BRANCH B (match to rubble): per block -> `Block Pair Match 3D` vs each ETH1100 rubble mesh -> top-N
  poses + Hausdorff residual; refine with `Soft ICP 3D`; oversize stones trimmed by
  `Adaptive Block Match 3D`. Convert mm-default matchers to meters at the boundary.

## Performance note (in-process vs out-of-process)
In-process (live Rhino via MCP) is fast and stable on the res2 + remesh path: 3.6 s for 173 booleans,
no lag, no crash. The earlier lag was the full 70k-face `bun_zipper.ply` + in-process FillHoles
exceeding the MCP timeout. For very large statues (multi-100k faces) the out-of-process harness mode
(`--statueblocks`, Rhino.Inside) is the production path; the algorithm is identical.
