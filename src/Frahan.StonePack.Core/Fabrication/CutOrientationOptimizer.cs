#nullable disable
using System;
using System.Collections.Generic;
using System.Text;
using Rhino.Geometry;
using Frahan.Core.Discontinuity;

namespace Frahan.Core.Fabrication;

// =============================================================================
// CutOrientationOptimizer -- choose the orientation of a rectangular (three
// mutually-orthogonal) saw-cut grid so the extracted blocks are right prisms AND
// the cuts follow the natural joint fabric as closely as possible. This is the
// geology->fabrication decision the marble-quarry literature makes by hand
// (Lorgino / Palissandro: only foliation-parallel cuts above ~72 deg dip yield
// right-prism blocks): an orthogonal cut grid gives rectangular blocks by
// construction (q = |det| = 1), and the free parameter is how you rotate that
// grid against the rock. Aligning a cut plane to a joint set means cutting ALONG
// the natural plane -- cheaper, cleaner faces, less wire. Where the fabric is not
// orthogonal, some obliquity is unavoidable; the optimizer reports it.
//
// Objective: maximise sum_i |c_i . n_match(i)| over an orthonormal cut frame
// {c1,c2,c3}, greedily matching each cut normal to a distinct joint pole. The
// per-cut obliquity = acos(|c_i . n_match(i)|) is the angle the cut makes to the
// nearest natural joint (0 = cutting along a joint).
//
// Modes:
//   - Bench-constrained (default): one cut is the horizontal bench floor
//     (normal +Z); the two vertical cuts are optimised over strike azimuth. 1 DOF.
//   - Free: the full orthonormal frame is optimised over SO(3) by a
//     Fibonacci-hemisphere x roll search. 3 DOF.
//
// References:
//   - block-volume / right-prism criterion V = s1 s2 s3 / q, q = |det(n1,n2,n3)|
//     (Palmstrom 2005; the 2024 Rock Mech Rock Eng IBSD paper's q thresholds).
//   - Diamond-wire dimension-stone extraction practice (foliation-aligned cuts).
//
// Pure managed arithmetic (Rhino value types), deterministic, headless-testable.
// =============================================================================

/// <summary>Optimal rectangular saw-cut grid orientation for a jointed rock mass.</summary>
public sealed class CutOrientationResult
{
    /// <summary>The three orthonormal cut-plane normals (the saw grid).</summary>
    public Vector3d[] CutNormals = new Vector3d[3];
    /// <summary>Per-cut dip (deg).</summary>
    public double[] CutDip = new double[3];
    /// <summary>Per-cut dip-direction (deg).</summary>
    public double[] CutDipDir = new double[3];
    /// <summary>Which joint set (0-based) each cut follows; -1 if none.</summary>
    public int[] FollowsSet = new int[3];
    /// <summary>Per-cut obliquity (deg): angle between the cut and its nearest joint (0 = along a joint).</summary>
    public double[] ObliquityDeg = new double[3];
    public double MaxObliquityDeg, MeanObliquityDeg;
    /// <summary>Fabric-fit score in [0,1] (mean |cos obliquity|; 1 = every cut on a joint).</summary>
    public double FitScore;
    /// <summary>Right-prism-ness of the natural 3-set block, q = |det(n1,n2,n3)| (the grid's is 1).</summary>
    public double NaturalQ;
    public bool BenchConstrained;
    public string Report = "";
}

public static class CutOrientationOptimizer
{
    private const double R2D = 180.0 / Math.PI;

