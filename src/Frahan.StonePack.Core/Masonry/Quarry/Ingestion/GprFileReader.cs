#nullable disable
using System;
using System.IO;

namespace Frahan.Masonry.Quarry.Ingestion;

// =============================================================================
// GprFileReader -- dispatch ground-penetrating-radar files to the right reader
// by extension. Single canvas-side entry point.
//
// Supported extensions:
//   .csv          -> GprRadargramReader.Load(id, csv, null)
//   .sgy / .segy  -> GprSegYReader.Load
//   .rd3          -> GprMalaRd3Reader.Load
//   .dt1          -> GprDt1Reader.Load   (Sensors & Software pulseEKKO)
//   .dt           -> GprIdsDtReader.Load (IDS GeoRadar GRED HD + companion .hdr_dt)
//   .dzt          -> GprDztReader.Load
//
// Two-CSV form (traces.csv + picks.csv) is not handled by this dispatcher;
// for that, call GprRadargramReader.Load directly.
// =============================================================================

public static class GprFileReader
{
    public static GprRadargram Load(string path, string id = null)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path required", nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("GPR file not found", path);

        var ext = Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".csv":
                return GprRadargramReader.Load(id ?? Path.GetFileNameWithoutExtension(path), path, null);
            case ".sgy":
            case ".segy":
                return GprSegYReader.Load(path, id);
            case ".rd3":
                return GprMalaRd3Reader.Load(path, id);
            case ".dt1":
                return GprDt1Reader.Load(path, id);
            case ".dt":
                return GprIdsDtReader.Load(path, id);
            case ".dzt":
                return GprDztReader.Load(path, id);
            case ".gsf":
                // Geoscanners GSF: proprietary, no open binary spec (confirmed). Bridge, not guess.
                throw new NotSupportedException(
                    "GSF (Geoscanners AKULA) is a proprietary format with no open binary spec, " +
                    "so it is not read natively. Convert it to SEG-Y with GPRSoft or RGPR, then load " +
                    "the resulting .sgy with this reader (GprSegYReader).");
            default:
                throw new NotSupportedException(
                    $"GPR format '{ext}' not supported. Use .csv / .sgy / .segy / .rd3 / .dt1 / .dt / .dzt. " +
                    "Proprietary formats (.gsf): convert to SEG-Y first.");
        }
    }
}
