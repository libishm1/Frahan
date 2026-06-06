using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.EdgeMatching
{
    /// <summary>
    /// Spatial-3D segmenter. Uses discrete Frenet–Serret invariants
    /// (curvature, torsion) for break-point detection and signature
    /// computation. Curvature is rotation-invariant; torsion flips
    /// sign under reflection so it disambiguates mirror-image
    /// complementary edges. Activated when Panel.Mode == Spatial3D.
    /// </summary>
    public static class BoundarySegmenter3D
    {
        public static List<Segment> Segment(Panel panel, SegmenterOptions3D? opt = null)
        {
            if (panel == null) throw new ArgumentNullException(nameof(panel));
            opt ??= new SegmenterOptions3D();

            var pts = BoundarySegmenter.ResampleByArcLength(panel.SourceContour, opt.SampleSpacing);
            int n = pts.Count;
            if (n < 5) return new List<Segment>();

            double[] kappa = new double[n];
            double[] tau = new double[n];
            ComputeFrenetInvariants(pts, kappa, tau, panel.SourceContour.IsClosed);

            if (opt.ComputeTorsion)
                tau = SmoothGaussian(tau, opt.TorsionSmoothingWindow);

            double thresh = opt.CurvatureBreakThreshold;
            var breaks = new List<int> { 0 };
            int window = Math.Max(1, opt.BreakWindow);
            for (int i = window; i < n - window; i++)
            {
                double maxK = 0.0;
                for (int k = -window; k <= window; k++)
                    if (kappa[i + k] > maxK) maxK = kappa[i + k];
                if (maxK > thresh) breaks.Add(i);
            }
            if (!panel.SourceContour.IsClosed) breaks.Add(n - 1);
            breaks = BoundarySegmenter.Coalesce(breaks, window);

            var segments = new List<Segment>();
            int segIdx = 0;

            // Build one 3D Segment from sample range [s0,s1] (inclusive).
            // Returns false if too short or below MinSegmentLength. Shared by
            // the base break-to-break segments and (when EmitPartials) the
            // partial sub-windows, so each partial recomputes its own curvature
            // / torsion / signed-turn signatures, chord, and sign over its own
            // window — a first-class Segment that SegmentHashIndex buckets in
            // the 3D table and PhaseCorrelator can compare.
            bool TryBuild(int s0, int s1, out Segment seg)
            {
                seg = null!;
                if (s1 - s0 < 2) return false;

                var poly = new Polyline();
                for (int j = s0; j <= s1; j++) poly.Add(pts[j]);

                double chord = poly.First.DistanceTo(poly.Last);
                if (chord < opt.MinSegmentLength) return false;

                double turnSum = IntegrateCurvature(kappa, s0, s1);
                int sign = SignFromTangentRotation(pts, s0, s1, panel.LocalFrame);

                double[] kSig = BoundarySegmenter.ResampleSignal(kappa, s0, s1, opt.SignatureBins);
                double[]? tSig = opt.ComputeTorsion
                    ? BoundarySegmenter.ResampleSignal(tau, s0, s1, opt.SignatureBins)
                    : null;
                double[] turnSig = SignedTurnSignature(pts, s0, s1, panel.LocalFrame, opt.SignatureBins);

                seg = new Segment(
                    panel.Id, segIdx++, poly,
                    chord, turnSum, sign,
                    turnSig, kSig, tSig,
                    panelPlanarityRms: panel.PlanarityRms);
                return true;
            }

            for (int b = 0; b < breaks.Count - 1; b++)
            {
                int i0 = breaks[b];
                int i1 = breaks[b + 1];

                if (TryBuild(i0, i1, out var baseSeg))
                    segments.Add(baseSeg);

                // R1: partial sub-windows. Default-off (PartialWindows returns
                // empty), so this loop is a no-op unless EmitPartials is set.
                foreach (var (ws, we) in BoundarySegmenter.PartialWindows(i0, i1, opt))
                    if (TryBuild(ws, we, out var partSeg))
                        segments.Add(partSeg);
            }
            return segments;
        }

        private static void ComputeFrenetInvariants(
            List<Point3d> p, double[] kappa, double[] tau, bool closed)
        {
            int n = p.Count;
            for (int i = 1; i < n - 1; i++)
            {
                Vector3d e1 = p[i] - p[i - 1];
                Vector3d e2 = p[i + 1] - p[i];
                double l1 = e1.Length, l2 = e2.Length;
                if (l1 < 1e-9 || l2 < 1e-9) { kappa[i] = 0; tau[i] = 0; continue; }
                e1.Unitize(); e2.Unitize();

                Vector3d diff = e2 - e1;
                double meanL = 0.5 * (l1 + l2);
                kappa[i] = diff.Length / meanL;

                if (i >= 2 && i < n - 2)
                {
                    Vector3d b1 = Vector3d.CrossProduct(e1, e2);
                    Vector3d e3 = p[i + 2] - p[i + 1];
                    if (e3.Length > 1e-9)
                    {
                        e3.Unitize();
                        Vector3d b2 = Vector3d.CrossProduct(e2, e3);
                        if (b1.Length > 1e-9 && b2.Length > 1e-9)
                        {
                            b1.Unitize(); b2.Unitize();
                            Vector3d cross = Vector3d.CrossProduct(b1, b2);
                            double sin = cross.Length;
                            double cos = b1 * b2;
                            double signv = (cross * e2) >= 0 ? 1.0 : -1.0;
                            tau[i] = signv * Math.Atan2(sin, cos) / meanL;
                        }
                    }
                }
            }
            if (closed && n > 4)
            {
                tau[0] = tau[n - 2]; tau[n - 1] = tau[1];
                kappa[0] = kappa[n - 2]; kappa[n - 1] = kappa[1];
            }
        }

        private static double[] SmoothGaussian(double[] xs, double window)
        {
            int w = (int)Math.Max(1, Math.Round(window));
            double sigma = Math.Max(window / 2.0, 1e-6);
            int n = xs.Length;
            var result = new double[n];
            for (int i = 0; i < n; i++)
            {
                double sum = 0, wsum = 0;
                for (int k = -w; k <= w; k++)
                {
                    int j = i + k;
                    if (j < 0 || j >= n) continue;
                    double weight = Math.Exp(-(k * k) / (2 * sigma * sigma));
                    sum += weight * xs[j];
                    wsum += weight;
                }
                result[i] = wsum > 0 ? sum / wsum : xs[i];
            }
            return result;
        }

        private static double IntegrateCurvature(double[] kappa, int i0, int i1)
        {
            double s = 0;
            for (int i = i0; i <= i1; i++) s += kappa[i];
            return s;
        }

        private static int SignFromTangentRotation(
            List<Point3d> p, int i0, int i1, Plane frame)
        {
            if (i1 - i0 < 2) return +1;
            Vector3d t0 = p[i0 + 1] - p[i0];
            Vector3d t1 = p[i1] - p[i1 - 1];

            // Map endpoints + tangents to plane-local space, then take
            // the XY signed cross of the two end tangents. Convex/concave
            // is conventionally read off this sign in the local frame.
            frame.RemapToPlaneSpace(p[i0] + t0, out Point3d t0p);
            frame.RemapToPlaneSpace(p[i0], out Point3d o0p);
            frame.RemapToPlaneSpace(p[i1] + t1, out Point3d t1p);
            frame.RemapToPlaneSpace(p[i1], out Point3d o1p);

            double t0x = t0p.X - o0p.X, t0y = t0p.Y - o0p.Y;
            double t1x = t1p.X - o1p.X, t1y = t1p.Y - o1p.Y;
            double cross = t0x * t1y - t0y * t1x;
            return cross >= 0 ? +1 : -1;
        }

        private static double[] SignedTurnSignature(
            List<Point3d> p, int i0, int i1, Plane frame, int bins)
        {
            int n = i1 - i0 + 1;
            if (n < 3 || bins <= 0) return new double[bins];

            double[] turns = new double[n];
            for (int j = 1; j < n - 1; j++)
            {
                frame.RemapToPlaneSpace(p[i0 + j - 1], out Point3d a);
                frame.RemapToPlaneSpace(p[i0 + j], out Point3d b);
                frame.RemapToPlaneSpace(p[i0 + j + 1], out Point3d c);
                double ax = b.X - a.X, ay = b.Y - a.Y;
                double bx = c.X - b.X, by = c.Y - b.Y;
                double cross = ax * by - ay * bx;
                double dot = ax * bx + ay * by;
                turns[j] = Math.Atan2(cross, dot);
            }
            return BoundarySegmenter.ResampleSignal(turns, 0, n - 1, bins);
        }
    }
}
