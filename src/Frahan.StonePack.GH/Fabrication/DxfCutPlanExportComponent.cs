#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Frahan.Core.Fabrication;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Fabrication;

// =============================================================================
// DxfCutPlanExportComponent (D5F10053, Frahan > Fabricate)
//
// Exports cut-profile curves to a CAM-readable DXF (the format every stone CAM
// imports: ALPHACAM, DDX EasySTONE, Breton, Intermac) with one layer per piece +
// an id label. Optionally flattens tilted profiles to their plane and shelf-nests
// them into a 2D cut sheet, AND lays down a mason-readable CUTTING SCHEDULE (title
// block + cut-list/BOM table with piece dimensions & quantities + numbered saw
// passes) on their own layers, so a yard/site crew can work from the sheet - not
// only a CAM operator. The pre-CAM handoff: Frahan owns the stone logic and hands
// a clean layered DXF to the machine's own CAM.
// =============================================================================

[Algorithm("DXF cut-plan export", "Minimal AutoCAD R12 DXF (POLYLINE + LINE + TEXT + LAYER); the universal stone-CAM import",
    Note = "Thin interface, not a CAM engine. R12 on purpose: opens in strict ODA readers (Rhino) AND every CAM. Flatten + shelf-nest optional.")]
[Algorithm("Mason cutting schedule", "Title block + cut-list/BOM table (piece / size / qty / op) + numbered saw passes on TITLE/SCHEDULE/CUT_SEQUENCE layers",
    Note = "For a site/yard crew reading the sheet, not just the CAM. Table auto-groups identical pieces; feed Sizes for L x W x H.")]
[RelatedComponent("Frahan > Fabricate > Cut Orientation", Reason = "Its cut rectangles/profiles feed straight into this export.")]
[RelatedComponent("Frahan > Fabricate > Block Yield", Reason = "Its sound blocks + block size drive the cut list and saw passes.")]
[RelatedComponent("Frahan > Masonry > Fabrication Schedule", Reason = "Piece ids / CSV manifest to pair with the DXF.")]
public sealed class DxfCutPlanExportComponent : FrahanComponentBase
{
    public DxfCutPlanExportComponent()
        : base("DXF Cut Plan", "DXFCut",
            "Export cut-profile curves to a CAM-readable DXF (one layer per piece + id label), the format every stone " +
            "CAM imports (Alphacam, DDX EasySTONE, Breton). Optionally flattens + shelf-nests into a 2D cut sheet and " +
            "adds a mason-readable cutting schedule (title + cut-list table + numbered saw passes). Set Write = true to write.",
            "Frahan", "Fabricate")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F10053-ED9E-4ED9-A053-ED9EED9E0053");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("DxfCutPlan.png");

    private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

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
        // ---- mason cutting-schedule (all optional) ----
        p.AddBooleanParameter("Schedule", "Sc", "Add a mason cutting schedule (title + cut-list table + numbered saw passes).", GH_ParamAccess.item, true);
        p.AddLineParameter("Cut lines", "Cl", "Ordered saw passes to draw + number on CUT_SEQUENCE (same frame as the profiles; best with Flatten nest = false).", GH_ParamAccess.list);
        p[9].Optional = true;
        p.AddVectorParameter("Sizes", "Sz", "Per-piece L x W x H for the cut list (one for all, or one per piece). Absent = 2D size from the outline.", GH_ParamAccess.list);
        p[10].Optional = true;
        p.AddTextParameter("Title", "Ti", "Sheet title for the schedule block.", GH_ParamAccess.item, "Stone cutting schedule");
        p.AddTextParameter("Units", "U", "Units label for the schedule (m / mm).", GH_ParamAccess.item, "m");
        p.AddCurveParameter("Fracture traces", "Ft",
            "Bed / flaw / joint traces to draw on a dedicated FRACTURES layer (orange), already in the sheet " +
            "frame - e.g. Block Face Map > Fracture traces. Best with Flatten nest = false.", GH_ParamAccess.list);
        p[13].Optional = true;
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
        bool schedule = true; da.GetData(8, ref schedule);
        var cutLines = new List<Line>(); da.GetDataList(9, cutLines);
        var sizes = new List<Vector3d>(); da.GetDataList(10, sizes);
        string title = "Stone cutting schedule"; da.GetData(11, ref title);
        string units = "m"; da.GetData(12, ref units);
        var fractureCrvs = new List<Curve>(); da.GetDataList(13, fractureCrvs);
        var fractures = fractureCrvs.Select(ToPolyline).Where(pl => pl != null && pl.Count > 1).ToList();

