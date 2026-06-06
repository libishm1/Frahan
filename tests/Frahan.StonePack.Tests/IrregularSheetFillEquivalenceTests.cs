#nullable disable
using System;
using System.Collections.Generic;
using Frahan.GH.TwoD;
using Rhino.Geometry;

namespace Frahan.Tests;

// R3 PR 1 - equivalence harness scaffold for the unified IrregularSheetFill
// vs the legacy V-suffixed solver classes. Tests in this file should:
//   1. Run the same fixture through both BUILDs (legacy + unified façade).
//   2. Compare PackingResult fields with the tolerances spelled out in
//      docs/future_work/R3_versioned_solver_collapse_plan.md section 4.2.
//   3. Surface any deviation as a test failure, blocking the R3 collapse
//      until both code paths are bit-identical.
//
// Status: SCAFFOLD only.
//   - Façade-construction guards (V1/V2/V3 throw; V506 builds) - pure
//     managed and unit-tested today.
//   - Per-fixture equivalence runs (24 cases per the R3 plan) require Rhino
//     runtime to construct LineCurve / Polyline curves; tagged "Rhino" and
//     SKIPed by the runner without rhcommon_c.dll. R3 PRs 2 - 5 fill in the
//     bodies as each variant's Pack delegate is wired.

static class IrregularSheetFillEquivalenceTests
{
    // -- Façade construction guards (pure managed) --------------------------

    public static void ForV506_Construction_Succeeds()
    {
        // Build with empty inputs; ctor must not throw because the legacy
        // V506 constructor tolerates empty outline lists.
        var unified = IrregularSheetFill.ForV506(
            sheetOutlines: Array.Empty<Curve>(),
            sheetHoles: Array.Empty<IReadOnlyList<Curve>>(),
            spacing: 1.0,
            rotationsDeg: null,
            tolerance: 0.001,
            sortMode: PackingSortMode.AreaDescending,
            cornerMode: PackingCornerMode.BottomLeft,
            seed: 0,
            maxCandidates: 300);

        Assert(unified != null, "V506 façade should construct");
        Assert(unified.Variant == SolverVariant.V506, $"variant should be V506, got {unified.Variant}");
    }

    public static void ForV506_NullSheetOutlines_Throws()
    {
        bool threw = false;
        try
        {
            _ = IrregularSheetFill.ForV506(
                sheetOutlines: null,
                sheetHoles: Array.Empty<IReadOnlyList<Curve>>(),
                spacing: 1.0,
                rotationsDeg: null,
                tolerance: 0.001,
                sortMode: PackingSortMode.AreaDescending,
                cornerMode: PackingCornerMode.BottomLeft,
                seed: 0,
                maxCandidates: 300);
        }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null sheetOutlines should throw ArgumentNullException");
    }

    public static void ForV506_NullSheetHoles_Throws()
    {
        bool threw = false;
        try
        {
            _ = IrregularSheetFill.ForV506(
                sheetOutlines: Array.Empty<Curve>(),
                sheetHoles: null,
                spacing: 1.0,
                rotationsDeg: null,
                tolerance: 0.001,
                sortMode: PackingSortMode.AreaDescending,
                cornerMode: PackingCornerMode.BottomLeft,
                seed: 0,
                maxCandidates: 300);
        }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null sheetHoles should throw ArgumentNullException");
    }

    public static void ForV1_Construction_Succeeds()
    {
        // R3 PR 2: ForV1 is now wired. Build with empty inputs; ctor must
        // not throw because the legacy V1 (IrregularSheetFillRhino) constructor
        // tolerates empty outline lists.
        var unified = IrregularSheetFill.ForV1(
            sheetOutlines: Array.Empty<Curve>(),
            sheetHoles: Array.Empty<IReadOnlyList<Curve>>(),
            spacing: 1.0,
            rotationsDeg: null,
            tolerance: 0.001,
            sortMode: PackingSortMode.AreaDescending,
            simplifyCurves: false,
            simplifyTolerance: 0.001,
            seed: 0,
            maxCandidates: 300,
            cornerMode: PackingCornerMode.BottomLeft);

        Assert(unified != null, "V1 façade should construct");
        Assert(unified.Variant == SolverVariant.V1, $"variant should be V1, got {unified.Variant}");
    }

