#nullable disable
// =============================================================================
// FloorTileCost -- material + operation cost model for a FloorTileGridLayout
// result. Rhino-free Core. Turns the layout's tile/cut/waste counts into a
// material vs operation breakdown, a $/m^2 figure, and two sweeps:
//   * match sweep  (PerTile / Slip / Book at the current geometry -- same cuts,
//     different material grade + matching labour),
//   * size sweep   (re-pack at a ladder of tile sizes -- the waste-vs-labour
//     U-curve against this room).
//
// All rates are ILLUSTRATIVE engineering assumptions (see
// outputs/2026-06-20/floor_tiling/COST_ANALYSIS.md); override via TileRateCard.
// Money is `Currency` per the units noted on each field.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Text;

namespace Frahan.Packing.TwoD
{
    public sealed class TileRateCard
    {
        public double MaterialPerM2 = 90.0;   // tile material, money/m^2 of full tile
        public double OveragePct = 0.10;      // breakage/repair buffer on purchased area
        public double CutPerTile = 6.0;       // wet-saw cost per cut (border) tile
        public int CornerExtraCuts = 4;       // corner tiles are 2-cut: +4 per closed loop (room + each hole)
        public double SetOutStack = 150.0;    // setting-out (fixed): stack/corner/centred
        public double SetOutMatched = 300.0;  // setting-out (fixed): slip/book (sequenced layout)
        public double LayPerM2 = 40.0;        // laying labour, money/m^2
        public double LayPerTile = 3.0;       // per-piece handling (more, smaller tiles cost more)
        public double LargeFmtPerM2 = 10.0;   // large-format handling surcharge, money/m^2
        public double LargeFmtMm = 800.0;     // a tile side >= this triggers the surcharge
        public double[] MatchPremium = { 1.0, 1.4, 1.7 };   // material x, by (int)FloorMatchMode
        public double[] MatchLabPerM2 = { 0.0, 10.0, 18.0 };// selection/orientation labour, money/m^2
        public string Currency = "$";

        // Map a flat list onto the card (defaults kept where the list is short or NaN).
        // Order: material/m2, overage, cut/tile, set-out stack, set-out matched, lay/m2,
        //        lay/tile, largefmt/m2, premium[per,slip,book], matchLab[per,slip,book].
        public static TileRateCard FromList(IReadOnlyList<double> v)
        {
            var r = new TileRateCard();
            if (v == null || v.Count == 0) return r;
            r.MaterialPerM2 = G(v, 0, r.MaterialPerM2);
            r.OveragePct = G(v, 1, r.OveragePct);
            r.CutPerTile = G(v, 2, r.CutPerTile);
            r.SetOutStack = G(v, 3, r.SetOutStack);
            r.SetOutMatched = G(v, 4, r.SetOutMatched);
            r.LayPerM2 = G(v, 5, r.LayPerM2);
            r.LayPerTile = G(v, 6, r.LayPerTile);
            r.LargeFmtPerM2 = G(v, 7, r.LargeFmtPerM2);
            r.MatchPremium = new[] { G(v, 8, 1.0), G(v, 9, 1.4), G(v, 10, 1.7) };
            r.MatchLabPerM2 = new[] { G(v, 11, 0.0), G(v, 12, 10.0), G(v, 13, 18.0) };
            return r;
        }

        private static double G(IReadOnlyList<double> v, int i, double dflt)
        {
            if (i < 0 || i >= v.Count) return dflt;
            double x = v[i];
            return double.IsNaN(x) ? dflt : x;
        }
    }

    public sealed class TileCostBreakdown
    {
        public string Mode;
        public double TileX, TileY;
        public int Tiles, Cut, Cuts;
        public double PurchasedM2, FloorM2, WastePct;
        public double Material, Cutting, SetOut, Laying, Matching, Total, PerM2;
    }

