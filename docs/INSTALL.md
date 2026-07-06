# Install

Style: short sentences, no em dashes. Windows + Rhino 8.

## For users (no build needed)

- **Rhino Package Manager (recommended):** in Rhino 8 run `_PackageManager`,
  search **FrahanStonePack**, click Install, restart Rhino. Grasshopper gains
  the `Frahan` ribbon tab. Updates arrive through the same dialog.
- **Offline zip:** download the installation zip from
  [Food4Rhino](https://www.food4rhino.com/) or the
  [GitHub releases](https://github.com/libishm1/Frahan/releases). Close Rhino,
  extract, and copy the whole `Frahan.StonePack` folder into
  `%AppData%\Grasshopper\Libraries\`. Keep every DLL in the same folder as
  `Frahan.StonePack.gha`.
- **Examples:** the starter set ships on Food4Rhino; the full 50-example set
  is in [`examples/`](../examples/README.md). Open any `.gh` after installing.

## Developer build (from source)

### Prerequisites
- Rhino 8 (Windows), installed at the default `C:\Program Files\Rhino 8\`. The csproj reference RhinoCommon
  via HintPath to `C:\Program Files\Rhino 8\System\RhinoCommon.dll` with `Private=false`.
- .NET SDK (dotnet on PATH). Projects target net48; `Directory.Build.props` sets LangVersion 10 + Nullable.
- For the headless harness only: Rhino.Inside 8.0.7-beta (fetched once into the global NuGet packages folder
  via `dotnet add package Rhino.Inside --version 8.0.7-beta`), referenced by HintPath to the resolver DLL.
- For native geometry (CGAL/geogram/CoACD) + Bullet physics: the native libs (see `docs/NATIVE.md`, staged)
  are not vendored in the repo; a fetch/build script provides them. Pure-managed paths work without them.

### Build
- Plugin: `dotnet build src/Frahan.StonePack.GH/Frahan.StonePack.GH.csproj -c Release` -> `Frahan.StonePack.gha`.
- Whole solution: `dotnet build Frahan.StonePack.sln -c Release`.
- Headless packer harness: `dotnet build tools/Frahan.StonePack.Harness/Frahan.StonePack.Harness.csproj -c Release`.

### Deploy (Rhino CLOSED)
1. Close Rhino completely.
2. File-copy `Frahan.StonePack.gha` + `Frahan.StonePack.Core.dll` (+ `frahan_e57_worker.py` + native libs)
   into `%APPDATA%\Grasshopper\Libraries\`. There is exactly ONE `.gha`. Never build into the live folder.
3. Reopen Rhino + Grasshopper. The `Frahan` ribbon tab appears.

### Test + benchmark
- Test suite (xUnit-style runner; 1056 PASS / 0 FAIL / 154 SKIP headless with `FRAHAN_SKIP_NATIVE=1`,
  2026-07-05): `dotnet run -c Release` in `tests/Frahan.StonePack.Tests`.
  Rhino-dependent tests skip gracefully when RhinoCommon native (rhcommon_c) cannot load (bitness); the
  pure-managed + Core tests always run.
- Packer benches: `tools/Frahan.StonePack.Harness --packbench` (3D + masonry + fracture) and
  `--pack2dstudy` (full 2D study with util_stock + the 80% bar). These boot Rhino.Inside headless.

### Gotchas
- ONE `.gha`; CopyLocal=false on RhinoCommon (Rhino.Inside resolves satellites from the Rhino System folder).
- In-process CGAL/geogram BOOLEAN can crash Rhino: route heavy boolean/recon through the out-of-process
  worker. See `handoffs/KNOWN_BUGS.md`.
- Never internalize a multi-million-vertex mesh in a saved `.gh` (autosave crash, KB-1).
