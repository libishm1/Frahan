#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Core.Reports;

namespace Frahan.Tests;

// Unit tests for Frahan.Core.Reports.AudienceReportComposer (W1, keep-or-cut).
// Pure managed; no Rhino runtime required.
static class AudienceReportTests
{
    private static ReportProvenance Prov(string crs)
    {
        return new ReportProvenance
        {
            Site = "Granite Dells",
            ScanFile = "ot_GD_TLS_data_UTM.laz",
            DateText = "2026-06-05",
            CrsDatum = crs,
            Units = "m",
            SolverVersion = "0.7.0"
        };
    }

    public static void Engineer_NoCrs_Refused()
    {
        var res = AudienceReportComposer.Compose(
            ReportAudience.Engineer, Prov(null), null, null, ReportFormat.Both);
        Assert(res.Refused, "engineer report must be refused when no CRS is declared");
        Assert(res.RefusalReason != null && res.RefusalReason.Length > 0, "refusal must carry a reason");
        Assert(res.Markdown.IndexOf("refused", StringComparison.OrdinalIgnoreCase) >= 0,
            "refused markdown should say so");
    }

    public static void Engineer_WithCrs_NotRefused()
    {
        var res = AudienceReportComposer.Compose(
            ReportAudience.Engineer, Prov("UTM 12N / NAD83"), null, null, ReportFormat.Both);
        Assert(!res.Refused, "engineer report with a CRS must not be refused");
        Assert(res.Markdown.IndexOf("Engineer", StringComparison.Ordinal) >= 0,
            "markdown should name the audience");
        Assert(res.Markdown.IndexOf("UTM 12N", StringComparison.Ordinal) >= 0,
            "provenance CRS should appear in the report");
        // No risk section supplied -> the composer must add the P50/P90 caveat.
        Assert(res.Markdown.IndexOf("P90", StringComparison.Ordinal) >= 0,
            "engineer report should add the P50/P90 risk caveat when none supplied");
    }

    public static void Artist_AddsGrainVeinCaveat()
    {
        var res = AudienceReportComposer.Compose(
            ReportAudience.Artist, Prov(null), null, null, ReportFormat.Markdown);
        Assert(!res.Refused, "artist report is never refused for a missing CRS");
        Assert(res.Markdown.IndexOf("Grain", StringComparison.OrdinalIgnoreCase) >= 0,
            "artist report must flag grain/vein");
        Assert(res.Markdown.IndexOf("UNKNOWN", StringComparison.Ordinal) >= 0,
            "artist report must mark grain/vein UNKNOWN from scan");
    }

    public static void Geologist_FlagsRockMassWorksheet()
    {
        var res = AudienceReportComposer.Compose(
            ReportAudience.Geologist, Prov(null), null, null, ReportFormat.Markdown);
        Assert(!res.Refused, "geologist report is never refused for a missing CRS");
        bool flagged = false;
        for (int i = 0; i < res.Warnings.Count; i++)
            if (res.Warnings[i].IndexOf("worksheet", StringComparison.OrdinalIgnoreCase) >= 0) flagged = true;
        Assert(flagged, "geologist report must warn the rock-mass rating needs a worksheet");
        Assert(res.Markdown.IndexOf("Terzaghi", StringComparison.Ordinal) >= 0,
            "geologist report must carry the Terzaghi-bias uncertainty caveat");
    }

    public static void Sections_RoutedByAudience()
    {
        var geoOnly = new ReportSection("Fracture sets").For(ReportAudience.Geologist);
        geoOnly.Add("Set 1 dip", "62", "deg", null);
        var sections = new List<ReportSection> { geoOnly };

        var eng = AudienceReportComposer.Compose(
            ReportAudience.Engineer, Prov("UTM 12N"), null, sections, ReportFormat.Markdown);
        var geo = AudienceReportComposer.Compose(
            ReportAudience.Geologist, Prov(null), null, sections, ReportFormat.Markdown);

        Assert(eng.Markdown.IndexOf("Fracture sets", StringComparison.Ordinal) < 0,
            "geologist-only section must not appear in the engineer report");
        Assert(geo.Markdown.IndexOf("Fracture sets", StringComparison.Ordinal) >= 0,
            "geologist-only section must appear in the geologist report");
    }

    public static void FrahanReport_NumericsBecomeRows()
    {
        var pr = new PackingReport
        {
            ProducerComponent = "Frahan Packing Report",
            PlacedCount = 21,
            Coverage = 0.602,
            Numerics = new Dictionary<string, double>
            {
                { "PlacedCount", 21 },
                { "Coverage", 0.602 }
            },
            Diagnostics = new List<string> { "21 of 24 placed" },
            AllInvariantsHeld = true
        };
        var res = AudienceReportComposer.Compose(
            ReportAudience.Engineer, Prov("UTM 12N"), new List<FrahanReport> { pr }, null, ReportFormat.Both);
        Assert(res.Markdown.IndexOf("Frahan Packing Report", StringComparison.Ordinal) >= 0,
            "the producing component should title its section");
        Assert(res.Markdown.IndexOf("Coverage", StringComparison.Ordinal) >= 0,
            "numeric keys should become rows");
        Assert(res.Markdown.IndexOf("21 of 24 placed", StringComparison.Ordinal) >= 0,
            "diagnostics should become notes");
    }

    public static void Csv_IsWellFormed()
    {
        var res = AudienceReportComposer.Compose(
            ReportAudience.Geologist, Prov("UTM 12N"), null, null, ReportFormat.Csv);
        Assert(res.Csv.IndexOf("section,field,value,unit,flag", StringComparison.Ordinal) >= 0,
            "CSV must carry the column header");
        Assert(res.Csv.IndexOf("# audience,Geologist", StringComparison.Ordinal) >= 0,
            "CSV must carry the audience provenance comment");
        Assert(string.IsNullOrEmpty(res.Markdown),
            "CSV-only format must not also render markdown");
    }

    private static void Assert(bool cond, string msg)
    {
        if (!cond) throw new Exception("AudienceReportTests failed: " + msg);
    }
}
