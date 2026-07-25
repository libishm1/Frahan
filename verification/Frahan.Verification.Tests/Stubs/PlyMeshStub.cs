using System.Collections.Generic;

// Minimal stub so the linked ConvexPolyhedron.cs compiles standalone.
// ConvexPolyhedron.ToPlyMesh() is NOT exercised by the verification suite;
// only its return type needs to exist. This does not alter any code path
// under test. (Ported verbatim from outputs/2026-07-24/clip_verification.)
namespace Frahan.Masonry.Geometry
{
    public sealed class PlyMesh
    {
        public readonly List<double> Vertices;
        public readonly List<int> Triangles;
        public readonly object Extra;
        public PlyMesh(List<double> vertices, List<int> triangles, object extra)
        {
            Vertices = vertices;
            Triangles = triangles;
            Extra = extra;
        }
    }
}
