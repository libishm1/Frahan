#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Frahan.Core.ScanIngest;

// =============================================================================
// VrmlMeshReader -- pure-managed VRML 2.0 / VRML97 (.wrl) IndexedFaceSet reader.
// No native dependency, no NuGet -- safe to load inside Rhino/Grasshopper on net48
// (chosen over AssimpNet, whose native assimp.dll fails to load on net48).
//
// Handles ASCII VRML exported from Artec Studio (and most VRML tools). Per
// IndexedFaceSet it parses:
//   - Coordinate { point [ x y z, ... ] }   -> vertices (the ONLY 'point' read;
//     TextureCoordinate's 2D 'point' is skipped because its node token differs)
//   - coordIndex [ i j k -1, ... ]           -> faces, -1 separated, n-gons fan-
//     triangulated
//   - Color { color [ r g b, ... ] }         -> optional per-vertex colour (0..1
//     -> byte RGB) attached when its count matches the coordinate count
// textureCoordinate, normals, transforms, DEF/USE instancing are ignored.
// Returns one ScanMesh per IndexedFaceSet. Strict validation with clear messages.
// =============================================================================

public static class VrmlMeshReader
{
    public static IReadOnlyList<ScanMesh> ReadFile(string path)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("WRL/VRML file not found", path);

        var tk = Tokenize(StripComments(File.ReadAllText(path)));
        var coordBlocks = new List<List<double>>();
        var indexBlocks = new List<List<int>>();
        var colorBlocks = new List<List<double>>();

        for (int i = 0; i < tk.Count; i++)
        {
            if (tk[i] == "Coordinate")
            {
                var pts = ReadBracketNumbersAfter(tk, ref i, "point");
                if (pts != null) coordBlocks.Add(pts);
            }
            else if (tk[i] == "coordIndex")
            {
                var idx = ReadBracketNumbersAfter(tk, ref i, null);
                if (idx != null)
                {
                    var ints = new List<int>(idx.Count);
                    foreach (var d in idx) ints.Add((int)d);
                    indexBlocks.Add(ints);
                }
            }
            else if (tk[i] == "Color")
            {
                var cols = ReadBracketNumbersAfter(tk, ref i, "color");
                if (cols != null) colorBlocks.Add(cols);
            }
        }

        if (coordBlocks.Count == 0)
            throw new FormatException("WRL: no Coordinate.point found (not an IndexedFaceSet mesh?).");
        if (coordBlocks.Count != indexBlocks.Count)
            throw new FormatException(
                $"WRL: {coordBlocks.Count} Coordinate block(s) but {indexBlocks.Count} coordIndex block(s); " +
                "cannot pair (DEF/USE instancing is not supported yet).");

        string stem = Path.GetFileNameWithoutExtension(path);
        bool colorAligned = colorBlocks.Count == coordBlocks.Count;
        var result = new List<ScanMesh>(coordBlocks.Count);

        for (int b = 0; b < coordBlocks.Count; b++)
        {
            var pts = coordBlocks[b];
            if (pts.Count % 3 != 0)
                throw new FormatException($"WRL: Coordinate point count {pts.Count} is not a multiple of 3.");
            int vcount = pts.Count / 3;
            var tris = Triangulate(indexBlocks[b], vcount);

            List<byte> rgb = null;
            if (colorAligned && colorBlocks[b].Count == pts.Count) // per-vertex r g b in 0..1
            {
                rgb = new List<byte>(pts.Count);
                foreach (var f in colorBlocks[b])
                    rgb.Add((byte)Math.Max(0, Math.Min(255, (int)Math.Round(f * 255.0))));
            }

            result.Add(new ScanMesh(coordBlocks.Count > 1 ? stem + "-" + b : stem, pts, tris, rgb));
        }
        return result;
    }

    private static List<int> Triangulate(List<int> coordIndex, int vcount)
    {
        var tris = new List<int>();
        var face = new List<int>();
        foreach (int idx in coordIndex)
        {
            if (idx < 0) { EmitFace(face, tris, vcount); face.Clear(); }
            else face.Add(idx);
        }
        if (face.Count > 0) EmitFace(face, tris, vcount); // tolerate a missing trailing -1
        if (tris.Count == 0)
            throw new FormatException("WRL: coordIndex produced no triangles.");
        return tris;
    }

    private static void EmitFace(List<int> face, List<int> tris, int vcount)
    {
        if (face.Count < 3) return;
        for (int k = 1; k <= face.Count - 2; k++) // fan
        {
            int a = face[0], b = face[k], c = face[k + 1];
            if (a < 0 || a >= vcount || b < 0 || b >= vcount || c < 0 || c >= vcount)
                throw new FormatException($"WRL: coordIndex references an out-of-range vertex (vcount={vcount}).");
            tris.Add(a); tris.Add(b); tris.Add(c);
        }
    }

    // From token i, optionally find `keyword`, then the next '[', read numbers until ']'.
    // Stops (returns null) if the enclosing node closes ('}') before the data is found.
    private static List<double> ReadBracketNumbersAfter(List<string> tk, ref int i, string keyword)
    {
        int j = i + 1;
        if (keyword != null)
        {
            while (j < tk.Count && tk[j] != keyword)
            {
                if (tk[j] == "}") return null;
                j++;
            }
            if (j >= tk.Count) return null;
        }
        while (j < tk.Count && tk[j] != "[")
        {
            if (tk[j] == "}") return null;
            j++;
        }
        if (j >= tk.Count) return null;
        j++;
        var vals = new List<double>();
        for (; j < tk.Count && tk[j] != "]"; j++)
            if (double.TryParse(tk[j], NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                vals.Add(d);
        i = j;
        return vals;
    }

    private static string StripComments(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool inComment = false;
        foreach (char ch in text)
        {
            if (inComment) { if (ch == '\n') { inComment = false; sb.Append(ch); } continue; }
            if (ch == '#') { inComment = true; continue; }
            sb.Append(ch);
        }
        return sb.ToString();
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var cur = new StringBuilder();
        foreach (char ch in text)
        {
            if (ch == '{' || ch == '}' || ch == '[' || ch == ']')
            {
                if (cur.Length > 0) { tokens.Add(cur.ToString()); cur.Clear(); }
                tokens.Add(ch.ToString());
            }
            else if (char.IsWhiteSpace(ch) || ch == ',')
            {
                if (cur.Length > 0) { tokens.Add(cur.ToString()); cur.Clear(); }
            }
            else cur.Append(ch);
        }
        if (cur.Length > 0) tokens.Add(cur.ToString());
        return tokens;
    }
}
