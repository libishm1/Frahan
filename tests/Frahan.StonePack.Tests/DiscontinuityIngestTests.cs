#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using Frahan.Core.Discontinuity;
using Frahan.Core.Discontinuity.Ingest;
using Frahan.GH.Quarry;
using Rhino.Geometry;

namespace Frahan.Tests;

// =============================================================================
// DiscontinuityIngestTests -- Feature A (trace/plane ingest) + Feature B
// (block-size + stereonet math) for the quarry discontinuity workflow.
// Mirrors the static-method + Assert() harness used by JointSetDfnTests; these
// are registered in Program.cs. The numerics here were cross-checked against an
// independent Python reference (orientation round-trip, Vb = 3.0, Jv = 2.167).
// =============================================================================

static class DiscontinuityIngestTests
{
    // rhcommon_c.dll (RhinoCommon's native geometry backing) only initialises
    // inside a live Rhino.exe; in the headless test runner Vector3d.Unitize throws
    // DllNotFoundException. The orientation/block-size/stereonet tests below let
    // that exception propagate, so Program.cs SKIPs them. The CSV/DXF/GeoJSON
    // reader tests, however, drive the readers, which (per the "log, skip, continue"
    // policy) swallow the native error as a "bad row" -> 0 rows -> a misleading
    // FAIL. Probe the native up-front so those reader tests SKIP cleanly headless;
    // they are covered live in Rhino (truth criterion (c)) instead.
    private static void RequireRhinoNative()
    {
        try { var v = new Vector3d(0, 0, 1); v.Unitize(); }
        catch (DllNotFoundException)
        { throw new SkipTest("requires live Rhino (rhcommon_c native unavailable headless)"); }
        catch (TypeInitializationException)
        { throw new SkipTest("requires live Rhino (RhinoCommon native unavailable headless)"); }
    }

    // ---- orientation inverse ----------------------------------------------

    public static void Orientation_RoundTrip()
    {
        var cases = new (double dip, double dd)[]
        { (35, 120), (10, 5), (80, 300), (45, 200), (60, 45), (15, 359) };
        foreach (var (dip, dd) in cases)
        {
            var n = OrientationMath.NormalFromDipDipDir(dip, dd);
            var (rdip, rdd) = OrientationMath.DipDipDir(n);
            Assert(Math.Abs(rdip - dip) < 1e-6, $"dip round-trip {dip} -> {rdip}");
            // axial equivalence (n and -n are the same plane)
            double ddErr = Math.Abs(((rdd - dd + 180) % 360) - 180);
            Assert(ddErr < 1e-6, $"dipdir round-trip {dd} -> {rdd}");
        }
    }

    // ---- CSV ---------------------------------------------------------------

    public static void Csv_DipDipDir_Header_RoundTrip()
    {
        RequireRhinoNative();
        string p = Temp(".csv", "dip,dipdir,x,y,z\n35,120,10,20,30\n62,8,0,0,0\n");
        var col = DiscontinuityReader.Load(p);
        Assert(col.Items.Count == 2, $"expected 2 rows, got {col.Items.Count}");
        Assert(Math.Abs(col.Items[0].DipDeg - 35) < 1e-6, $"dip {col.Items[0].DipDeg}");
        Assert(Math.Abs(col.Items[0].DipDirDeg - 120) < 1e-6, $"dipdir {col.Items[0].DipDirDeg}");
        Assert(col.Items[0].Centroid.DistanceTo(new Point3d(10, 20, 30)) < 1e-9, "centroid");
        File.Delete(p);
    }

    public static void Csv_PlaneCoeff()
    {
        RequireRhinoNative();
        // plane 0*x+0*y+1*z = 5  -> horizontal plane at z=5, pole +/-Z, centroid (0,0,5)
        string p = Temp(".csv", "a,b,c,d\n0,0,1,5\n");
        var col = DiscontinuityReader.Load(p);
        Assert(col.Items.Count == 1, "one plane");
        Assert(Math.Abs(col.Items[0].DipDeg) < 1e-6, $"horizontal dip, got {col.Items[0].DipDeg}");
        Assert(col.Items[0].Centroid.DistanceTo(new Point3d(0, 0, 5)) < 1e-9, "coeff centroid");
        File.Delete(p);
    }

