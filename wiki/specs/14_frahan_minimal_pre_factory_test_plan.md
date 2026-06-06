# 14 - Frahan Minimal Pre-Factory Test Plan

**Spec version:** 0.1
**Sources:** the runbook ("minimal pre-factory test plan"),
`13_frahan_testing_and_validation_spec.md`,
the live `Frahan.StonePack.GH` component set,
`Template-General/outputs/2026-05-01/frahan_stonepack/PATCH_SUMMARY.md`,
`HANDOFF_LATEST.md`.

## 1. Purpose

A focused checklist for the **first real fabrication** to be performed
with Frahan StonePack 0.5.6. The list is intentionally short. Pass
this and the human signs off on a factory run.

## 2. Pre-flight (V1, build)

- [ ] `dotnet build Frahan.StonePack.sln` returns 0 errors,
      0 warnings on a clean Rhino 8 SR8+ developer machine.
- [ ] `Frahan.StonePack.Core.dll`, `Frahan.StonePack.gha`, and
      `Frahan.StonePack.Rhino.rhp` produced under `bin/Debug/` (or
      Release).
- [ ] No file under `bin/Debug/` references a missing native DLL
      (verified with `dumpbin /imports` or equivalent).

## 3. Static checks (V1)

- [ ] `Frahan.StonePack.GH` does **not** add a new `using Frahan.StonePack.Core;`
      that pulls in `Frahan.StonePack.Core.SurfacePacking` into a
      Grasshopper component that previously did not depend on the
      surface-pack subsystem (would force a transitive
      `Rhino.Geometry` dependency that may surprise downstream
      consumers).
- [ ] No `HashCode.Combine`, no record types, no `init` setters in
      `Frahan.StonePack.Core` - net48 friendliness is preserved.
- [ ] No new `Console.WriteLine` in `Frahan.StonePack.GH` source.

## 4. Unit tests (V2)

- [ ] `dotnet test tests/Frahan.StonePack.Tests/` passes on net6.0 and
      on net48.
- [ ] `SurfacePackingTests.FaceCornerUvTable_RoundTrip` passes.
- [ ] `SurfacePackingTests.BarycentricMapper_KnownTriangle` passes.

## 5. Integration tests (V2 + V3)

For each of these, open a Rhino 8 SR8+ session, install the
`Frahan.StonePack_Rhino8_0.5.6` build (yak install or manual zip
unpack), then:

- [ ] **2D NFP packing.** Open a sample Grasshopper file with
      `NfpPack2DComponent`. Run on the test sheet + parts fixture.
      Confirm packed output, no overlap, no exception.
- [ ] **2D irregular sheet (V506).** Use
      `Pack2DIrregularSheetV506Component` with the freeform-curve
      fixture. Confirm yield > 0.50 on the standard fixture.
- [ ] **3D irregular box.** `Pack3DIrregularComponent` with the
      10-stone fixture; confirm no overlap (overlap volume = 0 from
      `ValidatePackedTransformComponent`).
- [ ] **3D mesh container.** `Pack3DIrregularContainerComponent` with
      the cup-mesh container fixture; confirm placements are inside
      the container.
- [ ] **3D mesh heightmap.** `Pack3DMeshHeightmapComponent` with the
      stone-pile fixture; confirm heightmap mesh is non-empty.
- [ ] **Surface chart.** `SurfaceChartComponent` on the column-mesh
      fixture; confirm BFF runs (ExitCode == 0), `Distortion.MaxAreaRatio
      < 1.5`.
- [ ] **Pack on surface.** `PackOnSurfaceComponent` with the
      previously-built chart and the tile fixture; confirm
      `Curves3D.Count > 0`.
- [ ] **Validate transforms.** `ValidatePackedTransformComponent` on
      the 3D irregular box result; confirm `Valid == true`,
      `OverlapVolume == 0`.

## 6. Cancellation (V2)

- [ ] On any task-capable component, cancel mid-solve. Component
      returns a partial result + a `Cancelled` flag, and the GH UI
      stays responsive.

## 7. Resource hygiene (V2)

- [ ] After a full test run, no temporary OBJ file remains in
      `%TEMP%\Frahan_BFF\` (or wherever `BffCommandLineRunner` writes
      its scratch files; verify the cleanup path).
- [ ] No native DLL is left loaded (proposed when native backends
      land; not gating today).

## 8. Fabrication readiness (V3)

- [ ] Manual visual inspection of the 2D irregular sheet result for
      a fabrication run: no overlapping parts, all parts inside the
      sheet, no parts crossing a hole.
- [ ] Manual visual inspection of the 3D ashlar result: stones sit
      on heightmap, no obvious floaters.
- [ ] Yield numbers in the report match the visual estimate within
      ±5 %.

## 9. Sign-off

- [ ] All checkboxes above are ticked.
- [ ] One human (Libish or designated reviewer) confirms in writing
      (a comment in `PATCH_SUMMARY.md` or `HANDOFF_LATEST.md`) that
      the build is fabrication-ready.
- [ ] The release zip's checksum is recorded in
      `THIRD_PARTY_NOTICES.md` (proposed; tracked in
      `next_agent_tasks.md`).

## 10. Known limits acknowledged before the run

- `preserveZone` ternary bug (B1) does not affect 0.5.6 because the
  boundary-rail-index code path is **not yet enabled** in
  production.
- The typo'd `frahans_stonepack-0.5.6-rh8-win..zip` exists alongside
  the canonical `frahan_stonepack-0.5.6-rh8-win.zip`. Use the
  canonical (single-`f`, single-`.`) bundle for installation.
