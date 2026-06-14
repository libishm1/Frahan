#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Rhino.Geometry;

namespace Frahan.Core.Discontinuity.Ingest;

// =============================================================================
// DiscontinuityReader -- dispatch a discontinuity vector file to the right
// format reader and return a DiscontinuityCollection. Formats:
//   .csv            -> CsvDiscontinuityReader      (hand-rolled, no dependency)
//   .geojson/.json  -> GeoJsonDiscontinuityReader  (NetTopologySuite.IO.GeoJSON)
//   .dxf            -> DxfDiscontinuityReader       (ASCII group-code, hand-rolled)
//   .shp            -> ShapefileDiscontinuityReader (NetTopologySuite.IO.Esri)
//
// Policy (AGENTS.md "log, skip, continue"): a malformed row is skipped with a
// warning, never thrown. The whole read throws only on a missing file.
// =============================================================================

public static class DiscontinuityReader
{
    public static DiscontinuityCollection Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("path is empty", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException("Discontinuity file not found.", path);

        string ext = Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".csv":
            case ".tsv":
            case ".txt": return CsvDiscontinuityReader.Read(path);
            case ".geojson":
            case ".json": return GeoJsonDiscontinuityReader.Read(path);
            case ".dxf": return DxfDiscontinuityReader.Read(path);
            case ".shp": return ShapefileDiscontinuityReader.Read(path);
            default:
                return new DiscontinuityCollection(
                    Array.Empty<Discontinuity>(), path, null,
                    new[] { $"Unsupported extension '{ext}'. Use .csv / .geojson / .dxf / .shp." });
        }
    }
}

// --- shared helpers ----------------------------------------------------------

internal static class IngestUtil
{
    public static bool TryNum(string s, out double v) =>
        double.TryParse((s ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out v);

    public static double NumOr(IReadOnlyDictionary<string, string> m, string key, double fallback)
        => m.TryGetValue(key, out var s) && TryNum(s, out var v) ? v : fallback;

    // Pull a discontinuity from a name->value attribute bag at a location.
    // Recognises dip/dipdir (or strike+dip), set id. Returns null if no orientation.
    public static Discontinuity FromAttributes(
        IReadOnlyDictionary<string, string> attrs, Point3d at, string source)
    {
        double dip = double.NaN, dipdir = double.NaN, strike = double.NaN;
        int setId = -1;
        foreach (var kv in attrs)
        {
            string k = kv.Key.Trim().ToLowerInvariant();
            if (!TryNum(kv.Value, out double val)) continue;
            if (k is "dip" or "dipangle" or "dip_angle") dip = val;
            else if (k is "dipdir" or "dip_dir" or "dipdirection" or "dip_direction" or "azimuth" or "azi") dipdir = val;
            else if (k == "strike") strike = val;
            else if (k is "set" or "setid" or "set_id" or "jointset" or "joint_set") setId = (int)Math.Round(val);
        }
        if (double.IsNaN(dipdir) && !double.IsNaN(strike)) dipdir = (strike + 90.0) % 360.0; // right-hand rule
        if (double.IsNaN(dip) || double.IsNaN(dipdir)) return null;
        var d = Discontinuity.FromDipDipDir(dip, dipdir, at, DiscontinuityKind.PointMeasurement, attrs, source);
        d.SetId = setId;
        return d;
    }
}

// --- CSV ---------------------------------------------------------------------

public static class CsvDiscontinuityReader
{
    public static DiscontinuityCollection Read(string path)
    {
        var warnings = new List<string>();
        var items = new List<Discontinuity>();
        string[] lines = File.ReadAllLines(path);
        string src = Path.GetFileName(path);

        // strip blank / comment lines, remember original line numbers for warnings
        var rows = new List<(int ln, string text)>();
        for (int i = 0; i < lines.Length; i++)
        {
            string t = lines[i].Trim();
            if (t.Length == 0 || t.StartsWith("#") || t.StartsWith("//")) continue;
            rows.Add((i + 1, lines[i]));
        }
        if (rows.Count == 0)
            return new DiscontinuityCollection(items, path, null, new[] { "CSV is empty." });

        char delim = SniffDelimiter(rows[0].text);
        string[] First() => Split(rows[0].text, delim);

        // header present if the first row has any non-numeric, non-empty token
        var f0 = First();
        bool header = f0.Any(tok => tok.Trim().Length > 0 && !IngestUtil.TryNum(tok, out _));
        Dictionary<string, int> col = header ? MapHeader(f0) : null;
        int start = header ? 1 : 0;

        for (int r = start; r < rows.Count; r++)
        {
            var (ln, raw) = rows[r];
            string[] tok = Split(raw, delim);
            try
            {
                Discontinuity d = header
                    ? ParseHeaderRow(tok, col, src)
                    : ParseHeaderlessRow(tok, src);
                if (d != null) items.Add(d);
                else warnings.Add($"line {ln}: no usable orientation; skipped.");
            }
            catch (Exception ex) { warnings.Add($"line {ln}: {ex.Message}; skipped."); }
        }

        if (items.Count == 0 && warnings.Count == 0)
            warnings.Add("CSV had no parseable orientation rows.");
        return new DiscontinuityCollection(items, path, null, warnings);
    }

