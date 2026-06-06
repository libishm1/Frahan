#nullable disable
using System;
using System.Linq;
using Frahan.Masonry.Quarry.Ingestion;
using Frahan.Masonry.Quarry.Processing;

namespace Frahan.Tests;

// Tests for the Rhino-free GPR processing chain (RadargramProcessor +
// FractureExtractor + bundled Fft). All synthetic / self-contained -- no
// dependency on on-disk datasets, so they run anywhere.
public static class RadargramProcessingTests
{
    private const double Tol = 1e-9;

    // --- Fft: forward then inverse recovers the signal ---
    public static void Fft_ForwardInverse_RoundTrips()
    {
        var rng = new Random(7);
        int n = 64;
        var x = new System.Numerics.Complex[n];
        var orig = new System.Numerics.Complex[n];
        for (int i = 0; i < n; i++) { x[i] = new System.Numerics.Complex(rng.NextDouble() - 0.5, 0); orig[i] = x[i]; }
        // round-trip via reflection-free public path: use a real signal through Forward
        // then a manual inverse using Transform.
        var spec = (System.Numerics.Complex[])x.Clone();
        typeof(RadargramProcessor).Assembly
            .GetType("Frahan.Masonry.Quarry.Processing.Fft")
            .GetMethod("Transform").Invoke(null, new object[] { spec, false });
        typeof(RadargramProcessor).Assembly
            .GetType("Frahan.Masonry.Quarry.Processing.Fft")
            .GetMethod("Transform").Invoke(null, new object[] { spec, true });
        for (int i = 0; i < n; i++)
            Assert(Math.Abs(spec[i].Real - orig[i].Real) < 1e-9, $"FFT round-trip drift at {i}");
    }

    // --- Hilbert envelope of a pure cosine is ~ its amplitude (flat) ---
    public static void HilbertEnergy_Cosine_FlatEnvelope()
    {
        int ns = 256, ntr = 1;
        var B = new double[ns, ntr];
        for (int i = 0; i < ns; i++) B[i, 0] = 1.5 * Math.Cos(2 * Math.PI * 12 * i / ns);
        var e = RadargramProcessor.HilbertEnergy(B);
        // ignore edge transients; interior energy ~ amplitude^2 = 2.25
        double mean = 0; int c = 0;
        for (int i = 40; i < ns - 40; i++) { mean += e[i, 0]; c++; }
        mean /= c;
        Assert(Math.Abs(mean - 2.25) < 0.15, $"Hilbert envelope of cosine not flat (got {mean:F3}, want ~2.25)");
    }

    // --- full chain finds a planted (gently dipping) reflector at the right depth ---
    public static void Chain_PlantedReflector_ExtractedAtCorrectDepth()
    {
        int ns = 256, ntr = 120;
        double dt = 0.4, v = 0.12, dx = 0.5;            // ns, m/ns, m
        int rsamp = 120;                                 // reflector apex near sample 120
        // gentle dip + lateral amplitude variation so it SURVIVES background removal
        // (a perfectly flat event is correctly nulled as horizontal banding).
        var B = SyntheticReflector(ns, ntr, rsamp, dipSamplesPerTrace: 0.15);
        // clean synthetic: no depth decay, so disable AGC-style equalisation
        var proc = new RadargramProcessor { Migrate = false, DepthEqualize = false, TPowerGainExponent = 0.0 };
        var e = proc.Run(B, dt, dx, v);
        var fx = new FractureExtractor { EnergyQuantile = 0.95, MinContinuitySupport = 10 };
        var picks = fx.Extract(e, dt, dx, v);
        Assert(picks.Count > 0, "no fractures extracted from planted reflector");
        // the MEDIAN pick depth should sit on the planted reflector band (apex + half-dip)
        double dipMid = (rsamp + 0.15 * ntr / 2.0) * dt;
        double targetDepth = v * dipMid / 2.0;
        var depths = picks.Select(p => p.DepthMetres).OrderBy(d => d).ToArray();
        double median = depths[depths.Length / 2];
        Assert(Math.Abs(median - targetDepth) < v * (10 * dt) / 2.0,
            $"planted reflector not the dominant pick depth (median {median:F2} m vs target {targetDepth:F2} m)");
    }

