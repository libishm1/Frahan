#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Core.Registration;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Registration;

// =============================================================================
// GeoreferenceComponent — Phase I4 of the UX architecture report §7.7.F.
//
// Given world-frame control points expressed in a global coordinate system
// (WGS84 LLH degrees, UTM, or already-local ENU metres) and matching
// scan-frame points, derive the rigid scan→world transform AND report the
// origin used for ENU conversion (so downstream `.gh` graphs can re-create
// the same ENU frame).
//
// Coordinate System enum values:
//   0 = LLH-WGS84-degrees: control points carry (lon, lat, height) packed
//       into a Rhino Point3d as (X=lon°, Y=lat°, Z=height-m). The first
//       control point's LLH becomes the ENU origin.
//   1 = UTM: control points carry (easting, northing, elevation) in metres
//       (Z=elevation). Zone is inferred from the first control point via
//       LLH→UTM round-trip; alternatively the user wires the explicit
//       Zone + IsNorth inputs.
//   2 = Local-ENU: control points already in ENU metres. No conversion;
//       the transform is recovered directly via marker registration.
//
// The intermediate ENU frame is metric and rigid-body-friendly, so the
// downstream Horn / Kabsch math works without scale ambiguity.
// =============================================================================

[Algorithm("Absolute orientation + UTM/EPSG transform", "Horn, B.K.P. (1987). Closed-form solution of absolute orientation using unit quaternions. J. Opt. Soc. Am. A 4(4):629-642", WikiPath = "wiki/index/references.md")]
[DesignApplication(
    "Rigid scan→world transform from N≥3 control-point pairs in a  global coordinate system",
    DesignFlow.Bridges,
    Precedent = "Standard UTM / EPSG transforms + Horn 1987 best-fit absolute orientation")]
public sealed class GeoreferenceComponent : GH_Component
{
    public GeoreferenceComponent()
        : base("Georeference", "GeorefCRS",
            "Rigid scan→world transform from N≥3 control-point pairs in a " +
            "global coordinate system. Supports WGS84 LLH degrees, UTM, " +
            "and pre-converted ENU metres. World points are converted to " +
            "ENU about the first control point's origin before solving. " +
            "Implements absolute orientation (Horn 1987). " +
            "Sibling: GeorefCRS handles the WGS84/UTM/ENU datum; " +
            "'GeorefPts' (Georeference (Align by Points)) is the local fit " +
            "via Horn when both datasets share a frame.",
            "Frahan", "Mesh")
    {
    }

