#nullable disable
using System;
using Frahan.GH.Masonry.Sequencing;

namespace Frahan.Tests;

// =============================================================================
// PolygonalMasonrySequenceComponentTests — metadata smoke tests for the
// Polygonal Masonry Sequence component. Mirrors the metadata-only smoke
// tests used elsewhere in this assembly (e.g. BlockBuildOrderComponent
// Gh_BlockBuildOrderComponent_Metadata): construct the component, verify
// GUID + category + IO param counts. The full SolveInstance pass needs a
// live Grasshopper canvas and is covered by truth criterion (c) on a
// Rhino session, not by this headless test runner.
// =============================================================================

static class PolygonalMasonrySequenceComponentTests
{
    public static void Metadata_IsExpectedValues()
    {
        var c = new PolygonalMasonrySequenceComponent();
        Assert(c.ComponentGuid == new Guid("B4E07A3C-7F4D-4E5B-9C71-0EAF21C9B6A1"),
            $"GUID mismatch: {c.ComponentGuid}");
        Assert(c.Category == "Frahan",
            $"Category expected 'Frahan', got '{c.Category}'");
        Assert(c.SubCategory == "Masonry",
            $"SubCategory expected 'Masonry', got '{c.SubCategory}'");
        Assert(c.Name == "Polygonal Masonry Sequence",
            $"Name expected 'Polygonal Masonry Sequence', got '{c.Name}'");
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
