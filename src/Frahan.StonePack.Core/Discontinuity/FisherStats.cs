#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Core.Discontinuity;

// =============================================================================
// FisherStats -- Fisher (1953) spherical statistics for one joint family, used
// by the cuboid-frame gate's 2*alpha95 guard. Fold to the hemisphere of the mean
// (Dips practice, iterated), then K = (N-1)/(N-R), the exact Fisher-1953
// alpha_(1-p) cone, and the 81/sqrt(K) angular SD (Butler 1992 / Dips eq.3).
//
// Port of pyfrahan.cluster.fisher_stats (a verbatim copy of
// scripts/geodemo/g2_g3_fisher_dips.py fisher_stats). Pure managed, Rhino-free.
// =============================================================================

public struct FisherResult
{
    public int N;
    public double Rbar;        // resultant length R (0..N)
    public double K;           // Dips eq.2 best-estimate concentration
    public double Alpha95Deg;  // half-apex of the (1-p) confidence cone, degrees
    public double AngSdDeg;    // 81/sqrt(K)
}

public static class FisherStats
{
    private const double R2D = 180.0 / Math.PI;

    /// <summary>
    /// Fisher stats for a set of unit axes (n and -n treated as the same plane).
    /// vecs: N x 3 (each length-3). p = tail probability (0.05 -&gt; alpha95).
    /// Mirrors fisher_stats: fold-to-hemisphere iterated to convergence, then
    /// K, the exact Fisher-1953 cone, and the angular SD.
    /// </summary>
    public static FisherResult Compute(IReadOnlyList<double[]> vecs, double p = 0.05)
    {
        int N = vecs.Count;
        var V = new double[N][];
        for (int i = 0; i < N; i++)
        {
            double[] v = vecs[i];
            double nn = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            if (nn <= 0) nn = 1.0;
            V[i] = new[] { v[0] / nn, v[1] / nn, v[2] / nn };
        }

        // initial mean = normalised arithmetic mean
        double mx = 0, my = 0, mz = 0;
        for (int i = 0; i < N; i++) { mx += V[i][0]; my += V[i][1]; mz += V[i][2]; }
        mx /= N; my /= N; mz /= N;
        double mn0 = Math.Sqrt(mx * mx + my * my + mz * mz);
        if (mn0 <= 0) mn0 = 1.0;
        mx /= mn0; my /= mn0; mz /= mn0;

        double Rbar = 0.0;
        for (int iter = 0; iter < 10; iter++)          // fold-to-hemisphere, iterate
        {
            double sx = 0, sy = 0, sz = 0;
            for (int i = 0; i < N; i++)
            {
                double d = V[i][0] * mx + V[i][1] * my + V[i][2] * mz;
                double s = d >= 0 ? 1.0 : -1.0;         // sign(0) -> +1 (numpy s[s==0]=1)
                sx += s * V[i][0]; sy += s * V[i][1]; sz += s * V[i][2];
            }
            Rbar = Math.Sqrt(sx * sx + sy * sy + sz * sz);
            double m2x = sx / Rbar, m2y = sy / Rbar, m2z = sz / Rbar;
            double conv = m2x * mx + m2y * my + m2z * mz;
            mx = m2x; my = m2y; mz = m2z;
            if (conv > 1 - 1e-12) break;
        }

        double K = (N - 1.0) / (N - Rbar);             // Dips eq.2
        double cosA = 1.0 - ((N - Rbar) / Rbar) * (Math.Pow(1.0 / p, 1.0 / (N - 1.0)) - 1.0);
        cosA = Math.Max(-1.0, Math.Min(1.0, cosA));
        double alpha = Math.Acos(cosA) * R2D;
        double angSd = 81.0 / Math.Sqrt(K);

        return new FisherResult { N = N, Rbar = Rbar, K = K, Alpha95Deg = alpha, AngSdDeg = angSd };
    }
}
