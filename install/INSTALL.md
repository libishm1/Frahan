# Install - Frahan StonePack (Rhino 8, Windows)

Everything needed to run the plugin and the `examples/` is bundled here. No build step required for users.
Style: short sentences, no em dashes.

## One-shot deploy
1. CLOSE Rhino (the `.gha` is locked while Rhino runs; file-copy deploy needs it closed).
2. Fetch the large binaries (stored via Git LFS): `git lfs pull`.
3. Run the deploy script:
   - PowerShell: `pwsh -File install\deploy.ps1`
   - git-bash:   `bash install/deploy.sh`
4. Start Rhino + Grasshopper. The `Frahan` ribbon tab holds the component families.
5. Open any `examples/<name>/<name>.gh` on the bundled data and toggle `Run`.

The script copies everything below into `%APPDATA%\Grasshopper\Libraries\`.

## What is in this folder
- `plugin/` - the deployable plugin set. Copy ALL of these into the Libraries folder together:
  - `Frahan.StonePack.gha` - the one and only `.gha` (all components).
  - `Frahan.StonePack.dll`, `Frahan.StonePack.Core.dll`, `Frahan.EdgeMatching.Core.dll`,
    `Frahan.Kintsugi.Port.dll` - managed dependencies.
  - `frahan_cgal.dll`, `frahan_coacd.dll`, `frahan_geogram.dll`, `gmp-10.dll`,
    `frahan_recon_worker.exe` - native shims (CGAL segmentation/boolean/repair, CoACD, geogram
    reconstruction, GMP). Out-of-process recon worker (in-process CGAL/geogram boolean can crash Rhino).
  - `Clipper2Lib.dll` - 2D polygon/NFP back-end for the nesters.
- `weights/kintsugi.bin` (~255 MB, LFS) - PuzzleFusion++ port weights. Required ONLY for
  `Frahan Kintsugi` with `Use Port Mode = True`. Verified working: the example 14 parity run scored
  0.71 STRONG on `bb_sample_00697`. Without it, use the geometric Kintsugi path on clean-rim data.
- `data/` - Kintsugi Breaking Bad parity samples (`bb_sample_*.bin`) consumed by `Load BB Sample`.
  Also copied under `examples/14_kintsugi/data/`.
- `tools/bff-command-line.exe` (LFS) - Boundary-First Flattening, for `Surface Chart`. The BFF path input
  on Surface Chart is OPTIONAL; provide this exe path for distortion-free 2D->3D surface charts.

## Which examples need what
- `11_pack3d`, `12_trencadis`, `13_surface_mapping` - need only `plugin/` (CGAL native libs for 13).
- `14_kintsugi` (Port mode) - needs `plugin/` + `weights/kintsugi.bin` + `data/bb_sample_*.bin`.
- `10_pack2d` - needs `plugin/`. SEE the 2D caveat below.

## Known caveat (KB-7): 2D NFP nesters need a current-source build
The bundled `Frahan.StonePack.gha` here is a snapshot. The 2D no-fit-polygon nesters (V506 / exact-NFP)
in this snapshot can overlap parts on the canvas (the source is fixed and headless-validated 0-overlap;
the deployed binary lagged). For a guaranteed-correct live 2D pack, rebuild the `.gha` from current source:
`dotnet build src/Frahan.StonePack.GH/Frahan.StonePack.GH.csproj -c Release` and replace `plugin/*.gha`
+ `plugin/Frahan.StonePack*.dll`. The `examples/10_pack2d/` result artifacts are from current source.
See `../handoffs/KNOWN_BUGS.md` KB-7. 3D packing, Trencadis, surface segmentation, and Kintsugi are
unaffected and run correctly from the bundled binaries.

## Developer build (from source)
See `../docs/INSTALL.md` for the full toolchain (dotnet, RhinoCommon HintPath, the headless `tools/`
harness). Build: `dotnet build src/Frahan.StonePack.GH/Frahan.StonePack.GH.csproj -c Release`, then
redeploy via `deploy.ps1` pointed at your build output, or copy the built `.gha` over `plugin/`.
