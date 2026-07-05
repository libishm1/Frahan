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

    /// <summary>
    /// Stage A.5 output. Refine() combs + smooths the field IN PLACE (updates the
    /// ThrustFieldResult E1/E2) and reports the per-triangle 4-RoSy singularity index
    /// and the interior cones -- the gate that tells Stage B it must cut a seam.
    /// </summary>
    public sealed class FieldRefineResult
    {
        public int[] SingularIndex;                          // per triangle: 0 regular, 1 valence-3 cone, 3 valence-5
        public List<int> InteriorSingularFaces = new List<int>();
        public bool HasInteriorSingularity;
        public int TriangleCount;
        public int SmoothSweeps;
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

        // =====================================================================
        // Stage A.5 — comb + smooth + singularity index (extends the field).
        // Spec: outputs/2026-06-30/thrust_remesh/HANDOFF_IMPLEMENTATION.md §2.
        //   - comb: BFS over the 1-ring, rotate each e1 into its parent's 4-RoSy
        //     orbit so it is a single consistent representative;
        //   - smooth: Gauss-Seidel on the aligned neighbour reps (umbilic cleanup);
        //   - singularity index: per triangle, sum of the mod-90 matches, mod 4.
        //     0 = regular, 1 = valence-3 cone (+1/4), 3 = valence-5 (-1/4).
        // The interior cones are the flag Stage B seams on (the 3-prong -> 1 hub cone).
        // Writes the refined e1/e2 back into `field`.
        // =====================================================================
        public static FieldRefineResult Refine(ThrustFieldResult field, Mesh mesh, int smoothSweeps = 4)
        {
            int nv = field.E1.Length;
            var N = field.Normal;

            // triangle faces + 1-ring adjacency
            var m = mesh.DuplicateMesh(); m.Faces.ConvertQuadsToTriangles(); m.Compact();
            int nf = m.Faces.Count;
            var F = new int[nf][];
            for (int f = 0; f < nf; f++) { var mf = m.Faces[f]; F[f] = new[] { mf.A, mf.B, mf.C }; }
            var VAdj = new List<int>[nv];
            for (int i = 0; i < nv; i++) VAdj[i] = new List<int>();
            var seen = new HashSet<long>();
            for (int f = 0; f < nf; f++)
            {
                int[] tri = F[f];
                for (int p = 0; p < 3; p++)
                {
                    int a = tri[p], b = tri[(p + 1) % 3];
                    if (a == b || a < 0 || b < 0 || a >= nv || b >= nv) continue;
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (seen.Add(key)) { VAdj[a].Add(b); VAdj[b].Add(a); }
                }
            }

            // boundary faces (touch a naked edge): interior cones are what we gate on
            var boundaryFace = new bool[nf];
            var te = m.TopologyEdges;
            for (int e = 0; e < te.Count; e++)
            {
                var cf = te.GetConnectedFaces(e);
                if (cf != null && cf.Length == 1 && cf[0] >= 0 && cf[0] < nf) boundaryFace[cf[0]] = true;
            }

            // --- comb: BFS, rotate each e1 into its parent's orbit ---
            var visited = new bool[nv];
            var comb = (Vector3d[])field.E1.Clone();
            var q = new Queue<int>();
            for (int s = 0; s < nv; s++)
            {
                if (visited[s]) continue;
                visited[s] = true; q.Enqueue(s);
                while (q.Count > 0)
                {
                    int i = q.Dequeue();
                    foreach (int j in VAdj[i])
                        if (!visited[j])
                        {
                            Vector3d r = comb[i]; r -= (r * N[j]) * N[j];
                            if (r.Length < 1e-9) r = field.E1[j];
                            r.Unitize();
                            comb[j] = Rot(field.E1[j], N[j], MatchK(r, field.E1[j], N[j]));
                            if (comb[j].Length > 1e-12) comb[j].Unitize();
                            visited[j] = true; q.Enqueue(j);
                        }
                }
            }
            field.E1 = comb;

            // --- smooth: Gauss-Seidel on the aligned neighbour reps ---
            for (int sweep = 0; sweep < smoothSweeps; sweep++)
                for (int i = 0; i < nv; i++)
                {
                    if (field.IsBoundary[i]) continue;
                    Vector3d acc = Vector3d.Zero;
                    foreach (int j in VAdj[i])
                    {
                        Vector3d ej = field.E1[j]; ej -= (ej * N[i]) * N[i];
                        if (ej.Length < 1e-9) continue; ej.Unitize();
                        int k = MatchK(field.E1[i], ej, N[i]);
                        acc += Rot(ej, N[i], k);
                    }
                    acc -= (acc * N[i]) * N[i];
                    if (acc.Length > 1e-9) { field.E1[i] = acc; field.E1[i].Unitize(); }
                }
            for (int i = 0; i < nv; i++) { field.E2[i] = Vector3d.CrossProduct(N[i], field.E1[i]); field.E2[i].Unitize(); }

            // --- per-triangle 4-RoSy singularity index ---
            var res = new FieldRefineResult { SingularIndex = new int[nf], TriangleCount = nf, SmoothSweeps = smoothSweeps };
            for (int f = 0; f < nf; f++)
            {
                int i = F[f][0], j = F[f][1], k = F[f][2];
                if (i >= nv || j >= nv || k >= nv) continue;
                int sum = MatchK(Pln(field.E1[j], N[i]), field.E1[i], N[i])
                        + MatchK(Pln(field.E1[k], N[j]), field.E1[j], N[j])
                        + MatchK(Pln(field.E1[i], N[k]), field.E1[k], N[k]);
                int idx = ((sum % 4) + 4) % 4;
                res.SingularIndex[f] = idx;
                if (idx != 0 && !boundaryFace[f]) res.InteriorSingularFaces.Add(f);
            }
            res.HasInteriorSingularity = res.InteriorSingularFaces.Count > 0;
            return res;
        }

        // best 90*k rotation of the cross {e1} to align with reference dir r (in n's plane)
        private static int MatchK(Vector3d r, Vector3d e1, Vector3d n)
        {
            Vector3d e2 = Vector3d.CrossProduct(n, e1);
            double d0 = r * e1, d1 = r * e2, d2 = -d0, d3 = -d1;
            int k = 0; double best = d0;
            if (d1 > best) { best = d1; k = 1; }
            if (d2 > best) { best = d2; k = 2; }
            if (d3 > best) { k = 3; }
            return k;
        }

        // R^k e1, where R v = n x v (90 deg about n)
        private static Vector3d Rot(Vector3d e1, Vector3d n, int k)
        {
            Vector3d v = e1; int kk = k & 3;
            for (int i = 0; i < kk; i++) v = Vector3d.CrossProduct(n, v);
            return v;
        }

        // project v into n's tangent plane, unit
        private static Vector3d Pln(Vector3d v, Vector3d n)
        {
            var r = v - (v * n) * n; if (r.Length > 1e-12) r.Unitize(); return r;
        }
    }
}
