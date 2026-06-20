#nullable disable
// =============================================================================
// FloorTileGridLayout -- divide a floor boundary into standard stone tiles by
// straight (guillotine) module lines, trim perimeter tiles to the boundary, and
// carry a per-tile GRAIN DIRECTION that drives Rhino-native texture mapping.
//
// Rhino-free Core (tuple geometry only), sibling of ContactNfpHoleNester. Uses
// Frahan.Masonry.Geometry.Clipper2Adapter for the boolean trim and grout insets.
//
// Layout rules from the floor-tiling research dossier (2026-06-20):
//   * module pitch P = tile face + one grout joint; tiles sit on P, grout is the gap.
//   * start modes: CornerMin / PickedPoint / CentredSymmetric.
//   * no-sliver rule (ANSI A108 4.3.2): perimeter cut >= SliverFraction * tile
//     (0.5 hard, 0.333 fallback); auto-centre shifts the lattice by P/2 to enlarge slivers.
//   * grain direction is a first-class feature (DirectionRad + arrows) AND the
//     source of the per-tile UV frame for an image material (texture mapping).
// =============================================================================
using System;
using System.Collections.Generic;
using Frahan.Masonry.Geometry;

namespace Frahan.Packing.TwoD
{
    public enum FloorStartMode { CornerMin, PickedPoint, CentredSymmetric }
    public enum FloorGrainField { Monolithic, QuarterTurn, Random }
    public enum FloorMatchMode { PerTile, Slip, Book }   // texture continuity across joints

    public sealed class FloorTileOptions
    {
        public double TileX = 600.0;          // tile face width  (mm)
        public double TileY = 600.0;          // tile face height (mm)
        public double Joint = 3.0;            // grout joint width (mm); module pitch = tile + joint
        public FloorStartMode StartMode = FloorStartMode.CentredSymmetric;
        public double AnchorX = 0.0;          // PickedPoint lattice origin
        public double AnchorY = 0.0;
        public double GrainAngleRad = 0.0;    // global grain direction
        public FloorGrainField GrainField = FloorGrainField.Monolithic;
        public int Seed = 12345;              // for Random field
        public double SliverFraction = 0.5;   // perimeter-cut acceptance (>= this * tile)
        public bool AutoCentreSliver = true;  // try P/2 lattice shifts to avoid slivers
        public FloorMatchMode Match = FloorMatchMode.PerTile; // PerTile / Slip (continuous, one slab) / Book (mirror)
        public double RowStaggerFraction = 0.0;// running-bond row offset along X (0, 1/3, 1/2)
        public double LargeFormatMm = 380.0;  // a tile side above this caps the running-bond offset at 1/3 (lippage)
        public bool LippageCapApplied = false;// set by Pack when the 1/3 cap clamped RowStaggerFraction
        public double Eps = 0.0;              // <=0 => scale-relative max(1e-6, 1e-3*diag)
    }

    public sealed class FloorTile
    {
        public List<(double X, double Y)> Loop;        // trimmed tile polygon (CCW)
        public bool IsFull;                            // full module tile vs cut perimeter tile
        public double Cx, Cy;                          // mapping origin (the un-trimmed cell centre)
        public double DirectionRad;                    // grain direction (the feature)
        public double Area;
        public double MinDim;                          // min bbox dimension of the trimmed loop (sliver test)
        public int Row, Col;
        public List<(double U, double V)> UV;          // per-vertex texture coords, grain-aligned
    }

    public sealed class FloorTileResult
    {
        public List<FloorTile> Tiles = new List<FloorTile>();
        public int FullCount;
        public int CutCount;
        public double CoverageArea;                    // sum of placed tile areas
        public double FloorArea;                       // usable (grout-inset, holes removed) area
        public double MinBorderDim;                    // smallest perimeter-cut dimension
        public bool SliverPass;                        // MinBorderDim >= SliverFraction * min(tile)
        public string Report;
    }

