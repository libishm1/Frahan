#nullable disable
using System;
using Rhino.Geometry;

namespace Frahan.Core.ScanIngest;

// =============================================================================
// ScaleCalibration — Phase F3 of the UX architecture report §7.7.A scan
// ingest rollout. Closes UX report §6.6 friction: "Photogrammetry
// meshes arrive unitless or in pipeline-specific units. The user
// eyeballs a reference object and pre-scales in Rhino..."
//
// Use case: the user marks a known real-world distance in the scan
// (e.g. they placed a 1.000 m ruler on the bench, picks the two end
// points after import → measured curve). Given the real-world length,
// derive the uniform scale factor that brings the scan into the
// chosen unit system.
//
// Pure managed, depends only on RhinoCommon's managed Transform / Curve
// API. No third-party deps, no native shim.
// =============================================================================

/// <summary>
/// Result of a scan scale-calibration solve. Carries the uniform scale
/// factor, the Rhino <see cref="Transform"/> ready to apply, the
/// measured length found in the scan, and a short human-readable
/// summary.
/// </summary>
public sealed class ScaleCalibrationResult
{
    public ScaleCalibrationResult(
        double scaleFactor, Transform scaleTransform,
        double measuredLength, double referenceLength,
        string reportedUnits)
    {
        if (double.IsNaN(scaleFactor) || double.IsInfinity(scaleFactor) || scaleFactor <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(scaleFactor),
                $"scale factor must be a positive finite number, got {scaleFactor}");
        ScaleFactor = scaleFactor;
        ScaleTransform = scaleTransform;
        MeasuredLength = measuredLength;
        ReferenceLength = referenceLength;
        ReportedUnits = reportedUnits ?? string.Empty;
    }

    /// <summary>Uniform scale factor (target / source).</summary>
    public double ScaleFactor { get; }
    /// <summary>Rhino transform built from <see cref="ScaleFactor"/>
    /// centred at the world origin.</summary>
    public Transform ScaleTransform { get; }
    /// <summary>Length of the measured curve in source-frame units.</summary>
    public double MeasuredLength { get; }
    /// <summary>The real-world reference length the user supplied.</summary>
    public double ReferenceLength { get; }
    /// <summary>Free-form unit label (e.g. "m", "mm", "ft") for the report
    /// output. Math is unit-agnostic; this is for display only.</summary>
    public string ReportedUnits { get; }

    public override string ToString() =>
        $"ScaleFactor={ScaleFactor:0.######} (measured {MeasuredLength:0.######} → target {ReferenceLength:0.######} {ReportedUnits})";
}

/// <summary>
/// Static façade. All methods deterministic, pure-managed, no Rhino-
/// native dependency.
/// </summary>
public static class ScaleCalibration
{
    /// <summary>
    /// Solve the scale factor given an in-scan reference curve whose
    /// real-world length is known.
    /// </summary>
    /// <param name="measuredCurveLength">Length of the curve as currently
    /// stored in the scan (model units).</param>
    /// <param name="referenceLength">The real-world length the curve
    /// should map to (in the user's target unit system).</param>
    /// <param name="reportedUnits">Free-form label for the report
    /// output (e.g. "m").</param>
    /// <exception cref="ArgumentOutOfRangeException">If either length is
    /// non-positive or non-finite.</exception>
    public static ScaleCalibrationResult Solve(
        double measuredCurveLength,
        double referenceLength,
        string reportedUnits = "m")
    {
        if (double.IsNaN(measuredCurveLength) || double.IsInfinity(measuredCurveLength)
            || measuredCurveLength <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(measuredCurveLength),
                $"measured curve length must be a positive finite number, got {measuredCurveLength}");
        if (double.IsNaN(referenceLength) || double.IsInfinity(referenceLength)
            || referenceLength <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(referenceLength),
                $"reference length must be a positive finite number, got {referenceLength}");

        double factor = referenceLength / measuredCurveLength;
        var xform = Transform.Scale(Plane.WorldXY, factor, factor, factor);
        return new ScaleCalibrationResult(
            scaleFactor: factor,
            scaleTransform: xform,
            measuredLength: measuredCurveLength,
            referenceLength: referenceLength,
            reportedUnits: reportedUnits);
    }

    /// <summary>
    /// Convenience overload: pass the actual <see cref="Curve"/> object
    /// (e.g. a polyline picked from the canvas). The curve's length is
    /// computed via the Rhino-managed
    /// <see cref="Curve.GetLength()"/> method.
    /// </summary>
    public static ScaleCalibrationResult SolveFromCurve(
        Curve measuredCurve,
        double referenceLength,
        string reportedUnits = "m")
    {
        if (measuredCurve == null) throw new ArgumentNullException(nameof(measuredCurve));
        double length = measuredCurve.GetLength();
        return Solve(length, referenceLength, reportedUnits);
    }
}
