using System;

namespace Frahan.EdgeMatching
{
    /// <summary>
    /// Rotation- and translation-invariant hash key for the 2D path.
    /// Two segments fall in the same bucket iff every component matches.
    /// </summary>
    public readonly struct SegmentHashKey : IEquatable<SegmentHashKey>
    {
        public int LengthBin { get; }
        public int TurningBin { get; }
        public int MeanBin { get; }
        public int StdBin { get; }
        public int Sign { get; }

        public SegmentHashKey(int len, int turn, int mean, int std, int sign)
        {
            LengthBin = len;
            TurningBin = turn;
            MeanBin = mean;
            StdBin = std;
            Sign = sign;
        }

        public bool Equals(SegmentHashKey other) =>
            LengthBin == other.LengthBin
            && TurningBin == other.TurningBin
            && MeanBin == other.MeanBin
            && StdBin == other.StdBin
            && Sign == other.Sign;

        public override bool Equals(object? obj) => obj is SegmentHashKey k && Equals(k);

        // net48 lacks System.HashCode.Combine; manual fold per net48 compat rule.
        public override int GetHashCode()
        {
            unchecked
            {
                int h = LengthBin;
                h = (h * 397) ^ TurningBin;
                h = (h * 397) ^ MeanBin;
                h = (h * 397) ^ StdBin;
                h = (h * 397) ^ Sign;
                return h;
            }
        }

        public static bool operator ==(SegmentHashKey a, SegmentHashKey b) => a.Equals(b);
        public static bool operator !=(SegmentHashKey a, SegmentHashKey b) => !a.Equals(b);
    }

    /// <summary>
    /// 3D extension of the hash key. Adds planarity and torsion-variance
    /// bins. Used only when at least one segment originates from a
    /// Spatial3D panel.
    /// </summary>
    public readonly struct SegmentHashKey3D : IEquatable<SegmentHashKey3D>
    {
        public SegmentHashKey Base { get; }
        public int PlanarityBin { get; }
        public int TorsionVarBin { get; }

        public SegmentHashKey3D(SegmentHashKey baseKey, int planarity, int torsionVar)
        {
            Base = baseKey;
            PlanarityBin = planarity;
            TorsionVarBin = torsionVar;
        }

        public bool Equals(SegmentHashKey3D other) =>
            Base.Equals(other.Base)
            && PlanarityBin == other.PlanarityBin
            && TorsionVarBin == other.TorsionVarBin;

        public override bool Equals(object? obj) => obj is SegmentHashKey3D k && Equals(k);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Base.GetHashCode();
                h = (h * 397) ^ PlanarityBin;
                h = (h * 397) ^ TorsionVarBin;
                return h;
            }
        }

        public static bool operator ==(SegmentHashKey3D a, SegmentHashKey3D b) => a.Equals(b);
        public static bool operator !=(SegmentHashKey3D a, SegmentHashKey3D b) => !a.Equals(b);
    }
}
