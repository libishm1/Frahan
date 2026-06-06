#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace Frahan.Core.Registration;

// =============================================================================
// MarkerFileReader — import photogrammetry markers / ground-control points
// (GCPs) so a floating photogrammetry result can be positioned + scaled onto a
// known base via the existing Georeference (Align by Points) / RegistrationApi
// (Horn). We do NOT reconstruct photogrammetry; we ingest the markers that fix
// the 7-DoF similarity.
//
// Tolerant CSV (Metashape / COLMAP / RealityCapture can export to it):
//   label, worldX, worldY, worldZ                          (world only)
//   label, worldX, worldY, worldZ, modelX, modelY, modelZ  (world + model)
// A numeric first token = no label (auto "M{i}"), so 3 or 6 numeric columns are
// also accepted. Lines starting with '#' or '//' and blank lines are skipped.
//
// Pure-managed (no Rhino types) so it is unit-testable headless.
// =============================================================================

public sealed class MarkerControlPoint
{
    public string Label;
    public double[] World;        // length 3 (target / base frame)
    public double[] Model;        // length 3 or null (source / scan frame)
    public bool HasModel => Model != null;
}

public static class MarkerFileReader
{
    private static readonly char[] Seps = { ',', ';', '\t', ' ' };

    public static IReadOnlyList<MarkerControlPoint> ReadCsv(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("Marker file not found", path);
        return Parse(File.ReadAllLines(path));
    }

