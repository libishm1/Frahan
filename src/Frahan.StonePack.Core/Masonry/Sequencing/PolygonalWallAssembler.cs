#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.DataModel;

namespace Frahan.Masonry.Sequencing;

// =============================================================================
// PolygonalWallAssembler — P1.2 of EVOLUTION_PLAN_MASONRY.md (2026-06-10):
// exact generator-adjacency joints.
//
// The wall generator KNOWS the cell adjacency (shared power-diagram edges), so
// re-detecting contacts from triangle meshes is both wasteful and lossy: the
// MeshContactDetector splinters 40 stones into ~125 sub-interfaces / ~612
// contact vertices (every mesh face pair clips separately), inflating and
// ill-conditioning the equilibrium QP, and it depends on distance/angle
// tolerances that break on curved surfaces.
//
// This assembler instead emits ONE exact planar-quad interface per adjacent
// stone pair, directly from the shared (u,v) edge:
//
//     contact quad = [ F1, F2, B2, B1 ]
//     F_k = map(edge endpoint k)            (front rail, on the surface)
//     B_k = F_k + nrm(edge endpoint k)·d    (back rail, per-vertex normal)
//
// Because stones are extruded PER-VERTEX along the surface normal (the
// 2026-06-10 double-curved fix), both stones build their side walls from the
// SAME two rails — the quad is exactly the shared face on any curvature, with
// zero tolerance dependence. Normal = in-plane quad normal oriented from cell
// A toward cell B; (n, t1, t2) right-handed.
//
// Output = a ready-to-verify MasonryAssembly (+ the stone vertex/triangle
// buffers for baking), with the ground course fixed as boundary conditions.
// =============================================================================

/// <summary>Result of <see cref="PolygonalWallAssembler.Build"/>.</summary>
public sealed class WallAssembly
{
    public WallAssembly(MasonryAssembly assembly,
                        IReadOnlyList<IReadOnlyList<double>> stoneCoords,
                        IReadOnlyList<IReadOnlyList<int>> stoneTris,
                        IReadOnlyList<string> stoneIds)
    {
        Assembly = assembly; StoneCoords = stoneCoords; StoneTris = stoneTris; StoneIds = stoneIds;
    }
    /// <summary>Blocks + exact joint interfaces + ground-course boundary conditions.</summary>
    public MasonryAssembly Assembly { get; }
    public IReadOnlyList<IReadOnlyList<double>> StoneCoords { get; }
    public IReadOnlyList<IReadOnlyList<int>> StoneTris { get; }
    public IReadOnlyList<string> StoneIds { get; }
}

