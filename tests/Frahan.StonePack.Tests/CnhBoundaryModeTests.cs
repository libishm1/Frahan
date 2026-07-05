#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Frahan.Packing.TwoD;

namespace Frahan.Tests;

// CNH boundary-mode (evolution of V506's ComputeBoundaryAffinity, see the
// header doc in ContactNfpHoleNester.cs). boundaryMode=0 must stay byte-
// identical to the pre-existing engine; boundaryMode=1 must measurably pull
// outer parts toward the sheet rim, spread across DISTINCT rim stretches
// instead of piling on one, and fall back cleanly to plain bottom-left when
// no candidate clears the contact threshold.
static class CnhBoundaryModeTests
{
    public static void BoundaryModeOff_IsByteIdenticalToBaseline()
    {
        var sheet = Rect(0, 0, 150, 100);
        var parts = MixedRects();
        var noHoles = new List<IReadOnlyList<(double X, double Y)>>();

        // "the old default overload" = call Pack without naming boundaryMode
        // at all; boundaryMode:0 explicit must produce the exact same result.
        var baseline = ContactNfpHoleNester.Pack(sheet, noHoles, parts, spacing: 1.5, enableRectFastPath: false);
        var explicitOff = ContactNfpHoleNester.Pack(sheet, noHoles, parts, spacing: 1.5, enableRectFastPath: false,
            boundaryMode: 0, minBoundaryContact: 0.25);

        Console.WriteLine($"      [bench] CNH boundaryMode=0 parity: baseline placed {baseline.PlacedCount}, explicit-off placed {explicitOff.PlacedCount}");
        if (!baseline.Valid) throw new Exception("baseline layout invalid: " + baseline.Note);
        AssertSamePlacements("boundaryMode:0 vs default overload", baseline, explicitOff);
    }

    public static void BoundaryHug_RectanglesReachTheRim()
    {
        var sheet = Rect(0, 0, 200, 200);
        var parts = new List<HoleNestPart>();
        // ResampledRect, not the raw 4-corner Rect(): BoundaryContactLength's
        // tol assumes the "already resampled at the caller's res" fine
        // vertex pitch documented on the engine (a bare 4-corner rectangle
        // has only 4 samples, each carrying a huge Voronoi-length share, so
        // the coarse-vertex tol swamps the sheet and both modes score full
        // rim contact — see ContactTol's header doc).
        for (int i = 0; i < 8; i++) parts.Add(new HoleNestPart { Outer = ResampledRect(0, 0, 30, 18, 2.0) });
        var noHoles = new List<IReadOnlyList<(double X, double Y)>>();

        var hug = ContactNfpHoleNester.Pack(sheet, noHoles, parts, spacing: 2.0, enableRectFastPath: false,
            boundaryMode: 1, minBoundaryContact: 0.25);
        var bl = ContactNfpHoleNester.Pack(sheet, noHoles, parts, spacing: 2.0, enableRectFastPath: false,
            boundaryMode: 0);

        if (!hug.Valid) throw new Exception("boundary-hug layout invalid: " + hug.Note);
        if (!bl.Valid) throw new Exception("bottom-left layout invalid: " + bl.Note);

        int hugRim = CountRimContact(hug, sheet, 2.0, 0.25);
        int blRim = CountRimContact(bl, sheet, 2.0, 0.25);
        Console.WriteLine($"      [bench] CNH boundary-hug rectangles: placed {hug.PlacedCount}/8, rim-contact hug={hugRim} bl={blRim}");

        if (hug.PlacedCount < 8) throw new Exception($"boundary-hug only placed {hug.PlacedCount}/8 rectangles");
        if (hugRim < 5) throw new Exception($"boundary-hug only put {hugRim}/8 rectangles in rim contact (need >= 5)");
        if (hugRim <= blRim) throw new Exception($"boundary-hug rim contact ({hugRim}) did not beat plain bottom-left ({blRim})");
    }

