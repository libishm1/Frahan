#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.DataModel;

namespace Frahan.Masonry.Packing;

/// <summary>
/// Output of <see cref="AshlarLayoutEngine.Pack"/>. Carries the assembly that
/// downstream stability solvers consume, plus the diagnostics that the
/// Stage 3 components surface.
/// </summary>
public sealed class AshlarPackResult
{
    public AshlarPackResult(
        MasonryAssembly assembly,
        IReadOnlyList<MasonryBlock> placedBlocks,
        IReadOnlyList<Slab> leftovers,
        int courseCount,
        double coverageRatio,
        IReadOnlyList<string> notes)
    {
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));
        if (placedBlocks == null) throw new ArgumentNullException(nameof(placedBlocks));
        if (leftovers == null) throw new ArgumentNullException(nameof(leftovers));
        if (notes == null) throw new ArgumentNullException(nameof(notes));
        if (courseCount < 0)
            throw new ArgumentOutOfRangeException(nameof(courseCount), "must be >= 0");
        if (!(coverageRatio >= 0.0 && coverageRatio <= 1.0 + 1e-9))
            throw new ArgumentOutOfRangeException(nameof(coverageRatio),
                $"must be in [0, 1], got {coverageRatio}");

        Assembly = assembly;
        PlacedBlocks = placedBlocks;
        Leftovers = leftovers;
        CourseCount = courseCount;
        CoverageRatio = coverageRatio;
        Notes = notes;
    }

    public MasonryAssembly Assembly { get; }
    public IReadOnlyList<MasonryBlock> PlacedBlocks { get; }
    public IReadOnlyList<Slab> Leftovers { get; }
    public int CourseCount { get; }
    public double CoverageRatio { get; }
    public IReadOnlyList<string> Notes { get; }

    public override string ToString() =>
        $"AshlarPackResult(courses={CourseCount}, placed={PlacedBlocks.Count}, " +
        $"leftovers={Leftovers.Count}, coverage={CoverageRatio:0.###})";
}
