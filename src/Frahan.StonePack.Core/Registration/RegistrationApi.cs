#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Geometry;
using Rhino.Geometry;

namespace Frahan.Core.Registration;

// =============================================================================
// RegistrationApi — public Rhino-friendly façade over RigidTransformRecovery
// (Horn 1987 absolute orientation, in Frahan.Masonry.Geometry).
//
// Phase I1 of the UX architecture report's §7.7.F rollout. The existing
// edge-matching solver has a private Kabsch3D static (in
// Frahan.EdgeMatching.Core/ConstrainedIcp3D.cs) that uses MathNet.Numerics
// SVD. RigidTransformRecovery in this same Core project uses Horn's
// quaternion method (4x4 Jacobi eigendecomposition) and produces a
// numerically equivalent result without an SVD dependency.
//
// This façade exposes the existing Horn implementation in a shape that
// GH components and downstream callers can use without flattening points
// to double[] arrays by hand.
//
// Use cases:
//   - Marker-based registration (N≥3 corresponding point pairs).
//   - Reference-object / starting-block registration (same math).
//   - Georeferenced registration (after LLH→ECEF→ENU via GeoreferenceMath).
//   - Per-iteration Kabsch step inside a future PointCloudIcp (Phase I10).
// =============================================================================

/// <summary>
/// Result of a Rhino-friendly rigid registration. Wraps a
/// <see cref="Transform"/> alongside the RMS residual and per-pair
/// residual distances after applying the transform.
/// </summary>
public sealed class RegistrationResult
{
    public RegistrationResult(Transform xform, double rmsError, double[] perPairResiduals)
    {
        if (perPairResiduals == null) throw new ArgumentNullException(nameof(perPairResiduals));
        if (rmsError < 0.0) throw new ArgumentOutOfRangeException(nameof(rmsError));
        Transform = xform;
        RmsError = rmsError;
        PerPairResiduals = perPairResiduals;
    }

    public Transform Transform { get; }
    public double RmsError { get; }
    public double[] PerPairResiduals { get; }
}

/// <summary>
/// Public registration façade. All methods are deterministic, pure-
/// managed, and depend only on the existing RigidTransformRecovery
/// (Horn 1987). No SVD, no third-party native code, no Rhino runtime.
/// </summary>
public static class RegistrationApi
{
    /// <summary>
    /// Closed-form rigid alignment of source points onto target points.
    /// Solves min Σ ‖ R·sᵢ + t − tᵢ ‖² over (R, t) for paired-by-index
    /// source / target lists. Requires N≥3 non-collinear pairs.
    /// </summary>
    /// <param name="source">Source (scan-frame) points.</param>
    /// <param name="target">Target (world-frame) points. Same count as source.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="ArgumentException">Counts differ or N&lt;3.</exception>
    public static RegistrationResult SolveFromPoints(
        IReadOnlyList<Point3d> source,
        IReadOnlyList<Point3d> target)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (source.Count != target.Count)
            throw new ArgumentException(
                $"source and target must have the same point count; got {source.Count} vs {target.Count}",
                nameof(target));
        if (source.Count < 3)
            throw new ArgumentException(
                $"need at least 3 point pairs to recover a rigid transform, got {source.Count}",
                nameof(source));

        var srcFlat = new double[3 * source.Count];
        var dstFlat = new double[3 * target.Count];
        for (int i = 0; i < source.Count; i++)
        {
            srcFlat[3 * i + 0] = source[i].X;
            srcFlat[3 * i + 1] = source[i].Y;
            srcFlat[3 * i + 2] = source[i].Z;
            dstFlat[3 * i + 0] = target[i].X;
            dstFlat[3 * i + 1] = target[i].Y;
            dstFlat[3 * i + 2] = target[i].Z;
        }

        var inner = RigidTransformRecovery.Solve(srcFlat, dstFlat);

        var xform = ToRhinoTransform(inner.Rotation, inner.Translation);

        var perPair = new double[source.Count];
        for (int i = 0; i < source.Count; i++)
        {
            var p = source[i];
            p.Transform(xform);
            perPair[i] = p.DistanceTo(target[i]);
        }

        return new RegistrationResult(xform, inner.RmsError, perPair);
    }

    /// <summary>
    /// Convert a Horn-style row-major 3x3 rotation + length-3 translation
    /// into a Rhino <see cref="Transform"/>. Bottom row stays (0,0,0,1).
    /// </summary>
    public static Transform ToRhinoTransform(double[,] rotation, double[] translation)
    {
        if (rotation == null) throw new ArgumentNullException(nameof(rotation));
        if (translation == null) throw new ArgumentNullException(nameof(translation));
        if (rotation.GetLength(0) != 3 || rotation.GetLength(1) != 3)
            throw new ArgumentException("rotation must be 3x3", nameof(rotation));
        if (translation.Length != 3)
            throw new ArgumentException("translation must be length 3", nameof(translation));

        var t = Transform.Identity;
        t.M00 = rotation[0, 0]; t.M01 = rotation[0, 1]; t.M02 = rotation[0, 2]; t.M03 = translation[0];
        t.M10 = rotation[1, 0]; t.M11 = rotation[1, 1]; t.M12 = rotation[1, 2]; t.M13 = translation[1];
        t.M20 = rotation[2, 0]; t.M21 = rotation[2, 1]; t.M22 = rotation[2, 2]; t.M23 = translation[2];
        return t;
    }
}
