# 22 — Frahan CoACD native shim build setup (Windows)

**Date:** 2026-05-09
**Outcome:** `frahan_coacd.dll` built successfully against CoACD 1.0.11 in lightweight mode (`FRAHAN_COACD_WITH_3RD_PARTY=OFF`), deployed alongside `Frahan.StonePack.gha`. The `Mesh Decompose (CoACD)` Grasshopper component is wired and shipping in `Frahan.StonePack.gha`.

## Purpose

Document the build, deploy, and use procedure for `native/coacd_shim/frahan_coacd.dll`, the C-ABI shim around SarahWeiii/CoACD ("Approximate Convex Decomposition for 3D Meshes with Collision-Aware Concavity and Tree Search", SIGGRAPH 2022). Future agents and future-Libish should be able to reproduce this build in under 15 minutes (lightweight mode) by following these steps.

CoACD splits a non-convex mesh into a small set of nearly-convex sub-meshes whose union approximates the input. Used for collision-detection acceleration, robotics physics pre-pass, and as a per-piece pre-stage to masonry packing on irregular scanned blocks.

## Confirmed environment

- Windows 11
- Visual Studio 2022 Community with **Desktop development with C++** workload
- CMake 4.3.2 (standalone, at `C:\Program Files\CMake\bin\cmake.exe`)
- Network access during first configure (CMake `FetchContent` pulls CoACD from GitHub)
- No vcpkg required — CoACD vendors all its dependencies

The lightweight build does NOT require Boost, Eigen, OpenVDB, GMP, or MPFR. CoACD's vendored libs (libigl, btConvexHullComputer, sobol, etc.) compile from source as part of CoACD's own CMake project and link statically into our shim.

## Source acquisition

Two paths:

1. **Default — FetchContent at configure time** (network required, first run only). CMake clones CoACD upstream, recursively initialises submodules, pins to tag `1.0.11`. Subsequent reconfigures reuse the cached source.

2. **Local checkout** — pass `-DCOACD_ROOT=<path>` if you already have CoACD cloned locally. Faster, fully offline.

Tags are unprefixed: `1.0.11` not `v1.0.11`. Override with `-DFRAHAN_COACD_TAG=<tag>`.

## Build commands (lightweight, no preprocess)

```powershell
$env:PATH = "C:\Program Files\CMake\bin;" + $env:PATH
cd D:\code_ws\Template-General\outputs\2026-05-01\frahan_stonepack\native\coacd_shim
.\build_native.ps1 -WithoutThirdParty
```

What this does:

1. Configures with `-DFRAHAN_COACD_WITH_3RD_PARTY=OFF`. CoACD's heavy preprocess pipeline (OpenVDB-based manifold-isation) is skipped.
2. Builds Release x64 via MSBuild.
3. Copies `build\Release\frahan_coacd.dll` to `%APPDATA%\Grasshopper\Libraries\Frahan.StonePack.MeshHeightmap\`.

Expected first-build wall-clock: ~5–10 minutes on a developer laptop. Subsequent rebuilds of just the shim layer: seconds.

## Build commands (full, with OpenVDB preprocess)

When inputs may be non-manifold (e.g. raw scan data), build with the full 3rd-party stack:

```powershell
.\build_native.ps1
```

This is the default — drops the `-WithoutThirdParty` flag, defines `WITH_3RD_PARTY_LIBS=ON` for CoACD. Pulls in OpenVDB + boost + spdlog + zlib via CoACD's `3rd/` dir. **Budget 30–60 minutes** for the first build. Subsequent shim-layer rebuilds remain fast.

## Verifying the deploy

```powershell
Get-ChildItem "$env:APPDATA\Grasshopper\Libraries\Frahan.StonePack.MeshHeightmap\frahan_*.dll" |
    Format-Table Name, @{n="MB";e={[math]::Round($_.Length/1MB,2)}}, LastWriteTime -AutoSize
```

Expect:

```
Name                 MB LastWriteTime
----                 -- -------------
frahan_cgal.dll    1.92 2026-05-08 ...
frahan_coacd.dll   1.77 2026-05-09 ...
```

Lightweight `frahan_coacd.dll` is ~1.8 MB. Full build with OpenVDB linked in statically lands at ~5.5 MB (verified 5.51 MB on the 2026-05-09 build). Smaller than expected because CoACD only uses a subset of OpenVDB's surface, and dead-code elimination drops the rest.

## Pitfalls encountered (and fixed)

These are pre-emptive fixes already baked into `coacd_shim/CMakeLists.txt`. Documented here so future debugging is faster.

### B1. CMake 4.x rejects CoACD's vendored zlib

CoACD's `3rd/zlib/CMakeLists.txt` calls `cmake_minimum_required(VERSION 2.4.4)`. CMake 4.x refuses anything below 3.5.

Fix: `set(CMAKE_POLICY_VERSION_MINIMUM 3.5 CACHE STRING ... FORCE)` set BEFORE `project()` propagates to every nested CMakeLists, including FetchContent'd deps.

### B2. MSVC runtime mismatch (only with `WITH_3RD_PARTY_LIBS=ON`)

When OpenVDB is enabled, CoACD's `openvdb.cmake` sets `USE_STATIC_DEPENDENCIES=ON`, which forces `/MT` (static MSVC runtime). Our shim uses `/MD` (dynamic) to match Rhino. Linking the two together produces `LNK2038`.

Fix: `set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreadedDLL" CACHE STRING "" FORCE)` before `project()`. The `FORCE` is required because CoACD's openvdb.cmake otherwise overrides it.

### B3. CoACD's `Config.cmake.in` template expected at our project root

