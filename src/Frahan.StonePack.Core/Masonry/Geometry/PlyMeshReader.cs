#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Frahan.Masonry.Geometry;

// =============================================================================
// PlyMeshReader — pure-managed PLY parser. No third-party dependency.
//
// Supports:
//   format ascii 1.0
//   format binary_little_endian 1.0
//   format binary_big_endian 1.0
//
// Recognised elements:
//   vertex     — must declare x, y, z (any scalar numeric type). Optional
//                red, green, blue (any uchar / uint8).
//   face       — must declare a list property (typically uchar int
//                vertex_indices or vertex_index). Polygons are fan-
//                triangulated; quads / pentagons / etc. are split.
//
// Other elements / properties are SKIPPED — the reader still walks past
// their bytes / tokens correctly.
//
// Not supported:
//   Texture coordinates, normals on faces, edge elements, custom types.
//   These can be added incrementally without touching the parse loop.
//
// Reference: Greg Turk, "The PLY Polygon File Format", 1994.
// http://gamma.cs.unc.edu/POWERPLANT/papers/ply.pdf
// =============================================================================

public sealed class PlyMesh
{
    public PlyMesh(
        IReadOnlyList<double> vertexCoordsXyz,
        IReadOnlyList<int> triangleIndices,
        IReadOnlyList<byte> vertexColorsRgb)
    {
        if (vertexCoordsXyz == null) throw new ArgumentNullException(nameof(vertexCoordsXyz));
        if (triangleIndices == null) throw new ArgumentNullException(nameof(triangleIndices));
        if (vertexCoordsXyz.Count % 3 != 0)
            throw new ArgumentException(
                $"vertexCoordsXyz length must be a multiple of 3, got {vertexCoordsXyz.Count}",
                nameof(vertexCoordsXyz));
        if (triangleIndices.Count % 3 != 0)
            throw new ArgumentException(
                $"triangleIndices length must be a multiple of 3, got {triangleIndices.Count}",
                nameof(triangleIndices));
        if (vertexColorsRgb != null && vertexColorsRgb.Count != 0
            && vertexColorsRgb.Count != vertexCoordsXyz.Count)
            throw new ArgumentException(
                $"vertexColorsRgb length ({vertexColorsRgb.Count}) must equal " +
                $"vertexCoordsXyz length ({vertexCoordsXyz.Count}) or be empty.",
                nameof(vertexColorsRgb));

        VertexCoordsXyz = vertexCoordsXyz;
        TriangleIndices = triangleIndices;
        VertexColorsRgb = vertexColorsRgb ?? Array.Empty<byte>();
    }

    public IReadOnlyList<double> VertexCoordsXyz { get; }
    public IReadOnlyList<int> TriangleIndices { get; }
    public IReadOnlyList<byte> VertexColorsRgb { get; }

    public int VertexCount => VertexCoordsXyz.Count / 3;
    public int TriangleCount => TriangleIndices.Count / 3;
    public bool HasColors => VertexColorsRgb.Count > 0;
}