    public static void Csv_Headerless_Normals()
    {
        RequireRhinoNative();
        string p = Temp(".csv", "1,0,0\n0,1,0\n");
        var col = DiscontinuityReader.Load(p);
        Assert(col.Items.Count == 2, $"two normals, got {col.Items.Count}");
        foreach (var d in col.Items) Assert(d.HasOrientation, "oriented");
        File.Delete(p);
    }

    public static void Csv_BadRow_SkippedNotThrown()
    {
        RequireRhinoNative();
        string p = Temp(".csv", "dip,dipdir\n30,40\nhello,world\n55,200\n");
        var col = DiscontinuityReader.Load(p);
        Assert(col.Items.Count == 2, $"2 good rows kept, got {col.Items.Count}");
        Assert(col.Warnings.Count >= 1, "a warning for the bad row");
        File.Delete(p);
    }

    // ---- DXF ---------------------------------------------------------------

    public static void Dxf_LwPolyline_Trace()
    {
        RequireRhinoNative();
        string dxf =
            "0\nSECTION\n2\nENTITIES\n" +
            "0\nLWPOLYLINE\n90\n3\n70\n0\n10\n0.0\n20\n0.0\n10\n1.0\n20\n0.0\n10\n1.0\n20\n1.0\n" +
            "0\nENDSEC\n0\nEOF\n";
        string p = Temp(".dxf", dxf);
        var col = DiscontinuityReader.Load(p);
        Assert(col.Items.Count == 1, $"one polyline, got {col.Items.Count}");
        Assert(col.Items[0].Trace.Count == 3, $"3 trace pts, got {col.Items[0].Trace.Count}");
        Assert(col.Items[0].HasOrientation, "coplanar trace fits a plane");
        File.Delete(p);
    }

    public static void Dxf_3dFace_Plane()
    {
        RequireRhinoNative();
        // a 3DFACE in the z=0 plane -> pole +/-Z
        string dxf =
            "0\nSECTION\n2\nENTITIES\n" +
            "0\n3DFACE\n10\n0.0\n20\n0.0\n30\n0.0\n11\n1.0\n21\n0.0\n31\n0.0\n12\n0.0\n22\n1.0\n32\n0.0\n13\n0.0\n23\n1.0\n33\n0.0\n" +
            "0\nENDSEC\n0\nEOF\n";
        string p = Temp(".dxf", dxf);
        var col = DiscontinuityReader.Load(p);
        Assert(col.Items.Count == 1, $"one face, got {col.Items.Count}");
        Assert(Math.Abs(col.Items[0].DipDeg) < 1e-6, $"face is horizontal, dip {col.Items[0].DipDeg}");
        File.Delete(p);
    }

    // ---- GeoJSON -----------------------------------------------------------

    public static void GeoJson_PointAndLine()
    {
        RequireRhinoNative();
        string gj =
            "{\"type\":\"FeatureCollection\",\"features\":[" +
            "{\"type\":\"Feature\",\"properties\":{\"dip\":40,\"dipdir\":135,\"set\":2}," +
            "\"geometry\":{\"type\":\"Point\",\"coordinates\":[10,20,5]}}," +
            "{\"type\":\"Feature\",\"properties\":{},\"geometry\":" +
            "{\"type\":\"LineString\",\"coordinates\":[[0,0,0],[1,0,0],[1,1,0]]}}]}";
        string p = Temp(".geojson", gj);
        var col = DiscontinuityReader.Load(p);
        Assert(col.Items.Count == 2, $"point + line = 2, got {col.Items.Count}");
        // find the point measurement
        Discontinuity pt = null, tr = null;
        foreach (var d in col.Items)
        { if (d.Kind == DiscontinuityKind.PointMeasurement) pt = d; if (d.Kind == DiscontinuityKind.Trace) tr = d; }
        Assert(pt != null && Math.Abs(pt.DipDeg - 40) < 1e-6, "point dip 40");
        Assert(pt != null && pt.SetId == 2, "point set id 2");
        Assert(tr != null && tr.Trace.Count == 3, "line has 3 vertices");
        File.Delete(p);
    }