    public static class FloorTileGridLayout
    {
        public static FloorTileResult Pack(
            IReadOnlyList<(double X, double Y)> boundaryOuter,
            IReadOnlyList<IReadOnlyList<(double X, double Y)>> holes,
            FloorTileOptions opt)
        {
            if (boundaryOuter == null || boundaryOuter.Count < 3)
                throw new ArgumentException("boundaryOuter needs >= 3 points", nameof(boundaryOuter));
            if (opt == null) opt = new FloorTileOptions();
            if (opt.TileX <= 0 || opt.TileY <= 0) throw new ArgumentException("tile dims must be > 0");

            double minX, minY, maxX, maxY;
            Bbox(boundaryOuter, out minX, out minY, out maxX, out maxY);
            double diag = Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));
            double eps = opt.Eps > 0 ? opt.Eps : Math.Max(1e-6, 1e-3 * diag);

            double px = opt.TileX + opt.Joint;
            double py = opt.TileY + opt.Joint;
            double halfJ = opt.Joint * 0.5;

            // large-format lippage rule (TCNA/ANSI): cap the running-bond offset at 1/3 for long tiles
            opt.LippageCapApplied = false;
            if (Math.Max(opt.TileX, opt.TileY) > opt.LargeFormatMm && opt.RowStaggerFraction > 1.0 / 3.0 + 1e-9)
            { opt.RowStaggerFraction = 1.0 / 3.0; opt.LippageCapApplied = true; }

            // ---- usable region = outer eroded by joint/2, minus holes inflated by joint/2 (grout at walls + holes)
            var outerWrapped = new List<IReadOnlyList<(double X, double Y)>> { boundaryOuter };
            List<List<(double X, double Y)>> usable = halfJ > 0
                ? Clipper2Adapter.InflateLoops(outerWrapped, -halfJ)
                : CopyLoops(outerWrapped);
            if (holes != null && holes.Count > 0)
            {
                var holesExp = halfJ > 0 ? Clipper2Adapter.InflateLoops(holes, halfJ) : CopyLoops(holes);
                usable = Clipper2Adapter.DifferenceLoops(usable, holesExp);
            }
            if (usable.Count == 0)
                return new FloorTileResult { Report = "empty usable region (boundary smaller than the grout inset)" };
            double floorArea = NetArea(usable);

            // ---- candidate lattice phases by start mode (+ P/2 sliver shifts) ----
            double midX = 0.5 * (minX + maxX), midY = 0.5 * (minY + maxY);
            var phaseX = new List<double>();
            var phaseY = new List<double>();
            switch (opt.StartMode)
            {
                case FloorStartMode.CornerMin:
                    phaseX.Add(minX); phaseY.Add(minY); break;
                case FloorStartMode.PickedPoint:
                    phaseX.Add(opt.AnchorX); phaseY.Add(opt.AnchorY); break;
                default: // CentredSymmetric: a tile centred on the span midpoint (equal opposite borders)
                    phaseX.Add(midX - opt.TileX * 0.5); phaseY.Add(midY - opt.TileY * 0.5); break;
            }
            if (opt.AutoCentreSliver)
            {
                phaseX.Add(phaseX[0] + px * 0.5);   // half-module shift (centre-on-joint / split the sliver)
                phaseY.Add(phaseY[0] + py * 0.5);
            }

