# 21 — Frahan CGAL native shim build setup (Windows)

**Date:** 2026-05-08
**Outcome:** `frahan_cgal.dll` built successfully against CGAL 6.1.1 + GMP/MPFR backend, deployed alongside `Frahan.StonePack.gha`. HitL pass #1 logged at `wiki/algorithms/cgal_shim/validation_log.md`.

## Purpose

Document the dependency installation, cmake-gui workflow, and pitfalls encountered while building the `native/cgal_shim/frahan_cgal.dll` against CGAL 6.1.1 on a fresh Windows 11 machine. Future agents and future-Libish should be able to reproduce this build in under 30 minutes by following these steps.

## Confirmed environment

- Windows 11
- Visual Studio 2022 Community with **Desktop development with C++** workload
- CMake 4.3.2 (standalone, on PATH)
- vcpkg installed at `C:\vcpkg`
- CGAL source archive at `C:\dev\CGAL-6.1.1\CGAL-6.1.1\` (note doubled folder from Windows zip-of-zip extraction)
- Boost 1.91.0 MSVC binary install at `C:\dev\boost_1_91_0\`
- Eigen master branch at `C:\dev\eigen-master\eigen-master\`

## Dependency install order

The build needs four C++ libraries resolved before cmake-gui can configure successfully. Install in this order to minimise wasted time on retries.

### 1. CGAL source headers (header-only library, no compile)

Download `CGAL-6.1.1.zip` from https://github.com/CGAL/cgal/releases/tag/v6.1.1. Pick the explicitly-named release asset, NOT "Source code (zip)" — the GitHub auto-generated zip lacks `CGALConfig.cmake` at the root. Unzip to `C:\dev\CGAL-6.1.1\`.

Verify `CGALConfig.cmake` exists at the install root before continuing:

```powershell
Get-ChildItem -Path C:\dev -Filter "CGALConfig.cmake" -Recurse | Select-Object FullName
```

### 2. Boost 1.91.0 MSVC binaries

Download `boost_1_91_0-msvc-14.3-64.exe` from https://sourceforge.net/projects/boost/files/boost-binaries/1.91.0/. The `14.3` matches VS 2022's MSVC toolset; `64` selects x64. Run installer, default destination `C:\dev\boost_1_91_0\`.

After install, verify `BoostConfig.cmake` exists at the expected path:

```
C:\dev\boost_1_91_0\lib64-msvc-14.3\cmake\Boost-1.91.0\BoostConfig.cmake
```

### 3. Eigen (header-only, optional for OBB only)

Download from https://gitlab.com/libeigen/eigen/-/archive/master/eigen-master.zip OR the 3.4.0 release if a stable version is preferred. Unzip to `C:\dev\eigen-master\eigen-master\`. No build step needed — Eigen is fully header-only.

### 4. GMP + MPFR via vcpkg

The CGAL auxiliary archive's prebuilt GMP/MPFR did NOT work — no MSVC-compatible import libraries were present. The clean fix is vcpkg:

```powershell
git clone https://github.com/microsoft/vcpkg C:\vcpkg
C:\vcpkg\bootstrap-vcpkg.bat
C:\vcpkg\vcpkg integrate install
C:\vcpkg\vcpkg install gmp:x64-windows mpfr:x64-windows
```

Compile time: ~5–10 minutes for GMP + MPFR alone (much faster than full CGAL via vcpkg). Output appears under `C:\vcpkg\installed\x64-windows\` with `.lib` and `.dll` files in `lib\` and `bin\` respectively.

## cmake-gui workflow

Source dir: `D:\code_ws\Template-General\outputs\2026-05-01\frahan_stonepack\native\cgal_shim\`
Build dir: same path with `\build` appended.

Generator: **Visual Studio 17 2022**, Platform **x64**, no toolchain file in initial Configure dialog.

Configure failures resolve in this order — each requires a manual entry in cmake-gui, then a re-Configure click:

| Red entry | Solution |
|---|---|
| `CGAL_DIR-NOTFOUND` | Set to `C:/dev/CGAL-6.1.1/CGAL-6.1.1` (use forward slashes) |
| Boost not found | Add Entry: `Boost_DIR` (PATH) = `C:/dev/boost_1_91_0/lib64-msvc-14.3/cmake/Boost-1.91.0` |
| Eigen `static_assert` (OBB) | Add Entry: `EIGEN3_INCLUDE_DIR` (PATH) = `C:/dev/eigen-master/eigen-master` |
| MPFR link errors | Add Entry: `CMAKE_TOOLCHAIN_FILE` (FILEPATH) = `C:/vcpkg/scripts/buildsystems/vcpkg.cmake`, then re-Configure |

Also set: `CGAL_CMAKE_EXACT_NT_BACKEND` = `GMP_BACKEND` for fastest exact arithmetic. (`Boost_backend` was tested but does not eliminate GMP/MPFR linking — the `Cartesian_converter<...,Gmpq,...>` symbols still appear because CGAL's lazy-exact framework references Gmpq directly regardless of backend selection.)

After last Configure ends with "Configuring done" and no red rows: click **Generate**, then **Open Project**.

## Visual Studio build

Solution Configuration: `Release`. Platform: `x64`. F7 to build.

Critical: open the `.sln` from inside Visual Studio 2022, not via double-click. Double-clicking opens with whichever VS is registered as the default `.sln` handler — if VS 2019 wins, the build fails immediately with `MSB8020: build tools for v143 cannot be found`. CMake configured the project for v143 (VS 2022's toolset) but VS 2019 only has v142 by default.

Expected build time: ~70 seconds. The output DLL lands at:

```
build\Release\frahan_cgal.dll
```

## Deployment

`frahan_cgal.dll` requires runtime DLLs alongside it. Use `dumpbin` from a Developer Command Prompt to enumerate:

```cmd
dumpbin /dependents build\Release\frahan_cgal.dll
```

Ignore Windows system DLLs (KERNEL32, MSVCP140, VCRUNTIME140, ucrtbase). Copy the rest from `C:\vcpkg\installed\x64-windows\bin\` to the Grasshopper Libraries folder:

```powershell
$dst = "$env:APPDATA\Grasshopper\Libraries\Frahan.StonePack.MeshHeightmap"
Copy-Item build\Release\frahan_cgal.dll                          $dst
Copy-Item C:\vcpkg\installed\x64-windows\bin\libgmp-10.dll       $dst
Copy-Item C:\vcpkg\installed\x64-windows\bin\libmpfr-6.dll       $dst
```

Restart Rhino. The Mesh CSG (CGAL) Grasshopper component should report `Backend = CGAL`.

## Code-level fixes applied during build

Three source bugs surfaced during the first compile pass — all fixed by Claude Code in commit `07de6ea`:

1. **`FRAHAN_CGAL_BUILDING` macro redefinition** — `frahan_cgal.cpp` had `#define FRAHAN_CGAL_BUILDING` while CMakeLists.txt also set it via `target_compile_definitions`. Removed from .cpp; CMakeLists.txt is now the single source of truth.

