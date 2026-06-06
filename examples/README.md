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
| `10_pack2d/` | 2D slab nesting (exact NFP-BLF, FreeNestX) | cut parts from a slab, 0-overlap | synthetic cut-list (in the .gh); METERS, slab 3.2x2.0 m |
| `11_pack3d/` | 3D block packing (Block Pack Tree, Kim 2025 guillotine) | quarry block subdivision, saw-cuttable | synthetic elements (in the .gh); METERS, block 3.0x1.5x1.5 m |
| `12_trencadis/` | Trencadis mosaic (Catalog Pack, CVD-Lloyd + Hungarian) | broken-tile cladding panel | synthetic shards (in the .gh); MILLIMETERS, panel ~1100 mm |
| `13_surface_mapping/` | twisted block -> CGAL split-by-angle -> Trencadis cladding | surfaces from a solid + surface mosaic | synthetic twisted monument (in the .gh); MILLIMETERS, 1.2x1.2x3.5 m |
| `14_kintsugi/` | fractured-vessel reassembly (PuzzleFusion++ Port mode) | restoration on Breaking Bad parity data | `14_kintsugi/data/bb_sample_*.bin`; natural network scale (auto-scale off), point-cloud display |

## Digital-fabrication entrypoint renders (examples 10-14)
Built and solved live, then captured. Each is at correct per-application physical scale (meters for
slab/block/monument, millimetres for mosaic/vessel) and sits on the z=0 ground plane (2D nests flat on XY).
See each folder's README for the numbers and `../wiki/research/tolerances_dimensions_slm_roses.md` for the
scale + tolerance basis.

| 2D slab nest | 3D quarry block | Trencadis mosaic |
|---|---|---|
| ![2D](10_pack2d/10_pack2d_result.png) | ![3D](11_pack3d/11_pack3d_result.png) | ![mosaic](12_trencadis/12_trencadis_result.png) |
| 18 parts, 0-overlap, 3.2x2.0 m | 12/12 packed, 3.0x1.5x1.5 m | 100 shards, 5 mm grout, 1100 mm |

| Surfaces from a solid | Trencadis on the twist | Kintsugi reassembly |
|---|---|---|
| ![surf](13_surface_mapping/13_surface_segments.png) | ![clad](13_surface_mapping/13_surface_trencadis.png) | ![kintsugi](14_kintsugi/14_kintsugi_result.png) |
| 6 surfaces (CGAL angle split) | 408 shards, 4 mm grout | 2/2 placed, verifier 0.71 |

## Data freshness (the links)
All datasets are in `../data/` with provenance in `../data/ATTRIBUTION.md`. The blobs are staged on disk
and move to Git LFS at the public step; until then fetch any missing set via the download links in
ATTRIBUTION. The example data inputs reference `../data/<set>/` by the path the README lists.

## Status + the live refresh
Examples 01-07 are the proven last-working canvases (the baseline). Examples 10-14 were built, solved,
captured, and saved LIVE (2026-06-06) via the official Rhino MCP + `GH_DocumentIO.SaveQuiet`, then
corrected to per-application physical scale + ground position + tolerances (the SLM+ROSES study). All
result `.3dm` carry the right unit label (m or mm) and sit on z=0. The bundled `.gha` in `../install/`
is the current build; the in-component defaults now self-scale (auto tolerance, auto cell size, strict
no-overlap) so the examples open correct on the first try. See `../wiki/research/tolerances_dimensions_slm_roses.md`,
`../handoffs/LIVE_EXAMPLE_BUILD_HANDOFF.md`, and `REGENERATION_RECIPE.md`.