    public static void BoundaryHug_SpreadsWithoutClustering()
    {
        var sheet = Rect(0, 0, 300, 300);
        var parts = new List<HoleNestPart>();
        for (int i = 0; i < 4; i++) parts.Add(new HoleNestPart { Outer = ResampledRect(0, 0, 20, 20, 2.0) });
        var noHoles = new List<IReadOnlyList<(double X, double Y)>>();

        var hug = ContactNfpHoleNester.Pack(sheet, noHoles, parts, spacing: 2.0, enableRectFastPath: false,
            boundaryMode: 1, minBoundaryContact: 0.25);
        if (!hug.Valid) throw new Exception("boundary-hug layout invalid: " + hug.Note);
        if (hug.PlacedCount != 4) throw new Exception($"boundary-hug only placed {hug.PlacedCount}/4 squares");

        var arcsPerPlacement = new List<List<(double T0, double T1)>>();
        foreach (var pl in hug.Placements)
        {
            double tol = ContactTol(pl.PlacedOuter, 2.0);
            ContactNfpHoleNester.BoundaryContactLength(pl.PlacedOuter, sheet, tol, out var arcs);
            arcsPerPlacement.Add(arcs);
        }

        Console.WriteLine($"      [bench] CNH boundary-hug spread: {arcsPerPlacement.Count} placements, arc-interval counts = " +
                          string.Join(",", arcsPerPlacement.Select(a => a.Count)));

        for (int i = 0; i < arcsPerPlacement.Count; i++)
            for (int j = i + 1; j < arcsPerPlacement.Count; j++)
            {
                double overlap = TotalOverlap(arcsPerPlacement[i], arcsPerPlacement[j]);
                if (overlap > 1e-6)
                    throw new Exception($"placements {i} and {j} share {overlap:0.###} of rim arc (expected pairwise non-overlapping)");
            }
    }

