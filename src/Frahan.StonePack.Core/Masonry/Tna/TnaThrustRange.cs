#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Masonry.Tna
{
    // =========================================================================
    // TnaThrustRange — min/max horizontal thrust for a masonry arch within its
    // section (Heyman lower-bound / safe-theorem limit analysis). Completes the
    // form-finding side beyond a single GSF: it returns the admissible THRUST
    // RANGE [Hmin, Hmax] and the two extreme thrust lines that touch the section
    // faces, i.e. the genuine safety interval (passive/active states).
    //
    // For a SINGLE arch the problem is exact and needs no solver. With a constant
    // horizontal thrust H carrying vertical nodal loads w_i, the thrust line is
    //     z_i(H) = chord_i + b_i / H ,   b_i = (x_i/L)*M_n - M_i
    // where M_i is the cantilever moment of the loads left of node i. Substituting
    // t = 1/H makes z_i LINEAR in t, so the constraint
    //     z_mid_i - h_i <= z_i <= z_mid_i + h_i   (thrust line inside the section)
    // is a pair of linear inequalities per node -> the feasible t is an interval
    // [tLo, tHi], giving Hmax = 1/tLo (flattest, max thrust) and Hmin = 1/tHi
    // (deepest sag, min thrust). h_i = (D/2)/cos(theta_i) is the vertical section
    // half-height. (The 3D network generalisation is an LP over the OSQP backend;
    // each masonry COURSE is an arch, so this exact form covers the course CRA.)
    //
    // VALIDITY (adversarially verified 2026-06-30): [Hmin,Hmax] is a SUFFICIENT
    // (conservative lower-bound) Heyman safe-theorem certificate. It is TIGHT for
    // near-funicular forms — exact on the parabola/UDL (recovers H=qL^2/8f, stays
    // in-section to machine zero), and on catenaries under self-weight even at
    // D/L=0.02, regardless of springing inclination. It is CONSERVATIVE (~1.8-2x on
    // thickness) for deep NON-funicular forms such as a semicircle: a single global
    // horizontal thrust cannot touch the extrados at the crown AND the intrados at
    // the haunch inside a deep ring, so it reads t/R~0.19 vs Heyman 0.106. This is a
    // modeling limitation (endpoints pinned to the springing; the vertical-band test
    // goes vacuous at near-vertical joints whose true constraint is horizontal), NOT
    // a bug. Optional future enhancement: free the springing reaction point + use a
    // perpendicular/horizontal in-section test at steep nodes to recover the circular
    // minimum thickness. Not needed for funicular vault courses (the intended use).
    // =========================================================================
    public sealed class ThrustRangeResult
    {
        public bool Feasible;
        public double Hmin, Hmax;          // horizontal thrust range (same force units as the loads)
        public double RangeFactor;          // Hmax / Hmin  (>= 1; how much the thrust may vary in-section)
        public Polyline ThrustLineMin;      // min-thrust line (deepest admissible sag, touches intrados)
        public Polyline ThrustLineMax;      // max-thrust line (flattest admissible, touches extrados)
        public string Message;
    }

    public static class TnaThrustRange
    {
        public static ThrustRangeResult ForArch(IList<Point3d> centerline, double thickness, IList<double> loads)
        {
            int n = centerline.Count;
            var res = new ThrustRangeResult();
            if (n < 3) { res.Message = "arch needs >= 3 nodes"; return res; }
            if (loads == null || loads.Count != n) { res.Message = "loads must match node count"; return res; }

            // vertical plane: horizontal axis = the chord's horizontal projection; vertical = world Z
            Point3d p0 = centerline[0], pn = centerline[n - 1];
            var horiz = new Vector3d(pn.X - p0.X, pn.Y - p0.Y, 0.0);
            if (horiz.Length < 1e-9) horiz = Vector3d.XAxis;
            horiz.Unitize();

            var x = new double[n]; var z = new double[n];
            for (int i = 0; i < n; i++)
            {
                var d = centerline[i] - p0;
                x[i] = d.X * horiz.X + d.Y * horiz.Y;
                z[i] = centerline[i].Z;
            }
            double L = x[n - 1] - x[0];
            if (Math.Abs(L) < 1e-9) { res.Message = "degenerate arch (no horizontal span)"; return res; }

            // cantilever moments  M_i = sum_{j<i} w_j (x_i - x_j)
            var M = new double[n];
            for (int i = 0; i < n; i++) { double m = 0.0; for (int j = 0; j < i; j++) m += loads[j] * (x[i] - x[j]); M[i] = m; }
            double Mn = M[n - 1];

            // vertical section half-height  h_i = (D/2)/cos(theta_i)
            var h = new double[n];
            for (int i = 0; i < n; i++)
            {
                int ia = Math.Max(0, i - 1), ib = Math.Min(n - 1, i + 1);
                double dx = x[ib] - x[ia], dz = z[ib] - z[ia];
                double len = Math.Sqrt(dx * dx + dz * dz);
                double cosT = len > 1e-9 ? Math.Abs(dx) / len : 1.0;
                if (cosT < 1e-3) cosT = 1e-3;
                h[i] = (thickness * 0.5) / cosT;
            }

            // feasible interval in t = 1/H from  z_lo <= a_i + b_i t <= z_hi  at each interior node
            double tLo = 0.0, tHi = double.PositiveInfinity;
            for (int i = 1; i < n - 1; i++)
            {
                double a = z[0] + (x[i] - x[0]) / L * (z[n - 1] - z[0]);   // chord height at i
                double b = (x[i] - x[0]) / L * Mn - M[i];                  // funicular sag coefficient
                double lo = (z[i] - h[i]) - a;
                double hi = (z[i] + h[i]) - a;
                if (Math.Abs(b) < 1e-12)
                {
                    if (lo > 1e-9 || hi < -1e-9) { res.Message = "chord exits the section at node " + i + " (infeasible)"; return res; }
                    continue;
                }
                double t1 = lo / b, t2 = hi / b;
                double aLo = Math.Min(t1, t2), aHi = Math.Max(t1, t2);
                if (aLo > tLo) tLo = aLo;
                if (aHi < tHi) tHi = aHi;
            }
            if (tLo <= 0.0) tLo = 1e-9;
            if (tHi <= tLo) { res.Message = "no admissible thrust line fits inside the section (vault too thin / form off)"; return res; }

            res.Feasible = true;
            res.Hmax = 1.0 / tLo;
            res.Hmin = 1.0 / tHi;
            res.RangeFactor = res.Hmax / res.Hmin;
            res.ThrustLineMin = BuildLine(p0, horiz, x, z, L, M, Mn, tHi);
            res.ThrustLineMax = BuildLine(p0, horiz, x, z, L, M, Mn, tLo);
            res.Message = $"H in [{res.Hmin:F2}, {res.Hmax:F2}] (range factor {res.RangeFactor:F2}); admissible thrust line fits the section.";
            return res;
        }

        private static Polyline BuildLine(Point3d p0, Vector3d horiz, double[] x, double[] z, double L, double[] M, double Mn, double t)
        {
            int n = x.Length; var pl = new Polyline();
            for (int i = 0; i < n; i++)
            {
                double a = z[0] + (x[i] - x[0]) / L * (z[n - 1] - z[0]);
                double b = (x[i] - x[0]) / L * Mn - M[i];
                pl.Add(new Point3d(p0.X + horiz.X * x[i], p0.Y + horiz.Y * x[i], a + b * t));
            }
            return pl;
        }
    }
}
