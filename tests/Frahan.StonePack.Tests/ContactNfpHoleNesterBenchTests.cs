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

    // REGRESSION (user report 2026-06-12): irregular shield-shaped sampled parts
    // overlapped on canvas ("general-nfp | part 0 overlaps another part").
    // Root cause: the general path trusted the Minkowski NFP blindly; concave /
    // sampled shapes have NFP coverage gaps, so placement needs an exact
    // boolean verification per candidate (the sibling nester's safety net).
    public static void Cnh_Irregular_Shields_GeneralValid()
    {
        var sheet = Rect(0, 0, 160, 110);
        var parts = new List<HoleNestPart>();
        for (int k = 0; k < 7; k++)
            parts.Add(new HoleNestPart { Outer = Shield(0.85 + 0.07 * k) });

        var r = ContactNfpHoleNester.Pack(sheet,
            new List<IReadOnlyList<(double X, double Y)>>(), parts);
        Console.WriteLine($"      [bench] CNH shields: engine={r.Note}, placed {r.PlacedCount}/7, valid={r.Valid}, {r.ElapsedMs:0.0} ms");
        if (!r.Note.StartsWith("general-nfp")) throw new Exception("shields must route to the general engine, got " + r.Note);
        if (!r.Valid) throw new Exception("INVALID layout on irregular shields: " + r.Note);
        if (r.PlacedCount < 6) throw new Exception($"only {r.PlacedCount}/7 shields placed");
    }

    // REGRESSION: small part vs much larger obstacle, general engine forced.
    // The boundary-sweep NFP has an interior hole (part fully inside obstacle)
    // that must never be selectable as a placement.
    public static void Cnh_FullyInside_GeneralValid()
    {
        var sheet = Rect(0, 0, 200, 120);
        var parts = new List<HoleNestPart>
        {
            new HoleNestPart { Outer = Rect(0, 0, 90, 90) },  // big solid square
            new HoleNestPart { Outer = Rect(0, 0, 8, 8) },     // small square
            new HoleNestPart { Outer = Rect(0, 0, 8, 8) },
        };
        var r = ContactNfpHoleNester.Pack(sheet,
            new List<IReadOnlyList<(double X, double Y)>>(), parts,
            enableRectFastPath: false);
        Console.WriteLine($"      [bench] CNH fully-inside: engine={r.Note}, placed {r.PlacedCount}/3, valid={r.Valid}");
        if (!r.Valid) throw new Exception("INVALID layout on fully-inside case: " + r.Note);
        if (r.PlacedCount != 3) throw new Exception($"placed {r.PlacedCount}/3");
    }

    // REGRESSION (the actual canvas trigger): the same shields drawn at
    // arbitrary WORLD positions. Before the normalization + t-space cull fix,
    // a part whose coordinate origin sat far from the part itself made the
    // obstacle cull compare translation space against world space, silently
    // dropping live obstacles -> stacked, overlapping parts.
    public static void Cnh_Irregular_Shields_WorldOffset_GeneralValid()
    {
        var sheet = Rect(0, 0, 160, 110);
        var parts = new List<HoleNestPart>();
        for (int k = 0; k < 7; k++)
        {
            var loop = Shield(0.85 + 0.07 * k);
            var moved = new List<(double X, double Y)>(loop.Count);
            foreach (var v in loop) moved.Add((v.X + 37.0 + 11.0 * k, v.Y + 18.0 + 3.0 * k));
            parts.Add(new HoleNestPart { Outer = moved });
        }
        var r = ContactNfpHoleNester.Pack(sheet,
            new List<IReadOnlyList<(double X, double Y)>>(), parts);
        Console.WriteLine($"      [bench] CNH shields@world-offset: engine={r.Note}, placed {r.PlacedCount}/7, valid={r.Valid}, {r.ElapsedMs:0.0} ms");
        if (!r.Valid) throw new Exception("INVALID layout on world-offset shields: " + r.Note);
        if (r.PlacedCount < 6) throw new Exception($"only {r.PlacedCount}/7 placed");
    }

    // NATIVE NFP KERNEL BENCH (2026-06-12): the 7-shield instance is the
    // profiled case where managed Minkowski NFP builds were ~95% of solve
    // time. Runs the SAME pack 5x in-process and prints every time + median,
    // tagged with the lane that ran (Note carries "+native-nfp" when the
    // batched native kernel did the Minkowski work). Drive the A/B from the
    // shell: FRAHAN_NFP_NATIVE=0 forces the managed lane in a process where
    // nfp_kernel.dll is present; alternate processes >=3x and compare medians
    // (machine drifts +-12% thermally — only interleaved medians count).
    public static void Cnh_Shields_NativeKernel_Bench()
    {
        var sheet = Rect(0, 0, 160, 110);
        var parts = new List<HoleNestPart>();
        for (int k = 0; k < 7; k++)
            parts.Add(new HoleNestPart { Outer = Shield(0.85 + 0.07 * k) });

        // warm-up (JIT + native probe), then 5 measured runs
        var warm = ContactNfpHoleNester.Pack(sheet,
            new List<IReadOnlyList<(double X, double Y)>>(), parts);
        var times = new List<double>(5);
        HoleNestResult last = null;
        for (int t = 0; t < 5; t++)
        {
            last = ContactNfpHoleNester.Pack(sheet,
                new List<IReadOnlyList<(double X, double Y)>>(), parts);
            times.Add(last.ElapsedMs);
        }
        var sorted = times.OrderBy(x => x).ToList();
        double median = sorted[2];
        string lane = last.Note != null && last.Note.Contains("+native-nfp") ? "native" : "managed";
        Console.WriteLine($"      [bench] CNH shields x5 lane={lane} engine=[{last.Note}]");
        Console.WriteLine($"      [bench]   times ms: {string.Join(", ", times.Select(x => x.ToString("0.0")))}");
        Console.WriteLine($"      [bench]   MEDIAN_MS={median:0.0} LANE={lane} placed={last.PlacedCount}/7 valid={last.Valid}");
        if (!last.Valid) throw new Exception("INVALID layout in native-kernel bench: " + last.Note);
        if (last.PlacedCount != warm.PlacedCount)
            throw new Exception($"placement drift across runs: {last.PlacedCount} vs warm {warm.PlacedCount}");
    }

    // Shield-ish sampled polygon: convex top arc, concave flanks, bottom point —
    // mimics curve-sampled GH input (the reported failure class).
    private static List<(double X, double Y)> Shield(double s)
    {
        var pts = new List<(double X, double Y)>();
        for (int i = 0; i <= 8; i++)        // top arc (convex bulge)
        {
            double u = i / 8.0;
            pts.Add(((-10 + 20 * u) * s, (24 + 2.2 * Math.Sin(Math.PI * u)) * s));
        }
        for (int i = 1; i <= 9; i++)        // right flank, curving inward (concave)
        {
            double u = i / 9.0;
            pts.Add(((10 * (1 - u) + 2.5 * Math.Sin(Math.PI * u)) * s, (24 * (1 - u) * (1 - u)) * s));
        }
        for (int i = 1; i < 9; i++)         // left flank back up (concave)
        {
            double u = i / 9.0;
            pts.Add(((-10 * u - 2.5 * Math.Sin(Math.PI * u)) * s, (24 * u * u) * s));
        }
        return pts;
    }

    private static List<(double X, double Y)> Rect(double x0, double y0, double x1, double y1) =>
        new List<(double X, double Y)> { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };
}
