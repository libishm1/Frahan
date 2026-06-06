using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Rhino.Geometry;

namespace Frahan.Surface
{
    /// <summary>
    /// Writes Rhino Mesh objects to OBJ for BFF input, and parses BFF OBJ output
    /// into a FaceCornerUvTable. UV coordinates are stored per face-corner to handle
    /// UV seams correctly — never assumed to be one-per-vertex.
    /// </summary>
    public static class MeshObjIO
    {
        /// <summary>
        /// Writes a triangulated mesh to an OBJ file suitable for BFF input.
        /// Quads are split to two triangles. The face order in the file matches
        /// mesh.Faces indices 0..N-1, which is required for UV correlation after BFF.
        /// </summary>
        public static void WriteMeshToObj(Mesh mesh, string path)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine("# Frahan StonePack — BFF input mesh");

            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                var v = mesh.Vertices[i];
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "v {0:G10} {1:G10} {2:G10}", v.X, v.Y, v.Z));
            }

            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                var f = mesh.Faces[i];
                if (f.IsTriangle)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "f {0} {1} {2}", f.A + 1, f.B + 1, f.C + 1));
                }
                else if (f.IsQuad)
                {
                    // Split quads — BFF expects triangles only
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "f {0} {1} {2}", f.A + 1, f.B + 1, f.C + 1));
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "f {0} {1} {2}", f.A + 1, f.C + 1, f.D + 1));
                }
            }

            File.WriteAllText(path, sb.ToString(), Encoding.ASCII);
        }

        /// <summary>
        /// Returns the number of triangular face entries the OBJ writer would produce
        /// for a given mesh. Quads each produce two triangles.
        /// Use this to pass expectedFaceCount to TryParseObjWithFaceCornerUVs.
        /// </summary>
        public static int CountWrittenFaces(Mesh mesh)
        {
            int count = 0;
            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                if (mesh.Faces[i].IsTriangle) count++;
                else if (mesh.Faces[i].IsQuad) count += 2;
            }
            return count;
        }

        /// <summary>
        /// Parses BFF output OBJ and builds a FaceCornerUvTable keyed by (faceIndex, cornerIndex).
        /// The face order in the OBJ must match the face order written by WriteMeshToObj.
        /// Returns false and sets errorMessage on any structural problem.
        /// </summary>
        public static bool TryParseObjWithFaceCornerUVs(
            string path,
            int expectedFaceCount,
            out FaceCornerUvTable uvTable,
            out string errorMessage)
        {
            uvTable = new FaceCornerUvTable();
            errorMessage = string.Empty;

            if (!File.Exists(path))
            {
                errorMessage = $"BFF output OBJ not found: {path}";
                return false;
            }

            var vtList = new List<Point2d>(capacity: 256);
            int parsedFaceIndex = 0;
            int lineNumber = 0;

            try
            {
                foreach (var rawLine in File.ReadLines(path))
                {
                    lineNumber++;
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    if (line.StartsWith("vt ", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!TryParseVt(line, lineNumber, out Point2d uv, out errorMessage))
                            return false;
                        vtList.Add(uv);
                    }
                    else if (line.StartsWith("f ", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!TryParseFaceUVs(line, lineNumber, vtList, parsedFaceIndex, uvTable, out errorMessage))
                            return false;
                        parsedFaceIndex++;
                    }
                    // Skip v, vn, o, g, usemtl, s lines silently
                }

                if (parsedFaceIndex == 0)
                {
                    errorMessage = "No face entries found in BFF output OBJ.";
                    return false;
                }

                if (expectedFaceCount > 0 && parsedFaceIndex != expectedFaceCount)
                {
                    errorMessage = $"Face count mismatch: BFF wrote {parsedFaceIndex} faces, expected {expectedFaceCount}. Topology changed during BFF execution.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"OBJ parsing failed at line {lineNumber}: {ex.Message}";
                return false;
            }
        }

        private static bool TryParseVt(string line, int lineNum, out Point2d uv, out string error)
        {
            uv = default;
            error = string.Empty;

            var tokens = line.Substring(3).Trim()
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length < 2)
            {
                error = $"Line {lineNum}: malformed vt — need at least u and v.";
                return false;
            }

            if (!double.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double u) ||
                !double.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            {
                error = $"Line {lineNum}: cannot parse vt floats.";
                return false;
            }

            uv = new Point2d(u, v);
            return true;
        }

        private static bool TryParseFaceUVs(
            string line, int lineNum,
            IReadOnlyList<Point2d> vtList,
            int faceIndex,
            FaceCornerUvTable table,
            out string error)
        {
            error = string.Empty;

            var tokens = line.Substring(2).Trim()
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length < 3)
            {
                error = $"Line {lineNum}: face has fewer than 3 corners.";
                return false;
            }

            for (int corner = 0; corner < 3; corner++)
            {
                var token = tokens[corner];
                var slash = token.IndexOf('/');

                if (slash < 0)
                {
                    error = $"Line {lineNum}: corner '{token}' has no UV index. BFF output should always include v/vt.";
                    return false;
                }

                var slash2 = token.IndexOf('/', slash + 1);
                var vtPart = slash2 >= 0
                    ? token.Substring(slash + 1, slash2 - slash - 1)
                    : token.Substring(slash + 1);

                if (string.IsNullOrWhiteSpace(vtPart))
                {
                    error = $"Line {lineNum}: corner '{token}' has empty UV index slot (v//vn format not supported here).";
                    return false;
                }

                if (!int.TryParse(vtPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int vtIdx1))
                {
                    error = $"Line {lineNum}: cannot parse UV index '{vtPart}'.";
                    return false;
                }

                int vtIdx0 = vtIdx1 - 1;
                if (vtIdx0 < 0 || vtIdx0 >= vtList.Count)
                {
                    error = $"Line {lineNum}: UV index {vtIdx1} is out of range (have {vtList.Count} vt entries).";
                    return false;
                }

                table.SetUv(faceIndex, corner, vtList[vtIdx0].X, vtList[vtIdx0].Y);
            }

            return true;
        }
    }
}
