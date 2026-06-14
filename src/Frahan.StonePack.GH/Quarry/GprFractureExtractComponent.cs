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
// GprFractureExtractComponent -- GH adapter over the Rhino-free Core GPR chain
// (RadargramProcessor + FractureExtractor; Fft; GprPresets). Reads a GPR file
// (IDS .dt / MALA .rd3 / GSSI .dzt / pulseEKKO .dt1 / SEG-Y / CSV via
// GprFileReader), runs the validated processing chain for the chosen STONE x
// FREQUENCY preset, and outputs the extracted fracture picks + a depth-converted
// energy mesh for the visual pass.
//
// PRESET-DRIVEN, all knobs TOGGLEABLE: pick a preset (marble_600, granite_160,
// travertine_390, andesite_390, limestone_200) for defaults tuned per antenna /
// stone, then override any single parameter (sentinel < 0 / NaN = "use preset").
// Velocity is the highest-leverage value (depth = v*t/2) -- override it with a
// WARR/CMP-measured velocity whenever you have one.
//
// Validated on real data: HITL cards in outputs/2026-06-04/gpr_extraction (3
// Botticino marble grids + 2 Grimsel granite tunnels all PASS the fracture-
// modelling criteria). ComponentGuid: A7E0B0F1-0C0F-4A16-9E3D-0FACE0FACE02.
//
// Frahan > Quarry > GPR fracture extraction.
// =============================================================================

/// <summary>
/// Frahan &gt; Quarry &gt; GPR Fracture Extract. Process a GPR radargram and
/// extract fracture reflectors (f-k migration + Hilbert energy + USGS continuity)
/// with stone/frequency presets. Wraps Core RadargramProcessor + FractureExtractor.
/// </summary>
[RelatedComponent("Frahan > Ingest > GPR Radargram Mesh", Reason = "Alternative radargram visual; this one adds processing + extraction.")]
[RelatedComponent("Frahan > Quarry > GPR Fractures on Mesh", Reason = "Drape these extracted picks onto a bench/block mesh.")]
[RelatedComponent("Frahan > Quarry > Overburden To Rock Face", Reason = "The deepest continuous reflector = bedrock surface z_r for the overburden strip.")]
[Algorithm("GPR fracture extraction: f-k (Stolt) migration + Hilbert instantaneous energy + USGS continuity",
    "Stolt 1978 (f-k migration); Taner 1979 (instantaneous attributes via Hilbert); USGS Mirror Lake WRIR 99-4018C (>=40-trace continuity); Porsani 2006 + Isakova 2021 (high-energy = fracture)",
    Note = "v=c/sqrt(eps_r); depth=v*t/2. Energy E=|s+iH{s}|^2; fractures are high-E continuous reflectors, intact stone is low-E. Presets per stone x antenna frequency.")]
[DesignApplication(
    "Map fractures / cavities inside a quarry block from a GPR scan before cutting, to avoid waste and plan blocks",
    DesignFlow.BottomUp,
    Precedent = "Bondua/Bruno/Elkarmoty 2024 (Botticino marble); Isakova 2021 (Karelia granite); Porsani 2006 (Capao Bonito granite)")]
public sealed class GprFractureExtractComponent : FrahanComponentBase
{
    public GprFractureExtractComponent()
        : base("GPR Fracture Extract", "GprFracture",
            "Process a GPR radargram and extract fracture reflectors. Reads IDS .dt / " +
            "MALA .rd3 / GSSI .dzt / pulseEKKO .dt1 / SEG-Y / CSV. Runs dewow -> background " +
            "removal -> time-zero mute -> gain -> f-k (Stolt) migration -> Hilbert energy -> " +
            "USGS >=40-trace continuity extraction. Choose a STONE x FREQUENCY preset for " +
            "tuned defaults (marble_600, granite_160, ...); override any knob (set < 0 to use " +
            "the preset). Outputs fracture picks, depths, confidence, and a depth-converted " +
            "energy mesh. Note: Geoscanners .gsf must be exported to SEG-Y (GPRSoft) first. " +
            "Workflows cross-checked against RGPR (the open R GPR-processing package) in the companion paper.",
            "Frahan", "Quarry")
    {
    }

