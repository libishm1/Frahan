using System;

namespace Frahan.EdgeMatching
{
    /// <summary>
    /// Cross-correlation between two turning signatures of equal length.
    /// The B signature is reversed and negated before correlation, because
    /// fitting fracture edges traverse the same physical curve in opposite
    /// senses with opposite turning sign.
    /// </summary>
    public static class PhaseCorrelator
    {
        public static (int lag, double score) Correlate(double[] a, double[] b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (a.Length != b.Length)
                throw new ArgumentException("Signatures must have equal length.");

            int n = a.Length;
            if (n == 0) return (0, 0.0);

            double[] bFlip = new double[n];
            for (int i = 0; i < n; i++) bFlip[i] = -b[n - 1 - i];

            double bestScore = double.PositiveInfinity;
            int bestLag = 0;

            for (int lag = 0; lag < n; lag++)
            {
                double sum = 0.0;
                for (int i = 0; i < n; i++)
                    sum += Math.Abs(a[i] - bFlip[(i + lag) % n]);
                if (sum < bestScore) { bestScore = sum; bestLag = lag; }
            }

            // Normalised similarity: 1.0 = perfect complement, 0.0 = worst case.
            // Per-sample turning is bounded by ±π, so maximal disagreement is 2π·n.
            double maxDiff = 2.0 * Math.PI * n;
            double similarity = maxDiff > 0 ? 1.0 - (bestScore / maxDiff) : 0.0;
            if (similarity < 0.0) similarity = 0.0;
            if (similarity > 1.0) similarity = 1.0;
            return (bestLag, similarity);
        }
    }
}
