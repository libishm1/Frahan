#nullable disable
using System;
using Frahan.EdgeMatching;
using Rhino.Geometry;

namespace Frahan.Tests;

// Needs rhcommon_c.dll for Plane.FitPlaneToPoints + Curve.GetLength.
// SKIPs cleanly when Rhino is not installed.
static class EdgeMatchingPlanarityTesterTests
{
    public static void PerfectlyPlanarSquare_HasZeroRms()
    {
        var poly = new Polyline
        {
            new Point3d(0, 0, 0),
            new Point3d(10, 0, 0),
            new Point3d(10, 10, 0),
            new Point3d(0, 10, 0),
            new Point3d(0, 0, 0),
        };
        var (plane, rms) = PlanarityTester.BestFitPlane(poly.ToPolylineCurve());
        Assert(rms < 1e-9, $"expected zero RMS on planar square, got {rms}");
        Assert(plane.IsValid, "expected a valid best-fit plane");
    }

    public static void HelixCurve_HasNonZeroRms()
    {
        var poly = new Polyline();
        const int n = 64;
        for (int i = 0; i <= n; i++)
        {
            double t = 2 * Math.PI * i / n;
            poly.Add(new Point3d(Math.Cos(t) * 10, Math.Sin(t) * 10, t)); // climbing helix
        }
        var (_, rms) = PlanarityTester.BestFitPlane(poly.ToPolylineCurve());
        Assert(rms > 0.5, $"expected helix RMS > 0.5 mm, got {rms}");
    }

    public static void PlanarContour_ClassifiesAsPlanar2D()
    {
        var poly = new Polyline
        {
            new Point3d(0, 0, 0),
            new Point3d(10, 0, 0),
            new Point3d(10, 10, 0),
            new Point3d(0, 10, 0),
            new Point3d(0, 0, 0),
        };
        var panel = new Panel("test", poly.ToPolylineCurve(), PanelKind.Shard);
        Assert(panel.Mode == PanelMode.Planar2D,
            $"expected Planar2D, got {panel.Mode} (rms={panel.PlanarityRms})");
        Assert(panel.PlanarityRms < 1e-9,
            $"expected near-zero planarity RMS, got {panel.PlanarityRms}");
    }

    public static void WarpedContour_ClassifiesAsSpatial3D()
    {
        var poly = new Polyline();
        const int n = 32;
        for (int i = 0; i <= n; i++)
        {
            double t = 2 * Math.PI * i / n;
            poly.Add(new Point3d(Math.Cos(t) * 10, Math.Sin(t) * 10, Math.Sin(2 * t) * 5));
        }
        var panel = new Panel("warped", poly.ToPolylineCurve(), PanelKind.Shard);
        Assert(panel.Mode == PanelMode.Spatial3D,
            $"expected Spatial3D for warped contour, got {panel.Mode} (rms={panel.PlanarityRms})");
    }

    public static void NonFrame_OpenContour_Throws()
    {
        var poly = new Polyline
        {
            new Point3d(0, 0, 0),
            new Point3d(10, 0, 0),
            new Point3d(10, 10, 0),
        };
        bool threw = false;
        try { _ = new Panel("open", poly.ToPolylineCurve(), PanelKind.Shard); }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "expected ArgumentException for non-frame open contour");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
