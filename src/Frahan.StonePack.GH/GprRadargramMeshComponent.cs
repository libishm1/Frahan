#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Frahan.Masonry.Quarry.Ingestion;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH;

// =============================================================================
// GPR Radargram -> Mesh (2026-05-28). The GPR loaders only emitted trace-origin
// POINTS, so a radargram looked like "a line of dots". A radargram is a
// traces x samples amplitude IMAGE. This builds the readable thing: a vertical
// section ("curtain") mesh that follows the survey line in plan (trace X,Y) and
// goes DOWN by sample depth, with each vertex coloured by reflection amplitude.
// Reflector picks come out as points so you can overlay interpreted reflectors.
// =============================================================================

[RelatedComponent("Frahan > Ingest > GPR File Loader", Reason = "Same GPR file; this draws the radargram instead of just trace origins.")]
[RelatedComponent("Frahan > Quarry > BlockCutOpt Load Fractures", Reason = "Picked reflectors become fracture inputs for block-cut optimisation.")]
[Algorithm("GPR amplitude section visualisation",
    "Frahan-original: traces x samples amplitude grid -> vertex-coloured vertical-section mesh",
    Note = "Curtain follows trace X,Y in plan; depth downward = sample index x sample spacing.")]
[DesignApplication(
    "Read a GPR file and draw the radargram as a vertex-coloured vertical  section mesh: the curtain follows the...",
    DesignFlow.Bridges,
    Precedent = "Frahan-original radargram-to-mesh visualisation")]
public sealed class GprRadargramMeshComponent : GH_Component
{
    public GprRadargramMeshComponent()
        : base("GPR Radargram Mesh", "GprMesh",
            "Read a GPR file and draw the radargram as a vertex-coloured vertical " +
            "section mesh: the curtain follows the survey line (trace X,Y) and " +
            "goes down by sample depth; vertex colour = reflection amplitude " +
            "(blue low, white mid, red high). Reflector picks come out as points. " +
            "Use instead of GPR File Loader when you want to SEE the radargram.",
            "Frahan", "Ingest")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D05A04-1A2B-4C3D-9E4F-5A6B7C8D9E04");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("Downsample.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("File Path", "F", "Path to a GPR file (CSV radargram; see GPR File Loader for formats).", GH_ParamAccess.item);
        p.AddTextParameter("Id", "Id", "Optional radargram id (defaults to file name).", GH_ParamAccess.item);
        p[1].Optional = true;
        p.AddNumberParameter("Depth Scale", "Z",
            "Vertical exaggeration of the depth axis. 1 = true scale.", GH_ParamAccess.item, 1.0);
        p.AddNumberParameter("Trace Spacing", "Dx",
            "Fallback in-plane spacing between traces when traces share the same " +
            "X,Y (no survey geometry). 0 = use the trace X,Y as-is.", GH_ParamAccess.item, 0.0);
        p.AddNumberParameter("Contrast", "C",
            "Amplitude contrast (gamma on the normalized amplitude). 1 = linear; " +
            ">1 boosts faint reflectors.", GH_ParamAccess.item, 1.0);
        p.AddTextParameter("Picks CSV", "Pk",
            "OPTIONAL path to an interpreted-reflector picks CSV " +
            "(x_m,y_m,depth_m,confidence_01,label). Most GPR files carry NO picks, " +
            "so without this the Pick Points output is empty. Supply picks here to " +
            "drive GPR Fractures on Mesh.", GH_ParamAccess.item);
        p[5].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Radargram", "M", "Vertex-coloured radargram section mesh.", GH_ParamAccess.item);
        p.AddPointParameter("Pick Points", "P", "Interpreted reflector picks at depth.", GH_ParamAccess.list);
        p.AddTextParameter("Pick Labels", "L", "Label per pick.", GH_ParamAccess.list);
        p.AddNumberParameter("Pick Confidence", "Cf", "0..1 confidence per pick.", GH_ParamAccess.list);
        p.AddIntervalParameter("Amplitude Range", "A", "Min/max amplitude used for the colour map.", GH_ParamAccess.item);
        p.AddTextParameter("Report", "R", "Summary.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        string path = null, id = null, picksCsv = null;
        double depthScale = 1.0, traceSpacing = 0.0, contrast = 1.0;
        if (!da.GetData(0, ref path) || string.IsNullOrWhiteSpace(path)) return;
        da.GetData(1, ref id);
        da.GetData(2, ref depthScale);
        da.GetData(3, ref traceSpacing);
        da.GetData(4, ref contrast);
        da.GetData(5, ref picksCsv);
        if (!System.IO.File.Exists(path)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"File not found: {path}"); return; }
        if (contrast <= 0) contrast = 1.0;

