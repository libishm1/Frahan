#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Core.Quarry;
using Frahan.Core.Voussoir;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.Voussoir;

// =============================================================================
// VoussoirPackIntoBlockComponent (GUID D5F10011)
//
// Phase 3 (final) of the Voussoir trio per
// wiki/research/voussoir_stereotomy_integration.md.
//
// Takes a VoussoirAssembly + a SINGLE quarried block (Mesh or QuarryBlock)
// and runs a 3D bin-pack to determine which voussoirs can be carved out of
// the block. Outputs: subset of voussoirs that fit (with placement
// transforms), the cut-plan (one cut plane per inter-voussoir interface),
// and the achieved yield ratio (sum_placed_volume / block_volume).
//
// Use case: a single large quarry block (e.g. Quarra's Two Horse Relief
// 80,000 lb pre-cut block) holds N voussoirs of a vault. Pack-Into-Block
// determines the cutting plan to extract them all from the one block.
//
// Algorithm: greedy first-fit-decreasing on AABB extents. Voussoirs are
// sorted by volume descending; for each voussoir, scan a grid of candidate
// positions in the block; first non-overlapping position wins. AABB-vs-AABB
// overlap test (no exact mesh-mesh boolean needed for grid-step packing).
//
// v1 simplification: voussoirs placed at AABB-extent boxes inside the
// block's world AABB. v2 will route through BlockPackTree's DLBF for true
// 3D heightmap packing and via CGAL mesh-boolean for exact-shape fit checks.
// v1 is sufficient for proof-of-concept architectural workflows; v2
// production builds enter the v1.x backlog.
// =============================================================================

[Algorithm("Greedy first-fit-decreasing 3D bin-pack",
    "Frahan-original v1 packing; foundational reference: Garey-Johnson 1979 FFD",
    Note = "v1 AABB-vs-AABB greedy; v2 will route through BlockPackTree DLBF + CGAL exact-shape boolean fit")]
[Algorithm("Park2024TreePack",
    "Park-Han 2024 tree-packing for irregular 3D containers; cited in BlockPackTreeComponent",
    Note = "v2 routing target")]
[Algorithm("Chehrazad2025DLBF",
    "Chehrazad-Roose-Wauters 2025 Deepest-Left-Bottom-Fill, J. Production Research 63:6606-6629",
    Note = "v2 routing target")]
[DesignApplication(
    "Pack as many designed voussoirs as possible into a single quarried block; emit the cut plan.",
    DesignFlow.TopDown,
    Precedent = "Quarra Two Horse Relief Met 2024 (80,000 lb block extracted intact, cut into bas-relief halves); UCL Devadass 2025 SS2.7 minimum-machining; Rippmann-Block 2011 Digital Stereotomy",
    Tolerance = "yield ratio (sum_voussoir_vol / block_vol) >= 0.4; per-voussoir AABB+spacing fits inside block AABB; v1 AABB-grid v2 exact-shape via BlockPackTree",
    CardSet = "wiki/research/hitl_cards/td_voussoir/ (proposed; TD-VOUSSOIR per master plan)")]
