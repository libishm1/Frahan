#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.DataModel;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // MasonryAssemblyComponent — composes a list of MasonryBlocks plus a list
    // of MasonryInterfaces and a (possibly empty) "fixed block ids" list into
    // a Frahan.Masonry.DataModel.MasonryAssembly DTO. Auto-detection of
    // interfaces from raw block geometry is a future task; for now interfaces
    // must be supplied explicitly (or omitted entirely for a free-standing
    // block sanity check).
    //
    // ComponentGuid: E5A9B2C3-3D4E-4F60-AB2C-4D5E6F7A8B9C
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Masonry Assembly.
    /// Composes blocks, interfaces, and boundary conditions into a
    /// <see cref="MasonryAssembly"/>.
    /// </summary>
        [DesignApplication(
        "Composes MasonryBlocks, MasonryInterfaces, and fixed-block boundary  conditions into a MasonryAssembly DTO",
        DesignFlow.BottomUp,
        Precedent = "ETH Gramazio Kohler robotic masonry assembly lineage; Kao 2022 Coupled Rigid-Block Analysis")]
    public sealed class MasonryAssemblyComponent : FrahanComponentBase
    {
        public MasonryAssemblyComponent()
            : base(
                "Masonry Assembly", "MasAsm",
                "Composes MasonryBlocks, MasonryInterfaces, and fixed-block boundary " +
                "conditions into a MasonryAssembly DTO. Interfaces must be supplied " +
                "explicitly; auto-detection is a future task.",
                "Frahan", "Masonry")
        {
        }

        // GUID literal: E5A9B2C3-3D4E-4F60-AB2C-4D5E6F7A8B9C
        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        public override Guid ComponentGuid =>
            new Guid("E5A9B2C3-3D4E-4F60-AB2C-4D5E6F7A8B9C");

        protected override Bitmap Icon => IconProvider.Load("Voussoir.png");

        // ─── Params ─────────────────────────────────────────────────────────

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Blocks", "B",
                "MasonryBlock DTOs from Masonry Block.",
                GH_ParamAccess.list);
            p.AddGenericParameter("Interfaces", "I",
                "MasonryInterface DTOs. May be empty; auto-detection is a future task.",
                GH_ParamAccess.list);
            p[1].Optional = true;
            p.AddTextParameter("Fixed Block Ids", "F",
                "Identifiers of blocks that are grounded (boundary conditions). " +
                "Empty list means all blocks are free.",
                GH_ParamAccess.list);
            p[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Assembly", "A",
                "MasonryAssembly DTO. Wire into Masonry Stability (RBE).",
                GH_ParamAccess.item);
        }

        // ─── Solve ──────────────────────────────────────────────────────────

        protected override void SolveSafe(IGH_DataAccess da)
        {
            // ---- Blocks (required, list) -----------------------------------
            var rawBlocks = new List<object>();
            if (!da.GetDataList(0, rawBlocks))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "No blocks provided.");
                return;
            }
            var blocks = new List<MasonryBlock>(rawBlocks.Count);
            for (int i = 0; i < rawBlocks.Count; i++)
            {
                var raw = rawBlocks[i];
                var b = UnwrapBlock(raw);
                if (b == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"Blocks[{i}] is not a MasonryBlock (got {DescribeType(raw)}).");
                    return;
                }
                blocks.Add(b);
            }
            if (blocks.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "At least one block is required.");
                return;
            }

            // ---- Interfaces (optional, list) -------------------------------
            // GH list-access GetDataList returns false when the input is
            // unwired; in that case we treat the list as empty.
            var rawIfaces = new List<object>();
            da.GetDataList(1, rawIfaces);
            var interfaces = new List<MasonryInterface>(rawIfaces.Count);
            for (int i = 0; i < rawIfaces.Count; i++)
            {
                var raw = rawIfaces[i];
                if (raw == null) continue;
                var iface = UnwrapInterface(raw);
                if (iface == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"Interfaces[{i}] is not a MasonryInterface (got {DescribeType(raw)}).");
                    return;
                }
                interfaces.Add(iface);
            }

            // ---- Fixed block ids (optional, list) --------------------------
            var fixedIds = new List<string>();
            da.GetDataList(2, fixedIds);
            // Filter blanks defensively; BoundaryConditions throws on them.
            var cleanedFixed = new List<string>(fixedIds.Count);
            for (int i = 0; i < fixedIds.Count; i++)
            {
                string s = fixedIds[i];
                if (string.IsNullOrWhiteSpace(s)) continue;
                cleanedFixed.Add(s);
            }

            // ---- Assemble --------------------------------------------------
            BoundaryConditions bc;
            try
            {
                bc = new BoundaryConditions(cleanedFixed);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"BoundaryConditions construction failed: {ex.Message}");
                return;
            }

            MasonryAssembly assembly;
            try
            {
                assembly = new MasonryAssembly(blocks, interfaces, bc);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"MasonryAssembly construction failed: {ex.Message}");
                return;
            }

            da.SetData(0, assembly);
        }

        // ─── Helpers ────────────────────────────────────────────────────────

        private static MasonryBlock UnwrapBlock(object raw)
        {
            if (raw is MasonryBlock direct) return direct;
            if (raw is GH_ObjectWrapper wrap && wrap.Value is MasonryBlock fromWrap)
                return fromWrap;
            return null;
        }

        private static MasonryInterface UnwrapInterface(object raw)
        {
            if (raw is MasonryInterface direct) return direct;
            if (raw is GH_ObjectWrapper wrap && wrap.Value is MasonryInterface fromWrap)
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
