#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Core.Fabrication;

namespace Frahan.Tests;

// Pure-managed tests for the stone-cut metadata payload (no Rhino).
static class StoneCutMetadataTests
{
    public static void ToUserStrings_AlwaysEmitsSchema()
    {
        var d = ToDict(new StoneCutMetadata());
        Assert(d.ContainsKey(StoneCutMetadata.KeySchema)
               && d[StoneCutMetadata.KeySchema] == StoneCutMetadata.SchemaValue,
               "schema key always present");
    }

    public static void ToUserStrings_SkipsUnsetFields()
    {
        var d = ToDict(new StoneCutMetadata { PieceId = "S001" }); // weight/kerf NaN, others null
        Assert(d.ContainsKey(StoneCutMetadata.KeyPieceId), "id present");
        Assert(!d.ContainsKey(StoneCutMetadata.KeyWeightKg), "NaN weight skipped");
        Assert(!d.ContainsKey(StoneCutMetadata.KeyKerfMm), "NaN kerf skipped");
        Assert(!d.ContainsKey(StoneCutMetadata.KeyBedDir), "null bed dir skipped");
        Assert(!d.ContainsKey(StoneCutMetadata.KeyStone), "null stone skipped");
    }

    public static void ToUserStrings_EmitsPopulatedFields()
    {
        var d = ToDict(new StoneCutMetadata
        {
            PieceId = "S002", Stone = "TN Granite", Finish = "polished",
            BedDirection = new[] { 0.0, 0, 1 }, WeightKg = 42.5, KerfMm = 3.0, Provenance = "block7",
        });
        Assert(d[StoneCutMetadata.KeyStone] == "TN Granite", "stone");
        Assert(d[StoneCutMetadata.KeyFinish] == "polished", "finish");
        Assert(d.ContainsKey(StoneCutMetadata.KeyBedDir), "bed dir present");
        Assert(d.ContainsKey(StoneCutMetadata.KeyWeightKg), "weight present");
        Assert(d[StoneCutMetadata.KeyProvenance] == "block7", "provenance");
    }

    private static Dictionary<string, string> ToDict(StoneCutMetadata m)
    {
        var d = new Dictionary<string, string>();
        foreach (var kv in m.ToUserStrings()) d[kv.Key] = kv.Value;
        return d;
    }

    private static void Assert(bool c, string msg) { if (!c) throw new InvalidOperationException("StoneCutMetadata: " + msg); }
}
