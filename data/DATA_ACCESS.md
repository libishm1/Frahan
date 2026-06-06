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
| Granite Dells TLS | `granite_dells_tls/` | 5.4 MB | https://drive.google.com/drive/folders/1OVqu7hzPF8__vR4rX2Res1LWd5VILBFS | OpenTopography / TLS (see ATTRIBUTION.md) |
| Research reports | `research_reports/` | 80 KB | https://drive.google.com/drive/folders/1U03dm_ZZY5_-n8fNe4uh2UhFjL8FsNwO | curated reports |

Total ~6.3 GB. Full provenance + licenses: `ATTRIBUTION.md`.

## Local copy
The blobs live locally at `D:/code_ws/Data/` (outside this repo, so git operations never touch them).
Example workflows reference datasets by that path; repoint to your local copy or a Drive-synced folder.

## Why Google Drive, not Git LFS
6.3 GB exceeds GitHub's 1 GB free LFS storage + 1 GB/month bandwidth (a paid data pack would work, ~$5/mo
per 50 GB, but Drive is cheaper + simpler for mostly-third-party download data). The small plugin binaries
+ Kintsugi weights the examples need ARE in git LFS under `install/`.
