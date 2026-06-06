#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Frahan.Core.Reports;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Frahan.GH.Attributes;

namespace Frahan.GH;

/// <summary>
/// W1 (keep-or-cut: reports generation parity). The single Report / Export
/// terminal node from SAMPLE_GH_SPEC.md: one engine, three audiences selected by
/// the Audience input (engineer / artist / geologist). It consumes the typed
/// FrahanReport records the kept algorithms already emit (Packing / MeshDiagnostics
/// / FabricationPrep / BlockCutOpt / ChartFlatness) plus optional pipe-delimited
/// Sections, then orders, routes, flags, and formats them per audience and applies
/// the spec's audience rules (engineer refuses without a CRS; artist flags
/// grain/vein UNKNOWN; geologist flags rock-mass needing a worksheet). Output is
/// Markdown + CSV; with Run + a file path it also writes the files.
///
/// The composition logic lives in Frahan.Core.Reports.AudienceReportComposer
/// (Rhino-free, unit-tested). This component is a thin marshaller.
/// </summary>
[DesignApplication(
    "One Report / Export terminal driven by an Audience enum (engineer mining-plan / artist carving-guide / geologist brief).",
    DesignFlow.Bridges,
    Precedent = "SAMPLE_GH_SPEC.md three-audience report terminal; Frahan-original audience composer",
    Tolerance = "Engineer release refused without a declared CRS/datum")]
public sealed class AudienceReportComponent : GH_Component
{
    public AudienceReportComponent()
        : base("Frahan Report / Export", "Report",
            "Audience-tailored report terminal. Pick Audience (0 engineer, 1 artist, " +
            "2 geologist). Consumes Frahan report records + optional pipe-delimited " +
            "Sections; emits Markdown + CSV. Engineer release is refused without a " +
            "declared CRS/datum. With Run + File Path it writes the .md / .csv files.",
            "Frahan", "Reports")
    {
    }

