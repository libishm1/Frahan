using System;
using System.Collections.Generic;
using System.Text;
using Frahan.Masonry.Nbo;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH
{
    /// <summary>
    /// Dry-Stone Wall (NBO). Incrementally fills a wall from a stone inventory with
    /// the Next-Best-Object planner: at each placement front every candidate stone is
    /// oriented by the hybrid rule (rest on the hull stable face + yaw the long axis
    /// into the wall), dropped to contact on the as-built, gated for stability
    /// (CoM-over-support + d/h >= 0.5 + seating), and the lowest-cost admissible stone
    /// is committed. Outputs the ordered, gated placement sequence -- the robot job.
    /// Algorithm: Furrer 2017 (online next-best-object) / Johns 2020 (autonomous dry stone);
    /// stable-pose by Goldberg-Mirtich 1999. Optional Bullet-settle confirmation.
    /// </summary>
    public sealed class DryStoneWallNboComponent : FrahanComponentBase
    {
        public DryStoneWallNboComponent()
            : base("Dry-Stone Wall (NBO)", "NBOWall",
                "Fill a dry-stone wall from a stone inventory with the Next-Best-Object planner " +
                "(hybrid orient -> drop-to-contact -> analytic stability gate -> lowest-cost pick). " +
                "Outputs the ordered, gated placement sequence. Optional target envelope, physical Seat " +
                "validation (settle each placement onto the fixed wall), and Bullet settle / CRA confirmation. " +
                "Ref: Furrer 2017 / Johns 2020.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid => new Guid("D5F10030-0BA0-4ED9-A030-0BA00BA00030");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => IconProvider.Load("DryStoneWallNbo.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Inventory", "I", "Stone meshes to draw from.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Wall Length", "L",
                "Wall length to fill along +X (m). Overridden by Envelope if supplied.", GH_ParamAccess.item, 3.0);
            pManager.AddNumberParameter("Target Height", "H",
                "Fill until the wall top reaches this height (m). Overridden by Envelope.", GH_ParamAccess.item, 1.6);
            pManager.AddNumberParameter("Course Offset", "O",
                "Running-bond offset applied on alternating courses (m).", GH_ParamAccess.item, 0.25);
            pManager.AddNumberParameter("Gap", "G",
                "Minimum gap between stones along a course (m).", GH_ParamAccess.item, 0.02);
            pManager.AddBrepParameter("Envelope", "E",
                "Optional closed target envelope (Brep): bounds the wall and rejects stones whose CoM falls outside it.",
                GH_ParamAccess.item);
            pManager.AddCurveParameter("Spine", "Sp",
                "Optional plan-rim spine curve: the wall follows it (front advances along arc length, long axis " +
                "into the wall along the local normal). A straight line reproduces the straight-X wall.",
                GH_ParamAccess.item);
            pManager.AddBooleanParameter("Confirm", "C",
                "Run a Bullet physics settle confirmation of the produced wall.", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("CRA", "Cra",
                "Run the compas-CRA rigid-block-equilibrium wall-gate (the strongest stability tier) on the produced wall.",
                GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Run", "R", "Execute the fill.", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Seat", "Se",
                "Physically VALIDATE each placement: drop every candidate (in its top stable orientations) onto " +
                "the fixed as-built and keep only the one that beds firmly, committed at its settled pose. Builds " +
                "a wall that holds (fewer stones, no slips) -- the robot-ready mode. Needs the Bullet backend.",
                GH_ParamAccess.item, false);
            pManager[5].Optional = true;   // Envelope
            pManager[6].Optional = true;   // Spine
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Placed", "Pl", "Placed stone meshes in placement order.", GH_ParamAccess.list);
            pManager.AddTransformParameter("Transforms", "X", "Per-stone placement transform (matches Placed order).", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Course", "Cr", "Per-stone course index.", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Stable", "St", "Per-stone analytic stability verdict.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Cost", "Co", "Per-stone selection cost (lower is better).", GH_ParamAccess.list);
            pManager.AddTextParameter("Report", "Rp", "Solve summary.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var inv = new List<Mesh>();
            if (!da.GetDataList(0, inv) || inv.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No inventory meshes.");
                return;
            }
            double len = 3.0, h = 1.6, off = 0.25, gap = 0.02;
            bool confirm = false, cra = false, run = false, seat = false;
            Brep env = null; Curve spine = null;
            da.GetData(1, ref len); da.GetData(2, ref h); da.GetData(3, ref off); da.GetData(4, ref gap);
            da.GetData(5, ref env); da.GetData(6, ref spine);
            da.GetData(7, ref confirm); da.GetData(8, ref cra); da.GetData(9, ref run); da.GetData(10, ref seat);

            if (!run) { da.SetData(5, "Run is false; toggle to execute."); return; }

            var stones = new List<Mesh>();
            foreach (var m in inv) if (m != null && m.Vertices.Count >= 4) stones.Add(m);
            if (stones.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No usable meshes (each needs >= 4 vertices).");
                return;
            }

            var opt = new NboFillOptions
            {
                WallLength = len,
                TargetHeight = h,
                CourseOffset = off,
                Gap = gap,
                Envelope = env,
                SettleValidate = seat,
            };
            var seq = spine != null
                ? NboPlanner.FillSpine(stones, spine, opt)
                : NboPlanner.FillWall(stones, opt);

            var placed = new List<Mesh>();
            var xforms = new List<object>();
            var courses = new List<int>();
            var stable = new List<bool>();
            var costs = new List<double>();
            foreach (var s in seq.Steps)
            {
                var pm = stones[s.StoneIndex].DuplicateMesh();
                pm.Transform(s.Placement);
                placed.Add(pm);
                xforms.Add(new GH_Transform(s.Placement));
                courses.Add(s.Course);
                stable.Add(s.Verdict.Stable);
                costs.Add(s.Cost);
            }

            var rep = new StringBuilder();
            rep.AppendLine($"Dry-Stone Wall (NBO): placed {seq.Placed}/{stones.Count} stones, " +
                           $"{seq.StableCount}/{seq.Placed} stable, {seq.Courses} courses, " +
                           $"top {seq.TopHeight:F2} m, length {seq.FilledLength:F2} m.");
            if (env != null) rep.AppendLine("Target envelope active (bounds + CoM containment).");
            if (spine != null) rep.AppendLine($"Plan-rim spine active (length {seq.FilledLength:F2} m).");
            if (seat) rep.AppendLine("Seat validation ON: each placement was settle-checked onto the fixed wall and " +
                                     "committed at its bedded pose (physically seated, robot-ready; fewer stones, no slips).");
            if (confirm)
            {
                var c = NboSettle.ConfirmSettle(placed);
                if (!c.Available)
                    rep.AppendLine("Settle confirm: Bullet backend unavailable (no libbulletc.dll).");
                else
                    rep.AppendLine($"Settle confirm: {c.Held}/{c.Total} held (mean disp {c.MeanDisplacement * 1000:F0} mm, max {c.MaxDisplacement * 1000:F0} mm).");
            }
            if (cra && placed.Count > 0)
            {
                try
                {
                    var cr = NboCra.ConfirmSettledCra(placed);   // incremental settle FIRST, then CRA on the settled patches
                    rep.AppendLine($"CRA wall-gate (settle->CRA): {(cr.IsStable ? "STABLE" : "not confirmed")} ({cr.Status}), " +
                                   $"max friction util {cr.MaxFrictionUtilization:F2}, {cr.InterfaceCount} interfaces.");
                    if (!seat)
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                            "CRA runs on the incremental settled wall. For the strongest verdict, enable Seat so the wall " +
                            "is built physically seated; without it the upper courses can slip and the verdict reflects that.");
                }
                catch (Exception ex)
                {
                    rep.AppendLine("CRA wall-gate: failed (" + ex.Message + ").");
                }
            }
            if (seq.Placed == 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No stones placed (check inventory size vs gate / envelope).");

            Message = $"{seq.Placed} placed / {seq.StableCount} stable / {seq.Courses}c" + (seat ? " / seated" : "");

            da.SetDataList(0, placed);
            da.SetDataList(1, xforms);
            da.SetDataList(2, courses);
            da.SetDataList(3, stable);
            da.SetDataList(4, costs);
            da.SetData(5, rep.ToString());
        }
    }
}