    public static void HighThreshold_FallsBackToBottomLeft()
    {
        var sheet = Rect(0, 0, 150, 100);
        var parts = MixedRects();
        var noHoles = new List<IReadOnlyList<(double X, double Y)>>();

        var bl = ContactNfpHoleNester.Pack(sheet, noHoles, parts, spacing: 1.5, enableRectFastPath: false,
            boundaryMode: 0);
        // minBoundaryContact:10.0 is impossible (contact can never reach 10x
        // a perimeter) -> every part must fall back to the exact bottom-left
        // placement, so the two layouts must match exactly.
        var hugImpossible = ContactNfpHoleNester.Pack(sheet, noHoles, parts, spacing: 1.5, enableRectFastPath: false,
            boundaryMode: 1, minBoundaryContact: 10.0);

        Console.WriteLine($"      [bench] CNH boundary-hug impossible-threshold fallback: bl placed {bl.PlacedCount}, hug placed {hugImpossible.PlacedCount}");
        AssertSamePlacements("impossible-threshold fallback vs bottom-left", bl, hugImpossible);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static List<HoleNestPart> MixedRects() => new List<HoleNestPart>
    {
        new HoleNestPart { Outer = Rect(0, 0, 30, 20) },
        new HoleNestPart { Outer = Rect(0, 0, 25, 15) },
        new HoleNestPart { Outer = Rect(0, 0, 40, 10) },
        new HoleNestPart { Outer = Rect(0, 0, 15, 15) },
        new HoleNestPart { Outer = Rect(0, 0, 20, 30) },
        new HoleNestPart { Outer = Rect(0, 0, 10, 10) },
    };

    private static void AssertSamePlacements(string label, HoleNestResult a, HoleNestResult b)
    {
        if (a.Placements.Count != b.Placements.Count)
            throw new Exception($"CNH {label}: placement count differs ({a.Placements.Count} vs {b.Placements.Count})");
        for (int i = 0; i < a.Placements.Count; i++)
        {
            var pa = a.Placements[i]; var pb = b.Placements[i];
            if (pa.PartIndex != pb.PartIndex || Math.Abs(pa.AngleRad - pb.AngleRad) > 1e-12 ||
                Math.Abs(pa.Tx - pb.Tx) > 1e-9 || Math.Abs(pa.Ty - pb.Ty) > 1e-9 ||
                pa.NestedInHost != pb.NestedInHost || pa.HostIndex != pb.HostIndex || pa.HostHole != pb.HostHole)
                throw new Exception($"CNH {label}: placement {i} differs (part {pa.PartIndex} vs {pb.PartIndex}, " +
                                    $"ang {pa.AngleRad} vs {pb.AngleRad}, tx {pa.Tx} vs {pb.Tx}, ty {pa.Ty} vs {pb.Ty})");
        }
    }

    // Same formula the engine uses internally (see BoundaryContactLength's
    // header doc): tol = spacing + 2.5 x mean vertex spacing of the placed
    // outline. Recomputed here (caller-space, unscaled) purely from the
    // public Pack() output, so the test never depends on engine-internal
    // scaled-space state.
    private static double ContactTol(IReadOnlyList<(double X, double Y)> placedOuter, double spacing)
    {
        int n = placedOuter.Count;
        double perim = 0;
        for (int i = 0; i < n; i++)
        {
            var u = placedOuter[i]; var v = placedOuter[(i + 1) % n];
            perim += Math.Sqrt((v.X - u.X) * (v.X - u.X) + (v.Y - u.Y) * (v.Y - u.Y));
        }
        double meanSpacing = perim / Math.Max(1, n);
        return spacing + 2.5 * meanSpacing;
    }

    private static double LoopPerimeter(IReadOnlyList<(double X, double Y)> loop)
    {
        double p = 0; int n = loop.Count;
        for (int i = 0; i < n; i++)
        {
            var a = loop[i]; var b = loop[(i + 1) % n];
            p += Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        }
        return p;
    }

    private static int CountRimContact(HoleNestResult r, IReadOnlyList<(double X, double Y)> sheet, double spacing, double minFraction)
    {
        int count = 0;
        foreach (var pl in r.Placements)
        {
            double tol = ContactTol(pl.PlacedOuter, spacing);
            double contact = ContactNfpHoleNester.BoundaryContactLength(pl.PlacedOuter, sheet, tol, out _);
            if (contact >= minFraction * LoopPerimeter(pl.PlacedOuter)) count++;
        }
        return count;
    }

    private static double TotalOverlap(List<(double T0, double T1)> a, List<(double T0, double T1)> b)
    {
        double sum = 0;
        foreach (var x in a)
            foreach (var y in b)
            {
                double lo = Math.Max(x.T0, y.T0), hi = Math.Min(x.T1, y.T1);
                if (hi > lo) sum += hi - lo;
            }
        return sum;
    }

    private static List<(double X, double Y)> Rect(double x0, double y0, double x1, double y1) =>
        new List<(double X, double Y)> { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };

    // dense polyline rectangle: mimics a curve-sampled GH part outline (the
    // codebase's stated convention that loops arrive "already resampled at
    // the caller's res"), which is the regime BoundaryContactLength's tol
    // formula is designed for.
    private static List<(double X, double Y)> ResampledRect(double x0, double y0, double x1, double y1, double step)
    {
        var pts = new List<(double X, double Y)>();
        void AddEdge(double ax, double ay, double bx, double by)
        {
            double len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
            int n = Math.Max(1, (int)Math.Round(len / step));
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / n;
                pts.Add((ax + (bx - ax) * t, ay + (by - ay) * t));
            }
        }
        AddEdge(x0, y0, x1, y0);
        AddEdge(x1, y0, x1, y1);
        AddEdge(x1, y1, x0, y1);
        AddEdge(x0, y1, x0, y0);
        return pts;
    }
}