    private static char SniffDelimiter(string line)
    {
        int c = line.Count(ch => ch == ','), s = line.Count(ch => ch == ';'), t = line.Count(ch => ch == '\t');
        if (t >= c && t >= s && t > 0) return '\t';
        if (s >= c && s > 0) return ';';
        return ',';
    }

    private static string[] Split(string line, char delim) =>
        line.Split(delim).Select(x => x.Trim()).ToArray();

    private static Dictionary<string, int> MapHeader(string[] h)
    {
        var col = new Dictionary<string, int>();
        for (int i = 0; i < h.Length; i++)
        {
            string k = h[i].Trim().ToLowerInvariant();
            switch (k)
            {
                case "dip": case "dipangle": case "dip_angle": col["dip"] = i; break;
                case "dipdir": case "dip_dir": case "dipdirection": case "dip_direction":
                case "azimuth": case "azi": col["dipdir"] = i; break;
                case "strike": col["strike"] = i; break;
                case "nx": col["nx"] = i; break;
                case "ny": col["ny"] = i; break;
                case "nz": col["nz"] = i; break;
                case "a": col["a"] = i; break;
                case "b": col["b"] = i; break;
                case "c": col["c"] = i; break;
                case "d": col["d"] = i; break;
                case "x": case "easting": case "east": col["x"] = i; break;
                case "y": case "northing": case "north": col["y"] = i; break;
                case "z": case "elev": case "elevation": case "height": col["z"] = i; break;
                case "set": case "setid": case "set_id": case "jointset": case "joint_set": col["set"] = i; break;
            }
        }
        return col;
    }

    private static double Get(string[] tok, Dictionary<string, int> col, string key, double fallback)
    {
        if (col.TryGetValue(key, out int i) && i < tok.Length && IngestUtil.TryNum(tok[i], out double v)) return v;
        return fallback;
    }

    private static Point3d Loc(string[] tok, Dictionary<string, int> col) =>
        new Point3d(Get(tok, col, "x", 0), Get(tok, col, "y", 0), Get(tok, col, "z", 0));