    // --- Stolt migration concentrates a diffraction hyperbola toward its apex ---
    public static void StoltMigration_Diffraction_ConcentratesEnergy()
    {
        int ns = 256, ntr = 128;
        double dt = 0.4, v = 0.12, dx = 0.5;
        int apexTrace = 64, apexSample = 90;
        var B = SyntheticDiffraction(ns, ntr, apexTrace, apexSample, dt, dx, v);
        var mig = RadargramProcessor.StoltMigration(B, dt, dx, v);
        // Physical effect: migration FOCUSES the diffraction toward its apex. The
        // scale-invariant signature of focusing is a rise in energy CONCENTRATION =
        // peak-pixel energy / total energy (a Gini-like measure). It is robust to
        // Stolt's FFT amplitude scaling and to the residual smile that the
        // production dip-taper leaves on an isolated synthetic diffractor (verified
        // in _test_stolt.py: concentration 0.029 -> 0.039, +36%).
        double Concentration(double[,] g)
        {
            double tot = 0, peak = 0;
            foreach (var x in g) { double e = x * x; tot += e; if (e > peak) peak = e; }
            return peak / (tot + 1e-30);
        }
        double cBefore = Concentration(B), cAfter = Concentration(mig);
        Assert(cAfter > cBefore * 1.1,
            $"migration did not focus the diffraction (concentration {cBefore:F5}->{cAfter:F5})");
    }

    // --- IDS reader -> processor on real marble data IF present (else skip) ---
    public static void RealMarble_EndToEnd_IfPresent()
    {
        string dt = @"D:\code_ws\Template-General\raw\2026-06-04\bondua_gpr\italy_extracted\g1_LA010004.DT";
        if (!System.IO.File.Exists(dt)) { Console.WriteLine("    SKIP real marble .DT not present"); return; }
        var rg = GprIdsDtReader.Load(dt, "marble-test");
        var B = RadargramProcessor.ToGrid(rg, out double dtns, out double dx);
        // header Y_TIME_CELL = 1.5625e-10 s -> dt = 0.15625 ns; carried via SampleIntervalNs
        double v = 0.10;
        Assert(Math.Abs(dtns - 0.15625) < 1e-4, $"dt recovery wrong: got {dtns:F5} ns, expect 0.15625");
        var e = new RadargramProcessor { Migrate = true }.Run(B, dtns, dx, v);
        var picks = new FractureExtractor().Extract(e, dtns, dx, v);
        Console.WriteLine($"    real marble: {rg.TraceCount} traces, {picks.Count} fracture picks");
        Assert(picks.Count > 0, "no fractures from real marble data");
    }

    // ---- synthetic generators ----
    private static double[,] SyntheticReflector(int ns, int ntr, int rsamp, double dipSamplesPerTrace)
    {
        var w = Ricker(0.12 / 0.4, 41);                 // ~central freq in samples
        var B = new double[ns, ntr];
        for (int t = 0; t < ntr; t++)
        {
            int c = rsamp + (int)Math.Round(dipSamplesPerTrace * t);   // gentle dip
            double amp = 1.0 + 0.3 * Math.Sin(0.2 * t);                // lateral variation
            for (int k = 0; k < w.Length; k++)
            {
                int i = c + k - w.Length / 2;
                if (i >= 0 && i < ns) B[i, t] += amp * w[k];
            }
        }
        return B;
    }

    private static double[,] SyntheticDiffraction(int ns, int ntr, int apexTrace, int apexSample,
        double dt, double dx, double v)
    {
        var w = Ricker(0.2, 41);
        var B = new double[ns, ntr];
        double t0 = apexSample * dt;
        for (int t = 0; t < ntr; t++)
        {
            double x = (t - apexTrace) * dx;
            double tt = Math.Sqrt(t0 * t0 + (2 * x / v) * (2 * x / v));  // hyperbola
            int c = (int)Math.Round(tt / dt);
            for (int k = 0; k < w.Length; k++)
            {
                int i = c + k - w.Length / 2;
                if (i >= 0 && i < ns) B[i, t] += w[k];
            }
        }
        return B;
    }

    private static double[] Ricker(double f, int n)
    {
        var r = new double[n];
        for (int i = 0; i < n; i++)
        {
            double t = i - n / 2;
            double a = (Math.PI * f * t) * (Math.PI * f * t);
            r[i] = (1 - 2 * a) * Math.Exp(-a);
        }
        return r;
    }

    private static void Assert(bool cond, string msg)
    {
        if (!cond) throw new Exception("ASSERT FAILED: " + msg);
    }
}
