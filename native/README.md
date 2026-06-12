# native/ — C ABI shims for the Frahan StonePack plugin

This folder holds the full C++ source for the native shims that
the managed plugin calls via P/Invoke. It exists for two reasons:

1. **GPL corresponding source.** The repo ships prebuilt shim binaries
   in `install/plugin/`. The root `LICENSE` is GPL-3.0, so the source
   that produced those binaries must travel with the distribution.
   These folders are that source.
2. **Reproducibility.** Anyone can rebuild the DLLs from these sources
   plus the pinned upstream dependencies. No private build tree is
   required.

The shims were written for this project. They are original Frahan work
that wraps third-party geometry libraries behind a stable C ABI; the
wrapped libraries (CGAL, Geogram, CoACD) remain the work of their
respective authors and are credited below.

## What each shim wraps

| Folder | Output DLL | Wraps | Upstream licence |
|---|---|---|---|
| `cgal_shim/` | `frahan_cgal.dll` | CGAL, primarily Polygon Mesh Processing (PMP) corefinement booleans (union / intersection / difference), plus repair, edge-collapse decimation, OBB, 2D straight skeleton, polygon partition, SDF and angle segmentation, geodesic Voronoi, alpha shape, advancing front, Poisson reconstruction, normal estimation. | CGAL open-source distribution. The packages this shim uses (PMP, `Surface_mesh`, surface-mesh simplification, reconstruction) are **GPL** in that distribution. |
| `geogram_shim/` | `frahan_geogram.dll` | Bruno Levy's Geogram (Inria / ALICE): vertex-clustering decimation, repair, hole filling, OBB, uniform remeshing, tetrahedralisation, CVT / Lloyd, restricted Voronoi diagrams (RVD), volumetric Voronoi blocks, voxel downsampling, kd-tree queries, and **Poisson surface reconstruction via the Kazhdan PoissonRecon code that Geogram bundles** (`GEO::PoissonReconstruction`). | Geogram core is BSD-3. The bundled PoissonRecon is MIT (Kazhdan). TetGen, which the volumetric-blocks entry point needs, is **AGPL**; the CMakeLists enables it by default and documents `-DFRAHAN_WITH_TETGEN=OFF` for an AGPL-free build. Triangle is kept OFF. |
| `coacd_shim/` | `frahan_coacd.dll` | SarahWeiii/CoACD approximate convex decomposition (Wei et al., SIGGRAPH 2022), tag pin `1.0.11`. One decompose entry point with the full tunable set (threshold, MCTS, merge, real_metric, ...). | CoACD is MIT. CoACD internally vendors some CGAL components (GPL) plus boost / openvdb / spdlog / zlib under its `3rd/` folder. |
| `nfp_kernel/` | `nfp_kernel.dll` | Batched No-Fit-Polygon builds (Minkowski sum + NonZero union on the exact Int64 lane) for the 2D hole-aware nester (`ContactNfpHoleNester`); one P/Invoke per part covers all rotations x all obstacles. Vendors official Clipper2 C++ at tag `Clipper2_2.0.1` unmodified. Builds with llvm-mingw or MSVC, no vcpkg (see `nfp_kernel/README.md`). | Clipper2 is BSL-1.0 (no copyleft). |

All three shims share the same Frahan flat-array contract: vertices as
`3*N` doubles, triangles as `3*T` int32s, outputs `malloc`'d by the
library and released through the shim's `free_*` exports, `extern "C"`
cdecl entry points, no C++ exceptions across the boundary (negative
return codes plus a `*_last_error` string). See each folder's
`BUILD.md` for the per-shim ABI details.

## Runtime dependency DLLs

`frahan_cgal.dll` depends on GMP. The shipped `install/plugin/` folder
therefore also carries `gmp-10.dll`. When you rebuild the CGAL shim
yourself, vcpkg's applocal step copies `gmp-10.dll` (and `mpfr-6.dll`
when the build links MPFR) next to the freshly built DLL in
`build/Release/`; deploy whichever of those your build produced
alongside the shim.

## Where the binaries ship

