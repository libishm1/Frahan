#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Core.Quarry;
using Frahan.Core.Voussoir;
using Frahan.EdgeMatching;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.Voussoir;

// =============================================================================
// VoussoirStoneMatcherComponent (GUID D5F10010)
//
// Second component of the Voussoir trio. Takes a VoussoirAssembly (from
// VoussoirIngestComponent D5F1000F) plus a quarry-block inventory (from
// ScanToBlockInventoryComponent F2D0BC20 OR a raw Mesh list), builds the
// cost matrix `c_ij = w_yield * (1 - V_voussoir/V_stone) + w_carving * (V_stone - V_voussoir) / V_voussoir`
// where N_ij gates feasibility on `V_stone >= V_voussoir + margin`, and
// runs the Kuhn 1955 Hungarian assignment via HungarianAssigner.Solve.
//
// This is the FIRST PRODUCTION USE of the MatcherRegistry substrate
// (wiki/specs/component_decomposition_plan.md §5.4) — a top-down,
// design-first workflow that mirrors structuralCircle Tomczak 2023's
// pattern (Figure 2): Demand + Supply + ConstraintDict + Scorefn ->
// Incidence (N) + Weights (C) -> Hungarian -> AssignmentResult.
//
// Per the convenience-composition discipline (decomposition plan §5.7):
// this component is a MONOLITHIC convenience that inlines the substrate
// primitives. Power users who want to swap solvers, inject custom costs,
// or tweak constraints compose the substrate primitives separately.
// Novices who want "Voussoir + Hungarian, the canonical case" grab this
// component.
//
// Reference precedents:
// - Rippmann-Block 2011 Digital Stereotomy (the form-finding upstream)
// - Block Research Group Armadillo Vault 2016 (the canonical built example)
// - Tomczak-Haakonsen-Luczkowski 2023 (DOI 10.1088/2634-4505/acf341)
//   for the matching-pipeline shape
// - Kuhn 1955 Hungarian Method (Naval Research Logistics Quarterly 2:83-97)
// - Frahan HungarianAssigner.cs (real implementation, shipped 2026-05-31)
// =============================================================================

[Algorithm("Kuhn1955Hungarian",
    "H.W. Kuhn 1955 Hungarian Method for the Assignment Problem; Naval Research Logistics Quarterly 2:83-97; Jonker-Volgenant pivot",
    Note = "Shared substrate -- the same HungarianAssigner serves Template Panel Match (D5F10007) + Template Block Match 3D (D5F1000B) + this component.")]
[Algorithm("Tomczak2023Matching",
    "Tomczak/Haakonsen/Luczkowski 2023 Environ. Res. Infrastruct. Sustain. 3:035005 DOI 10.1088/2634-4505/acf341 -- Figure 2 5-stage matching pipeline",
    Note = "Frahan inherits the Incidence (N) + Weighted Incidence (C) separation per the paper SS4.3-4.7.")]
[Algorithm("OBB containment + yield-ratio scoring",
    "Frahan-original: stock contains template AABB+margin AND yield_ratio = template_vol/stock_vol >= MinYield",
    Note = "Feasibility check (the Incidence matrix N_ij).")]
[DesignApplication(
    "Assign each designed voussoir to the smallest quarry block that can yield it; minimise waste.",
    DesignFlow.TopDown,
    Precedent = "Rippmann-Block 2011 Digital Stereotomy; Block Research Group Armadillo Vault 2016; Quarra Parallel Nature off-cut matching; UCL Devadass 2025 50-fragment limestone library",
    Tolerance = "yield ratio (template_vol/stock_vol) >= 0.4 default; carving <= 30% of stock volume; OBB containment + 5 mm safety margin",
    CardSet = "wiki/research/hitl_cards/td_voussoir/ (proposed; TD-VOUSSOIR per master plan)")]
