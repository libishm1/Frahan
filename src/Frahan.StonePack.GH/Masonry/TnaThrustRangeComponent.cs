#nullable disable
using System;
using System.Collections.Generic;
using Frahan.GH;
using Frahan.Masonry.Tna;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.StonePack.GH.Masonry
{
    // =========================================================================
    // TNA Thrust Range — min/max horizontal thrust for an arch within its section
    // (Heyman lower-bound limit analysis). Beyond a single GSF, this returns the
    // admissible thrust RANGE [Hmin, Hmax] and the two extreme thrust lines that
    // touch the section faces (the active/passive safety states). Exact for a
    // single arch (no solver); run it per masonry course.
    // =========================================================================
    public sealed class TnaThrustRangeComponent : FrahanComponentBase
    {
        public TnaThrustRangeComponent()
            : base("TNA Thrust Range", "ThrustRange",
                "Min/max horizontal thrust for a masonry arch within its section (Heyman safe-theorem " +
                "limit analysis). Samples the arch axis, applies vertical loads (self-weight by tributary " +
                "length if none given), and returns the admissible thrust interval [Hmin, Hmax], the range " +
                "factor, and the two extreme thrust lines that touch the intrados/extrados. Exact for one " +
                "arch; feed a masonry course centerline.",
                "Frahan", "Vault")
        {
        }

        public override Guid ComponentGuid => new Guid("B7A11500-0007-4A11-B500-0000000000A7");
        protected override System.Drawing.Bitmap Icon => Frahan.GH.IconProvider.Load("TnaThrustRange.png");
        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddCurveParameter("Arch", "A", "Arch axis curve (in a vertical plane).", GH_ParamAccess.item);
            p.AddIntegerParameter("Segments", "N", "Number of segments to sample the arch into.", GH_ParamAccess.item, 24);
            p.AddNumberParameter("Thickness", "T", "Section depth (perpendicular to the axis), m.", GH_ParamAccess.item, 0.35);
            p.AddNumberParameter("Loads", "W", "Vertical load per node (optional). Empty = self-weight by tributary length x Density.", GH_ParamAccess.list);
            p.AddNumberParameter("Density", "D", "Weight per unit length for the self-weight fallback.", GH_ParamAccess.item, 1.0);
            p.AddBooleanParameter("Run", "R", "Execute.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBooleanParameter("Feasible", "F", "True if an admissible thrust line fits the section.", GH_ParamAccess.item);
            p.AddNumberParameter("H min", "Hl", "Minimum admissible horizontal thrust.", GH_ParamAccess.item);
            p.AddNumberParameter("H max", "Hh", "Maximum admissible horizontal thrust.", GH_ParamAccess.item);
            p.AddNumberParameter("Range Factor", "Rf", "Hmax / Hmin (thrust safety margin in-section).", GH_ParamAccess.item);
            p.AddCurveParameter("Thrust Min", "Tl", "Deepest admissible thrust line (touches intrados).", GH_ParamAccess.item);
            p.AddCurveParameter("Thrust Max", "Th", "Flattest admissible thrust line (touches extrados).", GH_ParamAccess.item);
            p.AddTextParameter("Report", "Rp", "Summary.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            Curve arch = null;
            int segs = 24; double thick = 0.35, density = 1.0; bool run = false;
            var loadsIn = new List<double>();

            if (!GhGuard.Item(this, da, 0, ref arch, "Arch")) return;
            da.GetData(1, ref segs); da.GetData(2, ref thick);
            da.GetDataList(3, loadsIn); da.GetData(4, ref density); da.GetData(5, ref run);

            if (!run) { da.SetData(6, "Run = false. Toggle to analyse."); return; }
            if (segs < 2) segs = 2;

            arch.DivideByCount(segs, true, out Point3d[] pts);
            if (pts == null || pts.Length < 3) { da.SetData(6, "Could not sample the arch."); return; }
            var cl = new List<Point3d>(pts);
            int n = cl.Count;

            // loads: provided per node, else self-weight by tributary length
            var loads = new List<double>(n);
            if (loadsIn.Count == n) loads.AddRange(loadsIn);
            else
            {
                for (int i = 0; i < n; i++)
                {
                    double trib = 0.0;
                    if (i > 0) trib += cl[i].DistanceTo(cl[i - 1]) * 0.5;
                    if (i < n - 1) trib += cl[i].DistanceTo(cl[i + 1]) * 0.5;
                    loads.Add(trib * density);
                }
            }

            var r = TnaThrustRange.ForArch(cl, thick, loads);
            da.SetData(0, r.Feasible);
            if (r.Feasible)
            {
                da.SetData(1, r.Hmin); da.SetData(2, r.Hmax); da.SetData(3, r.RangeFactor);
                if (r.ThrustLineMin != null) da.SetData(4, new PolylineCurve(r.ThrustLineMin));
                if (r.ThrustLineMax != null) da.SetData(5, new PolylineCurve(r.ThrustLineMax));
            }
            da.SetData(6, r.Message);
            Message = r.Feasible ? $"Hx {r.RangeFactor:F2}" : "infeasible";
        }
    }
}
