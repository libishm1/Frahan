#nullable disable
using System;
using System.Drawing;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Quarry;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // ConvexHullSlabComponent — wraps a (possibly non-convex) Rhino mesh in
    // its convex hull and emits the hull as a Slab. Useful for rough quarry-
    // block scans where the user accepts losing concavity in exchange for a
    // valid Slab.
    //
    // ComponentGuid: ECFDAEBF-CADB-4234-5678-9012345678AB
    // =========================================================================

    /// <summary>
    /// Frahan Cut &gt; Quarry &gt; Convex Hull Slab.
    /// </summary>
    [Algorithm("QuickHull convex hull", "Barber, Dobkin, Huhdanpaa 1996, The Quickhull algorithm for convex hulls, ACM TOMS 22(4):469-483", WikiPath = "wiki/index/references.md")]
        [DesignApplication(
        "Builds the convex hull of a Rhino mesh's vertices and emits  the hull as a Slab",
        DesignFlow.Bridges,
        Precedent = "Standard QuickHull (Barber Dobkin Huhdanpaa 1996); Rhino Mesh.CreateConvexHull primitive")]
    public sealed class ConvexHullSlabComponent : FrahanComponentBase
    {
        public ConvexHullSlabComponent()
            : base(
                "Convex Hull Slab", "HullSlab",
                "Builds the convex hull of a Rhino mesh's vertices and emits " +
                "the hull as a Slab. Loses concavity by definition; opt in " +
                "for fast Mesh -> Slab on roughly-convex inputs. Implements QuickHull (Barber-Dobkin-Huhdanpaa 1996).",
                "Frahan", "Block Cutting")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("ECFDAEBF-CADB-4234-5678-9012345678AB");

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override Bitmap Icon => IconProvider.Load("ConvexHull2D.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Mesh", "M",
                "Rhino mesh whose vertices seed the hull. At least 4 non-coplanar vertices required.",
                GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Slab", "S",
                "Convex-hull Slab.",
                GH_ParamAccess.item);
            p.AddMeshParameter("Mesh", "M",
                "Convex hull as a Rhino Mesh (same geometry, fan-triangulated).",
                GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            Mesh mesh = null;
            if (!da.GetData(0, ref mesh) || mesh == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No mesh provided.");
                return;
            }
            int vCount = mesh.Vertices.Count;
            if (vCount < 4)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Mesh needs at least 4 vertices for a 3D hull, got {vCount}.");
                return;
            }
            var verts = new double[vCount * 3];
            for (int i = 0; i < vCount; i++)
            {
                var pt = mesh.Vertices[i];
                verts[3 * i + 0] = pt.X;
                verts[3 * i + 1] = pt.Y;
                verts[3 * i + 2] = pt.Z;
            }

            Slab hull;
            try
            {
                hull = ConvexHullBuilder.BuildSlab(verts);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Convex hull failed: {ex.Message}");
                return;
            }
            da.SetData(0, hull);
            da.SetData(1, GhInterop.SlabToMesh(hull));
        }
    }
}
