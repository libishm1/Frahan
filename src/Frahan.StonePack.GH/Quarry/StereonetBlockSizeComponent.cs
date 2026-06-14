#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using Frahan.Core.Discontinuity;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Display;
using Rhino.Geometry;

namespace Frahan.GH.Quarry;

// =============================================================================
// StereonetBlockSizeComponent (D5F1004A, Frahan > Quarry)
//
// A SELF-PRESENTING report card: it takes the per-set dip / dip-direction /
// spacing / share from "Discontinuity Sets (Async)" (and, optionally, the
// facets.csv path for a pole-density cloud) and draws an equal-area (Schmidt)
// lower-hemisphere stereonet with great circles + set poles + labels, plus an
// in-situ block-size readout (Jv, Palmstrom Vb, RQD, Deq). It draws everything
// in the viewport itself (DrawViewportWires), so re-opening the .gh cold
// reproduces the figure with no external bake script.
//
// UNITS: spacings arrive in the cloud's own units. Use the Unit scale input to
// convert to metres. The block-size numbers are a proxy and are labelled so.
// =============================================================================

[Algorithm("Equal-area stereonet", "Schmidt/Lambert lower-hemisphere projection; Wulff toggle",
    Note = "r = sqrt(2) sin(theta/2); great circles as cyclographic traces.")]
[Algorithm("In-situ block size", "Palmstrom Jv / Vb / RQD (Palmstrom 1995, 2005)",
    Note = "Vb = s1 s2 s3 / (sin g12 sin g23 sin g31); Jv = sum 1/s; RQD ~ 110 - 2.5 Jv.")]
[RelatedComponent("Frahan > Quarry > Discontinuity Sets (Async)", Reason = "Upstream source of dip/dipdir/spacing/share + facets.csv.")]
public sealed class StereonetBlockSizeComponent : GH_Component
{
    public StereonetBlockSizeComponent()
        : base("Stereonet + Block Size", "Stereonet",
            "Self-presenting card: equal-area lower-hemisphere stereonet (great circles + set poles + facet-pole " +
            "density) plus an in-situ block-size readout (Jv, Palmstrom Vb, RQD, Deq). Feed the per-set Dip / Dip dir / " +
            "Spacing / Share (and optional Facets path) from Discontinuity Sets (Async). Set Unit scale to convert " +
            "spacing to metres; block-size numbers are a proxy.",
            "Frahan", "Quarry")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F1004A-ED9E-4ED9-A04A-ED9EED9E004A");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("DiscontinuitySets.png");

