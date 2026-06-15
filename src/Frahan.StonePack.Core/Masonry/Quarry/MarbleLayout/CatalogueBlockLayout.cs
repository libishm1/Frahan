#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Frahan.Masonry.Quarry.MarbleLayout;

// =============================================================================
// CatalogueBlockLayout -- cost/volume dimension-block layout from a fixed block
// CATALOGUE, layer by layer, the way a marble quarry actually plans a bedded
// bench (example 08, the real Botticino-marble study).
//
// Each fracture-bounded LAYER (thickness = bed spacing) is tiled with blocks
// from a small catalogue of marketable footprints (A 3.0x1.5, B 2.0x1.5, ...),
// each block as TALL as the layer (cut along the beds -> "oblique" / bed-
// following). A single weight W sweeps the objective from COST to VOLUME:
//
//     maximise   sum_b  vol_b * (price_b + W)
//
//   W = 0      -> maximise sale value: high price-density tiling (the big A
//                 blocks where they fit, C to fill) = the max-COST plan.
//   W -> large -> maximise sum(vol) = layer thickness * covered AREA: the most
//                 area-efficient tiling regardless of price = the max-VOLUME plan.
//   W ~ 500    -> the balanced point between the two.
//
// The per-layer tiling is an exact recursive GUILLOTINE search (place a catalogue
// block flush in a corner, split the remainder with one full-span cut into a
// right and a top sub-region, recurse) memoised on the quantised (w, l) region,
// so it returns the objective-optimal guillotine-cuttable tiling. Every block is
// freed by straight full-span saw passes -> manufacturable. Rhino-free.
//
// NET value = sum(vol*price) - cutUsdPerM2 * sawn-face area (the bed-parallel
// parting face + the vertical rip faces of each block). This is a primitive:
// the GH front ends (Bench Bed Layers + Catalogue Block Layout) compose over it.
// =============================================================================

/// <summary>One marketable block size: a footprint (length x width, m) at a price (USD/m3).</summary>
public sealed class CatalogueBlock
{
    public CatalogueBlock(string name, double footLengthM, double footWidthM, double pricePerM3)
    {
        Name = name ?? "?";
        FootLengthM = Math.Abs(footLengthM);
        FootWidthM = Math.Abs(footWidthM);
        PricePerM3 = Math.Max(0, pricePerM3);
    }
    public string Name { get; }
    public double FootLengthM { get; }
    public double FootWidthM { get; }
    public double PricePerM3 { get; }
    public double FootAreaM2 => FootLengthM * FootWidthM;
}

/// <summary>A placed block: min corner (X,Y,Z) and extents (Lx,Ly,Lz), with its class + economics.</summary>
public sealed class PlacedCatalogueBlock
{
    public double X, Y, Z, Lx, Ly, Lz;
    public string ClassName;
    public int LayerIndex;
    public double VolumeM3 => Lx * Ly * Lz;
}

public sealed class CatalogueLayoutOptions
{
    public double VolumeWeightW = 0.0;     // 0 = max cost; ~500 = balanced; large = max volume
    public double CutUsdPerM2 = 200.0;
    public double KerfM = 0.0;             // saw gap between blocks (m)
    public double GridM = 0.1;             // memo quantisation of the region search (m)
    public double MinLayerThicknessM = 0.15; // layers thinner than this yield no blocks
}

public sealed class CatalogueLayoutResult
{
    public List<PlacedCatalogueBlock> Blocks = new List<PlacedCatalogueBlock>();
    public double TotalVolumeM3;
    public double GrossUsd;
    public double CutUsd;
    public double NetUsd;
    public Dictionary<string, int> Mix = new Dictionary<string, int>();
    public int BlockCount => Blocks.Count;
}

