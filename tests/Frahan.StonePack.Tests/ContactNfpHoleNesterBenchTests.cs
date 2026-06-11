#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Frahan.Packing.TwoD;

namespace Frahan.Tests;

// CNH head-to-head bench. Reconstructs the SAME class of true-hole instance the
// Sparrow/native study used (1 sheet + 1 sheet-hole, host parts WITH holes,
// filler parts), runs the Rhino-free CNH core, and prints validity/density/ms.
// REPORTED (asserts only sanity + validity). Reference numbers reproduced on
// this machine 2026-06-12: native nester 21.6 ms valid; Sparrow 3255 ms INVALID
// (hole-blind). See outputs/2026-06-12/hole_packer_evolution.
static class ContactNfpHoleNesterBenchTests
{
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

        // warm + timed
        ContactNfpHoleNester.Pack(sheet, sheetHoles, parts, spacing: 0.0);
        HoleNestResult r = null;
        double best = double.MaxValue;
        for (int t = 0; t < 5; t++)
        {
            var rr = ContactNfpHoleNester.Pack(sheet, sheetHoles, parts, spacing: 0.0);
            if (rr.ElapsedMs < best) { best = rr.ElapsedMs; r = rr; }
        }

        Console.WriteLine("      [bench] CNH true-hole (clean-room Frahan hole nester):");
        Console.WriteLine($"      [bench]   placed {r.PlacedCount}/{parts.Count}, part-holes filled {r.PartHolesFilled}, " +
                          $"valid={r.Valid}, density={r.Density:0.000}, {best:0.0} ms (best of 5)");
        if (!r.Valid) Console.WriteLine($"      [bench]   INVALID: {r.Note}");
        Console.WriteLine($"      [bench]   reference (this machine): native nester 21.6 ms valid | Sparrow 3255 ms INVALID (hole-blind)");
        if (best < 21.6 && r.Valid)
            Console.WriteLine($"      [bench]   => CNH valid and {21.6 / best:0.00}x faster than the fastest valid baseline; " +
                              $">100x vs Sparrow's invalid result on the same input");

        if (!r.Valid) throw new Exception("CNH produced an invalid hole-aware layout");
        if (r.PlacedCount == 0) throw new Exception("CNH placed nothing");
        if (r.PartHolesFilled < 1) throw new Exception("CNH filled no part-holes (Phase A inactive)");
    }

    private static List<(double X, double Y)> Rect(double x0, double y0, double x1, double y1) =>
        new List<(double X, double Y)> { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };
}
