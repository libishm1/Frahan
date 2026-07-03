#nullable disable
using System;
using System.Collections.Generic;
using System.Text;
using Rhino.Geometry;
using RGSurf = Rhino.Geometry.Surface;

namespace Frahan.Core.Fabrication;

// =============================================================================
// WireSawFeasibility -- pre-CAM manufacturability check for a diamond wire-saw
// (or multi-wire / robotic wire) cut. A tensioned wire is straight at every
// instant, so the cut surface it sweeps is a RULED surface: the fundamental
// wire-sawability condition is that the target cut surface be ruled (straight
// lines in one parameter direction = the successive wire positions). A ruled
// surface that is also DEVELOPABLE (zero Gaussian curvature) unrolls flat and is
// the cleanest single-pass cut; a ruled but doubly-curved surface (e.g. a
// hyperbolic paraboloid) is still wire-sawable but twists the wire.
//
// This is the stone-specific check no general CAM does: it tells you, before the
// machine, whether a designed cut can be made with a straight wire, and emits the
// kerf-compensated toolpath surface.
//
// Kerf/tolerance budget (JCDE 2024, robotic diamond-wire cutting):
//     Delta = (D + delta) / 2      D = wire diameter, delta = vibration/positioning error
//   toolpath = cut surface offset by Delta along its normal.
//
// References:
//   - Steuben, Michopoulos et al.; do Carmo, Differential Geometry (ruled /
//     developable surfaces: K = 0 <=> developable; ruled <=> S(u,v)=C(u)+v a(u)).
//   - Robotic diamond-wire cutting of natural stone (J. Computational Design and
//     Engineering, 2024): developable ruled cut, orthogonality C'(u).a(u)=0,
//     kerf offset Delta=(D+delta)/2, +/-2 mm demonstrated accuracy.
//
// Operates on RhinoCommon geometry (Surface), pure computation (no doc/runtime).
// =============================================================================

/// <summary>Wire-saw manufacturability verdict for one target cut surface.</summary>
public sealed class WireSawResult
{
    /// <summary>The surface is (near) ruled -- straight lines in one parameter direction.</summary>
    public bool IsRuled;
    /// <summary>Direction of the rulings: 0 = along U (v-isocurves straight), 1 = along V, -1 = none.</summary>
    public int RulingDirection = -1;
    /// <summary>Max deviation of a ruling from its chord (model units).</summary>
    public double MaxRulingDeviation;
    /// <summary>The surface is (near) developable -- Gaussian curvature ~ 0 everywhere.</summary>
    public bool IsDevelopable;
    /// <summary>Max |Gaussian curvature| x charLen^2 (dimensionless; ~0 = developable).</summary>
    public double MaxGaussianScaled;
    /// <summary>The surface is planar (the trivial wire-sawable case).</summary>
    public bool IsPlanar;
    /// <summary>Wire-sawable = planar OR ruled (a straight wire can sweep it).</summary>
    public bool WireSawable;
    /// <summary>Max twist between consecutive rulings (deg) -- fast twist is hard to wire-saw.</summary>
    public double MaxRulingTwistDeg;
    /// <summary>Kerf/tolerance offset Delta = (D+delta)/2 (model units).</summary>
    public double KerfOffset;
    /// <summary>The cut surface offset by the kerf along its normal (the toolpath surface); null if offset failed.</summary>
    public RGSurf OffsetSurface;
    /// <summary>The successive wire positions (rulings) for preview; empty if not ruled.</summary>
    public List<Line> Rulings = new List<Line>();
    public List<string> Notes = new List<string>();
    public string Report = "";
}

public static class WireSawFeasibility
{
    private const double R2D = 180.0 / Math.PI;

    /// <summary>
    /// Analyze a target cut surface for wire-saw feasibility.
    /// <paramref name="wireDiameter"/> and <paramref name="vibration"/> are in model
    /// units (kerf Delta = (D+delta)/2). <paramref name="devTolFrac"/> is the ruling-
    /// straightness tolerance as a fraction of the surface diagonal.
    /// </summary>
    public static WireSawResult Analyze(
        RGSurf srf,
        double wireDiameter,
        double vibration,
        double devTolFrac = 0.005,
        int samples = 12)
    {
        var res = new WireSawResult();
        if (srf == null) { res.Report = "No surface."; return res; }
        int n = Math.Max(4, samples);
        res.KerfOffset = 0.5 * (Math.Max(0, wireDiameter) + Math.Max(0, vibration));

        var du = srf.Domain(0); var dv = srf.Domain(1);
        double charLen = Math.Max(1e-9, srf.GetBoundingBox(true).Diagonal.Length);
        double tol = Math.Max(1e-9, devTolFrac * charLen);

        // sample grid
        var P = new Point3d[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                P[i, j] = srf.PointAt(du.ParameterAt(i / (n - 1.0)), dv.ParameterAt(j / (n - 1.0)));

        // planar?
        res.IsPlanar = srf.TryGetPlane(out Plane _pl, tol);

        // ruledness: v-isocurves straight (fix i, vary j) -> rulings along V; and u-isocurves.
        double devV = 0; for (int i = 0; i < n; i++) devV = Math.Max(devV, ChordDeviation(P, i, true, n));
        double devU = 0; for (int j = 0; j < n; j++) devU = Math.Max(devU, ChordDeviation(P, j, false, n));

        bool ruledV = devV <= tol, ruledU = devU <= tol;
        if (ruledV || ruledU)
        {
            res.IsRuled = true;
            // pick the straighter direction
            if (ruledV && (!ruledU || devV <= devU)) { res.RulingDirection = 1; res.MaxRulingDeviation = devV; }
            else { res.RulingDirection = 0; res.MaxRulingDeviation = devU; }
            BuildRulings(res, P, n);
        }
        else res.MaxRulingDeviation = Math.Min(devU, devV);

        // developability: max |Gaussian| over the grid, scaled
        double maxK = 0;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                var c = srf.CurvatureAt(du.ParameterAt(i / (n - 1.0)), dv.ParameterAt(j / (n - 1.0)));
                if (c != null) maxK = Math.Max(maxK, Math.Abs(c.Gaussian));
            }
        res.MaxGaussianScaled = maxK * charLen * charLen;
        res.IsDevelopable = res.MaxGaussianScaled <= 1e-2;   // ~1% of unit dimensionless curvature

        res.WireSawable = res.IsPlanar || res.IsRuled;

        // kerf-compensated toolpath surface
        if (res.KerfOffset > 1e-12)
        {
            try
            {
                var off = srf.Offset(res.KerfOffset, tol);
                if (off != null) res.OffsetSurface = off;
                else res.Notes.Add("Kerf offset surface could not be generated (self-intersection?).");
            }
            catch { res.Notes.Add("Kerf offset threw; skipped."); }
        }

        res.Report = BuildReport(res, tol, charLen);
        return res;
    }