    public static class FloorTileCost
    {
        // One breakdown for a packed result. holeCount adds corner-cut allowance per hole.
        public static TileCostBreakdown Estimate(FloorTileResult r, FloorTileOptions opt, TileRateCard rc, int holeCount)
        {
            if (r == null || opt == null || r.Tiles == null) return null;
            if (rc == null) rc = new TileRateCard();
            int mi = (int)opt.Match; if (mi < 0) mi = 0; if (mi > 2) mi = 2;

            double fullM2 = opt.TileX * opt.TileY / 1e6;
            int n = r.Tiles.Count;
            double purchasedM2 = fullM2 * n;
            double floorM2 = r.FloorArea / 1e6;
            double coverageM2 = r.CoverageArea / 1e6;
            double wastePct = purchasedM2 > 1e-9 ? 100.0 * (purchasedM2 - coverageM2) / purchasedM2 : 0.0;
            int cuts = r.CutCount + rc.CornerExtraCuts * (1 + Math.Max(0, holeCount));
            double maxSide = Math.Max(opt.TileX, opt.TileY);

            double material = purchasedM2 * (1.0 + rc.OveragePct) * rc.MaterialPerM2 * Idx(rc.MatchPremium, mi, 1.0);
            double cutting = cuts * rc.CutPerTile;
            double setOut = (mi == 0) ? rc.SetOutStack : rc.SetOutMatched;
            double laying = rc.LayPerM2 * floorM2 + rc.LayPerTile * n
                            + (maxSide >= rc.LargeFmtMm ? rc.LargeFmtPerM2 * floorM2 : 0.0);
            double matching = Idx(rc.MatchLabPerM2, mi, 0.0) * floorM2;
            double total = material + cutting + setOut + laying + matching;

            return new TileCostBreakdown
            {
                Mode = opt.Match.ToString(), TileX = opt.TileX, TileY = opt.TileY,
                Tiles = n, Cut = r.CutCount, Cuts = cuts,
                PurchasedM2 = purchasedM2, FloorM2 = floorM2, WastePct = wastePct,
                Material = material, Cutting = cutting, SetOut = setOut, Laying = laying,
                Matching = matching, Total = total, PerM2 = floorM2 > 1e-9 ? total / floorM2 : 0.0
            };
        }

        // PerTile / Slip / Book at the SAME geometry (match mode does not change the cut layout).
        public static List<TileCostBreakdown> SweepModes(FloorTileResult r, FloorTileOptions opt, TileRateCard rc, int holeCount)
        {
            var outl = new List<TileCostBreakdown>(3);
            var modes = new[] { FloorMatchMode.PerTile, FloorMatchMode.Slip, FloorMatchMode.Book };
            foreach (var m in modes)
            {
                var o2 = Clone(opt); o2.Match = m;
                var b = Estimate(r, o2, rc, holeCount);
                if (b != null) outl.Add(b);
            }
            return outl;
        }

        // Re-pack at each square size (plus the current size), keeping the current match mode.
        public static List<TileCostBreakdown> SweepSizes(
            IReadOnlyList<(double X, double Y)> boundary,
            IReadOnlyList<IReadOnlyList<(double X, double Y)>> holes,
            FloorTileOptions opt, TileRateCard rc, IReadOnlyList<double> squareSizes, int holeCount)
        {
            var sizes = new List<(double X, double Y)>();
            if (squareSizes != null)
                foreach (var s in squareSizes) if (s > 0) sizes.Add((s, s));
            bool has = false;
            foreach (var s in sizes)
                if (Math.Abs(s.X - opt.TileX) < 1e-6 && Math.Abs(s.Y - opt.TileY) < 1e-6) { has = true; break; }
            if (!has) sizes.Add((opt.TileX, opt.TileY));

            var outl = new List<TileCostBreakdown>(sizes.Count);
            foreach (var s in sizes)
            {
                var o2 = Clone(opt); o2.TileX = s.X; o2.TileY = s.Y;
                FloorTileResult rr;
                try { rr = FloorTileGridLayout.Pack(boundary, holes, o2); }
                catch { continue; }
                if (rr == null || rr.Tiles == null || rr.Tiles.Count == 0) continue;
                var b = Estimate(rr, o2, rc, holeCount);
                if (b != null) outl.Add(b);
            }
            outl.Sort((a, b2) => a.TileX != b2.TileX ? a.TileX.CompareTo(b2.TileX) : a.TileY.CompareTo(b2.TileY));
            return outl;
        }