    // ---- preview state (filled in SolveInstance, drawn in DrawViewportWires) ----
    private readonly List<Polyline> _netLines = new List<Polyline>();
    private readonly List<Polyline> _greatCircles = new List<Polyline>();
    private readonly List<Point3d> _setPoles = new List<Point3d>();
    private readonly List<string> _setLabels = new List<string>();
    private readonly List<Point3d> _density = new List<Point3d>();
    private readonly List<Color> _setColors = new List<Color>();
    private Plane _net = Plane.WorldXY;
    private double _radius = 10.0;
    private string _readout = "";
    private Point3d _readoutAt = Point3d.Origin;
    private BoundingBox _clip = BoundingBox.Empty;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddNumberParameter("Dip", "D", "Per-set dip (deg).", GH_ParamAccess.list);
        p.AddNumberParameter("Dip dir", "Dd", "Per-set dip-direction (deg).", GH_ParamAccess.list);
        p.AddNumberParameter("Spacing", "Sp", "Per-set mean normal spacing (cloud units).", GH_ParamAccess.list);
        p.AddNumberParameter("Share", "Sh", "Per-set point share (optional; picks the 3 dominant sets).", GH_ParamAccess.list);
        p.AddTextParameter("Facets path", "Fp", "Optional facets.csv path (from 'Keep facets') for the pole-density cloud.", GH_ParamAccess.item);
        p.AddPlaneParameter("Plane", "Pl", "Base plane for the net (origin + X=East, Y=North). Default World XY.", GH_ParamAccess.item, Plane.WorldXY);
        p.AddNumberParameter("Radius", "R", "Net radius (model units).", GH_ParamAccess.item, 10.0);
        p.AddNumberParameter("Unit scale", "U", "Multiplies spacing into metres for the block-size math (e.g. 1 if already metres).", GH_ParamAccess.item, 1.0);
        p.AddBooleanParameter("Equal area", "Ea", "True = equal-area (Schmidt); false = equal-angle (Wulff).", GH_ParamAccess.item, true);
        for (int i = 3; i <= 5; i++) p[i].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddCurveParameter("Net", "N", "Primitive circle + N/E/S/W ticks.", GH_ParamAccess.list);
        p.AddCurveParameter("Great circles", "Gc", "Cyclographic trace per set.", GH_ParamAccess.list);
        p.AddPointParameter("Set poles", "P", "Projected pole per set.", GH_ParamAccess.list);
        p.AddPointParameter("Facet poles", "Fp", "Projected facet-pole density cloud (if Facets path given).", GH_ParamAccess.list);
        p.AddNumberParameter("Jv", "Jv", "Volumetric joint count (joints/m^3 proxy).", GH_ParamAccess.item);
        p.AddNumberParameter("Vb", "Vb", "Palmstrom block volume (m^3; NaN if < 3 sets).", GH_ParamAccess.item);
        p.AddNumberParameter("RQD", "Rq", "RQD proxy (0..100).", GH_ParamAccess.item);
        p.AddNumberParameter("Deq", "De", "Equivalent block diameter (m).", GH_ParamAccess.item);
        p.AddTextParameter("Report", "Re", "Per-set table + block-size readout + unit notes.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        ClearPreview();

        var dip = new List<double>(); var dipdir = new List<double>(); var spacing = new List<double>();
        var share = new List<double>();
        if (!da.GetDataList(0, dip) || !da.GetDataList(1, dipdir) || !da.GetDataList(2, spacing))
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Provide Dip, Dip dir and Spacing lists."); return; }
        da.GetDataList(3, share);
        string facetsPath = null; da.GetData(4, ref facetsPath);
        _net = Plane.WorldXY; da.GetData(5, ref _net);
        _radius = 10.0; da.GetData(6, ref _radius); if (_radius <= 0) _radius = 10.0;
        double unit = 1.0; da.GetData(7, ref unit);
        bool equalArea = true; da.GetData(8, ref equalArea);

        int n = Math.Min(dip.Count, Math.Min(dipdir.Count, spacing.Count));
        if (n == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Empty input."); return; }

        // poles per set
        var poles = new List<Vector3d>(n);
        for (int i = 0; i < n; i++) poles.Add(OrientationMath.NormalFromDipDipDir(dip[i], dipdir[i]));

        // ---- net frame (primitive circle + ticks + cardinal labels) ----
        BuildNet(equalArea);

        // ---- per-set great circles + poles ----
        for (int i = 0; i < n; i++)
        {
            var col = SetColor(i);
            _setColors.Add(col);
            var gc = StereonetProjection.GreatCircleOnPlane(poles[i], _net, _radius, !equalArea, 96);
            _greatCircles.Add(new Polyline(gc));
            var pp = StereonetProjection.ProjectOnPlane(poles[i], _net, _radius, !equalArea);
            _setPoles.Add(pp);
            _setLabels.Add($"S{i + 1} {dip[i]:F0}/{dipdir[i]:F0}");
        }

        // ---- facet-pole density cloud ----
        if (!string.IsNullOrWhiteSpace(facetsPath) && File.Exists(facetsPath))
        {
            foreach (var pole in ReadFacetPoles(facetsPath))
                _density.Add(StereonetProjection.ProjectOnPlane(pole, _net, _radius, !equalArea));
        }

        // ---- block size ----
        var bs = BlockSizeMath.Compute(poles, spacing.GetRange(0, n), unit,
            share.Count == n ? share : null, unit == 1.0 ? "m (assumed)" : "scaled to m");
        _readout = BuildReadout(dip, dipdir, spacing, share, n, bs, unit, equalArea);
        _readoutAt = _net.PointAt(_radius * 1.15, _radius);

        // ---- outputs ----
        da.SetDataList(0, _netLines.Select(pl => pl.ToPolylineCurve()));
        da.SetDataList(1, _greatCircles.Select(pl => pl.ToPolylineCurve()));
        da.SetDataList(2, _setPoles);
        da.SetDataList(3, _density);
        da.SetData(4, bs.Jv);
        da.SetData(5, bs.Vb);
        da.SetData(6, bs.Rqd);
        da.SetData(7, bs.Deq);
        da.SetData(8, _readout);

        if (!bs.VolumeDefined)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, bs.Descriptor + " (see Report).");

        // clipping box for preview
        _clip = BoundingBox.Empty;
        foreach (var pl in _netLines) _clip.Union(pl.BoundingBox);
        foreach (var pl in _greatCircles) _clip.Union(pl.BoundingBox);
        _clip.Union(_readoutAt);
    }

