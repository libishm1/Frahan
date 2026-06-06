# Example 07 - Scan ingestion (LiDAR / photogrammetry point cloud / GPR)

The full ingestion entrypoint: pull a raw site scan into Rhino as clean geometry the downstream
Frahan workflows (bench, slabs, quarry packing) consume. Covers three scan modalities. Units: meters.
Style: short sentences, no em dashes.

## DATA - download from Google Drive first

The scan datasets are too large for git. Download them from the shared Google Drive master folder,
then place each under `D:/code_ws/Data/<name>/` (the path this `.gh` references). The same link and
file names are written onto the canvas as a scribble.

**MASTER FOLDER:** https://drive.google.com/drive/folders/1mDj1Z20BB70SrkjQKnU6O3kDbfuA-mcS?usp=sharing

Download the file for the modality you want, and drop it in the matching local folder:

| Modality | Download this file from Drive | Put it here (local) |
|---|---|---|
| LiDAR / TLS point cloud | `granite_dells_tls/ot_GD_TLS_data_UTM.laz` | `D:/code_ws/Data/granite_dells_tls/` |
| Photogrammetry point cloud | `tongjiang/detail_cloudAB.ply` | `D:/code_ws/Data/tongjiang/` |
| GPR radargram (granite) | `gpr/grimsel/GPR_AU_N-to-S.rd3` | `D:/code_ws/Data/gpr/grimsel/` |

The full dataset list + original public sources is in `../../data/DATA_ACCESS.md`. Upload of the
datasets into the master folder completes within ~2 hours of 2026-06-06.

## Pipeline (LiDAR + photogrammetry point cloud)
1. INGEST: `Load E57 Cloud` (out-of-process pye57 worker) for `.e57`, or read the `.laz` / `.ply`
   point cloud. Voxel-downsample to a workable density.
2. CLEAN: statistical outlier removal + crop to the region of interest.
3. RECONSTRUCT: surface reconstruction (Poisson / Geogram) to a closed mesh, or keep the cloud for
   downstream point-based steps.
4. HAND OFF: the clean mesh / cloud flows into example 04 (scan to bench), 03 (quarry to slabs), or
   the quarry packing workflows.

## GPR ingestion + quarry GPR -> block packing
See `../03_gpr_fracture_granite/` (geologist GPR fracture spine): read the MALA radargram, migrate,
extract fracture reflectors, build 3D fracture surfaces, then pack saw-cuttable blocks between the
fractures with `Fracture Block Pack`. Download the GPR file named above first.

## Run
1. Open Rhino 8 + Grasshopper with the Frahan `.gha` deployed (see `../../docs/INSTALL.md`).
2. Open `07_scan_ingest_full.gh`. Read the on-canvas scribble: download the named file from the Drive
   master folder above and set the file-path input to it.
3. Press the per-stage `Run` toggles in order (default false on the heavy reconstruct node).
4. The clean mesh / cloud appears, ready for the downstream workflow.

## Tested (2026-06-06, real data)
Verified end-to-end on the local datasets:

- **Photogrammetry point cloud ingestion: WORKS.** `tongjiang/detail_cloudAB.ply` imports as a
  6,857,772-point cloud. See `07_photogrammetry_ingest.png`.

![Photogrammetry point cloud](07_photogrammetry_ingest.png)

- **Scan to mesh reconstruction: WORKS.** The cloud (subsampled to ~60k) reconstructs to a closed
  surface via `Scan Reconstruct` (Advancing-Front backend, out-of-process worker): 59,971 verts /
  111,973 tris in 3.9 s. AlphaShape auto-alpha gave a coarse hull on this noisy photogrammetry cloud;
  Advancing-Front follows the surface. The long spanning triangles are cap artifacts that
  `Clean Scan Mesh` peels. See `07_scan_to_mesh.png`.

![Scan to mesh reconstruction](07_scan_to_mesh.png)

- **LiDAR .laz: needs laszip, not Rhino import.** Rhino `-Import` of `ot_GD_TLS_data_UTM.laz` produces
  zero objects. Route `.laz` / `.las` through `laszip.net.dll` (shipped in the harness) or convert to
  `.e57` for the `Load E57 Cloud` worker. Documented so the workflow does not silently fail.
- **GPR .rd3: reads via Core** (`GprFileReader.Load` -> `GprRadargram`, 986 traces with picks). The
  GPR `.gh` opens without delay. See `../03_gpr_fracture_granite/`.

## Note
The `.gh` references data by local path and never internalizes the large cloud (KB-1). Repoint the
file input to your downloaded copy. Point clouds and meshes are referenced from `data/`, not embedded.
