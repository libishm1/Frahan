#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Rhino.Geometry;

namespace Frahan.Core.Fabrication;

// =============================================================================
// DxfCutPlanExporter -- writes a minimal, CAM-readable ASCII DXF (AutoCAD R2000)
// of a stone cut plan: one LWPOLYLINE per cut profile on a per-piece LAYER, plus
// a TEXT label. DXF is the lingua franca every stone CAM ingests (ALPHACAM, DDX
// EasySTONE, Breton, Intermac), so this is the pre-CAM handoff: Frahan owns the
// stone logic (which cut, order, kerf, block id) and hands a clean, layered DXF
// to the machine's own CAM instead of trying to be the CAM.
//
// WriteCutPlan additionally lays down a MASON-READABLE CUTTING SCHEDULE on its own
// layers so a yard/site crew (not just a CAM operator) can work from the sheet:
//   * TITLE       -- a sheet title + units note
//   * SCHEDULE    -- a boxed cut list / BOM table: # | PIECE | SIZE | QTY | OP
//   * CUT_SEQUENCE-- the ordered saw passes drawn as numbered LINES ("1","2",..)
// All overlays are LINE + TEXT only (no arcs/splines), so any DXF reader or a
// plotted paper sheet on the bench renders them. Coordinate-independent table +
// title are anchored below the nested pieces; cut lines are drawn as supplied
// (feed them in the same frame as the profiles, best with Flatten nest = false).
//
// This is a thin INTERFACE, not a CAD kernel: LWPOLYLINE + LINE + TEXT + LAYER
// only. Pure managed string + file IO (Rhino value types only), headless-unit-
// testable. STEP/IGES solids go through Rhino's native File > Export.
//
// Reference: AutoCAD DXF Reference (LWPOLYLINE 90/70/10/20; LINE 10/20/11/21;
// TEXT 40/1; LAYER table 2/62/6). ALPHACAM/EasySTONE DXF import (research 2026-07).
// =============================================================================

public static class DxfCutPlanExporter
{
    private static readonly CultureInfo CI = CultureInfo.InvariantCulture;
    private static readonly int[] AcadColors = { 1, 2, 3, 4, 5, 6, 30, 40, 150, 250 };

    // Reserved overlay layers (kept off the piece-colour rotation).
    private const string LTitle = "TITLE";
    private const string LSchedule = "SCHEDULE";
    private const string LCutSeq = "CUT_SEQUENCE";
    private const string LFractures = "FRACTURES";

    /// <summary>A row of the mason cut list / BOM. Depth &lt;= 0 renders as a 2D piece.</summary>
    public struct CutScheduleRow
    {
        public string Id;
        public double Width, Height, Depth;
        public int Qty;
        public string Op;
        public CutScheduleRow(string id, double width, double height, double depth, int qty, string op)
        { Id = id; Width = width; Height = height; Depth = depth; Qty = qty; Op = op; }
    }

    /// <summary>
    /// Write cut-profile polylines to a DXF, one LWPOLYLINE per profile on the layer
    /// named by its id, plus an id TEXT label at each profile centroid. Coordinates
    /// are written as 2D (x, y); flatten upstream via <see cref="FlattenAndNest"/> if
    /// the profiles are tilted in 3D. Returns false and a reason on failure.
    /// </summary>
    public static bool Write(
        string path,
        IReadOnlyList<Polyline> profiles,
        IReadOnlyList<string> ids,
        double textHeight,
        out string report)
        => WriteCutPlan(path, profiles, ids, textHeight,
            title: null, units: null, includeSchedule: false,
            schedule: null, cutLines: null, cutLabels: null, out report);

    /// <summary>
    /// Write the cut profiles (as <see cref="Write"/>) plus an optional mason-readable
    /// cutting schedule: a <paramref name="title"/> block, a cut-list/BOM table
    /// (auto-built from the profile bounding boxes when <paramref name="schedule"/> is
    /// null and <paramref name="includeSchedule"/> is true), and the ordered saw
    /// passes <paramref name="cutLines"/> drawn as numbered lines on CUT_SEQUENCE.
    /// </summary>
    public static bool WriteCutPlan(
        string path,
        IReadOnlyList<Polyline> profiles,
        IReadOnlyList<string> ids,
        double textHeight,
        string title,
        string units,
        bool includeSchedule,
        IReadOnlyList<CutScheduleRow> schedule,
        IReadOnlyList<Line> cutLines,
        IReadOnlyList<string> cutLabels,
        out string report)
        => WriteCutPlan(path, profiles, ids, textHeight, title, units, includeSchedule,
            schedule, cutLines, cutLabels, fractureTraces: null, out report);

