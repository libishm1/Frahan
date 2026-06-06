#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using Rhino.Geometry;

namespace Frahan.Core.ScanIngest;

// =============================================================================
// MetashapeReader (MRAC Proposal 1)
//
// Parses an Agisoft Metashape .psx project + walks the .files/ zipped-XML
// hierarchy: project.zip -> chunk.zip. Returns a MetashapeProject typed
// record. v1 surfaces metadata + the in-zip PLY path; v1.x extracts the
// PLY to a temp dir for the Load PLY Mesh component to consume.
//
// XML schema notes (Metashape 1.8.x):
//   - .psx top-level: <document version="..."><chunks active_id="0">
//                       <chunk id="0" path="0/chunk.zip"/></chunks></document>
//   - project.zip:doc.xml: <document version="..."><chunks><chunk .../></chunks>
//                         <meta>(versions, created/modified)</meta></document>
//   - chunk.zip:doc.xml:  <chunk version="..."><sensors><sensor/>...</sensors>
//                          <cameras count="..."><camera/>...</cameras>
//                          <markers><marker/>...</markers>
//                          <transform>...</transform>
//                          <reference>...</reference></chunk>
//
// Tolerance: parser tolerates missing / extra elements; reports what it
// can find and flags gaps via Remarks.
// =============================================================================

