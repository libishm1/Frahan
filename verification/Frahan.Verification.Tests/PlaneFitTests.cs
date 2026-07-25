using System;
using System.Collections.Generic;
using Xunit;
using Rhino.Geometry;
using Frahan.Core.Discontinuity;

namespace Frahan.Verification.Tests
{
    /// <summary>
    /// Verifies <c>CloudMath.Pca</c> — the local least-squares (PCA / orthogonal
    /// total-least-squares) plane fit the FACETS facet extractor
    /// (<c>PointCloudFacetExtractor.cs</c>) calls to build every facet plane. The
    /// fit is the plane through the centroid with normal = the smallest-eigenvalue
    /// eigenvector of the point scatter matrix, i.e. the minimizer of the sum of
    /// squared point-to-plane distances. It is checked against the least-squares
    /// existence/uniqueness lemma proved in
    /// <c>frahan_proofs/FrahanProofs/Fitting.lean</c> — <c>ls_fit_unique</c> /
    /// <c>gram_posDef_iff_span</c> (tex <c>lem:plane</c>): the LS fit is the unique
    /// minimizer when the picks are not collinear (the Gram form is positive
    /// definite).
    ///
    /// This is the CLEANEST accessible managed plane fit: it takes a plain
    /// <c>IReadOnlyList&lt;Point3d&gt;</c> plus an index list, so it needs no
    /// point-cloud/kNN infrastructure. CloudMath is internal but compiled into
    /// this test assembly, so the tests call it directly. All oracles are
    /// independent double-precision computations; the kernel's own Rms/eta is
    /// never used to check itself.
    ///
    /// Facts:
    ///   T1 OPTIMALITY — the fitted plane's SSE (Σ squared point-plane residual)
    ///                   is ≤ that of randomly tilted/offset planes: it is a
    ///                   genuine minimizer (the ls_fit_unique minimality claim).
    ///   T2 EXACTNESS  — points exactly on a known non-collinear plane recover
    ///                   that plane (normal within angular tol, offset ≈ 0).
    ///   T3 DEGENERATE — collinear input is characterized (no crash; unit normal
    ///                   perpendicular to the line; planarity η ≈ 0), not gated
    ///                   beyond the kernel's own contract (returns a unit normal).
    /// Scales tested: point spreads at 1e-2, 1, 1e2 model units.
    /// </summary>
    public class PlaneFitTests
    {
        /// <summary>T1 — the PCA plane is the orthogonal-least-squares minimizer:
        /// no tilt of the normal and no offset of the plane reduces the SSE.</summary>
        [Fact]
        public void T1_Fit_MinimizesSquaredResiduals()
        {
            var rnd = new Random(20260725);
            double worstDeficit = 0; string worstMsg = "(none)";
            foreach (double scale in new[] { 1e-2, 1.0, 1e2 })
            {
                for (int t = 0; t < 3000; t++)
                {
                    OrthoBasis(rnd, out var u, out var v, out var n0);
                    var center = new Point3d(RandC(rnd, scale), RandC(rnd, scale), RandC(rnd, scale));
                    int m = 8 + rnd.Next(40);
                    var pts = new List<Point3d>(m);
                    for (int i = 0; i < m; i++)
                    {
                        double a = (rnd.NextDouble() * 2 - 1) * scale;
                        double b = (rnd.NextDouble() * 2 - 1) * scale;
                        double e = (rnd.NextDouble() * 2 - 1) * 0.05 * scale; // off-plane noise
                        pts.Add(center + a * u + b * v + e * n0);
                    }
                    var idx = Range(m);
                    CloudMath.Pca(pts, idx, out var normal, out double eta, out var centroid, out _);

                    double sseFit = Sse(pts, centroid, normal);
                    double tol = 1e-9 * sseFit + 1e-300;

                    // Try several tilted + offset planes; none may beat the fit.
                    for (int k = 0; k < 12; k++)
                    {
                        double ang = (0.5 + rnd.NextDouble() * 20) * Math.PI / 180.0; // 0.5..20.5 deg tilt
                        var axis = RandUnit(rnd);
                        var nP = Rodrigues(normal, axis, ang);
                        double off = (rnd.NextDouble() * 2 - 1) * 0.3 * scale;
                        var pP = new Point3d(centroid.X + off * nP.X, centroid.Y + off * nP.Y, centroid.Z + off * nP.Z);
                        double ssePert = Sse(pts, pP, nP);
                        double deficit = sseFit - ssePert; // must be <= tol
                        if (deficit > worstDeficit) { worstDeficit = deficit; worstMsg = $"scale={scale} sseFit={sseFit:E3} ssePert={ssePert:E3} deficit={deficit:E3} eta={eta:E3}"; }
                        Assert.True(deficit <= tol, $"T1 fit is NOT the minimizer: {worstMsg}");
                    }
                }
            }
        }

