#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Masonry.Vault
{
    // =========================================================================
    // VaultSteelTies — resolve a compression-only vault's OUTWARD horizontal support
    // thrusts with a closed steel TENSION RING (the Armadillo Vault's steel-tie logic,
    // AAG 2016). Masonry carries no tension, so the horizontal thrust at the springing
    // must be taken by ties (or buttressing/ground). Given the support (springing) points
    // and the outward horizontal thrust vector at each (from the TNA reactions), this
    // sorts the supports into a convex ring in plan, solves tension-only node equilibrium
    // for the tie forces (least-squares), and sizes a round steel bar per tie.
    //
    // Self-contained: needs only points + thrust vectors; pairs with TNA Vault's reactions
    // but does not depend on it. For a regular N-gon with equal radial thrust H this
    // reproduces the classic ring force T = H / (2 sin(pi/N)).
    // =========================================================================
    public sealed class SteelTieResult
    {
        public List<Line> Ties = new List<Line>();          // tie segments, the support ring
        public List<double> Tension = new List<double>();   // N per tie (>= 0 tension)
        public List<double> Diameter = new List<double>();  // required round-bar diameter (m)
        public double MaxTension;
        public double TotalSteelVolume;                     // sum(area_i * length_i), m^3
        public string Note = "";
    }

    public static class VaultSteelTies
    {
        /// <summary>
        /// Generate the steel tie ring. supports = springing points (any order);
        /// thrust[i] = outward horizontal thrust vector at support i (z ignored).
        /// allowableStress in Pa (default S355 yield); safety = safety factor on tension.
        /// </summary>
        public static SteelTieResult Generate(
            IList<Point3d> supports, IList<Vector3d> thrust,
            double allowableStress = 355e6, double safety = 1.5)
        {
            var res = new SteelTieResult();
            int n = supports != null ? supports.Count : 0;
            if (n < 2) { res.Note = "need >= 2 supports"; return res; }
            if (thrust == null || thrust.Count != n)
                throw new ArgumentException("thrust must parallel supports");
            if (allowableStress <= 0) allowableStress = 355e6;
            if (safety < 1.0) safety = 1.0;

            // ---- sort supports into a convex ring by plan angle about the centroid ----
            double cx = 0, cy = 0;
            for (int i = 0; i < n; i++) { cx += supports[i].X; cy += supports[i].Y; }
            cx /= n; cy /= n;
            var order = new List<int>(n);
            for (int i = 0; i < n; i++) order.Add(i);
            order.Sort((a, b) =>
                Math.Atan2(supports[a].Y - cy, supports[a].X - cx)
                .CompareTo(Math.Atan2(supports[b].Y - cy, supports[b].X - cx)));

            var P = new Point3d[n];
            var H = new Vector2d[n];   // outward horizontal thrust at each ring node
            for (int k = 0; k < n; k++)
            {
                int src = order[k];
                P[k] = supports[src];
                H[k] = new Vector2d(thrust[src].X, thrust[src].Y);
            }

            // ---- ring edges k -> (k+1) mod n; unit plan directions ----
            int m = (n == 2) ? 1 : n;   // 2 supports => a single tie, not a closed loop
            var dir = new Vector2d[m];
            for (int e = 0; e < m; e++)
            {
                int a = e, b = (e + 1) % n;
                var d = new Vector2d(P[b].X - P[a].X, P[b].Y - P[a].Y);
                double L = Math.Sqrt(d.X * d.X + d.Y * d.Y);
                dir[e] = (L < 1e-12) ? new Vector2d(1, 0) : new Vector2d(d.X / L, d.Y / L);
            }

            // ---- tension-only node equilibrium, least-squares: for each node k,
            //   sum over incident edges ( T_e * (unit dir from k along that edge) ) + H_k = 0.
            // Edge e=(k,k+1) pulls node k toward k+1 (+dir[e]) and node k+1 toward k (-dir[e]).
            // Assemble A (2n x m), b (2n) with rows = -H, solve normal equations (AtA) T = At b.
            double[,] AtA = new double[m, m];
            double[] Atb = new double[m];
            // accumulate per node
            for (int k = 0; k < n; k++)
            {
                // incident edges of node k and the sign (direction the tie pulls node k)
                // outgoing edge e0 = k     -> pulls toward +dir[k]
                // incoming edge e1 = k-1   -> pulls toward -dir[k-1]
                var inc = new List<KeyValuePair<int, Vector2d>>(2);
                if (k < m) inc.Add(new KeyValuePair<int, Vector2d>(k, dir[k]));
                int eIn = (k - 1 + n) % n;
                if (eIn < m && eIn != k) inc.Add(new KeyValuePair<int, Vector2d>(eIn, new Vector2d(-dir[eIn].X, -dir[eIn].Y)));

                // two scalar rows (x, y): sum_e coef_e * T_e = -H_k
                for (int axis = 0; axis < 2; axis++)
                {
                    double rhs = -(axis == 0 ? H[k].X : H[k].Y);
                    // row coefficients
                    for (int ii = 0; ii < inc.Count; ii++)
                    {
                        int ei = inc[ii].Key;
                        double ci = (axis == 0 ? inc[ii].Value.X : inc[ii].Value.Y);
                        Atb[ei] += ci * rhs;
                        for (int jj = 0; jj < inc.Count; jj++)
                        {
                            int ej = inc[jj].Key;
                            double cj = (axis == 0 ? inc[jj].Value.X : inc[jj].Value.Y);
                            AtA[ei, ej] += ci * cj;
                        }
                    }
                }
            }
            // tiny Tikhonov term keeps the (rank-deficient global-rotation) system solvable
            for (int e = 0; e < m; e++) AtA[e, e] += 1e-6;
            double[] T = SolveSym(AtA, Atb, m);

            // ---- emit ties + sizing ----
            double maxT = 0, vol = 0;
            for (int e = 0; e < m; e++)
            {
                int a = e, b = (e + 1) % n;
                double tension = Math.Max(0.0, T[e]);   // ties take tension only
                var ln = new Line(P[a], P[b]);
                double area = tension * safety / allowableStress;       // m^2
                double dia = 2.0 * Math.Sqrt(Math.Max(area, 0.0) / Math.PI);
                res.Ties.Add(ln);
                res.Tension.Add(tension);
                res.Diameter.Add(dia);
                vol += area * ln.Length;
                if (tension > maxT) maxT = tension;
            }
            res.MaxTension = maxT;
            res.TotalSteelVolume = vol;
            res.Note = (n == 2 ? "single tie" : n + "-support tension ring")
                       + $"; max {maxT / 1000.0:0.0} kN";
            return res;
        }

        // small symmetric positive-definite solve (Cholesky); m is tiny (<= ~12)
        static double[] SolveSym(double[,] A, double[] b, int m)
        {
            var L = new double[m, m];
            for (int i = 0; i < m; i++)
                for (int j = 0; j <= i; j++)
                {
                    double sum = A[i, j];
                    for (int k = 0; k < j; k++) sum -= L[i, k] * L[j, k];
                    if (i == j) L[i, j] = Math.Sqrt(Math.Max(sum, 1e-18));
                    else L[i, j] = sum / L[j, j];
                }
            var y = new double[m];
            for (int i = 0; i < m; i++)
            {
                double sum = b[i];
                for (int k = 0; k < i; k++) sum -= L[i, k] * y[k];
                y[i] = sum / L[i, i];
            }
            var x = new double[m];
            for (int i = m - 1; i >= 0; i--)
            {
                double sum = y[i];
                for (int k = i + 1; k < m; k++) sum -= L[k, i] * x[k];
                x[i] = sum / L[i, i];
            }
            return x;
        }
    }
}
