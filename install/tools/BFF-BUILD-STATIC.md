# Rebuilding BFF as a static single-exe (Windows / MSYS2 mingw64)

Produces `bff-command-line.exe` with **no third-party DLL dependencies**
(KERNEL32 + msvcrt only). Verified 2026-06-13: byte-identical UV output to the
upstream dynamic build, ~38 MB, runs in an empty directory.

## Toolchain (MSYS2 mingw64)

```
g++ 14.1.0            C:\msys64\mingw64\bin\g++.exe
static archives in    C:\msys64\mingw64\lib\:
  libcholmod.a libamd.a libcamd.a libccolamd.a libcolamd.a
  libsuitesparseconfig.a libumfpack.a libopenblas.a (bundles LAPACK)
  libgfortran.a libquadmath.a libgomp.a
headers:
  C:\msys64\mingw64\include\suitesparse\   (cholmod.h, umfpack.h, ...)
  C:\msys64\mingw64\include\openblas\       (cblas.h)
```

One-time: `pacman -S --needed mingw-w64-x86_64-gcc-fortran` installs
`libgfortran.a` (OpenBLAS's static LAPACK has ~50 `_gfortran_*` symbol refs).

## Source

```
git clone --depth 1 https://github.com/GeometryCollective/boundary-first-flattening.git bff-src
cd bff-src
git submodule update --init --depth 1 deps/rectangle-bin-pack   # the only non-GUI submodule
```

## Build (one g++ invocation, GUI skipped)

```
export PATH="/c/msys64/mingw64/bin:$PATH"
L=/c/msys64/mingw64/lib
g++ -std=c++11 -O2 -DNDEBUG -D_USE_MATH_DEFINES -include cstdint -include cmath \
  -Iinclude -Ideps/rectangle-bin-pack \
  -I/c/msys64/mingw64/include/suitesparse -I/c/msys64/mingw64/include/openblas \
  deps/rectangle-bin-pack/GuillotineBinPack.cpp \
  deps/rectangle-bin-pack/SkylineBinPack.cpp \
  deps/rectangle-bin-pack/Rect.cpp \
  src/linear-algebra/*.cpp src/mesh/*.cpp src/project/*.cpp \
  apps/command-line/src/CommandLine.cpp \
  -o bff-command-line.exe \
  -static -static-libgcc -static-libstdc++ \
  -Wl,--start-group \
    $L/libumfpack.a $L/libcholmod.a $L/libccolamd.a $L/libcamd.a \
    $L/libcolamd.a $L/libamd.a $L/libsuitesparseconfig.a $L/libopenblas.a \
  -Wl,--end-group \
  -lgfortran -lquadmath -lgomp -lpthread
```

Notes / gotchas:
- `-include cstdint -include cmath -D_USE_MATH_DEFINES` works around GCC 14's
  stricter transitive includes (`uint8_t`, `M_PI`) without patching BFF source.
- `cblas.h` lives under `include/openblas/`, not the include root.
- `-Wl,--start-group ... --end-group` resolves the circular SuiteSparse↔BLAS
  static refs; link order otherwise matters (dependents before dependencies).
- `-static` folds in libstdc++ / libgcc / winpthread / quadmath; OpenBLAS's
  Fortran symbols need `-lgfortran`.

## Verify

```
objdump -p bff-command-line.exe | grep -i "DLL Name"   # only KERNEL32 + msvcrt
# run in an empty dir on a disk-topology .obj; expect a UV-bearing output .obj
./bff-command-line.exe disk.obj out.obj --normalizeUVs
```
