using System;
using System.Collections.Generic;
using Frahan.Masonry.Nbo;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH
{
    /// <summary>
    /// Next-Best-Object Pose -> Robot Frame. The planner-to-robot bridge (Layer-0): for
    /// each stone + its NBO placement transform it computes a top-pick grasp (gripper
    /// over the CoM, on the face opposite the resting face, tool pointing down), then the
    /// world TCP frames to PICK the stone where it sits and to PLACE it, an approach
    /// waypoint above the place frame, and the place pose as a UR p[x,y,z,rx,ry,rz]
    /// (metres + axis-angle) expressed in the robot base frame. Grip width/length are
    /// reported for gripper sizing. The grasp/tool offset belongs to the planner because
    /// it depends on the stone geometry; IK/trajectory/execution are downstream.
    /// </summary>
    public sealed class NboPoseToRobotFrameComponent : FrahanComponentBase
    {
        public NboPoseToRobotFrameComponent()
            : base("Next-Best-Object Pose → Robot Frame", "NBORobot",
                "Turn NBO placements into robot TCP frames + UR poses via a top-pick grasp model. " +
                "Outputs pick / place / approach frames, the place pose as UR p[...] in the robot base, " +
                "and the grip width/length. The live robot stays downstream (Robots/visose, " +
                "UnderAutomation, compas_fab); this is the planner->robot handoff only.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid => new Guid("D5F10031-0BA0-4ED9-A031-0BA00BA00031");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => IconProvider.Load("NboPoseRobotFrame.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Stones", "S",
                "Source stone meshes (where they currently sit, for the pick frame).", GH_ParamAccess.list);
            pManager.AddTransformParameter("Placements", "X",
                "NBO placement transforms, matching the Stones order.", GH_ParamAccess.list);
            pManager.AddPlaneParameter("Robot Base", "B",
                "Robot base frame in world coords; the place pose is expressed in it.", GH_ParamAccess.item, Plane.WorldXY);
            pManager.AddNumberParameter("Approach", "A",
                "Approach/retract clearance above each place frame (m).", GH_ParamAccess.item, 0.15);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPlaneParameter("Pick Frames", "Pk", "TCP frame to grab each stone where it sits.", GH_ParamAccess.list);
            pManager.AddPlaneParameter("Place Frames", "Pl", "TCP frame to place each stone.", GH_ParamAccess.list);
            pManager.AddPlaneParameter("Approach Frames", "Ap", "Pre-place / retract waypoint above each place frame.", GH_ParamAccess.list);
            pManager.AddTextParameter("Place Poses", "Ps", "Place TCP as a UR p[x,y,z,rx,ry,rz] (m, axis-angle) in the robot base.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Grip Width", "Gw", "Stone extent across the jaw axis (gripper opening).", GH_ParamAccess.list);
            pManager.AddNumberParameter("Grip Length", "Gl", "Stone extent along the jaw-open axis.", GH_ParamAccess.list);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var stones = new List<Mesh>();
            var xforms = new List<Transform>();
            if (!da.GetDataList(0, stones) || stones.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No stones.");
                return;
            }
            if (!da.GetDataList(1, xforms) || xforms.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No placement transforms.");
                return;
            }
            Plane basePlane = Plane.WorldXY;
            double approach = 0.15;
            da.GetData(2, ref basePlane);
            da.GetData(3, ref approach);

            int n = Math.Min(stones.Count, xforms.Count);
            if (stones.Count != xforms.Count)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Stones ({stones.Count}) and Placements ({xforms.Count}) differ; using {n}.");

            var pick = new List<Plane>(); var place = new List<Plane>(); var appr = new List<Plane>();
            var poses = new List<string>(); var gw = new List<double>(); var gl = new List<double>();
            for (int i = 0; i < n; i++)
            {
                var m = stones[i];
                if (m == null || m.Vertices.Count < 4) continue;
                var shape = StoneShapeAnalyzer.Analyze(m);
                var rest = StoneShapeAnalyzer.BestRestingFace(shape);
                if (rest == null) continue;
                var grasp = NboGrasp.TopPick(shape, rest);

                var pickW = NboGrasp.PickFrame(grasp, Transform.Identity);   // grasp where the stone sits
                var placeW = NboGrasp.PlaceFrame(grasp, xforms[i]);
                var apprW = NboGrasp.WithApproach(placeW, approach);
                var placeB = NboGrasp.InBase(placeW, basePlane);
                var pose = NboGrasp.ToUrPose(placeB);

                pick.Add(pickW); place.Add(placeW); appr.Add(apprW);
                poses.Add(pose.ToString());
                gw.Add(grasp.GripWidth); gl.Add(grasp.GripLength);
            }

            da.SetDataList(0, pick);
            da.SetDataList(1, place);
            da.SetDataList(2, appr);
            da.SetDataList(3, poses);
            da.SetDataList(4, gw);
            da.SetDataList(5, gl);
        }
    }
}
