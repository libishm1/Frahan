using System;
using System.Collections.Generic;
using System.Linq;

namespace Frahan.Core;

public readonly struct MeshTriangle
{
    public MeshTriangle(int a, int b, int c)
    {
        A = a;
        B = b;
        C = c;
    }

    public int A { get; }
    public int B { get; }
    public int C { get; }
}

public sealed class MeshPackItem
{
    public MeshPackItem(string id, IReadOnlyList<Vec3> vertices, IReadOnlyList<MeshTriangle> triangles, object? source = null)
    {
        if (vertices == null) throw new ArgumentNullException(nameof(vertices));
        if (triangles == null) throw new ArgumentNullException(nameof(triangles));
        if (vertices.Count == 0) throw new ArgumentException("Mesh item needs at least one vertex.", nameof(vertices));

        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
        Vertices = vertices.ToArray();
        Triangles = triangles.ToArray();
        Source = source;
        Bounds = BoundsFrom(Vertices);
    }

    public string Id { get; }
    public IReadOnlyList<Vec3> Vertices { get; }
    public IReadOnlyList<MeshTriangle> Triangles { get; }
    public object? Source { get; }
    public Box3 Bounds { get; }

    /// <summary>
    /// Axis-aligned bounding-box volume. Fast, but an over-estimate for any
    /// non-box mesh. Use <see cref="MeshVolume"/> for the honest packing
    /// density numerator.
    /// </summary>
    public double VolumeEstimate => Bounds.Size.Volume;

    private double? _meshVolume;

    /// <summary>
    /// True closed-mesh volume by the signed-tetrahedron (divergence) sum
    /// V = (1/6) sum_t (a_t . (b_t x c_t)) over triangles, each spanning the
    /// tetra to the origin. Sign-robust (translation-invariant up to a
    /// telescoping cancellation for closed meshes) and takes |.| so winding
    /// does not flip the magnitude. Cached. This is the managed equivalent of
    /// the harness RhinoCommon VolumeMassProperties path, so the Core honest
    /// density rho = sum MeshVolume / (A * H_used) no longer needs Rhino.
    /// </summary>
    public double MeshVolume
    {
        get
        {
            if (_meshVolume.HasValue) return _meshVolume.Value;
            double six = 0.0;
            for (int t = 0; t < Triangles.Count; t++)
            {
                var tri = Triangles[t];
                var a = Vertices[tri.A];
                var b = Vertices[tri.B];
                var c = Vertices[tri.C];
                // a . (b x c)
                double cx = b.Y * c.Z - b.Z * c.Y;
                double cy = b.Z * c.X - b.X * c.Z;
                double cz = b.X * c.Y - b.Y * c.X;
                six += a.X * cx + a.Y * cy + a.Z * cz;
            }
            _meshVolume = Math.Abs(six) / 6.0;
            return _meshVolume.Value;
        }
    }

    private static Box3 BoundsFrom(IReadOnlyList<Vec3> vertices)
    {
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var minZ = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        var maxZ = double.NegativeInfinity;

        foreach (var vertex in vertices)
        {
            minX = Math.Min(minX, vertex.X);
            minY = Math.Min(minY, vertex.Y);
            minZ = Math.Min(minZ, vertex.Z);
            maxX = Math.Max(maxX, vertex.X);
            maxY = Math.Max(maxY, vertex.Y);
            maxZ = Math.Max(maxZ, vertex.Z);
        }

        return new Box3(
            new Vec3(minX, minY, minZ),
            new Size3(Math.Max(1e-9, maxX - minX), Math.Max(1e-9, maxY - minY), Math.Max(1e-9, maxZ - minZ)));
    }
}

public sealed class MeshPackSettings
{
    public double CellSize { get; set; } = 10.0;
    public double Clearance { get; set; } = 0.0;
    public int MaxCandidatesPerItem { get; set; } = 50000;
    public bool AllowYaw90 { get; set; } = true;

    // When true the packer also tries tilting each item so its local X or Y axis
    // points down (DownAxis X / Y) in addition to the default Z-up set, giving the
    // 6-orientation set (3 down axes x yaw {0,90}). Default false keeps the legacy
    // yaw-only behaviour byte-for-byte identical.
    public bool AllowAxisDownOrientations { get; set; } = false;

    public bool SortByVolumeDescending { get; set; } = true;
    public double CompactnessWeight { get; set; } = 0.001;
    public double HeightWeight { get; set; } = 1.0;
    public int Seed { get; set; } = 0;
    public double RandomTieBreakWeight { get; set; } = 0.0;
}

public sealed class MeshPackPlacement
{
    public MeshPackPlacement(
        MeshPackItem item,
        Vec3 geometryOrigin,
        Vec3 orientationBoundsMin,
        Size3 orientedGeometrySize,
        double yawDegrees,
        int downAxis,
        double score,
        int sequence)
    {
        Item = item;
        GeometryOrigin = geometryOrigin;
        OrientationBoundsMin = orientationBoundsMin;
        OrientedGeometrySize = orientedGeometrySize;
        YawDegrees = yawDegrees;
        DownAxis = downAxis;
        Score = score;
        Sequence = sequence;
    }

    public MeshPackItem Item { get; }
    public Vec3 GeometryOrigin { get; }
    public Vec3 OrientationBoundsMin { get; }
    public Size3 OrientedGeometrySize { get; }
    public double YawDegrees { get; }

    // Which local axis was tilted to point down before the yaw rotation:
    // 0 = None (Z up, legacy), 1 = X down, 2 = Y down. The world transform must
    // apply the matching down rotation BEFORE the yaw rotation.
    public int DownAxis { get; }

    public double Score { get; }
    public int Sequence { get; }
}

public sealed class MeshPackFailure
{
    public MeshPackFailure(MeshPackItem item, string reason)
    {
        Item = item;
        Reason = reason;
    }

    public MeshPackItem Item { get; }
    public string Reason { get; }
}

public sealed class MeshPackResult
{
    public MeshPackResult(
        IReadOnlyList<MeshPackPlacement> placements,
        IReadOnlyList<MeshPackFailure> failures,
        MeshPileHeightmap pile,
        PackContainer container)
    {
        Placements = placements;
        Failures = failures;
        Pile = pile;
        Container = container;
    }

    public IReadOnlyList<MeshPackPlacement> Placements { get; }
    public IReadOnlyList<MeshPackFailure> Failures { get; }
    public MeshPileHeightmap Pile { get; }
    public PackContainer Container { get; }

    public double PackedVolumeEstimate => Placements.Sum(p => p.Item.VolumeEstimate);
    public double FillRatioEstimate => Container.Volume <= 0 ? 0 : PackedVolumeEstimate / Container.Volume;

    /// <summary>Sum of true signed-tetra mesh volumes of the placed stones (honest rho numerator).</summary>
    public double PackedMeshVolume => Placements.Sum(p => p.Item.MeshVolume);

    /// <summary>
    /// Honest packing density rho = sum(true mesh volume) / container volume,
    /// the divergence-volume analogue of <see cref="FillRatioEstimate"/> (which
    /// uses bbox volume and so over-reports for non-box stones).
    /// </summary>
    public double FillRatioMeshVolume => Container.Volume <= 0 ? 0 : PackedMeshVolume / Container.Volume;
}
