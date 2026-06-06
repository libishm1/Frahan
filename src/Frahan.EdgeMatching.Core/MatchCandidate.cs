namespace Frahan.EdgeMatching
{
    public sealed class MatchCandidate
    {
        public Segment A { get; }
        public Segment B { get; }
        public double CoarseScore { get; }
        public int Lag { get; }

        public MatchCandidate(Segment a, Segment b, double score, int lag)
        {
            A = a;
            B = b;
            CoarseScore = score;
            Lag = lag;
        }
    }
}
