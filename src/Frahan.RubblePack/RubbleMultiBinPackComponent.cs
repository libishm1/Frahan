#nullable disable
using System;
using System.Drawing;
using System.Collections.Generic;
using System.Text;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.RubblePack
{
    /// <summary>
    /// Rubble Multi-Bin Pack -- pack many carved blocks into each rough rubble
    /// stone (each stone = one BIN), spilling to the next stone when full.
    /// A per-stone voxel-occupancy grid (built from Mesh.IsPointInside) drives a
    /// first-fit-decreasing placement; each placement is accepted only when every
    /// block vertex is inside the stone (TRUE enclosure) and the kerf-padded
    /// voxel footprint is free. Multiple blocks per stone. Fully managed.
    /// Companion to example 15 (statue -> blocks -> rubble carving).
    /// </summary>
    public sealed class RubbleMultiBinPackComponent : GH_Component
    {
        public RubbleMultiBinPackComponent()
            : base("Rubble Multi-Bin Pack", "RubbleBinPack",
                   "Pack many carved blocks into each rubble stone (one stone = one bin), spilling " +
                   "to the next stone when full. Voxel-occupancy FFD with true per-vertex enclosure " +
                   "and a kerf gap. Multiple blocks per stone.",
                   "Frahan", "3D Packing")
        { }

        public override Guid ComponentGuid => new Guid("b1c2d3e4-aa03-4f5e-9c10-7e60cada1502");
        protected override Bitmap Icon => Frahan.GH.RubblePack.RubbleIconProvider.Load("RubbleMultiBin.png");
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Blocks", "B",
                "Carved block meshes to pack into the rubble bins.", GH_ParamAccess.list);
            p.AddMeshParameter("Stones", "S",
                "Rough rubble-lot stone meshes (closed). Each is one bin.", GH_ParamAccess.list);
            p.AddNumberParameter("Voxel Size", "V",
                "Occupancy lattice cell size (model units). Smaller = finer fit, slower.",
                GH_ParamAccess.item, 0.05);
            p.AddNumberParameter("Kerf", "K",
                "Saw-cut gap reserved around each placed block (model units).",
                GH_ParamAccess.item, 0.01);
            p.AddBooleanParameter("Run", "R", "Compute the packing.", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Placed", "P",
                "Placed block meshes, transformed into their bins (fully enclosed).",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Bin Index", "Bi",
                "Stone/bin index for each placed block.", GH_ParamAccess.list);
            p.AddNumberParameter("Bin Fill", "F",
                "Per-bin fill = recovered block volume / stone volume (one value per used bin).",
                GH_ParamAccess.list);
            p.AddTextParameter("Report", "Rpt", "Summary.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var blocks = new List<Mesh>();
            var stones = new List<Mesh>();
            double vox = 0.05, kerf = 0.01;
            bool run = true;
            if (!da.GetDataList(0, blocks) || blocks.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No blocks."); return; }
            if (!da.GetDataList(1, stones) || stones.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No stones."); return; }
            da.GetData(2, ref vox);
            da.GetData(3, ref kerf);
            da.GetData(4, ref run);
            if (!run) return;
            if (vox <= 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Voxel Size must be > 0."); return; }

            var rots = RubbleGeom.ProperRotations();
            int nb = blocks.Count, ns = stones.Count;

            var bvol = new double[nb];
            for (int i = 0; i < nb; i++) bvol[i] = RubbleGeom.Volume(blocks[i]);
            var svol = new double[ns];
            for (int i = 0; i < ns; i++) svol[i] = RubbleGeom.Volume(stones[i]);

            var border = new List<int>();
            for (int i = 0; i < nb; i++) border.Add(i);
            border.Sort((a, b) => bvol[b].CompareTo(bvol[a]));
            var sorder = new List<int>();
            for (int i = 0; i < ns; i++) sorder.Add(i);
            sorder.Sort((a, b) => svol[b].CompareTo(svol[a]));

            var remaining = new List<int>(border);
            var placed = new List<Mesh>();
            var placedBin = new List<int>();
            var binFill = new List<double>();
            var usedBins = new List<int>();

            foreach (int si in sorder)
            {
                if (remaining.Count == 0) break;
                var st = stones[si];
                var sbb = st.GetBoundingBox(true);
                var sd = RubbleGeom.SortedDims(sbb);
                int nx = Math.Max(1, (int)Math.Ceiling((sbb.Max.X - sbb.Min.X) / vox));
                int ny = Math.Max(1, (int)Math.Ceiling((sbb.Max.Y - sbb.Min.Y) / vox));
                int nz = Math.Max(1, (int)Math.Ceiling((sbb.Max.Z - sbb.Min.Z) / vox));
                if ((long)nx * ny * nz > 4000000) continue; // safety cap
                var free = new bool[nx, ny, nz];
                for (int ix = 0; ix < nx; ix++)
                    for (int iy = 0; iy < ny; iy++)
                        for (int iz = 0; iz < nz; iz++)
                        {
                            var pt = new Point3d(sbb.Min.X + (ix + 0.5) * vox,
                                                 sbb.Min.Y + (iy + 0.5) * vox,
                                                 sbb.Min.Z + (iz + 0.5) * vox);
                            free[ix, iy, iz] = st.IsPointInside(pt, 1e-6, false);
                        }

                int placedHere = 0;
                double recHere = 0;
                bool progress = true;
                while (progress)
                {
                    progress = false;
                    for (int q = remaining.Count - 1; q >= 0; q--)
                    {
                        int bi = remaining[q];
                        var blk = blocks[bi];
                        var b0 = blk.GetBoundingBox(true);
                        var bd = RubbleGeom.SortedDims(b0);
                        if (bd[0] > sd[0] || bd[1] > sd[1] || bd[2] > sd[2]) continue;

                        bool done = false;
                        foreach (var r in rots)
                        {
                            var rb = blk.DuplicateMesh();
                            rb.Transform(r);
                            var rbb = rb.GetBoundingBox(true);
                            int wx = (int)Math.Ceiling((rbb.Max.X - rbb.Min.X + kerf) / vox);
                            int wy = (int)Math.Ceiling((rbb.Max.Y - rbb.Min.Y + kerf) / vox);
                            int wz = (int)Math.Ceiling((rbb.Max.Z - rbb.Min.Z + kerf) / vox);
                            if (wx > nx || wy > ny || wz > nz) continue;
                            for (int iz = 0; iz <= nz - wz && !done; iz++)
                                for (int iy = 0; iy <= ny - wy && !done; iy++)
                                    for (int ix = 0; ix <= nx - wx && !done; ix++)
                                    {
                                        bool okf = true;
                                        for (int dx = 0; dx < wx && okf; dx++)
                                            for (int dy = 0; dy < wy && okf; dy++)
                                                for (int dz = 0; dz < wz && okf; dz++)
                                                    if (!free[ix + dx, iy + dy, iz + dz]) okf = false;
                                        if (!okf) continue;
                                        var tgt = new Point3d(sbb.Min.X + ix * vox + kerf * 0.5,
                                                              sbb.Min.Y + iy * vox + kerf * 0.5,
                                                              sbb.Min.Z + iz * vox + kerf * 0.5);
                                        var cand = rb.DuplicateMesh();
                                        cand.Transform(Transform.Translation(tgt - rbb.Min));
                                        bool enc = true;
                                        var vs = cand.Vertices;
                                        for (int vi = 0; vi < vs.Count; vi++)
                                        {
                                            var pv = new Point3d(vs[vi].X, vs[vi].Y, vs[vi].Z);
                                            if (!st.IsPointInside(pv, 1e-6, false)) { enc = false; break; }
                                        }
                                        if (!enc) continue;
                                        for (int dx = 0; dx < wx; dx++)
                                            for (int dy = 0; dy < wy; dy++)
                                                for (int dz = 0; dz < wz; dz++)
                                                    free[ix + dx, iy + dy, iz + dz] = false;
                                        placed.Add(cand);
                                        placedBin.Add(si);
                                        recHere += bvol[bi];
                                        placedHere++;
                                        remaining.RemoveAt(q);
                                        progress = true;
                                        done = true;
                                    }
                            if (done) break;
                        }
                    }
                }
                if (placedHere > 0)
                {
                    usedBins.Add(si);
                    binFill.Add(svol[si] > 0 ? recHere / svol[si] : 0.0);
                }
            }

            double meanFill = 0;
            for (int i = 0; i < binFill.Count; i++) meanFill += binFill[i];
            if (binFill.Count > 0) meanFill /= binFill.Count;

            var rpt = new StringBuilder();
            rpt.AppendLine("Rubble Multi-Bin Pack (true enclosure, many blocks/stone)");
            rpt.AppendLine($"placed {placed.Count} / {nb} blocks; bins used {usedBins.Count}; " +
                           $"blocks/bin {(usedBins.Count > 0 ? (double)placed.Count / usedBins.Count : 0):F1}");
            rpt.AppendLine($"mean bin fill {meanFill * 100.0:F1}% ; enclosure 100% (every vertex inside)");

            da.SetDataList(0, placed);
            da.SetDataList(1, placedBin);
            da.SetDataList(2, binFill);
            da.SetData(3, rpt.ToString());
        }
    }
}
