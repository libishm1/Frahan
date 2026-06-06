#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Frahan.Core.Reports;

// =============================================================================
// AudienceReport (W1, keep-or-cut: reports generation parity).
//
// One report composer driven by an Audience enum (engineer / artist / geologist),
// per SAMPLE_GH_SPEC.md: "the three definitions differ only in the Report/Export
// terminal driven by the Audience enum". The composer is Rhino-free and pure, so
// it is unit-tested headlessly (no Rhino license / no ghost geometry risk).
//
// It does NOT invent data. It consumes the typed FrahanReport records the kept
// algorithms already emit (their Numerics + Diagnostics) plus caller-supplied
// ReportSections (block schedule, fracture sets, etc.), then ORDERS, ROUTES,
// FLAGS, and FORMATS them per audience, and applies the spec's audience rules:
//   - Engineer  refuses to release without a declared CRS/datum (SPEC line 39).
//   - Artist    flags grain/vein as UNKNOWN-from-scan and surfaces confidence.
//   - Geologist flags rock-mass ratings as needing a scanline/borehole worksheet
//               and adds the Terzaghi-bias uncertainty caveat.
// Output is Markdown + CSV. The GH AudienceReportComponent is a thin marshaller
// over this class.
// =============================================================================

/// <summary>Report audience. Drives section ordering, flags, and the refuse rules.</summary>
public enum ReportAudience { Engineer = 0, Artist = 1, Geologist = 2 }

/// <summary>Output format selector.</summary>
public enum ReportFormat { Markdown = 0, Csv = 1, Both = 2 }

/// <summary>Provenance header shared by every audience (SAMPLE_GH_SPEC "0 COVER").</summary>
public sealed class ReportProvenance
{
    public string Site;
    public string ScanFile;
    public string DateText;
    public string CrsDatum;       // engineer release is REFUSED when this is empty
    public string Units = "m";
    public string SolverVersion;
    public string MeshVersion;
}

/// <summary>One labelled value in a report section. Flag carries an optional
/// confidence / caveat tag (e.g. "UNKNOWN (scan-only)", "needs scanline").</summary>
public struct ReportRow
{
    public string Label;
    public string Value;
    public string Unit;
    public string Flag;

    public ReportRow(string label, string value, string unit, string flag)
    {
        Label = label;
        Value = value;
        Unit = unit;
        Flag = flag;
    }
}

/// <summary>A titled block of rows + notes, optionally restricted to specific
/// audiences. An empty <see cref="Audiences"/> list means "show to all".</summary>
public sealed class ReportSection
{
    public string Title;
    public readonly List<ReportRow> Rows = new List<ReportRow>();
    public readonly List<string> Notes = new List<string>();
    public readonly List<ReportAudience> Audiences = new List<ReportAudience>();

    public ReportSection(string title) { Title = title; }

    public ReportSection Add(string label, string value, string unit, string flag)
    {
        Rows.Add(new ReportRow(label, value, unit, flag));
        return this;
    }

    public ReportSection Add(string label, string value, string unit)
    {
        return Add(label, value, unit, null);
    }

    public ReportSection Add(string label, double value, string unit)
    {
        return Add(label, value.ToString("0.###", CultureInfo.InvariantCulture), unit, null);
    }

    public ReportSection Note(string note)
    {
        if (!string.IsNullOrEmpty(note)) Notes.Add(note);
        return this;
    }

    public ReportSection For(params ReportAudience[] audiences)
    {
        if (audiences != null) Audiences.AddRange(audiences);
        return this;
    }

    public bool VisibleTo(ReportAudience a)
    {
        return Audiences.Count == 0 || Audiences.Contains(a);
    }
}

/// <summary>Composed report: Markdown + CSV strings, plus the refuse verdict and
/// any warnings raised while composing.</summary>
public sealed class AudienceReportResult
{
    public ReportAudience Audience;
    public bool Refused;
    public string RefusalReason;
    public string Markdown = "";
    public string Csv = "";
    public readonly List<string> Warnings = new List<string>();
}

