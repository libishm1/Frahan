#nullable disable
using System;
using System.Threading;
using ILGPU;
using ILGPU.Runtime;

namespace Frahan.Kintsugi.Port.Primitives;

/// <summary>
/// GPU-accelerated matrix multiply via ILGPU 1.5.x. Compiles a C#
/// kernel to CUDA / OpenCL / CPU at runtime; auto-selects the fastest
/// accelerator available on the host. Falls back gracefully to the
/// scalar+SIMD <see cref="Matmul.MatMul"/> path on any error.
///
/// USAGE
///   if (GpuMatmul.IsAvailable) GpuMatmul.MatMul(a, b, c, M, K, N);
///   else                       Matmul.MatMul(a, b, c, M, K, N);
///
/// Or call <see cref="Matmul.MatMul(float[], float[], float[], int, int, int)"/>
/// directly: it dispatches to GpuMatmul above a size threshold when
/// <see cref="GpuEnabled"/> is true.
///
/// THREAD SAFETY
/// The static accelerator + kernel handles are initialised lazily
/// under a one-shot lock. Subsequent calls share them. ILGPU's
/// accelerator launches are thread-safe.
///
/// LIFETIME
/// The accelerator is process-singleton. <see cref="Dispose"/> is
/// optional; ILGPU cleans up on AppDomain unload.
/// </summary>
public static class GpuMatmul
{
    private static readonly object _lock = new object();
    private static bool _initialised;
    private static bool _available;
    private static string _diagnostic = "(not initialised)";
    private static ILGPU.Context _context;
    private static ILGPU.Runtime.Accelerator _accelerator;
    private static Action<ILGPU.Index2D,
                          ILGPU.ArrayView<float>,
                          ILGPU.ArrayView<float>,
                          ILGPU.ArrayView<float>,
                          int, int, int> _kernel;

    /// <summary>True iff an accelerator was loaded and the kernel
    /// compiled successfully. Probes lazily on first read.</summary>
    public static bool IsAvailable
    {
        get
        {
            EnsureInitialised();
            return _available;
        }
    }

    /// <summary>Free-text diagnostic from the last init attempt.</summary>
    public static string Diagnostic
    {
        get { EnsureInitialised(); return _diagnostic; }
    }

    /// <summary>User-controlled master switch. Set to false to force the
    /// scalar fallback regardless of GPU availability (useful for
    /// parity testing and benchmark comparison).</summary>
    public static bool GpuEnabled { get; set; } = true;

    /// <summary>The size threshold above which GPU is preferred. Below
    /// this M*K*N product, host->device transfer overhead exceeds the
    /// compute savings. Empirically ~64*64*64 = 262144 is the
    /// break-even point on a modern laptop GPU; we use 200k as a
    /// safe default.</summary>
    public static long PreferGpuMacThreshold { get; set; } = 200_000;

    /// <summary>Optional device-name hint. If the substring appears in
    /// a CUDA device's name, that device is preferred. Default: "RTX"
    /// to bias toward NVIDIA discrete GPUs (RTX / Quadro RTX).
    /// Set to null to use simple "first CUDA" fallback.</summary>
    public static string PreferredDeviceNameSubstring { get; set; } = "RTX";

    /// <summary>Name of the actually-selected accelerator. Empty until init.</summary>
    public static string SelectedDeviceName { get; private set; } = "";

    /// <summary>Type of the actually-selected accelerator (Cuda / OpenCL / CPU).</summary>
    public static string SelectedDeviceType { get; private set; } = "";

