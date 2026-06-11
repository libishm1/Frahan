#nullable disable
using System;
using Frahan.GH.TwoD;

namespace Frahan.Tests;

static class CsvPartsReaderComponentTests
{
    public static void Metadata_IsExpectedValues()
    {
        var c = new CsvPartsReaderComponent();
        Assert(c.ComponentGuid == new Guid("F2D00C5F-CADC-4F2D-9C5F-7E60CADA15A0"),
            $"GUID mismatch: {c.ComponentGuid}");
        Assert(c.Category == "Frahan", $"Category: {c.Category}");
        Assert(c.SubCategory == "2D Packing", $"SubCategory: {c.SubCategory}");
        Assert(c.Name == "CSV Parts Reader", $"Name: {c.Name}");
        Assert(c.Params.Input.Count == 3, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 5, $"Output count {c.Params.Output.Count}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
