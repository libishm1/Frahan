using System;
using System.Collections.Generic;
using Frahan.Masonry.Nbo;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH
{
    /// <summary>
    /// Force-Seat (URScript). Emits Universal-Robots URScript to PLACE and FORCE-SEAT a
    /// stone at each place TCP frame: approach above, descend, zero the F/T sensor,
    /// force_mode press down the seat-frame Z until the stone rocks into seat, then
    /// retract. Force-seating is the irregular-stone enabler -- the physical analog of the
    /// planner's drop-to-contact + settle (Furrer 2017 used a UR10 + FT150 sensor).
    /// TEXT ONLY: this generates URScript; it never sends to hardware. The robot backend
    /// (Robots/visose "Remote UR", UnderAutomation, Dashboard load+play) streams it, and
    /// hardware-in-loop stays dormant -- validate in URSim first.
    /// </summary>
    public sealed class ForceSeatUrScriptComponent : FrahanComponentBase
    {
        public ForceSeatUrScriptComponent()
            : base("Force-Seat (URScript)", "FSeat",
                "Emit UR URScript to place + force-seat a stone at each place TCP frame " +
                "(approach -> descend -> force_mode press -> retract). TEXT ONLY (code-gen, no " +
                "hardware send); validate in URSim. Force-seating is the irregular-stone enabler.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid => new Guid("D5F10032-0BA0-4ED9-A032-0BA00BA00032");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => IconProvider.Load("ForceSeatUrScript.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPlaneParameter("Place Frames", "F",
                "Seat TCP frames (from Next-Best-Object Pose -> Robot Frame). Frame Z is the press direction.",
                GH_ParamAccess.list);
            pManager.AddPlaneParameter("Robot Base", "B",
                "Robot base frame in world coords; poses are emitted in it.", GH_ParamAccess.item, Plane.WorldXY);
            pManager.AddNumberParameter("Seat Force", "Fz", "Downward press force to seat the stone (N).", GH_ParamAccess.item, 50.0);
            pManager.AddNumberParameter("Approach", "A", "Approach/retract clearance above the seat (m).", GH_ParamAccess.item, 0.15);
            pManager.AddNumberParameter("Descend Speed", "V", "Compliant descent speed during seating (m/s).", GH_ParamAccess.item, 0.04);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("URScript", "U",
                "One place + force-seat URScript program per place frame (text; validate in URSim before any hardware).",
                GH_ParamAccess.list);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var frames = new List<Plane>();
            if (!da.GetDataList(0, frames) || frames.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No place frames.");
                return;
            }
            Plane basePlane = Plane.WorldXY;
            double force = 50.0, approach = 0.15, descend = 0.04;
            da.GetData(1, ref basePlane);
            da.GetData(2, ref force);
            da.GetData(3, ref approach);
            da.GetData(4, ref descend);

            var opt = new UrSeatOptions { SeatForce = force, DescendSpeed = descend };

            var scripts = new List<string>(frames.Count);
            int idx = 0;
            foreach (var f in frames)
            {
                var apprFrame = NboGrasp.WithApproach(f, approach);
                var seatPose = NboGrasp.ToUrPose(NboGrasp.InBase(f, basePlane));
                var apprPose = NboGrasp.ToUrPose(NboGrasp.InBase(apprFrame, basePlane));
                opt.FunctionName = "frahan_place_" + idx;
                scripts.Add(NboUrScript.PlaceAndSeat(seatPose, apprPose, opt));
                idx++;
            }

            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "Code-gen only: URScript is NOT sent to hardware. Validate in URSim before any robot run.");
            da.SetDataList(0, scripts);
        }
    }
}
