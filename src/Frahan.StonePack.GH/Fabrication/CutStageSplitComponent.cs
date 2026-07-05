#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Fabrication;

// =============================================================================
// CutStageSplitComponent (D5F10057, Frahan > Fabricate)
//
// Dimension-stone production is two-staged. QUARRY: wide-spaced wire-saw passes
// free large primary (gangsaw / transport) blocks. FACTORY: the gangsaw / block
// cutter subdivides each primary block into the final product blocks or slabs.
// This component takes a bin's flat saw-pass grid (Fracture Block Pack > Saw
// passes) and splits it hierarchically into the two stages, so the quarry crew
// and the factory crew each get only the passes that belong to them.
// =============================================================================

[Algorithm("Two-stage cut allocation", "Hierarchical split of a guillotine pass grid into quarry (primary/gangsaw) and factory (secondary) passes",
    Note = "Quarry pass spacing = n x (product + kerf) with n = floor(gangsaw/product+kerf) per axis; grid boundaries always quarry. Gangsaw block sizing per industry practice (~3 x 1.9 x 1.5 m typical).")]
[RelatedComponent("Frahan > Block > Fracture Block Pack", Reason = "Its Saw passes output (one branch per bin) is the input here.")]
[RelatedComponent("Frahan > Fabricate > DXF Cut Plan", Reason = "Quarry + factory lines and Q/F labels feed its Cut lines / Cut labels inputs.")]
[RelatedComponent("Frahan > Fabricate > Block Yield", Reason = "Run it per primary block for the factory-stage yield of each gangsaw block.")]
public sealed class CutStageSplitComponent : FrahanComponentBase
{
    public CutStageSplitComponent()
        : base("Cut Stage Split", "StageSplit",
            "Split a bin's saw passes into QUARRY cuts (wide-spaced wire-saw passes that free transportable " +
            "gangsaw blocks) and FACTORY cuts (the gangsaw / block-cutter kerfs inside each primary block). " +
            "Every n-th grid pass is a quarry pass where n = floor(gangsaw dim / (product dim + kerf)); " +
            "boundary passes are always quarry. Feed both stages + labels into DXF Cut Plan.",
            "Frahan", "Fabricate")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F10057-ED9E-4ED9-A057-ED9EED9E0057");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("DxfCutPlan.png");

