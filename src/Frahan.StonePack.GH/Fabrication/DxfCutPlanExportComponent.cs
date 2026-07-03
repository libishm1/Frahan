#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Frahan.Core.Fabrication;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Fabrication;

// =============================================================================
// DxfCutPlanExportComponent (D5F10053, Frahan > Fabrication)
//
// Exports cut-profile curves to a CAM-readable DXF (the format every stone CAM
// imports: ALPHACAM, DDX EasySTONE, Breton, Intermac) with one layer per piece +
// an id label. Optionally flattens tilted profiles to their plane and shelf-nests
// them into a 2D cut sheet. The pre-CAM handoff: Frahan owns the stone logic and
// hands a clean layered DXF to the machine's own CAM. (For solids / .3dm with
// stone metadata use Stone-Aware Cut Export; for STEP/IGES use Rhino File>Export.)
// =============================================================================

[Algorithm("DXF cut-plan export", "Minimal AutoCAD R2000 DXF (LWPOLYLINE + TEXT + LAYER); the universal stone-CAM import",
    Note = "Thin interface, not a CAM engine. Flatten + shelf-nest to a 2D cut sheet optional.")]
[RelatedComponent("Frahan > Fabrication > Stone-Aware Cut Export", Reason = "The .3dm-with-metadata sibling of this DXF export.")]
[RelatedComponent("Frahan > Fabrication > Cut Orientation", Reason = "Its cut rectangles/profiles feed straight into this export.")]
[RelatedComponent("Frahan > Masonry > Fabrication Schedule", Reason = "Piece ids / CSV manifest to pair with the DXF.")]
public sealed class DxfCutPlanExportComponent : FrahanComponentBase
{
    public DxfCutPlanExportComponent()
        : base("DXF Cut Plan", "DXFCut",
            "Export cut-profile curves to a CAM-readable DXF (one layer per piece + id label), the format every stone " +
            "CAM imports (Alphacam, DDX EasySTONE, Breton). Optionally flattens tilted profiles and shelf-nests them " +
            "into a 2D cut sheet. Set Write = true to write the file.",
            "Frahan", "Fabricate")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F10053-ED9E-4ED9-A053-ED9EED9E0053");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("DxfCutPlan.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddCurveParameter("Curves", "C", "Cut-profile / block-outline curves.", GH_ParamAccess.list);
        p.AddTextParameter("Piece Ids", "Id", "Per-curve id (layer name). Auto piece_001.. if absent.", GH_ParamAccess.list);
        p[1].Optional = true;
        p.AddBooleanParameter("Flatten nest", "Fn", "Project tilted profiles to their plane and shelf-nest into a 2D cut sheet.", GH_ParamAccess.item, true);
        p.AddNumberParameter("Gap", "G", "Gap between nested pieces (model units).", GH_ParamAccess.item, 0.1);
        p.AddNumberParameter("Sheet width", "Sw", "Wrap the nest at this width (0 = single row).", GH_ParamAccess.item, 0.0);
        p.AddNumberParameter("Text height", "Th", "Label height (0 = auto).", GH_ParamAccess.item, 0.0);
        p.AddTextParameter("File Path", "Fp", "Output .dxf path.", GH_ParamAccess.item);
        p.AddBooleanParameter("Write", "Wr", "Set true to write the .dxf. False = dry run (layout + report only).", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("File Path", "Fp", "Path written (empty on dry run / failure).", GH_ParamAccess.item);
        p.AddIntegerParameter("Count", "N", "Profiles written.", GH_ParamAccess.item);
        p.AddCurveParameter("Layout", "L", "The laid-out (nested) profiles, for preview.", GH_ParamAccess.list);
        p.AddTextParameter("Report", "Re", "Export summary.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var crvs = new List<Curve>();
        if (!da.GetDataList(0, crvs) || crvs.Count == 0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Provide cut-profile curves."); return; }
        var ids = new List<string>(); da.GetDataList(1, ids);
        bool nest = true; da.GetData(2, ref nest);
        double gap = 0.1; da.GetData(3, ref gap);
        double sw = 0.0; da.GetData(4, ref sw);
        double th = 0.0; da.GetData(5, ref th);
        string path = null; da.GetData(6, ref path);
        bool write = false; da.GetData(7, ref write);

        var polys = new List<Polyline>();
        foreach (var c in crvs) polys.Add(ToPolyline(c));
        var laid = nest ? DxfCutPlanExporter.FlattenAndNest(polys, gap, sw) : polys;

        da.SetDataList(2, laid.Select(pl => pl != null && pl.Count > 1 ? (Curve)pl.ToPolylineCurve() : null));

        if (!write)
        {
            da.SetData(0, string.Empty);
            da.SetData(1, polys.Count);
            da.SetData(2, laid.Select(pl => pl != null && pl.Count > 1 ? (Curve)pl.ToPolylineCurve() : null));
            da.SetData(3, $"Dry run: {polys.Count} profile(s) laid out. Set Write = true to write '{path}'.");
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Write = false (dry run). Set Write = true to write the .dxf.");
            return;
        }
        if (string.IsNullOrWhiteSpace(path))
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No File Path provided."); return; }

        bool ok = DxfCutPlanExporter.Write(path, laid, ids.Count > 0 ? ids : null, th, out string report);
        if (!ok) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, report); da.SetData(3, report); return; }
        da.SetData(0, path);
        da.SetData(1, laid.Count(pl => pl != null && pl.Count > 1));
        da.SetData(3, report);
    }

    private static Polyline ToPolyline(Curve c)
    {
        if (c == null) return null;
        if (c.TryGetPolyline(out Polyline pl)) return pl;
        var ts = c.DivideByCount(64, true);
        if (ts == null) return null;
        var pts = ts.Select(t => c.PointAt(t)).ToList();
        if (c.IsClosed && pts.Count > 0) pts.Add(pts[0]);
        return new Polyline(pts);
    }
}
