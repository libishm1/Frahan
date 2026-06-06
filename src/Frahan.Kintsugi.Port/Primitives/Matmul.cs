#nullable disable
using System;
using System.Numerics;

namespace Frahan.Kintsugi.Port.Primitives;

/// <summary>
/// Dense matrix-matrix multiply, SIMD-accelerated via
/// <see cref="System.Numerics.Vector{T}"/> where the platform supports
/// hardware vectorisation. Used by the PointNet++ encoder
/// (Conv1D collapses to matmul for kernel size 1), the VQ-VAE codebook
/// lookup, every transformer layer's QKV projection, and the diffusion
/// denoiser's MLP heads.
///
/// All matrices stored row-major as flat <c>float[]</c>. We use float
/// (not double) to match PyTorch's default and to fit twice as many
/// values per SIMD register.
///
/// Implementations:
///   - <see cref="MatMul(float[], float[], float[], int, int, int)"/>
///     naïve i,k,j loop with SIMD inner reduction.
///   - <see cref="MatVec(float[], float[], float[], int, int)"/>
///     matrix × vector specialisation; faster than calling MatMul
///     with N=1.
/// </summary>
public static class Matmul
{
    /// <summary>
    /// C[M,N] = A[M,K] * B[K,N]. Row-major. Output array preallocated
    /// length M*N. SIMD via Vector&lt;float&gt; on supported platforms.
    /// Above PreferGpuMacThreshold (default 200k MACs) and when
    /// GpuMatmul.IsAvailable + GpuEnabled, transparently dispatches
    /// to GPU and falls back to scalar on any error.
    /// </summary>
    public static void MatMul(float[] a, float[] b, float[] c, int M, int K, int N)
    {
        if (a == null || b == null || c == null) throw new ArgumentNullException();
        if (a.Length < M * K) throw new ArgumentException("a too small.");
        if (b.Length < K * N) throw new ArgumentException("b too small.");
        if (c.Length < M * N) throw new ArgumentException("c too small.");

        // GPU fast path. Only above the MAC threshold to amortise
        // host->device transfer. Falls back to scalar on any error.
        if (GpuMatmul.GpuEnabled
            && (long)M * K * N >= GpuMatmul.PreferGpuMacThreshold
            && GpuMatmul.IsAvailable)
        {
            try
            {
                GpuMatmul.MatMul(a, b, c, M, K, N);
                return;
            }
            catch
            {
                // Fall through to scalar; do NOT throw out of the
                // common code path on a GPU hiccup.
            }
        }

        // Zero C.
        Array.Clear(c, 0, M * N);
        int vsz = Vector<float>.Count;
        // Loop order i, k, j with SIMD over j. The aik scalar is
        // broadcast and FMA-fused with b[k,:] into c[i,:].
        for (int i = 0; i < M; i++)
        {
            int aRow = i * K;
            int cRow = i * N;
            for (int k = 0; k < K; k++)
            {
                float aik = a[aRow + k];
                int bRow = k * N;
                int j = 0;
                if (Vector.IsHardwareAccelerated && vsz > 1)
                {
                    var av = new Vector<float>(aik);
                    int simdEnd = N - (N % vsz);
                    for (; j < simdEnd; j += vsz)
                    {
                        var bv = new Vector<float>(b, bRow + j);
                        var cv = new Vector<float>(c, cRow + j);
                        var nv = cv + av * bv;
                        nv.CopyTo(c, cRow + j);
                    }
                }
                for (; j < N; j++)
                    c[cRow + j] += aik * b[bRow + j];
            }
        }
    }

    /// <summary>
    /// y[M] = A[M,K] * x[K]. Row-major A. Specialisation of MatMul for
    /// N=1 — about 2× faster because we skip the j SIMD vectorisation
    /// and instead vectorise the k reduction.
    /// </summary>
    public static void MatVec(float[] a, float[] x, float[] y, int M, int K)
    {
        if (a == null || x == null || y == null) throw new ArgumentNullException();
        if (a.Length < M * K) throw new ArgumentException("a too small.");
        if (x.Length < K) throw new ArgumentException("x too small.");
        if (y.Length < M) throw new ArgumentException("y too small.");
        int vsz = Vector<float>.Count;
        for (int i = 0; i < M; i++)
        {
            int aRow = i * K;
            float sum = 0;
            int k = 0;
            if (Vector.IsHardwareAccelerated && vsz > 1)
            {
                var accv = Vector<float>.Zero;
                int simdEnd = K - (K % vsz);
                for (; k < simdEnd; k += vsz)
                {
                    var av = new Vector<float>(a, aRow + k);
                    var xv = new Vector<float>(x, k);
                    accv += av * xv;
                }
                for (int t = 0; t < vsz; t++) sum += accv[t];
            }
            for (; k < K; k++) sum += a[aRow + k] * x[k];
            y[i] = sum;
        }
    }

    /// <summary>Add bias[N] to every row of C[M,N] in place.</summary>
    public static void AddBias(float[] c, float[] bias, int M, int N)
    {
        if (bias == null) return;
        if (bias.Length < N) throw new ArgumentException("bias too small.");
        for (int i = 0; i < M; i++)
        {
            int row = i * N;
            for (int j = 0; j < N; j++) c[row + j] += bias[j];
        }
    }
}