    private static Discontinuity ParseHeaderRow(string[] tok, Dictionary<string, int> col, string src)
    {
        var loc = Loc(tok, col);
        int set = (int)Math.Round(Get(tok, col, "set", -1));

        Discontinuity d = null;
        if (col.ContainsKey("dip") && (col.ContainsKey("dipdir") || col.ContainsKey("strike")))
        {
            double dip = Get(tok, col, "dip", double.NaN);
            double dd = col.ContainsKey("dipdir")
                ? Get(tok, col, "dipdir", double.NaN)
                : (Get(tok, col, "strike", double.NaN) + 90.0) % 360.0;
            if (!double.IsNaN(dip) && !double.IsNaN(dd))
                d = Discontinuity.FromDipDipDir(dip, dd, loc, DiscontinuityKind.PointMeasurement, null, src);
        }
        else if (col.ContainsKey("nx") && col.ContainsKey("ny") && col.ContainsKey("nz"))
        {
            var n = new Vector3d(Get(tok, col, "nx", 0), Get(tok, col, "ny", 0), Get(tok, col, "nz", 0));
            if (n.SquareLength > 1e-18) d = Discontinuity.FromPlane(n, loc, DiscontinuityKind.Plane, null, src);
        }
        else if (col.ContainsKey("a") && col.ContainsKey("b") && col.ContainsKey("c") && col.ContainsKey("d"))
        {
            d = PlaneCoeff(Get(tok, col, "a", 0), Get(tok, col, "b", 0), Get(tok, col, "c", 0), Get(tok, col, "d", 0), src);
        }
        if (d != null && set >= 0) d.SetId = set;
        return d;
    }

    private static Discontinuity ParseHeaderlessRow(string[] tok, string src)
    {
        var nums = new List<double>();
        foreach (var t in tok) if (IngestUtil.TryNum(t, out double v)) nums.Add(v);
        switch (nums.Count)
        {
            case 2: // dip, dipdir
                return Discontinuity.FromDipDipDir(nums[0], nums[1], Point3d.Origin, DiscontinuityKind.PointMeasurement, null, src);
            case 3: // nx, ny, nz
                var n = new Vector3d(nums[0], nums[1], nums[2]);
                return n.SquareLength > 1e-18 ? Discontinuity.FromPlane(n, Point3d.Origin, DiscontinuityKind.Plane, null, src) : null;
            case 4: // a, b, c, d  (plane n.p = d)
                return PlaneCoeff(nums[0], nums[1], nums[2], nums[3], src);
            case 5: // dip, dipdir, x, y, z
                return Discontinuity.FromDipDipDir(nums[0], nums[1], new Point3d(nums[2], nums[3], nums[4]), DiscontinuityKind.PointMeasurement, null, src);
            case 6: // nx, ny, nz, x, y, z
                var n6 = new Vector3d(nums[0], nums[1], nums[2]);
                return n6.SquareLength > 1e-18 ? Discontinuity.FromPlane(n6, new Point3d(nums[3], nums[4], nums[5]), DiscontinuityKind.Plane, null, src) : null;
            default:
                throw new FormatException($"{nums.Count} numeric columns is ambiguous (use a header)");
        }
    }

