# Running the Frahan StonePack tests

## Quick start

From this directory:

```powershell
dotnet build --configuration Debug
dotnet run    --configuration Debug --no-build
```

Or use the wrapper:

```powershell
.\run-tests.ps1
```

Expected gate: **150 PASS / 16 SKIP / 0 FAIL**. The 16 SKIPs are
runtime-gated and require either a live Rhino process or Rhino.Inside.
See "Why the 16 SKIPs persist" below for the full story and the
two unblock strategies.

## What the gate actually means

There are three counts:

| Count | Meaning |
|---|---|
| PASS | Test ran to completion and assertions held. |
| SKIP | Test threw `DllNotFoundException`, `BadImageFormatException`, or `FileNotFoundException` mentioning Grasshopper / GH_IO / RhinoCommon. The test runner converts those to SKIP because they signal "your environment cannot load the dependency" rather than "the code is wrong". |
| FAIL | Anything else throws. The runner exits with code 1. |

Detection logic lives in `Program.cs` (`IsNativeRhinoException`). When a
SKIP fires, the runner now prints the actual exception type, message,
and inner-exception chain immediately under the `SKIP` line — that is
the diagnostic added on 2026-05-04. Use the indented `type: ... /
message: ...` lines to confirm what was missing.

## Why the 16 SKIPs persist

Every one of the 16 throws the same exception:

```
System.DllNotFoundException
Unable to load DLL 'rhcommon_c': A dynamic link library (DLL)
initialization routine failed. (Exception from HRESULT: 0x8007045A)
```