When CoACD is consumed via `add_subdirectory` / `FetchContent` (rather than as the top-level project), its `configure_package_config_file` call resolves paths against OUR project root, not CoACD's. The template must therefore exist at `coacd_shim/cmake/Config.cmake.in`. We mirror CoACD's upstream template — the install path is never invoked because we link `coacd` static into our shared lib instead of installing CoACD.

### B4. `pwsh.exe` not on PATH (deploy-step false alarm)

The original `build_native.ps1` invoked `pwsh.exe` for the deploy copy. Older Windows boxes only have Windows PowerShell 5.1 (`powershell.exe`); the build itself succeeds but the deploy command fails with `'pwsh.exe' is not recognized`. **The DLL is still produced** at `build\Release\frahan_coacd.dll`; manual `Copy-Item` to the Grasshopper Libraries dir completes the deploy.

Two recommended fixes:

1. **Install PowerShell 7** so `pwsh.exe` resolves on PATH:
   ```powershell
   winget install --id Microsoft.PowerShell --silent --accept-package-agreements --accept-source-agreements
   ```
   On the 2026-05-09 install run, this dropped `pwsh.exe` at `%LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe` (PowerShell 7.6.1, MSIX user install). That dir is in `%PATH%` already, but the running shell needs to be restarted (or pick up `[Environment]::GetEnvironmentVariable("Path","User")` manually) for the binding to refresh.
2. The script has since been updated to use `Copy-Item` directly so it no longer depends on `pwsh.exe`. If you see the error in old logs, the DLL is fine; run the manual copy step from BUILD.md.

## Output binary identity

Reported version string from the binary:

```
Frahan-CoACD 0.1 (CoACD 1.0.11)
```

(Read from C# via `CoacdMeshDecompose.Version` after `IsAvailable` evaluates true.)

Runtime dependencies (per `dumpbin /dependents`, lightweight build):

- Windows system DLLs only (KERNEL32, MSVCP140, VCRUNTIME140, ucrtbase). CoACD's static lib is linked in at build time.

## Wiring into Grasshopper

The managed front-end is `src/Frahan.StonePack.Core/Masonry/Geometry/CoacdMeshDecompose.cs`. The Grasshopper component is `src/Frahan.StonePack.GH/CoacdTestComponents.cs` → `CoacdMeshDecomposeComponent`.

On the canvas:

```
Frahan tab > CoACD subcategory > Mesh Decompose (CoACD)
```

Inputs: Mesh, Threshold (concavity), Real Metric, Preprocess Mode, Sample Resolution, MCTS Nodes / Iters / Depth, PCA, Merge, Max Pieces, Seed, Run.

Outputs: Pieces (list of meshes), Count, Available, Version, Report.

When the lightweight (no-preprocess) shim is loaded, **inputs MUST be 2-manifold**. CoACD will throw `runtime_error("The mesh is not a 2-manifold!")` on bad input; the component's exception handler surfaces the message in red. Pre-clean noisy / scan-derived input through `Mesh Repair (CGAL)` upstream.

## Tunables — quick reference

| Param                    | Default | Meaning |
|---|---:|---|
| `Threshold`              | 0.05  | concavity threshold (lower = more pieces, tighter fit) |
| `Real Metric`            | false | when true, threshold is in metres (CoACD `-rm`, v1.0.11+) |
| `Preprocess Mode`        | 0     | 0=auto, 1=on, 2=off (lightweight build treats `on` as no-op) |
| `Sample Resolution`      | 2000  | concavity sampling grid |
| `MCTS Nodes`             | 20    | nodes per cut |
| `MCTS Iters`             | 150   | iterations per cut |
| `MCTS Depth`             | 3     | tree depth |
| `PCA`                    | false | align cuts to PCA frame |
| `Merge`                  | true  | post-merge pieces where merging stays convex |
| `Max Pieces`             | -1    | piece-count cap (-1 = unlimited) |
| `Seed`                   | 0     | RNG seed |

For statue-scale architectural input, set `Real Metric = true` and pass `Threshold` in metres. This matches the CGAL HYBRID + repair pipeline already used elsewhere in the kit.

## License

CoACD is MIT-licensed. The shim DLL ships freely inside / alongside the .gha. CoACD vendors libigl + smaller utilities under permissive licenses; the **full** build with `WITH_3RD_PARTY_LIBS=ON` additionally pulls OpenVDB (Apache-2.0) and other deps — review their licenses if you redistribute the heavy build.

## Cross-references

- `native/coacd_shim/BUILD.md` — original build instructions (mirrored here)
- `src/Frahan.StonePack.Core/Masonry/Geometry/CoacdMeshDecompose.cs` — managed front-end
- `src/Frahan.StonePack.GH/CoacdTestComponents.cs` — Grasshopper component
- `wiki/specs/21_frahan_cgal_build_setup.md` — sister CGAL build setup (analogous structure)

## What to do next to advance the stability counter

This deploy sets `stability_counter = 1` for `coacd_shim`. To reach the algorithm-checkpoint tag (`coacd-shim-v1-validated`, counter = 3) the following passes are needed:

1. **Pass #1**: Decompose a known clean masonry block (e.g. a scanned arch voussoir) on the canvas. Confirm output piece count is reasonable (3–10 pieces typically), inspect each piece visually, confirm union approximates the input. Log in `wiki/algorithms/coacd_shim/validation_log.md`.
2. **Pass #2**: Throw a known-fragile fixture at it — a non-convex, deeply concave mesh. Confirm CoACD finds tighter cuts than a single convex hull would. Compare to a manual decomposition baseline.
3. **Pass #3**: Stress test with a real production-scale boulder mesh (10k+ triangles) against the new `Quarry Decompose By Mesh (CGAL)` for cross-validation: do the two decomposition methods give complementary results?

Each pass appends an entry to the validation log and triggers a new `coacd-shim-hitl-N` tag + checkpoint per the AGENTS.md cadence.
