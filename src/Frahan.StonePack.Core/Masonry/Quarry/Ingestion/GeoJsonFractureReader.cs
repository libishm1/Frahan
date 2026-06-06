#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace Frahan.Masonry.Quarry.Ingestion;

// =============================================================================
// GeoJsonFractureReader -- RFC 7946 GeoJSON to FractureTraceCollection via
// NetTopologySuite.IO.GeoJSON.
//
// LineString and MultiLineString features become FractureTrace entries.
// Points and Polygons silently skipped. CRS is not part of RFC 7946 — the
// caller assumes the file is already in the desired CRS, or wires that
// metadata externally.
// =============================================================================

public static class GeoJsonFractureReader
{
    public static FractureTraceCollection Load(string geojsonPath)
    {
        if (string.IsNullOrWhiteSpace(geojsonPath)) throw new ArgumentException("geojsonPath required", nameof(geojsonPath));
        if (!File.Exists(geojsonPath)) throw new FileNotFoundException("GeoJSON not found", geojsonPath);
        var text = File.ReadAllText(geojsonPath);
        return LoadFromText(text, geojsonPath);
    }

    public static FractureTraceCollection LoadFromText(string geojsonText, string sourceFileLabel)
    {
        if (string.IsNullOrEmpty(geojsonText)) throw new ArgumentException("geojsonText required", nameof(geojsonText));
        var reader = new GeoJsonReader();
        var coll = reader.Read<FeatureCollection>(geojsonText);
        var traces = new List<FractureTrace>();
        var label = sourceFileLabel ?? string.Empty;

        foreach (var feature in coll)
        {
            NetTopologySuite.Geometries.Geometry geom = feature.Geometry;
            if (geom == null) continue;
            var attrs = ToStringMap(feature.Attributes);
            CollectTracesFromGeometry(geom, attrs, label, traces);
        }

        return new FractureTraceCollection(traces, string.Empty, label);
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
        }
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

    private static IReadOnlyDictionary<string, string> ToStringMap(IAttributesTable attrs)
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
}
