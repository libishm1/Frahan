#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Frahan.Masonry.Geometry;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// PhotogrammetryContract -- Phase 11.5; the photogrammetry-to-PLY bridge.
//
// Defines the minimum interface needed to plug any fracture detector
// (GeoFractNet, manual digitisation, OpenCV, classical edge detection) into
// the Frahan.BlockCutOpt pipeline. The contract is intentionally narrow:
//
//   IFractureDetector: given a calibrated image, return a list of
//                      (x1, y1, x2, y2) traces in WORLD coordinates (metres).
//
//   ImageToWorldMap:   converts pixel coordinates to world (X, Y) metres
//                      given a per-image GSD and origin.
//
//   CsvFractureTraceSource: reads detector output from disk (one row per
//                      trace, CSV columns x1, y1, x2, y2 in metres).
//
// With these in place, the consumer wires:
//     detector(image) -> world traces (metres) ->
//         TraceVerticalExtruder.Extrude(traces, zMin, zMax) ->
//         BlockCutOptSolver.Solve(area, ply, opts)
//
// All world units are metres. For Rhino models in non-metre units, the
// consumer multiplies by BlockCutOptTolerances.ToRhinoUnit.
// =============================================================================

/// <summary>
/// One fracture trace in world coordinates: a 2D line segment in the
/// horizontal X-Y plane, to be vertically extruded.
/// </summary>
public readonly struct FractureTrace
{
    public FractureTrace(double x1, double y1, double x2, double y2)
    {
        X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
    }
    public double X1 { get; }
    public double Y1 { get; }
    public double X2 { get; }
    public double Y2 { get; }
    public double Length =>
        Math.Sqrt((X2 - X1) * (X2 - X1) + (Y2 - Y1) * (Y2 - Y1));
}

/// <summary>
/// Maps pixel coordinates of a calibrated image to world (X, Y) metres.
/// Origin (originX, originY) is the world coordinate of pixel (0, 0).
/// gsdMetresPerPx is the ground sampling distance.
/// </summary>
public sealed class ImageToWorldMap
{
    public ImageToWorldMap(double originX, double originY, double gsdMetresPerPx, bool flipY = true)
    {
        if (!(gsdMetresPerPx > 0)) throw new ArgumentOutOfRangeException(nameof(gsdMetresPerPx));
        OriginX = originX;
        OriginY = originY;
        GsdMetresPerPx = gsdMetresPerPx;
        FlipY = flipY;
    }

    public double OriginX { get; }
    public double OriginY { get; }
    public double GsdMetresPerPx { get; }
    /// <summary>If true, Y pixel coordinate is flipped (image-Y points down, world-Y points up).</summary>
    public bool FlipY { get; }

    public (double X, double Y) PixelToWorld(double pxX, double pxY)
    {
        double sign = FlipY ? -1.0 : 1.0;
        return (OriginX + pxX * GsdMetresPerPx,
                OriginY + sign * pxY * GsdMetresPerPx);
    }
}

/// <summary>
/// Minimal contract for any fracture-detection backend.
/// </summary>
public interface IFractureDetector
{
    /// <summary>
    /// Given the path to a calibrated image and an image-to-world coordinate
    /// map, return a list of traces in WORLD metres.
    /// </summary>
    IReadOnlyList<FractureTrace> Detect(string imagePath, ImageToWorldMap map);

    /// <summary>Human-readable backend name.</summary>
    string BackendName { get; }
}

/// <summary>
/// Reads a CSV of traces already in WORLD metres. Useful when an external
/// detector (GeoFractNet via Python, manual digitisation in QGIS, etc.) has
/// already produced the trace file. CSV columns: x1, y1, x2, y2.
/// </summary>
public sealed class CsvFractureTraceSource : IFractureDetector
{
    public string BackendName => "csv";

    public IReadOnlyList<FractureTrace> Detect(string imagePath, ImageToWorldMap map)
    {
        return ReadCsv(imagePath);
    }

    public static IReadOnlyList<FractureTrace> ReadCsv(string csvPath)
    {
        if (string.IsNullOrWhiteSpace(csvPath)) throw new ArgumentException(nameof(csvPath));
        if (!File.Exists(csvPath)) throw new FileNotFoundException(csvPath);
        var list = new List<FractureTrace>();
        bool headerSeen = false;
        foreach (var raw in File.ReadLines(csvPath))
        {
            var line = raw == null ? "" : raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("#")) continue;
            var parts = line.Split(',');
            if (parts.Length < 4) continue;
            if (!headerSeen
                && (parts[0].IndexOf("x1", StringComparison.OrdinalIgnoreCase) >= 0
                    || parts[0].IndexOf("X", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                // try to parse; if it fails, treat as header
                if (!double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                {
                    headerSeen = true;
                    continue;
                }
            }
            if (double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double x1)
                && double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double y1)
                && double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double x2)
                && double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out double y2))
            {
                list.Add(new FractureTrace(x1, y1, x2, y2));
            }
        }
        return list;
    }
}

/// <summary>
/// Convenience helper that runs the full ingestion pipeline:
///   detector -> traces -> vertical extrusion -> PLY -> consumer.
/// </summary>
public static class PhotogrammetryPipeline
{
    /// <summary>
    /// Run a detector against an image and return a PLY mesh consumable by
    /// BlockCutOptSolver. The Z range matches the bench AABB.
    /// </summary>
    public static PlyMesh DetectAndExtrude(
        IFractureDetector detector,
        string imagePath,
        ImageToWorldMap map,
        double zMin,
        double zMax)
    {
        if (detector == null) throw new ArgumentNullException(nameof(detector));
        var traces = detector.Detect(imagePath, map);
        var asTuples = new List<(double X1, double Y1, double X2, double Y2)>(traces.Count);
        for (int i = 0; i < traces.Count; i++)
        {
            var t = traces[i];
            asTuples.Add((t.X1, t.Y1, t.X2, t.Y2));
        }
        return TraceVerticalExtruder.Extrude(asTuples, zMin, zMax);
    }
}
