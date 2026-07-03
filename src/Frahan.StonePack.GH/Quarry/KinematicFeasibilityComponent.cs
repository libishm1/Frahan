#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Frahan.Core.Discontinuity;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Display;
using Rhino.Geometry;

namespace Frahan.GH.Quarry;

// =============================================================================
// KinematicFeasibilityComponent (D5F1004F, Frahan > Quarry)
//
// A self-presenting Markland / Hoek-Bray kinematic screen: given a cut face
// (dip / dip-direction) and a joint friction angle, it tests every joint set for
// planar sliding and flexural toppling and every set pair for wedge sliding, and
// draws a lower-hemisphere POLE stereonet with the friction circle, the slope
// great circle, and each set pole coloured red (a feasible failure mode) or
// green (favourable). The bread-and-butter rock-slope screen a raw pole plot
// leaves out.
// =============================================================================

[Algorithm("Kinematic feasibility", "Markland (1972) / Hoek & Bray (1981) / Goodman & Bray (1976)",
    Note = "Planar & toppling per set, wedge per pair; daylight vs apparent face dip, slip vs friction.")]
[RelatedComponent("Frahan > Quarry > Discontinuity Sets (Cloud)", Reason = "Upstream source of per-set Dip / Dip dir.")]
[RelatedComponent("Frahan > Quarry > Stereonet + Block Size", Reason = "Same stereonet; this adds the failure screen.")]
public sealed class KinematicFeasibilityComponent : FrahanComponentBase
{
    public KinematicFeasibilityComponent()
        : base("Kinematic Feasibility", "Kinematic",
            "Markland / Hoek-Bray kinematic screen for a rock cut. Feed per-set Dip / Dip dir + the cut face " +
            "(Slope dip / Slope dip dir) + joint Friction. Tests planar sliding & flexural toppling per set and wedge " +
            "sliding per pair; draws a pole stereonet with the friction circle, slope great circle, and set poles " +
            "coloured by feasibility. Re-opens cold as a finished figure.",
            "Frahan", "Quarry")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F1004F-ED9E-4ED9-A04F-ED9EED9E004F");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("KinematicFeasibility.png");

    // ---- preview state ----
    private readonly List<Polyline> _net = new List<Polyline>();
    private Polyline _friction = new Polyline();
    private Polyline _slopeGc = new Polyline();
    private readonly List<Point3d> _poles = new List<Point3d>();
    private readonly List<Color> _poleCols = new List<Color>();
    private readonly List<string> _poleLabels = new List<string>();
    private Plane _basePlane = Plane.WorldXY;
    private double _radius = 10.0;
    private string _readout = "";
    private Point3d _readoutAt = Point3d.Origin;
    private BoundingBox _clip = BoundingBox.Empty;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddNumberParameter("Dip", "D", "Per-set dip (deg).", GH_ParamAccess.list);
        p.AddNumberParameter("Dip dir", "Dd", "Per-set dip-direction (deg).", GH_ParamAccess.list);
        p.AddNumberParameter("Slope dip", "Sd", "Cut-face dip (deg).", GH_ParamAccess.item, 65.0);
        p.AddNumberParameter("Slope dip dir", "Sdd", "Cut-face dip-direction (deg).", GH_ParamAccess.item, 180.0);
        p.AddNumberParameter("Friction", "F", "Joint friction angle (deg).", GH_ParamAccess.item, 30.0);
        p.AddNumberParameter("Lateral", "Ll", "Lateral limit for daylight / toppling (deg).", GH_ParamAccess.item, 20.0);
        p.AddPlaneParameter("Plane", "Pl", "Base plane for the stereonet.", GH_ParamAccess.item, Plane.WorldXY);
        p.AddNumberParameter("Radius", "R", "Net radius (model units).", GH_ParamAccess.item, 10.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddBooleanParameter("Planar", "Pl", "Per-set planar-sliding feasibility.", GH_ParamAccess.list);
        p.AddTextParameter("Wedge", "We", "Feasible wedge pairs (SixSj: plunge/trend).", GH_ParamAccess.list);
        p.AddBooleanParameter("Toppling", "To", "Per-set flexural-toppling feasibility.", GH_ParamAccess.list);
        p.AddIntegerParameter("Feasible", "N", "Total feasible failure modes.", GH_ParamAccess.item);
        p.AddPointParameter("Set poles", "P", "Projected set poles (red = a feasible failure).", GH_ParamAccess.list);
        p.AddCurveParameter("Net", "Nt", "Net circle + friction circle + slope great circle.", GH_ParamAccess.list);
        p.AddTextParameter("Report", "Re", "Per-mode screen with governing angles.", GH_ParamAccess.item);
    }

