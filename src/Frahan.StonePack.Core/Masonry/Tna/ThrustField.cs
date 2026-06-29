#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Masonry.Tna
{
    // =========================================================================
    // ThrustField — Stage A of thrust-following field-aligned remeshing (see
    // outputs/2026-06-30/thrust_remesh/DERIVATION.md). Builds a per-vertex 4-RoSy
    // cross-field {e1, e2} from a TNA force network: e1 = principal compression
    // (thrust) direction, e2 = transverse. A force network is the discrete dual of
    // a membrane stress field, so the vertex stress tensor
    //     sigma_v = sum_{e in v}  w_e (t_e (x) t_e)        w_e = |N_e| = q_e L_e
    // (t_e projected into v's tangent plane) has eigenvectors = the principal stress
    // = thrust trajectories. Only the eigen-DIRECTIONS are used, so the absolute
    // scale of w_e is irrelevant. This field feeds the field-aligned parametrization.
    // =========================================================================
    public sealed class ThrustFieldResult
    {
        public Vector3d[] E1;          // principal thrust direction (unit, in tangent plane)
        public Vector3d[] E2;          // transverse direction (unit, e2 = n x e1)
        public Vector3d[] Normal;      // vertex normal
        public double[] Anisotropy;    // (lambda1 - lambda2)/(lambda1 + lambda2) in [0,1]; 0 = isotropic (field undefined)
        public bool[] IsBoundary;      // naked-edge vertices
    }

    public static class ThrustField
    {
        /// <summary>
        /// Cross-field from a mesh + network edges with per-edge force magnitudes w (= q_e * L_e).
        /// edges = vertex-index pairs into the mesh. w parallel to edges (null = use edge length).
        /// alignBoundary forces e1 along the boundary tangent at naked-edge vertices.
        /// </summary>
        public static ThrustFieldResult Compute(Mesh mesh, IList<int[]> edges, IList<double> w, bool alignBoundary = true)
        {
            int nv = mesh.Vertices.Count;
            mesh.Normals.ComputeNormals();
            var P = new Point3d[nv]; for (int i = 0; i < nv; i++) P[i] = mesh.Vertices[i];
            var N = new Vector3d[nv]; for (int i = 0; i < nv; i++) { var nf = mesh.Normals[i]; N[i] = new Vector3d(nf.X, nf.Y, nf.Z); if (N[i].Length < 1e-12) N[i] = Vector3d.ZAxis; N[i].Unitize(); }

            // boundary vertices (naked-edge endpoints) + a boundary tangent each
            var isB = new bool[nv]; var bTan = new Vector3d[nv];
            var te = mesh.TopologyEdges; var tv = mesh.TopologyVertices;
            for (int i = 0; i < te.Count; i++)
            {
                if (te.GetConnectedFaces(i).Length != 1) continue;
                var pr = te.GetTopologyVertices(i);
                foreach (int t in new[] { pr.I, pr.J })
                {
                    foreach (int mv in tv.MeshVertexIndices(t))
                    {
                        isB[mv] = true;
                        var dir = P[tv.MeshVertexIndices(pr.J)[0]] - P[tv.MeshVertexIndices(pr.I)[0]];
                        if (dir.Length > 1e-9) { dir.Unitize(); bTan[mv] += dir; }
                    }
                }
            }

            // tangent-plane stress tensor per vertex, accumulated as [a b; b d] in a local basis
            var b1 = new Vector3d[nv]; var b2 = new Vector3d[nv];
            for (int i = 0; i < nv; i++)
            {
                Vector3d t1 = Vector3d.CrossProduct(N[i], Vector3d.XAxis);
                if (t1.Length < 1e-6) t1 = Vector3d.CrossProduct(N[i], Vector3d.YAxis);
                t1.Unitize(); b1[i] = t1; b2[i] = Vector3d.CrossProduct(N[i], t1); b2[i].Unitize();
            }
            var Saa = new double[nv]; var Sbb = new double[nv]; var Sab = new double[nv];

            for (int e = 0; e < edges.Count; e++)
            {
                int a = edges[e][0], c = edges[e][1];
                double we = (w != null && w.Count == edges.Count) ? Math.Abs(w[e]) : P[a].DistanceTo(P[c]);
                Vector3d d = P[c] - P[a]; if (d.Length < 1e-12) continue; d.Unitize();
                Accumulate(a, d, we, N, b1, b2, Saa, Sbb, Sab);
                Accumulate(c, -d, we, N, b1, b2, Saa, Sbb, Sab);
            }

            var res = new ThrustFieldResult { E1 = new Vector3d[nv], E2 = new Vector3d[nv], Normal = N, Anisotropy = new double[nv], IsBoundary = isB };
            for (int i = 0; i < nv; i++)
            {
                double a = Saa[i], d = Sbb[i], b = Sab[i];
                // eigenvalues of [[a,b],[b,d]]
                double tr = a + d, disc = Math.Sqrt(Math.Max(0.0, (a - d) * (a - d) * 0.25 + b * b));
                double l1 = tr * 0.5 + disc, l2 = tr * 0.5 - disc;
                // eigenvector of the LARGER eigenvalue, in (b1,b2): ∝ (b, l1 - a)
                double ex = b, ey = l1 - a;
                if (Math.Abs(ex) < 1e-12 && Math.Abs(ey) < 1e-12) { ex = 1; ey = 0; }
                double nrm = Math.Sqrt(ex * ex + ey * ey); ex /= nrm; ey /= nrm;
                Vector3d e1 = ex * b1[i] + ey * b2[i]; e1.Unitize();

                if (alignBoundary && isB[i] && bTan[i].Length > 1e-9)
                {
                    var bt = bTan[i]; bt -= (bt * N[i]) * N[i]; if (bt.Length > 1e-9) { bt.Unitize(); e1 = bt; }
                }
                res.E1[i] = e1;
                res.E2[i] = Vector3d.CrossProduct(N[i], e1); res.E2[i].Unitize();
                res.Anisotropy[i] = (l1 + l2) > 1e-12 ? (l1 - l2) / (l1 + l2) : 0.0;
            }
            return res;
        }

        private static void Accumulate(int v, Vector3d d, double we, Vector3d[] N, Vector3d[] b1, Vector3d[] b2,
                                       double[] Saa, double[] Sbb, double[] Sab)
        {
            // project d into v's tangent plane, express in (b1,b2)
            Vector3d t = d - (d * N[v]) * N[v];
            if (t.Length < 1e-12) return; t.Unitize();
            double c = t * b1[v], s = t * b2[v];
            Saa[v] += we * c * c; Sbb[v] += we * s * s; Sab[v] += we * c * s;
        }
    }
}
