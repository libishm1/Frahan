#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Masonry.Vault
{
    // =========================================================================
    // VaultQuadCells — turn a (thrust-aligned) QUAD mesh into the cell/frame/
    // columnness lists the mould + stone-fit stages consume, so the quad grid
    // itself is the masonry cell decomposition (no Voronoi needed).
    //
    // Knobs (exposed as GH sliders, Libish 2026-07-02):
    //   shrink       joint gap: each cell scaled about its centre (v004: 0.92).
    //   zLo/zHi      columnness band: faces below zLo are full column (c=1),
    //                above zHi full vault (c=0), linear in between.
    //   columnSplit  subdivision factor on column faces (c > 0.5): each quad is
    //                split into columnSplit x columnSplit sub-cells (2 = the
    //                validated "columns twice as fine" setting).
    // =========================================================================
    public sealed class QuadCellsResult
    {
        public readonly List<PolylineCurve> Cells = new List<PolylineCurve>();
        public readonly List<Plane> Frames = new List<Plane>();
        public readonly List<double> Columnness = new List<double>();
        // per-cell max INWARD mould offset (0 = unlimited). ~0.6 x local tube
        // radius on column shafts so opposite/adjacent stones never meet through
        // the tube axis; 0 on vault/wall cells (symmetric capping as before).
        public readonly List<double> InnerLimit = new List<double>();
        public int SplitFaces;
        public int Count { get { return Cells.Count; } }
    }

    public static class VaultQuadCells
    {
        public static QuadCellsResult Build(Mesh quadMesh, double shrink,
                                            double zLo, double zHi, int columnSplit,
                                            double tubeAngleDeg = 12.0)
        {
            var res = new QuadCellsResult();
            if (quadMesh == null || quadMesh.Faces.Count == 0) return res;
            var m = quadMesh.DuplicateMesh();
            m.Normals.ComputeNormals();
            m.FaceNormals.ComputeFaceNormals();
            if (shrink <= 0.0 || shrink > 1.0) shrink = 0.92;
            if (columnSplit < 1) columnSplit = 1;
            double band = Math.Max(1e-9, zHi - zLo);
            double cosTube = Math.Cos(Math.Max(0.5, tubeAngleDeg) * Math.PI / 180.0);

            // vertex -> faces adjacency for the tube test (curved column shafts vs
            // FLAT wall base: the wall bottom must keep full-size footer stones,
            // Libish 2026-07-02 — z alone cannot tell them apart).
            var vFaces = new List<int>[m.Vertices.Count];
            for (int i = 0; i < m.Vertices.Count; i++) vFaces[i] = new List<int>(6);
            for (int i = 0; i < m.Faces.Count; i++)
            {
                var ff = m.Faces[i];
                vFaces[ff.A].Add(i); vFaces[ff.B].Add(i); vFaces[ff.C].Add(i);
                if (ff.IsQuad) vFaces[ff.D].Add(i);
            }

            for (int i = 0; i < m.Faces.Count; i++)
            {
                var f = m.Faces[i];
                if (!f.IsQuad) continue;
                Point3d A = m.Vertices[f.A], B = m.Vertices[f.B], C = m.Vertices[f.C], D = m.Vertices[f.D];
                var ctr = (A + B + C + D) / 4.0;
                double cc = Math.Max(0.0, Math.Min(1.0, (zHi - ctr.Z) / band));
                Vector3d n = m.FaceNormals[i]; n.Unitize();
                double minDot = 1.0;
                if (cc > 0.0)
                {
                    // tube test: min 1-ring normal agreement below cos(tubeAngle)
                    // means the neighbourhood curves like a column shaft; a flat
                    // wall base has near-parallel neighbours and stays cc = 0.
                    int[] cv = { f.A, f.B, f.C, f.D };
                    foreach (int v in cv)
                        foreach (int nf2 in vFaces[v])
                        {
                            if (nf2 == i) continue;
                            Vector3d nn = m.FaceNormals[nf2];
                            double d = n * nn;
                            if (d < minDot) minDot = d;
                        }
                    if (minDot > cosTube) cc = 0.0; // flat neighbourhood -> footer, not column
                }
                // inward mould cap on column shafts: local tube radius from the
                // 1-ring normal turn over the circumferential edge (r ~ e/theta);
                // 0.6 r keeps opposite AND adjacent stones clear of the axis.
                double lim = 0.0;
                if (cc > 0.5)
                {
                    double theta = Math.Acos(Math.Max(-1.0, Math.Min(1.0, minDot)));
                    Vector3d abE = B - A, adE = D - A;
                    double hAB2 = Math.Abs(abE.Z) / Math.Max(1e-9, abE.Length);
                    double hAD2 = Math.Abs(adE.Z) / Math.Max(1e-9, adE.Length);
                    double eH = hAB2 <= hAD2 ? abE.Length : adE.Length;
                    double r = eH / Math.Max(0.05, theta);
                    lim = Math.Max(0.03, Math.Min(5.0, 0.6 * r));
                }

                int s = cc > 0.5 ? columnSplit : 1;
                if (s > 1) res.SplitFaces++;
                // split ONLY around the tube (the more-horizontal quad direction):
                // column COURSE HEIGHT stays the same as the vault's (Libish
                // 2026-07-02 "same height as the vault for the column subdivisions").
                int su = 1, sv = 1;
                if (s > 1)
                {
                    Vector3d ab = B - A, ad = D - A;
                    double hAB = Math.Abs(ab.Z) / Math.Max(1e-9, ab.Length);
                    double hAD = Math.Abs(ad.Z) / Math.Max(1e-9, ad.Length);
                    if (hAB <= hAD) su = s; else sv = s;   // more-horizontal direction = circumference
                }
                for (int u = 0; u < su; u++)
                    for (int v = 0; v < sv; v++)
                    {
                        Point3d p00 = Bilinear(A, B, C, D, (double)u / su, (double)v / sv);
                        Point3d p10 = Bilinear(A, B, C, D, (double)(u + 1) / su, (double)v / sv);
                        Point3d p11 = Bilinear(A, B, C, D, (double)(u + 1) / su, (double)(v + 1) / sv);
                        Point3d p01 = Bilinear(A, B, C, D, (double)u / su, (double)(v + 1) / sv);
                        var cx = (p00 + p10 + p11 + p01) / 4.0;
                        Point3d S(Point3d p) { return cx + (p - cx) * shrink; }
                        res.Cells.Add(new PolylineCurve(new Polyline(new[] { S(p00), S(p10), S(p11), S(p01), S(p00) })));
                        res.Frames.Add(new Plane(cx, n));
                        res.Columnness.Add(cc);
                        res.InnerLimit.Add(lim);
                    }
            }
            return res;
        }

        private static Point3d Bilinear(Point3d a, Point3d b, Point3d c, Point3d d, double u, double v)
        {
            // quad corner order A-B-C-D (loop): interpolate A->B along u on the
            // bottom edge, D->C along u on the top edge, then blend by v.
            Point3d bot = a + (b - a) * u;
            Point3d top = d + (c - d) * u;
            return bot + (top - bot) * v;
        }
    }
}