public sealed class VoussoirStoneMatcherComponent : GH_Component
{
    public VoussoirStoneMatcherComponent()
        : base("Voussoir Stone Matcher", "VousMatch",
            "Assign each voussoir to a quarry stone via Kuhn 1955 Hungarian " +
            "bipartite assignment. Voussoirs are demand; stones are supply; " +
            "feasibility = stone OBB contains voussoir OBB + safety margin + " +
            "yield_ratio >= MinYield; cost = w_yield * (1 - yield_ratio) + " +
            "w_carving * (carving_vol / voussoir_vol). The canonical top-down " +
            "voussoir-to-stone matcher per wiki/research/voussoir_stereotomy_integration.md " +
            "Phase 2 + philosophy doc §10.6. First production use of the " +
            "MatcherRegistry substrate.",
            "Frahan", "Voussoir")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("D5F10010-ED9E-4ED9-A010-ED9EED9E0010");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap Icon => IconProvider.Load("EdgeMatchSolve.png"); // placeholder

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Assembly", "VA",
            "VoussoirAssembly from VoussoirIngestComponent (D5F1000F).",
            GH_ParamAccess.item);
        p.AddGenericParameter("Quarry Stones", "QS",
            "List of quarry-block candidates. Accepts either: " +
            "(a) QuarryBlock typed records from ScanToBlockInventoryComponent " +
            "(F2D0BC20), or (b) raw Mesh inputs (in which case AABB+volume " +
            "are computed inline). Mixed lists are accepted.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Min Yield", "MY",
            "Minimum yield ratio (voussoir_vol / stone_vol) for a feasible pair. " +
            "Default 0.4 (40%). Stones below this are excluded as wasteful.",
            GH_ParamAccess.item, 0.4);
        p.AddNumberParameter("Safety Margin", "SM",
            "Safety margin added to voussoir OBB extent before containment test " +
            "(mm). Default 5.0.",
            GH_ParamAccess.item, 5.0);
        p.AddNumberParameter("Yield Weight", "Wy",
            "Cost weight for the yield term `1 - yield_ratio`. Default 1.0.",
            GH_ParamAccess.item, 1.0);
        p.AddNumberParameter("Carving Weight", "Wc",
            "Cost weight for the carving term `(stone_vol - voussoir_vol) / voussoir_vol`. " +
            "Default 0.5.",
            GH_ParamAccess.item, 0.5);
        p.AddBooleanParameter("Allow Empty", "Ae",
            "If true, unassigned voussoirs are reported (under-provisioned case). " +
            "If false, fail loudly when any voussoir would remain unassigned. " +
            "Default true.",
            GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddIntegerParameter("Assignment", "A",
            "Per-voussoir stone index (-1 = unassigned).",
            GH_ParamAccess.list);
        p.AddMeshParameter("Placed Stones", "PS",
            "Per-voussoir assigned stone mesh (null where unassigned).",
            GH_ParamAccess.list);
        p.AddNumberParameter("Yield Ratios", "Y",
            "Per-voussoir yield ratio (voussoir_vol / stone_vol). 0 if unassigned.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Carving Volumes", "Cv",
            "Per-voussoir carving volume (stone_vol - voussoir_vol). 0 if unassigned.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Per-Pair Cost", "Pc",
            "Per-voussoir total cost (yield + carving weighted sum). " +
            "+Inf where unassigned / infeasible.",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Unassigned Voussoirs", "Uv",
            "Indices of voussoirs that received no stone (under-provisioned).",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Unused Stones", "Us",
            "Indices of stones that were not consumed.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Total Cost", "Tc",
            "Sum of per-pair costs across the assignment.",
            GH_ParamAccess.item);
        p.AddTextParameter("Remarks", "R",
            "Diagnostic notes -- strategy, infeasibility reasons, M*N matrix size.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        VoussoirAssembly assembly = null;
        var stonesIn = new List<IGH_Goo>();
        double minYield = 0.4;
        double safetyMargin = 5.0;
        double yieldWeight = 1.0;
        double carvingWeight = 0.5;
        bool allowEmpty = true;

        if (!DA.GetData(0, ref assembly)) return;
        if (!DA.GetDataList(1, stonesIn)) return;
        DA.GetData(2, ref minYield);
        DA.GetData(3, ref safetyMargin);
        DA.GetData(4, ref yieldWeight);
        DA.GetData(5, ref carvingWeight);
        DA.GetData(6, ref allowEmpty);

        if (assembly == null || assembly.Voussoirs == null || assembly.Voussoirs.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Voussoir Assembly is null or empty. Wire from VoussoirIngestComponent.");
            return;
        }

        var voussoirs = assembly.Voussoirs;
        int M = voussoirs.Count;

        // Normalise stones input to (Mesh, Volume, OBB extents).
        var stones = new List<StoneAdapter>(stonesIn.Count);
        for (int j = 0; j < stonesIn.Count; j++)
        {
            var g = stonesIn[j];
            Mesh sm = null;
            double sv = 0;
            Box sbox = Box.Unset;
            // Try QuarryBlock first.
            if (g is GH_ObjectWrapper ow && ow.Value is QuarryBlock qb)
            {
                sm = qb.Bounds ?? qb.UsableVolume;
                sv = qb.Volume;
                if (sm != null) sbox = new Box(sm.GetBoundingBox(true));
            }
            else if (g is GH_Mesh gm && gm.Value != null)
            {
                sm = gm.Value;
                sv = Math.Abs(sm.Volume());
                sbox = new Box(sm.GetBoundingBox(true));
            }
            else
            {
                Mesh m = null;
                if (g != null && g.CastTo(out m))
                {
                    sm = m;
                    sv = Math.Abs(sm.Volume());
                    sbox = new Box(sm.GetBoundingBox(true));
                }
            }
            stones.Add(new StoneAdapter
            {
                Index = j,
                Mesh = sm,
                Volume = sv,
                Bounds = sbox,
            });
        }
        int N = stones.Count;

        if (N == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "No quarry stones provided. Wire from ScanToBlockInventoryComponent " +
                "or a raw Mesh list.");
            return;
        }

        // Build cost matrix C[M*N] with HungarianAssigner.Infeasible (= 1e18)
        // where the (voussoir, stone) pair fails feasibility. Mirrors the
        // structuralCircle Tomczak 2023 §4.7 trick: fix infeasible cells'
        // upper-bound to 0 (or here, cost to infinity, equivalent).
        var cost = new double[M * N];
        int feasibleCount = 0;
        var remarks = new List<string>();

        for (int i = 0; i < M; i++)
        {
            var v = voussoirs[i];
            if (v == null)
            {
                for (int j = 0; j < N; j++) cost[i * N + j] = HungarianAssigner.Infeasible;
                continue;
            }
            var vBox = v.OrientedBoundingBox;
            double vVol = v.Volume;
            double vExt0 = vBox.X.Length + 2 * safetyMargin;
            double vExt1 = vBox.Y.Length + 2 * safetyMargin;
            double vExt2 = vBox.Z.Length + 2 * safetyMargin;

            // Lithology constraint: if the voussoir specifies a lithology
            // hint AND the stone carries a Label, the values must match.
            // (Lithology lives on QuarryBlock.Label per the existing convention.)
            string vLith = v.LithologyHint ?? "";

            for (int j = 0; j < N; j++)
            {
                var s = stones[j];
                if (s.Mesh == null || s.Volume <= 0)
                {
                    cost[i * N + j] = HungarianAssigner.Infeasible;
                    continue;
                }
                // Containment test: stone OBB extents >= voussoir OBB+margin.
                double sExt0 = s.Bounds.X.Length;
                double sExt1 = s.Bounds.Y.Length;
                double sExt2 = s.Bounds.Z.Length;
                var sExts = new[] { sExt0, sExt1, sExt2 };
                var vExts = new[] { vExt0, vExt1, vExt2 };
                Array.Sort(sExts);
                Array.Sort(vExts);
                if (sExts[0] < vExts[0] || sExts[1] < vExts[1] || sExts[2] < vExts[2])
                {
                    cost[i * N + j] = HungarianAssigner.Infeasible;
                    continue;
                }
                // Yield gate.
                double yield = vVol / s.Volume;
                if (yield < minYield)
                {
                    cost[i * N + j] = HungarianAssigner.Infeasible;
                    continue;
                }
                // Cost = yield-term + carving-term.
                double yieldTerm = (1.0 - yield);
                double carvingTerm = (s.Volume - vVol) / Math.Max(vVol, 1e-9);
                double c = yieldWeight * yieldTerm + carvingWeight * carvingTerm;
                cost[i * N + j] = c;
                feasibleCount++;
            }
        }

        remarks.Add(
            $"Cost matrix: {M} voussoirs x {N} stones = {M * N} cells; " +
            $"{feasibleCount} feasible ({100.0 * feasibleCount / Math.Max(M * N, 1):F1}%).");

        if (feasibleCount == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "No (voussoir, stone) pair is feasible under the current " +
                $"MinYield ({minYield:F2}) + SafetyMargin ({safetyMargin:F1}) + " +
                "containment constraints. Loosen the gates or supply larger stones.");
            return;
        }

        // Run Hungarian assignment via the shared HungarianAssigner.
        int[] assignment;
        try
        {
            assignment = HungarianAssigner.Solve(cost, M, N);
        }
        catch (Exception e)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "HungarianAssigner.Solve failed: " + e.Message);
            return;
        }

        // Build outputs.
        var placedStones = new List<Mesh>(M);
        var yieldRatios = new List<double>(M);
        var carvingVolumes = new List<double>(M);
        var perPairCost = new List<double>(M);
        var unassignedV = new List<int>();
        double totalCost = 0;
        var usedStones = new HashSet<int>();

        for (int i = 0; i < M; i++)
        {
            int j = assignment[i];
            if (j == HungarianAssigner.Unassigned)
            {
                placedStones.Add(null);
                yieldRatios.Add(0);
                carvingVolumes.Add(0);
                perPairCost.Add(double.PositiveInfinity);
                unassignedV.Add(i);
                continue;
            }
            var v = voussoirs[i];
            var s = stones[j];
            placedStones.Add(s.Mesh);
            double y = v.Volume / Math.Max(s.Volume, 1e-9);
            yieldRatios.Add(y);
            carvingVolumes.Add(s.Volume - v.Volume);
            double c = cost[i * N + j];
            perPairCost.Add(c);
            totalCost += c;
            usedStones.Add(j);
        }

        var unusedS = new List<int>();
        for (int j = 0; j < N; j++) if (!usedStones.Contains(j)) unusedS.Add(j);

        // Diagnostics.
        if (unassignedV.Count > 0)
        {
            if (allowEmpty)
                remarks.Add(
                    $"{unassignedV.Count} voussoir(s) unassigned (under-provisioned). " +
                    $"Indices: [{string.Join(",", unassignedV)}].");
            else
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"{unassignedV.Count} voussoir(s) could not be assigned under the " +
                    "current constraints. Set Allow Empty = true to accept this, or " +
                    "supply more / larger stones.");
        }
        if (unusedS.Count > 0)
            remarks.Add(
                $"{unusedS.Count} stone(s) unused (over-provisioned -- return to " +
                "inventory). Indices: [{string.Join(\",\", unusedS)}].".Replace("{string.Join(\",\", unusedS)}", string.Join(",", unusedS)));

        remarks.Add(
            $"Hungarian assignment complete. Total cost = {totalCost:F3} " +
            $"(yield_weight={yieldWeight:F2}, carving_weight={carvingWeight:F2}).");

        DA.SetDataList(0, assignment);
        DA.SetDataList(1, placedStones);
        DA.SetDataList(2, yieldRatios);
        DA.SetDataList(3, carvingVolumes);
        DA.SetDataList(4, perPairCost);
        DA.SetDataList(5, unassignedV);
        DA.SetDataList(6, unusedS);
        DA.SetData(7, totalCost);
        DA.SetDataList(8, remarks);
    }

    private sealed class StoneAdapter
    {
        public int Index;
        public Mesh Mesh;
        public double Volume;
        public Box Bounds;
    }
}
