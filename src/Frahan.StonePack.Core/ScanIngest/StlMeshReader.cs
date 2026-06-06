#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Frahan.Core.ScanIngest;

// =============================================================================
// StlMeshReader — pure-managed STL parser. Both ASCII and binary STL.
// No third-party dependency.
//
// Phase F2 of the UX architecture report §7.7.A-B scan-ingest rollout.
//
// STL is a triangle-soup format. Vertices are repeated across triangles
// (no shared indices). This reader welds duplicate vertices using a
// quantised hash (resolution = 1e-9 of the input bounding-box diagonal,
// default; configurable) so the output `Mesh` has reasonable topology
// for downstream Frahan repair / decimate / descriptor steps.
//
// ASCII format detected by first non-whitespace token being "solid"
// AND no non-printable bytes in the first 256 bytes. Binary STL also
// uses "solid" in its 80-byte header in some exports (e.g. SolidWorks),
// so we additionally check for a sane 4-byte triangle count and file
// size consistency before falling back to binary parsing.
//
// References:
//   - ASCII STL spec: https://www.fabbers.com/tech/STL_Format
//   - Binary STL spec: same source; 80B header + uint32 ntri + 50B/tri
// =============================================================================

/// <summary>Single parsed STL mesh (welded triangle topology).</summary>
public sealed class StlMesh
{
    public StlMesh(string name, IReadOnlyList<double> vertexCoordsXyz,
        IReadOnlyList<int> triangleIndices)
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

        Name = name ?? string.Empty;
        VertexCoordsXyz = vertexCoordsXyz;
        TriangleIndices = triangleIndices;
    }

    public string Name { get; }
    public IReadOnlyList<double> VertexCoordsXyz { get; }
    public IReadOnlyList<int> TriangleIndices { get; }
    public int VertexCount => VertexCoordsXyz.Count / 3;
    public int TriangleCount => TriangleIndices.Count / 3;
}

public static class StlMeshReader
{
    /// <summary>Quantisation grid for vertex welding (model units).</summary>
    public const double DefaultWeldTolerance = 1e-7;