    public override Guid ComponentGuid => new Guid("B1C2D3A4-1112-4F5E-A6B7-C8D9E0F12345");
    protected override Bitmap Icon => IconProvider.Load("GeoreferenceMarker.png");
    public override GH_Exposure Exposure => GH_Exposure.quarternary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddPointParameter("World Control Points", "W",
            "World-frame control points in the chosen Coord System. " +
            "LLH-WGS84-degrees: pack as (X=lon°, Y=lat°, Z=height-m). " +
            "UTM: pack as (X=easting-m, Y=northing-m, Z=elevation-m). " +
            "Local-ENU: pack as (X=east-m, Y=north-m, Z=up-m).",
            GH_ParamAccess.list);
        pManager.AddPointParameter("Scan-Frame Points", "S",
            "Scan-frame points paired by INDEX with World Control Points.",
            GH_ParamAccess.list);
        pManager.AddIntegerParameter("Coord System", "C",
            "0 = LLH-WGS84-degrees, 1 = UTM, 2 = Local-ENU.",
            GH_ParamAccess.item, 0);
        pManager.AddIntegerParameter("UTM Zone", "Z",
            "Optional UTM zone override (1..60). Ignored unless Coord " +
            "System = 1. Default 0 means auto-pick from origin.",
            GH_ParamAccess.item, 0);
        pManager[3].Optional = true;
        pManager.AddBooleanParameter("UTM Northern Hemisphere", "NH",
            "True = northern hemisphere, false = southern. Ignored " +
            "unless Coord System = 1.",
            GH_ParamAccess.item, true);
        pManager[4].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTransformParameter("Transform", "X",
            "Rigid transform mapping scan-frame onto the ENU frame " +
            "centred at the first control point. Apply to your scan to " +
            "place it in world-relative coordinates.",
            GH_ParamAccess.item);
        pManager.AddNumberParameter("RMS Error", "RMS",
            "Root-mean-square per-pair residual after the transform " +
            "(ENU metres).",
            GH_ParamAccess.item);
        pManager.AddPointParameter("ENU Origin (LLH)", "O",
            "The LLH origin used for ENU conversion (X=lon°, Y=lat°, Z=h-m). " +
            "Wire this into a Panel to record the projection origin alongside " +
            "the .gh file.",
            GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R",
            "Human-readable summary of the solve.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Per-Pair Residuals", "Res",
            "Per-pair residual distances after applying Transform (m).",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var worldPts = new List<Point3d>();
        var scanPts = new List<Point3d>();
        int coordSystem = 0;
        int utmZoneOverride = 0;
        bool utmNorth = true;

        if (!da.GetDataList(0, worldPts) || worldPts.Count < 3)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Need at least 3 world control points.");
            return;
        }
        if (!da.GetDataList(1, scanPts) || scanPts.Count < 3)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Need at least 3 scan-frame points.");
            return;
        }
        if (worldPts.Count != scanPts.Count)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"World/scan point counts must match; got {worldPts.Count} vs {scanPts.Count}.");
            return;
        }
        da.GetData(2, ref coordSystem);
        da.GetData(3, ref utmZoneOverride);
        da.GetData(4, ref utmNorth);

        // Convert all world points to a metric ENU frame centred at the
        // first control point's LLH (or its UTM-equivalent LLH).
        Point3d enuOriginLlh;
        List<Point3d> worldEnu;
        try
        {
            (enuOriginLlh, worldEnu) = ToEnu(
                worldPts, coordSystem, utmZoneOverride, utmNorth);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        RegistrationResult result;
        try
        {
            result = RegistrationApi.SolveFromPoints(scanPts, worldEnu);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        da.SetData(0, result.Transform);
        da.SetData(1, result.RmsError);
        da.SetData(2, enuOriginLlh);
        da.SetData(3, BuildReport(coordSystem, enuOriginLlh, worldPts.Count, result.RmsError));
        da.SetDataList(4, result.PerPairResiduals);

        for (int i = 0; i < result.PerPairResiduals.Length; i++)
        {
            if (result.PerPairResiduals[i] > result.RmsError * 5.0 && result.RmsError > 1e-9)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Control point {i} residual {result.PerPairResiduals[i]:F4} m >> 5× RMS ({result.RmsError:F4} m); possible mis-tagged GPS.");
            }
        }
    }

    /// <summary>
    /// Convert a list of world control points (in LLH degrees, UTM, or ENU
    /// metres) into a metric ENU list centred on the first control point's
    /// equivalent LLH origin. Returns the chosen LLH origin and the ENU list.
    /// </summary>
    private static (Point3d originLlhDeg, List<Point3d> enuPoints) ToEnu(
        IReadOnlyList<Point3d> worldPts, int coordSystem,
        int utmZoneOverride, bool utmNorth)
    {
        var enu = new List<Point3d>(worldPts.Count);

        switch (coordSystem)
        {
            case 0: // LLH-WGS84-degrees: X=lon°, Y=lat°, Z=height-m
            {
                double lon0Deg = worldPts[0].X;
                double lat0Deg = worldPts[0].Y;
                double h0      = worldPts[0].Z;
                const double Deg2Rad = Math.PI / 180.0;
                double lat0 = lat0Deg * Deg2Rad;
                double lon0 = lon0Deg * Deg2Rad;
                for (int i = 0; i < worldPts.Count; i++)
                {
                    double lat = worldPts[i].Y * Deg2Rad;
                    double lon = worldPts[i].X * Deg2Rad;
                    double h   = worldPts[i].Z;
                    GeoreferenceMath.LlhToEcef(lat, lon, h, out double x, out double y, out double z);
                    GeoreferenceMath.EcefToEnu(x, y, z, lat0, lon0, h0,
                        out double e, out double n, out double u);
                    enu.Add(new Point3d(e, n, u));
                }
                return (new Point3d(lon0Deg, lat0Deg, h0), enu);
            }
            case 1: // UTM: X=easting-m, Y=northing-m, Z=elevation-m
            {
                double e0 = worldPts[0].X;
                double n0 = worldPts[0].Y;
                double h0 = worldPts[0].Z;
                int zone = utmZoneOverride;
                bool isNorth = utmNorth;
                if (zone < 1 || zone > 60)
                    throw new ArgumentException(
                        "UTM zone must be set explicitly via the Zone input (1..60).",
                        nameof(utmZoneOverride));

                // Derive the origin's LLH from the first control point's UTM.
                GeoreferenceMath.UtmToLlh(e0, n0, zone, isNorth, out double lat0, out double lon0);

                // Convert each UTM point → LLH → ECEF → ENU about origin.
                for (int i = 0; i < worldPts.Count; i++)
                {
                    GeoreferenceMath.UtmToLlh(worldPts[i].X, worldPts[i].Y, zone, isNorth,
                        out double lat, out double lon);
                    double h = worldPts[i].Z;
                    GeoreferenceMath.LlhToEcef(lat, lon, h, out double x, out double y, out double z);
                    GeoreferenceMath.EcefToEnu(x, y, z, lat0, lon0, h0,
                        out double ex, out double ny, out double uz);
                    enu.Add(new Point3d(ex, ny, uz));
                }
                const double Rad2Deg = 180.0 / Math.PI;
                return (new Point3d(lon0 * Rad2Deg, lat0 * Rad2Deg, h0), enu);
            }
            case 2: // Local-ENU: already metric, no conversion needed
            {
                foreach (var p in worldPts) enu.Add(p);
                return (Point3d.Unset, enu);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(coordSystem),
                    "Coord System must be 0 (LLH), 1 (UTM), or 2 (ENU).");
        }
    }

    private static string BuildReport(int coordSystem, Point3d originLlh, int count, double rms)
    {
        string sys = coordSystem switch
        {
            0 => "LLH-WGS84",
            1 => "UTM",
            2 => "Local-ENU (no conversion)",
            _ => "?"
        };
        string origin = originLlh.IsValid
            ? $"origin LLH = ({originLlh.Y:F8}°, {originLlh.X:F8}°, {originLlh.Z:F3} m)"
            : "origin = (n/a — pre-converted ENU)";
        return $"Coord System: {sys}; {origin}; {count} pairs; RMS = {rms:F4} m";
    }
}
