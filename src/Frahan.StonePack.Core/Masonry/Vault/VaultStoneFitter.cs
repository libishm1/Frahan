#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Frahan.Masonry.Library;
using Frahan.Masonry.Packing;
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

        // .obj parsing shared with the public stone-library loader (dedup); the LoadPool
        // recipe below is intentionally NOT routed through StoneLibraryLoader.Load -- its
        // candidate-budget semantics are the validated Park Guell v004 recipe.
        private static Mesh LoadObj(string path) => StoneLibraryLoader.LoadObj(path);

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
            int maxPool = 140, bool useMatcher = false)
        {
            var res = new StoneFitResult();
            var pool = LoadPool(ethDir, seed, maxPool, poolArMax);
            res.PoolSize = pool.Count;
            if (pool.Count == 0) return res;

            int nm = moulds == null ? 0 : moulds.Count;

            // Optional upgrade: choose each mould's stone by the Hungarian BEST-FIT matcher
            // (StoneCellAssignment -> the stone needing the least carving) instead of the
            // arbitrary modular index. Placement/scale/trim below are unchanged; only WHICH
            // stone goes in each cell changes. Hungarian is O(N^3): best for up to a few
            // hundred cells; for a denser vault keep the modular default. Falls back to
            // modular on any failure, so the validated Park Guell recipe is the default.
            int[] cellToStone = null;
            if (useMatcher && nm > 0)
            {
                try
                {
                    var poolMeshes = new List<Mesh>(pool.Count);
                    foreach (var st in pool) poolMeshes.Add(st.Mesh);
                    var valMoulds = new List<Mesh>(); var valIdx = new List<int>();
                    for (int i = 0; i < nm; i++) if (moulds[i] != null) { valMoulds.Add(moulds[i]); valIdx.Add(i); }
                    ToBuffers(poolMeshes, out var sC, out var sT);
                    ToBuffers(valMoulds, out var cC, out var cT);
                    var asg = StoneCellAssignment.Assign(sC, sT, cC, cT);
                    cellToStone = new int[nm];
                    for (int i = 0; i < nm; i++) cellToStone[i] = -1;
                    foreach (var p in asg.Placements)
                        if (p.CellIndex >= 0 && p.CellIndex < valIdx.Count)
                            cellToStone[valIdx[p.CellIndex]] = p.StoneIndex;
                }
                catch { cellToStone = null; }   // any failure -> validated modular path
            }

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

                int si = (cellToStone != null && cellToStone[i] >= 0 && cellToStone[i] < pool.Count)
                         ? cellToStone[i] : (i * 13 + 7) % pool.Count;
                Stock s = pool[si];
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
                    // Accept the trim ONLY if it is a usable stone (closed + sane volume).
                    // Degenerate boolean outputs (open shells, slivers) fall back to the
                    // mould, which is always a valid closed voussoir -> no "empty" ETH
                    // stones can reach the output (Libish 2026-07-02, three-prong).
                    if (bi != null && bi.Length > 0 && IsUsableStone(bi[0], mo)) outMesh = bi[0];
                }
                catch { outMesh = mo; }

                outMesh.Weld(0.01);
                outMesh.Normals.ComputeNormals();
                outMesh.UnifyNormals();
                if (outMesh.Faces.Count < 4) continue; // never emit a degenerate stone
                res.Rubble.Add(outMesh);
            }
            return res;
        }

        // Validity gate for a trimmed stone: non-trivial, CLOSED after a light weld,
        // and at least 5% of its mould's volume (rejects boolean slivers/shards).
        private static bool IsUsableStone(Mesh s, Mesh mould)
        {
            if (s == null || s.Faces.Count <= 3) return false;
            var w = s.DuplicateMesh();
            w.Weld(0.01);
            if (!w.IsClosed) return false;
            try
            {
                var vs = VolumeMassProperties.Compute(w);
                if (vs == null || Math.Abs(vs.Volume) < 1e-9) return false;
                var vm = VolumeMassProperties.Compute(mould);
                double mv = vm != null ? Math.Abs(vm.Volume) : 0.0;
                if (mv > 1e-9 && Math.Abs(vs.Volume) < 0.05 * mv) return false;
            }
            catch { return false; }
            return true;
        }

        // Mesh -> flat (coords, tris) buffers for StoneCellAssignment (parallel lists).
        private static void ToBuffers(IList<Mesh> meshes,
            out List<IReadOnlyList<double>> coords, out List<IReadOnlyList<int>> tris)
        {
            coords = new List<IReadOnlyList<double>>(meshes.Count);
            tris = new List<IReadOnlyList<int>>(meshes.Count);
            foreach (var mesh in meshes)
            {
                var t = mesh.DuplicateMesh();
                t.Faces.ConvertQuadsToTriangles();
                var cs = new List<double>(t.Vertices.Count * 3);
                for (int v = 0; v < t.Vertices.Count; v++)
                { var p = t.Vertices[v]; cs.Add(p.X); cs.Add(p.Y); cs.Add(p.Z); }
                var ts = new List<int>(t.Faces.Count * 3);
                for (int f = 0; f < t.Faces.Count; f++)
                { var fa = t.Faces[f]; ts.Add(fa.A); ts.Add(fa.B); ts.Add(fa.C); }
                coords.Add(cs); tris.Add(ts);
            }
        }
    }
}
