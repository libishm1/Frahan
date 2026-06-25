#nullable disable
using System;
using System.Drawing;
using Frahan.Masonry.Packing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // PackDiagnosticsComponent — decomposes an AshlarPackResult into the
    // diagnostic outputs (coverage, course count, leftovers, notes, placed
    // blocks). Pure inspection; no side effects.
    //
    // ComponentGuid: C4D5E6F7-A8B9-4ABC-DEF0-123456789012
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Pack Diagnostics.
    /// Splits an AshlarPackResult into individual diagnostic outputs.
    /// </summary>
        [DesignApplication(
        "Splits an AshlarPackResult into Coverage / Course Count /  Leftovers / Notes / Placed Blocks for inspection",
        DesignFlow.Bridges,
        Precedent = "Frahan-original diagnostic / report")]
    public sealed class PackDiagnosticsComponent : FrahanComponentBase
    {
        public PackDiagnosticsComponent()
            : base(
                "Pack Diagnostics", "PackDiag",
                "Splits an AshlarPackResult into Coverage / Course Count / " +
                "Leftovers / Notes / Placed Blocks for inspection.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("C4D5E6F7-A8B9-4ABC-DEF0-123456789012");

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override Bitmap Icon => IconProvider.Load("PackDiagnostics.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Result", "R",
                "AshlarPackResult from Ashlar Pack.",
                GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddNumberParameter("Coverage", "Cov",
                "Wall area filled by placed blocks divided by total wall area, in [0, 1].",
                GH_ParamAccess.item);
            p.AddIntegerParameter("Course Count", "N",
                "Number of courses laid.",
                GH_ParamAccess.item);
            p.AddGenericParameter("Leftovers", "L",
                "Slabs from the input inventory that were not placed.",
                GH_ParamAccess.list);
            p.AddTextParameter("Notes", "Notes",
                "Diagnostic messages emitted by the layout engine.",
                GH_ParamAccess.list);
            p.AddGenericParameter("Placed Blocks", "B",
                "MasonryBlocks in the order they were laid.",
                GH_ParamAccess.list);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            object raw = null;
            if (!da.GetData(0, ref raw))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No result provided.");
                return;
            }
            AshlarPackResult result = UnwrapResult(raw);
            if (result == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Result is not an AshlarPackResult (got {DescribeType(raw)}).");
                return;
            }

            da.SetData(0, result.CoverageRatio);
            da.SetData(1, result.CourseCount);
            da.SetDataList(2, result.Leftovers);
            da.SetDataList(3, result.Notes);
            da.SetDataList(4, result.PlacedBlocks);
        }

        private static AshlarPackResult UnwrapResult(object raw)
        {
            if (raw is AshlarPackResult direct) return direct;
            if (raw is GH_ObjectWrapper wrap && wrap.Value is AshlarPackResult fromWrap)
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
