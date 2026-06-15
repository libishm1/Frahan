#nullable disable
using System;
using Frahan.Masonry.Fractures;            // BoundingBox3
using Frahan.Masonry.Quarry.BlockCutOpt;   // BaecherSet, BaecherDfnGenerator
using Frahan.Masonry.Quarry.Processing;    // Kriging

namespace Frahan.Tests;

// Phase A "measure" debt retirement: the Stochastic DFN (Baecher) generator and the GPR
// Fracture Surfaces 3D ordinary-kriging engine are both fully implemented but had zero tests.
// These cover the load-bearing invariants headlessly (no Rhino).
static class QuarryDfnKrigingTests
{
    // Baecher finite-disc DFN: deterministic given seed, P32 > 0, and denser sets (smaller
    // spacing) produce more fractures and higher fracture intensity P32.
    public static void Baecher_Deterministic_And_P32_RisesWithIntensity()
    {
        var domain = new BoundingBox3(0, 0, 0, 10.0, 10.0, 10.0);
        var coarse = new[] { new BaecherSet(0, 0, 1, kappa: 8.0, spacing: 2.0, meanDiameter: 3.0) };
        var dense = new[] { new BaecherSet(0, 0, 1, kappa: 8.0, spacing: 0.5, meanDiameter: 3.0) };

        var a = BaecherDfnGenerator.Generate(coarse, domain, seed: 7);
        var b = BaecherDfnGenerator.Generate(coarse, domain, seed: 7);
        Assert(a.FractureCount == b.FractureCount && Math.Abs(a.P32 - b.P32) < 1e-12,
            $"Baecher not deterministic: N {a.FractureCount}/{b.FractureCount} P32 {a.P32}/{b.P32}");
        Assert(a.P32 > 0 && a.FractureCount > 0, "Baecher produced no fractures / zero P32");

        var d = BaecherDfnGenerator.Generate(dense, domain, seed: 7);
        Assert(d.FractureCount > a.FractureCount,
            $"smaller spacing should give more fractures: dense {d.FractureCount} vs coarse {a.FractureCount}");
        Assert(d.P32 > a.P32,
            $"smaller spacing should raise P32: dense {d.P32:F3} vs coarse {a.P32:F3}");
        Console.WriteLine($"        Baecher: coarse N={a.FractureCount} P32={a.P32:F3} | dense N={d.FractureCount} P32={d.P32:F3}");
    }

    // Ordinary kriging is an exact interpolator: with a tiny nugget, Predict at a sample point
    // returns that sample's value with ~0 variance; variance is non-negative everywhere; and the
    // factorisation is deterministic for fixed (range, sill, nugget).
    public static void Kriging_ExactAtSamples_VarianceNonNegative_Deterministic()
    {
        double[] x = { 0, 1, 2, 0, 1, 2, 0.5, 1.5 };
        double[] y = { 0, 0, 0, 1, 1, 1, 0.5, 0.5 };
        var z = new double[x.Length];
        for (int i = 0; i < x.Length; i++) z[i] = x[i] + 2.0 * y[i]; // smooth planar field

        var k = new Kriging(x, y, z, range: 2.0, sill: 4.0, nugget: 1e-6);
        for (int i = 0; i < x.Length; i++)
        {
            var (mean, variance) = k.Predict(x[i], y[i]);
            Assert(Math.Abs(mean - z[i]) < 1e-3, $"kriging not exact at sample {i}: {mean:F4} vs {z[i]:F4}");
            Assert(variance >= -1e-9, $"negative kriging variance {variance} at sample {i}");
        }

        var q = k.Predict(1.0, 0.5);
        Assert(!double.IsNaN(q.Mean) && q.Variance >= -1e-9, "interior query invalid");

        var k2 = new Kriging(x, y, z, range: 2.0, sill: 4.0, nugget: 1e-6);
        Assert(Math.Abs(k2.Predict(1.0, 0.5).Mean - q.Mean) < 1e-12, "kriging non-deterministic");
        Console.WriteLine($"        Kriging: exact at {x.Length} samples; q(1,0.5) mean={q.Mean:F3} sigma={k.Sigma(1.0, 0.5):F3}");
    }

    private static void Assert(bool c, string m) { if (!c) throw new InvalidOperationException(m); }
}
