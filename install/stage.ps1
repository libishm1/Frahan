# Frahan StonePack - stage the fresh dev build into install/plugin (run BEFORE deploy.ps1).
# Copies the Frahan-owned managed assemblies + the .gha from the build output into the
# staging folder that deploy.ps1 ships. Native libs (frahan_cgal/coacd/geogram, workers,
# gmp, libbulletc) and 3rd-party DLLs are left as-is -- they are not produced by this build.
# Safe to run with Rhino open (this only copies files inside the repo). Then CLOSE Rhino
# and run: pwsh -File install\deploy.ps1
# Style: short sentences, no em dashes.

$ErrorActionPreference = "Stop"
$here   = Split-Path -Parent $MyInvocation.MyCommand.Path
$bin    = Join-Path $here "..\src\Frahan.StonePack.GH\bin\Debug\net48"
# Use the Release output when Debug is absent or Release holds the newer .gha.
$binRel = Join-Path $here "..\src\Frahan.StonePack.GH\bin\Release\net48"
$ghaDbg = Join-Path $bin "Frahan.StonePack.gha"
$ghaRel = Join-Path $binRel "Frahan.StonePack.gha"
if (-not (Test-Path $ghaDbg)) { $bin = $binRel }
elseif ((Test-Path $ghaRel) -and ((Get-Item $ghaRel).LastWriteTime -gt (Get-Item $ghaDbg).LastWriteTime)) { $bin = $binRel }
$plugin = Join-Path $here "plugin"

if (-not (Test-Path $bin)) { Write-Error "Build output not found: $bin. Build the GH project first (dotnet build src\Frahan.StonePack.GH)."; exit 1 }
if (-not (Test-Path $plugin)) { Write-Error "Staging folder not found: $plugin"; exit 1 }

# The managed assemblies this build produces (the only files that change on a dev build).
# PDB files are included so Visual Studio breakpoints bind when attaching to Rhino.
$names = @(
    "Frahan.StonePack.gha",
    "Frahan.StonePack.dll",
    "Frahan.StonePack.pdb",
    "Frahan.StonePack.Core.dll",
    "Frahan.StonePack.Core.pdb",
    "Frahan.EdgeMatching.Core.dll",
    "Frahan.EdgeMatching.Core.pdb",
    "Frahan.Kintsugi.Port.dll",
    "Frahan.Kintsugi.Port.pdb"
)

Write-Host "Staging fresh build -> $plugin"
foreach ($n in $names) {
    $src = Join-Path $bin $n
    if (Test-Path $src) {
        Copy-Item $src (Join-Path $plugin $n) -Force
        $kb = [int]((Get-Item $src).Length / 1KB)
        Write-Host ("  staged  {0}  ({1} KB)" -f $n, $kb)
    } else {
        Write-Warning ("MISSING in build output: " + $n)
    }
}
Write-Host "Staged. Now CLOSE Rhino and run:  pwsh -File install\deploy.ps1"