2. **`get()` C-linkage on a C++ template** — the HYBRID kernel's `ExactVertexPointMap` struct lived inside `extern "C" { ... }`. Its `friend get()` returns `Epeck::Point_3` (a C++ template) which fails MSVC's C-linkage check. Moved struct into an anonymous namespace OUTSIDE `extern "C"`. The C ABI functions still reference the struct from inside their bodies.

3. **OBB `static_assert` requires Eigen** — CGAL's `oriented_bounding_box` needs Eigen for SVD. Wrapped OBB declaration and implementation with `#ifdef FRAHAN_CGAL_HAVE_EIGEN`. CMakeLists.txt does `find_package(Eigen3 QUIET)` and defines the macro when found. OBB compiles when Eigen is available; other entry points (mesh boolean, straight skeleton, partition) work without it.

## Risks and pitfalls

- **Windows zip-of-zip nesting.** Both CGAL and Eigen extracted into doubled folders (`CGAL-6.1.1\CGAL-6.1.1\` and `eigen-master\eigen-master\`). `CGAL_DIR` and include paths must point at the inner folder, not the outer.

- **GitHub "Source code (zip)" vs named release asset.** CGAL's GitHub-auto-generated zip lacks `CGALConfig.cmake`. Always download the explicitly-named release zip.

- **Visual Studio version conflict.** Multiple VS installations on one machine: CMake configures for whichever toolset it finds (v143 here), but Windows file association may launch a different VS for the `.sln`. Always open from inside the correct VS, not via double-click.

- **Boost toolset must match VS.** `boost_1_91_0-msvc-14.3-64.exe` matches VS 2022. Using `msvc-14.2-64` (VS 2019 build) with v143 produces ABI mismatch crashes at runtime, not link errors.

- **CGAL auxiliary GMP/MPFR may be incomplete.** The 6.1.1 auxiliary archive's `lib/` did not contain MSVC-compatible import libraries. vcpkg is the reliable path.

- **`CGAL_CMAKE_EXACT_NT_BACKEND` does NOT eliminate GMP/MPFR linking.** The setting controls CGAL's default NT, but `Cartesian_converter<...,Gmpq,...>` symbols still appear in compiled code, requiring MPFR at link time. Plan for GMP+MPFR as mandatory dependencies.

- **CGAL 6.x deprecated `CGAL_SetupGMP.cmake` warnings.** Four "Targets may link only to libraries" warnings during Configure when `auxiliary/gmp/` exists but lacks proper libs. Harmless — CMake drops the bad target — but cosmetic noise. Resolve by renaming `auxiliary/gmp/` → `auxiliary/gmp.unused/` after switching to vcpkg.

- **Runtime DLL discovery is silent.** If a runtime DLL (libgmp, libmpfr, Boost runtime) is missing in the Grasshopper Libraries folder, `LoadLibrary` fails silently and `CgalMeshBoolean.IsAvailable` returns `false` with no useful Grasshopper-side error. Diagnose with `ctypes.CDLL(...)` in Rhino Python — Python prints the missing DLL name.

## Open questions

- Should the build_native.ps1 / .sh scripts be updated to detect the GMP/MPFR auxiliary failure path automatically and fall back to vcpkg?
- The `pwsh.exe is not recognized` post-build warning — what step triggers it? Cosmetic only since DLL is already produced, but worth tracing.
- Is there a `find_package(GMP CONFIG)` or `find_package(MPFR CONFIG)` path that bypasses the broken auxiliary detection without requiring vcpkg's toolchain file?

## Related wiki pages

- [`wiki/specs/20_frahan_cgal_audit.md`](20_frahan_cgal_audit.md) — the C ABI surface, exception-safety, GPL footprint, code-level fixes applied.
- [`wiki/specs/19_frahan_source_relocation_plan.md`](19_frahan_source_relocation_plan.md) — when active source moves out of `outputs/2026-05-01/`, the `native/cgal_shim/` tree moves with it; this build doc's paths will need updating in the same commit.
- ``wiki/algorithms/cgal_shim/validation_log.md`` (internal log, not published) — HitL passes against this build configuration.

## Last updated

2026-05-08
