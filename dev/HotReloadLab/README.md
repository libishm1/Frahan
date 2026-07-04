# HotReloadLab - edit Grasshopper .NET code live, no Rhino restart

Verified setup facts (2026-07-04): Rhino 8.30 runs on .NET 8.0.28 (CoreCLR) by
default on Windows. VS Code's C# debugger (coreclr attach) supports .NET Hot
Reload: method-body edits apply to the RUNNING Rhino. The classic failure
(ENC0003 "updating attribute requires restarting") is the SDK appending a source
revision to AssemblyInformationalVersion each build; this project pins
`IncludeSourceRevisionInInformationalVersion=false` to prevent it.

## One-time setup (done)

1. `dotnet build dev/HotReloadLab/HotReloadLab.csproj -c Debug` (net7.0-windows,
   portable PDB, references Rhino 8 system DLLs with Private=false).
2. Copy `bin/Debug/net7.0-windows/HotReloadLab.gha` + `.pdb` to
   `%APPDATA%\Grasshopper\Libraries\`.
3. Start Rhino once after the copy (the ONLY restart in the workflow).
   Component appears as **Dev > HotReload > Hot Lab** (verified: solves 2+3=5,
   note "HotLab v1: A+B").

## The live loop

1. In VS Code (folder `D:\frahan-stonepack`): Run and Debug -> **Attach to
   Rhino 8** (F5). Pick the Rhino process if prompted.
2. Open `dev/HotReloadLab/HotLabComponent.cs`. Edit the body of
   `SolveInstance` - e.g. change `a + b` to `a * b` and the note to
   "HotLab v2: A*B".
3. Save. `csharp.debug.hotReloadOnSave` applies the change to the running
   Rhino (watch the fire icon / debug console; manual apply = Ctrl+Shift+Enter).
4. In Grasshopper: right-click the Hot Lab component -> Recompute (or wiggle a
   slider). Output changes to 6 / "HotLab v2: A*B". No restart, no redeploy.

## What hot reload can and cannot do

- CAN: edit method bodies - logic, math, strings, branches, local vars. This
  covers most SolveInstance iteration.
- CANNOT (debugger reports a rude edit; restart required): change method /
  component signatures, add or remove inputs/outputs or components, change
  attributes, rename types. GUID rules from AGENTS.md unchanged.
- Debug config only. The deployed Release .gha still ships via
  `install/stage.ps1` + `install/deploy.ps1`.

## Agent-harness protocol

The human starts ONE attach session (F5) and leaves it running. After that the
agent loop per iteration is:

1. Agent edits the component source (Edit tool) - file save triggers the
   VS Code hot-reload apply automatically.
2. Agent re-solves and reads outputs via Rhino MCP `run_csharp`
   (`doc.NewSolution(true)` + read `VolatileData`), or asks the human to eyeball
   the canvas.

No build, no deploy, no restart per iteration. Restart only on signature /
ribbon changes.

## Applying this to Frahan.StonePack

Frahan targets net48 (loads fine on Rhino 8's CoreCLR as a compatibility
assembly), but hot reload wants a CoreCLR-targeted Debug build. To get the same
loop on Frahan components: add `net7.0-windows` to `<TargetFrameworks>`
(multi-target per the McNeel migration guide), deploy the net7 Debug build to
Libraries during dev sessions, and keep net48 Release for shipping. Queued as
follow-up work; validate `compat` and the native shims first.
