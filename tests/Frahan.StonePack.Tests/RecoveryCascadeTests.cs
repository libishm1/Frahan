#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Quarry.BlockCutOpt;

namespace Frahan.Tests;

// Headless tests for the multi-scale crack-aware RecoveryCascade. Proves: (1) it reduces to the
// shipping BlockCutOpt at a single scale; (2) a finer scale recovers material the coarse scale
// rejected; (3) recovery is monotone non-decreasing in depth; (4) the CSUL marketable-volume
// threshold gates recursion; (5) it is deterministic; (6) it does nothing pathological with no
// fractures. Single-pose options (psi/dx/dy fixed) keep each scale's search fast and deterministic.
static class RecoveryCascadeTests
{
    // discrete-fracture-network of random triangle facets (same structure as the granite DFN)
    private static PlyMesh Dfn(int nFacets, BoundingBox3 area, int seed)
    {
        var rng = new Random(seed);
        var v = new List<double>(nFacets * 9);
        var t = new List<int>(nFacets * 3);
        for (int k = 0; k < nFacets; k++)
        {
            double cx = area.MinX + rng.NextDouble() * area.SizeX;
            double cy = area.MinY + rng.NextDouble() * area.SizeY;
            double cz = area.MinZ + rng.NextDouble() * area.SizeZ;
            double r = 0.5 + rng.NextDouble() * 2.5;
            int b = v.Count / 3;
            for (int j = 0; j < 3; j++)
            {
                double a = 2 * Math.PI * (j + rng.NextDouble()) / 3.0;
                v.Add(cx + r * Math.Cos(a)); v.Add(cy + r * Math.Sin(a)); v.Add(cz + (rng.NextDouble() - 0.5) * r);
            }
            t.Add(b); t.Add(b + 1); t.Add(b + 2);
        }
        return new PlyMesh(v, t, null);
    }

    // single-pose options (psi fixed at 0, no dx/dy sweep) -> 1 pose -> fast + deterministic
    private static BlockCutOptOptions Opt(double bx, double by, double bz, double kerf = 0.05)
        => new BlockCutOptOptions(bx, by, bz, kerf, 0.0, 0.0, 1.0, 0.0, 1.0, 0.0, 1.0);

    private static BoundingBox3 Area() => new BoundingBox3(0, 0, 0, 27.0, 65.0, 0.8);

    public static void ReducesToBaseline_SingleScale()
    {
        var area = Area();
        var dfn = Dfn(180, area, 11);
        var opt0 = Opt(3.0, 2.0, 0.8);
        var (baseResult, baseKept) = BlockCutOptSolver.SolveAndExtract(area, dfn, opt0);

        var scales = new List<ScaleSpec> { new ScaleSpec(opt0, 0.0, BlockValueModel.Default, "block") };
        var c = RecoveryCascade.Run(area, dfn, scales);

        Assert(c.Tiers[0].RecoveredCount == baseKept.Count,
            $"single-scale cascade count {c.Tiers[0].RecoveredCount} != baseline kept {baseKept.Count}");
        double baseVol = baseKept.Count * (3.0 * 2.0 * 0.8);
        Assert(Math.Abs(c.TotalRecoveredVolumeM3 - baseVol) < 1e-6,
            $"single-scale recovered vol {c.TotalRecoveredVolumeM3} != {baseVol}");
        Assert(c.CrackedRoutedCount == 0, "single scale must not route any cracked block (no finer scale)");
        Console.WriteLine($"        cascade baseline: tier0={c.Tiers[0].RecoveredCount} blk (== BlockCutOpt {baseKept.Count})");
    }

    public static void CrackRouting_RecoversExtraValue()
    {
        var area = Area();
        var dfn = Dfn(180, area, 11);
        var s1 = new List<ScaleSpec> { new ScaleSpec(Opt(3.0, 2.0, 0.8), 0.0, BlockValueModel.Default, "block") };
        var s2 = new List<ScaleSpec>
        {
            new ScaleSpec(Opt(3.0, 2.0, 0.8), 0.0, BlockValueModel.Default, "block"),
            new ScaleSpec(Opt(1.0, 1.0, 0.4), 0.0, BlockValueModel.Default, "slab"),
        };
        var c1 = RecoveryCascade.Run(area, dfn, s1);
        var c2 = RecoveryCascade.Run(area, dfn, s2);

        Assert(c2.Tiers[1].RecoveredCount > 0, "finer scale recovered nothing from cracked blocks");
        Assert(c2.TotalRecoveredVolumeM3 > c1.TotalRecoveredVolumeM3 + 1e-9,
            $"2-scale total {c2.TotalRecoveredVolumeM3:0.##} not greater than 1-scale {c1.TotalRecoveredVolumeM3:0.##}");
        Console.WriteLine($"        cascade recover-fine: tier1={c2.Tiers[1].RecoveredCount} slabs, " +
                          $"total {c1.TotalRecoveredVolumeM3:0.#}->{c2.TotalRecoveredVolumeM3:0.#} m^3");
    }