    public static void ForV1_NullSheetOutlines_Throws()
    {
        bool threw = false;
        try
        {
            _ = IrregularSheetFill.ForV1(
                sheetOutlines: null,
                sheetHoles: Array.Empty<IReadOnlyList<Curve>>(),
                spacing: 1.0,
                rotationsDeg: null,
                tolerance: 0.001,
                sortMode: PackingSortMode.AreaDescending,
                simplifyCurves: false,
                simplifyTolerance: 0.001,
                seed: 0,
                maxCandidates: 300,
                cornerMode: PackingCornerMode.BottomLeft);
        }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null sheetOutlines should throw ArgumentNullException");
    }

    public static void ForV1_NullSheetHoles_Throws()
    {
        bool threw = false;
        try
        {
            _ = IrregularSheetFill.ForV1(
                sheetOutlines: Array.Empty<Curve>(),
                sheetHoles: null,
                spacing: 1.0,
                rotationsDeg: null,
                tolerance: 0.001,
                sortMode: PackingSortMode.AreaDescending,
                simplifyCurves: false,
                simplifyTolerance: 0.001,
                seed: 0,
                maxCandidates: 300,
                cornerMode: PackingCornerMode.BottomLeft);
        }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null sheetHoles should throw ArgumentNullException");
    }

    public static void ForV2_Construction_Succeeds()
    {
        var unified = IrregularSheetFill.ForV2(
            sheetOutlines: Array.Empty<Curve>(),
            sheetHoles: Array.Empty<IReadOnlyList<Curve>>(),
            spacing: 1.0,
            rotationsDeg: null,
            tolerance: 0.001,
            sortMode: PackingSortMode.AreaDescending,
            cornerMode: PackingCornerMode.BottomLeft,
            seed: 0,
            maxCandidates: 300);
        Assert(unified != null, "V2 façade should construct");
        Assert(unified.Variant == SolverVariant.V2, $"variant should be V2, got {unified.Variant}");
    }

    public static void ForV2_NullSheetOutlines_Throws()
    {
        bool threw = false;
        try
        {
            _ = IrregularSheetFill.ForV2(
                sheetOutlines: null,
                sheetHoles: Array.Empty<IReadOnlyList<Curve>>(),
                spacing: 1.0, rotationsDeg: null, tolerance: 0.001,
                sortMode: PackingSortMode.AreaDescending,
                cornerMode: PackingCornerMode.BottomLeft,
                seed: 0, maxCandidates: 300);
        }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null sheetOutlines should throw ArgumentNullException");
    }

    public static void ForV2_NullSheetHoles_Throws()
    {
        bool threw = false;
        try
        {
            _ = IrregularSheetFill.ForV2(
                sheetOutlines: Array.Empty<Curve>(),
                sheetHoles: null,
                spacing: 1.0, rotationsDeg: null, tolerance: 0.001,
                sortMode: PackingSortMode.AreaDescending,
                cornerMode: PackingCornerMode.BottomLeft,
                seed: 0, maxCandidates: 300);
        }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null sheetHoles should throw ArgumentNullException");
    }

    public static void ForV3_Construction_Succeeds()
    {
        var unified = IrregularSheetFill.ForV3(
            sheetOutlines: Array.Empty<Curve>(),
            sheetHoles: Array.Empty<IReadOnlyList<Curve>>(),
            spacing: 1.0,
            rotationsDeg: null,
            tolerance: 0.001,
            sortMode: PackingSortMode.AreaDescending,
            cornerMode: PackingCornerMode.BottomLeft,
            seed: 0,
            maxCandidates: 300);
        Assert(unified != null, "V3 façade should construct");
        Assert(unified.Variant == SolverVariant.V3, $"variant should be V3, got {unified.Variant}");
    }

