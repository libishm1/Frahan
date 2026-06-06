# run-tests.ps1 - PowerShell wrapper for the Frahan StonePack test harness.
# See RUN_TESTS.md for the full story.
#
# Usage:
#   .\run-tests.ps1                 # build + run, print PASS / SKIP / FAIL
#   .\run-tests.ps1 -Skip           # skip the build step (run only)
#   .\run-tests.ps1 -Configuration Release
#   $env:FRAHAN_RHINO_SYSTEM = "C:\path\to\Rhino\System"; .\run-tests.ps1
#   $env:FRAHAN_SKIP_NATIVE = "1"; .\run-tests.ps1
#
# Exits with the test runner's exit code (0 if no FAILs).

[CmdletBinding()]
param(
    [string]$Configuration = "Debug",
    [switch]$Skip,
    [string]$LogPath = "last_run.log"
)

$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot

# Detect Rhino install unless explicitly overridden or disabled.
if (-not $env:FRAHAN_SKIP_NATIVE -and -not $env:FRAHAN_RHINO_SYSTEM) {
    foreach ($d in @(
        "C:\Program Files\Rhino 8\System",
        "C:\Program Files\Rhino 8 WIP\System",
        "C:\Program Files\Rhino 7\System"
    )) {
        if (Test-Path (Join-Path $d "rhcommon_c.dll")) {
            $env:FRAHAN_RHINO_SYSTEM = $d
            Write-Host "INFO Detected Rhino native dir: $d" -ForegroundColor DarkGray
            break
        }
    }
    if (-not $env:FRAHAN_RHINO_SYSTEM) {
        Write-Host "WARN No Rhino install found at the default paths." -ForegroundColor Yellow
        Write-Host "WARN Native-runtime tests will SKIP. Set FRAHAN_RHINO_SYSTEM or install Rhino." -ForegroundColor Yellow
    }
}

if (-not $Skip) {
    Write-Host "INFO Building tests ($Configuration)..." -ForegroundColor DarkGray
    & dotnet build --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAIL Build failed with exit code $LASTEXITCODE." -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

Write-Host "INFO Running tests..." -ForegroundColor DarkGray
& dotnet run --configuration $Configuration --no-build *>&1 | Tee-Object -FilePath $LogPath
$exit = $LASTEXITCODE

$pass = (Select-String -Path $LogPath -Pattern '^PASS').Count
$skip = (Select-String -Path $LogPath -Pattern '^SKIP').Count
$fail = (Select-String -Path $LogPath -Pattern '^FAIL').Count
Write-Host ""
Write-Host "------------------------------------------"
Write-Host ("PASS: {0,-4} SKIP: {1,-4} FAIL: {2,-4}" -f $pass, $skip, $fail) -ForegroundColor (
    if ($fail -gt 0) { "Red" } elseif ($skip -gt 0) { "Yellow" } else { "Green" }
)
Write-Host "Log:  $LogPath"
Write-Host "------------------------------------------"

exit $exit
