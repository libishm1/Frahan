#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Frahan.Packing.TwoD;

namespace Frahan.Tests;

// CNH head-to-head bench. Reconstructs the SAME class of true-hole instance the
// Sparrow/native study used (1 sheet + 1 sheet-hole, host parts WITH holes,
// filler parts) and times BOTH CNH engines:
//   * fast    = rect shelf fast-path (exact special case, default on)
//   * general = exact-NFP engine (forced via enableRectFastPath:false)
// REPORTED (asserts validity + exact placement counts for both engines).
// Reference numbers reproduced on this machine 2026-06-12: native nester
// 21.6 ms valid; Sparrow 3255 ms INVALID (hole-blind). See
// outputs/2026-06-12/hole_packer_evolution.
static class ContactNfpHoleNesterBenchTests
{
    private const double NativeRefMs = 21.6;
    private const double SparrowRefMs = 3255.0;

    public static void Cnh_TrueHole_ValidAndTimed()
    {
        // sheet 120 x 80 with one rectangular defect (sheet-hole)
        var sheet = Rect(0, 0, 120, 80);
        var sheetHoles = new List<IReadOnlyList<(double X, double Y)>> { Rect(50, 55, 70, 75) };

        // 4 host parts: 26x26 squares each with a 16x16 square hole (room for a filler)
        var parts = new List<HoleNestPart>();
        for (int i = 0; i < 4; i++)
            parts.Add(new HoleNestPart
            {
                Outer = Rect(0, 0, 26, 26),
                Holes = new List<IReadOnlyList<(double X, double Y)>> { Rect(5, 5, 21, 21) }
            });
        // 8 fillers: 14x14 squares (fit inside the 16x16 host holes, 4 used; rest pack outside)
        for (int i = 0; i < 8; i++)
            parts.Add(new HoleNestPart { Outer = Rect(0, 0, 14, 14) });

        // warm both engines, then best-of-5 each
        ContactNfpHoleNester.Pack(sheet, sheetHoles, parts, spacing: 0.0);
        ContactNfpHoleNester.Pack(sheet, sheetHoles, parts, spacing: 0.0, enableRectFastPath: false);
        var (fast, fastMs) = BestOf5(sheet, sheetHoles, parts, enableRectFastPath: true);
        var (gen, genMs) = BestOf5(sheet, sheetHoles, parts, enableRectFastPath: false);

        Console.WriteLine("      [bench] CNH true-hole (clean-room Frahan hole nester, both engines):");
        Console.WriteLine($"      [bench]   fast    [{fast.Note}]: placed {fast.PlacedCount}/{parts.Count}, part-holes filled {fast.PartHolesFilled}, " +
                          $"valid={fast.Valid}, density={fast.Density:0.000}, {fastMs:0.000} ms (best of 5)");
        Console.WriteLine($"      [bench]   general [{gen.Note}]: placed {gen.PlacedCount}/{parts.Count}, part-holes filled {gen.PartHolesFilled}, " +
                          $"valid={gen.Valid}, density={gen.Density:0.000}, {genMs:0.0} ms (best of 5)");
        if (!fast.Valid) Console.WriteLine($"      [bench]   fast INVALID: {fast.Note}");
        if (!gen.Valid) Console.WriteLine($"      [bench]   general INVALID: {gen.Note}");
        Console.WriteLine($"      [bench]   reference (this machine): native nester {NativeRefMs:0.0} ms valid | Sparrow {SparrowRefMs:0} ms INVALID (hole-blind)");
        Console.WriteLine($"      [bench]   => fast path {NativeRefMs / fastMs:0.0}x faster than the {NativeRefMs:0.0} ms native reference, " +
                          $"{SparrowRefMs / fastMs:0}x faster than Sparrow's {SparrowRefMs:0} ms invalid result");

        AssertMode("fast", fast, parts.Count, expectedEngine: "rect-shelf");
        AssertMode("general", gen, parts.Count, expectedEngine: "general-nfp");
    }

    private static (HoleNestResult Result, double BestMs) BestOf5(
        IReadOnlyList<(double X, double Y)> sheet,
        List<IReadOnlyList<(double X, double Y)>> sheetHoles,
        List<HoleNestPart> parts, bool enableRectFastPath)
    {
        HoleNestResult best = null;
        double bestMs = double.MaxValue;
        for (int t = 0; t < 5; t++)
        {
            var r = ContactNfpHoleNester.Pack(sheet, sheetHoles, parts, spacing: 0.0,
                enableRectFastPath: enableRectFastPath);
            if (r.ElapsedMs < bestMs) { bestMs = r.ElapsedMs; best = r; }
        }
        return (best, bestMs);
    }

    private static void AssertMode(string label, HoleNestResult r, int partCount, string expectedEngine)
    {
        if (!r.Valid) throw new Exception($"CNH {label} produced an invalid hole-aware layout: {r.Note}");
        if (r.PlacedCount != partCount) throw new Exception($"CNH {label} placed {r.PlacedCount}/{partCount}");
        if (r.PartHolesFilled != 4) throw new Exception($"CNH {label} filled {r.PartHolesFilled}/4 part-holes");
        if (r.Note == null || !r.Note.StartsWith(expectedEngine))
            throw new Exception($"CNH {label} ran engine '{r.Note}', expected '{expectedEngine}'");
    }

    private static List<(double X, double Y)> Rect(double x0, double y0, double x1, double y1) =>
        new List<(double X, double Y)> { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };
}
