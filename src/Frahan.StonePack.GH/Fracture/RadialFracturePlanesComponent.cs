#nullable disable
using System;
using System.Drawing;
using Frahan.Masonry.Fractures;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // RadialFracturePlanesComponent — N planes radiating around a single
    // axis line, like splitting a log into pie wedges.
    //
    // ComponentGuid: A9CADBEC-FDAE-4456-789A-012345678BCD
    // =========================================================================

    /// <summary>
    /// Frahan Cut &gt; Fracture &gt; Radial Fracture Planes.
    /// </summary>
        [DesignApplication(
        "N planes that share a common axis line, rotated by  180/N degrees per plane",
        DesignFlow.BottomUp,
        Precedent = "Frahan-original radial fracture set")]
    [Algorithm("Radial / fan fracture set", "Frahan-original",
        Note = "frame construction plus rotation; no canonical published source")]
    public sealed class RadialFracturePlanesComponent : GH_Component
    {
        public RadialFracturePlanesComponent()
            : base(
                "Radial Fracture Planes", "RadialFx",
                "N planes that share a common axis line, rotated by " +
                "180/N degrees per plane. Pie-wedge cut pattern; common " +
                "for log-like or cylindrical stones. Frahan-original method.",
                "Frahan", "Fracture")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("A9CADBEC-FDAE-4456-789A-012345678BCD");

        protected override Bitmap Icon => IconProvider.Load("CompressionDesign.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddPointParameter("Center", "C",
                "Point on the rotation axis.",
                GH_ParamAccess.item, new Point3d(0, 0, 0));
            p.AddVectorParameter("Axis", "A",
                "Axis direction.",
                GH_ParamAccess.item, new Vector3d(0, 0, 1));
            p.AddIntegerParameter("Count", "N",
                "Number of radial planes. Must be >= 0.",
                GH_ParamAccess.item, 4);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Planes", "P",
                "FracturePlane DTOs around the rotation axis.",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            Point3d center = Point3d.Origin;
            Vector3d axis = Vector3d.ZAxis;
            int count = 4;
            da.GetData(0, ref center);
            da.GetData(1, ref axis);
            da.GetData(2, ref count);

            try
            {
                var planes = FracturePlaneGenerators.Radial(
                    center.X, center.Y, center.Z,
                    axis.X, axis.Y, axis.Z,
                    count);
                da.SetDataList(0, planes);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Radial generation failed: {ex.Message}");
            }
        }
    }
}
