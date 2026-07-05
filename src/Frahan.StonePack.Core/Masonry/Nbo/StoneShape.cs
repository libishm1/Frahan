#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;
using Frahan.Masonry.Quarry;        // ConvexHullBuilder
using Frahan.Masonry.Cutting;       // Slab
using Frahan.Core.Discontinuity;    // CloudMath.SymEig3 (internal, same assembly)

namespace Frahan.Masonry.Nbo;

// =============================================================================
// StoneShape / StoneShapeAnalyzer -- the 3D shape descriptor that the
// Next-Best-Object dry-stone planner reasons over. It is the 3D analog of the
// shipped 2D whole-side matcher (min-area rect -> 4 corner-to-corner sides ->
// exclude flat border sides -> match side shape): here the convex HULL plays
// the role of the bounding rect, its DOMINANT FACETS play the role of the
// "sides", and the STABLE resting faces play the role of the flat-side test.
//
// Two validated primitives (NBO_3D_DESIGN.md, on real ETH1100 stone 0007):
//   A. Dominant facets -- a stone hull is rounded (hundreds of micro-triangles),
//      so region-grow edge-adjacent hull triangles within ~20 deg into a small
//      set of merged near-planar facets, keep those >= 4% of hull area. Stone
//      0007 -> 3 dominant facets (20/17/8% of hull area). These are the few
//      candidate resting/contact faces a matcher needs.
//   B. Stable resting faces -- a facet is a *stable* rest iff the stone's centre
//      of mass projects (along the facet normal) inside the facet polygon. This
//      is the convex-hull stable-pose criterion (Goldberg & Mirtich 1999, "On
//      the existence of solutions to stable pose problems" / pose statistics):
//      a polyhedron rests on a hull face and is stable iff CoM-over-face.
//   C. Principal axes -- PCA of the hull vertices. The LONGEST axis is the
//      dry-stone "lay the length into the wall" direction (ETH/Johns 2020); the
//      section perpendicular to it is the smallest cross-section = the clean rim
//      profile. Eigensolver reused from the discontinuity worker (CloudMath).
//
// Rhino-bound (Mesh/Point3d/Vector3d), consistent with its Masonry siblings
// (RubbleWallSettle). Deterministic: region grow seeds by descending area with
// index tie-breaks; no RNG. Heavy validation (point-of-10 guards) on inputs.
// =============================================================================

/// <summary>One merged near-planar facet of the convex hull (a candidate
/// resting / contact face -- the 3D analog of a 2D "side").</summary>
public sealed class DominantFace
{
    /// <summary>Area-weighted outward unit normal (points away from the stone).</summary>
    public Vector3d Normal;
    /// <summary>Area-weighted centroid of the member triangles.</summary>
    public Point3d Centroid;
    /// <summary>Total facet area.</summary>
    public double Area;
    /// <summary>Area / total hull area (the "dominance" of this facet).</summary>
    public double AreaFraction;
    /// <summary>Member hull-triangle indices (into the hull mesh).</summary>
    public List<int> TriangleIds = new List<int>();
    /// <summary>True iff the stone's CoM projects inside this facet -> a stable rest.</summary>
    public bool IsStableRest;
    /// <summary>Signed CoM-over-face margin, normalized by sqrt(Area): >0 inside
    /// (stable), &lt;0 outside (would topple). The bigger, the more stable.</summary>
    public double ComMargin;
}

