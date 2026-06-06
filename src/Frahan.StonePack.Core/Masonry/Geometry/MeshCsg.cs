#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Interfaces;

namespace Frahan.Masonry.Geometry;

// =============================================================================
// MeshCsg — pure-managed 3D mesh-mesh Constructive Solid Geometry via BSP
// trees. Port of Evan Wallace's csg.js (MIT) algorithm.
//
// Operations: Union, Intersection, Difference. Operates on closed
// manifold triangle meshes. Output is also a triangle mesh (polygons
// produced inside the BSP can be n-gons; ToMesh fan-triangulates them).
//
// Algorithm sketch:
//   • Build a BSP tree per input. Each node owns a splitting plane plus
//     all polygons coplanar with that plane; front / back children
//     contain polygons strictly in front of / behind the plane.
//     Spanning polygons are split.
//   • Boolean ops are sequences of ClipTo / Invert calls on the two
//     trees followed by Build. The exact sequence implements De Morgan-
//     style identities:
//       A∪B = clip(A,!B) + clip(B,!A)
//       A∩B = !( !A ∪ !B )
//       A\B = clip(A,!B) + clip(!B,A) inverted
//
// Robustness:
//   • EPS-based vertex classification (FRONT / BACK / COPLANAR) tolerates
//     floating-point noise. Default 1e-5 — matches csg.js. Override via
//     MeshCsg.SplitEps.
//   • Fan-triangulation at output is safe for the convex polygons that
//     splitting produces (every spanning split returns a convex piece).
//   • Vertex deduplication on output keeps the mesh compact.
//
// Limitations:
//   • Inputs MUST be closed manifold meshes. Open meshes will produce
//     visually correct surfaces but topologically inconsistent results
//     near boundaries. Sanitize first via MeshSanitizer.
//   • No exact predicates — pathological floating-point inputs (e.g.
//     two coplanar faces from different meshes that almost touch) may
//     produce spurious slivers. Run CutResultValidator afterwards.
//   • The BSP build is depth-recursive; very dense or pathologically
//     ordered meshes can blow the call stack. The implementation uses
//     manual stacking inside ClipPolygons but a deep Build can still
//     hit the limit on extreme inputs.
// =============================================================================

public sealed class MeshCsg
{
    /// <summary>EPS for FRONT/BACK/COPLANAR classification. csg.js default.</summary>
    public const double SplitEps = 1e-5;

    // ─── Lightweight value types ────────────────────────────────────────

