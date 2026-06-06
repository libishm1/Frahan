using System;
using Rhino.Geometry;

namespace Frahan.Surface
{
    /// <summary>
    /// Computes the scale factor needed to convert normalized UV coordinates
    /// (BFF output with --normalizeUVs) into real-world surface distances.
    /// Uses the ratio of total 3D edge length to total 2D UV edge length,
    /// averaged across all triangles. Returns 1.0 with a warning if inputs are invalid.
    /// </summary>
    public static class ChartScaleComputer
    {
        public static double ComputeGlobalScale(Mesh flatMesh, Mesh surfaceMesh, out string warning)
        {
            warning = string.Empty;

            if (flatMesh == null || surfaceMesh == null)
            {
                warning = "Cannot compute chart scale: null mesh input.";
                return 1.0;
            }

            if (flatMesh.Faces.Count != surfaceMesh.Faces.Count)
            {
                warning = $"Cannot compute chart scale: face count mismatch " +
                          $"(flat={flatMesh.Faces.Count}, surface={surfaceMesh.Faces.Count}).";
                return 1.0;
            }

            double totalFlat3D = 0.0;
            double totalSurf3D = 0.0;
            int validFaces = 0;

            for (int i = 0; i < flatMesh.Faces.Count; i++)
            {
                if (!flatMesh.Faces[i].IsTriangle || !surfaceMesh.Faces[i].IsTriangle) continue;

                Point3d fA = flatMesh.Vertices[flatMesh.Faces[i].A];
                Point3d fB = flatMesh.Vertices[flatMesh.Faces[i].B];
                Point3d fC = flatMesh.Vertices[flatMesh.Faces[i].C];

                Point3d sA = surfaceMesh.Vertices[surfaceMesh.Faces[i].A];
                Point3d sB = surfaceMesh.Vertices[surfaceMesh.Faces[i].B];
                Point3d sC = surfaceMesh.Vertices[surfaceMesh.Faces[i].C];

                totalFlat3D += fA.DistanceTo(fB) + fB.DistanceTo(fC) + fC.DistanceTo(fA);
                totalSurf3D += sA.DistanceTo(sB) + sB.DistanceTo(sC) + sC.DistanceTo(sA);
                validFaces++;
            }

            if (validFaces == 0 || totalFlat3D < 1e-12)
            {
                warning = "Cannot compute chart scale: no valid triangles or degenerate flat mesh.";
                return 1.0;
            }

            return totalSurf3D / totalFlat3D;
        }
    }
}
