#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Quarry;
using Frahan.Masonry.Quarry.BlockCutOpt;
using Frahan.Masonry.Quarry.GeoPack;
using Frahan.Masonry.Quarry.Monuments;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Quarry
{
    // =========================================================================
    // Heterogeneous-extraction components, 2026-05-15.
    //
    // Two components:
    //   FrahanMixedSizeBlockPack3DComponent       direct 3D DLBF (BCOMixedPack3D)
    //   FrahanHeterogeneousExtractionComponent    composite 4-step pipeline (HeteroExt)
    //
    // Why composite: Libish asked for a single component that strings together
    //   1. BCOSolve with the prime (biggest) block dim to find which bench
    //      regions are usable (non-intersected) vs scrap (intersected).
    //   2. Mark intersected cells as forbidden boxes for DLBF.
    //   3. 3D DLBF mixed-size pack of the catalogue (monuments + dimension
    //      stones + slabs) avoiding the forbidden regions.
    //   4. Optional Monument inventory placement (BenchMonumentPacker) on a
    //      BlockGraph derived from the same fracture mesh.
    //
    // Outputs surface all four stages so the user can wire previews of each.
    // =========================================================================

    // -------------------------------------------------------------------------
    // 1. 3D Mixed-Size Block Pack -- direct exposure of Dlbf3dMixedSizePacker.
    //    Supports variable-height pieces and an optional stacking mode.
    // -------------------------------------------------------------------------
    [Algorithm("Deepest-Left-Bottom-Fill (3D)", "Chehrazad, Roose, Wauters 2025, Int. J. Production Research 63:6606-6629", Doi = "10.1080/00207543.2025.2478434", WikiPath = "wiki/index/references.md")]
    [DesignApplication(
        "3D generalisation of DLBF (Chehrazad 2025)",
        DesignFlow.TopDown)]
    public sealed class FrahanMixedSizeBlockPack3DComponent : FrahanComponentBase
    {
        public FrahanMixedSizeBlockPack3DComponent()
            : base(
                "Frahan Mixed-Size Block Pack 3D", "BCOMixedPack3D",
                "3D generalisation of DLBF (Chehrazad 2025). Each piece has " +
                "its own (Width, Depth, Height); pieces sort by revenue-per-" +
                "volume. Floor-only mode (default) places every piece at " +
                "z = bench.MinZ, matching quarry extraction where blocks are " +
                "cut OUT of solid rock (no stacking). Disable Floor-Only for " +
                "monument storage / slab racking / container loading. Implements Deepest-Left-Bottom-Fill 3D (Chehrazad 2025).",
                "Frahan", "Block")
        { }

        public override Guid ComponentGuid =>
            new Guid("F2D0BC18-1234-4F2D-A0B0-7E60CADA15B8");

        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override Bitmap Icon => IconProvider.Load("BinPack.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddBoxParameter("Tested Area", "A", "Bench bounding box (m).", GH_ParamAccess.item);
            p.AddTextParameter("Piece Ids", "Id", "One id per catalogue entry.", GH_ParamAccess.list);
            p.AddNumberParameter("Piece Widths (m)", "W", "Width per entry (X).", GH_ParamAccess.list);
            p.AddNumberParameter("Piece Depths (m)", "D", "Depth per entry (Y).", GH_ParamAccess.list);
            p.AddNumberParameter("Piece Heights (m)", "H", "Height per entry (Z).", GH_ParamAccess.list);
            p.AddNumberParameter("Piece Revenues", "Rev", "RMV per entry.", GH_ParamAccess.list);
            p.AddBoxParameter("Forbidden Boxes", "X", "Optional forbidden regions (e.g. fracture-intersected cells).", GH_ParamAccess.list);
            p.AddNumberParameter("Grid Cell (m)", "Gc", "Discretisation cell; 0 = min(W,D,H)/4.", GH_ParamAccess.item, 0.0);
            p.AddBooleanParameter("Floor Only", "Fl", "True = pieces sit on bench floor (no stacking).", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBoxParameter("Placed Boxes", "B", "One Box per placed piece.", GH_ParamAccess.list);
            p.AddTextParameter("Placed Ids", "I", "Id of each placed piece (multiplicity preserved).", GH_ParamAccess.list);
            p.AddNumberParameter("Total Revenue", "Pi", "Sum of placed-piece revenues.", GH_ParamAccess.item);
            p.AddNumberParameter("Occupied Volume (m^3)", "Vol", "Sum of placed-piece volumes.", GH_ParamAccess.item);
            p.AddIntegerParameter("Placed Count", "N", "Number of placements.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var box = Box.Empty;
            var ids = new List<string>();
            var ws = new List<double>();
            var ds = new List<double>();
            var hs = new List<double>();
            var revs = new List<double>();
            var forb = new List<Box>();
            double gc = 0.0;
            bool floorOnly = true;
            if (!da.GetData(0, ref box) || !box.IsValid)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid tested-area box."); return; }
            da.GetDataList(1, ids); da.GetDataList(2, ws);
            da.GetDataList(3, ds); da.GetDataList(4, hs);
            da.GetDataList(5, revs);
            int n = ids.Count;
            if (n == 0 || ws.Count != n || ds.Count != n || hs.Count != n || revs.Count != n)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Id / W / D / H / Rev lists must be non-empty and equal length."); return; }
            da.GetDataList(6, forb);
            da.GetData(7, ref gc);
            da.GetData(8, ref floorOnly);

            var catalog = new List<PieceSize3D>(n);
            try
            {
                for (int i = 0; i < n; i++)
                    catalog.Add(new PieceSize3D(ids[i], ws[i], ds[i], hs[i], revs[i]));
            }
            catch (Exception ex)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Piece #{catalog.Count + 1}: {ex.Message}"); return; }

            var area = GhBlockCutOptInterop.BoxToBbox(box);
            List<BoundingBox3> forbidden = null;
            if (forb.Count > 0)
            {
                forbidden = new List<BoundingBox3>(forb.Count);
                foreach (var b in forb) if (b.IsValid) forbidden.Add(GhBlockCutOptInterop.BoxToBbox(b));
            }

            Dlbf3dPackResult result;
            try { result = Dlbf3dMixedSizePacker.Pack(area, catalog, forbidden, gc, floorOnly); }
            catch (Exception ex)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); return; }

            var outBoxes = new List<Box>(result.Placed.Count);
            var outIds = new List<string>(result.Placed.Count);
            foreach (var p in result.Placed)
            {
                outBoxes.Add(new Box(Plane.WorldXY,
                    new Interval(p.XMin, p.XMax),
                    new Interval(p.YMin, p.YMax),
                    new Interval(p.ZMin, p.ZMax)));
                outIds.Add(p.Size.Id);
            }
            da.SetDataList(0, outBoxes);
            da.SetDataList(1, outIds);
            da.SetData(2, result.TotalRevenue);
            da.SetData(3, result.OccupiedVolumeMetres3);
            da.SetData(4, result.Placed.Count);
        }
    }

    // -------------------------------------------------------------------------
    // 2. Heterogeneous Quarry Extraction -- the 4-step composite pipeline.
    //
    //    Stage 1: BlockCutOpt at the prime (biggest) block dim. Identifies the
    //             optimal (psi, dx, dy) and which cells are fracture-clean.
    //    Stage 2: Intersected cells become forbidden AABBs for downstream
    //             mixed-size packing.
    //    Stage 3: 3D DLBF places the full mixed-size catalogue around the
    //             forbidden regions. The prime block size can (and usually
    //             should) be the largest entry in the catalogue, so the
    //             packer naturally chooses big blocks where the bench is
    //             clean and falls back to smaller pieces in the gaps.
    //    Stage 4: Optional. If a MonumentInventory is wired, the component
    //             also builds a BlockGraph from the same fracture mesh and
    //             runs BenchMonumentPacker on it, returning per-monument
    //             placements with 24-rotation SO(3) sampling.
    // -------------------------------------------------------------------------
    [Algorithm("Heterogeneous quarry extraction pipeline", "Frahan-original", Note = "Composes Elkarmoty 2020 (BlockCutOpt) and Chehrazad 2025 (DLBF), both interpreted and reimplemented in managed code for this plugin; the composition and the heterogeneity model are the contribution.")]
    [RelatedComponent("Frahan > Lab > Frahan Mixed-Size Block Pack",
        Reason = "Standalone 2D DLBF mixed-size packer (F2D0BC17); the same engine this facade composes.")]
    [RelatedComponent("Frahan > Quarry > Frahan Mixed-Size Block Pack 3D",
        Reason = "Standalone 3D DLBF mixed-size packer (F2D0BC18); the same engine this facade composes.")]
    [RelatedComponent("Frahan > Quarry > BlockCutOpt Solve",
        Reason = "Standalone stage-1 solver: optimum cutting direction + displacement (Elkarmoty 2020).")]
    [DesignApplication(
        "Composite 4-step extraction pipeline: BlockCutOpt to find  the fracture-clean regions, then 3D DLBF mixed-s...",
        DesignFlow.TopDown)]
    public sealed class FrahanHeterogeneousExtractionComponent : FrahanComponentBase
    {
        public FrahanHeterogeneousExtractionComponent()
            : base(
                "Frahan Heterogeneous Quarry Extraction", "HeteroExt",
                "Composite 4-step extraction pipeline: BlockCutOpt to find " +
                "the fracture-clean regions, then 3D DLBF mixed-size pack " +
                "(monuments + dimension stones + slabs) avoiding fractured " +
                "regions, plus optional MonumentInventory placement on a " +
                "fracture-derived BlockGraph. One component, four outputs " +
                "per stage. Frahan-original method.",
                "Frahan", "Block")
        { }

        public override Guid ComponentGuid =>
            new Guid("F2D0BC19-1234-4F2D-A0B0-7E60CADA15B9");

        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override Bitmap Icon => IconProvider.Load("QuarryBlock.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddBoxParameter("Bench", "B", "Bench bounding box (m).", GH_ParamAccess.item);
            p.AddMeshParameter("Fractures", "Fx", "Fracture mesh.", GH_ParamAccess.item);

            p.AddNumberParameter("Prime Block X", "Plx", "Prime (max) block length (m) for BCO stage 1.", GH_ParamAccess.item, 3.0);
            p.AddNumberParameter("Prime Block Y", "Ply", "Prime block width (m).", GH_ParamAccess.item, 2.0);
            p.AddNumberParameter("Prime Block Z", "Plz", "Prime block height (m).", GH_ParamAccess.item, 1.5);
            p.AddNumberParameter("Kerf", "K", "Saw kerf (m).", GH_ParamAccess.item, BlockCutOptTolerances.KerfDefaultMetres);
            p.AddNumberParameter("Psi Step (deg)", "Pdeg", "Angular search step.", GH_ParamAccess.item, 3.0);

            p.AddTextParameter("Catalogue Ids", "Cid", "DLBF catalogue ids.", GH_ParamAccess.list);
            p.AddNumberParameter("Catalogue Widths (m)", "Cw", "DLBF widths.", GH_ParamAccess.list);
            p.AddNumberParameter("Catalogue Depths (m)", "Cd", "DLBF depths.", GH_ParamAccess.list);
            p.AddNumberParameter("Catalogue Heights (m)", "Ch", "DLBF heights.", GH_ParamAccess.list);
            p.AddNumberParameter("Catalogue Revenues", "Cr", "DLBF revenues.", GH_ParamAccess.list);
            p.AddNumberParameter("Grid Cell (m)", "Gc", "DLBF discretisation cell; 0 = min(W,D,H)/4.", GH_ParamAccess.item, 0.0);
            p.AddBooleanParameter("Floor Only", "Fl", "True = pieces on bench floor (no stacking).", GH_ParamAccess.item, true);

            p.AddGenericParameter("Monument Inventory", "Mon", "Optional MonumentInventory (from MonInv) for stage 4.", GH_ParamAccess.item);
            p[14].Optional = true;
            p.AddNumberParameter("Monument Grid (m)", "Mg", "Monument-placement grid stride.", GH_ParamAccess.item, 0.1);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            // Stage 1: BCO prime cuts
            p.AddBoxParameter("Prime Boxes", "Pb", "Non-intersected cells at the prime block dim.", GH_ParamAccess.list);
            p.AddIntegerParameter("Prime Count", "Pn", "Count of fracture-clean prime cells.", GH_ParamAccess.item);
            p.AddNumberParameter("Prime Recovery %", "Pr", "BlockCutOpt recovery at the prime dim.", GH_ParamAccess.item);
            p.AddNumberParameter("Best Psi (deg)", "Psi", "Optimal cutting direction.", GH_ParamAccess.item);

            // Stage 2: forbidden boxes derived from intersected cells
            p.AddBoxParameter("Forbidden Boxes", "Fb", "Fracture-intersected cells (forbidden for DLBF).", GH_ParamAccess.list);

            // Stage 3: DLBF mixed pack
            p.AddBoxParameter("Mixed Boxes", "Mb", "DLBF-placed mixed-size piece boxes.", GH_ParamAccess.list);
            p.AddTextParameter("Mixed Ids", "Mi", "Id of each DLBF piece.", GH_ParamAccess.list);
            p.AddNumberParameter("Mixed Revenue", "Mr", "DLBF total revenue.", GH_ParamAccess.item);
            p.AddNumberParameter("Mixed Volume", "Mv", "DLBF occupied volume (m^3).", GH_ParamAccess.item);

            // Stage 4: monument placement (optional)
            p.AddBoxParameter("Monument Boxes", "Mo", "Monument-placement AABBs (empty if no inventory).", GH_ParamAccess.list);
            p.AddTextParameter("Monument Ids", "Moi", "Monument ids in placement order.", GH_ParamAccess.list);
            p.AddIntegerParameter("Monument Count", "Mon", "Total monuments placed.", GH_ParamAccess.item);
            p.AddTextParameter("Unplaced Monuments", "Mou", "Monuments that did not fit anywhere.", GH_ParamAccess.list);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var box = Box.Empty;
            Mesh fxMesh = null;
            double Plx = 3.0, Ply = 2.0, Plz = 1.5;
            double kerf = BlockCutOptTolerances.KerfDefaultMetres;
            double psiDeg = 3.0;
            var cIds = new List<string>();
            var cWs = new List<double>();
            var cDs = new List<double>();
            var cHs = new List<double>();
            var cRs = new List<double>();
            double gc = 0.0;
            bool floorOnly = true;
            GH_ObjectWrapper monW = null;
            double mg = 0.1;

            if (!da.GetData(0, ref box) || !box.IsValid)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid bench."); return; }
            if (!da.GetData(1, ref fxMesh) || fxMesh == null)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Fracture mesh required."); return; }
            da.GetData(2, ref Plx); da.GetData(3, ref Ply); da.GetData(4, ref Plz);
            da.GetData(5, ref kerf); da.GetData(6, ref psiDeg);
            da.GetDataList(7, cIds); da.GetDataList(8, cWs);
            da.GetDataList(9, cDs); da.GetDataList(10, cHs);
            da.GetDataList(11, cRs);
            int n = cIds.Count;
            if (n == 0 || cWs.Count != n || cDs.Count != n || cHs.Count != n || cRs.Count != n)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Catalogue lists must be non-empty and equal length."); return; }
            da.GetData(12, ref gc); da.GetData(13, ref floorOnly);
            da.GetData(14, ref monW); da.GetData(15, ref mg);

            var area = GhBlockCutOptInterop.BoxToBbox(box);
            var ply = GhBlockCutOptInterop.RhinoMeshToPly(fxMesh);

            // ---- Stage 1: BCO at prime block dim -----------------------------
            var primeOpts = new BlockCutOptOptions(
                Plx, Ply, Plz, kerf,
                psiStartRad: 0.0, psiStopRad: Math.PI,
                psiStepRad: BlockCutOptTolerances.DegToRad(psiDeg),
                dxMax: 1.5, dxStep: 0.5,
                dyMax: 1.5, dyStep: 0.5);
            BlockCutOptResult primeResult;
            try { primeResult = BlockCutOptSolver.Solve(area, ply, primeOpts); }
            catch (Exception ex)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Stage 1 (BCO): {ex.Message}"); return; }

            var primeGrid = CuttingGrid.GenerateTilted(
                area, Plx, Ply, Plz, kerf,
                primeResult.BestPsiRad, primeResult.BestThetaRad, primeResult.BestPhiRad,
                primeResult.BestDx, primeResult.BestDy);
            var bvh = TriangleAabbBvh.Build(ply);

            var primeBoxes = new List<Box>(primeGrid.Count);
            var forbidden = new List<BoundingBox3>(primeGrid.Count);
            var forbiddenBoxes = new List<Box>(primeGrid.Count);
            foreach (var obb in primeGrid)
            {
                var aabb = ObbToAabb(obb);
                var rhBox = new Box(Plane.WorldXY,
                    new Interval(aabb.MinX, aabb.MaxX),
                    new Interval(aabb.MinY, aabb.MaxY),
                    new Interval(aabb.MinZ, aabb.MaxZ));
                if (bvh.AnyTriangleIntersects(in obb))
                {
                    forbidden.Add(aabb);
                    forbiddenBoxes.Add(rhBox);
                }
                else
                {
                    primeBoxes.Add(rhBox);
                }
            }

            // ---- Stage 3: 3D DLBF over the catalogue with forbidden cells ----
            var catalog = new List<PieceSize3D>(n);
            try
            {
                for (int i = 0; i < n; i++)
                    catalog.Add(new PieceSize3D(cIds[i], cWs[i], cDs[i], cHs[i], cRs[i]));
            }
            catch (Exception ex)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Catalogue piece #{catalog.Count + 1}: {ex.Message}"); return; }

            Dlbf3dPackResult dlbfResult;
            try { dlbfResult = Dlbf3dMixedSizePacker.Pack(area, catalog, forbidden, gc, floorOnly); }
            catch (Exception ex)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Stage 3 (DLBF): {ex.Message}"); return; }

            var mixedBoxes = new List<Box>(dlbfResult.Placed.Count);
            var mixedIds = new List<string>(dlbfResult.Placed.Count);
            foreach (var p in dlbfResult.Placed)
            {
                mixedBoxes.Add(new Box(Plane.WorldXY,
                    new Interval(p.XMin, p.XMax),
                    new Interval(p.YMin, p.YMax),
                    new Interval(p.ZMin, p.ZMax)));
                mixedIds.Add(p.Size.Id);
            }

            // ---- Stage 4 (optional): MonPack on BlockGraph -------------------
            var monBoxes = new List<Box>();
            var monIds = new List<string>();
            var monUnplaced = new List<string>();
            int monPlacedCount = 0;

            if (monW != null && monW.Value is MonumentInventory inv)
            {
                try
                {
                    var slab = Slab.Box(area.MinX, area.MinY, area.MinZ,
                                        area.MaxX, area.MaxY, area.MaxZ);
                    var planes = ExtractFracturePlanesFromMesh(fxMesh);
                    var crack = CrackGraphBuilder.FromPlanes(planes);
                    var graph = BlockGraphBuilder.Partition(slab, crack);
                    var opts = new BenchMonumentPackerOptions(gridStride: mg);
                    var plan = BenchMonumentPacker.PackBlockGraph(graph, inv, opts);
                    foreach (var pp in plan.Placements)
                    {
                        monBoxes.Add(new Box(Plane.WorldXY,
                            new Interval(pp.OriginX, pp.OriginX + pp.Dx),
                            new Interval(pp.OriginY, pp.OriginY + pp.Dy),
                            new Interval(pp.OriginZ, pp.OriginZ + pp.Dz)));
                        monIds.Add(pp.MonumentId);
                    }
                    foreach (var u in plan.UnplacedMonumentIds) monUnplaced.Add(u);
                    monPlacedCount = plan.PlacedCount;
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"Stage 4 (MonPack) skipped: {ex.Message}");
                }
            }

            // ---- emit ---------------------------------------------------------
            da.SetDataList(0, primeBoxes);
            da.SetData(1, primeBoxes.Count);
            da.SetData(2, primeResult.RecoveryPercent);
            da.SetData(3, primeResult.BestPsiDeg);
            da.SetDataList(4, forbiddenBoxes);
            da.SetDataList(5, mixedBoxes);
            da.SetDataList(6, mixedIds);
            da.SetData(7, dlbfResult.TotalRevenue);
            da.SetData(8, dlbfResult.OccupiedVolumeMetres3);
            da.SetDataList(9, monBoxes);
            da.SetDataList(10, monIds);
            da.SetData(11, monPlacedCount);
            da.SetDataList(12, monUnplaced);
        }

        // ─── helpers ──────────────────────────────────────────────────────────

        private static BoundingBox3 ObbToAabb(OrientedBlock o)
        {
            double xMin = double.PositiveInfinity, yMin = double.PositiveInfinity, zMin = double.PositiveInfinity;
            double xMax = double.NegativeInfinity, yMax = double.NegativeInfinity, zMax = double.NegativeInfinity;
            for (int k = 0; k < 8; k++)
            {
                double sx = ((k & 1) != 0 ? +1 : -1) * o.HalfX;
                double sy = ((k & 2) != 0 ? +1 : -1) * o.HalfY;
                double sz = ((k & 4) != 0 ? +1 : -1) * o.HalfZ;
                double wx = o.CenterX + sx * o.UX + sy * o.VX + sz * o.WX;
                double wy = o.CenterY + sx * o.UY + sy * o.VY + sz * o.WY;
                double wz = o.CenterZ + sx * o.UZ + sy * o.VZ + sz * o.WZ;
                if (wx < xMin) xMin = wx; if (wx > xMax) xMax = wx;
                if (wy < yMin) yMin = wy; if (wy > yMax) yMax = wy;
                if (wz < zMin) zMin = wz; if (wz > zMax) zMax = wz;
            }
            return new BoundingBox3(xMin, yMin, zMin, xMax, yMax, zMax);
        }

        private static List<FracturePlane> ExtractFracturePlanesFromMesh(Mesh mesh)
        {
            if (mesh.FaceNormals.Count != mesh.Faces.Count)
                mesh.FaceNormals.ComputeFaceNormals();
            var planes = new List<FracturePlane>(mesh.Faces.Count);
            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                var f = mesh.Faces[i];
                Point3d centroid;
                if (f.IsQuad)
                {
                    var a = mesh.Vertices[f.A]; var b = mesh.Vertices[f.B];
                    var c = mesh.Vertices[f.C]; var d = mesh.Vertices[f.D];
                    centroid = new Point3d(
                        0.25 * (a.X + b.X + c.X + d.X),
                        0.25 * (a.Y + b.Y + c.Y + d.Y),
                        0.25 * (a.Z + b.Z + c.Z + d.Z));
                }
                else
                {
                    var a = mesh.Vertices[f.A]; var b = mesh.Vertices[f.B]; var c = mesh.Vertices[f.C];
                    centroid = new Point3d(
                        (a.X + b.X + c.X) / 3.0,
                        (a.Y + b.Y + c.Y) / 3.0,
                        (a.Z + b.Z + c.Z) / 3.0);
                }
                var n = mesh.FaceNormals[i];
                var v = new Vector3d(n.X, n.Y, n.Z);
                if (v.Length < 1e-12) continue;
                v.Unitize();
                try { planes.Add(new FracturePlane(centroid.X, centroid.Y, centroid.Z, v.X, v.Y, v.Z)); }
                catch (ArgumentException) { /* skip zero-normal face */ }
            }
            return planes;
        }
    }
}
