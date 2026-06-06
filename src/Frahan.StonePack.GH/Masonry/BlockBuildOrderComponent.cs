#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Geometry;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // BlockBuildOrderComponent — produce a physically valid place-this-then-
    // that order for a MasonryAssembly. The build order respects the support
    // graph: a block is only placed once everything it sits on is already
    // placed. Layered output (course number) lets the user filter / colour
    // by course.
    //
    // ComponentGuid: 3456789A-BCDE-F012-3456-789ABCDEF012
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Block Build Order.
    /// Topological sort over the contact-support DAG, prioritising lowest-
    /// elevation ready blocks.
    /// </summary>
    [Algorithm("Support-DAG topological install order", "Kim et al. 2024, ASME IDETC/CIE DETC2024-142563 Polygonal masonry install-order DAG", Note = "Generalised to 3D contact-support DAG with course-by-course (Kahn) traversal", WikiPath = "wiki/algorithms/polygonal_masonry/kim_2024_install_order.md")]
        [DesignApplication(
        "Computes a physically valid build order for a masonry  assembly",
        DesignFlow.TopDown,
        Precedent = "Kim 2024 polygonal masonry install order (DETC2024-142563)",
        CardSet = "wiki/research/hitl_cards/td_voussoir/")]
    public sealed class BlockBuildOrderComponent : GH_Component
    {
        public BlockBuildOrderComponent()
            : base(
                "Block Build Order", "BuildOrd",
                "Computes a physically valid build order for a masonry " +
                "assembly. A block is placed only after every block it " +
                "rests on is already placed. Layer = course number " +
                "(longest support path from ground).",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("3456789A-BCDE-F012-3456-789ABCDEF012");

        protected override Bitmap Icon => IconProvider.Load("AssemblySolver.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Assembly", "A",
                "MasonryAssembly DTO.",
                GH_ParamAccess.item);
            p.AddVectorParameter("Up Vector", "Up",
                "Direction the courses stack along. Default world Z.",
                GH_ParamAccess.item, Vector3d.ZAxis);
            p[1].Optional = true;
            p.AddNumberParameter("Up Tolerance Deg", "Tol",
                "An interface counts as a bed joint when its normal is " +
                "within this many degrees of the up axis. Head joints / " +
                "vertical contacts beyond this tolerance contribute no " +
                "support constraint. Default 30°.",
                GH_ParamAccess.item, 30.0);
            p[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Ordered Block Ids", "Id",
                "Block ids in build order (lowest course first).",
                GH_ParamAccess.list);
            p.AddMeshParameter("Ordered Meshes", "M",
                "Block meshes in build order, parallel to Ordered Block Ids.",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Order Index", "i",
                "0-based placement index per block, parallel to Ordered " +
                "Block Ids. Equals the list index — exposed for downstream " +
                "components that rebuild the order.",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Layer", "L",
                "Course number (longest support path). 0 = ground course. " +
                "Useful for colour-by-course visualisation.",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            object raw = null;
            if (!da.GetData(0, ref raw))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No assembly provided.");
                return;
            }
            var assembly = UnwrapAssembly(raw);
            if (assembly == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Assembly is not a MasonryAssembly (got {GhInterop.DescribeType(raw)}).");
                return;
            }

            var up = Vector3d.ZAxis;
            da.GetData(1, ref up);

            double tolDeg = 30.0;
            da.GetData(2, ref tolDeg);
            if (tolDeg < 0.0 || tolDeg >= 90.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Up Tolerance Deg must be in [0, 90), got {tolDeg}.");
                return;
            }
            double upToleranceCos = Math.Cos(tolDeg * Math.PI / 180.0);

            IReadOnlyList<BuildStep> steps;
            try
            {
                steps = BlockBuildOrderer.Solve(
                    assembly, up.X, up.Y, up.Z, upToleranceCos);
            }
            catch (InvalidOperationException ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }
            catch (ArgumentException ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            var ids = new List<string>(steps.Count);
            var meshes = new List<Mesh>(steps.Count);
            var orderIdx = new List<int>(steps.Count);
            var layers = new List<int>(steps.Count);

            for (int i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                ids.Add(s.BlockId);
                meshes.Add(GhInterop.BlockToMesh(assembly.GetBlock(s.BlockId)));
                orderIdx.Add(s.OrderIndex);
                layers.Add(s.Layer);
            }

            da.SetDataList(0, ids);
            da.SetDataList(1, meshes);
            da.SetDataList(2, orderIdx);
            da.SetDataList(3, layers);
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