        var polys = new List<Polyline>();
        foreach (var c in crvs) polys.Add(ToPolyline(c));
        var laid = nest ? DxfCutPlanExporter.FlattenAndNest(polys, gap, sw) : polys;

        da.SetDataList(2, laid.Select(pl => pl != null && pl.Count > 1 ? (Curve)pl.ToPolylineCurve() : null));

        // explicit cut-list rows from Sizes (L x W x H), grouped by size with a quantity
        List<DxfCutPlanExporter.CutScheduleRow> rows = null;
        if (schedule && sizes.Count > 0)
            rows = BuildRows(sizes, ids, polys.Count);

        if (!write)
        {
            da.SetData(0, string.Empty);
            da.SetData(1, polys.Count);
            string sMsg = schedule ? $" (+ cutting schedule: {(rows != null ? rows.Count : DistinctFootprints(laid))} row(s)" +
                (cutLines.Count > 0 ? $", {cutLines.Count} numbered cut line(s)" : "") +
                (fractures.Count > 0 ? $", {fractures.Count} fracture trace(s))" : ")") : "";
            da.SetData(3, $"Dry run: {polys.Count} profile(s) laid out{sMsg}. Set Write = true to write '{path}'.");
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Write = false (dry run). Set Write = true to write the .dxf.");
            return;
        }
        if (string.IsNullOrWhiteSpace(path))
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No File Path provided."); return; }

        bool ok = DxfCutPlanExporter.WriteCutPlan(
            path, laid, ids.Count > 0 ? ids : null, th,
            schedule ? title : null, units, schedule, rows,
            cutLines.Count > 0 ? cutLines : null, null,
            fractures.Count > 0 ? fractures : null, out string report);
        if (!ok) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, report); da.SetData(3, report); return; }
        da.SetData(0, path);
        da.SetData(1, laid.Count(pl => pl != null && pl.Count > 1));
        da.SetData(3, report);
    }

    // group identical (rounded) L x W x H into cut-list rows with a quantity
    private static List<DxfCutPlanExporter.CutScheduleRow> BuildRows(List<Vector3d> sizes, List<string> ids, int pieceCount)
    {
        int n = Math.Max(sizes.Count, sizes.Count == 1 ? pieceCount : sizes.Count);
        var order = new List<string>();
        var acc = new Dictionary<string, DxfCutPlanExporter.CutScheduleRow>();
        for (int i = 0; i < n; i++)
        {
            var s = sizes[sizes.Count == 1 ? 0 : Math.Min(i, sizes.Count - 1)];
            double l = Math.Round(s.X, 3), w = Math.Round(s.Y, 3), h = Math.Round(s.Z, 3);
            string key = l.ToString("0.###", CI) + "|" + w.ToString("0.###", CI) + "|" + h.ToString("0.###", CI);
            if (!acc.TryGetValue(key, out var row))
            {
                string id = (ids != null && ids.Count > i && !string.IsNullOrWhiteSpace(ids[i])) ? ids[i] : "block";
                row = new DxfCutPlanExporter.CutScheduleRow(id, l, w, h, 0, "saw");
                order.Add(key);
            }
            row.Qty += 1;
            acc[key] = row;
        }
        return order.Select(k => acc[k]).ToList();
    }

    private static int DistinctFootprints(List<Polyline> laid)
    {
        var set = new HashSet<string>();
        foreach (var pl in laid)
        {
            if (pl == null || pl.Count < 2) continue;
            var bb = pl.BoundingBox;
            set.Add(Math.Round(bb.Max.X - bb.Min.X, 3).ToString("0.###", CI) + "x" + Math.Round(bb.Max.Y - bb.Min.Y, 3).ToString("0.###", CI));
        }
        return Math.Max(1, set.Count);
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
