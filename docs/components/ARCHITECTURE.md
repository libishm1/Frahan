# Frahan StonePack - Architecture

`v0.1.0-alpha` research preview. Repository: https://github.com/libishm1/Frahan.
This page is wiki source: the system architecture, the end-to-end pipeline, the code
modules, and the component map. The per-component detail is in `COMPONENTS.md` /
`components.json`; the live connection graph is in `connection_map.mmd` (187 nodes, 93 edges).
Style: short sentences, no em dashes.

## 1. What it is

A Rhino 8 / Grasshopper plugin for stone-fabrication readiness: the bridge layer between
design intent and machine-ready fabrication for dimension stone, monuments, and dry-stone
masonry. It is **pre-CAM**: it turns survey + design intent into fabrication-ready geometry
(blocks, cut plans, nested sheets, stable assemblies), and hands clean geometry to the CAM /
machine step. It is not a CAM controller and not a structural-engineering authority.

## 2. The pipeline (data flow)

One spine runs left to right. Each stage is a Grasshopper component family on the `Frahan`
ribbon tab; the `[RelatedComponent]` edges in `connection_map.mmd` wire the stages together.

```
GPR / scan  ->  discontinuity / fracture  ->  reconstruction  ->  DFN  ->
block packing + cutting  ->  masonry assembly  ->  fabrication export
```

| Stage | Input | Output | Component subcategories |
|---|---|---|---|
| Ingest | GPR radargrams, point clouds, vector/CAD/raster | clouds, meshes, fracture traces | Ingest, ScanIngest |
| Discontinuity / fracture | point cloud, GPR B-scan | joint sets, stereonet, fracture planes, block size | Quarry, Fracture, Analysis |
| Reconstruction | point cloud / mesh soup | clean watertight mesh | Mesh |
| DFN | joint-set statistics | fracture-network mesh + bench | Quarry (Joint Sets to DFN, Stochastic DFN) |
| Block packing + cutting | bench + fractures / containers | block sets, guillotine cut plans, recovery | Quarry (BlockCutOpt, FractureBlockPack), 3D Packing, Slab |
| 2D nesting | parts + sheet (with holes) | 0-overlap nested layout | 2D Packing, Surface Packing, Trencadis |
| Masonry assembly | stones / wall target | placed wall, equilibrium certificate, metrics | Masonry, Voussoir |
| Restoration / matching | broken fragments | reassembly poses | EdgeMatch, Kintsugi |
| Fabrication export | cut planes / placements | cut plans, robot targets | Fabricate, Reports |

The DFN bridge is the key decoupling: joint-set statistics produce a fracture network even
when the scan mesh is incomplete, so block packing does not depend on a watertight scan.

## 3. Code modules

Five managed modules plus native shims and external tools. The Core is Rhino-free so the
algorithms are headless-testable.

| Module | Role | Depends on |
|---|---|---|
| `Frahan.StonePack.Core` | Rhino-free algorithm library (packing, NFP, block-cut, masonry, DFN, discontinuity math, readers). Source of truth for the math. | Clipper2, NetTopologySuite; no RhinoCommon |
| `Frahan.StonePack.GH` (`.gha`) | The Grasshopper front end: 187 components, the `Frahan` ribbon tab, async Run-gated heavy nodes, icons. | Core, RhinoCommon, Grasshopper |
| `Frahan.StonePack` (`.rhp`) | Rhino plugin shell. | Core, RhinoCommon |
| `Frahan.EdgeMatching.Core` | Rhino-free edge-matching / fracture-reassembly library. | Core |
| `Frahan.Kintsugi.Port` | Learned 6-DoF reassembly, a non-commercial research-only port of PuzzleFusion++. Quarantined, absent from the default install. | TorchSharp/libtorch (optional) |

### Native shims (optional at runtime, with managed fallbacks)
- `frahan_cgal.dll` - CGAL booleans / PMP / segmentation / reconstruction (GPL; corresponding shim source in `native/cgal_shim/`). Fallback: `MeshCsg` (csg.js port, MIT) + BSP.
- `frahan_geogram.dll` - Geogram reconstruction + remesh (bundles Kazhdan Poisson).
- `frahan_coacd.dll` - approximate convex decomposition for physics settling.
- `native/nfp_kernel` (Clipper2) - exact no-fit-polygon kernel.
- `frahan_discontinuity_worker.exe` / `frahan_recon_worker.exe` - out-of-process workers.

### External tools
- `bff-command-line.exe` - Boundary First Flattening (Sawhney & Crane 2017), static single-exe.
- `tools/Frahan.StonePack.Harness` - headless benchmark + test driver (boots Rhino.Inside).

## 4. Component map (187 components, 18 subcategories)

