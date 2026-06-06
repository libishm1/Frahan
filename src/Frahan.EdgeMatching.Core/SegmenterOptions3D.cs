namespace Frahan.EdgeMatching
{
    public sealed class SegmenterOptions3D : SegmenterOptions
    {
        public double TorsionSmoothingWindow { get; set; } = 5.0;
        public bool ComputeTorsion { get; set; } = true;
        public double CurvatureBreakThreshold { get; set; } = 0.05;
    }
}