    // ---- block size --------------------------------------------------------

    public static void BlockSize_ThreeOrthogonal()
    {
        var poles = new List<Vector3d> { Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis };
        var s = new List<double> { 1.0, 1.5, 2.0 };
        var bs = BlockSizeMath.Compute(poles, s, 1.0);
        Assert(bs.VolumeDefined, "volume defined for 3 orthogonal sets");
        Assert(Math.Abs(bs.Vb - 3.0) < 1e-9, $"Vb expected 3.0, got {bs.Vb}");
        Assert(Math.Abs(bs.Jv - (1 + 1 / 1.5 + 0.5)) < 1e-9, $"Jv expected 2.1667, got {bs.Jv}");
        Assert(Math.Abs(bs.Deq - Math.Pow(3.0, 1.0 / 3.0)) < 1e-9, "Deq");
    }

    public static void BlockSize_TwoSets_NoVolume()
    {
        var poles = new List<Vector3d> { Vector3d.XAxis, Vector3d.YAxis };
        var s = new List<double> { 1.0, 1.0 };
        var bs = BlockSizeMath.Compute(poles, s, 1.0);
        Assert(!bs.VolumeDefined, "2 sets -> no block volume");
        Assert(double.IsNaN(bs.Vb), "Vb is NaN");
        Assert(bs.SetsUsed == 2, "2 sets used");
    }

    public static void BlockSize_NearParallel_IllConditioned()
    {
        // three nearly-parallel sets -> volume undefined
        var poles = new List<Vector3d>
        { new Vector3d(0,0,1), new Vector3d(0,0.02,1), new Vector3d(0.02,0,1) };
        var s = new List<double> { 1.0, 1.0, 1.0 };
        var bs = BlockSizeMath.Compute(poles, s, 1.0);
        Assert(!bs.VolumeDefined, "near-parallel -> undefined");
    }

    // ---- stereonet ---------------------------------------------------------

    public static void Stereonet_ProjectionRange()
    {
        for (int dip = 0; dip <= 90; dip += 15)
        {
            var n = OrientationMath.NormalFromDipDipDir(dip, 0);
            var uv = StereonetProjection.Project(n, 10.0, false);
            double r = Math.Sqrt(uv.X * uv.X + uv.Y * uv.Y);
            Assert(r <= 10.0 + 1e-6, $"dip {dip} radius {r} within net");
        }
        // horizontal joint (dip 0) -> centre
        var c = StereonetProjection.Project(OrientationMath.NormalFromDipDipDir(0, 0), 10.0, false);
        Assert(Math.Sqrt(c.X * c.X + c.Y * c.Y) < 1e-6, "dip 0 at centre");
    }

    // ---- GH component metadata --------------------------------------------

    public static void Gh_DiscontinuityIngest_Metadata()
    {
        var c = new DiscontinuityIngestComponent();
        Assert(c.ComponentGuid == new Guid("D5F10049-ED9E-4ED9-A049-ED9EED9E0049"), $"GUID {c.ComponentGuid}");
        Assert(c.Category == "Frahan" && c.SubCategory == "Quarry", "tab");
        Assert(c.Params.Input.Count == 2, $"inputs {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 6, $"outputs {c.Params.Output.Count}");
    }

    public static void Gh_StereonetBlockSize_Metadata()
    {
        var c = new StereonetBlockSizeComponent();
        Assert(c.ComponentGuid == new Guid("D5F1004A-ED9E-4ED9-A04A-ED9EED9E004A"), $"GUID {c.ComponentGuid}");
        Assert(c.Category == "Frahan" && c.SubCategory == "Quarry", "tab");
        Assert(c.Params.Input.Count == 9, $"inputs {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 9, $"outputs {c.Params.Output.Count}");
    }

    // ---- helpers -----------------------------------------------------------

    private static string Temp(string ext, string content)
    {
        string p = Path.Combine(Path.GetTempPath(), "frahan_disc_test_" + Guid.NewGuid().ToString("N") + ext);
        File.WriteAllText(p, content);
        return p;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
