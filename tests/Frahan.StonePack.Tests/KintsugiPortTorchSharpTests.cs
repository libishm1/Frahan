#nullable disable
using System;
using System.IO;
using Frahan.Kintsugi.Port.Outer;
using Frahan.Kintsugi.Port.Weights;

namespace Frahan.Tests;

// =============================================================================
// TorchSharp parallel-path smoke tests. TorchSharp adds ~200 MB of
// libtorch native DLLs to the deploy folder when properly set up;
// these tests detect whether libtorch is available at runtime and
// skip gracefully if not.
//
// The TorchSharp path is OPT-IN: the manual port via
// EncoderWeightLoader remains the default. TorchSharp gives access
// to PyTorch's optimised CPU/CUDA kernels for benchmarking and
// for users with libtorch already installed.
// =============================================================================

static class KintsugiPortTorchSharpTests
{
    private static readonly string KintsugiBinPath =
        @"D:\code_ws\Template-General\outputs\2026-05-22\reference\kintsugi.bin";

    public static void TorchSharp_LibtorchAvailable_Report()
    {
        try
        {
            // Smoke: try to allocate a 1x1 tensor. If libtorch is missing,
            // this throws DllNotFoundException at runtime.
            var t = TorchSharp.torch.zeros(new long[] { 1 });
            Console.WriteLine($"        TorchSharp libtorch: OK (allocated {t.numel()} elements)");
            t.Dispose();
        }
        catch (DllNotFoundException e)
        {
            Console.WriteLine($"        TorchSharp libtorch unavailable: {e.Message}");
            Console.WriteLine($"        ... Manual port + ILGPU still works; TorchSharp is opt-in.");
            // Skip-with-info, not a hard failure.
        }
        catch (Exception e)
        {
            Console.WriteLine($"        TorchSharp init failed: {e.GetType().Name}: {e.Message}");
        }
    }

    public static void TorchSharp_EncoderPathLoadsWeights()
    {
        if (!File.Exists(KintsugiBinPath))
        { Console.WriteLine($"        info: kintsugi.bin missing -- skip"); return; }
        try
        {
            // Probe libtorch first. If unavailable, skip.
            var probe = TorchSharp.torch.zeros(new long[] { 1 });
            probe.Dispose();
        }
        catch (Exception e)
        {
            Console.WriteLine($"        info: libtorch unavailable ({e.GetType().Name}) -- skip");
            return;
        }
        // Real load test.
        var reader = new WeightReader(KintsugiBinPath);
        using var ts = new TorchSharpEncoderPath(reader);
        int count = 0;
        foreach (var n in ts.ParameterNames) count++;
        AssertTrue(count > 0, "TorchSharp encoder loaded zero parameters");
        Console.WriteLine($"        TorchSharp encoder loaded {count} ae.* parameters");
    }

    private static void AssertTrue(bool ok, string msg)
    {
        if (!ok) throw new Exception(msg);
    }
}
