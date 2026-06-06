using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Geometry;

namespace Frahan.EdgeMatching
{
    /// <summary>
    /// Splits a planar contour into segments at curvature break-points,
    /// then computes the canonical signed-turning signature per segment.
    /// Assumes the panel contour is XY-planar (Panel.Mode == Planar2D
    /// with LocalFrame.Normal aligned to WorldXY's Z); upstream is
    /// responsible for flattening if not. The spatial 3D path is handled
    /// by BoundarySegmenter3D and dispatched via Panel.Mode at call sites.
    /// </summary>
    public static class BoundarySegmenter
    {
        public static List<Segment> Segment(Panel panel, SegmenterOptions? opt = null)
        {
            if (panel == null) throw new ArgumentNullException(nameof(panel));
            opt ??= new SegmenterOptions();

            var local = panel.SourceContour;
            var pts = ResampleByArcLength(local, opt.SampleSpacing);
            int n = pts.Count;
            if (n < 3) return new List<Segment>();

            var turning = new double[n];
            for (int i = 1; i < n - 1; i++)
                turning[i] = SignedTurn(pts[i - 1], pts[i], pts[i + 1]);
            if (local.IsClosed && n > 2)
            {
                turning[0] = SignedTurn(pts[n - 2], pts[0], pts[1]);
                turning[n - 1] = turning[0];
            }

            double thresh = RhinoMath.ToRadians(opt.BreakAngleDeg);
            var breaks = new List<int> { 0 };
            for (int i = opt.BreakWindow; i < n - opt.BreakWindow; i++)
            {
                double sum = 0.0;
                for (int k = -opt.BreakWindow; k <= opt.BreakWindow; k++)
                    sum += turning[i + k];
                if (Math.Abs(sum) > thresh) breaks.Add(i);
            }
            if (!local.IsClosed) breaks.Add(n - 1);
            breaks = Coalesce(breaks, opt.BreakWindow);

            var segments = new List<Segment>();
            int segIdx = 0;

            // Build one Segment from sample range [s0,s1] (inclusive). Returns
            // false if the range is too short or below MinSegmentLength so the
            // caller skips it. Used for both the base break-to-break segments
            // and (when EmitPartials) the partial sub-windows, so a partial is
            // an ordinary Segment with its OWN recomputed turning/curvature
            // signature, chord, and sign — exactly what SegmentHashIndex buckets
            // and PhaseCorrelator (equal-length) compare.
            bool TryBuild(int s0, int s1, out Segment seg)
            {
                seg = null!;
                if (s1 - s0 < 2) return false;

                var poly = new Polyline();
                for (int j = s0; j <= s1; j++) poly.Add(pts[j]);

                double chord = poly.First.DistanceTo(poly.Last);
                if (chord < opt.MinSegmentLength) return false;

                double turnSum = 0.0;
                for (int j = s0 + 1; j < s1; j++) turnSum += turning[j];
                int sign = turnSum >= 0 ? +1 : -1;

                double[] turnSig = ResampleSignal(turning, s0, s1, opt.SignatureBins);
                double[] kSig = new double[opt.SignatureBins];
                for (int j = 0; j < opt.SignatureBins; j++) kSig[j] = Math.Abs(turnSig[j]);

                seg = new Segment(
                    panel.Id, segIdx++, poly,
                    chord, turnSum, sign,
                    turnSig, kSig, null);
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
                foreach (var (ws, we) in PartialWindows(i0, i1, opt))
                    if (TryBuild(ws, we, out var partSeg))
                        segments.Add(partSeg);
            }
            return segments;
        }

        /// <summary>
        /// R1 partial emission. For a base window [i0,i1] over a resampled
        /// polyline, returns the deterministic list of partial sub-window index
        /// ranges (each an inclusive [start,end] pair over the SAME global
        /// sample indices). For each fraction f in opt.PartialFractions, the
        /// window length is round(f * (i1-i0)) samples; windows slide from i0
        /// by a stride of round(strideFraction * windowLen) samples, fixed
        /// order: fractions outer (in declared order), window position inner
        /// (ascending). Windows shorter than 2 samples or below MinSegmentLength
        /// (chord) are dropped by the caller, which also recomputes signatures.
        /// Returns empty when partials are off or no valid window fits, so the
        /// default-off path adds nothing.
        /// </summary>
        public static List<(int start, int end)> PartialWindows(
            int i0, int i1, SegmenterOptions opt)
        {
            var windows = new List<(int, int)>();
            if (opt == null || !opt.EmitPartials || opt.PartialFractions == null) return windows;
            int span = i1 - i0;            // number of intervals in the base window
            if (span < 2) return windows;

            double stride = opt.PartialStrideFraction;
            if (stride < 0.1) stride = 0.1;
            if (stride > 1.0) stride = 1.0;

            foreach (double frac in opt.PartialFractions)
            {
                if (frac <= 0.0 || frac >= 1.0) continue;   // 1.0 == base segment
                int winLen = (int)Math.Round(frac * span);
                if (winLen < 2) continue;
                int stridePts = (int)Math.Round(stride * winLen);
                if (stridePts < 1) stridePts = 1;

                for (int s = i0; s + winLen <= i1; s += stridePts)
                    windows.Add((s, s + winLen));
            }
            return windows;
        }

        internal static List<Point3d> ResampleByArcLength(Curve c, double spacing)
        {
            double L = c.GetLength();
            int n = Math.Max(8, (int)Math.Ceiling(L / Math.Max(spacing, 1e-9)));
            var ts = c.DivideByCount(n, true);
            var pts = new List<Point3d>(ts.Length);
            foreach (var ti in ts) pts.Add(c.PointAt(ti));
            return pts;
        }

        internal static double SignedTurn(Point3d a, Point3d b, Point3d c)
        {
            double ax = b.X - a.X, ay = b.Y - a.Y;
            double bx = c.X - b.X, by = c.Y - b.Y;
            double cross = ax * by - ay * bx;
            double dot = ax * bx + ay * by;
            return Math.Atan2(cross, dot);
        }

        internal static List<int> Coalesce(List<int> idxs, int window)
        {
            var result = new List<int>();
            int last = -window - 1;
            foreach (var i in idxs)
            {
                if (i - last > window) { result.Add(i); last = i; }
            }
            return result;
        }

        internal static double[] ResampleSignal(double[] xs, int i0, int i1, int bins)
        {
            int n = i1 - i0 + 1;
            double total = n - 1;
            double[] sig = new double[bins];
            if (bins <= 0) return sig;
            if (n == 1) { for (int j = 0; j < bins; j++) sig[j] = xs[i0]; return sig; }

            for (int j = 0; j < bins; j++)
            {
                double u = (double)j / Math.Max(1, bins - 1) * total;
                int k = (int)Math.Floor(u);
                if (k >= n - 1) { sig[j] = xs[i1]; continue; }
                if (k < 0) { sig[j] = xs[i0]; continue; }
                double t = u - k;
                sig[j] = (1 - t) * xs[i0 + k] + t * xs[i0 + k + 1];
            }
            return sig;
        }
    }
}
