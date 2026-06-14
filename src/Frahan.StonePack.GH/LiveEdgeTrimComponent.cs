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
/// Live Edge Trim -- scribes a board's two LIVE edges onto target seam curves
/// (the lower and upper "rivers"), returning the trimmed board outline, the
/// scribe-and-fill slivers removed, and the max trim depth. The scribe-and-fill
/// / river gap-fill move from live-edge practice. Assumes the board is laid
/// horizontally (live edges running roughly along x). Leave a seam unconnected
/// to keep that live edge untouched.
/// </summary>
[Algorithm("Scribe-and-fill trim", "Frahan-original; live-edge river gap-fill practice",
    Note = "Each live-edge point is moved to the target seam height at its x; the swept strip is the trim sliver.")]
[RelatedComponent("Frahan > EdgeMatch > Live Edge Classify", Reason = "Provides the live/sawn split this trims by.")]
[RelatedComponent("Frahan > EdgeMatch > Live Edge Stagger Layup", Reason = "Applies this trim across a whole staggered floor.")]
public class LiveEdgeTrimComponent : FrahanComponentBase
{
    public LiveEdgeTrimComponent()
        : base("Live Edge Trim", "LETrim",
            "Scribe a board's two live edges onto target seam curves (lower + upper rivers). Returns the trimmed " +
            "outline, the scribe-and-fill slivers, and the max trim depth. Leave a seam unconnected to keep that edge.",
            "Frahan", "EdgeMatch")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F10045-ED9E-4ED9-A045-ED9EED9E0045");
    protected override Bitmap Icon => IconProvider.Load("LiveEdgeTrim.png");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Board", "B", "Closed board outline (laid horizontally).", GH_ParamAccess.item);
        pManager.AddCurveParameter("Lower seam", "L", "Target seam for the bottom live edge.", GH_ParamAccess.item);
        pManager.AddCurveParameter("Upper seam", "U", "Target seam for the top live edge.", GH_ParamAccess.item);
        pManager[1].Optional = true;
        pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Trimmed", "T", "The scribed (trimmed) board outline.", GH_ParamAccess.item);
        pManager.AddCurveParameter("Slivers", "S", "The scribe-and-fill strips removed (bottom + top).", GH_ParamAccess.list);
        pManager.AddNumberParameter("Depth", "D", "Max trim depth.", GH_ParamAccess.item);
    }

    private static Func<double, double> YInterp(Curve crv)
    {
        if (crv == null) return null;
        Point3d[] pts;
        crv.DivideByCount(120, true, out pts);
        if (pts == null || pts.Length < 2) return null;
        var sp = pts.OrderBy(p => p.X).ToList();
        return x =>
        {
            if (x <= sp[0].X) return sp[0].Y;
            if (x >= sp[sp.Count - 1].X) return sp[sp.Count - 1].Y;
            for (int i = 1; i < sp.Count; i++)
                if (sp[i].X >= x)
                {
                    double t = (x - sp[i - 1].X) / Math.Max(1e-9, sp[i].X - sp[i - 1].X);
                    return sp[i - 1].Y + (sp[i].Y - sp[i - 1].Y) * t;
                }
            return sp[sp.Count - 1].Y;
        };
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Curve board = null, lower = null, upper = null;
        if (!da.GetData(0, ref board) || board == null) return;
        da.GetData(1, ref lower);
        da.GetData(2, ref upper);

        var loop = LiveEdgeGhUtil.CurveToLoop(board);
        if (loop == null || loop.Count < 8)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Could not read a usable polyline from the board.");
            return;
        }
        var c = LiveEdgeClassifier.Classify(loop);
        var liveEdges = Enumerable.Range(0, 4).Where(e => c.IsLive[e]).ToList();
        if (liveEdges.Count != 2)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Board did not classify to two live edges.");
            return;
        }
        var e0 = c.EdgePoints(liveEdges[0]);
        var e1 = c.EdgePoints(liveEdges[1]);
        var bottom = (e0.Average(p => p.Y) < e1.Average(p => p.Y) ? e0 : e1).OrderBy(p => p.X).ToList();
        var top = (e0.Average(p => p.Y) < e1.Average(p => p.Y) ? e1 : e0).OrderBy(p => p.X).ToList();

        var lowerY = YInterp(lower);
        var upperY = YInterp(upper);
        double depth = 0;
        Point3d Scr(Point3d p, Func<double, double> seam)
        {
            double y = seam != null ? seam(p.X) : p.Y;
            double d = Math.Abs(p.Y - y);
            if (d > depth) depth = d;
            return new Point3d(p.X, y, 0);
        }
        var scrB = bottom.Select(p => Scr(p, lowerY)).ToList();
        var scrT = top.Select(p => Scr(p, upperY)).ToList();

        var outline = new List<Point3d>(scrB);
        for (int i = scrT.Count - 1; i >= 0; i--) outline.Add(scrT[i]);
        outline.Add(scrB[0]);

        var botSliver = new List<Point3d>(scrB);
        for (int i = bottom.Count - 1; i >= 0; i--) botSliver.Add(bottom[i]);
        botSliver.Add(scrB[0]);
        var topSliver = new List<Point3d>(scrT);
        for (int i = top.Count - 1; i >= 0; i--) topSliver.Add(top[i]);
        topSliver.Add(scrT[0]);

        da.SetData(0, new PolylineCurve(outline));
        da.SetDataList(1, new List<Curve> { new PolylineCurve(botSliver), new PolylineCurve(topSliver) });
        da.SetData(2, depth);
    }
}
