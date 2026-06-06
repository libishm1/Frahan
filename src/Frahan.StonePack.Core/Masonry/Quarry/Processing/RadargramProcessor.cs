#nullable disable
using System;
using System.Collections.Generic;
using System.Numerics;
using Frahan.Masonry.Quarry.Ingestion;

namespace Frahan.Masonry.Quarry.Processing;

// =============================================================================
// RadargramProcessor -- Rhino-free GPR B-scan processing chain.
//
// Ports the pipeline validated in outputs/2026-06-04/gpr_extraction (Python
// prototype on the Bondua/Tinti 2024 Botticino marble + Doetsch et al. Grimsel
// granite data) to Core C#. Operates on a [samples, traces] amplitude grid.
//
// Stages (citations -> gpr_math_derivations.md):
//   dewow            -- high-pass running-mean subtraction (remove low-freq "wow")
//   backgroundRemoval-- subtract mean trace (horizontal banding / direct wave)
//   timeZeroMute     -- zero air-wave / coupling band
//   tPowerGain       -- (t)^p spherical-divergence + absorption compensation
//   agc              -- sliding-window RMS automatic gain control
//   stoltMigration   -- f-k constant-velocity migration (Stolt 1978): collapse
//                       diffractions, reposition dipping reflectors to true depth
//   hilbertEnergy    -- |analytic signal|^2 instantaneous energy (fracture proxy)
//   depthEqualize    -- per-row median normalisation (deep weak reflectors up)
//
// Velocity model: v = c / sqrt(eps_r); marble eps_r ~ 9 (v ~ 0.10 m/ns), granite
// eps_r ~ 6 (v ~ 0.12 m/ns). Depth = v * t / 2 (two-way). Pure managed code; FFT
// via the bundled radix-2 Fft (no MathNet, no Rhino).
// =============================================================================

public sealed class RadargramProcessor
{
    public double DewowWindowFraction { get; set; } = 1.0 / 30.0;
    public double TimeZeroMuteFraction { get; set; } = 0.05;
    public double TPowerGainExponent { get; set; } = 1.6;
    public double AgcWindowFraction { get; set; } = 1.0 / 25.0;
    public bool Migrate { get; set; } = true;
    public bool DepthEqualize { get; set; } = true;
    public int EqualizeWindow { get; set; } = 31;

    /// <summary>Build a regular [samples, traces] grid from a radargram. Returns the TRUE
    /// two-way sample interval in nanoseconds (velocity-independent), so the caller scales
    /// depth = v*(i*dtNs)/2 with the stone velocity. Prefers GprTrace.SampleIntervalNs (set
    /// by the MALA/IDS readers); falls back to recovering dt from the metres-per-sample dz at
    /// the vacuum velocity (dz = c*dt/2 convention) only when the interval is unknown.
    /// Requires a uniform sample count; ragged traces are truncated to the shortest length.</summary>
    public static double[,] ToGrid(GprRadargram g, out double dtNs, out double dx)
    {
        if (g == null) throw new ArgumentNullException(nameof(g));
        int ntr = g.TraceCount;
        if (ntr == 0) throw new ArgumentException("radargram has no traces");
        int ns = int.MaxValue;
        for (int t = 0; t < ntr; t++) ns = Math.Min(ns, g.Traces[t].SampleCount);
        var B = new double[ns, ntr];
        for (int t = 0; t < ntr; t++)
        {
            var s = g.Traces[t].SampleAmplitudes;
            for (int i = 0; i < ns; i++) B[i, t] = s[i];
        }
        dx = ntr > 1 ? Math.Abs(g.Traces[1].X - g.Traces[0].X) : 1.0;
        double dtTrue = g.Traces[0].SampleIntervalNs;        // true ns/sample if the reader knew it
        if (dtTrue > 0.0)
        {
            dtNs = dtTrue;
        }
        else
        {
            // fallback: recover dt from the metres-per-sample two-way step at vacuum velocity
            // (dz = c*dt/2 convention; c = 0.2998 m/ns). Only used when SampleIntervalNs is 0.
            double dz = g.Traces[0].SampleSpacingMetres;
            dtNs = 2.0 * dz / 0.2998;
        }
        return B;
    }

