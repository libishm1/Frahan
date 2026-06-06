#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Esri;

namespace Frahan.Masonry.Quarry.Ingestion;

// =============================================================================
// ShapefileFractureReader -- ESRI Shapefile to FractureTraceCollection via
// NetTopologySuite.IO.Esri.Shapefile.
//
// Polyline shapefiles only (shape type 3 / PolyLineM / PolyLineZ). Polygons,
// points, and multipatch are out of scope; this reader is for fracture-trace
// data specifically (the Loviisa Zenodo and similar UAV-photogrammetry
// products).
//
// Companion .dbf is loaded automatically by the library when present.
// Companion .prj is read if present and stored verbatim in CrsWkt; the
// caller decides whether to reproject.
// =============================================================================

public static class ShapefileFractureReader
{
    public static FractureTraceCollection Load(string shpPath)
    {
        if (string.IsNullOrWhiteSpace(shpPath)) throw new ArgumentException("shpPath required", nameof(shpPath));
        if (!File.Exists(shpPath)) throw new FileNotFoundException("Shapefile not found", shpPath);

        string crsWkt = LoadCompanionPrj(shpPath);
        var traces = new List<FractureTrace>();

        foreach (var feature in Shapefile.ReadAllFeatures(shpPath))
        {
            NetTopologySuite.Geometries.Geometry geom = feature.Geometry;
            if (geom == null) continue;
            var attrs = ToStringMap(feature.Attributes);
            CollectTracesFromGeometry(geom, attrs, shpPath, traces);
        }

        return new FractureTraceCollection(traces, crsWkt, shpPath);
    }

    private static void CollectTracesFromGeometry(
        NetTopologySuite.Geometries.Geometry geom,
        IReadOnlyDictionary<string, string> attrs,
        string sourceFile,
        List<FractureTrace> sink)
    {
        if (geom is LineString line)
        {
            var verts = ToTracePoints(line.Coordinates);
            if (verts.Count >= 2)
                sink.Add(new FractureTrace(verts, attrs, sourceFile));
            return;
        }
        if (geom is MultiLineString multi)
        {
            for (int i = 0; i < multi.NumGeometries; i++)
            {
                CollectTracesFromGeometry(multi.GetGeometryN(i), attrs, sourceFile, sink);
            }
            return;
        }
        // Points, polygons, etc are silently skipped — not a fracture trace.
    }

    private static List<TracePoint2D> ToTracePoints(Coordinate[] coords)
    {
        var output = new List<TracePoint2D>(coords.Length);
        for (int i = 0; i < coords.Length; i++)
        {
            var c = coords[i];
            output.Add(new TracePoint2D(c.X, c.Y));
        }
        return output;
    }

    private static IReadOnlyDictionary<string, string> ToStringMap(
        NetTopologySuite.Features.IAttributesTable attrs)
    {
        var output = new Dictionary<string, string>();
        if (attrs == null) return output;
        foreach (var name in attrs.GetNames())
        {
            var value = attrs[name];
            output[name] = value == null ? string.Empty : value.ToString();
        }
        return output;
    }

    private static string LoadCompanionPrj(string shpPath)
    {
        var prj = Path.ChangeExtension(shpPath, ".prj");
        if (!File.Exists(prj)) return string.Empty;
        try
        {
            return File.ReadAllText(prj).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }
}