/// <summary>
/// Builds watertight prism stones AND their exact joint interfaces from a
/// <see cref="WallGenResult"/>, mapped onto a surface via caller delegates.
/// Pure managed; no Rhino dependency (delegates return [x,y,z] triples).
/// </summary>
public static class PolygonalWallAssembler
{
    /// <summary>
    /// Assemble the wall. <paramref name="map"/> maps (u,v) to a world point
    /// [x,y,z]; <paramref name="nrm"/> returns the unit surface normal [nx,ny,nz]
    /// at (u,v). For a flat XZ panel pass map=(u,v)=>[u,0,v], nrm=(u,v)=>[0,1,0].
    /// </summary>
    public static WallAssembly Build(
        WallGenResult gen,
        Func<double, double, double[]> map,
        Func<double, double, double[]> nrm,
        double depth = 0.25,
        double density = 2400.0,
        double fixBelowZ = 0.02)
    {
        if (gen == null) throw new ArgumentNullException(nameof(gen));
        if (map == null) throw new ArgumentNullException(nameof(map));
        if (nrm == null) throw new ArgumentNullException(nameof(nrm));
        if (!(depth > 0)) throw new ArgumentOutOfRangeException(nameof(depth));
        if (!(density > 0)) throw new ArgumentOutOfRangeException(nameof(density));

        int nCells = gen.Cells.Count;
        var ids = new List<string>(nCells);
        var coordsList = new List<IReadOnlyList<double>>(nCells);
        var trisList = new List<IReadOnlyList<int>>(nCells);
        var blocks = new List<MasonryBlock>(nCells);
        var centroids3 = new double[nCells][];

        // ---- domain scale for quantised edge matching ----
        double domain = 0;
        for (int ci = 0; ci < nCells; ci++)
        {
            var c = gen.Cells[ci];
            for (int k = 0; k < c.VertexCount; k++)
            {
                if (Math.Abs(c.Us[k]) > domain) domain = Math.Abs(c.Us[k]);
                if (Math.Abs(c.Vs[k]) > domain) domain = Math.Abs(c.Vs[k]);
            }
        }
        double qs = Math.Max(domain, 1.0) * 1e-7;

        // ---- stones (per-vertex-normal prisms, ngon-equivalent fans, outward) ----
        double globalMinZ = double.MaxValue;
        var minZ = new double[nCells];
        for (int ci = 0; ci < nCells; ci++)
        {
            var cell = gen.Cells[ci];
            int m = cell.VertexCount;
            var coords = new List<double>(m * 6 + 6);
            var f = new double[m][];
            var b = new double[m][];
            for (int k = 0; k < m; k++)
            {
                var fp = map(cell.Us[k], cell.Vs[k]);
                var nv = nrm(cell.Us[k], cell.Vs[k]);
                f[k] = fp;
                b[k] = new[] { fp[0] + nv[0] * depth, fp[1] + nv[1] * depth, fp[2] + nv[2] * depth };
            }
            foreach (var p in f) { coords.Add(p[0]); coords.Add(p[1]); coords.Add(p[2]); }
            foreach (var p in b) { coords.Add(p[0]); coords.Add(p[1]); coords.Add(p[2]); }
            // cap centres keep the fans well-shaped on warped cells
            var cf = Mean(f); var cb = Mean(b);
            coords.Add(cf[0]); coords.Add(cf[1]); coords.Add(cf[2]);
            coords.Add(cb[0]); coords.Add(cb[1]); coords.Add(cb[2]);
            int ci2 = 2 * m, cbi = 2 * m + 1;
            var tris = new List<int>(m * 12);
            for (int k = 0; k < m; k++)
            {
                int a2 = k, b2 = (k + 1) % m;
                tris.Add(ci2); tris.Add(a2); tris.Add(b2);            // front fan
                tris.Add(cbi); tris.Add(m + b2); tris.Add(m + a2);    // back fan (reversed)
                tris.Add(a2); tris.Add(m + a2); tris.Add(m + b2);     // side quad
                tris.Add(a2); tris.Add(m + b2); tris.Add(b2);
            }
            // outward orientation: flip everything if the signed volume is negative
            if (SignedVolume(coords, tris) < 0)
            {
                for (int t = 0; t < tris.Count; t += 3)
                { int tmp = tris[t + 1]; tris[t + 1] = tris[t + 2]; tris[t + 2] = tmp; }
            }

            string id = "stone_" + ci.ToString("000");
            ids.Add(id);
            coordsList.Add(coords);
            trisList.Add(tris);
            blocks.Add(new MasonryBlock(id, coords, tris, density));
            centroids3[ci] = Mean(f);
            double mz = double.MaxValue;
            for (int k = 2; k < coords.Count; k += 3) if (coords[k] < mz) mz = coords[k];
            minZ[ci] = mz;
            if (mz < globalMinZ) globalMinZ = mz;
        }

        // ---- exact joints from shared diagram edges ----
        // key = quantised sorted endpoint pair -> (cell, endpoint uv pair)
        var edgeOwner = new Dictionary<long, int>();
        var edgeUv = new Dictionary<long, double[]>(); // [u1,v1,u2,v2]
        var interfaces = new List<MasonryInterface>();
        for (int ci = 0; ci < nCells; ci++)
        {
            var cell = gen.Cells[ci];
            int m = cell.VertexCount;
            for (int k = 0; k < m; k++)
            {
                double u1 = cell.Us[k], v1 = cell.Vs[k];
                double u2 = cell.Us[(k + 1) % m], v2 = cell.Vs[(k + 1) % m];
                long k1 = Key(u1, v1, qs), k2 = Key(u2, v2, qs);
                if (k1 == k2) continue; // degenerate
                long ek = k1 < k2 ? unchecked(k1 * 1000003L) ^ k2 : unchecked(k2 * 1000003L) ^ k1;
                if (edgeOwner.TryGetValue(ek, out int other))
                {
                    if (other == ci) continue;
                    var uv = edgeUv[ek];
                    var iface = MakeInterface(other, ci, uv, map, nrm, depth, centroids3, ids);
                    if (iface != null) interfaces.Add(iface);
                }
                else
                {
                    edgeOwner[ek] = ci;
                    edgeUv[ek] = new[] { u1, v1, u2, v2 };
                }
            }
        }

        // ---- boundary conditions: ground course fixed ----
        var fixedIds = new List<string>();
        for (int ci = 0; ci < nCells; ci++)
            if (minZ[ci] <= globalMinZ + fixBelowZ) fixedIds.Add(ids[ci]);

        var assembly = new MasonryAssembly(blocks, interfaces, new BoundaryConditions(fixedIds));
        return new WallAssembly(assembly, coordsList, trisList, ids);
    }