    private static void EnsureInitialised()
    {
        if (_initialised) return;
        lock (_lock)
        {
            if (_initialised) return;
            try
            {
                _context = ILGPU.Context.CreateDefault();
                _accelerator = null;
                // Enumerate everything we have. Pick by priority:
                //   1. CUDA device whose name contains PreferredDeviceNameSubstring
                //      (default "RTX" → biases toward NVIDIA discrete GPUs like
                //      Quadro RTX 4000, RTX 4090, etc.)
                //   2. Any CUDA device with the largest reported memory
                //      (avoids integrated MX-style cards if both are present)
                //   3. Any OpenCL device (could be Intel iGPU or AMD)
                //   4. CPU emitter (slowest; really a "fallback")
                ILGPU.Runtime.Device cudaPreferred = null;
                ILGPU.Runtime.Device cudaBiggest = null;
                long cudaBiggestMem = -1;
                ILGPU.Runtime.Device openclDev = null;
                ILGPU.Runtime.Device cpuDev = null;
                var enumerated = new System.Text.StringBuilder();
                foreach (var dev in _context.Devices)
                {
                    enumerated.AppendLine(
                        $"  [{dev.AcceleratorType}] {dev.Name} " +
                        $"mem={dev.MemorySize / (1024L * 1024L)}MB");
                    if (dev.AcceleratorType == ILGPU.Runtime.AcceleratorType.Cuda)
                    {
                        if (!string.IsNullOrEmpty(PreferredDeviceNameSubstring)
                            && dev.Name != null
                            && dev.Name.IndexOf(PreferredDeviceNameSubstring,
                                StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            cudaPreferred = dev;
                        }
                        if (dev.MemorySize > cudaBiggestMem)
                        {
                            cudaBiggestMem = dev.MemorySize;
                            cudaBiggest = dev;
                        }
                    }
                    else if (dev.AcceleratorType == ILGPU.Runtime.AcceleratorType.OpenCL)
                    {
                        if (openclDev == null) openclDev = dev;
                    }
                    else if (dev.AcceleratorType == ILGPU.Runtime.AcceleratorType.CPU)
                    {
                        if (cpuDev == null) cpuDev = dev;
                    }
                }
                var chosen = cudaPreferred ?? cudaBiggest ?? openclDev ?? cpuDev;
                if (chosen != null) _accelerator = chosen.CreateAccelerator(_context);

                if (_accelerator == null)
                {
                    _diagnostic = "no compatible ILGPU accelerator found\n" + enumerated;
                    _available = false;
                    return;
                }
                SelectedDeviceName = _accelerator.Name ?? "(unnamed)";
                SelectedDeviceType = _accelerator.AcceleratorType.ToString();
                _kernel = _accelerator.LoadAutoGroupedStreamKernel<
                    ILGPU.Index2D,
                    ILGPU.ArrayView<float>,
                    ILGPU.ArrayView<float>,
                    ILGPU.ArrayView<float>,
                    int, int, int>(MatMulKernel);
                _diagnostic = $"OK: {_accelerator.AcceleratorType} '{_accelerator.Name}' " +
                              $"({_accelerator.MemorySize / (1024L * 1024L)}MB)";
                _available = true;
            }
            catch (Exception e)
            {
                _diagnostic = $"init failed: {e.GetType().Name}: {e.Message}";
                _available = false;
            }
            finally
            {
                _initialised = true;
            }
        }
    }

    /// <summary>The GPU kernel: each thread computes ONE output element
    /// c[i, j] = sum_k a[i, k] * b[k, j]. ILGPU dispatches the 2D
    /// (M, N) grid; the kernel scalar-loops over K.</summary>
    private static void MatMulKernel(
        ILGPU.Index2D idx,
        ILGPU.ArrayView<float> a,
        ILGPU.ArrayView<float> b,
        ILGPU.ArrayView<float> c,
        int M, int K, int N)
    {
        int i = idx.X;
        int j = idx.Y;
        if (i >= M || j >= N) return;
        float sum = 0f;
        int aRow = i * K;
        for (int k = 0; k < K; k++)
            sum += a[aRow + k] * b[k * N + j];
        c[i * N + j] = sum;
    }

    /// <summary>
    /// C[M,N] = A[M,K] * B[K,N]. Same contract as
    /// <see cref="Matmul.MatMul(float[], float[], float[], int, int, int)"/>
    /// but runs on the GPU. Throws if <see cref="IsAvailable"/> is false.
    /// </summary>
    public static void MatMul(float[] a, float[] b, float[] c, int M, int K, int N)
    {
        if (!IsAvailable)
            throw new InvalidOperationException($"GpuMatmul not available: {Diagnostic}");
        using var aBuf = _accelerator.Allocate1D<float>(M * K);
        using var bBuf = _accelerator.Allocate1D<float>(K * N);
        using var cBuf = _accelerator.Allocate1D<float>(M * N);
        // Stage host -> device. ILGPU 1.5.x's MemoryBuffer1D supports
        // CopyFromCPU(array) directly.
        var aSlice = new float[M * K];
        var bSlice = new float[K * N];
        Array.Copy(a, aSlice, M * K);
        Array.Copy(b, bSlice, K * N);
        aBuf.CopyFromCPU(aSlice);
        bBuf.CopyFromCPU(bSlice);
        _kernel(new Index2D(M, N), aBuf.View, bBuf.View, cBuf.View, M, K, N);
        _accelerator.Synchronize();
        var cSlice = new float[M * N];
        cBuf.CopyToCPU(cSlice);
        Array.Copy(cSlice, c, M * N);
    }

    /// <summary>One-shot cleanup. Optional. ILGPU disposes on AppDomain
    /// unload automatically; call this only if you want deterministic
    /// shutdown.</summary>
    public static void Dispose()
    {
        lock (_lock)
        {
            try { _accelerator?.Dispose(); } catch { }
            try { _context?.Dispose(); } catch { }
            _accelerator = null;
            _context = null;
            _kernel = null;
            _available = false;
            _initialised = false;
            _diagnostic = "(disposed)";
        }
    }
}
