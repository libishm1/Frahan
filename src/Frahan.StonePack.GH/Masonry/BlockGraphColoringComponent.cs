#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Interfaces;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // BlockGraphColoringComponent — 4-colour the contact graph of a
    // MasonryAssembly. Reference: okaydemir/4-color-theorem.
    //
    // ComponentGuid: F2D000B0-CADC-4F2D-A0B0-7E60CADA15A0
    // (was DEF01234-5678-9ABC-DEF0-123456789ABC; collided with CutValidationComponent)
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Block Graph Coloring.
    /// Assigns one of 4 colours per block so no two adjacent blocks share
    /// a colour. Useful for visualization, material distribution, or
    /// robotic-assembly parallelisation.
    /// </summary>
    [Algorithm("Welsh-Powell graph coloring", "Welsh and Powell 1967, Computer Journal 10(1):85-86", Note = "Degree-sorted greedy four-color approximation")]
        [DesignApplication(
        "4-colours the contact graph of a MasonryAssembly: no two  blocks sharing an interface get the same colour",
        DesignFlow.BottomUp,
        Precedent = "Welsh Powell 1967 graph chromatic-number bound (Computer Journal 10(1):85-86); 4-colour theorem reference impl")]
    public sealed class BlockGraphColoringComponent : GH_Component
    {
        public BlockGraphColoringComponent()
            : base(
                "Block Graph Coloring", "BlockColor",
                "4-colours the contact graph of a MasonryAssembly: no two " +
                "blocks sharing an interface get the same colour. Output " +
                "is one integer per block (0-3 typically; up to 7 for " +
                "non-planar topologies). Wire into native colour-mapping " +
                "to drive visualization or material assignment. [Welsh & Powell 1967]",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("F2D000B0-CADC-4F2D-A0B0-7E60CADA15A0");

        protected override Bitmap Icon => IconProvider.Load("BeamConfig.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Assembly", "A",
                "MasonryAssembly with blocks + interfaces.",
                GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Block Ids", "Id",
                "Block identifiers, in iteration order.",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Colour", "C",
                "Per-block colour index (0-based). Same length and order as " +
                "the Block Ids output.",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Colours Used", "N",
                "Total number of distinct colours used. Should be <= 4 for " +
                "planar contact graphs (4-Colour Theorem).",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
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

            IReadOnlyDictionary<string, int> coloring;
            try
            {
                coloring = BlockGraphColorer.Color(assembly);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Coloring failed: {ex.Message}");
                return;
            }

            var ids = new List<string>(assembly.Blocks.Count);
            var cols = new List<int>(assembly.Blocks.Count);
            int maxCol = -1;
            for (int i = 0; i < assembly.Blocks.Count; i++)
            {
                string id = assembly.Blocks[i].Id;
                int c = coloring.TryGetValue(id, out int v) ? v : 0;
                ids.Add(id);
                cols.Add(c);
                if (c > maxCol) maxCol = c;
            }
            int coloursUsed = maxCol + 1;
            if (coloursUsed > 4)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Used {coloursUsed} colours — contact graph isn't planar; " +
                    $"4-Colour Theorem applies only to planar graphs.");
            }

            da.SetDataList(0, ids);
            da.SetDataList(1, cols);
            da.SetData(2, coloursUsed);
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
