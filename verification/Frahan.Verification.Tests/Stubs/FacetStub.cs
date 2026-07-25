// Minimal stand-in for Frahan.Core.Discontinuity.Facet, in the SAME namespace.
// Only the members SetClusterer.Cluster actually reads: .Normal, .PointCount,
// .Centroid. The real Facet (PointCloudFacetExtractor.cs) also carries Dip,
// DipDir, Rms, AreaProxy, but the clusterer never touches those, so linking the
// real file (which drags kNN/region-grow deps) is unnecessary.
// (Ported verbatim from outputs/2026-07-24/clustering_verification.)
using Rhino.Geometry;

namespace Frahan.Core.Discontinuity
{
    public sealed class Facet
    {
        public Vector3d Normal;
        public Point3d Centroid;
        public int[] PointIndices;
        public int PointCount => PointIndices == null ? 0 : PointIndices.Length;
    }
}
