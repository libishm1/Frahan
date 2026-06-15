#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;
using Frahan.Masonry.Quarry.Ingestion;
using Frahan.Masonry.Quarry.Processing;

namespace Frahan.GH.Quarry;

// =============================================================================
// GprSurveyGridComponent -- ONE component that ingests a whole GPR survey GRID.
//
// A GPR survey of a quarry floor is a set of PARALLEL scan lines, not a single
// section. The single-line "GPR Fracture Extract" emits every pick at y=0, so
// stacking N lines means N components all piled on the same origin (they overlap)
// plus a Merge. That does not scale to 20-40 slices.
//
// This component takes the LIST of slice files and a line SPACING (or explicit
// per-line positions), runs the same validated Core chain (RadargramProcessor +
// FractureExtractor) on each, and lays line i at y = position[i] (default
// i*spacing). The result is ONE 3D pick cloud (x = distance, y = line offset,
// z = -depth) plus per-pick energy -- exactly the input GPR Fracture Surfaces 3D
// kriges into dipping bed surfaces. Wire P -> Fracture Picks and Cf -> Pick Energy.
//
// Frahan > Quarry > GPR survey-grid ingest.
// =============================================================================

/// <summary>
/// Frahan &gt; Quarry &gt; GPR Survey Grid. Ingest a whole grid of parallel GPR
/// scan-line files in ONE component and lay them out into a single 3D fracture-pick
/// cloud (x = distance, y = line offset, z = -depth) for GPR Fracture Surfaces 3D.
/// </summary>
[RelatedComponent("Frahan > Quarry > GPR Fracture Surfaces 3D", Reason = "Krige this multi-line pick cloud into 3D dipping bed surfaces.")]
[RelatedComponent("Frahan > Quarry > GPR Fracture Extract", Reason = "Single-section twin; this one batches a whole survey grid.")]
[Algorithm("GPR survey-grid ingest: per-line f-k migration + Hilbert energy + USGS continuity, laid out by line offset",
    "Stolt 1978 (f-k migration); Taner 1979 (instantaneous attributes); USGS Mirror Lake WRIR 99-4018C (>=40-trace continuity)",
    Note = "Each line i placed at y = position[i] (default i*spacing); picks at (distance, y, -depth).")]
public sealed class GprSurveyGridComponent : FrahanComponentBase
{
    public GprSurveyGridComponent()
        : base("GPR Survey Grid", "GprGrid",
            "Ingest a whole GPR survey GRID in ONE component. Give it the LIST of scan-line files " +
            "(.dt / .rd3 / .dzt / .dt1 / .sgy / .csv) and a Line Spacing (or explicit Line Positions); " +
            "it runs the validated GPR chain (dewow -> background -> time-zero -> gain -> f-k migration " +
            "-> Hilbert energy -> USGS continuity) on each line and lays line i at y = position[i] " +
            "(default i*spacing). Outputs one 3D fracture-pick cloud (x=distance, y=line offset, z=-depth) " +
            "plus per-pick energy -- feed Picks -> 'GPR Fracture Surfaces 3D' Fracture Picks and Confidence " +
            "-> its Pick Energy. Replaces N single-line components + Merge.",
            "Frahan", "Quarry")
    {
    }

