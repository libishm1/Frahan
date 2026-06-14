using System;
using System.Drawing;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.Masonry.Packing;
using Frahan.GH;

namespace Frahan.StonePack.GH.Masonry
{
    /// <summary>
    /// Stone-Cell Match (Λ) — the imposition↔negotiation balance engine on
    /// canvas (evolution P4, 2026-06-10). Assigns a stone INVENTORY (found /
    /// scanned stones) to a generated wall's TARGET CELLS by Hungarian matching
    /// on a PCA-aligned voxel symmetric-difference cost, and reports the trade:
    /// per-stone carve ratio λ, workflow imposition index Λ (0 = stones used as
    /// found … 1 = pure stock; Clifford &amp; McGee's Cyclopean Cannibalism wall
    /// measured ≈0.27), and the gap (under-fill) ratio. Outputs the stones
    /// transformed into their cells, ready for the exact CGAL carve-back.
    /// Math: Frahan.Masonry.Packing.StoneCellAssignment (Rhino-free Core,
    /// validated against the closed-form λ = 1 − 1/k³ inflation anchor).
    /// </summary>
    public class StoneCellMatchComponent : FrahanComponentBase
    {
        public StoneCellMatchComponent()
          : base("Stone-Cell Match (Λ)", "StoneMatchL",
                 "Hungarian assignment of a stone inventory to target wall cells, minimising carved material. " +
                 "Reports per-stone carve ratio λ, the workflow imposition index Λ (0 = as-found … 1 = pure " +
                 "stock; Cyclopean Cannibalism datum ≈0.27) and gap ratios, and outputs the stones placed " +
                 "into their cells. Refs: Clifford & McGee 2018 (ACADIA); Kuhn 1955 (Hungarian); " +
                 "Frahan SLM+ROSES masonry review 2026-06-10.",
                 "Frahan", "Masonry")
        { }

        public override Guid ComponentGuid => new Guid("D5F10016-6C2D-4F1B-B3E8-7A95D0C41F62");
        protected override Bitmap Icon => Frahan.GH.IconProvider.Load("MatchCandidate.png");
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager p)
        {
            p.AddMeshParameter("Stones", "St", "Stone inventory (closed meshes, found/scanned)", GH_ParamAccess.list);
            p.AddMeshParameter("Cells", "Ce", "Target cells (closed meshes, e.g. the Polygonal Wall Generator's stones at Mortar = 0)", GH_ParamAccess.list);
            p.AddIntegerParameter("TopK", "K", "Prefilter candidates per cell that get the voxel cost", GH_ParamAccess.item, 6);
            p.AddIntegerParameter("CostRes", "Cr", "Voxel resolution for the assignment cost", GH_ParamAccess.item, 12);
            p.AddIntegerParameter("RefineRes", "Rr", "Voxel resolution for the final per-pair metrics", GH_ParamAccess.item, 24);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager p)
        {
            p.AddMeshParameter("Placed", "P", "Stones transformed into their assigned cells (cell order)", GH_ParamAccess.list);
            p.AddNumberParameter("Carve", "L", "Per-placement carve ratio lambda_i = carved/found volume (cell order)", GH_ParamAccess.list);
            p.AddNumberParameter("Gap", "G", "Per-placement gap ratio (cell volume the stone fails to fill)", GH_ParamAccess.list);
            p.AddNumberParameter("Lambda", "LL", "Workflow imposition index: volume-weighted carve fraction (0..1)", GH_ParamAccess.item);
            p.AddIntegerParameter("StoneIndex", "Si", "Assigned stone index per placement (cell order)", GH_ParamAccess.list);
            p.AddIntegerParameter("Unused", "U", "Inventory indices that were not used", GH_ParamAccess.list);
            p.AddTextParameter("Report", "R", "Summary", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var stones = new List<Mesh>();
            var cells = new List<Mesh>();
            int topK = 6, costRes = 12, refineRes = 24;
            if (!da.GetDataList(0, stones) || stones.Count == 0) return;
            if (!da.GetDataList(1, cells) || cells.Count == 0) return;
            da.GetData(2, ref topK); da.GetData(3, ref costRes); da.GetData(4, ref refineRes);

            if (stones.Count < cells.Count)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Inventory ({stones.Count}) smaller than the cell count ({cells.Count}); some cells stay empty.");

            ToBuffers(stones, out var sCoords, out var sTris);
            ToBuffers(cells, out var cCoords, out var cTris);

            AssignmentResult result;
            try
            {
                result = StoneCellAssignment.Assign(sCoords, sTris, cCoords, cTris,
                    new StoneCellAssignmentOptions
                    {
                        PrefilterTopK = Math.Max(1, topK),
                        CostVoxels = Math.Max(6, costRes),
                        RefineVoxels = Math.Max(8, refineRes),
                    });
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Assignment failed: " + ex.Message);
                return;
            }

            var placed = new List<Mesh>();
            var carve = new List<double>();
            var gap = new List<double>();
            var stoneIdx = new List<int>();
            foreach (var pl in result.Placements)
            {
                var m = stones[pl.StoneIndex].DuplicateMesh();
                var t = pl.Transform;
                var x = new Transform
                {
                    M00 = t[0], M01 = t[1], M02 = t[2], M03 = t[3],
                    M10 = t[4], M11 = t[5], M12 = t[6], M13 = t[7],
                    M20 = t[8], M21 = t[9], M22 = t[10], M23 = t[11],
                    M30 = 0, M31 = 0, M32 = 0, M33 = 1,
                };
                m.Transform(x);
                placed.Add(m);
                carve.Add(pl.CarveRatio);
                gap.Add(pl.GapRatio);
                stoneIdx.Add(pl.StoneIndex);
            }

            string report =
                $"Lambda = {result.ImpositionIndex:0.000} (0 as-found .. 1 pure stock; Cyclopean datum 0.27) | " +
                $"mean gap {result.MeanGapRatio:0.000} | placed {result.Placements.Count}/{cells.Count} cells | " +
                $"unused stones {result.UnusedStones.Count}";

            da.SetDataList(0, placed);
            da.SetDataList(1, carve);
            da.SetDataList(2, gap);
            da.SetData(3, result.ImpositionIndex);
            da.SetDataList(4, stoneIdx);
            da.SetDataList(5, result.UnusedStones);
            da.SetData(6, report);
        }

        private static void ToBuffers(List<Mesh> meshes,
            out List<IReadOnlyList<double>> coords, out List<IReadOnlyList<int>> tris)
        {
            coords = new List<IReadOnlyList<double>>(meshes.Count);
            tris = new List<IReadOnlyList<int>>(meshes.Count);
            foreach (var mesh in meshes)
            {
                if (mesh == null) continue;
                var t = mesh.DuplicateMesh();
                t.Faces.ConvertQuadsToTriangles();
                var cs = new List<double>(t.Vertices.Count * 3);
                for (int v = 0; v < t.Vertices.Count; v++)
                {
                    var pt = t.Vertices[v];
                    cs.Add(pt.X); cs.Add(pt.Y); cs.Add(pt.Z);
                }
                var ts = new List<int>(t.Faces.Count * 3);
                for (int f = 0; f < t.Faces.Count; f++)
                {
                    var fa = t.Faces[f];
                    ts.Add(fa.A); ts.Add(fa.B); ts.Add(fa.C);
                }
                coords.Add(cs); tris.Add(ts);
            }
        }
    }
}
