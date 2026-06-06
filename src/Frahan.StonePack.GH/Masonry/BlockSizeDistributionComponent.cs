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
    // BlockSizeDistributionComponent — diagnostic stats over a list of
    // slab volumes. CV (coefficient of variation) is the most useful
    // single number; > 1 signals long-tail / pathological joint set.
    //
    // ComponentGuid: EF012345-6789-ABCD-EF01-23456789ABCD
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Block Size Distribution.
    /// Histogram + percentiles + Tukey-fence outliers over slab volumes.
    /// </summary>
        [Algorithm("Descriptive statistics + Tukey-fence outlier rule",
        "Tukey 1977, Exploratory Data Analysis, Addison-Wesley",
        WikiPath = "wiki/index/references.md#Tukey1977EDA",
        Note = "Descriptive stats (Frahan); 1.5·IQR outlier fence per Tukey 1977")]
        [DesignApplication(
        "Diagnostic stats over a list of slab volumes",
        DesignFlow.BottomUp,
        Precedent = "Frahan-original size-distribution diagnostic")]
    public sealed class BlockSizeDistributionComponent : GH_Component
    {
        public BlockSizeDistributionComponent()
            : base(
                "Block Size Distribution", "BlkSize",
                "Diagnostic stats over a list of slab volumes. Use to QA " +
                "quarry decomposition: high CV (> 1) signals the joint-set " +
                "parameters need retuning. Outlier fence per Tukey 1977 (EDA).",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("EF012345-6789-ABCD-EF01-23456789ABCD");

        protected override Bitmap Icon => IconProvider.Load("YieldEstimator.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Slabs", "S",
                "Slab DTOs.",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Bins", "B",
                "Histogram bin count. 0 = ceil(sqrt(N)). Default 0.",
                GH_ParamAccess.item, 0);
            p[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddIntegerParameter("Count", "N", "Total piece count.", GH_ParamAccess.item);
            p.AddNumberParameter("Total Volume", "V", "Sum of all volumes.", GH_ParamAccess.item);
            p.AddNumberParameter("Min", "Min", "", GH_ParamAccess.item);
            p.AddNumberParameter("Max", "Max", "", GH_ParamAccess.item);
            p.AddNumberParameter("Mean", "Mean", "", GH_ParamAccess.item);
            p.AddNumberParameter("Median", "P50", "", GH_ParamAccess.item);
            p.AddNumberParameter("StdDev", "SD", "", GH_ParamAccess.item);
            p.AddNumberParameter("CV", "CV", "Coefficient of variation (StdDev/Mean).", GH_ParamAccess.item);
            p.AddNumberParameter("Percentiles", "P",
                "[P10, P25, P50, P75, P90].", GH_ParamAccess.list);
            p.AddIntegerParameter("Outlier Indices", "Out",
                "Indices outside the Tukey fence (Q1−1.5·IQR, Q3+1.5·IQR).",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Bin Counts", "Hist",
                "Histogram counts (length = Bins).", GH_ParamAccess.list);
            p.AddNumberParameter("Bin Width", "Bw",
                "Width of each histogram bin.", GH_ParamAccess.item);
            p.AddTextParameter("Report", "R",
                "One-line summary.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var raw = new List<object>();
            int bins = 0;
            if (!da.GetDataList(0, raw) || raw.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No slabs.");
                return;
            }
            da.GetData(1, ref bins);

            var slabs = new List<Slab>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                var s = Unwrap(raw[i]);
                if (s != null) slabs.Add(s);
            }
            if (slabs.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Inputs did not contain any Slab DTOs.");
                return;
            }
            var rep = BlockSizeDistribution.Analyse(slabs, bins);
            da.SetData(0, rep.Count);
            da.SetData(1, rep.TotalVolume);
            da.SetData(2, rep.Min);
            da.SetData(3, rep.Max);
            da.SetData(4, rep.Mean);
            da.SetData(5, rep.Median);
            da.SetData(6, rep.StdDev);
            da.SetData(7, rep.CoefficientOfVariation);
            da.SetDataList(8, new[] { rep.P10, rep.P25, rep.P50, rep.P75, rep.P90 });
            da.SetDataList(9, rep.OutlierIndices);
            da.SetDataList(10, rep.BinCounts);
            da.SetData(11, rep.BinWidth);
            da.SetData(12, rep.ToString());
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
