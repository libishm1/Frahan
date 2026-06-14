#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Core.Discontinuity;

// =============================================================================
// BlockSizeMath -- in-situ block-size descriptors from a set of discontinuity
// orientations + normal spacings. Pure managed arithmetic (Rhino value types
// only), headless-unit-testable.
//
// References (rock-mechanics standard):
//   - Palmstrom, A. (1995, 2005). Volumetric joint count Jv and block volume Vb.
//       Vb = s1*s2*s3 / (sin g12 * sin g23 * sin g31)   (3 dominant sets)
//       Jv = sum_j 1/s_j   [joints / m per set, summed -> joints/m^3 proxy]
//       RQD ~= 110 - 2.5*Jv  (clamped 0..100; Palmstrom 1974 correlation)
//   - ISRM Suggested Methods: spacing measured along the set normal.
//
// UNITS WARNING (see HANDOFF_05 sec B.3): spacings come from the worker in the
// cloud's own units. A detail scan in metres with 3-12 mm spacings yields
// Jv ~ hundreds/m and RQD = 0 -- physically meaningless at that scale. Callers
// MUST pass the correct unitScale (multiplies spacing -> metres) and TREAT the
// output as a proxy, labelling the spacing units. s_j <= eps and S < 3 are
// guarded here (single planes / slabs / columns are not blocks).
// =============================================================================

/// <summary>Block-size descriptors for a jointed rock mass.</summary>
public sealed class BlockSizeResult
{
    /// <summary>Number of distinct sets actually used for the block volume (0..3).</summary>
    public int SetsUsed;
    /// <summary>Volumetric joint count, joints/m^3 proxy (sum of 1/spacing over all valid sets).</summary>
    public double Jv;
    /// <summary>Palmstrom block volume (m^3). NaN if fewer than 3 bounding sets.</summary>
    public double Vb;
    /// <summary>Block-size index Ib = mean of the 3 dominant spacings (m). NaN if &lt; 3 sets.</summary>
    public double Ib;
    /// <summary>Equivalent block diameter Vb^(1/3) (m). NaN if undefined.</summary>
    public double Deq;
    /// <summary>RQD proxy (0..100) from Jv.</summary>
    public double Rqd;
    /// <summary>Human-readable shape descriptor (e.g. "blocky", "tabular (2 sets -> slabs)").</summary>
    public string Descriptor = "";
    /// <summary>Inter-set acute angles (deg) for the dominant sets, [i,j]; diagonal 0.</summary>
    public double[,] Gamma;
    /// <summary>True when a finite block volume could be computed (>= 3 non-parallel sets).</summary>
    public bool VolumeDefined;
    /// <summary>Spacing unit label echoed for display (set by the caller).</summary>
    public string SpacingUnits = "";
    public List<string> Notes = new List<string>();
}

public static class BlockSizeMath
{
    private const double Eps = 1e-9;
    private const double D2R = Math.PI / 180.0;

