#nullable disable
using System;
using Frahan.GH.Masonry.Sequencing;

namespace Frahan.Tests;

// =============================================================================
// PolygonalMasonrySequence3DComponentTests — metadata smoke test for the
// 3D sequencing component. Mirrors the 2D version's metadata-only style.
// Full SolveInstance needs a live Rhino + Grasshopper canvas and is
// covered by truth criterion (c).
// =============================================================================

static class PolygonalMasonrySequence3DComponentTests
{
    public static void Metadata_IsExpectedValues()
    {
        var c = new PolygonalMasonrySequence3DComponent();
        Assert(c.ComponentGuid == new Guid("C5F18B4D-8A6F-4E72-AC83-1FBD32D8C7B2"),
            $"GUID mismatch: {c.ComponentGuid}");
        Assert(c.Category == "Frahan",
            $"Category expected 'Frahan', got '{c.Category}'");
        Assert(c.SubCategory == "Masonry",
            $"SubCategory expected 'Masonry', got '{c.SubCategory}'");
        Assert(c.Name == "Polygonal Masonry Sequence 3D",
            $"Name expected 'Polygonal Masonry Sequence 3D', got '{c.Name}'");
        Assert(c.Params.Input.Count == 4,
            $"Input count expected 4, got {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 5,
            $"Output count expected 5, got {c.Params.Output.Count}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
