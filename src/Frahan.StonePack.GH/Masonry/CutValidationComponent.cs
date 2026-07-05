#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Geometry;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // CutValidationComponent — post-cut hygiene check. Volume conservation
    // (sum of post-pieces should match the pre-slab to within tolerance)
    // plus sliver enumeration.
    //
    // ComponentGuid: DEF01234-5678-9ABC-DEF0-123456789ABC
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Cut Validation.
    /// Validates volume conservation between pre-cut and post-cut slabs;
    /// flags slivers and dropouts.
    /// </summary>
        [DesignApplication(
        "Validates a cut: sum(post-piece volumes) ≈ sum(pre-slab  volumes) within tolerance",
        DesignFlow.TopDown,
        Precedent = "Frahan-original cut-plan validator")]
    public sealed class CutValidationComponent : FrahanComponentBase
    {
        public CutValidationComponent()
            : base(
                "Cut Validation", "CutVal",
                "Validates a cut: sum(post-piece volumes) ≈ sum(pre-slab " +
                "volumes) within tolerance. Flags slivers and dropouts.",
                "Frahan", "Masonry")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.senary;

        public override Guid ComponentGuid =>
            new Guid("DEF01234-5678-9ABC-DEF0-123456789ABC");

        protected override Bitmap Icon => IconProvider.Load("StereotomyJoint.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Pre Slabs", "Pre",
                "Slab DTOs before the cut.",
                GH_ParamAccess.list);
            p.AddGenericParameter("Post Slabs", "Post",
                "Slab DTOs after the cut.",
                GH_ParamAccess.list);
            p.AddNumberParameter("Relative Tolerance", "Tr",
                "Acceptable relative volume mismatch. Default 1e-6.",
                GH_ParamAccess.item, 1e-6);
            p[2].Optional = true;
            p.AddNumberParameter("Sliver Fraction", "Ts",
                "Pieces whose volume is below this fraction of the total " +
                "are flagged as slivers. Default 1e-4.",
                GH_ParamAccess.item, 1e-4);
            p[3].Optional = true;
            p.AddNumberParameter("Dropout Volume", "Td",
                "Pieces below this absolute volume are flagged as " +
                "dropouts (likely lost geometry). Default 1e-12.",
                GH_ParamAccess.item, 1e-12);
            p[4].Optional = true;
            p.AddBooleanParameter("Drop Slivers", "DropS",
                "If true, also output a sliver-free subset of post slabs. " +
                "Default false.",
                GH_ParamAccess.item, false);
            p[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBooleanParameter("Conserved", "OK",
                "True iff |postVol − preVol| / preVol ≤ Relative Tolerance.",
                GH_ParamAccess.item);
            p.AddNumberParameter("Pre Volume", "Vpre",
                "Sum |signed volume| of pre slabs.",
                GH_ParamAccess.item);
            p.AddNumberParameter("Post Volume", "Vpost",
                "Sum |signed volume| of post slabs.",
                GH_ParamAccess.item);
            p.AddNumberParameter("Absolute Error", "AbsErr",
                "|Vpre − Vpost|.",
                GH_ParamAccess.item);
            p.AddNumberParameter("Relative Error", "RelErr",
                "AbsErr / Vpre.",
                GH_ParamAccess.item);
            p.AddIntegerParameter("Sliver Indices", "Sli",
                "0-based indices of post slabs flagged as slivers.",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Dropout Indices", "Dro",
                "0-based indices of post slabs with effectively zero volume.",
                GH_ParamAccess.list);
            p.AddGenericParameter("Cleaned Slabs", "Clean",
                "Sliver-free subset of post slabs. Empty when Drop " +
                "Slivers is false.",
                GH_ParamAccess.list);
            p.AddTextParameter("Report", "R",
                "One-line summary.",
                GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var preRaw = new List<object>();
            var postRaw = new List<object>();
            if (!da.GetDataList(0, preRaw) || preRaw.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No pre slabs.");
                return;
            }
            if (!da.GetDataList(1, postRaw) || postRaw.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No post slabs.");
                return;
            }
            double relTol = 1e-6, sliverFrac = 1e-4, dropoutVol = 1e-12;
            bool drop = false;
            da.GetData(2, ref relTol);
            da.GetData(3, ref sliverFrac);
            da.GetData(4, ref dropoutVol);
            da.GetData(5, ref drop);

            var pre = UnwrapAll(preRaw);
            var post = UnwrapAll(postRaw);
            if (pre.Count == 0 || post.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Inputs did not contain any Slab DTOs.");
                return;
            }

            var rep = CutResultValidator.Validate(pre, post,
                relTol, sliverFrac, dropoutVol);

            // Re-enumerate sliver / dropout indices for the output lists.
            double total = 0;
            var vols = new double[post.Count];
            for (int i = 0; i < post.Count; i++)
            {
                vols[i] = Math.Abs(post[i].SignedVolume());
                total += vols[i];
            }
            double sliverCutoff = sliverFrac * total;
            var sliverIdx = new List<int>();
            var dropoutIdx = new List<int>();
            for (int i = 0; i < post.Count; i++)
            {
                if (vols[i] <= dropoutVol) dropoutIdx.Add(i);
                else if (vols[i] < sliverCutoff) sliverIdx.Add(i);
            }

            var cleaned = drop
                ? CutResultValidator.DropSlivers(post, sliverFrac)
                : new List<Slab>(0);

            da.SetData(0, rep.Conserved);
            da.SetData(1, rep.PreVolume);
            da.SetData(2, rep.PostVolumeSum);
            da.SetData(3, rep.AbsoluteError);
            da.SetData(4, rep.RelativeError);
            da.SetDataList(5, sliverIdx);
            da.SetDataList(6, dropoutIdx);
            da.SetDataList(7, cleaned);
            da.SetData(8, rep.ToString());
        }

        private static List<Slab> UnwrapAll(List<object> raw)
        {
            var list = new List<Slab>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                var s = Unwrap(raw[i]);
                if (s != null) list.Add(s);
            }
            return list;
        }

        private static Slab Unwrap(object raw)
        {
            if (raw is Slab direct) return direct;
            if (raw is GH_ObjectWrapper wrap && wrap.Value is Slab fromWrap)
                return fromWrap;
            return null;
        }
    }
}
