using Rhino.Geometry;

namespace Frahan.EdgeMatching
{
    public sealed class MatchResult
    {
        public Segment A { get; }
        public Segment B { get; }
        public Transform AontoB { get; }
        public double Residual { get; }
        public bool Converged { get; }
        public int Iterations { get; }

        public MatchResult(
            Segment a,
            Segment b,
            Transform aontoB,
            double residual,
            bool converged,
            int iterations)
        {
            A = a;
            B = b;
            AontoB = aontoB;
            Residual = residual;
            Converged = converged;
            Iterations = iterations;
        }
    }
}
