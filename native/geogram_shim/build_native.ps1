# =============================================================================
# build_native.ps1 - one-shot Windows build for frahan_geogram.dll.
#
# Prerequisites:
#   - Visual Studio 2022 (any edition with C++ workload).
#   - git on PATH (needed when Geogram is fetched at configure time).
#   - CMake 3.24+.
#
# Usage:
#   .\build_native.ps1                              # Release x64 + deploy
#   .\build_native.ps1 -Config Debug                # Debug build
#   .\build_native.ps1 -NoDeploy                    # build only, no copy
#   .\build_native.ps1 -GeogramRoot D:\src\geogram  # use local checkout
#   .\build_native.ps1 -GeogramTag v1.9.9           # pin upstream tag
# =============================================================================

[CmdletBinding()]
param(
    [string]$Config      = "Release",
    [string]$GeogramRoot = "",
    [string]$GeogramTag  = "v1.9.9",
    [switch]$NoDeploy,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $here

# Locate cmake.exe. Standard locations first; fall back to PATH.
$cmake = $null
$candidates = @(
    "${env:ProgramFiles}\CMake\bin\cmake.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
)
foreach ($c in $candidates) { if (Test-Path $c) { $cmake = $c; break } }
if (-not $cmake) { $cmake = "cmake" }  # PATH fallback

# Prepend cmake's dir to PATH so any sub-build whose scripts spawn bare
# `cmake -E ...` (CoACD/OpenVDB do this) can find it. Harmless if cmake
# is already on PATH.
$cmakeBin = Split-Path -Parent $cmake
if ($cmakeBin -and ($env:PATH -notlike "*$cmakeBin*")) {
    $env:PATH = "$cmakeBin;" + $env:PATH
}

$buildDir = Join-Path $here "build"
if ($Clean -and (Test-Path $buildDir)) {
    Write-Host "Cleaning $buildDir" -ForegroundColor Yellow
    Remove-Item -Recurse -Force $buildDir
}
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

$cmakeArgs = @(
    "-G", "Visual Studio 17 2022", "-A", "x64",
    "-S", $here, "-B", $buildDir,
    "-DFRAHAN_GEOGRAM_TAG=$GeogramTag"
)
if (-not [string]::IsNullOrWhiteSpace($GeogramRoot)) {
    if (-not (Test-Path (Join-Path $GeogramRoot "CMakeLists.txt"))) {
        throw "GeogramRoot '$GeogramRoot' does not contain a CMakeLists.txt."
    }
    $cmakeArgs += "-DGEOGRAM_ROOT=$GeogramRoot"
}

Write-Host "==> cmake configure ($Config)" -ForegroundColor Cyan
& $cmake @cmakeArgs
if ($LASTEXITCODE -ne 0) { throw "cmake configure failed (exit $LASTEXITCODE)" }

Write-Host "==> cmake build" -ForegroundColor Cyan
& $cmake --build $buildDir --config $Config --target frahan_geogram
if ($LASTEXITCODE -ne 0) { throw "cmake build failed (exit $LASTEXITCODE)" }

$dll = Join-Path $buildDir "$Config\frahan_geogram.dll"
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
    Write-Host "Grasshopper libraries dir not found at $gh - skipping deploy." -ForegroundColor Yellow
    return
}

Copy-Item $dll (Join-Path $gh "frahan_geogram.dll") -Force
Write-Host "Deployed -> $gh\frahan_geogram.dll" -ForegroundColor Green
Write-Host "Restart Rhino to pick up the new shim." -ForegroundColor Cyan
