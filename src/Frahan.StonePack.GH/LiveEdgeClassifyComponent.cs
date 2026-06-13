using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Frahan.EdgeMatching;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH;

/// <summary>
/// Live Edge Classify -- splits a wood-offcut outline into its LIVE (curvy,
/// natural) edges and SAWN (straight, cut) ends for the live-edge flooring
/// 2D edge-matching workflow. Step 1 of Classify -> Match -> Trim -> Stagger.
/// </summary>
[Algorithm("Live/sawn straight-run classifier", "Frahan-original",
    Note = "The two longest straight runs are the sawn ends; their endpoints are the corners; the two arcs between are the live edges.")]
[RelatedComponent("Frahan > EdgeMatch > Live Edge Stagger Layup",
    Reason = "End-to-end floor that consumes classified offcuts.")]
public class LiveEdgeClassifyComponent : GH_Component
{
    public LiveEdgeClassifyComponent()
        : base("Live Edge Classify", "LEClassify",
            "Classify a wood-offcut outline into LIVE (curvy, natural) edges and SAWN (straight, machine-cut) ends, " +
            "for live-edge flooring 2D edge matching. Robust to live-edge wiggles: the two longest straight runs are " +
            "taken as the sawn ends and the two arcs between them as the live edges.",
            "Frahan", "EdgeMatch")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F10043-ED9E-4ED9-A043-ED9EED9E0043");
    protected override Bitmap Icon => IconProvider.Load("LiveEdgeClassify.png");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Outline", "O", "Closed offcut outline (one board).", GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Live edges", "L", "The two LIVE (curvy) edges.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Sawn edges", "S", "The two SAWN (straight) ends.", GH_ParamAccess.list);
        pManager.AddPointParameter("Corners", "C", "The four detected corners.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Straightness", "St", "Per-edge chord/arc-length (~1 = straight).", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        Curve crv = null;
        if (!da.GetData(0, ref crv) || crv == null) return;

        var loop = LiveEdgeGhUtil.CurveToLoop(crv);
        if (loop == null || loop.Count < 8)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Could not read a usable polyline from the outline.");
            return;
        }

        var res = LiveEdgeClassifier.Classify(loop);
        var live = new List<Curve>();
        var sawn = new List<Curve>();
        for (int e = 0; e < 4; e++)
        {
            var seg = res.EdgePoints(e);
            var pc = new PolylineCurve(seg);
            if (res.IsLive[e]) live.Add(pc); else sawn.Add(pc);
        }
        var corners = res.Corners.Select(ci => res.Loop[ci]).ToList();

        da.SetDataList(0, live);
        da.SetDataList(1, sawn);
        da.SetDataList(2, corners);
        da.SetDataList(3, res.Straightness.ToList());
    }
}
