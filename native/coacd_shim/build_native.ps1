# =============================================================================
# build_native.ps1 — one-shot Windows build for frahan_coacd.dll.
#
# Prerequisites:
#   - Visual Studio 2022 (any edition with C++ workload).
#   - git on PATH (needed when CoACD is fetched at configure time).
#   - CMake 3.24+.
#
# Usage:
#   .\build_native.ps1                          # Release x64 build + deploy
#   .\build_native.ps1 -Config Debug            # Debug build
#   .\build_native.ps1 -NoDeploy                # build only, do not copy
#   .\build_native.ps1 -CoacdRoot D:\src\CoACD  # use local CoACD checkout
#   .\build_native.ps1 -CoacdTag v1.0.11        # pin a different upstream tag
# =============================================================================

[CmdletBinding()]
param(
    [string]$Config    = "Release",
    [string]$CoacdRoot = "",
    [string]$CoacdTag  = "1.0.11",
    [switch]$NoDeploy,
    [switch]$Clean,
    [switch]$WithoutThirdParty
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $here

$buildDir = Join-Path $here "build"
if ($Clean -and (Test-Path $buildDir)) {
    Write-Host "Cleaning $buildDir" -ForegroundColor Yellow
    Remove-Item -Recurse -Force $buildDir
}
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

$cmakeArgs = @(
    "-G", "Visual Studio 17 2022", "-A", "x64",
    "-S", $here, "-B", $buildDir,
    "-DFRAHAN_COACD_TAG=$CoacdTag"
)
if (-not [string]::IsNullOrWhiteSpace($CoacdRoot)) {
    if (-not (Test-Path (Join-Path $CoacdRoot "CMakeLists.txt"))) {
        throw "CoacdRoot '$CoacdRoot' does not contain a CMakeLists.txt."
    }
    $cmakeArgs += "-DCOACD_ROOT=$CoacdRoot"
}
if ($WithoutThirdParty) {
    $cmakeArgs += "-DFRAHAN_COACD_WITH_3RD_PARTY=OFF"
}

Write-Host "==> cmake configure ($Config)" -ForegroundColor Cyan
cmake @cmakeArgs
if ($LASTEXITCODE -ne 0) { throw "cmake configure failed (exit $LASTEXITCODE)" }

Write-Host "==> cmake build" -ForegroundColor Cyan
cmake --build $buildDir --config $Config
if ($LASTEXITCODE -ne 0) { throw "cmake build failed (exit $LASTEXITCODE)" }

$dll = Join-Path $buildDir "$Config\frahan_coacd.dll"
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

Copy-Item $dll (Join-Path $gh "frahan_coacd.dll") -Force
Write-Host "Deployed -> $gh\frahan_coacd.dll" -ForegroundColor Green
Write-Host "Restart Rhino to pick up the new shim." -ForegroundColor Cyan
