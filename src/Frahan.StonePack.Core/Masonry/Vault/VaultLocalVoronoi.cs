#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Masonry.Vault
{
    // =========================================================================
    // VaultLocalVoronoi — per-seed LOCAL TANGENT-PLANE Voronoi cells.
    //
    // Ported from the validated Park Güell rubble-vault v004 recipe (Stage 2).
    // For each seed: build a tangent frame (u = world-X projected to tangent,
    // v = n x u), project neighbours into (u,v), and clip a square box by each
    // perpendicular bisector (Sutherland-Hodgman half-plane clip). A columnness
    // seal gradient shrinks vault cells to a hairline joint and lets column cells
    // overlap to seal the slender legs:  shrink = 0.92 + 0.22 * columnness.
    //
    // This is intentionally NOT the Geogram restricted-Voronoi (RVD): the local
    // tangent-plane clip reproduces the v004 cell shape (flat polygonal joints).
    //
    // Output lists are COMPACTED (degenerate cells dropped) and kept aligned:
    // Cells[i], Frames[i], Columnness[i] all describe the same cell.
    // =========================================================================
    public sealed class VoronoiResult
    {
        public readonly List<PolylineCurve> Cells = new List<PolylineCurve>();
        public readonly List<Plane> Frames = new List<Plane>();
        public readonly List<double> Columnness = new List<double>();
        public int Count { get { return Cells.Count; } }
    }

    public static class VaultLocalVoronoi
    {
        private const double NeighbourCell = 0.24;

        private static long Key(int ix, int iy, int iz)
        {
            const long off = 1L << 20;
            return (((long)ix + off) << 42) | (((long)iy + off) << 21) | ((long)iz + off);
        }

        // Clip a 2D convex polygon by the half-plane qx*x + qy*y <= c.
        private static List<Point2d> Clip(List<Point2d> poly, double qx, double qy, double c)
        {
            var o = new List<Point2d>(poly.Count + 2);
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Point2d a = poly[i];
                Point2d b = poly[(i + 1) % n];
                double da = a.X * qx + a.Y * qy - c;
                double db = b.X * qx + b.Y * qy - c;
                if (da <= 0) o.Add(a);
                if ((da < 0) != (db < 0))
                {
                    double t = da / (da - db);
                    o.Add(new Point2d(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t));
                }
            }
            return o;
        }

        public static VoronoiResult Build(
            IList<Point3d> points, IList<Vector3d> normals, IList<double> columnness,
            double rVault, double rCol)
        {
            var res = new VoronoiResult();
            int np = points == null ? 0 : points.Count;
            if (np == 0) return res;

            // Neighbour spatial grid.
            var grid = new Dictionary<long, List<int>>();
            for (int i = 0; i < np; i++)
            {
                Point3d p = points[i];
                long k = Key((int)Math.Floor(p.X / NeighbourCell),
                             (int)Math.Floor(p.Y / NeighbourCell),
                             (int)Math.Floor(p.Z / NeighbourCell));
                List<int> bucket;
                if (!grid.TryGetValue(k, out bucket)) { bucket = new List<int>(); grid[k] = bucket; }
                bucket.Add(i);
            }

            List<int> Neighbours(int i)
            {
                Point3d p = points[i];
                int kx = (int)Math.Floor(p.X / NeighbourCell);
                int ky = (int)Math.Floor(p.Y / NeighbourCell);
                int kz = (int)Math.Floor(p.Z / NeighbourCell);
                var o = new List<int>();
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            List<int> b;
                            if (grid.TryGetValue(Key(kx + dx, ky + dy, kz + dz), out b)) o.AddRange(b);
                        }
                return o;
            }

            for (int i = 0; i < np; i++)
            {
                var n = new Vector3d(normals[i]);
                if (n.Length < 1e-6) continue;
                n.Unitize();

                var u = new Vector3d(1, 0, 0) - n * (new Vector3d(1, 0, 0) * n);
                if (u.Length < 1e-3) u = new Vector3d(0, 1, 0) - n * (new Vector3d(0, 1, 0) * n);
                u.Unitize();
                var v = Vector3d.CrossProduct(n, u);
                v.Unitize();

                Point3d p = points[i];
                double cc = columnness[i];
                double rpv = rVault + (rCol - rVault) * cc;
                double bh = Math.Max(0.85 * rpv, 0.10);

                var poly = new List<Point2d>
                {
                    new Point2d(-bh, -bh), new Point2d(bh, -bh),
                    new Point2d(bh, bh), new Point2d(-bh, bh)
                };

                var nb = Neighbours(i);
                for (int q = 0; q < nb.Count; q++)
                {
                    int j = nb[q];
                    if (j == i) continue;
                    Vector3d d = points[j] - p;
                    double qx = d * u;
                    double qy = d * v;
                    if (qx * qx + qy * qy < 1e-6) continue;
                    poly = Clip(poly, qx, qy, 0.5 * (qx * qx + qy * qy));
                    if (poly.Count < 3) break;
                }

                var fr = new Plane(p, u, v);
                if (poly.Count < 3) continue;

                // Seal gradient: vault hairline shrink -> column overlap.
                double shk = 0.92 + 0.22 * cc;
                double ox = 0, oy = 0;
                for (int t = 0; t < poly.Count; t++) { ox += poly[t].X; oy += poly[t].Y; }
                ox /= poly.Count; oy /= poly.Count;

                var pl = new Polyline();
                for (int t = 0; t < poly.Count; t++)
                {
                    double sx = ox + (poly[t].X - ox) * shk;
                    double sy = oy + (poly[t].Y - oy) * shk;
                    pl.Add(p + u * sx + v * sy);
                }
                pl.Add(pl[0]);

                res.Cells.Add(new PolylineCurve(pl));
                res.Frames.Add(fr);
                res.Columnness.Add(cc);
            }

            return res;
        }
    }
}
