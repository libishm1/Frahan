#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Quarry.BlockCutOpt;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Quarry;

// =============================================================================
// StochasticDfnComponent (D5F1004C, Frahan > Quarry)
//
// A STOCHASTIC, finite-persistence DFN (Baecher disc model) -- the rigorous
// alternative to the infinite-plane "Joint Sets to DFN" (D5F1004B). Each fracture
// is a finite disc: Poisson centre, Fisher-sampled pole (dispersion kappa), and a
// lognormal radius (persistence). Deterministic by seed; change the seed for a new
// realisation (run many for a Monte-Carlo block-yield distribution). The DFN mesh
// + bench box wire straight into the BlockCutOpt packers.
//
// References: Baecher et al. (1977) disc model; Fisher (1953) orientation; Priest
// (1993) DFN; Dershowitz & Herda (1992) P10/P32. Estimate kappa from a set's
// orientation scatter and persistence from its exposed trace lengths.
// =============================================================================

[Algorithm("Baecher stochastic DFN", "Baecher et al. 1977 finite-disc DFN; Fisher (1953) orientation; lognormal persistence",
    Note = "Poisson centres + Fisher poles + lognormal radii; intensity from P10 = 1/spacing. Deterministic by seed.")]
[RelatedComponent("Frahan > Quarry > Discontinuity Sets (Async)", Reason = "Upstream: dip/dipdir/spacing per set.")]
[RelatedComponent("Frahan > Quarry > Joint Sets to DFN", Reason = "The infinite-plane (deterministic) sibling; this is finite-persistence + stochastic.")]
[RelatedComponent("Frahan > Quarry > BlockCutOpt Omni Solve", Reason = "Downstream: block-cut yield per realisation (Monte-Carlo over seeds).")]
public sealed class StochasticDfnComponent : FrahanComponentBase
{
    public StochasticDfnComponent()
        : base("Stochastic DFN (Baecher)", "BaecherDFN",
            "Stochastic finite-persistence discrete fracture network (Baecher disc model): Poisson centres, " +
            "Fisher-sampled poles (dispersion kappa), lognormal persistence. Intensity from spacing (P10=1/sp). " +
            "Deterministic by seed; vary the seed for a Monte-Carlo block-yield distribution. Output DFN mesh + " +
            "bench feed the BlockCutOpt packers.",
            "Frahan", "Quarry")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F1004C-ED9E-4ED9-A04C-ED9EED9E004C");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("DiscontinuitySets.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddNumberParameter("Dip", "D", "Per-set dip (deg).", GH_ParamAccess.list);
        p.AddNumberParameter("Dip dir", "Dd", "Per-set dip-direction (deg).", GH_ParamAccess.list);
        p.AddNumberParameter("Spacing", "Sp", "Per-set normal spacing (m) -> intensity P10 = 1/spacing.", GH_ParamAccess.list);
        p.AddNumberParameter("Kappa", "K", "Per-set Fisher dispersion (one value applies to all). Higher = tighter.", GH_ParamAccess.list);
        p.AddNumberParameter("Persistence", "P", "Per-set mean fracture diameter (m) (one value applies to all).", GH_ParamAccess.list);
        p.AddNumberParameter("Persistence CV", "Cv", "Lognormal coefficient of variation of persistence.", GH_ParamAccess.item, 1.0);
        p.AddBoxParameter("Bench", "B", "Domain box the fractures are generated in (also feed to BlockCutOpt Tested Area).", GH_ParamAccess.item);
        p.AddIntegerParameter("Seed", "S", "Realisation seed (vary for Monte-Carlo).", GH_ParamAccess.item, 1);
        p[3].Optional = true; p[4].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("DFN", "F", "Stochastic finite-disc fracture mesh. Feed to BlockCutOpt 'Fractures'.", GH_ParamAccess.item);
        p.AddBoxParameter("Tested area", "A", "The bench box, passed through to BlockCutOpt 'Tested Area'.", GH_ParamAccess.item);
        p.AddIntegerParameter("Fractures", "N", "Number of fracture discs generated.", GH_ParamAccess.item);
        p.AddNumberParameter("P32", "P32", "Fracture area per unit volume (1/m).", GH_ParamAccess.item);
        p.AddTextParameter("Report", "Re", "Per-set disc counts + intensity + notes.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var dip = new List<double>(); var dipdir = new List<double>(); var spacing = new List<double>();
        var kappa = new List<double>(); var persist = new List<double>();
        if (!da.GetDataList(0, dip) || !da.GetDataList(1, dipdir) || !da.GetDataList(2, spacing))
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Provide Dip, Dip dir and Spacing."); return; }
        da.GetDataList(3, kappa); da.GetDataList(4, persist);
        double cv = 1.0; da.GetData(5, ref cv);
        var box = Box.Unset;
        if (!da.GetData(6, ref box) || !box.IsValid)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Provide a valid Bench box."); return; }
        int seed = 1; da.GetData(7, ref seed);

        int n = Math.Min(dip.Count, Math.Min(dipdir.Count, spacing.Count));
        double Kof(int i) => kappa.Count == n ? kappa[i] : (kappa.Count >= 1 ? kappa[0] : 12.0);
        double Pof(int i) => persist.Count == n ? persist[i] : (persist.Count >= 1 ? persist[0] : Math.Max(0.1, 2.0 * spacing[i]));

        var sets = new List<BaecherSet>(n);
        var notes = new List<string>();
        for (int i = 0; i < n; i++)
        {
            if (!(spacing[i] > 0)) { notes.Add($"set {i + 1}: spacing<=0 skipped."); continue; }
            double dr = dip[i] * Math.PI / 180.0, ar = dipdir[i] * Math.PI / 180.0;
            double nx = Math.Sin(dr) * Math.Sin(ar), ny = Math.Sin(dr) * Math.Cos(ar), nz = Math.Cos(dr);
            try { sets.Add(new BaecherSet(nx, ny, nz, Kof(i), spacing[i], Pof(i), Math.Max(0.0, cv))); }
            catch (Exception ex) { notes.Add($"set {i + 1}: {ex.Message}; skipped."); }
        }
        if (sets.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No usable sets."); return; }

        var bbox = GhBlockCutOptInterop.BoxToBbox(box);
        BaecherDfnResult r;
        try { r = BaecherDfnGenerator.Generate(sets, bbox, seed); }
        catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "DFN failed: " + ex.Message); return; }

