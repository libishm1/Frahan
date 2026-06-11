# =============================================================================
# build_native.ps1 — one-shot Windows build for frahan_cgal.dll.
#
# Prerequisites:
#   - Visual Studio 2022 (any edition with C++ workload).
#   - vcpkg installed at $env:VCPKG_ROOT (or override via -VcpkgRoot).
#   - CGAL installed via vcpkg: `vcpkg install cgal:x64-windows`.
#
# Usage:
#   .\build_native.ps1                          # Release x64 build + deploy
#   .\build_native.ps1 -Config Debug            # Debug build
#   .\build_native.ps1 -NoDeploy                # build only, do not copy
#   .\build_native.ps1 -VcpkgRoot D:\vcpkg      # override vcpkg location
# =============================================================================

[CmdletBinding()]
param(
    [string]$Config = "Release",
    [string]$VcpkgRoot = $env:VCPKG_ROOT,
    [string]$Triplet = "x64-windows",
    [switch]$NoDeploy,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $here

if ([string]::IsNullOrWhiteSpace($VcpkgRoot)) {
    if (Test-Path "C:\vcpkg") { $VcpkgRoot = "C:\vcpkg" }
    elseif (Test-Path "D:\vcpkg") { $VcpkgRoot = "D:\vcpkg" }
    else { throw "vcpkg not found. Install vcpkg, set `$env:VCPKG_ROOT, or pass -VcpkgRoot." }
}

$toolchain = Join-Path $VcpkgRoot "scripts\buildsystems\vcpkg.cmake"
if (-not (Test-Path $toolchain)) {
    throw "vcpkg toolchain not found at $toolchain. Re-bootstrap vcpkg or check the path."
}

$buildDir = Join-Path $here "build"
if ($Clean -and (Test-Path $buildDir)) {
    Write-Host "Cleaning $buildDir" -ForegroundColor Yellow
    Remove-Item -Recurse -Force $buildDir
}
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

Write-Host "==> cmake configure ($Config / $Triplet)" -ForegroundColor Cyan
cmake -G "Visual Studio 17 2022" -A x64 `
    -S $here -B $buildDir `
    -DCMAKE_TOOLCHAIN_FILE=$toolchain `
    -DVCPKG_TARGET_TRIPLET=$Triplet
if ($LASTEXITCODE -ne 0) { throw "cmake configure failed (exit $LASTEXITCODE)" }

Write-Host "==> cmake build" -ForegroundColor Cyan
cmake --build $buildDir --config $Config
if ($LASTEXITCODE -ne 0) { throw "cmake build failed (exit $LASTEXITCODE)" }

$dll = Join-Path $buildDir "$Config\frahan_cgal.dll"
if (-not (Test-Path $dll)) {
    throw "build succeeded but DLL not found at $dll"
}
Write-Host "Built: $dll  ($((Get-Item $dll).Length) bytes)" -ForegroundColor Green

if ($NoDeploy) {
    Write-Host "Deploy skipped (-NoDeploy)." -ForegroundColor Yellow
    return
}

$gh = Join-Path $env:APPDATA "Grasshopper\Libraries\Frahan.StonePack.MeshHeightmap"
if (-not (Test-Path $gh)) {
    Write-Host "Grasshopper libraries dir not found at $gh — skipping deploy." -ForegroundColor Yellow
    return
}

Copy-Item $dll (Join-Path $gh "frahan_cgal.dll") -Force
Write-Host "Deployed → $gh\frahan_cgal.dll" -ForegroundColor Green
Write-Host "Restart Rhino to pick up the new shim." -ForegroundColor Cyan
