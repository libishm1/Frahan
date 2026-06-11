using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Frahan.Core;
using Frahan.GH.Attributes;
using Frahan.GH.Quarry;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH;

[RelatedComponent("Frahan > 3D Packing > Settle 3D (Physics)",
    Reason = "EVOLVED PATH: the canonical volume packer; physically settles real geometry into contact.")]
[RelatedComponent("Frahan > Masonry > Block Pack (Tree)",
    Reason = "EVOLVED PATH: saw-cuttable guillotine subdivision (Kim 2025).")]
[Algorithm("Heightmap-greedy 3D bin packing", "Park and Han 2024 tree-packing for 3D-BPP / orthogonal-block packing", Note = "Mesh-derived top/bottom heightmap with vertical-column collision; greedy XY/orientation search", WikiPath = "wiki/papers/kim2025_tree_packing.md")]
[DesignApplication(
    "Mesh-heightmap packer inside a mesh-derived irregular container footprint and height volume",
    DesignFlow.TopDown,
    Precedent = "Park Han 2024 tree-packing (BlockPackTree); Chehrazad Roose Wauters 2025 DLBF (DOI 10.1080/00207543.2025.2478434)",
    Tolerance = ">= 70 % container fill; 0 overlap")]
public sealed class Pack3DIrregularContainerComponent : GH_Component
{
    public Pack3DIrregularContainerComponent()
        : base("Pack3D Irregular Container", "Pack3DContainer",
            "EVOLVED PATH: for volume packing use Settle 3D (Physics); for saw-cuttable subdivision use Block Pack (Tree). This heightmap packer remains the validated baseline. " +
            "Mesh-heightmap packer inside a mesh-derived irregular container footprint and height volume. [Park & Han 2024]",
            "Frahan", "3D Packing")
    {
    }

    public override Guid ComponentGuid => new Guid("B3E8A42F-F67E-42B5-B3C3-1D1A5A1195C7");
    protected override Bitmap? Icon => IconProvider.Load("PackIntoBlock.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Meshes", "M", "Meshes to pack using mesh-derived footprint and heightmap proxies.", GH_ParamAccess.list);
        pManager.AddMeshParameter("Container Meshes", "C", "One or more irregular container meshes. Each top-down footprint and per-cell height defines an allowed packing volume.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Cell Size", "Grid", "Solver grid resolution in model units. 0 (default) = AUTO: derived from the smallest element (min bounding-box edge / 8), so the packer works at any unit/scale. Set a positive value to override.", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Clearance", "Gap", "Extra XY gap added around each mesh footprint in model units. Larger values leave more space between packed parts.", GH_ParamAccess.item, 0.0);
        pManager.AddBooleanParameter("Yaw 90", "Y90", "Try 90 degree yaw rotations.", GH_ParamAccess.item, true);
        pManager.AddIntegerParameter("Max Candidates", "N", "Maximum XY/orientation candidates evaluated per mesh.", GH_ParamAccess.item, 50000);
        pManager.AddIntegerParameter("Seed", "Seed", "0 is deterministic. Nonzero seeds explore alternative candidate orders.", GH_ParamAccess.item, 0);
        pManager.AddNumberParameter("Random Tie", "Rnd", "Small score jitter for seed-driven alternatives. Use 0 for no jitter.", GH_ParamAccess.item, 0.0);
        pManager.AddBooleanParameter("Run", "Run", "Run the irregular-container packer.", GH_ParamAccess.item, false);
        // 2026-05-30: optional QuarryBlock input (HITL #2). Indexed at the
        // END so existing build_gh wiring (indices 0-8) is untouched.
        // When wired, Block.UsableVolume is used as the container mesh (and
        // takes precedence over the Container Meshes input if both are
        // wired). GUID unchanged.
        pManager.AddParameter(new Param_QuarryBlock(), "Block", "QB",
            "Optional QuarryBlock from Scan to Block Inventory. When wired, " +
            "Block.UsableVolume is the container mesh (takes precedence over " +
            "Container Meshes if both are wired).",
            GH_ParamAccess.item);
        pManager[9].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Placed Meshes", "P", "Packed mesh duplicates.", GH_ParamAccess.list);
        pManager.AddTransformParameter("Transforms", "T", "Placement transforms.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Sequence", "Seq", "Placement sequence by input index.", GH_ParamAccess.list);
        pManager.AddMeshParameter("Failed Meshes", "Fail", "Meshes that could not be placed.", GH_ParamAccess.list);
        pManager.AddTextParameter("Failure Reasons", "Why", "Failure reason for each failed mesh.", GH_ParamAccess.list);
        pManager.AddTextParameter("Info", "Info", "Packing report.", GH_ParamAccess.item);
        pManager.AddMeshParameter("Heightmaps", "H", "Final pile heightmap debug mesh for each container.", GH_ParamAccess.list);
        pManager.AddMeshParameter("Container Cells", "Cells", "Allowed container cells for each container shown at ceiling heights.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Source Indices", "Src", "Original input mesh index for each placed mesh and transform.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Container Indices", "Con", "Input container mesh index for each placed mesh and transform.", GH_ParamAccess.list);
        pManager.AddGenericParameter("Pack Result", "PR",
            "Opaque PackResult for downstream Frahan Packing Report.",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var meshes = new List<Mesh>();
        var containerMeshes = new List<Mesh>();
        var cellSize = 0.0;
        var clearance = 0.0;
        var allowYaw90 = true;
        var maxCandidates = 50000;
        var seed = 0;
        var randomTie = 0.0;
        var run = false;

        if (!da.GetDataList(0, meshes)) return;
        bool gotContainersFromMesh = da.GetDataList(1, containerMeshes) && containerMeshes.Count > 0;
        da.GetData(2, ref cellSize);
        da.GetData(3, ref clearance);
        da.GetData(4, ref allowYaw90);
        da.GetData(5, ref maxCandidates);
        da.GetData(6, ref seed);
        da.GetData(7, ref randomTie);
        da.GetData(8, ref run);

        // 2026-05-30: optional QuarryBlock input. When wired, Block.UsableVolume
        // becomes the container mesh; takes precedence over the Container
        // Meshes input per HITL #2.
        QuarryBlockGoo blockGoo = null;
        if (da.GetData(9, ref blockGoo) && blockGoo != null && blockGoo.Value != null &&
            blockGoo.Value.UsableVolume != null && blockGoo.Value.UsableVolume.IsValid)
        {
            if (gotContainersFromMesh)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "Block input takes precedence over Mesh input.");
            }
            containerMeshes = new List<Mesh> { blockGoo.Value.UsableVolume };
        }
        else if (!gotContainersFromMesh)
        {
            return;
        }