    public static void MonotoneRecovery_InDepth()
    {
        var area = Area();
        var dfn = Dfn(180, area, 11);
        var b = new ScaleSpec(Opt(3.0, 2.0, 0.8), 0.0, BlockValueModel.Default, "block");
        var sl = new ScaleSpec(Opt(1.0, 1.0, 0.4), 0.0, BlockValueModel.Default, "slab");
        var ti = new ScaleSpec(Opt(0.5, 0.5, 0.2), 0.0, BlockValueModel.Default, "tile");
        var c1 = RecoveryCascade.Run(area, dfn, new List<ScaleSpec> { b });
        var c2 = RecoveryCascade.Run(area, dfn, new List<ScaleSpec> { b, sl });
        var c3 = RecoveryCascade.Run(area, dfn, new List<ScaleSpec> { b, sl, ti });

        Assert(c2.TotalRecoveredVolumeM3 >= c1.TotalRecoveredVolumeM3 - 1e-9, "recovery dropped from S1 to S2");
        Assert(c3.TotalRecoveredVolumeM3 >= c2.TotalRecoveredVolumeM3 - 1e-9, "recovery dropped from S2 to S3");
        Assert(c3.ResidualVolumeM3 <= c2.ResidualVolumeM3 + 1e-9, "residual rose from S2 to S3");
        Assert(c2.ResidualVolumeM3 <= c1.ResidualVolumeM3 + 1e-9, "residual rose from S1 to S2");
        Console.WriteLine($"        cascade monotone: recovered {c1.TotalRecoveredVolumeM3:0.#}<= {c2.TotalRecoveredVolumeM3:0.#}<= {c3.TotalRecoveredVolumeM3:0.#} m^3");
    }

    public static void StoppingRule_ThresholdGatesRecursion()
    {
        var area = Area();
        var dfn = Dfn(180, area, 11);
        var block = new ScaleSpec(Opt(3.0, 2.0, 0.8), 0.0, BlockValueModel.Default, "block");
        // finer scale slab is 1x1x0.4 = 0.4 m^3; set the threshold above the cracked-block AABB so nothing routes
        var slabHigh = new ScaleSpec(Opt(1.0, 1.0, 0.4), 1.0e6, BlockValueModel.Default, "slab");
        var slabLow = new ScaleSpec(Opt(1.0, 1.0, 0.4), 0.0, BlockValueModel.Default, "slab");

        var hi = RecoveryCascade.Run(area, dfn, new List<ScaleSpec> { block, slabHigh });
        var lo = RecoveryCascade.Run(area, dfn, new List<ScaleSpec> { block, slabLow });

        Assert(hi.Tiers[1].RecoveredCount == 0, "high threshold still recovered at the finer scale");
        Assert(hi.CrackedRoutedCount == 0, "high threshold still routed cracked blocks down");
        Assert(lo.Tiers[1].RecoveredCount > 0, "low threshold recovered nothing at the finer scale");
        Console.WriteLine($"        cascade threshold: hi tier1={hi.Tiers[1].RecoveredCount}, lo tier1={lo.Tiers[1].RecoveredCount}");
    }

    public static void Deterministic_TwoRunsIdentical()
    {
        var area = Area();
        var dfn = Dfn(180, area, 11);
        var scales = new List<ScaleSpec>
        {
            new ScaleSpec(Opt(3.0, 2.0, 0.8), 0.0, BlockValueModel.Default, "block"),
            new ScaleSpec(Opt(1.0, 1.0, 0.4), 0.0, BlockValueModel.Default, "slab"),
        };
        var a = RecoveryCascade.Run(area, dfn, scales);
        var b = RecoveryCascade.Run(area, dfn, scales);
        Assert(a.TotalRecoveredCount == b.TotalRecoveredCount, "non-deterministic count");
        Assert(Math.Abs(a.TotalRecoveredVolumeM3 - b.TotalRecoveredVolumeM3) < 1e-9, "non-deterministic volume");
        Assert(a.CrackedRoutedCount == b.CrackedRoutedCount, "non-deterministic cracked-routed");
        Assert(Math.Abs(a.ResidualVolumeM3 - b.ResidualVolumeM3) < 1e-9, "non-deterministic residual");
    }

    public static void ZeroFracture_FullRecoveryNoRecursion()
    {
        var area = Area();
        // one triangle far outside the tested area -> no OBB ever intersects it
        var v = new List<double> { 1000, 1000, 1000, 1001, 1000, 1000, 1000, 1001, 1000 };
        var t = new List<int> { 0, 1, 2 };
        var far = new PlyMesh(v, t, null);
        var scales = new List<ScaleSpec>
        {
            new ScaleSpec(Opt(3.0, 2.0, 0.8), 0.0, BlockValueModel.Default, "block"),
            new ScaleSpec(Opt(1.0, 1.0, 0.4), 0.0, BlockValueModel.Default, "slab"),
        };
        var c = RecoveryCascade.Run(area, far, scales);
        Assert(c.CrackedRoutedCount == 0, "no fractures yet cracked blocks were routed");
        Assert(c.Tiers[1].RecoveredCount == 0, "no fractures yet the finer scale ran");
        Assert(c.ResidualCount == 0, "no fractures yet residual waste was produced");
        Assert(c.Tiers[0].RecoveredCount > 0, "no blocks recovered on a clean bench");
        Console.WriteLine($"        cascade zero-fracture: tier0={c.Tiers[0].RecoveredCount} blk, cracked=0, residual=0");
    }

    private static void Assert(bool c, string m) { if (!c) throw new InvalidOperationException(m); }
}
