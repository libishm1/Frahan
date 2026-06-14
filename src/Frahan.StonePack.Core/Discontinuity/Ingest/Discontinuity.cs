#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Core.Discontinuity.Ingest;

// =============================================================================
// Discontinuity ingest model -- the managed result of reading mapped structural
// discontinuities (joints / faults / bedding / measured planes / digitised
// traces) from a vector file (CSV / GeoJSON / DXF / shapefile). This is the
// INVERSE companion to the point-cloud worker: where the worker DISCOVERS sets
// from a scan, this INGESTS orientations a geologist already measured or a
// CloudCompare / Compass / field-compass survey already exported.
//
// Rhino-LIGHT: Point3d / Vector3d are used as value containers only (no Rhino
// runtime calls), so the model + readers are headless-unit-testable. Poles are
// stored as lower-hemisphere unit normals, consistent with OrientationMath.
//
// References:
//   - ISRM Suggested Methods (Brown 1981): a discontinuity is characterised by
//     dip / dip-direction (orientation), plus spacing / persistence / aperture.
//   - Riquelme et al. (2014/2015) DSE: the set-extraction target this feeds.
// =============================================================================

/// <summary>What kind of structural feature a <see cref="Discontinuity"/> records.</summary>
public enum DiscontinuityKind
{
    /// <summary>A measured planar orientation (has a pole normal + centroid).</summary>
    Plane,
    /// <summary>A digitised surface trace (polyline); a plane is fit by PCA.</summary>
    Trace,
    /// <summary>A point measurement (orientation at a location).</summary>
    PointMeasurement
}

/// <summary>
/// One ingested structural discontinuity: a lower-hemisphere pole + centroid,
/// an optional digitised trace polyline, the cached dip / dip-direction, and an
/// optional set id (for pre-classified inputs). Build via the factory methods so
/// the pole, dip and dip-direction always stay mutually consistent.
/// </summary>
public sealed class Discontinuity
{
    /// <summary>Lower-hemisphere unit pole. May be <c>Vector3d.Zero</c> for a degenerate (collinear) trace.</summary>
    public Vector3d Normal { get; private set; }
    /// <summary>Representative location (plane through here, or trace centroid).</summary>
    public Point3d Centroid { get; private set; }
    /// <summary>Optional digitised polyline (DXF / GeoJSON lines); empty for pure orientation rows.</summary>
    public IReadOnlyList<Point3d> Trace { get; private set; }
    public DiscontinuityKind Kind { get; private set; }
    /// <summary>Pre-assigned set id, or -1 if unassigned.</summary>
    public int SetId { get; set; } = -1;
    /// <summary>Dip angle from horizontal, degrees in [0, 90]. Cached from <see cref="Normal"/>.</summary>
    public double DipDeg { get; private set; }
    /// <summary>Dip-direction azimuth, degrees in [0, 360). Cached from <see cref="Normal"/>.</summary>
    public double DipDirDeg { get; private set; }
    /// <summary>Free-form per-row attributes carried from the source file.</summary>
    public IReadOnlyDictionary<string, string> Meta { get; private set; }
    /// <summary>Originating file / layer, for provenance.</summary>
    public string Source { get; set; }

    /// <summary>Did this row carry a usable orientation (non-zero pole)?</summary>
    public bool HasOrientation => Normal.SquareLength > 1e-18;

    private Discontinuity() { }

    private static IReadOnlyDictionary<string, string> M(IReadOnlyDictionary<string, string> meta)
        => meta ?? EmptyMeta;
    private static readonly IReadOnlyDictionary<string, string> EmptyMeta =
        new Dictionary<string, string>(0);

    /// <summary>Build from an explicit pole normal (any orientation; folded to the lower hemisphere).</summary>
    public static Discontinuity FromPlane(
        Vector3d normal, Point3d centroid,
        DiscontinuityKind kind = DiscontinuityKind.Plane,
        IReadOnlyDictionary<string, string> meta = null, string source = null)
    {
        var n = OrientationMath.LowerHemisphere(normal);
        var (dip, dd) = OrientationMath.DipDipDir(n);
        return new Discontinuity
        {
            Normal = n,
            Centroid = centroid,
            Trace = Array.Empty<Point3d>(),
            Kind = kind,
            DipDeg = dip,
            DipDirDeg = dd,
            Meta = M(meta),
            Source = source
        };
    }

