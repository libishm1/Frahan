#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Geometry;

// =============================================================================
// RigidTransformRecovery — Horn 1987 closed-form absolute orientation via
// unit quaternions. Pure-managed, no SVD.
//
// Reference: Horn, B.K.P. "Closed-form solution of absolute orientation using
// unit quaternions." JOSA A, 4(4):629-642, April 1987.
//
// Use case: a canonical mesh is placed somewhere in the world (rigid motion
// only — translate + rotate). Recover (R, t) from N pairs of corresponding
// vertices (source → target). Vertex pairing is by INDEX, so the caller
// must ensure both lists have the same vertex order (true if the placed
// mesh was produced by transforming the source mesh without remeshing or
// dedup).
//
// Algorithm:
//   1. Centroids cA (source), cB (target).
//   2. Cross-covariance M = Σ (Aᵢ - cA) ⊗ (Bᵢ - cB), 3x3.
//   3. Build N (4x4 symmetric) from M's nine entries (Horn eq. 25).
//   4. Largest eigenvalue of N gives the optimal quaternion (q0, q1, q2, q3).
//   5. R = quat→matrix; t = cB - R·cA.
//
// Eigendecomposition: cyclic Jacobi on a 4x4 symmetric matrix. Converges in
// ~10 sweeps for typical inputs; capped at 50.
// =============================================================================

/// <summary>
/// Result of a Horn QAO fit. Rotation is row-major 3x3, translation is
/// length-3. RmsError is the per-pair root-mean-square residual after
/// applying (R, t) to the source points.
/// </summary>
public sealed class RigidTransformResult
{
    public RigidTransformResult(double[,] rotation, double[] translation, double rmsError)
    {
        if (rotation == null) throw new ArgumentNullException(nameof(rotation));
        if (translation == null) throw new ArgumentNullException(nameof(translation));
        if (rotation.GetLength(0) != 3 || rotation.GetLength(1) != 3)
            throw new ArgumentException("rotation must be 3x3", nameof(rotation));
        if (translation.Length != 3)
            throw new ArgumentException("translation must be length 3", nameof(translation));
        if (rmsError < 0.0)
            throw new ArgumentOutOfRangeException(nameof(rmsError), "must be >= 0");

        Rotation = rotation;
        Translation = translation;
        RmsError = rmsError;
    }

    public double[,] Rotation { get; }
    public double[] Translation { get; }
    public double RmsError { get; }
}

public static class RigidTransformRecovery
{
    private const int MaxJacobiSweeps = 50;
    private const double JacobiOffEps = 1e-30;

