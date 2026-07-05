using System;
using Frahan.GH.Masonry;

namespace Frahan.Tests;

// GH-component smoke tests for the five new quarry / fracture / interface
// components. Same pattern as MasonryGhComponentTests / AshlarPackGhComponentTests.

static class QuarryFracturesGhComponentTests
{
    // ─── GridFracturePlanesComponent ────────────────────────────────────────

    public static void GridFracturePlanes_ComponentGuid_IsExpectedValue()
    {
        var c = new GridFracturePlanesComponent();
        Assert(c.ComponentGuid == new Guid("E6F7A8B9-CADB-4CDE-F012-345678901234"),
            $"GridFracturePlanesComponent GUID mismatch: {c.ComponentGuid}");
    }

    public static void GridFracturePlanes_Metadata_IsCorrect()
    {
        var c = new GridFracturePlanesComponent();
        Assert(c.Name == "Grid Fracture Planes", $"Name '{c.Name}'");
        Assert(c.Category == "Frahan", $"Category '{c.Category}'");
        Assert(c.SubCategory == "Fracture", $"SubCategory '{c.SubCategory}'");
        Assert(c.Params.Input.Count == 4, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 1, $"Output count {c.Params.Output.Count}");
    }

    // ─── RandomFracturePlanesComponent ──────────────────────────────────────

    public static void RandomFracturePlanes_ComponentGuid_IsExpectedValue()
    {
        var c = new RandomFracturePlanesComponent();
        Assert(c.ComponentGuid == new Guid("F7A8B9CA-DBEC-4DEF-0123-456789012345"),
            $"RandomFracturePlanesComponent GUID mismatch: {c.ComponentGuid}");
    }

    public static void RandomFracturePlanes_Metadata_IsCorrect()
    {
        var c = new RandomFracturePlanesComponent();
        Assert(c.Name == "Random Fracture Planes", $"Name '{c.Name}'");
        Assert(c.SubCategory == "Fracture", $"SubCategory '{c.SubCategory}'");
        Assert(c.Params.Input.Count == 3, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 1, $"Output count {c.Params.Output.Count}");
    }

    // ─── VoronoiFracturePlanesComponent ─────────────────────────────────────

    public static void VoronoiFracturePlanes_ComponentGuid_IsExpectedValue()
    {
        var c = new VoronoiFracturePlanesComponent();
        Assert(c.ComponentGuid == new Guid("A8B9CADB-ECFD-4EF0-1234-567890123456"),
            $"VoronoiFracturePlanesComponent GUID mismatch: {c.ComponentGuid}");
    }

    public static void VoronoiFracturePlanes_Metadata_IsCorrect()
    {
        var c = new VoronoiFracturePlanesComponent();
        Assert(c.Name == "Voronoi Fracture Planes", $"Name '{c.Name}'");
        Assert(c.SubCategory == "Fracture", $"SubCategory '{c.SubCategory}'");
        Assert(c.Params.Input.Count == 1, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 1, $"Output count {c.Params.Output.Count}");
    }

    // ─── QuarryDecomposeComponent ───────────────────────────────────────────

    public static void QuarryDecompose_ComponentGuid_IsExpectedValue()
    {
        var c = new QuarryDecomposeComponent();
        Assert(c.ComponentGuid == new Guid("B9CADBEC-FDAE-4F01-2345-678901234567"),
            $"QuarryDecomposeComponent GUID mismatch: {c.ComponentGuid}");
    }

    public static void QuarryDecompose_Metadata_IsCorrect()
    {
        var c = new QuarryDecomposeComponent();
        Assert(c.Name == "Quarry Decompose", $"Name '{c.Name}'");
        Assert(c.Category == "Frahan", $"Category '{c.Category}'");
        Assert(c.SubCategory == "Block", $"SubCategory '{c.SubCategory}'");
        Assert(c.Params.Input.Count == 5, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 3, $"Output count {c.Params.Output.Count} (Slabs+Parents+Mesh)");
    }

    // ─── AutoInterfacesComponent ────────────────────────────────────────────

    public static void AutoInterfaces_ComponentGuid_IsExpectedValue()
    {
        var c = new AutoInterfacesComponent();
        Assert(c.ComponentGuid == new Guid("CADBECFD-AEBF-4012-3456-789012345678"),
            $"AutoInterfacesComponent GUID mismatch: {c.ComponentGuid}");
    }

    public static void AutoInterfaces_Metadata_IsCorrect()
    {
        var c = new AutoInterfacesComponent();
        Assert(c.Name == "Auto Interfaces", $"Name '{c.Name}'");
        Assert(c.Category == "Frahan", $"Category '{c.Category}'");
        Assert(c.SubCategory == "Masonry", $"SubCategory '{c.SubCategory}'");
        Assert(c.Params.Input.Count == 4, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 1, $"Output count {c.Params.Output.Count}");
    }

    public static void NewQuarryFractureGuids_AreUnique()
    {
        var ids = new[]
        {
            new Guid("E6F7A8B9-CADB-4CDE-F012-345678901234"),
            new Guid("F7A8B9CA-DBEC-4DEF-0123-456789012345"),
            new Guid("A8B9CADB-ECFD-4EF0-1234-567890123456"),
            new Guid("B9CADBEC-FDAE-4F01-2345-678901234567"),
            new Guid("CADBECFD-AEBF-4012-3456-789012345678"),
            new Guid("DBECFDAE-BFCA-4123-4567-89012345678A"),
            new Guid("ECFDAEBF-CADB-4234-5678-9012345678AB"),
        };
        for (int i = 0; i < ids.Length; i++)
            for (int j = i + 1; j < ids.Length; j++)
                Assert(ids[i] != ids[j], $"GUID collision: ids[{i}] == ids[{j}]");
    }

    // ─── Stage C GH components ──────────────────────────────────────────────

    public static void MeshShellSplit_ComponentGuid_IsExpectedValue()
    {
        var c = new MeshShellSplitComponent();
        Assert(c.ComponentGuid == new Guid("DBECFDAE-BFCA-4123-4567-89012345678A"),
            $"MeshShellSplitComponent GUID mismatch: {c.ComponentGuid}");
    }

    public static void MeshShellSplit_Metadata_IsCorrect()
    {
        var c = new MeshShellSplitComponent();
        Assert(c.Name == "Mesh Shell Split", $"Name '{c.Name}'");
        Assert(c.Category == "Frahan", $"Category '{c.Category}'");
        Assert(c.SubCategory == "Block", $"SubCategory '{c.SubCategory}'");
        Assert(c.Params.Input.Count == 1, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 2, $"Output count {c.Params.Output.Count} (Slabs+Mesh)");
    }

    public static void ConvexHullSlab_ComponentGuid_IsExpectedValue()
    {
        var c = new ConvexHullSlabComponent();
        Assert(c.ComponentGuid == new Guid("ECFDAEBF-CADB-4234-5678-9012345678AB"),
            $"ConvexHullSlabComponent GUID mismatch: {c.ComponentGuid}");
    }

    public static void ConvexHullSlab_Metadata_IsCorrect()
    {
        var c = new ConvexHullSlabComponent();
        Assert(c.Name == "Convex Hull Slab", $"Name '{c.Name}'");
        Assert(c.Category == "Frahan", $"Category '{c.Category}'");
        Assert(c.SubCategory == "Block", $"SubCategory '{c.SubCategory}'");
        Assert(c.Params.Input.Count == 1, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 2, $"Output count {c.Params.Output.Count} (Slab+Mesh)");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