    public override Guid ComponentGuid => new Guid("A7E0B0F1-0C0F-4A16-9E3D-0FACE0FACE02");
    public override GH_Exposure Exposure => GH_Exposure.secondary;
    protected override Bitmap Icon => IconProvider.Load("GprIngest.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("File", "F",
            "Path to the GPR radargram file (.dt / .rd3 / .dzt / .dt1 / .sgy / .csv).",
            GH_ParamAccess.item);
        p.AddTextParameter("Preset", "Pr",
            "Stone x frequency preset for tuned defaults: " +
            "marble_600, granite_160, travertine_390, andesite_390, limestone_200.",
            GH_ParamAccess.item, "granite_160");
        p.AddNumberParameter("Velocity", "v",
            "EM velocity (m/ns), depth = v*t/2. < 0 = use the preset value. Override with a " +
            "WARR/CMP-measured velocity when available (highest-leverage parameter).",
            GH_ParamAccess.item, -1.0);
        p.AddBooleanParameter("Migrate", "Mig",
            "f-k (Stolt) migration to reposition dipping reflectors / collapse diffractions. " +
            "Leave unset to use the preset.", GH_ParamAccess.item, true);
        p.AddBooleanParameter("Depth Equalize", "Eq",
            "Per-depth energy normalisation so deep weak reflectors surface. Preset default.",
            GH_ParamAccess.item, true);
        p.AddNumberParameter("Energy Quantile", "Q",
            "Energy quantile (0..1) above which a sample is a fracture candidate. < 0 = preset " +
            "(typically 0.985; lower it for broad CAVITY anomalies).", GH_ParamAccess.item, -1.0);
        p.AddIntegerParameter("Continuity Traces", "C",
            "USGS lateral-continuity window in traces (>= 40 keeps only continuous reflectors). " +
            "< 0 = preset (41).", GH_ParamAccess.item, -1);
        p.AddIntegerParameter("Min Support", "S",
            "Minimum like-picks within the continuity window to keep a pick. < 0 = preset (12).",
            GH_ParamAccess.item, -1);
        p.AddNumberParameter("Max Dip", "Dip",
            "USGS dip gate (deg): continuity is followed along reflector dips up to this angle; " +
            "steeper events are rejected. 45 = the USGS crystalline-rock standard. < 0 = default 45. " +
            "Raise toward 60 to keep steeper shear zones; lower toward 20 for sub-horizontal only.",
            GH_ParamAccess.item, -1.0);
        p.AddIntegerParameter("Trace Mode", "Tm",
            "How discrete picks are grouped into continuous fracture lines: 0 = connected-components " +
            "(simple, merges crossings), 1 = orientation-gated (separates crossing fractures by local " +
            "dip). Default 0.", GH_ParamAccess.item, 0);
        // --- tolerance-ladder inputs (uncertainty metric) ---
        p.AddNumberParameter("Perm Uncertainty", "dEr",
            "Absolute uncertainty of the relative permittivity eps_r (e.g. 1.0 for eps_r 9+-1). Drives " +
            "the depth velocity error sigma_v/v = 0.5*dEr/eps_r, the dominant deep-fracture deviation. " +
            "Lower it (toward 0.3) when you have a CMP/WARR velocity calibration. Default 1.0.",
            GH_ParamAccess.item, 1.0);
        p.AddNumberParameter("Tolerance", "T",
            "Target tolerance T (m) for the confidence metric = probability each pick's depth is within " +
            "+-T of the truth (Gaussian, 1-sigma). Default 0.02 (2 cm precision-cutting).",
            GH_ParamAccess.item, 0.02);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddPointParameter("Fracture Picks", "P",
            "Extracted fracture pick points at (distance, 0, -depth) in metres.", GH_ParamAccess.list);
        p.AddNumberParameter("Depths", "D", "Depth (m) of each pick.", GH_ParamAccess.list);
        p.AddNumberParameter("Confidence", "Cf", "Normalised energy (0..1) of each pick.", GH_ParamAccess.list);
        p.AddMeshParameter("Energy Mesh", "E",
            "Depth-converted energy section as a mesh (x=distance, z=-depth), vertex-coloured " +
            "by instantaneous energy (blue=intact -> red=fracture).", GH_ParamAccess.item);
        p.AddNumberParameter("Bedrock Depth", "Z",
            "Depth (m) of the deepest continuous reflector = candidate bedrock / rock-face top " +
            "(feeds Overburden To Rock Face).", GH_ParamAccess.item);
        p.AddIntegerParameter("Fracture Id", "Fid",
            "Continuous-fracture id per pick (aligned to Fracture Picks; 0 = unassigned). Feed into " +
            "'GPR Fractures on Mesh' Labels to drape each fracture onto a bench/block mesh.",
            GH_ParamAccess.list);
        p.AddCurveParameter("Fracture Lines", "L",
            "Continuous fracture trace polylines in the section plane (x, 0, -depth), one per " +
            "reflector (FractureTracer). Extrude / loft these into fracture surfaces.",
            GH_ParamAccess.list);
        p.AddTextParameter("Report", "Rpt", "Parameters used + extraction summary.", GH_ParamAccess.item);
        // --- tolerance-ladder outputs (uncertainty metric) ---
        p.AddNumberParameter("Depth Sigma", "Ds",
            "Per-pick 1-sigma depth uncertainty (m), aligned to Fracture Picks: the GPR time->depth " +
            "deviation sqrt((depth*sigma_v/v)^2 + (lambda/4)^2). Grows with depth (velocity error) " +
            "off a lambda/4 resolution floor. Stage 1 of the GPR->fracture->mesh tolerance ladder.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Confidence within T", "Cf%",
            "OPTIMISATION METRIC: mean over the picks of P(|depth deviation| <= T) = erf(T/(sigma*sqrt2)). " +
            "0..1 (= the fraction of the fracture trace within +-T of truth). Raise it by calibrating " +
            "velocity (Perm Uncertainty down) or higher frequency. Section-level (no inter-line term).",
            GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        string file = null, presetKey = "granite_160";
        double vOverride = -1.0, qOverride = -1.0, dipOverride = -1.0;
        bool migrate = true, equalize = true;
        int contOverride = -1, supOverride = -1, traceMode = 0;
        if (!da.GetData(0, ref file) || string.IsNullOrWhiteSpace(file))
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No GPR file path provided."); return; }
        da.GetData(1, ref presetKey);
        da.GetData(2, ref vOverride);
        bool migrateSet = da.GetData(3, ref migrate);
        bool eqSet = da.GetData(4, ref equalize);
        da.GetData(5, ref qOverride);
        da.GetData(6, ref contOverride);
        da.GetData(7, ref supOverride);
        da.GetData(8, ref dipOverride);
        da.GetData(9, ref traceMode);
        double epsUnc = 1.0, tolM = 0.02;
        da.GetData(10, ref epsUnc);
        da.GetData(11, ref tolM);

        if (!System.IO.File.Exists(file))
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "File not found: " + file); return; }
        if (file.EndsWith(".gsf", StringComparison.OrdinalIgnoreCase))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Geoscanners .gsf is proprietary (embedded per-trace GPS). Export to SEG-Y with " +
                "GPRSoft first, then load the .sgy here.");
            return;
        }

        if (!GprPresets.TryGet(presetKey, out var preset))
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
            $"Unknown preset '{presetKey}'. Have: {string.Join(", ", GprPresets.Keys)}"); return; }
        if (!preset.IsEmpirical)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Preset '{presetKey}' is literature-default (not yet tuned on real data); verify the velocity.");

        GprRadargram rg;
        try { rg = GprFileReader.Load(file, null); }
        catch (Exception ex)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "GPR load failed: " + ex.Message); return; }
        if (rg.TraceCount < 2)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Radargram has too few traces."); return; }

        var proc = new RadargramProcessor();
        var fx = new FractureExtractor();
        preset.Apply(proc, fx);
        double v = vOverride > 0 ? vOverride : preset.VelocityMNsPerNs;
        if (migrateSet) proc.Migrate = migrate;
        if (eqSet) proc.DepthEqualize = equalize;
        if (qOverride > 0) fx.EnergyQuantile = qOverride;
        if (contOverride > 0) fx.ContinuityWindowTraces = contOverride;
        if (supOverride > 0) fx.MinContinuitySupport = supOverride;
        if (dipOverride > 0) fx.DipMaxDeg = Math.Min(89.0, dipOverride);   // USGS dip gate (deg)

        double[,] B;
        double dtNs, dx;
        try { B = RadargramProcessor.ToGrid(rg, out dtNs, out dx); }
        catch (Exception ex)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Grid build failed: " + ex.Message); return; }

        int ns = B.GetLength(0), ntr = B.GetLength(1);
        // dtNs is the TRUE two-way sample interval from the reader (velocity-independent);
        // depth = v*(i*dtNs)/2 with the stone velocity v.

        double[,] energy;
        IReadOnlyList<FractureExtractor.FracturePick> picks;
        try
        {
            energy = proc.Run(B, dtNs, dx, v);
            picks = fx.Extract(energy, dtNs, dx, v);
        }
        catch (Exception ex)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Processing failed: " + ex.Message); return; }

        // outputs
        var pts = new List<Point3d>(picks.Count);
        var depths = new List<double>(picks.Count);
        var conf = new List<double>(picks.Count);
        double bedrock = 0.0;
        foreach (var p in picks)
        {
            double xpos = rg.Traces[Math.Min(p.TraceIndex, ntr - 1)].X;
            pts.Add(new Point3d(xpos, 0.0, -p.DepthMetres));
            depths.Add(p.DepthMetres);
            conf.Add(p.Energy);
            if (p.DepthMetres > bedrock) bedrock = p.DepthMetres;
        }

        Mesh emesh = BuildEnergyMesh(energy, dx, dtNs, v);

        // group discrete picks into continuous fracture LINES (FractureTracer)
        var tracer = new FractureTracer
        {
            Mode = traceMode == 1 ? FractureTraceMode.OrientationGated : FractureTraceMode.ConnectedComponents,
            MinSpanTraces = fx.ContinuityWindowTraces,
        };
        var trace = tracer.Trace(picks, ns, ntr, dtNs, dx, v);
        var fids = new List<int>(trace.LabelPerPick);            // aligned 1:1 to `picks` / `pts`
        var lines = new List<Curve>(trace.Lines.Count);
        double x0base = rg.Traces[0].X;                          // section x origin (trace 0)
        foreach (var fl in trace.Lines)
        {
            var poly = new Polyline(fl.PointCount);
            for (int k = 0; k < fl.PointCount; k++)
                poly.Add(new Point3d(x0base + fl.X[k], 0.0, -fl.Depth[k]));
            if (poly.Count >= 2) lines.Add(poly.ToPolylineCurve());
        }

        // tolerance ladder -- stage 1 (GPR time->depth) per-pick 1-sigma + confidence within T
        double sigmaVRel = FractureUncertainty.VelocityRelUncertainty(preset.EpsR, epsUnc);
        double lambda4 = FractureUncertainty.LambdaQuarter(v, preset.FrequencyMhz);
        double[] sigmaArr = FractureUncertainty.SectionSigma(depths.ToArray(), sigmaVRel, lambda4);
        var sigmaList = new List<double>(sigmaArr);
        var summ = FractureUncertainty.Summarise(sigmaArr, tolM);

        string rpt =
            $"preset={presetKey} ({preset.Label}) | v={v:0.###} m/ns | dt={dtNs:0.####} ns | dx={dx:0.####} m | " +
            $"migrate={proc.Migrate} equalize={proc.DepthEqualize} Q={fx.EnergyQuantile:0.###} " +
            $"cont={fx.ContinuityWindowTraces} sup={fx.MinContinuitySupport} dip<={fx.DipMaxDeg:0.#}deg | " +
            $"{ntr} traces x {ns} samples -> {picks.Count} picks -> {trace.LineCount} fracture lines " +
            $"({(traceMode == 1 ? "orientation-gated" : "connected")}) | " +
            $"depth window {v * ns * dtNs / 2.0:0.##} m | bedrock candidate {bedrock:0.##} m | " +
            $"tolerance ladder: sigma_v/v={sigmaVRel * 100:0.#}% (eps_r {preset.EpsR:0.#}+-{epsUnc:0.#}), " +
            $"lambda/4={lambda4:0.###} m -> mean depth sigma={summ.MeanSigma:0.###} m, " +
            $"P95={summ.P95Sigma:0.###} m; confidence within +-{tolM * 100:0.#}cm = {summ.Confidence * 100:0.#}%";

        da.SetDataList(0, pts);
        da.SetDataList(1, depths);
        da.SetDataList(2, conf);
        da.SetData(3, emesh);
        da.SetData(4, bedrock);
        da.SetDataList(5, fids);
        da.SetDataList(6, lines);
        da.SetData(7, rpt);
        da.SetDataList(8, sigmaList);
        da.SetData(9, summ.Confidence);
    }

    // depth-converted energy section -> coloured mesh (x = distance, z = -depth).
    private static Mesh BuildEnergyMesh(double[,] e, double dx, double dtNs, double v)
    {
        int ns = e.GetLength(0), ntr = e.GetLength(1);
        double emax = 1e-12; foreach (var x in e) if (x > emax) emax = x;
        var m = new Mesh();
        for (int t = 0; t < ntr; t++)
            for (int i = 0; i < ns; i++)
            {
                double depth = v * (i * dtNs) / 2.0;
                m.Vertices.Add(new Point3d(t * dx, 0.0, -depth));
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
        // simple blue->cyan->yellow->red colormap
        double r = Math.Max(0, Math.Min(1, 1.5 - Math.Abs(4 * f - 3)));
        double g = Math.Max(0, Math.Min(1, 1.5 - Math.Abs(4 * f - 2)));
        double b = Math.Max(0, Math.Min(1, 1.5 - Math.Abs(4 * f - 1)));
        return Color.FromArgb((int)(255 * r), (int)(255 * g), (int)(255 * b));
    }
}
