#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Frahan.Core.ScanIngest;

// =============================================================================
// ObjMeshReader — pure-managed Wavefront OBJ parser. No third-party
// dependency. Scope: scan/photogrammetry export files (v + f, optionally
// vn / vt; arbitrary groups/objects).
//
// Phase F2 of the UX architecture report §7.7.A-B scan-ingest rollout.
//
// Supported:
//   v  x y z [w]                — vertex (w ignored)
//   f  a b c                    — triangle, vertex-only
//   f  a/t b/t c/t              — triangle, vertex/tex
//   f  a//n b//n c//n           — triangle, vertex//normal
//   f  a/t/n b/t/n c/t/n        — triangle, vertex/tex/normal
//   f  a b c d [...]            — n-gon, fan-triangulated
//   g  name                     — group (used as mesh boundary in
//                                  ReadBundle)
//   o  name                     — object (treated like g for bundling)
//   # comment                   — ignored
//
// Skipped (parsed past but discarded):
//   vt, vn, vp, s, usemtl, mtllib, l (line), p (point), curv, surf, etc.
//
// Negative face indices (relative to end-of-list, per OBJ spec) are
// resolved against the running vertex count.
//
// References: Wavefront OBJ spec
// http://paulbourke.net/dataformats/obj/
// =============================================================================

/// <summary>Single parsed mesh (one OBJ file, or one group/object inside
/// a multi-group OBJ).</summary>
public sealed class ObjMesh
{
    public ObjMesh(string name, IReadOnlyList<double> vertexCoordsXyz,
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

    /// <summary>Group / object name, or "" for an unnamed OBJ file.</summary>
    public string Name { get; }
    public IReadOnlyList<double> VertexCoordsXyz { get; }
    public IReadOnlyList<int> TriangleIndices { get; }
    public int VertexCount => VertexCoordsXyz.Count / 3;
    public int TriangleCount => TriangleIndices.Count / 3;
}

public static class ObjMeshReader
{
    /// <summary>
    /// Read a single .obj file and return one mesh per `o` / `g` group.
    /// Files without any group declaration return a single unnamed mesh.
    /// Empty groups (header-only) are dropped.
    /// </summary>
    public static IReadOnlyList<ObjMesh> ReadFile(string path)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("OBJ file not found", path);
        using var reader = new StreamReader(path);
        return ReadInternal(reader);
    }

    /// <summary>Read OBJ content from any TextReader (file or string).</summary>
    public static IReadOnlyList<ObjMesh> ReadFromString(string objText)
    {
        if (objText == null) throw new ArgumentNullException(nameof(objText));
        using var reader = new StringReader(objText);
        return ReadInternal(reader);
    }

    private static IReadOnlyList<ObjMesh> ReadInternal(TextReader reader)
    {
        // Vertex pool is global across groups (OBJ semantics: face indices
        // refer to the file's running vertex pool, not the group's local
        // pool).
        var vertices = new List<double>(1024);
        // Per-group accumulators.
        var groups = new List<(string name, List<int> indices)>();
        groups.Add(("", new List<int>(1024))); // default unnamed group

        string line;
        var sep = new[] { ' ', '\t' };
        int lineNo = 0;
        while ((line = reader.ReadLine()) != null)
        {
            lineNo++;
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line.Substring(0, hash);
            line = line.Trim();
            if (line.Length == 0) continue;

            var parts = line.Split(sep, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            switch (parts[0])
            {
                case "v":
                    if (parts.Length < 4)
                        throw new FormatException($"OBJ line {lineNo}: 'v' needs 3 coords, got {parts.Length - 1}.");
                    vertices.Add(double.Parse(parts[1], CultureInfo.InvariantCulture));
                    vertices.Add(double.Parse(parts[2], CultureInfo.InvariantCulture));
                    vertices.Add(double.Parse(parts[3], CultureInfo.InvariantCulture));
                    break;

                case "f":
                {
                    if (parts.Length < 4)
                        throw new FormatException($"OBJ line {lineNo}: 'f' needs at least 3 vertices.");
                    int polyN = parts.Length - 1;
                    var polyVertIdx = new int[polyN];
                    int totalVerts = vertices.Count / 3;
                    for (int k = 0; k < polyN; k++)
                    {
                        // Strip texture/normal indices if present (we only
                        // need vertex index).
                        string token = parts[k + 1];
                        int slash = token.IndexOf('/');
                        if (slash >= 0) token = token.Substring(0, slash);
                        int idx = int.Parse(token, CultureInfo.InvariantCulture);
                        if (idx < 0) idx = totalVerts + idx + 1; // -1 → last; -2 → second-last, etc.
                        if (idx < 1 || idx > totalVerts)
                            throw new FormatException(
                                $"OBJ line {lineNo}: face vertex index {idx} out of range [1, {totalVerts}].");
                        polyVertIdx[k] = idx - 1; // OBJ is 1-based; convert to 0-based
                    }
                    // Fan-triangulate.
                    var gi = groups[groups.Count - 1].indices;
                    for (int k = 1; k < polyN - 1; k++)
                    {
                        gi.Add(polyVertIdx[0]);
                        gi.Add(polyVertIdx[k]);
                        gi.Add(polyVertIdx[k + 1]);
                    }
                    break;
                }

                case "g":
                case "o":
                {
                    string name = parts.Length > 1 ? parts[1] : (parts[0] == "g" ? "group" : "object");
                    // If the existing default/unnamed group is empty, replace it; else append.
                    var cur = groups[groups.Count - 1];
                    if (cur.indices.Count == 0)
                        groups[groups.Count - 1] = (name, cur.indices);
                    else
                        groups.Add((name, new List<int>(512)));
                    break;
                }

                // Skip vertex normals / texture coords / smoothing / materials.
                case "vn":
                case "vt":
                case "vp":
                case "s":
                case "usemtl":
                case "mtllib":
                case "l":
                case "p":
                case "curv":
                case "surf":
                    break;

                default:
                    // Unknown directive — skip silently to stay permissive.
                    break;
            }
        }

        // Materialise groups: each group's index list references the global
        // vertex pool. To produce self-contained ObjMesh entries, remap.
        var result = new List<ObjMesh>();
        foreach (var (name, indices) in groups)
        {
            if (indices.Count == 0) continue;

            // Build a remap dense for this group.
            var remap = new Dictionary<int, int>(indices.Count);
            var groupVerts = new List<double>();
            var groupIdx = new List<int>(indices.Count);
            foreach (int globalIdx in indices)
            {
                if (!remap.TryGetValue(globalIdx, out int localIdx))
                {
                    localIdx = groupVerts.Count / 3;
                    groupVerts.Add(vertices[3 * globalIdx + 0]);
                    groupVerts.Add(vertices[3 * globalIdx + 1]);
                    groupVerts.Add(vertices[3 * globalIdx + 2]);
                    remap[globalIdx] = localIdx;
                }
                groupIdx.Add(localIdx);
            }
            result.Add(new ObjMesh(name, groupVerts, groupIdx));
        }

        if (result.Count == 0)
            throw new FormatException("OBJ file produced no triangles (no 'f' lines or all groups empty).");

        return result;
    }
}