/// <summary>Convex-hull shape descriptor of one stone: dominant facets, stable
/// resting faces, principal axes and CoM. Produced by <see cref="StoneShapeAnalyzer"/>.</summary>
public sealed class StoneShape
{
    /// <summary>The convex hull as a triangulated Rhino mesh (faces CCW outward).</summary>
    public Mesh Hull;
    /// <summary>Volume centroid (centre of mass for a homogeneous stone) of the hull.</summary>
    public Point3d Com;
    /// <summary>Total hull surface area.</summary>
    public double HullArea;
    /// <summary>Hull volume.</summary>
    public double HullVolume;
    /// <summary>Longest principal axis (e1) -- the "length into the wall" direction.</summary>
    public Vector3d AxisLong;
    /// <summary>Middle principal axis (e2).</summary>
    public Vector3d AxisMid;
    /// <summary>Shortest principal axis (e3) -- the flatness / broad-face normal.</summary>
    public Vector3d AxisThin;
    /// <summary>Full span of the hull along (thin, mid, long) = (e3, e2, e1).</summary>
    public double[] Extents = new double[3];
    /// <summary>All dominant facets, ordered by descending area.</summary>
    public List<DominantFace> DominantFaces = new List<DominantFace>();
    /// <summary>Subset of <see cref="DominantFaces"/> that are stable rests, descending area.</summary>
    public List<DominantFace> StableFaces = new List<DominantFace>();

    /// <summary>Long-axis span / thin-axis span -- the elongation of the stone.</summary>
    public double Elongation => Extents[0] > 1e-12 ? Extents[2] / Extents[0] : 1.0;
}

public static class StoneShapeAnalyzer
{
    /// <summary>Merge hull triangles whose normals are within ~20 deg (dot &gt;= 0.94).</summary>
    public const double DefaultMergeDot = 0.94;
    /// <summary>Keep facets that are at least 4% of the hull area.</summary>
    public const double DefaultMinAreaFraction = 0.04;

    /// <summary>
    /// Analyze a stone mesh into its convex-hull shape descriptor (dominant
    /// facets, stable resting faces, principal axes, CoM).
    /// </summary>
    public static StoneShape Analyze(
        Mesh stone,
        double mergeDot = DefaultMergeDot,
        double minAreaFraction = DefaultMinAreaFraction)
    {
        if (stone == null) throw new ArgumentNullException(nameof(stone));
        if (stone.Vertices.Count < 4)
            throw new ArgumentException($"stone needs >= 4 vertices (got {stone.Vertices.Count})", nameof(stone));
        if (mergeDot < -1.0 || mergeDot > 1.0) throw new ArgumentOutOfRangeException(nameof(mergeDot));
        if (minAreaFraction < 0.0 || minAreaFraction > 1.0) throw new ArgumentOutOfRangeException(nameof(minAreaFraction));

        // ---- 1. convex hull (reuse the quarry incremental hull) -------------
        var pts = new List<double>(stone.Vertices.Count * 3);
        foreach (var v in stone.Vertices)
        {
            pts.Add(v.X); pts.Add(v.Y); pts.Add(v.Z);
        }
        Slab slab = ConvexHullBuilder.BuildSlab(pts);
        Mesh hull = SlabToMesh(slab);
        hull.RebuildNormals();

        var shape = new StoneShape { Hull = hull };

        // ---- 2. CoM + volume (exact for the closed hull) --------------------
        var vmp = VolumeMassProperties.Compute(hull);
        shape.Com = vmp != null ? vmp.Centroid : CentroidOfVertices(hull);
        shape.HullVolume = vmp != null ? vmp.Volume : 0.0;

        // ---- 3. per-triangle normal / area / centroid -----------------------
        Point3d hullCtr = CentroidOfVertices(hull);
        int tc = hull.Faces.Count;
        var triN = new Vector3d[tc];
        var triA = new double[tc];
        var triC = new Point3d[tc];
        double hullArea = 0.0;
        for (int f = 0; f < tc; f++)
        {
            var face = hull.Faces[f];
            Point3d a = hull.Vertices[face.A], b = hull.Vertices[face.B], c = hull.Vertices[face.C];
            triC[f] = new Point3d((a.X + b.X + c.X) / 3.0, (a.Y + b.Y + c.Y) / 3.0, (a.Z + b.Z + c.Z) / 3.0);
            Vector3d cross = Vector3d.CrossProduct(b - a, c - a);
            double len = cross.Length;
            triA[f] = 0.5 * len;
            Vector3d nrm = len > 1e-20 ? cross / len : Vector3d.ZAxis;
            // force outward (convex hull): the normal must point away from the interior centroid.
            if ((triC[f] - hullCtr) * nrm < 0.0) nrm = -nrm;
            triN[f] = nrm;
            hullArea += triA[f];
        }
        shape.HullArea = hullArea;

        // ---- 4. PCA of the hull vertices (reuse CloudMath.SymEig3) -----------
        PrincipalAxes(hull, out Vector3d eThin, out Vector3d eMid, out Vector3d eLong, out double[] extents);
        shape.AxisThin = eThin; shape.AxisMid = eMid; shape.AxisLong = eLong; shape.Extents = extents;

        // ---- 5. region-grow dominant facets ---------------------------------
        var facets = RegionGrowFacets(hull, triN, triA, triC, mergeDot, minAreaFraction * hullArea);

        // ---- 6. stable-rest test (CoM over facet, Goldberg-Mirtich) ---------
        foreach (var df in facets)
        {
            df.AreaFraction = hullArea > 1e-20 ? df.Area / hullArea : 0.0;
            ComOverFace(hull, df, shape.Com, out bool inside, out double margin);
            df.IsStableRest = inside;
            df.ComMargin = (inside ? 1.0 : -1.0) * (margin / Math.Max(1e-9, Math.Sqrt(df.Area)));
        }
        facets.Sort((x, y) => y.Area.CompareTo(x.Area));
        shape.DominantFaces = facets;
        foreach (var df in facets) if (df.IsStableRest) shape.StableFaces.Add(df);

        return shape;
    }

