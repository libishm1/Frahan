# frahan_discontinuity_worker

Out-of-process, clean-room point-cloud **discontinuity extraction** (planar facets + joint
sets + normal spacing) for the `Discontinuity Sets (Async)` Grasshopper component
(`DiscontinuitySetsAsyncComponent`, GUID D5F10048, Frahan > Quarry).

## What it does
PLY cloud → downsample to budget → counting-sort CSR grid → PCA normals + surface variation
→ FACETS planar region-grow → Watson axial mean-shift joint sets → family-constrained normal
spacing → writes `discontinuity.json` (+ `segmented.ply`, RGB by set). Runs in a separate
process so the Grasshopper canvas never blocks; scales to 10M points.

## Clean-room / licence
Implemented from the published math (Pauly 2002 surface variation; Dewez et al. 2016 FACETS;
Riquelme et al. 2014/2015 DSE) and first-principles spatial data structures (counting-sort
CSR uniform grid, Hoetzlein 2014 fixed-radius NN / SPH cell lists). **No CloudCompare /
CCCoreLib / qFACETS (GPL-3.0) source is read or copied.** CloudCompare/Open3D/PDAL are used
only as black-box benchmark baselines. Free of copyleft; usable under Frahan's own licence.

## Performance (full 7,858,334-pt Tongjiang quarry-face cloud, 16 cores)
Apples-to-apples, k=24 PCA normals: **~10.2 s, ~1.4x faster than Open3D's KD-tree (14.8 s)**
at the same neighbourhood. Whole discontinuity pipeline ~14.7 s. (CloudCompare's octree
at radius 0.5 m took 2127 s, but radius 0.5 on this 8 mm-spacing cloud is thousands of
neighbours/pt — a much larger neighbourhood than k=24, so it is a scale reference, not a
like-for-like speedup.) Numbers are the shipped double-precision worker; the SoA-float
`csr_normals_bench` harness reaches ~8 s on the same op. See
`outputs/2026-06-14/cc_discontinuity_worker/{MATH_DERIVATIONS,BENCHMARK_RESULTS,HANDOFF_02}`.

## Build
```bash
bash build_mingw.sh   # static mingw64 g++ -O3 -fopenmp, no external libs, ~1.2 MB exe
```
`build_mingw.sh` forces a writable Windows `%TMP%` (mingw's g++ dies if `%TMP%` is `C:\WINDOWS`).
Deploy the exe beside the plug-in (`Libraries/Frahan.StonePack.MeshHeightmap/`).

## CLI
```
frahan_discontinuity_worker --in cloud.ply --out <dir>
  [--k 24] [--angle 12] [--band 2.5] [--seedeta 0.06] [--minfacet 40]
  [--bw 15] [--merge 8] [--minset 4] [--minshare 0.02] [--voxel 0]
  [--maxpts 6000000] [--segply]
```
Outputs `discontinuity.json` (per-set dip/dipdir/spacing/share + timings),
`facets.csv` (per-facet centroid + lower-hemi pole + set id + point count, for
density stereonets), and `segmented.ply` (RGB by set) when `--segply` is set.
`--minshare` drops joint sets holding less than this fraction of facet points.

## Notes / limits
Region-grow facet covariance is accumulated **seed-relative** (translation-invariant,
well-conditioned). A UTM cloud stored as float32 loses sub-decimetre precision in the
file itself (upstream) — ingest with a local offset or from LAZ scale+offset. The exact
minor-set count is bandwidth- and density-sensitive (the dominant sets are robust).

## Files
- `frahan_discontinuity_worker.cpp` — the worker (CSR grid + analytic eig + FACETS + mean-shift).
- `csr_normals_bench.cpp` — standalone neighbour-search benchmark harness (radius vs kNN,
  reorder on/off) used to derive the CSR evolution; not shipped in the plug-in.
- `build_mingw.sh` — static build.
