# 01 - Frahan Software Principles

**Spec version:** 0.1
**Source:** runbook §§ 4 and 15.4, plus
`Template-General/wiki/local_ai_workflow/frahan_agent_rules.md`,
plus the existing `outputs/2026-05-01/frahan_stonepack/frahan_agent_rules.md`.

## 1. Core principle

```
Small RhinoCommon core.
Clear public APIs.
Lazy optional backends.
Proxy-first search.
Original-geometry validation.
Boundary, edge, and face matching before expensive solving.
Fabrication-aware trim and cut suggestions.
Responsive Grasshopper execution.
```

## 2. Hard rules

1. **Frahan-owned interfaces** at every public boundary. No CGAL,
   Geogram, VHACD, CoACD, geometry3Sharp, libigl, or MeshLib type
   appears in a Frahan public API.
2. **RhinoCommon at boundaries only.** Conversion to Frahan internal
   DTOs happens at the Grasshopper component edge, not deep inside
   the core.
3. **Original-geometry validation.** Solvers run on proxies; final
   acceptance checks run against the original mesh / curve / surface.
4. **Lazy native backends.** Native binaries are loaded only on first
   use; the plugin must remain functional with the managed-only
   default backend.
5. **No RhinoDoc mutation from worker threads.** All `RhinoDoc`
   writes happen on the GH solve thread.
6. **No Grasshopper UI freeze.** Long-running solvers go through
   `GH_TaskCapableComponent` (the live `Pack2DIrregularSheetV2..506`
   components already follow this pattern). Provide cancellation and
   progress reporting.
7. **Validation reports beat boolean results.** Every solver returns
   a `*Result` DTO with placements, failures, metrics, and
   diagnostics - see `Frahan.StonePack.Core.PackResult` /
   `MeshPackResult` as the existing template.
8. **Boundary, edge, and face matching before expensive solving.**
   Cheap signature matching prunes the candidate space before any
   NFP, BVH, or simulated-annealing pass.
9. **Fabrication awareness.** Every output must be queryable for
   trim suggestions, kerf, fillet radius, saw-bed compatibility, and
   slab yield.
10. **No production-ready claims without tests.** Every readiness
    claim in any document must be tied to an executed test.

## 3. C# language version and target frameworks

- `Frahan.Core` (target naming; currently `Frahan.StonePack.Core`):
  netstandard2.0 + net48 dual-target. **No** `HashCode.Combine`,
  **no** `init` setters that require netstandard2.1, **no** records
  unless the build is bumped past net48. The live
  `FaceCornerUvTable.cs` already uses a manual `GetHashCode` for
  this reason.
- `Frahan.Surface` (proposed): same as Core today; this code already
  depends on `Rhino.Geometry`.
- `Frahan.GH` (target naming; currently `Frahan.StonePack.GH`):
  net48 only. Grasshopper in Rhino 8 still requires .NET Framework
  4.8 for `.gha` assemblies.
- `Frahan.Rhino` (currently `Frahan.StonePack.Rhino`): net48.
- `Frahan.Tests`: net6.0 + net48 dual-target (matches the live
  `Frahan.StonePack.Tests.csproj`).
- Native bindings (`Frahan.NativeBridge`, `Frahan.Native.*`):
  managed wrappers in netstandard2.0 + net48; native DLLs Windows-x64
  primarily, with a future macOS path tracked separately.

## 4. Style guide

- Short declarative sentences in docstrings and `/// <summary>`.
- File-scoped namespaces preferred when targeting C# 10+; the source
  mixes file-scoped and block-scoped today (drift report § 10).
- No em dashes in comments. (The wider AGENTS.md style rule.)
- "Frahan" (capitalised) in proper-noun usage; lowercase only in
  prose like "frahan plugin".
- "Trencadis" (no accent) in code identifiers; "Trencadís" allowed
  in research-narrative prose.
- Initialisms expanded on first mention: "BFF (Boundary First
  Flattening)", "NFP (No-Fit Polygon)", "VHACD (Volumetric-Hierarchical
  Approximate Convex Decomposition)".

## 5. Verification levels (V0–V3)

Inherits from `Template-General/AGENTS.md` § "Task completion
verification":

- **V0** diff-only - README edits, doc rewording.
- **V1** static - lint, type check, compile.
- **V2** runtime - unit test, script run, generated file diff.
- **V3** human-in-loop - robot motion, fabrication output,
  packaging release artefacts.

Frahan-specific minima:

- Adding a Grasshopper component: V1 (build) + V3 (open in Rhino 8
  and confirm component appears in the right ribbon).
- Modifying a solver: V1 + V2 (unit test on a known fixture).
- Releasing a `.yak` or `.zip` bundle: V3 (human approval).

## 6. Cancellation and progress

Every solver implements:

- a `CancellationToken` parameter,
- a periodic progress callback `(double fraction, string message)`,
- bounded internal queues so memory stays predictable on huge
  inputs.

`GH_TaskCapableComponent` carries the cancellation token through the
Grasshopper solve.

## 7. Logging and diagnostics

- Use a `Frahan.Core.Diagnostics` static logger with severity levels
  (proposed).
- Never use `Console.WriteLine` from inside `Frahan.GH` (Rhino's
  console capture has changed across SR versions).
- Surface user-visible warnings via
  `IGH_ActiveObject.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, …)`.
