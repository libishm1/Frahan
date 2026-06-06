#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Frahan.Masonry.Quarry.Ingestion;

// =============================================================================
// GprRadargramReader -- CSV ingestion for Frahan radargrams.
//
// Two CSV formats supported:
//
//   * traces.csv -- one row per trace, columns:
//        x_m, y_m, sample_spacing_m, a0, a1, a2, ...
//     where ai are sequential downward sample amplitudes. Header optional.
//
//   * picks.csv -- one row per interpreted reflector pick, columns:
//        x_m, y_m, depth_m, confidence_01, label
//     Label is optional; missing -> "".
//
// Comma-separated. Lines starting with '#' are treated as comments. Header
// is auto-detected (first non-blank, non-comment line whose first cell does
// not parse as a number).
// =============================================================================

public static class GprRadargramReader
{
    public static GprRadargram Load(string id, string tracesCsvPath, string picksCsvPath)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id required", nameof(id));
        if (string.IsNullOrWhiteSpace(tracesCsvPath)) throw new ArgumentException("tracesCsvPath required", nameof(tracesCsvPath));
        if (!File.Exists(tracesCsvPath))
            throw new FileNotFoundException("traces CSV not found", tracesCsvPath);

        var traces = ReadTraces(tracesCsvPath);
        var picks = string.IsNullOrWhiteSpace(picksCsvPath) || !File.Exists(picksCsvPath)
            ? (IReadOnlyList<GprReflectorPick>)new List<GprReflectorPick>()
            : ReadPicks(picksCsvPath);
        return new GprRadargram(id, traces, picks);
    }

    public static IReadOnlyList<GprTrace> ReadTraces(string path)
    {
        var output = new List<GprTrace>();
        using (var reader = new StreamReader(path))
        {
            string line;
            int lineNo = 0;
            while ((line = reader.ReadLine()) != null)
            {
                lineNo++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.TrimStart().StartsWith("#")) continue;

                var parts = line.Split(',');
                if (parts.Length < 4) continue;

                if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
                    continue; // header
                if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                    throw new FormatException($"line {lineNo}: cannot parse y_m");
                if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var dz)
                    || dz <= 0)
                    throw new FormatException($"line {lineNo}: cannot parse sample_spacing_m");

                var samples = new double[parts.Length - 3];
                for (int i = 0; i < samples.Length; i++)
                {
                    if (!double.TryParse(parts[3 + i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        throw new FormatException($"line {lineNo}: cannot parse amplitude column {3 + i}");
                    samples[i] = v;
                }
                output.Add(new GprTrace(x, y, samples, dz));
            }
        }
        return output;
    }

    public static IReadOnlyList<GprReflectorPick> ReadPicks(string path)
    {
        var output = new List<GprReflectorPick>();
        using (var reader = new StreamReader(path))
        {
            string line;
            int lineNo = 0;
            while ((line = reader.ReadLine()) != null)
            {
                lineNo++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.TrimStart().StartsWith("#")) continue;

                var parts = line.Split(',');
                if (parts.Length < 4) continue;
                if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
                    continue; // header
                if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                    throw new FormatException($"line {lineNo}: cannot parse y_m");
                if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    throw new FormatException($"line {lineNo}: cannot parse depth_m");
                if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var conf))
                    throw new FormatException($"line {lineNo}: cannot parse confidence_01");
                if (conf < 0) conf = 0;
                if (conf > 1) conf = 1;
                string label = parts.Length > 4 ? parts[4].Trim() : string.Empty;
                output.Add(new GprReflectorPick(x, y, d, conf, label));
            }
        }
        return output;
    }
}
