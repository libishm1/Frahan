#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.DataModel;

namespace Frahan.Masonry.Cutting;

// =============================================================================
// Slab — convex polyhedron with planar polygonal faces.
//
// Geometry is stored runtime-agnostic, mirroring the MasonryBlock convention:
// flat double[] vertex coords + jagged int[][] faces (each face = ordered
// vertex indices, CCW when viewed from outside). Unlike MasonryBlock the
// faces are NOT pre-triangulated; they retain their natural polygon shape
// so the cutter can split them along plane intersections without inheriting
// triangulation artefacts. Conversion to MasonryBlock happens at the
// downstream consumer via <see cref="ToMasonryBlock"/>, which fan-
// triangulates each face.
//
// Convex assumption: SlabCutter assumes input slabs are convex. Non-convex
// inputs may produce incorrect splits (the algorithm relies on the cap-
// polygon being a simple convex polygon).
// =============================================================================

/// <summary>
/// One convex polyhedral slab. Immutable.
/// </summary>
public sealed class Slab
{
    /// <param name="vertexCoordsXyz">Flat [x0,y0,z0,...]; length must be a multiple of 3.</param>
    /// <param name="faces">Jagged: each face is an ordered list of vertex indices, CCW from outside, length &gt;= 3.</param>
    public Slab(IReadOnlyList<double> vertexCoordsXyz, IReadOnlyList<IReadOnlyList<int>> faces)
    {
        if (vertexCoordsXyz == null) throw new ArgumentNullException(nameof(vertexCoordsXyz));
        if (faces == null) throw new ArgumentNullException(nameof(faces));
        if (vertexCoordsXyz.Count % 3 != 0)
            throw new ArgumentException(
                $"vertexCoordsXyz length must be a multiple of 3, got {vertexCoordsXyz.Count}",
                nameof(vertexCoordsXyz));

        int vertexCount = vertexCoordsXyz.Count / 3;
        for (int i = 0; i < faces.Count; i++)
        {
            var f = faces[i];
            if (f == null)
                throw new ArgumentException($"faces[{i}] is null", nameof(faces));
            if (f.Count < 3)
                throw new ArgumentException(
                    $"faces[{i}] has {f.Count} vertices; need at least 3",
                    nameof(faces));
            for (int k = 0; k < f.Count; k++)
            {
                if (f[k] < 0 || f[k] >= vertexCount)
                    throw new ArgumentException(
                        $"faces[{i}][{k}] = {f[k]} out of range [0, {vertexCount})",
                        nameof(faces));
            }
        }

        VertexCoordsXyz = vertexCoordsXyz;
        Faces = faces;
    }

    public IReadOnlyList<double> VertexCoordsXyz { get; }
    public IReadOnlyList<IReadOnlyList<int>> Faces { get; }

    public int VertexCount => VertexCoordsXyz.Count / 3;
    public int FaceCount => Faces.Count;

    /// <summary>
    /// Signed volume via origin-tetrahedra decomposition (each face
    /// fan-triangulated from its first vertex; signed). Positive when faces
    /// are CCW outward.
    /// </summary>
    public double SignedVolume()
    {
        var v = VertexCoordsXyz;
        double total = 0.0;
        for (int fi = 0; fi < Faces.Count; fi++)
        {
            var face = Faces[fi];
            int v0 = face[0];
            double ax = v[3 * v0], ay = v[3 * v0 + 1], az = v[3 * v0 + 2];
            for (int k = 1; k + 1 < face.Count; k++)
            {
                int v1 = face[k];
                int v2 = face[k + 1];
                double bx = v[3 * v1], by = v[3 * v1 + 1], bz = v[3 * v1 + 2];
                double cx = v[3 * v2], cy = v[3 * v2 + 1], cz = v[3 * v2 + 2];
                // signed tet (origin, a, b, c) = (a . (b x c)) / 6
                double crossx = by * cz - bz * cy;
                double crossy = bz * cx - bx * cz;
                double crossz = bx * cy - by * cx;
                total += ax * crossx + ay * crossy + az * crossz;
            }
        }
        return total / 6.0;
    }

    /// <summary>
    /// Convert this slab to a MasonryBlock by fan-triangulating each face
    /// from its first vertex.
    /// </summary>
    public MasonryBlock ToMasonryBlock(string id, double density)
    {
        var verts = new double[VertexCoordsXyz.Count];
        for (int i = 0; i < verts.Length; i++) verts[i] = VertexCoordsXyz[i];

        var tris = new List<int>(Faces.Count * 3);
        for (int fi = 0; fi < Faces.Count; fi++)
        {
            var face = Faces[fi];
            for (int k = 1; k + 1 < face.Count; k++)
            {
                tris.Add(face[0]);
                tris.Add(face[k]);
                tris.Add(face[k + 1]);
            }
        }

        return new MasonryBlock(id, verts, tris, density);
    }

    /// <summary>
    /// Convenience: build a Slab for an axis-aligned box from min and max
    /// corners. Outward normals; CCW faces.
    /// </summary>
    public static Slab Box(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        if (!(maxX > minX && maxY > minY && maxZ > minZ))
            throw new ArgumentException("max must exceed min on every axis");

        var v = new[]
        {
            minX, minY, minZ,   // 0
            maxX, minY, minZ,   // 1
            maxX, maxY, minZ,   // 2
            minX, maxY, minZ,   // 3
            minX, minY, maxZ,   // 4
            maxX, minY, maxZ,   // 5
            maxX, maxY, maxZ,   // 6
            minX, maxY, maxZ,   // 7
        };
        var faces = new IReadOnlyList<int>[]
        {
            new[] { 0, 3, 2, 1 },  // -Z bottom
            new[] { 4, 5, 6, 7 },  // +Z top
            new[] { 0, 1, 5, 4 },  // -Y front
            new[] { 1, 2, 6, 5 },  // +X right
            new[] { 2, 3, 7, 6 },  // +Y back
            new[] { 3, 0, 4, 7 },  // -X left
        };
        return new Slab(v, faces);
    }

    public override string ToString() =>
        $"Slab(V={VertexCount}, F={FaceCount}, vol={SignedVolume():0.###})";
}
