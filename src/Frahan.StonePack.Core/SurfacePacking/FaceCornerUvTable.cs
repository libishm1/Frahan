using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Surface
{
    /// <summary>
    /// Key type for (faceIndex, cornerIndex) UV lookups.
    /// Uses manual GetHashCode to avoid boxing on .NET Framework 4.8.
    /// </summary>
    public readonly struct FaceCornerKey : IEquatable<FaceCornerKey>
    {
        public readonly int FaceIndex;
        public readonly int CornerIndex; // 0 = A, 1 = B, 2 = C

        public FaceCornerKey(int faceIndex, int cornerIndex)
        {
            FaceIndex = faceIndex;
            CornerIndex = cornerIndex;
        }

        public bool Equals(FaceCornerKey other) =>
            FaceIndex == other.FaceIndex && CornerIndex == other.CornerIndex;

        public override bool Equals(object obj) =>
            obj is FaceCornerKey other && Equals(other);

        public override int GetHashCode() => (FaceIndex * 397) ^ CornerIndex;
    }

    /// <summary>
    /// Stores UV coordinates per (faceIndex, cornerIndex).
    /// This is the only correct way to handle UV seams from BFF output —
    /// a single vertex may appear with different UVs on either side of a seam cut.
    /// Never default a missing UV to (0,0): throw explicitly so bugs surface immediately.
    /// </summary>
    public sealed class FaceCornerUvTable
    {
        private readonly Dictionary<FaceCornerKey, Point2d> _data =
            new Dictionary<FaceCornerKey, Point2d>();

        public void SetUv(int faceIndex, int cornerIndex, double u, double v) =>
            _data[new FaceCornerKey(faceIndex, cornerIndex)] = new Point2d(u, v);

        public bool TryGetUv(int faceIndex, int cornerIndex, out Point2d uv) =>
            _data.TryGetValue(new FaceCornerKey(faceIndex, cornerIndex), out uv);

        public int EntryCount => _data.Count;

        /// <summary>
        /// Builds an un-welded 2D flat mesh in the Z=0 plane.
        /// Each face corner becomes its own vertex so UV seams are never bridged.
        /// Face i in the output corresponds exactly to face i in original3D.
        /// Throws InvalidOperationException if any UV is missing for a triangular face.
        /// </summary>
        public Mesh ToFlatUnweldedMesh(Mesh original3D)
        {
            if (original3D == null) throw new ArgumentNullException(nameof(original3D));

            var flat = new Mesh();

            for (int i = 0; i < original3D.Faces.Count; i++)
            {
                if (!original3D.Faces[i].IsTriangle) continue;

                if (!TryGetUv(i, 0, out Point2d uvA))
                    throw new InvalidOperationException($"Missing UV at face {i}, corner 0.");
                if (!TryGetUv(i, 1, out Point2d uvB))
                    throw new InvalidOperationException($"Missing UV at face {i}, corner 1.");
                if (!TryGetUv(i, 2, out Point2d uvC))
                    throw new InvalidOperationException($"Missing UV at face {i}, corner 2.");

                int v0 = flat.Vertices.Count;
                flat.Vertices.Add(uvA.X, uvA.Y, 0.0);
                flat.Vertices.Add(uvB.X, uvB.Y, 0.0);
                flat.Vertices.Add(uvC.X, uvC.Y, 0.0);
                flat.Faces.AddFace(v0, v0 + 1, v0 + 2);
            }

            flat.FaceNormals.ComputeFaceNormals();
            return flat;
        }
    }
}
