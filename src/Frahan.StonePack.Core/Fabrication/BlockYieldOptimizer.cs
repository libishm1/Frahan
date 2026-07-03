#nullable disable
using System;
using System.Collections.Generic;
using System.Text;
using Rhino.Geometry;

namespace Frahan.Core.Fabrication;

// =============================================================================
// BlockYieldOptimizer -- maximise the usable-block yield when a raw quarry block
// is sawn into rectangular (right-prism) product blocks, and (optionally) dodge
// internal fractures so the recovered blocks are SOUND, not just geometric.
//
// Geometry lever (always): per axis of raw length L, target size s (+/- tol),
// saw kerf k -- for each block count n the largest block that fits is
//   size_n = (L - (n-1)k) / n ;   usable_n = n * size_n
// pick the n (and size in [s-tol, s+tol]) maximising the used length, then the
// best of the 6 axis-permutations. This tiles the raw extent with least off-cut.
//
// Fracture lever (when fractures are supplied, in the same frame coords with the
// raw block at the origin): a product block straddled by a fracture plane is
// unsound (rejected). The grid ORIGIN is slid within the per-axis trim slack
// (a coarse 3D phase search) to align cut planes with the fractures, so blocks
// fall BETWEEN fractures instead of across them -- the cuts dodge the defects.
// Sound yield = sound-block volume / raw volume.
//
// References: dimension-stone recovery / block-cutting optimisation practice
// (avoid defect-crossing blocks); Palmstrom block-size; guillotine tiling.
//
// Pure managed arithmetic (Rhino value types), deterministic, headless-testable.
// =============================================================================

/// <summary>Yield-optimal (and optionally fracture-dodging) rectangular cut plan for one raw block.</summary>
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
    /// <summary>Grid-origin offset per axis (the fracture-dodging phase).</summary>
    public double[] Phase = new double[3];
    /// <summary>Winning target-dimension permutation (index per raw axis).</summary>
    public int[] Perm = { 0, 1, 2 };
    public int TotalBlocks;
    /// <summary>Per-block soundness, indexed (i*ny + j)*nz + k. Empty if no fractures.</summary>
    public bool[] Sound = new bool[0];
    public int SoundBlocks, FlawedBlocks;
    public double RawVolume, BlockVolume, UsableVolume, Yield, Waste, SoundYield;
    public bool FractureAware;
    public string Report = "";
}

public static class BlockYieldOptimizer
{
    /// <summary>
    /// Yield-optimal cut plan. If <paramref name="fractures"/> (planes in frame coords,
    /// raw block at the origin) are supplied, the grid phase is optimised to minimise
    /// fracture-straddled blocks and the sound yield is reported.
    /// </summary>
    public static BlockYieldResult Optimize(
        double lx, double ly, double lz, Vector3d target, double tolFrac, double kerf,
        IReadOnlyList<Plane> fractures = null, int phaseSteps = 8)
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

        best.SoundYield = best.Yield;
        best.SoundBlocks = best.TotalBlocks;
        best.Sound = new bool[best.TotalBlocks];
        for (int i = 0; i < best.Sound.Length; i++) best.Sound[i] = true;

        if (fractures != null && fractures.Count > 0 && best.TotalBlocks > 0)
        {
            best.FractureAware = true;
            OptimizePhase(best, kerf, fractures, phaseSteps);
        }

