#nullable disable
using System;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// BlockCutOptTolerances -- single source of truth for every numerical
// tolerance, default, and unit referenced by Frahan.BlockCutOpt v2.
//
// All world coordinates are in METRES unless explicitly named otherwise. This
// matches the Rhino model-unit default for engineering models. Where a paper
// uses millimetres (the Shao 2022 sawblade), the conversion is documented
// inline and a helper is provided.
//
// Reference: `D:\code_ws\wiki\papers\equations_and_diagrams\14_tolerances_and_units.md`
// holds the full justification; this file is the runtime API.
// =============================================================================

public static class BlockCutOptTolerances
{
    // ─── World units ────────────────────────────────────────────────────────

    /// <summary>Default world unit is the metre.</summary>
    public const double UnitMetre = 1.0;

    /// <summary>One millimetre in metres.</summary>
    public const double UnitMm = 1.0e-3;

    /// <summary>One centimetre in metres.</summary>
    public const double UnitCm = 1.0e-2;

    /// <summary>One nanosecond in seconds (used by GPR papers).</summary>
    public const double UnitNs = 1.0e-9;

    // ─── BlockCutOpt 2020 (Elkarmoty et al., Resources Policy 68) ───────────

    /// <summary>
    /// Material-lost-by-quarrying default = 50 mm = 0.05 m, used in both case
    /// studies of the published BlockCutOpt paper (limestone and granite).
    /// </summary>
    public const double KerfDefaultMetres = 0.05;

    /// <summary>
    /// Default angular sweep step = 3 degrees = 0.05236 rad (limestone +
    /// granite cases of BlockCutOpt 2020).
    /// </summary>
    public const double PsiStepDefaultRad = 3.0 * Math.PI / 180.0;

    /// <summary>
    /// Default horizontal displacement step for limestone-bench-scale
    /// problems: 0.5 m. The granite-LVB-scale problem uses 7 m in X and
    /// 5 m in Y; pass those explicitly when scaling up.
    /// </summary>
    public const double DxyStepLimestoneDefault = 0.5;

    /// <summary>Default coarse step for Phase 4 coarse-to-fine angular search: 12 deg.</summary>
    public const double PsiStepCoarseRad = 12.0 * Math.PI / 180.0;

    /// <summary>Default medium step for Phase 4: 3 deg.</summary>
    public const double PsiStepMediumRad = 3.0 * Math.PI / 180.0;

    /// <summary>Default fine step for Phase 4: 0.5 deg.</summary>
    public const double PsiStepFineRad = 0.5 * Math.PI / 180.0;

    // ─── Geometric epsilon (intersection tests) ─────────────────────────────

    /// <summary>
    /// Default geometric epsilon used by ObbTriangleIntersection SAT axis-
    /// length tests: 1e-12. Matches the existing project convention; safe for
    /// metre-scale models with sub-mm precision.
    /// </summary>
    public const double GeometricEps = 1.0e-12;

    /// <summary>
    /// Coincident-vertex de-duplication tolerance (used by
    /// JointSetDfnPlyEmitter to drop degenerate edges): 1e-9 m = 1 nm.
    /// </summary>
    public const double VertexDedupeTol = 1.0e-9;

    // ─── Ornamental-stone block tolerances (Elkarmoty thesis Chapter 6/7) ──

    /// <summary>
    /// Half-kerf added to a block's nominal dimension in the published PAR
    /// files: 0.025 m = 25 mm. So a 3.0 m block stored as 3.025 m on each
    /// axis (kerf is shared between adjacent blocks).
    /// </summary>
    public const double HalfKerfMetres = 0.025;

    // ─── GPR-related (Elkarmoty thesis Ch 2-4, ConBuildMat 2018) ────────────

    /// <summary>Free-space EM velocity, c = 299,792,458 m/s.</summary>
    public const double SpeedOfLightMps = 299_792_458.0;

    /// <summary>
    /// Bulk relative permittivity range observed in Apricena limestone (thesis
    /// Ch 4): 7.4 to 7.9. Used for GPR depth conversion ONLY; not consumed by
    /// Frahan.BlockCutOpt itself but documented here as the upstream
    /// acquisition tolerance.
    /// </summary>
    public const double LimestoneEpsRMin = 7.4;
    public const double LimestoneEpsRMax = 7.9;

    /// <summary>Fracture-aperture default for the limestone block (ConBuildMat 2018): 1.5-2.0 mm.</summary>
    public const double LimestoneFractureApertureMmMin = 1.5;
    public const double LimestoneFractureApertureMmMax = 2.0;

    // ─── Shao 2022 (Processes 10:695) -- in-block secondary cutting ─────────

    /// <summary>
    /// Default sawblade radius for the Shao planner: 100 mm = 0.1 m. The
    /// Shao paper uses millimetres; the Frahan planner converts on entry
    /// using UnitMm.
    /// </summary>
    public const double SawBladeRadiusMmDefault = 100.0;

    /// <summary>Default feeding speed for Shao AMRR: 50 mm/min.</summary>
    public const double FeedSpeedMmPerMinDefault = 50.0;

    /// <summary>AMRR convergence tolerance: stop when relative volume reduction is below 0.1 percent.</summary>
    public const double AmrrConvergenceFraction = 1.0e-3;

    // ─── Photogrammetry / GeoFractNet (Ansari 2024) ─────────────────────────

    /// <summary>Default GSD for UAV photogrammetry of a bench: 2 cm/px = 0.02 m/px.</summary>
    public const double GsdBenchUavMetresPerPx = 0.02;

    /// <summary>Default GSD for smartphone close-range photogrammetry: 5 mm/px = 5e-3 m/px.</summary>
    public const double GsdCloseRangePhoneMetresPerPx = 5.0e-3;

    /// <summary>GeoFractNet input patch size in pixels.</summary>
    public const int GeoFractNetPatchPx = 224;

    // ─── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Convert degrees to radians.</summary>
    public static double DegToRad(double deg) => deg * Math.PI / 180.0;

    /// <summary>Convert radians to degrees.</summary>
    public static double RadToDeg(double rad) => rad * 180.0 / Math.PI;

    /// <summary>Convert millimetres to metres.</summary>
    public static double MmToMetres(double mm) => mm * UnitMm;

    /// <summary>Convert metres to millimetres.</summary>
    public static double MetresToMm(double m) => m * 1000.0;

    /// <summary>
    /// Adapter for a Rhino model with non-metre document units. Pass in
    /// Rhino's <c>UnitSystemFactor</c> (metres per Rhino unit) to translate
    /// any default tolerance above into the active Rhino model's space.
    /// Example: model in millimetres -> rhinoMetresPerUnit = 1e-3.
    /// </summary>
    public static double ToRhinoUnit(double metres, double rhinoMetresPerUnit)
    {
        if (!(rhinoMetresPerUnit > 0)) throw new ArgumentOutOfRangeException(nameof(rhinoMetresPerUnit));
        return metres / rhinoMetresPerUnit;
    }
}
