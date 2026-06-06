#nullable disable
using System;
using System.Collections.Generic;
using System.IO;

namespace Frahan.Masonry.Quarry.Ingestion;

// =============================================================================
// GprSegYReader -- SEG-Y rev 0/1/2 to GprRadargram.
//
// SEG-Y is the SEG (Society of Exploration Geophysicists) industry-standard
// trace format. Most modern GPR systems export it; nearly every seismic
// processing tool consumes it. Big-endian by default. Spec is public.
//
// File layout:
//   * 3200-byte EBCDIC textual header  (40 cards of 80 bytes each)
//   * 400-byte binary header           (sample count, interval, format code...)
//   * N traces, each:
//        - 240-byte trace header (source/receiver XY, scalar, sample count)
//        - sample data (sample_count * bytes_per_sample)
//
// Sample format codes (binary header bytes 24-25):
//   1  = 4-byte IBM 360 floating point   (legacy, decoded here)
//   2  = 4-byte int32 big-endian
//   3  = 2-byte int16 big-endian
//   5  = 4-byte IEEE-754 floating point  (common in modern GPR exports)
//   8  = 1-byte int8
//
// This reader handles formats 1, 2, 3, 5. Format 8 falls through with a
// clear NotSupportedException. Code 4 (4-byte fixed-point with gain) and
// codes 6..16 (rev2 extensions) are out of scope for GPR.
//
// Coordinates: trace header bytes 73-76 (sourceX) and 77-80 (sourceY), int32
// big-endian, scaled by bytes 71-72 (coordScalar) per SEG-Y rev1 §3.
// =============================================================================

public static class GprSegYReader
{
    public static GprRadargram Load(string path, string id = null)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path required", nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("SEG-Y file not found", path);

