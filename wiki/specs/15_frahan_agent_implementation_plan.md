# 15 - Frahan Agent Implementation Plan

**Spec version:** 0.1
**Audience:** the next agent (Claude / Codex / Gemini / local Qwen)
that opens this folder.

## 1. Sequence (per runbook § 15.5, instantiated)

1. **Documentation freeze** - done tonight (this folder + the
   `docs/index/` audits + the `docs/future_work/` plans).
2. **Core DTOs** - already implemented in `Frahan.StonePack.Core`;
   keep, then split per refactor R1.
3. **RhinoCommon geometry validation** - covered by
   `ValidatePackedTransformComponent`; expand per spec **13** § 4.
4. **2D Trencadis v0.1** - `Pack2DIrregularSheetV506Component`
   covers the V0.1 surface; the boundary rail index (spec **05** §
   4) is the next deliverable.
5. **Boundary rail and edge descriptors** - spec **05** § 4; bug B1
   must be resolved first.
6. **Packing reports** - `PackingResult` covers the V0.1 surface;
   add residual-void detection per spec **05** § 5.
7. **Surface mapping prototype** - already implemented (`SurfaceChartComponent`,
   `PackOnSurfaceComponent`, `PackSurfacesComponent`); harden per
   spec **06**.
8. **3D Ashlar descriptors** - proposed components per spec **07** §
   4; build descriptor + face-match before volume-pack.
9. **GeoCut proof of concept** - proposed components per spec **09**;
   start with `Frahan Slab Candidate Generator` and
   `Frahan Crack-Slab Intersector` on a single fixture block.
10. **Native backend wrappers** - proposed per spec **11**; do
    `Frahan.NativeBridge` first, then `Frahan.Native.Packing` (lowest
    license risk per reference register).
11. **Learning-guided ordering** - gated per spec **12**; only after
    deterministic heuristics are stable.

## 2. Branch strategy

- One branch per spec deliverable.
- Branch name: `frahan/<spec-number>-<slug>` (e.g.
  `frahan/05-boundary-rail-index`).
- Target branch: `main`.
- PR body must reference the spec section and include a build /
  test evidence block.

## 3. Per-step deliverable contract

Each step produces:

- updated source under `src/Frahan.<Module>/`;
- a unit test under `tests/Frahan.Tests/`;
- an entry in `docs/index/frahan_class_method_component_audit.md`;
- a CHECKPOINT under
  `Template-General/outputs/<date>/frahan_stonepack/CHECKPOINT_NNN.md`;
- a HANDOFF under the same folder if the next step is for a
  different agent.

## 4. Allowed and forbidden actions per agent

| Agent | Allowed | Forbidden |
| --- | --- | --- |
| Documentation agent | edit `docs/`, `agent_reports/` | edit `src/`, `tests/`, `dist/`, `share/`, binary files |
| Implementation agent | edit `src/`, `tests/`, with V1 + V2 evidence | rename namespaces; rewrite solvers; install packages; commit |
| Release agent | edit `dist/manifest.yml`, `dist/README.md` | edit `src/`, `tests/`, source files (only the release script does that) |
| Refactor agent | execute one of the R1–R7 refactors per a written PLAN.md | start a new refactor without a plan |

## 5. Tonight's leftover work for the next agent

In priority order:

1. **Fix bug B1** in both research markdowns (single-line replacement of
   the no-op ternary). See `docs/future_work/frahan_code_bug_register.md`.
2. **Add `namespace Frahan.StonePack.Tests;`** to the two test files,
   with a matching update to `Program.cs` if the runner uses a fully
   qualified type name. Single-file each side, ≤ 5 lines, but the
   pair counts as multi-file so it was not auto-applied tonight.
3. **Author `THIRD_PARTY_NOTICES.md`** under the project root with
   per-component license text for every shipped DLL. See
   `docs/index/frahan_reference_register.md` § 8.
4. **Decide and document the V2/V3/V506 collapse strategy**  - 
   `[GH_ParamObsolete]` plan with stable `ComponentGuid`s. See
   `docs/future_work/frahan_major_refactor_plan.md` Refactor R3.
5. **Decide and document the `Frahan.StonePack.*` → `Frahan.*` rename
   plan** - full multi-file refactor with parallel-namespace transition
   period. See refactor R1.

## 6. Gate before any rename

Before any namespace rename or assembly rename, verify:

- All callers in the live Frahan source compile against both old
  and new names (parallel `[Obsolete("Renamed to Frahan.X")]` shims
  for one release).
- Every `.gh` document in
  `outputs/.../frahan_stonepack/share/Frahan.StonePack_Rhino8_*.zip`
  loads the same components by ComponentGuid (the GUIDs do not
  change in a rename).
- Every `manifest.yml` is updated and re-signed.
- A fresh Rhino 8 install can install the renamed yak package
  alongside the old `Frahan.StonePack` package without conflict.

## 7. Stop conditions

The implementation agent stops and asks if any of the following
appears:

- A solver change that needs > 20 lines in a single file (escalate
  to a written PLAN.md).
- A native backend dependency request (CGAL / Geogram) - license
  review required.
- A change to `.gitattributes` or `dist/manifest.yml`.
- A request to commit, push, install, or run the robot.