        GprRadargram rg;
        try { rg = GprFileReader.Load(path, string.IsNullOrWhiteSpace(id) ? null : id); }
        catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"GPR load failed: {ex.Message}"); return; }

        // Picks: GprFileReader.Load never reads them (passes null), so most files
        // yield zero picks. An optional picks CSV supplies interpreted reflectors.
        IReadOnlyList<GprReflectorPick> reflectors = rg.Picks;
        if (!string.IsNullOrWhiteSpace(picksCsv))
        {
            if (System.IO.File.Exists(picksCsv))
            {
                try { reflectors = GprRadargramReader.ReadPicks(picksCsv); }
                catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Picks CSV read failed: {ex.Message}"); }
            }
            else AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Picks CSV not found: {picksCsv}");
        }

        int nt = rg.TraceCount;
        if (nt < 2) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Need >= 2 traces to build a section."); return; }

        // Rectangular grid: use the smallest sample count across traces.
        int ns = int.MaxValue;
        for (int i = 0; i < nt; i++) ns = Math.Min(ns, rg.Traces[i].SampleCount);
        if (ns < 2) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Traces have < 2 samples."); return; }

        // Do traces have real plan geometry, or are they coincident?
        var bb = BoundingBox.Empty;
        for (int i = 0; i < nt; i++) bb.Union(new Point3d(rg.Traces[i].X, rg.Traces[i].Y, 0));
        double planSpan = bb.IsValid ? bb.Diagonal.Length : 0.0;
        bool useIndex = planSpan < 1e-6 || traceSpacing > 0.0;
        double dx = traceSpacing > 0.0 ? traceSpacing : 1.0;

        // Amplitude range for the colour map.
        double amin = double.MaxValue, amax = double.MinValue;
        for (int i = 0; i < nt; i++)
        {
            var sa = rg.Traces[i].SampleAmplitudes;
            for (int j = 0; j < ns; j++) { double a = sa[j]; if (a < amin) amin = a; if (a > amax) amax = a; }
        }
        if (amax <= amin) amax = amin + 1.0;
        double span = amax - amin;

        var mesh = new Mesh();
        for (int i = 0; i < nt; i++)
        {
            var tr = rg.Traces[i];
            double ox = useIndex ? i * dx : tr.X;
            double oy = useIndex ? 0.0 : tr.Y;
            double dz = tr.SampleSpacingMetres;
            var sa = tr.SampleAmplitudes;
            for (int j = 0; j < ns; j++)
            {
                mesh.Vertices.Add(ox, oy, -(j * dz) * depthScale);
                double t = (sa[j] - amin) / span;            // 0..1
                t = Math.Pow(Math.Max(0.0, Math.Min(1.0, t)), 1.0 / contrast);
                mesh.VertexColors.Add(AmplitudeColor(t));
            }
        }
        for (int i = 0; i < nt - 1; i++)
            for (int j = 0; j < ns - 1; j++)
            {
                int a = i * ns + j, b = (i + 1) * ns + j;
                mesh.Faces.AddFace(a, b, b + 1, a + 1);
            }
        mesh.Normals.ComputeNormals();
        mesh.Compact();

        // Picks at depth.
        var picks = new List<Point3d>(reflectors.Count);
        var labels = new List<string>(reflectors.Count);
        var conf = new List<double>(reflectors.Count);
        for (int i = 0; i < reflectors.Count; i++)
        {
            var pk = reflectors[i];
            double px = useIndex ? NearestTraceIndex(rg, pk.X, pk.Y) * dx : pk.X;
            double py = useIndex ? 0.0 : pk.Y;
            picks.Add(new Point3d(px, py, -pk.DepthMetres * depthScale));
            labels.Add(pk.Label);
            conf.Add(pk.Confidence);
        }

        da.SetData(0, mesh);
        da.SetDataList(1, picks);
        da.SetDataList(2, labels);
        da.SetDataList(3, conf);
        da.SetData(4, new Interval(amin, amax));
        da.SetData(5,
            $"Radargram '{rg.Id}': {nt} traces x {ns} samples\n" +
            $"Layout   : {(useIndex ? $"index (Dx={dx})" : "trace X,Y (survey line)")}\n" +
            $"Depth    : {rg.Traces[0].SampleSpacingMetres * ns:F2} m (scale {depthScale})\n" +
            $"Amplitude: [{amin:G4}, {amax:G4}]\n" +
            $"Picks    : {reflectors.Count}{(reflectors.Count == 0 ? " (supply a Picks CSV to drive GPR Fractures on Mesh)" : "")}");
    }

    // Blue (low) -> white (mid) -> red (high) diverging map.
    private static Color AmplitudeColor(double t)
    {
        if (t < 0.5) { int u = (int)(255 * (t * 2.0)); return Color.FromArgb(u, u, 255); }
        int w = (int)(255 * (1.0 - (t - 0.5) * 2.0)); return Color.FromArgb(255, w, w);
    }

    private static int NearestTraceIndex(GprRadargram rg, double x, double y)
    {
        int best = 0; double bd = double.MaxValue;
        for (int i = 0; i < rg.TraceCount; i++)
        {
            double dx = rg.Traces[i].X - x, dy = rg.Traces[i].Y - y;
            double d = dx * dx + dy * dy;
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }
}
