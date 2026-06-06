#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Frahan.Masonry;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // RubbleWallSettleComponent — concave-aware Z-up rubble-wall settle.
    //
    // Compiled C# component wrapping Frahan.Masonry.RubbleWallSettle, a faithful
    // port of the user-signed-off Python prototype:
    //   outputs/2026-05-25/eth1100_pack/eth1100_rubble_settle.py (algorithm)
    //   outputs/2026-05-25/eth1100_pack/stability.py            (COM-over-support)
    //
    // Z-up wall: gravity = -Z (courses stack UP in +Z), length = X, thickness
    // = Y. Each stone is PCA-oriented for flat bedding (broad face beds DOWN),
    // gets 4 flip variants, and is dropped into the dimples of the course below
    // per (x,y)-cell with a small X-slot search. Stability is the shared
    // COM-over-support test (COM projected to the bed must lie inside the
    // convex hull of the contact footprint).
    //
    // ComponentGuid: 6514A1BB-FE82-4919-9419-141A07D2358A
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Rubble Wall Settle.
    /// Settles stone meshes into an upright Z-up rubble wall with concave-aware
    /// per-cell contact and a COM-over-support stability check.
    /// </summary>
    [Algorithm("Concave-aware Z-up rubble settle", "Frahan-original (ETH1100 dry-stone HITL rev 2, signed off 2026-05-25)", Note = "PCA flat-bed + 4 flip variants + per-(x,y)-cell drop settle")]
    [Algorithm("COM-over-support stability", "Heyman 1966 limit-state masonry (centre of thrust within the support)", Note = "COM projected to the bed must lie inside the convex hull of the contact footprint")]
    [RelatedComponent("Frahan > Masonry > Ashlar Pack",
        Reason = "production coursed layout; this settle drops rough rubble into the dimples instead of a regular grid")]
    [RelatedComponent("Frahan > Masonry > Best Fit Pack",
        Reason = "inventory-aware ashlar packer for the same stone inventory")]
    [RelatedComponent("Frahan > Masonry > Masonry Stability (RBE)",
        Reason = "full rigid-block equilibrium; this component does only the per-stone COM-over-support gate")]
        [DesignApplication(
        "Settles stone meshes into an upright Z-up rubble wall",
        DesignFlow.BottomUp,
        Precedent = "Heyman 1966 limit-state masonry theorem; Gramazio Kohler Autonomous Dry Stone (Johns et al. 2020-2022); Furrer et al. 2017 IROS Autonomous Robotic Stone Stacking",
        Tolerance = "all stones with CoM over support polygon; max penetration <= 1 mm",
        CardSet = "wiki/research/hitl_cards/bu_drop_settle/")]
    public sealed class RubbleWallSettleComponent : GH_Component
    {
        public RubbleWallSettleComponent()
            : base(
                "Rubble Wall Settle", "RubbleSettle",
                "Settles stone meshes into an upright Z-up rubble wall. Each " +
                "stone is PCA-oriented so its broad/flat face beds DOWN, then " +
                "dropped (gravity = -Z) into the per-(x,y)-cell dimples of the " +
                "course below, trying 4 orientation flips and a small X-slot " +
                "shift. Non-penetrating by construction. Reports a per-stone " +
                "COM-over-support stability flag and signed support clearance. " +
                "Apply each output mesh as-is; transforms are already baked in.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("6514A1BB-FE82-4919-9419-141A07D2358A");

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override Bitmap Icon => IconProvider.Load("RubbleWallSettle.png");

        // ─── Params ─────────────────────────────────────────────────────────

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Stones", "S",
                "Stone inventory as Rhino meshes (e.g. ETH1100 dry-stone scans, " +
                "Quarry blocks, or hand-authored). Each is PCA-oriented for flat " +
                "bedding; the input meshes are not modified. Order is preserved " +
                "in the outputs.",
                GH_ParamAccess.list);
            p.AddNumberParameter("Width", "W",
                "Wall length along +X, in units of the mean stone X-extent. " +
                "> 0. Default 7.0 (the signed-off proportion). Larger spreads " +
                "stones into more, shorter courses; smaller piles them taller.",
                GH_ParamAccess.item, 7.0);
            p.AddBooleanParameter("Stability Aware", "St",
                "When true, each stone prefers the first seat whose COM projects " +
                "inside its contact support polygon (won't topple), then the " +
                "deepest. When false, always takes the deepest (densest) seat. " +
                "Default true.",
                GH_ParamAccess.item, true);
            p.AddNumberParameter("Margin", "M",
                "Required COM-over-support clearance (document units) for a seat " +
                "to count as stable. >= 0. Default 0.0 (COM merely inside the " +
                "support polygon).",
                GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Settled", "S",
                "Placed stones, upright in the Z-up wall, one per input mesh in " +
                "input order. The PCA flat-bed orientation, flip, and settle " +
                "offsets are already applied.",
                GH_ParamAccess.list);
            p.AddBooleanParameter("Stable", "St",
                "Per-stone COM-over-support flag: true if the projected COM lies " +
                "inside the contact support polygon by at least Margin.",
                GH_ParamAccess.list);
            p.AddNumberParameter("Clearance", "C",
                "Per-stone signed support clearance. > 0 = COM inside the support " +
                "polygon (distance to the nearest edge); <= 0 = would topple; " +
                "-1 = degenerate support (< 3 non-collinear contacts).",
                GH_ParamAccess.list);
        }

        // ─── Solve ──────────────────────────────────────────────────────────

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var stones = new List<Mesh>();
            if (!da.GetDataList(0, stones) || stones.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No stones provided.");
                return;
            }

            double width = 7.0;
            bool stabilityAware = true;
            double margin = 0.0;
            if (!da.GetData(1, ref width)) width = 7.0;
            if (!da.GetData(2, ref stabilityAware)) stabilityAware = true;
            if (!da.GetData(3, ref margin)) margin = 0.0;

            if (width <= 0.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Width must be > 0, got {width}.");
                return;
            }
            if (margin < 0.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Margin must be >= 0, got {margin}.");
                return;
            }

            // Count valid meshes for a remark; the engine handles nulls gracefully.
            int valid = 0;
            for (int i = 0; i < stones.Count; i++)
                if (stones[i] != null && stones[i].Vertices.Count >= 3) valid++;
            if (valid == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "No mesh has >= 3 vertices; nothing to settle.");
                return;
            }
            if (valid < stones.Count)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{stones.Count - valid} of {stones.Count} meshes are null or degenerate; " +
                    "they are left at the origin (Stable=false, Clearance=-1).");

            IList<RubbleStonePlacement> placements;
            try
            {
                placements = RubbleWallSettle.Settle(
                    stones, width, stabilityAware, margin);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Rubble wall settle failed: {ex.Message}");
                return;
            }

            var settled = new List<Mesh>(stones.Count);
            var stable = new List<bool>(stones.Count);
            var clearance = new List<double>(stones.Count);
            int nStable = 0;
            for (int i = 0; i < placements.Count; i++)
            {
                var src = stones[i];
                Mesh placed;
                if (src != null)
                {
                    placed = src.DuplicateMesh();
                    placed.Transform(placements[i].Transform);
                }
                else
                {
                    placed = new Mesh();
                }
                settled.Add(placed);
                stable.Add(placements[i].Stable);
                clearance.Add(placements[i].Clearance);
                if (placements[i].Stable) nStable++;
            }

            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Settled {settled.Count} stones; STABLE {nStable}/{settled.Count} (COM over support). " +
                "Visual HITL: confirm stones bed on their flat face and the wall reads as rubble.");

            da.SetDataList(0, settled);
            da.SetDataList(1, stable);
            da.SetDataList(2, clearance);
        }
    }
}
