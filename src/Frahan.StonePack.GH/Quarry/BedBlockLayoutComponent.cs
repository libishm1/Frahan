#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;
using Frahan.Masonry.Quarry.MarbleLayout;

namespace Frahan.GH.Quarry;

// =============================================================================
// BedBlockLayoutComponent -- the marble-quarry HERO end stage: turn a fractured
// bench (bench box + GPR/kriged bed surfaces) into a cost/volume-optimised
// dimension-block layout from a marketable block CATALOGUE, cut along the beds.
//
// Thin facade over the Rhino-free Core primitive Frahan.Masonry.Quarry.
// MarbleLayout.CatalogueBlockLayout (the guillotine catalogue optimiser). This
// component only does the bench->layers geometry + Box marshalling; the layout
// maths is the Core primitive, reusable headless. Reproduces the example-08
// Botticino study: oblique (bed-following) recovers the full bed spacing; a
// single Volume Weight sweeps cost -> balanced -> volume.
//
// Frahan > Quarry > GPR/DFN beds -> dimension-block cost/volume layout.
// =============================================================================

/// <summary>
/// Frahan &gt; Quarry &gt; Bed Block Layout (cost/volume). Lay marketable
/// dimension blocks (catalogue A/B/C/D) into the intact layers between fracture
/// beds, cut along the beds (oblique) or at the dip-safe envelope (flat), under a
/// cost-to-volume objective. Facade over Core CatalogueBlockLayout.
/// </summary>
[RelatedComponent("Frahan > Quarry > GPR Fracture Surfaces 3D", Reason = "Source of the bed surfaces this lays blocks between.")]
[RelatedComponent("Frahan > Quarry > Fracture Block Pack", Reason = "Uniform-block guillotine packer; this one is the priced multi-size CATALOGUE layout.")]
[Algorithm("Cost/volume dimension-block catalogue layout, per bed-bounded layer, exact guillotine tiling",
    "Elkarmoty et al. 2020 (block recovery on bedded stone); guillotine cutting stock (Gilmore & Gomory 1965)",
    Note = "maximise sum vol*(price+W) - cut; W sweeps max-cost (W=0) -> balanced -> max-volume. Oblique = full bed spacing; flat = dip-safe envelope.")]
public sealed class BedBlockLayoutComponent : FrahanComponentBase
{
    public BedBlockLayoutComponent()
        : base("Bed Block Layout", "BedBlocks",
            "Lay marketable dimension blocks (catalogue) into the intact layers between fracture beds, " +
            "cut along the beds. Inputs the bench box + the kriged bed surfaces; builds one layer per " +
            "inter-bed gap (Oblique = full bed spacing / bed-following; off = flat dip-safe envelope) and " +
            "tiles each layer with the block catalogue under a cost-to-volume objective. Volume Weight W " +
            "sweeps the plan: 0 = max cost (fewer big high-value blocks), ~500 = balanced, large = max " +
            "volume (fill). Outputs the blocks + net value + recovered volume. Reproduces the example-08 " +
            "Botticino marble study. Facade over Core CatalogueBlockLayout.",
            "Frahan", "Quarry")
    { }