    private static MasonryInterface MakeInterface(
        int cellA, int cellB, double[] uv,
        Func<double, double, double[]> map, Func<double, double, double[]> nrm,
        double depth, double[][] centroids3, List<string> ids)
    {
        var f1 = map(uv[0], uv[1]); var f2 = map(uv[2], uv[3]);
        var n1 = nrm(uv[0], uv[1]); var n2 = nrm(uv[2], uv[3]);
        var b1 = new[] { f1[0] + n1[0] * depth, f1[1] + n1[1] * depth, f1[2] + n1[2] * depth };
        var b2 = new[] { f2[0] + n2[0] * depth, f2[1] + n2[1] * depth, f2[2] + n2[2] * depth };

        // quad normal: edge x (average extrusion direction), oriented A -> B
        var e = Sub(f2, f1);
        var ext = new[] { (n1[0] + n2[0]) * 0.5, (n1[1] + n2[1]) * 0.5, (n1[2] + n2[2]) * 0.5 };
        var n = Cross(e, ext);
        double nl = Norm(n);
        if (nl < 1e-12) return null; // degenerate edge
        n[0] /= nl; n[1] /= nl; n[2] /= nl;
        var ab = Sub(centroids3[cellB], centroids3[cellA]);
        if (Dot(n, ab) < 0) { n[0] = -n[0]; n[1] = -n[1]; n[2] = -n[2]; }

        var t1 = Sub(f2, f1);
        double t1l = Norm(t1);
        if (t1l < 1e-12) return null;
        t1[0] /= t1l; t1[1] /= t1l; t1[2] /= t1l;
        var t2 = Cross(n, t1); // right-handed (n, t1, t2)

        var poly = new List<ContactVertex>
        {
            new ContactVertex(f1[0], f1[1], f1[2]),
            new ContactVertex(f2[0], f2[1], f2[2]),
            new ContactVertex(b2[0], b2[1], b2[2]),
            new ContactVertex(b1[0], b1[1], b1[2]),
        };
        return new MasonryInterface(ids[cellA], ids[cellB], poly,
            n[0], n[1], n[2], t1[0], t1[1], t1[2], t2[0], t2[1], t2[2]);
    }

    private static long Key(double u, double v, double qs)
        => unchecked((long)Math.Round(u / qs) * 73856093L ^ (long)Math.Round(v / qs) * 19349663L);

    private static double[] Mean(double[][] pts)
    {
        var r = new double[3];
        for (int i = 0; i < pts.Length; i++) { r[0] += pts[i][0]; r[1] += pts[i][1]; r[2] += pts[i][2]; }
        r[0] /= pts.Length; r[1] /= pts.Length; r[2] /= pts.Length;
        return r;
    }

    private static double SignedVolume(List<double> coords, List<int> tris)
    {
        double vol = 0;
        for (int t = 0; t < tris.Count; t += 3)
        {
            int a = tris[t] * 3, b = tris[t + 1] * 3, c = tris[t + 2] * 3;
            vol += coords[a] * (coords[b + 1] * coords[c + 2] - coords[b + 2] * coords[c + 1])
                 - coords[a + 1] * (coords[b] * coords[c + 2] - coords[b + 2] * coords[c])
                 + coords[a + 2] * (coords[b] * coords[c + 1] - coords[b + 1] * coords[c]);
        }
        return vol / 6.0;
    }

    private static double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
    private static double[] Cross(double[] a, double[] b) => new[]
    { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    private static double Norm(double[] a) => Math.Sqrt(Dot(a, a));
}
