# Master-spine examples

The working master-spine workflows, each with its last-working/tested/benchmarked `.gh` + `.3dm` + the
fresh `data/` link. Open them in Rhino 8 + Grasshopper with the Frahan `.gha` deployed (see
`../docs/INSTALL.md`). Read `GRASSHOPPER_BEST_PRACTICES.md` first. Style: short sentences, no em dashes.

## How to run any example
1. Deploy the `.gha` (Rhino closed), open the `.3dm`, then the `.gh`.
2. Heavy nodes (Scan Reconstruct, GPR migration, Block Pack, Carving, Recovery Cascade) ship with a
   default-FALSE `Run` toggle. Press the per-stage Run toggles IN ORDER; do a single deliberate solve per
   stage (this avoids long blocking solves). Watch the message line for progress.
3. Point each data input at the `data/` link listed below. The `.3dm` carries its own geometry; only the
   external file-reference nodes need the path.

## The workflows
| Folder | Workflow | Demonstrates | Data link |
|---|---|---|---|
| `01_quarry_to_wall/` | quarry block -> fracture decompose -> ashlar pack -> masonry wall | the full quarry-to-masonry spine | synthetic quarry block (in the .3dm) |
| `02_masonry_assembly/` | masonry assembly + build-order colouring + stability | assembly + RBE stability (W4-fixed) | synthetic blocks (in the .3dm) |
| `03_quarry_to_slabs/` | quarry block -> slab-cut-by-fractures -> slabs (block-packing in fractures) | the FractureBlockPack / slab-cut path | synthetic block + fracture planes; or `data/gpr/*` fractures |
| `03_gpr_fracture_granite/` | GPR radargram -> migrate -> fracture extraction (geologist brief) | granite-domain GPR fracture mapping | `data/gpr/grimsel/` (MALA .rd3/.rad) |
| `04_scan_to_bench_engineer/` | scan cloud -> normals -> reconstruct -> bench (engineer block-plan) | scan-to-bench reconstruction | `data/granite_dells_tls/` or `data/tongjiang/` |
| `05_artist_pointing_machine/` | scan mesh -> carving stages -> pointing-machine guide (artist) | the carving / pointing-machine spine | a scan mesh: `data/stanford_scans/` (or your own temple scan, DECIMATED per KB-1) |
| `07_scan_ingest_full/` | full scan-ingest pipeline (load -> downsample -> normals -> ICP -> reconstruct) | the ingest front-end every spine shares | `data/granite_dells_tls/`, `data/tongjiang/`, `data/stanford_scans/` |

## Data freshness (the links)
All datasets are in `../data/` with provenance in `../data/ATTRIBUTION.md`. The blobs are staged on disk
and move to Git LFS at the public step; until then fetch any missing set via the download links in
ATTRIBUTION. The example data inputs reference `../data/<set>/` by the path the README lists.

## Status + the live refresh
These `.gh` are the proven last-working canvases (the baseline). The remaining live refresh -- repath each
data input to the `data/` link, add/verify the coloured stage groups + scribbles, run one deliberate gated
solve per canvas, and capture a shaded viewport PNG -- runs on a live Rhino + Grasshopper slot. It is
gated on the Grasshopper MCP load/save listener being up (the g1 bridge can build canvases but not write
`.gh` files; the save/load listener was down at packaging time). See `REGENERATION_RECIPE.md`.
