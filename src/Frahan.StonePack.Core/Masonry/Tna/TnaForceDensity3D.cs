#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Masonry.Tna
{
    // =========================================================================
    // TnaForceDensity3D — full 3D force-density form-finder (Schek 1974) that
    // handles HORIZONTAL loads, not just vertical. This is the Checkpoint-3
    // increment over the native frahan_tna solver, whose header notes that the
    // horizontal earth-pressure loads px/py are "stored but ... do not enter the
    // height-finding solve directly". Here they do: solving x, y AND z under
    // gravity + Rankine/Jaky lateral earth pressure produces the leaning Park
    // Güell funicular automatically (Gaudí's hanging-chain-with-side-load).
    //
    // Method (Schek force density): with q_e = force/length per branch fixed, the
    // weighted graph Laplacian D = Cᵀ Q C makes nodal equilibrium linear in the
    // coordinates. For each coordinate independently:
    //     D_FF · r_F = p_F − D_FX · r_X
    // (F = free nodes, X = fixed supports, p = applied load in that coordinate).
    // Solved with a dense Cholesky (D_FF is symmetric positive-definite for q>0).
    //
    // The raw solve is a HANGING net (sags under gravity). VaultFromHang() mirrors
    // it about the support datum to give the inverted, compression-only vault.
    // =========================================================================
    public sealed class ForceDensity3DResult
    {
        public Point3d[] Positions;       // solved positions for ALL nodes (fixed unchanged)
        public bool[] IsFixed;
        public int FreeCount;
        public double CrownRise;          // max |z - support datum| over free nodes
        public double LateralShift;       // max horizontal drift of free nodes from their plan x,y
    }

    public static class TnaForceDensity3D
    {
        // Dense Cholesky solve of a symmetric positive-definite system A x = b.
        private static double[] CholeskySolve(double[,] A, double[] b)
        {
            int n = b.Length;
            var L = new double[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j <= i; j++)
                {
                    double s = A[i, j];
                    for (int k = 0; k < j; k++) s -= L[i, k] * L[j, k];
                    if (i == j) { if (s <= 0) s = 1e-12; L[i, i] = Math.Sqrt(s); }
                    else L[i, j] = s / L[j, j];
                }
            var y = new double[n];
            for (int i = 0; i < n; i++) { double s = b[i]; for (int k = 0; k < i; k++) s -= L[i, k] * y[k]; y[i] = s / L[i, i]; }
            var x = new double[n];
            for (int i = n - 1; i >= 0; i--) { double s = y[i]; for (int k = i + 1; k < n; k++) s -= L[k, i] * x[k]; x[i] = s / L[i, i]; }
            return x;
        }

        /// <summary>
        /// Solve the 3D force-density network. nodes/isFixed length = nNodes;
        /// edges = [i,j] index pairs; q = per-edge force density (>0); loadX/Y/Z =
        /// per-node applied load (gravity in Z negative, earth pressure in X/Y).
        /// </summary>
        public static ForceDensity3DResult Solve(
            IList<Point3d> nodes, IList<bool> isFixed, IList<int[]> edges, IList<double> q,
            IList<double> loadX, IList<double> loadY, IList<double> loadZ)
        {
            int n = nodes.Count;
            // map free nodes to a compact index
            var freeIndex = new int[n];
            int nf = 0;
            for (int i = 0; i < n; i++) freeIndex[i] = isFixed[i] ? -1 : nf++;

            // weighted Laplacian D (dense n×n)
            var D = new double[n, n];
            for (int e = 0; e < edges.Count; e++)
            {
                int a = edges[e][0], b = edges[e][1];
                double qe = q[e]; if (qe < 1e-9) qe = 1e-9;
                D[a, a] += qe; D[b, b] += qe; D[a, b] -= qe; D[b, a] -= qe;
            }

            // assemble D_FF and the three RHS (x,y,z)
            var Dff = new double[nf, nf];
            var rhsX = new double[nf]; var rhsY = new double[nf]; var rhsZ = new double[nf];
            for (int i = 0; i < n; i++)
            {
                int fi = freeIndex[i];
                if (fi < 0) continue;
                rhsX[fi] = loadX != null ? loadX[i] : 0.0;
                rhsY[fi] = loadY != null ? loadY[i] : 0.0;
                rhsZ[fi] = loadZ != null ? loadZ[i] : 0.0;
                for (int j = 0; j < n; j++)
                {
                    double d = D[i, j];
                    if (d == 0.0) continue;
                    int fj = freeIndex[j];
                    if (fj >= 0) Dff[fi, fj] = d;                 // free-free block
                    else { rhsX[fi] -= d * nodes[j].X; rhsY[fi] -= d * nodes[j].Y; rhsZ[fi] -= d * nodes[j].Z; }
                }
            }

            double[] sx = CholeskySolve((double[,])Dff.Clone(), rhsX);
            double[] sy = CholeskySolve((double[,])Dff.Clone(), rhsY);
            double[] sz = CholeskySolve((double[,])Dff.Clone(), rhsZ);

            var pos = new Point3d[n];
            double datum = 0.0; int sup = 0;
            for (int i = 0; i < n; i++) if (isFixed[i]) { datum += nodes[i].Z; sup++; }
            if (sup > 0) datum /= sup;

            double rise = 0, lat = 0;
            for (int i = 0; i < n; i++)
            {
                int fi = freeIndex[i];
                if (fi < 0) { pos[i] = nodes[i]; continue; }
                var p = new Point3d(sx[fi], sy[fi], sz[fi]);
                pos[i] = p;
                double dz = Math.Abs(p.Z - datum); if (dz > rise) rise = dz;
                double dl = Math.Sqrt((p.X - nodes[i].X) * (p.X - nodes[i].X) + (p.Y - nodes[i].Y) * (p.Y - nodes[i].Y));
                if (dl > lat) lat = dl;
            }

            return new ForceDensity3DResult { Positions = pos, IsFixed = ToArray(isFixed), FreeCount = nf, CrownRise = rise, LateralShift = lat };
        }

        /// <summary>
        /// Invert the hanging net into the compression vault by reflecting free
        /// nodes across the PLANE through the supports (not a horizontal datum).
        /// This is correct for unequal springer heights (e.g. Park Güell: high
        /// retaining wall vs low colonnade), where a horizontal-datum mirror would
        /// flip the node next to the high springer down and break the arch.
        /// </summary>
        public static Point3d[] VaultFromHang(ForceDensity3DResult r)
        {
            var sup = new List<Point3d>();
            for (int i = 0; i < r.Positions.Length; i++) if (r.IsFixed[i]) sup.Add(r.Positions[i]);

            Plane pl = Plane.WorldXY;
            bool ok = sup.Count >= 3 && Plane.FitPlaneToPoints(sup, out pl) == PlaneFitResult.Success;
            if (!ok)
            {
                double datum = 0.0; foreach (var s in sup) datum += s.Z;
                if (sup.Count > 0) datum /= sup.Count;
                pl = new Plane(new Point3d(0, 0, datum), Vector3d.ZAxis);
            }
            if (pl.Normal.Z < 0) pl.Flip(); // normal points up so the vault bulges above the supports

            var v = new Point3d[r.Positions.Length];
            for (int i = 0; i < r.Positions.Length; i++)
            {
                var p = r.Positions[i];
                if (r.IsFixed[i]) { v[i] = p; continue; }
                double d = pl.DistanceTo(p);           // signed distance (negative below the plane)
                v[i] = p - 2.0 * d * pl.Normal;         // reflect across the support plane
            }
            return v;
        }

        private static bool[] ToArray(IList<bool> l) { var a = new bool[l.Count]; for (int i = 0; i < l.Count; i++) a[i] = l[i]; return a; }
    }
}
