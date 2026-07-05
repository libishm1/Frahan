#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace Frahan.Masonry.Vault
{
    // =========================================================================
    // ShellCurveTrimmer — trim a vault shell mesh along user-drawn plan curves
    // (Libish's cut_curves workflow, 2026-07-02, productized). Each curve is
    // projected to plan and read as a (y -> x) boundary table; mesh faces whose
    // vertices fall OUTSIDE any boundary (beyond the curve, toward its end of
    // the shell) are removed. Curves near the low-x end cut the low side;
    // curves near the high-x end cut the high side — detected automatically
    // from each curve's position relative to the mesh centre. The same logic
    // validated live on the Güell portico (guell_curvecut_shell_v001).
    // =========================================================================
    public sealed class CurveTrimResult
    {
        public Mesh Kept;
        public Mesh Removed;
        public int RemovedFaces;
        public string Note = "";
    }

    public static class ShellCurveTrimmer
    {
        public static CurveTrimResult Trim(Mesh shell, IList<Curve> cutCurves, int samplesPerCurve = 200)
        {
            var res = new CurveTrimResult();
            if (shell == null || shell.Faces.Count == 0) { res.Note = "empty shell"; return res; }
            if (cutCurves == null || cutCurves.Count == 0) { res.Note = "no cut curves"; return res; }

            var m = shell.DuplicateMesh();
            m.Faces.ConvertQuadsToTriangles();
            m.Compact();
            var bb = m.GetBoundingBox(true);
            double xMid = 0.5 * (bb.Min.X + bb.Max.X);

            // per-curve plan tables: (y -> x), plus which side of the shell it cuts
            var tables = new List<SortedList<double, double>>();
            var lowSide = new List<bool>();
            foreach (var c in cutCurves)
            {
                if (c == null) continue;
                var t = new SortedList<double, double>();
                var prm = c.DivideByCount(Math.Max(8, samplesPerCurve), true);
                if (prm == null) continue;
                foreach (var u in prm)
                {
                    Point3d p = c.PointAt(u);
                    if (!t.ContainsKey(p.Y)) t.Add(p.Y, p.X);
                }
                if (t.Count < 2) continue;
                tables.Add(t);
                lowSide.Add(c.GetBoundingBox(true).Center.X < xMid);
            }
            if (tables.Count == 0) { res.Note = "no usable curves (need plan-projectable curves)"; return res; }

            double Lerp(SortedList<double, double> t, double y)
            {
                var keys = t.Keys;
                if (y <= keys[0]) return t[keys[0]];
                if (y >= keys[keys.Count - 1]) return t[keys[keys.Count - 1]];
                for (int i = 1; i < keys.Count; i++)
                {
                    if (keys[i] < y) continue;
                    double y0 = keys[i - 1], y1 = keys[i];
                    double u = (y - y0) / Math.Max(1e-12, y1 - y0);
                    return t[y0] + u * (t[y1] - t[y0]);
                }
                return t[keys[keys.Count - 1]];
            }

            bool Outside(Point3d p)
            {
                for (int i = 0; i < tables.Count; i++)
                {
                    double xc = Lerp(tables[i], p.Y);
                    if (lowSide[i] ? p.X < xc : p.X > xc) return true;
                }
                return false;
            }

            var kept = new Mesh(); var removed = new Mesh();
            kept.Vertices.AddVertices(m.Vertices.ToPoint3dArray());
            removed.Vertices.AddVertices(m.Vertices.ToPoint3dArray());
            int nRemoved = 0;
            for (int i = 0; i < m.Faces.Count; i++)
            {
                var f = m.Faces[i];
                bool inside = true;
                foreach (var vi in new[] { f.A, f.B, f.C })
                {
                    if (Outside(m.Vertices[vi])) { inside = false; break; }
                }
                if (inside) kept.Faces.AddFace(f.A, f.B, f.C);
                else { removed.Faces.AddFace(f.A, f.B, f.C); nRemoved++; }
            }
            kept.Compact(); kept.Vertices.CombineIdentical(true, true); kept.Normals.ComputeNormals();
            removed.Compact(); removed.Vertices.CombineIdentical(true, true); removed.Normals.ComputeNormals();

            res.Kept = kept;
            res.Removed = removed;
            res.RemovedFaces = nRemoved;
            res.Note = $"{tables.Count} cut curve(s): kept {kept.Faces.Count}, removed {nRemoved} faces " +
                       $"({tables.Count(x => true)} boundaries; sides auto-detected from curve position).";
            return res;
        }
    }
}
