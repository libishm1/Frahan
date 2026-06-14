#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Quarry;
using Frahan.Masonry.Quarry.BlockCutOpt;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Quarry;

// =============================================================================
// JointSetsToDfnComponent (D5F1004B, Frahan > Quarry)
//
// THE BRIDGE: turn discovered/measured joint sets (dip / dip-direction / spacing,
// e.g. from "Discontinuity Sets (Async)" D5F10048 or "Discontinuity Ingest"
// D5F10049) into a discrete fracture network (DFN) mesh clipped to a bench box,
// ready to feed "Block Cut Optimiser" (F2D0BC02). This closes the loop
//   scan -> joint sets -> DFN -> block-cut yield
// on one canvas.
//
// IMPORTANT (decoupled from scan continuity): this consumes only the joint-set
// STATISTICS (orientation + spacing). It does NOT use the scan mesh geometry, so
// a patchy / holey / noisy scan is fine as long as each set was sampled enough to
// measure dip/dipdir/spacing. The blocks are cut from the clean bench Box you
// supply, using a stochastic DFN that matches those statistics. (Carving the
// ACTUAL scanned solid into blocks is a different workflow that needs a watertight
// mesh -- see example 15.)
//
// References: Priest (1993) DFN; Latham et al. (2006) block-size from joint sets;
// Palmstrom Vb as the first-order yield bound the cut optimiser then realises.
// =============================================================================

[Algorithm("Joint-set DFN", "Priest 1993 discrete fracture network from joint-set statistics",
    Note = "Planes per set spaced along the set normal, clipped to the bench AABB; deterministic by seed.")]