    // ---- elementary column/row operations on a [ns, ntr] grid ----

    // numpy.convolve(.,ones(win)/win,'same') box-mean of a 1-D signal: window for output
    // index i is [i-loOff, i+hiOff] with loOff=win/2, hiOff=win-1-win/2, out-of-range = 0,
    // divided by the FULL width win (NOT the clipped count) -- matches numpy 'same' exactly.
    // O(len) running sum. dst may alias src is NOT allowed (read src, write dst).
    private static void BoxMeanSame(double[] src, int len, int win, double[] dst)
    {
        if (win <= 1) { Array.Copy(src, dst, len); return; }
        int loOff = win / 2, hiOff = win - 1 - loOff;
        double s = 0;
        int lo0 = Math.Max(0, -loOff), hi0 = Math.Min(len - 1, hiOff);
        for (int k = lo0; k <= hi0; k++) s += src[k];
        double inv = 1.0 / win;
        for (int i = 0; i < len; i++)
        {
            dst[i] = s * inv;
            int add = i + 1 + hiOff, rem = i - loOff;
            if (add < len) s += src[add];
            if (rem >= 0) s -= src[rem];
        }
    }

    public static double[,] Dewow(double[,] B, int win)
    {
        int ns = B.GetLength(0), ntr = B.GetLength(1);
        var outp = new double[ns, ntr];
        // independent columns -> deterministic Parallel.For (thread-local scratch)
        System.Threading.Tasks.Parallel.For(0, ntr, () => (new double[ns], new double[ns]),
            (t, _, sc) =>
            {
                var (col, lf) = sc;
                for (int i = 0; i < ns; i++) col[i] = B[i, t];
                BoxMeanSame(col, ns, win, lf);             // running-mean low-pass (numpy 'same')
                for (int i = 0; i < ns; i++) outp[i, t] = col[i] - lf[i];   // high-pass
                return sc;
            }, _ => { });
        return outp;
    }

    /// <summary>Separable box smoothing matching Python smooth2d: box of width 2*rt+1 along
    /// depth (axis 0), then 2*rx+1 along traces (axis 1), numpy 'same' edges.</summary>
    public static double[,] Smooth2d(double[,] B, int rt, int rx)
    {
        int ns = B.GetLength(0), ntr = B.GetLength(1);
        var cur = (double[,])B.Clone();
        if (rt > 0)
        {
            int win = 2 * rt + 1;
            System.Threading.Tasks.Parallel.For(0, ntr, () => (new double[ns], new double[ns]),
                (t, _, sc) =>
                {
                    var (col, sm) = sc;
                    for (int i = 0; i < ns; i++) col[i] = cur[i, t];
                    BoxMeanSame(col, ns, win, sm);
                    for (int i = 0; i < ns; i++) cur[i, t] = sm[i];
                    return sc;
                }, _ => { });
        }
        if (rx > 0)
        {
            int win = 2 * rx + 1;
            System.Threading.Tasks.Parallel.For(0, ns, () => (new double[ntr], new double[ntr]),
                (i, _, sc) =>
                {
                    var (row, sm) = sc;
                    for (int t = 0; t < ntr; t++) row[t] = cur[i, t];
                    BoxMeanSame(row, ntr, win, sm);
                    for (int t = 0; t < ntr; t++) cur[i, t] = sm[t];
                    return sc;
                }, _ => { });
        }
        return cur;
    }

    public static double[,] BackgroundRemoval(double[,] B)
    {
        int ns = B.GetLength(0), ntr = B.GetLength(1);
        var outp = new double[ns, ntr];
        for (int i = 0; i < ns; i++)
        {
            double mean = 0; for (int t = 0; t < ntr; t++) mean += B[i, t];
            mean /= ntr;
            for (int t = 0; t < ntr; t++) outp[i, t] = B[i, t] - mean;
        }
        return outp;
    }