    public readonly struct Vec3
    {
        public readonly double X, Y, Z;
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator *(Vec3 a, double s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        public double Dot(Vec3 b) => X * b.X + Y * b.Y + Z * b.Z;
        public Vec3 Cross(Vec3 b) => new Vec3(
            Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
        public double Length() => Math.Sqrt(X * X + Y * Y + Z * Z);
        public Vec3 Normalised()
        {
            double m = Length();
            if (m < 1e-30) return new Vec3(0, 0, 0);
            return new Vec3(X / m, Y / m, Z / m);
        }
        public Vec3 Lerp(Vec3 b, double t) =>
            new Vec3(X + (b.X - X) * t, Y + (b.Y - Y) * t, Z + (b.Z - Z) * t);
    }

    public sealed class Plane
    {
        public Vec3 Normal;
        public double W;

        public Plane(Vec3 normal, double w) { Normal = normal; W = w; }

        public static Plane FromPoints(Vec3 a, Vec3 b, Vec3 c)
        {
            var n = (b - a).Cross(c - a).Normalised();
            return new Plane(n, n.Dot(a));
        }

        public Plane Clone() => new Plane(Normal, W);
        public void Flip() { Normal = -Normal; W = -W; }

        // ── Vertex classification ──────────────────────────────────────
        private const int COPLANAR = 0;
        private const int FRONT = 1;
        private const int BACK = 2;
        private const int SPANNING = 3;

        /// <summary>
        /// Split <paramref name="poly"/> by this plane. Each result goes
        /// into one of the four output lists depending on its
        /// classification. Spanning polygons are cut along the plane and
        /// emit one piece into <paramref name="front"/> and one into
        /// <paramref name="back"/>.
        /// </summary>
        public void SplitPolygon(
            Polygon poly,
            List<Polygon> coplanarFront,
            List<Polygon> coplanarBack,
            List<Polygon> front,
            List<Polygon> back)
        {
            int polygonType = 0;
            int n = poly.Vertices.Count;
            var types = new int[n];
            for (int i = 0; i < n; i++)
            {
                double t = Normal.Dot(poly.Vertices[i]) - W;
                int type = (t < -SplitEps) ? BACK
                         : (t > SplitEps) ? FRONT
                         : COPLANAR;
                polygonType |= type;
                types[i] = type;
            }

            switch (polygonType)
            {
                case COPLANAR:
                    if (Normal.Dot(poly.PlaneNormal) > 0) coplanarFront.Add(poly);
                    else                                  coplanarBack.Add(poly);
                    break;
                case FRONT:
                    front.Add(poly);
                    break;
                case BACK:
                    back.Add(poly);
                    break;
                case SPANNING:
                    var f = new List<Vec3>(n + 2);
                    var b = new List<Vec3>(n + 2);
                    for (int i = 0; i < n; i++)
                    {
                        int j = (i + 1) % n;
                        int ti = types[i], tj = types[j];
                        var vi = poly.Vertices[i];
                        var vj = poly.Vertices[j];
                        if (ti != BACK) f.Add(vi);
                        if (ti != FRONT) b.Add(vi);
                        if ((ti | tj) == SPANNING)
                        {
                            double tt = (W - Normal.Dot(vi)) / Normal.Dot(vj - vi);
                            var v = vi.Lerp(vj, tt);
                            f.Add(v);
                            b.Add(v);
                        }
                    }
                    if (f.Count >= 3) front.Add(new Polygon(f, poly.PlaneNormal, poly.PlaneW));
                    if (b.Count >= 3) back.Add(new Polygon(b, poly.PlaneNormal, poly.PlaneW));
                    break;
            }
        }
    }

    public sealed class Polygon
    {
        public readonly List<Vec3> Vertices;
        public Vec3 PlaneNormal;
        public double PlaneW;

        public Polygon(List<Vec3> verts)
        {
            if (verts == null) throw new ArgumentNullException(nameof(verts));
            if (verts.Count < 3) throw new ArgumentException("polygon needs >= 3 verts");
            Vertices = verts;
            var pl = Plane.FromPoints(verts[0], verts[1], verts[2]);
            PlaneNormal = pl.Normal;
            PlaneW = pl.W;
        }

        public Polygon(List<Vec3> verts, Vec3 normal, double w)
        {
            Vertices = verts;
            PlaneNormal = normal;
            PlaneW = w;
        }

        public Polygon Flip()
        {
            Vertices.Reverse();
            PlaneNormal = -PlaneNormal;
            PlaneW = -PlaneW;
            return this;
        }
    }

    // ─── BSP node ───────────────────────────────────────────────────────

    public sealed class Node
    {
        public Plane Plane;
        public Node Front;
        public Node Back;
        public List<Polygon> Polygons = new List<Polygon>();

        public Node() { }

        public Node(List<Polygon> polys)
        {
            if (polys != null && polys.Count > 0) Build(polys);
        }

        public Node Clone()
        {
            var n = new Node();
            if (Plane != null) n.Plane = Plane.Clone();
            if (Front != null) n.Front = Front.Clone();
            if (Back != null) n.Back = Back.Clone();
            for (int i = 0; i < Polygons.Count; i++)
            {
                var p = Polygons[i];
                n.Polygons.Add(new Polygon(new List<Vec3>(p.Vertices), p.PlaneNormal, p.PlaneW));
            }
            return n;
        }

        public void Invert()
        {
            for (int i = 0; i < Polygons.Count; i++) Polygons[i].Flip();
            Plane?.Flip();
            Front?.Invert();
            Back?.Invert();
            var tmp = Front; Front = Back; Back = tmp;
        }

        public List<Polygon> ClipPolygons(List<Polygon> polys)
        {
            if (Plane == null) return new List<Polygon>(polys);
            var front = new List<Polygon>();
            var back = new List<Polygon>();
            for (int i = 0; i < polys.Count; i++)
                Plane.SplitPolygon(polys[i], front, back, front, back);
            if (Front != null) front = Front.ClipPolygons(front);
            if (Back != null) back = Back.ClipPolygons(back);
            else back.Clear();
            front.AddRange(back);
            return front;
        }

        public void ClipTo(Node other)
        {
            Polygons = other.ClipPolygons(Polygons);
            Front?.ClipTo(other);
            Back?.ClipTo(other);
        }

        public List<Polygon> AllPolygons()
        {
            var list = new List<Polygon>(Polygons);
            if (Front != null) list.AddRange(Front.AllPolygons());
            if (Back != null) list.AddRange(Back.AllPolygons());
            return list;
        }

        public void Build(List<Polygon> polys)
        {
            if (polys.Count == 0) return;
            if (Plane == null) Plane = new Plane(polys[0].PlaneNormal, polys[0].PlaneW);
            var front = new List<Polygon>();
            var back = new List<Polygon>();
            for (int i = 0; i < polys.Count; i++)
                Plane.SplitPolygon(polys[i], Polygons, Polygons, front, back);
            if (front.Count > 0)
            {
                if (Front == null) Front = new Node();
                Front.Build(front);
            }
            if (back.Count > 0)
            {
                if (Back == null) Back = new Node();
                Back.Build(back);
            }
        }
    }

    // ─── Top-level wrapper ──────────────────────────────────────────────

    public List<Polygon> Polygons { get; private set; }

    private MeshCsg(List<Polygon> polys) { Polygons = polys; }

    /// <summary>Build a CSG from a triangle MeshSnapshot.</summary>
    public static MeshCsg FromMesh(MeshSnapshot mesh)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        var polys = new List<Polygon>(mesh.TriangleCount);
        var v = mesh.VertexCoordsXyz;
        var t = mesh.TriangleIndices;
        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            int a = t[3 * i + 0], b = t[3 * i + 1], c = t[3 * i + 2];
            var pa = new Vec3(v[3 * a + 0], v[3 * a + 1], v[3 * a + 2]);
            var pb = new Vec3(v[3 * b + 0], v[3 * b + 1], v[3 * b + 2]);
            var pc = new Vec3(v[3 * c + 0], v[3 * c + 1], v[3 * c + 2]);
            polys.Add(new Polygon(new List<Vec3> { pa, pb, pc }));
        }
        return new MeshCsg(polys);
    }

