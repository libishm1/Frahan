#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Frahan.Core.Discontinuity;

// =============================================================================
// CuboidFrameFit -- the dolerite ("black granite") cuboid-frame prior on top of
// the Watson mean-shift set clusterer (DiscontinuitySetClusterer.cs). Fits ONE
// rotation R in SO(3) so the joint-family poles sit near the frame axes +-R e_i,
// then GATES the cube behind a per-family misfit test: keep the cube iff every
// family misfit <= max(theta_max, 2*alpha95); else FALL BACK and leave the frame
// ABSENT (never force an orthogonal cube onto oblique geology -- the DSR
// Jammanahalli "cuboidal" joints dip 36/62, not 90 apart). Columns of R are the
// three cut-plane normals, handed to the G10 staged-guillotine packer.
//
// This is the C# counterpart of pyfrahan.cluster.fit_cuboid_frame + the
// cluster_poles gate (G12). The Python was itself ported FROM this C# clusterer's
// Watson kernel; this file completes the round trip by adding the frame fit.
//
// Method (spec G12 S3): seed by enumerating the 6 permutations of the 3 strongest
// families onto the axes (a Wahba/orthogonal-Procrustes problem per permutation,
// solved by the Horn unit-quaternion engine RigidTransformRecovery.
// RotationFromCorrelation), keep the best, then refine assignment<->R over ALL
// families (nearest axis, axial signs). Deterministic, closed-form, no SVD.
//
// Pure managed (System only) -- deliberately Rhino-free so it builds and parity-
// tests headless. The Rhino-facing wiring over JointSet lives in
// SetFrameAnalyzer.cs.
// =============================================================================

public enum FrameMode
{
    /// <summary>Every family misfit passed the gate; FrameR is a valid orthogonal frame.</summary>
    Cuboid,
    /// <summary>At least one family misfit exceeded the gate; FrameR is ABSENT (null).</summary>
    Fallback,
    /// <summary>Fewer than 3 families; no cube attempted; FrameR is ABSENT (null).</summary>
    Unconstrained
}

/// <summary>
/// Result of the gated cuboid-frame fit. On <see cref="FrameMode.Cuboid"/>,
/// <see cref="FrameR"/> is a 3x3 proper rotation whose columns are the three
/// cut-plane normals. On Fallback / Unconstrained it is null (the cube is NOT
/// forced onto oblique geology). Misfits/AxisOf are still reported on Fallback
/// (the attempted fit) so callers can see why the gate fired.
/// </summary>
public sealed class CuboidFrameResult
{
    public FrameMode Mode;
    public double[,] FrameR;        // 3x3, columns = cut-plane normals; null unless Cuboid
    public double[] MisfitsDeg;     // per family: axial angle to nearest frame axis; null if <3 families
    public int[] AxisOf;            // per family: nearest frame axis 0/1/2; null if <3 families
    public double MaxMisfitDeg;     // max over families; NaN if <3 families
    public double ThetaMaxDeg;      // the gate threshold used
}

public static class CuboidFrameFit
{
    private const double R2D = 180.0 / Math.PI;

