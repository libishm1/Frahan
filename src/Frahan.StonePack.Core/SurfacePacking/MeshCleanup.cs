#nullable disable
using System;
using Rhino.Geometry;

namespace Frahan.Surface
{
    public static class MeshCleanup
    {
        /// <summary>
        /// Prepares a mesh for BFF unwrapping: triangulates, deduplicates vertices,
        /// culls degenerate faces, and recomputes normals.
        /// Does NOT weld with Math.PI or force solid orientation — both break open chart boundaries.
        /// </summary>
        public static bool TryCleanMesh(Mesh mesh, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (mesh == null || !mesh.IsValid)
            {
                errorMessage = "Input mesh is null or invalid.";
                return false;
            }

            try
            {
                mesh.Faces.ConvertQuadsToTriangles();
                mesh.Compact();
                mesh.Vertices.CombineIdentical(true, true);
                mesh.Vertices.CullUnused();
                mesh.Faces.CullDegenerateFaces();
                mesh.FaceNormals.ComputeFaceNormals();
                mesh.Normals.ComputeNormals();
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Mesh cleanup failed: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Converts a BrepFace to a clean triangulated mesh at the given density.
        /// density controls the maximum edge length: lower = finer mesh.
        /// </summary>
        public static bool TryMeshBrepFace(BrepFace face, double density, out Mesh mesh, out string errorMessage)
        {
            mesh = null;
            errorMessage = string.Empty;

            if (face == null)
            {
                errorMessage = "BrepFace is null.";
                return false;
            }

            try
            {
                var meshParams = new MeshingParameters(density);
                var meshes = Mesh.CreateFromBrep(face.Brep, meshParams);
                if (meshes == null || meshes.Length == 0)
                {
                    errorMessage = "Meshing produced no output.";
                    return false;
                }

                mesh = new Mesh();
                foreach (var m in meshes) mesh.Append(m);

                return TryCleanMesh(mesh, out errorMessage);
            }
            catch (Exception ex)
            {
                errorMessage = $"BrepFace meshing failed: {ex.Message}";
                return false;
            }
        }

        public static Mesh[] DecomposeDisjointMeshes(Mesh mesh, bool decompose)
        {
            if (mesh == null) return Array.Empty<Mesh>();
            if (!decompose) return new[] { mesh };

            var pieces = mesh.SplitDisjointPieces();
            return (pieces == null || pieces.Length == 0) ? new[] { mesh } : pieces;
        }
    }
}