        best.Report = BuildReport(best, kerf);
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
            double maxSize = (L - (k - 1) * kerf) / k;
            if (maxSize < smin - 1e-9) continue;
            double sz = Math.Min(smax, maxSize);
            double u = k * sz;
            if (u > used) { used = u; size = sz; n = k; }
        }
    }

    // Slide the grid origin within the per-axis trim slack to minimise fracture-straddled blocks.
    private static void OptimizePhase(BlockYieldResult r, double kerf, IReadOnlyList<Plane> fractures, int steps)
    {
        int nx = r.Count[0], ny = r.Count[1], nz = r.Count[2];
        double sx = r.Size[0], sy = r.Size[1], sz = r.Size[2];
        double[] trim = { r.Length[0] - r.Used[0], r.Length[1] - r.Used[1], r.Length[2] - r.Used[2] };
        steps = Math.Max(1, steps);
        // keep the phase*block work bounded
        while (steps > 1 && (long)steps * steps * steps * r.TotalBlocks > 500000) steps--;

        int bestSound = -1;
        double[] bestPhase = { 0, 0, 0 };
        bool[] bestFlags = null;
        var pcx = PhaseCandidates(trim[0], steps);
        var pcy = PhaseCandidates(trim[1], steps);
        var pcz = PhaseCandidates(trim[2], steps);

        foreach (double px in pcx)
            foreach (double py in pcy)
                foreach (double pz in pcz)
                {
                    var flags = new bool[r.TotalBlocks];
                    int sound = 0;
                    for (int i = 0; i < nx; i++)
                        for (int j = 0; j < ny; j++)
                            for (int k = 0; k < nz; k++)
                            {
                                double cx = px + i * (sx + kerf), cy = py + j * (sy + kerf), cz = pz + k * (sz + kerf);
                                var lo = new Point3d(cx, cy, cz);
                                var hi = new Point3d(cx + sx, cy + sy, cz + sz);
                                bool sound1 = !AnyFractureCrosses(fractures, lo, hi);
                                int idx = (i * ny + j) * nz + k;
                                flags[idx] = sound1;
                                if (sound1) sound++;
                            }
                    if (sound > bestSound)
                    {
                        bestSound = sound; bestFlags = flags;
                        bestPhase[0] = px; bestPhase[1] = py; bestPhase[2] = pz;
                    }
                }

        r.Phase = bestPhase;
        r.Sound = bestFlags ?? r.Sound;
        r.SoundBlocks = bestSound < 0 ? r.TotalBlocks : bestSound;
        r.FlawedBlocks = r.TotalBlocks - r.SoundBlocks;
        r.SoundYield = r.RawVolume > 1e-12 ? r.SoundBlocks * r.BlockVolume / r.RawVolume : 0;
    }

    private static double[] PhaseCandidates(double trim, int steps)
    {
        if (trim <= 1e-9 || steps <= 1) return new[] { 0.0 };
        var a = new double[steps];
        for (int i = 0; i < steps; i++) a[i] = trim * i / (steps - 1);
        return a;
    }

    private static bool AnyFractureCrosses(IReadOnlyList<Plane> fractures, Point3d lo, Point3d hi)
    {
        for (int f = 0; f < fractures.Count; f++)
            if (Crosses(fractures[f], lo, hi)) return true;
        return false;
    }

    // A plane crosses the box interior when the 8 corners straddle it (mixed signs).
    private static bool Crosses(Plane f, Point3d lo, Point3d hi)
    {
        const double eps = 1e-7;
        int pos = 0, neg = 0;
        for (int cx = 0; cx < 2; cx++)
            for (int cy = 0; cy < 2; cy++)
                for (int cz = 0; cz < 2; cz++)
                {
                    var pt = new Point3d(cx == 0 ? lo.X : hi.X, cy == 0 ? lo.Y : hi.Y, cz == 0 ? lo.Z : hi.Z);
                    double d = f.DistanceTo(pt);
                    if (d > eps) pos++; else if (d < -eps) neg++;
                    if (pos > 0 && neg > 0) return true;
                }
        return false;
    }

    private static string BuildReport(BlockYieldResult r, double kerf)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Block-yield optimisation" + (r.FractureAware ? " + fracture dodging." : " (rectangular cutting, waste-minimising)."));
        char[] ax = { 'x', 'y', 'z' };
        for (int a = 0; a < 3; a++)
            sb.AppendLine($"  {ax[a]}: raw {r.Length[a]:0.###} -> {r.Count[a]} x {r.Size[a]:0.###} (used {r.Used[a]:0.###}, trim {r.Length[a] - r.Used[a]:0.###}, phase {r.Phase[a]:0.###})");
        sb.AppendLine($"  blocks: {r.Count[0]} x {r.Count[1]} x {r.Count[2]} = {r.TotalBlocks}  @ {r.Size[0]:0.###} x {r.Size[1]:0.###} x {r.Size[2]:0.###}");
        sb.AppendLine($"  geometric YIELD = {r.Yield * 100:0.#}%   waste = {r.Waste * 100:0.#}%");
        if (r.FractureAware)
        {
            sb.AppendLine($"  fracture dodge: {r.SoundBlocks} sound / {r.FlawedBlocks} flawed  ->  SOUND YIELD = {r.SoundYield * 100:0.#}%");
            if (r.FlawedBlocks > 0) sb.AppendLine("  ! Flawed blocks straddle a fracture: reject or down-grade; align the cut frame to the joints to cut fewer.");
        }
        if (r.TotalBlocks == 0) sb.AppendLine("  ! No whole block fits: raw block smaller than the minimum size band.");
        else if (r.Waste > 0.25) sb.AppendLine("  ! High geometric waste (>25%): widen the tolerance or pick a size dividing the raw extents.");
        return sb.ToString().TrimEnd();
    }
}