    // permutations(range(3)) in Python's lexicographic order; the seed loop keeps
    // the FIRST minimal score, so the order must match for bit-parity tie-breaks.
    private static readonly int[][] Perms6 =
    {
        new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
        new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 }
    };

    // ----------------------------------------------------------------- vec helpers
    // LowerHemisphere / AxialAngleDeg mirror OrientationMath (x=E,y=N,z=Up) but on
    // double[3] so this stays Rhino-free. Same deterministic equator tie-break.
    internal static double[] LowerHemisphere(double[] n)
    {
        double nn = Math.Sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]);
        double x = n[0], y = n[1], z = n[2];
        if (nn > 0) { x /= nn; y /= nn; z /= nn; }
        bool flip = false;
        if (z > 1e-12) flip = true;
        else if (Math.Abs(z) <= 1e-12)
        {
            if (x < -1e-12 || (Math.Abs(x) <= 1e-12 && y < 0)) flip = true;
        }
        return flip ? new[] { -x, -y, -z } : new[] { x, y, z };
    }

    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

    private static double[] Cross(double[] a, double[] b) => new[]
    {
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0]
    };

    internal static double AxialAngleDeg(double[] a, double[] b)
    {
        double na = Math.Sqrt(Dot(a, a)), nb = Math.Sqrt(Dot(b, b));
        double ax = a[0], ay = a[1], az = a[2], bx = b[0], by = b[1], bz = b[2];
        if (na > 0) { ax /= na; ay /= na; az /= na; }
        if (nb > 0) { bx /= nb; by /= nb; bz /= nb; }
        double d = Math.Abs(ax * bx + ay * by + az * bz);
        return Math.Acos(Math.Min(1.0, d)) * R2D;
    }

    private static double[] Col(double[,] R, int j) => new[] { R[0, j], R[1, j], R[2, j] };

    // ----------------------------------------------------------------- the raw fit
    /// <summary>
    /// Fit one R in SO(3) so the family poles sit near the frame axes +-R e_i.
    /// Seeds by the 6-permutation Wahba on the 3 strongest families, then refines
    /// assignment&lt;-&gt;R over ALL families. Returns R (3x3, columns = the three
    /// cut-plane normals; always a proper rotation), the per-family axial misfit
    /// (deg) to its nearest frame axis, and that axis index (0/1/2).
    /// Port of pyfrahan.cluster.fit_cuboid_frame. No gate here -- see
    /// <see cref="FitWithGate"/>.
    /// </summary>
    public static void Fit(IReadOnlyList<double[]> familyPoles, IReadOnlyList<double> weights,
                           out double[,] R, out double[] misfitsDeg, out int[] axisOf, int iters = 3)
    {
        int m = familyPoles.Count;
        if (m < 2) throw new ArgumentException("cuboid frame needs >= 2 family poles", nameof(familyPoles));

        var P = new double[m][];
        for (int i = 0; i < m; i++) P[i] = LowerHemisphere(familyPoles[i]);
        var w = new double[m];
        for (int i = 0; i < m; i++) w[i] = weights == null ? 1.0 : weights[i];

        // order families by weight descending; stable tie-break by index so the
        // "3 strongest" selection is deterministic and matches numpy argsort(-w)
        // when weights are distinct (the case here).
        var order = Enumerable.Range(0, m).OrderByDescending(i => w[i]).ThenBy(i => i).ToArray();

        double[][] top;
        double[] wtop;
        if (m == 2)
        {
            var third = LowerHemisphere(Cross(P[order[0]], P[order[1]]));
            top = new[] { P[order[0]], P[order[1]], third };
            wtop = new[] { w[order[0]], w[order[1]], 1e-6 };
        }
        else
        {
            top = new[] { P[order[0]], P[order[1]], P[order[2]] };
            wtop = new[] { w[order[0]], w[order[1]], w[order[2]] };
        }

        // ---- seed: 6-permutation Wahba on the 3 strongest ----
        double[,] bestR = null;
        double bestScore = double.PositiveInfinity;
        foreach (var perm in Perms6)
        {
            var sgn = new double[3];
            for (int i = 0; i < 3; i++) sgn[i] = top[i][perm[i]] >= 0 ? 1.0 : -1.0;

            double[,] Rp = null;
            for (int it = 0; it < iters; it++)
            {
                var M = new double[3, 3];
                for (int i = 0; i < 3; i++)
                {
                    double s = wtop[i] * sgn[i];              // a = e_perm[i], b = s * pole
                    M[perm[i], 0] += s * top[i][0];
                    M[perm[i], 1] += s * top[i][1];
                    M[perm[i], 2] += s * top[i][2];
                }
                Rp = Frahan.Masonry.Geometry.RigidTransformRecovery.RotationFromCorrelation(M);
                for (int i = 0; i < 3; i++)
                    sgn[i] = Dot(top[i], Col(Rp, perm[i])) >= 0 ? 1.0 : -1.0;
            }

            double score = 0.0;
            for (int i = 0; i < 3; i++)
                score = Math.Max(score, AxialAngleDeg(top[i], Col(Rp, perm[i])));
            if (score < bestScore) { bestScore = score; bestR = Rp; }
        }
        var Rf = bestR;

        // ---- refine over ALL families: nearest-axis assignment + signs ----
        for (int it = 0; it < iters; it++)
        {
            var cols = new[] { Col(Rf, 0), Col(Rf, 1), Col(Rf, 2) };
            var M = new double[3, 3];
            // anchor unassigned axes to the current R (M = 1e-6 * R^T: row j = 1e-6 * col_j).
            for (int j = 0; j < 3; j++)
            {
                M[j, 0] = 1e-6 * cols[j][0];
                M[j, 1] = 1e-6 * cols[j][1];
                M[j, 2] = 1e-6 * cols[j][2];
            }
            for (int i = 0; i < m; i++)
            {
                int j = 0; double bestA = double.MaxValue;
                for (int k = 0; k < 3; k++)
                {
                    double a = AxialAngleDeg(P[i], cols[k]);
                    if (a < bestA) { bestA = a; j = k; }
                }
                double si = Dot(P[i], cols[j]) >= 0 ? 1.0 : -1.0;
                double s = w[i] * si;
                M[j, 0] += s * P[i][0];
                M[j, 1] += s * P[i][1];
                M[j, 2] += s * P[i][2];
            }
            Rf = Frahan.Masonry.Geometry.RigidTransformRecovery.RotationFromCorrelation(M);
        }

        var fcols = new[] { Col(Rf, 0), Col(Rf, 1), Col(Rf, 2) };
        misfitsDeg = new double[m];
        axisOf = new int[m];
        for (int i = 0; i < m; i++)
        {
            int j = 0; double bestA = double.MaxValue;
            for (int k = 0; k < 3; k++)
            {
                double a = AxialAngleDeg(P[i], fcols[k]);
                if (a < bestA) { bestA = a; j = k; }
            }
            misfitsDeg[i] = bestA;
            axisOf[i] = j;
        }
        R = Rf;
    }

    // ---------------------------------------------------------------- gated fit
    /// <summary>
    /// Fit the cuboid frame and apply the misfit fallback gate (spec G12 S3):
    /// keep the cube iff every family misfit &lt;= max(thetaMax, 2*alpha95_i);
    /// else return <see cref="FrameMode.Fallback"/> with FrameR ABSENT. Fewer
    /// than 3 families returns <see cref="FrameMode.Unconstrained"/>.
    /// alpha95Deg may be null (or hold NaN for a family), in which case that
    /// family's guard is thetaMax alone -- exactly the Python non-finite branch.
    /// Port of the cluster_poles gate.
    /// </summary>
    public static CuboidFrameResult FitWithGate(
        IReadOnlyList<double[]> familyPoles, IReadOnlyList<double> weights,
        IReadOnlyList<double> alpha95Deg = null, double thetaMaxDeg = 15.0, int iters = 3)
    {
        int m = familyPoles.Count;
        var res = new CuboidFrameResult { ThetaMaxDeg = thetaMaxDeg };
        if (m < 3)
        {
            res.Mode = FrameMode.Unconstrained;
            res.FrameR = null;
            res.MisfitsDeg = null;
            res.AxisOf = null;
            res.MaxMisfitDeg = double.NaN;
            return res;
        }

        Fit(familyPoles, weights, out double[,] R, out double[] mf, out int[] ax, iters);
        double maxMisfit = 0.0;
        foreach (var v in mf) maxMisfit = Math.Max(maxMisfit, v);

        bool keep = true;
        for (int i = 0; i < m; i++)
        {
            double a95 = (alpha95Deg == null || i >= alpha95Deg.Count) ? double.NaN : alpha95Deg[i];
            double guard = (double.IsNaN(a95) || double.IsInfinity(a95))
                ? thetaMaxDeg
                : Math.Max(thetaMaxDeg, 2.0 * a95);
            if (mf[i] > guard) keep = false;
        }

        res.MisfitsDeg = mf;
        res.AxisOf = ax;
        res.MaxMisfitDeg = maxMisfit;
        if (keep) { res.Mode = FrameMode.Cuboid; res.FrameR = R; }
        else { res.Mode = FrameMode.Fallback; res.FrameR = null; }
        return res;
    }

    /// <summary>
    /// Max over the 3 axes of the axial angle between R's columns and their best
    /// (axial) match among R0's columns -- the spec's frame-recovery score.
    /// Port of pyfrahan.cluster.frame_axial_error_deg.
    /// </summary>
    public static double FrameAxialErrorDeg(double[,] R, double[,] R0)
    {
        double err = 0.0;
        for (int j = 0; j < 3; j++)
        {
            double best = double.MaxValue;
            for (int i = 0; i < 3; i++)
                best = Math.Min(best, AxialAngleDeg(Col(R, j), Col(R0, i)));
            err = Math.Max(err, best);
        }
        return err;
    }
}