    /// <summary>
    /// Recover the rigid transform (R, t) that best maps source points
    /// onto target points (least-squares sense). Both lists must be
    /// flat XYZ triples (length divisible by 3) with identical point
    /// counts. Minimum 3 non-collinear pairs.
    /// </summary>
    public static RigidTransformResult Solve(
        IReadOnlyList<double> sourceXyz,
        IReadOnlyList<double> targetXyz)
    {
        if (sourceXyz == null) throw new ArgumentNullException(nameof(sourceXyz));
        if (targetXyz == null) throw new ArgumentNullException(nameof(targetXyz));
        if (sourceXyz.Count % 3 != 0)
            throw new ArgumentException(
                $"sourceXyz length must be a multiple of 3, got {sourceXyz.Count}",
                nameof(sourceXyz));
        if (targetXyz.Count % 3 != 0)
            throw new ArgumentException(
                $"targetXyz length must be a multiple of 3, got {targetXyz.Count}",
                nameof(targetXyz));
        if (sourceXyz.Count != targetXyz.Count)
            throw new ArgumentException(
                $"sourceXyz and targetXyz must have the same length; got {sourceXyz.Count} vs {targetXyz.Count}",
                nameof(targetXyz));

        int n = sourceXyz.Count / 3;
        if (n < 3)
            throw new ArgumentException(
                $"need at least 3 point pairs to recover a rigid transform, got {n}",
                nameof(sourceXyz));

        // Centroids.
        double cax = 0, cay = 0, caz = 0;
        double cbx = 0, cby = 0, cbz = 0;
        for (int i = 0; i < n; i++)
        {
            cax += sourceXyz[3 * i + 0]; cay += sourceXyz[3 * i + 1]; caz += sourceXyz[3 * i + 2];
            cbx += targetXyz[3 * i + 0]; cby += targetXyz[3 * i + 1]; cbz += targetXyz[3 * i + 2];
        }
        double inv = 1.0 / n;
        cax *= inv; cay *= inv; caz *= inv;
        cbx *= inv; cby *= inv; cbz *= inv;

        // Cross-covariance M = Σ (Aᵢ - cA) ⊗ (Bᵢ - cB).  Sxy = Σ ax·by etc.
        double Sxx = 0, Sxy = 0, Sxz = 0;
        double Syx = 0, Syy = 0, Syz = 0;
        double Szx = 0, Szy = 0, Szz = 0;
        for (int i = 0; i < n; i++)
        {
            double ax = sourceXyz[3 * i + 0] - cax;
            double ay = sourceXyz[3 * i + 1] - cay;
            double az = sourceXyz[3 * i + 2] - caz;
            double bx = targetXyz[3 * i + 0] - cbx;
            double by = targetXyz[3 * i + 1] - cby;
            double bz = targetXyz[3 * i + 2] - cbz;
            Sxx += ax * bx; Sxy += ax * by; Sxz += ax * bz;
            Syx += ay * bx; Syy += ay * by; Syz += ay * bz;
            Szx += az * bx; Szy += az * by; Szz += az * bz;
        }

        // Build N (4x4 symmetric). Horn 1987 eq. 25.
        var nm = new double[4, 4];
        nm[0, 0] = Sxx + Syy + Szz;
        nm[0, 1] = Syz - Szy; nm[1, 0] = nm[0, 1];
        nm[0, 2] = Szx - Sxz; nm[2, 0] = nm[0, 2];
        nm[0, 3] = Sxy - Syx; nm[3, 0] = nm[0, 3];
        nm[1, 1] = Sxx - Syy - Szz;
        nm[1, 2] = Sxy + Syx; nm[2, 1] = nm[1, 2];
        nm[1, 3] = Szx + Sxz; nm[3, 1] = nm[1, 3];
        nm[2, 2] = -Sxx + Syy - Szz;
        nm[2, 3] = Syz + Szy; nm[3, 2] = nm[2, 3];
        nm[3, 3] = -Sxx - Syy + Szz;

        JacobiEigen4x4Symmetric(nm, out double[] eigvals, out double[,] eigvecs);

        // Pick the eigenvector for the largest eigenvalue.
        int best = 0;
        for (int i = 1; i < 4; i++)
            if (eigvals[i] > eigvals[best]) best = i;

        double q0 = eigvecs[0, best];
        double q1 = eigvecs[1, best];
        double q2 = eigvecs[2, best];
        double q3 = eigvecs[3, best];
        double qm = Math.Sqrt(q0 * q0 + q1 * q1 + q2 * q2 + q3 * q3);
        if (qm < 1e-12)
            throw new InvalidOperationException(
                "Horn QAO produced a near-zero quaternion; inputs likely degenerate (collinear).");
        q0 /= qm; q1 /= qm; q2 /= qm; q3 /= qm;

        var R = new double[3, 3];
        R[0, 0] = q0 * q0 + q1 * q1 - q2 * q2 - q3 * q3;
        R[0, 1] = 2.0 * (q1 * q2 - q0 * q3);
        R[0, 2] = 2.0 * (q1 * q3 + q0 * q2);
        R[1, 0] = 2.0 * (q1 * q2 + q0 * q3);
        R[1, 1] = q0 * q0 - q1 * q1 + q2 * q2 - q3 * q3;
        R[1, 2] = 2.0 * (q2 * q3 - q0 * q1);
        R[2, 0] = 2.0 * (q1 * q3 - q0 * q2);
        R[2, 1] = 2.0 * (q2 * q3 + q0 * q1);
        R[2, 2] = q0 * q0 - q1 * q1 - q2 * q2 + q3 * q3;

        // Translation: cB - R · cA.
        double tx = cbx - (R[0, 0] * cax + R[0, 1] * cay + R[0, 2] * caz);
        double ty = cby - (R[1, 0] * cax + R[1, 1] * cay + R[1, 2] * caz);
        double tz = cbz - (R[2, 0] * cax + R[2, 1] * cay + R[2, 2] * caz);

        // RMS residual.
        double sse = 0;
        for (int i = 0; i < n; i++)
        {
            double sx = sourceXyz[3 * i + 0];
            double sy = sourceXyz[3 * i + 1];
            double sz = sourceXyz[3 * i + 2];
            double rx = R[0, 0] * sx + R[0, 1] * sy + R[0, 2] * sz + tx;
            double ry = R[1, 0] * sx + R[1, 1] * sy + R[1, 2] * sz + ty;
            double rz = R[2, 0] * sx + R[2, 1] * sy + R[2, 2] * sz + tz;
            double dx = rx - targetXyz[3 * i + 0];
            double dy = ry - targetXyz[3 * i + 1];
            double dz = rz - targetXyz[3 * i + 2];
            sse += dx * dx + dy * dy + dz * dz;
        }
        double rms = Math.Sqrt(sse / n);

        return new RigidTransformResult(R, new[] { tx, ty, tz }, rms);
    }