    /// <summary>The best resting facet: the LARGEST-area stable face (lay the
    /// stone on its broadest stable face -> lowest CoM, widest support -> the
    /// 12/12-stable backbone from the 3-course study), with CoM margin as the
    /// tie-break. Null only if the hull degenerates to no facets.</summary>
    public static DominantFace BestRestingFace(StoneShape shape)
    {
        if (shape == null) throw new ArgumentNullException(nameof(shape));
        DominantFace best = null;
        foreach (var df in shape.StableFaces)
            if (best == null || df.Area > best.Area ||
                (df.Area == best.Area && df.ComMargin > best.ComMargin))
                best = df;
        // Fall back to the largest facet if none qualifies as stable.
        if (best == null && shape.DominantFaces.Count > 0) best = shape.DominantFaces[0];
        return best;
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static Mesh SlabToMesh(Slab slab)
    {
        var m = new Mesh();
        var v = slab.VertexCoordsXyz;
        for (int i = 0; i + 2 < v.Count; i += 3)
            m.Vertices.Add(v[i], v[i + 1], v[i + 2]);
        foreach (var face in slab.Faces)
        {
            if (face.Count == 3) m.Faces.AddFace(face[0], face[1], face[2]);
            else if (face.Count == 4) m.Faces.AddFace(face[0], face[1], face[2], face[3]);
            else // fan-triangulate any larger polygon
                for (int k = 1; k + 1 < face.Count; k++) m.Faces.AddFace(face[0], face[k], face[k + 1]);
        }
        m.Compact();
        return m;
    }

    private static Point3d CentroidOfVertices(Mesh m)
    {
        double sx = 0, sy = 0, sz = 0; int n = m.Vertices.Count;
        for (int i = 0; i < n; i++) { var p = m.Vertices[i]; sx += p.X; sy += p.Y; sz += p.Z; }
        return n > 0 ? new Point3d(sx / n, sy / n, sz / n) : Point3d.Origin;
    }

    private static void PrincipalAxes(Mesh hull, out Vector3d eThin, out Vector3d eMid, out Vector3d eLong, out double[] extents)
    {
        int n = hull.Vertices.Count;
        double cx = 0, cy = 0, cz = 0;
        for (int i = 0; i < n; i++) { var p = hull.Vertices[i]; cx += p.X; cy += p.Y; cz += p.Z; }
        cx /= n; cy /= n; cz /= n;
        double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        for (int i = 0; i < n; i++)
        {
            var p = hull.Vertices[i];
            double dx = p.X - cx, dy = p.Y - cy, dz = p.Z - cz;
            xx += dx * dx; xy += dx * dy; xz += dx * dz; yy += dy * dy; yz += dy * dz; zz += dz * dz;
        }
        // eigenvalues ascending: vec[0]=thin(e3), vec[1]=mid(e2), vec[2]=long(e1)
        CloudMath.SymEig3(xx, xy, xz, yy, yz, zz, out _, out Vector3d[] vec);
        eThin = vec[0]; eMid = vec[1]; eLong = vec[2];
        extents = new double[3];
        extents[0] = Span(hull, eThin);
        extents[1] = Span(hull, eMid);
        extents[2] = Span(hull, eLong);
    }

    private static double Span(Mesh hull, Vector3d axis)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        for (int i = 0; i < hull.Vertices.Count; i++)
        {
            var p = hull.Vertices[i];
            double t = p.X * axis.X + p.Y * axis.Y + p.Z * axis.Z;
            if (t < lo) lo = t; if (t > hi) hi = t;
        }
        return hi - lo;
    }

