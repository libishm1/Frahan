#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Cutting;

// =============================================================================
// FractureCutter — split convex Slabs by FINITE-extent FracturePolygons.
//
// Phase E.2 algorithm:
//
//   1. Compute the slab's cross-section on the polygon's supporting plane
//      using SlabCrossSection.Compute. Empty result -> Miss outcome.
//   2. Project both the cross-section and the polygon to the supporting
//      plane's 2D in-plane coordinate frame.
//   3. Test whether the polygon (convex) contains every cross-section vertex
//      (point-in-convex-polygon, with a containment tolerance).
//   4. All vertices contained => Spans outcome -> delegate to SlabCutter
//      using the polygon's supporting plane (infinite-plane equivalent).
//   5. Otherwise => Partial outcome. If options.ExtendPartialToInfinitePlane,
//      cut anyway via SlabCutter and report PartialExtended; otherwise
//      passthrough.
//
// Convex polygon assumption: FracturePolygon enforces this on construction.
// Non-convex polygons (Phase E.3) need a real CSG kernel to handle the case
// where the cut produces a non-convex output piece.
//
// Reference: this approach matches the "DFN-with-finite-discs" approximation
// commonly used in geological discrete-fracture-network modelling. Spans /
// Miss are exact; Partial degrades gracefully.
// =============================================================================

public static class FractureCutter
{
    /// <summary>
    /// Cut one slab by one finite fracture polygon.
    /// </summary>
    public static FractureCutResult Cut(
        Slab slab,
        FracturePolygon polygon,
        FractureCutOptions options = null)
    {
        if (slab == null) throw new ArgumentNullException(nameof(slab));
        if (polygon == null) throw new ArgumentNullException(nameof(polygon));
        options = options ?? FractureCutOptions.Default;

        var plane = polygon.SupportingPlane;
        var crossSection = SlabCrossSection.Compute(slab, plane, options.Epsilon);

        if (crossSection.Length == 0)
        {
            return new FractureCutResult(new[] { slab }, FractureCutOutcome.Miss);
        }

        bool spans = ContainsAllInPlane(polygon, crossSection, plane, options.ContainmentTolerance);
        if (spans)
        {
            var pieces = SlabCutter.Cut(slab, plane, options.Epsilon);
            return new FractureCutResult(pieces.Slabs, FractureCutOutcome.Spans);
        }

        if (options.ExtendPartialToInfinitePlane)
        {
            var pieces = SlabCutter.Cut(slab, plane, options.Epsilon);
            return new FractureCutResult(pieces.Slabs, FractureCutOutcome.PartialExtended);
        }

        return new FractureCutResult(new[] { slab }, FractureCutOutcome.Partial);
    }

    /// <summary>
    /// Cut every slab in <paramref name="slabs"/> by every fracture polygon
    /// in <paramref name="polygons"/>, applied iteratively. Per-piece
    /// outcomes are not preserved across multi-polygon application; the
    /// returned outcome list reports one outcome per (slab, polygon) pass
    /// in input order. To preserve provenance use the single-cut overload
    /// in a manual loop.
    /// </summary>
    public static IReadOnlyList<Slab> CutMany(
        IReadOnlyList<Slab> slabs,
        IReadOnlyList<FracturePolygon> polygons,
        FractureCutOptions options = null)
    {
        if (slabs == null) throw new ArgumentNullException(nameof(slabs));
        if (polygons == null) throw new ArgumentNullException(nameof(polygons));
        options = options ?? FractureCutOptions.Default;

        var current = new List<Slab>(slabs.Count);
        for (int i = 0; i < slabs.Count; i++)
        {
            if (slabs[i] == null) throw new ArgumentException($"slabs[{i}] is null", nameof(slabs));
            current.Add(slabs[i]);
        }

        for (int p = 0; p < polygons.Count; p++)
        {
            if (polygons[p] == null) throw new ArgumentException($"polygons[{p}] is null", nameof(polygons));
            var poly = polygons[p];
            var next = new List<Slab>(current.Count * 2);
            for (int i = 0; i < current.Count; i++)
            {
                var r = Cut(current[i], poly, options);
                for (int k = 0; k < r.Slabs.Count; k++) next.Add(r.Slabs[k]);
            }
            current = next;
        }

        return current;
    }

