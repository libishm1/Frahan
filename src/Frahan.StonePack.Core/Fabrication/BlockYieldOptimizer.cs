#nullable disable
using System;
using System.Text;
using Rhino.Geometry;

namespace Frahan.Core.Fabrication;

// =============================================================================
// BlockYieldOptimizer -- maximise the usable-block yield when a raw quarry block
// is sawn into rectangular (right-prism) product blocks. Off-cut waste at the far
// faces is the loss; the levers are (1) which product dimension maps to which raw
// axis (6 permutations), and (2) the exact block size WITHIN a tolerance band, so
// the grid tiles the raw extent with little or no trim.
//
// Per axis of raw length L, target size s (+/- tol), saw kerf k:
//   for each block count n: the largest block that fits n cuts is
//     size_n = (L - (n-1)k) / n ;   usable_n = n * size_n
//   pick the n (and size in [s-tol, s+tol]) that maximises the used length.
// Doing this per axis and choosing the best axis-permutation maximises the
// volume yield = (n_x n_y n_z * block volume) / raw volume.
//
// A larger tolerance recovers more yield (the size flexes to divide L exactly);
// zero tolerance falls back to fixed-size cutting with whatever trim remains.
//
// Pure managed arithmetic (Rhino value types), deterministic, headless-testable.
// The GH layer orients the raw block into the cut frame, calls Optimize, and
// builds the block geometry.
// =============================================================================

/// <summary>Yield-optimal rectangular cut plan for one raw block.</summary>
public sealed class BlockYieldResult
{
    /// <summary>Blocks per axis (x,y,z in the cut frame).</summary>
    public int[] Count = new int[3];
    /// <summary>Optimised block size per axis (within tolerance).</summary>
    public double[] Size = new double[3];
    /// <summary>Raw extent per axis.</summary>
    public double[] Length = new double[3];
    /// <summary>Used extent per axis (n*size); the remainder is off-cut waste.</summary>
    public double[] Used = new double[3];
    /// <summary>Which target dimension index maps to each raw axis (the winning permutation).</summary>
    public int[] Perm = { 0, 1, 2 };
    public int TotalBlocks;
    public double RawVolume, BlockVolume, UsableVolume, Yield, Waste;
    public string Report = "";
}

public static class BlockYieldOptimizer
{
    /// <summary>
    /// Yield-optimal cut plan for a raw block of extents (lx,ly,lz) sawn into
    /// blocks near <paramref name="target"/> with a +/- <paramref name="tolFrac"/>
    /// fractional size band and saw <paramref name="kerf"/>. Tries all 6 axis
    /// permutations of the target and keeps the highest-yield one.
    /// </summary>
    public static BlockYieldResult Optimize(double lx, double ly, double lz, Vector3d target, double tolFrac, double kerf)
    {
        double[] L = { Math.Abs(lx), Math.Abs(ly), Math.Abs(lz) };
        double[] tgt = { Math.Abs(target.X), Math.Abs(target.Y), Math.Abs(target.Z) };
        if (tolFrac < 0) tolFrac = 0;
        if (kerf < 0) kerf = 0;

        int[][] perms =
        {
            new[]{0,1,2}, new[]{0,2,1}, new[]{1,0,2}, new[]{1,2,0}, new[]{2,0,1}, new[]{2,1,0}
        };
        BlockYieldResult best = null;
        foreach (var p in perms)
        {
            var r = new BlockYieldResult { Length = (double[])L.Clone(), Perm = (int[])p.Clone() };
            for (int a = 0; a < 3; a++)
            {
                double s = tgt[p[a]];
                Axis(L[a], s, tolFrac * s, kerf, out int n, out double sz, out double u);
                r.Count[a] = n; r.Size[a] = sz; r.Used[a] = u;
            }
            r.TotalBlocks = r.Count[0] * r.Count[1] * r.Count[2];
            r.BlockVolume = r.Size[0] * r.Size[1] * r.Size[2];
            r.RawVolume = L[0] * L[1] * L[2];
            r.UsableVolume = r.TotalBlocks * r.BlockVolume;
            r.Yield = r.RawVolume > 1e-12 ? r.UsableVolume / r.RawVolume : 0;
            r.Waste = 1.0 - r.Yield;
            if (best == null || r.Yield > best.Yield) best = r;
        }
        best.Report = BuildReport(best);
        return best;
    }

    // Best block count + size for one axis: maximise used = n*size, size in [s-tol, s+tol].
    private static void Axis(double L, double s, double tol, double kerf, out int n, out double size, out double used)
    {
        n = 0; size = 0; used = 0;
        double smin = Math.Max(1e-6, s - tol), smax = s + tol;
        if (L < smin) return;
        int nmax = (int)Math.Floor((L + kerf) / (smin + kerf) + 1e-9);
        for (int k = 1; k <= nmax; k++)
        {
            double maxSize = (L - (k - 1) * kerf) / k;   // largest block for k blocks + kerf gaps
            if (maxSize < smin - 1e-9) continue;
            double sz = Math.Min(smax, maxSize);
            double u = k * sz;
            if (u > used) { used = u; size = sz; n = k; }
        }
    }

    private static string BuildReport(BlockYieldResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Block-yield optimisation (rectangular cutting, waste-minimising).");
        char[] ax = { 'x', 'y', 'z' };
        for (int a = 0; a < 3; a++)
            sb.AppendLine($"  {ax[a]}: raw {r.Length[a]:0.###} -> {r.Count[a]} x {r.Size[a]:0.###} (used {r.Used[a]:0.###}, trim {r.Length[a] - r.Used[a]:0.###})");
        sb.AppendLine($"  blocks: {r.Count[0]} x {r.Count[1]} x {r.Count[2]} = {r.TotalBlocks}  @ {r.Size[0]:0.###} x {r.Size[1]:0.###} x {r.Size[2]:0.###}");
        sb.AppendLine($"  volume: block {r.BlockVolume:0.###}  usable {r.UsableVolume:0.###}  raw {r.RawVolume:0.###}");
        sb.AppendLine($"  YIELD = {r.Yield * 100:0.#}%   waste = {r.Waste * 100:0.#}%");
        if (r.TotalBlocks == 0) sb.AppendLine("  ! No whole block fits: raw block smaller than the minimum size band.");
        else if (r.Waste > 0.25) sb.AppendLine("  ! High waste (>25%): widen the tolerance or pick a size that divides the raw extents.");
        return sb.ToString().TrimEnd();
    }
}
