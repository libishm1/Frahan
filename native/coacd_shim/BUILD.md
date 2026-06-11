# frahan_coacd — build instructions

C ABI shim that wraps SarahWeiii/CoACD for use from .NET via P/Invoke.
Output is a single shared library (`frahan_coacd.dll` on Windows,
`libfrahan_coacd.so` on Linux, `libfrahan_coacd.dylib` on macOS) that the
managed wrapper auto-detects at runtime.

When the DLL is absent, the managed code can fall through to a simpler
in-tree decomposition (or skip the decomposition step). The shim is
optional; only build it when CoACD's tree-search decomposition is wanted.

Pattern matches `native/cgal_shim/`. Same Frahan flat-array contract.

## Source acquisition

CoACD is fetched at configure time (network required on first run) OR
sourced from a local checkout passed via `-DCOACD_ROOT=<path>`.

Tag pin: `1.0.11` (latest verified release). Tags are UNPREFIXED — use
`1.0.11`, not `v1.0.11`. Override with `-DFRAHAN_COACD_TAG=<tag>`.

CoACD upstream: https://github.com/SarahWeiii/CoACD
Paper: Wei et al., "Approximate Convex Decomposition for 3D Meshes with
Collision-Aware Concavity and Tree Search", SIGGRAPH 2022.

## Windows (vcpkg / system toolchain)

```powershell
cd <repo-root>\native\coacd_shim

# Option A: let CMake fetch CoACD on first configure (needs network).
.\build_native.ps1

# Option B: point at a local CoACD checkout.
.\build_native.ps1 -CoacdRoot D:\src\CoACD

# Output: build\Release\frahan_coacd.dll
# Deploy alongside Frahan.StonePack.gha:
copy build\Release\frahan_coacd.dll `
    "$env:APPDATA\Grasshopper\Libraries\Frahan.StonePack.MeshHeightmap\"
```

CoACD vendors its 3rd-party sources under `3rd/` (boost, openvdb,
spdlog, zlib, plus smaller utilities) — **no vcpkg required**, but the
first build is heavy (budget 30–60 minutes for OpenVDB on a fresh
machine). Subsequent rebuilds of just the shim layer are seconds.

### Lightweight build (no preprocess)

Pass `-DFRAHAN_COACD_WITH_3RD_PARTY=OFF` to skip OpenVDB and friends.
CoACD will refuse non-manifold inputs at decompose time
(`runtime_error("The mesh is not a 2-manifold!")`). Use this mode when
you can guarantee clean input meshes (e.g. you already pre-sanitised
through `frahan_cgal_repair_mesh`).

```powershell
.\build_native.ps1 -WithoutThirdParty
```

```bash
FRAHAN_COACD_WITH_3RD_PARTY=OFF ./build_native.sh
```

## Linux

```bash
sudo apt install cmake g++ git

cd native/coacd_shim
./build_native.sh
# Output: build/libfrahan_coacd.so
```

## macOS

```bash
brew install cmake git

cd native/coacd_shim
./build_native.sh
# Output: build/libfrahan_coacd.dylib
```

## ABI / lifetime contract

* Inputs: vertices = `3 * N` doubles, triangles = `3 * T` int32s. Same
  convention as `MeshSnapshot` and `frahan_cgal`.
* Output is N convex pieces, returned as concatenated vertex / triangle
  buffers plus per-part start arrays (same layout as
  `frahan_cgal_polygon_partition_2d`):
    - `out_vert_starts[i]` is the first vertex of piece i in `out_verts`.
    - `out_tri_starts[i]`  is the first triangle of piece i in `out_tris`.
    - Triangle indices are LOCAL to each piece (rooted at 0). Lift each
      piece into its own mesh by slicing the buffers at the start arrays.
* Outputs are `malloc`'d by the library. Release with
  `frahan_coacd_free_pdouble` / `frahan_coacd_free_pint`. The managed
  wrapper handles this automatically.
* All entry points are `extern "C"`, `cdecl`. No C++ exceptions cross
  the boundary; failures return negative codes plus
  `frahan_coacd_last_error`.

## Tunables (decompose entry point)

Pass `-1` (or `0` where appropriate) to use CoACD's defaults:

| Param                    | Default | Meaning |
|---|---:|---|
| `threshold`              | 0.05  | concavity threshold (lower = more pieces) |
| `preprocess_mode`        | 0     | 0=auto, 1=on, 2=off |
| `preprocess_resolution`  | 50    | manifold-isation voxel grid |
| `sample_resolution`      | 2000  | concavity sampling resolution |
| `mcts_nodes`             | 20    | MCTS nodes per cut |
| `mcts_iters`             | 150   | MCTS iterations per cut |
| `mcts_max_depth`         | 3     | MCTS tree depth |
| `pca`                    | 0     | align cuts to PCA frame |
| `merge`                  | 1     | post-merge convex pieces |
| `max_convex_hull`        | -1    | piece-count cap |
| `seed`                   | 0     | RNG seed |
| `real_metric`            | 0     | 1 = treat threshold as metres (CoACD `-rm`, v1.0.11+) |

For statue-scale architectural input, `real_metric=1` with a
`threshold` in metres makes the concavity bound interpretable in
project units (matches the recently-shipped CGAL HYBRID + repair
pipeline that already handles real-world scale).

## License notes

CoACD is MIT-licensed. CoACD's vendored CGAL components remain GPL
internally to CoACD; the same per-distribution caveats that apply to
`cgal_shim` apply here. The recommended path is the same:
ship the shim DLL as a separate user-installed component when GPL
compatibility is a concern.
