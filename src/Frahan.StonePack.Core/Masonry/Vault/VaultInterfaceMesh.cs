#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;
using Frahan.Masonry.DataModel;

namespace Frahan.Masonry.Vault
{
    // =========================================================================
    // VaultInterfaceMesh — build a CONTACT-READY masonry assembly (voussoir
    // blocks + EXPLICIT shared-joint interfaces) from an arch centerline.
    //
    // This is Checkpoint-2.5: CRA/RBE needs explicit MasonryInterface objects with
    // correct A->B normals and contact polygons — NOT MeshContactDetector, which
    // produces a bad equilibrium even for a simple 2-box stack. Here every joint
    // is a planar quad shared exactly by two voussoirs, with the interface normal
    // along the local thrust (tangent) and the contact polygon wound CCW about it.
    //
    // The centerline must lie in the Y-Z section plane (X = 0); X is the ring-width
    // axis. Voussoir i spans joint[i]..joint[i+1]; voussoirs 0 and n-2 are the
    // springers (fixed). Joints are perpendicular to the local thrust direction.
    // =========================================================================
    public sealed class VaultArchAssembly
    {
        public MasonryAssembly Assembly;
        public List<Mesh> Voussoirs = new List<Mesh>();
        public List<int> FixedIndices = new List<int>();
        public int InterfaceCount;
        public int BlockCount;
    }

    public static class VaultInterfaceMesh
    {
        // 12-triangle box from 8 corners [0..3 = near joint, 4..7 = far joint].
        private static readonly int[] BoxTris =
        {
            0,1,2, 0,2,3,   // near cap
            4,6,5, 4,7,6,   // far cap
            0,4,5, 0,5,1,   // side
            1,5,6, 1,6,2,
            2,6,7, 2,7,3,
            3,7,4, 3,4,0
        };

        // Order the 4 joint corners so the polygon winds CCW about `nrm`
        // (polygon normal by the right-hand rule aligned with nrm).
        private static Point3d[] OrderAbout(Point3d[] face, Vector3d nrm)
        {
            var center = new Point3d(
                (face[0].X + face[1].X + face[2].X + face[3].X) / 4.0,
                (face[0].Y + face[1].Y + face[2].Y + face[3].Y) / 4.0,
                (face[0].Z + face[1].Z + face[2].Z + face[3].Z) / 4.0);
            var pn = Vector3d.Zero;
            for (int i = 0; i < 4; i++)
            {
                var a = face[i] - center;
                var b = face[(i + 1) % 4] - center;
                pn += Vector3d.CrossProduct(a, b);
            }
            if (pn * nrm < 0)
            {
                var r = new Point3d[] { face[0], face[3], face[2], face[1] };
                return r;
            }
            return face;
        }

        /// <summary>
        /// Build a voussoir-arch assembly from a section centerline (in the Y-Z
        /// plane). thickness = section depth D; ringWidth = block extent in X.
        /// </summary>
        public static VaultArchAssembly BuildArch(
            IList<Point3d> centerline, double thickness, double ringWidth, double density)
            => BuildArchDir(centerline, Vector3d.XAxis, thickness, ringWidth, density);

