# Data access

The large dataset blobs are NOT stored in git (6.3 GB total; GitHub free Git-LFS is only 1 GB). They are
hosted on Google Drive (folders created + linked below) and also have their original public source links.
This file, the folder structure, and all metadata/docs ARE tracked in git, so the data folder + provenance
are never lost. Style: short sentences, no em dashes.

## Google Drive (shared 2026-06-06)
- MASTER FOLDER (all datasets, shared): https://drive.google.com/drive/folders/1mDj1Z20BB70SrkjQKnU6O3kDbfuA-mcS?usp=sharing
- The datasets upload into this one shared folder (upload completes within ~2 hours of 2026-06-06).
- TO USE: open the master folder, download the dataset subfolder you need, and place it under
  `D:/code_ws/Data/<name>/` (the path the example workflows reference). The Grasshopper scan-ingestion
  examples carry this link and name the exact file to download.

## Datasets (size + Drive subfolder + source)
| Dataset | Folder | Size | Google Drive | Original source |
|---|---|---|---|---|
| GeoCrack fracture patches | `geocrack/` | 4.9 GB | https://drive.google.com/drive/folders/1yeKQbT_P9rjohnzgBCmKBSzsATfVi69F | Dataverse (GeoCrack; 11 sites, 12158 patches, MIT) |
| Tongjiang quarry scan | `tongjiang/` | 563 MB | https://drive.google.com/drive/folders/1G-sGBFGY-Dx7K0wAupOj3CisW36CHnZ6 | quarry TLS/photogrammetry (see ATTRIBUTION.md) |
| GPR radargrams | `gpr/` | 463 MB | https://drive.google.com/drive/folders/1iMIBjz94dkT2pV0Kv8Y2IQAKnmvkzG6T | MALA / Grimsel + marble grids (see ATTRIBUTION.md) |
| Stanford scans | `stanford_scans/` | 252 MB | https://drive.google.com/drive/folders/1_QPGBLvojkbaVoPgwkP9sbnRpXGSj_4e | Stanford 3D Scanning Repository (bunny, dragon, armadillo) |
| ETH1100 dry-stone | `eth1100/` | 113 MB | https://drive.google.com/drive/folders/1mcFl7oiyh4wQHyOlL6f_WLplxFRDQkfO | Zenodo 10038881 (ETH dry-stone, 1100 meshes + labels) |
| Misc PLY | `misc_ply/` | 27 MB | https://drive.google.com/drive/folders/1imtAVqaAhjP9WQdFFmxi_oVfU4wRhg4b | assorted (see ATTRIBUTION.md) |
| Marble grids | `marbles/` | 13 MB | https://drive.google.com/drive/folders/1HcSuWLGBWHSi5ewkqyeO_B4TtZnfNcrw | marble GPR grids (see ATTRIBUTION.md) |
| Granite Dells TLS | `granite_dells_tls/` | 5.4 MB LAZ + 60 MB f32 PLY | https://drive.google.com/drive/folders/1OVqu7hzPF8__vR4rX2Res1LWd5VILBFS | OpenTopography OT.122010.26912.1 (clean in-situ granite; + discontinuity_result.json) |
| Research reports | `research_reports/` | 80 KB | https://drive.google.com/drive/folders/1U03dm_ZZY5_-n8fNe4uh2UhFjL8FsNwO | curated reports |
| Loviisa fracture maps | `loviisa/` | 2.1 MB | in-git (small, LFS) | Chudasama 2022 rapakivi-granite surface fracture traces, Zenodo 10.5281/zenodo.7077494, CC-BY 4.0 |
| Finestrat rock slope | `finestrat/` | 62 MB | https://drive.google.com/drive/folders/1Y40u9sQgfxAW-WTqzPnmuPDOK1fHfjwh | Riquelme/DSE gypsum slope, Zenodo 7576524, CC-BY 4.0 (complete-face discontinuity demo) |
| RockCloud-Align crops | `rockcloud_align/` | 200 KB sample (7.4 GB full) | https://drive.google.com/drive/folders/1vwat2M_KH_LOCi_YZnwfCy_-h4-5h0sc | Wang et al. RS 2025; registration benchmark (crops, not full faces) |

The discontinuity-analysis outputs (result JSONs, stereonets, dataset README + catalog) live under the
master folder's `Data/_discontinuity_analysis_2026-06-14/`:
https://drive.google.com/drive/folders/13I5hiauANeryzL2OR3qXYzo2q9MOTfVC . The heavy clouds
(`granite_dells_f32.ply` 60 MB, `finestrat_2011.txt` 62 MB, the 7.4 GB RockCloud-Align rar) upload
separately. Full annotated catalog: `../docs/rockfaces_dataset.md`.

Total ~6.3 GB on Drive plus the small in-git Loviisa shapefiles. Full provenance + licenses:
`ATTRIBUTION.md` and `loviisa/DATASET.md`. Loviisa is small enough to live in git (LFS), so example 26
runs with no download. Worked example: `../examples/26_loviisa_surface_fractures/`.

## Local copy
The blobs live locally at `D:/code_ws/Data/` (outside this repo, so git operations never touch them).
Example workflows reference datasets by that path; repoint to your local copy or a Drive-synced folder.

## Why Google Drive, not Git LFS
6.3 GB exceeds GitHub's 1 GB free LFS storage + 1 GB/month bandwidth (a paid data pack would work, ~$5/mo
per 50 GB, but Drive is cheaper + simpler for mostly-third-party download data). The small plugin binaries
+ Kintsugi weights the examples need ARE in git LFS under `install/`.
