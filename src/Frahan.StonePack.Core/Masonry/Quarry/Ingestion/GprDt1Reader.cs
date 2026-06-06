#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Frahan.Masonry.Quarry.Ingestion;

// =============================================================================
// GprDt1Reader -- Sensors & Software pulseEKKO .dt1 + .hd ingest.
//
// pulseEKKO machines write paired files:
//   * <name>.HD   ASCII key:value header (sampling info + geometry)
//   * <name>.DT1  binary trace data; each trace = 25 floats (100-byte) header
//                 + N 16-bit signed integers (little-endian sample amplitudes)
//
// Format reference: RGPR R package import tutorial + USGS OFR 02-166 1999
// public-domain spec (Lucius & Powers). Verified against pulseEKKO PRO export.
//
// Per-trace 25-float header layout (floats are 32-bit little-endian):
//   [0]  trace number
//   [1]  position (metres along survey line)
//   [2]  number of points (samples) in this trace
//   [3]  topographic data
//   [4]  bytes per point (usually 2 = int16)
//   [5]  trace window position (ns)
//   [6]  number of stacks
//   [7]  GPS X
//   [8]  GPS Y
//   [9]  GPS Z
//   ... (the rest are reserved / equipment-specific; we read but discard)
//
// Sample spacing: pulseEKKO HD header gives TOTAL TIME WINDOW (ns) plus
// NUMBER OF PTS/TRACE; the sample spacing in time = TOTAL/N. Convert to
// metres via 0.15 m/ns (free-space two-way; user rescales for the medium).
// =============================================================================

public static class GprDt1Reader
{
    public static GprRadargram Load(string dt1Path, string id = null)
    {
        if (string.IsNullOrWhiteSpace(dt1Path)) throw new ArgumentException("dt1Path required", nameof(dt1Path));
        if (!File.Exists(dt1Path)) throw new FileNotFoundException("DT1 file not found", dt1Path);

        string hdPath = Path.ChangeExtension(dt1Path, ".HD");
        if (!File.Exists(hdPath))
        {
            var hdLower = Path.ChangeExtension(dt1Path, ".hd");
            if (!File.Exists(hdLower))
                throw new FileNotFoundException("Companion .HD header not found alongside .DT1", hdPath);
            hdPath = hdLower;
        }

        var meta = ParseHdHeader(hdPath);
        double dzMetres = meta.SampleSpacingMetres > 0 ? meta.SampleSpacingMetres : 1.0;

        var traces = new List<GprTrace>(meta.TraceCount > 0 ? meta.TraceCount : 64);
        using (var stream = File.OpenRead(dt1Path))
        using (var reader = new BinaryReader(stream))
        {
            while (stream.Position + 100 <= stream.Length)
            {
                // 25-float per-trace header
                var hdr = new float[25];
                for (int i = 0; i < 25; i++) hdr[i] = reader.ReadSingle();
                int nPoints = (int)hdr[2];
                int bytesPerPoint = (int)hdr[4];
                if (nPoints <= 0)
                {
                    if (meta.SamplesPerTrace <= 0)
                        throw new InvalidDataException("DT1 trace declared 0 samples and HD has no fallback NUMBER OF PTS/TRACE.");
                    nPoints = meta.SamplesPerTrace;
                }
                if (bytesPerPoint <= 0) bytesPerPoint = 2;

                long payload = (long)nPoints * bytesPerPoint;
                if (stream.Position + payload > stream.Length) break; // truncated

                var samples = new double[nPoints];
                if (bytesPerPoint == 2)
                {
                    for (int i = 0; i < nPoints; i++) samples[i] = reader.ReadInt16();
                }
                else if (bytesPerPoint == 4)
                {
                    for (int i = 0; i < nPoints; i++) samples[i] = reader.ReadInt32();
                }
                else
                {
                    throw new NotSupportedException($"DT1 bytes-per-point = {bytesPerPoint} not supported (2 or 4 only).");
                }

                double x = hdr[1];
                double y = 0.0;
                traces.Add(new GprTrace(x, y, samples, dzMetres));
            }
        }

        return new GprRadargram(
            id ?? Path.GetFileNameWithoutExtension(dt1Path),
            traces,
            new List<GprReflectorPick>());
    }

    private struct HdMeta
    {
        public int TraceCount;
        public int SamplesPerTrace;
        public double TotalTimeWindowNs;
        public double SampleSpacingMetres;
    }

    private static HdMeta ParseHdHeader(string hdPath)
    {
        var meta = default(HdMeta);
        foreach (var rawLine in File.ReadAllLines(hdPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            // pulseEKKO HD uses "KEY = VALUE" or "KEY: VALUE" forms; tolerate both
            int sep = line.IndexOf('=');
            if (sep < 0) sep = line.IndexOf(':');
            if (sep < 1) continue;
            var key = line.Substring(0, sep).Trim().ToUpperInvariant();
            var value = line.Substring(sep + 1).Trim();
            // strip trailing units / comments
            int spaceIdx = value.IndexOf(' ');
            var valueNumeric = spaceIdx > 0 ? value.Substring(0, spaceIdx) : value;
            if (key.Contains("NUMBER OF TRACES"))
            {
                if (int.TryParse(valueNumeric, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                    meta.TraceCount = v;
            }
            else if (key.Contains("NUMBER OF PTS"))
            {
                if (int.TryParse(valueNumeric, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                    meta.SamplesPerTrace = v;
            }
            else if (key.Contains("TOTAL TIME WINDOW"))
            {
                if (double.TryParse(valueNumeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    meta.TotalTimeWindowNs = v;
            }
        }
        if (meta.SamplesPerTrace > 0 && meta.TotalTimeWindowNs > 0)
        {
            double dtNs = meta.TotalTimeWindowNs / meta.SamplesPerTrace;
            meta.SampleSpacingMetres = dtNs * 0.15;
        }
        return meta;
    }
}
