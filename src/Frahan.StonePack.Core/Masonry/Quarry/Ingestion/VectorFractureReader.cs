#nullable disable
using System;
using System.IO;

namespace Frahan.Masonry.Quarry.Ingestion;

// =============================================================================
// VectorFractureReader -- dispatcher that picks the right ingest path based
// on file extension. Single entry point for the canvas-side "load fractures"
// component; callers don't have to know whether the user dropped a .shp or
// a .geojson on the input.
// =============================================================================

public static class VectorFractureReader
{
    public static FractureTraceCollection Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path required", nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("Vector file not found", path);

        var ext = Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".shp":
                return ShapefileFractureReader.Load(path);
            case ".geojson":
            case ".json":
                return GeoJsonFractureReader.Load(path);
            default:
                throw new NotSupportedException(
                    $"Vector format '{ext}' not supported. Use .shp or .geojson.");
        }
    }
}
