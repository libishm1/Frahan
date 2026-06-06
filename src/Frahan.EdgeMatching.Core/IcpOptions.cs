namespace Frahan.EdgeMatching
{
    public class IcpOptions
    {
        public int MaxIterations { get; set; } = 50;
        public double TranslationTol { get; set; } = 1e-4;
        public double RotationTolDeg { get; set; } = 1e-3;
        public double PenetrationPenalty { get; set; } = 100.0;
        public int SamplesPerSegment { get; set; } = 64;

        /// <summary>
        /// When false (default) the ICP correspondence step uses free
        /// nearest-point matching (Curve.ClosestPoint) exactly as before, so
        /// the default numeric path is byte-for-byte unchanged. When true the
        /// correspondence step instead uses
        /// <see cref="OrderedBoundaryMatcher"/> to build an ORDER-PRESERVING
        /// (monotone, non-crossing) point pairing between the two rims. This
        /// is more robust on wiggly / noisy rims where free nearest-neighbour
        /// produces tangled, crossing correspondences. Opt-in.
        /// </summary>
        public bool NonCrossingCorrespondence { get; set; } = false;

        /// <summary>
        /// Bound (in sample-index units) on how far the two rims' running
        /// indices may diverge inside the monotone DP when
        /// <see cref="NonCrossingCorrespondence"/> is on. Zero or negative =
        /// unbounded band. Only consulted by the non-crossing path.
        /// </summary>
        public int NonCrossingMaxGap { get; set; } = 0;
    }
}
