#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Frahan.Core.ScanIngest;

// =============================================================================
// StreamingCloudReader — gate-free, pure-managed streaming point-cloud loader.
//
// Goal: read a large point-cloud FILE and voxel-downsample DURING the read so
// the full cloud never materialises. A 28M-point file collapses straight into
// a voxel hash-grid as it streams, so peak memory is bounded by the number of
// OCCUPIED voxels, not the input point count.
//
// Supported inputs:
//   PLY  format binary_little_endian 1.0  — points-only AND mesh-vertex clouds
//   PLY  format ascii 1.0                 — points-only AND mesh-vertex clouds
//   XYZ / PTS  plain ASCII "x y z [...]"  — one point per line, extra columns
//                                           (intensity / rgb / normals) ignored
//
// The hash-grid is identical to VoxelDownsampleComponent.ManagedVoxelDownsample
// (key = floor(x/voxel), floor(y/voxel), floor(z/voxel) -> running centroid
// sum + count). Forward-only stream; we never build a full double[] of all
// input points.
//
// PLY header / scalar parsing follows the same approach as PlyMeshReader
// (byte-level header reader so the binary body stays aligned). This reader does
// NOT triangulate or keep faces — it only harvests vertex x / y / z.
// =============================================================================

/// <summary>
/// Result of a streaming downsample: one centroid per occupied voxel, plus the
/// counts and bounding box needed by the GH layer to report and draw.
/// </summary>
public sealed class StreamingCloudResult
{
    public StreamingCloudResult(
        double[] downsampledXyz,
        long inputPointCount,
        int occupiedVoxelCount,
        double minX, double minY, double minZ,
        double maxX, double maxY, double maxZ)
    {
        DownsampledXyz = downsampledXyz ?? Array.Empty<double>();
        InputPointCount = inputPointCount;
        OccupiedVoxelCount = occupiedVoxelCount;
        MinX = minX; MinY = minY; MinZ = minZ;
        MaxX = maxX; MaxY = maxY; MaxZ = maxZ;
    }

    /// <summary>Flat xyz of the per-voxel centroids. Length = 3 * voxel count.</summary>
    public double[] DownsampledXyz { get; }

    /// <summary>Total points seen in the input stream.</summary>
    public long InputPointCount { get; }

    /// <summary>Number of occupied voxels == number of output centroids.</summary>
    public int OccupiedVoxelCount { get; }

    public double MinX { get; }
    public double MinY { get; }
    public double MinZ { get; }
    public double MaxX { get; }
    public double MaxY { get; }
    public double MaxZ { get; }

    public bool HasBounds => InputPointCount > 0;
}

public enum CloudFormat
{
    /// <summary>Auto-detect from extension, then magic bytes.</summary>
    Auto = 0,
    Ply = 1,
    /// <summary>Plain ASCII "x y z [...]" — .xyz / .pts / .txt / .asc.</summary>
    Xyz = 2,
}

public static class StreamingCloudReader
{
    /// <summary>
    /// Stream a point-cloud file and voxel-downsample on the fly. When
    /// <paramref name="voxelSize"/> is &lt;= 0 the points are kept verbatim
    /// (no downsample); in that case peak memory IS proportional to the
    /// retained point count, so the caller should warn for very large files.
    /// </summary>
    public static StreamingCloudResult ReadAndDownsample(
        string path, double voxelSize, CloudFormat format = CloudFormat.Auto)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("Cloud file not found", path);

        var resolved = format == CloudFormat.Auto ? Detect(path) : format;
        var sink = new VoxelGridSink(voxelSize);

        switch (resolved)
        {
            case CloudFormat.Ply:
                StreamPly(path, sink);
                break;
            case CloudFormat.Xyz:
                StreamXyz(path, sink);
                break;
            default:
                throw new NotSupportedException($"Unsupported cloud format: {resolved}");
        }

