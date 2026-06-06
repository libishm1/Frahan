#nullable disable
using System.Collections.Generic;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.DataModel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // GhInterop — shared GH-layer helpers for cross-compatible inputs/outputs.
    //
    // Goal: every component in the family can accept EITHER the Frahan DTO
    // (Slab / FracturePlane / FracturePolygon / MasonryBlock) OR the Rhino-
    // native equivalent (Mesh / Plane / Curve), and every slab-producing
    // component can emit a Mesh alongside the Slab DTO so downstream Rhino-
    // native components (Move, Bake, Boolean, Volume, etc.) just work.
    //
    // See outputs/2026-05-08/frahan_stonepack_docs/DATAFLOW_DIAGNOSTIC_REPORT.md
    // for the full rationale.
    // =========================================================================

    public static class GhInterop
    {
        // ─── Plane unwrap (Frahan FracturePlane DTO OR Rhino Plane) ────────

        public static FracturePlane UnwrapPlane(object raw)
        {
            if (raw == null) return null;
            if (raw is FracturePlane direct) return direct;
            if (raw is Plane rhinoPlane) return FromRhinoPlane(rhinoPlane);
            if (raw is GH_ObjectWrapper wrap)
            {
                if (wrap.Value is FracturePlane fp) return fp;
                if (wrap.Value is Plane rp) return FromRhinoPlane(rp);
            }
            if (raw is GH_Plane ghPlane) return FromRhinoPlane(ghPlane.Value);
            return null;
        }

        private static FracturePlane FromRhinoPlane(Plane p)
        {
            if (!p.IsValid) return null;
            return new FracturePlane(
                p.OriginX, p.OriginY, p.OriginZ,
                p.Normal.X, p.Normal.Y, p.Normal.Z);
        }

        // ─── Slab unwrap (Frahan Slab DTO OR Rhino Mesh) ───────────────────

        public static Slab UnwrapSlab(object raw)
        {
            if (raw == null) return null;
            if (raw is Slab direct) return direct;
            if (raw is Mesh mesh) return SlabFromMesh(mesh);
            if (raw is GH_ObjectWrapper wrap)
            {
                if (wrap.Value is Slab s) return s;
                if (wrap.Value is Mesh m) return SlabFromMesh(m);
            }
            if (raw is GH_Mesh ghMesh) return SlabFromMesh(ghMesh.Value);
            return null;
        }

        public static Slab SlabFromMesh(Mesh mesh)
        {
            return SlabFromMesh(mesh, mergeCoplanarTolDeg: 1.0);
        }

        /// <summary>
        /// Convert a Rhino mesh to a Slab. Optionally merge near-coplanar
        /// adjacent triangles into single polygonal faces, so a triangulated
        /// "cube top" (2 triangles) becomes one quad face. Auto Interfaces
        /// then sees one bed-joint contact per touching pair instead of one
        /// contact per triangle.
        /// </summary>
        /// <param name="mergeCoplanarTolDeg">Angular tolerance in degrees;
        /// 0 disables merging (keeps every triangle as its own face).</param>
        public static Slab SlabFromMesh(Mesh mesh, double mergeCoplanarTolDeg)
        {
            if (mesh == null) return null;
            if (mesh.Vertices.Count < 4 || mesh.Faces.Count < 4) return null;

            int vCount = mesh.Vertices.Count;
            var verts = new double[vCount * 3];
            for (int i = 0; i < vCount; i++)
            {
                var pt = mesh.Vertices[i];
                verts[i * 3 + 0] = pt.X;
                verts[i * 3 + 1] = pt.Y;
                verts[i * 3 + 2] = pt.Z;
            }

            var triFaces = new List<int[]>(mesh.Faces.Count * 2);
            for (int f = 0; f < mesh.Faces.Count; f++)
            {
                var face = mesh.Faces[f];
                triFaces.Add(new[] { face.A, face.B, face.C });
                if (face.IsQuad)
                    triFaces.Add(new[] { face.A, face.C, face.D });
            }

            if (mergeCoplanarTolDeg <= 0.0)
            {
                return new Slab(verts, triFaces.ToArray());
            }

            var merged = MergeCoplanarTriangles(verts, triFaces, mergeCoplanarTolDeg);
            return new Slab(verts, merged);
        }

        // ─── Coplanar-triangle merging ──────────────────────────────────────
        private static IReadOnlyList<IReadOnlyList<int>> MergeCoplanarTriangles(
            double[] verts, IReadOnlyList<int[]> tris, double tolDeg)
        {
            int n = tris.Count;
            var normals = new (double X, double Y, double Z, double D)[n];
            for (int i = 0; i < n; i++)
            {
                int a = tris[i][0], b = tris[i][1], c = tris[i][2];
                double ax = verts[3 * a + 0], ay = verts[3 * a + 1], az = verts[3 * a + 2];
                double bx = verts[3 * b + 0], by = verts[3 * b + 1], bz = verts[3 * b + 2];
                double cx = verts[3 * c + 0], cy = verts[3 * c + 1], cz = verts[3 * c + 2];
                double ex = bx - ax, ey = by - ay, ez = bz - az;
                double fx = cx - ax, fy = cy - ay, fz = cz - az;
                double nx = ey * fz - ez * fy;
                double ny = ez * fx - ex * fz;
                double nz = ex * fy - ey * fx;
                double mag = System.Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (mag < 1e-12) { normals[i] = (0, 0, 0, 0); continue; }
                nx /= mag; ny /= mag; nz /= mag;
                double d = ax * nx + ay * ny + az * nz;
                normals[i] = (nx, ny, nz, d);
            }

            double cosTol = System.Math.Cos(tolDeg * System.Math.PI / 180.0);
            var groupId = new int[n];
            for (int i = 0; i < n; i++) groupId[i] = -1;
            int nextGroup = 0;

            for (int i = 0; i < n; i++)
            {
                if (groupId[i] >= 0) continue;
                if (normals[i].X == 0 && normals[i].Y == 0 && normals[i].Z == 0) continue;
                groupId[i] = nextGroup;
                for (int j = i + 1; j < n; j++)
                {
                    if (groupId[j] >= 0) continue;
                    double dot = normals[i].X * normals[j].X
                                + normals[i].Y * normals[j].Y
                                + normals[i].Z * normals[j].Z;
                    if (dot < cosTol) continue;
                    if (System.Math.Abs(normals[i].D - normals[j].D) > 1e-6) continue;
                    groupId[j] = nextGroup;
                }
                nextGroup += 1;
            }

            var faces = new List<int[]>(nextGroup);
            for (int g = 0; g < nextGroup; g++)
            {
                var groupTris = new List<int[]>();
                for (int i = 0; i < n; i++)
                    if (groupId[i] == g) groupTris.Add(tris[i]);
                if (groupTris.Count == 0) continue;

                var edges = new Dictionary<long, int>();
                foreach (var t in groupTris)
                {
                    BumpEdge(edges, t[0], t[1]);
                    BumpEdge(edges, t[1], t[2]);
                    BumpEdge(edges, t[2], t[0]);
                }
                var boundary = new List<(int A, int B)>();
                foreach (var kv in edges)
                {
                    if (kv.Value == 1)
                    {
                        long key = kv.Key;
                        int a = (int)(key >> 32);
                        int b = (int)(key & 0xFFFFFFFF);
                        boundary.Add((a, b));
                    }
                }
                if (boundary.Count < 3)
                {
                    foreach (var t in groupTris) faces.Add(t);
                    continue;
                }
                var ring = StitchBoundary(boundary);
                if (ring == null || ring.Count < 3)
                {
                    foreach (var t in groupTris) faces.Add(t);
                    continue;
                }
                faces.Add(ring.ToArray());
            }
            return faces.ToArray();
        }

        private static void BumpEdge(Dictionary<long, int> map, int a, int b)
        {
            long fwd = ((long)a << 32) | (uint)b;
            long rev = ((long)b << 32) | (uint)a;
            if (map.ContainsKey(fwd)) map[fwd] += 1;
            else if (map.ContainsKey(rev)) map[rev] += 1;
            else map[fwd] = 1;
        }

        private static List<int> StitchBoundary(List<(int A, int B)> edges)
        {
            if (edges.Count == 0) return null;
            var adj = new Dictionary<int, List<int>>();
            foreach (var (a, b) in edges)
            {
                if (!adj.TryGetValue(a, out var la)) { la = new List<int>(); adj[a] = la; }
                la.Add(b);
                if (!adj.TryGetValue(b, out var lb)) { lb = new List<int>(); adj[b] = lb; }
                lb.Add(a);
            }
            foreach (var kv in adj)
                if (kv.Value.Count != 2) return null;

            var ring = new List<int>(edges.Count);
            int start = edges[0].A;
            int prev = -1;
            int cur = start;
            int safety = edges.Count + 4;
            while (safety-- > 0)
            {
                ring.Add(cur);
                var ns = adj[cur];
                int next = ns[0] == prev ? ns[1] : ns[0];
                if (next == start) return ring;
                prev = cur;
                cur = next;
            }
            return null;
        }

        // ─── Slab → Rhino Mesh (for the new parallel Mesh outputs) ─────────

        public static Mesh SlabToMesh(Slab slab)
        {
            if (slab == null) return null;
            var mesh = new Mesh();
            int n = slab.VertexCount;
            for (int i = 0; i < n; i++)
            {
                mesh.Vertices.Add(
                    slab.VertexCoordsXyz[3 * i + 0],
                    slab.VertexCoordsXyz[3 * i + 1],
                    slab.VertexCoordsXyz[3 * i + 2]);
            }
            // Fan-triangulate each polygonal face from its first vertex.
            for (int f = 0; f < slab.FaceCount; f++)
            {
                var face = slab.Faces[f];
                for (int k = 1; k + 1 < face.Count; k++)
                {
                    mesh.Faces.AddFace(face[0], face[k], face[k + 1]);
                }
            }
            mesh.Normals.ComputeNormals();
            mesh.Compact();
            return mesh;
        }

        public static List<Mesh> SlabsToMeshes(IReadOnlyList<Slab> slabs)
        {
            if (slabs == null) return new List<Mesh>();
            var result = new List<Mesh>(slabs.Count);
            for (int i = 0; i < slabs.Count; i++) result.Add(SlabToMesh(slabs[i]));
            return result;
        }

        // ─── MasonryBlock → Rhino Mesh ─────────────────────────────────────

        public static Mesh BlockToMesh(MasonryBlock block)
        {
            if (block == null) return null;
            var mesh = new Mesh();
            int n = block.VertexCount;
            for (int i = 0; i < n; i++)
            {
                mesh.Vertices.Add(
                    block.VertexCoordsXyz[3 * i + 0],
                    block.VertexCoordsXyz[3 * i + 1],
                    block.VertexCoordsXyz[3 * i + 2]);
            }
            int t = block.TriangleCount;
            for (int i = 0; i < t; i++)
            {
                mesh.Faces.AddFace(
                    block.TriangleIndices[3 * i + 0],
                    block.TriangleIndices[3 * i + 1],
                    block.TriangleIndices[3 * i + 2]);
            }
            mesh.Normals.ComputeNormals();
            mesh.Compact();
            return mesh;
        }

        // ─── Type description (for clear runtime errors) ───────────────────

        public static string DescribeType(object raw)
        {
            if (raw == null) return "null";
            if (raw is GH_ObjectWrapper wrap)
            {
                var inner = wrap.Value;
                return inner == null
                    ? "GH_ObjectWrapper(null)"
                    : $"GH_ObjectWrapper({inner.GetType().FullName})";
            }
            return raw.GetType().FullName;
        }
    }
}
