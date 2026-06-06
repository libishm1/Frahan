#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Core;
using Rhino.Geometry;

namespace Frahan.Surface;

/// <summary>
/// Rhino-bound builder that converts a closed planar Rhino <see cref="Curve"/>
/// into a <see cref="FragmentDescriptor"/> with one <see cref="EdgeDescriptor"/>
/// per polyline segment. Counterpart to <see cref="BoundaryRailBuilder"/>:
/// where the builder populates the rail index, this builder produces the
/// query side (fragment edges to look up).
///
/// Spec 5 + the proposed "Frahan Fragment Descriptors" GH component
/// (runbook section 16.1).
/// </summary>
public static class FragmentDescriptorBuilder
{
    /// <summary>
    /// Build a <see cref="FragmentDescriptor"/> from a closed planar Rhino curve.
    /// </summary>
    /// <param name="id">Caller-supplied fragment identifier.</param>
    /// <param name="boundary">Closed planar Rhino curve.</param>
    /// <param name="zoneId">Zone the fragment belongs to (ZoneBucket on every emitted EdgeDescriptor).</param>
    /// <param name="discretisationTolerance">Tolerance for ToPolyline conversion.</param>
    /// <returns>Populated FragmentDescriptor, or null if the input is invalid.</returns>
    public static FragmentDescriptor BuildFromCurve(
        string id,
        Curve boundary,
        int zoneId,
        double discretisationTolerance = 0.01)
    {
        if (id == null) throw new ArgumentNullException(nameof(id));
        if (boundary == null) throw new ArgumentNullException(nameof(boundary));
        if (discretisationTolerance <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(discretisationTolerance), "must be > 0");

        // Convert curve to polyline so the per-edge analysis is deterministic.
        var polylineCurve = boundary.ToPolyline(0, 0, 0.0, 0.0, 0.0, discretisationTolerance, 0.0, 0.0, true);
        if (polylineCurve == null) return null;
        if (!polylineCurve.TryGetPolyline(out var poly) || poly.Count < 4) return null;

        // Drop the closing-duplicate vertex if present so segment iteration
        // produces N edges from N+1 vertices.
        int n = poly.Count;
        if (poly[0].EpsilonEquals(poly[n - 1], 1e-9)) n--;
        if (n < 3) return null;

        // Compute summary geometry (area, perimeter, bbox) from the polyline.
        double area = SignedArea2D(poly, n);
        double absArea = Math.Abs(area);
        double perimeter = 0.0;
        var bbox = BoundingBox.Empty;
        for (int i = 0; i < n; i++) bbox.Union(poly[i]);
        for (int i = 0; i < n; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % n];
            perimeter += a.DistanceTo(b);
        }

        var bboxSize = bbox.Diagonal;
        double w = Math.Max(1e-12, Math.Abs(bboxSize.X));
        double h = Math.Max(1e-12, Math.Abs(bboxSize.Y));
        double aspect = w >= h ? w / h : h / w;

        // Build per-segment EdgeDescriptors.
        var edges = new List<EdgeDescriptor>(n);
        for (int i = 0; i < n; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % n];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-12) continue;

            double angleDeg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            // Wrap to [0, 360) for stable bucketing.
            if (angleDeg < 0) angleDeg += 360.0;

            // Polyline edges are straight: curvature = 0 by construction.
            // Straightness score = 0 by the same definition (chord = arc).
            edges.Add(new EdgeDescriptor(
                length: len,
                angleDegrees: angleDeg,
                curvatureScore: 0.0,
                straightnessScore: 0.0,
                zoneId: zoneId));
        }

        return new FragmentDescriptor(
            id: id,
            area: absArea,
            perimeter: perimeter,
            aspectRatio: aspect,
            edges: edges);
    }

    /// <summary>
    /// Signed 2D area of a polyline (shoelace formula). Positive when the
    /// polyline winds CCW in XY; negative when CW. Pure managed; called with
    /// the Rhino Polyline indexer.
    /// </summary>
    private static double SignedArea2D(Polyline poly, int n)
    {
        double sum = 0.0;
        for (int i = 0; i < n; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % n];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return 0.5 * sum;
    }
}
