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
            "The GRID of GPR scan-line files (one per survey line). .dt / .rd3 / .dzt / .dt1 / .sgy / .csv / " +
            ".gsf (Geoscanners Akula -- now read natively).",
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
        p.AddIntegerParameter("Orientation", "Ax",
            "OPTIONAL per-line axis for a BIDIRECTIONAL grid: 0 = longitudinal (line runs along X, lines " +
            "stacked in Y), 1 = transverse / cross-line (runs along Y, stacked in X). Empty = auto-detect " +
            "from the filename (contains 'TA' -> transverse, else longitudinal); a single value applies to " +
            "all. With BOTH axes present the picks form a true crossing grid and each axis is spaced to fit " +
            "the other axis' extent (the cross-lines MEASURE the perpendicular dip instead of interpolating " +
            "it); with one axis it falls back to Line Spacing (parallel lines).",
            GH_ParamAccess.list);
        p[p.ParamCount - 1].Optional = true;
        p.AddGenericParameter("Custom Preset", "CPr",
            "OPTIONAL constructed GPR preset (from 'Construct GPR Preset'). If provided, it OVERRIDES the named " +
            "Preset string -- use it for any stone/antenna the two built-in empirical presets do not cover.",
            GH_ParamAccess.item);
        p[p.ParamCount - 1].Optional = true;
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
        var orient = new List<int>(); da.GetDataList(8, orient);
        bool havePositions = positions != null && positions.Count == files.Count;

        // resolve the preset: a constructed Custom Preset (input 9) OVERRIDES the named key.
        GprPreset preset = null;
        Grasshopper.Kernel.Types.IGH_Goo cprGoo = null;
        if (da.GetData(9, ref cprGoo) && cprGoo is GprPresetGoo cpg && cpg.Value != null)
            preset = cpg.Value;
        if (preset == null)
        {
            if (!GprPresets.TryGet(presetKey, out preset))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Unknown preset '{presetKey}'. Have: {string.Join(", ", GprPresets.Keys)}"); return; }
            if (!preset.IsEmpirical)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Preset '{presetKey}' is literature-default (not yet tuned on real data); verify the velocity.");
        }

        var rpt = new System.Text.StringBuilder();

        // ---- PASS 1: load + process each line; capture picks, energy, length, axis ----
        var recs = new List<LineRec>(files.Count);
        for (int li = 0; li < files.Count; li++)
        {
            string file = files[li];
            if (string.IsNullOrWhiteSpace(file) || !System.IO.File.Exists(file))
            { rpt.AppendLine($"  line {li}: SKIP (file not found: {file})"); continue; }
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
            var rec = new LineRec { Energy = energy, DtNs = dtNs, Dx = dx, V = v, FileIndex = li,
                Fname = System.IO.Path.GetFileName(file) };
            // axis: explicit list (per-line or single) else auto-detect 'TA' in the filename -> transverse
            if (orient != null && orient.Count == files.Count) rec.Axis = orient[li] != 0 ? 1 : 0;
            else if (orient != null && orient.Count == 1) rec.Axis = orient[0] != 0 ? 1 : 0;
            else rec.Axis = rec.Fname.ToUpperInvariant().Contains("TA") ? 1 : 0;
            double maxTx = 0;
            foreach (var p in picks)
            {
                double tx = rg.Traces[Math.Min(p.TraceIndex, ntr - 1)].X;
                rec.Picks.Add(new[] { tx, p.Energy, p.DepthMetres });
                if (p.DepthMetres > rec.Bedrock) rec.Bedrock = p.DepthMetres;
                if (tx > maxTx) maxTx = tx;
            }
            rec.Length = Math.Max(maxTx, (ntr - 1) * dx);
            recs.Add(rec);
        }

        // ---- layout: bidirectional grid auto-fits each axis to the crossing axis' extent ----
        int lonCount = recs.Count(r => r.Axis == 0), traCount = recs.Count(r => r.Axis == 1);
        double lonLen = recs.Where(r => r.Axis == 0).Select(r => r.Length).DefaultIfEmpty(0).Max();
        double traLen = recs.Where(r => r.Axis == 1).Select(r => r.Length).DefaultIfEmpty(0).Max();
        bool bidir = lonCount > 0 && traCount > 0;
        double lonSpace = bidir && lonCount > 1 ? traLen / (lonCount - 1) : spacing; // long lines stacked in Y
        double traSpace = bidir && traCount > 1 ? lonLen / (traCount - 1) : spacing; // cross lines stacked in X
        rpt.Insert(0, $"GPR survey grid: {recs.Count} line(s) ({lonCount} longitudinal + {traCount} transverse, " +
                      $"{(bidir ? "BIDIRECTIONAL - cross-lines measure the perpendicular dip" : "single-axis")}), " +
                      $"preset={presetKey} ({preset.Label})\n");

        var pts = new List<Point3d>();
        var lineIds = new List<int>(); var conf = new List<double>(); var depths = new List<double>();
        var sections = new List<Mesh>(); var bedrocks = new List<double>();
        int kept = 0, lonIdx = 0, traIdx = 0;
        foreach (var rec in recs)
        {
            double perp = havePositions ? positions[rec.FileIndex]
                        : rec.Axis == 0 ? (lonIdx++) * lonSpace : (traIdx++) * traSpace;
            foreach (var p in rec.Picks)
            {
                double tx = p[0]; double depth = p[2];
                // longitudinal: along X at y=perp. transverse: along Y at x=perp.
                pts.Add(rec.Axis == 0 ? new Point3d(tx, perp, -depth) : new Point3d(perp, tx, -depth));
                lineIds.Add(rec.FileIndex); conf.Add(p[1]); depths.Add(depth);
            }
            bedrocks.Add(rec.Bedrock);
            sections.Add(BuildEnergyMesh(rec.Energy, rec.Dx, rec.DtNs, rec.V, perp, rec.Axis));
            kept += rec.Picks.Count;
            rpt.AppendLine($"  line {rec.FileIndex} [{(rec.Axis == 0 ? "LON" : "TRA")}] @ {(rec.Axis == 0 ? "y" : "x")}={perp:0.##} m: " +
                           $"{rec.Picks.Count} picks, bedrock {rec.Bedrock:0.##} m ({rec.Fname})");
        }

        rpt.AppendLine($"TOTAL {kept} picks across {recs.Count} ingested line(s) -> feed Picks + Confidence " +
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

    // per-line scratch record (pass 1 -> placement).
    private sealed class LineRec
    {
        public readonly List<double[]> Picks = new List<double[]>(); // [traceX, energy, depth]
        public double[,] Energy;
        public double DtNs, Dx, V, Bedrock, Length;
        public int Axis;       // 0 = longitudinal (along X), 1 = transverse (along Y)
        public int FileIndex;
        public string Fname;
    }

    // depth-converted energy section -> coloured mesh at perpendicular position perp, oriented by axis
    // (0 = longitudinal: vertex (t*dx, perp, -depth); 1 = transverse: vertex (perp, t*dx, -depth)).
    private static Mesh BuildEnergyMesh(double[,] e, double dx, double dtNs, double v, double perp, int axis)
    {
        int ns = e.GetLength(0), ntr = e.GetLength(1);
        double emax = 1e-12; foreach (var x in e) if (x > emax) emax = x;
        var m = new Mesh();
        for (int t = 0; t < ntr; t++)
            for (int i = 0; i < ns; i++)
            {
                double depth = v * (i * dtNs) / 2.0;
                m.Vertices.Add(axis == 0 ? new Point3d(t * dx, perp, -depth) : new Point3d(perp, t * dx, -depth));
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
