#nullable disable
using System;
using System.Collections.Generic;
using Frahan.GH;
using Frahan.Masonry.Vault;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.StonePack.GH.Masonry
{
    // =========================================================================
    // Vault Interlock Check — measure how interlocked a tessellation is by finding
    // the longest CONTINUOUS JOINT (a near-straight connected run of joint edges =
    // a potential sliding plane), reported as a fraction of the span. Low = well
    // interlocked (random rubble bond, or staggered courses); high = a straight
    // joint runs across many units (sliding risk). Works on the quad-course mesh
    // and on a meshed Voronoi rubble tessellation alike.
    // =========================================================================
    public sealed class VaultInterlockComponent : FrahanComponentBase
    {
        public VaultInterlockComponent()
            : base("Vault Interlock Check", "Interlock",
                "Measure how interlocked a masonry tessellation is. Traces the joint network (the mesh's " +
                "topology edges) for the longest near-straight connected run -- a continuous joint is a " +
                "potential sliding plane. Reports the longest run and its fraction of the span: low = " +
                "interlocked (random rubble bond / staggered courses), high = sliding-plane risk. Verifies " +
                "the free stagger of a Voronoi rubble vault AND the imposed running-bond stagger of quad courses.",
                "Frahan", "Vault")
        {
        }

        public override Guid ComponentGuid => new Guid("B7A11500-0009-4A11-B500-0000000000A9");
        protected override System.Drawing.Bitmap Icon => Frahan.GH.IconProvider.Load("VaultInterlock.png");
        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Tessellation", "M", "The tessellation mesh (quad-course mesh, or a meshed/joined Voronoi rubble tessellation). Its topology edges are the joints.", GH_ParamAccess.item);
            p.AddNumberParameter("Angle Tol", "A", "Max turn (deg) between consecutive joint edges to count as one straight run.", GH_ParamAccess.item, 15.0);
            p.AddNumberParameter("Flag Fraction", "Ff", "Collect every joint run longer than this fraction of the span.", GH_ParamAccess.item, 0.5);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddCurveParameter("Longest Joint", "J", "The longest continuous joint run (the worst sliding-plane candidate).", GH_ParamAccess.item);
            p.AddCurveParameter("Flagged Runs", "Fr", "All joint runs over the flag fraction.", GH_ParamAccess.list);
            p.AddNumberParameter("Longest Run", "L", "Length of the longest joint run (m).", GH_ParamAccess.item);
            p.AddNumberParameter("Span Fraction", "%", "Longest run / bounding-box diagonal (0 = interlocked, ~1 = full-span sliding plane).", GH_ParamAccess.item);
            p.AddTextParameter("Verdict", "V", "interlocked / moderate / sliding-plane risk.", GH_ParamAccess.item);
            p.AddTextParameter("Report", "Rp", "Summary.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            Mesh tess = null;
            double angleTol = 15.0, flagFrac = 0.5;
            if (!GhGuard.Item(this, da, 0, ref tess, "Tessellation")) return;
            da.GetData(1, ref angleTol); da.GetData(2, ref flagFrac);

            var res = VaultInterlock.Analyze(tess, angleTol, flagFrac);

            da.SetData(0, res.LongestJoint != null && res.LongestJoint.Count > 1 ? new PolylineCurve(res.LongestJoint) : null);
            var flagged = new List<Curve>();
            foreach (var pl in res.FlaggedRuns) if (pl.Count > 1) flagged.Add(new PolylineCurve(pl));
            da.SetDataList(1, flagged);
            da.SetData(2, res.LongestRun);
            da.SetData(3, res.SpanFraction);
            da.SetData(4, res.Verdict);
            da.SetData(5, $"{res.Verdict}: longest continuous joint {res.LongestRun:F2}m = {res.SpanFraction * 100.0:F0}% of span; " +
                          $"{res.FlaggedRuns.Count} run(s) over {flagFrac * 100.0:F0}%; {res.JointEdges} joint edges.");
            Message = $"{res.SpanFraction * 100.0:F0}% {res.Verdict}";
        }
    }
}
