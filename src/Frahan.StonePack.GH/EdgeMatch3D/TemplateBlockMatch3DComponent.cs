#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.EdgeMatching;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.EdgeMatch3D;

// =============================================================================
// TemplateBlockMatch3DComponent (Component D3D, GUID D5F1000B)
//
// 3D sibling of Component D (Template Panel Match). Designer supplies an
// N-cell 3D template (voussoir layout from Voussoir Ingest, or a brep with
// planar cells), and an inventory of scanned stones. The component runs
// Hungarian bipartite assignment using Component C3D as the per-pair atom,
// computing a cost matrix from the trim-needed volume + post-trim
// residual.
//
// Same algorithm as the Voussoir Stone Matcher (per
// wiki/research/voussoir_stereotomy_integration.md Phase 2). The
// HungarianAssigner.cs utility is the shared substrate.
//
// Status: SKELETON. The Hungarian solver is real (HungarianAssigner.cs);
// the cost matrix evaluation via Component C3D is the v1.x build target.
// =============================================================================

[Algorithm("Hungarian assignment",
    "H.W. Kuhn 1955 Hungarian Method for the Assignment Problem; Jonker-Volgenant pivot",
    Note = "Stage 2: bipartite assignment from M x N cost matrix; O(N^3)")]
[Algorithm("Adaptive Block Match 3D",
    "See AdaptiveBlockMatch3DComponent for C3D pipeline references",
    Note = "Stage 1: per-pair cost computation via dry-run trim evaluation")]
[Algorithm("Pareto multi-objective fallback",
    "Deb et al. 2002 NSGA-II, IEEE TEVC 6(2):182-197",
    Note = "Strategy=Pareto only; used when M*N > 40k cost cells exceeds Hungarian capacity")]
[DesignApplication(
    "Assign scanned stones to a designed voussoir / template layout via optimal bipartite matching.",
    DesignFlow.TopDown,
    Precedent = "Quarra Parallel Nature off-cut matching; Block Research Group Armadillo Vault voussoir subdivision; Voussoir GH plugin",
    Tolerance = "per-cell trim volume <= 10 %; total assignment cost globally optimal (Hungarian)",
    CardSet = "wiki/research/hitl_cards/em_2d_template_panel_match/ (2D sibling); em_3d_chain_ucl_bartlett/ (3D consumer)")]
public sealed class TemplateBlockMatch3DComponent : GH_Component
{
    public TemplateBlockMatch3DComponent()
        : base("Template Block Match 3D", "TmplBlk3D",
            "3D sibling of Component D. Designer supplies an N-cell 3D template " +
            "(voussoir layout). Inventory is a list of scanned stones. " +
            "Hungarian bipartite assignment solves the optimal one-to-one " +
            "mapping (stone -> cell) minimising total trim volume + post-trim " +
            "residual. Cost matrix per-cell evaluated via Component C3D in " +
            "dry-run mode. Same algorithm as Voussoir Stone Matcher (shared " +
            "HungarianAssigner.cs). [Kuhn 1955]",
            "Frahan", "EdgeMatch")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("D5F1000B-ED9E-4ED9-A00B-ED9EED9E000B");

    // 2026-06-05 (W6, keep-or-cut): hidden from the ribbon. Only Strategy=1 runs,
    // and it solves a Hungarian assignment on a VOLUME-DIFFERENCE PROXY cost (not
    // the real per-pair fit), and it never applies a placement transform, so the
    // "Placed Stones" output returns inventory meshes in their original pose. That
    // is misleading on the primary ribbon. Not Obsolete: it is unbuilt future work.
    // Build target = real cost matrix from the BlockPairMatch3D per-pair dry-run +
    // an actual placement transform. Flip back to primary when those land.
    // GUID preserved. See outputs/2026-06-05/keep_or_cut/UNBUILT_COMPONENTS.md.
    public override GH_Exposure Exposure => GH_Exposure.hidden;