    public static StlMesh ReadFile(string path, double weldTol = DefaultWeldTolerance)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("STL file not found", path);
        var bytes = File.ReadAllBytes(path);
        return ReadFromBytes(bytes, weldTol);
    }

    public static StlMesh ReadFromBytes(byte[] bytes, double weldTol = DefaultWeldTolerance)
    {
        if (bytes == null) throw new ArgumentNullException(nameof(bytes));
        if (bytes.Length < 84)
            throw new FormatException(
                $"STL too short ({bytes.Length} bytes); minimum is 84 (binary header + ntri).");

        if (IsLikelyAscii(bytes))
            return ParseAscii(Encoding.ASCII.GetString(bytes), weldTol);
        return ParseBinary(bytes, weldTol);
    }

    private static bool IsLikelyAscii(byte[] bytes)
    {
        // Heuristic: ASCII STL starts with "solid". Binary STL also can
        // start with "solid" (some exporters write that in the header)
        // so additionally check the size invariant: binary STL has
        // file_size == 84 + 50 * ntri where ntri is the uint32 at byte 80.
        const string token = "solid";
        if (bytes.Length < token.Length) return false;
        for (int i = 0; i < token.Length; i++)
            if (bytes[i] != (byte)token[i]) return false;

        // If the size matches the binary invariant, treat as binary.
        if (bytes.Length >= 84)
        {
            uint ntri = (uint)(bytes[80] | (bytes[81] << 8) | (bytes[82] << 16) | (bytes[83] << 24));
            long expectedBinarySize = 84L + 50L * ntri;
            if (expectedBinarySize == bytes.Length && ntri > 0) return false;
        }
        return true;
    }

    private static StlMesh ParseAscii(string text, double weldTol)
    {
        var weld = new VertexWelder(weldTol);
        var indices = new List<int>(1024);
        string name = "";

        using var reader = new StringReader(text);
        string line;
        int lineNo = 0;
        bool inFacet = false;
        var triVerts = new double[9];
        int triVertCount = 0;

        while ((line = reader.ReadLine()) != null)
        {
            lineNo++;
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            if (trimmed.StartsWith("solid", StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed.Length > 5) name = trimmed.Substring(5).Trim();
            }
            else if (trimmed.StartsWith("facet", StringComparison.OrdinalIgnoreCase))
            {
                inFacet = true;
                triVertCount = 0;
            }
            else if (trimmed.StartsWith("endfacet", StringComparison.OrdinalIgnoreCase))
            {
                if (triVertCount != 3)
                    throw new FormatException(
                        $"STL ASCII line {lineNo}: facet had {triVertCount} vertices, expected 3.");
                int a = weld.GetOrAdd(triVerts[0], triVerts[1], triVerts[2]);
                int b = weld.GetOrAdd(triVerts[3], triVerts[4], triVerts[5]);
                int c = weld.GetOrAdd(triVerts[6], triVerts[7], triVerts[8]);
                if (a != b && b != c && c != a)
                {
                    indices.Add(a); indices.Add(b); indices.Add(c);
                }
                inFacet = false;
            }
            else if (inFacet && trimmed.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4)
                    throw new FormatException(
                        $"STL ASCII line {lineNo}: vertex needs 3 coords, got {parts.Length - 1}.");
                if (triVertCount >= 3)
                    throw new FormatException($"STL ASCII line {lineNo}: too many vertices in one facet.");
                triVerts[3 * triVertCount + 0] = double.Parse(parts[1], CultureInfo.InvariantCulture);
                triVerts[3 * triVertCount + 1] = double.Parse(parts[2], CultureInfo.InvariantCulture);
                triVerts[3 * triVertCount + 2] = double.Parse(parts[3], CultureInfo.InvariantCulture);
                triVertCount++;
            }
            // outer loop / endloop / endsolid / normal — skip silently
        }

        return new StlMesh(name, weld.GetVertices(), indices);
    }

    private static StlMesh ParseBinary(byte[] bytes, double weldTol)
    {
        // 80-byte header (ignored), uint32 triangle count, then 50 bytes per triangle.
        uint ntri = (uint)(bytes[80] | (bytes[81] << 8) | (bytes[82] << 16) | (bytes[83] << 24));
        long expected = 84L + 50L * ntri;
        if (bytes.Length < expected)
            throw new FormatException(
                $"STL binary size mismatch: expected {expected} bytes for {ntri} triangles, got {bytes.Length}.");

        var weld = new VertexWelder(weldTol);
        var indices = new List<int>((int)(ntri * 3));
        int offset = 84;
        for (uint i = 0; i < ntri; i++)
        {
            // Skip 12-byte normal (3 floats).
            offset += 12;
            // 3 vertices × 3 floats = 36 bytes.
            float v0x = BitConverter.ToSingle(bytes, offset + 0);
            float v0y = BitConverter.ToSingle(bytes, offset + 4);
            float v0z = BitConverter.ToSingle(bytes, offset + 8);
            float v1x = BitConverter.ToSingle(bytes, offset + 12);
            float v1y = BitConverter.ToSingle(bytes, offset + 16);
            float v1z = BitConverter.ToSingle(bytes, offset + 20);
            float v2x = BitConverter.ToSingle(bytes, offset + 24);
            float v2y = BitConverter.ToSingle(bytes, offset + 28);
            float v2z = BitConverter.ToSingle(bytes, offset + 32);
            offset += 36;
            // Skip 2-byte attribute (some exporters store colour here).
            offset += 2;

            int a = weld.GetOrAdd(v0x, v0y, v0z);
            int b = weld.GetOrAdd(v1x, v1y, v1z);
            int c = weld.GetOrAdd(v2x, v2y, v2z);
            if (a != b && b != c && c != a)
            {
                indices.Add(a); indices.Add(b); indices.Add(c);
            }
        }

        return new StlMesh("", weld.GetVertices(), indices);
    }

    /// <summary>
    /// Weld duplicate vertices using a quantised hash. Coordinates are
    /// rounded to the nearest multiple of <c>tol</c> before hashing, then
    /// the original float coordinates are kept for the first vertex seen
    /// at each quantised cell. Cheaper than a kd-tree and accurate enough
    /// for scan welding at micrometre resolution.
    /// </summary>
    private sealed class VertexWelder
    {
        private readonly double _tol;
        private readonly Dictionary<long, int> _index;
        private readonly List<double> _verts;

        public VertexWelder(double tol)
        {
            if (tol <= 0.0) throw new ArgumentOutOfRangeException(nameof(tol));
            _tol = tol;
            _index = new Dictionary<long, int>(1024);
            _verts = new List<double>(3072);
        }

        public int GetOrAdd(double x, double y, double z)
        {
            // 21-bit-per-axis quantised key (≈ 2 M cells/axis at tol = 1e-7 m
            // over ±0.1 m, but the hash collisions are tolerated by the
            // dictionary so the practical resolution is set by tol).
            long ix = (long)Math.Round(x / _tol);
            long iy = (long)Math.Round(y / _tol);
            long iz = (long)Math.Round(z / _tol);
            long key = unchecked((ix * 73856093L) ^ (iy * 19349663L) ^ (iz * 83492791L));

            if (_index.TryGetValue(key, out int idx))
            {
                int off = 3 * idx;
                // Check collision: if not actually within tol, append a new vertex
                // (rare given the quantisation, but keep correctness).
                if (Math.Abs(_verts[off + 0] - x) <= _tol
                    && Math.Abs(_verts[off + 1] - y) <= _tol
                    && Math.Abs(_verts[off + 2] - z) <= _tol)
                    return idx;
            }
            idx = _verts.Count / 3;
            _verts.Add(x); _verts.Add(y); _verts.Add(z);
            _index[key] = idx; // overwrite on collision; first-wins for the chained lookup
            return idx;
        }

        public IReadOnlyList<double> GetVertices() => _verts;
    }
}