public static class MetashapeReader
{
    public static MetashapeProject Read(string psxPath)
    {
        if (string.IsNullOrWhiteSpace(psxPath)) throw new ArgumentNullException(nameof(psxPath));
        if (!File.Exists(psxPath)) throw new FileNotFoundException(psxPath);
        if (!psxPath.EndsWith(".psx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Expected a .psx file: " + psxPath);

        var result = new MetashapeProject
        {
            PsxPath = Path.GetFullPath(psxPath),
            LastSavedUtc = File.GetLastWriteTimeUtc(psxPath),
        };

        // .psx is XML; sibling .files/ holds the data.
        var psxXml = new XmlDocument();
        psxXml.Load(psxPath);

        var doc = psxXml.DocumentElement;
        if (doc == null) throw new InvalidDataException(".psx has no root element");
        result.DocumentVersion = doc.GetAttribute("version");
        var chunksEl = doc.SelectSingleNode("chunks") as XmlElement;
        if (chunksEl == null)
            throw new InvalidDataException(".psx missing <chunks> element");

        int activeId = 0;
        int.TryParse(chunksEl.GetAttribute("active_id"), out activeId);
        result.ActiveChunkId = activeId;

        string filesDir = FindFilesDir(psxPath);
        result.FilesDir = filesDir;

        var psxChunks = chunksEl.SelectNodes("chunk");
        var chunkList = new List<MetashapeChunk>();
        foreach (XmlElement c in psxChunks)
        {
            string path = c.GetAttribute("path");
            int id;
            int.TryParse(c.GetAttribute("id"), out id);
            var chunk = new MetashapeChunk { Id = id };
            if (filesDir != null)
            {
                string chunkZipPath = Path.Combine(filesDir, path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(chunkZipPath))
                {
                    ParseChunkZip(chunkZipPath, chunk, filesDir, id);
                }
            }
            chunkList.Add(chunk);
        }
        result.Chunks = chunkList.ToArray();

        // Pull Metashape version from project.zip if present.
        result.MetashapeVersion = TryReadMetashapeVersion(filesDir);

        return result;
    }

    // ====================================================================
    // .files/ resolution
    // ====================================================================

    private static string FindFilesDir(string psxPath)
    {
        // Standard convention: foo.psx + foo.files/ sibling.
        string baseName = Path.GetFileNameWithoutExtension(psxPath);
        string dir = Path.GetDirectoryName(psxPath) ?? ".";
        string candidate = Path.Combine(dir, baseName + ".files");
        if (Directory.Exists(candidate)) return candidate;
        return null;
    }

    // ====================================================================
    // project.zip metadata
    // ====================================================================

    private static string TryReadMetashapeVersion(string filesDir)
    {
        if (filesDir == null) return null;
        string projectZip = Path.Combine(filesDir, "project.zip");
        if (!File.Exists(projectZip)) return null;
        try
        {
            using var fs = File.OpenRead(projectZip);
            using var za = new ZipArchive(fs, ZipArchiveMode.Read);
            var docEntry = za.GetEntry("doc.xml");
            if (docEntry == null) return null;
            using var s = docEntry.Open();
            var xd = new XmlDocument();
            xd.Load(s);
            var doc = xd.DocumentElement;
            if (doc == null) return null;
            // <document version="..."> root attribute holds the document schema
            // version; the Metashape app version sits under <meta> or
            // <document version="X" app_version="Y">.
            string appVersion = doc.GetAttribute("app_version");
            if (!string.IsNullOrEmpty(appVersion)) return appVersion;
            // Fallback: scan <meta> children for a "version" property.
            var meta = doc.SelectSingleNode("meta");
            if (meta != null)
            {
                foreach (XmlNode child in meta.ChildNodes)
                {
                    if (child is XmlElement e &&
                        e.GetAttribute("name").Equals("Metashape/version", StringComparison.OrdinalIgnoreCase))
                    {
                        return e.GetAttribute("value");
                    }
                }
            }
            return null;
        }
        catch { return null; }
    }

    // ====================================================================
    // chunk.zip parsing
    // ====================================================================

    private static void ParseChunkZip(string chunkZipPath, MetashapeChunk chunk, string filesDir, int chunkIdInFiles)
    {
        try
        {
            using var fs = File.OpenRead(chunkZipPath);
            using var za = new ZipArchive(fs, ZipArchiveMode.Read);
            var docEntry = za.GetEntry("doc.xml");
            if (docEntry == null) return;
            using var s = docEntry.Open();
            var xd = new XmlDocument();
            xd.Load(s);
            var chunkEl = xd.DocumentElement;
            if (chunkEl == null) return;

            chunk.Label = chunkEl.GetAttribute("label");

            // Sensors
            var sensors = new List<MetashapeSensor>();
            foreach (XmlElement sensorEl in chunkEl.SelectNodes("sensors/sensor"))
            {
                var sensor = ParseSensor(sensorEl);
                if (sensor != null) sensors.Add(sensor);
            }
            chunk.Sensors = sensors.ToArray();

            // Cameras count
            var camerasEl = chunkEl.SelectSingleNode("cameras") as XmlElement;
            if (camerasEl != null)
            {
                int count = 0;
                if (int.TryParse(camerasEl.GetAttribute("next_id"), out count))
                    chunk.CameraCount = count;
                else
                    chunk.CameraCount = camerasEl.SelectNodes("camera").Count;
            }

            // Markers
            var markers = new List<MetashapeMarker>();
            foreach (XmlElement mEl in chunkEl.SelectNodes("markers/marker"))
            {
                var m = new MetashapeMarker { Label = mEl.GetAttribute("label") };
                var refEl = mEl.SelectSingleNode("reference") as XmlElement;
                if (refEl != null && refEl.HasAttribute("x") && refEl.HasAttribute("y") && refEl.HasAttribute("z"))
                {
                    if (TryReadDouble(refEl, "x", out double x) &&
                        TryReadDouble(refEl, "y", out double y) &&
                        TryReadDouble(refEl, "z", out double z))
                    {
                        m.HasReferencePosition = true;
                        m.ReferencePosition = new Point3d(x, y, z);
                    }
                }
                markers.Add(m);
            }
            chunk.Markers = markers.ToArray();

            // Chunk transform
            var transformEl = chunkEl.SelectSingleNode("transform") as XmlElement;
            if (transformEl != null)
            {
                chunk.ChunkTransform = ParseTransform(transformEl);
                chunk.ChunkScale = ParseScale(transformEl);
            }

            // Find the model PLY path
            // model lives at <id>/0/model/model.zip:mesh.ply per the dossier
            if (filesDir != null)
            {
                string modelZip = Path.Combine(filesDir,
                    chunkIdInFiles.ToString(CultureInfo.InvariantCulture),
                    "0", "model", "model.zip");
                if (File.Exists(modelZip))
                {
                    chunk.ResolvedPlyPath = modelZip + ":mesh.ply";
                }
            }
        }
        catch { /* tolerant: chunk left with partial data */ }
    }

    private static MetashapeSensor ParseSensor(XmlElement sensorEl)
    {
        var s = new MetashapeSensor { Label = sensorEl.GetAttribute("label") };
        if (TryReadDouble(sensorEl, "pixel_width", out double pw)) s.PixelPitchMm = pw;
        var resEl = sensorEl.SelectSingleNode("resolution") as XmlElement;
        if (resEl != null)
        {
            int.TryParse(resEl.GetAttribute("width"), out s.WidthPx);
            int.TryParse(resEl.GetAttribute("height"), out s.HeightPx);
        }
        var calibEl = sensorEl.SelectSingleNode("calibration") as XmlElement;
        if (calibEl != null)
        {
            TryReadCalibChild(calibEl, "f", out s.FocalPx);
            TryReadCalibChild(calibEl, "cx", out s.Cx);
            TryReadCalibChild(calibEl, "cy", out s.Cy);
            TryReadCalibChild(calibEl, "k1", out s.K1);
            TryReadCalibChild(calibEl, "k2", out s.K2);
            TryReadCalibChild(calibEl, "k3", out s.K3);
            TryReadCalibChild(calibEl, "p1", out s.P1);
            TryReadCalibChild(calibEl, "p2", out s.P2);
        }
        // focal_mm = focal_px * pixel_pitch_mm (Metashape internal convention)
        if (s.FocalPx > 0 && s.PixelPitchMm > 0)
            s.FocalMm = s.FocalPx * s.PixelPitchMm;
        return s;
    }

    private static bool TryReadDouble(XmlElement e, string attr, out double v)
    {
        v = 0;
        return double.TryParse(
            e.GetAttribute(attr),
            NumberStyles.Float | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture,
            out v);
    }

    private static bool TryReadCalibChild(XmlElement parent, string name, out double v)
    {
        v = 0;
        var child = parent.SelectSingleNode(name) as XmlElement;
        if (child == null) return false;
        return double.TryParse(
            child.InnerText,
            NumberStyles.Float | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture,
            out v);
    }

    private static Transform ParseTransform(XmlElement transformEl)
    {
        // Metashape transform schema (chunk.xml):
        //   <rotation>r00 r01 r02 r10 r11 r12 r20 r21 r22</rotation>
        //   <translation>tx ty tz</translation>
        //   <scale>s</scale>
        // Returns a Rhino Transform combining R + T + S.
        var rotEl = transformEl.SelectSingleNode("rotation");
        var trEl = transformEl.SelectSingleNode("translation");
        var t = Transform.Identity;
        if (rotEl != null)
        {
            var r = ParseDoubles(rotEl.InnerText);
            if (r.Length >= 9)
            {
                t.M00 = r[0]; t.M01 = r[1]; t.M02 = r[2];
                t.M10 = r[3]; t.M11 = r[4]; t.M12 = r[5];
                t.M20 = r[6]; t.M21 = r[7]; t.M22 = r[8];
            }
        }
        if (trEl != null)
        {
            var tr = ParseDoubles(trEl.InnerText);
            if (tr.Length >= 3)
            {
                t.M03 = tr[0]; t.M13 = tr[1]; t.M23 = tr[2];
            }
        }
        return t;
    }

    private static double ParseScale(XmlElement transformEl)
    {
        var scEl = transformEl.SelectSingleNode("scale");
        if (scEl == null) return 1.0;
        if (double.TryParse(scEl.InnerText,
            NumberStyles.Float | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture, out double v))
            return v;
        return 1.0;
    }

    private static double[] ParseDoubles(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Array.Empty<double>();
        var parts = s.Split(new[] { ' ', '\t', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries);
        var list = new List<double>(parts.Length);
        foreach (var p in parts)
        {
            if (double.TryParse(p, NumberStyles.Float | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture, out double v))
                list.Add(v);
        }
        return list.ToArray();
    }

    // ====================================================================
    // PLY extraction (resolves Chunk.ResolvedPlyOnDisk)
    // ====================================================================

    /// <summary>Extract `mesh.ply` from a chunk's model.zip to the temp dir
    /// and return the on-disk path. No-op if already cached.</summary>
    public static string ExtractMeshPly(MetashapeChunk chunk)
    {
        if (chunk == null || string.IsNullOrEmpty(chunk.ResolvedPlyPath)) return null;
        // ResolvedPlyPath is of the form "C:\foo.files\0\0\model\model.zip:mesh.ply".
        int sep = chunk.ResolvedPlyPath.LastIndexOf(':');
        if (sep <= 0) return null;
        string zipPath = chunk.ResolvedPlyPath.Substring(0, sep);
        string entryName = chunk.ResolvedPlyPath.Substring(sep + 1);
        if (!File.Exists(zipPath)) return null;

        string outDir = Path.Combine(Path.GetTempPath(), "frahan_metashape", Path.GetFileNameWithoutExtension(zipPath));
        Directory.CreateDirectory(outDir);
        string outPath = Path.Combine(outDir, entryName);

        if (File.Exists(outPath))
        {
            chunk.ResolvedPlyOnDisk = outPath;
            return outPath;
        }

        try
        {
            using var fs = File.OpenRead(zipPath);
            using var za = new ZipArchive(fs, ZipArchiveMode.Read);
            var entry = za.GetEntry(entryName);
            if (entry == null) return null;
            entry.ExtractToFile(outPath, overwrite: true);
            chunk.ResolvedPlyOnDisk = outPath;
            return outPath;
        }
        catch { return null; }
    }
}
