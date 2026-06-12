# build_native.ps1 — build x64 nfp_kernel.dll + smoke test.
#
# Toolchain probe order:
#   1. MSVC cl.exe on PATH (cmake/ninja not required: single TU + engine.cpp)
#   2. llvm-mingw (proven on this machine):
#      D:\ref-20260610T154710Z-3-001\ref\tools\llvm-mingw\llvm-mingw-20260602-ucrt-x86_64\bin
#
# Output: nfp_kernel.dll (x64, statically linked runtime) in this directory.
# Deploy: copy beside tests\Frahan.StonePack.Tests\bin\Release\net48\ AND
#         beside the .gha for canvas use (see ..\..\docs / README notes).

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $here
try {
    $cl = Get-Command cl.exe -ErrorAction SilentlyContinue
    if ($cl) {
        Write-Host "Building with MSVC cl.exe"
        cl /nologo /O2 /EHsc /LD /DNDEBUG /I clipper2\include `
            nfp_kernel.cpp clipper2\src\clipper.engine.cpp `
            /Fe:nfp_kernel.dll
        cl /nologo /O2 /Fe:test_nfp_kernel.exe test_nfp_kernel.c
    }
    else {
        $mingw = 'D:\ref-20260610T154710Z-3-001\ref\tools\llvm-mingw\llvm-mingw-20260602-ucrt-x86_64\bin'
        if (-not (Test-Path "$mingw\x86_64-w64-mingw32-g++.exe")) {
            throw "No cl.exe on PATH and llvm-mingw not found at $mingw"
        }
        Write-Host "Building with llvm-mingw ($mingw)"
        & "$mingw\x86_64-w64-mingw32-g++.exe" -O2 -shared -static -DNDEBUG `
            -I clipper2\include `
            nfp_kernel.cpp clipper2\src\clipper.engine.cpp `
            -o nfp_kernel.dll
        if ($LASTEXITCODE -ne 0) { throw "dll build failed" }
        & "$mingw\x86_64-w64-mingw32-gcc.exe" -O2 test_nfp_kernel.c -o test_nfp_kernel.exe
        if ($LASTEXITCODE -ne 0) { throw "test build failed" }
    }
    Write-Host "Running smoke test"
    & .\test_nfp_kernel.exe
    if ($LASTEXITCODE -ne 0) { throw "smoke test FAILED" }
    Write-Host "nfp_kernel.dll built and smoke-tested OK"
}
finally { Pop-Location }