    public static void TimeZeroMute(double[,] B, int muteSamples)
    {
        int ntr = B.GetLength(1);
        for (int i = 0; i < Math.Min(muteSamples, B.GetLength(0)); i++)
            for (int t = 0; t < ntr; t++) B[i, t] = 0.0;
    }

    public static double[,] TPowerGain(double[,] B, double dt, double p)
    {
        int ns = B.GetLength(0), ntr = B.GetLength(1);
        var outp = new double[ns, ntr];
        for (int i = 0; i < ns; i++)
        {
            double g = Math.Pow(i * dt + 1.0, p);
            for (int t = 0; t < ntr; t++) outp[i, t] = B[i, t] * g;
        }
        return outp;
    }

    public static double[,] Agc(double[,] B, int win)
    {
        int ns = B.GetLength(0), ntr = B.GetLength(1);
        var outp = new double[ns, ntr];
        var col = new double[ns]; var sq = new double[ns]; var ms = new double[ns];
        for (int t = 0; t < ntr; t++)
        {
            for (int i = 0; i < ns; i++) { col[i] = B[i, t]; sq[i] = col[i] * col[i]; }
            BoxMeanSame(sq, ns, win, ms);                  // mean of squares (numpy 'same')
            for (int i = 0; i < ns; i++)
                outp[i, t] = col[i] / Math.Sqrt(ms[i] + 1e-9);
        }
        return outp;
    }

