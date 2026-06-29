#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Solvers;
using Rhino.Geometry;

namespace Frahan.Masonry.Vault
{
    // =========================================================================
    // VaultQuadCourses — field-aligned masonry COURSE analysis on a vault shell.
    //
    // Pipeline (validated on the three-prong catenary vault, 2026-06-30):
    //   QuadRemesh the funicular shell to ~targetEdge (RhinoCommon; TargetEdgeLength
    //   is the engine's intended sizing knob) -> the quad field follows the surface
    //   flow (principal curvature ~= principal thrust on a funicular membrane).
    //   Walk the quad FACE-STRIPS in both lattice directions -> each strip is a
    //   masonry course (a chain of voussoirs running along the thrust). Build each
    //   course with VaultInterfaceMesh.BuildArchOnSurface (voussoir depth along the
    //   surface normal = Striatus stereotomy) and run CRA (rigid-block equilibrium,
    //   native OSQP when available). A course is STABLE when an admissible
    //   compression-only, friction-bounded force state exists.
    //
    // Output is the per-course verdict plus a per-face coverage score (how many of
    // the <=2 courses through a face are stable): 2 = both-way, 1 = one-way, 0 = none.
    // =========================================================================
    public sealed class QuadCourseResult
    {
        public Mesh QuadMesh;                                 // the analysis (quad) mesh
        public List<int[]> QuadFaces = new List<int[]>();     // quad face vertex indices (aligned with FaceScore)
        public int StripCount;
        public int StableCount;
        public double StablePercent;
        public bool[] StripStable;                            // per-course stability
        public List<Polyline> Courses = new List<Polyline>(); // per-course centerline polyline
        public int[] FaceScore;                               // per quad face: # stable courses through it (0,1,2)
        public int FacesBothWay, FacesOneWay, FacesNone;
    }

    public static class VaultQuadCourses
    {
        /// <summary>
        /// QuadRemesh the input shell to ~targetEdge, then analyze field-aligned courses.
        /// targetEdge &lt;= 0 skips remeshing and analyzes the input mesh as-is.
        /// </summary>
        public static QuadCourseResult Analyze(
            Mesh input, double targetEdge, double thickness, double courseWidth,
            double friction, double density = 2400.0)
        {
            Mesh m;
            if (targetEdge > 0)
            {
                var qp = new QuadRemeshParameters
                {
                    TargetEdgeLength = targetEdge,
                    AdaptiveSize = 0.0,
                    AdaptiveQuadCount = false
                };
                m = input.QuadRemesh(qp) ?? input.DuplicateMesh();
            }
            else m = input.DuplicateMesh();

            return AnalyzeQuadMesh(m, thickness, courseWidth, friction, density);
        }