    /// <summary>
    /// As <see cref="WriteCutPlan(string,IReadOnlyList{Polyline},IReadOnlyList{string},double,string,string,bool,IReadOnlyList{CutScheduleRow},IReadOnlyList{Line},IReadOnlyList{string},out string)"/>,
    /// plus <paramref name="fractureTraces"/>: bed / flaw / joint traces (already mapped
    /// into the sheet frame, e.g. by Block Face Map) drawn as open LWPOLYLINEs on a
    /// dedicated FRACTURES layer so the fabricator sees flaw positions on every face.
    /// </summary>
    public static bool WriteCutPlan(
        string path,
        IReadOnlyList<Polyline> profiles,
        IReadOnlyList<string> ids,
        double textHeight,
        string title,
        string units,
        bool includeSchedule,
        IReadOnlyList<CutScheduleRow> schedule,
        IReadOnlyList<Line> cutLines,
        IReadOnlyList<string> cutLabels,
        IReadOnlyList<Polyline> fractureTraces,
        out string report)
    {
        report = "";
        if (string.IsNullOrWhiteSpace(path)) { report = "No path."; return false; }
        if (profiles == null || profiles.Count == 0) { report = "No profiles."; return false; }
        if (textHeight <= 0) textHeight = EstimateTextHeight(profiles);
        if (string.IsNullOrWhiteSpace(units)) units = "m";
        bool hasSchedule = includeSchedule || (schedule != null && schedule.Count > 0);
        bool hasCuts = cutLines != null && cutLines.Count > 0;
        bool hasTitle = !string.IsNullOrWhiteSpace(title);
        bool hasFractures = fractureTraces != null && fractureTraces.Count > 0;

        // distinct piece layers, in first-seen order
        var layerOrder = new List<string>();
        var layerColor = new Dictionary<string, int>();
        for (int i = 0; i < profiles.Count; i++)
        {
            string id = LayerName(ids, i);
            if (!layerColor.ContainsKey(id)) { layerColor[id] = AcadColors[layerOrder.Count % AcadColors.Length]; layerOrder.Add(id); }
        }
        // reserved overlay layers (parallel lists; no ValueTuple dependency on net48)
        var overlayNames = new List<string>();
        var overlayColors = new List<int>();
        if (hasTitle) { overlayNames.Add(LTitle); overlayColors.Add(5); }       // blue
        if (hasSchedule) { overlayNames.Add(LSchedule); overlayColors.Add(7); } // white/black
        if (hasCuts) { overlayNames.Add(LCutSeq); overlayColors.Add(1); }       // red
        if (hasFractures) { overlayNames.Add(LFractures); overlayColors.Add(30); } // orange

        var sb = new StringBuilder();
        // ---- HEADER (minimal, declares R2000 so LWPOLYLINE is valid) ----
        W(sb, 0, "SECTION"); W(sb, 2, "HEADER");
        W(sb, 9, "$ACADVER"); W(sb, 1, "AC1015");
        W(sb, 9, "$INSUNITS"); W(sb, 70, units == "mm" ? "4" : "6");   // 4 mm, 6 m (informational)
        W(sb, 9, "$HANDSEED"); W(sb, 5, "FFFF");
        W(sb, 0, "ENDSEC");
        // ---- TABLES (LAYER) ----
        W(sb, 0, "SECTION"); W(sb, 2, "TABLES");
        W(sb, 0, "TABLE"); W(sb, 2, "LAYER"); W(sb, 70, (layerOrder.Count + overlayNames.Count).ToString(CI));
        W(sb, 0, "LAYER"); W(sb, 2, "0"); W(sb, 70, "0"); W(sb, 62, "7"); W(sb, 6, "CONTINUOUS");
        foreach (var ln in layerOrder)
        {
            W(sb, 0, "LAYER"); W(sb, 2, San(ln)); W(sb, 70, "0");
            W(sb, 62, layerColor[ln].ToString(CI)); W(sb, 6, "CONTINUOUS");
        }
        for (int k = 0; k < overlayNames.Count; k++)
        {
            W(sb, 0, "LAYER"); W(sb, 2, overlayNames[k]); W(sb, 70, "0");
            W(sb, 62, overlayColors[k].ToString(CI)); W(sb, 6, "CONTINUOUS");
        }
        W(sb, 0, "ENDTAB"); W(sb, 0, "ENDSEC");
        // ---- ENTITIES ----
        W(sb, 0, "SECTION"); W(sb, 2, "ENTITIES");
        int written = 0;
        int handle = 0x100;

        // overall extent of the pieces (anchor for the title + schedule table)
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

        for (int i = 0; i < profiles.Count; i++)
        {
            var pl = profiles[i];
            if (pl == null || pl.Count < 2) continue;
            string layer = LayerName(ids, i);
            // managed closure test (first == last); avoids Polyline.IsClosed allocating a native array
            bool closed = pl.Count >= 2 && pl[0].DistanceTo(pl[pl.Count - 1]) < 1e-9;
            int nv = closed ? pl.Count - 1 : pl.Count;   // drop duplicate closing vertex
            if (nv < 2) continue;

            W(sb, 0, "LWPOLYLINE"); W(sb, 5, (handle++).ToString("X", CI));
            W(sb, 100, "AcDbEntity"); W(sb, 8, San(layer)); W(sb, 100, "AcDbPolyline");
            W(sb, 90, nv.ToString(CI)); W(sb, 70, closed ? "1" : "0");
            for (int v = 0; v < nv; v++)
            {
                W(sb, 10, pl[v].X.ToString("0.######", CI)); W(sb, 20, pl[v].Y.ToString("0.######", CI));
                if (pl[v].X < minX) minX = pl[v].X; if (pl[v].X > maxX) maxX = pl[v].X;
                if (pl[v].Y < minY) minY = pl[v].Y; if (pl[v].Y > maxY) maxY = pl[v].Y;
            }

            // label at centroid
            var c = Centroid(pl, nv);
            AppendText(sb, ref handle, San(layer), c.X, c.Y, textHeight, San(layer));
            written++;
        }

        // ---- fracture / bed / flaw traces (open polylines, no labels) ----
        if (hasFractures)
        {
            foreach (var tr in fractureTraces)
            {
                if (tr == null || tr.Count < 2) continue;
                bool trClosed = tr.Count >= 3 && tr[0].DistanceTo(tr[tr.Count - 1]) < 1e-9;
                int tn = trClosed ? tr.Count - 1 : tr.Count;
                if (tn < 2) continue;
                W(sb, 0, "LWPOLYLINE"); W(sb, 5, (handle++).ToString("X", CI));
                W(sb, 100, "AcDbEntity"); W(sb, 8, LFractures); W(sb, 100, "AcDbPolyline");
                W(sb, 90, tn.ToString(CI)); W(sb, 70, trClosed ? "1" : "0");
                for (int v = 0; v < tn; v++)
                { W(sb, 10, tr[v].X.ToString("0.######", CI)); W(sb, 20, tr[v].Y.ToString("0.######", CI)); }
            }
        }

        // ---- cut-sequence lines (numbered saw passes) ----
        if (hasCuts)
        {
            for (int i = 0; i < cutLines.Count; i++)
            {
                var ln = cutLines[i];
                if (!ln.IsValid || ln.Length < 1e-9) continue;
                AppendLine(sb, ref handle, LCutSeq, ln.FromX, ln.FromY, ln.ToX, ln.ToY);
                string lab = (cutLabels != null && i < cutLabels.Count && !string.IsNullOrWhiteSpace(cutLabels[i]))
                    ? cutLabels[i] : (i + 1).ToString(CI);
                double mx = 0.5 * (ln.FromX + ln.ToX);
                double my = 0.5 * (ln.FromY + ln.ToY);
                AppendText(sb, ref handle, LCutSeq, mx, my, textHeight, lab);
            }
        }

        // ---- title + schedule table, anchored below the pieces ----
        if (hasTitle || hasSchedule)
        {
            if (minX > maxX) { minX = 0; maxX = 1; minY = 0; maxY = 1; }
            double th = textHeight;
            double x0 = minX;
            double y = minY - th * 2.5;   // gap below the pieces

            if (hasTitle)
            {
                AppendText(sb, ref handle, LTitle, x0, y, th * 1.6, title.ToUpperInvariant());
                y -= th * 1.6 * 1.8;
                AppendText(sb, ref handle, LTitle, x0, y, th * 0.9, "units: " + units +
                    "   (SCHEDULE = cut list, CUT_SEQUENCE = saw order" + (hasFractures ? ", FRACTURES = bed/flaw traces)" : ")"));
                y -= th * 2.2;
            }

            if (hasSchedule)
            {
                var rows = schedule != null && schedule.Count > 0
                    ? new List<CutScheduleRow>(schedule)
                    : AutoScheduleFromProfiles(profiles, ids);
                AppendScheduleTable(sb, ref handle, LSchedule, x0, y, th, units, rows);
            }
        }

        W(sb, 0, "ENDSEC");
        W(sb, 0, "EOF");

        try { File.WriteAllText(path, sb.ToString()); }
        catch (Exception ex) { report = "Write failed: " + ex.Message; return false; }

        var extra = new StringBuilder();
        if (hasSchedule) extra.Append(" + cut-list table");
        if (hasCuts) extra.Append($" + {cutLines.Count} numbered cut line(s)");
        if (hasFractures) extra.Append($" + {fractureTraces.Count} fracture trace(s)");
        if (hasTitle) extra.Append(" + title");
        report = $"Wrote {written} profile(s) on {layerOrder.Count} piece layer(s){extra} to {Path.GetFileName(path)} (DXF R2000).";
        return true;
    }