[RelatedComponent("Frahan > Quarry > Discontinuity Sets (Async)", Reason = "Upstream: discovers dip/dipdir/spacing from a scan.")]
[RelatedComponent("Frahan > Quarry > Discontinuity Ingest", Reason = "Upstream: ingests measured dip/dipdir orientations.")]
[RelatedComponent("Frahan > Quarry > BlockCutOpt Omni Solve", Reason = "Downstream (evolved): sub-division + coarse-to-fine + Pareto recovery on this DFN.")]
[RelatedComponent("Frahan > Quarry > Fracture Block Pack", Reason = "Downstream: wire-saw staged guillotine packing against this DFN.")]
public sealed class JointSetsToDfnComponent : FrahanComponentBase
{
    public JointSetsToDfnComponent()
        : base("Joint Sets to DFN", "Sets2DFN",
            "Bridge joint sets (dip / dip-direction / spacing) into a discrete fracture network mesh clipped " +
            "to a bench box, ready for the Block Cut Optimiser. Uses only the joint-set statistics, not the " +
            "scan mesh, so an incomplete scan still works. Deterministic by seed.",
            "Frahan", "Quarry")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F1004B-ED9E-4ED9-A04B-ED9EED9E004B");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("DiscontinuitySets.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddNumberParameter("Dip", "D", "Per-set dip (deg, 0..90).", GH_ParamAccess.list);
        p.AddNumberParameter("Dip dir", "Dd", "Per-set dip-direction (deg, 0..360).", GH_ParamAccess.list);
        p.AddNumberParameter("Spacing", "Sp", "Per-set mean normal spacing (cloud units). Sets with spacing<=0 are skipped.", GH_ParamAccess.list);
        p.AddNumberParameter("Spacing scale", "Ss", "Multiplies spacing into the bench's units (e.g. 100 to take a cm-scale detail scan to bench metres). Default 1.", GH_ParamAccess.item, 1.0);
        p.AddBoxParameter("Bench", "B", "Bench / blank bounding box the DFN is clipped to (and that you also feed to the Block Cut Optimiser as Tested Area).", GH_ParamAccess.item);
        p.AddNumberParameter("Scatter", "Sc", "Per-set orientation scatter (deg, Fisher dispersion). One value applies to all sets. Default 0 = planar.", GH_ParamAccess.list);
        p.AddIntegerParameter("Seed", "S", "Random seed (deterministic given the same inputs).", GH_ParamAccess.item, 1);
        p.AddBooleanParameter("Exp spacing", "E", "Negative-exponential spacing (Priest) instead of constant.", GH_ParamAccess.item, false);
        p[5].Optional = true; // Scatter
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("DFN", "F", "Fracture-network mesh (triangulated planes clipped to the bench). Feed to Block Cut Optimiser 'Fractures'.", GH_ParamAccess.item);
        p.AddBoxParameter("Tested area", "A", "The bench box, passed through. Feed to Block Cut Optimiser 'Tested Area'.", GH_ParamAccess.item);
        p.AddIntegerParameter("Fractures", "N", "Number of fracture planes clipped into the bench.", GH_ParamAccess.item);
        p.AddIntegerParameter("Sets used", "Su", "Joint sets actually used (spacing>0, valid orientation).", GH_ParamAccess.item);
        p.AddTextParameter("Report", "Re", "Per-set summary + DFN stats + any skipped sets.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var dip = new List<double>(); var dipdir = new List<double>(); var spacing = new List<double>();
        var scatter = new List<double>();
        if (!da.GetDataList(0, dip) || !da.GetDataList(1, dipdir) || !da.GetDataList(2, spacing))
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Provide Dip, Dip dir and Spacing lists."); return; }
        double sScale = 1.0; da.GetData(3, ref sScale); if (sScale <= 0) sScale = 1.0;
        var box = Box.Unset;
        if (!da.GetData(4, ref box) || !box.IsValid)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Provide a valid Bench box."); return; }
        da.GetDataList(5, scatter);
        int seed = 1; da.GetData(6, ref seed);
        bool exp = false; da.GetData(7, ref exp);

        int n = Math.Min(dip.Count, Math.Min(dipdir.Count, spacing.Count));
        var jointSets = new List<JointSet>(n);
        var notes = new List<string>();
        for (int i = 0; i < n; i++)
        {
            double dd = ((dipdir[i] % 360.0) + 360.0) % 360.0;
            double dp = Math.Max(0.0, Math.Min(90.0, dip[i]));
            double sp = spacing[i] * sScale;
            double sc = scatter.Count == n ? scatter[i] : (scatter.Count == 1 ? scatter[0] : 0.0);
            sc = Math.Max(0.0, Math.Min(89.9, sc));
            if (!(sp > 0.0)) { notes.Add($"set {i + 1}: spacing {sp:G3} <= 0, skipped (single-plane / unspaced)."); continue; }
            try { jointSets.Add(new JointSet(dd, dp, sp, sc, exp)); }
            catch (Exception ex) { notes.Add($"set {i + 1}: {ex.Message}; skipped."); }
        }

        if (jointSets.Count == 0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No usable joint sets (need spacing>0)."); return; }

        var bbox = GhBlockCutOptInterop.BoxToBbox(box);
        IReadOnlyList<FracturePlane> planes;
        PlyMesh ply;
        try
        {
            planes = JointSetDfnGenerator.Generate(jointSets, bbox, seed);
            ply = JointSetDfnPlyEmitter.Emit(planes, bbox);
        }
        catch (Exception ex)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "DFN generation failed: " + ex.Message); return; }

        var mesh = GhBlockCutOptInterop.PlyToRhinoMesh(ply);

        da.SetData(0, mesh);
        da.SetData(1, box);
        da.SetData(2, planes.Count);
        da.SetData(3, jointSets.Count);
        da.SetData(4, BuildReport(jointSets, planes.Count, mesh, notes, seed, exp));

        if (notes.Count > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{notes.Count} set(s) skipped; see Report.");
    }

    private static string BuildReport(List<JointSet> sets, int planeCount, Mesh mesh, List<string> notes, int seed, bool exp)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"DFN from {sets.Count} joint set(s), seed {seed}, {(exp ? "exponential" : "constant")} spacing:");
        for (int i = 0; i < sets.Count; i++)
            sb.AppendLine($"  set {i + 1}: dipdir {sets[i].DipDirectionDeg:F1} / dip {sets[i].DipDeg:F1}, spacing {sets[i].MeanSpacing:G3}, scatter {sets[i].ScatterDeg:F1}");
        sb.AppendLine($"Fracture planes clipped to bench: {planeCount}");
        sb.AppendLine($"DFN mesh: {mesh.Vertices.Count} verts, {mesh.Faces.Count} triangles");
        sb.AppendLine("Feed DFN -> Block Cut Optimiser 'Fractures', Tested area -> 'Tested Area'.");
        foreach (var note in notes) sb.AppendLine("  ! " + note);
        return sb.ToString().TrimEnd();
    }
}