public sealed class VoussoirPackIntoBlockComponent : FrahanComponentBase
{
    public VoussoirPackIntoBlockComponent()
        : base("Voussoir Pack Into Block", "VousPack",
            "Pack as many voussoirs as possible into a single quarried block. " +
            "Greedy first-fit-decreasing on AABB extents (v1; v2 routes through " +
            "BlockPackTree DLBF + CGAL exact-shape fit). Outputs: placed " +
            "voussoir indices + per-voussoir transforms + cut-plane plan + " +
            "achieved yield ratio. Use case: extract all voussoirs of a vault " +
            "from one large quarry block (Quarra Two Horse Relief pattern).",
            "Frahan", "Voussoir")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("D5F10011-ED9E-4ED9-A011-ED9EED9E0011");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap Icon => IconProvider.Load("EdgeMatchSolve.png"); // placeholder

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Assembly", "VA",
            "VoussoirAssembly from VoussoirIngestComponent (D5F1000F).",
            GH_ParamAccess.item);
        p.AddGenericParameter("Block", "B",
            "A single quarried block (accepts QuarryBlock typed record from " +
            "ScanToBlockInventoryComponent F2D0BC20, OR a raw Mesh).",
            GH_ParamAccess.item);
        p.AddNumberParameter("Spacing", "S",
            "Gap between adjacent voussoirs (mm), used as the saw-kerf + " +
            "carving allowance. Default 5.0 mm.",
            GH_ParamAccess.item, 5.0);
        p.AddNumberParameter("Grid Step", "G",
            "Grid step for the candidate-position scan (mm). Smaller = denser " +
            "search = slower. Default 25.0 mm.",
            GH_ParamAccess.item, 25.0);
        p.AddBooleanParameter("Allow Skip", "As",
            "If true, voussoirs that cannot be placed are skipped + reported. " +
            "If false, fail loudly when any voussoir cannot be placed. Default true.",
            GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Placed Voussoirs", "PV",
            "Per-voussoir transformed mesh (placed in the block's local frame). " +
            "Null where the voussoir could not be placed.",
            GH_ParamAccess.list);
        p.AddTransformParameter("Transforms", "T",
            "Per-voussoir placement transform (identity where unplaced).",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Fit Voussoir Indices", "Fi",
            "Indices of voussoirs that were successfully placed (sorted by " +
            "placement order = volume descending).",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Skip Voussoir Indices", "Si",
            "Indices of voussoirs that could NOT be placed (under-provisioned).",
            GH_ParamAccess.list);
        p.AddNumberParameter("Yield Ratio", "Y",
            "Sum-of-placed-volumes / block-volume. >= 0.4 is typically " +
            "production-acceptable per UCL Devadass 2025 §2.7.",
            GH_ParamAccess.item);
        p.AddPlaneParameter("Cut Planes", "Cp",
            "Cut-plan: one plane per adjacent-voussoir-pair joint inside the " +
            "block. The cutting sequence is implied by placement order.",
            GH_ParamAccess.list);
        p.AddTextParameter("Remarks", "R",
            "Diagnostic notes -- voussoir count placed/skipped, yield, " +
            "block fill rate.",
            GH_ParamAccess.list);
    }

    protected override void SolveSafe(IGH_DataAccess DA)
    {
        VoussoirAssembly assembly = null;
        IGH_Goo blockGoo = null;
        double spacing = 5.0;
        double gridStep = 25.0;
        bool allowSkip = true;

        if (!DA.GetData(0, ref assembly)) return;
        if (!DA.GetData(1, ref blockGoo)) return;
        DA.GetData(2, ref spacing);
        DA.GetData(3, ref gridStep);
        DA.GetData(4, ref allowSkip);

        if (assembly == null || assembly.Voussoirs == null || assembly.Voussoirs.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Voussoir Assembly is null or empty. Wire from VoussoirIngestComponent.");
            return;
        }

        // Resolve the block to a Mesh + AABB + volume.
        Mesh blockMesh = null;
        double blockVolume = 0;
        BoundingBox blockAabb;

        if (blockGoo is GH_ObjectWrapper ow && ow.Value is QuarryBlock qb)
        {
            blockMesh = qb.Bounds ?? qb.UsableVolume;
            blockVolume = qb.Volume;
        }
        else if (blockGoo is GH_Mesh gm && gm.Value != null)
        {
            blockMesh = gm.Value;
            blockVolume = Math.Abs(blockMesh.Volume());
        }
        else
        {
            Mesh m = null;
            if (blockGoo != null && blockGoo.CastTo(out m))
            {
                blockMesh = m;
                blockVolume = Math.Abs(blockMesh.Volume());
            }
        }

        if (blockMesh == null || blockMesh.Vertices.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Block must be a non-empty Mesh or QuarryBlock with Bounds. " +
                "Wire from ScanToBlockInventoryComponent.");
            return;
        }

        blockAabb = blockMesh.GetBoundingBox(true);

        // Sort voussoirs by volume descending (first-fit-decreasing).
        int n = assembly.Voussoirs.Count;
        var indexByVolDesc = new int[n];
        for (int i = 0; i < n; i++) indexByVolDesc[i] = i;
        Array.Sort(indexByVolDesc, (a, b) =>
        {
            var va = assembly.Voussoirs[a]?.Volume ?? 0;
            var vb = assembly.Voussoirs[b]?.Volume ?? 0;
            return vb.CompareTo(va);
        });

        // Pre-allocate outputs in the ORIGINAL voussoir order (not the sorted order).
        var placedMeshes = new Mesh[n];
        var transforms = new Transform[n];
        for (int i = 0; i < n; i++) transforms[i] = Transform.Identity;
        var fitIndices = new List<int>();
        var skipIndices = new List<int>();

        // Track placed AABBs for overlap checks.
        var placedAabbs = new List<BoundingBox>();

        foreach (int vi in indexByVolDesc)
        {
            var v = assembly.Voussoirs[vi];
            if (v == null || v.Geometry == null)
            {
                skipIndices.Add(vi);
                continue;
            }

            // Voussoir's AABB extent (with spacing margin).
            var vAabbWorld = v.Geometry.GetBoundingBox(true);
            double vDx = (vAabbWorld.Max.X - vAabbWorld.Min.X) + 2 * spacing;
            double vDy = (vAabbWorld.Max.Y - vAabbWorld.Min.Y) + 2 * spacing;
            double vDz = (vAabbWorld.Max.Z - vAabbWorld.Min.Z) + 2 * spacing;

            // Block-relative extent budget.
            double bDx = blockAabb.Max.X - blockAabb.Min.X;
            double bDy = blockAabb.Max.Y - blockAabb.Min.Y;
            double bDz = blockAabb.Max.Z - blockAabb.Min.Z;

            if (vDx > bDx || vDy > bDy || vDz > bDz)
            {
                // Voussoir's bounding extents exceed the block's; skip.
                skipIndices.Add(vi);
                continue;
            }

            // Greedy grid scan for an unoccupied position.
            bool placed = false;
            for (double x = blockAabb.Min.X; x + vDx <= blockAabb.Max.X + 1e-6 && !placed; x += gridStep)
            for (double y = blockAabb.Min.Y; y + vDy <= blockAabb.Max.Y + 1e-6 && !placed; y += gridStep)
            for (double z = blockAabb.Min.Z; z + vDz <= blockAabb.Max.Z + 1e-6 && !placed; z += gridStep)
            {
                var candidate = new BoundingBox(
                    new Point3d(x, y, z),
                    new Point3d(x + vDx, y + vDy, z + vDz));
                if (OverlapsAny(candidate, placedAabbs)) continue;

                // Translate the voussoir from its current world position to the candidate.
                var dx = candidate.Center.X - vAabbWorld.Center.X;
                var dy = candidate.Center.Y - vAabbWorld.Center.Y;
                var dz = candidate.Center.Z - vAabbWorld.Center.Z;
                var xform = Transform.Translation(new Vector3d(dx, dy, dz));
                var movedMesh = v.Geometry.DuplicateMesh();
                movedMesh.Transform(xform);

                placedMeshes[vi] = movedMesh;
                transforms[vi] = xform;
                placedAabbs.Add(candidate);
                fitIndices.Add(vi);
                placed = true;
            }

            if (!placed) skipIndices.Add(vi);
        }

        // Yield ratio.
        double placedVolSum = 0;
        foreach (int fi in fitIndices) placedVolSum += assembly.Voussoirs[fi].Volume;
        double yieldRatio = blockVolume > 0 ? placedVolSum / blockVolume : 0;

        // Cut planes: one plane between each pair of placed-and-adjacent voussoirs.
        // Use the midpoint of the two voussoirs' AABB centres as the cut plane origin,
        // and the direction from one centre to the other as the plane normal.
        var cutPlanes = new List<Plane>();
        for (int a = 0; a < fitIndices.Count; a++)
        for (int b = a + 1; b < fitIndices.Count; b++)
        {
            int ia = fitIndices[a], ib = fitIndices[b];
            if (placedMeshes[ia] == null || placedMeshes[ib] == null) continue;
            var ca = placedMeshes[ia].GetBoundingBox(true).Center;
            var cb = placedMeshes[ib].GetBoundingBox(true).Center;
            double d = ca.DistanceTo(cb);
            if (d < 1e-9) continue;
            var origin = (ca + cb) * 0.5;
            var normal = cb - ca;
            normal.Unitize();
            // Only emit the plane if the pair is genuinely adjacent (centres
            // within Sum-of-half-spans + spacing). Cheap proxy.
            var aBox = placedMeshes[ia].GetBoundingBox(true);
            var bBox = placedMeshes[ib].GetBoundingBox(true);
            double aHalf = aBox.Diagonal.Length * 0.5;
            double bHalf = bBox.Diagonal.Length * 0.5;
            if (d <= aHalf + bHalf + spacing * 2.0)
            {
                cutPlanes.Add(new Plane(origin, normal));
            }
        }

        var remarks = new List<string>
        {
            $"Pack-Into-Block complete. {fitIndices.Count}/{n} voussoirs placed; " +
            $"{skipIndices.Count} skipped.",
            $"Block volume: {blockVolume:F0} mm³; placed volume sum: {placedVolSum:F0} mm³.",
            $"Yield ratio: {yieldRatio:F3} ({100 * yieldRatio:F1}%).",
            $"Grid step: {gridStep:F1} mm; spacing: {spacing:F1} mm. " +
            $"v1 AABB greedy; v2 will route through BlockPackTree DLBF for tighter packing.",
            $"Cut-plane count: {cutPlanes.Count} (one per adjacent placed pair)."
        };

        if (!allowSkip && skipIndices.Count > 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"{skipIndices.Count} voussoir(s) could not be placed. Set Allow Skip = true " +
                "to accept this, supply a larger block, or reduce voussoir count.");
            return;
        }
        if (skipIndices.Count > 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{skipIndices.Count}/{n} voussoir(s) could not be placed in this block.");
        }

        DA.SetDataList(0, placedMeshes);
        DA.SetDataList(1, transforms);
        DA.SetDataList(2, fitIndices);
        DA.SetDataList(3, skipIndices);
        DA.SetData(4, yieldRatio);
        DA.SetDataList(5, cutPlanes);
        DA.SetDataList(6, remarks);
    }

    private static bool OverlapsAny(BoundingBox candidate, List<BoundingBox> placed)
    {
        foreach (var p in placed)
        {
            if (BoxesOverlap(candidate, p)) return true;
        }
        return false;
    }

    private static bool BoxesOverlap(BoundingBox a, BoundingBox b)
    {
        // Standard AABB overlap test.
        if (a.Max.X < b.Min.X || b.Max.X < a.Min.X) return false;
        if (a.Max.Y < b.Min.Y || b.Max.Y < a.Min.Y) return false;
        if (a.Max.Z < b.Min.Z || b.Max.Z < a.Min.Z) return false;
        return true;
    }
}
