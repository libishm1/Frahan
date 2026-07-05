#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // PickPlaceFramesComponent — geometric primitive for masonry pick-and-
    // place workflows. Given the placement transform per block (typically
    // the output of Block Ground Transforms, sequenced through Block Build
    // Order), emit five Planes per block:
    //
    //   pick               — fixed location where each block is picked up.
    //   approach pick      — pick + approach offset along the approach
    //                        vector. The robot moves to this pose first,
    //                        then descends to pick.
    //   place              — placement pose = pick transformed by the
    //                        block's placement transform.
    //   approach place     — place + approach offset. Robot reaches this
    //                        before descending to place.
    //   retract place      — place + retract offset. Robot retreats here
    //                        after release.
    //
    // No motion / robot logic; just geometry. The component does not check
    // for collisions, joint limits, or kinematic feasibility — that's the
    // robot side's job.
    //
    // ComponentGuid: 456789AB-CDEF-0123-4567-89ABCDEF0123
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Pick / Place Frames.
    /// Emits the five canonical planes (pick, approach-pick, place,
    /// approach-place, retract-place) per block placement transform.
    /// </summary>
        [DesignApplication(
        "Per-block pick-and-place planes for a robot consumer",
        DesignFlow.BottomUp,
        Precedent = "Frahan-original pick-and-place frame generator")]
    public sealed class PickPlaceFramesComponent : FrahanComponentBase
    {
        public PickPlaceFramesComponent()
            : base(
                "Pick Place Frames", "PickPlc",
                "Per-block pick-and-place planes for a robot consumer. " +
                "Wire Place Transforms from Block Ground Transforms; the " +
                "component returns pick + approach-pick (shared across " +
                "all blocks) and place / approach-place / retract-place " +
                "(one per block).",
                "Frahan", "Masonry")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.quinary;

        public override Guid ComponentGuid =>
            new Guid("456789AB-CDEF-0123-4567-89ABCDEF0123");

        protected override Bitmap Icon => IconProvider.Load("FrameBuilder.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTransformParameter("Place Transforms", "T",
                "Per-block placement transform (typically the output of " +
                "Block Ground Transforms, ordered by Build Order). Each " +
                "transform takes the canonical pose at Pick Plane to " +
                "the placed pose.",
                GH_ParamAccess.list);
            p.AddPlaneParameter("Pick Plane", "Pp",
                "Where each canonical block is picked up. Default world XY.",
                GH_ParamAccess.item, Plane.WorldXY);
            p[1].Optional = true;
            p.AddVectorParameter("Approach Vector", "Av",
                "World-frame direction the robot approaches FROM (i.e., " +
                "moves opposite to when descending). Default world +Z so " +
                "the gripper hovers above pick / place poses.",
                GH_ParamAccess.item, Vector3d.ZAxis);
            p[2].Optional = true;
            p.AddNumberParameter("Approach Distance", "Ad",
                "Distance the gripper hovers above pick / place poses " +
                "before descending. Default 0.05 (5 cm in metres, or " +
                "5 mm in millimetres — match your unit system).",
                GH_ParamAccess.item, 0.05);
            p[3].Optional = true;
            p.AddNumberParameter("Retract Distance", "Rd",
                "Distance the gripper retracts after release. Default 0.05.",
                GH_ParamAccess.item, 0.05);
            p[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddPlaneParameter("Pick", "Pi",
                "Shared pick pose (same as input Pick Plane).",
                GH_ParamAccess.item);
            p.AddPlaneParameter("Approach Pick", "ApPi",
                "Pick + approach offset. Hover here before descending to pick.",
                GH_ParamAccess.item);
            p.AddPlaneParameter("Place", "Pl",
                "Per-block place pose = Pick · transform[i].",
                GH_ParamAccess.list);
            p.AddPlaneParameter("Approach Place", "ApPl",
                "Per-block approach-place = place + approach offset.",
                GH_ParamAccess.list);
            p.AddPlaneParameter("Retract Place", "RtPl",
                "Per-block retract-place = place + retract offset.",
                GH_ParamAccess.list);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var transforms = new List<Transform>();
            if (!da.GetDataList(0, transforms) || transforms.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "No place transforms provided.");
                return;
            }

            var pick = Plane.WorldXY;
            da.GetData(1, ref pick);

            var approach = Vector3d.ZAxis;
            da.GetData(2, ref approach);
            if (approach.Length < 1e-9)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Approach Vector is degenerate (zero length).");
                return;
            }
            approach.Unitize();

            double ad = 0.05;
            double rd = 0.05;
            da.GetData(3, ref ad);
            da.GetData(4, ref rd);
            if (ad < 0.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Approach Distance must be >= 0, got {ad}.");
                return;
            }
            if (rd < 0.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Retract Distance must be >= 0, got {rd}.");
                return;
            }

            // Approach-pick: pick origin + approach * ad.
            var approachPick = pick;
            approachPick.Origin = pick.Origin + approach * ad;

            int n = transforms.Count;
            var places = new List<Plane>(n);
            var approachPlaces = new List<Plane>(n);
            var retractPlaces = new List<Plane>(n);

            for (int i = 0; i < n; i++)
            {
                var place = pick;
                place.Transform(transforms[i]);
                places.Add(place);

                var ap = place;
                ap.Origin = place.Origin + approach * ad;
                approachPlaces.Add(ap);

                var rp = place;
                rp.Origin = place.Origin + approach * rd;
                retractPlaces.Add(rp);
            }

            da.SetData(0, pick);
            da.SetData(1, approachPick);
            da.SetDataList(2, places);
            da.SetDataList(3, approachPlaces);
            da.SetDataList(4, retractPlaces);
        }
    }
}
