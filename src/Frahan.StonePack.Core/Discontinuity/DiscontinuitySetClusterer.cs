#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace Frahan.Core.Discontinuity;

// =============================================================================
// DiscontinuitySetClusterer -- groups facet poles into joint sets (after the
// DSE set-identification step, Riquelme et al. 2014). Mode-seeking by mean-shift
// on the unit sphere with a Watson (axial) kernel exp(kappa*(m.x)^2), which is
// antipodal-symmetric (n and -n are the same plane) so a set never splits across
// the equator -- no hemisphere-fold seam. The number of sets is discovered, not
// preset (k-means rejected for exactly that reason). Modes weighted by facet
// size. Each set reports its mean pole + dip/dip-direction + members + normal
// spacing of its parallel planes.
//
// Managed, deterministic, Rhino-LIGHT.
// =============================================================================

public sealed class JointSet
{
    public Vector3d Pole;        // unit, lower hemisphere
    public double Dip;           // degrees
    public double DipDir;        // degrees
    public int[] FacetIndices;
    public int FacetCount => FacetIndices == null ? 0 : FacetIndices.Length;
    public double PointShare;    // fraction of all facet points in this set
    public double MeanSpacing;   // mean perpendicular distance between this set's parallel planes (>=0; 0 if <2 planes)
}

public sealed class SetOptions
{
    public double BandwidthDeg = 15;  // mean-shift angular kernel width
    public double MergeDeg = 8;       // merge converged modes within this axial angle
    public int MinSetFacets = 3;
    public int MaxStarts = 1200;      // cap mean-shift start points for speed
}

public static class SetClusterer
{
    public static List<JointSet> Cluster(IReadOnlyList<Facet> facets, SetOptions opt = null)
    {
        opt = opt ?? new SetOptions();
        int n = facets.Count;
        var sets = new List<JointSet>();
        if (n == 0) return sets;

        var poles = new Vector3d[n];
        var wt = new double[n];
        for (int i = 0; i < n; i++) { poles[i] = OrientationMath.LowerHemisphere(facets[i].Normal); wt[i] = Math.Max(1, facets[i].PointCount); }

        double bw = opt.BandwidthDeg * Math.PI / 180.0;
        double sinbw = Math.Max(1e-4, Math.Sin(bw));
        double kappa = 1.0 / (sinbw * sinbw);

        // mean-shift from a (capped) set of starts
        int stride = Math.Max(1, n / Math.Max(1, opt.MaxStarts));
        var modes = new List<Vector3d>();
        for (int s = 0; s < n; s += stride)
        {
            Vector3d m = poles[s];
            for (int it = 0; it < 60; it++)
            {
                double sx = 0, sy = 0, sz = 0;
                for (int j = 0; j < n; j++)
                {
                    double dot = m.X * poles[j].X + m.Y * poles[j].Y + m.Z * poles[j].Z;
                    double w = wt[j] * Math.Exp(kappa * (dot * dot - 1.0));
                    double sgn = dot >= 0 ? 1.0 : -1.0;
                    sx += w * sgn * poles[j].X; sy += w * sgn * poles[j].Y; sz += w * sgn * poles[j].Z;
                }
                var mn = new Vector3d(sx, sy, sz);
                if (mn.Length < 1e-12) break;
                mn.Unitize();
                if (OrientationMath.AxialAngleDeg(mn, m) < 0.02) { m = mn; break; }
                m = mn;
            }
            modes.Add(OrientationMath.LowerHemisphere(m));
        }

        // merge converged modes into distinct set poles
        var setPoles = new List<Vector3d>();
        foreach (var md in modes)
        {
            bool merged = false;
            for (int k = 0; k < setPoles.Count; k++)
                if (OrientationMath.AxialAngleDeg(md, setPoles[k]) < opt.MergeDeg) { merged = true; break; }
            if (!merged) setPoles.Add(md);
        }

        // assign each facet to its nearest set pole (axial)
        var members = new List<int>[setPoles.Count];
        for (int k = 0; k < setPoles.Count; k++) members[k] = new List<int>();
        for (int i = 0; i < n; i++)
        {
            int best = -1; double bestAng = double.MaxValue;
            for (int k = 0; k < setPoles.Count; k++)
            {
                double a = OrientationMath.AxialAngleDeg(poles[i], setPoles[k]);
                if (a < bestAng) { bestAng = a; best = k; }
            }
            if (best >= 0) members[best].Add(i);
        }

        double totalPts = facets.Sum(f => (double)Math.Max(1, f.PointCount));
        for (int k = 0; k < setPoles.Count; k++)
        {
            if (members[k].Count < opt.MinSetFacets) continue;
            // axial mean pole weighted by facet size
            double sx = 0, sy = 0, sz = 0; double pts = 0;
            var seed = setPoles[k];
            foreach (var i in members[k])
            {
                double dot = seed.X * poles[i].X + seed.Y * poles[i].Y + seed.Z * poles[i].Z;
                double sgn = dot >= 0 ? 1.0 : -1.0;
                sx += wt[i] * sgn * poles[i].X; sy += wt[i] * sgn * poles[i].Y; sz += wt[i] * sgn * poles[i].Z;
                pts += facets[i].PointCount;
            }
            var pole = OrientationMath.LowerHemisphere(new Vector3d(sx, sy, sz));
            var (dip, dipdir) = OrientationMath.DipDipDir(pole);
            sets.Add(new JointSet
            {
                Pole = pole,
                Dip = dip,
                DipDir = dipdir,
                FacetIndices = members[k].ToArray(),
                PointShare = totalPts > 0 ? pts / totalPts : 0,
                MeanSpacing = NormalSpacing(facets, members[k], pole)
            });
        }
        // largest sets first
        sets.Sort((a, b) => b.PointShare.CompareTo(a.PointShare));
        return sets;
    }

    // Mean perpendicular distance between consecutive family-parallel planes:
    // project each facet centroid onto the set normal, sort offsets, average the gaps.
    private static double NormalSpacing(IReadOnlyList<Facet> facets, List<int> idx, Vector3d pole)
    {
        if (idx.Count < 2) return 0;
        var offs = new List<double>(idx.Count);
        foreach (var i in idx)
            offs.Add(facets[i].Centroid.X * pole.X + facets[i].Centroid.Y * pole.Y + facets[i].Centroid.Z * pole.Z);
        offs.Sort();
        // collapse near-duplicate offsets (same physical plane) before measuring gaps
        var gaps = new List<double>();
        double prev = offs[0];
        for (int i = 1; i < offs.Count; i++)
        {
            double g = offs[i] - prev;
            if (g > 1e-9) gaps.Add(g);
            prev = offs[i];
        }
        if (gaps.Count == 0) return 0;
        double sum = 0; foreach (var g in gaps) sum += g;
        return sum / gaps.Count;
    }
}