    /// <summary>
    /// Project each profile onto its own best-fit plane, orient it to world XY, and
    /// shelf-nest the flattened outlines left-to-right, wrapping at <paramref name="sheetWidth"/>.
    /// Returns the laid-out (2D) polylines aligned to <paramref name="profiles"/> by index.
    /// </summary>
    public static List<Polyline> FlattenAndNest(
        IReadOnlyList<Polyline> profiles, double gap, double sheetWidth)
    {
        var outp = new List<Polyline>();
        if (profiles == null) return outp;
        if (gap < 0) gap = 0;
        double cx = 0, cy = 0, rowH = 0;
        foreach (var pl in profiles)
        {
            if (pl == null || pl.Count < 2) { outp.Add(pl); continue; }
            // to that profile's plane, then to world XY
            if (!Plane.FitPlaneToPoints(pl, out Plane pln).Equals(PlaneFitResult.Failure) && pln.IsValid)
            {
                var flat = new Polyline(pl);
                flat.Transform(Transform.PlaneToPlane(pln, Plane.WorldXY));
                // move so bbox min is at origin
                var bb = flat.BoundingBox;
                flat.Transform(Transform.Translation(-bb.Min.X, -bb.Min.Y, -bb.Min.Z));
                double w = bb.Max.X - bb.Min.X, h = bb.Max.Y - bb.Min.Y;
                if (sheetWidth > 0 && cx > 0 && cx + w > sheetWidth) { cx = 0; cy += rowH + gap; rowH = 0; }
                flat.Transform(Transform.Translation(cx, cy, 0));
                cx += w + gap; rowH = Math.Max(rowH, h);
                outp.Add(flat);
            }
            else outp.Add(pl);
        }
        return outp;
    }

