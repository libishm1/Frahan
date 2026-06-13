#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Core.Discontinuity;

// =============================================================================
// CloudMath -- shared managed point-cloud math for the discontinuity pipeline:
// a uniform spatial grid for k-nearest-neighbour queries, a symmetric 3x3
// Jacobi eigensolver, and PCA-normal estimation. Rhino-LIGHT (Point3d/Vector3d
// used as value containers only, no Rhino-runtime calls -> unit-testable
// headless). No native dependency; CGAL/geogram are not required for facets.
// =============================================================================

internal sealed class SpatialGrid
{
    private readonly IReadOnlyList<Point3d> _pts;
    private readonly double _cell;
    private readonly double _minx, _miny, _minz;
    private readonly int _nx, _ny, _nz;
    private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();

    public SpatialGrid(IReadOnlyList<Point3d> pts, double cell)
    {
        _pts = pts;
        _cell = Math.Max(1e-9, cell);
        double maxx = double.MinValue, maxy = double.MinValue, maxz = double.MinValue;
        _minx = _miny = _minz = double.MaxValue;
        foreach (var p in pts)
        {
            if (p.X < _minx) _minx = p.X; if (p.Y < _miny) _miny = p.Y; if (p.Z < _minz) _minz = p.Z;
            if (p.X > maxx) maxx = p.X; if (p.Y > maxy) maxy = p.Y; if (p.Z > maxz) maxz = p.Z;
        }
        _nx = Math.Max(1, (int)((maxx - _minx) / _cell) + 1);
        _ny = Math.Max(1, (int)((maxy - _miny) / _cell) + 1);
        _nz = Math.Max(1, (int)((maxz - _minz) / _cell) + 1);
        for (int i = 0; i < pts.Count; i++)
        {
            long key = KeyOf(pts[i]);
            if (!_cells.TryGetValue(key, out var list)) { list = new List<int>(); _cells[key] = list; }
            list.Add(i);
        }
    }

    private void Cell(Point3d p, out int cx, out int cy, out int cz)
    {
        cx = (int)((p.X - _minx) / _cell); cy = (int)((p.Y - _miny) / _cell); cz = (int)((p.Z - _minz) / _cell);
    }
    private static long Key(int cx, int cy, int cz) => ((long)(cx & 0x1FFFFF) << 42) | ((long)(cy & 0x1FFFFF) << 21) | (long)(cz & 0x1FFFFF);
    private long KeyOf(Point3d p) { Cell(p, out int cx, out int cy, out int cz); return Key(cx, cy, cz); }

    // k nearest neighbours of point index qi (excludes qi). Expands by shells
    // (Chebyshev rings), keeping a sorted k-buffer; stops one shell after k are
    // found. With cell ~ point spacing this is a few microseconds per query.
    public int[] KNearest(int qi, int k)
    {
        var q = _pts[qi];
        Cell(q, out int cx, out int cy, out int cz);
        var bd = new double[k]; var bi = new int[k]; int cnt = 0;
        for (int t = 0; t < k; t++) bd[t] = double.MaxValue;
        int ring = 0, after = 0;
        int maxRing = Math.Max(_nx, Math.Max(_ny, _nz)) + 1;
        while (true)
        {
            ring++;
            for (int dx = -ring; dx <= ring; dx++)
                for (int dy = -ring; dy <= ring; dy++)
                    for (int dz = -ring; dz <= ring; dz++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz))) != ring) continue;
                        if (!_cells.TryGetValue(Key(cx + dx, cy + dy, cz + dz), out var list)) continue;
                        foreach (var idx in list)
                        {
                            if (idx == qi) continue;
                            double d = q.DistanceToSquared(_pts[idx]);
                            if (d < bd[k - 1])
                            {
                                int pos = k - 1;
                                while (pos > 0 && bd[pos - 1] > d) { bd[pos] = bd[pos - 1]; bi[pos] = bi[pos - 1]; pos--; }
                                bd[pos] = d; bi[pos] = idx; if (cnt < k) cnt++;
                            }
                        }
                    }
            if (cnt >= k) after++;
            if (after >= 1 || ring > maxRing) break;
        }
        int take = Math.Min(k, cnt);
        var outk = new int[take];
        Array.Copy(bi, outk, take);
        return outk;
    }

    // points within radius r of point qi (excludes qi).
    public List<int> WithinRadius(int qi, double r)
    {
        var q = _pts[qi]; double r2 = r * r;
        Cell(q, out int cx, out int cy, out int cz);
        int ring = Math.Max(1, (int)(r / _cell) + 1);
        var res = new List<int>();
        for (int dx = -ring; dx <= ring; dx++)
            for (int dy = -ring; dy <= ring; dy++)
                for (int dz = -ring; dz <= ring; dz++)
                    if (_cells.TryGetValue(Key(cx + dx, cy + dy, cz + dz), out var list))
                        foreach (var idx in list) if (idx != qi && q.DistanceToSquared(_pts[idx]) <= r2) res.Add(idx);
        return res;
    }
}

