#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Surface
{
    /// <summary>
    /// Maps a 2D curve (in the flat UV chart, real units) back to its 3D surface curve.
    /// Pipeline:
    ///   1. Sample the curve as a polyline.
    ///   2. Search for the containing triangle using explicit 2D barycentric coordinates.
    ///   3. Fall back to Mesh.ClosestMeshPoint for points slightly outside the chart boundary.
    ///   4. Reconstruct the 3D position using the same barycentric weights applied
    ///      to the corresponding 3D surface triangle.
    /// Returns null if any point cannot be mapped (caller receives a clear failure,
    /// not a silently corrupt curve).
    ///
    /// Coordinate space contract:
    ///   curve2D  — real units, Z=0 plane (same space as FrahanSurfaceChart.FlatMesh).
    ///   flatMesh — real units, Z=0 plane (FrahanSurfaceChart.FlatMesh, already scaled).
    ///   surfaceMesh — 3D model units (FrahanSurfaceChart.SurfaceMesh3D).
    ///   Face i in flatMesh corresponds exactly to face i in surfaceMesh.
    /// </summary>
    public static class BarycentricMapper2DTo3D
    {
        /// <summary>
        /// Maps a single 2D curve to a 3D polyline curve.
        /// curve2D and flatMesh must be in the same real-unit coordinate space (Z=0).
        /// </summary>
        public static Curve MapCurveTo3DSurface(
            Curve curve2D,
            Mesh flatMesh,
            Mesh surfaceMesh,
            double samplingTolerance = 0.01)
        {
            if (curve2D == null) return null;
            if (flatMesh == null) throw new ArgumentNullException(nameof(flatMesh));
            if (surfaceMesh == null) throw new ArgumentNullException(nameof(surfaceMesh));

            if (flatMesh.Faces.Count != surfaceMesh.Faces.Count)
                throw new ArgumentException(
                    "flatMesh and surfaceMesh must have the same face count and topology.");

            var poly2D = curve2D.ToPolyline(
                0, 0, 0, 0, 0, samplingTolerance, 0, 0, true);
            if (poly2D == null || poly2D.PointCount == 0) return null;

            var points3D = new List<Point3d>(poly2D.PointCount);

            for (int i = 0; i < poly2D.PointCount; i++)
            {
                var pt = poly2D.Point(i);
                // Both curve2D and flatMesh are in real units — use XY directly.
                var uvPt = new Point3d(pt.X, pt.Y, 0.0);

                var mapped = MapSinglePoint(uvPt, flatMesh, surfaceMesh, samplingTolerance);
                if (mapped == Point3d.Unset) return null;

                points3D.Add(mapped);
            }

            return new PolylineCurve(points3D);
        }

        /// <summary>
        /// Maps a list of 2D curves. Each curve that fails mapping is reported in failedIndices.
        /// </summary>
        public static List<Curve> MapCurvesTo3DSurface(
            IReadOnlyList<Curve> curves2D,
            Mesh flatMesh,
            Mesh surfaceMesh,
            out List<int> failedIndices,
            double samplingTolerance = 0.01)
        {
            failedIndices = new List<int>();
            var results = new List<Curve>(curves2D.Count);

            for (int i = 0; i < curves2D.Count; i++)
            {
                var mapped = MapCurveTo3DSurface(
                    curves2D[i], flatMesh, surfaceMesh, samplingTolerance);

                if (mapped == null) failedIndices.Add(i);
                results.Add(mapped); // null = failed, caller can check failedIndices
            }

            return results;
        }

        /// <summary>
        /// Maps a single 2D point (real units, Z=0) to its 3D surface position.
        /// Both uvPt and flatMesh must be in the same real-unit coordinate space.
        /// Returns Point3d.Unset if the point is outside the chart.
        /// </summary>
        public static Point3d MapPoint(Point3d uvPt, Mesh flatMesh, Mesh surfaceMesh,
            double samplingTolerance = 0.01)
            => MapSinglePoint(uvPt, flatMesh, surfaceMesh, samplingTolerance);

        // --- Internal ----------------------------------------------------------

        internal static Point3d MapSinglePoint(
            Point3d uvPt, Mesh flatMesh, Mesh surfaceMesh, double samplingTolerance = 0.01)
        {
            // Pass 1: explicit barycentric search across all flat triangles.
            // O(N) per point — acceptable for mesh densities < ~2000 faces.
            for (int f = 0; f < flatMesh.Faces.Count; f++)
            {
                var ff = flatMesh.Faces[f];
                if (!ff.IsTriangle) continue;

                Point3d pA = flatMesh.Vertices[ff.A];
                Point3d pB = flatMesh.Vertices[ff.B];
                Point3d pC = flatMesh.Vertices[ff.C];

                if (!TryBarycentricCoords2D(uvPt, pA, pB, pC,
                    out double wA, out double wB, out double wC)) continue;

                return BlendSurfacePoint(surfaceMesh, f, wA, wB, wC);
            }

            // Pass 2: ClosestMeshPoint fallback for points very slightly outside
            // the chart boundary. Tolerance is relative to the sampling accuracy.
            double fallbackTol = samplingTolerance * 5.0;
            MeshPoint mp = flatMesh.ClosestMeshPoint(uvPt, fallbackTol);
            if (mp == null) return Point3d.Unset;

            int fi = mp.FaceIndex;
            if (fi < 0 || fi >= surfaceMesh.Faces.Count) return Point3d.Unset;

            double[] t = mp.T;
            if (t == null || t.Length < 3) return Point3d.Unset;

            return BlendSurfacePoint(surfaceMesh, fi, t[0], t[1], t[2]);
        }

        /// <summary>
        /// Computes barycentric coordinates for point p in the 2D triangle (a, b, c).
        /// Returns true only when p is inside or on the triangle edge (small epsilon).
        /// </summary>
        private static bool TryBarycentricCoords2D(
            Point3d p, Point3d a, Point3d b, Point3d c,
            out double wA, out double wB, out double wC)
        {
            wA = wB = wC = 0.0;

            double v0x = b.X - a.X, v0y = b.Y - a.Y;
            double v1x = c.X - a.X, v1y = c.Y - a.Y;
            double v2x = p.X - a.X, v2y = p.Y - a.Y;

            double d00 = v0x * v0x + v0y * v0y;
            double d01 = v0x * v1x + v0y * v1y;
            double d11 = v1x * v1x + v1y * v1y;
            double d20 = v2x * v0x + v2y * v0y;
            double d21 = v2x * v1x + v2y * v1y;

            double denom = d00 * d11 - d01 * d01;
            if (Math.Abs(denom) < 1e-12) return false;

            wB = (d11 * d20 - d01 * d21) / denom;
            wC = (d00 * d21 - d01 * d20) / denom;
            wA = 1.0 - wB - wC;

            const double eps = 1e-6;
            return wA >= -eps && wB >= -eps && wC >= -eps;
        }

        private static Point3d BlendSurfacePoint(
            Mesh surfaceMesh, int faceIndex, double wA, double wB, double wC)
        {
            var sf = surfaceMesh.Faces[faceIndex];
            Point3d a = surfaceMesh.Vertices[sf.A];
            Point3d b = surfaceMesh.Vertices[sf.B];
            Point3d c = surfaceMesh.Vertices[sf.C];

            return new Point3d(
                a.X * wA + b.X * wB + c.X * wC,
                a.Y * wA + b.Y * wB + c.Y * wC,
                a.Z * wA + b.Z * wB + c.Z * wC);
        }
    }
}
