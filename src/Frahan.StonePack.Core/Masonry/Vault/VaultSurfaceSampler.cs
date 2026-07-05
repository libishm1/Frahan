#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Masonry.Vault
{
    // =========================================================================
    // VaultSurfaceSampler — variable-density Poisson-disk (blue-noise) sampling
    // of a mesh surface, with a per-sample "columnness" field that shrinks the
    // disk radius on the colonnade legs so stones pack finer there.
    //
    // Ported from the validated Park Güell rubble-vault v004 recipe (Stage 1).
    // Method: area-weighted face pick -> random barycentric point -> spatial-grid
    // rejection (reject if closer than 0.5*(r_p + r_q) to an accepted sample).
    //
    // columnness c(p) = (1 - smoothstep(z, zLo, zHi)) * smoothstep(y, yLo, yHi)
    //   -> 1 on the low colonnade-side legs, 0 on the broad upper vault.
    // target radius r(p) = rVault + (rCol - rVault) * c(p).
    // =========================================================================
    public sealed class SurfaceSampleResult
    {
        public readonly List<Point3d> Points = new List<Point3d>();
        public readonly List<Vector3d> Normals = new List<Vector3d>();
        public readonly List<double> Columnness = new List<double>();
        public int Count { get { return Points.Count; } }
    }

    public static class VaultSurfaceSampler
    {
        public static double SmoothStep(double x, double a, double b)
        {
            if (Math.Abs(b - a) < 1e-12) return x >= b ? 1.0 : 0.0;
            double t = (x - a) / (b - a);
            if (t < 0.0) t = 0.0; else if (t > 1.0) t = 1.0;
            return t * t * (3.0 - 2.0 * t);
        }

        // Pack a 3D grid index into a collision-free long (each axis offset into 21 bits).
        private static long Key(int ix, int iy, int iz)
        {
            const long off = 1L << 20;
            return (((long)ix + off) << 42) | (((long)iy + off) << 21) | ((long)iz + off);
        }

        public static SurfaceSampleResult Sample(
            Mesh mesh,
            double rVault, double rCol,
            double zLo, double zHi, double yLo, double yHi,
            int seed,
            int maxAttempts = 120000)
        {
            var res = new SurfaceSampleResult();
            if (mesh == null || mesh.Faces.Count == 0) return res;

            var m = mesh.DuplicateMesh();
            m.Normals.ComputeNormals();
            m.FaceNormals.ComputeFaceNormals();

            var faces = m.Faces;
            var verts = m.Vertices;

            // Per-face area + cumulative distribution for area-weighted sampling.
            int nf = faces.Count;
            var cum = new double[nf];
            double total = 0.0;
            for (int fi = 0; fi < nf; fi++)
            {
                MeshFace ff = faces[fi];
                Point3d a = verts[ff.A];
                Point3d b = verts[ff.B];
                Point3d c = verts[ff.C];
                double ar = 0.5 * Vector3d.CrossProduct(b - a, c - a).Length;
                if (ff.IsQuad)
                {
                    Point3d d = verts[ff.D];
                    ar += 0.5 * Vector3d.CrossProduct(c - a, d - a).Length;
                }
                total += ar;
                cum[fi] = total;
            }
            if (total <= 0.0) return res;

            var rng = new Random(seed);

            double Columnness(Point3d p)
            {
                return (1.0 - SmoothStep(p.Z, zLo, zHi)) * SmoothStep(p.Y, yLo, yHi);
            }
            double Radius(Point3d p)
            {
                return rVault + (rCol - rVault) * Columnness(p);
            }

            int PickFace()
            {
                double t = rng.NextDouble() * total;
                int lo = 0, hi = nf - 1;
                while (lo < hi)
                {
                    int mid = (lo + hi) >> 1;
                    if (cum[mid] < t) lo = mid + 1; else hi = mid;
                }
                return lo;
            }

            double gs = Math.Max(rCol, 1e-4);   // grid cell = smallest disk radius
            var grid = new Dictionary<long, List<int>>();
            var accPos = new List<Point3d>();
            var accR = new List<double>();

            for (int it = 0; it < maxAttempts; it++)
            {
                int fi = PickFace();
                MeshFace ff = faces[fi];
                Point3d a = verts[ff.A];
                Point3d b = verts[ff.B];
                Point3d c = verts[ff.C];
                if (ff.IsQuad && rng.NextDouble() < 0.5) { b = verts[ff.C]; c = verts[ff.D]; }

                double u1 = Math.Sqrt(rng.NextDouble());
                double u2 = rng.NextDouble();
                var p = new Point3d(
                    a.X * (1 - u1) + b.X * (u1 * (1 - u2)) + c.X * (u1 * u2),
                    a.Y * (1 - u1) + b.Y * (u1 * (1 - u2)) + c.Y * (u1 * u2),
                    a.Z * (1 - u1) + b.Z * (u1 * (1 - u2)) + c.Z * (u1 * u2));
                var n = new Vector3d(m.FaceNormals[fi]);

                double rp = Radius(p);
                int kx = (int)Math.Floor(p.X / gs);
                int ky = (int)Math.Floor(p.Y / gs);
                int kz = (int)Math.Floor(p.Z / gs);

                bool ok = true;
                for (int dx = -2; dx <= 2 && ok; dx++)
                    for (int dy = -2; dy <= 2 && ok; dy++)
                        for (int dz = -2; dz <= 2 && ok; dz++)
                        {
                            List<int> bucket;
                            if (!grid.TryGetValue(Key(kx + dx, ky + dy, kz + dz), out bucket)) continue;
                            for (int q = 0; q < bucket.Count; q++)
                            {
                                int j = bucket[q];
                                if (p.DistanceTo(accPos[j]) < 0.5 * (rp + accR[j])) { ok = false; break; }
                            }
                        }
                if (!ok) continue;

                int idx = accPos.Count;
                accPos.Add(p);
                accR.Add(rp);
                long key = Key(kx, ky, kz);
                List<int> cell;
                if (!grid.TryGetValue(key, out cell)) { cell = new List<int>(); grid[key] = cell; }
                cell.Add(idx);

                res.Points.Add(p);
                res.Normals.Add(n);
                res.Columnness.Add(Columnness(p));
            }

            return res;
        }
    }
}