    // ---------- schedule helpers ----------

    // Group identical (rounded) piece footprints into cut-list rows with a quantity.
    private static List<CutScheduleRow> AutoScheduleFromProfiles(IReadOnlyList<Polyline> profiles, IReadOnlyList<string> ids)
    {
        var order = new List<string>();
        var acc = new Dictionary<string, CutScheduleRow>();
        for (int i = 0; i < profiles.Count; i++)
        {
            var pl = profiles[i];
            if (pl == null || pl.Count < 2) continue;
            var bb = pl.BoundingBox;
            double w = Math.Round(bb.Max.X - bb.Min.X, 3), h = Math.Round(bb.Max.Y - bb.Min.Y, 3);
            string key = w.ToString("0.###", CI) + "x" + h.ToString("0.###", CI);
            if (!acc.TryGetValue(key, out var row))
            {
                row = new CutScheduleRow(LayerName(ids, i), w, h, 0, 0, "saw");
                order.Add(key);
            }
            row.Qty += 1;
            acc[key] = row;
        }
        var list = new List<CutScheduleRow>();
        foreach (var k in order) list.Add(acc[k]);
        return list;
    }

    // Draw a boxed cut list: header + one row per piece group, with a LINE grid.
    private static void AppendScheduleTable(
        StringBuilder sb, ref int handle, string layer,
        double x0, double yTop, double th, string units, IReadOnlyList<CutScheduleRow> rows)
    {
        // column layout (widths in model units)
        double[] cw = { th * 3, th * 10, th * 15, th * 5, th * 7 };
        string[] hdr = { "#", "PIECE", "SIZE (" + units + ")", "QTY", "OP" };
        int nCol = cw.Length;
        double[] cx = new double[nCol + 1];
        cx[0] = x0;
        for (int c = 0; c < nCol; c++) cx[c + 1] = cx[c] + cw[c];
        double tableW = cx[nCol] - cx[0];
        double rowH = th * 1.9;
        double pad = th * 0.45;
        int nRow = rows.Count + 1;                 // + header
        double yBot = yTop - nRow * rowH;

        // grid: outer border + row/col separators
        for (int r = 0; r <= nRow; r++) AppendLine(sb, ref handle, layer, cx[0], yTop - r * rowH, cx[nCol], yTop - r * rowH);
        for (int c = 0; c <= nCol; c++) AppendLine(sb, ref handle, layer, cx[c], yTop, cx[c], yBot);

        // header text
        for (int c = 0; c < nCol; c++)
            AppendText(sb, ref handle, layer, cx[c] + pad, yTop - rowH + pad, th * 0.85, hdr[c]);

        // rows
        for (int i = 0; i < rows.Count; i++)
        {
            double yr = yTop - (i + 2) * rowH + pad;
            var r = rows[i];
            string size = r.Depth > 0
                ? $"{F(r.Width)} x {F(r.Height)} x {F(r.Depth)}"
                : $"{F(r.Width)} x {F(r.Height)}";
            AppendText(sb, ref handle, layer, cx[0] + pad, yr, th * 0.8, (i + 1).ToString(CI));
            AppendText(sb, ref handle, layer, cx[1] + pad, yr, th * 0.8, San(string.IsNullOrWhiteSpace(r.Id) ? "piece" : r.Id));
            AppendText(sb, ref handle, layer, cx[2] + pad, yr, th * 0.8, size);
            AppendText(sb, ref handle, layer, cx[3] + pad, yr, th * 0.8, (r.Qty > 0 ? r.Qty : 1).ToString(CI));
            AppendText(sb, ref handle, layer, cx[4] + pad, yr, th * 0.8, San(string.IsNullOrWhiteSpace(r.Op) ? "saw" : r.Op));
        }
    }