        /// <summary>T2 — exact recovery: points generated exactly on a known plane
        /// (well spread in both in-plane directions ⇒ non-collinear ⇒ unique fit)
        /// return that plane's normal (axially) and pass through it.</summary>
        [Fact]
        public void T2_ExactPlane_Recovered()
        {
            var rnd = new Random(77);
            double worstAng = 0, worstOff = 0; string worstMsg = "(none)";
            foreach (double scale in new[] { 1e-2, 1.0, 1e2 })
            {
                for (int t = 0; t < 3000; t++)
                {
                    OrthoBasis(rnd, out var u, out var v, out var n0);
                    var p0 = new Point3d(RandC(rnd, scale), RandC(rnd, scale), RandC(rnd, scale));
                    int m = 6 + rnd.Next(40);
                    var pts = new List<Point3d>(m);
                    for (int i = 0; i < m; i++)
                    {
                        double a = (rnd.NextDouble() * 2 - 1) * scale;
                        double b = (rnd.NextDouble() * 2 - 1) * scale;
                        pts.Add(p0 + a * u + b * v); // exactly on the plane
                    }
                    CloudMath.Pca(pts, Range(m), out var normal, out _, out var centroid, out _);

                    double dotAbs = Math.Min(1.0, Math.Abs(normal.X * n0.X + normal.Y * n0.Y + normal.Z * n0.Z));
                    double ang = Math.Acos(dotAbs);                 // radians
                    double off = Math.Abs((centroid.X - p0.X) * n0.X + (centroid.Y - p0.Y) * n0.Y + (centroid.Z - p0.Z) * n0.Z);
                    if (ang > worstAng) { worstAng = ang; }
                    if (off > worstOff) { worstOff = off; }
                    worstMsg = $"scale={scale} ang={worstAng:E3}rad off={worstOff:E3}";
                    Assert.True(ang < 1e-6, $"T2 normal not recovered: {worstMsg}");
                    Assert.True(off < 1e-9 * scale + 1e-12, $"T2 offset not recovered: {worstMsg}");
                }
            }
        }

        /// <summary>T3 — degenerate collinear input: characterize the kernel's
        /// behaviour. No exception; the returned normal is unit-length and
        /// perpendicular to the line direction; planarity η ≈ 0. Only the unit-
        /// normal / no-crash contract is gated; the rest is documented.</summary>
        [Fact]
        public void T3_Collinear_Characterized()
        {
            var rnd = new Random(999);
            double worstLen = 0, worstPerp = 0, worstEta = 0;
            foreach (double scale in new[] { 1e-2, 1.0, 1e2 })
            {
                for (int t = 0; t < 2000; t++)
                {
                    var d = RandUnit(rnd);
                    var p0 = new Point3d(RandC(rnd, scale), RandC(rnd, scale), RandC(rnd, scale));
                    int m = 5 + rnd.Next(20);
                    var pts = new List<Point3d>(m);
                    for (int i = 0; i < m; i++)
                    {
                        double s = (rnd.NextDouble() * 2 - 1) * scale;
                        pts.Add(new Point3d(p0.X + s * d.X, p0.Y + s * d.Y, p0.Z + s * d.Z));
                    }
                    CloudMath.Pca(pts, Range(m), out var normal, out double eta, out _, out _);

                    double len = Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z);
                    double perp = Math.Abs(normal.X * d.X + normal.Y * d.Y + normal.Z * d.Z); // ⊥ line ⇒ ~0
                    worstLen = Math.Max(worstLen, Math.Abs(len - 1));
                    worstPerp = Math.Max(worstPerp, perp);
                    worstEta = Math.Max(worstEta, Math.Abs(eta));
                }
            }
            // Contract gate: the fit always returns a unit normal (no NaN/crash).
            Assert.True(worstLen < 1e-9, $"T3 normal not unit-length on collinear input: worst |len-1|={worstLen:E3}");
            // Characterization (documented behaviour of a rank-1 scatter matrix):
            Assert.True(worstPerp < 1e-6, $"T3 collinear normal not ⊥ line: worst |n·d|={worstPerp:E3}");
            Assert.True(worstEta < 1e-9, $"T3 collinear planarity η not ≈ 0: worst |η|={worstEta:E3}");
        }

        // ---- independent oracles + geometry helpers --------------------------
        static double Sse(IReadOnlyList<Point3d> pts, Point3d c, Vector3d n)
        {
            double s = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                double dd = (pts[i].X - c.X) * n.X + (pts[i].Y - c.Y) * n.Y + (pts[i].Z - c.Z) * n.Z;
                s += dd * dd;
            }
            return s;
        }

        static int[] Range(int m) { var a = new int[m]; for (int i = 0; i < m; i++) a[i] = i; return a; }
        static double RandC(Random r, double scale) => (r.NextDouble() * 2 - 1) * 10 * scale;

        static Vector3d RandUnit(Random r)
        {
            // uniform-ish direction; reject tiny vectors
            while (true)
            {
                var v = new Vector3d(r.NextDouble() * 2 - 1, r.NextDouble() * 2 - 1, r.NextDouble() * 2 - 1);
                double L = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
                if (L > 1e-3) return new Vector3d(v.X / L, v.Y / L, v.Z / L);
            }
        }

        // orthonormal (u, v, n): two in-plane axes and the plane normal
        static void OrthoBasis(Random r, out Vector3d u, out Vector3d v, out Vector3d n)
        {
            n = RandUnit(r);
            var helper = Math.Abs(n.X) < 0.9 ? new Vector3d(1, 0, 0) : new Vector3d(0, 1, 0);
            u = Cross(n, helper); Unit(ref u);
            v = Cross(n, u); Unit(ref v);
        }

        static Vector3d Cross(Vector3d a, Vector3d b) =>
            new Vector3d(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        static void Unit(ref Vector3d a)
        {
            double L = Math.Sqrt(a.X * a.X + a.Y * a.Y + a.Z * a.Z);
            a = new Vector3d(a.X / L, a.Y / L, a.Z / L);
        }

        // Rodrigues rotation of vector x about unit axis k by angle ang.
        static Vector3d Rodrigues(Vector3d x, Vector3d k, double ang)
        {
            double c = Math.Cos(ang), s = Math.Sin(ang);
            var kx = Cross(k, x);
            double kd = k.X * x.X + k.Y * x.Y + k.Z * x.Z;
            var res = new Vector3d(
                x.X * c + kx.X * s + k.X * kd * (1 - c),
                x.Y * c + kx.Y * s + k.Y * kd * (1 - c),
                x.Z * c + kx.Z * s + k.Z * kd * (1 - c));
            Unit(ref res);
            return res;
        }
    }
}
