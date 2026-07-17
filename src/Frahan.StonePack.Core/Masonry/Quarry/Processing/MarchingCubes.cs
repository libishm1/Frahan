#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Quarry.Processing;

// =============================================================================
// MarchingCubes / MarchingSquares -- clean-room isosurface / iso-contour
// extraction for the implicit kriging field F (KrigingField3D / KrigingField2D).
//
// PROVENANCE (required by the port brief -- no third-party case tables pasted):
//   * The cube corner / edge / face numbering below is defined from first
//     principles in this file (corner bit c = (i,j,k) offsets; 12 edges = the
//     corner pairs differing in one bit; 6 faces = fix one axis).
//   * A surface vertex is placed on every cube edge whose two endpoints straddle
//     `level`, at the LINEAR zero-crossing t = (level - vA)/(vB - vA). This is the
//     universal MC vertex rule and matches skimage.measure.marching_cubes vertex
//     positions to floating point (the crossing point is parameterization-
//     independent up to ULP), so the extracted VERTEX SET is comparable to
//     skimage's by Hausdorff distance regardless of the triangulation topology.
//   * Triangulation is derived HERE by the "face-contour loop" method: on each
//     cube face the surface meets the 4 face edges in an even number of active
//     edges (0/2/4); those active edges are joined by segments on the face; every
//     active edge lies on exactly two faces, so the segments close into loop(s)
//     through the active-edge vertices; each loop is fan-triangulated. Ambiguous
//     (4-active) faces are paired by a fixed cyclic-adjacency convention. This
//     reproduces classic MC vertex placement without a Lorensen-Cline / Bourke
//     lookup table; the connectivity may differ from Lewiner on ambiguous cells
//     (documented; the parity metric is on vertices, so this does not affect it).
//
//   Lorensen & Cline 1987 (the method) is public; the specific 256-entry triangle
//   TABLE is what this file deliberately does NOT reproduce. The G11/G13 note that
//   the clean-room C# MC would be needed because RhinoCommon ships no MC.
//
// net48 / Rhino-free.
// =============================================================================

/// <summary>Clean-room marching cubes over a scalar volume. Volume is a flat
/// C-order array (index (i,j,k) = (i*ny + j)*nz + k) with the same 'ij' lattice
/// convention KrigingField3D.PredictLattice3d emits.</summary>
public static class MarchingCubes
{
    // corner c -> (di,dj,dk) local offsets, bit0=i, bit1=j, bit2=k.
    private static int CDi(int c) => c & 1;
    private static int CDj(int c) => (c >> 1) & 1;
    private static int CDk(int c) => (c >> 2) & 1;

    // 12 edges as corner pairs (corners differ in exactly one bit).
    private static readonly int[][] Edges =
    {
        new[]{0,1}, new[]{2,3}, new[]{4,5}, new[]{6,7},   // e0..e3  (x-edges)
        new[]{0,2}, new[]{1,3}, new[]{4,6}, new[]{5,7},   // e4..e7  (y-edges)
        new[]{0,4}, new[]{1,5}, new[]{2,6}, new[]{3,7},   // e8..e11 (z-edges)
    };

    // 6 faces, each 4 edges in perimeter cyclic order (for ambiguous-face pairing).
    private static readonly int[][] Faces =
    {
        new[]{0,5,1,4},    // z=0  corners 0-1-3-2
        new[]{2,7,3,6},    // z=1  corners 4-5-7-6
        new[]{0,9,2,8},    // y=0  corners 0-1-5-4
        new[]{1,11,3,10},  // y=1  corners 2-3-7-6
        new[]{4,10,6,8},   // x=0  corners 0-2-6-4
        new[]{5,11,7,9},   // x=1  corners 1-3-7-5
    };

