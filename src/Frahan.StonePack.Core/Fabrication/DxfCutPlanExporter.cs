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
// This is a thin INTERFACE, not a CAD kernel: LWPOLYLINE + TEXT + LAYER only, no
// splines/arcs (curves are polylined upstream). Pure managed string + file IO
// (Rhino value types only), headless-unit-testable. STEP/IGES solids go through
// Rhino's native File > Export; this covers the 2D cut-sheet path CAM expects.
//
// Reference: AutoCAD DXF Reference (LWPOLYLINE group codes 90/70/10/20; TEXT
// 40/1; LAYER table 2/62/6). ALPHACAM/EasySTONE DXF import (research 2026-07-03).
// =============================================================================

public static class DxfCutPlanExporter
{
    private static readonly CultureInfo CI = CultureInfo.InvariantCulture;
    private static readonly int[] AcadColors = { 1, 2, 3, 4, 5, 6, 30, 40, 150, 250 };

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
    {
        report = "";
        if (string.IsNullOrWhiteSpace(path)) { report = "No path."; return false; }
        if (profiles == null || profiles.Count == 0) { report = "No profiles."; return false; }
        if (textHeight <= 0) textHeight = EstimateTextHeight(profiles);

        // distinct layers, in first-seen order
        var layerOrder = new List<string>();
        var layerColor = new Dictionary<string, int>();
        for (int i = 0; i < profiles.Count; i++)
        {
            string id = LayerName(ids, i);
            if (!layerColor.ContainsKey(id)) { layerColor[id] = AcadColors[layerOrder.Count % AcadColors.Length]; layerOrder.Add(id); }
        }

        var sb = new StringBuilder();
        // ---- HEADER (minimal, declares R2000 so LWPOLYLINE is valid) ----
        W(sb, 0, "SECTION"); W(sb, 2, "HEADER");
        W(sb, 9, "$ACADVER"); W(sb, 1, "AC1015");
        W(sb, 9, "$INSUNITS"); W(sb, 70, "6");   // 6 = metres (informational)
        W(sb, 9, "$HANDSEED"); W(sb, 5, "FFFF");
        W(sb, 0, "ENDSEC");
        // ---- TABLES (LAYER) ----
        W(sb, 0, "SECTION"); W(sb, 2, "TABLES");
        W(sb, 0, "TABLE"); W(sb, 2, "LAYER"); W(sb, 70, layerOrder.Count.ToString(CI));
        W(sb, 0, "LAYER"); W(sb, 2, "0"); W(sb, 70, "0"); W(sb, 62, "7"); W(sb, 6, "CONTINUOUS");
        foreach (var ln in layerOrder)
        {
            W(sb, 0, "LAYER"); W(sb, 2, San(ln)); W(sb, 70, "0");
            W(sb, 62, layerColor[ln].ToString(CI)); W(sb, 6, "CONTINUOUS");
        }
        W(sb, 0, "ENDTAB"); W(sb, 0, "ENDSEC");
        // ---- ENTITIES ----
        W(sb, 0, "SECTION"); W(sb, 2, "ENTITIES");
        int written = 0;
        int handle = 0x100;
        for (int i = 0; i < profiles.Count; i++)
        {
            var pl = profiles[i];
            if (pl == null || pl.Count < 2) continue;
            string layer = LayerName(ids, i);
            bool closed = pl.IsClosed;
            int nv = closed ? pl.Count - 1 : pl.Count;   // drop duplicate closing vertex
            if (nv < 2) continue;

            W(sb, 0, "LWPOLYLINE"); W(sb, 5, (handle++).ToString("X", CI));
            W(sb, 100, "AcDbEntity"); W(sb, 8, San(layer)); W(sb, 100, "AcDbPolyline");
            W(sb, 90, nv.ToString(CI)); W(sb, 70, closed ? "1" : "0");
            for (int v = 0; v < nv; v++)
            { W(sb, 10, pl[v].X.ToString("0.######", CI)); W(sb, 20, pl[v].Y.ToString("0.######", CI)); }

            // label at centroid
            var c = Centroid(pl, nv);
            W(sb, 0, "TEXT"); W(sb, 5, (handle++).ToString("X", CI));
            W(sb, 100, "AcDbEntity"); W(sb, 8, San(layer)); W(sb, 100, "AcDbText");
            W(sb, 10, c.X.ToString("0.######", CI)); W(sb, 20, c.Y.ToString("0.######", CI)); W(sb, 30, "0");
            W(sb, 40, textHeight.ToString("0.######", CI)); W(sb, 1, San(layer));
            written++;
        }
        W(sb, 0, "ENDSEC");
        W(sb, 0, "EOF");

        try { File.WriteAllText(path, sb.ToString()); }
        catch (Exception ex) { report = "Write failed: " + ex.Message; return false; }

        report = $"Wrote {written} profile(s) on {layerOrder.Count} layer(s) to {Path.GetFileName(path)} (DXF R2000).";
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

    // ---------- helpers ----------
    private static void W(StringBuilder sb, int code, string val)
    { sb.Append(code.ToString(CI)); sb.Append('\n'); sb.Append(val); sb.Append('\n'); }

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
