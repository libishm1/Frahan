#nullable disable
using System;

namespace Frahan.Core.Sculpt;

// =============================================================================
// SculptureFitter — runtime-agnostic math for the "digital pointing machine".
//
// The pointing machine is the classical sculptor's tool for transferring and
// SCALING a maquette to a full-size carving. Its digital equivalent: scan a
// maquette, enlarge it to the final size, and verify it fits inside an
// available raw block before committing to carve. This class holds the pure
// arithmetic (no Rhino types): the enlargement factors and the does-it-fit
// check. The GH layer supplies extents measured from RhinoCommon meshes.
// =============================================================================

public enum EnlargeMode
{
    /// <summary>Multiply every axis by Value.</summary>
    Factor = 0,
    /// <summary>Scale uniformly so the LONGEST axis becomes Value.</summary>
    TargetLongest = 1,
    /// <summary>Scale uniformly so the Z (height) axis becomes Value.</summary>
    TargetHeight = 2,
    /// <summary>Scale each axis independently to TargetXyz (non-uniform).</summary>
    NonUniformXyz = 3,
}

public sealed class FitResult
{
    public bool Fits;
    /// <summary>Per-(sorted)-axis slack: block_extent - sculpture_extent. Negative = overflow.</summary>
    public double[] Clearance = new double[3];
    /// <summary>Smallest clearance across the three axes (most binding).</summary>
    public double MinClearance;
    /// <summary>Linear scale that would make the sculpture just fit (>=1 means it already fits).</summary>
    public double MaxScaleToFit;
}

public static class SculptureFitter
{
    /// <summary>
    /// Per-axis enlargement factors for the given mode. <paramref name="size"/>
    /// is the current bounding size (x,y,z, all &gt; 0). For uniform modes the
    /// three returned factors are equal.
    /// </summary>
    public static double[] EnlargeFactors(
        double[] size, EnlargeMode mode, double value, double[] targetXyz)
    {
        if (size == null || size.Length != 3) throw new ArgumentException("size must be length 3");
        double sx = size[0], sy = size[1], sz = size[2];
        switch (mode)
        {
            case EnlargeMode.Factor:
                return new[] { value, value, value };
            case EnlargeMode.TargetLongest:
            {
                double longest = Math.Max(sx, Math.Max(sy, sz));
                double f = longest > 0 ? value / longest : 1.0;
                return new[] { f, f, f };
            }
            case EnlargeMode.TargetHeight:
            {
                double f = sz > 0 ? value / sz : 1.0;
                return new[] { f, f, f };
            }
            case EnlargeMode.NonUniformXyz:
            {
                if (targetXyz == null || targetXyz.Length != 3)
                    throw new ArgumentException("NonUniformXyz needs targetXyz length 3");
                return new[]
                {
                    sx > 0 ? targetXyz[0] / sx : 1.0,
                    sy > 0 ? targetXyz[1] / sy : 1.0,
                    sz > 0 ? targetXyz[2] / sz : 1.0,
                };
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    /// <summary>
    /// Does a sculpture of the given oriented extents fit inside a block of the
    /// given oriented extents? Both are matched largest-axis-to-largest-axis
    /// (the best axis-aligned orientation), so this answers "is there ANY
    /// orientation that fits" for box-aligned placement. Clearance is reported
    /// per sorted axis. <paramref name="margin"/> is subtracted from each block
    /// extent (saw kerf / roughing allowance / handling clearance).
    /// </summary>
    public static FitResult FitsInBlock(double[] sculptExtents, double[] blockExtents, double margin = 0.0)
    {
        if (sculptExtents == null || sculptExtents.Length != 3) throw new ArgumentException("sculptExtents length 3");
        if (blockExtents == null || blockExtents.Length != 3) throw new ArgumentException("blockExtents length 3");

        double[] s = SortDesc(sculptExtents);
        double[] b = SortDesc(blockExtents);

        var r = new FitResult();
        double minClear = double.PositiveInfinity;
        double minScale = double.PositiveInfinity;
        for (int i = 0; i < 3; i++)
        {
            double avail = b[i] - 2.0 * margin;       // margin on both sides
            r.Clearance[i] = avail - s[i];
            if (r.Clearance[i] < minClear) minClear = r.Clearance[i];
            double scale = s[i] > 1e-12 ? avail / s[i] : double.PositiveInfinity;
            if (scale < minScale) minScale = scale;
        }
        r.MinClearance = minClear;
        r.MaxScaleToFit = minScale;
        r.Fits = minClear >= 0.0;
        return r;
    }

    private static double[] SortDesc(double[] v)
    {
        double a = v[0], b = v[1], c = v[2];
        // sort three descending
        if (a < b) { var t = a; a = b; b = t; }
        if (b < c) { var t = b; b = c; c = t; }
        if (a < b) { var t = a; a = b; b = t; }
        return new[] { a, b, c };
    }
}
