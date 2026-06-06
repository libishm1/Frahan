#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.Fractures;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // VoronoiFracturePlanesComponent — emits the perpendicular bisector
    // plane between every pair of seed points. Cutting a slab by these
    // bisectors approximates Voronoi-cell decomposition.
    //
    // Lives in the GH project (not Core) because it accepts Rhino Point3d
    // input. The Core generator takes a flat double[] for runtime
    // independence.
    //
    // ComponentGuid: A8B9CADB-ECFD-4EF0-1234-567890123456
    // =========================================================================

    /// <summary>
    /// Frahan Cut &gt; Fracture &gt; Voronoi Fracture Planes.
    /// </summary>
        [DesignApplication(
        "Emits the perpendicular bisector plane between every pair of  input seed Points",
        DesignFlow.BottomUp,
        Precedent = "Aurenhammer 1991 Voronoi diagrams; standard 3D Voronoi shatter pattern")]
    [Algorithm("Voronoi perpendicular-bisector cell construction",
        "Aurenhammer, F. (1991). \"Voronoi diagrams—a survey.\" ACM Computing Surveys 23(3):345-405",
        Doi = "10.1145/116873.116880",
        WikiPath = "wiki/index/references.md#Aurenhammer1991Voronoi")]
    public sealed class VoronoiFracturePlanesComponent : GH_Component
    {
        public VoronoiFracturePlanesComponent()
            : base(
                "Voronoi Fracture Planes", "VoroFx",
                "Emits the perpendicular bisector plane between every pair of " +
                "input seed Points. Cutting a slab with these planes " +
                "approximates Voronoi-cell decomposition. Implements Voronoi partition (Aurenhammer 1991).",
                "Frahan", "Fracture")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("A8B9CADB-ECFD-4EF0-1234-567890123456");

        protected override Bitmap Icon => IconProvider.Load("Voronoi.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddPointParameter("Seeds", "S",
                "Voronoi seed points. At least 2 required.",
                GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Planes", "P",
                "FracturePlane DTOs (one per distinct seed pair).",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var pts = new List<Point3d>();
            if (!da.GetDataList(0, pts))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No seed points provided.");
                return;
            }
            if (pts.Count < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Need at least 2 seed points, got {pts.Count}.");
                return;
            }

            var flat = new double[pts.Count * 3];
            for (int i = 0; i < pts.Count; i++)
            {
                flat[3 * i + 0] = pts[i].X;
                flat[3 * i + 1] = pts[i].Y;
                flat[3 * i + 2] = pts[i].Z;
            }

            try
            {
                var planes = FracturePlaneGenerators.VoronoiBisectors(flat);
                da.SetDataList(0, planes);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Voronoi generation failed: {ex.Message}");
            }
        }
    }
}
