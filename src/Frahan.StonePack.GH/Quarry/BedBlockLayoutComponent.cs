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
            "Frahan", "Block Cutting")
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
        p.AddMeshParameter("Blocks", "B",
            "Placed dimension blocks. With Oblique on these are bed-bounded HEXAHEDRA: each block's top " +
            "face rides the upper bed and its bottom face rides the lower bed (sheared to the dip), so no " +
            "block crosses a fracture and the layout follows the real bed dip. Oblique off = flat boxes.",
            GH_ParamAccess.list);
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

        // --- sample each bed onto a common grid -> single-valued height field (shallow -> deep) ---
        int gr = 30;
        int Nx = Wx >= Wy ? gr : Math.Max(4, (int)Math.Round(gr * Wx / Wy));
        int Ny = Wy >= Wx ? gr : Math.Max(4, (int)Math.Round(gr * Wy / Wx));
        double SampleBed(Mesh m, double gx, double gy)
        {
            var ray = new Ray3d(new Point3d(gx, gy, zTopBench + 2.0), new Vector3d(0, 0, -1));
            double t = Rhino.Geometry.Intersect.Intersection.MeshRay(m, ray);
            if (t >= 0) return ray.PointAt(t).Z;
            var mp = m.ClosestMeshPoint(new Point3d(gx, gy, (zTopBench + zBotBench) * 0.5), 0.0);
            return mp != null ? m.PointAt(mp).Z : (zTopBench + zBotBench) * 0.5;
        }
        double[,] HField(Mesh m)
        {
            var h = new double[Nx, Ny];
            for (int i = 0; i < Nx; i++) for (int j = 0; j < Ny; j++)
                h[i, j] = SampleBed(m, x0 + Wx * i / (Nx - 1), y0 + Wy * j / (Ny - 1));
            return h;
        }
        double FieldMean(double[,] h) { double s = 0; foreach (var v in h) s += v; return s / (Nx * Ny); }
        // bilinear sample of a height field at world (gx, gy)
        double SampleField(double[,] h, double gx, double gy)
        {
            double fx = (gx - x0) / Wx * (Nx - 1), fy = (gy - y0) / Wy * (Ny - 1);
            int i0 = Math.Max(0, Math.Min(Nx - 2, (int)Math.Floor(fx)));
            int j0 = Math.Max(0, Math.Min(Ny - 2, (int)Math.Floor(fy)));
            double tx = Math.Max(0, Math.Min(1, fx - i0)), ty = Math.Max(0, Math.Min(1, fy - j0));
            return (1 - tx) * (1 - ty) * h[i0, j0] + tx * (1 - ty) * h[i0 + 1, j0]
                 + (1 - tx) * ty * h[i0, j0 + 1] + tx * ty * h[i0 + 1, j0 + 1];
        }

        var fields = beds.Select(m => { var h = HField(m); return new { H = h, Mean = FieldMean(h) }; })
                         .Where(f => f.Mean < zTopBench - 1e-6 && f.Mean > zBotBench + 1e-6)
                         .OrderByDescending(f => f.Mean).ToList();

        // boundary fields: bench top (flat), each bed, bench bottom (flat). Layer k = bounds[k]..bounds[k+1].
        var topFlat = new double[Nx, Ny]; var botFlat = new double[Nx, Ny];
        for (int i = 0; i < Nx; i++) for (int j = 0; j < Ny; j++) { topFlat[i, j] = zTopBench; botFlat[i, j] = zBotBench; }
        var bounds = new List<double[,]> { topFlat };
        foreach (var f in fields) bounds.Add(f.H);
        bounds.Add(botFlat);

        var rpt = new System.Text.StringBuilder();
        rpt.AppendLine($"bench {Wx:0.##} x {Wy:0.##} x {(zTopBench - zBotBench):0.##} m | beds {fields.Count} | " +
                       $"{(oblique ? "OBLIQUE (bed-following hexahedra, dip-tilted)" : "FLAT (axis-aligned)")} | " +
                       $"W={W:0} cut=${cut:0}/m2 keepout={keepout:0.###}m");

        // representative flat layers fed to the catalogue packer (the tiling decision); the blocks are
        // then SHEARED to the actual bed fields below.
        var layers = new List<(double zTop, double thicknessM)>();
        for (int k = 0; k < bounds.Count - 1; k++)
        {
            double zt = FieldMean(bounds[k]) - (k > 0 ? keepout : 0);
            double zbm = FieldMean(bounds[k + 1]) + (k < bounds.Count - 2 ? keepout : 0);
            layers.Add((zt, zt - zbm));
        }

        var opt = new CatalogueLayoutOptions { VolumeWeightW = W, CutUsdPerM2 = cut, KerfM = 0.0 };
        var result = CatalogueBlockLayout.Pack(layers, x0, y0, Wx, Wy, catalogue, opt);

        // build each block: OBLIQUE -> a sheared hexahedron whose top rides the upper bed and bottom the
        // lower bed (sampled at the 4 footprint corners), so it tilts with the dip and never crosses a bed.
        // FLAT -> a flat box. Economics recomputed on the ACTUAL recovered volume (oblique recovers the wedge).
        var meshes = new List<Mesh>(result.Blocks.Count);
        var classes = new List<string>(result.Blocks.Count);
        var price = catalogue.ToDictionary(b => b.Name, b => b.PricePerM3);
        double totVol = 0, gross = 0, cutArea = 0;
        foreach (var pb in result.Blocks)
        {
            int li = Math.Max(0, Math.Min(bounds.Count - 2, pb.LayerIndex));
            var up = bounds[li]; var lo = bounds[li + 1];
            double upKO = li > 0 ? keepout : 0, loKO = li < bounds.Count - 2 ? keepout : 0;
            double[] cx = { pb.X, pb.X + pb.Lx, pb.X + pb.Lx, pb.X };
            double[] cy = { pb.Y, pb.Y, pb.Y + pb.Ly, pb.Y + pb.Ly };
            var top = new double[4]; var bot = new double[4];
            for (int c = 0; c < 4; c++)
            {
                if (oblique)
                {
                    double zt = SampleField(up, cx[c], cy[c]) - upKO;
                    double zb = SampleField(lo, cx[c], cy[c]) + loKO;
                    if (zt < zb + 0.03) zt = zb + 0.03;
                    top[c] = zt; bot[c] = zb;
                }
                else { top[c] = pb.Z + pb.Lz; bot[c] = pb.Z; }
            }
            meshes.Add(Hexahedron(cx, cy, top, bot));
            classes.Add(pb.ClassName);
            double meanThk = ((top[0] - bot[0]) + (top[1] - bot[1]) + (top[2] - bot[2]) + (top[3] - bot[3])) / 4.0;
            double vol = pb.Lx * pb.Ly * meanThk;
            totVol += vol; gross += vol * (price.TryGetValue(pb.ClassName, out var pr) ? pr : 0);
            cutArea += 2.0 * (pb.Lx * pb.Ly + (pb.Lx + pb.Ly) * meanThk);
        }
        double cutUsd = cut * cutArea, net = gross - cutUsd;
        string mix = string.Join(", ", result.Mix.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}"));
        rpt.AppendLine($"-> {result.BlockCount} blocks, {totVol:0.##} m3, gross ${gross:0}, cut ${cutUsd:0}, " +
                       $"NET ${net:0} | mix {{{mix}}}");

        da.SetDataList(0, meshes);
        da.SetDataList(1, classes);
        da.SetData(2, totVol);
        da.SetData(3, net);
        da.SetData(4, result.BlockCount);
        da.SetData(5, rpt.ToString().TrimEnd());
    }

    // Closed sheared hexahedron from 4 footprint corners (cx,cy) with per-corner top + bottom Z.
    private static Mesh Hexahedron(double[] cx, double[] cy, double[] top, double[] bot)
    {
        var m = new Mesh();
        for (int c = 0; c < 4; c++) m.Vertices.Add(cx[c], cy[c], top[c]);   // 0..3 top (CCW)
        for (int c = 0; c < 4; c++) m.Vertices.Add(cx[c], cy[c], bot[c]);   // 4..7 bottom
        m.Faces.AddFace(0, 1, 2, 3);       // top  (+Z out)
        m.Faces.AddFace(4, 7, 6, 5);       // bottom (-Z out)
        m.Faces.AddFace(0, 4, 5, 1);       // y-min wall (-Y out)
        m.Faces.AddFace(1, 5, 6, 2);       // x-max wall (+X out)
        m.Faces.AddFace(2, 6, 7, 3);       // y-max wall (+Y out)
        m.Faces.AddFace(3, 7, 4, 0);       // x-min wall (-X out)
        m.RebuildNormals(); m.UnifyNormals(); m.Compact();
        return m;
    }
}