    // ---------- entity helpers ----------
    private static void W(StringBuilder sb, int code, string val)
    { sb.Append(code.ToString(CI)); sb.Append('\n'); sb.Append(val); sb.Append('\n'); }

    private static void AppendText(StringBuilder sb, ref int handle, string layer, double x, double y, double h, string text)
    {
        W(sb, 0, "TEXT"); W(sb, 5, (handle++).ToString("X", CI));
        W(sb, 100, "AcDbEntity"); W(sb, 8, San(layer)); W(sb, 100, "AcDbText");
        W(sb, 10, x.ToString("0.######", CI)); W(sb, 20, y.ToString("0.######", CI)); W(sb, 30, "0");
        W(sb, 40, h.ToString("0.######", CI)); W(sb, 1, text);
    }

    private static void AppendLine(StringBuilder sb, ref int handle, string layer, double x1, double y1, double x2, double y2)
    {
        W(sb, 0, "LINE"); W(sb, 5, (handle++).ToString("X", CI));
        W(sb, 100, "AcDbEntity"); W(sb, 8, San(layer)); W(sb, 100, "AcDbLine");
        W(sb, 10, x1.ToString("0.######", CI)); W(sb, 20, y1.ToString("0.######", CI)); W(sb, 30, "0");
        W(sb, 11, x2.ToString("0.######", CI)); W(sb, 21, y2.ToString("0.######", CI)); W(sb, 31, "0");
    }

    private static string F(double v) => v.ToString("0.###", CI);

    private static string LayerName(IReadOnlyList<string> ids, int i)
        => (ids != null && ids.Count > i && !string.IsNullOrWhiteSpace(ids[i]))
            ? ids[i] : "piece_" + (i + 1).ToString("D3", CI);

    private static string San(string s)
    { // DXF layer/text: strip control chars and the DXF-reserved <>/\":;?*|=`,
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            sb.Append(ch < 32 || "<>/\\\":;?*|=`,".IndexOf(ch) >= 0 ? '_' : ch);
        return sb.Length == 0 ? "_" : sb.ToString();
    }

    private static Point3d Centroid(Polyline pl, int nv)
    {
        double x = 0, y = 0;
        for (int v = 0; v < nv; v++) { x += pl[v].X; y += pl[v].Y; }
        return new Point3d(x / nv, y / nv, 0);
    }

    private static double EstimateTextHeight(IReadOnlyList<Polyline> profiles)
    {
        double diag = 1;
        foreach (var pl in profiles) if (pl != null && pl.Count > 1) diag = Math.Max(diag, pl.BoundingBox.Diagonal.Length);
        return Math.Max(1e-3, diag * 0.05);
    }
}
