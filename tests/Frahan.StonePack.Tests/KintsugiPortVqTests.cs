#nullable disable
using System;
using System.IO;
using Frahan.Kintsugi.Port.Weights;

namespace Frahan.Tests;

// Diagnostic to find the VQ codebook tensor in kintsugi.bin.
static class KintsugiPortVqTests
{
    private static readonly string KintsugiBinPath =
        @"D:\code_ws\Template-General\outputs\2026-05-22\reference\kintsugi.bin";

    public static void Vq_FindCodebookTensor()
    {
        if (!File.Exists(KintsugiBinPath))
        { Console.WriteLine("        info: kintsugi.bin missing -- skip"); return; }
        var reader = new WeightReader(KintsugiBinPath);
        int hits = 0;
        foreach (var n in reader.Names)
        {
            if (n.Contains("quantization") || n.Contains("codebook") ||
                n.Contains("embedding") && n.StartsWith("ae."))
            {
                var shape = reader.GetShape(n);
                Console.WriteLine($"        VQ candidate: {n}  shape=[{string.Join(",", shape)}]");
                hits++;
            }
        }
        Console.WriteLine($"        total VQ-related tensor matches: {hits}");
    }
}
