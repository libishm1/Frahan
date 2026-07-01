# Frahan StonePack - one-shot deploy (Windows / Rhino 8).
# Copies the plugin binaries, native libs, BFF, and the Kintsugi weights into the
# Grasshopper Libraries folder. CLOSE RHINO before running. Style: short sentences, no em dashes.
#
# Usage (PowerShell):  pwsh -File install\deploy.ps1

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$lib  = Join-Path $env:APPDATA "Grasshopper\Libraries"

if (-not (Test-Path $lib)) { New-Item -ItemType Directory -Force -Path $lib | Out-Null }

# Refuse to run while Rhino is open (file-copy deploy requires Rhino closed).
if (Get-Process -Name "Rhino" -ErrorAction SilentlyContinue) {
    Write-Error "Rhino is running. Close Rhino and re-run (the .gha is locked while Rhino is open)."
    exit 1
}

Write-Host "Deploying Frahan StonePack to $lib"

# Plugin binaries + native libs (the .gha plus everything it loads).
Get-ChildItem (Join-Path $here "plugin") -File | ForEach-Object {
    Copy-Item $_.FullName $lib -Force
    Write-Host ("  plugin  " + $_.Name)
}
# Subfolders (arm's-length third-party workers, e.g. thirdparty/quadwild-bimdf
# with bin/ + config/ + LICENSE). Copied recursively, structure preserved.
Get-ChildItem (Join-Path $here "plugin") -Directory | ForEach-Object {
    Copy-Item $_.FullName $lib -Recurse -Force
    Write-Host ("  plugin  " + $_.Name + "\ (recursive)")
}
# BFF (optional; used by Surface Chart for distortion-free charts).
Copy-Item (Join-Path $here "tools\bff-command-line.exe") $lib -Force
Write-Host "  tool    bff-command-line.exe"
# Kintsugi Port-mode weights (~255 MB). Required only for Kintsugi Use Port Mode = True.
$w = Join-Path $here "weights\kintsugi.bin"
if (Test-Path $w) { Copy-Item $w $lib -Force; Write-Host "  weights kintsugi.bin" }
else { Write-Host "  weights kintsugi.bin NOT FOUND (run: git lfs pull). Port mode will be unavailable." }

Write-Host "Done. Start Rhino + Grasshopper. The 'Frahan' ribbon tab holds the components."
Write-Host "Open any examples\*\*.gh on the bundled data."