        /// <summary>
        /// Generalised arch builder: the centerline may lie in ANY vertical plane;
        /// widthDir is the ring-width axis (the slice direction). With widthDir =
        /// world-X and a Y-Z centerline this is byte-identical to the Güell BuildArch
        /// path. Used to CRA a shell course-by-course (slice into arch rings).
        /// </summary>
        public static VaultArchAssembly BuildArchDir(
            IList<Point3d> centerline, Vector3d widthDir, double thickness, double ringWidth, double density)
        {
            int n = centerline.Count;
            if (n < 3) throw new ArgumentException("centerline needs >= 3 points");
            double hd = thickness * 0.5, hw = ringWidth * 0.5;
            Vector3d wd = widthDir; if (wd.Length < 1e-9) wd = Vector3d.XAxis; wd.Unitize();

            // tangent (3D) and section normal (perp to tangent, in the vertical plane)
            var tan = new Vector3d[n];
            var sn = new Vector3d[n];
            for (int i = 0; i < n; i++)
            {
                Point3d a = centerline[Math.Max(0, i - 1)];
                Point3d b = centerline[Math.Min(n - 1, i + 1)];
                var t = b - a;
                if (t.Length < 1e-9) t = Vector3d.CrossProduct(wd, Vector3d.ZAxis);
                t.Unitize();
                tan[i] = t;
                var v = Vector3d.CrossProduct(wd, t);
                if (v.Length < 1e-9) v = Vector3d.ZAxis;
                v.Unitize();
                sn[i] = v;
            }

            // joint faces (4 corners each) at every centerline node
            var face = new Point3d[n][];
            for (int i = 0; i < n; i++)
            {
                Point3d c = centerline[i];
                Vector3d v = sn[i];
                face[i] = new Point3d[]
                {
                    c - v * hd - wd * hw,
                    c + v * hd - wd * hw,
                    c + v * hd + wd * hw,
                    c - v * hd + wd * hw
                };
            }

            var res = new VaultArchAssembly();
            var blocks = new List<MasonryBlock>();
            for (int i = 0; i < n - 1; i++)
            {
                var pts = new Point3d[8];
                for (int j = 0; j < 4; j++) { pts[j] = face[i][j]; pts[4 + j] = face[i + 1][j]; }
                var coords = new List<double>(24);
                foreach (var p in pts) { coords.Add(p.X); coords.Add(p.Y); coords.Add(p.Z); }
                blocks.Add(new MasonryBlock("v" + i, coords, new List<int>(BoxTris), density));

                var m = new Mesh();
                foreach (var p in pts) m.Vertices.Add(p);
                for (int t = 0; t < BoxTris.Length; t += 3) m.Faces.AddFace(BoxTris[t], BoxTris[t + 1], BoxTris[t + 2]);
                m.RebuildNormals();   // unify winding so the baked block renders solid (display only)
                res.Voussoirs.Add(m);
            }

            // explicit interfaces at the interior joints i = 1..n-2
            var ifaces = new List<MasonryInterface>();
            for (int i = 1; i <= n - 2; i++)
            {
                Vector3d nrm = tan[i]; // A=v_{i-1} -> B=v_i, along increasing thrust
                Point3d[] ordered = OrderAbout((Point3d[])face[i].Clone(), nrm);
                var poly = new List<ContactVertex>(4);
                foreach (var p in ordered) poly.Add(new ContactVertex(p.X, p.Y, p.Z));
                Vector3d t1 = wd;
                Vector3d t2 = Vector3d.CrossProduct(nrm, t1);
                t2.Unitize();
                ifaces.Add(new MasonryInterface("v" + (i - 1), "v" + i, poly,
                    nrm.X, nrm.Y, nrm.Z, t1.X, t1.Y, t1.Z, t2.X, t2.Y, t2.Z));
            }

            var fixedIds = new List<string> { "v0", "v" + (n - 2) };
            res.Assembly = new MasonryAssembly(blocks, ifaces, new BoundaryConditions(fixedIds));
            res.FixedIndices.Add(0); res.FixedIndices.Add(n - 2);
            res.InterfaceCount = ifaces.Count;
            res.BlockCount = blocks.Count;
            return res;
        }

        // triangular-prism block: 6 verts [0..2 bottom -n, 3..5 top +n], 8 triangles.
        private static readonly int[] PrismTris =
        {
            0, 2, 1,            // bottom cap (outward = -n)
            3, 4, 5,            // top cap
            0, 1, 4, 0, 4, 3,   // side v0-v1
            1, 2, 5, 1, 5, 4,   // side v1-v2
            2, 0, 3, 2, 3, 5    // side v2-v0
        };

