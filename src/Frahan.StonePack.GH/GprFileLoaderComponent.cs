#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Frahan.Masonry.Quarry.Ingestion;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Quarry;

// =============================================================================
// Frahan > Ingest > GPR File Loader.
//
// Thin canvas adapter over Frahan.Masonry.Quarry.Ingestion.GprFileReader.
// Auto-detects format by extension:
//   .csv          CSV traces
//   .sgy / .segy  SEG-Y rev 0/1/2 (IBM-float, int32, int16, IEEE-754 BE)
//   .rd3          MALA RAMAC (+ companion .rad header)
//   .dt1          Sensors and Software pulseEKKO (+ companion .HD header)
//
// Outputs:
//   - Trace count
//   - Per-trace start points (Point3d list, in source XY plus z=0)
//   - Sample-spacing metres (uniform across traces in a radargram)
//   - Source file path echoed
//
// The radargram samples themselves are intentionally NOT bulk-emitted to the
// canvas (a typical .rd3 can carry millions of int16 values; piping that
// through Grasshopper would crash the document). For numerical access to
// samples, call the Core readers directly or build a follow-up component
// that takes a (trace_index, sample_index) pair and emits one sample.
// =============================================================================

[Algorithm("Multi-format GPR ingest dispatcher", "Frahan-original; routes .csv / .sgy / .segy / .rd3 / .dt1 to matching reader")]
[Algorithm("SEG-Y rev 0/1/2", "SEG Society of Exploration Geophysicists Y-format, formats 1 (IBM-float), 2 (int32 BE), 3 (int16 BE), 5 (IEEE-754 BE)")]
[Algorithm("MALA RD3 + RAD", "MALA Geoscience binary trace + ASCII header; reverse-engineered from RGPR R-package source")]
[Algorithm("Sensors and Software DT1 + HD", "pulseEKKO binary trace + ASCII header; USGS OFR 02-166 public-domain spec (Lucius and Powers 1999)")]
[Algorithm("GSSI DZT (SIR)", "GSSI SIR controller format; layout cross-referenced from readgssi (BSD-3) + RGPR", Note = "8/16/32-bit samples; 1024-byte header; added 2026-05-27")]
[Algorithm("IDS GeoRadar GRED .dt", "IDS proprietary; public record layout via RGPR readIDS.R, clean-room implementation", Note = "'V0' record format, len_rec stride, 'R' trace records; added 2026-05-27")]
[DesignApplication(
    "Load a ground-penetrating-radar file by extension: CSV / SEG-Y / MALA RD3 / pulseEKKO DT1",
    DesignFlow.Bridges,
    Precedent = "SEG-Y rev 1 / Mala RD3 / PulseEKKO DT1 / GSSI DZT / IDS DT format specs")]
public sealed class GprFileLoaderComponent : GH_Component
{
    public GprFileLoaderComponent()
        : base("GPR File Loader", "GprLoad",
            "Load a ground-penetrating-radar file by extension: CSV / SEG-Y / MALA RD3 / pulseEKKO DT1. " +
            "Emits trace-start points + count + sample spacing. Sample amplitudes are not piped to the " +
            "canvas (too large for GH data trees); use the Core reader for per-sample access. " +
            "Workflows cross-checked against RGPR (the open R GPR-processing package) in the companion paper.",
            "Frahan", "Ingest")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("F2D00BEC-2026-4523-B0B0-2ABE15A0DEAD");

    protected override Bitmap Icon => IconProvider.Load("GprIngest.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("File Path", "F",
            "Absolute path to a .csv / .sgy / .segy / .rd3 / .dt1 file. " +
            ".rd3 expects a companion .rad alongside; .dt1 expects a companion .HD.",
            GH_ParamAccess.item);
        p.AddTextParameter("Id", "Id",
            "Optional radargram identifier label. Defaults to the file's basename.",
            GH_ParamAccess.item, string.Empty);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddIntegerParameter("Trace Count", "N",
            "Number of traces in the radargram.", GH_ParamAccess.item);
        p.AddPointParameter("Trace Origins", "P",
            "One Point3d per trace, at (sourceX, sourceY, 0) in source CRS units.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Sample Spacing m", "dz",
            "Sample spacing (metres) of the first trace; uniform within one radargram.",
            GH_ParamAccess.item);
        p.AddIntegerParameter("Sample Count", "Ns",
            "Number of samples per trace (first trace).", GH_ParamAccess.item);
        p.AddTextParameter("Source File", "Src",
            "The source file path echoed verbatim.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        string path = string.Empty;
        string id = string.Empty;
        if (!da.GetData(0, ref path)) return;
        da.GetData(1, ref id);

        GprRadargram rg;
        try
        {
            rg = GprFileReader.Load(path, string.IsNullOrWhiteSpace(id) ? null : id);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"GPR load failed: {ex.Message}");
            return;
        }

        var origins = new List<Point3d>(rg.TraceCount);
        for (int i = 0; i < rg.TraceCount; i++)
        {
            var t = rg.Traces[i];
            origins.Add(new Point3d(t.X, t.Y, 0.0));
        }

        double dz = rg.TraceCount > 0 ? rg.Traces[0].SampleSpacingMetres : 0.0;
        int ns = rg.TraceCount > 0 ? rg.Traces[0].SampleCount : 0;

        da.SetData(0, rg.TraceCount);
        da.SetDataList(1, origins);
        da.SetData(2, dz);
        da.SetData(3, ns);
        da.SetData(4, path);
    }
}