    /// <summary>
    /// Compute block-size descriptors from per-set poles + spacings.
    /// <paramref name="poles"/> need not be unit (they are normalised here).
    /// <paramref name="unitScale"/> multiplies spacing into metres.
    /// <paramref name="share"/> (optional) selects the 3 dominant sets by point
    /// share; if null, the first 3 valid sets (assumed dominance-ordered) are used.
    /// </summary>
    public static BlockSizeResult Compute(
        IReadOnlyList<Vector3d> poles,
        IReadOnlyList<double> spacings,
        double unitScale = 1.0,
        IReadOnlyList<double> share = null,
        string spacingUnits = "")
    {
        if (poles == null) throw new ArgumentNullException(nameof(poles));
        if (spacings == null) throw new ArgumentNullException(nameof(spacings));
        if (poles.Count != spacings.Count)
            throw new ArgumentException("poles and spacings differ in length");
        if (unitScale <= 0) unitScale = 1.0;

        var res = new BlockSizeResult { SpacingUnits = spacingUnits };

        // valid sets = positive spacing after scaling
        var valid = new List<int>();
        for (int i = 0; i < poles.Count; i++)
        {
            double s = spacings[i] * unitScale;
            if (s > Eps && poles[i].SquareLength > Eps) valid.Add(i);
        }

        // Jv over ALL valid sets
        double jv = 0;
        foreach (int i in valid) jv += 1.0 / (spacings[i] * unitScale);
        res.Jv = jv;
        res.Rqd = Math.Max(0.0, Math.Min(100.0, 110.0 - 2.5 * jv));

        if (valid.Count < 3)
        {
            res.SetsUsed = valid.Count;
            res.Vb = double.NaN; res.Ib = double.NaN; res.Deq = double.NaN;
            res.VolumeDefined = false;
            res.Descriptor = valid.Count switch
            {
                0 => "no spaced sets (massive / single facet)",
                1 => "one set -> tabular slabs (unbounded in 2 directions)",
                _ => "two sets -> columns (unbounded along the mutual line)"
            };
            res.Notes.Add("Block volume needs >= 3 non-parallel sets; reported as a shape only.");
            return res;
        }

        // pick the 3 dominant sets
        var order = new List<int>(valid);
        if (share != null && share.Count == poles.Count)
            order.Sort((a, b) => share[b].CompareTo(share[a])); // by share desc
        var pick = order.GetRange(0, 3);

        double s1 = spacings[pick[0]] * unitScale;
        double s2 = spacings[pick[1]] * unitScale;
        double s3 = spacings[pick[2]] * unitScale;
        var n1 = Unit(poles[pick[0]]);
        var n2 = Unit(poles[pick[1]]);
        var n3 = Unit(poles[pick[2]]);

        double g12 = AxialDeg(n1, n2), g23 = AxialDeg(n2, n3), g31 = AxialDeg(n3, n1);
        res.Gamma = new double[3, 3];
        res.Gamma[0, 1] = res.Gamma[1, 0] = g12;
        res.Gamma[1, 2] = res.Gamma[2, 1] = g23;
        res.Gamma[2, 0] = res.Gamma[0, 2] = g31;

        double sin12 = Math.Sin(g12 * D2R), sin23 = Math.Sin(g23 * D2R), sin31 = Math.Sin(g31 * D2R);
        const double minSin = 0.087; // ~5 deg: below this two sets are effectively parallel
        if (sin12 < minSin || sin23 < minSin || sin31 < minSin)
        {
            res.SetsUsed = 3;
            res.Vb = double.NaN; res.Ib = (s1 + s2 + s3) / 3.0; res.Deq = double.NaN;
            res.VolumeDefined = false;
            res.Descriptor = "near-parallel dominant sets -> volume ill-conditioned";
            res.Notes.Add("Two of the three dominant sets are within ~5 deg; Palmstrom Vb is undefined.");
            return res;
        }

        double vb = s1 * s2 * s3 / (sin12 * sin23 * sin31);
        res.SetsUsed = 3;
        res.Vb = vb;
        res.Ib = (s1 + s2 + s3) / 3.0;
        res.Deq = Math.Pow(vb, 1.0 / 3.0);
        res.VolumeDefined = true;
        res.Descriptor = DescribeVb(vb);
        return res;
    }

    /// <summary>Overload taking dip / dip-direction (deg) instead of poles.</summary>
    public static BlockSizeResult ComputeFromDip(
        IReadOnlyList<double> dipDeg,
        IReadOnlyList<double> dipDirDeg,
        IReadOnlyList<double> spacings,
        double unitScale = 1.0,
        IReadOnlyList<double> share = null,
        string spacingUnits = "")
    {
        if (dipDeg == null || dipDirDeg == null) throw new ArgumentNullException();
        if (dipDeg.Count != dipDirDeg.Count) throw new ArgumentException("dip / dipdir length mismatch");
        var poles = new List<Vector3d>(dipDeg.Count);
        for (int i = 0; i < dipDeg.Count; i++)
            poles.Add(OrientationMath.NormalFromDipDipDir(dipDeg[i], dipDirDeg[i]));
        return Compute(poles, spacings, unitScale, share, spacingUnits);
    }

    private static Vector3d Unit(Vector3d v) { v.Unitize(); return v; }

    private static double AxialDeg(Vector3d a, Vector3d b)
    {
        double d = Math.Abs(a.X * b.X + a.Y * b.Y + a.Z * b.Z);
        return Math.Acos(Math.Min(1.0, d)) / D2R;
    }

    // Palmstrom block-volume descriptor bands (m^3).
    private static string DescribeVb(double vb)
    {
        if (vb < 1e-5) return "very small blocks (crushed)";
        if (vb < 1e-3) return "small blocks";
        if (vb < 0.03) return "medium blocks";
        if (vb < 1.0) return "large blocks";
        return "very large blocks";
    }
}