    /// <summary>Extract the {F=level} isosurface. Returns world-space verts and
    /// triangle faces (indices into verts).</summary>
    public static (List<double[]> Verts, List<int[]> Faces) Extract(
        double[] f, int nx, int ny, int nz, double level,
        double dx, double dy, double dz, double ox, double oy, double oz)
    {
        var verts = new List<double[]>();
        var faces = new List<int[]>();
        var edgeVert = new Dictionary<long, int>();
        long ng = Math.Max(nx, Math.Max(ny, nz)) + 2;

        int Idx(int i, int j, int k) => (i * ny + j) * nz + k;

        for (int i = 0; i < nx - 1; i++)
            for (int j = 0; j < ny - 1; j++)
                for (int k = 0; k < nz - 1; k++)
                {
                    // 8 corner values
                    var cv = new double[8];
                    for (int c = 0; c < 8; c++)
                        cv[c] = f[Idx(i + CDi(c), j + CDj(c), k + CDk(c))];

                    // active edges -> global vertex indices
                    var localVert = new int[12];
                    for (int e = 0; e < 12; e++) localVert[e] = -1;
                    bool any = false;
                    for (int e = 0; e < 12; e++)
                    {
                        int ca = Edges[e][0], cb = Edges[e][1];
                        double va = cv[ca], vb = cv[cb];
                        bool ina = va < level, inb = vb < level;
                        if (ina == inb) continue;                       // no crossing
                        any = true;
                        localVert[e] = EdgeVertex(f, nx, ny, nz, level, dx, dy, dz, ox, oy, oz,
                            i, j, k, ca, cb, va, vb, edgeVert, verts, ng);
                    }
                    if (!any) continue;

                    Triangulate(cv, level, localVert, faces);
                }

        return (verts, faces);
    }

    private static int EdgeVertex(double[] f, int nx, int ny, int nz, double level,
        double dx, double dy, double dz, double ox, double oy, double oz,
        int bi, int bj, int bk, int ca, int cb, double va, double vb,
        Dictionary<long, int> cache, List<double[]> verts, long ng)
    {
        int ai = bi + CDi(ca), aj = bj + CDj(ca), ak = bk + CDk(ca);
        int bi2 = bi + CDi(cb), bj2 = bj + CDj(cb), bk2 = bk + CDk(cb);

        // canonical grid-edge key (min grid vertex + axis)
        int axis = (ai != bi2) ? 0 : (aj != bj2) ? 1 : 2;
        int gi = Math.Min(ai, bi2), gj = Math.Min(aj, bj2), gk = Math.Min(ak, bk2);
        long key = ((axis * ng + gi) * ng + gj) * ng + gk;
        int vidx;
        if (cache.TryGetValue(key, out vidx)) return vidx;

        double t = (level - va) / (vb - va);
        double pi = ai + t * (bi2 - ai);
        double pj = aj + t * (bj2 - aj);
        double pk = ak + t * (bk2 - ak);
        var p = new[] { ox + pi * dx, oy + pj * dy, oz + pk * dz };
        vidx = verts.Count;
        verts.Add(p);
        cache[key] = vidx;
        return vidx;
    }

    private static void Triangulate(double[] cv, double level, int[] localVert, List<int[]> faces)
    {
        // build segments over active edges via the 6 faces
        var adj = new Dictionary<int, List<int>>();
        void AddSeg(int ea, int eb)
        {
            int va = localVert[ea], vb = localVert[eb];
            if (va < 0 || vb < 0 || va == vb) return;
            if (!adj.TryGetValue(va, out var la)) { la = new List<int>(); adj[va] = la; }
            if (!adj.TryGetValue(vb, out var lb)) { lb = new List<int>(); adj[vb] = lb; }
            la.Add(vb); lb.Add(va);
        }

        foreach (var face in Faces)
        {
            // active edges of this face, in cyclic order
            var act = new List<int>(4);
            foreach (int e in face) if (localVert[e] >= 0) act.Add(e);
            if (act.Count == 2) AddSeg(act[0], act[1]);
            else if (act.Count == 4)
            {
                // cyclic-adjacency pairing (a fixed non-crossing convention)
                // 'act' preserves the face's cyclic order, so pair (0,1),(2,3)
                AddSeg(act[0], act[1]);
                AddSeg(act[2], act[3]);
            }
            // 0 active -> nothing
        }
        if (adj.Count < 3) return;

        // extract loops (each vertex has degree 2) and fan-triangulate
        var visited = new HashSet<int>();
        foreach (var start in adj.Keys)
        {
            if (visited.Contains(start)) continue;
            var loop = new List<int>();
            int prev = -1, cur = start;
            int guard = 0;
            while (cur >= 0 && !visited.Contains(cur) && guard++ < 64)
            {
                visited.Add(cur);
                loop.Add(cur);
                var nb = adj[cur];
                int next = -1;
                foreach (int cand in nb)
                    if (cand != prev && !visited.Contains(cand)) { next = cand; break; }
                if (next < 0)
                {
                    // close back to start if adjacent (normal loop end)
                    foreach (int cand in nb) if (cand == start) { next = -1; break; }
                    break;
                }
                prev = cur; cur = next;
            }
            if (loop.Count >= 3)
                for (int t = 1; t + 1 < loop.Count; t++)
                    faces.Add(new[] { loop[0], loop[t], loop[t + 1] });
        }
    }
}

