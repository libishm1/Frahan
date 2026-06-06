#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Frahan.Core.ScanIngest;

/// <summary>
/// Chunked reader for a binary_little_endian, points-only PLY (the format the
/// E57 worker writes: vertex element with float/double x/y/z, extra scalar
/// columns tolerated and skipped). Streams the vertex block in fixed-size
/// point chunks so the GH layer can build ONE RhinoCommon PointCloud
/// incrementally (AddRange per chunk) instead of holding a giant point list.
/// Pure-managed; no Rhino types here.
/// </summary>
public static class PlyCloudReader
{
    /// <summary>
    /// Stream the vertex x/y/z of a binary_le PLY in chunks of at most
    /// <paramref name="chunkPoints"/>. <paramref name="onChunk"/> receives a
    /// flat xyz buffer (length 3*count) and the point count in that chunk; do
    /// not retain the buffer past the callback. Returns the total point count.
    /// </summary>
    public static long ReadFloatXyzChunks(
        string path, int chunkPoints,
        Action<float[], int> onChunk)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("PLY not found", path);
        if (chunkPoints < 1) chunkPoints = 1_000_000;
        if (onChunk == null) throw new ArgumentNullException(nameof(onChunk));

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 1 << 20, FileOptions.SequentialScan))
        {
            ParseHeader(fs, out long vertexCount, out int stride,
                        out int xOff, out int yOff, out int zOff,
                        out bool xD, out bool yD, out bool zD);

            // Bound the read buffer to ~48 MB regardless of the requested chunk.
            const long maxBytes = 48L * 1024 * 1024;
            if ((long)stride * chunkPoints > maxBytes)
                chunkPoints = (int)Math.Max(1, maxBytes / stride);
            var raw = new byte[stride * chunkPoints];
            var xyz = new float[3 * chunkPoints];
            long remaining = vertexCount;
            while (remaining > 0)
            {
                int thisChunk = (int)Math.Min(chunkPoints, remaining);
                int want = thisChunk * stride;
                int got = ReadFully(fs, raw, want);
                if (got < want)
                {
                    thisChunk = got / stride;
                    if (thisChunk == 0) break;
                }
                for (int i = 0; i < thisChunk; i++)
                {
                    int b = i * stride;
                    xyz[3 * i + 0] = ReadComp(raw, b + xOff, xD);
                    xyz[3 * i + 1] = ReadComp(raw, b + yOff, yD);
                    xyz[3 * i + 2] = ReadComp(raw, b + zOff, zD);
                }
                onChunk(xyz, thisChunk);
                remaining -= thisChunk;
            }
            return vertexCount - Math.Max(0, remaining);
        }
    }

    private static float ReadComp(byte[] buf, int off, bool isDouble)
        => isDouble ? (float)BitConverter.ToDouble(buf, off) : BitConverter.ToSingle(buf, off);

    private static int ReadFully(Stream s, byte[] buf, int want)
    {
        int total = 0;
        while (total < want)
        {
            int n = s.Read(buf, total, want - total);
            if (n <= 0) break;
            total += n;
        }
        return total;
    }

    private static void ParseHeader(
        Stream fs, out long vertexCount, out int stride,
        out int xOff, out int yOff, out int zOff,
        out bool xDouble, out bool yDouble, out bool zDouble)
    {
        string first = ReadLine(fs);
        if (first == null || first.Trim() != "ply")
            throw new FormatException("PLY: missing 'ply' magic");

        bool le = false;
        string curElement = null;
        long vCount = 0;
        var props = new List<(string name, int size, bool isDouble)>();
        bool inVertex = false;

        string ln;
        while ((ln = ReadLine(fs)) != null)
        {
            string l = ln.Trim();
            if (l.Length == 0) continue;
            if (l == "end_header") break;
            if (l.StartsWith("comment", StringComparison.Ordinal)) continue;
            if (l.StartsWith("obj_info", StringComparison.Ordinal)) continue;
            if (l.StartsWith("format", StringComparison.Ordinal))
            {
                if (l.IndexOf("binary_little_endian", StringComparison.Ordinal) < 0)
                    throw new FormatException("PlyCloudReader only supports binary_little_endian: " + l);
                le = true;
                continue;
            }
            if (l.StartsWith("element", StringComparison.Ordinal))
            {
                var p = l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                curElement = p.Length > 1 ? p[1] : null;
                inVertex = curElement == "vertex";
                if (inVertex && p.Length > 2)
                    vCount = long.Parse(p[2], CultureInfo.InvariantCulture);
                continue;
            }
            if (l.StartsWith("property", StringComparison.Ordinal) && inVertex)
            {
                var p = l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 2 && p[1] == "list")
                    throw new FormatException("PlyCloudReader: list property in vertex element not supported");
                if (p.Length < 3) throw new FormatException("PLY: malformed property: " + l);
                (int size, bool isD) = ScalarInfo(p[1]);
                props.Add((p[2], size, isD));
            }
        }
        if (!le) throw new FormatException("PLY: missing binary_little_endian format directive");

        stride = 0;
        xOff = yOff = zOff = -1;
        xDouble = yDouble = zDouble = false;
        foreach (var pr in props)
        {
            if (pr.name == "x") { xOff = stride; xDouble = pr.isDouble; }
            else if (pr.name == "y") { yOff = stride; yDouble = pr.isDouble; }
            else if (pr.name == "z") { zOff = stride; zDouble = pr.isDouble; }
            stride += pr.size;
        }
        if (xOff < 0 || yOff < 0 || zOff < 0)
            throw new FormatException("PLY: vertex element missing x / y / z properties");
        vertexCount = vCount;
    }

    private static (int size, bool isDouble) ScalarInfo(string t)
    {
        switch (t)
        {
            case "char": case "int8": case "uchar": case "uint8": return (1, false);
            case "short": case "int16": case "ushort": case "uint16": return (2, false);
            case "int": case "int32": case "uint": case "uint32": return (4, false);
            case "float": case "float32": return (4, false);
            case "double": case "float64": return (8, true);
            default: throw new FormatException("PLY: unsupported scalar type '" + t + "'");
        }
    }

    private static string ReadLine(Stream s)
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
}
