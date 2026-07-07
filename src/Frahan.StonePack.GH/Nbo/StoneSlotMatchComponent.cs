#nullable disable
using System;
using System.Collections.Generic;
using System.Text;
using Frahan.Masonry.Nbo;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH
{
    /// <summary>
    /// Stone Slot Match (LAPJV).  Pre-assigns inventory stones to wall slots via the
    /// Jonker-Volgenant linear assignment algorithm (globally optimal bijective match,
    /// O(n³)).  The assignment uses a cheap dimension-based cost (no ray-casts) so the
    /// full N×M matrix is built in milliseconds; the NBO planner then executes the
    /// pre-assigned sequence with full drop-to-contact and stability gating.
    ///
    /// Outputs a re-ordered stone list: index j in the output corresponds to slot j
    /// (left-to-right, bottom course first).  Feed directly into Dry-Stone Wall (NBO)
    /// when you want globally-optimal stone selection rather than greedy local picks.
    ///
    /// Ref: Jonker and Volgenant 1987, ACM Trans. Math. Software.
    /// ComponentGuid: D5F10045-0BA0-4ED9-A045-0BA00BA00045
    /// </summary>
    public sealed class StoneSlotMatchComponent : FrahanComponentBase
    {
        public StoneSlotMatchComponent()
            : base("Stone Slot Match (LAPJV)", "SlotMatch",
                "Globally-optimal stone-to-slot pre-assignment using the Jonker-Volgenant " +
                "linear assignment algorithm (O(n^3)).  Returns stones in slot order so the " +
                "NBO planner executes the globally-optimised sequence. " +
                "Ref: Jonker & Volgenant 1987.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("D5F10045-0BA0-4ED9-A045-0BA00BA00045");
        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override System.Drawing.Bitmap Icon =>
            IconProvider.Load("DryStoneWallNbo.png");  // reuse until a dedicated icon exists

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Inventory", "I",
                "Stone meshes to assign.", GH_ParamAccess.list);
            p.AddNumberParameter("Wall Length", "L",
                "Wall length (m).", GH_ParamAccess.item, 3.0);
            p.AddNumberParameter("Target Height", "H",
                "Fill-to height (m).", GH_ParamAccess.item, 1.6);
            p.AddNumberParameter("Gap", "G",
                "Minimum gap between stones along a course (m).",
                GH_ParamAccess.item, 0.02);
            p.AddNumberParameter("Course Offset", "O",
                "Running-bond offset on alternating courses (m).",
                GH_ParamAccess.item, 0.25);
            p.AddBooleanParameter("Run", "R",
                "Set true to run.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Ordered Stones", "S",
                "Stones re-ordered by slot (slot 0 first). Feed into NBO wall fill.", GH_ParamAccess.list);
            p.AddIntegerParameter("Slot Assignment", "A",
                "A[j] = original inventory index assigned to slot j, or -1 if the slot is unfilled.",
                GH_ParamAccess.list);
            p.AddTextParameter("Report", "Rp",
                "Assignment report.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var meshes   = new List<Mesh>();
            double wallL = 3.0, targetH = 1.6, gap = 0.02, offset = 0.25;
            bool run = false;

            if (!da.GetDataList(0, meshes) || meshes.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No inventory.");
                return;
            }
            da.GetData(1, ref wallL);
            da.GetData(2, ref targetH);
            da.GetData(3, ref gap);
            da.GetData(4, ref offset);
            da.GetData(5, ref run);

            if (!run)
            {
                da.SetData(2, $"Run = false. {meshes.Count} stones ready.");
                return;
            }

            // Analyse stone shapes.
            var shapes = new List<StoneShape>(meshes.Count);
            for (int i = 0; i < meshes.Count; i++)
            {
                try   { shapes.Add(StoneShapeAnalyzer.Analyze(meshes[i])); }
                catch { shapes.Add(null); }
            }

            // Build slot grid.
            var opt = new NboFillOptions
            {
                WallLength   = wallL,
                TargetHeight = targetH,
                Gap          = gap,
                CourseOffset = offset,
            };
            var slots = StoneSlotMatcher.GenerateWallSlots(shapes, opt);

            // Run LAPJV assignment.
            var assignment = StoneSlotMatcher.Match(meshes, shapes, null, slots, opt);

            // Build output: stones in slot order.
            var ordered = new List<Mesh>(slots.Count);
            var slotIdxOut = new List<int>(slots.Count);
            for (int j = 0; j < slots.Count; j++)
            {
                int si = assignment.SlotToStone[j];
                slotIdxOut.Add(si);
                if (si >= 0 && si < meshes.Count)
                    ordered.Add(meshes[si]);
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Stones: {meshes.Count}  Slots: {slots.Count}  Assigned: {assignment.Assigned}");
            sb.AppendLine($"Algorithm: {(assignment.LapjvUsed ? "LAPJV native (" + StoneSlotMatcher.LapjvVersion + ")" : "greedy fallback (frahan_lapjv.dll absent)")}");
            sb.Append($"Total cost: {assignment.TotalCost:F4}");

            if (!assignment.LapjvUsed)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "frahan_lapjv.dll not found — using greedy fallback. " +
                    "Build native/lapjv_shim/build_native.ps1 for the LAPJV solver.");

            Message = $"{assignment.Assigned}/{slots.Count} | {(assignment.LapjvUsed ? "LAPJV" : "greedy")}";

            da.SetDataList(0, ordered);
            da.SetDataList(1, slotIdxOut);
            da.SetData(2, sb.ToString().TrimEnd());
        }
    }
}
