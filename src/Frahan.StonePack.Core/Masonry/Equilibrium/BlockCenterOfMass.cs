#nullable disable
using System;
using Frahan.Masonry.DataModel;

namespace Frahan.Masonry.Equilibrium;

/// <summary>
/// Centroid (centre of mass) of a <see cref="MasonryBlock"/>. Two flavours:
/// vertex-mean (cheap, exact for axis-aligned uniform-density boxes) and
/// volume-weighted via the divergence-theorem tetrahedra trick (matches
/// compas_cra's <c>block.center()</c> behaviour for arbitrary closed
/// triangulated meshes). The volume-weighted form is what feeds the moment-
/// balance constraints in the equilibrium matrix.
/// </summary>
public static class BlockCenterOfMass
{
    /// <summary>
    /// Average of vertex positions. Cheap; exact only for shapes with the
    /// vertex sample at the geometric centroid (regular boxes, tetrahedra).
    /// </summary>
    public static (double X, double Y, double Z) VertexMean(MasonryBlock block)
    {
        if (block == null) throw new ArgumentNullException(nameof(block));
        int n = block.VertexCount;
        if (n == 0) throw new ArgumentException("block has zero vertices", nameof(block));

        double sx = 0, sy = 0, sz = 0;
        var v = block.VertexCoordsXyz;
        for (int i = 0; i < n; i++)
        {
            sx += v[3 * i];
            sy += v[3 * i + 1];
            sz += v[3 * i + 2];
        }
        return (sx / n, sy / n, sz / n);
    }

    /// <summary>
    /// Volume-weighted centroid via signed-tetrahedron decomposition (origin
    /// to each triangle). Correct for any closed, well-oriented (outward-
    /// normal) triangulation. Returns <see cref="VertexMean"/> as a
    /// fallback when total signed volume is degenerate (planar input,
    /// open mesh).
    /// </summary>
    /// <param name="signedVolume">Out: signed volume V; if &lt; <c>volumeEps</c>, the centroid falls back to <see cref="VertexMean"/> and this value reports zero.</param>
    public static (double X, double Y, double Z) VolumeWeighted(
        MasonryBlock block,
        out double signedVolume,
        double volumeEps = 1e-12)
    {
        if (block == null) throw new ArgumentNullException(nameof(block));

        var v = block.VertexCoordsXyz;
        var t = block.TriangleIndices;
        int triCount = block.TriangleCount;

        double V = 0.0;
        double cx = 0.0, cy = 0.0, cz = 0.0;

        for (int f = 0; f < triCount; f++)
        {
            int i0 = t[3 * f];
            int i1 = t[3 * f + 1];
            int i2 = t[3 * f + 2];

            double ax = v[3 * i0], ay = v[3 * i0 + 1], az = v[3 * i0 + 2];
            double bx = v[3 * i1], by = v[3 * i1 + 1], bz = v[3 * i1 + 2];
            double cxv = v[3 * i2], cyv = v[3 * i2 + 1], czv = v[3 * i2 + 2];

            // Signed volume of tet (origin, a, b, c) = (a . (b x c)) / 6
            double crossx = by * czv - bz * cyv;
            double crossy = bz * cxv - bx * czv;
            double crossz = bx * cyv - by * cxv;
            double tetV6 = ax * crossx + ay * crossy + az * crossz; // 6 * V_tet
            V += tetV6;
            // Centroid of tet (origin, a, b, c) is (a + b + c) / 4
            cx += tetV6 * (ax + bx + cxv);
            cy += tetV6 * (ay + by + cyv);
            cz += tetV6 * (az + bz + czv);
        }

        signedVolume = V / 6.0;

        if (Math.Abs(signedVolume) < volumeEps)
        {
            signedVolume = 0.0;
            return VertexMean(block);
        }

        // V was 6V_total above, so the (V_tet * centroid_tet * 4) accumulator
        // needs division by (V_total * 4 * 6) to get the weighted average.
        // Equivalently: cx accumulator / (V_total * 24).
        double inv = 1.0 / (signedVolume * 24.0);
        return (cx * inv, cy * inv, cz * inv);
    }

    /// <summary>
    /// Total signed volume from the divergence-theorem decomposition.
    /// Negative or near-zero values indicate inward-flipped or non-closed
    /// triangulations.
    /// </summary>
    public static double SignedVolume(MasonryBlock block)
    {
        VolumeWeighted(block, out double v);
        return v;
    }
}
