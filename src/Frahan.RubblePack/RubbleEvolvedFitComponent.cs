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
    /// Rubble Evolved Fit -- match each carved block to the TIGHTEST rough
    /// rubble stone that can FULLY ENCLOSE it (carve one block from one stone).
    /// The pose is evolved: 24 axis-rotation seeds, then a (1+lambda) evolution
    /// strategy that perturbs rotation + translation to drive the outside-vertex
    /// count to zero. Enclosure is TRUE containment (every block vertex inside
    /// the closed stone). One block per stone. Fully managed (RhinoCommon only).
    /// Companion to example 15 (statue -> blocks -> rubble carving).
    /// </summary>
    public sealed class RubbleEvolvedFitComponent : GH_Component
    {
        public RubbleEvolvedFitComponent()
            : base("Rubble Evolved Fit", "RubbleFit",
                   "Match each carved block to the tightest rubble stone that fully encloses it, " +
                   "evolving the placement pose (24 rotation seeds + (1+8)-ES) until every block " +
                   "vertex is inside the stone. One block per stone. True geometric enclosure.",
                   "Frahan", "Masonry")
        { }

        public override Guid ComponentGuid => new Guid("b1c2d3e4-aa02-4f5e-9c10-7e60cada1501");
        protected override Bitmap Icon => Frahan.GH.RubblePack.RubbleIconProvider.Load("RubbleEvolvedFit.png");
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Blocks", "B",
                "Carved block meshes to be carved from rubble (e.g. statue decomposition).",
                GH_ParamAccess.list);
            p.AddMeshParameter("Stones", "S",
                "Rough rubble-lot stone meshes (closed). Each block is carved from one stone.",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Candidates", "C",
                "Max tightest-first candidate stones tried per block before giving up.",
                GH_ParamAccess.item, 12);
            p.AddIntegerParameter("Seed", "Sd", "RNG seed for the evolution strategy.",
                GH_ParamAccess.item, 12345);
            p.AddBooleanParameter("Run", "R", "Compute the fit.", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Placed", "P",
                "Each placed block, transformed into its matched stone (fully enclosed).",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Stone Index", "Si",
                "Index of the matched stone for each placed block.", GH_ParamAccess.list);
            p.AddNumberParameter("Yield", "Y",
                "Carve yield = block volume / stone volume for each placement.", GH_ParamAccess.list);
            p.AddTextParameter("Report", "Rpt", "Summary.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var blocks = new List<Mesh>();
            var stones = new List<Mesh>();
            int candidates = 12, seed = 12345;
            bool run = true;
            if (!da.GetDataList(0, blocks) || blocks.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No blocks."); return; }
            if (!da.GetDataList(1, stones) || stones.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No stones."); return; }
            da.GetData(2, ref candidates);
            da.GetData(3, ref seed);
            da.GetData(4, ref run);
            if (!run) return;
            if (candidates < 1) candidates = 1;

            var rots = RubbleGeom.ProperRotations();
            var rng = new Random(seed);

            // block order: largest volume first (FFD)
            int nb = blocks.Count, ns = stones.Count;
            var bvol = new double[nb];
            for (int i = 0; i < nb; i++) bvol[i] = RubbleGeom.Volume(blocks[i]);
            var border = new List<int>();
            for (int i = 0; i < nb; i++) border.Add(i);
            border.Sort((a, b) => bvol[b].CompareTo(bvol[a]));

            var svol = new double[ns];
            for (int i = 0; i < ns; i++) svol[i] = RubbleGeom.Volume(stones[i]);
            var used = new bool[ns];

            var placed = new List<Mesh>();
            var placedStone = new List<int>();
            var placedYield = new List<double>();
            int esUsed = 0;

            foreach (int bi in border)
            {
                var blk = blocks[bi];
                if (blk == null) continue;
                var bbb = blk.GetBoundingBox(true);
                var c0 = bbb.Center;
                var bd = RubbleGeom.SortedDims(bbb);
                var verts = RubbleGeom.VertexArray(blk);

                // candidate stones: unused, AABB-fit + vol-fit, tightest first
                var cand = new List<int>();
                for (int si = 0; si < ns; si++)
                {
                    if (used[si]) continue;
                    if (bvol[bi] > svol[si]) continue;
                    var sbb = stones[si].GetBoundingBox(true);
                    var sd = RubbleGeom.SortedDims(sbb);
                    if (bd[0] > sd[0] || bd[1] > sd[1] || bd[2] > sd[2]) continue;
                    cand.Add(si);
                }
                cand.Sort((a, b) => svol[a].CompareTo(svol[b]));
                if (cand.Count > candidates) cand = cand.GetRange(0, candidates);

                foreach (int si in cand)
                {
                    var st = stones[si];
                    var sc = st.GetBoundingBox(true).Center;

                    Transform best = Transform.Identity;
                    int bestOut = int.MaxValue;
                    foreach (var r in rots)
                    {
                        var rot = RubbleGeom.RotateAbout(r, c0);
                        var xf = Transform.Translation(sc - c0) * rot;
                        int oc = RubbleGeom.OutsideCount(verts, xf, st, bestOut == int.MaxValue ? verts.Length + 1 : bestOut);
                        if (oc < bestOut) { bestOut = oc; best = xf; }
                        if (oc == 0) break;
                    }

                    // (1+8)-ES refine if close
                    if (bestOut > 0 && bestOut <= verts.Length * 0.25)
                    {
                        var cur = best; int curOut = bestOut;
                        double ang = 0.30, tr = 0.06;
                        for (int it = 0; it < 40 && curOut > 0; it++)
                        {
                            int improved = 0;
                            for (int ch = 0; ch < 8; ch++)
                            {
                                var axis = new Vector3d(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1);
                                if (axis.Length < 1e-6) axis = Vector3d.ZAxis;
                                axis.Unitize();
                                var dR = Transform.Rotation((rng.NextDouble() * 2 - 1) * ang, axis, sc);
                                var dt = Transform.Translation((rng.NextDouble() * 2 - 1) * tr, (rng.NextDouble() * 2 - 1) * tr, (rng.NextDouble() * 2 - 1) * tr);
                                var c = dt * dR * cur;
                                int oc = RubbleGeom.OutsideCount(verts, c, st, curOut);
                                if (oc < curOut) { curOut = oc; cur = c; improved++; }
                            }
                            if (improved == 0) { ang *= 0.7; tr *= 0.7; }
                            if (ang < 0.02) break;
                        }
                        if (curOut < bestOut) { bestOut = curOut; best = cur; }
                        esUsed++;
                    }

                    if (bestOut == 0)
                    {
                        var tm = blk.DuplicateMesh();
                        tm.Transform(best);
                        placed.Add(tm);
                        placedStone.Add(si);
                        placedYield.Add(svol[si] > 0 ? bvol[bi] / svol[si] : 0.0);
                        used[si] = true;
                        break;
                    }
                }
            }

            double meanY = 0;
            for (int i = 0; i < placedYield.Count; i++) meanY += placedYield[i];
            if (placedYield.Count > 0) meanY /= placedYield.Count;

            var rpt = new StringBuilder();
            rpt.AppendLine("Rubble Evolved Fit (true enclosure, 1 block/stone)");
            rpt.AppendLine($"placed {placed.Count} / {nb} blocks; stones used {placed.Count}; ES-refined {esUsed}");
            rpt.AppendLine($"mean carve yield {meanY * 100.0:F1}% ; enclosure 100% (every vertex inside)");

            da.SetDataList(0, placed);
            da.SetDataList(1, placedStone);
            da.SetDataList(2, placedYield);
            da.SetData(3, rpt.ToString());
        }
    }
}
