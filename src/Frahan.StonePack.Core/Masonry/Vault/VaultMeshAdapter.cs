#nullable disable
using System;
using Rhino.Geometry;

namespace Frahan.Masonry.Vault
{
    // =========================================================================
    // VaultMeshAdapter — the SubD cage / quad mesh is the SINGLE SOURCE OF TRUTH;
    // the ANALYSIS view (triangle mesh -> TnaForceDensity3D + ThrustField, which are
    // triangle-native) and the FABRICATION view (quad mesh -> VaultQuadCourses course
    // CRA + BuildArchOnSurface) are two derivations of it with IDENTICAL vertices and
    // topology -- so holes (boundary loops) and field singularities are the SAME across
    // both, which is the invariant the tri<->quad unification rests on. (Unification
    // canvas, 2026-06-30; Bhooshan SubD-cage workflow + the BRG Armadillo force-aligned
    // quad mesh, AAG 2016, which reads orientation/singularities/spacing off ONE mesh.)
    // =========================================================================
    public sealed class CageViews
    {
        public Mesh QuadView;     // quad-dominant -> VaultQuadCourses / fabrication
        public Mesh TriView;      // triangulated  -> ThrustField / TnaForceDensity3D / FieldAlignedParam
        public int BoundaryLoops; // naked-edge loops (1 outer + 1 per hole)
        public int Holes;         // BoundaryLoops - 1 (g = 0 shell)
        public int EulerChi;      // V - E + F  (= 1 - Holes for a disk-with-holes)
    }

    public static class VaultMeshAdapter
    {
        /// <summary>
        /// Derive the triangle (analysis) and quad (fabrication) views from a
        /// quad-dominant cage mesh. Both share the welded vertex set, so their
        /// boundary loops + Euler characteristic + singularity structure match.
        /// </summary>
        public static CageViews FromCage(Mesh cage)
        {
            if (cage == null) throw new ArgumentNullException(nameof(cage));

            // canonical repair chain (per the unification canvas: Weld -> UnifyNormals -> Compute)
            var quad = cage.DuplicateMesh();
            quad.Vertices.CombineIdentical(true, true);
            quad.Weld(Math.PI);
            quad.UnifyNormals();
            quad.Normals.ComputeNormals();
            quad.Compact();

            // triangle view: same vertices, quads split to triangles
            var tri = quad.DuplicateMesh();
            tri.Faces.ConvertQuadsToTriangles();
            tri.Normals.ComputeNormals();
            tri.Compact();

            var nk = quad.GetNakedEdges();
            int loops = nk == null ? 0 : nk.Length;
            return new CageViews
            {
                QuadView = quad,
                TriView = tri,
                BoundaryLoops = loops,
                Holes = Math.Max(0, loops - 1),
                EulerChi = quad.Vertices.Count - quad.TopologyEdges.Count + quad.Faces.Count,
            };
        }

        /// <summary>
        /// Derive both views from a Rhino SubD cage: take its quad limit mesh at the
        /// given display density, then split. The designer authors holes (deleted cage
        /// faces) + extraordinary vertices once in the SubD; both views inherit them.
        /// </summary>
        public static CageViews FromSubD(SubD subd, int density = 3)
        {
            if (subd == null) throw new ArgumentNullException(nameof(subd));
            Mesh quad = Mesh.CreateFromSubD(subd, density);
            if (quad == null) throw new InvalidOperationException("SubD -> mesh produced null");
            return FromCage(quad);
        }
    }
}
