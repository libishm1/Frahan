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
        public List<Mesh> Voussoirs = new List<Mesh>();       // per-course voussoir blocks (staggered if requested)
        public bool Staggered;                                // running-bond 1/2-voussoir offset on alternate courses
    }

    public static class VaultQuadCourses
    {
        /// <summary>
        /// QuadRemesh the input shell to ~targetEdge, then analyze field-aligned courses.
        /// targetEdge &lt;= 0 skips remeshing and analyzes the input mesh as-is.
        /// </summary>
        public static QuadCourseResult Analyze(
            Mesh input, double targetEdge, double thickness, double courseWidth,
            double friction, double density = 2400.0, double thicknessTop = -1.0, bool stagger = false)
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

            return AnalyzeQuadMesh(m, thickness, courseWidth, friction, density, thicknessTop, stagger);
        }

        /// <summary>
        /// Analyze an existing (quad-dominant) mesh: extract face-strip courses and CRA each.
        /// </summary>
        public static QuadCourseResult AnalyzeQuadMesh(
            Mesh mesh, double thickness, double courseWidth, double friction, double density = 2400.0,
            double thicknessTop = -1.0, bool stagger = false)
        {
            var m = mesh.DuplicateMesh();
            m.Normals.ComputeNormals(); m.UnifyNormals(); m.Normals.ComputeNormals();

            int nv = m.Vertices.Count;
            var P = new Point3d[nv]; for (int i = 0; i < nv; i++) P[i] = m.Vertices[i];
            double zmin = double.MaxValue, zmax = double.MinValue;
            for (int i = 0; i < nv; i++) { if (P[i].Z < zmin) zmin = P[i].Z; if (P[i].Z > zmax) zmax = P[i].Z; }
            double zrange = Math.Max(1e-9, zmax - zmin);   // load-driven thickness grades base->crown
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
                // running-bond stagger: shift alternate courses' joints to the midpoints of
                // their segments (1/2-voussoir offset against sliding); even courses unchanged.
                IList<Point3d> cpts = stripPts[s];
                IList<Vector3d> cns = stripNs[s];
                if (stagger && (s % 2 == 1) && cpts.Count >= 2)
                    StaggerStrip(stripPts[s], stripNs[s], out cpts, out cns);
                try
                {
                    double[] th = null;
                    if (thicknessTop > 0.0 && Math.Abs(thicknessTop - thickness) > 1e-9)
                    {
                        th = new double[cpts.Count];
                        for (int k = 0; k < th.Length; k++)
                        {
                            double u = Math.Min(1.0, Math.Max(0.0, (cpts[k].Z - zmin) / zrange));
                            th[k] = thickness + (thicknessTop - thickness) * u;   // base(thick) -> crown(thin)
                        }
                    }
                    var arch = th != null
                        ? VaultInterfaceMesh.BuildArchOnSurface(cpts, cns, th, courseWidth, density)
                        : VaultInterfaceMesh.BuildArchOnSurface(cpts, cns, thickness, courseWidth, density);
                    st = MasonryStabilityChecker.Check(arch.Assembly, friction, 8, true, 1.0, -9.80665).IsStable;
                    if (arch.Voussoirs != null) res.Voussoirs.AddRange(arch.Voussoirs);
                }
                catch { st = false; }
                res.StripStable[s] = st;
                res.Courses.Add(new Polyline(cpts));
                if (st) { nstab++; foreach (int fi in stripFaces[s]) res.FaceScore[fi]++; }
            }
            res.Staggered = stagger;

            res.StableCount = nstab;
            res.StablePercent = stripPts.Count > 0 ? 100.0 * nstab / stripPts.Count : 0.0;
            int c2 = 0, c1 = 0, c0 = 0;
            for (int fi = 0; fi < nfc; fi++) { if (res.FaceScore[fi] >= 2) c2++; else if (res.FaceScore[fi] == 1) c1++; else c0++; }
            res.FacesBothWay = c2; res.FacesOneWay = c1; res.FacesNone = c0;
            return res;
        }

        // ---------------------------------------------------------------------
        // Running-bond stagger: rebuild a course's node list so its voussoir joints fall
        // at the MIDPOINTS of the original segments -- a 1/2-voussoir offset from the
        // neighbouring un-staggered course, so the head joints no longer line up across
        // courses (interlock against sliding; the Armadillo's staggered-course principle).
        // The two end voussoirs become half-width; normals are averaged at the new nodes.
        // ---------------------------------------------------------------------
        static void StaggerStrip(IList<Point3d> pts, IList<Vector3d> ns,
            out IList<Point3d> opts, out IList<Vector3d> ons)
        {
            int n = pts.Count;
            var op = new List<Point3d>(n + 1);
            var on = new List<Vector3d>(n + 1);
            op.Add(pts[0]); on.Add(ns[0]);
            for (int i = 0; i < n - 1; i++)
            {
                op.Add(new Point3d(
                    (pts[i].X + pts[i + 1].X) * 0.5,
                    (pts[i].Y + pts[i + 1].Y) * 0.5,
                    (pts[i].Z + pts[i + 1].Z) * 0.5));
                var v = ns[i] + ns[i + 1];
                if (v.Length < 1e-9) v = ns[i];
                v.Unitize();
                on.Add(v);
            }
            op.Add(pts[n - 1]); on.Add(ns[n - 1]);
            opts = op; ons = on;
        }
    }
}
