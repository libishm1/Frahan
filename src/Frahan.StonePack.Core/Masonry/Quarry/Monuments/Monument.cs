#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Geometry;

namespace Frahan.Masonry.Quarry.Monuments;

// =============================================================================
// Monument -- one target product for the monument-in-bench packer.
//
// A Monument is an arbitrary 3D shape (statue, column, plinth, capital, …)
// described by a triangle mesh in world coordinates. The packer treats the
// mesh's axis-aligned bounding box, recomputed per orientation, as the
// collision proxy; the mesh itself is preserved through the pipeline so
// downstream display / Boolean cuts use the actual statue shape.
//
// Spec: outputs/2026-05-15/connection_map/MONUMENT_PACKING.md (next).
// =============================================================================

public sealed class Monument
{
    public Monument(string id, PlyMesh mesh, double density = 2700.0)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id required", nameof(id));
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (mesh.TriangleCount == 0) throw new ArgumentException("mesh must have triangles", nameof(mesh));
        if (density <= 0) throw new ArgumentOutOfRangeException(nameof(density), "> 0");

        Id = id;
        Mesh = mesh;
        Density = density;

        // pre-compute the local AABB (origin-relative). The packer rotates this
        // 24 times during placement search.
        double xMin = double.PositiveInfinity, yMin = double.PositiveInfinity, zMin = double.PositiveInfinity;
        double xMax = double.NegativeInfinity, yMax = double.NegativeInfinity, zMax = double.NegativeInfinity;
        var v = mesh.VertexCoordsXyz;
        int n = mesh.VertexCount;
        for (int i = 0; i < n; i++)
        {
            double x = v[3 * i + 0], y = v[3 * i + 1], z = v[3 * i + 2];
            if (x < xMin) xMin = x; if (x > xMax) xMax = x;
            if (y < yMin) yMin = y; if (y > yMax) yMax = y;
            if (z < zMin) zMin = z; if (z > zMax) zMax = z;
        }
        AabbMinX = xMin; AabbMinY = yMin; AabbMinZ = zMin;
        AabbMaxX = xMax; AabbMaxY = yMax; AabbMaxZ = zMax;
    }

    public string Id { get; }
    public PlyMesh Mesh { get; }
    public double Density { get; }

    public double AabbMinX { get; } public double AabbMinY { get; } public double AabbMinZ { get; }
    public double AabbMaxX { get; } public double AabbMaxY { get; } public double AabbMaxZ { get; }

    public double SizeX => AabbMaxX - AabbMinX;
    public double SizeY => AabbMaxY - AabbMinY;
    public double SizeZ => AabbMaxZ - AabbMinZ;

    public double AabbVolume => SizeX * SizeY * SizeZ;

    public override string ToString() =>
        $"Monument({Id}: AABB {SizeX:0.###} x {SizeY:0.###} x {SizeZ:0.###} m, V_aabb={AabbVolume:0.###} m^3)";
}

public sealed class MonumentInventory
{
    public MonumentInventory(IReadOnlyList<Monument> monuments)
    {
        if (monuments == null) throw new ArgumentNullException(nameof(monuments));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < monuments.Count; i++)
        {
            var m = monuments[i];
            if (m == null) throw new ArgumentException($"monuments[{i}] is null", nameof(monuments));
            if (!seen.Add(m.Id))
                throw new ArgumentException($"duplicate monument id '{m.Id}'", nameof(monuments));
        }
        Monuments = monuments;
    }

    public IReadOnlyList<Monument> Monuments { get; }
    public int Count => Monuments.Count;

    public double TotalAabbVolume
    {
        get
        {
            double t = 0.0;
            for (int i = 0; i < Monuments.Count; i++) t += Monuments[i].AabbVolume;
            return t;
        }
    }

    public override string ToString() =>
        $"MonumentInventory(N={Count}, V_aabb_total={TotalAabbVolume:0.###} m^3)";
}