    /// <summary>
    /// Optimal proper rotation R (det = +1) that maximises Σ (R·aᵢ)·bᵢ given the
    /// 3×3 correlation matrix M = Σ wᵢ aᵢ ⊗ bᵢ (M[i,j] = Σ w aᵢ bⱼ). This is the
    /// un-centred Wahba specialisation of the Horn absolute-orientation solver
    /// used by <see cref="Solve"/>: the SAME 4×4 N-matrix (Horn 1987 eq. 25) and
    /// quaternion→R, with the centroid/translation step omitted because a frame
    /// fit has no translation. Column j of the returned R is the direction that
    /// the canonical axis aᵢ = eⱼ maps to.
    ///
    /// Reused by the cuboid-frame estimator
    /// (<c>Frahan.Core.Discontinuity.CuboidFrameFit</c>). This is the exact C#
    /// counterpart of pyfrahan.cluster._rotation_from_correlation (G12), which is
    /// itself a transcription of g4_registration_report.horn (validated 4e-16).
    /// </summary>
    public static double[,] RotationFromCorrelation(double[,] m)
    {
        if (m == null) throw new ArgumentNullException(nameof(m));
        if (m.GetLength(0) != 3 || m.GetLength(1) != 3)
            throw new ArgumentException("correlation matrix must be 3x3", nameof(m));

        double Sxx = m[0, 0], Sxy = m[0, 1], Sxz = m[0, 2];
        double Syx = m[1, 0], Syy = m[1, 1], Syz = m[1, 2];
        double Szx = m[2, 0], Szy = m[2, 1], Szz = m[2, 2];

        // Build N (4x4 symmetric). Horn 1987 eq. 25 — identical to Solve.
        var nm = new double[4, 4];
        nm[0, 0] = Sxx + Syy + Szz;
        nm[0, 1] = Syz - Szy; nm[1, 0] = nm[0, 1];
        nm[0, 2] = Szx - Sxz; nm[2, 0] = nm[0, 2];
        nm[0, 3] = Sxy - Syx; nm[3, 0] = nm[0, 3];
        nm[1, 1] = Sxx - Syy - Szz;
        nm[1, 2] = Sxy + Syx; nm[2, 1] = nm[1, 2];
        nm[1, 3] = Szx + Sxz; nm[3, 1] = nm[1, 3];
        nm[2, 2] = -Sxx + Syy - Szz;
        nm[2, 3] = Syz + Szy; nm[3, 2] = nm[2, 3];
        nm[3, 3] = -Sxx - Syy + Szz;

        JacobiEigen4x4Symmetric(nm, out double[] eigvals, out double[,] eigvecs);

        int best = 0;
        for (int i = 1; i < 4; i++)
            if (eigvals[i] > eigvals[best]) best = i;

        double q0 = eigvecs[0, best];
        double q1 = eigvecs[1, best];
        double q2 = eigvecs[2, best];
        double q3 = eigvecs[3, best];
        double qm = Math.Sqrt(q0 * q0 + q1 * q1 + q2 * q2 + q3 * q3);
        if (qm < 1e-12)
            throw new InvalidOperationException(
                "RotationFromCorrelation produced a near-zero quaternion; correlation is degenerate.");
        q0 /= qm; q1 /= qm; q2 /= qm; q3 /= qm;

        var R = new double[3, 3];
        R[0, 0] = q0 * q0 + q1 * q1 - q2 * q2 - q3 * q3;
        R[0, 1] = 2.0 * (q1 * q2 - q0 * q3);
        R[0, 2] = 2.0 * (q1 * q3 + q0 * q2);
        R[1, 0] = 2.0 * (q1 * q2 + q0 * q3);
        R[1, 1] = q0 * q0 - q1 * q1 + q2 * q2 - q3 * q3;
        R[1, 2] = 2.0 * (q2 * q3 - q0 * q1);
        R[2, 0] = 2.0 * (q1 * q3 - q0 * q2);
        R[2, 1] = 2.0 * (q2 * q3 + q0 * q1);
        R[2, 2] = q0 * q0 - q1 * q1 - q2 * q2 + q3 * q3;
        return R;
    }