        using (var stream = File.OpenRead(path))
        using (var reader = new BinaryReader(stream))
        {
            stream.Seek(3200, SeekOrigin.Begin); // skip textual header
            var binHeader = reader.ReadBytes(400);
            if (binHeader.Length < 400) throw new InvalidDataException("Binary header truncated.");

            int sampleInterval_us = ReadInt16BE(binHeader, 16);   // bytes 17-18: 1/4 sample interval microseconds
            int samplesPerTrace = ReadUInt16BE(binHeader, 20);    // bytes 21-22: samples per trace
            int formatCode = ReadInt16BE(binHeader, 24);          // bytes 25-26: data sample format
            int bytesPerSample = BytesPerSample(formatCode);
            if (samplesPerTrace <= 0) throw new InvalidDataException("Binary header: samples per trace must be > 0.");
            if (sampleInterval_us <= 0) throw new InvalidDataException("Binary header: sample interval must be > 0.");

            // GPR-flavoured assumption: sample interval is in picoseconds-per-step
            // converted to a sample-spacing in metres for our radargram POCO. The
            // CSV format already uses metres; we standardise on a per-sample
            // metre spacing derived from interval_us assuming v_air = 0.3 m/ns.
            // This is a placeholder physical conversion the caller can override
            // by re-emitting traces with a custom dz. For non-GPR seismic SEG-Y
            // the interval is microseconds-per-trace-sample which is meaningless
            // as metres — caller must reproject downstream.
            double dzMetresPerSample = (sampleInterval_us * 1e-6) * 0.15; // 0.3 m/ns * 0.5 for two-way
            if (dzMetresPerSample <= 0) dzMetresPerSample = 1.0; // safe default

            var traces = new List<GprTrace>();
            while (stream.Position < stream.Length)
            {
                long traceStart = stream.Position;
                var traceHeader = reader.ReadBytes(240);
                if (traceHeader.Length < 240) break; // truncated last trace

                int coordScalar = ReadInt16BE(traceHeader, 70); // bytes 71-72; >0 multiply, <0 divide
                int sourceX_raw = ReadInt32BE(traceHeader, 72); // bytes 73-76
                int sourceY_raw = ReadInt32BE(traceHeader, 76); // bytes 77-80
                int nsThisTrace = ReadUInt16BE(traceHeader, 114); // bytes 115-116; falls back to global

                int n = nsThisTrace > 0 ? nsThisTrace : samplesPerTrace;
                double x = ApplyScalar(sourceX_raw, coordScalar);
                double y = ApplyScalar(sourceY_raw, coordScalar);

                var samples = new double[n];
                var sampleBytes = reader.ReadBytes(n * bytesPerSample);
                if (sampleBytes.Length < n * bytesPerSample)
                {
                    throw new InvalidDataException(
                        $"Trace at offset {traceStart} truncated: expected {n * bytesPerSample} bytes, got {sampleBytes.Length}.");
                }
                DecodeSamples(sampleBytes, formatCode, samples);
                traces.Add(new GprTrace(x, y, samples, dzMetresPerSample));
            }

            return new GprRadargram(id ?? Path.GetFileNameWithoutExtension(path), traces, new List<GprReflectorPick>());
        }
    }

    private static int BytesPerSample(int formatCode)
    {
        switch (formatCode)
        {
            case 1: return 4;
            case 2: return 4;
            case 3: return 2;
            case 5: return 4;
            case 8: throw new NotSupportedException("SEG-Y format 8 (int8) not supported.");
            default: throw new NotSupportedException($"SEG-Y format code {formatCode} not supported. Supported: 1, 2, 3, 5.");
        }
    }

    private static void DecodeSamples(byte[] src, int formatCode, double[] dst)
    {
        switch (formatCode)
        {
            case 1:
                for (int i = 0; i < dst.Length; i++)
                    dst[i] = IbmFloat32(ReadUInt32BE(src, i * 4));
                break;
            case 2:
                for (int i = 0; i < dst.Length; i++)
                    dst[i] = ReadInt32BE(src, i * 4);
                break;
            case 3:
                for (int i = 0; i < dst.Length; i++)
                    dst[i] = ReadInt16BE(src, i * 2);
                break;
            case 5:
                for (int i = 0; i < dst.Length; i++)
                    dst[i] = ReadIeeeFloat32BE(src, i * 4);
                break;
        }
    }

    private static double ApplyScalar(int raw, int scalar)
    {
        if (scalar == 0) return raw;
        return scalar > 0 ? (double)raw * scalar : (double)raw / -scalar;
    }

    // IBM System/360 base-16 floating point. Sign bit + 7-bit excess-64 exponent
    // + 24-bit fraction. Reference: SEG-Y rev 1 Appendix E.
    private static double IbmFloat32(uint bits)
    {
        if (bits == 0) return 0.0;
        int sign = (bits & 0x80000000u) != 0 ? -1 : 1;
        int exponent = (int)((bits >> 24) & 0x7Fu) - 64;
        uint fraction = bits & 0x00FFFFFFu;
        return sign * fraction * Math.Pow(16.0, exponent - 6);
    }

    private static short ReadInt16BE(byte[] src, int offset) =>
        (short)((src[offset] << 8) | src[offset + 1]);

    private static ushort ReadUInt16BE(byte[] src, int offset) =>
        (ushort)((src[offset] << 8) | src[offset + 1]);

    private static int ReadInt32BE(byte[] src, int offset) =>
        (src[offset] << 24) | (src[offset + 1] << 16) | (src[offset + 2] << 8) | src[offset + 3];

    private static uint ReadUInt32BE(byte[] src, int offset) =>
        ((uint)src[offset] << 24) | ((uint)src[offset + 1] << 16) | ((uint)src[offset + 2] << 8) | src[offset + 3];

    private static double ReadIeeeFloat32BE(byte[] src, int offset)
    {
        uint bits = ReadUInt32BE(src, offset);
        var bytesLe = new byte[4]
        {
            (byte)(bits & 0xFF),
            (byte)((bits >> 8) & 0xFF),
            (byte)((bits >> 16) & 0xFF),
            (byte)((bits >> 24) & 0xFF),
        };
        return BitConverter.ToSingle(bytesLe, 0);
    }
}