    public static void ForV3_NullSheetOutlines_Throws()
    {
        bool threw = false;
        try
        {
            _ = IrregularSheetFill.ForV3(
                sheetOutlines: null,
                sheetHoles: Array.Empty<IReadOnlyList<Curve>>(),
                spacing: 1.0, rotationsDeg: null, tolerance: 0.001,
                sortMode: PackingSortMode.AreaDescending,
                cornerMode: PackingCornerMode.BottomLeft,
                seed: 0, maxCandidates: 300);
        }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null sheetOutlines should throw ArgumentNullException");
    }

    public static void ForV3_NullSheetHoles_Throws()
    {
        bool threw = false;
        try
        {
            _ = IrregularSheetFill.ForV3(
                sheetOutlines: Array.Empty<Curve>(),
                sheetHoles: null,
                spacing: 1.0, rotationsDeg: null, tolerance: 0.001,
                sortMode: PackingSortMode.AreaDescending,
                cornerMode: PackingCornerMode.BottomLeft,
                seed: 0, maxCandidates: 300);
        }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null sheetHoles should throw ArgumentNullException");
    }

    public static void V2_Facade_Equals_Legacy_OnEmptyInputs()
    {
        var legacy = new Frahan.GH.TwoD.IrregularSheetFillV2(
            sheetOutlines: Array.Empty<Curve>(),
            sheetHoles: Array.Empty<IReadOnlyList<Curve>>(),
            spacing: 1.0, rotationsDeg: null, tolerance: 0.001,
            sortMode: PackingSortMode.AreaDescending,
            cornerMode: PackingCornerMode.BottomLeft,
            seed: 0, maxCandidates: 300);
        var unified = IrregularSheetFill.ForV2(
            sheetOutlines: Array.Empty<Curve>(),
            sheetHoles: Array.Empty<IReadOnlyList<Curve>>(),
            spacing: 1.0, rotationsDeg: null, tolerance: 0.001,
            sortMode: PackingSortMode.AreaDescending,
            cornerMode: PackingCornerMode.BottomLeft,
            seed: 0, maxCandidates: 300);
        AssertResultsEquivalent(legacy.Pack(null), unified.Pack(null), "V2 empty-inputs case");
    }

    public static void V3_Facade_Equals_Legacy_OnEmptyInputs()
    {
        var legacy = new Frahan.GH.TwoD.IrregularSheetFillV3(
            sheetOutlines: Array.Empty<Curve>(),
            sheetHoles: Array.Empty<IReadOnlyList<Curve>>(),
            spacing: 1.0, rotationsDeg: null, tolerance: 0.001,
            sortMode: PackingSortMode.AreaDescending,
            cornerMode: PackingCornerMode.BottomLeft,
            seed: 0, maxCandidates: 300);
        var unified = IrregularSheetFill.ForV3(
            sheetOutlines: Array.Empty<Curve>(),
            sheetHoles: Array.Empty<IReadOnlyList<Curve>>(),
            spacing: 1.0, rotationsDeg: null, tolerance: 0.001,
            sortMode: PackingSortMode.AreaDescending,
            cornerMode: PackingCornerMode.BottomLeft,
            seed: 0, maxCandidates: 300);
        AssertResultsEquivalent(legacy.Pack(null), unified.Pack(null), "V3 empty-inputs case");
    }

    // -- V506 façade-vs-legacy equivalence (Rhino runtime) ------------------