    /// <summary>
    /// Cyclic Jacobi eigendecomposition for a 4x4 symmetric matrix.
    /// Returns eigenvalues on the diagonal (vals[i]) and eigenvectors
    /// as columns of vecs (vecs[row, col]).
    /// </summary>
    private static void JacobiEigen4x4Symmetric(
        double[,] m, out double[] vals, out double[,] vecs)
    {
        const int n = 4;
        var a = (double[,])m.Clone();
        var v = new double[n, n];
        for (int i = 0; i < n; i++) v[i, i] = 1.0;

        for (int sweep = 0; sweep < MaxJacobiSweeps; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                    off += a[p, q] * a[p, q];
            if (off < JacobiOffEps) break;

            for (int p = 0; p < n - 1; p++)
            {
                for (int q = p + 1; q < n; q++)
                {
                    double apq = a[p, q];
                    if (Math.Abs(apq) < 1e-20) continue;

                    double app = a[p, p];
                    double aqq = a[q, q];
                    double theta = (aqq - app) / (2.0 * apq);
                    double t = (theta >= 0)
                        ? 1.0 / (theta + Math.Sqrt(1.0 + theta * theta))
                        : -1.0 / (-theta + Math.Sqrt(1.0 + theta * theta));
                    double c = 1.0 / Math.Sqrt(1.0 + t * t);
                    double s = t * c;

                    a[p, p] = app - t * apq;
                    a[q, q] = aqq + t * apq;
                    a[p, q] = 0.0; a[q, p] = 0.0;

                    for (int i = 0; i < n; i++)
                    {
                        if (i != p && i != q)
                        {
                            double aip = a[i, p];
                            double aiq = a[i, q];
                            a[i, p] = c * aip - s * aiq; a[p, i] = a[i, p];
                            a[i, q] = s * aip + c * aiq; a[q, i] = a[i, q];
                        }
                        double vip = v[i, p];
                        double viq = v[i, q];
                        v[i, p] = c * vip - s * viq;
                        v[i, q] = s * vip + c * viq;
                    }
                }
            }
        }

        vals = new double[n];
        for (int i = 0; i < n; i++) vals[i] = a[i, i];
        vecs = v;
    }
}
