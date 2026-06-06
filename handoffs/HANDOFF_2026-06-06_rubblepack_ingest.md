# Handoff - 2026-06-06 (RubblePack components, BFF deps, Drive, ingestion)

Style: short sentences, no em dashes. This captures the late-session state for a clean resume.

## Done + pushed (github.com/libishm1/Frahan main)
- **Example 15 rubble carving (Branch B v2)**: true-enclosure comparison A (evolved fit) vs B
  (multi-bin) vs C (CoACD convex). Commits 6be7acd and earlier. See 15_statue_to_blocks/README.md.
- **Frahan.RubblePack.gha** (commit c8a4151): two REAL Frahan > Quarry components, per user directive
  "make these algorithms components, not script code":
  - `Rubble Evolved Fit` (GUID b1c2d3e4-aa02-...): one block per stone, tightest stone, pose evolved
    by 24 rotation seeds + (1+8)-ES, accepted only when every block vertex is inside the stone.
  - `Rubble Multi-Bin Pack` (GUID b1c2d3e4-aa03-...): voxel-occupancy FFD, many blocks per stone,
    true per-vertex enclosure + kerf, spill to next stone.
  - Source: `src/Frahan.RubblePack/` (csproj + 4 .cs). Build: `dotnet build` (2.9s, 0 err). Deployed
    to `%APPDATA%/Grasshopper/Libraries/` AND `install/plugin/`. Verified loading on fresh slot.
- **BFF self-contained** (commit c8a4151): 17 runtime DLLs (SuiteSparse + OpenBLAS + MinGW, ~66 MB)
  copied next to `install/tools/bff-command-line.exe` from the BFF windows-v1.6 distribution
  (`D:/code_ws/Agent-orchestration-main/.../dist/windows-v1.6/`). Verified: flattens face.obj exit 0.
  LFS-tracked (`.gitattributes` + `.gitignore` updated for `install/tools/*.dll`).
- **Bullet physics**: `libbulletc.dll` + `BulletSharp.dll` added to `install/plugin` (commit c8a4151).
- **Google Drive link** (commit 309bb0b): master folder
  `https://drive.google.com/drive/folders/1mDj1Z20BB70SrkjQKnU6O3kDbfuA-mcS` wired into
  `data/DATA_ACCESS.md`, `examples/07_scan_ingest_full/README.md` (+ on-canvas note panel in the .gh),
  and `examples/03_gpr_fracture_granite/README.md`. User uploads datasets via Drive-for-Desktop (~2h).
- **CoACD demonstrator**: `examples/15_statue_to_blocks/15C_coacd_demo.gh` (real Quarry Decompose By
  CoACD, bunny internalized, Run=false to avoid the 180s auto-solve).

## Ingestion test results (local data at D:/code_ws/Data/)
- **Photogrammetry .ply: WORKS.** `tongjiang/detail_cloudAB.ply` imports as a 6.86M-point PointCloud.
  PNG: `examples/07_scan_ingest_full/07_photogrammetry_ingest.png`.
- **LiDAR .laz: does NOT import via Rhino `-Import`** (0 objects). Needs `laszip.net.dll` (shipped in
  the harness) or the E57 worker. The scan-ingest workflow must route .laz through laszip, not Rhino.
- **GPR .rd3: reads via Core** `GprFileReader.Load` -> `GprRadargram` (986 traces, has Picks). The
  GPR .gh opens fine (the earlier "delay" was an error dialog the user dismissed, not a hang).
  Radargram-to-PNG render pending (GprTrace sample accessor: use `.Samples`, confirm member name).

## Key gotchas (verified this session)
- `doc.Objects` and run_csharp enumeration SKIP objects on invisible layers. Set all layers visible
  before reading, or use `ObjectEnumeratorSettings{HiddenObjects=true}`. (This explained earlier
  "vanished" meshes - they were only hidden.)
- Viewport `CaptureToBitmap` does NOT honor material transparency (Ghosted/Rendered both opaque
  headless). Show enclosure via WIREFRAME stone cages (`Mesh.TopologyEdges` -> lines) + solid blocks.
- `IsPointInside` is ~1 us in C# (run_csharp) vs slow CPython interop - do heavy point-in-mesh in C#.
- GhPython `ZuiPythonComponent.Code` is settable via SDK; RhinoCodePluginGH `Python3Component` is not.
- `EmitObject(guid)` works for Frahan + CoACD components but returns null for GhPython (use
  g1_place_component for that). `File3dm.AllLayers.Add` returns void here (layer index 0).
- Spawned slot (aardvark) has `ActiveCanvas == None`; build GH via `GH_Document` directly.

## Slots
- `armadillo` (adopted, user's Rhino, port 10501): has the example geometry baked; loaded GH BEFORE
  the RubblePack deploy so it lacks the new components until restarted.
- `aardvark` (spawned, port 10500): loaded the new .gha, has both RubblePack components; used for
  component verification + ingestion import tests.

## Pending (user's prioritized order)
1. **Scan-to-mesh GH workflow tested + visualized** (user's clarified want): point cloud -> reconstruct
   -> clean mesh, on canvas, PNG. Downsample the 6.86M cloud first.
2. Rubble + ashlar masonry GH examples (Rubble Wall Settle; Ashlar Pack via Slab From Mesh / Quarry
   Decompose), real ETH stones, + PNGs + metrics.
3. Evolved 3D packing + Settle 3D (Bullet) GH example + PNG + metrics.
4. Quarry->slab + block->slab workflow PNGs + metrics.
5. Finish RubblePack demo canvases (evolved fit + multi-bin) on aardvark via GH_Document.
6. LiDAR via laszip; GPR radargram PNG.
