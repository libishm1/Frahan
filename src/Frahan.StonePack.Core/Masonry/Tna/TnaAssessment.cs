#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Tna
{
    // =========================================================================
    // TnaAssessment — Heyman safe-theorem geometric assessment of a thrust
    // surface against a vault section. Checkpoint-3 increment (the envelope / GSF
    // gap the existing TNA lacks).
    //
    // Given the funicular thrust heights and the vault mid-surface heights plus
    // the vault thickness D, the eccentricity at each node is e = |z_thrust −
    // z_mid|. The thrust line is contained in the section while e ≤ D/2. The
    // Geometric Safety Factor is GSF = (D/2) / max|e| (Heyman / Maia-Avelino TNO):
    // GSF ≥ 1 means the thrust surface stays inside the masonry (safe);
    // GSF < 1 means it exits the section (a hinge / unsafe under that thrust).
    // =========================================================================
    public sealed class GsfResult
    {
        public double Gsf;                 // (D/2) / max|e|
        public double MaxEccentricity;     // max|e| over assessed nodes
        public double[] Eccentricity;      // per-node |e|
        public int WorstNode;              // index of max eccentricity (or -1)
        public bool Safe;                  // GSF >= 1
    }

    public static class TnaAssessment
    {
        public static GsfResult Assess(IList<double> thrustZ, IList<double> midZ, double thickness)
        {
            int n = thrustZ.Count;
            var e = new double[n];
            double mx = 0; int w = -1;
            for (int i = 0; i < n; i++)
            {
                e[i] = Math.Abs(thrustZ[i] - midZ[i]);
                if (e[i] > mx) { mx = e[i]; w = i; }
            }
            double gsf = mx < 1e-9 ? double.PositiveInfinity : (thickness * 0.5) / mx;
            return new GsfResult { Gsf = gsf, MaxEccentricity = mx, Eccentricity = e, WorstNode = w, Safe = gsf >= 1.0 };
        }
    }
}
