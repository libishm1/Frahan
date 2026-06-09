# Data attribution + provenance (Frahan StonePack)

Date: 2026-06-06. Every bundled dataset, its source, DOI, license, citation, download link, and the
workflow that consumes it. Per the migration decision: bundle everything with attribution (license risk
accepted by the maintainer). Large blobs live on disk now and move to Git LFS at the public step; this
markdown is tracked so provenance travels with the repo. Style: short sentences, no em dashes.

## Point-cloud / mesh scans
| Folder | Dataset | DOI / source | License | Citation | Workflow |
|---|---|---|---|---|---|
| `eth1100/closed` + CSVs | ETH1100 dry-stone closed meshes (1100 .obj) + shape/candidate CSVs | Zenodo 10038881 | CC-BY-4.0 | Johns et al., ETH Zurich | 2D/3D packing benchmark, Mesh Bench |
| `granite_dells_tls` | Granite Dells TLS rock-face (`ot_GD_TLS_data_UTM.laz`) | DOI 10.5069/G9Z60KZ8 (OpenTopography) | UNKNOWN ("Not Provided" on OT) | OpenTopography OT.122010.26912.1 | scan ingest -> engineer plan |
| `tongjiang` | Tongjiang limestone quarry UAV point clouds (7 .ply) | DOI 10.5281/zenodo.15614501 | CC-BY-4.0 | Zenodo 15614501 | scan ingest -> reconstruction -> Mesh Bench |
| `stanford_scans/*` | Stanford 3D Scanning Repository (bunny, dragon, happy buddha, armadillo, drill) | http://graphics.stanford.edu/data/3Dscanrep/ | Stanford research (commercial use needs permission) | Stanford CG Lab | mesh/recon smoke tests, carving |
| `marbles` | Open3D Marbles.ply | Open3D example asset | MIT | Open3D | Mesh Bench smoke test |
| `misc_ply` | granite_shards.ply, substrate.ply | maintainer-provided (scope TBC) | maintainer | Libish | TBC (confirm workflow) |
| `geocrack` | GeoCrack fracture dataset (`dataverse_files.zip`, 11 sites, 12158 patches) | MIT-licensed CNN dataset | MIT | GeoFractNet / GeoCrack | fracture digitisation / GeoFractNet |

## GPR radargrams
| Folder | Dataset | DOI / source | License | Workflow |
|---|---|---|---|---|
| `gpr/grimsel` | Grimsel ISC GPR (MALA GX160, AU+VE tunnels, granite) | DOI 10.3929/ethz-b-000420930 | CC-BY-4.0 | primary granite-domain GPR fracture extraction |
| `gpr/bondua` | Bondua et al. 2024 quarry GPR (Botticino marble grids `.DT`) | DOI 10.17632/w26n6nftxs.3 | CC-BY-4.0 (verified: MDPI Data 10.3390/data9030042 + Mendeley record; earlier NC-ND label was a mislabel) | 3D viz + extraction-algorithm testing (marble, not granite) |
| `gpr/tu1208` | TU1208 IFSTTAR multi-rock GPR (MALA .rd3/.rad + GSSI .dzt) | DOI 10.5281/zenodo.1211173 | CC-BY | GPR reader validation (gneiss/limestone/silt) |

## Reference reports
| Folder | Item | Source | Workflow |
|---|---|---|---|
| `research_reports` | deep-research market studies (stone fabricators; granite GPR + TN analogues) | maintainer research | market positioning, gap analysis |

## License notes
- The repo is GPL-3.0 (whole-repo, because Frahan.Kintsugi.Port is a GPL-3.0 port and stays linked).
- Datasets carry their OWN upstream licenses (above), independent of the code license. Non-commercial /
  unknown-license sets (Granite Dells "Not Provided", Stanford research-only) are
  bundled with attribution per the maintainer decision; downstream users must honor each upstream license.
- At the public/open-source step, large blobs migrate to Git LFS and a download script will fetch any that
  cannot be redistributed.
