#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Rhino.Geometry;

namespace Frahan.Masonry.Vault
{
    // =========================================================================
    // VaultStoneFitter — orient + scale a chunky ETH1100 stone into each voussoir
    // mould and boolean-intersect to leave a raw rubble face with flat joints.
    //
    // Ported from the validated Park Güell rubble-vault v004 recipe (Stage 4).
    // Orientation: thin axis -> cell normal (radial), long axis -> tangent u.
    // overfill = overfill0 + 0.34 * columnness, inflate = 0.035 + 0.045 * columnness
    // (column stones grow + overlap to seal). Stone = mould ∩ oriented stock.
    // =========================================================================
    public sealed class StoneFitResult
    {
        public readonly List<Mesh> Rubble = new List<Mesh>();
        public int Count { get { return Rubble.Count; } }
        public int PoolSize;
    }

    public static class VaultStoneFitter
    {
        private sealed class Stock
        {
            public Mesh Mesh;
            public Vector3d ThinAxis; public double ThinDim;
            public Vector3d MidAxis;  public double MidDim;
            public Vector3d LongAxis;  public double LongDim;
        }

        private static Mesh LoadObj(string path)
        {
            var verts = new List<Point3d>();
            var m = new Mesh();
            foreach (string raw in File.ReadLines(path))
            {
                if (raw.Length < 2) continue;
                if (raw[0] == 'v' && raw[1] == ' ')
                {
                    string[] pp = raw.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    verts.Add(new Point3d(
                        double.Parse(pp[1], CultureInfo.InvariantCulture),
                        double.Parse(pp[2], CultureInfo.InvariantCulture),
                        double.Parse(pp[3], CultureInfo.InvariantCulture)));
                }
                else if (raw[0] == 'f' && raw[1] == ' ')
                {
                    string[] pp = raw.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    var idx = new List<int>(pp.Length - 1);
                    for (int k = 1; k < pp.Length; k++)
                    {
                        string tok = pp[k];
                        int slash = tok.IndexOf('/');
                        if (slash >= 0) tok = tok.Substring(0, slash);
                        idx.Add(int.Parse(tok, CultureInfo.InvariantCulture) - 1);
                    }
                    if (idx.Count == 3) m.Faces.AddFace(idx[0], idx[1], idx[2]);
                    else if (idx.Count == 4) m.Faces.AddFace(idx[0], idx[1], idx[2], idx[3]);
                    else for (int t = 1; t < idx.Count - 1; t++) m.Faces.AddFace(idx[0], idx[t], idx[t + 1]);
                }
            }
            for (int i = 0; i < verts.Count; i++) m.Vertices.Add(verts[i]);
            m.Normals.ComputeNormals();
            return m;
        }

        private static List<Stock> LoadPool(string ethDir, int seed, int maxPool, double poolArMax)
        {
            var pool = new List<Stock>();
            if (string.IsNullOrEmpty(ethDir) || !Directory.Exists(ethDir)) return pool;

            var files = new List<string>(Directory.GetFiles(ethDir, "*.obj"));
            files.Sort(StringComparer.Ordinal);
            // deterministic shuffle then take first maxPool
            var rng = new Random(seed);
            for (int i = files.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                string tmp = files[i]; files[i] = files[j]; files[j] = tmp;
            }
            int take = Math.Min(maxPool, files.Count);

            var axisOf = new Vector3d[] { new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1) };
            for (int f = 0; f < take; f++)
            {
                Mesh mm;
                try { mm = LoadObj(files[f]); }
                catch { continue; }
                if (mm.Faces.Count == 0) continue;

                BoundingBox bb = mm.GetBoundingBox(true);
                Point3d cen = bb.Center;
                mm.Translate(-cen.X, -cen.Y, -cen.Z);
                bb = mm.GetBoundingBox(true);

                double dx = bb.Max.X - bb.Min.X;
                double dy = bb.Max.Y - bb.Min.Y;
                double dz = bb.Max.Z - bb.Min.Z;
                // sort the three (dim, axisIndex) ascending by dim
                var dims = new double[] { dx, dy, dz };
                var order = new int[] { 0, 1, 2 };
                for (int a = 0; a < 3; a++)
                    for (int b = a + 1; b < 3; b++)
                        if (dims[order[b]] < dims[order[a]]) { int t = order[a]; order[a] = order[b]; order[b] = t; }

                double td = dims[order[0]], md = dims[order[1]], kd = dims[order[2]];
                if (td < 1e-4) continue;
                if (kd / td > poolArMax) continue;   // chunky filter

                pool.Add(new Stock
                {
                    Mesh = mm,
                    ThinAxis = axisOf[order[0]], ThinDim = td,
                    MidAxis = axisOf[order[1]], MidDim = md,
                    LongAxis = axisOf[order[2]], LongDim = kd
                });
            }
            return pool;
        }

