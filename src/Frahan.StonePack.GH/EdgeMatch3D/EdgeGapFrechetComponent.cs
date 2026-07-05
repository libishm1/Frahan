#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.EdgeMatching;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.EdgeMatch3D;

// =============================================================================
// EdgeGapFrechetComponent (GUID E7C4A6F0)
//
// Standalone measurement primitive: the discrete Frechet distance ("ordered
// worst-case gap") between two curves -- e.g. two matched rims after alignment.
// It is a MAX (bounds the worst gap along the joint, unlike a mean/RMS residual)
// and it respects traversal ORDER + DIRECTION (rejects reversed/folded/scrambled
// alignments a closest-point or Hausdorff residual passes). See
// wiki/research/edge_matching_theory_vs_implementation.md (R1).
//
// This is the additive/opt-in first step: a pure metric node that touches no
// existing matcher and changes no example. Wiring the same FrechetDistance call
// into the block matchers' Joint Residual slot (as an optional accept gate) is
// the follow-up.
// =============================================================================

[Algorithm("Discrete Frechet distance (coupling measure)",
    "Eiter and Mannila, 'Computing discrete Frechet distance', TR CD-TR 94/64, TU Wien, 1994",
    Note = "Ordered worst-case gap between two curves; O(n*m); >= Hausdorff always")]
[DesignApplication(
    "Verify that two matched rims mate within tolerance EVERYWHERE (not just on average) before a cut is committed.",
    DesignFlow.Bridges,
    Precedent = "Frechet 1906; Eiter-Mannila 1994 discrete coupling; Alt-Godau curve matching",
    Tolerance = "Ordered gap <= joint tolerance (mm) along the whole mating edge")]
public sealed class EdgeGapFrechetComponent : FrahanComponentBase
{
    public EdgeGapFrechetComponent()
        : base("Edge Gap (Fréchet)", "EdgeGap",
            "Discrete Fréchet distance between two curves: the worst-case gap " +
            "along the best order-preserving (no-backtrack) traversal of both. " +
            "Unlike a mean/RMS residual it bounds the WORST local gap, and unlike " +
            "Hausdorff it respects traversal order and direction (a reversed or " +
            "folded match reads as far apart). Use it as the final check that two " +
            "matched rims mate within tolerance everywhere before emitting a cut.",
            "Frahan", "EdgeMatch")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("E7C4A6F0-1D3B-4A2E-9F5C-8B7A6D5E4C3B");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap Icon => IconProvider.Load("EdgeMatchSolve.png"); // reuse until a dedicated icon ships

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddCurveParameter("Curve A", "A",
            "First curve (e.g. a matched rim).", GH_ParamAccess.item);
        p.AddCurveParameter("Curve B", "B",
            "Second curve (e.g. its mate, after alignment).", GH_ParamAccess.item);
        p.AddIntegerParameter("Samples", "S",
            "Arc-length resample count per curve for the discrete metric. " +
            "Higher = closer to the continuous Fréchet distance. Default 128.",
            GH_ParamAccess.item, 128);
        p.AddNumberParameter("Max Gap", "Mg",
            "Optional tolerance (model units). <= 0 = report only (no gate); " +
            "when > 0, Within Tolerance is true iff the ordered gap <= this.",
            GH_ParamAccess.item, 0.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddNumberParameter("Ordered Gap", "Og",
            "Discrete Fréchet distance = worst-case order-respecting gap between " +
            "the two curves (model units).", GH_ParamAccess.item);
        p.AddBooleanParameter("Within Tolerance", "Ok",
            "True when Max Gap <= 0 (no gate) or the ordered gap <= Max Gap.",
            GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess DA)
    {
        Curve a = null, b = null;
        int samples = 128;
        double maxGap = 0.0;
        if (!DA.GetData(0, ref a)) return;
        if (!DA.GetData(1, ref b)) return;
        DA.GetData(2, ref samples);
        DA.GetData(3, ref maxGap);

        if (a == null || b == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Both curves are required.");
            return;
        }
        if (samples < 2) samples = 2;

        var pa = Resample(a, samples);
        var pb = Resample(b, samples);
        if (pa.Count == 0 || pb.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Could not sample one of the curves.");
            return;
        }

        double gap = FrechetDistance.Discrete(pa, pb);
        bool ok = maxGap <= 0.0 || gap <= maxGap;
        DA.SetData(0, gap);
        DA.SetData(1, ok);
    }

    // Arc-length resample a curve into `count`+1 points (falls back to the curve
    // endpoints for a degenerate / zero-length curve).
    private static List<Point3d> Resample(Curve c, int count)
    {
        var pts = new List<Point3d>();
        double[] t = c.DivideByCount(count, true, out Point3d[] sampled);
        if (sampled != null && sampled.Length > 0)
        {
            pts.AddRange(sampled);
            return pts;
        }
        pts.Add(c.PointAtStart);
        pts.Add(c.PointAtEnd);
        return pts;
    }
}