    public static void V506_Facade_Equals_Legacy_OnEmptyInputs()
    {
        var legacy = new Frahan.GH.TwoD.IrregularSheetFillV506(
            sheetOutlines: Array.Empty<Curve>(),
            sheetHoles: Array.Empty<IReadOnlyList<Curve>>(),
            spacing: 1.0,
            rotationsDeg: null,
            tolerance: 0.001,
            sortMode: PackingSortMode.AreaDescending,
            cornerMode: PackingCornerMode.BottomLeft,
            seed: 0,
            maxCandidates: 300);

        var unified = IrregularSheetFill.ForV506(
            sheetOutlines: Array.Empty<Curve>(),
            sheetHoles: Array.Empty<IReadOnlyList<Curve>>(),
            spacing: 1.0,
            rotationsDeg: null,
            tolerance: 0.001,
            sortMode: PackingSortMode.AreaDescending,
            cornerMode: PackingCornerMode.BottomLeft,
            seed: 0,
            maxCandidates: 300);

        var legacyResult = legacy.Pack(null);
        var unifiedResult = unified.Pack(null);

        AssertResultsEquivalent(legacyResult, unifiedResult, "empty-inputs case");
    }

    public static void V1_Facade_Equals_Legacy_OnEmptyInputs()
    {
        // R3 PR 2 fixture #1 of 6: empty-inputs equivalence for V1
        // (IrregularSheetFillRhino). V1 has two extra knobs (simplifyCurves,
        // simplifyTolerance) and cornerMode is the last constructor arg.
        var legacy = new Frahan.GH.TwoD.IrregularSheetFillRhino(
            sheetOutlines: Array.Empty<Curve>(),
            sheetHoles: Array.Empty<IReadOnlyList<Curve>>(),
            spacing: 1.0,
            rotationsDeg: null,
            tolerance: 0.001,
            sortMode: PackingSortMode.AreaDescending,
            simplifyCurves: false,
            simplifyTolerance: 0.001,
            seed: 0,
            maxCandidates: 300,
            cornerMode: PackingCornerMode.BottomLeft);

        var unified = IrregularSheetFill.ForV1(
            sheetOutlines: Array.Empty<Curve>(),
            sheetHoles: Array.Empty<IReadOnlyList<Curve>>(),
            spacing: 1.0,
            rotationsDeg: null,
            tolerance: 0.001,
            sortMode: PackingSortMode.AreaDescending,
            simplifyCurves: false,
            simplifyTolerance: 0.001,
            seed: 0,
            maxCandidates: 300,
            cornerMode: PackingCornerMode.BottomLeft);

        var legacyResult = legacy.Pack(null);
        var unifiedResult = unified.Pack(null);

        AssertResultsEquivalent(legacyResult, unifiedResult, "V1 empty-inputs case");
    }

    // -- ForVariant routing (R3 PR 6 foundation) ---------------------------

    public static void ForVariant_V1_Routes_To_V1_Facade()
    {
        var unified = IrregularSheetFill.ForVariant(
            variant: SolverVariant.V1,
            sheetOutlines: Array.Empty<Curve>(),
            sheetHoles: Array.Empty<IReadOnlyList<Curve>>(),
            spacing: 1.0, rotationsDeg: null, tolerance: 0.001,
            sortMode: PackingSortMode.AreaDescending,
            cornerMode: PackingCornerMode.BottomLeft,
            seed: 0, maxCandidates: 300);
        Assert(unified != null, "ForVariant should return non-null");
        Assert(unified.Variant == SolverVariant.V1, $"variant should be V1, got {unified.Variant}");
    }

    public static void ForVariant_V2_Routes_To_V2_Facade()
    {
        var unified = IrregularSheetFill.ForVariant(
            variant: SolverVariant.V2,
            sheetOutlines: Array.Empty<Curve>(),
            sheetHoles: Array.Empty<IReadOnlyList<Curve>>(),
            spacing: 1.0, rotationsDeg: null, tolerance: 0.001,
            sortMode: PackingSortMode.AreaDescending,
            cornerMode: PackingCornerMode.BottomLeft,
            seed: 0, maxCandidates: 300);
        Assert(unified.Variant == SolverVariant.V2, $"variant should be V2, got {unified.Variant}");
    }