    // Plane n.p = d -> centroid = (d / |n|^2) * n (closest point to origin on the plane).
    private static Discontinuity PlaneCoeff(double a, double b, double c, double d, string src)
    {
        var n = new Vector3d(a, b, c);
        double len2 = n.SquareLength;
        if (len2 < 1e-18) return null;
        var centroid = new Point3d(d / len2 * a, d / len2 * b, d / len2 * c);
        return Discontinuity.FromPlane(n, centroid, DiscontinuityKind.Plane, null, src);
    }
}

// --- DXF (ASCII group-code) --------------------------------------------------

public static class DxfDiscontinuityReader
{
    public static DiscontinuityCollection Read(string path)
    {
        var warnings = new List<string>();
        var items = new List<Discontinuity>();
        string src = Path.GetFileName(path);

        // binary DXF starts with this sentinel
        using (var fs = File.OpenRead(path))
        {
            var head = new byte[22];
            int got = fs.Read(head, 0, head.Length);
            string h = System.Text.Encoding.ASCII.GetString(head, 0, got);
            if (h.StartsWith("AutoCAD Binary DXF"))
                return new DiscontinuityCollection(items, path, null,
                    new[] { "Binary DXF is not supported; re-save as ASCII DXF." });
        }

        // group-code pairs: (code, value) one per line each
        string[] raw = File.ReadAllLines(path);
        var pairs = new List<(int code, string val)>(raw.Length / 2 + 1);
        for (int i = 0; i + 1 < raw.Length; i += 2)
        {
            if (!int.TryParse(raw[i].Trim(), out int code)) { i -= 1; continue; } // resync on stray line
            pairs.Add((code, raw[i + 1].Trim()));
        }

        bool bulgeWarned = false;
        int p = 0;
        while (p < pairs.Count)
        {
            if (pairs[p].code != 0) { p++; continue; }
            string entity = pairs[p].val.ToUpperInvariant();
            int bodyStart = ++p;
            // collect this entity's group codes until the next code-0
            var body = new List<(int code, string val)>();
            while (p < pairs.Count && pairs[p].code != 0) { body.Add(pairs[p]); p++; }

            try
            {
                switch (entity)
                {
                    case "LINE": AddLine(body, items, src); break;
                    case "LWPOLYLINE": AddLwPolyline(body, items, src, ref bulgeWarned, warnings); break;
                    case "3DFACE": AddFace(body, items, src); break;
                    case "POLYLINE":
                        // vertices follow as separate VERTEX entities until SEQEND
                        var vtx = new List<Point3d>();
                        while (p < pairs.Count)
                        {
                            if (pairs[p].code == 0)
                            {
                                string e2 = pairs[p].val.ToUpperInvariant();
                                if (e2 == "VERTEX")
                                {
                                    p++; var vb = new List<(int, string)>();
                                    while (p < pairs.Count && pairs[p].code != 0) { vb.Add(pairs[p]); p++; }
                                    var pt = Pt(vb);
                                    if (pt.HasValue) vtx.Add(pt.Value);
                                }
                                else if (e2 == "SEQEND") { p++; while (p < pairs.Count && pairs[p].code != 0) p++; break; }
                                else break;
                            }
                            else p++;
                        }
                        if (vtx.Count >= 2) items.Add(Discontinuity.FromTrace(vtx, null, src));
                        break;
                    default: break; // ignore TEXT, INSERT, etc.
                }
            }
            catch (Exception ex) { warnings.Add($"entity {entity}: {ex.Message}; skipped."); }
        }

        if (items.Count == 0) warnings.Add("No LINE / LWPOLYLINE / POLYLINE / 3DFACE entities found.");
        return new DiscontinuityCollection(items, path, null, warnings);
    }

    private static double V(List<(int code, string val)> body, int code, double fallback)
    {
        foreach (var b in body) if (b.code == code && IngestUtil.TryNum(b.val, out double v)) return v;
        return fallback;
    }

    private static Point3d? Pt(List<(int code, string val)> body)
    {
        bool hasX = false; double x = 0, y = 0, z = 0;
        foreach (var b in body)
        {
            if (b.code == 10 && IngestUtil.TryNum(b.val, out double vx)) { x = vx; hasX = true; }
            else if (b.code == 20 && IngestUtil.TryNum(b.val, out double vy)) y = vy;
            else if (b.code == 30 && IngestUtil.TryNum(b.val, out double vz)) z = vz;
        }
        return hasX ? new Point3d(x, y, z) : (Point3d?)null;
    }

    private static void AddLine(List<(int code, string val)> body, List<Discontinuity> items, string src)
    {
        var a = new Point3d(V(body, 10, 0), V(body, 20, 0), V(body, 30, 0));
        var b = new Point3d(V(body, 11, 0), V(body, 21, 0), V(body, 31, 0));
        items.Add(Discontinuity.FromTrace(new[] { a, b }, null, src)); // 2 pts -> trace, no plane fit
    }

    private static void AddLwPolyline(List<(int code, string val)> body, List<Discontinuity> items,
        string src, ref bool bulgeWarned, List<string> warnings)
    {
        double elev = V(body, 38, 0);
        var pts = new List<Point3d>();
        double? px = null, py = null;
        foreach (var bb in body)
        {
            if (bb.code == 42 && !bulgeWarned) { bulgeWarned = true; warnings.Add("LWPOLYLINE bulge (arc) segments are linearised."); }
            if (bb.code == 10 && IngestUtil.TryNum(bb.val, out double vx))
            {
                if (px.HasValue) { pts.Add(new Point3d(px.Value, py ?? 0, elev)); }
                px = vx; py = null;
            }
            else if (bb.code == 20 && IngestUtil.TryNum(bb.val, out double vy)) py = vy;
        }
        if (px.HasValue) pts.Add(new Point3d(px.Value, py ?? 0, elev));
        // closed flag (70 bit 1)
        int flags = (int)V(body, 70, 0);
        if ((flags & 1) != 0 && pts.Count >= 3) pts.Add(pts[0]);
        if (pts.Count >= 2) items.Add(Discontinuity.FromTrace(pts, null, src));
    }

