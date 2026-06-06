#nullable disable
using System;
using System.Collections.Generic;
using System.IO;

namespace Frahan.Masonry.Quarry.Ingestion;

// =============================================================================
// GprDztReader -- GSSI .dzt (SIR) ground-penetrating-radar ingest.
//
// GSSI SIR-series controllers write a single .dzt: a fixed header (MINHEADSIZE =
// 1024 bytes per channel) followed by raw scan data, rh_nsamp samples per scan,
// each sample rh_bits-bit LITTLE-ENDIAN. Reference: GSSI SIR System manual +
// readgssi (Ian Nesbitt, BSD-3) + RGPR (Emanuel Huber). Cross-checked against a
// real file (270MHz_gneiss0-20_h1.DZT: nsamp=1024 bits=32 nchan=1 -> 2582 traces).
//
// Header fields read (offsets, all little-endian):
//   2   rh_data   int16   header size flag (if < 1024, header is MINHEADSIZE)
//   4   rh_nsamp  int16   samples per scan
//   6   rh_bits   int16   bits per sample (8 / 16 / 32)
//   14  rhf_spm   float32 scans per metre (-> trace spacing dx = 1 / spm)
//   26  rhf_range float32 two-way time window (ns)
//   52  rh_nchan  int16   number of channels
//   54  rhf_epsr  float32 average dielectric (for depth conversion)
//   62  rhf_depth float32 depth window (m)
//
// Amplitudes are kept RAW (per the RD3 reader convention); callers needing
// gain / bias normalisation rescale GprTrace.SampleAmplitudes themselves.
//
// Limitation: multi-channel (.dzt with rh_nchan > 1) is read as a single
// concatenated scan stream (channel data is not de-interleaved yet). All known
// test files are single-channel. TODO when a multi-channel granite file appears.
// =============================================================================

public static class GprDztReader
{
    private const int MinHeadSize = 1024;

    public static GprRadargram Load(string path, string id = null)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path required", nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("DZT file not found", path);

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < MinHeadSize)
            throw new InvalidDataException("DZT smaller than minimum header (1024 bytes).");

        short rhData   = BitConverter.ToInt16(bytes, 2);
        short rhNsamp  = BitConverter.ToInt16(bytes, 4);
        short rhBits   = BitConverter.ToInt16(bytes, 6);
        float rhfSpm   = BitConverter.ToSingle(bytes, 14);
        float rhfRange = BitConverter.ToSingle(bytes, 26);
        short rhNchan  = BitConverter.ToInt16(bytes, 52);
        float rhfEpsr  = BitConverter.ToSingle(bytes, 54);
        float rhfDepth = BitConverter.ToSingle(bytes, 62);

        if (rhNsamp <= 0) throw new InvalidDataException("DZT header: rh_nsamp <= 0.");
        if (rhBits != 8 && rhBits != 16 && rhBits != 32)
            throw new InvalidDataException($"DZT header: unsupported rh_bits {rhBits} (expected 8/16/32).");
        int nchan = rhNchan > 0 ? rhNchan : 1;
        int bps = rhBits / 8;

        int dataOffset = (rhData < MinHeadSize) ? MinHeadSize * nchan : rhData * nchan;
        if (dataOffset >= bytes.Length)
            throw new InvalidDataException("DZT data offset is past the end of the file.");

        long scanBytes = (long)rhNsamp * bps;
        int traceCount = (int)((bytes.Length - dataOffset) / scanBytes);
        if (traceCount <= 0) throw new InvalidDataException("DZT: no trace data after the header.");

        double dz = SampleSpacingMetres(rhNsamp, rhfRange, rhfEpsr, rhfDepth);
        double dx = rhfSpm > 0f ? 1.0 / rhfSpm : 1.0;

        var traces = new List<GprTrace>(traceCount);
        int pos = dataOffset;
        for (int t = 0; t < traceCount; t++)
        {
            var samples = new double[rhNsamp];
            for (int i = 0; i < rhNsamp; i++)
            {
                switch (bps)
                {
                    case 1: samples[i] = bytes[pos]; break;                       // unsigned 8-bit
                    case 2: samples[i] = BitConverter.ToInt16(bytes, pos); break; // signed 16-bit LE
                    default: samples[i] = BitConverter.ToInt32(bytes, pos); break;// signed 32-bit LE
                }
                pos += bps;
            }
            traces.Add(new GprTrace(t * dx, 0.0, samples, dz));
        }

        return new GprRadargram(
            id ?? Path.GetFileNameWithoutExtension(path),
            traces,
            new List<GprReflectorPick>());
    }

    // Depth per sample. DZT carries the dielectric (rhf_epsr) and the depth
    // window (rhf_depth); prefer the file's own depth when present, else derive
    // from the two-way time range and epsr: v = 0.2998 / sqrt(epsr) m/ns.
    private static double SampleSpacingMetres(int nsamp, float rangeNs, float epsr, float depthM)
    {
        if (nsamp <= 0) return 1.0;
        if (depthM > 0f) return depthM / nsamp;
        double er = epsr > 1f ? epsr : 1.0;
        double v = 0.2998 / Math.Sqrt(er);
        double dtNs = (rangeNs > 0f ? rangeNs : 1.0) / nsamp;
        return dtNs * v / 2.0;
    }
}
