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
    "Deterministic heightmap packer for early irregular 3D packing workflows",
    DesignFlow.TopDown,
    Precedent = "Chehrazad Roose Wauters 2025 deepest-left-bottom-fill (DLBF); Park Han 2024 tree-packing")]
[Algorithm("Heightmap-greedy 3D bin packing (deepest-bottom-left family)",
    "Chehrazad, R., Roose, D., Wauters, T. (2025). \"A fast and scalable deepest-left-bottom-fill algorithm.\" Int. J. Production Research 63:6606-6629",
    Doi = "10.1080/00207543.2025.2478434",
    WikiPath = "wiki/index/references.md#Chehrazad2025DLBF")]
[Algorithm("Tree-packing for irregular 3D containers", "Park-Han (2024). Tree-packing for irregular 3D containers",
    WikiPath = "wiki/index/references.md#Park2024TreePack")]
public sealed class Pack3DIrregularComponent : GH_Component
{
    public Pack3DIrregularComponent()
        : base("Pack3D Irregular", "Pack3D",
            "Deterministic heightmap packer for early irregular 3D packing workflows. Implements deepest-left-bottom-fill packing (Chehrazad et al. 2025).",
            "Frahan", "3D Packing")
    {
    }

    public override Guid ComponentGuid => new Guid("E36C3F7D-7E2C-495E-9E2A-59312C5CF990");
    protected override Bitmap? Icon => IconProvider.Load("Pack3D.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Meshes", "M", "Meshes to pack. MVP uses each mesh bounding box as the packing proxy.", GH_ParamAccess.list);
        pManager.AddBoxParameter("Container", "C", "Container box.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Cell Size", "Grid", "Solver grid resolution in model units. Smaller values are faster but less detailed.", GH_ParamAccess.item, 10.0);
        pManager.AddNumberParameter("Clearance", "Gap", "Extra XY gap added around each packing proxy in model units.", GH_ParamAccess.item, 0.0);
        pManager.AddBooleanParameter("Yaw 90", "Y90", "Try 90 degree yaw rotations.", GH_ParamAccess.item, true);
        pManager.AddBooleanParameter("Run", "Run", "Run the packer.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Placed Meshes", "P", "Packed mesh duplicates.", GH_ParamAccess.list);
        pManager.AddTransformParameter("Transforms", "T", "Placement transforms.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Sequence", "Seq", "Placement sequence by input index.", GH_ParamAccess.list);
        pManager.AddTextParameter("Info", "Info", "Packing report and failures.", GH_ParamAccess.item);
        pManager.AddMeshParameter("Heightmap", "H", "Heightmap debug mesh.", GH_ParamAccess.item);
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
        var run = false;

        if (!da.GetDataList(0, meshes)) return;
        if (!da.GetData(1, ref containerBox)) return;
        da.GetData(2, ref cellSize);
        da.GetData(3, ref clearance);
        da.GetData(4, ref allowYaw90);
        da.GetData(5, ref run);

        if (!run)
        {
            da.SetData(3, "Set Run to true.");
            return;
        }

        if (!containerBox.IsValid || cellSize <= 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Container must be valid and Cell Size must be greater than zero.");
            return;
        }

        var containerBounds = containerBox.BoundingBox;
        var container = new PackContainer(containerBounds.Diagonal.X, containerBounds.Diagonal.Y, containerBounds.Diagonal.Z);
        var items = new List<PackItem>();

        for (var i = 0; i < meshes.Count; i++)
        {
            var mesh = meshes[i];
            if (mesh == null || !mesh.IsValid) continue;

            var bounds = mesh.GetBoundingBox(true);
            var size = new Size3(
                Math.Max(cellSize, bounds.Diagonal.X + clearance * 2.0),
                Math.Max(cellSize, bounds.Diagonal.Y + clearance * 2.0),
                Math.Max(1e-6, bounds.Diagonal.Z + clearance));
            items.Add(new PackItem(i.ToString(), size, mesh));
        }

        var settings = new PackSettings
        {
            CellSize = cellSize,
            Clearance = Math.Max(0, clearance),
            AllowYaw90 = allowYaw90,
            MaxCandidatesPerItem = 50000
        };

        var result = new GreedyHeightmapPacker().Pack(items, container, settings);
        var placedMeshes = new List<Mesh>();
        var transforms = new List<Transform>();
        var sequence = new List<int>();

        foreach (var placement in result.Placements)
        {
            if (placement.Item.Source is not Mesh source) continue;

            var duplicate = source.DuplicateMesh();
            var sourceBounds = source.GetBoundingBox(true);
            var targetMin = new Point3d(
                containerBounds.Min.X + placement.Box.Min.X,
                containerBounds.Min.Y + placement.Box.Min.Y,
                containerBounds.Min.Z + placement.Box.Min.Z);

            var rotation = Transform.Identity;
            if (Math.Abs(placement.YawDegrees) > 1e-9)
            {
                rotation = Transform.Rotation(Rhino.RhinoMath.ToRadians(placement.YawDegrees), Vector3d.ZAxis, sourceBounds.Center);
                duplicate.Transform(rotation);
            }

            var rotatedBounds = duplicate.GetBoundingBox(true);
            var translation = Transform.Translation(targetMin - rotatedBounds.Min);
            var transform = translation * rotation;
            duplicate.Transform(translation);

            placedMeshes.Add(duplicate);
            transforms.Add(transform);
            sequence.Add(int.Parse(placement.Item.Id));
        }

        da.SetDataList(0, placedMeshes);
        da.SetDataList(1, transforms);
        da.SetDataList(2, sequence);
        da.SetData(3, BuildReport(result, meshes.Count));
        da.SetData(4, BuildHeightmapMesh(result.Heightmap, containerBounds.Min));
        da.SetData(5, new GH_ObjectWrapper(result));
    }

    private static string BuildReport(PackResult result, int inputCount)
    {
        return $"Frahan StonePack 0.1.0 | Input: {inputCount} | Placed: {result.Placements.Count} | Failed: {result.Failures.Count} | Fill: {result.FillRatio:P2}";
    }

    private static Mesh BuildHeightmapMesh(Heightmap heightmap, Point3d origin)
    {
        var mesh = new Mesh();
        for (var x = 0; x < heightmap.WidthCells; x++)
        {
            for (var y = 0; y < heightmap.DepthCells; y++)
            {
                var x0 = origin.X + x * heightmap.CellSize;
                var y0 = origin.Y + y * heightmap.CellSize;
                var z = origin.Z + heightmap[x, y];
                var i = mesh.Vertices.Count;
                mesh.Vertices.Add(x0, y0, z);
                mesh.Vertices.Add(x0 + heightmap.CellSize, y0, z);
                mesh.Vertices.Add(x0 + heightmap.CellSize, y0 + heightmap.CellSize, z);
                mesh.Vertices.Add(x0, y0 + heightmap.CellSize, z);
                mesh.Faces.AddFace(i, i + 1, i + 2, i + 3);
            }
        }

        mesh.Normals.ComputeNormals();
        mesh.Compact();
        return mesh;
    }
}
