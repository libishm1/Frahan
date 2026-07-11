#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.Kintsugi;

// =============================================================================
// Frahan > Kintsugi > Scramble Fragments (test utility).
//
// Applies a deterministic random SE(3) to every fragment except the anchor,
// so reassembly components (Facet Match, Kintsugi geometric/Port) can be
// exercised on canvas against a known ground truth. The inverse transforms
// are emitted for measuring recovered-pose error.
// =============================================================================

[Algorithm("Deterministic SE(3) scrambler",
    "Per-fragment random rotation (up to Max Angle about a random axis " +
    "through the fragment centroid) + random translation (up to Max Offset " +
    "x union diagonal). Seeded; anchor stays. Test harness for reassembly.")]
[DesignApplication(
    "Scramble fragments deterministically to test reassembly components against ground truth",
    DesignFlow.BottomUp,
    Precedent = "Frahan-original canvas test utility (2026-07-11 Kintsugi validation sessions)")]
public sealed class ScrambleFragmentsComponent : FrahanComponentBase
{
    public ScrambleFragmentsComponent()
        : base("Scramble Fragments", "Scramble",
            "Apply a deterministic random rigid transform to every fragment " +
            "except the anchor. Wire between a fracture generator and a " +
            "reassembly component (Facet Match / Frahan Kintsugi) to test " +
            "reassembly on canvas against known ground truth.",
            "Frahan", "Kintsugi")
    {
    }

    // F2D00509: verified unique 2026-07-11 (series 501-508 taken; grep
    // before assigning).
    public override Guid ComponentGuid =>
        new Guid("F2D00509-2026-4522-B0B0-1ABE15A0CAFE");

    public override GH_Exposure Exposure => GH_Exposure.secondary;
    protected override Bitmap Icon => IconProvider.Load("SyntheticBlock.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Fragments", "F", "Fragments at ground-truth poses.",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Seed", "S", "Deterministic seed. Default 7.",
            GH_ParamAccess.item, 7);
        p.AddNumberParameter("Max Angle", "A",
            "Maximum rotation in DEGREES about a random axis through each " +
            "fragment's centroid. Default 180.",
            GH_ParamAccess.item, 180.0);
        p.AddNumberParameter("Max Offset", "O",
            "Maximum translation per axis as a FRACTION of the union " +
            "bounding-box diagonal. Default 0.3.",
            GH_ParamAccess.item, 0.3);
        p.AddIntegerParameter("Anchor", "An",
            "Index of the fragment left unmoved. Default 0.",
            GH_ParamAccess.item, 0);
        // Appended 2026-07-12 (appending preserves saved canvases).
        p.AddMeshParameter("Regions", "Rg",
            "OPTIONAL fracture-surface TREE (one branch per fragment; wire " +
            "Fracture Roughen's Fracture Surfaces output). Every mesh in " +
            "branch f gets fragment f's transform, so the regions stay glued " +
            "to their scrambled fragments. Wire the Scrambled Regions output " +
            "into Facet Match's Fracture Regions input.",
            GH_ParamAccess.tree);
        Params.Input[Params.Input.Count - 1].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Scrambled", "F", "Scrambled fragments.",
            GH_ParamAccess.list);
        p.AddTransformParameter("Transforms", "X",
            "Applied transform per fragment (identity for the anchor). " +
            "Invert to recover ground truth.", GH_ParamAccess.list);
        // Appended 2026-07-12.
        p.AddMeshParameter("Scrambled Regions", "SRg",
            "The Regions tree with each branch moved by its fragment's " +
            "transform (same paths). Empty when Regions is unwired.",
            GH_ParamAccess.tree);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var fragments = new List<Mesh>();
        int seed = 7, anchorIdx = 0;
        double maxAngle = 180.0, maxOffset = 0.3;
        if (!da.GetDataList(0, fragments)) return;
        da.GetData(1, ref seed);
        da.GetData(2, ref maxAngle);
        da.GetData(3, ref maxOffset);
        da.GetData(4, ref anchorIdx);

        var union = BoundingBox.Empty;
        foreach (var m in fragments) if (m != null) union.Union(m.GetBoundingBox(true));
        double d = union.IsValid ? union.Diagonal.Length : 1.0;

        var rng = new Random(seed);
        var outMeshes = new List<Mesh>(fragments.Count);
        var outXf = new List<Transform>(fragments.Count);
        for (int f = 0; f < fragments.Count; f++)
        {
            var m = fragments[f]?.DuplicateMesh();
            var T = Transform.Identity;
            if (m != null && f != anchorIdx)
            {
                var c = m.GetBoundingBox(true).Center;
                var axis = new Vector3d(rng.NextDouble() - 0.5,
                                        rng.NextDouble() - 0.5,
                                        rng.NextDouble() - 0.5);
                if (axis.Length < 1e-9) axis = Vector3d.ZAxis;
                axis.Unitize();
                double ang = rng.NextDouble() * maxAngle * Math.PI / 180.0;
                var rot = Transform.Rotation(ang, axis, c);
                var tr = new Vector3d(
                    (rng.NextDouble() - 0.5) * 2 * d * maxOffset,
                    (rng.NextDouble() - 0.5) * 2 * d * maxOffset,
                    (rng.NextDouble() - 0.5) * 2 * d * maxOffset);
                T = Transform.Multiply(Transform.Translation(tr), rot);
                m.Transform(T);
            }
            outMeshes.Add(m);
            outXf.Add(T);
        }
        da.SetDataList(0, outMeshes);
        da.SetDataList(1, outXf);

        // Regions tree: branch f rides with fragment f. Branch index maps
        // by the path's LAST index (Roughen emits {f}).
        GH_Structure<GH_Mesh> regionTree = null;
        da.GetDataTree(5, out regionTree);
        var srg = new GH_Structure<GH_Mesh>();
        if (regionTree != null && !regionTree.IsEmpty)
        {
            foreach (var path in regionTree.Paths)
            {
                int f = path.Indices[path.Indices.Length - 1];
                var T = f >= 0 && f < outXf.Count ? outXf[f] : Transform.Identity;
                foreach (var gm in regionTree.get_Branch(path))
                {
                    var mesh = (gm as GH_Mesh)?.Value?.DuplicateMesh();
                    if (mesh == null) { srg.Append(null, path); continue; }
                    mesh.Transform(T);
                    srg.Append(new GH_Mesh(mesh), path);
                }
            }
        }
        da.SetDataTree(2, srg);
    }
}