        /// <summary>
        /// Build a contact-ready assembly for a 2-D funicular SHELL: one triangular
        /// prism voussoir per mesh face, one explicit MasonryInterface per interior
        /// edge (the edge swept through the thickness), with A->B normals along the
        /// in-surface thrust direction. Faces with >= 2 support vertices are fixed.
        /// </summary>
        public static VaultArchAssembly BuildShell(
            IList<Point3d> verts, IList<int[]> faces, IList<bool> isSupportVertex,
            double thickness, double density)
        {
            int nf = faces.Count;
            double hd = thickness * 0.5;
            var fn = new Vector3d[nf];      // face normals
            var fc = new Point3d[nf];       // face centroids
            for (int f = 0; f < nf; f++)
            {
                Point3d a = verts[faces[f][0]], b = verts[faces[f][1]], c = verts[faces[f][2]];
                var n = Vector3d.CrossProduct(b - a, c - a);
                if (n.Length < 1e-12) n = Vector3d.ZAxis; n.Unitize();
                fn[f] = n;
                fc[f] = new Point3d((a.X + b.X + c.X) / 3.0, (a.Y + b.Y + c.Y) / 3.0, (a.Z + b.Z + c.Z) / 3.0);
            }

            var res = new VaultArchAssembly();
            var blocks = new List<MasonryBlock>(nf);
            var fixedIds = new List<string>();
            for (int f = 0; f < nf; f++)
            {
                var v = new Point3d[6];
                for (int j = 0; j < 3; j++)
                {
                    Point3d p = verts[faces[f][j]];
                    v[j] = p - fn[f] * hd;      // bottom
                    v[3 + j] = p + fn[f] * hd;  // top
                }
                var coords = new List<double>(18);
                foreach (var p in v) { coords.Add(p.X); coords.Add(p.Y); coords.Add(p.Z); }
                blocks.Add(new MasonryBlock("f" + f, coords, new List<int>(PrismTris), density));

                var m = new Mesh();
                foreach (var p in v) m.Vertices.Add(p);
                for (int t = 0; t < PrismTris.Length; t += 3) m.Faces.AddFace(PrismTris[t], PrismTris[t + 1], PrismTris[t + 2]);
                m.RebuildNormals();   // unify winding so the baked block renders solid (display only)
                res.Voussoirs.Add(m);

                int sv = 0; for (int j = 0; j < 3; j++) if (isSupportVertex[faces[f][j]]) sv++;
                if (sv >= 2) { fixedIds.Add("f" + f); res.FixedIndices.Add(f); }
            }

            // edge -> incident faces
            var edgeFaces = new Dictionary<long, List<int>>();
            for (int f = 0; f < nf; f++)
                for (int e = 0; e < 3; e++)
                {
                    int u = faces[f][e], w = faces[f][(e + 1) % 3];
                    int lo = Math.Min(u, w), hi = Math.Max(u, w);
                    long key = (long)lo * 1000000L + hi;
                    List<int> lst;
                    if (!edgeFaces.TryGetValue(key, out lst)) { lst = new List<int>(2); edgeFaces[key] = lst; }
                    lst.Add(f);
                }

            var ifaces = new List<MasonryInterface>();
            foreach (var kv in edgeFaces)
            {
                if (kv.Value.Count != 2) continue;            // interior edges only
                int fa = kv.Value[0], fb = kv.Value[1];
                int lo = (int)(kv.Key / 1000000L), hi = (int)(kv.Key % 1000000L);
                Point3d p0 = verts[lo], p1 = verts[hi];

                Vector3d nAvg = fn[fa] + fn[fb]; if (nAvg.Length < 1e-9) nAvg = fn[fa]; nAvg.Unitize();
                Vector3d edge = p1 - p0; if (edge.Length < 1e-9) continue; edge.Unitize();

                // A->B normal: from fa centroid to fb centroid, perpendicular to the edge
                Vector3d nrm = fc[fb] - fc[fa];
                nrm -= (nrm * edge) * edge;
                if (nrm.Length < 1e-9) nrm = Vector3d.CrossProduct(nAvg, edge);
                nrm.Unitize();

                var quad = new Point3d[]
                {
                    p0 - nAvg * hd, p1 - nAvg * hd, p1 + nAvg * hd, p0 + nAvg * hd
                };
                var ordered = OrderAbout(quad, nrm);
                var poly = new List<ContactVertex>(4);
                foreach (var p in ordered) poly.Add(new ContactVertex(p.X, p.Y, p.Z));
                Vector3d t1 = edge;
                Vector3d t2 = Vector3d.CrossProduct(nrm, t1); t2.Unitize();
                ifaces.Add(new MasonryInterface("f" + fa, "f" + fb, poly,
                    nrm.X, nrm.Y, nrm.Z, t1.X, t1.Y, t1.Z, t2.X, t2.Y, t2.Z));
            }

            res.Assembly = new MasonryAssembly(blocks, ifaces, new BoundaryConditions(fixedIds));
            res.InterfaceCount = ifaces.Count;
            res.BlockCount = blocks.Count;
            return res;
        }

        /// <summary>
        /// Build a voussoir course that FOLLOWS THE SHELL SURFACE: the centerline is
        /// a strip of mesh nodes, and at each node the section normal is the SURFACE
        /// NORMAL (so the voussoir depth runs through the shell thickness) and the
        /// width axis is the in-surface transverse (tangent x normal). Joints stay
        /// perpendicular to the local thrust. This is the correct masonry-course
        /// model — courses run along the mesh, not across flat world planes.
        /// </summary>
        public static VaultArchAssembly BuildArchOnSurface(
            IList<Point3d> centerline, IList<Vector3d> normals, double thickness, double width, double density)
        {
            var th = new double[centerline.Count];
            for (int i = 0; i < th.Length; i++) th[i] = thickness;
            return BuildArchOnSurface(centerline, normals, th, width, density);
        }