        if (!run)
        {
            da.SetData(5, "Set Run to true.");
            return;
        }

        if (containerMeshes.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one Container Mesh is required.");
            return;
        }
        if (cellSize <= 0)
        {
            // AUTO: grid from the smallest element (min bbox edge / 8) so the packer works at any
            // unit/scale; the shipped 10.0 default collapsed the grid to one cell in a metre model.
            var minDim = double.PositiveInfinity;
            foreach (var m in meshes)
            {
                if (m == null || !m.IsValid) continue;
                var d = m.GetBoundingBox(true).Diagonal;
                var e = System.Math.Min(d.X, System.Math.Min(d.Y, d.Z));
                if (e > 1e-9 && e < minDim) minDim = e;
            }
            if (double.IsInfinity(minDim) || minDim <= 0)
            {
                var cb = containerMeshes[0].GetBoundingBox(true);
                minDim = cb.Diagonal.Length / 50.0;
            }
            cellSize = minDim / 8.0;
            if (cellSize <= 1e-9) cellSize = 1.0;
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Cell Size auto-derived as {cellSize:0.####} (smallest element / 8).");
        }

        var containers = new List<ContainerContext>();
        for (var i = 0; i < containerMeshes.Count; i++)
        {
            var containerMesh = containerMeshes[i];
            if (containerMesh == null || !containerMesh.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Container Mesh {i} is invalid and was skipped.");
                continue;
            }

            var containerItem = CreateMeshPackItem(-1, containerMesh);
            if (containerItem == null || containerItem.Source is not MeshSource containerSource)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Container Mesh {i} could not be triangulated and was skipped.");
                continue;
            }

            containers.Add(new ContainerContext(i, IrregularMeshContainer.FromMesh(containerItem, cellSize), containerSource));
        }

        if (containers.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No valid container meshes were available for irregular-container packing.");
            return;
        }

        var items = new List<MeshPackItem>();

