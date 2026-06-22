#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Quarry;
using Frahan.Masonry.Quarry.BlockCutOpt;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.Quarry
{
    // =========================================================================
    // BlockCutOptLoadFracturesComponent -- multi-format fracture loader.
    //
    // Auto-dispatches by file extension via FractureInputReader (PLY, CSV,
    // .lines, .txt). Output is a Rhino Mesh ready to feed the solver.
    //
    // ComponentGuid: F2D0BC01-1234-4F2D-A0B0-7E60CADA15A1
    // =========================================================================

    [DesignApplication(
        "Load fractures from disk (PLY, CSV, .lines, .txt)",
        DesignFlow.TopDown)]
    public sealed class BlockCutOptLoadFracturesComponent : FrahanComponentBase
    {
        public BlockCutOptLoadFracturesComponent()
            : base(
                "BlockCutOpt Load Fractures", "BCOLoadFx",
                "Load fractures from disk (PLY, CSV, .lines, .txt). World " +
                "coordinates in metres. For 2D-trace formats, zMin / zMax " +
                "define the vertical extrusion range. Output is a Rhino " +
                "Mesh consumable by BlockCutOpt Solve.",
                "Frahan", "Block Cutting")
        { }

        public override Guid ComponentGuid =>
            new Guid("F2D0BC01-1234-4F2D-A0B0-7E60CADA15A1");

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override Bitmap Icon => IconProvider.Load("DefectMap.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Path", "P", "File path. .ply / .csv / .lines / .txt", GH_ParamAccess.item);
            p.AddNumberParameter("Z Min", "Zmin", "Bottom of vertical extrusion (m). Ignored for PLY.", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Z Max", "Zmax", "Top of vertical extrusion (m). Ignored for PLY.", GH_ParamAccess.item, 1.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Fractures", "F", "Rhino Mesh of fracture triangles.", GH_ParamAccess.item);
            p.AddIntegerParameter("Triangle Count", "N", "Number of fracture triangles.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            string path = null;
            double zMin = 0.0, zMax = 1.0;
            if (!da.GetData(0, ref path) || string.IsNullOrWhiteSpace(path))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Path is required.");
                return;
            }
            da.GetData(1, ref zMin);
            da.GetData(2, ref zMax);
            if (!(zMax > zMin))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Zmax must exceed Zmin.");
                return;
            }

            PlyMesh ply;
            try { ply = FractureInputReader.Load(path, zMin, zMax); }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Load failed: {ex.Message}");
                return;
            }

            var mesh = GhBlockCutOptInterop.PlyToRhinoMesh(ply);
            da.SetData(0, mesh);
            da.SetData(1, ply.TriangleCount);
        }
    }

    // =========================================================================
    // BlockCutOptSolveComponent -- run the brute-force solver.
    //
    // ComponentGuid: F2D0BC02-1234-4F2D-A0B0-7E60CADA15A2
    // =========================================================================

    [Algorithm("BlockCutOpt brute-force search", "Elkarmoty Bondua Bruno 2020, Resources Policy 68:101761", Doi = "10.1016/j.resourpol.2020.101761", WikiPath = "wiki/papers/equations_and_diagrams/00_blockcutopt_final_published.md")]
    [Algorithm("Full 3D rotation grid", "Frahan I1 improvement over Elkarmoty 2020 psi-only", Note = "Adds theta and phi axes")]
    [Algorithm("Triangle-AABB BVH pruning", "Akenine-Moller 2001 fast 3D triangle-box overlap", Note = "Frahan I2 acceleration")]
    [Algorithm("Coarse-to-fine angular search", "Frahan I4 refinement, 12deg to 3deg to 0.5deg", Note = "Frahan-original")]
        [DesignApplication(
        "Brute-force search for the optimum cutting direction +  displacement that maximises the count of non-inters...",
        DesignFlow.TopDown,
        Precedent = "Elkarmoty 2020 + Goodman 1985 key-block; Akenine-Moller 2001 SAT tri-box; Frahan BlockCutOpt v2 synthesis",
        CardSet = "wiki/research/hitl_cards/td_blockcutopt_pareto/")]
    public sealed class BlockCutOptSolveComponent : FrahanComponentBase
    {
        public BlockCutOptSolveComponent()
            : base(
                "BlockCutOpt Solve", "BCOSolve",
                "Brute-force search for the optimum cutting direction + " +
                "displacement that maximises the count of non-intersected " +
                "blocks. All units in metres. [Elkarmoty et al. 2020]",
                "Frahan", "Block Cutting")
        { }

        public override Guid ComponentGuid =>
            new Guid("F2D0BC02-1234-4F2D-A0B0-7E60CADA15A2");

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

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
            p.AddNumberParameter("Dx Max", "Dx", "Half-range of dx search (m).", GH_ParamAccess.item, 1.5);
            p.AddNumberParameter("Dx Step", "DxS", "Dx step (m).", GH_ParamAccess.item, 0.5);
            p.AddNumberParameter("Dy Max", "Dy", "Half-range of dy search (m).", GH_ParamAccess.item, 1.5);
            p.AddNumberParameter("Dy Step", "DyS", "Dy step (m).", GH_ParamAccess.item, 0.5);
            // Run gate (2026-06-11). Appended LAST so old canvases load
            // unchanged (the new param takes its default, false). The
            // pose-grid search is unbounded on canvas drop without it.
            p.AddBooleanParameter("Run", "R", "Execute the solve (the search is expensive; bound it before running)", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddIntegerParameter("Non-Intersected Count", "N", "Best non-intersected block count.", GH_ParamAccess.item);
            p.AddNumberParameter("Recovery %", "R", "Recovery percentage.", GH_ParamAccess.item);
            p.AddNumberParameter("Best Psi (deg)", "Psi", "Optimum cutting direction.", GH_ParamAccess.item);
            p.AddNumberParameter("Best Dx (m)", "Dx", "Optimum dx.", GH_ParamAccess.item);
            p.AddNumberParameter("Best Dy (m)", "Dy", "Optimum dy.", GH_ParamAccess.item);
            p.AddIntegerParameter("Evaluations", "E", "Total (psi, dx, dy) samples evaluated.", GH_ParamAccess.item);
            p.AddNumberParameter("Elapsed (ms)", "T", "Wall-clock duration.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            // Run gate first (matches the IfcExportComponent pattern):
            // the brute-force pose-grid search must not fire on canvas drop.
            bool run = false;
            da.GetData(11, ref run);
            if (!run)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Set Run = true to solve.");
                Message = "Set Run = true to solve.";
                return;
            }

            var box = Box.Empty;
            Mesh fxMesh = null;
            double Lx = 3.0, Ly = 2.0, Lz = 0.8;
            double kerf = BlockCutOptTolerances.KerfDefaultMetres;
            double psiDeg = 3.0;
            double dxMax = 1.5, dxStep = 0.5, dyMax = 1.5, dyStep = 0.5;
            if (!da.GetData(0, ref box) || !box.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid tested-area box.");
                return;
            }
            if (!da.GetData(1, ref fxMesh) || fxMesh == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Fracture mesh is required.");
                return;
            }
            da.GetData(2, ref Lx); da.GetData(3, ref Ly); da.GetData(4, ref Lz);
            da.GetData(5, ref kerf);
            da.GetData(6, ref psiDeg);
            da.GetData(7, ref dxMax); da.GetData(8, ref dxStep);
            da.GetData(9, ref dyMax); da.GetData(10, ref dyStep);

            var area = GhBlockCutOptInterop.BoxToBbox(box);
            var ply = GhBlockCutOptInterop.RhinoMeshToPly(fxMesh);
            var opts = new BlockCutOptOptions(
                Lx, Ly, Lz, kerf,
                psiStartRad: 0.0, psiStopRad: Math.PI,
                psiStepRad: BlockCutOptTolerances.DegToRad(psiDeg),
                dxMax: dxMax, dxStep: dxStep,
                dyMax: dyMax, dyStep: dyStep);
            var r = BlockCutOptSolver.Solve(area, ply, opts);

            da.SetData(0, r.NonIntersectedCount);
            da.SetData(1, r.RecoveryPercent);
            da.SetData(2, r.BestPsiDeg);
            da.SetData(3, r.BestDx);
            da.SetData(4, r.BestDy);
            da.SetData(5, (int)Math.Min(r.TotalEvaluations, int.MaxValue));
            da.SetData(6, r.Elapsed.TotalMilliseconds);
        }
    }

    // =========================================================================
    // BlockCutOptAmrrPlanComponent -- Shao 2022 in-block plane-sequence cut.
    //
    // ComponentGuid: F2D0BC03-1234-4F2D-A0B0-7E60CADA15A3
    // =========================================================================

    [Algorithm("AMRR in-block plane-sequence cutting", "Shao, Liu, Gao 2022, AMRR in-block plane-sequence cutting strategy, Processes (MDPI)", WikiPath = "wiki/index/references.md")]
    [DesignApplication(
        "Plan a sequence of plane cuts (Shao 2022) that reduces the  starting block toward a target bounding sphere",
        DesignFlow.TopDown)]
    public sealed class BlockCutOptAmrrPlanComponent : FrahanComponentBase
    {
        public BlockCutOptAmrrPlanComponent()
            : base(
                "BlockCutOpt AMRR Plan", "BCOAmrr",
                "Plan a sequence of plane cuts (Shao 2022) that reduces the " +
                "starting block toward a target bounding sphere. Maximises " +
                "the average material removal rate. Implements AMRR in-block plane-sequence cutting (Shao 2022).",
                "Frahan", "Block Cutting")
        { }

        public override Guid ComponentGuid =>
            new Guid("F2D0BC03-1234-4F2D-A0B0-7E60CADA15A3");

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override Bitmap Icon => IconProvider.Load("QuarryCutOpt.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddBoxParameter("Blank Block", "B", "Starting block (m).", GH_ParamAccess.item);
            p.AddPointParameter("Target Center", "C", "Target sphere centre.", GH_ParamAccess.item);
            p.AddNumberParameter("Target Radius", "R", "Target sphere radius (m).", GH_ParamAccess.item);
            p.AddNumberParameter("Sawblade Radius (mm)", "SBR", "Sawblade radius in mm.", GH_ParamAccess.item, BlockCutOptTolerances.SawBladeRadiusMmDefault);
            p.AddNumberParameter("Feed Speed (mm/min)", "FS", "Feed speed in mm/min.", GH_ParamAccess.item, BlockCutOptTolerances.FeedSpeedMmPerMinDefault);
            p.AddIntegerParameter("Max Cuts", "MC", "Iteration cap.", GH_ParamAccess.item, 50);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddPlaneParameter("Cut Planes", "P", "Sequence of cutting planes.", GH_ParamAccess.list);
            p.AddNumberParameter("Removed Volume (m^3)", "V", "Removed volume per step.", GH_ParamAccess.list);
            p.AddNumberParameter("Cutting Time (min)", "T", "Cutting time per step.", GH_ParamAccess.list);
            p.AddNumberParameter("Material Removal %", "MRP", "Overall material removal percentage.", GH_ParamAccess.item);
            p.AddNumberParameter("AMRR (mm^3/min)", "AMRR", "Average material removal rate.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var box = Box.Empty;
            var center = Point3d.Origin;
            double radius = 0.5;
            double sbrMm = BlockCutOptTolerances.SawBladeRadiusMmDefault;
            double fsMmMin = BlockCutOptTolerances.FeedSpeedMmPerMinDefault;
            int maxCuts = 50;
            if (!da.GetData(0, ref box) || !box.IsValid) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid blank."); return; }
            if (!da.GetData(1, ref center)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Centre required."); return; }
            if (!da.GetData(2, ref radius) || !(radius > 0))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Radius must be positive."); return; }
            da.GetData(3, ref sbrMm); da.GetData(4, ref fsMmMin); da.GetData(5, ref maxCuts);

            var obb = GhBlockCutOptInterop.BoxToOrientedBlock(box);
            var blank = ConvexPolyhedron.FromOrientedBlock(in obb);
            var amrrOpts = new AmrrPlannerOptions
            {
                SawBladeRadiusMetres = BlockCutOptTolerances.MmToMetres(sbrMm),
                FeedSpeedMetresPerMin = BlockCutOptTolerances.MmToMetres(fsMmMin),
                MaxCuts = maxCuts,
            };
            var plan = AmrrPlanner.PlanBoundingSphere(
                blank, center.X, center.Y, center.Z, radius, amrrOpts);

            var planes = new List<Plane>(plan.Steps.Count);
            var vols = new List<double>(plan.Steps.Count);
            var times = new List<double>(plan.Steps.Count);
            foreach (var step in plan.Steps)
            {
                planes.Add(new Plane(
                    new Point3d(step.PlanePx, step.PlanePy, step.PlanePz),
                    new Vector3d(step.PlaneNx, step.PlaneNy, step.PlaneNz)));
                vols.Add(step.RemovalVolumeMetres3);
                times.Add(step.CuttingTimeMin);
            }
            da.SetDataList(0, planes);
            da.SetDataList(1, vols);
            da.SetDataList(2, times);
            da.SetData(3, plan.MaterialRemovalPercent);
            da.SetData(4, plan.Amrr * 1.0e9);
        }
    }

    // =========================================================================
    // BlockCutOptOmniSolveComponent -- sub-divided multi-objective solver.
    //
    // ComponentGuid: F2D0BC04-1234-4F2D-A0B0-7E60CADA15A4
    // =========================================================================

    [Algorithm("BlockCutOpt omni-solve (Pareto over recovery/revenue/risk/cut-area)", "Elkarmoty Bondua Bruno 2020, Resources Policy 68:101761", Doi = "10.1016/j.resourpol.2020.101761", WikiPath = "wiki/index/references.md")]
    [Algorithm("BCSdbBV cost objective (cutting-surface area / block value)", "Jalalian 2023 BCSdbBV", WikiPath = "wiki/index/references.md")]
    [DesignApplication(
        "Run the omni-solver: uniform (mx, my) sub-division per zone,  4-axis Pareto multi-objective (recovery, reve...",
        DesignFlow.TopDown)]
    public sealed class BlockCutOptOmniSolveComponent : FrahanComponentBase
    {
        public BlockCutOptOmniSolveComponent()
            : base(
                "BlockCutOpt Omni Solve", "BCOOmni",
                "Run the omni-solver: uniform (mx, my) sub-division per zone, " +
                "4-axis Pareto multi-objective (recovery, revenue, kerf-time, " +
                "BCSdbBV). Returns one row per zone. Implements BlockCutOpt omni-solve (Elkarmoty 2020; Jalalian 2023).",
                "Frahan", "Block Cutting")
        { }

        public override Guid ComponentGuid =>
            new Guid("F2D0BC04-1234-4F2D-A0B0-7E60CADA15A4");

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override Bitmap Icon => IconProvider.Load("BlockCutOpt.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddBoxParameter("Tested Area", "A", "Bench bounding box (m).", GH_ParamAccess.item);
            p.AddMeshParameter("Fractures", "F", "Fracture mesh.", GH_ParamAccess.item);
            p.AddIntegerParameter("Mx", "Mx", "Sub-divisions in X.", GH_ParamAccess.item, 1);
            p.AddIntegerParameter("My", "My", "Sub-divisions in Y.", GH_ParamAccess.item, 1);
            p.AddNumberParameter("Block X", "Lx", "Block length (m).", GH_ParamAccess.item, 3.0);
            p.AddNumberParameter("Block Y", "Ly", "Block width (m).", GH_ParamAccess.item, 2.0);
            p.AddNumberParameter("Block Z", "Lz", "Block height (m).", GH_ParamAccess.item, 0.8);
            p.AddNumberParameter("Kerf", "K", "Material-lost-by-quarrying (m).", GH_ParamAccess.item, BlockCutOptTolerances.KerfDefaultMetres);
            p.AddNumberParameter("Psi Step (deg)", "Pdeg", "Angular search step.", GH_ParamAccess.item, 3.0);
            // Run gate (2026-06-11). Appended LAST so old canvases load
            // unchanged (the new param takes its default, false). The
            // per-zone pose-grid search is unbounded on canvas drop without it.
            p.AddBooleanParameter("Run", "R", "Execute the solve (the search is expensive; bound it before running)", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Zone Id", "Z", "Sub-zone identifier (i, j).", GH_ParamAccess.list);
            p.AddIntegerParameter("Best Recovery Count", "N", "Best recovery count per zone.", GH_ParamAccess.list);
            p.AddNumberParameter("Best Revenue", "Pi", "Best revenue per zone.", GH_ParamAccess.list);
            p.AddNumberParameter("Best BCSdbBV", "BCS", "Best BCSdbBV cost per zone.", GH_ParamAccess.list);
            p.AddNumberParameter("Best Psi (deg)", "Psi", "Recovery-optimal psi per zone.", GH_ParamAccess.list);
            p.AddIntegerParameter("Aggregate Recovery", "R", "Sum of recovery counts.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            // Run gate first (matches the IfcExportComponent pattern):
            // the per-zone pose-grid search must not fire on canvas drop.
            bool run = false;
            da.GetData(9, ref run);
            if (!run)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Set Run = true to solve.");
                Message = "Set Run = true to solve.";
                return;
            }

            var box = Box.Empty;
            Mesh fxMesh = null;
            int mx = 1, my = 1;
            double Lx = 3.0, Ly = 2.0, Lz = 0.8;
            double kerf = BlockCutOptTolerances.KerfDefaultMetres;
            double psiDeg = 3.0;
            if (!da.GetData(0, ref box) || !box.IsValid) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid area."); return; }
            if (!da.GetData(1, ref fxMesh) || fxMesh == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Fracture mesh required."); return; }
            da.GetData(2, ref mx); da.GetData(3, ref my);
            da.GetData(4, ref Lx); da.GetData(5, ref Ly); da.GetData(6, ref Lz);
            da.GetData(7, ref kerf); da.GetData(8, ref psiDeg);

            var area = GhBlockCutOptInterop.BoxToBbox(box);
            var ply = GhBlockCutOptInterop.RhinoMeshToPly(fxMesh);
            var search = new BlockCutOptOptions(
                Lx, Ly, Lz, kerf,
                psiStartRad: 0.0, psiStopRad: Math.PI,
                psiStepRad: BlockCutOptTolerances.DegToRad(psiDeg),
                dxMax: 1.5, dxStep: 0.5,
                dyMax: 1.5, dyStep: 0.5);
            var omni = new OmniSolverOptions
            {
                Search = search,
                SubdivMode = SubdivisionMode.Uniform,
                Mx = mx, My = my,
            };
            var result = BlockCutOptOmniSolver.Solve(area, ply, omni);

            var ids = new List<string>(result.PerZone.Count);
            var counts = new List<int>(result.PerZone.Count);
            var revs = new List<double>(result.PerZone.Count);
            var bcss = new List<double>(result.PerZone.Count);
            var psis = new List<double>(result.PerZone.Count);
            foreach (var zr in result.PerZone)
            {
                var bestR = zr.Front.BestRecovery();
                var bestRev = zr.Front.BestRevenue();
                var bestBcs = zr.Front.BestBcsdbBv();
                ids.Add(zr.Zone.Id);
                counts.Add(bestR.RecoveryCount);
                revs.Add(bestRev.Revenue);
                bcss.Add(bestBcs.BcsdbBv);
                psis.Add(bestR.PsiDeg);
            }
            da.SetDataList(0, ids);
            da.SetDataList(1, counts);
            da.SetDataList(2, revs);
            da.SetDataList(3, bcss);
            da.SetDataList(4, psis);
            da.SetData(5, result.AggregateRecoveryCount);
        }
    }

    // =========================================================================
    // Interop helpers (Rhino types <-> Frahan.Masonry.Quarry.BlockCutOpt types).
    // =========================================================================

    internal static class GhBlockCutOptInterop
    {
        public static BoundingBox3 BoxToBbox(Box box)
        {
            var bb = box.BoundingBox;
            return new BoundingBox3(
                bb.Min.X, bb.Min.Y, bb.Min.Z,
                bb.Max.X, bb.Max.Y, bb.Max.Z);
        }

        public static OrientedBlock BoxToOrientedBlock(Box box)
        {
            var plane = box.Plane;
            var x = plane.XAxis; var y = plane.YAxis; var z = plane.ZAxis;
            var c = plane.Origin
                  + 0.5 * (box.X.Min + box.X.Max) * x
                  + 0.5 * (box.Y.Min + box.Y.Max) * y
                  + 0.5 * (box.Z.Min + box.Z.Max) * z;
            double hx = 0.5 * (box.X.Max - box.X.Min);
            double hy = 0.5 * (box.Y.Max - box.Y.Min);
            double hz = 0.5 * (box.Z.Max - box.Z.Min);
            return new OrientedBlock(
                c.X, c.Y, c.Z,
                x.X, x.Y, x.Z,
                y.X, y.Y, y.Z,
                z.X, z.Y, z.Z,
                hx, hy, hz);
        }

        public static PlyMesh RhinoMeshToPly(Mesh mesh)
        {
            var verts = new List<double>(mesh.Vertices.Count * 3);
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                var v = mesh.Vertices[i];
                verts.Add(v.X); verts.Add(v.Y); verts.Add(v.Z);
            }
            var tris = new List<int>(mesh.Faces.Count * 6);
            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                var f = mesh.Faces[i];
                if (f.IsQuad)
                {
                    tris.Add(f.A); tris.Add(f.B); tris.Add(f.C);
                    tris.Add(f.A); tris.Add(f.C); tris.Add(f.D);
                }
                else
                {
                    tris.Add(f.A); tris.Add(f.B); tris.Add(f.C);
                }
            }
            return new PlyMesh(verts, tris, null);
        }

        public static Mesh PlyToRhinoMesh(PlyMesh ply)
        {
            var m = new Mesh();
            for (int i = 0; i < ply.VertexCount; i++)
            {
                m.Vertices.Add(
                    ply.VertexCoordsXyz[3 * i + 0],
                    ply.VertexCoordsXyz[3 * i + 1],
                    ply.VertexCoordsXyz[3 * i + 2]);
            }
            for (int i = 0; i < ply.TriangleCount; i++)
            {
                m.Faces.AddFace(
                    ply.TriangleIndices[3 * i + 0],
                    ply.TriangleIndices[3 * i + 1],
                    ply.TriangleIndices[3 * i + 2]);
            }
            m.Normals.ComputeNormals();
            return m;
        }
    }
}
