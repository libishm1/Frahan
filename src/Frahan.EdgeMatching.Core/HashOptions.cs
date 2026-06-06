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
    }
}
