#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Core.Discontinuity;

// =============================================================================
// PointCloudFacetExtractor -- FACETS-style planar-facet extraction from an
// unorganized point cloud (after Dewez, Girardeau-Montaut et al. 2016, qFacets).
// Per-point PCA normals over k nearest neighbours, then region-grow contiguous
// points into planar facets gated by (a) axial normal agreement and (b) the
// 99%-band distance to the growing facet's least-squares plane. Each facet
// reports its plane normal and geological dip / dip-direction.
//
// Managed + Rhino-LIGHT (no CGAL/geogram needed; CGAL has no point-cloud RANSAC,
// only normals + mesh ops). Deterministic. Unit-testable headless.
// =============================================================================

public sealed class Facet
{
    public Vector3d Normal;       // unit, lower hemisphere
    public Point3d Centroid;
    public double Dip;            // degrees, 0..90
    public double DipDir;         // degrees, 0..360
    public int[] PointIndices;
    public double Rms;            // RMS point-to-plane distance
    public double AreaProxy;      // PointCount * spacing^2 (rough)
    public int PointCount => PointIndices == null ? 0 : PointIndices.Length;
}

public sealed class FacetOptions
{
    public int K = 24;                    // kNN for normals + connectivity
    public double MaxNormalAngleDeg = 12; // region-grow axial normal agreement
    public double PlaneDistFactor = 2.5;  // band = factor * point spacing
    public int MinFacetPoints = 40;
    public double SeedEtaMax = 0.05;      // seed only from planar points (lambda0/sum)
}

public sealed class FacetExtractResult
{
    public List<Facet> Facets = new List<Facet>();
    public Vector3d[] PointNormals;
    public double Spacing;
}

public static class FacetExtractor
{
    public static FacetExtractResult Extract(IReadOnlyList<Point3d> pts, FacetOptions opt = null)
    {
        opt = opt ?? new FacetOptions();
        var res = new FacetExtractResult();
        int n = pts.Count;
        if (n < opt.K + 1) return res;

        // coarse spacing for grid cell, then refine
        var bb = BoundingBox.Empty; foreach (var p in pts) bb.Union(p);
        double coarse = Math.Max(1e-6, bb.Diagonal.Length / Math.Pow(n, 1.0 / 3.0));
        var grid = new SpatialGrid(pts, coarse);
        double spacing = CloudMath.EstimateSpacing(pts, grid);
        res.Spacing = spacing;
        grid = new SpatialGrid(pts, Math.Max(1e-6, spacing)); // tight cells -> fast shell-expansion kNN

        // per-point kNN, normals, planarity
        var knn = new int[n][];
        var normals = new Vector3d[n];
        var eta = new double[n];
        for (int i = 0; i < n; i++)
        {
            var nb = grid.KNearest(i, opt.K);
            knn[i] = nb;
            var idx = new List<int>(nb.Length + 1) { i };
            idx.AddRange(nb);
            CloudMath.Pca(pts, idx, out var nrm, out double e, out _, out _);
            normals[i] = OrientationMath.LowerHemisphere(nrm);
            eta[i] = e;
        }
        res.PointNormals = normals;

        double cosMax = Math.Cos(opt.MaxNormalAngleDeg * Math.PI / 180.0);
        double band = opt.PlaneDistFactor * spacing;

        // seed from most-planar points first
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        Array.Sort(order, (a, b) => eta[a].CompareTo(eta[b]));

        var visited = new bool[n];
        var queue = new Queue<int>();
        for (int oi = 0; oi < n; oi++)
        {
            int seed = order[oi];
            if (visited[seed] || eta[seed] > opt.SeedEtaMax) continue;

            var members = new List<int>();
            Vector3d fn = normals[seed];
            Point3d fc = pts[seed];
            queue.Clear(); queue.Enqueue(seed); visited[seed] = true;
            int sinceFit = 0;
            while (queue.Count > 0)
            {
                int j = queue.Dequeue();
                members.Add(j);
                if (++sinceFit >= 64 && members.Count >= 16)
                {
                    CloudMath.Pca(pts, members, out var rn, out _, out var rc, out _);
                    fn = OrientationMath.LowerHemisphere(rn); fc = rc; sinceFit = 0;
                }
                foreach (var nb in knn[j])
                {
                    if (visited[nb]) continue;
                    double d = Math.Abs(normals[nb].X * fn.X + normals[nb].Y * fn.Y + normals[nb].Z * fn.Z);
                    if (d < cosMax) continue; // axial normal angle gate
                    var v = pts[nb] - fc;
                    if (Math.Abs(v.X * fn.X + v.Y * fn.Y + v.Z * fn.Z) > band) continue; // plane band
                    visited[nb] = true; queue.Enqueue(nb);
                }
            }
            if (members.Count < opt.MinFacetPoints) continue;

            CloudMath.Pca(pts, members, out var fnrm, out _, out var fcent, out _);
            var unit = OrientationMath.LowerHemisphere(fnrm);
            double sse = 0;
            foreach (var m in members)
            {
                var v = pts[m] - fcent;
                double dd = v.X * unit.X + v.Y * unit.Y + v.Z * unit.Z; sse += dd * dd;
            }
            var (dip, dipdir) = OrientationMath.DipDipDir(unit);
            res.Facets.Add(new Facet
            {
                Normal = unit,
                Centroid = fcent,
                Dip = dip,
                DipDir = dipdir,
                PointIndices = members.ToArray(),
                Rms = Math.Sqrt(sse / members.Count),
                AreaProxy = members.Count * spacing * spacing
            });
        }
        return res;
    }
}
