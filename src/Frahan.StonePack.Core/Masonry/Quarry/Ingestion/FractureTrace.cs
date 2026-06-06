#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Quarry.Ingestion;

// =============================================================================
// FractureTrace -- domain DTO for one 2D fracture trace polyline mapped on an
// outcrop. Lingua franca for the vector-format ingest layer; every reader
// (Shapefile / GeoJSON / WKT / GeoPackage) lands here.
//
// Coordinates are in the source file's CRS units (metres for the EUREF_FIN
// projected systems of the Loviisa Zenodo data, projected metres for most
// outcrop surveys). Frahan downstream code chooses whether to reproject.
//
// Attributes are kept as a flat string -> string map. The .dbf attribute
// schema differs per dataset; preserving raw strings means no per-dataset
// type drift here. Downstream consumers parse the fields they need.
// =============================================================================

public sealed class FractureTrace
{
    public FractureTrace(
        IReadOnlyList<TracePoint2D> vertices,
        IReadOnlyDictionary<string, string> attributes,
        string sourceFile)
    {
        if (vertices == null) throw new ArgumentNullException(nameof(vertices));
        if (vertices.Count < 2) throw new ArgumentException("FractureTrace requires >= 2 vertices.", nameof(vertices));
        Vertices = vertices;
        Attributes = attributes ?? new Dictionary<string, string>();
        SourceFile = sourceFile ?? string.Empty;
    }

    public IReadOnlyList<TracePoint2D> Vertices { get; }
    public IReadOnlyDictionary<string, string> Attributes { get; }
    public string SourceFile { get; }
    public int VertexCount => Vertices.Count;

    public double TotalLengthMetres()
    {
        double total = 0.0;
        for (int i = 1; i < Vertices.Count; i++)
        {
            var a = Vertices[i - 1];
            var b = Vertices[i];
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            total += Math.Sqrt(dx * dx + dy * dy);
        }
        return total;
    }
}

public readonly struct TracePoint2D
{
    public TracePoint2D(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; }
    public double Y { get; }
}

public sealed class FractureTraceCollection
{
    public FractureTraceCollection(
        IReadOnlyList<FractureTrace> traces,
        string crsWkt,
        string sourceFile)
    {
        Traces = traces ?? throw new ArgumentNullException(nameof(traces));
        CrsWkt = crsWkt ?? string.Empty;
        SourceFile = sourceFile ?? string.Empty;
    }

    public IReadOnlyList<FractureTrace> Traces { get; }
    public string CrsWkt { get; }
    public string SourceFile { get; }
    public int Count => Traces.Count;
}
