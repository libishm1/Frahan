# frahan_cgal — build instructions

C ABI shim that wraps CGAL Polygon Mesh Processing for use from .NET via
P/Invoke. Output is a single shared library (`frahan_cgal.dll` on Windows,
`libfrahan_cgal.so` on Linux, `libfrahan_cgal.dylib` on macOS) that the
managed `CgalMeshBoolean` wrapper auto-detects at runtime.

When the DLL is absent, the managed code transparently falls back to the
in-tree BSP CSG kernel (`MeshCsg`). The shim is therefore optional — only
build it if you need the exact-arithmetic robustness of CGAL.

## Windows (vcpkg)

```powershell
# One-time vcpkg setup. Skip if you already have vcpkg.
git clone https://github.com/microsoft/vcpkg C:\vcpkg
C:\vcpkg\bootstrap-vcpkg.bat
C:\vcpkg\vcpkg integrate install

# Install CGAL with all needed dependencies.
C:\vcpkg\vcpkg install cgal:x64-windows

# Build the shim.
cd <repo-root>\native\cgal_shim
mkdir build
cd build
cmake -G "Visual Studio 17 2022" -A x64 `
    -DCMAKE_TOOLCHAIN_FILE=C:\vcpkg\scripts\buildsystems\vcpkg.cmake `
    -DVCPKG_TARGET_TRIPLET=x64-windows ..
cmake --build . --config Release

# Output: build\Release\frahan_cgal.dll
# Copy alongside Frahan.StonePack.gha:
copy build\Release\frahan_cgal.dll `
    "$env:APPDATA\Grasshopper\Libraries\Frahan.StonePack.MeshHeightmap\"
```

## Linux (apt + system CGAL)

```bash
sudo apt install libcgal-dev cmake g++

cd native/cgal_shim
mkdir build && cd build
cmake -DCMAKE_BUILD_TYPE=Release ..
cmake --build .
# Output: libfrahan_cgal.so (place on LD_LIBRARY_PATH or alongside the .gha)
```

## macOS (brew)

```bash
brew install cgal cmake

cd native/cgal_shim
mkdir build && cd build
cmake -DCMAKE_BUILD_TYPE=Release ..
cmake --build .
# Output: libfrahan_cgal.dylib
```

## Verifying the shim is loaded

After deploying the DLL, run any GH document that uses CGAL operations. The
managed `CgalMeshBoolean.IsAvailable` static returns `true` once the DLL is
on the search path; `CgalMeshBoolean.Version` returns a string like
`"Frahan-CGAL 0.1 (CGAL 5.6)"`. The `Mesh CSG (CGAL)` Grasshopper component
reports the active back-end in its `Backend` output.

## ABI / lifetime contract

* Inputs are flat arrays: vertices = `3 * N` doubles, triangles = `3 * T`
  int32s. Same convention as `MeshSnapshot` elsewhere in Frahan.
* Outputs are allocated by the library via `malloc`. Callers must release
  via `frahan_cgal_free_buffers` before the buffers go out of scope. The
  managed wrapper handles this automatically.
* All entry points are `extern "C"`, `cdecl` calling convention. No C++
  exceptions cross the boundary — exceptions are caught and converted to
  negative return codes plus `frahan_cgal_last_error`.
* Inputs MUST be triangulated, closed, manifold, and consistently
  oriented. CGAL's corefinement requires this; pre-process via
  `MeshSanitizer` before calling.

## License notes

CGAL's core is GPL with a commercial-license option from GeometryFactory.
The CGAL components used by this shim (`Polygon_mesh_processing`,
`Surface_mesh`, `Exact_predicates_inexact_constructions_kernel`) are
GPL-licensed in the open-source distribution.

If you ship a binary plugin built against CGAL, your distribution must
either (a) be GPL-compatible, (b) carry a CGAL commercial licence, or
(c) treat the CGAL DLL as a separate user-installed component. Option
(c) is the path Frahan recommends: the user installs CGAL themselves;
the plugin auto-detects and uses it when present.
