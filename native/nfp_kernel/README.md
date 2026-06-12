# nfp_kernel — native batched No-Fit-Polygon kernel

Native C++ kernel behind `Frahan.Packing.TwoD.NativeNfpKernel`
(P/Invoke). Replaces the managed Clipper2 Minkowski-sum loop in
`ContactNfpHoleNester.TryPlaceOuter` with ONE batched call per part
(all budgeted rotations x all current obstacles). Profiling showed
managed NFP builds were ~95% of the general-engine solve; measured
interleaved A/B on the 7-shield bench: ~8x wall-time (see below).

## Contents

| Path | What |
|---|---|
| `nfp_kernel_capi.h` | C API: one export, `nfp_batch` (full contract in the header) |
| `nfp_kernel.cpp` | Kernel: rotate/reflect + RDP simplify + Int64 Minkowski NFP, deterministic thread pool over angles |
| `clipper2/` | Vendored official Clipper2 C++ (Angus Johnson, BSL-1.0), tag `Clipper2_2.0.1`, unmodified, with upstream `LICENSE` |
| `test_nfp_kernel.c` | Smoke test (LoadLibrary): hand-checked NFP areas/bboxes, capacity contract, determinism |
| `build_native.ps1` | Build script (MSVC if on PATH, else the proven llvm-mingw) |

## Semantics

For each angle `a`: `R = rotate(part, a)`, `refl = -R`; optional
RDP simplification of `refl` and each obstacle (`simplifyTol < 0`
means relative: `|tol| x bbox-diagonal` per shape — pass `-2e-3` to
mirror the managed nester's `NfpSimplifyTol`); then
`NFP(obst, part@a) = MinkowskiSum(obst, refl)` NonZero-unioned on
Clipper2's exact Int64 lane (`scale = 100.0` mirrors the managed
PathD lane at decimal precision 2). Loops return flat with
`(angleIdx, obstIdx)` tags. Caller-allocated buffers only; on
return code 1 the required sizes are reported and the caller
retries. Deterministic, independent of thread count.

## Build

```powershell
# from this directory
./build_native.ps1
```

Probe order: `cl.exe` on PATH, else llvm-mingw at
`D:\ref-20260610T154710Z-3-001\ref\tools\llvm-mingw\llvm-mingw-20260602-ucrt-x86_64\bin`.
Manual llvm-mingw command:

```
x86_64-w64-mingw32-g++ -O2 -shared -static -DNDEBUG -I clipper2/include \
    nfp_kernel.cpp clipper2/src/clipper.engine.cpp -o nfp_kernel.dll
x86_64-w64-mingw32-gcc -O2 test_nfp_kernel.c -o test_nfp_kernel.exe
./test_nfp_kernel.exe   # must print SMOKE TEST PASSED
```

Output is a self-contained x64 `nfp_kernel.dll` (statically linked
runtime, no mingw DLL dependencies).

## Deploy

The dll must sit beside the consuming managed assemblies:

- headless tests: `tests/Frahan.StonePack.Tests/bin/Release/net48/nfp_kernel.dll`
- Grasshopper canvas: beside the `.gha` in
  `%APPDATA%/Grasshopper/Libraries/Frahan.StonePack.MeshHeightmap/`
  (same pattern as `frahan_cgal.dll` / `libbulletc.dll`)

When the dll is absent the managed Clipper2 lane runs verbatim — no
behavior change. `FRAHAN_NFP_NATIVE=0` force-disables the native
lane even when the dll is present (benchmark A/B + emergency opt-out).
`Result.Note` carries `+native-nfp` when the native lane ran.

## Measured (2026-06-12, this machine)

Interleaved A/B (4 alternating managed/native processes, 5 packs
each, medians), 7-shield bench (`Cnh_Shields_NativeKernel_Bench`):

- managed medians: 3194 / 3213 / 3221 / 3094 ms -> 3203 ms
- native medians: 412 / 385 / 416 / 392 ms -> 402 ms
- **multiplier: ~8.0x** (per-pair 7.7-8.4x); placed/valid identical
  (7/7 valid both lanes)

Tiny all-rect instance (true-hole bench, general engine forced,
2 interleaved pairs): native at parity or slightly faster
(102/88 ms vs 119/97 ms) after the small-batch single-thread
threshold (< 4000 swept quads).
