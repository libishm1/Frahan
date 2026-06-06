#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using Frahan.Masonry.Geometry;

namespace Frahan.Core.ScanIngest;

// =============================================================================
// MultiFormatMeshReader — Phase F1+F2 unified entry point. Dispatches a
// file path to the right pure-managed parser based on extension OR
// magic-byte sniffing for ambiguous cases.
//
// Returns a normalised ScanMesh list (Name + flat vertex/index arrays)
// so downstream Frahan code can build Rhino Meshes without caring which
// format the user provided.
// =============================================================================

public enum ScanFormat
{
    /// <summary>Auto-detect from extension, then magic bytes.</summary>
    Auto = 0,
    Ply = 1,
    Obj = 2,
    Stl = 3,
    /// <summary>VRML 2.0 / VRML97 .wrl IndexedFaceSet (Artec Studio export etc.).</summary>
    Vrml = 4,
}

/// <summary>Format-agnostic mesh wrapper used across the scan-ingest path.</summary>
public sealed class ScanMesh
{
    public ScanMesh(string name, IReadOnlyList<double> vertexCoordsXyz,
        IReadOnlyList<int> triangleIndices, IReadOnlyList<byte> vertexColorsRgb)
    {
        if (vertexCoordsXyz == null) throw new ArgumentNullException(nameof(vertexCoordsXyz));
        if (triangleIndices == null) throw new ArgumentNullException(nameof(triangleIndices));
        if (vertexCoordsXyz.Count % 3 != 0)
            throw new ArgumentException("vertex coords length must be a multiple of 3.",
                nameof(vertexCoordsXyz));
        if (triangleIndices.Count % 3 != 0)
            throw new ArgumentException("triangle index length must be a multiple of 3.",
                nameof(triangleIndices));

        Name = name ?? string.Empty;
        VertexCoordsXyz = vertexCoordsXyz;
        TriangleIndices = triangleIndices;
        VertexColorsRgb = vertexColorsRgb; // may be null when format does not carry colour
    }

    public string Name { get; }
    public IReadOnlyList<double> VertexCoordsXyz { get; }
    public IReadOnlyList<int> TriangleIndices { get; }
    public IReadOnlyList<byte> VertexColorsRgb { get; }
    public int VertexCount => VertexCoordsXyz.Count / 3;
    public int TriangleCount => TriangleIndices.Count / 3;
    public bool HasColors => VertexColorsRgb != null && VertexColorsRgb.Count >= 3 * VertexCount;
}

public static class MultiFormatMeshReader
{
    /// <summary>
    /// Read a scan file, auto-detecting the format unless one is forced.
    /// Returns one or more ScanMesh entries: PLY always returns one;
    /// OBJ may return many (one per group / object); STL returns one.
    /// </summary>
    public static IReadOnlyList<ScanMesh> ReadFile(string path, ScanFormat format = ScanFormat.Auto)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("Scan file not found", path);