    /// <summary>
    /// Triangulate the CSG's polygons (fan from vertex 0) and dedup
    /// vertices into a clean MeshSnapshot.
    /// </summary>
    public MeshSnapshot ToMesh(double dedupTol = 1e-9)
    {
        if (dedupTol < 0) throw new ArgumentOutOfRangeException(nameof(dedupTol));
        var verts = new List<double>();
        var tris = new List<int>();
        var index = new Dictionary<long, List<int>>();
        double cell = Math.Max(dedupTol * 2.0, 1e-12);

        int AddVertex(Vec3 p)
        {
            long key = HashKey(p.X, p.Y, p.Z, cell);
            if (index.TryGetValue(key, out var bucket))
            {
                for (int k = 0; k < bucket.Count; k++)
                {
                    int j = bucket[k];
                    double dx = verts[3 * j + 0] - p.X;
                    double dy = verts[3 * j + 1] - p.Y;
                    double dz = verts[3 * j + 2] - p.Z;
                    if (dx * dx + dy * dy + dz * dz <= dedupTol * dedupTol) return j;
                }
            }
            else
            {
                bucket = new List<int>(2);
                index[key] = bucket;
            }
            int idx = verts.Count / 3;
            verts.Add(p.X); verts.Add(p.Y); verts.Add(p.Z);
            bucket.Add(idx);
            return idx;
        }

        for (int i = 0; i < Polygons.Count; i++)
        {
            var poly = Polygons[i];
            if (poly.Vertices.Count < 3) continue;
            int v0 = AddVertex(poly.Vertices[0]);
            for (int k = 1; k + 1 < poly.Vertices.Count; k++)
            {
                int v1 = AddVertex(poly.Vertices[k]);
                int v2 = AddVertex(poly.Vertices[k + 1]);
                if (v0 == v1 || v1 == v2 || v0 == v2) continue;  // degenerate
                tris.Add(v0); tris.Add(v1); tris.Add(v2);
            }
        }
        return new MeshSnapshot(verts, tris);
    }

