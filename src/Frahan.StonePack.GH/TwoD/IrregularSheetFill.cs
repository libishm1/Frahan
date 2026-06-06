using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Rhino.Geometry;

namespace Frahan.GH.TwoD;

/// <summary>
/// Algorithm variant selector for the unified <see cref="IrregularSheetFill"/>.
///
/// Today these correspond 1:1 with the legacy V-suffixed solver classes:
///   - V1    -> IrregularSheetFillRhino    (polyline-only)
///   - V2    -> IrregularSheetFillV2       (freeform; GH_TaskCapableComponent)
///   - V3    -> IrregularSheetFillV3       (adaptive ToPolyline, non-convex)
///   - V506  -> IrregularSheetFillV506     (current 0.5.6 candidate)
///
/// Per refactor R3 (see docs/future_work/R3_versioned_solver_collapse_plan.md),
/// the four legacy classes will be inlined into this one over PRs 2 - 5;
/// today this entry point is a thin façade that delegates to whichever V-class
/// you select.
/// </summary>
public enum SolverVariant
{
    V1,
    V2,
    V3,
    V506,
}

/// <summary>
/// **R3 PR 1 stub** - unified entry point for all four 2D irregular-sheet
/// solver variants. Today V506 is fully wired; V1, V2, V3 throw
/// <see cref="NotImplementedException"/> until refactor R3 PRs 2 - 4 inline
/// the legacy class bodies. The throwing variants surface the migration
/// status loudly so callers cannot silently rely on a not-yet-wired path.
///
/// See <c>docs/future_work/R3_versioned_solver_collapse_plan.md</c> for the
/// full migration plan, equivalence-test design, and per-PR file lists.
/// </summary>
public sealed class IrregularSheetFill
{
    private readonly SolverVariant _variant;
    private readonly Func<IEnumerable<Curve>?, CancellationToken, PackingResult> _packDelegate;

    private IrregularSheetFill(
        SolverVariant variant,
        Func<IEnumerable<Curve>?, CancellationToken, PackingResult> packDelegate)
    {
        _variant = variant;
        _packDelegate = packDelegate ?? throw new ArgumentNullException(nameof(packDelegate));
    }

    public SolverVariant Variant => _variant;

    /// <summary>
    /// Build a unified solver for the V506 algorithm. Mirrors the
    /// <see cref="IrregularSheetFillV506"/> constructor exactly so callers
    /// can swap in this façade with one edit.
    /// </summary>
    public static IrregularSheetFill ForV506(
        IEnumerable<Curve> sheetOutlines,
        IReadOnlyList<IReadOnlyList<Curve>> sheetHoles,
        double spacing,
        IEnumerable<double>? rotationsDeg,
        double tolerance,
        PackingSortMode sortMode,
        PackingCornerMode cornerMode,
        int seed,
        int maxCandidates,
        int boundaryMode = 0,
        double minBoundaryAffinity = 0.5,
        double discretizationTolerance = -1.0,
        double trimTolerance = 0.0,
        bool qualityNfp = false)
    {
        if (sheetOutlines == null) throw new ArgumentNullException(nameof(sheetOutlines));
        if (sheetHoles == null) throw new ArgumentNullException(nameof(sheetHoles));

        var inner = new IrregularSheetFillV506(
            sheetOutlines, sheetHoles, spacing, rotationsDeg, tolerance,
            sortMode, cornerMode, seed, maxCandidates,
            boundaryMode, minBoundaryAffinity, discretizationTolerance, trimTolerance, qualityNfp);

        return new IrregularSheetFill(
            variant: SolverVariant.V506,
            packDelegate: inner.Pack);
    }

    /// <summary>
    /// Build a unified solver for the V1 algorithm. Mirrors the
    /// <see cref="IrregularSheetFillRhino"/> constructor exactly. Note that
    /// V1 has two extra knobs absent from V506 (<paramref name="simplifyCurves"/>
    /// and <paramref name="simplifyTolerance"/>) and that V1's underlying
    /// <c>Pack</c> is synchronous; the <see cref="CancellationToken"/> on
    /// the unified <see cref="Pack"/> entry point is silently ignored when
    /// the variant is V1.
    /// </summary>
    public static IrregularSheetFill ForV1(
        IEnumerable<Curve> sheetOutlines,
        IReadOnlyList<IReadOnlyList<Curve>> sheetHoles,
        double spacing,
        IEnumerable<double>? rotationsDeg,
        double tolerance,
        PackingSortMode sortMode,
        bool simplifyCurves,
        double simplifyTolerance,
        int seed,
        int maxCandidates,
        PackingCornerMode cornerMode = PackingCornerMode.BottomLeft)
    {
        if (sheetOutlines == null) throw new ArgumentNullException(nameof(sheetOutlines));
        if (sheetHoles == null) throw new ArgumentNullException(nameof(sheetHoles));

        var inner = new IrregularSheetFillRhino(
            sheetOutlines, sheetHoles, spacing, rotationsDeg, tolerance,
            sortMode, simplifyCurves, simplifyTolerance, seed, maxCandidates, cornerMode);

        return new IrregularSheetFill(
            variant: SolverVariant.V1,
            packDelegate: (curves, _ct) => inner.Pack(curves));
    }

