using System;
using Rhino.Geometry;

namespace Frahan.EdgeMatching
{
    /// <summary>
    /// One open boundary segment between two curvature break-points.
    /// Geometry is in panel-local coordinates. TurningSignature is the
    /// 2D-canonical signal. CurvatureSignature is populated for every
    /// panel; TorsionSignature is null for planar panels (per
    /// addendum: torsion flips sign under reflection and is used for
    /// 3D mirror disambiguation).
    /// </summary>
    public sealed class Segment
    {
        public string PanelId { get; }
        public int Index { get; }
        public Polyline LocalPolyline { get; }
        public double ChordLength { get; }
        public double TotalTurning { get; }
        public int Sign { get; }

        public double[] TurningSignature { get; }
        public double[] CurvatureSignature { get; }
        public double[]? TorsionSignature { get; }

        /// <summary>
        /// RMS planarity deviation of the parent panel. Carried on the
        /// segment so SegmentHashIndex can bucket 3D segments by planarity
        /// without having to keep a parent-panel reference. Zero for
        /// planar-2D segments.
        /// </summary>
        public double PanelPlanarityRms { get; }

        public Segment(
            string panelId,
            int index,
            Polyline poly,
            double chord,
            double totalTurning,
            int sign,
            double[] turningSignature,
            double[] curvatureSignature,
            double[]? torsionSignature = null,
            double panelPlanarityRms = 0.0)
        {
            if (panelId == null) throw new ArgumentNullException(nameof(panelId));
            if (poly == null) throw new ArgumentNullException(nameof(poly));
            if (turningSignature == null) throw new ArgumentNullException(nameof(turningSignature));
            if (curvatureSignature == null) throw new ArgumentNullException(nameof(curvatureSignature));

            PanelId = panelId;
            Index = index;
            LocalPolyline = new Polyline(poly);
            ChordLength = chord;
            TotalTurning = totalTurning;
            Sign = sign;
            TurningSignature = (double[])turningSignature.Clone();
            CurvatureSignature = (double[])curvatureSignature.Clone();
            TorsionSignature = torsionSignature == null
                ? null
                : (double[])torsionSignature.Clone();
            PanelPlanarityRms = panelPlanarityRms;
        }
    }
}
