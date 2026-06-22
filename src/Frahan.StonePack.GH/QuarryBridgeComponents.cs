#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Quarry.BlockCutOpt;
using Frahan.Masonry.Quarry.CutOpt;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Quarry
{
    // =========================================================================
    // Bridge components added 2026-05-14 to close the gaps documented in
    // outputs/2026-05-14/connection_map/FRAHAN_PIPELINE_MAP.md §8.
    //
    // - FrahanBlockCutOptExtractGridComponent  (§8.2)
    // - FrahanBenchBlockToSlabsComponent       (§8.1)
    // - FrahanMeshAsFractureComponent          (bonus: skip-the-PLY)
    // - FrahanMeshFacesToFracturePlanesComponent (Mesh -> FracturePlane list
    //   for SlabCutter consumption)
    // =========================================================================

    [DesignApplication(
        "Brute-force search + extract the winning OrientedBlock grid",
        DesignFlow.Bridges)]
    public sealed class FrahanBlockCutOptExtractGridComponent : FrahanComponentBase
    {
        public FrahanBlockCutOptExtractGridComponent()
            : base(
                "BlockCutOpt Extract Grid", "BCOExtract",
                "Brute-force search + extract the winning OrientedBlock grid. " +
                "Outputs the non-intersected blocks as Rhino Boxes plus the " +
                "BlockCutOptResult headline numbers.",
                "Frahan", "Block Cutting")
        { }

        public override Guid ComponentGuid =>
            new Guid("F7A13001-0001-4F2D-A0B0-7E60CADA17C1");

        public override GH_Exposure Exposure => Frahan.GH.Attributes.LabConfig.EffectiveExposure(ComponentGuid, GH_Exposure.tertiary);

        protected override Bitmap Icon => IconProvider.Load("BlockCutOpt.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddBoxParameter("Tested Area", "A", "Bench bounding box (m).", GH_ParamAccess.item);
            p.AddMeshParameter("Fractures", "F", "Fracture mesh.", GH_ParamAccess.item);
            p.AddNumberParameter("Block X", "Lx", "Block length (m).", GH_ParamAccess.item, 3.0);
            p.AddNumberParameter("Block Y", "Ly", "Block width (m).", GH_ParamAccess.item, 2.0);
            p.AddNumberParameter("Block Z", "Lz", "Block height (m).", GH_ParamAccess.item, 0.8);
            p.AddNumberParameter("Kerf", "K", "Material-lost-by-quarrying (m).", GH_ParamAccess.item, BlockCutOptTolerances.KerfDefaultMetres);
            p.AddNumberParameter("Psi Step (deg)", "Pdeg", "Angular search step.", GH_ParamAccess.item, 3.0);
            p.AddNumberParameter("Dx Max", "Dx", "Half-range of dx (m).", GH_ParamAccess.item, 1.5);
            p.AddNumberParameter("Dx Step", "DxS", "Dx step (m).", GH_ParamAccess.item, 0.5);
            p.AddNumberParameter("Dy Max", "Dy", "Half-range of dy (m).", GH_ParamAccess.item, 1.5);
            p.AddNumberParameter("Dy Step", "DyS", "Dy step (m).", GH_ParamAccess.item, 0.5);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBoxParameter("Boxes", "B", "Non-intersected blocks as Rhino Boxes.", GH_ParamAccess.list);
            p.AddIntegerParameter("Count", "N", "Number of non-intersected blocks.", GH_ParamAccess.item);
            p.AddNumberParameter("Recovery %", "R", "Recovery percentage.", GH_ParamAccess.item);
            p.AddNumberParameter("Best Psi (deg)", "Psi", "Optimum cutting direction.", GH_ParamAccess.item);
            p.AddNumberParameter("Best Dx (m)", "Dx", "Optimum dx.", GH_ParamAccess.item);
            p.AddNumberParameter("Best Dy (m)", "Dy", "Optimum dy.", GH_ParamAccess.item);
            p.AddNumberParameter("Elapsed (ms)", "T", "Wall-clock duration.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            Box area = Box.Empty;
            Mesh fxMesh = null;
            double Lx = 3.0, Ly = 2.0, Lz = 0.8;
            double kerf = BlockCutOptTolerances.KerfDefaultMetres;
            double psiDeg = 3.0;
            double dxMax = 1.5, dxStep = 0.5, dyMax = 1.5, dyStep = 0.5;

            if (!da.GetData(0, ref area) || !area.IsValid)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid tested-area box."); return; }
            if (!da.GetData(1, ref fxMesh) || fxMesh == null)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Fracture mesh required."); return; }
            da.GetData(2, ref Lx); da.GetData(3, ref Ly); da.GetData(4, ref Lz);
            da.GetData(5, ref kerf); da.GetData(6, ref psiDeg);
            da.GetData(7, ref dxMax); da.GetData(8, ref dxStep);
            da.GetData(9, ref dyMax); da.GetData(10, ref dyStep);

            var bbox = GhBlockCutOptInterop.BoxToBbox(area);
            var ply = GhBlockCutOptInterop.RhinoMeshToPly(fxMesh);
            var opts = new BlockCutOptOptions(
                Lx, Ly, Lz, kerf,
                psiStartRad: 0.0, psiStopRad: Math.PI,
                psiStepRad: BlockCutOptTolerances.DegToRad(psiDeg),
                dxMax: dxMax, dxStep: dxStep,
                dyMax: dyMax, dyStep: dyStep);
            var (r, grid) = BlockCutOptSolver.SolveAndExtract(bbox, ply, opts);

            var boxes = new List<Box>(grid.Count);
            for (int i = 0; i < grid.Count; i++) boxes.Add(OrientedBlockToRhinoBox(grid[i]));

            da.SetDataList(0, boxes);
            da.SetData(1, grid.Count);
            da.SetData(2, r.RecoveryPercent);
            da.SetData(3, r.BestPsiDeg);
            da.SetData(4, r.BestDx);
            da.SetData(5, r.BestDy);
            da.SetData(6, r.Elapsed.TotalMilliseconds);
        }

        internal static Box OrientedBlockToRhinoBox(OrientedBlock obb)
        {
            var origin = new Point3d(obb.CenterX, obb.CenterY, obb.CenterZ);
            var xAxis = new Vector3d(obb.UX, obb.UY, obb.UZ);
            var yAxis = new Vector3d(obb.VX, obb.VY, obb.VZ);
            // Plane(origin, xAxis, yAxis) derives the Z axis as cross(x, y);
            // for our orthonormal U/V/W triple this matches W up to a sign.
            var plane = new Plane(origin, xAxis, yAxis);
            var xi = new Interval(-obb.HalfX, obb.HalfX);
            var yi = new Interval(-obb.HalfY, obb.HalfY);
            var zi = new Interval(-obb.HalfZ, obb.HalfZ);
            return new Box(plane, xi, yi, zi);
        }
    }

    [DesignApplication(
        "Run BlockCutOpt per BenchBlock in the ExtractionPlan order  and emit the winning cut-grid as Slabs (Mesh form)",
        DesignFlow.Bridges)]
    public sealed class FrahanBenchBlockToSlabsComponent : FrahanComponentBase
    {
        public FrahanBenchBlockToSlabsComponent()
            : base(
                "BenchBlock Cut → Slabs", "QCut",
                "Run BlockCutOpt per BenchBlock in the ExtractionPlan order " +
                "and emit the winning cut-grid as Slabs (Mesh form). " +
                "Closes the Layer 7 → Layer 5 / 6 handoff.",
                "Frahan", "Block Cutting")
        { }

        public override Guid ComponentGuid =>
            new Guid("F7A13002-0001-4F2D-A0B0-7E60CADA17C2");

        public override GH_Exposure Exposure => Frahan.GH.Attributes.LabConfig.EffectiveExposure(ComponentGuid, GH_Exposure.quarternary);

        protected override Bitmap Icon => IconProvider.Load("QuarryCutOpt.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Inventory", "Inv", "QuarryInventory.", GH_ParamAccess.item);
            p.AddGenericParameter("Plan", "P", "ExtractionPlan (accepted blocks are cut in plan order).", GH_ParamAccess.item);
            p.AddMeshParameter("Fractures", "F", "Fracture mesh.", GH_ParamAccess.item);
            p.AddNumberParameter("Product X (m)", "Lx", "Dimension-block target X.", GH_ParamAccess.item, 3.0);
            p.AddNumberParameter("Product Y (m)", "Ly", "Dimension-block target Y.", GH_ParamAccess.item, 2.0);
            p.AddNumberParameter("Product Z (m)", "Lz", "Dimension-block target Z.", GH_ParamAccess.item, 0.8);
            p.AddNumberParameter("Kerf (m)", "K", "Saw kerf.", GH_ParamAccess.item, BlockCutOptTolerances.KerfDefaultMetres);
            p.AddNumberParameter("Psi Step (deg)", "Pdeg", "Angular search step.", GH_ParamAccess.item, 5.0);
            p.AddNumberParameter("Dx Max", "Dx", "Half-range of dx (m).", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Dx Step", "DxS", "Dx step (m).", GH_ParamAccess.item, 0.5);
            p.AddNumberParameter("Dy Max", "Dy", "Half-range of dy (m).", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Dy Step", "DyS", "Dy step (m).", GH_ParamAccess.item, 0.5);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Slabs", "S", "Per-BenchBlock cut slabs concatenated in plan order. Wire into Ashlar Pack.", GH_ParamAccess.list);
            p.AddTextParameter("Block Ids", "I", "BenchBlock id parallel to each slab.", GH_ParamAccess.list);
            p.AddIntegerParameter("Counts", "N", "Slab count per BenchBlock (parallel to ExtractionPlan.Accepted).", GH_ParamAccess.list);
            p.AddGenericParameter("Cut Results", "C", "List of BenchBlockCutResult objects.", GH_ParamAccess.list);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var invW = new GH_ObjectWrapper();
            var planW = new GH_ObjectWrapper();
            Mesh fxMesh = null;
            double Lx = 3.0, Ly = 2.0, Lz = 0.8;
            double kerf = BlockCutOptTolerances.KerfDefaultMetres;
            double psiDeg = 5.0;
            double dxMax = 1.0, dxStep = 0.5, dyMax = 1.0, dyStep = 0.5;

            if (!da.GetData(0, ref invW) || !(invW.Value is QuarryInventory inv))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Inventory is not a QuarryInventory."); return; }
            if (!da.GetData(1, ref planW) || !(planW.Value is ExtractionPlan plan))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Plan is not an ExtractionPlan."); return; }
            if (!da.GetData(2, ref fxMesh) || fxMesh == null)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Fracture mesh required."); return; }
            da.GetData(3, ref Lx); da.GetData(4, ref Ly); da.GetData(5, ref Lz);
            da.GetData(6, ref kerf); da.GetData(7, ref psiDeg);
            da.GetData(8, ref dxMax); da.GetData(9, ref dxStep);
            da.GetData(10, ref dyMax); da.GetData(11, ref dyStep);

            var ply = GhBlockCutOptInterop.RhinoMeshToPly(fxMesh);
            var opts = new BlockCutOptOptions(
                Lx, Ly, Lz, kerf,
                psiStartRad: 0.0, psiStopRad: Math.PI,
                psiStepRad: BlockCutOptTolerances.DegToRad(psiDeg),
                dxMax: dxMax, dxStep: dxStep,
                dyMax: dyMax, dyStep: dyStep);

            IReadOnlyList<BenchBlockCutResult> results;
            try { results = BenchBlockSlabBuilder.CutPlan(plan, inv, ply, opts); }
            catch (Exception ex)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); return; }

            var slabMeshes = new List<Mesh>();
            var ids = new List<string>();
            var counts = new List<int>(results.Count);
            var wrappers = new List<GH_ObjectWrapper>(results.Count);
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                counts.Add(r.SlabCount);
                wrappers.Add(new GH_ObjectWrapper(r));
                for (int k = 0; k < r.Slabs.Count; k++)
                {
                    slabMeshes.Add(SlabToRhinoMesh(r.Slabs[k]));
                    ids.Add($"{r.BlockId}::cut{k:D4}");
                }
            }
            da.SetDataList(0, slabMeshes);
            da.SetDataList(1, ids);
            da.SetDataList(2, counts);
            da.SetDataList(3, wrappers);
        }

        internal static Mesh SlabToRhinoMesh(Slab slab)
        {
            var m = new Mesh();
            for (int i = 0; i < slab.VertexCount; i++)
            {
                m.Vertices.Add(
                    slab.VertexCoordsXyz[3 * i + 0],
                    slab.VertexCoordsXyz[3 * i + 1],
                    slab.VertexCoordsXyz[3 * i + 2]);
            }
            for (int fi = 0; fi < slab.FaceCount; fi++)
            {
                var face = slab.Faces[fi];
                // fan-triangulate the polygon face
                for (int k = 1; k + 1 < face.Count; k++)
                {
                    m.Faces.AddFace(face[0], face[k], face[k + 1]);
                }
            }
            m.Normals.ComputeNormals();
            m.Compact();
            return m;
        }
    }

    [Algorithm("Mesh-face to fracture-plane conversion", "Frahan-original", Note = "Per-face centroid + normal to plane; trivial geometry glue, no prior art.")]
    [DesignApplication(
        "Convert a hand-drawn Rhino Mesh into a List<FracturePlane>  consumable by Slab Cut By Fractures",
        DesignFlow.Bridges)]
    public sealed class FrahanMeshFacesToFracturePlanesComponent : FrahanComponentBase
    {
        public FrahanMeshFacesToFracturePlanesComponent()
            : base(
                "Mesh → Fracture Planes", "Mesh2FxPl",
                "Convert a hand-drawn Rhino Mesh into a List<FracturePlane> " +
                "consumable by Slab Cut By Fractures. One plane per face " +
                "(centroid + face normal). Lets you author fractures on the " +
                "Rhino canvas without going through a PLY file. Frahan-original method.",
                "Frahan", "Block Cutting")
        { }

        public override Guid ComponentGuid =>
            new Guid("F7A13003-0001-4F2D-A0B0-7E60CADA17C3");

        public override GH_Exposure Exposure => Frahan.GH.Attributes.LabConfig.EffectiveExposure(ComponentGuid, GH_Exposure.primary);

        protected override Bitmap Icon => IconProvider.Load("DefectMap.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Mesh", "M", "Rhino mesh whose faces become fracture planes.", GH_ParamAccess.item);
            p.AddBooleanParameter("Unitize Normals", "U", "Re-normalise face normals (recommended).", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Fracture Planes", "F", "List<FracturePlane> for Slab Cut By Fractures.", GH_ParamAccess.list);
            p.AddPlaneParameter("Rhino Planes", "Pl", "Same fractures as Rhino Planes for preview.", GH_ParamAccess.list);
            p.AddIntegerParameter("Count", "N", "Number of fracture planes.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            Mesh mesh = null;
            bool unit = true;
            if (!da.GetData(0, ref mesh) || mesh == null)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh required."); return; }
            da.GetData(1, ref unit);

            if (mesh.FaceNormals.Count != mesh.Faces.Count)
                mesh.FaceNormals.ComputeFaceNormals();

            var planes = new List<GH_ObjectWrapper>(mesh.Faces.Count);
            var rhPlanes = new List<Plane>(mesh.Faces.Count);
            int kept = 0;
            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                var f = mesh.Faces[i];
                Point3d centroid;
                if (f.IsQuad)
                {
                    var a = mesh.Vertices[f.A];
                    var b = mesh.Vertices[f.B];
                    var c = mesh.Vertices[f.C];
                    var d = mesh.Vertices[f.D];
                    centroid = new Point3d(
                        0.25 * (a.X + b.X + c.X + d.X),
                        0.25 * (a.Y + b.Y + c.Y + d.Y),
                        0.25 * (a.Z + b.Z + c.Z + d.Z));
                }
                else
                {
                    var a = mesh.Vertices[f.A];
                    var b = mesh.Vertices[f.B];
                    var c = mesh.Vertices[f.C];
                    centroid = new Point3d(
                        (a.X + b.X + c.X) / 3.0,
                        (a.Y + b.Y + c.Y) / 3.0,
                        (a.Z + b.Z + c.Z) / 3.0);
                }
                var n = mesh.FaceNormals[i];
                if (unit)
                {
                    var v = new Vector3d(n.X, n.Y, n.Z);
                    if (v.Length < 1e-12) continue;
                    v.Unitize();
                    n = new Vector3f((float)v.X, (float)v.Y, (float)v.Z);
                }
                FracturePlane fp;
                try
                {
                    fp = new FracturePlane(centroid.X, centroid.Y, centroid.Z, n.X, n.Y, n.Z);
                }
                catch (ArgumentException)
                {
                    continue; // skip zero-normal face
                }
                planes.Add(new GH_ObjectWrapper(fp));
                rhPlanes.Add(new Plane(centroid, new Vector3d(n.X, n.Y, n.Z)));
                kept++;
            }
            da.SetDataList(0, planes);
            da.SetDataList(1, rhPlanes);
            da.SetData(2, kept);
        }
    }
}
