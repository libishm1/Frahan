using System;
using Rhino.Geometry;

namespace Frahan.Surface;

/// <summary>
/// Static read-only diagnostics for a Rhino <see cref="Mesh"/>. The mesh is
/// never mutated. All methods return defensive defaults (0, false) when the
/// input is null rather than throwing, except where the caller's intent is
/// clearly to validate (IsManifold, IsClosed) - those return false on null.
///
/// Lives in Frahan.Surface (Rhino-bound). Spec 11 section 2 lists this as a
/// proposed Frahan.Mesh helper; today it ships in Frahan.Surface and will
/// move into a dedicated Frahan.Mesh assembly when refactor R2 splits.
/// </summary>
public static class MeshDiagnostics
{
    public static int VertexCount(Mesh mesh) =>
        mesh == null ? 0 : mesh.Vertices.Count;

    public static int FaceCount(Mesh mesh) =>
        mesh == null ? 0 : mesh.Faces.Count;

    public static int TriangleCount(Mesh mesh)
    {
        if (mesh == null) return 0;
        int n = 0;
        for (int i = 0; i < mesh.Faces.Count; i++)
            if (mesh.Faces[i].IsTriangle) n++;
        return n;
    }

    public static int QuadCount(Mesh mesh)
    {
        if (mesh == null) return 0;
        int n = 0;
        for (int i = 0; i < mesh.Faces.Count; i++)
            if (mesh.Faces[i].IsQuad) n++;
        return n;
    }

    /// <summary>
    /// True if the mesh is closed (manifold + watertight). Returns false on null.
    /// </summary>
    public static bool IsClosed(Mesh mesh) =>
        mesh != null && mesh.IsClosed;

    /// <summary>
    /// True if the mesh is manifold (every edge bounds at most two faces, no
    /// non-manifold vertices). Returns false on null. Wraps Mesh.IsManifold
    /// with topologicalTest=true.
    /// </summary>
    public static bool IsManifold(Mesh mesh) =>
        mesh != null && mesh.IsManifold(topologicalTest: true, out _, out _);

    /// <summary>
    /// True if every face has consistent winding. Returns false on null.
    /// </summary>
    public static bool HasConsistentWinding(Mesh mesh)
    {
        if (mesh == null) return false;
        // RhinoCommon does not expose a single "winding consistent" flag; the
        // closest proxy is asking the mesh to report manifold + oriented.
        bool _isManifold = mesh.IsManifold(topologicalTest: true, out bool isOriented, out _);
        return _isManifold && isOriented;
    }

    /// <summary>
    /// Average length of all unique edges. Returns 0.0 on null or empty mesh.
    /// </summary>
    public static double AverageEdgeLength(Mesh mesh)
    {
        if (mesh == null || mesh.Faces.Count == 0) return 0.0;

        var topology = mesh.TopologyEdges;
        int edges = topology.Count;
        if (edges == 0) return 0.0;

        double total = 0.0;
        int counted = 0;
        for (int i = 0; i < edges; i++)
        {
            var line = topology.EdgeLine(i);
            double len = line.Length;
            // double.IsFinite is .NET Standard 2.1+; we target netstandard2.0 / net48,
            // so test for NaN + Infinity manually.
            if (!double.IsNaN(len) && !double.IsInfinity(len) && len > 0.0)
            {
                total += len;
                counted++;
            }
        }
        return counted == 0 ? 0.0 : total / counted;
    }

    /// <summary>
    /// Volume of the mesh's axis-aligned bounding box. Returns 0.0 on null
    /// or empty mesh.
    /// </summary>
    public static double BoundingBoxVolume(Mesh mesh)
    {
        if (mesh == null || mesh.Vertices.Count == 0) return 0.0;
        var bbox = mesh.GetBoundingBox(accurate: false);
        if (!bbox.IsValid) return 0.0;
        var size = bbox.Diagonal;
        return Math.Abs(size.X) * Math.Abs(size.Y) * Math.Abs(size.Z);
    }
}
