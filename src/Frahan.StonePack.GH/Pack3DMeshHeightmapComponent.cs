using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Core;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH;

[DesignApplication(
    "Mesh-derived top/bottom heightmap packer with conservative vertical-column collision checks",
    DesignFlow.TopDown,
    Precedent = "Frahan-original mesh-pile heightmap packing; Chehrazad 2025 DLBF substrate")]
[Algorithm("Mesh top/bottom heightmap greedy packing", "Frahan-original",
    Note = "Frahan-original mesh-pile heightmap proxy; greedy deepest-bottom-left substrate per Chehrazad 2025 DLBF")]
public sealed class Pack3DMeshHeightmapComponent : GH_Component
{
    public Pack3DMeshHeightmapComponent()
        : base("Pack3D Mesh Heightmap", "Pack3DMesh",
            "Mesh-derived top/bottom heightmap packer with conservative vertical-column collision checks. Frahan-original method.",
            "Frahan", "3D Packing")
    {
    }

    public override Guid ComponentGuid => new Guid("A16D6426-38A8-44B1-AB6A-4BA80EB39730");
    protected override Bitmap? Icon => IconProvider.Load("LayeredPack.png");
    public override GH_Exposure Exposure => GH_Exposure.hidden;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Meshes", "M", "Meshes to pack using mesh-derived footprint and heightmap proxies.", GH_ParamAccess.list);
        pManager.AddBoxParameter("Container", "C", "Container box.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Cell Size", "Grid", "Solver grid resolution in model units. Smaller values are more detailed but slower.", GH_ParamAccess.item, 10.0);
        pManager.AddNumberParameter("Clearance", "Gap", "Extra XY gap added around each mesh footprint in model units. Larger values leave more space between packed parts.", GH_ParamAccess.item, 0.0);
        pManager.AddBooleanParameter("Yaw 90", "Y90", "Try 90 degree yaw rotations.", GH_ParamAccess.item, true);
        pManager.AddIntegerParameter("Max Candidates", "N", "Maximum XY/orientation candidates evaluated per mesh.", GH_ParamAccess.item, 50000);
        pManager.AddIntegerParameter("Seed", "Seed", "0 is deterministic. Nonzero seeds explore alternative candidate orders.", GH_ParamAccess.item, 0);
        pManager.AddNumberParameter("Random Tie", "Rnd", "Small score jitter for seed-driven alternatives. Use 0 for no jitter.", GH_ParamAccess.item, 0.0);
        pManager.AddBooleanParameter("Run", "Run", "Run the mesh-heightmap packer.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Placed Meshes", "P", "Packed mesh duplicates.", GH_ParamAccess.list);
        pManager.AddTransformParameter("Transforms", "T", "Placement transforms.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Sequence", "Seq", "Placement sequence by input index.", GH_ParamAccess.list);
        pManager.AddMeshParameter("Failed Meshes", "Fail", "Meshes that could not be placed.", GH_ParamAccess.list);
        pManager.AddTextParameter("Failure Reasons", "Why", "Failure reason for each failed mesh.", GH_ParamAccess.list);
        pManager.AddTextParameter("Info", "Info", "Packing report.", GH_ParamAccess.item);
        pManager.AddMeshParameter("Heightmap", "H", "Final pile heightmap debug mesh.", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Source Indices", "Src", "Original input mesh index for each placed mesh and transform.", GH_ParamAccess.list);
        pManager.AddGenericParameter("Pack Result", "PR",
            "Opaque PackResult for downstream Frahan Packing Report.",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var meshes = new List<Mesh>();
        var containerBox = Box.Unset;
        var cellSize = 10.0;
        var clearance = 0.0;
        var allowYaw90 = true;
        var maxCandidates = 50000;
        var seed = 0;
        var randomTie = 0.0;
        var run = false;

        if (!da.GetDataList(0, meshes)) return;
        if (!da.GetData(1, ref containerBox)) return;
        da.GetData(2, ref cellSize);
        da.GetData(3, ref clearance);
        da.GetData(4, ref allowYaw90);
        da.GetData(5, ref maxCandidates);
        da.GetData(6, ref seed);
        da.GetData(7, ref randomTie);
        da.GetData(8, ref run);

        if (!run)
        {
            da.SetData(5, "Set Run to true.");
            return;
        }

        if (!containerBox.IsValid || cellSize <= 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Container must be valid and Cell Size must be greater than zero.");
            return;
        }

        var containerBounds = containerBox.BoundingBox;
        var container = new PackContainer(containerBounds.Diagonal.X, containerBounds.Diagonal.Y, containerBounds.Diagonal.Z);
        var items = new List<MeshPackItem>();

        for (var i = 0; i < meshes.Count; i++)
        {
            var mesh = meshes[i];
            if (mesh == null || !mesh.IsValid) continue;

            var item = CreateMeshPackItem(i, mesh);
            if (item == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Mesh {i} could not be triangulated for mesh-heightmap packing.");
                continue;
            }

            items.Add(item);
        }

        var settings = new MeshPackSettings
        {
            CellSize = cellSize,
            Clearance = Math.Max(0, clearance),
            AllowYaw90 = allowYaw90,
            MaxCandidatesPerItem = Math.Max(1, maxCandidates),
            Seed = seed,
            RandomTieBreakWeight = Math.Max(0, randomTie)
        };

        var result = new GreedyMeshHeightmapPacker().Pack(items, container, settings);
        var placedMeshes = new List<Mesh>();
        var transforms = new List<Transform>();
        var sequence = new List<int>();
        var sourceIndices = new List<int>();
        var failedMeshes = new List<Mesh>();
        var failureReasons = new List<string>();

        foreach (var placement in result.Placements)
        {
            if (placement.Item.Source is not MeshSource source) continue;

            var duplicate = source.Mesh.DuplicateMesh();
            var transform = BuildTransform(placement, source.SourceMin, containerBounds.Min);
            duplicate.Transform(transform);

            placedMeshes.Add(duplicate);
            transforms.Add(transform);
            var sourceIndex = int.Parse(placement.Item.Id);
            sequence.Add(sourceIndex);
            sourceIndices.Add(sourceIndex);
        }

        foreach (var failure in result.Failures)
        {
            if (failure.Item.Source is not MeshSource source) continue;
            failedMeshes.Add(source.Mesh);
            failureReasons.Add($"{failure.Item.Id}: {failure.Reason}");
        }

        da.SetDataList(0, placedMeshes);
        da.SetDataList(1, transforms);
        da.SetDataList(2, sequence);
        da.SetDataList(3, failedMeshes);
        da.SetDataList(4, failureReasons);
        da.SetData(5, BuildReport(result, meshes.Count));
        da.SetData(6, BuildHeightmapMesh(result.Pile, containerBounds.Min));
        da.SetDataList(7, sourceIndices);
        da.SetData(8, new GH_ObjectWrapper(ToPackResult(result, container, settings.CellSize)));
    }

    // Adapts a MeshPackResult into the box-based PackResult that
    // Frahan.Core.PackingMetrics.Compute and the Frahan Packing Report
    // component consume. The conversion preserves per-placement oriented
    // bounding-box volume (which PackingMetrics treats as item Volume),
    // top-of-item Z, score, and failure reasons. The Heightmap field is
    // not read by PackingMetrics; an empty heightmap matching the container
    // is supplied for shape parity.
    private static PackResult ToPackResult(MeshPackResult mesh, PackContainer container, double cellSize)
    {
        var placements = new List<PackPlacement>(mesh.Placements.Count);
        for (var i = 0; i < mesh.Placements.Count; i++)
        {
            var mp = mesh.Placements[i];
            var item = new PackItem(mp.Item.Id, mp.OrientedGeometrySize, mp.Item.Source);
            var box = new Box3(mp.GeometryOrigin, mp.OrientedGeometrySize);
            placements.Add(new PackPlacement(item, box, mp.YawDegrees, mp.Score, mp.Sequence));
        }

        var failures = new List<PackFailure>(mesh.Failures.Count);
        for (var i = 0; i < mesh.Failures.Count; i++)
        {
            var mf = mesh.Failures[i];
            // Use OrientedGeometrySize when available; failures don't carry one,
            // fall back to the source mesh-item bounds size which is always > 0
            // (MeshPackItem.Bounds enforces a 1e-9 floor per Core invariant).
            var fallbackSize = mf.Item.Bounds.Size;
            var fallbackItem = new PackItem(mf.Item.Id, fallbackSize, mf.Item.Source);
            failures.Add(new PackFailure(fallbackItem, mf.Reason));
        }

        var heightmap = new Heightmap(container, cellSize);
        return new PackResult(placements, failures, heightmap, container);
    }

    private static MeshPackItem? CreateMeshPackItem(int index, Mesh mesh)
    {
        var sourceBounds = mesh.GetBoundingBox(true);
        if (!sourceBounds.IsValid)
        {
            return null;
        }

        var coreMesh = mesh.DuplicateMesh();
        coreMesh.Faces.ConvertQuadsToTriangles();
        coreMesh.Vertices.CombineIdentical(true, true);
        coreMesh.Vertices.CullUnused();
        coreMesh.Compact();

        if (coreMesh.Vertices.Count == 0 || coreMesh.Faces.Count == 0)
        {
            return null;
        }

        var vertices = new List<Vec3>(coreMesh.Vertices.Count);
        var sourceMin = sourceBounds.Min;
        foreach (var vertex in coreMesh.Vertices)
        {
            vertices.Add(new Vec3(vertex.X - sourceMin.X, vertex.Y - sourceMin.Y, vertex.Z - sourceMin.Z));
        }

        var triangles = new List<MeshTriangle>(coreMesh.Faces.Count);
        foreach (var face in coreMesh.Faces)
        {
            if (face.IsTriangle)
            {
                triangles.Add(new MeshTriangle(face.A, face.B, face.C));
            }
            else if (face.IsQuad)
            {
                triangles.Add(new MeshTriangle(face.A, face.B, face.C));
                triangles.Add(new MeshTriangle(face.A, face.C, face.D));
            }
        }

        if (triangles.Count == 0)
        {
            return null;
        }

        return new MeshPackItem(index.ToString(), vertices, triangles, new MeshSource(mesh, sourceMin));
    }

    private static Transform BuildTransform(MeshPackPlacement placement, Point3d sourceMin, Point3d containerMin)
    {
        var sourceToOrigin = Transform.Translation(-sourceMin.X, -sourceMin.Y, -sourceMin.Z);
        var downRot = DownRotation(placement.DownAxis);
        var yawRot = Transform.Rotation(Rhino.RhinoMath.ToRadians(placement.YawDegrees), Vector3d.ZAxis, Point3d.Origin);
        var rotation = yawRot * downRot; // yaw AFTER down, matching Core's Rdown-then-yaw order
        var normalize = Transform.Translation(
            -placement.OrientationBoundsMin.X,
            -placement.OrientationBoundsMin.Y,
            -placement.OrientationBoundsMin.Z);
        var target = Transform.Translation(
            containerMin.X + placement.GeometryOrigin.X,
            containerMin.Y + placement.GeometryOrigin.Y,
            containerMin.Z + placement.GeometryOrigin.Z);

        return target * normalize * rotation * sourceToOrigin;
    }

    // Down rotation tilts a local axis to -Z, applied BEFORE the yaw rotation.
    // Mirrors Core OrientedMeshHeightmap.Rotate: 0 = None (identity, byte-stable),
    // 1 = X down (rotate about +Y by +90deg), 2 = Y down (rotate about +X by -90deg).
    private static Transform DownRotation(int downAxis)
    {
        switch (downAxis)
        {
            case 1:
                return Transform.Rotation(Math.PI / 2.0, Vector3d.YAxis, Point3d.Origin);
            case 2:
                return Transform.Rotation(-Math.PI / 2.0, Vector3d.XAxis, Point3d.Origin);
            default:
                return Transform.Identity;
        }
    }

    private static string BuildReport(MeshPackResult result, int inputCount)
    {
        return $"Frahan StonePack 0.1.0 mesh-heightmap | Input: {inputCount} | Placed: {result.Placements.Count} | Failed: {result.Failures.Count} | Fill estimate: {result.FillRatioEstimate:P2}";
    }

    private static Mesh BuildHeightmapMesh(MeshPileHeightmap pile, Point3d origin)
    {
        var mesh = new Mesh();
        for (var x = 0; x < pile.WidthCells; x++)
        {
            for (var y = 0; y < pile.DepthCells; y++)
            {
                var x0 = origin.X + x * pile.CellSize;
                var y0 = origin.Y + y * pile.CellSize;
                var z = origin.Z + pile[x, y];
                var i = mesh.Vertices.Count;
                mesh.Vertices.Add(x0, y0, z);
                mesh.Vertices.Add(x0 + pile.CellSize, y0, z);
                mesh.Vertices.Add(x0 + pile.CellSize, y0 + pile.CellSize, z);
                mesh.Vertices.Add(x0, y0 + pile.CellSize, z);
                mesh.Faces.AddFace(i, i + 1, i + 2, i + 3);
            }
        }

        mesh.Normals.ComputeNormals();
        mesh.Compact();
        return mesh;
    }

    private sealed class MeshSource
    {
        public MeshSource(Mesh mesh, Point3d sourceMin)
        {
            Mesh = mesh;
            SourceMin = sourceMin;
        }

        public Mesh Mesh { get; }
        public Point3d SourceMin { get; }
    }
}