    private static List<DominantFace> RegionGrowFacets(
        Mesh hull, Vector3d[] triN, double[] triA, Point3d[] triC, double mergeDot, double minArea)
    {
        int tc = hull.Faces.Count;

        // edge -> the (up to two) triangles sharing it, for adjacency.
        var edgeTris = new Dictionary<long, List<int>>();
        for (int f = 0; f < tc; f++)
        {
            var face = hull.Faces[f];
            AddEdge(edgeTris, face.A, face.B, f);
            AddEdge(edgeTris, face.B, face.C, f);
            AddEdge(edgeTris, face.C, face.A, f);
        }

        // seed order: descending triangle area, index tie-break (deterministic).
        var order = new int[tc];
        for (int i = 0; i < tc; i++) order[i] = i;
        Array.Sort(order, (x, y) =>
        {
            int c = triA[y].CompareTo(triA[x]);
            return c != 0 ? c : x.CompareTo(y);
        });

        var assigned = new int[tc];
        for (int i = 0; i < tc; i++) assigned[i] = -1;
        var facets = new List<DominantFace>();

        foreach (int seed in order)
        {
            if (assigned[seed] >= 0) continue;
            int fid = facets.Count;
            var df = new DominantFace();
            var queue = new Queue<int>();
            assigned[seed] = fid; queue.Enqueue(seed);
            Vector3d seedN = triN[seed];
            while (queue.Count > 0)
            {
                int t = queue.Dequeue();
                df.TriangleIds.Add(t);
                foreach (int nb in Neighbours(hull, edgeTris, t))
                {
                    if (assigned[nb] >= 0) continue;
                    if (triN[nb] * seedN >= mergeDot)
                    {
                        assigned[nb] = fid; queue.Enqueue(nb);
                    }
                }
            }
            // aggregate (area-weighted normal + centroid)
            Vector3d nAcc = Vector3d.Zero; double aAcc = 0; double sx = 0, sy = 0, sz = 0;
            df.TriangleIds.Sort();
            foreach (int t in df.TriangleIds)
            {
                nAcc += triN[t] * triA[t]; aAcc += triA[t];
                sx += triC[t].X * triA[t]; sy += triC[t].Y * triA[t]; sz += triC[t].Z * triA[t];
            }
            df.Area = aAcc;
            if (nAcc.Length > 1e-20) { nAcc.Unitize(); df.Normal = nAcc; } else df.Normal = seedN;
            df.Centroid = aAcc > 1e-20 ? new Point3d(sx / aAcc, sy / aAcc, sz / aAcc) : triC[seed];
            facets.Add(df);
        }

        // keep only dominant facets (>= minArea); tiny micro-facets are noise.
        var kept = new List<DominantFace>();
        foreach (var df in facets) if (df.Area >= minArea) kept.Add(df);
        return kept;
    }

    private static void AddEdge(Dictionary<long, List<int>> edgeTris, int u, int v, int tri)
    {
        long key = u < v ? ((long)u << 32) | (uint)v : ((long)v << 32) | (uint)u;
        if (!edgeTris.TryGetValue(key, out var list)) { list = new List<int>(2); edgeTris[key] = list; }
        list.Add(tri);
    }