public static class CatalogueBlockLayout
{
    /// <summary>One layer: a rectangular footprint [x0..x0+Wx] x [y0..y0+Wy], blocks of height
    /// <paramref name="thicknessM"/> sitting with their top at <paramref name="zTop"/>.</summary>
    public static CatalogueLayoutResult PackLayer(double x0, double y0, double Wx, double Wy,
        double zTop, double thicknessM, IReadOnlyList<CatalogueBlock> catalogue,
        CatalogueLayoutOptions opt, int layerIndex = 0)
    {
        var res = new CatalogueLayoutResult();
        if (catalogue == null || catalogue.Count == 0) return res;
        opt = opt ?? new CatalogueLayoutOptions();
        if (thicknessM < opt.MinLayerThicknessM || Wx <= 0 || Wy <= 0) return res;

        double g = Math.Max(1e-3, opt.GridM);
        double kerf = Math.Max(0, opt.KerfM);
        double W = opt.VolumeWeightW;
        // candidate footprints: each catalogue block in both orientations
        var cands = new List<(double w, double l, double scorePerArea, CatalogueBlock b)>();
        foreach (var b in catalogue)
        {
            double s = b.PricePerM3 + W;   // objective weight per m3 (thickness is constant in a layer)
            cands.Add((b.FootLengthM, b.FootWidthM, s, b));
            if (Math.Abs(b.FootLengthM - b.FootWidthM) > 1e-9)
                cands.Add((b.FootWidthM, b.FootLengthM, s, b));
        }

        // memoised guillotine: best objective (sum vol*(price+W)) of a region, + the chosen placements.
        var memo = new Dictionary<long, RegionSolution>();
        long Key(int wi, int li) => ((long)wi << 20) ^ li;

        RegionSolution Solve(double w, double l)
        {
            int wi = (int)Math.Round(w / g), li = (int)Math.Round(l / g);
            if (wi <= 0 || li <= 0) return RegionSolution.Empty;
            long key = Key(wi, li);
            if (memo.TryGetValue(key, out var cached)) return cached;
            // guard runaway recursion on absurd regions
            if (memo.Count > 200000) return RegionSolution.Empty;
            var best = RegionSolution.Empty;
            foreach (var c in cands)
            {
                if (c.w > w + 1e-9 || c.l > l + 1e-9) continue;
                // marginal objective = vol*(price+W) - cut cost. The full-surface cut term makes small
                // blocks (high surface/volume) net-negative at W=0 -> excluded (fewer big blocks, lower
                // volume, higher net = max COST); large W overrides it -> filled tiling (max VOLUME).
                double surf = 2.0 * (c.w * c.l + c.w * thicknessM + c.l * thicknessM);
                double blockObj = c.w * c.l * thicknessM * c.scorePerArea - opt.CutUsdPerM2 * surf;
                // full-span guillotine split of the remainder: right (w-c.w) x l, top c.w x (l-c.l)
                var right = Solve(w - c.w - kerf, l);
                var top = Solve(c.w, l - c.l - kerf);
                double obj = blockObj + right.Objective + top.Objective;
                if (obj > best.Objective + 1e-9)
                    best = new RegionSolution(obj, c, right, top);
            }
            memo[key] = best;
            return best;
        }

        var sol = Solve(Wx, Wy);
        // walk the chosen tree, emitting placements at absolute coords
        void Emit(RegionSolution s, double ox, double oy)
        {
            if (s == null || s.Block.b == null) return;
            var c = s.Block;
            res.Blocks.Add(new PlacedCatalogueBlock
            {
                X = ox, Y = oy, Z = zTop - thicknessM,
                Lx = c.w, Ly = c.l, Lz = thicknessM,
                ClassName = c.b.Name, LayerIndex = layerIndex
            });
            Emit(s.Right, ox + c.w + kerf, oy);          // right sub-region
            Emit(s.Top, ox, oy + c.l + kerf);            // top sub-region
        }
        Emit(sol, x0, y0);
        Finalise(res, catalogue, opt);
        return res;
    }

    /// <summary>All layers (one (zTop, thickness) per inter-bed layer) over a common footprint.</summary>
    public static CatalogueLayoutResult Pack(IReadOnlyList<(double zTop, double thicknessM)> layers,
        double x0, double y0, double Wx, double Wy, IReadOnlyList<CatalogueBlock> catalogue,
        CatalogueLayoutOptions opt)
    {
        var res = new CatalogueLayoutResult();
        if (layers == null) return res;
        opt = opt ?? new CatalogueLayoutOptions();
        for (int i = 0; i < layers.Count; i++)
        {
            var lr = PackLayer(x0, y0, Wx, Wy, layers[i].zTop, layers[i].thicknessM, catalogue, opt, i);
            res.Blocks.AddRange(lr.Blocks);
        }
        Finalise(res, catalogue, opt);
        return res;
    }

    private static void Finalise(CatalogueLayoutResult res, IReadOnlyList<CatalogueBlock> catalogue,
        CatalogueLayoutOptions opt)
    {
        var price = catalogue.ToDictionary(b => b.Name, b => b.PricePerM3);
        res.TotalVolumeM3 = res.Blocks.Sum(b => b.VolumeM3);
        res.GrossUsd = res.Blocks.Sum(b => b.VolumeM3 * (price.TryGetValue(b.ClassName, out var p) ? p : 0));
        // sawn faces = full block surface (the bed-parallel parting faces + the vertical rip faces)
        double cutArea = res.Blocks.Sum(b => 2.0 * (b.Lx * b.Ly + b.Lx * b.Lz + b.Ly * b.Lz));
        res.CutUsd = opt.CutUsdPerM2 * cutArea;
        res.NetUsd = res.GrossUsd - res.CutUsd;
        res.Mix = res.Blocks.GroupBy(b => b.ClassName).ToDictionary(gp => gp.Key, gp => gp.Count());
    }

    private sealed class RegionSolution
    {
        public static readonly RegionSolution Empty = new RegionSolution(0, default, null, null);
        public RegionSolution(double objective, (double w, double l, double scorePerArea, CatalogueBlock b) block,
            RegionSolution right, RegionSolution top)
        { Objective = objective; Block = block; Right = right; Top = top; }
        public double Objective;
        public (double w, double l, double scorePerArea, CatalogueBlock b) Block;
        public RegionSolution Right, Top;
    }
}