    // max distance of interior points of one grid line from its chord.
    // alongV: fix row i, vary column j. else: fix column i, vary row j.
    private static double ChordDeviation(Point3d[,] P, int fixedIdx, bool alongV, int n)
    {
        Point3d a = alongV ? P[fixedIdx, 0] : P[0, fixedIdx];
        Point3d b = alongV ? P[fixedIdx, n - 1] : P[n - 1, fixedIdx];
        var chord = new Line(a, b);
        // Degenerate chord = a closed / periodic isocurve (its ends coincide at a
        // seam). That is NOT a straight ruling: return the loop's spread from the
        // endpoint so it fails the ruled test (else a cylinder/sphere seam reads as
        // a zero-deviation "ruling").
        if (chord.Length < 1e-12)
        {
            double dd = 0;
            for (int k = 1; k < n - 1; k++)
            {
                Point3d p = alongV ? P[fixedIdx, k] : P[k, fixedIdx];
                dd = Math.Max(dd, p.DistanceTo(a));
            }
            return dd;
        }
        double d = 0;
        for (int k = 1; k < n - 1; k++)
        {
            Point3d p = alongV ? P[fixedIdx, k] : P[k, fixedIdx];
            d = Math.Max(d, p.DistanceTo(chord.ClosestPoint(p, true)));
        }
        return d;
    }

    private static void BuildRulings(WireSawResult res, Point3d[,] P, int n)
    {
        res.Rulings.Clear();
        var dirs = new List<Vector3d>();
        if (res.RulingDirection == 1)          // rulings along V: for each row i, line P[i,0]->P[i,n-1]
            for (int i = 0; i < n; i++) { var L = new Line(P[i, 0], P[i, n - 1]); res.Rulings.Add(L); dirs.Add(L.Direction); }
        else                                    // rulings along U
            for (int j = 0; j < n; j++) { var L = new Line(P[0, j], P[n - 1, j]); res.Rulings.Add(L); dirs.Add(L.Direction); }
        // twist = max angle between consecutive rulings
        double tw = 0;
        for (int k = 1; k < dirs.Count; k++)
        {
            var x = dirs[k - 1]; var y = dirs[k]; x.Unitize(); y.Unitize();
            double dot = Math.Abs(x.X * y.X + x.Y * y.Y + x.Z * y.Z);
            tw = Math.Max(tw, Math.Acos(Math.Min(1.0, dot)) * R2D);
        }
        res.MaxRulingTwistDeg = tw;
    }

    private static string BuildReport(WireSawResult r, double tol, double charLen)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Wire-saw feasibility (diamond wire / robotic wire cut).");
        sb.AppendLine($"  surface diagonal {charLen:G3}; ruling tol {tol:G3} (model units)");
        sb.AppendLine($"  planar:      {(r.IsPlanar ? "YES" : "no")}");
        sb.AppendLine($"  ruled:       {(r.IsRuled ? "YES (rulings along " + (r.RulingDirection == 1 ? "V" : "U") + ")" : "NO")}  (max ruling deviation {r.MaxRulingDeviation:G3})");
        sb.AppendLine($"  developable: {(r.IsDevelopable ? "YES" : "no")}  (max |K| x L^2 = {r.MaxGaussianScaled:G3})");
        if (r.IsRuled) sb.AppendLine($"  max ruling twist {r.MaxRulingTwistDeg:F1} deg" + (r.MaxRulingTwistDeg > 45 ? "  ! high twist -- slow the feed or multi-pass" : ""));
        sb.AppendLine($"  kerf offset Delta = {r.KerfOffset:G3} (=(D+delta)/2)" + (r.OffsetSurface != null ? "  -> toolpath surface emitted" : ""));
        sb.AppendLine();
        if (r.WireSawable)
            sb.AppendLine(r.IsPlanar ? "  VERDICT: wire-sawable (planar cut)."
                : r.IsDevelopable ? "  VERDICT: wire-sawable (developable ruled cut -- clean single pass)."
                : "  VERDICT: wire-sawable (ruled but doubly-curved -- wire twists; watch feed).");
        else
            sb.AppendLine("  VERDICT: NOT wire-sawable with a straight wire -- the cut is doubly-curved and non-ruled. " +
                          "Use milling / multi-plane guillotine, or re-design the cut as a ruled surface.");
        foreach (var note in r.Notes) sb.AppendLine("  ! " + note);
        return sb.ToString().TrimEnd();
    }
}
