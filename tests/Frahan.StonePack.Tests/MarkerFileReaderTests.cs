#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Frahan.Core.Registration;

namespace Frahan.Tests;

// Pure-managed tests for photogrammetry marker / GCP CSV parsing (no Rhino).
static class MarkerFileReaderTests
{
    public static void Parse_WorldOnly_FourColumns()
    {
        var m = MarkerFileReader.Parse(new[] { "P1,1,2,3" });
        Assert(m.Count == 1, "one marker");
        Assert(m[0].Label == "P1", "label");
        Assert(m[0].World[0] == 1 && m[0].World[1] == 2 && m[0].World[2] == 3, "world coords");
        Assert(!m[0].HasModel, "no model");
    }

    public static void Parse_WorldAndModel_SevenColumns()
    {
        var m = MarkerFileReader.Parse(new[] { "P2, 1, 2, 3, 4, 5, 6" });
        Assert(m.Count == 1 && m[0].HasModel, "has model");
        Assert(m[0].World[2] == 3, "world z");
        Assert(m[0].Model[0] == 4 && m[0].Model[2] == 6, "model coords");
    }

    public static void Parse_NumericFirst_AutoLabels()
    {
        var m = MarkerFileReader.Parse(new[] { "1,2,3", "4,5,6" });
        Assert(m.Count == 2, "two markers");
        Assert(m[0].Label.StartsWith("M") && m[1].Label.StartsWith("M"), "auto labels");
        Assert(m[0].World[0] == 1 && m[1].World[0] == 4, "world coords by row");
    }

    public static void Parse_SkipsCommentsAndBlanks()
    {
        var m = MarkerFileReader.Parse(new[] { "# header", "", "  ", "// note", "P1,1,2,3" });
        Assert(m.Count == 1 && m[0].Label == "P1", "only the data row parses");
    }

    public static void Parse_TooFewColumns_Skipped()
    {
        var m = MarkerFileReader.Parse(new[] { "P,1,2", "P3,7,8,9" });
        Assert(m.Count == 1 && m[0].Label == "P3", "row with <3 numeric cols skipped");
    }

    public static void Parse_SemicolonAndTabSeparators()
    {
        var m = MarkerFileReader.Parse(new[] { "P1;1;2;3", "P2\t4\t5\t6" });
        Assert(m.Count == 2 && m[1].World.SequenceEqual(new double[] { 4, 5, 6 }), "alt separators");
    }

    private static void Assert(bool c, string msg) { if (!c) throw new InvalidOperationException("MarkerFileReader: " + msg); }
}
