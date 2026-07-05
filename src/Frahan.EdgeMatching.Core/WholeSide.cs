#nullable disable
using Rhino.Geometry;

namespace Frahan.EdgeMatching
{
    /// <summary>
    /// One contour side of a panel: the boundary arc between two detected corners,
    /// resampled to K points in an endpoint-chord CANONICAL frame (start corner at
    /// the origin, end corner on +x). This is the unit the whole-side matcher scores,
    /// in contrast to the curvature-broken <see cref="Segment"/> fragments the hash
    /// pipeline uses. Side shape (the whole seam wave) is the discriminative signal.
    /// </summary>
    internal sealed class WholeSide
    {
        /// <summary>Owner panel id.</summary>
        public string PanelId;

        /// <summary>0..3 side index in corner order (the parent/child side in the frontier key).</summary>
        public int SideIndex;

        /// <summary>Start corner in PANEL-LOCAL coords (Panel.SourceContour frame); used for the seam mate.</summary>
        public Point3d StartCorner;

        /// <summary>End corner in PANEL-LOCAL coords; used for the seam mate.</summary>
        public Point3d EndCorner;

        /// <summary>Straight chord length between the two corners (in the panel's local plane).</summary>
        public double ChordLength;

        /// <summary>True when the side is a flat border edge (maxPerpDev/chord &lt; threshold);
        /// flat sides are excluded from matching (every straight border matches at ~0).</summary>
        public bool IsFlat;

        /// <summary>K canonical points, side traversed forward.</summary>
        public Point2d[] Canonical;

        /// <summary>K canonical points, side traversed reversed (the complementary-seam orientation).</summary>
        public Point2d[] CanonicalFlipped;
    }
}
