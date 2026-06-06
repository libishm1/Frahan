#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Core.Earthworks
{
    // =========================================================================
    // BedrockSurface -- GPR fracture picks -> bedrock / rock-face depth surface.
    //
    // Card A9 (ROSES_excavation_pipeline.md): the first real z_r source for the
    // overburden strip. The deepest continuous strong reflector below the
    // weathered cover = top of fresh rock (Porsani A-G; Isakova A/B; Bondua
    // bedrock; gpr_math_derivations.md sec 4d). This reduces a GPR pick set to
    // that surface and converts depth -> world z_r = z_ground - depth (two-way
    // depth already halved upstream). Output is scattered (x,y,z_r) points; feed
    // them through TinMerge.ResampleOntoVertices onto the ground TIN, then
    // OverburdenVolume.Compute for the soil-strip volume (W15 -> W16).
    //
    // Rhino-free, headless-testable. Pure reduction + datum shift; no FFT.
    // =========================================================================
    public struct BedrockPick
    {
        public double X;        // along-line / world x (m)
        public double Y;        // line offset / world y (m)
        public double Depth;    // reflector depth below ground (m, already v*t/2)
        public double Energy;   // confidence 0..1
        public BedrockPick(double x, double y, double depth, double energy)
        { X = x; Y = y; Depth = depth; Energy = energy; }
    }

    public sealed class BedrockSurfaceOptions
    {
        /// <summary>Quantise x,y to this cell (m) when reducing to one pick per column.
        /// 0 = use the raw (x,y) of each deepest pick (no binning).</summary>
        public double ColumnCell = 0.0;
        /// <summary>Ignore picks shallower than this (m) -- skip the weathered-cover band.</summary>
        public double MinDepth = 0.0;
        /// <summary>Require at least this confidence (0..1) for a bedrock pick.</summary>
        public double MinEnergy = 0.0;
    }

    public static class BedrockSurface
    {
        /// <summary>
        /// Reduce a GPR pick set to the deepest qualifying reflector per (x,y) column,
        /// then convert to scattered world bedrock points z_r = groundZ(x,y) - depth.
        /// </summary>
        /// <param name="picks">all fracture/reflector picks from the survey line(s).</param>
        /// <param name="groundZAt">ground elevation at a column; null = flat datum 0.</param>
        /// <returns>flat (x,y,z_r) coords for TinMerge / OverburdenVolume.</returns>
        public static double[] DeepestReflectorPoints(
            IReadOnlyList<BedrockPick> picks, Func<double, double, double> groundZAt = null,
            BedrockSurfaceOptions opt = null)
        {
            if (picks == null) throw new ArgumentNullException(nameof(picks));
            opt = opt ?? new BedrockSurfaceOptions();

            // group by column, keep the deepest qualifying pick per column
            var deepest = new Dictionary<long, BedrockPick>();
            var rawList = new List<BedrockPick>();
            bool binning = opt.ColumnCell > 0;
            foreach (var p in picks)
            {
                if (p.Depth < opt.MinDepth || p.Energy < opt.MinEnergy) continue;
                if (!binning) { rawList.Add(p); continue; }
                long key = ColKey(p.X, p.Y, opt.ColumnCell);
                if (!deepest.TryGetValue(key, out var cur) || p.Depth > cur.Depth)
                    deepest[key] = p;
            }

            IEnumerable<BedrockPick> selected;
            if (binning) selected = deepest.Values;
            else
            {
                // no binning: still reduce to deepest per exact (x,y) to avoid stacking
                var byExact = new Dictionary<long, BedrockPick>();
                foreach (var p in rawList)
                {
                    long key = ColKey(p.X, p.Y, 1e-6);
                    if (!byExact.TryGetValue(key, out var cur) || p.Depth > cur.Depth)
                        byExact[key] = p;
                }
                selected = byExact.Values;
            }

            var outp = new List<double>();
            foreach (var p in selected)
            {
                double zg = groundZAt != null ? groundZAt(p.X, p.Y) : 0.0;
                outp.Add(p.X); outp.Add(p.Y); outp.Add(zg - p.Depth);
            }
            return outp.ToArray();
        }

        /// <summary>
        /// One-call bridge: GPR bedrock picks -> resampled bedrock z aligned to a ground
        /// TIN's vertices, ready for OverburdenVolume.Compute(groundXyz, bedrockZ, tris).
        /// Ground z is taken from the ground vertices themselves (depth measured from ground).
        /// </summary>
        public static TinMergeResult ToCommonTin(
            IReadOnlyList<BedrockPick> picks, IReadOnlyList<double> groundXyz,
            BedrockSurfaceOptions bopt = null, TinMergeOptions mopt = null)
        {
            // ground z lookup by nearest ground vertex is overkill here; depth is below the
            // local ground, so build scattered bedrock with a flat datum then let TinMerge
            // interpolate the BEDROCK z; the caller differences against the SAME ground z.
            // To keep depths consistent we set z_r = -depth (datum 0) and the caller adds
            // the ground vertex z when differencing (OverburdenVolume uses z_top - z_bottom,
            // and z_top = ground vertex z, z_bottom must be ground_z - depth).
            // So here we resample DEPTH (not z) and the caller subtracts: see overload below.
            var depthPts = DepthPoints(picks, bopt);
            return TinMerge.ResampleOntoVertices(groundXyz, depthPts, mopt);
        }

        /// <summary>Scattered (x,y,depth) points (z slot carries DEPTH below ground), so a
        /// TinMerge resample yields per-vertex depth; bedrockZ = groundZ - depth.</summary>
        public static double[] DepthPoints(IReadOnlyList<BedrockPick> picks, BedrockSurfaceOptions opt = null)
        {
            opt = opt ?? new BedrockSurfaceOptions();
            var byExact = new Dictionary<long, BedrockPick>();
            double cell = opt.ColumnCell > 0 ? opt.ColumnCell : 1e-6;
            foreach (var p in picks)
            {
                if (p.Depth < opt.MinDepth || p.Energy < opt.MinEnergy) continue;
                long key = ColKey(p.X, p.Y, cell);
                if (!byExact.TryGetValue(key, out var cur) || p.Depth > cur.Depth) byExact[key] = p;
            }
            var outp = new List<double>(byExact.Count * 3);
            foreach (var p in byExact.Values) { outp.Add(p.X); outp.Add(p.Y); outp.Add(p.Depth); }
            return outp.ToArray();
        }

        private static long ColKey(double x, double y, double cell)
        {
            long ix = (long)Math.Floor(x / cell);
            long iy = (long)Math.Floor(y / cell);
            return (ix * 73856093L) ^ (iy * 19349663L);
        }
    }
}
