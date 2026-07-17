#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Core.Discontinuity;

// =============================================================================
// SetFrameAnalyzer -- wires the Watson mean-shift set clusterer
// (SetClusterer.Cluster) into the cuboid-frame prior. For each JointSet it
// computes Fisher K / alpha95 from the member facet poles, fits + gates the
// cuboid frame (CuboidFrameFit.FitWithGate), and labels + dispatches every
// family (FamilyLabeling). On a kept cube the three cut-plane normals are the
// columns of FrameR (also surfaced as Vector3d for the G10 guillotine packer);
// on fallback / <3 families the frame is ABSENT.
//
// This is the Rhino-light glue that mirrors pyfrahan.cluster.cluster_poles'
// frame + gate + label stage sitting on top of the (already-ported) Watson
// clusterer. The heavy numeric work lives in the pure, Rhino-free CuboidFrameFit
// / FisherStats / FamilyLabeling.
// =============================================================================

public sealed class SetFrameFamily
{
    public JointSet Set;
    public FamilyLabel Label;
    public string LabelKey;
    public string Dispatch;
    public double FisherK;
    public double Alpha95Deg;
    public double? MisfitDeg;   // axial angle to its frame axis (null if no frame stage ran)
    public int? FrameAxis;      // 0/1/2 (null if none)
    public FrameMode Mode;
}

public sealed class SetFrameResult
{
    public List<SetFrameFamily> Families;
    public double[,] FrameR;      // 3x3, columns = cut-plane normals; null unless Cuboid
    public FrameMode Mode;
    public double MaxMisfitDeg;   // NaN if fewer than 3 families
    public double ThetaMaxDeg;
    public Vector3d[] CutNormals; // FrameR columns as Vector3d; null unless Cuboid
}

public sealed class FrameOptions
{
    public double ThetaMaxDeg = 15.0;   // gate threshold; == the mean-shift bandwidth
    public double DykeTrendDeg = 0.0;   // for transverse/longitudinal split
    public int Iters = 3;
    public bool Cuboid = true;          // set false to skip the cube (force Unconstrained)
}

public static class SetFrameAnalyzer
{
    /// <summary>
    /// Fit + gate the cuboid frame and label every family, given the facets and
    /// the sets returned by <see cref="SetClusterer.Cluster"/>.
    /// </summary>
    public static SetFrameResult Analyze(
        IReadOnlyList<Facet> facets, IReadOnlyList<JointSet> sets, FrameOptions opt = null)
    {
        opt = opt ?? new FrameOptions();
        int k = sets == null ? 0 : sets.Count;

        var famPoles = new List<double[]>(k);
        var famWeights = new List<double>(k);
        var famAlpha = new List<double>(k);
        var fisherK = new double[k];

        for (int f = 0; f < k; f++)
        {
            var js = sets[f];
            famPoles.Add(new[] { js.Pole.X, js.Pole.Y, js.Pole.Z });

            double w = 0.0;
            var members = new List<double[]>(js.FacetCount);
            if (js.FacetIndices != null)
            {
                foreach (int idx in js.FacetIndices)
                {
                    var nrm = OrientationMath.LowerHemisphere(facets[idx].Normal);
                    members.Add(new[] { nrm.X, nrm.Y, nrm.Z });
                    w += Math.Max(1, facets[idx].PointCount);   // matches the mean-shift facet-size weight
                }
            }
            famWeights.Add(w > 0 ? w : 1.0);

            if (members.Count >= 2)
            {
                var fs = FisherStats.Compute(members);
                famAlpha.Add(fs.Alpha95Deg);
                fisherK[f] = fs.K;
            }
            else
            {
                famAlpha.Add(double.NaN);
                fisherK[f] = double.NaN;
            }
        }

        CuboidFrameResult fr;
        if (opt.Cuboid && k >= 3)
            fr = CuboidFrameFit.FitWithGate(famPoles, famWeights, famAlpha, opt.ThetaMaxDeg, opt.Iters);
        else
            fr = new CuboidFrameResult
            {
                Mode = FrameMode.Unconstrained,
                FrameR = null,
                MisfitsDeg = null,
                AxisOf = null,
                MaxMisfitDeg = double.NaN,
                ThetaMaxDeg = opt.ThetaMaxDeg
            };

        var fams = new List<SetFrameFamily>(k);
        for (int f = 0; f < k; f++)
        {
            var js = sets[f];
            var lab = FamilyLabeling.Label(js.Dip, js.DipDir, opt.DykeTrendDeg);
            double? mis = (fr.MisfitsDeg != null && f < fr.MisfitsDeg.Length) ? fr.MisfitsDeg[f] : (double?)null;
            int? ax = (fr.AxisOf != null && f < fr.AxisOf.Length) ? fr.AxisOf[f] : (int?)null;
            fams.Add(new SetFrameFamily
            {
                Set = js,
                Label = lab,
                LabelKey = FamilyLabeling.ToKey(lab),
                Dispatch = FamilyLabeling.Dispatch(lab),
                FisherK = fisherK[f],
                Alpha95Deg = famAlpha[f],
                MisfitDeg = mis,
                FrameAxis = ax,
                Mode = fr.Mode
            });
        }

        Vector3d[] cut = null;
        if (fr.FrameR != null)
        {
            cut = new Vector3d[3];
            for (int j = 0; j < 3; j++)
                cut[j] = new Vector3d(fr.FrameR[0, j], fr.FrameR[1, j], fr.FrameR[2, j]);
        }

        return new SetFrameResult
        {
            Families = fams,
            FrameR = fr.FrameR,
            Mode = fr.Mode,
            MaxMisfitDeg = fr.MaxMisfitDeg,
            ThetaMaxDeg = fr.ThetaMaxDeg,
            CutNormals = cut
        };
    }
}