    private static readonly Color RedFail = Color.FromArgb(200, 40, 40);
    private static readonly Color GreenOk = Color.FromArgb(40, 160, 70);
    private static readonly Color Frame = Color.FromArgb(120, 120, 120);
    private static readonly Color FricCol = Color.FromArgb(70, 130, 180);
    private static readonly Color SlopeCol = Color.FromArgb(180, 100, 20);

    protected override void SolveSafe(IGH_DataAccess da)
    {
        ClearPreview();

        var dip = new List<double>(); var dipdir = new List<double>();
        if (!da.GetDataList(0, dip) || !da.GetDataList(1, dipdir) || dip.Count == 0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Provide Dip and Dip dir lists."); return; }
        int n = Math.Min(dip.Count, dipdir.Count);

        double sDip = 65, sDd = 180, fric = 30, lat = 20;
        da.GetData(2, ref sDip); da.GetData(3, ref sDd); da.GetData(4, ref fric); da.GetData(5, ref lat);
        _basePlane = Plane.WorldXY; da.GetData(6, ref _basePlane);
        _radius = 10.0; da.GetData(7, ref _radius); if (_radius <= 0) _radius = 10.0;

        var res = KinematicAnalysis.Analyze(dip.GetRange(0, n), dipdir.GetRange(0, n), sDip, sDd, fric, lat);

        // per-set feasibility (planar OR toppling OR any wedge involving it)
        var planar = new bool[n]; var topple = new bool[n]; var unstable = new bool[n];
        for (int i = 0; i < n && i < res.Planar.Count; i++) { planar[i] = res.Planar[i].Feasible; if (planar[i]) unstable[i] = true; }
        for (int i = 0; i < n && i < res.Toppling.Count; i++) { topple[i] = res.Toppling[i].Feasible; if (topple[i]) unstable[i] = true; }
        var wedgeText = new List<string>();
        foreach (var w in res.Wedge)
            if (w.Feasible)
            {
                wedgeText.Add($"S{w.SetA + 1}xS{w.SetB + 1}: {w.DipDeg:F0}/{w.DipDirDeg:F0}");
                if (w.SetA < n) unstable[w.SetA] = true;
                if (w.SetB < n) unstable[w.SetB] = true;
            }

        // ---- stereonet overlay ----
        BuildNet();
        BuildFrictionCircle(fric);
        _slopeGc = new Polyline(StereonetProjection.GreatCircleOnPlane(
            OrientationMath.NormalFromDipDipDir(sDip, sDd), _basePlane, _radius, false, 96));
        for (int i = 0; i < n; i++)
        {
            var pole = OrientationMath.NormalFromDipDipDir(dip[i], dipdir[i]);
            _poles.Add(StereonetProjection.ProjectOnPlane(pole, _basePlane, _radius, false));
            _poleCols.Add(unstable[i] ? RedFail : GreenOk);
            string mode = planar[i] ? "planar" : topple[i] ? "topple" : unstable[i] ? "wedge" : "ok";
            _poleLabels.Add($"S{i + 1} {mode}");
        }
        _readout = res.Report;
        _readoutAt = _basePlane.PointAt(_radius * 1.2, _radius);

        // ---- outputs ----
        da.SetDataList(0, planar.ToList());
        da.SetDataList(1, wedgeText);
        da.SetDataList(2, topple.ToList());
        da.SetData(3, res.FeasibleCount);
        da.SetDataList(4, _poles);
        var net = new List<Curve>();
        foreach (var pl in _net) net.Add(pl.ToPolylineCurve());
        if (_friction.Count > 1) net.Add(_friction.ToPolylineCurve());
        if (_slopeGc.Count > 1) net.Add(_slopeGc.ToPolylineCurve());
        da.SetDataList(5, net);
        da.SetData(6, res.Report);

        _clip = BoundingBox.Empty;
        foreach (var pl in _net) _clip.Union(pl.BoundingBox);
        _clip.Union(_readoutAt);

        if (res.FeasibleCount > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{res.FeasibleCount} feasible failure mode(s) -- see Report.");
    }

    private void BuildNet()
    {
        var circle = new Polyline();
        for (int i = 0; i <= 180; i++)
        {
            double a = 2.0 * Math.PI * i / 180.0;
            circle.Add(_basePlane.PointAt(_radius * Math.Sin(a), _radius * Math.Cos(a)));
        }
        _net.Add(circle);
        double tk = _radius * 0.04;
        _net.Add(new Polyline(new[] { _basePlane.PointAt(-tk, 0), _basePlane.PointAt(tk, 0) }));
        _net.Add(new Polyline(new[] { _basePlane.PointAt(0, -tk), _basePlane.PointAt(0, tk) }));
    }

    // Friction circle = locus of poles whose plane dips exactly at the friction
    // angle (angular distance = dip from the net centre). Poles inside it (dip <
    // friction) cannot slide.
    private void BuildFrictionCircle(double fricDeg)
    {
        _friction = new Polyline();
        for (int i = 0; i <= 72; i++)
        {
            double az = 360.0 * i / 72.0;
            var pole = OrientationMath.NormalFromDipDipDir(fricDeg, az);
            _friction.Add(StereonetProjection.ProjectOnPlane(pole, _basePlane, _radius, false));
        }
    }

    private void ClearPreview()
    {
        _net.Clear(); _friction = new Polyline(); _slopeGc = new Polyline();
        _poles.Clear(); _poleCols.Clear(); _poleLabels.Clear();
        _readout = ""; _clip = BoundingBox.Empty;
    }

    public override BoundingBox ClippingBox => _clip.IsValid ? _clip : base.ClippingBox;

    public override void DrawViewportWires(IGH_PreviewArgs args)
    {
        var d = args.Display;
        foreach (var pl in _net) d.DrawPolyline(pl, Frame, 1);
        if (_friction.Count > 1) d.DrawPolyline(_friction, FricCol, 2);
        if (_slopeGc.Count > 1) d.DrawPolyline(_slopeGc, SlopeCol, 2);
        for (int i = 0; i < _poles.Count; i++)
        {
            var c = _poleCols.ElementAtOrDefault(i);
            d.DrawPoint(_poles[i], PointStyle.RoundControlPoint, 6, c);
            if (i < _poleLabels.Count) d.Draw2dText(_poleLabels[i], c, _poles[i], false, 13);
        }
        d.Draw2dText("N", Frame, _basePlane.PointAt(0, _radius * 1.05), true, 16);
        d.Draw2dText("friction", FricCol, _basePlane.PointAt(0, -_radius * 0.0), true, 11);
        d.Draw2dText("slope", SlopeCol, _slopeGc.Count > 0 ? _slopeGc[0] : _basePlane.Origin, true, 11);
        if (!string.IsNullOrEmpty(_readout))
            d.Draw2dText(_readout, Color.FromArgb(40, 40, 40), _readoutAt, false, 12);
    }
}
