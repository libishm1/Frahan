# Migration dataset inventory + copy plan (Stage B)

Date: 2026-06-06. Style: short sentences, no em dashes. Stage B of MIGRATION_PLAN.md. The authoritative
register is `wiki/index/data_assets_inventory.md` (every asset: path, format, size, DOI, license, on-disk,
downstream consumer). This file adds the master-spine workflow -> dataset -> target `data/` mapping + the
D:-only extras found by the D-drive hunt + the copy rule. NO data is moved yet (copies happen in Stage C).

## D-drive hunt result (datasets outside code_ws)
| File | Size | Identity / license | Action |
|---|---|---|---|
| `D:/Downloads/Marbles.ply` | 13 MB | A13 Open3D Marbles, MIT | COPY to data/stanford_misc or data/marbles |
| `D:/dataverse_files.zip` | 4.9 GB | GeoCrack fracture dataset (11 sites, MIT) | LFS at public step; keep link now |
| `D:/granite_shards.ply` | (PLY) | user asset (not in register) | CONFIRM with Libish: include? which workflow? |
| `D:/substrate.ply` | (PLY) | user asset (not in register) | CONFIRM with Libish: include? which workflow? |
| `D:/deep-research-report*.md` | 80 KB | market studies (reference) | COPY to research/market/ |

## Master-spine workflow -> dataset -> target data/ folder
| Workflow | Dataset (source path) | Target | License | Note |
|---|---|---|---|---|
| 3D pack / Mesh Bench | ETH1100 closed meshes `raw/2026-05-25/eth_drystone/closed/` (+ CSVs) | data/eth1100/ | CC-BY-4.0 | the packing benchmark fixture; cite Johns et al., Zenodo 10038881 |
| Scan ingest / engineer plan | Granite Dells TLS `raw/2026-05-27/granite_dells_tls/ot_GD_TLS_data_UTM.laz` | data/granite_dells_tls/ | UNKNOWN (OT "Not Provided") | flag license before public; cite DOI 10.5069/G9Z60KZ8 |
| Scan ingest / reconstruction | Tongjiang quarry `raw/2026-05-27/tongjiang/` (7 ply, 563 MB) | data/tongjiang/ | CC-BY-4.0 | LFS; Zenodo 15614501 |
| Mesh/recon smoke + carving | Stanford bunny/dragon/buddha/armadillo/drill `raw/2026-05-27/stanford_*/` | data/stanford_scans/ | Stanford research (non-commercial) | flag: commercial use needs permission |
| GPR fracture (granite) | Grimsel `raw/2026-06-04/grimsel_gpr/` (17 MB) | data/gpr/grimsel/ | CC-BY-4.0 | primary granite GPR; DOI 10.3929/ethz-b-000420930 |
| GPR fracture (marble grids) | Bondua `raw/2026-06-04/bondua_gpr/` (.DT) | data/gpr/bondua/ | CC-BY-NC-ND | non-commercial, no-derivatives; viz + algo-test only |
| GPR multi-rock | TU1208 `raw/2026-05-27/gpr_tu1208/` (227 MB) | data/gpr/tu1208/ | CC-BY | MALA + GSSI |
| 2D packing examples | synthetic (harness-generated) + ETH footprints | data/eth1100/ (reuse) | CC-BY-4.0 | the .gh examples reference these |
| Carving / pointing-machine | the artist temple scan (Libish's own, 2.2M verts) | data/temple_scan/ (DECIMATED 146k per KB-1) | Libish-owned | KB-1: never internalize the full mesh in a .gh |

## Copy rule (Stage C, no breakage)
- COPY (never move) each dataset from its current `raw/` path into `code_ws/Data/<target>/`. The originals
  stay, so every existing HILT `.gh`/`.3dm` link keeps resolving. New example `.gh` (Stage E COPIES) point
  at `data/` relative paths.
- Each `data/<target>/` gets an `ATTRIBUTION.md` (source, DOI, license, citation, download link).
- Large data (Tongjiang 563 MB, TU1208 227 MB, GeoCrack 4.9 GB, Granite Dells) -> Git LFS at the PUBLIC
  step; private step keeps download links + small representative samples.
- CHECKPOINT one line per dataset copied in `migration/CHECKPOINT_MIGRATION.jsonl`.

## Decisions needed from Libish before Stage C
1. `granite_shards.ply` + `substrate.ply` (D:\ root): include in the repo? which workflow do they belong to?
2. License: Granite Dells TLS is "Not Provided" on OpenTopography, and Stanford scans are research-only
   (commercial = permission). Ship as samples-with-attribution, or download-link-only, in a public MIT repo?
3. Kintsugi.Port GPL-3.0 split (from MIGRATION_PLAN section 5): isolate behind an optional build flag so the
   core ships MIT? (recommended)
4. Official repo name + collaborators (for Stage H).
