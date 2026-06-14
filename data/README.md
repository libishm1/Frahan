# Data/ — bundled sample datasets

This folder holds the sample datasets for every master-spine workflow, copied (not moved) from the
workspace `raw/` evidence store and the D-drive hunt on 2026-06-06. The originals stay in place, so existing
HILT `.gh`/`.3dm` links keep resolving. See `ATTRIBUTION.md` for source, DOI, license, and citation per
dataset.

Status: private staging. The blobs live on disk and are gitignored (only this README + `ATTRIBUTION.md` are
tracked). At the public/open-source step they migrate to Git LFS, with a download script for any set that
cannot be redistributed.

Layout:
- `eth1100/` — ETH1100 dry-stone closed meshes + CSVs (packing benchmark, Mesh Bench). Full raw set via Zenodo 10038881.
- `granite_dells_tls/` — Granite Dells TLS rock-face (scan ingest).
- `tongjiang/` — Tongjiang quarry UAV point clouds (reconstruction).
- `stanford_scans/` — Stanford bunny/dragon/buddha/armadillo/drill (mesh/recon tests).
- `gpr/grimsel`, `gpr/bondua`, `gpr/tu1208` — GPR radargrams (fracture extraction).
- `geocrack/` — GeoCrack fracture dataset (digitisation / GeoFractNet).
- `marbles/`, `misc_ply/` — Open3D Marbles + maintainer PLY assets.
- `research_reports/` — market-study reports.
- Rock-face / discontinuity clouds (joint-set extraction, Discontinuity Sets D5F10048):
  Granite Dells AZ clean in-situ granite (OpenTopography OT.122010.26912.1), Finestrat gypsum
  slope (Zenodo 7576524, CC-BY 4.0), RockCloud-Align registration crops (Wang et al. RS 2025).
  Full annotated catalog of these + ~15 more: `../docs/rockfaces_dataset.md`. Google Drive +
  per-set links: `DATA_ACCESS.md`.

Total on-disk: ~6.3 GB.