    /// <summary>Auto-dispatch on file extension: .csv -> ReadCsv, .dxf -> ReadDxf,
    /// .xml -> ReadChunkXml. Per MRAC Proposal 3 (2026-05-31).</summary>
    public static IReadOnlyList<MarkerControlPoint> Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
        string ext = Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".dxf": return ReadDxf(path);
            case ".xml": return ReadChunkXml(path);
            case ".csv":
            case ".txt":
            case "":
            default: return ReadCsv(path);
        }
    }

    // ========================================================================
    // DXF reader (Metashape coded-target export -- AutoCAD R12 ASCII)
    // ========================================================================

    /// <summary>Read a Metashape DXF marker export. Metashape emits POINT entities
    /// whose 8 = layer, 1 = label / handle, 10 = X, 20 = Y, 30 = Z. Minimal AutoCAD
    /// R12 ASCII subset; no full DXF parser dependency.</summary>
    public static IReadOnlyList<MarkerControlPoint> ReadDxf(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("DXF not found", path);
        var lines = File.ReadAllLines(path);
        return ParseDxfPoints(lines);
    }

    /// <summary>Public for tests + in-memory use.</summary>
    public static IReadOnlyList<MarkerControlPoint> ParseDxfPoints(IList<string> lines)
    {
        var inv = CultureInfo.InvariantCulture;
        var result = new List<MarkerControlPoint>();
        int auto = 0;

        // DXF ASCII pairs: group code on one line, value on the next.
        // We walk pairs and assemble POINT entities. ENTITIES section start = "0\nSECTION\n2\nENTITIES";
        // POINT entity start = "0\nPOINT".
        bool inEntities = false;
        bool inPoint = false;
        string currentLabel = null;
        double x = 0, y = 0, z = 0;
        bool sawX = false, sawY = false;

        for (int i = 0; i + 1 < lines.Count; i += 2)
        {
            string codeStr = lines[i].Trim();
            string val = lines[i + 1];
            int code;
            if (!int.TryParse(codeStr, NumberStyles.Integer, inv, out code))
            {
                // Misaligned pair; skip one line.
                i--;
                continue;
            }

            if (code == 0)
            {
                // Entity boundary. Flush previous POINT if complete.
                if (inPoint && sawX && sawY)
                {
                    string label = !string.IsNullOrEmpty(currentLabel)
                        ? currentLabel
                        : "M" + (auto++).ToString("D3", inv);
                    result.Add(new MarkerControlPoint
                    {
                        Label = label,
                        World = new[] { x, y, z },
                    });
                }
                inPoint = false;
                currentLabel = null; x = y = z = 0; sawX = sawY = false;

                string ent = val.Trim();
                if (ent == "SECTION") continue;
                if (ent == "ENDSEC") { inEntities = false; continue; }
                if (ent == "POINT") { inPoint = true; }
            }
            else if (code == 2 && val.Trim() == "ENTITIES")
            {
                inEntities = true;
            }
            else if (inPoint && inEntities)
            {
                switch (code)
                {
                    case 1: // primary text / value
                    case 8: // layer name (Metashape uses layer as label)
                        if (string.IsNullOrEmpty(currentLabel)) currentLabel = val.Trim();
                        break;
                    case 10: if (double.TryParse(val, NumberStyles.Float, inv, out x)) sawX = true; break;
                    case 20: if (double.TryParse(val, NumberStyles.Float, inv, out y)) sawY = true; break;
                    case 30: double.TryParse(val, NumberStyles.Float, inv, out z); break;
                }
            }
        }

        // Flush the final POINT if any.
        if (inPoint && sawX && sawY)
        {
            string label = !string.IsNullOrEmpty(currentLabel)
                ? currentLabel
                : "M" + auto.ToString("D3", inv);
            result.Add(new MarkerControlPoint
            {
                Label = label,
                World = new[] { x, y, z },
            });
        }

        return result;
    }

    // ========================================================================
    // Chunk XML reader (Metashape chunk.zip:doc.xml -> markers element)
    // ========================================================================

    /// <summary>Read markers from a Metashape chunk-XML (the file extracted from
    /// chunk.zip). Surfaces label + reference world position when present.</summary>
    public static IReadOnlyList<MarkerControlPoint> ReadChunkXml(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("Chunk XML not found", path);
        var doc = new XmlDocument();
        doc.Load(path);
        return ParseChunkXml(doc);
    }

    /// <summary>Public for tests.</summary>
    public static IReadOnlyList<MarkerControlPoint> ParseChunkXml(XmlDocument doc)
    {
        var inv = CultureInfo.InvariantCulture;
        var result = new List<MarkerControlPoint>();
        if (doc?.DocumentElement == null) return result;

        var markerNodes = doc.SelectNodes("//markers/marker");
        if (markerNodes == null) return result;

        int auto = 0;
        foreach (XmlElement mEl in markerNodes)
        {
            string label = mEl.GetAttribute("label");
            if (string.IsNullOrWhiteSpace(label))
                label = "M" + (auto++).ToString("D3", inv);

            var refEl = mEl.SelectSingleNode("reference") as XmlElement;
            if (refEl == null) continue;

            if (TryDouble(refEl, "x", out double x) &&
                TryDouble(refEl, "y", out double y) &&
                TryDouble(refEl, "z", out double z))
            {
                result.Add(new MarkerControlPoint
                {
                    Label = label,
                    World = new[] { x, y, z },
                });
            }
        }
        return result;
    }

    private static bool TryDouble(XmlElement e, string attr, out double v)
    {
        v = 0;
        return double.TryParse(e.GetAttribute(attr),
            NumberStyles.Float | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture, out v);
    }

    /// <summary>Parse already-loaded lines (public for tests / in-memory use).</summary>
    public static IReadOnlyList<MarkerControlPoint> Parse(IEnumerable<string> lines)
    {
        var inv = CultureInfo.InvariantCulture;
        var result = new List<MarkerControlPoint>();
        int auto = 0;
        foreach (var raw in lines)
        {
            if (raw == null) continue;
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;

            var toks = line.Split(Seps, StringSplitOptions.RemoveEmptyEntries);
            if (toks.Length < 3) continue;

            int numStart;
            string label;
            if (double.TryParse(toks[0], NumberStyles.Float, inv, out _))
            {
                label = "M" + (auto++).ToString("D3", inv);
                numStart = 0;
            }
            else
            {
                label = toks[0];
                numStart = 1;
            }

            var nums = new List<double>(toks.Length - numStart);
            for (int i = numStart; i < toks.Length; i++)
            {
                if (double.TryParse(toks[i], NumberStyles.Float, inv, out double v)) nums.Add(v);
                else break; // stop at first non-numeric trailing column
            }
            if (nums.Count < 3) continue; // not a usable control point

            var mp = new MarkerControlPoint
            {
                Label = label,
                World = new[] { nums[0], nums[1], nums[2] },
            };
            if (nums.Count >= 6)
                mp.Model = new[] { nums[3], nums[4], nums[5] };
            result.Add(mp);
        }
        return result;
    }
}