        var mesh = GhBlockCutOptInterop.PlyToRhinoMesh(r.Mesh);
        da.SetData(0, mesh);
        da.SetData(1, box);
        da.SetData(2, r.FractureCount);
        da.SetData(3, r.P32);
        da.SetData(4, BuildReport(sets, r, seed, cv, notes));
        if (notes.Count > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{notes.Count} note(s); see Report.");
    }

    private static string BuildReport(List<BaecherSet> sets, BaecherDfnResult r, int seed, double cv, List<string> notes)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Baecher stochastic DFN, seed {seed}, persistence CV {cv:G3}:");
        for (int i = 0; i < sets.Count; i++)
            sb.AppendLine($"  set {i + 1}: kappa {sets[i].Kappa:G3}, spacing {sets[i].Spacing:G3} m, persistence {sets[i].MeanDiameter:G3} m -> {(r.PerSetCount != null && i < r.PerSetCount.Count ? r.PerSetCount[i] : 0)} discs");
        sb.AppendLine($"Total fractures: {r.FractureCount},  P32 = {r.P32:G3} /m");
        sb.AppendLine("Vary Seed and re-run for a Monte-Carlo block-yield distribution.");
        foreach (var note in notes) sb.AppendLine("  ! " + note);
        return sb.ToString().TrimEnd();
    }
}
