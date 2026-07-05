#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Tna;
using Rhino.Geometry;

namespace Frahan.Masonry.Vault
{
    // =========================================================================
    // FormFinding — small reusable primitives for VAULT GENERATION (Libish
    // 2026-07-02): boundary curves -> flat net -> catenary/force-density relax
    // (hang, then invert = Gaudí's hanging-chain method) -> optional SubD
    // smoothing. Each piece is deliberately single-purpose so it composes with
    // other workflows (the relax reuses the validated TnaForceDensity3D solver
    // that produced the Vault_TNA Güell surface).
    // =========================================================================
    public static class FormFinding
    {
        /// <summary>
        /// True catenary through a and b with arc length = lengthFactor x chord
        /// (lengthFactor &gt; 1; the hanging-chain primitive). Returns a sampled
        /// polyline of `segments` spans hanging in the vertical plane through ab.
        /// </summary>
        public static Polyline CatenaryCurve(Point3d a, Point3d b, double lengthFactor, int segments)
        {
            if (segments < 2) segments = 2;
            if (lengthFactor < 1.0005) lengthFactor = 1.0005;
            Vector3d ab = b - a;
            var h = new Vector3d(ab.X, ab.Y, 0.0);
            double d = h.Length;                       // horizontal span
            double dv = ab.Z;                          // vertical offset
            double L = lengthFactor * a.DistanceTo(b); // target arc length
            if (d < 1e-9)
            {
                // vertical chord: degenerate — just a straight drop
                var plv = new Polyline();
                for (int i = 0; i <= segments; i++) plv.Add(a + ab * ((double)i / segments));
                return plv;
            }
            h.Unitize();
            // solve 2*c*sinh(d/(2c)) = sqrt(L^2 - dv^2) for the catenary parameter c
            double rhs = Math.Sqrt(Math.Max(1e-12, L * L - dv * dv));
            double lo = 1e-4 * d, hi = 1e4 * d;
            for (int it = 0; it < 200; it++)
            {
                double mid = 0.5 * (lo + hi);
                double f = 2.0 * mid * Math.Sinh(d / (2.0 * mid)) - rhs;
                if (f > 0) lo = mid; else hi = mid;    // f decreases as c grows
            }
            double c = 0.5 * (lo + hi);
            // z(x) = c*cosh((x - x0)/c) + z0 through (0,0) and (d,dv)
            double x0 = 0.5 * (d - c * Math.Log((L + dv) / (L - dv)));
            double z0 = -c * Math.Cosh(-x0 / c);
            var pl = new Polyline();
            for (int i = 0; i <= segments; i++)
            {
                double x = d * i / (double)segments;
                double z = c * Math.Cosh((x - x0) / c) + z0;
                pl.Add(a + h * x + new Vector3d(0, 0, z));
            }
            return pl;
        }

        /// <summary>
        /// Flat triangulated net from a closed planar boundary (the form-finding
        /// network). anchors = the naked boundary vertices (default supports).
        /// </summary>
        public static Mesh BoundaryNet(Curve boundary, double edgeLen, out List<Point3d> anchors)
        {
            anchors = new List<Point3d>();
            if (boundary == null || !boundary.IsClosed) return null;
            var mp = new MeshingParameters
            {
                MaximumEdgeLength = Math.Max(0.02, edgeLen),
                MinimumEdgeLength = Math.Max(0.01, edgeLen * 0.4),
                GridAspectRatio = 1.0,
                SimplePlanes = false,
                RefineGrid = true,
            };
            var brep = Brep.CreatePlanarBreps(boundary, 0.001);
            if (brep == null || brep.Length == 0) return null;
            var parts = Mesh.CreateFromBrep(brep[0], mp);
            if (parts == null || parts.Length == 0) return null;
            var m = new Mesh();
            foreach (var p in parts) m.Append(p);
            m.Vertices.CombineIdentical(true, true);
            m.Faces.ConvertQuadsToTriangles();
            m.Compact();
            m.Normals.ComputeNormals();
            var te = m.TopologyEdges; var tv = m.TopologyVertices;
            var seen = new HashSet<int>();
            for (int i = 0; i < te.Count; i++)
            {
                if (te.GetConnectedFaces(i).Length != 1) continue;
                var pr = te.GetTopologyVertices(i);
                foreach (int t in new[] { pr.I, pr.J })
                    foreach (int mv in tv.MeshVertexIndices(t))
                        if (seen.Add(mv)) anchors.Add(m.Vertices[mv]);
            }
            return m;
        }