/// <summary>Composes an audience-tailored report from FrahanReport records and
/// caller-supplied sections. Pure and deterministic.</summary>
public static class AudienceReportComposer
{
    public static AudienceReportResult Compose(
        ReportAudience audience,
        ReportProvenance provenance,
        IReadOnlyList<FrahanReport> reports,
        IReadOnlyList<ReportSection> sections,
        ReportFormat format)
    {
        var result = new AudienceReportResult { Audience = audience };
        if (provenance == null) provenance = new ReportProvenance();

        // ---- Audience rule: engineer refuses to release without a CRS/datum ----
        if (audience == ReportAudience.Engineer && string.IsNullOrWhiteSpace(provenance.CrsDatum))
        {
            result.Refused = true;
            result.RefusalReason =
                "Engineer mining-plan release REFUSED: no CRS/datum declared. A mining plan " +
                "without a coordinate reference system cannot be staked in the field. Declare " +
                "the CRS/datum (e.g. UTM zone + datum) and re-run.";
            result.Warnings.Add(result.RefusalReason);
            result.Markdown = "# Report refused\n\n" + result.RefusalReason + "\n";
            result.Csv = "# REFUSED," + Csv(result.RefusalReason) + "\n";
            return result;
        }

        // ---- Assemble the ordered, audience-filtered section list ----
        var ordered = new List<ReportSection>();
        ordered.Add(BuildProvenanceSection(provenance));

        // Typed FrahanReport records become sections (Numerics + Diagnostics).
        if (reports != null)
        {
            for (int i = 0; i < reports.Count; i++)
            {
                var s = FromFrahanReport(reports[i]);
                if (s != null) ordered.Add(s);
                if (reports[i] != null && !reports[i].AllInvariantsHeld)
                    result.Warnings.Add(
                        (reports[i].ProducerComponent ?? "report") +
                        ": invariants did not all hold (see diagnostics).");
            }
        }

        // Caller sections, filtered by audience.
        if (sections != null)
        {
            for (int i = 0; i < sections.Count; i++)
            {
                var s = sections[i];
                if (s != null && s.VisibleTo(audience)) ordered.Add(s);
            }
        }

        // ---- Audience-specific standing caveats (no fabricated numbers) ----
        ApplyAudienceCaveats(audience, ordered, result);

        // Collect any flagged rows as warnings (confidence surfacing).
        for (int i = 0; i < ordered.Count; i++)
            for (int j = 0; j < ordered[i].Rows.Count; j++)
            {
                var r = ordered[i].Rows[j];
                if (!string.IsNullOrEmpty(r.Flag))
                    result.Warnings.Add(ordered[i].Title + " / " + r.Label + ": " + r.Flag);
            }

        // ---- Render ----
        if (format == ReportFormat.Markdown || format == ReportFormat.Both)
            result.Markdown = RenderMarkdown(audience, provenance, ordered, result.Warnings);
        if (format == ReportFormat.Csv || format == ReportFormat.Both)
            result.Csv = RenderCsv(audience, provenance, ordered);

        return result;
    }

    // -------------------------------------------------------------------------
    private static ReportSection BuildProvenanceSection(ReportProvenance p)
    {
        var s = new ReportSection("Provenance");
        s.Add("Site", NullDash(p.Site), null);
        s.Add("Scan file", NullDash(p.ScanFile), null);
        s.Add("Date", NullDash(p.DateText), null);
        s.Add("CRS / datum", NullDash(p.CrsDatum), null,
            string.IsNullOrWhiteSpace(p.CrsDatum) ? "NOT DECLARED" : null);
        s.Add("Units", NullDash(p.Units), null);
        s.Add("Solver version", NullDash(p.SolverVersion), null);
        s.Add("Mesh version", NullDash(p.MeshVersion), null);
        return s;
    }

    private static ReportSection FromFrahanReport(FrahanReport r)
    {
        if (r == null) return null;
        var s = new ReportSection(string.IsNullOrEmpty(r.ProducerComponent) ? "Algorithm report" : r.ProducerComponent);
        if (r.Numerics != null)
            foreach (var kv in r.Numerics)
                s.Add(kv.Key, kv.Value, null);
        if (r.Diagnostics != null)
            for (int i = 0; i < r.Diagnostics.Count; i++)
                s.Note(r.Diagnostics[i]);
        if (!r.AllInvariantsHeld)
            s.Note("WARNING: not all invariants held for this report.");
        return s;
    }