        // Human-readable report: current breakdown + the two sweeps.
        public static string Report(TileCostBreakdown cur, List<TileCostBreakdown> modes,
            List<TileCostBreakdown> sizes, TileRateCard rc)
        {
            string cy = (rc != null && rc.Currency != null) ? rc.Currency : "$";
            var sb = new StringBuilder();
            sb.AppendFormat("FLOOR-TILING COST (illustrative rates; floor {0:0.0} m^2)\n", cur.FloorM2);
            sb.AppendFormat("Current: {0:0.#}x{1:0.#} mm, {2}, {3} tiles ({4} cut), waste {5:0.0}%\n",
                cur.TileX, cur.TileY, cur.Mode, cur.Tiles, cur.Cut, cur.WastePct);
            sb.AppendFormat("  material {0}{1:n0} | cutting {0}{2:n0} | set-out {0}{3:n0} | laying {0}{4:n0} | matching {0}{5:n0}\n",
                cy, cur.Material, cur.Cutting, cur.SetOut, cur.Laying, cur.Matching);
            sb.AppendFormat("  TOTAL {0}{1:n0}  =  {0}{2:n0}/m^2  (material {3:0}% of total)\n",
                cy, cur.Total, cur.PerM2, cur.Total > 1e-9 ? 100.0 * cur.Material / cur.Total : 0.0);

            if (modes != null && modes.Count > 0)
            {
                double basis = modes[0].Total;
                sb.Append("\nMatch sweep (same layout):\n");
                foreach (var m in modes)
                    sb.AppendFormat("  {0,-8} {1}{2,8:n0}  {1}{3,4:n0}/m^2  {4:0.00}x\n",
                        m.Mode, cy, m.Total, m.PerM2, basis > 1e-9 ? m.Total / basis : 1.0);
            }

            if (sizes != null && sizes.Count > 0)
            {
                sb.Append("\nSize sweep (current match mode):\n");
                sb.AppendFormat("  {0,-10} {1,5} {2,7} {3,9} {4,8}\n", "tile mm", "tiles", "waste%", "total", "/m^2");
                foreach (var s in sizes)
                {
                    string sz = Math.Abs(s.TileX - s.TileY) < 1e-6
                        ? ((int)Math.Round(s.TileX)).ToString()
                        : ((int)Math.Round(s.TileX)) + "x" + ((int)Math.Round(s.TileY));
                    sb.AppendFormat("  {0,-10} {1,5} {2,6:0.0} {3}{4,7:n0} {3}{5,6:n0}\n",
                        sz, s.Tiles, s.WastePct, cy, s.Total, s.PerM2);
                }
            }
            return sb.ToString();
        }

        private static double Idx(double[] a, int i, double dflt)
        {
            return (a != null && i >= 0 && i < a.Length) ? a[i] : dflt;
        }

        private static FloorTileOptions Clone(FloorTileOptions o)
        {
            return new FloorTileOptions
            {
                TileX = o.TileX, TileY = o.TileY, Joint = o.Joint, StartMode = o.StartMode,
                AnchorX = o.AnchorX, AnchorY = o.AnchorY, GrainAngleRad = o.GrainAngleRad,
                GrainField = o.GrainField, Seed = o.Seed, SliverFraction = o.SliverFraction,
                AutoCentreSliver = o.AutoCentreSliver, Match = o.Match,
                RowStaggerFraction = o.RowStaggerFraction, LargeFormatMm = o.LargeFormatMm, Eps = o.Eps
            };
        }
    }
}