    protected override Bitmap Icon => IconProvider.Load("EdgeMatchSolve.png"); // placeholder

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Template Cells", "Tc",
            "List of designed cell meshes (voussoir layout). One mesh per cell.",
            GH_ParamAccess.list);
        p.AddMeshParameter("Stone Inventory", "I",
            "List of scanned-stone meshes.", GH_ParamAccess.list);
        p.AddIntegerParameter("Strategy", "St",
            "0=Greedy, 1=Hungarian (default; globally optimal), 2=Pareto (NSGA-II fallback for M*N > 40k).",
            GH_ParamAccess.item, 1);
        p.AddBooleanParameter("Allow Trim", "At",
            "If true, calls Component C3D to evaluate cost with minimal trim. " +
            "If false, only no-trim Block Pair Match 3D matches are considered.",
            GH_ParamAccess.item, true);
        p.AddNumberParameter("Max Trim Volume Ratio", "Mtv",
            "Per-pair trim volume budget. Per-pair cost = infinity if exceeded.",
            GH_ParamAccess.item, 0.1);
        p.AddBooleanParameter("Allow Empty", "Ae",
            "If true, cells with no feasible inventory match remain unassigned (reported). " +
            "If false, fail loudly when any cell would remain empty.",
            GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Placed Stones", "Ps",
            "Per-cell placed (and trimmed if applicable) stone meshes.",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Cell Indices", "Ci",
            "Per-cell index in the template (parallels Placed Stones).",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Stone Indices", "Si",
            "Per-cell inventory index assigned (parallels Cell Indices).",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Unassigned Cells", "Uc",
            "Cell indices with no feasible inventory match (when Allow Empty=true).",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Unused Stones", "Us",
            "Inventory indices not consumed in the assignment.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Total Cost", "Tc",
            "Sum of per-cell costs across the assignment (Hungarian objective value).",
            GH_ParamAccess.item);
        p.AddTextParameter("Remarks", "Rm",
            "Diagnostic notes -- strategy used, cost matrix size, rejected stones, etc.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var cells = new List<Mesh>();
        var inventory = new List<Mesh>();
        int strategy = 1;
        bool allowTrim = true;
        double maxTrimRatio = 0.1;
        bool allowEmpty = true;

        if (!DA.GetDataList(0, cells)) return;
        if (!DA.GetDataList(1, inventory)) return;
        DA.GetData(2, ref strategy);
        DA.GetData(3, ref allowTrim);
        DA.GetData(4, ref maxTrimRatio);
        DA.GetData(5, ref allowEmpty);

        int M = cells.Count;
        int N = inventory.Count;

        // STUB: emit a Hungarian assignment on a placeholder cost matrix
        // (volume-difference proxy). The full pipeline -- per-pair Component
        // C3D dry-run + real trim-cost evaluation -- is the v1.x build.
        if (strategy == 1 && M > 0 && N > 0)
        {
            var cost = new double[M * N];
            for (int i = 0; i < M; i++)
            for (int j = 0; j < N; j++)
            {
                double cellVol = cells[i] == null ? 1.0 : Math.Abs(cells[i].Volume());
                double stoneVol = inventory[j] == null ? 1.0 : Math.Abs(inventory[j].Volume());
                cost[i * N + j] = Math.Abs(cellVol - stoneVol) / Math.Max(cellVol, 1.0);
            }
            int[] assignment = HungarianAssigner.Solve(cost, M, N);

            var placed = new List<Mesh>();
            var cellIdx = new List<int>();
            var stoneIdx = new List<int>();
            var unassigned = new List<int>();
            for (int i = 0; i < M; i++)
            {
                if (assignment[i] >= 0)
                {
                    placed.Add(inventory[assignment[i]]);
                    cellIdx.Add(i);
                    stoneIdx.Add(assignment[i]);
                }
                else
                {
                    unassigned.Add(i);
                }
            }
            var usedSet = new HashSet<int>(stoneIdx);
            var unused = new List<int>();
            for (int j = 0; j < N; j++) if (!usedSet.Contains(j)) unused.Add(j);

            DA.SetDataList(0, placed);
            DA.SetDataList(1, cellIdx);
            DA.SetDataList(2, stoneIdx);
            DA.SetDataList(3, unassigned);
            DA.SetDataList(4, unused);
            DA.SetData(5, 0.0); // TODO compute Hungarian objective
            DA.SetDataList(6, new List<string>
            {
                "STUB v1.x: Hungarian assignment done on volume-difference cost. " +
                "Real cost matrix (Component C3D per-pair) lands in v1.x. " +
                $"Inputs: M={M} cells, N={N} stones."
            });
        }
        else
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "Strategy " + strategy + " not implemented in stub (only Strategy=1 Hungarian works). " +
                "Greedy and Pareto land in v1.x.");
        }
    }
}