        /// <summary>
        /// Force-density catenary relax (Schek 1974 via TnaForceDensity3D): hang
        /// the net from the anchors under loadZ, then optionally invert across the
        /// support plane (VaultFromHang) = the compression vault. anchors empty ->
        /// all naked-boundary vertices are fixed.
        /// </summary>
        public static Mesh CatenaryRelax(Mesh net, IList<Point3d> anchors, double q, double loadZ,
                                         bool invert, double anchorTol,
                                         out double rise, out double lateral, out int anchorCount)
        {
            rise = 0; lateral = 0; anchorCount = 0;
            if (net == null || net.Vertices.Count < 4) return null;
            var m = net.DuplicateMesh();
            m.Vertices.CombineIdentical(true, true);
            m.Faces.ConvertQuadsToTriangles();
            m.Compact();
            int n = m.Vertices.Count;
            var nodes = new List<Point3d>(n);
            for (int i = 0; i < n; i++) nodes.Add(m.Vertices[i]);

            // fixed set: explicit anchor points (proximity) or naked boundary
            var isFixed = new bool[n];
            if (anchors != null && anchors.Count > 0)
            {
                double t2 = Math.Max(1e-6, anchorTol) * Math.Max(1e-6, anchorTol);
                for (int i = 0; i < n; i++)
                    foreach (var ap in anchors)
                        if (nodes[i].DistanceToSquared(ap) <= t2) { isFixed[i] = true; break; }
            }
            else
            {
                var te0 = m.TopologyEdges; var tv0 = m.TopologyVertices;
                for (int i = 0; i < te0.Count; i++)
                {
                    if (te0.GetConnectedFaces(i).Length != 1) continue;
                    var pr = te0.GetTopologyVertices(i);
                    foreach (int t in new[] { pr.I, pr.J })
                        foreach (int mv in tv0.MeshVertexIndices(t)) isFixed[mv] = true;
                }
            }
            foreach (bool bf in isFixed) if (bf) anchorCount++;
            if (anchorCount < 3) return null;

            var te = m.TopologyEdges; var tv = m.TopologyVertices;
            var edges = new List<int[]>(te.Count);
            var qs = new List<double>(te.Count);
            for (int i = 0; i < te.Count; i++)
            {
                var pr = te.GetTopologyVertices(i);
                edges.Add(new[] { tv.MeshVertexIndices(pr.I)[0], tv.MeshVertexIndices(pr.J)[0] });
                qs.Add(Math.Max(1e-6, q));
            }
            var lz = new List<double>(n);
            for (int i = 0; i < n; i++) lz.Add(loadZ);

            var r = TnaForceDensity3D.Solve(nodes, isFixed, edges, qs, null, null, lz);
            Point3d[] pos = invert ? TnaForceDensity3D.VaultFromHang(r) : r.Positions;
            rise = r.CrownRise; lateral = r.LateralShift;

            var outMesh = m.DuplicateMesh();
            for (int i = 0; i < n; i++) outMesh.Vertices.SetVertex(i, pos[i]);
            outMesh.Normals.ComputeNormals();
            return outMesh;
        }

        /// <summary>Control mesh -> SubD -> smooth subdivided mesh (density 1-4).</summary>
        public static Mesh SubDVault(Mesh control, int density, out string note)
        {
            note = "";
            if (control == null || control.Faces.Count == 0) return null;
            density = Math.Max(1, Math.Min(4, density));
            var sd = SubD.CreateFromMesh(control);
            if (sd == null) { note = "SubD.CreateFromMesh failed (non-manifold control?)"; return null; }
            var m = Mesh.CreateFromSubD(sd, density);
            if (m == null) { note = "Mesh.CreateFromSubD failed"; return null; }
            m.Faces.ConvertQuadsToTriangles();
            m.Vertices.CombineIdentical(true, true);
            m.Compact();
            m.Normals.ComputeNormals();
            note = "density " + density;
            return m;
        }
    }
}