    // ---- net construction --------------------------------------------------
    private void BuildNet(bool equalArea)
    {
        // primitive circle (theta = 90 -> r = R)
        var circle = new Polyline();
        for (int i = 0; i <= 180; i++)
        {
            double a = 2.0 * Math.PI * i / 180.0;
            circle.Add(_net.PointAt(_radius * Math.Sin(a), _radius * Math.Cos(a)));
        }
        _netLines.Add(circle);
        // cardinal ticks
        double tk = _radius * 0.04;
        AddTick(0, _radius, tk);        // N (+Y)
        AddTick(_radius, 0, tk);        // E (+X)
        AddTick(0, -_radius, tk);       // S
        AddTick(-_radius, 0, tk);       // W
        // centre cross
        _netLines.Add(new Polyline(new[] { _net.PointAt(-tk, 0), _net.PointAt(tk, 0) }));
        _netLines.Add(new Polyline(new[] { _net.PointAt(0, -tk), _net.PointAt(0, tk) }));
    }

    private void AddTick(double x, double y, double tk)
    {
        var dir = new Vector2d(x, y); if (dir.Length > 0) dir.Unitize();
        _netLines.Add(new Polyline(new[]
        {
            _net.PointAt(x, y),
            _net.PointAt(x - dir.X * tk * 2, y - dir.Y * tk * 2)
        }));
    }

    // ---- facets.csv: cx,cy,cz,nx,ny,nz,set,npts ----------------------------
    private static IEnumerable<Vector3d> ReadFacetPoles(string path)
    {
        string[] lines;
        try { lines = File.ReadAllLines(path); } catch { yield break; }
        int nxCol = 3, nyCol = 4, nzCol = 5; int startRow = 0;
        if (lines.Length > 0)
        {
            var h = lines[0].Split(',').Select(s => s.Trim().ToLowerInvariant()).ToArray();
            int ix = Array.IndexOf(h, "nx"), iy = Array.IndexOf(h, "ny"), iz = Array.IndexOf(h, "nz");
            if (ix >= 0 && iy >= 0 && iz >= 0) { nxCol = ix; nyCol = iy; nzCol = iz; startRow = 1; }
            else if (!double.TryParse(h.ElementAtOrDefault(0), NumberStyles.Any, CultureInfo.InvariantCulture, out _)) startRow = 1;
        }
        for (int i = startRow; i < lines.Length; i++)
        {
            var c = lines[i].Split(',');
            if (c.Length <= nzCol) continue;
            if (double.TryParse(c[nxCol], NumberStyles.Any, CultureInfo.InvariantCulture, out double nx) &&
                double.TryParse(c[nyCol], NumberStyles.Any, CultureInfo.InvariantCulture, out double ny) &&
                double.TryParse(c[nzCol], NumberStyles.Any, CultureInfo.InvariantCulture, out double nz))
            {
                var v = new Vector3d(nx, ny, nz);
                if (v.SquareLength > 1e-12) yield return v;
            }
        }
    }

