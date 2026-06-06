#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Fractures;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// SubdivisionPartition -- uniform (mx, my) partition of a tested-area AABB
// into a regular grid of sub-zones. Each sub-zone is itself an AABB and is
// fed to BlockCutOptSolver independently.
//
// Convention follows Elkarmoty et al. 2020 BlockCutOpt section 2.4:
//   - mx = number of sub-areas in world X
//   - my = number of sub-areas in world Y
//   - i in [1, mx], j in [1, my]
//
// This is Phase 3 of the implementation roadmap in
// `D:\code_ws\wiki\papers\equations_and_diagrams\08_synthesis_and_optimum_algorithm.md`.
// The adaptive density-watershed alternative (I5) lands in Phase 7.
// =============================================================================

/// <summary>
/// One sub-zone of a partitioned tested area, identified by its (i, j) cell
/// indices (1-based to match BlockCutOpt's PAR-file convention).
/// </summary>
public sealed class SubZone
{
    public SubZone(int i, int j, BoundingBox3 aabb)
    {
        if (i < 1) throw new ArgumentOutOfRangeException(nameof(i));
        if (j < 1) throw new ArgumentOutOfRangeException(nameof(j));
        if (aabb == null) throw new ArgumentNullException(nameof(aabb));
        I = i; J = j; Aabb = aabb;
    }

    public int I { get; }
    public int J { get; }
    public BoundingBox3 Aabb { get; }
    public string Id => $"({I},{J})";

    public override string ToString() => $"SubZone{Id}: {Aabb}";
}

public static class SubdivisionPartition
{
    /// <summary>
    /// Partition <paramref name="area"/> into an mx by my grid of equal sub-AABBs.
    /// Z range is preserved (vertical sub-division is not supported in v1).
    /// </summary>
    public static IReadOnlyList<SubZone> Uniform(BoundingBox3 area, int mx, int my)
    {
        if (area == null) throw new ArgumentNullException(nameof(area));
        if (mx < 1) throw new ArgumentOutOfRangeException(nameof(mx));
        if (my < 1) throw new ArgumentOutOfRangeException(nameof(my));

        double dx = area.SizeX / mx;
        double dy = area.SizeY / my;
        var list = new List<SubZone>(mx * my);

        for (int j = 1; j <= my; j++)
        {
            for (int i = 1; i <= mx; i++)
            {
                double xMin = area.MinX + (i - 1) * dx;
                double xMax = (i == mx) ? area.MaxX : area.MinX + i * dx;
                double yMin = area.MinY + (j - 1) * dy;
                double yMax = (j == my) ? area.MaxY : area.MinY + j * dy;
                var sub = new BoundingBox3(xMin, yMin, area.MinZ, xMax, yMax, area.MaxZ);
                list.Add(new SubZone(i, j, sub));
            }
        }
        return list;
    }
}