`rhcommon_c.dll` is RhinoCommon's native bridge to OpenNURBS. It ships
inside the Rhino install at `C:\Program Files\Rhino 8\System\`. The
test runner now does three things to make it loadable:

1. **`PlatformTarget=x64` + `Prefer32Bit=false`** in
   `Frahan.StonePack.Tests.csproj`. Rhino 7 and 8 are x64-only; an
   AnyCPU test exe runs as x86 on most hosts and triggers a bitness
   mismatch that Windows reports as "DLL init failed" (0x8007045A).
   The csproj now hard-pins x64.
2. **`PATH` prepend** in `Program.cs` (`ConfigureRhinoNativeLoader`)
   adds the Rhino `System\` directory to the process PATH at startup,
   so the Win32 loader finds `rhcommon_c.dll` and its companion DLLs
   (`Rhino.UI.dll`, etc.).
3. **`SetDllDirectory(...)`** P/Invoke from `Program.cs`. Belt-and-
   suspenders: even if PATH is somehow stripped, `SetDllDirectory`
   directly tells the loader where to look.

The result: HRESULT moved from `0x8007007E` (ERROR_MOD_NOT_FOUND)
to `0x8007045A` (ERROR_DLL_INIT_FAILED). The DLL is found and
loaded; its `DllMain` then refuses to initialize.

The reason `DllMain` refuses: `rhcommon_c.dll` calls into RhinoCore
during startup. RhinoCore expects a Rhino process (license, document,
host bridge) to be alive. Outside a Rhino process there is no such
context, and initialization fails. **This is by design from McNeel —
RhinoCommon is not designed to run in standalone test processes.**

## Two strategies to actually run the 16 SKIPs

### Strategy A — run the tests inside Rhino (recommended for net48)

Rhino is the runtime context the DLLs need. Drive the test harness
from a Rhino command.

Manual procedure today:

1. Open Rhino 8.
2. In the command line, type `RunPythonScript`.
3. Pick the script `tests/Frahan.StonePack.Tests/run-from-rhino.py`
   (TODO: ship this script — see "Future work" below).

Or write a one-shot Rhino command in the existing
`Frahan.StonePack.Rhino` plugin that calls `Program.Main(new string[0])`
on the test exe. Once the plugin is loaded into Rhino, the rhcommon_c
init context is satisfied and every test runs.

Status: **not yet shipped**. Tracked as the first item under "Future
work".

### Strategy B — Rhino.Inside (cloud / CI friendly, but version-fragile)

`Rhino.Inside` is McNeel's NuGet package that boots a Rhino process
inside the host process. The test exe loads the Rhino runtime, which
satisfies `rhcommon_c.dll`'s init requirement, and every test runs.

A previous attempt failed at compile time because the published
`Rhino.Inside` version transitively pinned `RhinoCommon` to a
different version than the local install (CS1705 transitive version
conflict). See `outputs/2026-05-04/frahan_overnight_handoff/
B11_SPIKE_RESULT.md` for the full diagnosis.

Net path forward:

1. Land B11 cleanly (`<PackageReference Include="RhinoCommon"
   Version="8.7.24138.15431" />` decoupled from `Rhino.Inside`).
2. Add `<PackageReference Include="Rhino.Inside"
   Version="..." />` matching the resolved RhinoCommon major.
3. Initialize Rhino.Inside at the top of `Program.cs`, before the
   first test runs.

Status: **deferred**. The B11 spike branch
`spike/B11-rhinocommon-nuget` is the prerequisite.

## Environment knobs

| Variable | Effect |
|---|---|
| `FRAHAN_RHINO_SYSTEM` | Override the Rhino install's `System\` directory if it is not at the default `C:\Program Files\Rhino 8\System\`. |
| `FRAHAN_SKIP_NATIVE=1` | Disable the native loader entirely. Useful in CI environments that have no Rhino install and only want the pure-managed test subset. The 16 native-runtime tests will SKIP with the original "module not found" error. |

## Run-tests wrapper (PowerShell)

`run-tests.ps1` (alongside this file) does:

1. Verify Rhino 8 is installed (or use `FRAHAN_RHINO_SYSTEM` override).
2. Build Debug.
3. Run, capturing stdout to `last_run.log` for inspection.
4. Print the PASS / SKIP / FAIL counts.
5. Exit with the test runner's exit code (0 if no FAILs).

CI variant — set `FRAHAN_SKIP_NATIVE=1` if the runner has no Rhino
install:

```powershell
$env:FRAHAN_SKIP_NATIVE = "1"
.\run-tests.ps1
```

## Future work

- **Ship a `run-from-rhino.py` script** that drives the test harness
  from inside Rhino. Single click instead of compiling a custom
  command. Tracked alongside R3 PR 5 (V506 fixture suite) since it
  unlocks the same 16 SKIPs.
- **Land B11** so the build is decoupled from the Rhino install path
  (separate from making the 16 SKIPs run). See
  `outputs/2026-05-04/frahan_overnight_handoff/B11_SPIKE_RESULT.md`.
- **Add `Rhino.Inside` NuGet** on top of B11 so the test gate runs
  green out of the box with no Rhino UI. Lower priority — strategy A
  is enough for local-dev and tighter to the actual production
  environment.

## Troubleshooting

- **"INFO No Rhino install found; native-runtime tests will SKIP."**
  — `Program.cs` checked `C:\Program Files\Rhino 8\System\`,
  `C:\Program Files\Rhino 8 WIP\System\`, and
  `C:\Program Files\Rhino 7\System\` and found `rhcommon_c.dll` in
  none of them. Set `FRAHAN_RHINO_SYSTEM` or install Rhino.
- **`BadImageFormatException` on `rhcommon_c.dll`** — the test exe is
  built as x86 (AnyCPU + Prefer32Bit). Confirm the csproj has
  `<PlatformTarget>x64</PlatformTarget>` and
  `<Prefer32Bit>false</Prefer32Bit>`. After a csproj change, do a
  full clean rebuild: `Remove-Item -Recurse bin, obj` then
  `dotnet build`.
- **`FileNotFoundException` on `Grasshopper.dll`** — the test csproj
  has `<Private>true</Private>` on the `Grasshopper` and `GH_IO`
  references (Item E, 2026-05-04). If a SKIP mentions
  `Grasshopper.dll`, those references probably regressed back to
  `Private=false`. Re-flip and rebuild.
- **PASS / SKIP / FAIL counts changed unexpectedly** — eyeball the
  indented diagnostic lines under any new SKIP. They tell you exactly
  which DLL or type failed to load. The runner does not silently
  swallow anything.
