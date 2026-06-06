#nullable disable
using System;
using System.Numerics;

namespace Frahan.Masonry.Quarry.Processing;

// =============================================================================
// Fft -- self-contained radix-2 Cooley-Tukey FFT (no external dependency).
//
// Core is dependency-light and Rhino-free; rather than pull MathNet.Numerics we
// ship a compact iterative in-place transform. Arbitrary lengths are handled by
// zero-padding to the next power of two (the GPR processor only needs the
// spectrum for filtering / Hilbert / Stolt, so padding is transparent once the
// result is truncated back to the original length).
//
// Algorithm: Cooley & Tukey 1965, "An algorithm for the machine calculation of
// complex Fourier series", Math. Comp. 19. Bit-reversal permutation + butterfly.
// A numerical method (not copyrightable); this is a clean-room implementation.
// =============================================================================

internal static class Fft
{
    public static int NextPow2(int n)
    {
        int p = 1;
        while (p < n) p <<= 1;
        return p;
    }

    /// <summary>In-place radix-2 FFT. Length of <paramref name="a"/> must be a power of two.
    /// <paramref name="inverse"/> true performs the inverse transform (1/N normalised).</summary>
    public static void Transform(Complex[] a, bool inverse)
    {
        int n = a.Length;
        if (n == 0) return;
        if ((n & (n - 1)) != 0)
            throw new ArgumentException("Fft length must be a power of two; pad first.", nameof(a));

        // bit-reversal permutation
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { var t = a[i]; a[i] = a[j]; a[j] = t; }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = 2.0 * Math.PI / len * (inverse ? 1.0 : -1.0);
            var wlen = new Complex(Math.Cos(ang), Math.Sin(ang));
            for (int i = 0; i < n; i += len)
            {
                Complex w = Complex.One;
                for (int k = 0; k < len / 2; k++)
                {
                    Complex u = a[i + k];
                    Complex v = a[i + k + len / 2] * w;
                    a[i + k] = u + v;
                    a[i + k + len / 2] = u - v;
                    w *= wlen;
                }
            }
        }

        if (inverse)
            for (int i = 0; i < n; i++) a[i] /= n;
    }

    /// <summary>DFT of ANY length (matches numpy.fft.fft / ifft exactly). Power-of-two
    /// lengths use the in-place radix-2 transform; other lengths use Bluestein's chirp-z
    /// algorithm, so HilbertEnergy + 2-D Stolt migration operate on the EXACT sample/trace
    /// counts (no zero-padding that would shift the frequency grid). Returns a new array.</summary>
    public static Complex[] Dft(Complex[] x, bool inverse)
    {
        int n = x.Length;
        if (n == 0) return new Complex[0];
        if ((n & (n - 1)) == 0)
        {
            var a = (Complex[])x.Clone();
            Transform(a, inverse);
            return a;
        }
        if (inverse)
        {
            // ifft(x) = conj(fft(conj(x)))/n
            var xc = new Complex[n];
            for (int i = 0; i < n; i++) xc[i] = Complex.Conjugate(x[i]);
            var y = Bluestein(xc);
            for (int i = 0; i < n; i++) y[i] = Complex.Conjugate(y[i]) / n;
            return y;
        }
        return Bluestein(x);
    }

    // Bluestein PLAN per length: the chirp w[] and the precomputed FFT of the mirrored
    // kernel Bspec are length-only (input-independent), so they are built ONCE per distinct
    // length and reused across every trace/column/row DFT of that length. This is the single
    // biggest speedup for HilbertEnergy (986 same-length DFTs) + Stolt migration. Read-only
    // after build, so safe to share across Parallel.For threads.
    private sealed class BluesteinPlan
    {
        public int N, M;
        public Complex[] W;       // chirp exp(-i*pi*j^2/n)
        public Complex[] Bspec;   // FFT of the mirrored conj-chirp kernel, length M
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, BluesteinPlan> Plans
        = new System.Collections.Concurrent.ConcurrentDictionary<int, BluesteinPlan>();

    private static BluesteinPlan GetPlan(int n) => Plans.GetOrAdd(n, BuildPlan);

    private static BluesteinPlan BuildPlan(int n)
    {
        int m = NextPow2(2 * n - 1);
        var w = new Complex[n];
        for (int j = 0; j < n; j++)
        {
            long jj = (long)j * j % (2L * n);     // j^2 mod 2n keeps the angle exact
            double ang = -Math.PI * jj / n;
            w[j] = new Complex(Math.Cos(ang), Math.Sin(ang));
        }
        var b = new Complex[m];
        for (int j = 0; j < n; j++)
        {
            var bv = Complex.Conjugate(w[j]);
            b[j] = bv;
            if (j > 0) b[m - j] = bv;
        }
        Transform(b, false);
        return new BluesteinPlan { N = n, M = m, W = w, Bspec = b };
    }

    // Forward DFT of arbitrary length via Bluestein: X[k] = w[k] * (a conv b)[k],
    // a[j]=x[j]*w[j]; convolution by a power-of-two FFT of size m >= 2n-1 using the cached plan.
    private static Complex[] Bluestein(Complex[] x)
    {
        var p = GetPlan(x.Length);
        int n = p.N, m = p.M;
        var a = new Complex[m];
        for (int j = 0; j < n; j++) a[j] = x[j] * p.W[j];
        Transform(a, false);
        var bs = p.Bspec;
        for (int i = 0; i < m; i++) a[i] *= bs[i];
        Transform(a, true);
        var X = new Complex[n];
        for (int k = 0; k < n; k++) X[k] = p.W[k] * a[k];
        return X;
    }

    /// <summary>Forward FFT of a real signal at its EXACT length (matches numpy).</summary>
    public static Complex[] Forward(double[] x)
    {
        var a = new Complex[x.Length];
        for (int i = 0; i < x.Length; i++) a[i] = new Complex(x[i], 0.0);
        return Dft(a, false);
    }

    /// <summary>Analytic-signal envelope via the Hilbert transform (Marple 1999):
    /// one-sided spectral weighting H = [1, 2,2,..,2, 1, 0,..,0] then inverse FFT;
    /// |analytic| is the instantaneous amplitude. Returned length == x.Length.</summary>
    public static double[] AnalyticEnvelope(double[] x)
    {
        int n = x.Length;
        var a = new Complex[n];
        for (int i = 0; i < n; i++) a[i] = new Complex(x[i], 0.0);
        var spec = Dft(a, false);                 // EXACT length (matches numpy.fft.fft)

        // one-sided weighting at the true length (numpy convention)
        if (n % 2 == 0)
        {
            for (int i = 1; i < n / 2; i++) spec[i] *= 2.0;
            for (int i = n / 2 + 1; i < n; i++) spec[i] = Complex.Zero;
        }
        else
        {
            for (int i = 1; i < (n + 1) / 2; i++) spec[i] *= 2.0;
            for (int i = (n + 1) / 2; i < n; i++) spec[i] = Complex.Zero;
        }
        var t = Dft(spec, true);

        var env = new double[n];
        for (int i = 0; i < n; i++) env[i] = t[i].Magnitude;
        return env;
    }
}
