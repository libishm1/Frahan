# frahan_geogram - build instructions

C ABI shim that wraps Bruno Levy's Geogram (Inria / ALICE) for use from
.NET via P/Invoke. Output is a single shared library
(`frahan_geogram.dll` on Windows, `libfrahan_geogram.so` on Linux,
`libfrahan_geogram.dylib` on macOS) that the managed wrapper auto-detects
at runtime.

Pattern matches `native/cgal_shim/` and `native/coacd_shim/`. Same
Frahan flat-array contract.

## Why Geogram (vs CGAL)

License: Geogram is **BSD-3 throughout**. CGAL's open-source
distribution carries GPL on the packages we use (PMP, Surface_mesh_
simplification, etc.). Anything we ship inside the binary plugin via
this shim has no GPL ceremony attached.

Future on-ramp: Geogram is the most direct path to **Restricted Voronoi
Diagrams**, **centroidal Voronoi tessellation**, and **surface
remeshing** - the natural toolchain for the masonry block-partition
pipeline (Kao 2022 CRA port).

## v1 entry point

`frahan_geogram_decimate_mesh` - vertex-clustering decimation
(`GEO::mesh_decimate_vertex_clustering`). Voxel-bin clustering: higher
`nb_bins` produces a finer output. Different from CGAL's edge-collapse
decimation; use both depending on the use case.

## Source acquisition

Geogram is fetched at configure time (network required on first run) OR
sourced from a local checkout passed via `-DGEOGRAM_ROOT=<path>`.

Tag pin: `v1.9.9` (latest verified release at time of writing).

## Windows

```powershell
cd <repo-root>\native\geogram_shim

# Option A: let CMake fetch Geogram on first configure (needs network).
.\build_native.ps1

# Option B: point at a local Geogram checkout.
.\build_native.ps1 -GeogramRoot D:\src\geogram

# Output: build\Release\frahan_geogram.dll
# Deploy alongside Frahan.StonePack.gha:
copy build\Release\frahan_geogram.dll `
    "$env:APPDATA\Grasshopper\Libraries\Frahan.StonePack.MeshHeightmap\"
```

## Linux

```bash
sudo apt install cmake g++ git

cd native/geogram_shim
./build_native.sh
# Output: build/libfrahan_geogram.so
```

## macOS

```bash
brew install cmake git

cd native/geogram_shim
./build_native.sh
# Output: build/libfrahan_geogram.dylib
```

## Build options

The CMakeLists.txt forces every optional Geogram component OFF that we
do not need:

| Option | State | Why |
|---|---|---|
| `GEOGRAM_WITH_GRAPHICS` | OFF | No OpenGL / GLFW dep |
| `GEOGRAM_WITH_LEGACY_NUMERICS` | OFF | Pre-OpenNL stack, unused |
| `GEOGRAM_WITH_HLBFGS` | OFF | Nonlinear solver, decimation does not use it |
| `GEOGRAM_WITH_TETGEN` | OFF | GPL/non-commercial, license cleanliness |
| `GEOGRAM_WITH_TRIANGLE` | OFF | GPL/non-commercial, license cleanliness |
| `GEOGRAM_WITH_LUA` | OFF | Not needed |
| `GEOGRAM_WITH_EXPLORAGRAM` | OFF | Experimental, not needed |
| `GEOGRAM_WITH_GARGANTUA` | OFF | 64-bit indices not needed |
| `GEOGRAM_WITH_TBB` | OFF | Keep simple; can enable later |
| `GEOGRAM_LIB_ONLY` | ON | Skip example programs and viewers |

Override individually via `-DGEOGRAM_WITH_<X>=ON` if a future entry point
needs them.

## ABI / lifetime contract

* Inputs: vertices = `3 * N` doubles, triangles = `3 * T` int32s.
* Outputs `malloc`'d by the library. Release with
  `frahan_geogram_free_pdouble` / `frahan_geogram_free_pint`.
* All entry points `extern "C"`, `cdecl`. SEH (access violation, stack
  overflow) gets converted to negative return codes via the `/EHa` +
  `_set_se_translator` pattern (same as `coacd_shim`).
* `GEO::initialize()` is called once per process under `std::call_once`
  on the first decompose call. No setup required from C# side.

## License notes

Geogram is BSD-3 throughout. The shim's exported surface is therefore
safe to ship inside a binary plugin. The build options above are tuned
to leave out any GPL/non-commercial components (Triangle, TetGen) that
Geogram bundles for completeness.
