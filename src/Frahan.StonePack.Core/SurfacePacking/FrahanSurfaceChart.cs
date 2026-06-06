#nullable disable
using Rhino.Geometry;

namespace Frahan.Surface
{
    /// <summary>
    /// Immutable result of a surface charting operation.
    /// Holds both the original 3D mesh and its 2D UV flattening (flat mesh),
    /// plus the scale factor that converts UV distances to real-world distances.
    /// Pass this object between the SurfaceChart and PackOnSurface GH components.
    /// </summary>
    public sealed class FrahanSurfaceChart
    {
        /// <summary>Original 3D surface mesh (cleaned, triangulated).</summary>
        public Mesh SurfaceMesh3D { get; }

        /// <summary>
        /// 2D flat mesh (Z=0 plane, un-welded face corners), already scaled to real units.
        /// Face i in FlatMesh corresponds exactly to face i in SurfaceMesh3D.
        /// Pass directly to BarycentricMapper2DTo3D — no further scaling needed.
        /// </summary>
        public Mesh FlatMesh { get; }

        /// <summary>
        /// Scale factor: totalSurfaceEdgeLength / totalFlatEdgeLength.
        /// FlatMesh is already stored post-scale. Use ChartScale only when converting
        /// raw BFF UV coordinates [0,1] to real units for informational purposes.
        /// </summary>
        public double ChartScale { get; }

        /// <summary>
        /// Outer boundary of the flat chart, in real units (Z=0 plane).
        /// Null if boundary extraction failed.
        /// </summary>
        public Polyline FlatOuterBoundary { get; }

        /// <summary>Distortion analysis report.</summary>
        public ChartDistortionReport Distortion { get; }

        public FrahanSurfaceChart(
            Mesh surfaceMesh3D,
            Mesh flatMesh,
            double chartScale,
            Polyline flatOuterBoundary,
            ChartDistortionReport distortion)
        {
            SurfaceMesh3D = surfaceMesh3D;
            FlatMesh = flatMesh;
            ChartScale = chartScale;
            FlatOuterBoundary = flatOuterBoundary;
            Distortion = distortion;
        }

        /// <summary>
        /// Extracts the outer boundary of the flat mesh as a polyline.
        /// Returns null if the mesh has no naked edges or GetNakedEdges fails.
        /// </summary>
        public static Polyline ExtractOuterBoundary(Mesh flatMesh)
        {
            if (flatMesh == null) return null;

            var nakedEdges = flatMesh.GetNakedEdges();
            if (nakedEdges == null || nakedEdges.Length == 0) return null;

            // Longest polyline = outer boundary; shorter ones = holes
            Polyline longest = nakedEdges[0];
            for (int i = 1; i < nakedEdges.Length; i++)
            {
                if (nakedEdges[i].Length > longest.Length)
                    longest = nakedEdges[i];
            }

            return longest;
        }
    }
}