    private static string BuildReadout(List<double> dip, List<double> dipdir, List<double> spacing,
        List<double> share, int n, BlockSizeResult bs, double unit, bool equalArea)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Stereonet ({(equalArea ? "equal-area / Schmidt" : "equal-angle / Wulff")}), lower hemisphere");
        sb.AppendLine($"{n} joint set(s):");
        for (int i = 0; i < n; i++)
        {
            string sh = share.Count == n ? $", share {share[i] * 100:F0}%" : "";
            sb.AppendLine($"  S{i + 1}: dip {dip[i]:F1} / dipdir {dipdir[i]:F1}, spacing {spacing[i]:G3}{sh}");
        }
        sb.AppendLine();
        sb.AppendLine($"Block size (spacing x {unit:G3} -> m; PROXY):");
        sb.AppendLine($"  Jv  = {bs.Jv:F2} joints/m^3");
        sb.AppendLine($"  RQD = {bs.Rqd:F0}");
        if (bs.VolumeDefined)
        {
            sb.AppendLine($"  Vb  = {bs.Vb:G3} m^3   ({bs.Descriptor})");
            sb.AppendLine($"  Ib  = {bs.Ib:G3} m,  Deq = {bs.Deq:G3} m");
        }
        else sb.AppendLine($"  Vb  = undefined  ({bs.Descriptor})");
        foreach (var note in bs.Notes) sb.AppendLine("  ! " + note);
        if (unit == 1.0) sb.AppendLine("  ! Unit scale = 1: spacings assumed already in metres. Set it if the cloud is cm/mm.");
        return sb.ToString().TrimEnd();
    }

    private static Color SetColor(int i)
    {
        Color[] palette =
        {
            Color.FromArgb(220,50,47), Color.FromArgb(38,139,210), Color.FromArgb(133,153,0),
            Color.FromArgb(181,137,0), Color.FromArgb(211,54,130), Color.FromArgb(42,161,152),
            Color.FromArgb(203,75,22), Color.FromArgb(108,113,196)
        };
        return palette[i % palette.Length];
    }

    private void ClearPreview()
    {
        _netLines.Clear(); _greatCircles.Clear(); _setPoles.Clear();
        _setLabels.Clear(); _density.Clear(); _setColors.Clear();
        _readout = ""; _clip = BoundingBox.Empty;
    }

    // ---- self-presenting viewport drawing ----------------------------------
    public override BoundingBox ClippingBox => _clip.IsValid ? _clip : base.ClippingBox;

    public override void DrawViewportWires(IGH_PreviewArgs args)
    {
        var d = args.Display;
        var frame = Color.FromArgb(120, 120, 120);
        foreach (var pl in _netLines) d.DrawPolyline(pl, frame, 1);
        for (int i = 0; i < _greatCircles.Count; i++)
            d.DrawPolyline(_greatCircles[i], _setColors.ElementAtOrDefault(i), 2);
        if (_density.Count > 0) d.DrawPoints(_density, PointStyle.RoundSimple, 2, Color.FromArgb(90, 90, 90));
        for (int i = 0; i < _setPoles.Count; i++)
        {
            var c = _setColors.ElementAtOrDefault(i);
            d.DrawPoint(_setPoles[i], PointStyle.RoundControlPoint, 6, c);
            if (i < _setLabels.Count) d.Draw2dText(_setLabels[i], c, _setPoles[i], false, 14);
        }
        // cardinal letters
        d.Draw2dText("N", frame, _net.PointAt(0, _radius * 1.05), true, 16);
        d.Draw2dText("E", frame, _net.PointAt(_radius * 1.05, 0), true, 16);
        // readout
        if (!string.IsNullOrEmpty(_readout))
            d.Draw2dText(_readout, Color.FromArgb(40, 40, 40), _readoutAt, false, 13);
    }
}
