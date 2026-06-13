#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace Frahan.EdgeMatching;

// =============================================================================
// LiveEdgeScribeMatcher -- fits a classified offcut into a slot bounded by two
// "river" seams and scribes (trims) its two live edges to them. The scribe-and-
// fill / river gap-fill move from live-edge and plank-flooring practice: align
// the board to the laid seam, then shave the curve to close the gap.
//
// Drives Live Edge Match (D5F10044, candidate cost + assignment) and Live Edge
// Trim (D5F10045, the trimmed outline + sliver). The assignment cost it builds
// is the same bipartite cost the HungarianAssigner consumes for the optional
// global-optimal layup mode.
//
// Rhino-LIGHT: Point3d used only as a value container.
// =============================================================================

public static class LiveEdgeScribeMatcher
{
    public sealed class ScribeResult
    {
        public double Dy;
        public double MeanTrim;
        public double MaxTrim;
        public Point3d[] ScribedBottom;   // on the lower river, left -> right
        public Point3d[] ScribedTop;      // on the upper river, left -> right
        public Point3d[] NaturalBottom;   // board's bottom live edge (pre-trim), placed
        public Point3d[] NaturalTop;
        public Point3d[] Outline;         // closed scribed board boundary
        public Point3d[] BottomSliver;    // trimmed strip (scribed vs natural), bottom
        public Point3d[] TopSliver;
    }

    // Sample a per-x array by normalized parameter t in [0,1] (linear interp).
    private static double SampleNormalized(double[] a, double t)
    {
        if (a.Length == 1) return a[0];
        double f = Math.Max(0, Math.Min(1, t)) * (a.Length - 1);
        int i = (int)Math.Floor(f);
        if (i >= a.Length - 1) return a[a.Length - 1];
        double frac = f - i;
        return a[i] + (a[i + 1] - a[i]) * frac;
    }

    // Scribe a board (resampled to slotWidth) onto the two rivers over [x0, x0+slotWidth].
    public static ScribeResult Scribe(LiveEdgeBoard board, double slotWidth, double[] riverBelow, double[] riverAbove, double dx, double x0)
    {
        int nx = riverBelow.Length;
        int Gi(double x) { int g = (int)Math.Round(x / dx); return g < 0 ? 0 : (g >= nx ? nx - 1 : g); }

        int le = Math.Max(2, (int)Math.Round(slotWidth / dx) + 1);
        // Clip so we never index past the floor width.
        while (le > 2 && x0 + (le - 1) * dx > (nx - 1) * dx) le--;

        var bN = new double[le];
        var tN = new double[le];
        double sum = 0;
        for (int i = 0; i < le; i++)
        {
            double t = slotWidth > 1e-9 ? (i * dx) / slotWidth : 0;
            bN[i] = SampleNormalized(board.BottomY, t);
            tN[i] = SampleNormalized(board.TopY, t);
            sum += riverBelow[Gi(x0 + i * dx)] - bN[i];
        }
        double dy = sum / le;

        var scrB = new Point3d[le];
        var scrT = new Point3d[le];
        var natB = new Point3d[le];
        var natT = new Point3d[le];
        double trimSum = 0, trimMax = 0;
        for (int i = 0; i < le; i++)
        {
            double gx = x0 + i * dx;
            double rb = riverBelow[Gi(gx)];
            double ra = riverAbove[Gi(gx)];
            scrB[i] = new Point3d(gx, rb, 0);
            scrT[i] = new Point3d(gx, ra, 0);
            natB[i] = new Point3d(gx, bN[i] + dy, 0);
            natT[i] = new Point3d(gx, tN[i] + dy, 0);
            double tb = Math.Abs((bN[i] + dy) - rb);
            double tt = Math.Abs((tN[i] + dy) - ra);
            trimSum += tb + tt;
            if (tb > trimMax) trimMax = tb;
            if (tt > trimMax) trimMax = tt;
        }

        var outline = new List<Point3d>();
        outline.AddRange(scrB);
        for (int i = le - 1; i >= 0; i--) outline.Add(scrT[i]);
        outline.Add(scrB[0]);

        var botSliver = new List<Point3d>();
        botSliver.AddRange(scrB);
        for (int i = le - 1; i >= 0; i--) botSliver.Add(natB[i]);
        botSliver.Add(scrB[0]);
        var topSliver = new List<Point3d>();
        topSliver.AddRange(scrT);
        for (int i = le - 1; i >= 0; i--) topSliver.Add(natT[i]);
        topSliver.Add(scrT[0]);

        return new ScribeResult
        {
            Dy = dy,
            MeanTrim = trimSum / le,
            MaxTrim = trimMax,
            ScribedBottom = scrB,
            ScribedTop = scrT,
            NaturalBottom = natB,
            NaturalTop = natT,
            Outline = outline.ToArray(),
            BottomSliver = botSliver.ToArray(),
            TopSliver = topSliver.ToArray()
        };
    }

    // The assignment cost = mean scribe trim (lower is better) of a board in a slot.
    public static double SlotCost(LiveEdgeBoard board, double slotWidth, double[] riverBelow, double[] riverAbove, double dx, double x0)
    {
        return Scribe(board, slotWidth, riverBelow, riverAbove, dx, x0).MeanTrim;
    }
}
