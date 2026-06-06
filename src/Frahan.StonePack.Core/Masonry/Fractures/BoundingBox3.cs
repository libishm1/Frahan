#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;

namespace Frahan.Masonry.Fractures;

/// <summary>
/// Lightweight axis-aligned bounding box in 3D. Used by the fracture
/// generators to seed grids and Voronoi diagrams in Slab-relative
/// coordinates. Immutable.
/// </summary>
public sealed class BoundingBox3
{
    public BoundingBox3(
        double minX, double minY, double minZ,
        double maxX, double maxY, double maxZ)
    {
        if (!(maxX > minX))
            throw new ArgumentException($"maxX ({maxX}) must exceed minX ({minX})", nameof(maxX));
        if (!(maxY > minY))
            throw new ArgumentException($"maxY ({maxY}) must exceed minY ({minY})", nameof(maxY));
        if (!(maxZ > minZ))
            throw new ArgumentException($"maxZ ({maxZ}) must exceed minZ ({minZ})", nameof(maxZ));

        MinX = minX; MinY = minY; MinZ = minZ;
        MaxX = maxX; MaxY = maxY; MaxZ = maxZ;
    }

    public double MinX { get; }
    public double MinY { get; }
    public double MinZ { get; }
    public double MaxX { get; }
    public double MaxY { get; }
    public double MaxZ { get; }

    public double SizeX => MaxX - MinX;
    public double SizeY => MaxY - MinY;
    public double SizeZ => MaxZ - MinZ;

    public double CenterX => 0.5 * (MinX + MaxX);
    public double CenterY => 0.5 * (MinY + MaxY);
    public double CenterZ => 0.5 * (MinZ + MaxZ);

    /// <summary>
    /// Returns true when (x, y, z) lies inside the box (with strict
    /// non-negative tolerance on all axes). The point is treated as on the
    /// face when within <paramref name="eps"/> of any boundary.
    /// </summary>
    public bool Contains(double x, double y, double z, double eps = 0.0)
    {
        if (eps < 0.0) throw new ArgumentOutOfRangeException(nameof(eps));
        return x >= MinX - eps && x <= MaxX + eps
            && y >= MinY - eps && y <= MaxY + eps
            && z >= MinZ - eps && z <= MaxZ + eps;
    }

    /// <summary>
    /// AABB enclosing a Slab. O(V).
    /// </summary>
    public static BoundingBox3 FromSlab(Slab slab)
    {
        if (slab == null) throw new ArgumentNullException(nameof(slab));
        if (slab.VertexCount < 1)
            throw new ArgumentException("slab has no vertices", nameof(slab));

        var v = slab.VertexCoordsXyz;
        double xMin = double.PositiveInfinity, yMin = double.PositiveInfinity, zMin = double.PositiveInfinity;
        double xMax = double.NegativeInfinity, yMax = double.NegativeInfinity, zMax = double.NegativeInfinity;
        int n = slab.VertexCount;
        for (int i = 0; i < n; i++)
        {
            double x = v[3 * i + 0]; double y = v[3 * i + 1]; double z = v[3 * i + 2];
            if (x < xMin) xMin = x; if (x > xMax) xMax = x;
            if (y < yMin) yMin = y; if (y > yMax) yMax = y;
            if (z < zMin) zMin = z; if (z > zMax) zMax = z;
        }
        return new BoundingBox3(xMin, yMin, zMin, xMax, yMax, zMax);
    }

    /// <summary>
    /// AABB enclosing the union of a list of points. Throws if the list is
    /// empty.
    /// </summary>
    public static BoundingBox3 FromPoints(IReadOnlyList<double> xyzFlat)
    {
        if (xyzFlat == null) throw new ArgumentNullException(nameof(xyzFlat));
        if (xyzFlat.Count < 3 || xyzFlat.Count % 3 != 0)
            throw new ArgumentException(
                $"xyzFlat length must be a positive multiple of 3, got {xyzFlat.Count}",
                nameof(xyzFlat));

        double xMin = double.PositiveInfinity, yMin = double.PositiveInfinity, zMin = double.PositiveInfinity;
        double xMax = double.NegativeInfinity, yMax = double.NegativeInfinity, zMax = double.NegativeInfinity;
        int n = xyzFlat.Count / 3;
        for (int i = 0; i < n; i++)
        {
            double x = xyzFlat[3 * i + 0]; double y = xyzFlat[3 * i + 1]; double z = xyzFlat[3 * i + 2];
            if (x < xMin) xMin = x; if (x > xMax) xMax = x;
            if (y < yMin) yMin = y; if (y > yMax) yMax = y;
            if (z < zMin) zMin = z; if (z > zMax) zMax = z;
        }
        return new BoundingBox3(xMin, yMin, zMin, xMax, yMax, zMax);
    }

    public override string ToString() =>
        $"BBox3(min=({MinX:0.###},{MinY:0.###},{MinZ:0.###}), max=({MaxX:0.###},{MaxY:0.###},{MaxZ:0.###}))";
}