    /// <summary>f-k (Stolt 1978) constant-velocity migration. Exploding-reflector
    /// model: migration velocity vm = v/2. 2-D FFT -> remap each (kz,kx) from the
    /// source frequency w' = vm*sign(kz)*sqrt(kz^2+kx^2) with the Stolt Jacobian
    /// vm*|kz|/|w'|, plus a cosine dip-taper where |vm*kx|/|w| -> 1 to suppress
    /// steep-dip aliasing -> inverse 2-D FFT. <paramref name="v"/> in m/ns,
    /// <paramref name="dt"/> in ns, <paramref name="dx"/> in m.</summary>
    public static double[,] StoltMigration(double[,] B, double dt, double dx, double v)
    {
        int ns = B.GetLength(0), ntr = B.GetLength(1);
        double vm = v / 2.0;

        // 2-D FFT on the EXACT grid (matches numpy fft2; no zero-pad that would shift the
        // w/kx grid). Columns (length ns) then rows (length ntr), via the arbitrary-length Dft.
        var grid = new Complex[ns, ntr];
        for (int i = 0; i < ns; i++)
            for (int t = 0; t < ntr; t++) grid[i, t] = new Complex(B[i, t], 0.0);
        // forward FFT down each column (length ns) -- ntr independent columns
        System.Threading.Tasks.Parallel.For(0, ntr, () => new Complex[ns], (t, _, col) =>
        {
            for (int i = 0; i < ns; i++) col[i] = grid[i, t];
            var c = Fft.Dft(col, false);
            for (int i = 0; i < ns; i++) grid[i, t] = c[i];
            return col;
        }, _ => { });
        // forward FFT along each row (length ntr) -- ns independent rows
        System.Threading.Tasks.Parallel.For(0, ns, () => new Complex[ntr], (i, _, row) =>
        {
            for (int t = 0; t < ntr; t++) row[t] = grid[i, t];
            var r = Fft.Dft(row, false);
            for (int t = 0; t < ntr; t++) grid[i, t] = r[t];
            return row;
        }, _ => { });

        double[] w = FftFreq(ns, dt, 2.0 * Math.PI);   // rad/ns
        double[] kx = FftFreq(ntr, dx, 2.0 * Math.PI); // rad/m

        // dip taper P *= taper(|vm*kx|/|w|)
        for (int i = 0; i < ns; i++)
            for (int t = 0; t < ntr; t++)
            {
                double ratio = Math.Abs(vm * kx[t]) / (Math.Abs(w[i]) + 1e-12);
                double taper = ratio < 0.85 ? 1.0
                    : ratio > 1.0 ? 0.0
                    : 0.5 * (1 + Math.Cos(Math.PI * (ratio - 0.85) / 0.15));
                grid[i, t] *= taper;
            }

        // Stolt remap: for each output (kz index i, kx index t) sample the source
        // spectrum at w' = vm*sign(kz)*sqrt(kz^2+kx^2) by linear interp in w.
        var sortIdx = ArgSort(w);
        var wSorted = new double[ns];
        for (int i = 0; i < ns; i++) wSorted[i] = w[sortIdx[i]];

        var mig = new Complex[ns, ntr];
        // remap each output column independently (thread-local re/im scratch)
        System.Threading.Tasks.Parallel.For(0, ntr, () => (new double[ns], new double[ns]),
            (t, _, scratch) =>
            {
                var (reSorted, imSorted) = scratch;
                for (int i = 0; i < ns; i++) { reSorted[i] = grid[sortIdx[i], t].Real; imSorted[i] = grid[sortIdx[i], t].Imaginary; }
                for (int i = 0; i < ns; i++)
                {
                    double kz = w[i] / vm;                          // target kz grid == w/vm
                    double wsrc = vm * Math.Sign(kz) * Math.Sqrt(kz * kz + kx[t] * kx[t]);
                    double re = Interp(wsrc, wSorted, reSorted);
                    double im = Interp(wsrc, wSorted, imSorted);
                    double jac = Math.Abs(wsrc) > 1e-9 ? vm * Math.Abs(kz) / (Math.Abs(wsrc) + 1e-12) : 0.0;
                    mig[i, t] = new Complex(re * jac, im * jac);
                }
                return scratch;
            }, _ => { });

        // inverse 2-D FFT (rows then columns)
        System.Threading.Tasks.Parallel.For(0, ns, () => new Complex[ntr], (i, _, row) =>
        {
            for (int t = 0; t < ntr; t++) row[t] = mig[i, t];
            var r = Fft.Dft(row, true);
            for (int t = 0; t < ntr; t++) mig[i, t] = r[t];
            return row;
        }, _ => { });
        System.Threading.Tasks.Parallel.For(0, ntr, () => new Complex[ns], (t, _, col) =>
        {
            for (int i = 0; i < ns; i++) col[i] = mig[i, t];
            var c = Fft.Dft(col, true);
            for (int i = 0; i < ns; i++) mig[i, t] = c[i];
            return col;
        }, _ => { });

        var outp = new double[ns, ntr];
        for (int i = 0; i < ns; i++)
            for (int t = 0; t < ntr; t++) outp[i, t] = mig[i, t].Real;
        return outp;
    }

    /// <summary>Instantaneous energy = |analytic signal|^2, per trace. The 986 traces are
    /// independent and write disjoint output columns, so a deterministic Parallel.For is
    /// bit-identical to the serial loop (each thread gets its own scratch trace buffer).</summary>
    public static double[,] HilbertEnergy(double[,] B)
    {
        int ns = B.GetLength(0), ntr = B.GetLength(1);
        var outp = new double[ns, ntr];
        System.Threading.Tasks.Parallel.For(0, ntr, () => new double[ns], (t, _, trace) =>
        {
            for (int i = 0; i < ns; i++) trace[i] = B[i, t];
            var env = Fft.AnalyticEnvelope(trace);
            for (int i = 0; i < ns; i++) outp[i, t] = env[i] * env[i];
            return trace;
        }, _ => { });
        return outp;
    }

