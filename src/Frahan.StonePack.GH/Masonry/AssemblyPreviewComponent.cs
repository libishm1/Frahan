#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.DataModel;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // AssemblyPreviewComponent — visualizes a MasonryAssembly: one Mesh per
    // block, plus per-interface contact polylines + centroids + normals so
    // the user can see exactly where contacts were detected.
    //
    // Companion to PackPreview (which only handles AshlarPackResult); this
    // works on any MasonryAssembly regardless of how it was assembled
    // (Auto Interfaces, Robust Auto Interfaces, hand-authored, etc.).
    //
    // ComponentGuid: 12345678-9ABC-DEF0-1234-56789ABCDEF0
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Assembly Preview.
    /// Visualizes a MasonryAssembly's blocks and contact interfaces.
    /// </summary>
        [DesignApplication(
        "Visualizes a MasonryAssembly",
        DesignFlow.BottomUp,
        Precedent = "Frahan-original assembly-visualisation helper")]
    public sealed class AssemblyPreviewComponent : FrahanComponentBase
    {
        public AssemblyPreviewComponent()
            : base(
                "Assembly Preview", "AsmPrev",
                "Visualizes a MasonryAssembly. Outputs one Mesh per block " +
                "(separated into free + fixed lists), the IDs of each, and " +
                "per-interface contact polylines + centroids + normals for " +
                "debugging.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("12345678-9ABC-DEF0-1234-56789ABCDEF0");

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override Bitmap Icon => IconProvider.Load("AssemblyState.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Assembly", "A",
                "MasonryAssembly DTO (from Masonry Assembly or Ashlar Pack).",
                GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Free Meshes", "Mf",
                "Block meshes for blocks that are NOT fixed by boundary conditions.",
                GH_ParamAccess.list);
            p.AddMeshParameter("Fixed Meshes", "Mfix",
                "Block meshes for blocks that ARE fixed (grounded).",
                GH_ParamAccess.list);
            p.AddTextParameter("Free Block Ids", "If",
                "IDs of the free blocks (parallel to Free Meshes).",
                GH_ParamAccess.list);
            p.AddTextParameter("Fixed Block Ids", "Ifix",
                "IDs of the fixed blocks (parallel to Fixed Meshes).",
                GH_ParamAccess.list);
            p.AddCurveParameter("Contact Polylines", "C",
                "One closed polyline per MasonryInterface — the contact " +
                "polygon as drawn on the canvas.",
                GH_ParamAccess.list);
            p.AddPointParameter("Contact Centroids", "Cc",
                "Centroid of each contact polygon. Use to anchor labels " +
                "or to draw normals.",
                GH_ParamAccess.list);
            p.AddVectorParameter("Contact Normals", "Cn",
                "Surface normal at each contact, pointing from block A to " +
                "block B (Frahan convention).",
                GH_ParamAccess.list);
            p.AddTextParameter("Contact Pairs", "Cp",
                "Per-interface 'aId -> bId' string for hover-debugging.",
                GH_ParamAccess.list);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            object raw = null;
            if (!da.GetData(0, ref raw))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No assembly provided.");
                return;
            }
            MasonryAssembly assembly = UnwrapAssembly(raw);
            if (assembly == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Assembly is not a MasonryAssembly (got {GhInterop.DescribeType(raw)}).");
                return;
            }

            var freeMeshes = new List<Mesh>(assembly.BlockCount);
            var fixedMeshes = new List<Mesh>(assembly.BlockCount);
            var freeIds = new List<string>(assembly.BlockCount);
            var fixedIds = new List<string>(assembly.BlockCount);

            for (int i = 0; i < assembly.Blocks.Count; i++)
            {
                var b = assembly.Blocks[i];
                var m = GhInterop.BlockToMesh(b);
                if (assembly.BoundaryConditions.IsFixed(b.Id))
                {
                    fixedMeshes.Add(m);
                    fixedIds.Add(b.Id);
                }
                else
                {
                    freeMeshes.Add(m);
                    freeIds.Add(b.Id);
                }
            }

            var polylines = new List<Curve>(assembly.InterfaceCount);
            var centroids = new List<Point3d>(assembly.InterfaceCount);
            var normals = new List<Vector3d>(assembly.InterfaceCount);
            var pairs = new List<string>(assembly.InterfaceCount);
            for (int i = 0; i < assembly.Interfaces.Count; i++)
            {
                var iface = assembly.Interfaces[i];
                var pts = new List<Point3d>(iface.ContactPolygon.Count + 1);
                double cx = 0, cy = 0, cz = 0;
                for (int v = 0; v < iface.ContactPolygon.Count; v++)
                {
                    var cv = iface.ContactPolygon[v];
                    pts.Add(new Point3d(cv.X, cv.Y, cv.Z));
                    cx += cv.X; cy += cv.Y; cz += cv.Z;
                }
                if (pts.Count > 0) pts.Add(pts[0]); // close the loop
                if (iface.ContactPolygon.Count > 0)
                {
                    cx /= iface.ContactPolygon.Count;
                    cy /= iface.ContactPolygon.Count;
                    cz /= iface.ContactPolygon.Count;
                }
                var pl = new Polyline(pts);
                polylines.Add(pl.ToPolylineCurve());
                centroids.Add(new Point3d(cx, cy, cz));
                normals.Add(new Vector3d(iface.NormalX, iface.NormalY, iface.NormalZ));
                pairs.Add($"{iface.BlockAId} -> {iface.BlockBId}");
            }

            da.SetDataList(0, freeMeshes);
            da.SetDataList(1, fixedMeshes);
            da.SetDataList(2, freeIds);
            da.SetDataList(3, fixedIds);
            da.SetDataList(4, polylines);
            da.SetDataList(5, centroids);
            da.SetDataList(6, normals);
            da.SetDataList(7, pairs);
        }

        private static MasonryAssembly UnwrapAssembly(object raw)
        {
            if (raw is MasonryAssembly direct) return direct;
            if (raw is GH_ObjectWrapper wrap && wrap.Value is MasonryAssembly fromWrap)
                return fromWrap;
            return null;
        }
    }
}