    private static void AddFace(List<(int code, string val)> body, List<Discontinuity> items, string src)
    {
        var v0 = new Point3d(V(body, 10, 0), V(body, 20, 0), V(body, 30, 0));
        var v1 = new Point3d(V(body, 11, 0), V(body, 21, 0), V(body, 31, 0));
        var v2 = new Point3d(V(body, 12, 0), V(body, 22, 0), V(body, 32, 0));
        var n = Vector3d.CrossProduct(v1 - v0, v2 - v0);
        var c = new Point3d((v0.X + v1.X + v2.X) / 3.0, (v0.Y + v1.Y + v2.Y) / 3.0, (v0.Z + v1.Z + v2.Z) / 3.0);
        if (n.SquareLength > 1e-18) items.Add(Discontinuity.FromPlane(n, c, DiscontinuityKind.Plane, null, src));
    }
}

// --- GeoJSON -----------------------------------------------------------------

public static class GeoJsonDiscontinuityReader
{
    public static DiscontinuityCollection Read(string path)
    {
        var warnings = new List<string>();
        var items = new List<Discontinuity>();
        string src = Path.GetFileName(path);
        string text = File.ReadAllText(path);
        string crs = SniffCrs(text);

        NetTopologySuite.Features.FeatureCollection fc;
        try { fc = new NetTopologySuite.IO.GeoJsonReader().Read<NetTopologySuite.Features.FeatureCollection>(text); }
        catch (Exception ex)
        {
            return new DiscontinuityCollection(items, path, crs, new[] { "GeoJSON parse failed: " + ex.Message });
        }

        foreach (var feat in fc)
        {
            try
            {
                var attrs = AttrBag(feat.Attributes);
                var g = feat.Geometry;
                if (g == null) continue;
                switch (g)
                {
                    case NetTopologySuite.Geometries.Point pt:
                        var at = P(pt.Coordinate);
                        var d = IngestUtil.FromAttributes(attrs, at, src);
                        if (d != null) { ApplySet(d, attrs); items.Add(d); }
                        else warnings.Add("point feature without dip/dipdir attributes; skipped.");
                        break;
                    case NetTopologySuite.Geometries.LineString ls:
                        AddTrace(ls, attrs, items, src);
                        break;
                    case NetTopologySuite.Geometries.MultiLineString mls:
                        for (int i = 0; i < mls.NumGeometries; i++)
                            AddTrace((NetTopologySuite.Geometries.LineString)mls.GetGeometryN(i), attrs, items, src);
                        break;
                    default:
                        warnings.Add($"geometry type {g.GeometryType} ignored.");
                        break;
                }
            }
            catch (Exception ex) { warnings.Add("feature skipped: " + ex.Message); }
        }
        if (items.Count == 0 && warnings.Count == 0) warnings.Add("GeoJSON had no usable features.");
        return new DiscontinuityCollection(items, path, crs, warnings);
    }

    private static void AddTrace(NetTopologySuite.Geometries.LineString ls,
        IReadOnlyDictionary<string, string> attrs, List<Discontinuity> items, string src)
    {
        var pts = ls.Coordinates.Select(P).ToList();
        if (pts.Count < 2) return;
        var d = Discontinuity.FromTrace(pts, attrs, src);
        ApplySet(d, attrs);
        items.Add(d);
    }

    private static Point3d P(NetTopologySuite.Geometries.Coordinate c) =>
        new Point3d(c.X, c.Y, double.IsNaN(c.Z) ? 0.0 : c.Z);