    /// <summary>
    /// Find the best rectangular cut-grid orientation for the given joint-set poles.
    /// Poles need not be unit. <paramref name="benchConstrained"/> pins one cut to the
    /// horizontal bench floor and optimises the two vertical cuts' azimuth.
    /// </summary>
    public static CutOrientationResult Optimize(
        IReadOnlyList<Vector3d> jointPoles,
        bool benchConstrained = true,
        int azSteps = 180,
        int hemiSamples = 500,
        int rollSteps = 30)
    {
        var res = new CutOrientationResult { BenchConstrained = benchConstrained };
        var poles = new List<Vector3d>();
        if (jointPoles != null)
            foreach (var p in jointPoles) { var u = p; if (u.SquareLength > 1e-18) { u.Unitize(); poles.Add(OrientationMath.LowerHemisphere(u)); } }
        if (poles.Count == 0) { res.Report = "No joint sets."; return res; }

        // natural-fabric right-prism-ness (3 dominant = first 3)
        res.NaturalQ = poles.Count >= 3 ? Math.Abs(Det(poles[0], poles[1], poles[2])) : double.NaN;

        Vector3d[] best = null; double bestScore = double.NegativeInfinity;

        if (benchConstrained)
        {
            var z = Vector3d.ZAxis;
            for (int a = 0; a < azSteps; a++)
            {
                double th = Math.PI * a / azSteps;      // 0..180 deg strike
                var c1 = new Vector3d(Math.Cos(th), Math.Sin(th), 0);
                var c2 = new Vector3d(-Math.Sin(th), Math.Cos(th), 0);
                double s = Score(new[] { z, c1, c2 }, poles, out _);
                if (s > bestScore) { bestScore = s; best = new[] { z, c1, c2 }; }
            }
        }
        else
        {
            double ga = Math.PI * (3.0 - Math.Sqrt(5.0));  // golden angle
            for (int k = 0; k < hemiSamples; k++)
            {
                double zc = 1.0 - (k + 0.5) / hemiSamples;  // upper hemisphere
                double r = Math.Sqrt(Math.Max(0, 1 - zc * zc));
                double phi = k * ga;
                var c1 = new Vector3d(r * Math.Cos(phi), r * Math.Sin(phi), zc);
                // base perpendicular to c1
                var b0 = Math.Abs(c1.Z) < 0.9 ? Vector3d.CrossProduct(c1, Vector3d.ZAxis) : Vector3d.CrossProduct(c1, Vector3d.XAxis);
                b0.Unitize();
                var b1 = Vector3d.CrossProduct(c1, b0); b1.Unitize();
                for (int rr = 0; rr < rollSteps; rr++)
                {
                    double roll = Math.PI * rr / rollSteps;   // 0..180 (c2 and -c2 same plane)
                    var c2 = Math.Cos(roll) * b0 + Math.Sin(roll) * b1; c2.Unitize();
                    var c3 = Vector3d.CrossProduct(c1, c2); c3.Unitize();
                    double s = Score(new[] { c1, c2, c3 }, poles, out _);
                    if (s > bestScore) { bestScore = s; best = new[] { c1, c2, c3 }; }
                }
            }
        }

        // finalise the best frame
        Score(best, poles, out int[] match);
        double sumCos = 0, maxOb = 0;
        for (int i = 0; i < 3; i++)
        {
            var c = OrientationMath.LowerHemisphere(best[i]);
            res.CutNormals[i] = c;
            var dd = OrientationMath.DipDipDir(c);
            res.CutDip[i] = dd.dip; res.CutDipDir[i] = dd.dipDir;
            res.FollowsSet[i] = match[i];
            double cos = match[i] >= 0 ? Math.Abs(Dot(c, poles[match[i]])) : 0.0;
            double ob = Math.Acos(Math.Min(1.0, cos)) * R2D;
            res.ObliquityDeg[i] = ob;
            sumCos += cos; maxOb = Math.Max(maxOb, ob);
        }
        res.MaxObliquityDeg = maxOb;
        res.MeanObliquityDeg = (res.ObliquityDeg[0] + res.ObliquityDeg[1] + res.ObliquityDeg[2]) / 3.0;
        res.FitScore = sumCos / 3.0;
        res.Report = BuildReport(res, poles.Count);
        return res;
    }

    // greedy-unique match of the 3 cut normals to distinct joint poles, score = sum |dot|.
    private static double Score(Vector3d[] frame, List<Vector3d> poles, out int[] match)
    {
        match = new[] { -1, -1, -1 };
        var used = new bool[poles.Count];
        double sum = 0;
        // assign in order of each axis' best available match, greedily by strongest pair first
        var pairs = new List<(double d, int ax, int pl)>();
        for (int a = 0; a < 3; a++)
            for (int p = 0; p < poles.Count; p++)
                pairs.Add((Math.Abs(Dot(frame[a], poles[p])), a, p));
        pairs.Sort((x, y) => y.d.CompareTo(x.d));
        var axDone = new bool[3];
        foreach (var pr in pairs)
        {
            if (axDone[pr.ax] || used[pr.pl]) continue;
            match[pr.ax] = pr.pl; used[pr.pl] = true; axDone[pr.ax] = true; sum += pr.d;
        }
        return sum;
    }

    private static double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static double Det(Vector3d n1, Vector3d n2, Vector3d n3)
        => n1.X * (n2.Y * n3.Z - n2.Z * n3.Y)
         - n1.Y * (n2.X * n3.Z - n2.Z * n3.X)
         + n1.Z * (n2.X * n3.Y - n2.Y * n3.X);

    private static string BuildReport(CutOrientationResult r, int nSets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Cut-orientation optimizer (rectangular saw grid vs joint fabric).");
        sb.AppendLine($"  mode: {(r.BenchConstrained ? "bench-constrained (one horizontal cut + optimised vertical grid)" : "free 3-axis")}");
        sb.AppendLine($"  {nSets} joint sets; natural-block right-prism q = {(double.IsNaN(r.NaturalQ) ? "n/a (<3 sets)" : r.NaturalQ.ToString("F2"))} -> orthogonal cut grid gives q = 1.00 (right prisms).");
        sb.AppendLine("  Optimal cut planes (dip / dip-dir):");
        string[] tag = { "cut A", "cut B", "cut C" };
        for (int i = 0; i < 3; i++)
        {
            string follows = r.FollowsSet[i] >= 0 ? $"follows set S{r.FollowsSet[i] + 1}, obliquity {r.ObliquityDeg[i]:F0} deg" : "no matching set";
            sb.AppendLine($"    {tag[i]}: {r.CutDip[i]:F0} / {r.CutDipDir[i]:F0}   ({follows})");
        }
        sb.AppendLine($"  fabric fit {r.FitScore * 100:F0}%   max obliquity {r.MaxObliquityDeg:F0} deg (the unavoidable oblique cut)");
        if (r.MaxObliquityDeg > 20)
            sb.AppendLine("  ! At least one cut is >20 deg off the nearest joint: expect more wire-sawing / rougher faces on that cut, or accept oblique blocks.");
        else
            sb.AppendLine("  Cuts follow the fabric closely: cheap, clean splits and rectangular blocks.");
        return sb.ToString().TrimEnd();
    }
}