- In-repo: `install/plugin/` holds the prebuilt `frahan_cgal.dll`,
  `frahan_geogram.dll`, `frahan_coacd.dll`, and `gmp-10.dll` next to
  the `.gha`.
- On a user machine: `install/deploy.ps1` (or `deploy.sh`) copies the
  whole `install/plugin/` folder flat into
  `%APPDATA%/Grasshopper/Libraries/`.
- Note: each shim's own `build_native.ps1` has a convenience deploy
  step that targets the legacy
  `%APPDATA%/Grasshopper/Libraries/Frahan.StonePack.MeshHeightmap/`
  subfolder from the original workspace layout. Use `-NoDeploy` and
  copy by hand (or run `install/deploy.ps1`) if you deploy flat.

The shims are optional at runtime. When a DLL is absent the managed
wrappers report unavailable and the code falls back where a fallback
exists (e.g. `CgalMeshBoolean` falls back to the in-tree BSP CSG
kernel `MeshCsg`).

## How to build

Each folder has `CMakeLists.txt`, `BUILD.md` (Windows / Linux / macOS
instructions), and one-shot scripts `build_native.ps1` /
`build_native.sh`. Summary of what the scripts actually do:

- **cgal_shim**: requires Visual Studio 2022 + vcpkg with
  `cgal:x64-windows` installed (Eigen3 optional; without it the OBB
  entry point is omitted). `build_native.ps1` locates vcpkg
  (`$env:VCPKG_ROOT`, `C:\vcpkg`, or `D:\vcpkg`), configures with the
  vcpkg toolchain file, builds Release x64, then deploys unless
  `-NoDeploy`.
- **geogram_shim**: requires Visual Studio 2022, git, CMake 3.24+.
  No vcpkg. CMake FetchContent pulls Geogram at the pinned tag
  `v1.9.9` on first configure (network needed once), or pass
  `-GeogramRoot <path>` for a local checkout. Unneeded Geogram
  options (graphics, Lua, HLBFGS, Triangle, ...) are forced OFF;
  TetGen is ON by default for volumetric Voronoi blocks (AGPL, see
  licence note).
- **coacd_shim**: requires Visual Studio 2022, git, CMake 3.24+.
  FetchContent pulls CoACD at the pinned tag `1.0.11` (tags are
  unprefixed upstream), or pass `-CoacdRoot <path>`. The default
  build compiles CoACD's vendored `3rd/` tree (OpenVDB etc.; budget
  30-60 minutes on a fresh machine). `-WithoutThirdParty` skips the
  preprocess stack but then CoACD rejects non-manifold input.

Linux / macOS builds go through `build_native.sh` with system
packages (`libcgal-dev`, `cmake`, `g++` / brew equivalents); outputs
are `libfrahan_*.so` / `.dylib`.

All three CMake files are present in this tree. Build artifact folders
(`build/`, `build-with3rd/`) are intentionally not vendored.

## Licence note

The root `LICENSE` (GPL-3.0) and its third-party note govern this
distribution. Specifics for this folder:

- The CGAL packages used by `cgal_shim` are GPL in CGAL's open-source
  distribution (commercial licences exist via GeometryFactory). Any
  distribution that bundles `frahan_cgal.dll` is therefore GPL-bound;
  **the CGAL GPL packages block relicensing this repo to MIT** even
  independently of `Frahan.Kintsugi.Port`. The historical alternative
  is to treat the CGAL shim as a separate user-installed component.
- `frahan_geogram.dll` built with the default `FRAHAN_WITH_TETGEN=ON`
  includes TetGen, which is AGPL. Set `-DFRAHAN_WITH_TETGEN=OFF` for
  an AGPL-free binary (volumetric Voronoi blocks then return an
  error; the surface RVD path keeps working).
- Geogram itself is BSD-3, the bundled Kazhdan PoissonRecon is MIT,
  and CoACD is MIT. These do not restrict the GPL distribution.

## Not in this folder

`install/plugin/frahan_recon_worker.exe` is a small managed
out-of-process worker (used by `OutOfProcessReconstructor`), not a
native shim. Its C# source lives in the original workspace snapshot
(`Template-General/outputs/2026-05-01/frahan_stonepack/native/recon_worker/`)
and is not vendored here.