    /// <summary>
    /// Build a unified solver for the V2 algorithm. Mirrors the
    /// <see cref="IrregularSheetFillV2"/> constructor exactly (same 9-arg
    /// shape as V506; cornerMode in the middle). V2's Pack accepts a
    /// <see cref="CancellationToken"/> natively; the unified Pack token is
    /// forwarded.
    /// </summary>
    public static IrregularSheetFill ForV2(
        IEnumerable<Curve> sheetOutlines,
        IReadOnlyList<IReadOnlyList<Curve>> sheetHoles,
        double spacing,
        IEnumerable<double>? rotationsDeg,
        double tolerance,
        PackingSortMode sortMode,
        PackingCornerMode cornerMode,
        int seed,
        int maxCandidates)
    {
        if (sheetOutlines == null) throw new ArgumentNullException(nameof(sheetOutlines));
        if (sheetHoles == null) throw new ArgumentNullException(nameof(sheetHoles));

        var inner = new IrregularSheetFillV2(
            sheetOutlines, sheetHoles, spacing, rotationsDeg, tolerance,
            sortMode, cornerMode, seed, maxCandidates);

        return new IrregularSheetFill(
            variant: SolverVariant.V2,
            packDelegate: inner.Pack);
    }

    /// <summary>
    /// Build a unified solver for the V3 algorithm. Mirrors the
    /// <see cref="IrregularSheetFillV3"/> constructor exactly (same 9-arg
    /// shape as V2 and V506). V3's Pack accepts a <see cref="CancellationToken"/>
    /// natively; the unified Pack token is forwarded.
    /// </summary>
    public static IrregularSheetFill ForV3(
        IEnumerable<Curve> sheetOutlines,
        IReadOnlyList<IReadOnlyList<Curve>> sheetHoles,
        double spacing,
        IEnumerable<double>? rotationsDeg,
        double tolerance,
        PackingSortMode sortMode,
        PackingCornerMode cornerMode,
        int seed,
        int maxCandidates)
    {
        if (sheetOutlines == null) throw new ArgumentNullException(nameof(sheetOutlines));
        if (sheetHoles == null) throw new ArgumentNullException(nameof(sheetHoles));

        var inner = new IrregularSheetFillV3(
            sheetOutlines, sheetHoles, spacing, rotationsDeg, tolerance,
            sortMode, cornerMode, seed, maxCandidates);

        return new IrregularSheetFill(
            variant: SolverVariant.V3,
            packDelegate: inner.Pack);
    }

    /// <summary>
    /// Variant-routing convenience used by the unified
    /// <c>IrregularSheetFillComponent</c> (R3 PR 6). Accepts the V2/V3/V506
    /// 9-arg shape and dispatches to the right ForV* factory. V1 is
    /// supported but its two extra knobs (<c>simplifyCurves</c>,
    /// <c>simplifyTolerance</c>) are hard-coded — callers needing V1's
    /// curve-simplification features should use <see cref="ForV1"/>
    /// directly. Default V1 settings: <c>simplifyCurves=false,
    /// simplifyTolerance=tolerance</c>.
    /// </summary>
    public static IrregularSheetFill ForVariant(
        SolverVariant variant,
        IEnumerable<Curve> sheetOutlines,
        IReadOnlyList<IReadOnlyList<Curve>> sheetHoles,
        double spacing,
        IEnumerable<double>? rotationsDeg,
        double tolerance,
        PackingSortMode sortMode,
        PackingCornerMode cornerMode,
        int seed,
        int maxCandidates,
        int boundaryMode = 0,
        double minBoundaryAffinity = 0.5,
        double discretizationTolerance = -1.0,
        double trimTolerance = 0.0,
        bool qualityNfp = false)
    {
        switch (variant)
        {
            case SolverVariant.V1:
                // Boundary mode + discretization tolerance + trim tolerance
                // are V506-only initially. V1 ignores them silently;
                // document this in known_failures.md.
                return ForV1(sheetOutlines, sheetHoles, spacing, rotationsDeg, tolerance,
                    sortMode, simplifyCurves: false, simplifyTolerance: tolerance,
                    seed, maxCandidates, cornerMode);
            case SolverVariant.V2:
                return ForV2(sheetOutlines, sheetHoles, spacing, rotationsDeg, tolerance,
                    sortMode, cornerMode, seed, maxCandidates);
            case SolverVariant.V3:
                return ForV3(sheetOutlines, sheetHoles, spacing, rotationsDeg, tolerance,
                    sortMode, cornerMode, seed, maxCandidates);
            case SolverVariant.V506:
                return ForV506(sheetOutlines, sheetHoles, spacing, rotationsDeg, tolerance,
                    sortMode, cornerMode, seed, maxCandidates,
                    boundaryMode, minBoundaryAffinity, discretizationTolerance, trimTolerance, qualityNfp);
            default:
                throw new ArgumentOutOfRangeException(nameof(variant),
                    $"Unknown SolverVariant: {variant}");
        }
    }

    /// <summary>
    /// Pack the supplied input curves into the configured sheets. Same
    /// contract as the legacy solver's Pack methods: returns a
    /// <see cref="PackingResult"/> with placements, failures, yield,
    /// runtime, and report.
    /// </summary>
    public PackingResult Pack(IEnumerable<Curve>? inputCurves, CancellationToken ct = default)
    {
        return _packDelegate(inputCurves, ct);
    }
}