        for (var i = 0; i < meshes.Count; i++)
        {
            var mesh = meshes[i];
            if (mesh == null || !mesh.IsValid) continue;

            var item = CreateMeshPackItem(i, mesh);
            if (item == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Mesh {i} could not be triangulated for irregular-container packing.");
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

        var remaining = items.ToDictionary(item => item.Id, item => item);
        var packed = new List<PackedOutput>();
        var containerResults = new List<ContainerResult>();
        var packer = new GreedyMeshHeightmapPacker();

        foreach (var containerContext in containers)
        {
            if (remaining.Count == 0)
            {
                break;
            }

            var result = packer.Pack(remaining.Values, containerContext.Container, settings);
            containerResults.Add(new ContainerResult(containerContext, result));

            foreach (var placement in result.Placements)
            {
                packed.Add(new PackedOutput(placement, containerContext));
                remaining.Remove(placement.Item.Id);
            }
        }

        var placedMeshes = new List<Mesh>();
        var transforms = new List<Transform>();
        var sequence = new List<int>();
        var sourceIndices = new List<int>();
        var containerIndices = new List<int>();
        var failedMeshes = new List<Mesh>();
        var failureReasons = new List<string>();

        foreach (var output in packed)
        {
            var placement = output.Placement;
            if (placement.Item.Source is not MeshSource source) continue;

            var duplicate = source.Mesh.DuplicateMesh();
            var transform = BuildTransform(placement, source.SourceMin, output.Container.Source.SourceMin);
            duplicate.Transform(transform);

            placedMeshes.Add(duplicate);
            transforms.Add(transform);
            var sourceIndex = int.Parse(placement.Item.Id);
            sequence.Add(sourceIndex);
            sourceIndices.Add(sourceIndex);
            containerIndices.Add(output.Container.Index);
        }

        foreach (var item in remaining.Values.OrderBy(item => int.Parse(item.Id)))
        {
            if (item.Source is not MeshSource source) continue;
            failedMeshes.Add(source.Mesh);
            failureReasons.Add($"{item.Id}: No irregular-container candidate fit in any supplied container.");
        }

        var heightmaps = containerResults
            .Select(result => BuildHeightmapMesh(result.Result.Pile, result.Container.Source.SourceMin))
            .ToList();
        var containerCells = containers
            .Select(container => BuildContainerCellsMesh(container.Container, container.Source.SourceMin))
            .ToList();

        da.SetDataList(0, placedMeshes);
        da.SetDataList(1, transforms);
        da.SetDataList(2, sequence);
        da.SetDataList(3, failedMeshes);
        da.SetDataList(4, failureReasons);
        da.SetData(5, BuildReport(meshes.Count, packed.Count, remaining.Count, containers));
        da.SetDataList(6, heightmaps);
        da.SetDataList(7, containerCells);
        da.SetDataList(8, sourceIndices);
        da.SetDataList(9, containerIndices);
        da.SetData(10, new GH_ObjectWrapper(ToPackResult(containerResults, remaining.Values, settings.CellSize)));
    }

    // Adapts multi-container MeshPackResult data into a single box-based
    // PackResult that Frahan.Core.PackingMetrics.Compute and the Frahan
    // Packing Report component consume. Placements are concatenated across
    // every container; failures aggregate the still-remaining items after
    // the multi-container greedy pass. The synthetic PackContainer reports
    // a Volume equal to the summed container volumes so FillRatio remains
    // meaningful. The Heightmap field is not read by PackingMetrics; an
    // empty heightmap sized to the synthetic container is supplied for
    // shape parity.
    private static PackResult ToPackResult(
        IReadOnlyList<ContainerResult> containerResults,
        IEnumerable<MeshPackItem> unplaced,
        double cellSize)
    {
        var placements = new List<PackPlacement>();
        double totalContainerVolume = 0.0;
        for (var c = 0; c < containerResults.Count; c++)
        {
            var meshResult = containerResults[c].Result;
            for (var i = 0; i < meshResult.Placements.Count; i++)
            {
                var mp = meshResult.Placements[i];
                var item = new PackItem(mp.Item.Id, mp.OrientedGeometrySize, mp.Item.Source);
                var box = new Box3(mp.GeometryOrigin, mp.OrientedGeometrySize);
                placements.Add(new PackPlacement(item, box, mp.YawDegrees, mp.Score, mp.Sequence));
            }

            if (meshResult.Container != null)
            {
                totalContainerVolume += meshResult.Container.Volume;
            }
        }

        var failures = new List<PackFailure>();
        foreach (var item in unplaced)
        {
            var fallbackSize = item.Bounds.Size;
            var fallbackItem = new PackItem(item.Id, fallbackSize, item.Source);
            failures.Add(new PackFailure(fallbackItem, "No irregular-container candidate fit in any supplied container."));
        }

        // Synthetic aggregate container: cube-rooted dimensions give
        // Volume = totalContainerVolume while keeping each side bounded so
        // the Heightmap allocation below stays tiny. PackContainer's
        // constructor requires strictly-positive width/depth/height.
        var aggregateVolume = Math.Max(1e-9, totalContainerVolume);
        var side = Math.Pow(aggregateVolume, 1.0 / 3.0);
        var aggregate = new PackContainer(side, side, side);
        // Heightmap is not read by PackingMetrics; size to a single cell so
        // the synthetic array allocation is constant-cost regardless of
        // model-unit magnitude.
        var heightmap = new Heightmap(aggregate, side);
        return new PackResult(placements, failures, heightmap, aggregate);
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

    private static string BuildReport(int inputCount, int placedCount, int failedCount, IReadOnlyList<ContainerContext> containers)
    {
        var allowedCells = 0;
        foreach (var containerContext in containers)
        {
            var container = containerContext.Container;
            for (var x = 0; x < container.WidthCells; x++)
            {
                for (var y = 0; y < container.DepthCells; y++)
                {
                    if (container.IsAllowed(x, y)) allowedCells++;
                }
            }
        }

        return $"Frahan StonePack 0.1.0 irregular-container | Input: {inputCount} | Containers: {containers.Count} | Placed: {placedCount} | Failed: {failedCount} | Allowed cells: {allowedCells}";
    }

    private static Mesh BuildHeightmapMesh(MeshPileHeightmap pile, Point3d origin)
    {
        var mesh = new Mesh();
        for (var x = 0; x < pile.WidthCells; x++)
        {
            for (var y = 0; y < pile.DepthCells; y++)
            {
                if (!pile.IsAllowed(x, y)) continue;
                AddCellFace(mesh, origin, x, y, pile.CellSize, pile[x, y]);
            }
        }

        mesh.Normals.ComputeNormals();
        mesh.Compact();
        return mesh;
    }

    private static Mesh BuildContainerCellsMesh(IrregularMeshContainer container, Point3d origin)
    {
        var mesh = new Mesh();
        for (var x = 0; x < container.WidthCells; x++)
        {
            for (var y = 0; y < container.DepthCells; y++)
            {
                if (!container.IsAllowed(x, y)) continue;
                AddCellFace(mesh, origin, x, y, container.CellSize, container.CeilingAt(x, y));
            }
        }

        mesh.Normals.ComputeNormals();
        mesh.Compact();
        return mesh;
    }

    private static void AddCellFace(Mesh mesh, Point3d origin, int x, int y, double cellSize, double zOffset)
    {
        var x0 = origin.X + x * cellSize;
        var y0 = origin.Y + y * cellSize;
        var z = origin.Z + zOffset;
        var i = mesh.Vertices.Count;
        mesh.Vertices.Add(x0, y0, z);
        mesh.Vertices.Add(x0 + cellSize, y0, z);
        mesh.Vertices.Add(x0 + cellSize, y0 + cellSize, z);
        mesh.Vertices.Add(x0, y0 + cellSize, z);
        mesh.Faces.AddFace(i, i + 1, i + 2, i + 3);
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

    private readonly struct ContainerContext
    {
        public ContainerContext(int index, IrregularMeshContainer container, MeshSource source)
        {
            Index = index;
            Container = container;
            Source = source;
        }

        public int Index { get; }
        public IrregularMeshContainer Container { get; }
        public MeshSource Source { get; }
    }

    private readonly struct PackedOutput
    {
        public PackedOutput(MeshPackPlacement placement, ContainerContext container)
        {
            Placement = placement;
            Container = container;
        }

        public MeshPackPlacement Placement { get; }
        public ContainerContext Container { get; }
    }

    private readonly struct ContainerResult
    {
        public ContainerResult(ContainerContext container, MeshPackResult result)
        {
            Container = container;
            Result = result;
        }

        public ContainerContext Container { get; }
        public MeshPackResult Result { get; }
    }
}
