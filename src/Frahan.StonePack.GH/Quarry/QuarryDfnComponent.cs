#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Quarry;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // QuarryDfnComponent — extracts dimension-stone blocks from a quarry mesh
    // following a Discrete Fracture Network defined by joint sets. The
    // physically-meaningful replacement for the simpler Quarry Decompose
    // (which only does axis-aligned grid cuts).
    //
    // Algorithm: ISRM / Priest 1993 — generate planes per joint set with
    // mean spacing along each set's normal, then split the input mesh by
    // those planes via SlabCutter.
    //
    // ComponentGuid: FDAEBFCA-DCED-4456-789A-CDEF01234567
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Quarry &gt; Quarry DFN.
    /// Block-extracts a quarry mesh by a joint-set DFN.
    /// </summary>
    [Algorithm("Discrete Fracture Network block extraction", "ISRM Suggested Methods + Priest 1993 joint-set DFN", WikiPath = "wiki/index/references.md")]
    [Algorithm("Fisher-distribution orientation scatter", "Azarafza et al. 2016 granite block-cut + Fisher-distribution joint scatter", WikiPath = "wiki/index/references.md")]
        [DesignApplication(
        "Extracts dimension-stone blocks from a quarry mesh following  a Discrete Fracture Network defined by joint ...",
        DesignFlow.BottomUp,
        Precedent = "Discrete Fracture Network synthesis; ISRM Suggested Methods + Priest 1993")]
    public sealed class QuarryDfnComponent : GH_Component
    {
        public QuarryDfnComponent()
            : base(
                "Quarry DFN", "QuarryDFN",
                "Extracts dimension-stone blocks from a quarry mesh following " +
                "a Discrete Fracture Network defined by joint sets. " +
                "Geomechanically faithful (Priest 1993 / ISRM Suggested Methods). Implements DFN block extraction (ISRM/Priest 1993; Azarafza 2016).",
                "Frahan", "Quarry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("FDAEBFCA-DCED-4456-789A-CDEF01234567");

        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override Bitmap Icon => IconProvider.Load("DefectMap.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Quarry", "Q",
                "Convex quarry mesh (e.g. a cube of rock).",
                GH_ParamAccess.item);
            p.AddGenericParameter("Joint Sets", "J",
                "JointSet DTOs (from Joint Set component).",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Seed", "Seed",
                "Random seed (controls spacing offset within meanSpacing).",
                GH_ParamAccess.item, 12345);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Blocks", "B",
                "Extracted block meshes.",
                GH_ParamAccess.list);
            p.AddGenericParameter("Slabs", "S",
                "Same blocks as Slab DTOs (for downstream Frahan plumbing).",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            Mesh quarryMesh = null;
            var rawJointSets = new List<object>();
            int seed = 12345;
            if (!da.GetData(0, ref quarryMesh) || quarryMesh == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No quarry mesh provided.");
                return;
            }
            if (!da.GetDataList(1, rawJointSets) || rawJointSets.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Need at least one Joint Set.");
                return;
            }
            da.GetData(2, ref seed);

            var quarry = GhInterop.SlabFromMesh(quarryMesh);
            if (quarry == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Quarry mesh must have at least 4 vertices and 4 faces.");
                return;
            }

            var jointSets = new List<JointSet>(rawJointSets.Count);
            for (int i = 0; i < rawJointSets.Count; i++)
            {
                JointSet js = UnwrapJointSet(rawJointSets[i]);
                if (js == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"Joint Sets[{i}] is not a JointSet (got {GhInterop.DescribeType(rawJointSets[i])}).");
                    return;
                }
                jointSets.Add(js);
            }

            try
            {
                var result = JointSetDfnGenerator.DecomposeByJointSets(quarry, jointSets, seed);
                da.SetDataList(0, GhInterop.SlabsToMeshes(result.Slabs));
                da.SetDataList(1, result.Slabs);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Quarry DFN failed: {ex.Message}");
            }
        }

        private static JointSet UnwrapJointSet(object raw)
        {
            if (raw == null) return null;
            if (raw is JointSet direct) return direct;
            if (raw is GH_ObjectWrapper wrap && wrap.Value is JointSet fromWrap)
                return fromWrap;
            return null;
        }
    }
}
