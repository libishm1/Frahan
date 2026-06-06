#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Quarry.Monuments;

// =============================================================================
// MonumentOrientationSampler -- the 24 axis-aligned rotations of the cube
// (rotational octahedral group O, order 24). For each rotation we apply a
// 3x3 integer matrix to the monument's local axes; the packer then computes
// the AABB of the rotated mesh from these axes and the original vertex set.
//
// The 24 orientations correspond to choosing any of the 6 unit-axis
// directions {+X, -X, +Y, -Y, +Z, -Z} as the "world +Z" of the monument
// (6 choices) and then any of the 4 rotations around that axis (4 choices).
//
// Each rotation is stored as a 3x3 matrix in row-major flat-9 form:
// new[] { m00,m01,m02,  m10,m11,m12,  m20,m21,m22 }. The matrix maps local
// axes (xL, yL, zL) to world (xW, yW, zW) via xW = M * xL.
//
// All entries are -1, 0, or +1, so multiplication is exact (no floating
// point drift); the rotated AABB has the same precision as the input AABB.
// =============================================================================

public static class MonumentOrientationSampler
{
    private static readonly int[][] _rotations = BuildRotations();

    /// <summary>The 24 axis-aligned rotation matrices (3×3 row-major).</summary>
    public static IReadOnlyList<IReadOnlyList<int>> Rotations => _rotations;

    public static int Count => _rotations.Length;

    /// <summary>
    /// Get one rotation as a flat-9 int array (row-major).
    /// </summary>
    public static int[] Get(int index)
    {
        if (index < 0 || index >= _rotations.Length)
            throw new ArgumentOutOfRangeException(nameof(index), $"0..{_rotations.Length - 1}");
        return _rotations[index];
    }

    /// <summary>
    /// Apply rotation <paramref name="index"/> to vertex coords and return
    /// the rotated AABB extents (dx, dy, dz). The output AABB is positioned
    /// so its min corner is at (0, 0, 0).
    /// </summary>
    public static void RotatedAabb(
        int index,
        double localMinX, double localMinY, double localMinZ,
        double localMaxX, double localMaxY, double localMaxZ,
        out double dx, out double dy, out double dz)
    {
        var R = Get(index);
        // For an axis-aligned rotation with entries in {-1, 0, 1}, the rotated
        // AABB has extent |row . (size_x, size_y, size_z)| along each axis.
        // Since each row has exactly one non-zero entry, |row . sizes| simplifies
        // to |non-zero entry| * size of the corresponding axis.
        double sx = localMaxX - localMinX;
        double sy = localMaxY - localMinY;
        double sz = localMaxZ - localMinZ;
        dx = Math.Abs(R[0] * sx) + Math.Abs(R[1] * sy) + Math.Abs(R[2] * sz);
        dy = Math.Abs(R[3] * sx) + Math.Abs(R[4] * sy) + Math.Abs(R[5] * sz);
        dz = Math.Abs(R[6] * sx) + Math.Abs(R[7] * sy) + Math.Abs(R[8] * sz);
    }

    /// <summary>
    /// Apply rotation to a single point (x, y, z) in local frame. World point
    /// is R * (x, y, z).
    /// </summary>
    public static void RotatePoint(
        int index,
        double x, double y, double z,
        out double wx, out double wy, out double wz)
    {
        var R = Get(index);
        wx = R[0] * x + R[1] * y + R[2] * z;
        wy = R[3] * x + R[4] * y + R[5] * z;
        wz = R[6] * x + R[7] * y + R[8] * z;
    }

    private static int[][] BuildRotations()
    {
        // Enumerate the 24 axis-aligned rotations by picking
        //   col0 = where local +X lands in world space  (6 choices)
        //   col1 = where local +Y lands                  (4 choices left after dropping col0's axis ± sign)
        //   col2 = col0 × col1 (right-handed, forced)
        var axes = new int[][]
        {
            new[] { 1, 0, 0 }, new[] { -1, 0, 0 },
            new[] { 0, 1, 0 }, new[] { 0, -1, 0 },
            new[] { 0, 0, 1 }, new[] { 0, 0, -1 },
        };
        var output = new List<int[]>(24);
        foreach (var col0 in axes)
        {
            foreach (var col1 in axes)
            {
                if (Math.Abs(col0[0] * col1[0] + col0[1] * col1[1] + col0[2] * col1[2]) > 0) continue; // not perpendicular
                int c2x = col0[1] * col1[2] - col0[2] * col1[1];
                int c2y = col0[2] * col1[0] - col0[0] * col1[2];
                int c2z = col0[0] * col1[1] - col0[1] * col1[0];
                output.Add(new[]
                {
                    col0[0], col1[0], c2x,
                    col0[1], col1[1], c2y,
                    col0[2], col1[2], c2z,
                });
            }
        }
        if (output.Count != 24)
            throw new InvalidOperationException($"expected 24 axis-aligned rotations, built {output.Count}");
        return output.ToArray();
    }
}
