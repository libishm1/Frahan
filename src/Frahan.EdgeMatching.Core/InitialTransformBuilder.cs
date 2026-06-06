using System;
using Rhino.Geometry;

namespace Frahan.EdgeMatching
{
    /// <summary>
    /// Constructs an initial rigid transform that brings segment A onto
    /// segment B given the coarse phase-correlation lag. Two variants:
    /// 2D uses an XY frame anchored at A's start point and B's lag-shifted
    /// sample; 3D uses the discrete Frenet frame (tangent + principal
    /// normal). Both flip B's frame to enforce the complement orientation
    /// (matching edges traverse the fracture in opposite senses).
    /// </summary>
    public static class InitialTransformBuilder
    {
        public static Transform FromLag2D(Segment a, Segment b, int lag)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            var aPts = a.LocalPolyline;
            var bPts = b.LocalPolyline;
            int n = a.TurningSignature.Length;
            int bIdx = (n - 1 - lag + n) % n;

            var aStart = aPts[0];
            int bPolyIdx = bPts.Count <= 1
                ? 0
                : Math.Min(bIdx * (bPts.Count - 1) / Math.Max(1, n - 1), bPts.Count - 1);
            var bTarget = bPts[bPolyIdx];

            var aDir = aPts.Count > 1 ? aPts[1] - aPts[0] : Vector3d.XAxis;
            var bDir = bPts.Count > 1
                ? bPts[Math.Max(1, bPolyIdx)] - bPts[Math.Max(0, bPolyIdx - 1)]
                : Vector3d.XAxis;

            var aPlane = new Plane(aStart, aDir, Vector3d.ZAxis);
            var bPlane = new Plane(bTarget, -bDir, Vector3d.ZAxis);
            return Transform.PlaneToPlane(aPlane, bPlane);
        }

        public static Transform FromLag3D(Segment a, Segment b, int lag)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            var aFrame = FrenetFrameAt(a, 0);
            var bFrame = FrenetFrameAtLag(b, lag);
            bFrame = new Plane(bFrame.Origin, -bFrame.XAxis, bFrame.YAxis);
            return Transform.PlaneToPlane(aFrame, bFrame);
        }

        private static Plane FrenetFrameAt(Segment s, int index)
        {
            var p = s.LocalPolyline;
            int i = Math.Max(1, Math.Min(p.Count - 2, index));
            Vector3d t = p[i + 1] - p[i - 1];
            if (!t.Unitize()) t = Vector3d.XAxis;

            Vector3d acc = (p[i + 1] - p[i]) - (p[i] - p[i - 1]);
            Vector3d n = acc - (acc * t) * t;
            if (n.Length < 1e-6) n = Vector3d.CrossProduct(t, Vector3d.ZAxis);
            if (!n.Unitize()) n = Vector3d.YAxis;
            return new Plane(p[i], t, n);
        }

        private static Plane FrenetFrameAtLag(Segment s, int lag)
        {
            int nSig = s.CurvatureSignature.Length;
            int polyN = s.LocalPolyline.Count;
            int bIdx = nSig == 0 ? 0 : (nSig - 1 - lag + nSig) % nSig;
            int denom = Math.Max(1, nSig - 1);
            int polyIdx = polyN <= 1 ? 0 : Math.Min(bIdx * (polyN - 1) / denom, polyN - 1);
            return FrenetFrameAt(s, polyIdx);
        }
    }
}
