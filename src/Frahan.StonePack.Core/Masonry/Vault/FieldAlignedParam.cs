#nullable disable
using System;
using Frahan.Masonry.Solvers;
using Rhino.Geometry;

namespace Frahan.Masonry.Vault
{
    // =========================================================================
    // FieldAlignedParam — Stage B of the thrust-following remesher: a cotangent-
    // Poisson parametrization (u, v) whose gradients follow the combed cross-field,
    //     min_u  Σ_f A_f |∇u_f - ρ·E1_f|²      (and v with ρ·E2_f),   ρ = 1/edgeLen.
    // Euler-Lagrange gives the PSD cotangent stiffness  L u = b, solved by dense
    // Cholesky (DenseLinAlg). The integer grid u,v ∈ ℤ is then extracted in Stage C.
    // Spec: outputs/2026-06-30/thrust_remesh/HANDOFF_IMPLEMENTATION.md §3.
    //
    // THIS is Stage B1 (no seam): correct for a mesh with NO interior singularity
    // (single chart). With an interior cone the (u,v) is multivalued; SolveWithSeam
    // (Stage B2) cuts the cone to a disk first. The field E1 MUST be combed (A.5).
    // =========================================================================
    public sealed class FieldAlignedParamResult
    {
        public double[] U, V;          // per-vertex parametrization
        public int PinnedVertex;
        public bool Ok;
        public double Residual;        // Σ A_f|∇u_f-X1|² normalised by Σ A_f|X1|² (0 = perfect field-follow)
        public string Message = "";
    }

    public static class FieldAlignedParam
    {
        /// <summary>
        /// triMesh must be a welded TRIANGLE mesh; E1/N parallel to its vertices (combed,
        /// Stage A.5). edgeLen sets the grid spacing (ρ = 1/edgeLen).
        /// </summary>
        public static FieldAlignedParamResult Solve(Mesh triMesh, Vector3d[] E1, Vector3d[] N, double edgeLen)
        {
            int nv = triMesh.Vertices.Count;
            var P = new Point3d[nv];
            for (int i = 0; i < nv; i++) P[i] = triMesh.Vertices[i];
            int nf = triMesh.Faces.Count;
            double rho = edgeLen > 1e-9 ? 1.0 / edgeLen : 1.0;

            var L = new double[nv, nv];
            var b1 = new double[nv]; var b2 = new double[nv];
            var X1f = new Vector3d[nf]; var gStore = new Vector3d[nf][]; var AStore = new double[nf]; var vStore = new int[nf][];

            for (int f = 0; f < nf; f++)
            {
                var mf = triMesh.Faces[f];
                int[] t = { mf.A, mf.B, mf.C };
                Vector3d n = Vector3d.CrossProduct(P[t[1]] - P[t[0]], P[t[2]] - P[t[0]]);
                double A = 0.5 * n.Length; if (A < 1e-12) continue; n.Unitize();

                var g = new[]
                {
                    Vector3d.CrossProduct(n, P[t[2]] - P[t[1]]) / (2 * A),   // ∇φ toward vertex 0
                    Vector3d.CrossProduct(n, P[t[0]] - P[t[2]]) / (2 * A),
                    Vector3d.CrossProduct(n, P[t[1]] - P[t[0]]) / (2 * A),
                };
                Vector3d e1f = E1[t[0]] + E1[t[1]] + E1[t[2]]; e1f -= (e1f * n) * n;
                if (e1f.Length > 1e-9) e1f.Unitize(); else { e1f = g[0]; e1f.Unitize(); }
                Vector3d e2f = Vector3d.CrossProduct(n, e1f);
                Vector3d X1 = rho * e1f, X2 = rho * e2f;

                for (int a = 0; a < 3; a++)
                {
                    b1[t[a]] += A * (g[a] * X1); b2[t[a]] += A * (g[a] * X2);
                    for (int bb = 0; bb < 3; bb++) L[t[a], t[bb]] += A * (g[a] * g[bb]);
                }
                X1f[f] = X1; gStore[f] = g; AStore[f] = A; vStore[f] = t;
            }

            // pin one vertex to fix the Neumann null space (u_p = 0)
            int p = 0;
            for (int j = 0; j < nv; j++) { L[p, j] = 0; L[j, p] = 0; }
            L[p, p] = 1; b1[p] = 0; b2[p] = 0;

            var u = new double[nv]; var v = new double[nv];
            bool ok1 = DenseLinAlg.CholeskySolve((double[,])L.Clone(), b1, u, 1e-9);
            bool ok2 = DenseLinAlg.CholeskySolve(L, b2, v, 1e-9);

            // field-follow residual on u (how well ∇u tracks ρ·E1)
            double num = 0, den = 0;
            for (int f = 0; f < nf; f++)
            {
                if (vStore[f] == null) continue;
                var g = gStore[f]; var t = vStore[f];
                Vector3d gu = u[t[0]] * g[0] + u[t[1]] * g[1] + u[t[2]] * g[2];
                Vector3d diff = gu - X1f[f];
                num += AStore[f] * diff.SquareLength;
                den += AStore[f] * X1f[f].SquareLength;
            }

            return new FieldAlignedParamResult
            {
                U = u, V = v, PinnedVertex = p, Ok = ok1 && ok2,
                Residual = den > 1e-12 ? num / den : 0.0,
                Message = (ok1 && ok2) ? "ok" : "cholesky failed (system not SPD?)",
            };
        }
    }
}