    public override Guid ComponentGuid => new Guid("AB12C010-1A2B-4C3D-9E4F-5A6B7C8D9E10");
    protected override Bitmap Icon => IconProvider.Load("PackDiagnostics.png");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Reports", "R",
            "Frahan report records (PackingReport, MeshDiagnostics, FabricationPrep, " +
            "BlockCutOpt, ChartFlatness). Optional; wire any number.", GH_ParamAccess.list);
        p[0].Optional = true;
        p.AddTextParameter("Sections", "S",
            "Optional extra rows, one per line, pipe-delimited: " +
            "'Section|Label|Value|Unit|Flag' for a value row, or 'Section|note|Text' " +
            "for a note. Use this to add a block schedule, fracture sets, etc.",
            GH_ParamAccess.list);
        p[1].Optional = true;
        p.AddIntegerParameter("Audience", "A",
            "0 = Engineer (mining-plan), 1 = Artist (carving-guide), 2 = Geologist (brief).",
            GH_ParamAccess.item, 0);
        p.AddIntegerParameter("Format", "Fmt",
            "0 = Markdown, 1 = CSV, 2 = Both.", GH_ParamAccess.item, 2);
        p.AddTextParameter("Site", "St", "Site name (provenance).", GH_ParamAccess.item, "");
        p.AddTextParameter("Scan File", "Sf", "Source scan file (provenance).", GH_ParamAccess.item, "");
        p.AddTextParameter("Date", "Dt", "Report date (provenance).", GH_ParamAccess.item, "");
        p.AddTextParameter("CRS / Datum", "Crs",
            "Coordinate reference system + datum. REQUIRED for an engineer release.",
            GH_ParamAccess.item, "");
        p.AddTextParameter("Units", "U", "Document units (provenance).", GH_ParamAccess.item, "m");
        p.AddTextParameter("Solver Version", "Sv", "Solver / mesh version (provenance).", GH_ParamAccess.item, "");
        p.AddTextParameter("File Path", "Fp",
            "Optional output base path WITHOUT extension (e.g. C:\\out\\mining_plan). " +
            "Writes <path>.md and/or <path>.csv when Run is true.", GH_ParamAccess.item, "");
        p.AddBooleanParameter("Run", "Run",
            "Set true to write the report file(s) to File Path.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("Markdown", "Md", "Audience-tailored Markdown report.", GH_ParamAccess.item);
        p.AddTextParameter("CSV", "Csv", "Audience-tailored CSV report.", GH_ParamAccess.item);
        p.AddBooleanParameter("Refused", "X",
            "True when the release was refused (engineer without a CRS).", GH_ParamAccess.item);
        p.AddTextParameter("Warnings", "W", "Warnings + surfaced confidence flags.", GH_ParamAccess.list);
        p.AddTextParameter("Files Written", "Fw", "Paths written when Run is true.", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var goos = new List<IGH_Goo>();
        da.GetDataList(0, goos);
        var sectionLines = new List<string>();
        da.GetDataList(1, sectionLines);
        int audienceVal = 0; da.GetData(2, ref audienceVal);
        int formatVal = 2; da.GetData(3, ref formatVal);
        string site = ""; da.GetData(4, ref site);
        string scanFile = ""; da.GetData(5, ref scanFile);
        string date = ""; da.GetData(6, ref date);
        string crs = ""; da.GetData(7, ref crs);
        string units = "m"; da.GetData(8, ref units);
        string solverVersion = ""; da.GetData(9, ref solverVersion);
        string filePath = ""; da.GetData(10, ref filePath);
        bool run = false; da.GetData(11, ref run);

        var audience = ToAudience(audienceVal);
        var format = ToFormat(formatVal);

        // Unwrap FrahanReport records from the Generic input.
        var reports = new List<FrahanReport>();
        for (int i = 0; i < goos.Count; i++)
        {
            var fr = AsFrahanReport(goos[i]);
            if (fr != null) reports.Add(fr);
            else if (goos[i] != null)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "Input " + i + " is not a Frahan report record; ignored.");
        }

        // Parse pipe-delimited section lines into ReportSections (grouped by title).
        var sections = ParseSections(sectionLines);

        var provenance = new ReportProvenance
        {
            Site = site, ScanFile = scanFile, DateText = date,
            CrsDatum = crs, Units = units, SolverVersion = solverVersion
        };

        AudienceReportResult res;
        try
        {
            res = AudienceReportComposer.Compose(audience, provenance, reports, sections, format);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Report composition failed: " + ex.Message);
            return;
        }

        if (res.Refused)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, res.RefusalReason);
        for (int i = 0; i < res.Warnings.Count; i++)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, res.Warnings[i]);

        var written = new List<string>();
        if (run && !string.IsNullOrWhiteSpace(filePath) && !res.Refused)
        {
            try
            {
                string baseNoExt = StripExtension(filePath);
                if (format == ReportFormat.Markdown || format == ReportFormat.Both)
                {
                    string mdPath = baseNoExt + ".md";
                    File.WriteAllText(mdPath, res.Markdown);
                    written.Add(mdPath);
                }
                if (format == ReportFormat.Csv || format == ReportFormat.Both)
                {
                    string csvPath = baseNoExt + ".csv";
                    File.WriteAllText(csvPath, res.Csv);
                    written.Add(csvPath);
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "File write failed: " + ex.Message);
            }
        }
        else if (run && string.IsNullOrWhiteSpace(filePath))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Run is true but File Path is empty; nothing written.");
        }

        da.SetData(0, res.Markdown);
        da.SetData(1, res.Csv);
        da.SetData(2, res.Refused);
        da.SetDataList(3, res.Warnings);
        da.SetDataList(4, written);
    }

    // -------------------------------------------------------------------------
    private static FrahanReport AsFrahanReport(IGH_Goo goo)
    {
        if (goo == null) return null;
        if (goo is GH_ObjectWrapper w && w.Value is FrahanReport fr) return fr;
        if (goo.ScriptVariable() is FrahanReport fr2) return fr2;
        return null;
    }

    private static List<ReportSection> ParseSections(List<string> lines)
    {
        var byTitle = new Dictionary<string, ReportSection>();
        var order = new List<ReportSection>();
        if (lines == null) return order;
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] parts = line.Split('|');
            string title = parts[0].Trim();
            if (title.Length == 0) continue;
            ReportSection sec;
            if (!byTitle.TryGetValue(title, out sec))
            {
                sec = new ReportSection(title);
                byTitle[title] = sec;
                order.Add(sec);
            }
            if (parts.Length >= 3 && string.Equals(parts[1].Trim(), "note", StringComparison.OrdinalIgnoreCase))
            {
                sec.Note(parts[2]);
            }
            else if (parts.Length >= 3)
            {
                string label = parts[1];
                string value = parts[2];
                string unit = parts.Length >= 4 ? parts[3] : null;
                string flag = parts.Length >= 5 ? parts[4] : null;
                sec.Add(label, value, unit, flag);
            }
        }
        return order;
    }

    private static string StripExtension(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        string ext = Path.GetExtension(path);
        if (string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase))
            return path.Substring(0, path.Length - ext.Length);
        return path;
    }

    private ReportAudience ToAudience(int v)
    {
        switch (v)
        {
            case 0: return ReportAudience.Engineer;
            case 1: return ReportAudience.Artist;
            case 2: return ReportAudience.Geologist;
            default:
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid Audience " + v + "; using Engineer.");
                return ReportAudience.Engineer;
        }
    }

    private ReportFormat ToFormat(int v)
    {
        switch (v)
        {
            case 0: return ReportFormat.Markdown;
            case 1: return ReportFormat.Csv;
            case 2: return ReportFormat.Both;
            default:
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid Format " + v + "; using Both.");
                return ReportFormat.Both;
        }
    }
}
