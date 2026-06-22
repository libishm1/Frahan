using System;

namespace Frahan.EdgeMatching
{
    public class HashOptions
    {
        public double LengthBinSize { get; set; } = 5.0;
        public double TurningBinSize { get; set; } = Math.PI / 12;
        public double MeanBinSize { get; set; } = 0.05;
        public double StdBinSize { get; set; } = 0.05;
        public int BinNeighbourhood { get; set; } = 1;

        // 3D-only bins. Ignored for planar segments.
        public double PlanarityBinSize { get; set; } = 0.5;        // mm of panel-level RMS
        public double TorsionVarBinSize { get; set; } = 0.02;      // 1/mm of |std(torsion)|

        // -------- Phase 0 recall foundation (opt-in; default off = byte-identical legacy) --------
        // #2 scale normalization: when Scale > 0, the LENGTH bin width becomes
        // Scale * RelativeLengthBinFraction instead of the absolute LengthBinSize,
        // so the length dimension is invariant to object size. Scale is the
        // assembly scale (median panel bbox diagonal). 0 = absolute (legacy).
        public double Scale { get; set; } = 0.0;
        public double RelativeLengthBinFraction { get; set; } = 0.04;

        // #1 multi-probe LSH: replace the uniform +/-BinNeighbourhood grid with a
        // query-directed probe sequence ranked by per-dimension boundary distance.
        // Reaches up to MaxProbeStep bins in the dimension where the query straddles
        // a bin boundary (catching tessellation-shifted complements the +/-1 grid misses),
        // while keeping the probe count bounded to MultiProbeT.
        public bool UseMultiProbe { get; set; } = false;
        public int MultiProbeT { get; set; } = 48;
        public int MaxProbeStep { get; set; } = 2;
    }
}
