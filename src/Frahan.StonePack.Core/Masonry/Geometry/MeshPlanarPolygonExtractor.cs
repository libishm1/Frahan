#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Interfaces;

namespace Frahan.Masonry.Geometry;

// =============================================================================
// MeshPlanarPolygonExtractor — for an open / planar mesh, walk the
// boundary edges (those incident to exactly one triangle), connect them
// into closed loops, and project each loop into the input plane's local
// 2D basis. Outer loop is the loop with the largest area; the rest are
// holes.
//
// Use case: feed a BFF-flattened mesh patch to the trencadís packer
// without going through Rhino curve extraction (which is lossy on
// non-trivial boundaries). The output is a list of 2D polygons that the
// existing packers can consume directly.
//
// Pure-managed, runtime-agnostic. The caller supplies the plane (origin
// + two orthogonal in-plane axes); no plane-fitting is done here so the
// result is deterministic and reproducible.
// =============================================================================

public sealed class PlanarPolygonExtractionResult
{
    public PlanarPolygonExtractionResult(
        List<(double X, double Y)> outer,
        List<List<(double X, double Y)>> holes)
    {
        Outer = outer ?? throw new ArgumentNullException(nameof(outer));
        Holes = holes ?? throw new ArgumentNullException(nameof(holes));
    }

    /// <summary>The largest-area loop, in CCW order.</summary>
    public List<(double X, double Y)> Outer { get; }

    /// <summary>Inner loops (holes), each in CW order.</summary>
    public List<List<(double X, double Y)>> Holes { get; }

    public bool HasOuter => Outer.Count >= 3;
    public int HoleCount => Holes.Count;
}

public static class MeshPlanarPolygonExtractor
{
    /// <summary>
    /// Extract closed boundary loops from <paramref name="mesh"/> and
    /// project them into the (origin, uAxis, vAxis) plane. Largest-area
    /// loop becomes the outer; the rest become holes (re-oriented CW).
    /// </summary>
    public static PlanarPolygonExtractionResult Extract(
        MeshSnapshot mesh,
        double originX, double originY, double originZ,
        double uX, double uY, double uZ,
        double vX, double vY, double vZ)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        // Caller is responsible for sending an orthogonal basis; we
        // unitise just in case.
        Unitise(ref uX, ref uY, ref uZ);
        Unitise(ref vX, ref vY, ref vZ);

        // Find boundary edges (incident to exactly one triangle).
        var edgeUse = new Dictionary<long, int>(mesh.TriangleCount * 3);
        // Track directed-edge → triangle to recover CCW boundary order.
        var directed = new Dictionary<long, int>(mesh.TriangleCount * 3);

        var t = mesh.TriangleIndices;
        int tc = mesh.TriangleCount;
        for (int i = 0; i < tc; i++)
        {
            int a = t[3 * i + 0], b = t[3 * i + 1], c = t[3 * i + 2];
            BumpEdge(edgeUse, a, b);
            BumpEdge(edgeUse, b, c);
            BumpEdge(edgeUse, c, a);
            directed[Pack(a, b)] = i;
            directed[Pack(b, c)] = i;
            directed[Pack(c, a)] = i;
        }

        var boundaryDirected = new HashSet<long>();
        foreach (var kv in edgeUse)
        {
            if (kv.Value != 1) continue;
            // Find which directed edge corresponds to this canonical edge.
            int hi = (int)(kv.Key & 0xFFFFFFFF);
            int lo = (int)(kv.Key >> 32);
            // The canonical key was built lo<hi; only one direction is used.
            long fwd = Pack(lo, hi);
            long bwd = Pack(hi, lo);
            if (directed.ContainsKey(fwd)) boundaryDirected.Add(fwd);
            else if (directed.ContainsKey(bwd)) boundaryDirected.Add(bwd);
        }

        // Walk loops by following directed boundary edges.
        var loops = new List<List<int>>();
        var nextFromVertex = new Dictionary<int, List<int>>();
        foreach (var d in boundaryDirected)
        {
            int from = (int)(d >> 32);
            int to = (int)(d & 0xFFFFFFFF);
            if (!nextFromVertex.TryGetValue(from, out var list))
            {
                list = new List<int>(2);
                nextFromVertex[from] = list;
            }
            list.Add(to);
        }
        var consumed = new HashSet<long>();
        foreach (var d in boundaryDirected)
        {
            if (consumed.Contains(d)) continue;
            var loop = new List<int>();
            long current = d;
            while (true)
            {
                if (consumed.Contains(current)) break;
                consumed.Add(current);
                int from = (int)(current >> 32);
                int to = (int)(current & 0xFFFFFFFF);
                loop.Add(from);
                if (!nextFromVertex.TryGetValue(to, out var outs))
                {
                    // Open chain — abandon (shouldn't happen on a clean mesh).
                    break;
                }
                long next = -1;
                for (int k = 0; k < outs.Count; k++)
                {
                    long candidate = Pack(to, outs[k]);
                    if (consumed.Contains(candidate)) continue;
                    next = candidate;
                    break;
                }
                if (next < 0) break;
                current = next;
            }
            if (loop.Count >= 3) loops.Add(loop);
        }

        // Project each loop into 2D and compute area.
        var projected = new List<List<(double X, double Y)>>(loops.Count);
        var areas = new List<double>(loops.Count);
        var v = mesh.VertexCoordsXyz;
        for (int li = 0; li < loops.Count; li++)
        {
            var poly = new List<(double X, double Y)>(loops[li].Count);
            for (int i = 0; i < loops[li].Count; i++)
            {
                int idx = loops[li][i];
                double dx = v[3 * idx + 0] - originX;
                double dy = v[3 * idx + 1] - originY;
                double dz = v[3 * idx + 2] - originZ;
                double u = dx * uX + dy * uY + dz * uZ;
                double w = dx * vX + dy * vY + dz * vZ;
                poly.Add((u, w));
            }
            projected.Add(poly);
            areas.Add(Math.Abs(RobustPolygon2D.SignedArea(poly)));
        }

        if (projected.Count == 0)
            return new PlanarPolygonExtractionResult(
                new List<(double, double)>(),
                new List<List<(double, double)>>());

        // Pick the largest loop as the outer.
        int outerIdx = 0;
        for (int i = 1; i < areas.Count; i++)
            if (areas[i] > areas[outerIdx]) outerIdx = i;

        var outer = projected[outerIdx];
        if (RobustPolygon2D.SignedArea(outer) < 0) outer.Reverse();  // ensure CCW

        var holes = new List<List<(double X, double Y)>>(projected.Count - 1);
        for (int i = 0; i < projected.Count; i++)
        {
            if (i == outerIdx) continue;
            var hole = projected[i];
            if (RobustPolygon2D.SignedArea(hole) > 0) hole.Reverse();  // ensure CW
            holes.Add(hole);
        }
        return new PlanarPolygonExtractionResult(outer, holes);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static long Pack(int a, int b) => ((long)a << 32) | (uint)b;

    private static void BumpEdge(Dictionary<long, int> edgeUse, int a, int b)
    {
        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        if (edgeUse.TryGetValue(key, out int n)) edgeUse[key] = n + 1;
        else edgeUse[key] = 1;
    }

    private static void Unitise(ref double x, ref double y, ref double z)
    {
        double m = Math.Sqrt(x * x + y * y + z * z);
        if (m < 1e-20) return;
        x /= m; y /= m; z /= m;
    }
}