        var resolved = format == ScanFormat.Auto ? Detect(path) : format;
        return resolved switch
        {
            ScanFormat.Ply => ReadPly(path),
            ScanFormat.Obj => ReadObj(path),
            ScanFormat.Stl => ReadStl(path),
            ScanFormat.Vrml => VrmlMeshReader.ReadFile(path),
            _ => throw new NotSupportedException($"Unsupported scan format: {resolved}"),
        };
    }

    /// <summary>
    /// Resolve a format by file extension first, then fall back to a
    /// magic-byte sniff for files with unknown / generic extensions
    /// (e.g. ".mesh", or no extension at all).
    /// </summary>
    public static ScanFormat Detect(string path)
    {
        string ext = (Path.GetExtension(path) ?? string.Empty).ToLowerInvariant();
        switch (ext)
        {
            case ".ply": return ScanFormat.Ply;
            case ".obj": return ScanFormat.Obj;
            case ".stl": return ScanFormat.Stl;
            case ".wrl":
            case ".vrml": return ScanFormat.Vrml;
            case ".e57":
                throw new NotSupportedException(
                    "E57 is not read natively yet. Convert it to PLY or LAS first " +
                    "(PDAL: 'pdal translate in.e57 out.ply', or CloudCompare), then load. " +
                    "A native libE57Format (BSL-1.0) binding is a planned future shim.");
            case ".aop":
            case ".asproj":
                throw new NotSupportedException(
                    "Artec project (.aop / .asproj) is a proprietary format. Export to " +
                    "WRL / OBJ / PLY / STL from Artec Studio, then load (all four read natively).");
        }
        return SniffMagic(path);
    }

    private static ScanFormat SniffMagic(string path)
    {
        // Read just enough bytes to discriminate. PLY starts with "ply\n"
        // or "ply\r\n"; STL ASCII starts with "solid"; STL binary's 84-byte
        // header looks like noise except for the trailing uint32 triangle
        // count that matches the file size. OBJ has no magic — fall through.
        const int probeLen = 6;
        var buf = new byte[probeLen];
        using (var fs = File.OpenRead(path))
        {
            int got = fs.Read(buf, 0, probeLen);
            if (got < probeLen)
                throw new FormatException("Scan file too small to identify format.");
        }
        // "ply\n" or "ply\r"
        if (buf[0] == 'p' && buf[1] == 'l' && buf[2] == 'y'
            && (buf[3] == '\n' || buf[3] == '\r'))
            return ScanFormat.Ply;
        // "solid" → could be ASCII STL or quirky binary; let the STL
        // parser disambiguate via the size invariant.
        if (buf[0] == 's' && buf[1] == 'o' && buf[2] == 'l'
            && buf[3] == 'i' && buf[4] == 'd')
            return ScanFormat.Stl;
        // "#VRML" header.
        if (buf[0] == '#' && buf[1] == 'V' && buf[2] == 'R' && buf[3] == 'M' && buf[4] == 'L')
            return ScanFormat.Vrml;
        // No PLY / STL / VRML magic — assume OBJ.
        return ScanFormat.Obj;
    }

    private static IReadOnlyList<ScanMesh> ReadPly(string path)
    {
        var ply = PlyMeshReader.ReadFile(path);
        var colors = ply.HasColors ? ply.VertexColorsRgb : null;
        return new[]
        {
            new ScanMesh(
                name: Path.GetFileNameWithoutExtension(path),
                vertexCoordsXyz: ply.VertexCoordsXyz,
                triangleIndices: ply.TriangleIndices,
                vertexColorsRgb: colors),
        };
    }

    private static IReadOnlyList<ScanMesh> ReadObj(string path)
    {
        var objs = ObjMeshReader.ReadFile(path);
        var result = new List<ScanMesh>(objs.Count);
        string fileStem = Path.GetFileNameWithoutExtension(path);
        for (int i = 0; i < objs.Count; i++)
        {
            string n = string.IsNullOrEmpty(objs[i].Name)
                ? (objs.Count > 1 ? $"{fileStem}-{i}" : fileStem)
                : objs[i].Name;
            result.Add(new ScanMesh(
                name: n,
                vertexCoordsXyz: objs[i].VertexCoordsXyz,
                triangleIndices: objs[i].TriangleIndices,
                vertexColorsRgb: null));
        }
        return result;
    }

    private static IReadOnlyList<ScanMesh> ReadStl(string path)
    {
        var stl = StlMeshReader.ReadFile(path);
        string fileStem = Path.GetFileNameWithoutExtension(path);
        return new[]
        {
            new ScanMesh(
                name: string.IsNullOrEmpty(stl.Name) ? fileStem : stl.Name,
                vertexCoordsXyz: stl.VertexCoordsXyz,
                triangleIndices: stl.TriangleIndices,
                vertexColorsRgb: null),
        };
    }
}