    public static void ForVariant_V3_Routes_To_V3_Facade()
    {
        var unified = IrregularSheetFill.ForVariant(
            variant: SolverVariant.V3,
            sheetOutlines: Array.Empty<Curve>(),
            sheetHoles: Array.Empty<IReadOnlyList<Curve>>(),
            spacing: 1.0, rotationsDeg: null, tolerance: 0.001,
            sortMode: PackingSortMode.AreaDescending,
            cornerMode: PackingCornerMode.BottomLeft,
            seed: 0, maxCandidates: 300);
        Assert(unified.Variant == SolverVariant.V3, $"variant should be V3, got {unified.Variant}");
    }

    public static void ForVariant_V506_Routes_To_V506_Facade()
    {
        var unified = IrregularSheetFill.ForVariant(
            variant: SolverVariant.V506,
            sheetOutlines: Array.Empty<Curve>(),
            sheetHoles: Array.Empty<IReadOnlyList<Curve>>(),
            spacing: 1.0, rotationsDeg: null, tolerance: 0.001,
            sortMode: PackingSortMode.AreaDescending,
            cornerMode: PackingCornerMode.BottomLeft,
            seed: 0, maxCandidates: 300);
        Assert(unified.Variant == SolverVariant.V506, $"variant should be V506, got {unified.Variant}");
    }

    public static void ForVariant_Invalid_Throws()
    {
        bool threw = false;
        try
        {
            _ = IrregularSheetFill.ForVariant(
                variant: (SolverVariant)999,
                sheetOutlines: Array.Empty<Curve>(),
                sheetHoles: Array.Empty<IReadOnlyList<Curve>>(),
                spacing: 1.0, rotationsDeg: null, tolerance: 0.001,
                sortMode: PackingSortMode.AreaDescending,
                cornerMode: PackingCornerMode.BottomLeft,
                seed: 0, maxCandidates: 300);
        }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "invalid SolverVariant should throw ArgumentOutOfRangeException");
    }

    // -- Equivalence assertion (used by every fixture once R3 PR 2 - 4 land) -

    /// <summary>
    /// Field-by-field equivalence check per the R3 plan section 4.2 tolerance
    /// table. Made internal-static so future R3 PRs can reuse it.
    /// </summary>
    private static void AssertResultsEquivalent(PackingResult legacy, PackingResult unified, string label)
    {
        if (legacy == null) throw new ArgumentNullException(nameof(legacy));
        if (unified == null) throw new ArgumentNullException(nameof(unified));

        Assert(legacy.InputCount == unified.InputCount,
            $"{label}: InputCount differs: legacy={legacy.InputCount}, unified={unified.InputCount}");
        Assert(legacy.PreparedCount == unified.PreparedCount,
            $"{label}: PreparedCount differs: legacy={legacy.PreparedCount}, unified={unified.PreparedCount}");
        Assert(legacy.InvalidCount == unified.InvalidCount,
            $"{label}: InvalidCount differs: legacy={legacy.InvalidCount}, unified={unified.InvalidCount}");
        Assert(legacy.SheetPreviewCurves.Count == unified.SheetPreviewCurves.Count,
            $"{label}: SheetPreviewCurves count differs");

        // Future R3 PRs add per-placement (Item.Id, Transform translation,
        // YawDegrees) checks here. Skeleton tolerances per the R3 plan:
        //   - Placements.Count: exact equality (CRITICAL)
        //   - Failures.Count:   exact equality (CRITICAL)
        //   - Yield:            abs diff < 1e-9
        //   - Per-placement Item.Id: exact equality (ordering matters)
        //   - Per-placement transform translation X/Y: abs diff < 1e-6
        //   - Per-placement Yaw: abs diff < 1e-9
        //   - Per-failure Reason: exact equality
        // The full assertion will land in R3 PR 2.
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
