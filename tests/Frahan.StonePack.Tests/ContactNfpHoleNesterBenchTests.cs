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

    // MULTI-START (2026-06-13): the density lever HoleNest lacked vs the
    // reference physics nester. Pack() now wraps the general engine in a loop
    // over K deterministic part orders (area / max-dim / width / height desc) and
    // keeps the densest valid layout. This test asserts the three contract
    // properties on an irregular instance that does NOT route to the rect
    // fast-path (so multi-start is actually exercised):
    //   1. K>1 places >= the single-pass count (never loses placements),
    //   2. the multi-start layout stays VALID (depth-gate certified),
    //   3. the result is DETERMINISTIC — the same K>1 inputs reproduce the
    //      EXACT same placements across 3 independent solves.
    public static void Cnh_MultiStart_DenserOrEqual_Valid_Deterministic()
    {
        var sheet = Rect(0, 0, 150, 110);
        var parts = MultiStartBlobs(25);

        var single = ContactNfpHoleNester.Pack(sheet,
            new List<IReadOnlyList<(double X, double Y)>>(), parts, multiStartOrders: 1);
        var multi = ContactNfpHoleNester.Pack(sheet,
            new List<IReadOnlyList<(double X, double Y)>>(), parts, multiStartOrders: 4);

        Console.WriteLine("      [bench] CNH multi-start (25 irregular blobs, general engine):");
        Console.WriteLine($"      [bench]   single K=1: engine=[{single.Note}], placed {single.PlacedCount}/{parts.Count}, " +
                          $"valid={single.Valid}, density={single.Density:0.0000}, {single.ElapsedMs:0.0} ms");
        Console.WriteLine($"      [bench]   multi  K=4: engine=[{multi.Note}], placed {multi.PlacedCount}/{parts.Count}, " +
                          $"valid={multi.Valid}, density={multi.Density:0.0000}, {multi.ElapsedMs:0.0} ms");
        double densityGainPct = single.Density > 1e-9 ? 100.0 * (multi.Density - single.Density) / single.Density : 0.0;
        double timeMult = single.ElapsedMs > 1e-6 ? multi.ElapsedMs / single.ElapsedMs : 0.0;
        Console.WriteLine($"      [bench]   => density gain {densityGainPct:+0.00;-0.00}% , time multiplier {timeMult:0.0}x");

        if (single.Note == null || !single.Note.StartsWith("general-nfp"))
            throw new Exception("multi-start test must run the general engine (rect fast-path is exact), got " + single.Note);
        if (!single.Valid) throw new Exception("single-pass baseline INVALID: " + single.Note);
        if (!multi.Valid) throw new Exception("multi-start layout INVALID: " + multi.Note);
        if (multi.PlacedCount < single.PlacedCount)
            throw new Exception($"multi-start lost placements: {multi.PlacedCount} < single {single.PlacedCount}");
        if (multi.Note == null || !multi.Note.Contains("multi-start"))
            throw new Exception("multi-start engine note missing: " + multi.Note);

        // determinism: 3 independent K=4 solves must be byte-identical in
        // placements (part index, angle, translation) — keep-best is total-order.
        var a = ContactNfpHoleNester.Pack(sheet, new List<IReadOnlyList<(double X, double Y)>>(), parts, multiStartOrders: 4);
        var b = ContactNfpHoleNester.Pack(sheet, new List<IReadOnlyList<(double X, double Y)>>(), parts, multiStartOrders: 4);
        var c = ContactNfpHoleNester.Pack(sheet, new List<IReadOnlyList<(double X, double Y)>>(), parts, multiStartOrders: 4);
        AssertSamePlacements(a, b, "run1 vs run2");
        AssertSamePlacements(b, c, "run2 vs run3");
        Console.WriteLine($"      [bench]   determinism: 3x K=4 identical ({a.PlacedCount} placements each)");

        // K=1 must reproduce the original single-pass path: engine note has NO
        // multi-start tag, and the layout is byte-identical to the NO-ARGUMENT
        // default call (proves existing call sites are unchanged).
        if (single.Note.Contains("multi-start"))
            throw new Exception("K=1 must not engage multi-start: " + single.Note);
        var legacyDefault = ContactNfpHoleNester.Pack(sheet,
            new List<IReadOnlyList<(double X, double Y)>>(), parts);
        AssertSamePlacements(single, legacyDefault, "explicit K=1 vs no-arg default");
    }

    // MULTI-START DENSITY A/B (2026-06-13): the honest measurement the task
    // demands. Runs K=1 vs K=4 on >=3 irregular instances, including TIGHT ones
    // where not every part fits, so multi-start can place MORE (the real density
    // lever). Reports placed/density/time per instance + the K=4/K=1 wall-time
    // multiplier. REPORTED (asserts only the contract: K=4 >= K=1 placements and
    // both valid; density gain is data-dependent and printed, not asserted).
    public static void Cnh_MultiStart_DensityAB_Reported()
    {
        Console.WriteLine("      [bench] CNH multi-start density A/B (K=1 vs K=4, general engine):");
        Console.WriteLine("      [bench]   instance                | K=1 placed/dens/ms     | K=4 placed/dens/ms (order)            | +placed +dens% xTime");

        // 3 irregular instances at deliberately TIGHT sheet sizes. blobs() areas
        // sum to roughly the sheet area so some parts spill -> multi-start has a
        // placement lever, not just a compaction lever.
        AbRow("25 blobs / tight 95x70", Rect(0, 0, 95, 70), MultiStartBlobs(25));
        AbRow("40 blobs / tight 120x95", Rect(0, 0, 120, 95), MultiStartBlobs(40));
        AbRow("18 shields / tight 95x60", Rect(0, 0, 95, 60), ShieldParts(18));
        AbRow("30 blobs / tight 110x80", Rect(0, 0, 110, 80), MultiStartBlobs(30));
        AbRow("25 blobs / loose 150x110", Rect(0, 0, 150, 110), MultiStartBlobs(25)); // slack control: expect ~0% gain

        // per-K sweep on the most placement-sensitive instance: how much of the
        // placed-count win is captured by K=2/K=3 vs the full K=4 (cost vs lever)
        Console.WriteLine("      [bench]   per-K sweep (25 blobs / tight 95x70): placed / density / ms");
        var sheet = Rect(0, 0, 95, 70); var parts = MultiStartBlobs(25);
        var empty = new List<IReadOnlyList<(double X, double Y)>>();
        for (int kk = 1; kk <= 4; kk++)
        {
            ContactNfpHoleNester.Pack(sheet, empty, parts, multiStartOrders: kk); // warm
            var r = ContactNfpHoleNester.Pack(sheet, empty, parts, multiStartOrders: kk);
            Console.WriteLine($"      [bench]     K={kk}: {r.PlacedCount}/{parts.Count}  dens={r.Density:0.0000}  {r.ElapsedMs:0.0} ms");
        }
    }

    private static void AbRow(string label, IReadOnlyList<(double X, double Y)> sheet, List<HoleNestPart> parts)
    {
        var empty = new List<IReadOnlyList<(double X, double Y)>>();
        // warm
        ContactNfpHoleNester.Pack(sheet, empty, parts, multiStartOrders: 1);
        ContactNfpHoleNester.Pack(sheet, empty, parts, multiStartOrders: 4);
        var s = ContactNfpHoleNester.Pack(sheet, empty, parts, multiStartOrders: 1);
        var m = ContactNfpHoleNester.Pack(sheet, empty, parts, multiStartOrders: 4);

        if (!s.Valid) throw new Exception($"AB '{label}' K=1 INVALID: {s.Note}");
        if (!m.Valid) throw new Exception($"AB '{label}' K=4 INVALID: {m.Note}");
        if (m.PlacedCount < s.PlacedCount)
            throw new Exception($"AB '{label}' K=4 lost placements: {m.PlacedCount} < {s.PlacedCount}");

        string order = "";
        int oi = m.Note != null ? m.Note.IndexOf("best order ") : -1;
        if (oi >= 0) { int e = m.Note.IndexOf(')', oi); order = m.Note.Substring(oi + 11, Math.Max(0, e - oi - 11)); }
        double densPct = s.Density > 1e-9 ? 100.0 * (m.Density - s.Density) / s.Density : 0.0;
        double xTime = s.ElapsedMs > 1e-6 ? m.ElapsedMs / s.ElapsedMs : 0.0;
        Console.WriteLine($"      [bench]   {label,-24}| {s.PlacedCount,2}/{parts.Count} {s.Density:0.0000} {s.ElapsedMs,7:0.0}ms | " +
                          $"{m.PlacedCount,2}/{parts.Count} {m.Density:0.0000} {m.ElapsedMs,7:0.0}ms ({order}) | " +
                          $"+{m.PlacedCount - s.PlacedCount} {densPct:+0.00;-0.00}% {xTime:0.0}x");
    }

    private static List<HoleNestPart> ShieldParts(int count)
    {
        var parts = new List<HoleNestPart>(count);
        for (int k = 0; k < count; k++)
            parts.Add(new HoleNestPart { Outer = Shield(0.7 + 0.05 * (k % 6)) });
        return parts;
    }

    private static void AssertSamePlacements(HoleNestResult x, HoleNestResult y, string label)
    {
        if (x.Placements.Count != y.Placements.Count)
            throw new Exception($"multi-start non-deterministic ({label}): {x.Placements.Count} vs {y.Placements.Count} placements");
        for (int i = 0; i < x.Placements.Count; i++)
        {
            var p = x.Placements[i]; var q = y.Placements[i];
            if (p.PartIndex != q.PartIndex ||
                Math.Abs(p.AngleRad - q.AngleRad) > 1e-12 ||
                Math.Abs(p.Tx - q.Tx) > 1e-9 || Math.Abs(p.Ty - q.Ty) > 1e-9)
                throw new Exception($"multi-start non-deterministic ({label}) at placement {i}: " +
                    $"({p.PartIndex},{p.AngleRad:0.######},{p.Tx:0.######},{p.Ty:0.######}) vs " +
                    $"({q.PartIndex},{q.AngleRad:0.######},{q.Tx:0.######},{q.Ty:0.######})");
        }
    }

    // 25 irregular convex-ish blobs of varied size + aspect ratio. NOT rectangles
    // (forces the general engine) and varied enough that area/width/height orders
    // differ, so multi-start has distinct passes to choose between. Deterministic
    // (seeded by index, no RNG).
    private static List<HoleNestPart> MultiStartBlobs(int count)
    {
        var parts = new List<HoleNestPart>(count);
        for (int k = 0; k < count; k++)
        {
            double rx = 6.0 + (k % 5) * 2.2;          // half-width 6..14.8
            double ry = 5.0 + ((k * 3) % 7) * 1.6;    // half-height 5..14.6 (aspect varies vs rx)
            int sides = 5 + (k % 4);                  // 5..8 sides (irregular, non-rect)
            double phase = 0.21 * k;                  // rotate the vertex pattern per part
            var loop = new List<(double X, double Y)>(sides);
            for (int s = 0; s < sides; s++)
            {
                double ang = phase + 2.0 * Math.PI * s / sides;
                // mild radius wobble so edges are not all tangent to one ellipse
                double wob = 1.0 + 0.12 * Math.Sin(3.0 * ang + k);
                loop.Add((rx * wob * Math.Cos(ang), ry * wob * Math.Sin(ang)));
            }
            parts.Add(new HoleNestPart { Outer = loop });
        }
        return parts;
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
