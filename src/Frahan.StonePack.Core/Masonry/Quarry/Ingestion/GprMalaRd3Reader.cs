#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Frahan.Masonry.Quarry.Ingestion;

// =============================================================================
// GprMalaRd3Reader -- MALA Geoscience .rd3 + .rad ingest.
//
// MALA RAMAC machines (ProEx, MIRA, GroundExplorer) write a paired set:
//   * <name>.rad  ASCII key:value header   (sampling info, geometry, metadata)
//   * <name>.rd3  raw binary trace data    (16-bit signed LE; rows = traces,
//                                          cols = samples per trace)
//   * <name>.cor  optional ASCII GPS marker positions (one row per marker)
//   * <name>.mrk  optional ASCII fiducial marker rows (one row per marker)
//
// Format reference: RGPR R-package source (Emanuel Huber, ETH Zurich) and the
// official MALA "Detailed description of RD3, RD7 and RAD formats" appendix
// (Guideline Geo). Cross-checked against readgssi Python lib for sample-rate
// conventions.
//
// Trace layout in .rd3:
//   raw[traceIndex * samplesPerTrace + sampleIndex] (samplesPerTrace from .rad
//   SAMPLES key). Each int16 sample is little-endian.
//
// Sample-spacing conversion:
//   dz_metres_per_sample = c0 / (2 * f_MHz)
//   where c0 = 0.3 m/ns (vacuum) scaled by typical granite e_r ~5.6 -> v ~0.13 m/ns
//   To keep generality the reader assumes c0/2 = 0.15 m/ns (free space, two-way).
//   Callers needing the dielectric-corrected depth must rescale GprTrace.SampleSpacingMetres.
//
// Trace positioning: traces are laid along the +X axis at .rad DISTANCE INTERVAL
// metres apart, starting at (0, 0). Marker / GPS positions from .cor are NOT
// applied to GprTrace.X/Y yet (TODO when needed).
// =============================================================================

public static class GprMalaRd3Reader
{
    public static GprRadargram Load(string rd3Path, string id = null)
    {
        if (string.IsNullOrWhiteSpace(rd3Path)) throw new ArgumentException("rd3Path required", nameof(rd3Path));
        if (!File.Exists(rd3Path)) throw new FileNotFoundException("RD3 file not found", rd3Path);

        var radPath = Path.ChangeExtension(rd3Path, ".rad");
        if (!File.Exists(radPath))
        {
            var radPathUpper = Path.ChangeExtension(rd3Path, ".RAD");
            if (!File.Exists(radPathUpper))
                throw new FileNotFoundException("Companion .rad header not found alongside .rd3", radPath);
            radPath = radPathUpper;
        }

        var meta = ParseRadHeader(radPath);
        int samplesPerTrace = meta.SamplesPerTrace;
        int traceCount = meta.TraceCount;
        if (samplesPerTrace <= 0) throw new InvalidDataException(".rad header: SAMPLES <= 0");
        if (traceCount <= 0) throw new InvalidDataException(".rad header: LAST TRACE <= 0");

        double dzMetres = SampleSpacingMetres(meta.SamplingFrequencyMhz);
        double dx = meta.DistanceIntervalMetres > 0 ? meta.DistanceIntervalMetres : 1.0;
        // TRUE two-way sample interval (ns), velocity-independent: 1000 / sampling-freq-MHz.
        // (dzMetres above bakes in the vacuum velocity 0.15 m/ns; recovering dt from dz with a
        // STONE velocity would be wrong, so carry dt explicitly -- see GprTrace.SampleIntervalNs.)
        double dtNs = meta.SamplingFrequencyMhz > 0 ? 1000.0 / meta.SamplingFrequencyMhz : 0.0;

        var traces = new List<GprTrace>(traceCount);
        using (var stream = File.OpenRead(rd3Path))
        using (var reader = new BinaryReader(stream))
        {
            long expected = (long)samplesPerTrace * traceCount * 2L;
            if (stream.Length < expected)
            {
                throw new InvalidDataException(
                    $".rd3 size {stream.Length} smaller than expected {expected} (samples={samplesPerTrace} traces={traceCount}).");
            }
            for (int t = 0; t < traceCount; t++)
            {
                var samples = new double[samplesPerTrace];
                for (int i = 0; i < samplesPerTrace; i++)
                {
                    samples[i] = reader.ReadInt16(); // LE on Windows; matches MALA convention
                }
                double x = t * dx;
                double y = 0.0;
                traces.Add(new GprTrace(x, y, samples, dzMetres, dtNs));
            }
        }

        return new GprRadargram(
            id ?? Path.GetFileNameWithoutExtension(rd3Path),
            traces,
            new List<GprReflectorPick>());
    }

    private static double SampleSpacingMetres(double samplingFrequencyMhz)
    {
        if (samplingFrequencyMhz <= 0) return 1.0;
        double dtNs = 1000.0 / samplingFrequencyMhz;
        return dtNs * 0.15; // 0.3 m/ns / 2 (two-way), vacuum velocity
    }

    private struct RadMeta
    {
        public int SamplesPerTrace;
        public int TraceCount;
        public double SamplingFrequencyMhz;
        public double DistanceIntervalMetres;
        public string Antenna;
    }

    private static RadMeta ParseRadHeader(string radPath)
    {
        var meta = default(RadMeta);
        foreach (var rawLine in File.ReadAllLines(radPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            int colon = line.IndexOf(':');
            if (colon < 1) continue;
            var key = line.Substring(0, colon).Trim().ToUpperInvariant();
            var value = line.Substring(colon + 1).Trim();
            switch (key)
            {
                case "SAMPLES":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var samples))
                        meta.SamplesPerTrace = samples;
                    break;
                case "LAST TRACE":
                case "LASTTRACE":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var last))
                        meta.TraceCount = last;
                    break;
                case "FREQUENCY":
                case "SAMPLING FREQUENCY":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var freq))
                        meta.SamplingFrequencyMhz = freq;
                    break;
                case "DISTANCE INTERVAL":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var dx))
                        meta.DistanceIntervalMetres = dx;
                    break;
                case "ANTENNAS":
                    meta.Antenna = value;
                    break;
            }
        }
        return meta;
    }
}