        return sink.ToResult();
    }

    /// <summary>
    /// Resolve a format by extension first, then a magic-byte sniff for unknown
    /// extensions. ".ply" -> PLY; ".xyz"/".pts"/".txt"/".asc" -> XYZ; otherwise
    /// PLY if the file starts with the "ply" magic, else XYZ.
    /// </summary>
    public static CloudFormat Detect(string path)
    {
        string ext = (Path.GetExtension(path) ?? string.Empty).ToLowerInvariant();
        switch (ext)
        {
            case ".ply": return CloudFormat.Ply;
            case ".xyz":
            case ".pts":
            case ".txt":
            case ".asc": return CloudFormat.Xyz;
            case ".e57":
                throw new NotSupportedException(
                    "E57 is not read natively yet. Convert it to PLY or LAS first " +
                    "(PDAL: 'pdal translate in.e57 out.ply', or CloudCompare), then load. " +
                    "Native libE57Format (BSL-1.0) binding is a planned future shim.");
        }

        // Sniff: PLY starts with "ply\n" or "ply\r". Anything else -> XYZ.
        var buf = new byte[4];
        using (var fs = File.OpenRead(path))
        {
            int got = fs.Read(buf, 0, 4);
            if (got >= 4 && buf[0] == 'p' && buf[1] == 'l' && buf[2] == 'y'
                && (buf[3] == '\n' || buf[3] == '\r'))
                return CloudFormat.Ply;
        }
        return CloudFormat.Xyz;
    }

    // ─── XYZ / PTS streaming ──────────────────────────────────────────────────
    // One point per line: "x y z [intensity] [r g b] ...". Extra columns are
    // ignored. Lines that do not start with a parseable number (headers, the
    // optional PTS leading count line, comments) are skipped.

    private static void StreamXyz(string path, VoxelGridSink sink)
    {
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 1 << 16, FileOptions.SequentialScan))
        using (var sr = new StreamReader(fs, Encoding.ASCII, false, 1 << 16))
        {
            char[] seps = { ' ', '\t', ',', ';' };
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                var toks = line.Split(seps, StringSplitOptions.RemoveEmptyEntries);
                if (toks.Length < 3) continue;
                if (!TryParseDouble(toks[0], out double x)) continue;
                if (!TryParseDouble(toks[1], out double y)) continue;
                if (!TryParseDouble(toks[2], out double z)) continue;
                sink.Add(x, y, z);
            }
        }
    }

    // ─── PLY streaming (vertex x/y/z only) ────────────────────────────────────

    private static void StreamPly(string path, VoxelGridSink sink)
    {
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 1 << 16, FileOptions.SequentialScan))
        {
            // Header: byte-level read so the binary body stays aligned.
            var headerLines = new List<string>(32);
            string first = ReadHeaderLine(fs);
            if (first == null || first.Trim() != "ply")
                throw new FormatException("PLY: missing 'ply' magic on first line");
            string ln;
            while ((ln = ReadHeaderLine(fs)) != null)
            {
                string t = ln.Trim();
                headerLines.Add(t);
                if (t == "end_header") break;
            }

            bool isAscii = false, isBinaryLE = false, isBinaryBE = false;
            var elements = new List<PlyEl>();
            PlyEl cur = null;
            for (int i = 0; i < headerLines.Count; i++)
            {
                string l = headerLines[i];
                if (l.Length == 0 || l == "end_header") { if (l == "end_header") break; continue; }
                if (l.StartsWith("comment", StringComparison.Ordinal)) continue;
                if (l.StartsWith("obj_info", StringComparison.Ordinal)) continue;
                if (l.StartsWith("format", StringComparison.Ordinal))
                {
                    if (l.IndexOf("ascii", StringComparison.Ordinal) >= 0) isAscii = true;
                    else if (l.IndexOf("binary_little_endian", StringComparison.Ordinal) >= 0) isBinaryLE = true;
                    else if (l.IndexOf("binary_big_endian", StringComparison.Ordinal) >= 0) isBinaryBE = true;
                    else throw new FormatException($"PLY: unknown format directive: '{l}'");
                    continue;
                }
                if (l.StartsWith("element", StringComparison.Ordinal))
                {
                    var parts = l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 3) throw new FormatException($"PLY: malformed element line: '{l}'");
                    if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
                        throw new FormatException($"PLY: invalid element count in '{l}'");
                    cur = new PlyEl(parts[1], count);
                    elements.Add(cur);
                    continue;
                }
                if (l.StartsWith("property", StringComparison.Ordinal))
                {
                    if (cur == null) throw new FormatException($"PLY: property before element: '{l}'");
                    cur.Properties.Add(PlyProp.Parse(l));
                    continue;
                }
            }
            if (!isAscii && !isBinaryLE && !isBinaryBE)
                throw new FormatException("PLY: missing 'format' directive");

            bool sawVertex = false;
            if (isAscii) StreamPlyAscii(fs, elements, sink, ref sawVertex);
            else StreamPlyBinaryLE(fs, elements, sink, ref sawVertex, isBinaryBE);

            if (!sawVertex) throw new FormatException("PLY: no 'vertex' element found");
        }
    }

    private static void StreamPlyAscii(Stream s, List<PlyEl> elements, VoxelGridSink sink, ref bool sawVertex)
    {
        for (int e = 0; e < elements.Count; e++)
        {
            var el = elements[e];
            if (el.Name == "vertex")
            {
                sawVertex = true;
                int xIdx = el.IndexOf("x"), yIdx = el.IndexOf("y"), zIdx = el.IndexOf("z");
                if (xIdx < 0 || yIdx < 0 || zIdx < 0)
                    throw new FormatException("PLY: vertex element missing x / y / z properties");
                int propCount = el.Properties.Count;
                for (int n = 0; n < el.Count; n++)
                {
                    string row = ReadBodyLine(s)
                        ?? throw new FormatException($"PLY: unexpected EOF reading vertex {n}/{el.Count}");
                    var toks = row.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (toks.Length < propCount)
                        throw new FormatException($"PLY: vertex {n} has {toks.Length} tokens, expected {propCount}");
                    sink.Add(ParseDouble(toks[xIdx]), ParseDouble(toks[yIdx]), ParseDouble(toks[zIdx]));
                }
            }
            else
            {
                // Skip element entirely (faces / edges / custom).
                for (int n = 0; n < el.Count; n++) ReadBodyLine(s);
            }
        }
    }

    private static void StreamPlyBinaryLE(Stream s, List<PlyEl> elements, VoxelGridSink sink, ref bool sawVertex, bool bigEndian)
    {
        using (var br = new BinaryReader(s, Encoding.ASCII, leaveOpen: true))
        {
            for (int e = 0; e < elements.Count; e++)
            {
                var el = elements[e];
                if (el.Name == "vertex")
                {
                    sawVertex = true;
                    int xIdx = el.IndexOf("x"), yIdx = el.IndexOf("y"), zIdx = el.IndexOf("z");
                    if (xIdx < 0 || yIdx < 0 || zIdx < 0)
                        throw new FormatException("PLY: vertex element missing x / y / z properties");
                    var values = new double[el.Properties.Count];
                    for (int n = 0; n < el.Count; n++)
                    {
                        for (int p = 0; p < el.Properties.Count; p++)
                        {
                            var prop = el.Properties[p];
                            if (prop.IsList)
                                throw new FormatException($"PLY: list property in vertex element not supported: '{prop.Name}'");
                            values[p] = ReadScalarLE(br, prop.ScalarType, bigEndian);
                        }
                        sink.Add(values[xIdx], values[yIdx], values[zIdx]);
                    }
                }
                else
                {
                    // Skip element. Walk each property and discard the bytes.
                    for (int n = 0; n < el.Count; n++)
                    {
                        for (int p = 0; p < el.Properties.Count; p++)
                        {
                            var prop = el.Properties[p];
                            if (prop.IsList)
                            {
                                int listN = (int)ReadScalarLE(br, prop.ListCountType, bigEndian);
                                for (int k = 0; k < listN; k++) ReadScalarLE(br, prop.ScalarType, bigEndian);
                            }
                            else ReadScalarLE(br, prop.ScalarType, bigEndian);
                        }
                    }
                }
            }
        }
    }

    private static double ReadScalarLE(BinaryReader br, PlyScalar t, bool bigEndian)
    {
        switch (t)
        {
            case PlyScalar.Char:   return br.ReadSByte();
            case PlyScalar.UChar:  return br.ReadByte();
            case PlyScalar.Short:  return bigEndian ? BitConverter.ToInt16(Rev(br, 2), 0) : br.ReadInt16();
            case PlyScalar.UShort: return bigEndian ? BitConverter.ToUInt16(Rev(br, 2), 0) : br.ReadUInt16();
            case PlyScalar.Int:    return bigEndian ? BitConverter.ToInt32(Rev(br, 4), 0) : br.ReadInt32();
            case PlyScalar.UInt:   return bigEndian ? (long)BitConverter.ToUInt32(Rev(br, 4), 0) : (long)br.ReadUInt32();
            case PlyScalar.Float:  return bigEndian ? BitConverter.ToSingle(Rev(br, 4), 0) : br.ReadSingle();
            case PlyScalar.Double: return bigEndian ? BitConverter.ToDouble(Rev(br, 8), 0) : br.ReadDouble();
            default: throw new FormatException($"PLY: unsupported scalar type {t}");
        }
    }

    // Read n bytes from a big-endian stream and reverse to host order for BitConverter.
    private static byte[] Rev(BinaryReader br, int n)
    {
        var b = br.ReadBytes(n);
        if (b.Length != n) throw new FormatException("PLY: unexpected EOF in binary scalar");
        Array.Reverse(b);
        return b;
    }

    // ─── line / number helpers ────────────────────────────────────────────────

    private static string ReadHeaderLine(Stream s)
    {
        var sb = new StringBuilder(64);
        int b;
        while ((b = s.ReadByte()) >= 0)
        {
            if (b == '\n') return sb.ToString();
            if (b == '\r') continue;
            sb.Append((char)b);
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static string ReadBodyLine(Stream s)
    {
        var sb = new StringBuilder(64);
        int b;
        while ((b = s.ReadByte()) >= 0)
        {
            if (b == '\n')
            {
                string l = sb.ToString().Trim();
                if (l.Length > 0) return l;
                sb.Length = 0;
                continue;
            }
            if (b == '\r') continue;
            sb.Append((char)b);
        }
        string tail = sb.ToString().Trim();
        return tail.Length > 0 ? tail : null;
    }

    private static double ParseDouble(string s) =>
        double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static bool TryParseDouble(string s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    // ─── header model (private, mirrors PlyMeshReader) ────────────────────────

    internal enum PlyScalar { Char, UChar, Short, UShort, Int, UInt, Float, Double }

    internal sealed class PlyEl
    {
        public PlyEl(string name, int count) { Name = name; Count = count; }
        public string Name { get; }
        public int Count { get; }
        public List<PlyProp> Properties { get; } = new List<PlyProp>();
        public int IndexOf(string name)
        {
            for (int i = 0; i < Properties.Count; i++)
                if (Properties[i].Name == name) return i;
            return -1;
        }
    }

    internal sealed class PlyProp
    {
        public string Name { get; private set; }
        public bool IsList { get; private set; }
        public PlyScalar ScalarType { get; private set; }
        public PlyScalar ListCountType { get; private set; }

        public static PlyProp Parse(string line)
        {
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || parts[0] != "property")
                throw new FormatException($"PLY: malformed property line '{line}'");
            if (parts[1] == "list")
            {
                if (parts.Length < 5) throw new FormatException($"PLY: malformed list property '{line}'");
                return new PlyProp
                {
                    IsList = true,
                    ListCountType = ParseScalar(parts[2]),
                    ScalarType = ParseScalar(parts[3]),
                    Name = parts[4],
                };
            }
            return new PlyProp { IsList = false, ScalarType = ParseScalar(parts[1]), Name = parts[2] };
        }

        private static PlyScalar ParseScalar(string t)
        {
            switch (t)
            {
                case "char":   case "int8":    return PlyScalar.Char;
                case "uchar":  case "uint8":   return PlyScalar.UChar;
                case "short":  case "int16":   return PlyScalar.Short;
                case "ushort": case "uint16":  return PlyScalar.UShort;
                case "int":    case "int32":   return PlyScalar.Int;
                case "uint":   case "uint32":  return PlyScalar.UInt;
                case "float":  case "float32": return PlyScalar.Float;
                case "double": case "float64": return PlyScalar.Double;
                default: throw new FormatException($"PLY: unknown scalar type '{t}'");
            }
        }
    }
}

// =============================================================================
// VoxelGridSink — the bounded-memory accumulator. Every streamed point is
// folded into a voxel cell (running centroid sum + count) so memory is
// O(occupied voxels). When voxelSize <= 0, points are kept verbatim in a flat
// list (no spatial reduction); used only when the caller opts out of
// downsampling. Shared by StreamingCloudReader and LazCloudReader.
// =============================================================================
internal sealed class VoxelGridSink
{
    private readonly double _voxel;
    private readonly bool _downsample;

    private readonly Dictionary<(long, long, long), Cell> _cells;
    private readonly List<double> _passthrough; // used only when _downsample == false

    private long _inputCount;
    private double _minX = double.PositiveInfinity, _minY = double.PositiveInfinity, _minZ = double.PositiveInfinity;
    private double _maxX = double.NegativeInfinity, _maxY = double.NegativeInfinity, _maxZ = double.NegativeInfinity;

    public VoxelGridSink(double voxelSize)
    {
        _voxel = voxelSize;
        _downsample = voxelSize > 0.0;
        _cells = _downsample ? new Dictionary<(long, long, long), Cell>() : null;
        _passthrough = _downsample ? null : new List<double>();
    }

    public void Add(double x, double y, double z)
    {
        _inputCount++;
        if (x < _minX) _minX = x; if (x > _maxX) _maxX = x;
        if (y < _minY) _minY = y; if (y > _maxY) _maxY = y;
        if (z < _minZ) _minZ = z; if (z > _maxZ) _maxZ = z;

        if (_downsample)
        {
            var key = ((long)Math.Floor(x / _voxel),
                       (long)Math.Floor(y / _voxel),
                       (long)Math.Floor(z / _voxel));
            if (_cells.TryGetValue(key, out var c))
            {
                c.Sx += x; c.Sy += y; c.Sz += z; c.C++;
                _cells[key] = c;
            }
            else
            {
                _cells[key] = new Cell { Sx = x, Sy = y, Sz = z, C = 1 };
            }
        }
        else
        {
            _passthrough.Add(x); _passthrough.Add(y); _passthrough.Add(z);
        }
    }

    public StreamingCloudResult ToResult()
    {
        double[] outFlat;
        int voxelCount;
        if (_downsample)
        {
            outFlat = new double[3 * _cells.Count];
            int j = 0;
            foreach (var kv in _cells)
            {
                double inv = 1.0 / kv.Value.C;
                outFlat[3 * j + 0] = kv.Value.Sx * inv;
                outFlat[3 * j + 1] = kv.Value.Sy * inv;
                outFlat[3 * j + 2] = kv.Value.Sz * inv;
                j++;
            }
            voxelCount = _cells.Count;
        }
        else
        {
            outFlat = _passthrough.ToArray();
            voxelCount = outFlat.Length / 3;
        }

        if (_inputCount == 0)
            return new StreamingCloudResult(outFlat, 0, voxelCount, 0, 0, 0, 0, 0, 0);

        return new StreamingCloudResult(
            outFlat, _inputCount, voxelCount,
            _minX, _minY, _minZ, _maxX, _maxY, _maxZ);
    }

    private struct Cell
    {
        public double Sx, Sy, Sz;
        public long C;
    }
}