    // -------------------------------------------------------------------------
    // 2D in-plane containment: every cross-section vertex inside or on the
    // (convex) fracture polygon, within ContainmentTolerance.
    // -------------------------------------------------------------------------

    private static bool ContainsAllInPlane(
        FracturePolygon polygon,
        double[] crossSectionXyz,
        FracturePlane plane,
        double tolerance)
    {
        // Build orthonormal in-plane basis (u, v) with u x v = +plane.normal.
        BuildInPlaneBasis(plane, out double ox, out double oy, out double oz,
                          out double ux, out double uy, out double uz,
                          out double vx, out double vy, out double vz);

        // Project the polygon to 2D.
        int polyN = polygon.VertexCount;
        var poly2 = new double[2 * polyN];
        var pts = polygon.BoundaryPointsXyz;
        for (int i = 0; i < polyN; i++)
        {
            double dx = pts[3 * i] - ox;
            double dy = pts[3 * i + 1] - oy;
            double dz = pts[3 * i + 2] - oz;
            poly2[2 * i] = dx * ux + dy * uy + dz * uz;
            poly2[2 * i + 1] = dx * vx + dy * vy + dz * vz;
        }

        // Determine polygon winding sign. (FracturePolygon ctor verified
        // convexity; here we just record whether the boundary cross-product
        // sign is positive or negative so the half-plane test agrees.)
        double signSum = 0.0;
        for (int i = 0; i < polyN; i++)
        {
            int j = (i + 1) % polyN;
            int k = (i + 2) % polyN;
            double e1x = poly2[2 * j] - poly2[2 * i];
            double e1y = poly2[2 * j + 1] - poly2[2 * i + 1];
            double e2x = poly2[2 * k] - poly2[2 * j];
            double e2y = poly2[2 * k + 1] - poly2[2 * j + 1];
            signSum += e1x * e2y - e1y * e2x;
        }
        double sign = signSum >= 0.0 ? 1.0 : -1.0;

        // Test each cross-section point against every polygon edge half-plane.
        int xsN = crossSectionXyz.Length / 3;
        for (int q = 0; q < xsN; q++)
        {
            double dx = crossSectionXyz[3 * q] - ox;
            double dy = crossSectionXyz[3 * q + 1] - oy;
            double dz = crossSectionXyz[3 * q + 2] - oz;
            double qu = dx * ux + dy * uy + dz * uz;
            double qv = dx * vx + dy * vy + dz * vz;

            for (int i = 0; i < polyN; i++)
            {
                int j = (i + 1) % polyN;
                double ex = poly2[2 * j] - poly2[2 * i];
                double ey = poly2[2 * j + 1] - poly2[2 * i + 1];
                double rx = qu - poly2[2 * i];
                double ry = qv - poly2[2 * i + 1];
                double cross = ex * ry - ey * rx;
                if (sign * cross < -tolerance) return false; // outside this edge
            }
        }
        return true;
    }

    private static void BuildInPlaneBasis(
        FracturePlane plane,
        out double ox, out double oy, out double oz,
        out double ux, out double uy, out double uz,
        out double vx, out double vy, out double vz)
    {
        ox = plane.PointX; oy = plane.PointY; oz = plane.PointZ;
        double nx = plane.NormalX, ny = plane.NormalY, nz = plane.NormalZ;
        double ax = Math.Abs(nx), ay = Math.Abs(ny), az = Math.Abs(nz);
        double rx, ry, rz;
        if (ax <= ay && ax <= az) { rx = 1; ry = 0; rz = 0; }
        else if (ay <= ax && ay <= az) { rx = 0; ry = 1; rz = 0; }
        else { rx = 0; ry = 0; rz = 1; }

        ux = ny * rz - nz * ry;
        uy = nz * rx - nx * rz;
        uz = nx * ry - ny * rx;
        double ulen = Math.Sqrt(ux * ux + uy * uy + uz * uz);
        ux /= ulen; uy /= ulen; uz /= ulen;
        vx = ny * uz - nz * uy;
        vy = nz * ux - nx * uz;
        vz = nx * uy - ny * ux;
        double vlen = Math.Sqrt(vx * vx + vy * vy + vz * vz);
        vx /= vlen; vy /= vlen; vz /= vlen;
    }
}
