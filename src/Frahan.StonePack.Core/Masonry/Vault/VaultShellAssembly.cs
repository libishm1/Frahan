#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.DataModel;
using Rhino.Geometry;

namespace Frahan.Masonry.Vault
{
    // =========================================================================
    // VaultShellAssembly — turn a (thrust-aligned) quad/tri shell mesh into a
    // CONTACT-READY voussoir assembly for whole-shell CRA. Each face becomes a
    // block by extruding its corners +/- thickness/2 along the SHARED vertex
    // normals, so two faces meeting at a mesh edge share that edge's extruded
    // rectangle EXACTLY -> contact by construction (no gap, no contact detection).
    // Interior mesh edges become interfaces; naked edges near the lowest z-band
    // (the springing) fix their block as a support. Feed .Assembly straight into
    // MasonryStabilityChecker.
    //
    // This is the structural counterpart of the geometric Voronoi rubble skin:
    // the quad partition follows the thrust (QuadRemesh ~ principal curvature ~
    // principal thrust on a funicular), the blocks TOUCH (share faces), and the
    // interface graph is exact -> the equilibrium (CRA) problem is well-posed.
    // =========================================================================
    public sealed class ShellAssemblyResult
    {
        public MasonryAssembly Assembly;
        public List<Mesh> Voussoirs = new List<Mesh>();   // display blocks (aligned with block order)
        public List<int> FixedIndices = new List<int>();  // support block indices
        public List<Line> InterfaceAxes = new List<Line>(); // interface centre -> +normal (debug/preview)
        public int BlockCount, InterfaceCount, SupportCount;
    }

    public static class VaultShellAssembly
    {
        // 8-corner hexahedron (inner 0-3, outer 4-7) -- same winding as VaultInterfaceMesh.BoxTris
        static readonly int[] BoxTris =
        {
            0,1,2, 0,2,3,   4,6,5, 4,7,6,
            0,4,5, 0,5,1,   1,5,6, 1,6,2,
            2,6,7, 2,7,3,   3,7,4, 3,4,0
        };
        // 6-corner prism (inner 0-2, outer 3-5)
        static readonly int[] PrismTris =
        {
            0,2,1, 3,4,5,
            0,3,5, 0,5,2,
            2,5,4, 2,4,1,
            1,4,3, 1,3,0
        };