            // ---- evaluate each phase combo, keep the one with the largest minimum border dimension ----
            FloorTileResult best = null;
            double bestScore = double.NegativeInfinity;
            foreach (double fx in phaseX)
                foreach (double fy in phaseY)
                {
                    var r = LayOut(usable, minX, minY, maxX, maxY, fx, fy, px, py, eps, floorArea, opt);
                    double score = r.MinBorderDim; // maximise the smallest perimeter cut
                    if (score > bestScore) { bestScore = score; best = r; }
                }
            return best;
        }

        // ---------------------------------------------------------------------
        private static FloorTileResult LayOut(
            List<List<(double X, double Y)>> usable,
            double minX, double minY, double maxX, double maxY,
            double fx, double fy, double px, double py, double eps, double floorArea,
            FloorTileOptions opt)
        {
            var res = new FloorTileResult { FloorArea = floorArea };
            int k0x = (int)Math.Floor((minX - fx) / px) - 1;
            int k1x = (int)Math.Ceiling((maxX - fx) / px) + 1;
            int k0y = (int)Math.Floor((minY - fy) / py) - 1;
            int k1y = (int)Math.Ceiling((maxY - fy) / py) + 1;
            double fullArea = opt.TileX * opt.TileY;
            double minTile = Math.Min(opt.TileX, opt.TileY);
            double minBorder = double.PositiveInfinity;
            var rng = new Lcg((ulong)opt.Seed);

            double stag = opt.RowStaggerFraction;
            for (int ky = k0y; ky <= k1y; ky++)
            {
                double rowFx = fx + (stag != 0.0 ? Frac(ky * stag) * px : 0.0); // running-bond row offset
                for (int kx = k0x; kx <= k1x; kx++)
                {
                    double x0 = rowFx + kx * px, y0 = fy + ky * py;
                    double x1 = x0 + opt.TileX, y1 = y0 + opt.TileY;
                    if (x1 < minX || x0 > maxX || y1 < minY || y0 > maxY) continue;
                    var cell = new List<(double X, double Y)> { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };
                    var cellWrapped = new List<IReadOnlyList<(double X, double Y)>> { cell };
                    var pieces = Clipper2Adapter.IntersectLoops(cellWrapped, usable);
                    if (pieces.Count == 0) continue;

                    double cx = x0 + opt.TileX * 0.5, cy = y0 + opt.TileY * 0.5;
                    double theta = GrainFor(opt, kx, ky, rng);
                    foreach (var piece in pieces)
                    {
                        double a = Math.Abs(ShoelaceArea(piece));
                        if (a < eps * eps) continue;
                        double bxMin, byMin, bxMax, byMax;
                        Bbox(piece, out bxMin, out byMin, out bxMax, out byMax);
                        double md = Math.Min(bxMax - bxMin, byMax - byMin);
                        bool full = a >= (1.0 - 1e-3) * fullArea;
                        var t = new FloorTile
                        {
                            Loop = piece, IsFull = full, Cx = cx, Cy = cy,
                            DirectionRad = theta, Area = a, MinDim = md, Row = ky, Col = kx,
                            UV = ComputeUV(piece, cx, cy, theta, minX, minY, kx, ky, opt)
                        };
                        res.Tiles.Add(t);
                        res.CoverageArea += a;
                        if (full) res.FullCount++;
                        else { res.CutCount++; if (md < minBorder) minBorder = md; }
                    }
                }
            }

            res.MinBorderDim = double.IsPositiveInfinity(minBorder) ? minTile : minBorder;
            res.SliverPass = res.MinBorderDim >= opt.SliverFraction * minTile - eps;
            res.Report = string.Format(
                "Floor tiling: {0} tiles ({1} full, {2} cut). Tile {3:0.#}x{4:0.#} mm, joint {5:0.#} mm, " +
                "start={6}. Coverage {7:0.0}/{8:0.0} m^2 ({9:0.0}%). Smallest perimeter cut {10:0.#} mm " +
                "({11} the {12:0.0}% no-sliver threshold of {13:0.#} mm). Grain field {14}, match {15}, " +
                "row offset {16:0.##}{17}.",
                res.Tiles.Count, res.FullCount, res.CutCount, opt.TileX, opt.TileY, opt.Joint, opt.StartMode,
                res.CoverageArea / 1e6, res.FloorArea / 1e6,
                res.FloorArea > 0 ? 100.0 * res.CoverageArea / res.FloorArea : 0.0,
                res.MinBorderDim, res.SliverPass ? "passes" : "FAILS", opt.SliverFraction * 100.0,
                opt.SliverFraction * minTile, opt.GrainField, opt.Match, opt.RowStaggerFraction,
                opt.LippageCapApplied ? " (capped at 1/3 for large format)" : "");
            return res;
        }

        // ---- grain direction per tile (the feature) -------------------------
        private static double GrainFor(FloorTileOptions opt, int kx, int ky, Lcg rng)
        {
            switch (opt.GrainField)
            {
                case FloorGrainField.QuarterTurn:
                    return opt.GrainAngleRad + (((kx + ky) & 1) == 0 ? 0.0 : Math.PI * 0.5);
                case FloorGrainField.Random:
                    // deterministic per-cell quarter turn (hash kx,ky into the stream)
                    ulong h = (ulong)((kx * 73856093) ^ (ky * 19349663) ^ (opt.Seed * 83492791));
                    int q = (int)((h >> 13) & 3);
                    return opt.GrainAngleRad + q * (Math.PI * 0.5);
                default:
                    return opt.GrainAngleRad;
            }
        }

        // ---- per-vertex UV (texture coords), grain-aligned ------------------
        // Per-tile: the full module maps [0,1]x[0,1] rotated by the grain; trimmed parts get partial UV.
        // Continuous (slip-match): UVs flow from the floor origin so the image reads as one slab.
        private static List<(double U, double V)> ComputeUV(
            List<(double X, double Y)> loop, double cx, double cy, double theta,
            double minX, double minY, int kx, int ky, FloorTileOptions opt)
        {
            double c = Math.Cos(-theta), s = Math.Sin(-theta);
            var uv = new List<(double U, double V)>(loop.Count);
            if (opt.Match == FloorMatchMode.Slip)
            {
                // slip-match: UVs flow from the floor origin so the image reads as one continuous slab
                for (int i = 0; i < loop.Count; i++)
                {
                    double dx = loop[i].X - minX, dy = loop[i].Y - minY;
                    double lx = dx * c - dy * s, ly = dx * s + dy * c;
                    uv.Add((lx / opt.TileX, ly / opt.TileY));
                }
                return uv;
            }
            // per-tile: full module maps [0,1]x[0,1] rotated by the grain; Book mirrors across joints
            bool flipU = opt.Match == FloorMatchMode.Book && ((kx & 1) != 0);
            bool flipV = opt.Match == FloorMatchMode.Book && ((ky & 1) != 0);
            for (int i = 0; i < loop.Count; i++)
            {
                double dx = loop[i].X - cx, dy = loop[i].Y - cy;
                double lx = dx * c - dy * s, ly = dx * s + dy * c;
                double u = lx / opt.TileX + 0.5, v = ly / opt.TileY + 0.5;
                if (flipU) u = 1.0 - u;
                if (flipV) v = 1.0 - v;
                uv.Add((u, v));
            }
            return uv;
        }

        private static double Frac(double v) { return v - Math.Floor(v); }

        // ---- geometry helpers ----------------------------------------------
        private static void Bbox(IReadOnlyList<(double X, double Y)> p,
            out double minX, out double minY, out double maxX, out double maxY)
        {
            minX = minY = double.PositiveInfinity; maxX = maxY = double.NegativeInfinity;
            for (int i = 0; i < p.Count; i++)
            {
                if (p[i].X < minX) minX = p[i].X; if (p[i].X > maxX) maxX = p[i].X;
                if (p[i].Y < minY) minY = p[i].Y; if (p[i].Y > maxY) maxY = p[i].Y;
            }
        }

        private static double ShoelaceArea(IReadOnlyList<(double X, double Y)> p)
        {
            double a = 0; int n = p.Count;
            for (int i = 0; i < n; i++)
            {
                var u = p[i]; var v = p[(i + 1) % n];
                a += u.X * v.Y - v.X * u.Y;
            }
            return 0.5 * a;
        }

        private static double NetArea(List<List<(double X, double Y)>> loops)
        {
            double a = 0;
            for (int i = 0; i < loops.Count; i++) a += ShoelaceArea(loops[i]);
            return Math.Abs(a);
        }

        private static List<List<(double X, double Y)>> CopyLoops(
            IReadOnlyList<IReadOnlyList<(double X, double Y)>> src)
        {
            var outl = new List<List<(double X, double Y)>>(src.Count);
            for (int i = 0; i < src.Count; i++)
            {
                var d = new List<(double X, double Y)>(src[i].Count);
                for (int j = 0; j < src[i].Count; j++) d.Add(src[i][j]);
                outl.Add(d);
            }
            return outl;
        }

        private sealed class Lcg
        {
            private ulong _s;
            public Lcg(ulong seed) { _s = seed == 0 ? 0x9E3779B97F4A7C15UL : seed; }
            public double Next()
            {
                unchecked { _s = _s * 6364136223846793005UL + 1442695040888963407UL; }
                return ((_s >> 33) & 0x7fffffff) / 2147483647.0;
            }
        }
    }
}
