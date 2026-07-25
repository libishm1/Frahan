// Minimal stand-in for Frahan.Masonry.Cutting.Slab, in the SAME namespace.
// The only reference to Slab in the linked SawBed dependency chain is
// BoundingBox3.FromSlab (a convenience factory reading slab.VertexCount and
// slab.VertexCoordsXyz). The SawBed schedule tests build BenchBlock footprints
// from the plain 6-double BoundingBox3 constructor and never call FromSlab, so
// only Slab's referenced members need to exist for the compile. Matches the
// Stubs/ philosophy (FacetStub, PlyMeshStub): supply the symbol the linked
// kernel references but the tests never exercise.

namespace Frahan.Masonry.Cutting
{
    public sealed class Slab
    {
        public int VertexCount { get; set; }
        public double[] VertexCoordsXyz { get; set; }
    }
}