        /// <summary>
        /// Variable-thickness course: per-node section thickness (load-driven). Lets the
        /// section be thicker at the supports/springers and thinner at the crown/midspan --
        /// the Armadillo Vault's 12 cm -> 5 cm distribution (Rippmann/Block, AAG 2016).
        /// thicknessPerNode parallels centerline; the voussoir between node i and i+1 spans
        /// the two local half-thicknesses, so the section grades smoothly along the course.
        /// </summary>
        public static VaultArchAssembly BuildArchOnSurface(
            IList<Point3d> centerline, IList<Vector3d> normals, IList<double> thicknessPerNode, double width, double density)
        {
            int n = centerline.Count;
            if (n < 3) throw new ArgumentException("course needs >= 3 nodes");
            if (thicknessPerNode == null || thicknessPerNode.Count < n)
                throw new ArgumentException("thicknessPerNode must parallel centerline");
            double hw = width * 0.5;

            var tan = new Vector3d[n];
            var sn = new Vector3d[n];   // section normal = surface normal (through the shell)
            var wd = new Vector3d[n];   // in-surface transverse (block width axis)
            for (int i = 0; i < n; i++)
            {
                Point3d a = centerline[Math.Max(0, i - 1)];
                Point3d b = centerline[Math.Min(n - 1, i + 1)];
                var t = b - a; if (t.Length < 1e-9) t = Vector3d.XAxis; t.Unitize(); tan[i] = t;
                var nrm = normals[i]; nrm.Unitize();
                nrm -= (nrm * t) * t;                       // make normal perpendicular to the thrust tangent
                if (nrm.Length < 1e-9) nrm = Vector3d.ZAxis; nrm.Unitize();
                sn[i] = nrm;
                var w = Vector3d.CrossProduct(t, nrm); if (w.Length < 1e-9) w = Vector3d.XAxis; w.Unitize();
                wd[i] = w;
            }

            var face = new Point3d[n][];
            for (int i = 0; i < n; i++)
            {
                Point3d c = centerline[i];
                double hd = thicknessPerNode[i] * 0.5;   // load-driven per-node half-thickness
                face[i] = new Point3d[]
                {
                    c - sn[i] * hd - wd[i] * hw,
                    c + sn[i] * hd - wd[i] * hw,
                    c + sn[i] * hd + wd[i] * hw,
                    c - sn[i] * hd + wd[i] * hw
                };
            }

            var res = new VaultArchAssembly();
            var blocks = new List<MasonryBlock>();
            for (int i = 0; i < n - 1; i++)
            {
                var pts = new Point3d[8];
                for (int j = 0; j < 4; j++) { pts[j] = face[i][j]; pts[4 + j] = face[i + 1][j]; }
                var coords = new List<double>(24);
                foreach (var p in pts) { coords.Add(p.X); coords.Add(p.Y); coords.Add(p.Z); }
                blocks.Add(new MasonryBlock("v" + i, coords, new List<int>(BoxTris), density));
                var m = new Mesh();
                foreach (var p in pts) m.Vertices.Add(p);
                for (int tt = 0; tt < BoxTris.Length; tt += 3) m.Faces.AddFace(BoxTris[tt], BoxTris[tt + 1], BoxTris[tt + 2]);
                m.RebuildNormals();   // unify winding so the baked block renders solid (display only)
                res.Voussoirs.Add(m);
            }

            var ifaces = new List<MasonryInterface>();
            for (int i = 1; i <= n - 2; i++)
            {
                Vector3d nrm = tan[i];
                Point3d[] ordered = OrderAbout((Point3d[])face[i].Clone(), nrm);
                var poly = new List<ContactVertex>(4);
                foreach (var p in ordered) poly.Add(new ContactVertex(p.X, p.Y, p.Z));
                Vector3d t1 = wd[i];
                Vector3d t2 = Vector3d.CrossProduct(nrm, t1); t2.Unitize();
                ifaces.Add(new MasonryInterface("v" + (i - 1), "v" + i, poly,
                    nrm.X, nrm.Y, nrm.Z, t1.X, t1.Y, t1.Z, t2.X, t2.Y, t2.Z));
            }

            res.Assembly = new MasonryAssembly(blocks, ifaces, new BoundaryConditions(new List<string> { "v0", "v" + (n - 2) }));
            res.FixedIndices.Add(0); res.FixedIndices.Add(n - 2);
            res.InterfaceCount = ifaces.Count; res.BlockCount = blocks.Count;
            return res;
        }

        /// <summary>Semicircular arch centerline (Y-Z), for a known-stable sanity test.</summary>
        public static List<Point3d> Semicircle(double radius, double yCenter, double zBase, int n)
        {
            var pts = new List<Point3d>(n);
            for (int i = 0; i < n; i++)
            {
                double a = Math.PI * i / (n - 1); // 0..pi
                pts.Add(new Point3d(0, yCenter - radius * Math.Cos(a), zBase + radius * Math.Sin(a)));
            }
            return pts;
        }
    }
}
