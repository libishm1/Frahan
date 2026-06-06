# Example 15 - Statue/monument -> brick blocks (factory workflow) - CONNECTION PLAN

Decompose a sculpture (Stanford bunny scaled to 3 m) into ~0.5 m brick-like blocks where boundary blocks
carry the REAL statue surface (not bounding boxes), producing clean closed block meshes for 3D packing
(gangsaw / quarry block) and matching to a rubble lot. Plan grounded in a 4-agent component research pass
(read source). Doc units METERS. Style: short sentences, no em dashes. STATUS: plan for review BEFORE build.

## The real-face guarantee (the core idea)
A 0.5 m brick GRID of cells covers the statue bbox. Each cell mesh is INTERSECTED (CGAL boolean) with the
closed statue solid. Interior cells return a full 0.5 m cube; boundary cells return the cell clipped by the
statue surface, so they keep the REAL mesh faces on the outside. Empty cells (outside the statue) are
dropped. This is exactly what `Quarry Decompose By Mesh (CGAL)` does.

## Pipeline (numbered connection graph)
1. SOURCE: `data/stanford_scans/bunny/.../bun_zipper.ply` (real scan) -> Scan Read (or Mesh Remesh File
   Path mode reads the .ply directly off-thread).
2. SCALE to 3 m + base on z=0: bbox -> uniform scale so Z-extent = 3.0 m -> Move so min Z = 0 (Box/Transform).
3. SANITIZE: `Sanitize Mesh` (Backend 0 = CGAL strict, auto-fallback Geogram) -> repair/orient. Probe with
   `Mesh Diagnostics`.
4. CLOSE HOLES: `Close Holes` (Backend 0 = managed->geogram). REQUIRED - bun_zipper has open base/feet holes;
   CGAL boolean needs a closed solid. Verify `Mesh Diagnostics` -> Is Closed = true, Is Manifold = true.
5. REMESH (uniform faces): `Mesh Remesh (Geogram)`, N (vertex count) high enough to keep detail (~30-60k).
   NOTE remesh can reopen the surface -> re-run `Close Holes` + `Sanitize` and re-verify CGAL-Ready. Confirm
   Average Edge Length ~ block/20 (~0.025 m) via Diagnostics.
6. DECOMPOSE (real faces): `Quarry Decompose By Mesh (CGAL)`. Inputs: the closed statue mesh (Q), Grid Box
   (its world AABB), nX/nY/nZ = ceil(extent / 0.5) (so ~6 x 6 x 6 for a 3 m cube envelope), Hybrid Kernel
   (EPECK) = true, Run gate. Output: one closed mesh per non-empty cell (interior cubes + boundary real-face
   blocks). ~216 cells, boundary shell ~120-150 -> ~150 CGAL booleans.
   - OPTIMIZATION (interior-skip): tag cells with `Clip Boxes By Mesh` / `BenchBoundary.ContainsBox`
     (threshold 1.0 = fully inside) -> fully-interior cells emit a cube via `Box To Mesh` (NO boolean);
     run the CGAL boolean ONLY on boundary-shell cells. Cuts boolean count to the shell.
7. TAG interior vs boundary + per-block volume: interior iff block true-volume >= (1 - eps) * cell-box
   volume, else boundary. Carry {interior|boundary, true volume, AABB WxDxH} per block (QuarryBlock carrier).
8. METRICS: block-size distribution (vol + edge min/max), recovered-volume ratio = sum(block true vol) /
   statue true vol, boundary-block ratio, sawability phi (guillotine-separable fraction).
9. BRANCH A - PACK into gangsaw / quarry block:
   - BOX route (axis-aligned saw plan): block AABBs -> `Block Pack (Tree)` (guillotine, Kerf 0.005-0.010 m,
     forbidden = fracture boxes) into stone-block containers -> cut plan + yield. OR `Frahan Mixed-Size
     Block Pack 3D` (catalogue of sizes, meters, Floor-Only) for revenue-max extraction.
   - MESH route (keep real surface): closed block meshes -> `Pack3D Irregular Container` (mesh-heightmap into
     the quarry-block mesh) -> `Settle 3D (Physics)` (Bullet, real contact) -> `Frahan Packing Report`
     (Fill Ratio) + `Per-Stone Overlap` (needs closed meshes).
10. BRANCH B - MATCH to a rubble lot: per block, `Block Pair Match 3D` vs each rubble scan mesh -> top-N
    poses + Hausdorff residual; refine with `Soft ICP 3D`; if a rubble stone is oversized for the slot,
    `Adaptive Block Match 3D` trims minimally (boolean via the CGAL shim). Global one-to-one assignment via
    Hungarian (Template Block Match 3D - currently a skeleton, needs build).

## Tolerances (METERS doc; from wiki/research/tolerances_dimensions_slm_roses.md + tolerance dossier)
- Geometric eps: scale-relative, recenter before CGAL; CGAL split-eps k ~ 1e-7 * bbox diagonal. Doc abs tol
  ~0.001 m (1 mm) for a 3 m / 0.5 m model.
- Remesh target edge length ~ block / 20 ~ 0.025 m (25 mm).
- Saw kerf 0.005-0.010 m (5-10 mm diamond wire); set Block Pack (Tree) Kerf accordingly (default 0).
- Joint Hausdorff (rubble match) 0.1-2 mm; min face-patch area 15000 mm^2 (Devadass 2025); carve budget
  <= 30 %. BEWARE: EdgeMatch components default in mm; this model is meters - convert at the boundary.

## Metrics to capture
Block count, block-size distribution (vol + min/max edge), recovered-volume ratio (true signed-tetra
volume), boundary-block ratio, sawability phi (set grid ALIGNED for guaranteed guillotine-separable), pack
fill ratio + 0-overlap (Per-Stone Overlap), rubble-match total cost + mean yield + #unassigned.

## Key decisions BEFORE building (need your call)
1. GRID STYLE: ALIGNED grid (guaranteed gangsaw guillotine-separable, clean cubes) vs STAGGERED running-bond
   (brick-like masonry look, but breaks pure axis-aligned sawability). Recommend ALIGNED for the gangsaw
   primary; staggered as a variant via Staggered Block Decompose -> Slab Cut By Tool Mesh.
2. CGAL BOOLEAN CRASH RISK: ~150 in-process CGAL booleans is the exact crash-class that contributed to the
   freeze. Options: (a) run in-process with Hybrid EPECK + recenter + Run gate (fast, some risk); (b) build
   an out-of-process CGAL boolean worker first (safe, matches the recon-worker pattern, more work). Recommend
   (a) for a first pass with the interior-skip optimization (fewer booleans), reboot-safe in small batches.
3. SCOPE now: build BRANCH A (decompose -> pack into gangsaw) first; BRANCH B (rubble match) needs a rubble
   catalog fixture + the Hungarian matcher built.

## Gaps surfaced by research (to implement or work around)
- Quarry Decompose By Mesh does not TAG interior/boundary or skip interior booleans -> add via Clip Boxes By
  Mesh + volume compare (workaround) or a small component addition.
- Core true signed-tetra mesh volume is missing (VolumeEstimate is bbox-only) -> needed for honest
  recovered-volume + boundary-ratio.
- No out-of-process CGAL boolean worker yet (in-process crash risk).
- Staggered layout breaks guillotine sawability phi.
- mm-vs-m default mismatch on EdgeMatch matchers vs the meters model.
- No rubble-stone catalog fixture for Branch B yet.
