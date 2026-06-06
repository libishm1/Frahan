namespace Frahan.EdgeMatching
{
    /// <summary>
    /// Knobs for the planar 2D boundary segmenter. Defaults are tuned
    /// for millimetre-unit Trencadís shards; raise SampleSpacing and
    /// MinSegmentLength for wood-plank scale.
    /// </summary>
    public class SegmenterOptions
    {
        public double SampleSpacing { get; set; } = 1.0;
        public double BreakAngleDeg { get; set; } = 18.0;
        public int BreakWindow { get; set; } = 3;
        public int SignatureBins { get; set; } = 128;
        public double MinSegmentLength { get; set; } = 8.0;

        // --- R1: partial sub-segment emission (opt-in) ------------------------
        // The base segmenter hashes WHOLE break-to-break segments. A long edge
        // and the short sub-edge it physically mates with land in different
        // length / turning bins and never meet in SegmentHashIndex. When
        // EmitPartials is on, each base segment ALSO emits shorter overlapping
        // sub-windows (sliding windows over its resampled polyline at each
        // fraction in PartialFractions) so the long edge's partials reach the
        // short complementary edge. Default OFF: when off, segmentation is
        // byte-for-byte identical to before (PartialFractions defaults to an
        // empty array, and the emit loop is skipped entirely).

        /// <summary>
        /// When true, the segmenter emits partial sub-windows of each base
        /// segment in addition to the base segments themselves. Default false
        /// (whole-segment-only, identical to previous behaviour).
        /// </summary>
        public bool EmitPartials { get; set; } = false;

        /// <summary>
        /// Window lengths for partial emission, as fractions of each base
        /// segment's resampled-point span. E.g. {0.5, 0.25} emits half-length
        /// and quarter-length sub-windows. Values are clamped to (0,1); 1.0 or
        /// above would duplicate the base segment and is skipped. Empty
        /// (default) means no partials even if <see cref="EmitPartials"/> is
        /// true. Order is preserved for determinism.
        /// </summary>
        public double[] PartialFractions { get; set; } = new double[0];

        /// <summary>
        /// Stride between consecutive partial windows, as a fraction of the
        /// window length. 1.0 (default) = non-overlapping tiling; 0.5 = 50%
        /// overlap. Clamped to [0.1, 1.0]. Smaller strides emit more windows
        /// (higher recall, more candidates / cost).
        /// </summary>
        public double PartialStrideFraction { get; set; } = 1.0;
    }
}
