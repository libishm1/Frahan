#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Cutting;

// =============================================================================
// FracturePolygon — finite-extent oriented planar polygon representing a real
// fracture surface in a quarry or masonry block.
//
// Constraints (Phase E.2):
//   - Polygon must be convex.
//   - Polygon must be planar within a coplanarity tolerance.
//   - At least 3 vertices.
//
// The supporting plane is derived from the first 3 non-collinear vertices;
// its normal direction follows the polygon's CCW orientation (right-hand rule).
//
// Non-convex fracture polygons are deferred to Phase E.3 (would require a
// real CSG kernel for partial-traversal cuts that produce non-convex output).
// =============================================================================

public sealed class FracturePolygon
{
    private const double DefaultPlanarityTolerance = 1e-6;
    private const double DefaultConvexityTolerance = 1e-9;

    public FracturePolygon(
        IReadOnlyList<double> boundaryPointsXyz,
        double planarityTolerance = DefaultPlanarityTolerance,
        double convexityTolerance = DefaultConvexityTolerance)
    {
        if (boundaryPointsXyz == null) throw new ArgumentNullException(nameof(boundaryPointsXyz));
        if (boundaryPointsXyz.Count % 3 != 0)
            throw new ArgumentException(
                $"boundaryPointsXyz length must be a multiple of 3, got {boundaryPointsXyz.Count}",
                nameof(boundaryPointsXyz));

        int n = boundaryPointsXyz.Count / 3;
        if (n < 3)
            throw new ArgumentException(
                $"polygon needs at least 3 vertices, got {n}",
                nameof(boundaryPointsXyz));

        // ---- Derive plane from the first 3 non-collinear vertices ----
        if (!TryDerivePlane(boundaryPointsXyz, out double px, out double py, out double pz,
                            out double nx, out double ny, out double nz))
        {
            throw new ArgumentException(
                "polygon vertices are collinear; cannot derive supporting plane",
                nameof(boundaryPointsXyz));
        }

        // ---- Verify all vertices are coplanar (within tolerance) ----
        var plane = new FracturePlane(px, py, pz, nx, ny, nz);
        for (int i = 0; i < n; i++)
        {
            double d = plane.SignedDistance(
                boundaryPointsXyz[3 * i],
                boundaryPointsXyz[3 * i + 1],
                boundaryPointsXyz[3 * i + 2]);
            if (Math.Abs(d) > planarityTolerance)
                throw new ArgumentException(
                    $"polygon vertex {i} is {Math.Abs(d):0.###e+00} from the supporting plane (tol {planarityTolerance:0.###e+00}); polygon must be planar",
                    nameof(boundaryPointsXyz));
        }

        // ---- Verify convexity: all cross products of consecutive edges
        //      must have the same sign (project onto the plane normal). ----
        VerifyConvex(boundaryPointsXyz, plane, convexityTolerance);

        BoundaryPointsXyz = boundaryPointsXyz;
        SupportingPlane = plane;
    }

    public IReadOnlyList<double> BoundaryPointsXyz { get; }
    public FracturePlane SupportingPlane { get; }

    public int VertexCount => BoundaryPointsXyz.Count / 3;

    public override string ToString() =>
        $"FracturePolygon(V={VertexCount}, plane=({SupportingPlane.NormalX:0.##}," +
        $"{SupportingPlane.NormalY:0.##},{SupportingPlane.NormalZ:0.##}))";

    // -------------------------------------------------------------------------

    private static bool TryDerivePlane(
        IReadOnlyList<double> pts,
        out double px, out double py, out double pz,
        out double nx, out double ny, out double nz)
    {
        int n = pts.Count / 3;
        double a0 = pts[0], a1 = pts[1], a2 = pts[2];
        for (int i = 1; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                double bx = pts[3 * i] - a0, by = pts[3 * i + 1] - a1, bz = pts[3 * i + 2] - a2;
                double cx = pts[3 * j] - a0, cy = pts[3 * j + 1] - a1, cz = pts[3 * j + 2] - a2;
                double tx = by * cz - bz * cy;
                double ty = bz * cx - bx * cz;
                double tz = bx * cy - by * cx;
                double m2 = tx * tx + ty * ty + tz * tz;
                if (m2 > 1e-20)
                {
                    px = a0; py = a1; pz = a2;
                    nx = tx; ny = ty; nz = tz; // FracturePlane ctor will normalise
                    return true;
                }
            }
        }
        px = py = pz = nx = ny = nz = 0;
        return false;
    }

    private static void VerifyConvex(
        IReadOnlyList<double> pts,
        FracturePlane plane,
        double convexityTolerance)
    {
        int n = pts.Count / 3;
        double nx = plane.NormalX, ny = plane.NormalY, nz = plane.NormalZ;
        double sign = 0.0;

        for (int i = 0; i < n; i++)
        {
            int prev = (i + n - 1) % n;
            int curr = i;
            int next = (i + 1) % n;

            double e1x = pts[3 * curr] - pts[3 * prev];
            double e1y = pts[3 * curr + 1] - pts[3 * prev + 1];
            double e1z = pts[3 * curr + 2] - pts[3 * prev + 2];
            double e2x = pts[3 * next] - pts[3 * curr];
            double e2y = pts[3 * next + 1] - pts[3 * curr + 1];
            double e2z = pts[3 * next + 2] - pts[3 * curr + 2];

            double cx = e1y * e2z - e1z * e2y;
            double cy = e1z * e2x - e1x * e2z;
            double cz = e1x * e2y - e1y * e2x;
            double dot = cx * nx + cy * ny + cz * nz;

            if (Math.Abs(dot) <= convexityTolerance) continue; // collinear-vertex skip
            double s = Math.Sign(dot);
            if (sign == 0.0) sign = s;
            else if (s != sign)
                throw new ArgumentException(
                    $"polygon is non-convex at vertex {i}; Phase E.2 requires convex fracture polygons",
                    nameof(pts));
        }
    }

    // -------------------------------------------------------------------------
    // Static factories for common shapes.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Axis-aligned rectangle on the XY plane at <paramref name="z"/>,
    /// spanning <paramref name="minX"/>..<paramref name="maxX"/> and
    /// <paramref name="minY"/>..<paramref name="maxY"/>. CCW from +Z view.
    /// </summary>
    public static FracturePolygon RectangleXY(double minX, double maxX, double minY, double maxY, double z)
    {
        if (!(maxX > minX) || !(maxY > minY))
            throw new ArgumentException("max must exceed min on both axes");

        var pts = new[]
        {
            minX, minY, z,
            maxX, minY, z,
            maxX, maxY, z,
            minX, maxY, z,
        };
        return new FracturePolygon(pts);
    }
}
