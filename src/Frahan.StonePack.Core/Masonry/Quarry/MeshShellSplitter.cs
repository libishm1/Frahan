#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Quarry;

// =============================================================================
// MeshShellSplitter — separates a multi-shell triangle mesh into one
// (vertices, triangles) pair per connected shell. Uses triangle-triangle
// adjacency over shared vertices and BFS for the partition. Pure managed,
// no Rhino runtime needed.
//
// "Shell" here means a connected component of the triangle adjacency graph,
// i.e. triangles that share at least one vertex. This is the right notion
// for quarry decomposition: a Rhino Mesh that contains two physically
// separate stones (after a Mesh.SplitDisjointPieces call upstream) becomes
// two Slabs downstream.
//
// The output (verts_i, tris_i) preserves vertex coordinates exactly; vertex
// indices are remapped per shell (a global vertex used by two shells is
// duplicated, once per shell, with a fresh index).
// =============================================================================

/// <summary>
/// Splits a multi-shell triangle mesh into one shell per connected component.
/// </summary>
public static class MeshShellSplitter
{
    /// <summary>One output shell — vertex coords (flat, [x,y,z,...]) and triangle indices (flat, [i,j,k,...]).</summary>
    public sealed class Shell
    {
        public Shell(double[] verts, int[] tris)
        {
            VertexCoordsXyz = verts ?? throw new ArgumentNullException(nameof(verts));
            TriangleIndices = tris  ?? throw new ArgumentNullException(nameof(tris));
            if (verts.Length % 3 != 0)
                throw new ArgumentException("verts length must be a multiple of 3", nameof(verts));
            if (tris.Length % 3 != 0)
                throw new ArgumentException("tris length must be a multiple of 3", nameof(tris));
        }

        public double[] VertexCoordsXyz { get; }
        public int[] TriangleIndices { get; }

        public int VertexCount => VertexCoordsXyz.Length / 3;
        public int TriangleCount => TriangleIndices.Length / 3;
    }

    /// <summary>
    /// Run the BFS partition. Returns one <see cref="Shell"/> per connected
    /// component.
    /// </summary>
    public static IReadOnlyList<Shell> Split(
        IReadOnlyList<double> vertexCoordsXyz,
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

        int vCount = vertexCoordsXyz.Count / 3;
        int tCount = triangleIndices.Count / 3;
        if (tCount == 0)
            return new List<Shell>();

        // ---- Build vertex-to-triangle adjacency. ----
        var vToTri = new List<List<int>>(vCount);
        for (int i = 0; i < vCount; i++) vToTri.Add(new List<int>(4));
        for (int t = 0; t < tCount; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                int v = triangleIndices[3 * t + k];
                if (v < 0 || v >= vCount)
                    throw new ArgumentException(
                        $"triangleIndices[{3 * t + k}] = {v} out of range [0, {vCount})",
                        nameof(triangleIndices));
                vToTri[v].Add(t);
            }
        }

        // ---- BFS over triangles. ----
        var triShell = new int[tCount];
        for (int i = 0; i < tCount; i++) triShell[i] = -1;
        int shellCount = 0;
        var queue = new Queue<int>();

        for (int seed = 0; seed < tCount; seed++)
        {
            if (triShell[seed] != -1) continue;

            queue.Enqueue(seed);
            triShell[seed] = shellCount;
            while (queue.Count > 0)
            {
                int t = queue.Dequeue();
                for (int k = 0; k < 3; k++)
                {
                    int v = triangleIndices[3 * t + k];
                    var neighbours = vToTri[v];
                    for (int n = 0; n < neighbours.Count; n++)
                    {
                        int nt = neighbours[n];
                        if (triShell[nt] != -1) continue;
                        triShell[nt] = shellCount;
                        queue.Enqueue(nt);
                    }
                }
            }
            shellCount += 1;
        }
        if (shellCount < 1)
            throw new InvalidOperationException("BFS produced 0 shells");

        // ---- Materialise per-shell vertex / triangle arrays. ----
        var shells = new List<Shell>(shellCount);
        for (int s = 0; s < shellCount; s++)
        {
            var localVertOf = new Dictionary<int, int>();
            var localVerts = new List<double>(vCount);
            var localTris = new List<int>(tCount);
            for (int t = 0; t < tCount; t++)
            {
                if (triShell[t] != s) continue;
                for (int k = 0; k < 3; k++)
                {
                    int globalV = triangleIndices[3 * t + k];
                    if (!localVertOf.TryGetValue(globalV, out int localV))
                    {
                        localV = localVerts.Count / 3;
                        localVertOf.Add(globalV, localV);
                        localVerts.Add(vertexCoordsXyz[3 * globalV + 0]);
                        localVerts.Add(vertexCoordsXyz[3 * globalV + 1]);
                        localVerts.Add(vertexCoordsXyz[3 * globalV + 2]);
                    }
                    localTris.Add(localV);
                }
            }
            shells.Add(new Shell(localVerts.ToArray(), localTris.ToArray()));
        }
        return shells;
    }
}