    private static IEnumerable<int> Neighbours(Mesh hull, Dictionary<long, List<int>> edgeTris, int tri)
    {
        var face = hull.Faces[tri];
        foreach (var nb in EdgeNbrs(edgeTris, face.A, face.B, tri)) yield return nb;
        foreach (var nb in EdgeNbrs(edgeTris, face.B, face.C, tri)) yield return nb;
        foreach (var nb in EdgeNbrs(edgeTris, face.C, face.A, tri)) yield return nb;
    }

    private static IEnumerable<int> EdgeNbrs(Dictionary<long, List<int>> edgeTris, int u, int v, int self)
    {
        long key = u < v ? ((long)u << 32) | (uint)v : ((long)v << 32) | (uint)u;
        if (edgeTris.TryGetValue(key, out var list))
            foreach (int t in list) if (t != self) yield return t;
    }

    // CoM projected along the facet normal -- inside the facet polygon? (the
    // convex union of its coplanar triangles). margin = distance to the nearest
    // facet boundary edge (>=0; caller signs it by `inside`).
    private static void ComOverFace(Mesh hull, DominantFace df, Point3d com, out bool inside, out double margin)
    {
        inside = false;
        margin = double.MaxValue;
        // project CoM onto the facet plane along the facet normal.
        Vector3d d = com - df.Centroid;
        Point3d proj = com - df.Normal * (d * df.Normal);

        foreach (int t in df.TriangleIds)
        {
            var face = hull.Faces[t];
            Point3d a = hull.Vertices[face.A], b = hull.Vertices[face.B], c = hull.Vertices[face.C];
            if (PointInTriangle(proj, a, b, c, df.Normal)) { inside = true; break; }
        }

        // facet boundary edges = edges used by exactly one member triangle.
        var edgeCount = new Dictionary<long, int>();
        var edgeVerts = new Dictionary<long, (int u, int v)>();
        foreach (int t in df.TriangleIds)
        {
            var face = hull.Faces[t];
            Tally(edgeCount, edgeVerts, face.A, face.B);
            Tally(edgeCount, edgeVerts, face.B, face.C);
            Tally(edgeCount, edgeVerts, face.C, face.A);
        }
        foreach (var kv in edgeCount)
        {
            if (kv.Value != 1) continue; // interior edge
            var (u, v) = edgeVerts[kv.Key];
            double dd = DistPointSeg(proj, hull.Vertices[u], hull.Vertices[v]);
            if (dd < margin) margin = dd;
        }
        if (margin == double.MaxValue) margin = 0.0;
    }

    private static void Tally(Dictionary<long, int> count, Dictionary<long, (int, int)> verts, int u, int v)
    {
        long key = u < v ? ((long)u << 32) | (uint)v : ((long)v << 32) | (uint)u;
        count[key] = count.TryGetValue(key, out int c) ? c + 1 : 1;
        if (!verts.ContainsKey(key)) verts[key] = u < v ? (u, v) : (v, u);
    }

    private static bool PointInTriangle(Point3d p, Point3d a, Point3d b, Point3d c, Vector3d n)
    {
        // same-side test in 3D using the facet normal as orientation reference.
        double s1 = Vector3d.CrossProduct(b - a, p - a) * n;
        double s2 = Vector3d.CrossProduct(c - b, p - b) * n;
        double s3 = Vector3d.CrossProduct(a - c, p - c) * n;
        const double e = -1e-9;
        return (s1 >= e && s2 >= e && s3 >= e) || (s1 <= -e && s2 <= -e && s3 <= -e);
    }

    private static double DistPointSeg(Point3d p, Point3d a, Point3d b)
    {
        Vector3d ab = b - a, ap = p - a;
        double t = ab.SquareLength > 1e-20 ? (ap * ab) / ab.SquareLength : 0.0;
        t = Math.Max(0.0, Math.Min(1.0, t));
        Point3d proj = a + ab * t;
        return p.DistanceTo(proj);
    }
}
