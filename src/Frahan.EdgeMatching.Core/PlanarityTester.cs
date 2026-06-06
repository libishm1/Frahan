using System;
using Rhino.Geometry;

namespace Frahan.EdgeMatching
{
    /// <summary>
    /// PCA best-fit plane and RMS deviation for a contour. Used to
    /// decide whether a panel takes the planar 2D or spatial 3D path.
    /// </summary>
    public static class PlanarityTester
    {
        public static (Plane plane, double rms) BestFitPlane(PolylineCurve curve)
        {
            if (curve == null) throw new ArgumentNullException(nameof(curve));
            var pts = curve.ToPolyline();
            if (pts == null || pts.Count < 3) return (Plane.WorldXY, 0.0);

            double cx = 0, cy = 0, cz = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                cx += pts[i].X; cy += pts[i].Y; cz += pts[i].Z;
            }
            cx /= pts.Count; cy /= pts.Count; cz /= pts.Count;
            var centroid = new Point3d(cx, cy, cz);

            Plane plane;
            var fit = Plane.FitPlaneToPoints(pts, out plane, out _);
            if (fit != PlaneFitResult.Success || !plane.IsValid)
                plane = new Plane(centroid, Vector3d.ZAxis);

            double sum = 0.0;
            for (int i = 0; i < pts.Count; i++)
            {
                double d = plane.DistanceTo(pts[i]);
                sum += d * d;
            }
            double rms = Math.Sqrt(sum / pts.Count);
            return (plane, rms);
        }

        public static bool IsPlanar(PolylineCurve curve, double tolerance)
        {
            var (_, rms) = BestFitPlane(curve);
            return rms <= tolerance;
        }

        /// <summary>Transform that takes the best-fit plane to WorldXY (flatten step for 2D pipeline).</summary>
        public static Transform FlattenTransform(Plane bestFit)
        {
            return Transform.PlaneToPlane(bestFit, Plane.WorldXY);
        }
    }
}
