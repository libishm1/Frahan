#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Interfaces;

/// <summary>
/// Pure-managed snapshot of a Rhino mesh: flat vertex coords + flat
/// triangle indices + optional pre-computed AABB. Used by the
/// proximity-based <see cref="MeshContactDetector"/> so the detector
/// stays Rhino-runtime-free.
/// </summary>
public sealed class MeshSnapshot
{
    public MeshSnapshot(IReadOnlyList<double> vertexCoordsXyz, IReadOnlyList<int> triangleIndices)
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

        VertexCoordsXyz = vertexCoordsXyz;
        TriangleIndices = triangleIndices;

        int n = vertexCoordsXyz.Count / 3;
        if (n > 0)
        {
            double xMin = double.PositiveInfinity, yMin = double.PositiveInfinity, zMin = double.PositiveInfinity;
            double xMax = double.NegativeInfinity, yMax = double.NegativeInfinity, zMax = double.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                double x = vertexCoordsXyz[3 * i + 0];
                double y = vertexCoordsXyz[3 * i + 1];
                double z = vertexCoordsXyz[3 * i + 2];
                if (x < xMin) xMin = x; if (x > xMax) xMax = x;
                if (y < yMin) yMin = y; if (y > yMax) yMax = y;
                if (z < zMin) zMin = z; if (z > zMax) zMax = z;
            }
            BBoxMinX = xMin; BBoxMinY = yMin; BBoxMinZ = zMin;
            BBoxMaxX = xMax; BBoxMaxY = yMax; BBoxMaxZ = zMax;
        }
    }

    public IReadOnlyList<double> VertexCoordsXyz { get; }
    public IReadOnlyList<int> TriangleIndices { get; }

    public int VertexCount => VertexCoordsXyz.Count / 3;
    public int TriangleCount => TriangleIndices.Count / 3;

    public double BBoxMinX { get; }
    public double BBoxMinY { get; }
    public double BBoxMinZ { get; }
    public double BBoxMaxX { get; }
    public double BBoxMaxY { get; }
    public double BBoxMaxZ { get; }

    public bool BBoxOverlaps(MeshSnapshot other, double tolerance)
    {
        if (other == null) return false;
        return BBoxMaxX + tolerance >= other.BBoxMinX
            && BBoxMinX - tolerance <= other.BBoxMaxX
            && BBoxMaxY + tolerance >= other.BBoxMinY
            && BBoxMinY - tolerance <= other.BBoxMaxY
            && BBoxMaxZ + tolerance >= other.BBoxMinZ
            && BBoxMinZ - tolerance <= other.BBoxMaxZ;
    }
}