    private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddLineParameter("Cut lines", "Cl",
            "One bin's saw passes (e.g. Fracture Block Pack > Saw passes, one branch). X rips then Y " +
            "cross-cuts, as produced by the pack.", GH_ParamAccess.list);
        p.AddVectorParameter("Gangsaw block", "Gb", "Max primary (gangsaw / transport) block L x W x H (m).",
            GH_ParamAccess.item, new Vector3d(3.0, 1.9, 1.5));
        p.AddVectorParameter("Product size", "Pb",
            "Final product block L x W x H (m) - the size the pass grid was built for.",
            GH_ParamAccess.item, new Vector3d(0.9, 0.7, 0.4));
        p.AddNumberParameter("Kerf", "K", "Saw kerf (m), same value the pack used.", GH_ParamAccess.item, 0.03);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddLineParameter("Quarry cuts", "Qc", "Wire-saw passes made at the quarry (free the primary blocks).", GH_ParamAccess.list);
        p.AddLineParameter("Factory cuts", "Fc", "Gangsaw / block-cutter kerfs made at the factory.", GH_ParamAccess.list);
        p.AddTextParameter("Quarry labels", "Ql", "Q1..Qn, parallel to Quarry cuts.", GH_ParamAccess.list);
        p.AddTextParameter("Factory labels", "Fl", "F1..Fm, parallel to Factory cuts.", GH_ParamAccess.list);
        p.AddIntegerParameter("Primary blocks", "N", "Number of primary (gangsaw) modules the quarry passes define in plan.", GH_ParamAccess.item);
        p.AddTextParameter("Report", "Re", "Per-axis module math + counts.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var lines = new List<Line>();
        if (!da.GetDataList(0, lines) || lines.Count == 0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Provide the bin's saw-pass cut lines."); return; }

        Vector3d gangsaw = new Vector3d(3.0, 1.9, 1.5); da.GetData(1, ref gangsaw);
        Vector3d product = new Vector3d(0.9, 0.7, 0.4); da.GetData(2, ref product);
        double kerf = 0.03; da.GetData(3, ref kerf);

        if (gangsaw.X <= 0.0 || gangsaw.Y <= 0.0 || gangsaw.Z <= 0.0 ||
            product.X <= 0.0 || product.Y <= 0.0 || product.Z <= 0.0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Gangsaw block and product size must have positive L, W and H."); return; }

        // ---- classify: X-rip (runs along Y, located at x = From.X) vs Y-cut (located at y = From.Y) ----
        var xGroup = new List<PassEntry>();
        var yGroup = new List<PassEntry>();
        foreach (var line in lines)
        {
            double dx = Math.Abs(line.To.X - line.From.X);
            double dy = Math.Abs(line.To.Y - line.From.Y);
            if (dx < dy) xGroup.Add(new PassEntry(line, line.From.X));
            else yGroup.Add(new PassEntry(line, line.From.Y));
        }
        xGroup.Sort((a, b) => a.Coord.CompareTo(b.Coord));
        yGroup.Sort((a, b) => a.Coord.CompareTo(b.Coord));

        // ---- per-axis module count: n = floor(gangsaw axis / (product axis + kerf)), at least 1 ----
        int nX = Math.Max(1, (int)Math.Floor(gangsaw.X / (product.X + kerf)));
        int nY = Math.Max(1, (int)Math.Floor(gangsaw.Y / (product.Y + kerf)));

        var quarryLines = new List<Line>();
        var quarryLabels = new List<string>();
        var factoryLines = new List<Line>();
        var factoryLabels = new List<string>();
        int qCounter = 0, fCounter = 0;

        int quarryX = ClassifyGroup(xGroup, nX, quarryLines, quarryLabels, factoryLines, factoryLabels, ref qCounter, ref fCounter);
        int quarryY = ClassifyGroup(yGroup, nY, quarryLines, quarryLabels, factoryLines, factoryLabels, ref qCounter, ref fCounter);

        // ---- primary (gangsaw) modules in plan: (quarryCount-1) per axis, empty group -> factor 1 ----
        int factorX = xGroup.Count == 0 ? 1 : Math.Max(0, quarryX - 1);
        int factorY = yGroup.Count == 0 ? 1 : Math.Max(0, quarryY - 1);
        int primaryBlocks = factorX * factorY;

        // ---- report ----
        var rpt = new StringBuilder();
        rpt.AppendLine($"gangsaw block {gangsaw.X.ToString("0.###", CI)} x {gangsaw.Y.ToString("0.###", CI)} x {gangsaw.Z.ToString("0.###", CI)} m, " +
            $"product {product.X.ToString("0.###", CI)} x {product.Y.ToString("0.###", CI)} x {product.Z.ToString("0.###", CI)} m, kerf {kerf.ToString("0.###", CI)} m");
        rpt.AppendLine($"X-rip: {xGroup.Count} pass(es), n = {nX} (floor {gangsaw.X.ToString("0.###", CI)}/({product.X.ToString("0.###", CI)}+{kerf.ToString("0.###", CI)})), " +
            $"quarry {quarryX}, factory {xGroup.Count - quarryX}");
        rpt.AppendLine($"Y-cross: {yGroup.Count} pass(es), n = {nY} (floor {gangsaw.Y.ToString("0.###", CI)}/({product.Y.ToString("0.###", CI)}+{kerf.ToString("0.###", CI)})), " +
            $"quarry {quarryY}, factory {yGroup.Count - quarryY}");
        rpt.AppendLine($"primary (gangsaw) blocks in plan: {primaryBlocks}");
        rpt.AppendLine("vertical Z lifts are the fracture-bounded bins themselves - already quarry-stage wire-saw cuts.");

        da.SetDataList(0, quarryLines);
        da.SetDataList(1, factoryLines);
        da.SetDataList(2, quarryLabels);
        da.SetDataList(3, factoryLabels);
        da.SetData(4, primaryBlocks);
        da.SetData(5, rpt.ToString().TrimEnd());
    }

    // One saw pass plus its sort coordinate (x for an X-rip, y for a Y-cut).
    private readonly struct PassEntry
    {
        public readonly Line Line;
        public readonly double Coord;
        public PassEntry(Line line, double coord) { Line = line; Coord = coord; }
    }

    // Splits one sorted axis group into quarry (index % n == 0, or the last index) and factory
    // passes, appending labels/lines to the running (cross-group) output lists. Returns the
    // number of quarry passes found in this group.
    private static int ClassifyGroup(List<PassEntry> group, int n,
        List<Line> quarryLines, List<string> quarryLabels,
        List<Line> factoryLines, List<string> factoryLabels,
        ref int qCounter, ref int fCounter)
    {
        int quarryCount = 0;
        int m = group.Count;
        for (int i = 0; i < m; i++)
        {
            bool isQuarry = (i % n == 0) || (i == m - 1);
            if (isQuarry)
            {
                qCounter++;
                quarryLines.Add(group[i].Line);
                quarryLabels.Add("Q" + qCounter.ToString(CI));
                quarryCount++;
            }
            else
            {
                fCounter++;
                factoryLines.Add(group[i].Line);
                factoryLabels.Add("F" + fCounter.ToString(CI));
            }
        }
        return quarryCount;
    }
}
