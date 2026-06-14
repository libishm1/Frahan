#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.Cutting;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // SlabCutByFracturePolygonsComponent — Phase E.2 finite-extent fracture
    // cutter. Wraps FractureCutter.CutMany(slabs, polygons, options).
    //
    // Mirrors SlabCutByFracturesComponent's "list of slabs + list of cutters
    // + options" pattern but the cutter input is a list of FracturePolygon
    // DTOs (finite, convex, planar polygons) rather than infinite Rhino
    // Planes. Use the ExtendPartialToInfinitePlane flag to opt in to the
    // partial-fracture-as-infinite-plane fallback (FractureCutOutcome.PartialExtended).
    //
    // ComponentGuid: E4D5F607-8C9B-40BD-CE3F-405162738491
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Slab Cut By Fracture Polygons.
    /// Cuts a list of <see cref="Slab"/> DTOs by an ordered list of finite
    /// <see cref="FracturePolygon"/> DTOs. Each polygon is applied to every
    /// surviving slab in turn (iterative <see cref="FractureCutter.CutMany"/>).
    /// </summary>
        [DesignApplication(
        "Cuts a list of Slabs by a list of finite FracturePolygons",
        DesignFlow.TopDown,
        Precedent = "Frahan-original slab-cut by fracture polygons")]
    public sealed class SlabCutByFracturePolygonsComponent : FrahanComponentBase
    {
        public SlabCutByFracturePolygonsComponent()
            : base(
                "Slab Cut By Fracture Polygons", "SlabCutFP",
                "Cuts a list of Slabs by a list of finite FracturePolygons. " +
                "A polygon that fully contains the slab cross-section produces " +
                "two pieces; a polygon that misses the slab is a passthrough; " +
                "a polygon that only partially overlaps the cross-section is a " +
                "passthrough unless ExtendPartial is set, in which case the " +
                "polygon's supporting plane is used as an infinite cut.",
                "Frahan", "Fracture")
        {
        }

        // GUID literal: E4D5F607-8C9B-40BD-CE3F-405162738491
        public override Guid ComponentGuid =>
            new Guid("E4D5F607-8C9B-40BD-CE3F-405162738491");

        protected override Bitmap Icon => IconProvider.Load("BlockCutOpt.png");

        // ─── Params ─────────────────────────────────────────────────────────

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Mesh", "M",
                "Convex meshes to cut. Standard Rhino mesh wires; the cutter " +
                "converts to its internal Slab DTO automatically.",
                GH_ParamAccess.list);
            p.AddGenericParameter("FracturePolygon", "F",
                "FracturePolygon DTOs (from Fracture Polygon From Curve).",
                GH_ParamAccess.list);
            p.AddBooleanParameter("ExtendPartial", "X",
                "When true, a fracture polygon that only partially covers the " +
                "slab cross-section is treated as if it were an infinite plane. " +
                "Default: false (partial fractures pass through untouched).",
                GH_ParamAccess.item, false);
            p[2].Optional = true;
            p.AddNumberParameter("Eps", "E",
                "Vertex-classification epsilon for the underlying SlabCutter. " +
                "Default 1e-9.",
                GH_ParamAccess.item, 1e-9);
            p[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Slab", "S",
                "Output Slabs after cutting.",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Count", "N",
                "Number of resulting Slabs.",
                GH_ParamAccess.item);
            p.AddNumberParameter("TotalVolume", "V",
                "Sum of signed volumes of all output Slabs (sanity check).",
                GH_ParamAccess.item);
            p.AddMeshParameter("Mesh", "M",
                "Output Slabs as Rhino Meshes (parallel to the Slab list).",
                GH_ParamAccess.list);
        }

        // ─── Solve ──────────────────────────────────────────────────────────

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var meshes = new List<Mesh>();
            da.GetDataList(0, meshes);

            var rawPolygons = new List<object>();
            da.GetDataList(1, rawPolygons);

            bool extend = false;
            da.GetData(2, ref extend);

            double eps = 1e-9;
            da.GetData(3, ref eps);

            if (meshes.Count == 0 || rawPolygons.Count == 0)
            {
                da.SetDataList(0, new List<Slab>());
                da.SetData(1, 0);
                da.SetData(2, 0.0);
                da.SetDataList(3, new List<Mesh>());
                return;
            }

            var slabs = new List<Slab>(meshes.Count);
            for (int i = 0; i < meshes.Count; i++)
            {
                var s = GhInterop.SlabFromMesh(meshes[i]);
                if (s == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"Mesh[{i}] is invalid (need >= 4 vertices and >= 4 faces).");
                    return;
                }
                slabs.Add(s);
            }

            // ---- Unwrap fracture polygons ---------------------------------
            var polygons = new List<FracturePolygon>(rawPolygons.Count);
            for (int i = 0; i < rawPolygons.Count; i++)
            {
                var raw = rawPolygons[i];
                var fp = UnwrapFracturePolygon(raw);
                if (fp == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"FracturePolygon[{i}] is not a FracturePolygon (got {DescribeType(raw)}).");
                    return;
                }
                polygons.Add(fp);
            }

            // ---- Build options + cut --------------------------------------
            FractureCutOptions options;
            try
            {
                options = new FractureCutOptions(
                    extendPartialToInfinitePlane: extend,
                    epsilon: eps);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"FractureCutOptions construction failed: {ex.Message}");
                return;
            }

            IReadOnlyList<Slab> result;
            try
            {
                result = FractureCutter.CutMany(slabs, polygons, options);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"FractureCutter.CutMany failed: {ex.Message}");
                return;
            }

            // ---- Outputs --------------------------------------------------
            var outSlabs = new List<Slab>(result.Count);
            double totalVolume = 0.0;
            for (int i = 0; i < result.Count; i++)
            {
                outSlabs.Add(result[i]);
                totalVolume += result[i].SignedVolume();
            }

            da.SetDataList(0, outSlabs);
            da.SetData(1, result.Count);
            da.SetData(2, totalVolume);
            da.SetDataList(3, GhInterop.SlabsToMeshes(outSlabs));
        }

        // ─── Helpers ────────────────────────────────────────────────────────

        private static Slab UnwrapSlab(object raw)
        {
            if (raw is Slab direct) return direct;
            if (raw is GH_ObjectWrapper wrap && wrap.Value is Slab fromWrap)
                return fromWrap;
            return null;
        }

        private static FracturePolygon UnwrapFracturePolygon(object raw)
        {
            if (raw is FracturePolygon direct) return direct;
            if (raw is GH_ObjectWrapper wrap && wrap.Value is FracturePolygon fromWrap)
                return fromWrap;
            return null;
        }

        private static string DescribeType(object raw)
        {
            if (raw == null) return "null";
            if (raw is GH_ObjectWrapper wrap)
            {
                var inner = wrap.Value;
                return inner == null
                    ? "GH_ObjectWrapper(null)"
                    : $"GH_ObjectWrapper({inner.GetType().FullName})";
            }
            return raw.GetType().FullName;
        }
    }
}
