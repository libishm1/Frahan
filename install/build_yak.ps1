# Build the Frahan StonePack Yak package for the Rhino Package Manager / Food4Rhino.
# Assembles a clean staging folder (the .gha + Core/native/third-party deps, minus
# pdbs and the separate RubblePack plugin), drops in the manifest + icon, and runs
# `yak build`. Run AFTER a Release/Debug build + install\stage.ps1.
#   pwsh -File install\build_yak.ps1
# To publish (HITL, requires `yak login`):  yak push <the .yak>
# Style: short sentences, no em dashes.

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$yak  = "C:\Program Files\Rhino 8\System\yak.exe"
if (-not (Test-Path $yak)) { Write-Error "yak.exe not found at $yak"; exit 1 }

$src = Join-Path $here "plugin"
$yb  = Join-Path $here "yak_build"
if (-not (Test-Path $src)) { Write-Error "install\plugin not found. Run stage.ps1 first."; exit 1 }

if (Test-Path $yb) { Remove-Item $yb -Recurse -Force }
New-Item -ItemType Directory -Force -Path $yb | Out-Null

# Files: everything in plugin EXCEPT pdbs and the separate RubblePack .gha.
Get-ChildItem $src -File |
    Where-Object { $_.Extension -ne ".pdb" -and $_.Name -ne "Frahan.RubblePack.gha" } |
    ForEach-Object { Copy-Item $_.FullName $yb -Force }
# Subfolders (arm's-length third-party workers, e.g. thirdparty\quadwild-bimdf).
Get-ChildItem $src -Directory | ForEach-Object { Copy-Item $_.FullName $yb -Recurse -Force }

# Manifest + package icon.
Copy-Item (Join-Path $here "yak_manifest.yml") (Join-Path $yb "manifest.yml") -Force
Copy-Item (Join-Path $here "..\src\Frahan.StonePack.GH\Resources\VaultShellCra.png") (Join-Path $yb "icon.png") -Force

Push-Location $yb
Remove-Item *.yak -Force -ErrorAction SilentlyContinue
& $yak build --platform win
Pop-Location

Write-Host "Done. Package(s):"
Get-ChildItem (Join-Path $yb "*.yak") | ForEach-Object { Write-Host ("  " + $_.Name) }
Write-Host "Publish (HITL): yak login   then   yak push <package>.yak"