        private static void Inflate(Mesh m, double a)
        {
            m.Normals.ComputeNormals();
            for (int i = 0; i < m.Vertices.Count; i++)
            {
                Point3f vv = m.Vertices[i];
                Vector3f nn = m.Normals[i];
                m.Vertices.SetVertex(i, vv.X + nn.X * (float)a, vv.Y + nn.Y * (float)a, vv.Z + nn.Z * (float)a);
            }
        }

        public static StoneFitResult FitAndTrim(
            IList<Mesh> moulds, IList<PolylineCurve> cells, IList<Plane> frames, IList<double> columnness,
            double dVault, double dCol, string ethDir, int seed, double overfill, double poolArMax,
            int maxPool = 140)
        {
            var res = new StoneFitResult();
            var pool = LoadPool(ethDir, seed, maxPool, poolArMax);
            res.PoolSize = pool.Count;
            if (pool.Count == 0) return res;

            int nm = moulds == null ? 0 : moulds.Count;
            for (int i = 0; i < nm; i++)
            {
                Mesh mo = moulds[i];
                if (mo == null) continue;
                Plane fr = frames[i];
                PolylineCurve cv = cells[i];
                double cc = columnness[i];

                double depth = dVault + (dCol - dVault) * cc;
                Vector3d n = fr.ZAxis, u = fr.XAxis, v2 = fr.YAxis;

                // cell extent in the frame
                Polyline poly;
                if (cv == null || !cv.TryGetPolyline(out poly)) continue;
                double minx = double.MaxValue, maxx = double.MinValue, miny = double.MaxValue, maxy = double.MinValue;
                for (int k = 0; k < poly.Count - 1; k++)
                {
                    Vector3d d = poly[k] - fr.Origin;
                    double xx = d * u, yy = d * v2;
                    if (xx < minx) minx = xx; if (xx > maxx) maxx = xx;
                    if (yy < miny) miny = yy; if (yy > maxy) maxy = yy;
                }
                double w = maxx - minx, h = maxy - miny;

                Stock s = pool[(i * 13 + 7) % pool.Count];
                double of = overfill + 0.34 * cc;
                double sc = Math.Min(Math.Max(depth / Math.Max(s.ThinDim, 1e-4),
                                     Math.Max(of * h / Math.Max(s.MidDim, 1e-4),
                                              of * w / Math.Max(s.LongDim, 1e-4))), 5.0);

                Mesh eth = s.Mesh.DuplicateMesh();
                Vector3d tvv = new Vector3d(s.ThinAxis);
                Vector3d kvv = new Vector3d(s.LongAxis);

                if ((tvv - n).Length > 1e-6 && (tvv + n).Length > 1e-6)
                {
                    Transform r1 = Transform.Rotation(tvv, n, Point3d.Origin);
                    eth.Transform(r1);
                    kvv.Transform(r1);
                }
                if ((kvv - u).Length > 1e-6 && (kvv + u).Length > 1e-6)
                    eth.Transform(Transform.Rotation(kvv, u, Point3d.Origin));

                eth.Transform(Transform.Scale(Point3d.Origin, sc));
                Inflate(eth, 0.035 + 0.045 * cc);
                eth.Translate(fr.Origin.X, fr.Origin.Y, fr.Origin.Z);

                Mesh outMesh = mo;
                try
                {
                    Mesh[] bi = Mesh.CreateBooleanIntersection(new Mesh[] { eth }, new Mesh[] { mo });
                    if (bi != null && bi.Length > 0 && bi[0] != null && bi[0].Faces.Count > 3) outMesh = bi[0];
                }
                catch { outMesh = mo; }

                outMesh.Weld(0.01);
                outMesh.Normals.ComputeNormals();
                outMesh.UnifyNormals();
                res.Rubble.Add(outMesh);
            }
            return res;
        }
    }
}
