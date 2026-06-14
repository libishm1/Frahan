using System;
using System.Drawing;
using Frahan.GH.TwoD;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH;

[DesignApplication(
    "Generate a diagnostic no-fit polygon from two closed planar polylines",
    DesignFlow.Bridges,
    Precedent = "Burke 2007 NFP test fixture")]
[Algorithm("No-fit polygon construction (orbital / boundary slide)",
    "Burke, E., Hellier, R., Kendall, G., Whitwell, G. (2007). \"Complete and robust no-fit polygon generation for the irregular stock cutting problem.\" Eur. J. Oper. Res.",
    Doi = "10.1016/j.ejor.2006.03.011",
    WikiPath = "wiki/index/references.md#BurkeNFP2007")]
public sealed class NfpTestComponent : FrahanComponentBase
{
    public NfpTestComponent()
        : base("NFP Test", "NFP",
            "Generate a diagnostic no-fit polygon from two closed planar polylines. Implements no-fit polygon construction (Burke et al. 2007).",
            "Frahan", "2D Packing")
    {
    }

    public override Guid ComponentGuid => new Guid("915FB7AF-425E-4F5B-9F57-7CE8F5C8A301");
    protected override Bitmap? Icon => IconProvider.Load("NoFitPolygon.png");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Stationary", "A", "Stationary closed polygon.", GH_ParamAccess.item);
        pManager.AddCurveParameter("Sliding", "B", "Sliding closed polygon.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Tolerance", "T", "Geometric tolerance.", GH_ParamAccess.item, 0.001);
        pManager.AddIntegerParameter("Max Iterations", "I", "Reserved for future full concave NFP implementation.", GH_ParamAccess.item, 150);
        pManager.AddBooleanParameter("Rectangle Shortcut", "R", "Reserved for future rectangle-specific NFP implementation.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("NFP", "N", "No-fit polygon curve.", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Error Code", "E", "1 convex OK, 2 approximation, 0 failed.", GH_ParamAccess.item);
        pManager.AddTextParameter("Message", "M", "NFP status message.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Curve? stationary = null;
        Curve? sliding = null;
        var tolerance = 0.001;
        var maxIterations = 150;
        var rectangleShortcut = false;

        if (!da.GetData(0, ref stationary)) return;
        if (!da.GetData(1, ref sliding)) return;
        da.GetData(2, ref tolerance);
        da.GetData(3, ref maxIterations);
        da.GetData(4, ref rectangleShortcut);

        try
        {
            var stationaryPolygon = NfpRhino.CurveToPolygon(stationary!, tolerance);
            var slidingPolygon = NfpRhino.CurveToPolygon(sliding!, tolerance);
            if (stationaryPolygon.Count < 3 || slidingPolygon.Count < 3)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Both curves must be valid closed polylines.");
                return;
            }

            var nfp = new NfpRhino(stationaryPolygon, slidingPolygon, tolerance, maxIterations, rectangleShortcut);
            if (nfp.ErrorCode == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, nfp.ErrorMessage);
            }
            else if (nfp.ErrorCode == 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, nfp.ErrorMessage);
            }

            da.SetData(0, nfp.ToCurve(true));
            da.SetData(1, nfp.ErrorCode);
            da.SetData(2, nfp.ErrorMessage);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "NFP preprocessing failed: " + ex.Message);
        }
    }
}