The component catalog and the connection graph are auto-generated from source
(`extract_components.py`), so they stay current. Distribution:

| Subcategory | Count | Pipeline role |
|---|---|---|
| Masonry | 35 | wall generation, CRA equilibrium, Lambda/J metrics, assembly |
| Quarry | 28 | discontinuity, DFN, BlockCutOpt, fracture block packing |
| Mesh | 24 | reconstruction, booleans, decimation, segmentation |
| EdgeMatch | 14 | fracture reassembly (geometric) |
| 2D Packing | 13 | NFP-BLF + hole-aware nesting |
| Fracture | 10 | GPR fracture extraction, fracture geometry |
| 3D Packing | 9 | block / item packing, settling |
| Fabricate | 9 | cut plans, robot/KUKA adapters, export |
| Kintsugi | 7 | learned 6-DoF reassembly (research-only) |
| Lab | 6 | experimental primitives (cross-referenced) |
| Ingest | 5 | vector / GPR / cloud / CAD readers |
| Trencadis | 5 | mosaic cladding |
| Voussoir | 5 | arch / vault stereotomy |
| Slab | 4 | slab cutting |
| Surface Packing | 4 | BFF flatten + pack + lift |
| Analysis | 3 | stereonet, block size, diagnostics |
| Reports | 3 | metrics / export |
| Sculpt | 3 | carving stages, pointing machine |

The 93 connection edges encode upstream/downstream relationships (e.g. `Discontinuity Sets ->
Joint Sets to DFN -> BlockCutOpt Omni Solve -> Fracture Block Pack`). See `connection_map.mmd`
for the full graph grouped by subcategory.

## 5. Architecture patterns (the design rules)

- **Rhino-free Core.** All algorithm math lives in `Frahan.StonePack.Core` with no RhinoCommon
  dependency, so it is unit-tested headless. The GH layer is a thin adapter.
- **Facade over published primitives.** Monolith components (e.g. heterogeneous packers) are a
  facade over standalone primitives, never a black box; both are kept and cross-linked.
- **Async, Run-gated heavy nodes.** Reconstruction, GPR migration, block packing, carving, and
  recovery ship a default-FALSE `Run` toggle and run on a background task, so the canvas never
  freezes. Source/terminal load-once nodes are async; mid-graph nodes stay synchronous.
- **Out-of-process workers.** The heaviest native work (E57 read, discontinuity worker, recon
  worker, BFF) runs as a separate process over binary IPC, so a native crash never takes Rhino
  down.
- **Self-describing components.** Every component carries an `[Algorithm]` attribute (title +
  citation, shown on hover) and `[RelatedComponent]` edges (the navigation graph). These drive
  the auto-generated catalog and connection map.
- **Numeric hygiene (one shared layer).** Recenter geometry before computing, use a
  scale-relative epsilon, snap booleans through a robust integer kernel. This is the
  cross-cutting fix that makes site-coordinate (UTM) work reliable.
- **Truth criterion (c): visual validation.** A green test gate verifies invariants; final
  correctness is live visual validation in Rhino/Grasshopper. Examples are live-built, captured
  at correct physical scale, and reproduce on cold reopen.

## 6. Data and assets

- Datasets are real and attributed (`data/ATTRIBUTION.md`, `data/DATA_ACCESS.md`): ETH1100,
  Tongjiang, Granite Dells, Finestrat, Grimsel/Bondua/TU1208 GPR, Loviisa, Stanford, GeoCrack.
- Large blobs (~6.3 GB) are gitignored and hosted on Google Drive; only small assets (Loviisa
  shapefiles, plugin binaries, Kintsugi weights) live in Git LFS.
- Each dataset keeps its own upstream license, independent of the GPL-3.0 code license.

## 7. Build, test, install

- Build: `dotnet build src/Frahan.StonePack.GH/Frahan.StonePack.GH.csproj -c Release` (net48).
- Test: `tests/Frahan.StonePack.Tests` - **1034 PASS / 0 FAIL / 147 SKIP** (2026-06-14, clean
  clone, headless; skips are Rhino-runtime + optional-dataset gates).
- Install: `git lfs pull` then `install/deploy.ps1` (Rhino closed); one `.gha`. See `docs/INSTALL.md`.

## 8. License posture

GPL-3.0, released for educational and research use (`LICENSE`, `NOTICE.md`). The default install
links no copyleft-incompatible or non-commercial code; CGAL (GPL) native shims are optional with
managed fallbacks; the Kintsugi learned module + weights (PuzzleFusion++, non-commercial
research-only) are quarantined out of the default install. Full register: `THIRD_PARTY_NOTICES.md`,
`docs/thesis/90_originality.md`.