    public override Guid ComponentGuid => new Guid("A7E0B0F5-0C0F-4A16-9E3D-0FACE0FACE06");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("BlockPackTree.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddBoxParameter("Bench", "A", "Bench bounding box (m). The XY footprint + Z range to lay blocks in.",
            GH_ParamAccess.item);
        p.AddMeshParameter("Bed Surfaces", "F",
            "Fracture bed surfaces (from GPR Fracture Surfaces 3D). One layer is built per gap between " +
            "consecutive beds (and bench top/bottom).", GH_ParamAccess.list);
        p.AddNumberParameter("Volume Weight", "W",
            "Cost-to-volume objective weight ($/m3 added to each block's price). 0 = max COST (fewer big " +
            "high-value blocks, lower volume, higher net); ~500 = balanced; large (e.g. 3000) = max VOLUME " +
            "(fill the layers). Default 0.", GH_ParamAccess.item, 0.0);
        p.AddBooleanParameter("Oblique", "Ob",
            "TRUE (default) = bed-following: each layer is as thick as the full bed spacing (recovers the " +
            "dip wedge; needs georeferenced sloped cuts to execute). FALSE = flat dip-safe envelope (top = " +
            "deepest point of the upper bed, bottom = shallowest of the lower bed; fabricable on any gangsaw " +
            "today, but the wedges are waste).", GH_ParamAccess.item, true);
        p.AddNumberParameter("Cut Cost", "Cut", "Diamond-saw cost (USD/m2 of sawn block face). Default 200.",
            GH_ParamAccess.item, 200.0);
        p.AddNumberParameter("Keep-out", "K",
            "Inward margin (m) kept from each bed (the GPR position keep-out). Default 0.05.",
            GH_ParamAccess.item, 0.05);
        p.AddNumberParameter("Catalogue", "Cat",
            "OPTIONAL block catalogue as flat triples [footLength, footWidth, pricePerM3, ...]. Omit for the " +
            "default A 3.0x1.5 $2200 / B 2.0x1.5 $1800 / C 1.5x1.0 $1400 / D 1.0x1.0 $1100.",
            GH_ParamAccess.list);
        p[p.ParamCount - 1].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddBoxParameter("Blocks", "B", "Placed dimension blocks (Boxes), bed-bounded.", GH_ParamAccess.list);
        p.AddTextParameter("Class", "C", "Catalogue class (A/B/C/D...) of each block, aligned to Blocks.", GH_ParamAccess.list);
        p.AddNumberParameter("Volume", "V", "Total recovered block volume (m3).", GH_ParamAccess.item);
        p.AddNumberParameter("Net Value", "Net", "Net value (USD) = block sale price - diamond-saw cut cost.", GH_ParamAccess.item);
        p.AddIntegerParameter("Count", "N", "Number of blocks placed.", GH_ParamAccess.item);
        p.AddTextParameter("Report", "Rpt", "Mix + economics + per-layer summary.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var bench = Box.Unset;
        if (!da.GetData(0, ref bench) || !bench.IsValid)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No valid Bench box."); return; }
        var beds = new List<Mesh>();
        da.GetDataList(1, beds);
        beds = beds.Where(m => m != null && m.Vertices.Count > 0).ToList();
        double W = 0, cut = 200, keepout = 0.05; bool oblique = true;
        da.GetData(2, ref W); da.GetData(3, ref oblique); da.GetData(4, ref cut); da.GetData(5, ref keepout);
        var catNums = new List<double>(); da.GetDataList(6, catNums);

        // catalogue (default A/B/C/D, or override triples)
        var catalogue = new List<CatalogueBlock>();
        if (catNums != null && catNums.Count >= 3)
            for (int i = 0; i + 2 < catNums.Count; i += 3)
                catalogue.Add(new CatalogueBlock(((char)('A' + i / 3)).ToString(), catNums[i], catNums[i + 1], catNums[i + 2]));
        else
        {
            catalogue.Add(new CatalogueBlock("A", 3.0, 1.5, 2200));
            catalogue.Add(new CatalogueBlock("B", 2.0, 1.5, 1800));
            catalogue.Add(new CatalogueBlock("C", 1.5, 1.0, 1400));
            catalogue.Add(new CatalogueBlock("D", 1.0, 1.0, 1100));
        }

        // bench footprint + Z range (world-axis box assumed; use the box's plane-aligned bbox)
        var bb = bench.BoundingBox;
        double x0 = bb.Min.X, y0 = bb.Min.Y, Wx = bb.Max.X - bb.Min.X, Wy = bb.Max.Y - bb.Min.Y;
        double zTopBench = bb.Max.Z, zBotBench = bb.Min.Z;

        // per-bed mean / shallowest(maxZ) / deepest(minZ), sorted shallow (z high) -> deep (z low)
        var bedStats = beds.Select(m => {
            var b = m.GetBoundingBox(true);
            double mean = 0; int nv = m.Vertices.Count;
            for (int i = 0; i < nv; i++) mean += m.Vertices[i].Z;
            mean = nv > 0 ? mean / nv : (b.Min.Z + b.Max.Z) / 2;
            return new { Mean = mean, ShallowZ = b.Max.Z, DeepZ = b.Min.Z };
        }).Where(s => s.Mean < zTopBench - 1e-6 && s.Mean > zBotBench + 1e-6)
          .OrderByDescending(s => s.Mean).ToList();

        // build layers (zTop, thickness)
        var layers = new List<(double zTop, double thicknessM)>();
        var rpt = new System.Text.StringBuilder();
        rpt.AppendLine($"bench {Wx:0.##} x {Wy:0.##} x {(zTopBench - zBotBench):0.##} m | beds {bedStats.Count} | " +
                       $"{(oblique ? "OBLIQUE (bed-following)" : "FLAT (dip-safe)")} | W={W:0} cut=${cut:0}/m2 keepout={keepout:0.###}m");
        if (oblique)
        {
            double prev = zTopBench;
            foreach (var s in bedStats) { layers.Add((prev - keepout, (prev - keepout) - (s.Mean + keepout))); prev = s.Mean; }
            layers.Add((prev - keepout, (prev - keepout) - zBotBench));
        }
        else // flat dip-safe: top = deepest of upper bed, bottom = shallowest of lower bed
        {
            double prevDeep = zTopBench;
            foreach (var s in bedStats) { layers.Add((prevDeep - keepout, (prevDeep - keepout) - (s.ShallowZ + keepout))); prevDeep = s.DeepZ; }
            layers.Add((prevDeep - keepout, (prevDeep - keepout) - zBotBench));
        }

        var opt = new CatalogueLayoutOptions { VolumeWeightW = W, CutUsdPerM2 = cut, KerfM = 0.0 };
        var result = CatalogueBlockLayout.Pack(layers, x0, y0, Wx, Wy, catalogue, opt);

        var boxes = new List<Box>(result.Blocks.Count);
        var classes = new List<string>(result.Blocks.Count);
        foreach (var pb in result.Blocks)
        {
            var b = new Box(Plane.WorldXY, new Interval(pb.X, pb.X + pb.Lx),
                new Interval(pb.Y, pb.Y + pb.Ly), new Interval(pb.Z, pb.Z + pb.Lz));
            boxes.Add(b); classes.Add(pb.ClassName);
        }
        string mix = string.Join(", ", result.Mix.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}"));
        rpt.AppendLine($"-> {result.BlockCount} blocks, {result.TotalVolumeM3:0.##} m3, " +
                       $"gross ${result.GrossUsd:0}, cut ${result.CutUsd:0}, NET ${result.NetUsd:0} | mix {{{mix}}}");

        da.SetDataList(0, boxes);
        da.SetDataList(1, classes);
        da.SetData(2, result.TotalVolumeM3);
        da.SetData(3, result.NetUsd);
        da.SetData(4, result.BlockCount);
        da.SetData(5, rpt.ToString().TrimEnd());
    }
}
