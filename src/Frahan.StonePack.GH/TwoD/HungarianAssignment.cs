#nullable disable
using System;

namespace Frahan.GH.TwoD;

/// <summary>
/// O(n^3) Kuhn-Munkres / Hungarian-method solver for square cost matrices.
/// Returns the minimising assignment as an int[] where result[i] = j means
/// row i is assigned to column j.
///
/// Pure-managed, allocation-bounded, net48-compatible. Based on the
/// classical "shortest augmenting path" formulation (Bourgeois-Lassalle
/// 1971), which is the standard textbook implementation and runs without
/// heap allocations beyond the cost-augment vectors.
/// </summary>
internal static class HungarianAssignment
{
    /// <summary>
    /// Solve a square assignment problem.
    /// </summary>
    /// <param name="cost">N×N cost matrix; cost[i,j] = cost of assigning row i to column j.</param>
    /// <returns>assignment[] of length N; assignment[i] = j or -1 if infeasible.</returns>
    public static int[] Solve(double[,] cost)
    {
        if (cost == null) throw new ArgumentNullException(nameof(cost));
        int n = cost.GetLength(0);
        if (cost.GetLength(1) != n)
            throw new ArgumentException("Cost matrix must be square.", nameof(cost));
        if (n == 0) return new int[0];

        // u[i] = potential of row i, v[j] = potential of column j.
        // matchRow[j] = which row is currently matched to column j (1-based, 0 = none).
        // p[j] = predecessor in the alternating tree ending at column j.
        var u = new double[n + 1];
        var v = new double[n + 1];
        var matchRow = new int[n + 1];
        var p = new int[n + 1];

        for (int i = 1; i <= n; i++)
        {
            matchRow[0] = i;
            int j0 = 0;
            var minv = new double[n + 1];
            var used = new bool[n + 1];
            for (int k = 0; k <= n; k++) { minv[k] = double.PositiveInfinity; used[k] = false; }

            do
            {
                used[j0] = true;
                int i0 = matchRow[j0];
                double delta = double.PositiveInfinity;
                int j1 = -1;
                for (int j = 1; j <= n; j++)
                {
                    if (used[j]) continue;
                    var cur = cost[i0 - 1, j - 1] - u[i0] - v[j];
                    if (cur < minv[j]) { minv[j] = cur; p[j] = j0; }
                    if (minv[j] < delta) { delta = minv[j]; j1 = j; }
                }
                if (j1 < 0) return ResultMinusOne(n);

                for (int j = 0; j <= n; j++)
                {
                    if (used[j]) { u[matchRow[j]] += delta; v[j] -= delta; }
                    else minv[j] -= delta;
                }
                j0 = j1;
            } while (matchRow[j0] != 0);

            do
            {
                int j1 = p[j0];
                matchRow[j0] = matchRow[j1];
                j0 = j1;
            } while (j0 != 0);
        }

        var result = new int[n];
        for (int j = 1; j <= n; j++)
        {
            int i = matchRow[j];
            if (i >= 1 && i <= n) result[i - 1] = j - 1;
        }
        return result;
    }

    private static int[] ResultMinusOne(int n)
    {
        var r = new int[n];
        for (int i = 0; i < n; i++) r[i] = -1;
        return r;
    }
}
