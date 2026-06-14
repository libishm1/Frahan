#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.TwoD;

// =============================================================================
// CsvPartsReaderComponent — read a 2D irregular-packing benchmark CSV
// (Albano / Blaz / Dagli / Jakobs / etc.) and emit one closed
// PolylineCurve per part.
//
// CSV format (matches the corpus at
// `code_ws/Agent-orchestration-main/.../references/2D-Irregular-Packing-
// Algorithm/data/*.csv`):
//
//   Header: num,polygon
//   Each data row:
//     <int_multiplicity>,"[[x1, y1], [x2, y2], ..., [xN, yN]]"
//
// where the second field is a JSON-ish list of [x, y] pairs. Multiplicity
// is the number of duplicates of that part the benchmark expects to be
// placed.
//
// Output curves are CLOSED PolylineCurves on the world-XY plane. Each
// listed part is emitted exactly `num` times in row-major order so the
// downstream packer sees the requested multiplicity.
//
// ComponentGuid: F2D00CSV-CADC-4F2D-9CSV-7E60CADA15A0
// Frahan > 2D > CSV Parts Reader
// =============================================================================

/// <summary>
/// Frahan &gt; 2D &gt; CSV Parts Reader.
/// Loads Albano-format 2D packing benchmark CSVs as closed PolylineCurves.
/// </summary>
[DesignApplication(
    "Read an Albano-format 2D packing benchmark CSV  (num,polygon rows where polygon is a JSON-ish [[x,y], ...])...",
    DesignFlow.Bridges,
    Precedent = "Standard CSV import for 2D parts (Bennell Oliveira 2008 review-conforming inputs)")]
public sealed class CsvPartsReaderComponent : FrahanComponentBase
{
    public CsvPartsReaderComponent()
        : base("CSV Parts Reader", "CSVParts",
            "Read an Albano-format 2D packing benchmark CSV " +
            "(num,polygon rows where polygon is a JSON-ish [[x,y], ...]) " +
            "and emit one closed PolylineCurve per part (with the row's " +
            "multiplicity respected).",
            "Frahan", "2D Packing")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("F2D00C5F-CADC-4F2D-9C5F-7E60CADA15A0");

    protected override Bitmap Icon => IconProvider.Load("CurveToPolygon.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("CSV Path", "Csv",
            "Absolute path to a benchmark CSV (Albano/Blaz/Dagli/Jakobs " +
            "format: header 'num,polygon', then rows with an integer " +
            "multiplicity and a JSON-ish polygon vertex list).",
            GH_ParamAccess.item);
        p.AddNumberParameter("Scale", "S",
            "Per-coordinate scale factor (e.g. set 0.001 to convert " +
            "millimetre Albano coordinates to metres).",
            GH_ParamAccess.item, 1.0);
        p.AddBooleanParameter("Expand Multiplicity", "E",
            "If True, each row is emitted `num` times. If False, the " +
            "row is emitted once and the multiplicity is exposed " +
            "verbatim in the Counts output.",
            GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddCurveParameter("Parts", "P",
            "One closed PolylineCurve per emitted part.",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Counts", "N",
            "Per-row multiplicity from the CSV.", GH_ParamAccess.list);
        p.AddIntegerParameter("Row Indices", "R",
            "0-based source row index per emitted part (lines up with " +
            "the canonical benchmark numbering).",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Total Parts", "T",
            "Total number of curves emitted.", GH_ParamAccess.item);
        p.AddTextParameter("Report", "Rep",
            "One-line summary of the read.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        string path = string.Empty;
        if (!da.GetData(0, ref path) || string.IsNullOrWhiteSpace(path))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No CSV path.");
            return;
        }
        if (!File.Exists(path))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "CSV path does not exist: " + path);
            return;
        }

        double scale = 1.0;
        da.GetData(1, ref scale);
        if (scale == 0.0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "Scale is zero; emitting degenerate curves.");
        }

        bool expand = true;
        da.GetData(2, ref expand);

        var rows = new List<(int count, List<double[]> verts, int srcRow)>();
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Failed to read CSV: " + ex.Message);
            return;
        }

        int srcRow = -1;
        foreach (var raw in lines)
        {
            srcRow++;
            var line = raw.Trim();
            if (line.Length == 0) continue;
            // Skip the header.
            if (line.StartsWith("num", StringComparison.OrdinalIgnoreCase))
                continue;
            // First field: integer count up to the first comma.
            int comma = line.IndexOf(',');
            if (comma < 0) continue;
            if (!int.TryParse(line.Substring(0, comma).Trim(),
                              NumberStyles.Integer, CultureInfo.InvariantCulture,
                              out int count))
            {
                continue;
            }
            string remainder = line.Substring(comma + 1).Trim();
            // The remainder is quoted JSON-ish. Strip surrounding quotes.
            if (remainder.StartsWith("\"") && remainder.EndsWith("\"") &&
                remainder.Length >= 2)
            {
                remainder = remainder.Substring(1, remainder.Length - 2);
            }
            var verts = ParsePolygon(remainder);
            if (verts.Count < 3) continue;
            rows.Add((count, verts, srcRow));
        }

        var parts = new List<Curve>();
        var counts = new List<int>();
        var rowIdx = new List<int>();
        foreach (var (count, verts, src) in rows)
        {
            counts.Add(count);
            int copies = expand ? Math.Max(count, 1) : 1;
            for (int k = 0; k < copies; k++)
            {
                var pts = new Point3d[verts.Count + 1];
                for (int i = 0; i < verts.Count; i++)
                {
                    pts[i] = new Point3d(verts[i][0] * scale,
                                          verts[i][1] * scale,
                                          0.0);
                }
                pts[verts.Count] = pts[0];
                parts.Add(new PolylineCurve(pts));
                rowIdx.Add(src);
            }
        }

        da.SetDataList(0, parts);
        da.SetDataList(1, counts);
        da.SetDataList(2, rowIdx);
        da.SetData(3, parts.Count);
        da.SetData(4, string.Format(
            CultureInfo.InvariantCulture,
            "{0}: {1} rows -> {2} curves (scale={3})",
            Path.GetFileName(path), rows.Count, parts.Count, scale));
    }

    private static List<double[]> ParsePolygon(string spec)
    {
        // spec looks like: [[x1, y1], [x2, y2], ...] (with optional outer brackets)
        // Strip the outermost [...].
        var result = new List<double[]>();
        if (spec == null) return result;
        var trimmed = spec.Trim();
        if (trimmed.StartsWith("[") && trimmed.EndsWith("]") &&
            trimmed.Length >= 2)
        {
            trimmed = trimmed.Substring(1, trimmed.Length - 2);
        }
        // Now split on "], [" (allowing optional spaces).
        int depth = 0;
        var current = new System.Text.StringBuilder();
        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            if (c == '[')
            {
                depth++;
                if (depth > 1) current.Append(c);
            }
            else if (c == ']')
            {
                depth--;
                if (depth >= 1) current.Append(c);
                if (depth == 0)
                {
                    var pair = current.ToString();
                    current.Clear();
                    var nums = pair.Split(',');
                    if (nums.Length >= 2 &&
                        double.TryParse(nums[0].Trim(), NumberStyles.Float,
                                         CultureInfo.InvariantCulture, out double x) &&
                        double.TryParse(nums[1].Trim(), NumberStyles.Float,
                                         CultureInfo.InvariantCulture, out double y))
                    {
                        result.Add(new[] { x, y });
                    }
                }
            }
            else if (depth >= 1)
            {
                current.Append(c);
            }
            // Outside any [...] we ignore commas and whitespace.
        }
        return result;
    }
}
