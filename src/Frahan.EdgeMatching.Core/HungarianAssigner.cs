#nullable disable
using System;

namespace Frahan.EdgeMatching;

// =============================================================================
// HungarianAssigner -- bipartite assignment solver for the M x N cost matrix
// that drives Component D (Template Panel Match, D5F10007), Component D3D
// (Template Block Match, D5F1000B), and the Voussoir Stone Matcher
// (proposed). See wiki/specs/frahan_design_philosophy.md SS10.6 -- the
// SAME algorithm serves voussoir/template top-down assignment in 2D and 3D.
//
// Implements Kuhn's Hungarian Method (1955) -- the optimal O(N^3) algorithm
// for the bipartite-graph minimum-cost assignment problem.
//
// Reference: H.W. Kuhn, "The Hungarian Method for the Assignment Problem,"
//            Naval Research Logistics Quarterly, vol. 2, pp. 83-97, 1955.
//            Public domain combinatorial algorithm.
//
// Implementation: standard square-matrix Hungarian (Jonker-Volgenant variant
// could be added later for speed; square Hungarian is O(N^3) which is fine
// for Frahan-scale workloads M, N <= 200 per philosophy doc SS11.3 fallback
// to Beam beyond 40k cells).
//
// Rectangular cost matrices are padded with a large sentinel before running
// the square algorithm. Padded rows / columns yield (UNASSIGNED = -1) in the
// output assignment.
// =============================================================================

public static class HungarianAssigner
{
    /// <summary>Sentinel cost meaning "no valid assignment" -- the algorithm
    /// will never select these unless the row/column is genuinely empty.</summary>
    public const double Infeasible = 1e18;

    /// <summary>Per-row unassigned sentinel in the output array.</summary>
    public const int Unassigned = -1;

    /// <summary>
    /// Solve the rectangular assignment problem on a row-major cost matrix.
    /// Returns an int[rows] array where result[i] is the column assigned to
    /// row i, or <see cref="Unassigned"/> if row i had no feasible column.
    /// </summary>
    /// <param name="cost">Row-major cost matrix; cost[i * cols + j] = c(i, j).</param>
    /// <param name="rows">Number of rows (M, the "panel inventory" or "slot count").</param>
    /// <param name="cols">Number of cols (N, the "template cells" or "block candidates").</param>
    /// <returns>Per-row column assignment, length = rows.</returns>
    public static int[] Solve(double[] cost, int rows, int cols)
    {
        if (cost == null) throw new ArgumentNullException(nameof(cost));
        if (rows < 0 || cols < 0)
            throw new ArgumentException("rows and cols must be non-negative");
        if (cost.Length != rows * cols)
            throw new ArgumentException("cost length must equal rows * cols");

        int n = Math.Max(rows, cols);
        if (n == 0)
            return Array.Empty<int>();

        // Data-dependent big-M sentinel for padded / infeasible cells (V3 review T2/T3 fix).
        // A fixed 1e18 here poisons the dual potentials u/v (they accumulate it, lines below),
        // so once a potential approaches 1e18 the real O(1) feasible costs fall below the
        // float64 relative ulp and can be mis-ranked on dense-infeasible inputs. Scale the
        // sentinel to the data instead: strictly larger in magnitude than any complete feasible
        // assignment (n costs, each |.| <= maxAbs), but bounded, so the duals stay well-scaled.
        double maxAbs = 0.0;
        for (int i = 0; i < rows; i++)
        for (int j = 0; j < cols; j++)
        {
            double cij = cost[i * cols + j];
            if (cij < Infeasible) maxAbs = Math.Max(maxAbs, Math.Abs(cij));
        }
        double bigM = (maxAbs + 1.0) * (n + 1);

        // Pad to square with the bounded big-M; caller-marked infeasible cells (>= Infeasible)
        // are also mapped to big-M so they never poison the duals.
        var c = new double[n * n];
        for (int i = 0; i < n; i++)
        for (int j = 0; j < n; j++)
        {
            double cij = (i < rows && j < cols) ? cost[i * cols + j] : double.PositiveInfinity;
            c[i * n + j] = (cij < Infeasible) ? cij : bigM;
        }

        // Jonker-Volgenant-style dual potentials. Standard O(n^3) Hungarian.
        var u = new double[n + 1];
        var v = new double[n + 1];
        var p = new int[n + 1];
        var way = new int[n + 1];
        var minv = new double[n + 1];
        var used = new bool[n + 1];

        for (int i = 1; i <= n; i++)
        {
            p[0] = i;
            int j0 = 0;
            for (int j = 0; j <= n; j++) { minv[j] = double.MaxValue; used[j] = false; }
            do
            {
                used[j0] = true;
                int i0 = p[j0];
                double delta = double.MaxValue;
                int j1 = -1;
                for (int j = 1; j <= n; j++)
                {
                    if (used[j]) continue;
                    double cur = c[(i0 - 1) * n + (j - 1)] - u[i0] - v[j];
                    if (cur < minv[j])
                    {
                        minv[j] = cur;
                        way[j] = j0;
                    }
                    if (minv[j] < delta)
                    {
                        delta = minv[j];
                        j1 = j;
                    }
                }
                if (j1 == -1)
                    throw new InvalidOperationException("Hungarian: no augmenting path found");

                for (int j = 0; j <= n; j++)
                {
                    if (used[j]) { u[p[j]] += delta; v[j] -= delta; }
                    else         { minv[j] -= delta; }
                }
                j0 = j1;
            } while (p[j0] != 0);

            do
            {
                int j1 = way[j0];
                p[j0] = p[j1];
                j0 = j1;
            } while (j0 != 0);
        }

        // p[col + 1] = row + 1 -- invert to get per-row assignment.
        var result = new int[rows];
        for (int i = 0; i < rows; i++) result[i] = Unassigned;
        for (int j = 1; j <= n; j++)
        {
            int i = p[j] - 1;
            int col = j - 1;
            // Assigned only if this was a genuinely feasible INPUT cell (check the original
            // cost, not the big-M-substituted c[]).
            if (i < rows && col < cols && cost[i * cols + col] < Infeasible)
                result[i] = col;
        }
        return result;
    }
}