    public override Guid ComponentGuid => new Guid("A7E0B0F0-0C0F-4A16-9E3D-0FACE0FACE01");
    public override GH_Exposure Exposure => GH_Exposure.secondary;
    protected override Bitmap Icon => IconProvider.Load("GprIngest.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("Files", "F",
            "The GRID of GPR scan-line files (one per survey line). .dt / .rd3 / .dzt / .dt1 / .sgy / .csv.",
            GH_ParamAccess.list);
        p.AddTextParameter("Preset", "Pr",
            "Stone x frequency preset for tuned defaults applied to every line: " +
            "marble_600, granite_160, travertine_390, andesite_390, limestone_200.",
            GH_ParamAccess.item, "granite_160");
        p.AddNumberParameter("Line Spacing", "Sp",
            "Distance (m) between consecutive parallel scan lines. Line i is placed at y = i * spacing. " +
            "Ignored where Line Positions supplies an explicit y. Default 2.0.",
            GH_ParamAccess.item, 2.0);
        p.AddNumberParameter("Line Positions", "Y",
            "OPTIONAL explicit y offset (m) per line, aligned to Files (use real survey line coordinates " +
            "when you have them). Overrides Line Spacing when its count matches the file count.",
            GH_ParamAccess.list);
        p[p.ParamCount - 1].Optional = true;
        p.AddNumberParameter("Velocity", "v",
            "EM velocity (m/ns), depth = v*t/2. < 0 = use the preset value. Override with a WARR/CMP-" +
            "measured velocity when available (highest-leverage parameter).", GH_ParamAccess.item, -1.0);
        p.AddNumberParameter("Energy Quantile", "Q",
            "Energy quantile (0..1) above which a sample is a fracture candidate. < 0 = preset (~0.985).",
            GH_ParamAccess.item, -1.0);
        p.AddNumberParameter("Max Dip", "Dip",
            "USGS dip gate (deg). < 0 = default 45 (crystalline-rock standard).", GH_ParamAccess.item, -1.0);
        p.AddBooleanParameter("Migrate", "Mig",
            "f-k (Stolt) migration on every line. Default true.", GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddPointParameter("Fracture Picks", "P",
            "One 3D fracture-pick cloud across the whole survey: (distance, line offset, -depth) in metres. " +
            "Wire into GPR Fracture Surfaces 3D > Fracture Picks.", GH_ParamAccess.list);
        p.AddIntegerParameter("Line Id", "Lid", "Survey line index (0-based) of each pick.", GH_ParamAccess.list);
        p.AddNumberParameter("Confidence", "Cf",
            "Normalised energy (0..1) of each pick. Wire into GPR Fracture Surfaces 3D > Pick Energy so the " +
            "PEAK reflector is kept per cell.", GH_ParamAccess.list);
        p.AddNumberParameter("Depths", "D", "Depth (m) of each pick.", GH_ParamAccess.list);
        p.AddMeshParameter("Energy Sections", "E",
            "Per-line depth-converted energy section meshes, each laid at its survey y (x=distance, " +
            "y=line offset, z=-depth), vertex-coloured by instantaneous energy.", GH_ParamAccess.list);
        p.AddNumberParameter("Bedrock Depth", "Z",
            "Deepest continuous reflector (m) per line = candidate bedrock / rock-face top.", GH_ParamAccess.list);
        p.AddTextParameter("Report", "Rpt", "Per-line ingest summary.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var files = new List<string>();
        if (!da.GetDataList(0, files) || files.Count == 0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No GPR files provided."); return; }
        string presetKey = "granite_160"; double spacing = 2.0, vOverride = -1.0, qOverride = -1.0, dipOverride = -1.0;
        bool migrate = true;
        da.GetData(1, ref presetKey);
        da.GetData(2, ref spacing);
        var positions = new List<double>(); da.GetDataList(3, positions);
        da.GetData(4, ref vOverride); da.GetData(5, ref qOverride); da.GetData(6, ref dipOverride);
        bool migrateSet = da.GetData(7, ref migrate);
        bool havePositions = positions != null && positions.Count == files.Count;

        if (!GprPresets.TryGet(presetKey, out var preset))
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
            $"Unknown preset '{presetKey}'. Have: {string.Join(", ", GprPresets.Keys)}"); return; }
        if (!preset.IsEmpirical)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Preset '{presetKey}' is literature-default (not yet tuned on real data); verify the velocity.");

        var pts = new List<Point3d>();
        var lineIds = new List<int>();
        var conf = new List<double>();
        var depths = new List<double>();
        var sections = new List<Mesh>();
        var bedrocks = new List<double>();
        var rpt = new System.Text.StringBuilder();
        rpt.AppendLine($"GPR survey grid: {files.Count} line(s), preset={presetKey} ({preset.Label}), " +
                       $"spacing={(havePositions ? "explicit" : spacing.ToString("0.##") + " m")}");
        int kept = 0;

        for (int li = 0; li < files.Count; li++)
        {
            string file = files[li];
            double yLine = havePositions ? positions[li] : li * spacing;
            if (string.IsNullOrWhiteSpace(file) || !System.IO.File.Exists(file))
            { rpt.AppendLine($"  line {li}: SKIP (file not found: {file})"); continue; }
            if (file.EndsWith(".gsf", StringComparison.OrdinalIgnoreCase))
            { rpt.AppendLine($"  line {li}: SKIP (.gsf proprietary -> export to SEG-Y first)"); continue; }

            GprRadargram rg;
            try { rg = GprFileReader.Load(file, null); }
            catch (Exception ex) { rpt.AppendLine($"  line {li}: SKIP (load failed: {ex.Message})"); continue; }
            if (rg.TraceCount < 2) { rpt.AppendLine($"  line {li}: SKIP (too few traces)"); continue; }

            var proc = new RadargramProcessor();
            var fx = new FractureExtractor();
            preset.Apply(proc, fx);
            double v = vOverride > 0 ? vOverride : preset.VelocityMNsPerNs;
            if (migrateSet) proc.Migrate = migrate;
            if (qOverride > 0) fx.EnergyQuantile = qOverride;
            if (dipOverride > 0) fx.DipMaxDeg = Math.Min(89.0, dipOverride);

            double[,] B; double dtNs, dx;
            try { B = RadargramProcessor.ToGrid(rg, out dtNs, out dx); }
            catch (Exception ex) { rpt.AppendLine($"  line {li}: SKIP (grid build failed: {ex.Message})"); continue; }

            double[,] energy; IReadOnlyList<FractureExtractor.FracturePick> picks;
            try { energy = proc.Run(B, dtNs, dx, v); picks = fx.Extract(energy, dtNs, dx, v); }
            catch (Exception ex) { rpt.AppendLine($"  line {li}: SKIP (processing failed: {ex.Message})"); continue; }

            int ntr = B.GetLength(1);
            double bedrock = 0.0;
            foreach (var p in picks)
            {
                double xpos = rg.Traces[Math.Min(p.TraceIndex, ntr - 1)].X;
                pts.Add(new Point3d(xpos, yLine, -p.DepthMetres));
                lineIds.Add(li); conf.Add(p.Energy); depths.Add(p.DepthMetres);
                if (p.DepthMetres > bedrock) bedrock = p.DepthMetres;
            }
            bedrocks.Add(bedrock);
            sections.Add(BuildEnergyMesh(energy, dx, dtNs, v, yLine));
            kept += picks.Count;
            rpt.AppendLine($"  line {li} @ y={yLine:0.##} m: {picks.Count} picks, bedrock {bedrock:0.##} m " +
                           $"({System.IO.Path.GetFileName(file)})");
        }

        rpt.AppendLine($"TOTAL {kept} picks across {bedrocks.Count} ingested line(s) -> feed Picks + Confidence " +
                       $"into GPR Fracture Surfaces 3D.");
        if (kept == 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No picks extracted from any line.");

        da.SetDataList(0, pts);
        da.SetDataList(1, lineIds);
        da.SetDataList(2, conf);
        da.SetDataList(3, depths);
        da.SetDataList(4, sections);
        da.SetDataList(5, bedrocks);
        da.SetData(6, rpt.ToString().TrimEnd());
    }

    // depth-converted energy section -> coloured mesh laid at y = yLine (x = distance, z = -depth).
    private static Mesh BuildEnergyMesh(double[,] e, double dx, double dtNs, double v, double yLine)
    {
        int ns = e.GetLength(0), ntr = e.GetLength(1);
        double emax = 1e-12; foreach (var x in e) if (x > emax) emax = x;
        var m = new Mesh();
        for (int t = 0; t < ntr; t++)
            for (int i = 0; i < ns; i++)
            {
                double depth = v * (i * dtNs) / 2.0;
                m.Vertices.Add(new Point3d(t * dx, yLine, -depth));
                double f = Math.Max(0.0, Math.Min(1.0, e[i, t] / emax));
                m.VertexColors.Add(Jet(f));
            }
        for (int t = 0; t < ntr - 1; t++)
            for (int i = 0; i < ns - 1; i++)
            {
                int a = t * ns + i, b = (t + 1) * ns + i, c = (t + 1) * ns + i + 1, d = t * ns + i + 1;
                m.Faces.AddFace(a, b, c, d);
            }
        m.Normals.ComputeNormals();
        return m;
    }

    private static Color Jet(double f)
    {
        double r = Math.Max(0, Math.Min(1, 1.5 - Math.Abs(4 * f - 3)));
        double g = Math.Max(0, Math.Min(1, 1.5 - Math.Abs(4 * f - 2)));
        double b = Math.Max(0, Math.Min(1, 1.5 - Math.Abs(4 * f - 1)));
        return Color.FromArgb((int)(255 * r), (int)(255 * g), (int)(255 * b));
    }
}