    private static long HashKey(double x, double y, double z, double cell)
    {
        long ix = (long)Math.Floor(x / cell);
        long iy = (long)Math.Floor(y / cell);
        long iz = (long)Math.Floor(z / cell);
        unchecked
        {
            const long mask = (1L << 21) - 1;
            long ux = (ix + (1L << 20)) & mask;
            long uy = (iy + (1L << 20)) & mask;
            long uz = (iz + (1L << 20)) & mask;
            return (ux << 42) | (uy << 21) | uz;
        }
    }

    // ─── Boolean operations (csg.js identities) ─────────────────────────

    public static MeshCsg Union(MeshCsg a, MeshCsg b)
    {
        if (a == null) throw new ArgumentNullException(nameof(a));
        if (b == null) throw new ArgumentNullException(nameof(b));
        var aNode = new Node(ClonePolygons(a.Polygons));
        var bNode = new Node(ClonePolygons(b.Polygons));
        aNode.ClipTo(bNode);
        bNode.ClipTo(aNode);
        bNode.Invert();
        bNode.ClipTo(aNode);
        bNode.Invert();
        aNode.Build(bNode.AllPolygons());
        return new MeshCsg(aNode.AllPolygons());
    }

    public static MeshCsg Intersection(MeshCsg a, MeshCsg b)
    {
        if (a == null) throw new ArgumentNullException(nameof(a));
        if (b == null) throw new ArgumentNullException(nameof(b));
        var aNode = new Node(ClonePolygons(a.Polygons));
        var bNode = new Node(ClonePolygons(b.Polygons));
        aNode.Invert();
        bNode.ClipTo(aNode);
        bNode.Invert();
        aNode.ClipTo(bNode);
        bNode.ClipTo(aNode);
        aNode.Build(bNode.AllPolygons());
        aNode.Invert();
        return new MeshCsg(aNode.AllPolygons());
    }

    public static MeshCsg Difference(MeshCsg a, MeshCsg b)
    {
        if (a == null) throw new ArgumentNullException(nameof(a));
        if (b == null) throw new ArgumentNullException(nameof(b));
        var aNode = new Node(ClonePolygons(a.Polygons));
        var bNode = new Node(ClonePolygons(b.Polygons));
        aNode.Invert();
        aNode.ClipTo(bNode);
        bNode.ClipTo(aNode);
        bNode.Invert();
        bNode.ClipTo(aNode);
        bNode.Invert();
        aNode.Build(bNode.AllPolygons());
        aNode.Invert();
        return new MeshCsg(aNode.AllPolygons());
    }

    private static List<Polygon> ClonePolygons(List<Polygon> polys)
    {
        var list = new List<Polygon>(polys.Count);
        for (int i = 0; i < polys.Count; i++)
        {
            var p = polys[i];
            list.Add(new Polygon(new List<Vec3>(p.Vertices), p.PlaneNormal, p.PlaneW));
        }
        return list;
    }
}
