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

        // =====================================================================
        // WHOLE-SHELL STAGGER (2026-07-03): running bond over the quad lattice.
        // Courses = quad strips across the HEAD edges (the more-vertical edge
        // pair of each quad); alternate courses merge quad PAIRS with an offset
        // of one, so head joints never align across bed joints (2:1 bond, same
        // pattern as the validated per-course stagger in Vault Quad Courses).
        // Merging is a face -> group mapping: a merged MasonryBlock is the two
        // closed extruded boxes CONCATENATED (volume/centroid integrate exactly
        // to the merged body), and contact-by-construction survives because
        // interfaces are still emitted per ORIGINAL lattice edge — just skipped
        // where both faces share a group (the healed head joint).
        // =====================================================================
        public static ShellAssemblyResult BuildStaggered(Mesh shell, double thickness,
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

            int nfc = m.Faces.Count;
            var faceVerts = new int[nfc][];
            for (int f = 0; f < nfc; f++)
            {
                var mf = m.Faces[f];
                faceVerts[f] = mf.IsQuad ? new[] { mf.A, mf.B, mf.C, mf.D } : new[] { mf.A, mf.B, mf.C };
            }

            var top = m.TopologyEdges;
            var tv = m.TopologyVertices;

            // ---- course strips: neighbours across each quad's HEAD edges ----
            int EdgeIndex(int va, int vb)
            {
                return top.GetEdgeIndex(tv.TopologyVertexIndex(va), tv.TopologyVertexIndex(vb));
            }
            var headNbr = new int[nfc][]; // up to 2 course-neighbours per quad
            for (int f = 0; f < nfc; f++)
            {
                var vs = faceVerts[f];
                if (vs.Length != 4) { headNbr[f] = new int[0]; continue; }
                double vert01 = 0, vert12 = 0;
                for (int k = 0; k < 4; k++)
                {
                    Vector3d d = P[vs[(k + 1) % 4]] - P[vs[k]];
                    double vfrac = Math.Abs(d.Z) / Math.Max(1e-12, d.Length);
                    if (k % 2 == 0) vert01 += vfrac; else vert12 += vfrac;
                }
                // head joints = the MORE vertical edge pair
                int[] headK = vert01 >= vert12 ? new[] { 0, 2 } : new[] { 1, 3 };
                var nbrs = new List<int>(2);
                foreach (int k in headK)
                {
                    int e = EdgeIndex(vs[k], vs[(k + 1) % 4]);
                    if (e < 0) continue;
                    int[] fs = top.GetConnectedFaces(e);
                    if (fs != null && fs.Length == 2)
                        nbrs.Add(fs[0] == f ? fs[1] : fs[0]);
                }
                headNbr[f] = nbrs.ToArray();
            }

            var strips = new List<List<int>>();
            var visited = new bool[nfc];
            for (int f = 0; f < nfc; f++)
            {
                if (visited[f] || faceVerts[f].Length != 4) continue;
                var strip = new List<int> { f };
                visited[f] = true;
                // grow both directions along head-neighbours
                for (int dir = 0; dir < 2; dir++)
                {
                    int cur = f;
                    while (true)
                    {
                        int nxt = -1;
                        foreach (int nb in headNbr[cur])
                            if (!visited[nb] && faceVerts[nb].Length == 4) { nxt = nb; break; }
                        if (nxt < 0) break;
                        visited[nxt] = true;
                        if (dir == 0) strip.Add(nxt); else strip.Insert(0, nxt);
                        cur = nxt;
                    }
                }
                strips.Add(strip);
            }
            // course parity by mean z (running bond alternates per course)
            strips.Sort((a, b) =>
            {
                double za = 0, zb = 0;
                foreach (int f2 in a) za += Centroid(P, faceVerts[f2]).Z;
                foreach (int f2 in b) zb += Centroid(P, faceVerts[f2]).Z;
                return (za / a.Count).CompareTo(zb / b.Count);
            });

            // ---- face -> group (merge pairs, offset by one on odd courses) ----
            var group = new int[nfc];
            for (int i = 0; i < nfc; i++) group[i] = -1;
            int ng = 0;
            for (int s = 0; s < strips.Count; s++)
            {
                var strip = strips[s];
                int start = s % 2;                 // the running-bond offset
                int i2 = 0;
                if (start == 1 && strip.Count > 0) { group[strip[0]] = ng++; i2 = 1; }
                for (; i2 + 1 < strip.Count; i2 += 2)
                {
                    group[strip[i2]] = ng;
                    group[strip[i2 + 1]] = ng;
                    ng++;
                }
                if (i2 < strip.Count) group[strip[i2]] = ng++;
            }
            for (int f = 0; f < nfc; f++) if (group[f] < 0) group[f] = ng++; // tris/leftovers stay single

            // ---- blocks per group: concatenated closed extrusions ----
            var groupFaces = new List<int>[ng];
            for (int f = 0; f < nfc; f++)
            {
                if (groupFaces[group[f]] == null) groupFaces[group[f]] = new List<int>(2);
                groupFaces[group[f]].Add(f);
            }
            var blocks = new List<MasonryBlock>(ng);
            for (int g = 0; g < ng; g++)
            {
                var coords = new List<double>();
                var tris = new List<int>();
                var dm = new Mesh();
                int baseIdx = 0;
                foreach (int f in groupFaces[g])
                {
                    var vs = faceVerts[f];
                    var pts = new List<Point3d>(vs.Length * 2);
                    foreach (var v in vs) pts.Add(inner[v]);
                    foreach (var v in vs) pts.Add(outer[v]);
                    foreach (var p in pts) { coords.Add(p.X); coords.Add(p.Y); coords.Add(p.Z); dm.Vertices.Add(p); }
                    var src = vs.Length == 4 ? BoxTris : PrismTris;
                    for (int t = 0; t < src.Length; t++) tris.Add(baseIdx + src[t]);
                    for (int t = 0; t < src.Length; t += 3) dm.Faces.AddFace(baseIdx + src[t], baseIdx + src[t + 1], baseIdx + src[t + 2]);
                    baseIdx += pts.Count;
                }
                blocks.Add(new MasonryBlock("g" + g, coords, tris, density));
                dm.RebuildNormals();
                res.Voussoirs.Add(dm);
            }
            res.BlockCount = ng;

            // ---- interfaces per original lattice edge, skipped inside a group ----
            var ifaces = new List<MasonryInterface>();
            for (int e = 0; e < top.Count; e++)
            {
                int[] fs = top.GetConnectedFaces(e);
                if (fs == null || fs.Length != 2) continue;
                int g0 = group[fs[0]], g1 = group[fs[1]];
                if (g0 == g1) continue;                        // healed head joint
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
                if (n * (c1 - c0) < 0) n = -n;
                Vector3d t1 = edge;
                Vector3d t2 = Vector3d.CrossProduct(n, t1); t2.Unitize();
                ifaces.Add(new MasonryInterface("g" + g0, "g" + g1, poly,
                    n.X, n.Y, n.Z, t1.X, t1.Y, t1.Z, t2.X, t2.Y, t2.Z));
                var mid = new Point3d((ia.X + ib.X + oa.X + ob.X) / 4, (ia.Y + ib.Y + oa.Y + ob.Y) / 4, (ia.Z + ib.Z + oa.Z + ob.Z) / 4);
                res.InterfaceAxes.Add(new Line(mid, mid + n * hd));
            }
            res.InterfaceCount = ifaces.Count;

            // ---- supports ----
            var fixedIds = new List<string>(); var fixedSet = new HashSet<int>();
            for (int e = 0; e < top.Count; e++)
            {
                int[] fs = top.GetConnectedFaces(e);
                if (fs == null || fs.Length != 1) continue;
                var ip = top.GetTopologyVertices(e);
                int a = tv.MeshVertexIndices(ip.I)[0];
                int b = tv.MeshVertexIndices(ip.J)[0];
                double zmid = 0.5 * (P[a].Z + P[b].Z);
                int g = group[fs[0]];
                if (zmid <= zsup && fixedSet.Add(g)) { fixedIds.Add("g" + g); res.FixedIndices.Add(g); }
            }
            res.SupportCount = fixedIds.Count;

            res.Assembly = new MasonryAssembly(blocks, ifaces, new BoundaryConditions(fixedIds));
            return res;
        }
    }
}