    /// <summary>Build from a measured dip / dip-direction (degrees) at a location.</summary>
    public static Discontinuity FromDipDipDir(
        double dipDeg, double dipDirDeg, Point3d centroid,
        DiscontinuityKind kind = DiscontinuityKind.PointMeasurement,
        IReadOnlyDictionary<string, string> meta = null, string source = null)
    {
        var n = OrientationMath.NormalFromDipDipDir(dipDeg, dipDirDeg);
        // Use the supplied dip/dipdir verbatim (they ARE the measurement); the
        // pole is derived. Snap dipdir into [0, 360).
        double dd = ((dipDirDeg % 360.0) + 360.0) % 360.0;
        return new Discontinuity
        {
            Normal = n,
            Centroid = centroid,
            Trace = Array.Empty<Point3d>(),
            Kind = kind,
            DipDeg = dipDeg,
            DipDirDeg = dd,
            Meta = M(meta),
            Source = source
        };
    }

    /// <summary>
    /// Build from a digitised trace polyline. The best-fit plane is found by PCA
    /// (total least squares); the pole is the smallest-eigenvalue eigenvector.
    /// A near-collinear trace (no well-defined plane) yields a zero pole and is
    /// flagged via <see cref="HasOrientation"/> = false.
    /// </summary>
    public static Discontinuity FromTrace(
        IReadOnlyList<Point3d> trace,
        IReadOnlyDictionary<string, string> meta = null, string source = null)
    {
        if (trace == null) throw new ArgumentNullException(nameof(trace));
        var pts = new List<Point3d>(trace);

        Vector3d normal = Vector3d.Zero;
        Point3d centroid;
        double dip = 0, dd = 0;

        if (pts.Count >= 3)
        {
            var idx = new int[pts.Count];
            for (int i = 0; i < pts.Count; i++) idx[i] = i;
            CloudMath.Pca(pts, idx, out Vector3d n, out double eta, out Point3d c, out double[] _);
            centroid = c;
            // eta = smallest/sum of eigenvalues; tiny -> a real plane, big -> a
            // fat/collinear blob. Only accept a pole when the fit is plane-like.
            if (eta < 0.20 && n.SquareLength > 1e-18)
            {
                normal = OrientationMath.LowerHemisphere(n);
                var o = OrientationMath.DipDipDir(normal);
                dip = o.dip; dd = o.dipDir;
            }
        }
        else
        {
            centroid = Centroidof(pts);
        }

        return new Discontinuity
        {
            Normal = normal,
            Centroid = centroid,
            Trace = pts,
            Kind = DiscontinuityKind.Trace,
            DipDeg = dip,
            DipDirDeg = dd,
            Meta = M(meta),
            Source = source
        };
    }

    private static Point3d Centroidof(IReadOnlyList<Point3d> pts)
    {
        if (pts == null || pts.Count == 0) return Point3d.Origin;
        double x = 0, y = 0, z = 0;
        foreach (var p in pts) { x += p.X; y += p.Y; z += p.Z; }
        return new Point3d(x / pts.Count, y / pts.Count, z / pts.Count);
    }
}

/// <summary>
/// The full result of reading one discontinuity file: the items, an optional CRS
/// (WKT, from GeoJSON / shapefile .prj), the source path, and any non-fatal
/// warnings accumulated while parsing (bad rows are skipped, never thrown).
/// </summary>
public sealed class DiscontinuityCollection
{
    public IReadOnlyList<Discontinuity> Items { get; }
    public string CrsWkt { get; }
    public string SourceFile { get; }
    public IReadOnlyList<string> Warnings { get; }

    public DiscontinuityCollection(
        IReadOnlyList<Discontinuity> items, string sourceFile,
        string crsWkt = null, IReadOnlyList<string> warnings = null)
    {
        Items = items ?? Array.Empty<Discontinuity>();
        SourceFile = sourceFile;
        CrsWkt = crsWkt;
        Warnings = warnings ?? Array.Empty<string>();
    }

    /// <summary>Count of items that carry a usable orientation (non-zero pole).</summary>
    public int OrientedCount
    {
        get { int n = 0; foreach (var d in Items) if (d.HasOrientation) n++; return n; }
    }
}