/// <summary>Clean-room marching squares over a 2D scalar field. Field is flat
/// C-order (index (i,k) = i*nz + k) with the 'ij' lattice convention
/// KrigingField2D.PredictLattice2d emits. Extracts the {F=level} crossing
/// vertices (comparable to contourpy line vertices by Hausdorff) and segments.</summary>
public static class MarchingSquares
{
    // corner c -> (di,dk) offsets, bit0=i, bit1=k.  cell corners 0..3.
    private static int CDi(int c) => c & 1;
    private static int CDk(int c) => (c >> 1) & 1;

    // 4 cell edges as corner pairs.
    private static readonly int[][] Edges =
    {
        new[]{0,1},   // e0 along i (k=0)
        new[]{2,3},   // e1 along i (k=1)
        new[]{0,2},   // e2 along k (i=0)
        new[]{1,3},   // e3 along k (i=1)
    };

    public static (List<double[]> Verts, List<int[]> Segments) Extract(
        double[] f, int nx, int nz, double level, double dx, double dz, double ox, double oz)
    {
        var verts = new List<double[]>();
        var segs = new List<int[]>();
        var edgeVert = new Dictionary<long, int>();
        long ng = Math.Max(nx, nz) + 2;

        int Idx(int i, int k) => i * nz + k;

        for (int i = 0; i < nx - 1; i++)
            for (int k = 0; k < nz - 1; k++)
            {
                var cv = new double[4];
                for (int c = 0; c < 4; c++) cv[c] = f[Idx(i + CDi(c), k + CDk(c))];

                var lv = new int[4];
                for (int e = 0; e < 4; e++) lv[e] = -1;
                var active = new List<int>(4);
                for (int e = 0; e < 4; e++)
                {
                    int ca = Edges[e][0], cb = Edges[e][1];
                    double va = cv[ca], vb = cv[cb];
                    if ((va < level) == (vb < level)) continue;
                    lv[e] = EdgeVertex(level, dx, dz, ox, oz, i, k, ca, cb, va, vb, edgeVert, verts, ng);
                    active.Add(e);
                }
                if (active.Count == 2) segs.Add(new[] { lv[active[0]], lv[active[1]] });
                else if (active.Count == 4)
                {
                    // ambiguous cell: pair by a fixed convention (e0-e2, e1-e3)
                    segs.Add(new[] { lv[0], lv[2] });
                    segs.Add(new[] { lv[1], lv[3] });
                }
            }
        return (verts, segs);
    }

    private static int EdgeVertex(double level, double dx, double dz, double ox, double oz,
        int bi, int bk, int ca, int cb, double va, double vb,
        Dictionary<long, int> cache, List<double[]> verts, long ng)
    {
        int ai = bi + CDi(ca), ak = bk + CDk(ca);
        int bi2 = bi + CDi(cb), bk2 = bk + CDk(cb);
        int axis = (ai != bi2) ? 0 : 1;
        int gi = Math.Min(ai, bi2), gk = Math.Min(ak, bk2);
        long key = (axis * ng + gi) * ng + gk;
        int vidx;
        if (cache.TryGetValue(key, out vidx)) return vidx;

        double t = (level - va) / (vb - va);
        double pi = ai + t * (bi2 - ai);
        double pk = ak + t * (bk2 - ak);
        var p = new[] { ox + pi * dx, oz + pk * dz };
        vidx = verts.Count;
        verts.Add(p);
        cache[key] = vidx;
        return vidx;
    }
}