        public static ShellAssemblyResult Build(Mesh shell, double thickness,
            double density = 2400.0, double supportBand = 0.08)
        {
            if (shell == null) throw new ArgumentNullException(nameof(shell));
            var res = new ShellAssemblyResult();

            var m = shell.DuplicateMesh();
            m.Vertices.CombineIdentical(true, true);
            m.Weld(Math.PI);
            m.UnifyNormals();
            m.Normals.ComputeNormals();
            m.Compact();

            int nv = m.Vertices.Count;
            var P = new Point3d[nv]; var N = new Vector3d[nv];
            double zmin = double.MaxValue, zmax = double.MinValue;
            for (int i = 0; i < nv; i++)
            {
                P[i] = m.Vertices[i];
                var nf = m.Normals[i]; N[i] = new Vector3d(nf.X, nf.Y, nf.Z);
                if (N[i].Length < 1e-9) N[i] = Vector3d.ZAxis; N[i].Unitize();
                if (P[i].Z < zmin) zmin = P[i].Z; if (P[i].Z > zmax) zmax = P[i].Z;
            }
            double hd = thickness * 0.5;
            var inner = new Point3d[nv]; var outer = new Point3d[nv];
            for (int i = 0; i < nv; i++) { inner[i] = P[i] - N[i] * hd; outer[i] = P[i] + N[i] * hd; }
            double zsup = zmin + supportBand * Math.Max(1e-9, zmax - zmin);

            // ---- one block per face (shared inner/outer verts -> shared contact faces) ----
            int nfc = m.Faces.Count;
            var blocks = new List<MasonryBlock>(nfc);
            var faceVerts = new int[nfc][];
            for (int f = 0; f < nfc; f++)
            {
                var mf = m.Faces[f];
                int[] vs = mf.IsQuad ? new[] { mf.A, mf.B, mf.C, mf.D } : new[] { mf.A, mf.B, mf.C };
                faceVerts[f] = vs;
                var pts = new List<Point3d>(vs.Length * 2);
                foreach (var v in vs) pts.Add(inner[v]);
                foreach (var v in vs) pts.Add(outer[v]);
                var coords = new List<double>(pts.Count * 3);
                foreach (var p in pts) { coords.Add(p.X); coords.Add(p.Y); coords.Add(p.Z); }
                var tris = mf.IsQuad ? BoxTris : PrismTris;
                blocks.Add(new MasonryBlock("b" + f, coords, new List<int>(tris), density));

                var dm = new Mesh();
                foreach (var p in pts) dm.Vertices.Add(p);
                for (int t = 0; t < tris.Length; t += 3) dm.Faces.AddFace(tris[t], tris[t + 1], tris[t + 2]);
                dm.RebuildNormals();
                res.Voussoirs.Add(dm);
            }
            res.BlockCount = nfc;

            var top = m.TopologyEdges;
            var tv = m.TopologyVertices;

            // ---- interfaces: interior edges (shared by exactly 2 faces) ----
            var ifaces = new List<MasonryInterface>();
            for (int e = 0; e < top.Count; e++)
            {
                int[] fs = top.GetConnectedFaces(e);
                if (fs == null || fs.Length != 2) continue;
                var ip = top.GetTopologyVertices(e);
                int a = tv.MeshVertexIndices(ip.I)[0];
                int b = tv.MeshVertexIndices(ip.J)[0];

                Point3d ia = inner[a], ib = inner[b], oa = outer[a], ob = outer[b];
                var poly = new List<ContactVertex>(4)
                {
                    new ContactVertex(ia.X, ia.Y, ia.Z),
                    new ContactVertex(ib.X, ib.Y, ib.Z),
                    new ContactVertex(ob.X, ob.Y, ob.Z),
                    new ContactVertex(oa.X, oa.Y, oa.Z),
                };
                Vector3d edge = ib - ia; if (edge.Length < 1e-12) continue; edge.Unitize();
                Vector3d thick = oa - ia; if (thick.Length < 1e-12) thick = Vector3d.ZAxis; thick.Unitize();
                Vector3d n = Vector3d.CrossProduct(edge, thick); if (n.Length < 1e-12) continue; n.Unitize();
                Point3d c0 = Centroid(P, faceVerts[fs[0]]), c1 = Centroid(P, faceVerts[fs[1]]);
                if (n * (c1 - c0) < 0) n = -n;                 // normal points from block fs[0] -> fs[1]
                Vector3d t1 = edge;
                Vector3d t2 = Vector3d.CrossProduct(n, t1); t2.Unitize();

                ifaces.Add(new MasonryInterface("b" + fs[0], "b" + fs[1], poly,
                    n.X, n.Y, n.Z, t1.X, t1.Y, t1.Z, t2.X, t2.Y, t2.Z));
                var mid = new Point3d((ia.X + ib.X + oa.X + ob.X) / 4, (ia.Y + ib.Y + oa.Y + ob.Y) / 4, (ia.Z + ib.Z + oa.Z + ob.Z) / 4);
                res.InterfaceAxes.Add(new Line(mid, mid + n * (hd)));
            }
            res.InterfaceCount = ifaces.Count;

            // ---- supports: naked edges in the lowest z-band fix their block ----
            var fixedIds = new List<string>(); var fixedSet = new HashSet<int>();
            for (int e = 0; e < top.Count; e++)
            {
                int[] fs = top.GetConnectedFaces(e);
                if (fs == null || fs.Length != 1) continue;
                var ip = top.GetTopologyVertices(e);
                int a = tv.MeshVertexIndices(ip.I)[0];
                int b = tv.MeshVertexIndices(ip.J)[0];
                double zmid = 0.5 * (P[a].Z + P[b].Z);
                if (zmid <= zsup && fixedSet.Add(fs[0])) { fixedIds.Add("b" + fs[0]); res.FixedIndices.Add(fs[0]); }
            }
            res.SupportCount = fixedIds.Count;

            res.Assembly = new MasonryAssembly(blocks, ifaces, new BoundaryConditions(fixedIds));
            return res;
        }

        static Point3d Centroid(Point3d[] P, int[] vs)
        {
            double x = 0, y = 0, z = 0;
            for (int i = 0; i < vs.Length; i++) { x += P[vs[i]].X; y += P[vs[i]].Y; z += P[vs[i]].Z; }
            return new Point3d(x / vs.Length, y / vs.Length, z / vs.Length);
        }
    }
}
