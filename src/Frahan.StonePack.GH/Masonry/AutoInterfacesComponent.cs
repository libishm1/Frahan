#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Interfaces;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // AutoInterfacesComponent — given a list of Slabs in their final placed
    // positions plus the IDs the user will assign to the resulting blocks,
    // detect face-face contacts and emit MasonryInterfaces. Closes P3 from
    // HANDOFF_TO_CLAUDE.md.
    //
    // ComponentGuid: CADBECFD-AEBF-4012-3456-789012345678
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Auto Interfaces.
    /// </summary>
    [Algorithm("Interface auto-detector", "Frahan-original proximity-based pairwise contact detection")]
    [Algorithm("Power-of-10 hardening", "Holzmann NASA/JPL 2006 ten coding rules", Note = "Loop bounds + entry assertions")]
        [DesignApplication(
        "Detects face-face contacts between a list of placed Slabs  and emits the corresponding MasonryInterfaces",
        DesignFlow.BottomUp,
        Precedent = "Frahan-original face-face contact detector; Holzmann 2006 Power-of-10 hardening")]
    public sealed class AutoInterfacesComponent : GH_Component
    {
        public AutoInterfacesComponent()
            : base(
                "Auto Interfaces", "AutoIf",
                "Detects face-face contacts between a list of placed Slabs " +
                "and emits the corresponding MasonryInterfaces. Wire output " +
                "into Masonry Assembly's Interfaces input.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("CADBECFD-AEBF-4012-3456-789012345678");

        protected override Bitmap Icon => IconProvider.Load("ContactDetector.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Meshes", "M",
                "Block meshes in their final placed positions. Standard Rhino " +
                "mesh wires; this component finds face-face contacts between them.",
                GH_ParamAccess.list);
            p.AddTextParameter("Block Ids", "Ids",
                "One ID per slab in the same order. Must match the IDs used " +
                "to construct MasonryBlocks.",
                GH_ParamAccess.list);
            p.AddNumberParameter("Distance Tolerance", "Dtol",
                "Max distance between coplanar faces (>= 0).",
                GH_ParamAccess.item, 1e-4);
            p.AddNumberParameter("Angle Tolerance Deg", "Atol",
                "Max angle between antiparallel face normals, in degrees (>= 0, < 90).",
                GH_ParamAccess.item, 1.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Interfaces", "I",
                "Detected MasonryInterfaces. Wire into Masonry Assembly.",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var meshes = new List<Mesh>();
            if (!da.GetDataList(0, meshes))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No meshes provided.");
                return;
            }
            var slabs = new List<Slab>(meshes.Count);
            for (int i = 0; i < meshes.Count; i++)
            {
                var s = GhInterop.SlabFromMesh(meshes[i]);
                if (s == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"Meshes[{i}] is invalid (need >= 4 vertices and >= 4 faces).");
                    return;
                }
                slabs.Add(s);
            }

            var ids = new List<string>();
            if (!da.GetDataList(1, ids))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No block ids provided.");
                return;
            }
            if (ids.Count != slabs.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Block Ids count ({ids.Count}) must equal Slabs count ({slabs.Count}).");
                return;
            }

            double dTol = 1e-4, aTol = 1.0;
            da.GetData(2, ref dTol); da.GetData(3, ref aTol);

            IReadOnlyList<MasonryInterface> result;
            try
            {
                result = InterfaceAutoDetector.Detect(slabs, ids, dTol, aTol);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Auto-interface detection failed: {ex.Message}");
                return;
            }

            if (result.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "No interfaces detected. Check that slabs share coplanar faces.");
            }
            da.SetDataList(0, result);
        }

        private static Slab UnwrapSlab(object raw)
        {
            if (raw is Slab direct) return direct;
            if (raw is GH_ObjectWrapper wrap && wrap.Value is Slab fromWrap) return fromWrap;
            return null;
        }
    }
}