internal static class CloudMath
{
    // Median nearest-neighbour distance over a sample (point spacing estimate).
    public static double EstimateSpacing(IReadOnlyList<Point3d> pts, SpatialGrid grid, int sample = 800)
    {
        int n = pts.Count; if (n < 2) return 1.0;
        int step = Math.Max(1, n / sample);
        var d = new List<double>();
        for (int i = 0; i < n; i += step)
        {
            var nn = grid.KNearest(i, 1);
            if (nn.Length > 0) d.Add(pts[i].DistanceTo(pts[nn[0]]));
        }
        if (d.Count == 0) return 1.0;
        d.Sort();
        return Math.Max(1e-9, d[d.Count / 2]);
    }

    // PCA of a neighbourhood: returns eigenvalues ascending (l0<=l1<=l2), unit eigenvectors,
    // the smallest-eigenvalue vector (the normal), planarity eta = l0/(l0+l1+l2), and centroid.
    public static void Pca(IReadOnlyList<Point3d> pts, IReadOnlyList<int> idx,
        out Vector3d normal, out double eta, out Point3d centroid, out double[] eigVals)
    {
        double cx = 0, cy = 0, cz = 0; int m = idx.Count;
        for (int i = 0; i < m; i++) { var p = pts[idx[i]]; cx += p.X; cy += p.Y; cz += p.Z; }
        cx /= m; cy /= m; cz /= m; centroid = new Point3d(cx, cy, cz);
        double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        for (int i = 0; i < m; i++)
        {
            var p = pts[idx[i]]; double dx = p.X - cx, dy = p.Y - cy, dz = p.Z - cz;
            xx += dx * dx; xy += dx * dy; xz += dx * dz; yy += dy * dy; yz += dy * dz; zz += dz * dz;
        }
        SymEig3(xx, xy, xz, yy, yz, zz, out double[] ev, out Vector3d[] vec);
        eigVals = ev;
        normal = vec[0]; // smallest eigenvalue -> normal
        double sum = ev[0] + ev[1] + ev[2];
        eta = sum > 1e-20 ? ev[0] / sum : 0.0;
    }

    // Symmetric 3x3 eigen-decomposition via cyclic Jacobi. Returns eigenvalues ascending
    // and matching unit eigenvectors (column k = vec[k]).
    public static void SymEig3(double a00, double a01, double a02, double a11, double a12, double a22,
        out double[] eig, out Vector3d[] vec)
    {
        double[,] A = { { a00, a01, a02 }, { a01, a11, a12 }, { a02, a12, a22 } };
        double[,] V = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        for (int sweep = 0; sweep < 24; sweep++)
        {
            double off = Math.Abs(A[0, 1]) + Math.Abs(A[0, 2]) + Math.Abs(A[1, 2]);
            if (off < 1e-18) break;
            for (int p = 0; p < 2; p++)
                for (int q = p + 1; q < 3; q++)
                {
                    if (Math.Abs(A[p, q]) < 1e-20) continue;
                    double theta = (A[q, q] - A[p, p]) / (2 * A[p, q]);
                    double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    if (theta == 0) t = 1;
                    double c = 1 / Math.Sqrt(t * t + 1), s = t * c;
                    for (int i = 0; i < 3; i++)
                    {
                        double aip = A[i, p], aiq = A[i, q];
                        A[i, p] = c * aip - s * aiq; A[i, q] = s * aip + c * aiq;
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        double api = A[p, i], aqi = A[q, i];
                        A[p, i] = c * api - s * aqi; A[q, i] = s * api + c * aqi;
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        double vip = V[i, p], viq = V[i, q];
                        V[i, p] = c * vip - s * viq; V[i, q] = s * vip + c * viq;
                    }
                }
        }
        var vals = new double[] { A[0, 0], A[1, 1], A[2, 2] };
        var order = new int[] { 0, 1, 2 };
        Array.Sort(order, (x, y) => vals[x].CompareTo(vals[y]));
        eig = new double[3]; vec = new Vector3d[3];
        for (int k = 0; k < 3; k++)
        {
            int o = order[k]; eig[k] = vals[o];
            var v = new Vector3d(V[0, o], V[1, o], V[2, o]); v.Unitize(); vec[k] = v;
        }
    }
}