    /// <summary>Per-row (depth) median normalisation: a locally strong DEEP
    /// reflector reads as a fracture even though absolute energy decays with
    /// depth (the relative-amplitude display behind the energy section).</summary>
    public static double[,] DepthEqualizeEnergy(double[,] e, int win = 31)
    {
        int ns = e.GetLength(0), ntr = e.GetLength(1);
        double emax = 0; foreach (var x in e) if (x > emax) emax = x;
        var rowMed = new double[ns];
        var buf = new double[ntr];
        for (int i = 0; i < ns; i++)
        {
            for (int t = 0; t < ntr; t++) buf[t] = e[i, t];
            Array.Sort(buf);                  // sort IN PLACE (was: sort a discarded clone)
            rowMed[i] = Median(buf);          // true per-row median (matches np.median)
        }
        // smooth the row-median profile with numpy 'same' box of width win
        var sm = new double[ns];
        BoxMeanSame(rowMed, ns, win, sm);
        var outp = new double[ns, ntr];
        for (int i = 0; i < ns; i++)
            for (int t = 0; t < ntr; t++) outp[i, t] = e[i, t] / (sm[i] + 1e-9 * emax);
        return outp;
    }

    /// <summary>Full chain on a [ns,ntr] grid; returns the instantaneous-energy section
    /// ready for FractureExtractor. Mirrors the validated Python run_chain EXACTLY:
    /// dewow -> background removal -> time-zero mute -> smooth2d(1,1) -> t-power gain ->
    /// [Stolt migration -> smooth2d(1,2)] -> Hilbert energy -> smooth2d(2,2) ->
    /// depth-equalise. NO AGC in the chain (the legacy prototype used AGC; the validated
    /// chain does not). <paramref name="dt"/> in ns, <paramref name="v"/> in m/ns.</summary>
    public double[,] Run(double[,] B, double dt, double dx, double v)
    {
        int ns = B.GetLength(0);
        var s = Dewow(B, Math.Max(5, (int)(ns * DewowWindowFraction)));
        s = BackgroundRemoval(s);
        TimeZeroMute(s, (int)(TimeZeroMuteFraction * ns));
        s = Smooth2d(s, 1, 1);                                  // mild pre-migration de-speckle
        s = TPowerGain(s, dt, TPowerGainExponent);
        if (Migrate && v > 0)
        {
            s = StoltMigration(s, dt, dx, v);
            s = Smooth2d(s, 1, 2);                              // post-migration clean-up
        }
        var e = HilbertEnergy(s);
        e = Smooth2d(e, 2, 2);                                  // smooth energy for stable picks
        if (DepthEqualize) e = DepthEqualizeEnergy(e, EqualizeWindow);
        return e;
    }

    // ---- helpers ----

    private static double[] FftFreq(int n, double d, double scale)
    {
        var f = new double[n];
        for (int i = 0; i < n; i++)
        {
            int k = i <= n / 2 ? i : i - n;     // numpy.fftfreq ordering
            f[i] = scale * k / (n * d);
        }
        return f;
    }

    private static int[] ArgSort(double[] a)
    {
        var idx = new int[a.Length];
        for (int i = 0; i < a.Length; i++) idx[i] = i;
        Array.Sort(idx, (x, y) => a[x].CompareTo(a[y]));
        return idx;
    }

    private static double Interp(double xq, double[] xs, double[] ys)
    {
        int n = xs.Length;
        if (xq <= xs[0]) return 0.0;            // outside -> 0 (left/right=0)
        if (xq >= xs[n - 1]) return 0.0;
        int lo = 0, hi = n - 1;
        while (hi - lo > 1) { int m = (lo + hi) / 2; if (xs[m] <= xq) lo = m; else hi = m; }
        double t = (xq - xs[lo]) / (xs[hi] - xs[lo] + 1e-30);
        return ys[lo] + t * (ys[hi] - ys[lo]);
    }

    private static double Median(double[] sorted)
    {
        int n = sorted.Length;
        return n % 2 == 1 ? sorted[n / 2] : 0.5 * (sorted[n / 2 - 1] + sorted[n / 2]);
    }
}
