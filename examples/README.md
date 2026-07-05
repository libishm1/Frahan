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
| `03_quarry_to_slabs/` | quarry block -> slab-cut-by-fractures -> slabs (SUPERSEDED by `23_quarry_to_slab/`, which ships README + PNGs + metrics) | the FractureBlockPack / slab-cut path | synthetic block + fracture planes; or `data/gpr/*` fractures |
| `03_gpr_fracture_granite/` | GPR radargram -> migrate -> fracture extraction (geologist brief) | granite-domain GPR fracture mapping | `data/gpr/grimsel/` (MALA .rd3/.rad) |
| `04_scan_to_bench_engineer/` | scan cloud -> normals -> reconstruct -> bench (engineer block-plan) | scan-to-bench reconstruction | `data/granite_dells_tls/` or `data/tongjiang/` |
| `05_artist_pointing_machine/` | scan mesh -> carving stages -> pointing-machine guide (artist) | the carving / pointing-machine spine | a scan mesh: `data/stanford_scans/` (or your own temple scan, DECIMATED per KB-1) |
| `07_scan_ingest_full/` | full scan-ingest pipeline (load -> downsample -> normals -> ICP -> reconstruct) | the ingest front-end every spine shares | `data/granite_dells_tls/`, `data/tongjiang/`, `data/stanford_scans/` |
| `10_pack2d/` | 2D slab nesting (exact NFP, hole-aware: HoleNest / Sheet Nest (Live)) | cut parts from a slab, 0-overlap | synthetic cut-list (in the .gh); METERS, slab 3.2x2.0 m |
| `11_pack3d/` | 3D block packing (Block Pack Tree, Kim 2025 guillotine) | quarry block subdivision, saw-cuttable | synthetic elements (in the .gh); METERS, block 3.0x1.5x1.5 m |
| `12_trencadis/` | Trencadis mosaic (Catalog Pack, CVD-Lloyd + Hungarian) | broken-tile cladding panel | synthetic shards (in the .gh); MILLIMETERS, panel ~1100 mm |
| `13_surface_mapping/` | twisted block -> CGAL split-by-angle -> Trencadis cladding | surfaces from a solid + surface mosaic | synthetic twisted monument (in the .gh); MILLIMETERS, 1.2x1.2x3.5 m |
| `14_kintsugi/` | fractured-vessel reassembly (PuzzleFusion++ Port mode) | restoration on Breaking Bad parity data | `14_kintsugi/data/bb_sample_*.bin`; natural network scale (auto-scale off), point-cloud display |
| `28_hole_nest/` | hole-aware 2D nesting (HoleNest / ContactNfpHoleNester) | pack parts INTO sheet-holes + part-holes, 0-overlap, deterministic | synthetic cut-list + holed sheet (in the .gh) |
| `29_liveedge_floor/` | live-edge flooring from irregular outlines (classify -> match+scribe -> brick-bond) | wavy-river irregular-board layout | synthetic boards (in the .gh, C# Script comp) |
| `30_discontinuity_sets/` | point cloud -> PCA normals -> FACETS facets -> Watson joint sets + stereonet | scan-to-joint-sets (Discontinuity Sets D5F10048) | `data/granite_dells_tls/` (clean granite) or `data/tongjiang/` |
| `31_discontinuity_ingest/` | measured-orientation ingest (CSV/GeoJSON/DXF/SHP) -> stereonet + Palmstrom block size | bring field/Compass/DSE measurements into Frahan | derived orientation tables (in the folder) |
| `32_scan_to_blocks/` | joint sets -> DFN bridge (D5F1004B/4C) -> BlockCutOpt Omni block yield | scan/DFN -> dimension-stone block packing | DFN from example 30 joint sets |

> Examples 15-27 (statue-to-blocks, surface packers, voussoirs 21/22, slab/marble 23-25,
> polygonal masonry 27) also ship in their folders; see `../docs/PERSONA_MAP.md` for the full map.

## More workflows (33-50 + vaults)
Each ships its own README (hero render, "what it shows", data provenance). The GPR chain (33-35, 40)
runs on real georeferenced surveys; 47-50 are the pre-CAM fabrication + BIM handoff tail.

| Folder | Demonstrates |
|---|---|
| `33_gpr_marble_guillotine/` | GPR marble -> stationary wire-saw guillotine blocks (the manufacturable hero) |
| `34_gpr_marble_oblique/` | oblique (dip-following) quarry cuts on the marble beds (the georeferencing prize) |
| `35_gpr_quarry_full_workflow/` | GPR quarry full spine: ingest -> beds -> slabs -> blocks |
| `36_fractured_block_to_slabs/` | fractured block -> two fracture-bounded slabs |
| `37_block_to_cladding_facade/` | block -> slabs -> cladding panels -> curved facade, with costing |
| `38_surface_discretize_tiles/` | surface discretization -> matched cut tiles -> slabs (Panel Tile Surface) |
| `39_concave_nest/` | concave-in-concave nesting (the honest high-yield trim) |
| `40_travertine_crosslithology/` | cross-lithology cut-yield across marble, travertine, andesite (native `.gsf`) |
| `41_floor_tiling/` | floor tile (boundary-trimmed) with grain direction + texture mapping |
| `42_wholeside_reassembly/` | whole-side reassembly (Whole-Side Assemble) |
| `43_nbo_dry_stone_wall/` | next-best-object dry-stone wall placement |
| `44_nbo_to_robot/` | NBO pose -> robot frame + Force-Seat URScript (robot handoff) |
| `45_cut_and_fill_excavation/` | cut-and-fill / soil excavation down to the rock face |
| `46_kinematic_intensity_screen/` | kinematic feasibility + fracture-intensity rock-mass screen |
| `47_fabrication_handoff/` | pre-CAM cut plan -> CAM / robot / COMPAS (the fabrication tail) |
| `48_block_matching_3d/` | 3D block matching / reassembly (Soft ICP 3D) |
| `49_extraction_order_plan/` | quarry inventory -> yield -> extraction order -> saw-bed schedule -> report |
| `50_castle_keep_ifc/` | castle keep -> masonry stability -> IFC / BIM export (the BIM handoff) |
| `vault_generation/` | compression-only masonry vaults (TNA form-finding + whole-shell CRA) |

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
| 6 surfaces (CGAL angle split) | 176 shards, 4 mm grout | 2/2 placed, verifier 0.71 |

## Data freshness (the links)
All datasets are in `../data/` with provenance in `../data/ATTRIBUTION.md`. The blobs are staged on disk
and move to Git LFS at the public step; until then fetch any missing set via the download links in
ATTRIBUTION. The example data inputs reference `../data/<set>/` by the path the README lists.

## Where to start
Two navigation docs route you to the right examples and components:

- [`../docs/PERSONA_MAP.md`](../docs/PERSONA_MAP.md) — entry points by persona: geologists (03/08/09),
  surveyors (04/07/26), computational designers/architects (16/17/27, voussoirs 21/22), stone masons
  (11/24/25), artists (05/12/14/15), OSS developers (Lab primitives).
- [`../docs/SUPERSESSION_MAP.md`](../docs/SUPERSESSION_MAP.md) — which legacy components were superseded
  by which current forms, with the benchmarks. Legacy stays loadable; use the current forms in new
  canvases.

## Status + the live refresh
Examples 01-07 are the proven last-working canvases (the baseline). Examples 10-14 were built, solved,
captured, and saved LIVE (2026-06-06) via the official Rhino MCP + `GH_DocumentIO.SaveQuiet`, then
corrected to per-application physical scale + ground position + tolerances (the SLM+ROSES study). All
result `.3dm` carry the right unit label (m or mm) and sit on z=0. The bundled `.gha` in `../install/`
is the current build; the in-component defaults now self-scale (auto tolerance, auto cell size, strict
no-overlap) so the examples open correct on the first try. See `../wiki/research/tolerances_dimensions_slm_roses.md`,
`../handoffs/LIVE_EXAMPLE_BUILD_HANDOFF.md`, and `REGENERATION_RECIPE.md`.
