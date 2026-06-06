using System;
using Frahan.Core;
using Rhino.Geometry;

namespace Frahan.Surface;

/// <summary>
/// Per-stone descriptor returned by <see cref="StoneDescriptorBuilder"/>. Pure
/// data; no Rhino types in the public surface (the caller already has the
/// source mesh - this DTO carries summary numbers only).
///
/// Spec 7 section 4 lists the proposed "Frahan Stone Descriptor" component
/// inputs/outputs; this DTO supplies them.
/// </summary>
public sealed class StoneDescriptor
{
    public StoneDescriptor(
        string id,
        Size3 axisAlignedSize,
        Size3 orientedBoundingBoxSize,
        double meshVolume,
        double aabbVolume,
        double obbVolume,
        double surfaceArea,
        int triangleCount,
        bool isClosed,
        bool isManifold,
        double averageEdgeLength,
        double aspectRatio,
        double compactness)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        AxisAlignedSize = axisAlignedSize;
        OrientedBoundingBoxSize = orientedBoundingBoxSize;
        MeshVolume = meshVolume;
        AabbVolume = aabbVolume;
        ObbVolume = obbVolume;
        SurfaceArea = surfaceArea;
        TriangleCount = triangleCount;
        IsClosed = isClosed;
        IsManifold = isManifold;
        AverageEdgeLength = averageEdgeLength;
        AspectRatio = aspectRatio;
        Compactness = compactness;
    }

    public string Id { get; }
    public Size3 AxisAlignedSize { get; }
    public Size3 OrientedBoundingBoxSize { get; }
    public double MeshVolume { get; }
    public double AabbVolume { get; }
    public double ObbVolume { get; }
    public double SurfaceArea { get; }
    public int TriangleCount { get; }
    public bool IsClosed { get; }
    public bool IsManifold { get; }
    public double AverageEdgeLength { get; }

    /// <summary>
    /// max(W, D, H) / min(W, D, H) of the AABB. Always &gt;= 1. Higher = more elongated.
    /// </summary>
    public double AspectRatio { get; }

    /// <summary>
    /// MeshVolume / AabbVolume. Range (0, 1]. Higher = more box-like.
    /// </summary>
    public double Compactness { get; }

    public override string ToString() =>
        $"StoneDescriptor(id={Id}, AABB={AxisAlignedSize.Width:0.##}x{AxisAlignedSize.Depth:0.##}x{AxisAlignedSize.Height:0.##}, " +
        $"vol={MeshVolume:0.##}, compactness={Compactness:0.##}, aspect={AspectRatio:0.##}, tri={TriangleCount})";
}

/// <summary>
/// Rhino-bound builder that turns a stone <see cref="Mesh"/> into a
/// <see cref="StoneDescriptor"/>. Counterpart to
/// <see cref="FragmentDescriptorBuilder"/> but for 3D stones / blocks.
///
/// Spec 7; runbook section 16.3 component family "Frahan Stone Descriptor".
/// </summary>
public static class StoneDescriptorBuilder
{
    public static StoneDescriptor BuildFromMesh(string id, Mesh mesh)
    {
        if (id == null) throw new ArgumentNullException(nameof(id));
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));

        // Axis-aligned bounding box.
        var aabb = mesh.GetBoundingBox(accurate: false);
        var aabbDiag = aabb.Diagonal;
        var aabbSize = aabb.IsValid
            ? new Size3(
                Math.Max(1e-12, Math.Abs(aabbDiag.X)),
                Math.Max(1e-12, Math.Abs(aabbDiag.Y)),
                Math.Max(1e-12, Math.Abs(aabbDiag.Z)))
            : new Size3(1e-12, 1e-12, 1e-12);
        double aabbVol = aabbSize.Volume;

        // Oriented bounding box: RhinoCommon does not expose a one-line OBB on
        // Mesh; we approximate it as the AABB until a future PR plugs in
        // Frahan.Native.GeometryCore (spec 11) which has the OBB primitive.
        // Document the placeholder so callers do not over-trust this value.
        var obbSize = aabbSize;
        double obbVol = aabbVol;

        // Mesh volume (signed; clamp to non-negative for the descriptor).
        double meshVol = 0.0;
        try
        {
            var props = VolumeMassProperties.Compute(mesh);
            if (props != null) meshVol = Math.Max(0.0, props.Volume);
        }
        catch { /* mesh not closed; volume undefined */ }

        // Surface area.
        double area = 0.0;
        try
        {
            var props = AreaMassProperties.Compute(mesh);
            if (props != null) area = props.Area;
        }
        catch { /* fall back to 0 */ }

        int tri = MeshDiagnostics.TriangleCount(mesh);
        bool closed = MeshDiagnostics.IsClosed(mesh);
        bool manifold = MeshDiagnostics.IsManifold(mesh);
        double avgEdge = MeshDiagnostics.AverageEdgeLength(mesh);

        double maxDim = Math.Max(aabbSize.Width, Math.Max(aabbSize.Depth, aabbSize.Height));
        double minDim = Math.Min(aabbSize.Width, Math.Min(aabbSize.Depth, aabbSize.Height));
        double aspect = minDim <= 1e-12 ? double.PositiveInfinity : maxDim / minDim;

        double compactness = aabbVol <= 1e-12 ? 0.0 : meshVol / aabbVol;
        if (compactness > 1.0) compactness = 1.0;

        return new StoneDescriptor(
            id: id,
            axisAlignedSize: aabbSize,
            orientedBoundingBoxSize: obbSize,
            meshVolume: meshVol,
            aabbVolume: aabbVol,
            obbVolume: obbVol,
            surfaceArea: area,
            triangleCount: tri,
            isClosed: closed,
            isManifold: manifold,
            averageEdgeLength: avgEdge,
            aspectRatio: aspect,
            compactness: compactness);
    }
}