public static class PlyMeshReader
{
    public static PlyMesh ReadFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("path must be non-blank", nameof(path));
        using (var fs = File.OpenRead(path))
            return Read(fs);
    }

    public static PlyMesh Read(Stream stream)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));

        // ─── Header: read line-by-line until 'end_header' ───────────────────
        var headerLines = new List<string>(32);
        using (var sr = new HeaderLineReader(stream))
        {
            string first = sr.ReadLine();
            if (first == null || first.Trim() != "ply")
                throw new FormatException("PLY: missing 'ply' magic on first line");
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                string t = line.Trim();
                headerLines.Add(t);
                if (t == "end_header") break;
            }
        }

        // ─── Parse header into elements + properties ────────────────────────
        bool isAscii = false;
        bool isBinaryLE = false;
        bool isBinaryBE = false;
        var elements = new List<PlyElement>();
        PlyElement current = null;

        for (int i = 0; i < headerLines.Count; i++)
        {
            string ln = headerLines[i];
            if (ln.Length == 0) continue;
            if (ln == "end_header") break;
            if (ln.StartsWith("comment", StringComparison.Ordinal)) continue;
            if (ln.StartsWith("obj_info", StringComparison.Ordinal)) continue;

            if (ln.StartsWith("format", StringComparison.Ordinal))
            {
                if (ln.IndexOf("ascii", StringComparison.Ordinal) >= 0) isAscii = true;
                else if (ln.IndexOf("binary_little_endian", StringComparison.Ordinal) >= 0) isBinaryLE = true;
                else if (ln.IndexOf("binary_big_endian", StringComparison.Ordinal) >= 0) isBinaryBE = true;
                else
                    throw new FormatException($"PLY: unknown format directive: '{ln}'");
                continue;
            }

            if (ln.StartsWith("element", StringComparison.Ordinal))
            {
                var parts = ln.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                    throw new FormatException($"PLY: malformed element line: '{ln}'");
                if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
                    throw new FormatException($"PLY: invalid element count in '{ln}'");
                current = new PlyElement(parts[1], count);
                elements.Add(current);
                continue;
            }

            if (ln.StartsWith("property", StringComparison.Ordinal))
            {
                if (current == null)
                    throw new FormatException($"PLY: property declared before any element: '{ln}'");
                current.Properties.Add(PlyProperty.Parse(ln));
                continue;
            }

            // Unknown header line — ignore (forwards-compat).
        }
        if (!isAscii && !isBinaryLE && !isBinaryBE)
            throw new FormatException("PLY: missing 'format' directive");

        // ─── Body ───────────────────────────────────────────────────────────
        var verts = new List<double>();
        var colors = new List<byte>();
        var tris = new List<int>();
        bool sawVertex = false;

        if (isAscii)
        {
            using (var br = new AsciiBodyReader(stream))
                ReadBodyAscii(br, elements, verts, colors, tris, ref sawVertex);
        }
        else
        {
            using (var br = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true))
                ReadBodyBinaryLE(br, elements, verts, colors, tris, ref sawVertex, isBinaryBE);
        }
        if (!sawVertex)
            throw new FormatException("PLY: no 'vertex' element found");

        return new PlyMesh(verts, tris, colors);
    }

    // ─── ASCII body ─────────────────────────────────────────────────────────

    private static void ReadBodyAscii(
        AsciiBodyReader br, List<PlyElement> elements,
        List<double> verts, List<byte> colors, List<int> tris,
        ref bool sawVertex)
    {
        for (int e = 0; e < elements.Count; e++)
        {
            var el = elements[e];
            if (el.Name == "vertex")
            {
                sawVertex = true;
                int xIdx = el.IndexOfProperty("x");
                int yIdx = el.IndexOfProperty("y");
                int zIdx = el.IndexOfProperty("z");
                if (xIdx < 0 || yIdx < 0 || zIdx < 0)
                    throw new FormatException("PLY: vertex element missing x / y / z properties");
                int rIdx = el.IndexOfProperty("red");
                int gIdx = el.IndexOfProperty("green");
                int bIdx = el.IndexOfProperty("blue");
                bool hasColor = rIdx >= 0 && gIdx >= 0 && bIdx >= 0;
                int propCount = el.Properties.Count;

                for (int n = 0; n < el.Count; n++)
                {
                    string ln = br.ReadNonEmptyLine()
                        ?? throw new FormatException(
                            $"PLY: unexpected EOF reading vertex {n}/{el.Count}");
                    var toks = ln.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (toks.Length < propCount)
                        throw new FormatException(
                            $"PLY: vertex {n} has {toks.Length} tokens, expected {propCount}");
                    verts.Add(ParseDouble(toks[xIdx]));
                    verts.Add(ParseDouble(toks[yIdx]));
                    verts.Add(ParseDouble(toks[zIdx]));
                    if (hasColor)
                    {
                        colors.Add(ParseByte(toks[rIdx]));
                        colors.Add(ParseByte(toks[gIdx]));
                        colors.Add(ParseByte(toks[bIdx]));
                    }
                }
            }
            else if (el.Name == "face")
            {
                int listIdx = el.IndexOfFirstListProperty();
                if (listIdx < 0)
                    throw new FormatException("PLY: face element has no list property");

                for (int n = 0; n < el.Count; n++)
                {
                    string ln = br.ReadNonEmptyLine()
                        ?? throw new FormatException(
                            $"PLY: unexpected EOF reading face {n}/{el.Count}");
                    var toks = ln.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    int t = 0;
                    // Walk properties in order; only one list property is parsed
                    // for triangulation; others are skipped past.
                    for (int p = 0; p < el.Properties.Count; p++)
                    {
                        var prop = el.Properties[p];
                        if (!prop.IsList)
                        {
                            if (t >= toks.Length) throw new FormatException("PLY: face token underflow");
                            t++;
                            continue;
                        }
                        if (t >= toks.Length) throw new FormatException("PLY: face token underflow");
                        int listN = int.Parse(toks[t++], CultureInfo.InvariantCulture);
                        if (p == listIdx && listN >= 3)
                        {
                            int v0 = int.Parse(toks[t], CultureInfo.InvariantCulture);
                            for (int k = 1; k <= listN - 2; k++)
                            {
                                int v1 = int.Parse(toks[t + k], CultureInfo.InvariantCulture);
                                int v2 = int.Parse(toks[t + k + 1], CultureInfo.InvariantCulture);
                                tris.Add(v0); tris.Add(v1); tris.Add(v2);
                            }
                        }
                        t += listN;
                    }
                }
            }
            else
            {
                // Skip element entirely.
                for (int n = 0; n < el.Count; n++) br.ReadNonEmptyLine();
            }
        }
    }

    // ─── Binary little-endian body ──────────────────────────────────────────

    private static void ReadBodyBinaryLE(
        BinaryReader br, List<PlyElement> elements,
        List<double> verts, List<byte> colors, List<int> tris,
        ref bool sawVertex, bool bigEndian)
    {
        for (int e = 0; e < elements.Count; e++)
        {
            var el = elements[e];
            if (el.Name == "vertex")
            {
                sawVertex = true;
                int xIdx = el.IndexOfProperty("x");
                int yIdx = el.IndexOfProperty("y");
                int zIdx = el.IndexOfProperty("z");
                if (xIdx < 0 || yIdx < 0 || zIdx < 0)
                    throw new FormatException("PLY: vertex element missing x / y / z properties");
                int rIdx = el.IndexOfProperty("red");
                int gIdx = el.IndexOfProperty("green");
                int bIdx = el.IndexOfProperty("blue");
                bool hasColor = rIdx >= 0 && gIdx >= 0 && bIdx >= 0;

                var values = new double[el.Properties.Count];
                for (int n = 0; n < el.Count; n++)
                {
                    for (int p = 0; p < el.Properties.Count; p++)
                    {
                        var prop = el.Properties[p];
                        if (prop.IsList)
                            throw new FormatException(
                                $"PLY: list property in vertex element not supported: '{prop.Name}'");
                        values[p] = ReadScalarLE(br, prop.ScalarType, bigEndian);
                    }
                    verts.Add(values[xIdx]); verts.Add(values[yIdx]); verts.Add(values[zIdx]);
                    if (hasColor)
                    {
                        colors.Add((byte)values[rIdx]);
                        colors.Add((byte)values[gIdx]);
                        colors.Add((byte)values[bIdx]);
                    }
                }
            }
            else if (el.Name == "face")
            {
                int listIdx = el.IndexOfFirstListProperty();
                if (listIdx < 0)
                    throw new FormatException("PLY: face element has no list property");

                for (int n = 0; n < el.Count; n++)
                {
                    for (int p = 0; p < el.Properties.Count; p++)
                    {
                        var prop = el.Properties[p];
                        if (!prop.IsList)
                        {
                            // Skip past one scalar.
                            ReadScalarLE(br, prop.ScalarType, bigEndian);
                            continue;
                        }
                        int listN = (int)ReadScalarLE(br, prop.ListCountType, bigEndian);
                        if (p == listIdx && listN >= 3)
                        {
                            var inds = new int[listN];
                            for (int k = 0; k < listN; k++)
                                inds[k] = (int)ReadScalarLE(br, prop.ScalarType, bigEndian);
                            for (int k = 1; k <= listN - 2; k++)
                            {
                                tris.Add(inds[0]); tris.Add(inds[k]); tris.Add(inds[k + 1]);
                            }
                        }
                        else
                        {
                            for (int k = 0; k < listN; k++)
                                ReadScalarLE(br, prop.ScalarType, bigEndian);
                        }
                    }
                }
            }
            else
            {
                // Skip element. Each entry: walk property list and discard.
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
                        else
                        {
                            ReadScalarLE(br, prop.ScalarType, bigEndian);
                        }
                    }
                }
            }
        }
    }

    private static double ReadScalarLE(BinaryReader br, PlyScalarType t, bool bigEndian)
    {
        switch (t)
        {
            case PlyScalarType.Char:    return br.ReadSByte();
            case PlyScalarType.UChar:   return br.ReadByte();
            case PlyScalarType.Short:   return bigEndian ? BitConverter.ToInt16(RevBytes(br, 2), 0) : br.ReadInt16();
            case PlyScalarType.UShort:  return bigEndian ? BitConverter.ToUInt16(RevBytes(br, 2), 0) : br.ReadUInt16();
            case PlyScalarType.Int:     return bigEndian ? BitConverter.ToInt32(RevBytes(br, 4), 0) : br.ReadInt32();
            case PlyScalarType.UInt:    return bigEndian ? (long)BitConverter.ToUInt32(RevBytes(br, 4), 0) : (long)br.ReadUInt32();
            case PlyScalarType.Float:   return bigEndian ? BitConverter.ToSingle(RevBytes(br, 4), 0) : br.ReadSingle();
            case PlyScalarType.Double:  return bigEndian ? BitConverter.ToDouble(RevBytes(br, 8), 0) : br.ReadDouble();
            default:
                throw new FormatException($"PLY: unsupported scalar type {t}");
        }
    }

    // Read n bytes from a big-endian stream and reverse to host (little-endian)
    // order for BitConverter. Used only on the binary_big_endian path.
    private static byte[] RevBytes(BinaryReader br, int n)
    {
        var b = br.ReadBytes(n);
        if (b.Length != n)
            throw new FormatException("PLY: unexpected EOF reading binary scalar");
        Array.Reverse(b);
        return b;
    }

    private static double ParseDouble(string s) =>
        double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    private static byte ParseByte(string s) =>
        byte.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);

    // ─── Header / property model ────────────────────────────────────────────

    internal enum PlyScalarType { Char, UChar, Short, UShort, Int, UInt, Float, Double }

    internal sealed class PlyElement
    {
        public PlyElement(string name, int count) { Name = name; Count = count; }
        public string Name { get; }
        public int Count { get; }
        public List<PlyProperty> Properties { get; } = new List<PlyProperty>();
        public int IndexOfProperty(string name)
        {
            for (int i = 0; i < Properties.Count; i++)
                if (Properties[i].Name == name) return i;
            return -1;
        }
        public int IndexOfFirstListProperty()
        {
            for (int i = 0; i < Properties.Count; i++)
                if (Properties[i].IsList) return i;
            return -1;
        }
    }

    internal sealed class PlyProperty
    {
        public string Name { get; private set; }
        public bool IsList { get; private set; }
        public PlyScalarType ScalarType { get; private set; }   // element type for lists, scalar type otherwise
        public PlyScalarType ListCountType { get; private set; } // only meaningful when IsList

        public static PlyProperty Parse(string line)
        {
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || parts[0] != "property")
                throw new FormatException($"PLY: malformed property line '{line}'");
            if (parts[1] == "list")
            {
                if (parts.Length < 5)
                    throw new FormatException($"PLY: malformed list property '{line}'");
                return new PlyProperty
                {
                    IsList = true,
                    ListCountType = ParseScalarType(parts[2]),
                    ScalarType = ParseScalarType(parts[3]),
                    Name = parts[4],
                };
            }
            return new PlyProperty
            {
                IsList = false,
                ScalarType = ParseScalarType(parts[1]),
                Name = parts[2],
            };
        }

        private static PlyScalarType ParseScalarType(string t)
        {
            switch (t)
            {
                case "char":   case "int8":   return PlyScalarType.Char;
                case "uchar":  case "uint8":  return PlyScalarType.UChar;
                case "short":  case "int16":  return PlyScalarType.Short;
                case "ushort": case "uint16": return PlyScalarType.UShort;
                case "int":    case "int32":  return PlyScalarType.Int;
                case "uint":   case "uint32": return PlyScalarType.UInt;
                case "float":  case "float32": return PlyScalarType.Float;
                case "double": case "float64": return PlyScalarType.Double;
                default:
                    throw new FormatException($"PLY: unknown scalar type '{t}'");
            }
        }
    }

    // ─── Header reader: byte-level so we leave the stream positioned right
    //     at the start of the body for the binary parser. .NET's StreamReader
    //     buffers ahead, which would mis-align binary reads.
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class HeaderLineReader : IDisposable
    {
        private readonly Stream _s;
        public HeaderLineReader(Stream s) { _s = s; }
        public string ReadLine()
        {
            var sb = new StringBuilder(64);
            int b;
            while ((b = _s.ReadByte()) >= 0)
            {
                if (b == '\n') return sb.ToString();
                if (b == '\r') continue;  // tolerate CRLF
                sb.Append((char)b);
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }
        public void Dispose() { /* leave underlying stream open */ }
    }

    private sealed class AsciiBodyReader : IDisposable
    {
        private readonly Stream _s;
        public AsciiBodyReader(Stream s) { _s = s; }
        public string ReadNonEmptyLine()
        {
            var sb = new StringBuilder(64);
            int b;
            while ((b = _s.ReadByte()) >= 0)
            {
                if (b == '\n')
                {
                    string ln = sb.ToString().Trim();
                    if (ln.Length > 0) return ln;
                    sb.Length = 0;
                    continue;
                }
                if (b == '\r') continue;
                sb.Append((char)b);
            }
            string tail = sb.ToString().Trim();
            return tail.Length > 0 ? tail : null;
        }
        public void Dispose() { }
    }
}