    private static void ApplyAudienceCaveats(
        ReportAudience audience, List<ReportSection> ordered, AudienceReportResult result)
    {
        if (audience == ReportAudience.Artist)
        {
            var s = new ReportSection("Carving caveats");
            s.Note("Grain / vein direction is UNKNOWN from scan geometry alone. Confirm on the " +
                   "physical block before committing a carving orientation.");
            s.Note("Occluded / back faces are guessed from the visible scan; treat per-block " +
                   "free-face counts on back faces as low-confidence.");
            ordered.Add(s);
        }
        else if (audience == ReportAudience.Geologist)
        {
            if (!HasSectionLike(ordered, "rock", "rqd", "rmr", "gsi"))
            {
                var s = new ReportSection("Rock-mass rating (worksheet required)");
                s.Note("RQD / RMR / GSI are NOT derivable from a surface scan alone. They require a " +
                       "scanline or borehole worksheet; supply it before quoting a rating.");
                ordered.Add(s);
                result.Warnings.Add("Rock-mass rating omitted: no scanline/borehole worksheet supplied.");
            }
            var u = new ReportSection("Uncertainty");
            u.Note("Orientation statistics carry Terzaghi sampling bias (planes sub-parallel to the " +
                   "scan face are under-counted). Auto-picked vs manually-picked fractures and data " +
                   "gaps must be reported alongside any DFN.");
            ordered.Add(u);
        }
        else if (audience == ReportAudience.Engineer)
        {
            if (!HasSectionLike(ordered, "risk", "p50", "p90"))
            {
                var s = new ReportSection("Risk");
                s.Note("Figures are best-estimate (P50). A P90 figure requires the named uncertainty " +
                       "driver (fracture-continuity calibration, recovery-cascade residual). Quote both " +
                       "before committing extraction tonnage.");
                ordered.Add(s);
            }
        }
    }

    private static bool HasSectionLike(List<ReportSection> sections, params string[] keys)
    {
        for (int i = 0; i < sections.Count; i++)
        {
            string t = sections[i].Title == null ? "" : sections[i].Title.ToLowerInvariant();
            for (int k = 0; k < keys.Length; k++)
                if (t.IndexOf(keys[k], StringComparison.Ordinal) >= 0) return true;
        }
        return false;
    }

    // -------------------------------------------------------------------------
    private static string RenderMarkdown(
        ReportAudience audience, ReportProvenance p,
        List<ReportSection> sections, List<string> warnings)
    {
        var sb = new StringBuilder();
        sb.Append("# Frahan StonePack report (").Append(audience.ToString()).Append(")\n\n");
        sb.Append("Audience: **").Append(audience.ToString()).Append("**. ");
        sb.Append("Generated ").Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)).Append(".\n\n");

        for (int i = 0; i < sections.Count; i++)
        {
            var s = sections[i];
            sb.Append("## ").Append(s.Title).Append('\n').Append('\n');
            if (s.Rows.Count > 0)
            {
                sb.Append("| Field | Value | Unit | Flag |\n");
                sb.Append("|---|---|---|---|\n");
                for (int j = 0; j < s.Rows.Count; j++)
                {
                    var r = s.Rows[j];
                    sb.Append("| ").Append(MdCell(r.Label))
                      .Append(" | ").Append(MdCell(r.Value))
                      .Append(" | ").Append(MdCell(r.Unit))
                      .Append(" | ").Append(MdCell(r.Flag))
                      .Append(" |\n");
                }
                sb.Append('\n');
            }
            for (int j = 0; j < s.Notes.Count; j++)
                sb.Append("- ").Append(s.Notes[j]).Append('\n');
            if (s.Notes.Count > 0) sb.Append('\n');
        }

        if (warnings != null && warnings.Count > 0)
        {
            sb.Append("## Warnings\n\n");
            for (int i = 0; i < warnings.Count; i++)
                sb.Append("- ").Append(warnings[i]).Append('\n');
        }
        return sb.ToString();
    }

    private static string RenderCsv(
        ReportAudience audience, ReportProvenance p, List<ReportSection> sections)
    {
        var sb = new StringBuilder();
        sb.Append("# audience,").Append(Csv(audience.ToString())).Append('\n');
        sb.Append("# generated_utc,").Append(Csv(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))).Append('\n');
        sb.Append("section,field,value,unit,flag\n");
        for (int i = 0; i < sections.Count; i++)
        {
            var s = sections[i];
            for (int j = 0; j < s.Rows.Count; j++)
            {
                var r = s.Rows[j];
                sb.Append(Csv(s.Title)).Append(',')
                  .Append(Csv(r.Label)).Append(',')
                  .Append(Csv(r.Value)).Append(',')
                  .Append(Csv(r.Unit)).Append(',')
                  .Append(Csv(r.Flag)).Append('\n');
            }
            for (int j = 0; j < s.Notes.Count; j++)
                sb.Append(Csv(s.Title)).Append(",note,").Append(Csv(s.Notes[j])).Append(",,\n");
        }
        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    private static string NullDash(string s) { return string.IsNullOrEmpty(s) ? "-" : s; }

    private static string MdCell(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("|", "\\|").Replace("\n", " ");
    }

    private static string Csv(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        bool needsQuote = s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0;
        if (!needsQuote) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