    private static void ApplySet(Discontinuity d, IReadOnlyDictionary<string, string> attrs)
    {
        foreach (var kv in attrs)
        {
            string k = kv.Key.ToLowerInvariant();
            if ((k is "set" or "setid" or "set_id" or "jointset" or "joint_set") && IngestUtil.TryNum(kv.Value, out double v))
            { d.SetId = (int)Math.Round(v); return; }
        }
    }

    private static Dictionary<string, string> AttrBag(NetTopologySuite.Features.IAttributesTable t)
    {
        var m = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (t == null) return m;
        foreach (var name in t.GetNames())
        {
            var v = t.GetOptionalValue(name);
            if (v != null) m[name] = Convert.ToString(v, CultureInfo.InvariantCulture);
        }
        return m;
    }

    private static string SniffCrs(string text)
    {
        var mm = Regex.Match(text, "\"(?:urn:ogc:def:crs:[^\"]+|EPSG:{1,2}\\d+)\"", RegexOptions.IgnoreCase);
        return mm.Success ? mm.Value.Trim('"') : null;
    }
}

// --- Shapefile ---------------------------------------------------------------

public static class ShapefileDiscontinuityReader
{
    public static DiscontinuityCollection Read(string path)
    {
        var warnings = new List<string>();
        var items = new List<Discontinuity>();
        string src = Path.GetFileName(path);
        string crs = ReadPrj(path);

        NetTopologySuite.Features.Feature[] feats;
        try { feats = NetTopologySuite.IO.Esri.Shapefile.ReadAllFeatures(path); }
        catch (Exception ex)
        {
            return new DiscontinuityCollection(items, path, crs, new[] { "Shapefile read failed: " + ex.Message });
        }

        foreach (var feat in feats)
        {
            try
            {
                var attrs = AttrBag(feat.Attributes);
                var g = feat.Geometry;
                if (g == null) continue;
                switch (g)
                {
                    case NetTopologySuite.Geometries.Point pt:
                        var d = IngestUtil.FromAttributes(attrs, P(pt.Coordinate), src);
                        if (d != null) items.Add(d);
                        else warnings.Add("point without dip/dipdir attributes; skipped.");
                        break;
                    case NetTopologySuite.Geometries.LineString ls:
                        AddTrace(ls, attrs, items, src);
                        break;
                    case NetTopologySuite.Geometries.MultiLineString mls:
                        for (int i = 0; i < mls.NumGeometries; i++)
                            AddTrace((NetTopologySuite.Geometries.LineString)mls.GetGeometryN(i), attrs, items, src);
                        break;
                    default:
                        warnings.Add($"geometry type {g.GeometryType} ignored.");
                        break;
                }
            }
            catch (Exception ex) { warnings.Add("feature skipped: " + ex.Message); }
        }
        if (items.Count == 0 && warnings.Count == 0) warnings.Add("Shapefile had no usable features.");
        return new DiscontinuityCollection(items, path, crs, warnings);
    }

    private static void AddTrace(NetTopologySuite.Geometries.LineString ls,
        IReadOnlyDictionary<string, string> attrs, List<Discontinuity> items, string src)
    {
        var pts = ls.Coordinates.Select(P).ToList();
        if (pts.Count >= 2) items.Add(Discontinuity.FromTrace(pts, attrs, src));
    }

    private static Point3d P(NetTopologySuite.Geometries.Coordinate c) =>
        new Point3d(c.X, c.Y, double.IsNaN(c.Z) ? 0.0 : c.Z);

    private static Dictionary<string, string> AttrBag(NetTopologySuite.Features.IAttributesTable t)
    {
        var m = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (t == null) return m;
        foreach (var name in t.GetNames())
        {
            var v = t.GetOptionalValue(name);
            if (v != null) m[name] = Convert.ToString(v, CultureInfo.InvariantCulture);
        }
        return m;
    }

    private static string ReadPrj(string shpPath)
    {
        try
        {
            string prj = Path.ChangeExtension(shpPath, ".prj");
            return File.Exists(prj) ? File.ReadAllText(prj).Trim() : null;
        }
        catch { return null; }
    }
}
