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

FULLY AGENT-DRIVEN via the `vscode-as-mcp-server` extension
(acomagu.vscode-as-mcp-server, installed 2026-07-04; registered as the `vscode`
server in `D:\code_ws\.mcp.json` via `C:\Program Files\nodejs\npx.cmd
vscode-as-mcp-server`). Requires: VS Code open on `D:\frahan-stonepack`, the
extension's MCP server started (status bar icon), and a Claude Code session
restart after first registration. The loop:

1. `start_debug_session` with the "Attach to Rhino 8" configuration (or
   `execute_vscode_command` -> `workbench.action.debug.start`). Once per session.
2. Agent edits the component source with the `text_editor` tool - edits go
   through VS Code, so the save event fires `csharp.debug.hotReloadOnSave` and
   the delta applies to the RUNNING Rhino. (Plain disk writes are seen as
   external changes and may NOT trigger the apply - use `text_editor`, or
   `execute_vscode_command` with the hot-reload apply command; discover its id
   via `list_vscode_commands`.)
3. Agent re-solves and reads outputs via Rhino MCP `run_csharp`
   (`doc.NewSolution(true)` + read `VolatileData`).

No build, no deploy, no restart per iteration. Restart only on signature /
ribbon changes (rude edits).

GOTCHA (hit live 2026-07-04): the project MUST be in the loaded solution.
A loose .cs file gives "Debugging C# files without a project is only supported
for .NET 10+" and hot reload never applies. HotReloadLab.csproj was added to
Frahan.StonePack.sln for exactly this reason.

## Applying this to Frahan.StonePack

Frahan targets net48 (loads fine on Rhino 8's CoreCLR as a compatibility
assembly), but hot reload wants a CoreCLR-targeted Debug build. To get the same
loop on Frahan components: add `net7.0-windows` to `<TargetFrameworks>`
(multi-target per the McNeel migration guide), deploy the net7 Debug build to
Libraries during dev sessions, and keep net48 Release for shipping. Queued as
follow-up work; validate `compat` and the native shims first.