        /// <summary>
        /// Analyze an existing (quad-dominant) mesh: extract face-strip courses and CRA each.
        /// </summary>
        public static QuadCourseResult AnalyzeQuadMesh(
            Mesh mesh, double thickness, double courseWidth, double friction, double density = 2400.0)
        {
            var m = mesh.DuplicateMesh();
            m.Normals.ComputeNormals(); m.UnifyNormals(); m.Normals.ComputeNormals();

            int nv = m.Vertices.Count;
            var P = new Point3d[nv]; for (int i = 0; i < nv; i++) P[i] = m.Vertices[i];
            var Nz = new Vector3d[nv];
            for (int i = 0; i < nv; i++) { var nf = m.Normals[i]; Nz[i] = new Vector3d(nf.X, nf.Y, nf.Z); }

            var faces = new List<int[]>();
            for (int i = 0; i < m.Faces.Count; i++)
            {
                var mf = m.Faces[i];
                if (mf.IsQuad) faces.Add(new[] { mf.A, mf.B, mf.C, mf.D });
            }
            int nfc = faces.Count;

            Func<int, int, long> ek = (u, v) => { int a = Math.Min(u, v), b = Math.Max(u, v); return (long)a * 100000 + b; };
            var e2f = new Dictionary<long, List<int[]>>();
            for (int fi = 0; fi < nfc; fi++)
                for (int li = 0; li < 4; li++)
                {
                    int u = faces[fi][li], v = faces[fi][(li + 1) % 4]; long k = ek(u, v);
                    if (!e2f.ContainsKey(k)) e2f[k] = new List<int[]>();
                    e2f[k].Add(new[] { fi, li });
                }
            Func<int, int, int[]> nbr = (fi, li) =>
            {
                int u = faces[fi][li], v = faces[fi][(li + 1) % 4];
                foreach (var pr in e2f[ek(u, v)]) if (pr[0] != fi) return pr;
                return null;
            };

            var vd = new HashSet<long>();
            Func<int, int, long> vk = (fi, p) => (long)fi * 2 + p;
            var stripPts = new List<List<Point3d>>();
            var stripNs = new List<List<Vector3d>>();
            var stripFaces = new List<List<int>>();

            for (int fi = 0; fi < nfc; fi++)
                for (int s0 = 0; s0 < 2; s0++)
                {
                    if (vd.Contains(vk(fi, s0))) continue;

                    var fwd = new List<Point3d>(); var fwdN = new List<Vector3d>(); var ff = new List<int>();
                    int cur = fi, ex = (s0 + 2) % 4, g = 0;
                    while (g++ < 5000)
                    {
                        int u = faces[cur][ex], v = faces[cur][(ex + 1) % 4];
                        fwd.Add(new Point3d((P[u].X + P[v].X) / 2, (P[u].Y + P[v].Y) / 2, (P[u].Z + P[v].Z) / 2));
                        var nn = Nz[u] + Nz[v]; if (nn.Length < 1e-9) nn = Vector3d.ZAxis; nn.Unitize(); fwdN.Add(nn);
                        vd.Add(vk(cur, ex % 2)); ff.Add(cur);
                        var nb = nbr(cur, ex); if (nb == null) break;
                        if (vd.Contains(vk(nb[0], nb[1] % 2))) break;
                        cur = nb[0]; ex = (nb[1] + 2) % 4;
                    }

                    var bwd = new List<Point3d>(); var bwdN = new List<Vector3d>(); var bf = new List<int>();
                    cur = fi; ex = s0; g = 0;
                    while (g++ < 5000)
                    {
                        int u = faces[cur][ex], v = faces[cur][(ex + 1) % 4];
                        bwd.Add(new Point3d((P[u].X + P[v].X) / 2, (P[u].Y + P[v].Y) / 2, (P[u].Z + P[v].Z) / 2));
                        var nn = Nz[u] + Nz[v]; if (nn.Length < 1e-9) nn = Vector3d.ZAxis; nn.Unitize(); bwdN.Add(nn);
                        vd.Add(vk(cur, ex % 2)); bf.Add(cur);
                        var nb = nbr(cur, ex); if (nb == null) break;
                        if (vd.Contains(vk(nb[0], nb[1] % 2))) break;
                        cur = nb[0]; ex = (nb[1] + 2) % 4;
                    }

                    var pts = new List<Point3d>(); var ns = new List<Vector3d>(); var fcs = new List<int>();
                    for (int i = bwd.Count - 1; i >= 1; i--) { pts.Add(bwd[i]); ns.Add(bwdN[i]); }
                    for (int i = 0; i < fwd.Count; i++) { pts.Add(fwd[i]); ns.Add(fwdN[i]); }
                    foreach (var f in bf) if (!fcs.Contains(f)) fcs.Add(f);
                    foreach (var f in ff) if (!fcs.Contains(f)) fcs.Add(f);
                    if (pts.Count >= 4) { stripPts.Add(pts); stripNs.Add(ns); stripFaces.Add(fcs); }
                }

            var res = new QuadCourseResult
            {
                QuadMesh = m,
                QuadFaces = faces,
                StripCount = stripPts.Count,
                StripStable = new bool[stripPts.Count],
                FaceScore = new int[nfc]
            };

            int nstab = 0;
            for (int s = 0; s < stripPts.Count; s++)
            {
                bool st;
                try
                {
                    var arch = VaultInterfaceMesh.BuildArchOnSurface(stripPts[s], stripNs[s], thickness, courseWidth, density);
                    st = MasonryStabilityChecker.Check(arch.Assembly, friction, 8, true, 1.0, -9.80665).IsStable;
                }
                catch { st = false; }
                res.StripStable[s] = st;
                res.Courses.Add(new Polyline(stripPts[s]));
                if (st) { nstab++; foreach (int fi in stripFaces[s]) res.FaceScore[fi]++; }
            }

            res.StableCount = nstab;
            res.StablePercent = stripPts.Count > 0 ? 100.0 * nstab / stripPts.Count : 0.0;
            int c2 = 0, c1 = 0, c0 = 0;
            for (int fi = 0; fi < nfc; fi++) { if (res.FaceScore[fi] >= 2) c2++; else if (res.FaceScore[fi] == 1) c1++; else c0++; }
            res.FacesBothWay = c2; res.FacesOneWay = c1; res.FacesNone = c0;
            return res;
        }
    }
}
