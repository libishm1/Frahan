#nullable disable
using System;
using System.Collections.Generic;
using System.IO;

namespace Frahan.Masonry.Quarry.Ingestion;

// =============================================================================
// GsfReader -- Geoscanners GSF (.gsf) ingest (Akula 9000/9500 systems).
//
// GSF was previously treated as an opaque proprietary format. The binary layout
// here was reverse-engineered from the official Geoscanners "GPRSoft Viewer"
// reader (GPRSoftViewer.exe, method GeoscannersFiles.Read_gsf_Files, decompiled
// to IL) and verified against the Bonduà et al. 2024 Carpinis travertine survey
// (Data 9(3):42): the parse reproduces GPRSoft's own time/depth axes and the
// adjacent-trace coherence of a real radargram (0.86, not noise).
//
// Layout (single-profile .gsf, little-endian):
//   bytes 0-3      ASCII "gsfm" magic (a multi-file index also contains "gsfm";
//                  this reader handles the single-profile form).
//   header block   bytes 0 .. 1499.
//     int16  @ 84    samples per trace (Sp_Samples).
//     float32@ 86    relative permittivity (gprDC) -> velocity v = 0.2998/sqrt(eps_r).
//     float32@ 363   Time_Range (ns) -> two-way sample interval dt = Time_Range/samples.
//   data start     byte 1500 (Start_byte = 0x5dc).
//   trace record   samples*2 bytes (int16 amplitudes, samples FIRST) + a 40-byte
//                  per-trace trailer  => period = samples*2 + 40.
//   trace count    floor((fileLength - 1500) / period); a ~777-byte file footer
//                  (markers) trails the last full trace and is ignored.
//
// As with the MALA reader, the TRUE two-way sample interval dt (ns) is carried on
// GprTrace.SampleIntervalNs (velocity-independent); the caller scales depth with
// the stone velocity (GPR Survey Grid Velocity / preset travertine_390). The
// vacuum sample spacing dz = dt*0.15 m is provided as a fallback only.
// =============================================================================

public static class GsfReader
{
    private const int DataStart = 1500;        // Start_byte = 0x5dc
    private const int TraceTrailer = 40;        // per-trace trailer after the samples
    private const int OffSamples = 84;          // int16  samples/trace
    private const int OffEpsR = 86;             // float32 relative permittivity
    private const int OffTimeRange = 363;       // float32 time range (ns)

    public static GprRadargram Load(string path, string id = null)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path required", nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("GSF file not found", path);

        byte[] b = File.ReadAllBytes(path);
        if (b.Length < DataStart + 4)
            throw new InvalidDataException($"GSF file too small ({b.Length} bytes).");
        if (!(b[0] == (byte)'g' && b[1] == (byte)'s' && b[2] == (byte)'f' && b[3] == (byte)'m'))
            throw new InvalidDataException("Not a GSF file (missing 'gsfm' magic).");

        int samples = BitConverter.ToInt16(b, OffSamples);
        if (samples <= 0 || samples > 16384)
            throw new InvalidDataException($"GSF samples/trace out of range ({samples}).");
        float timeRangeNs = BitConverter.ToSingle(b, OffTimeRange);
        float epsR = BitConverter.ToSingle(b, OffEpsR);

        int period = samples * 2 + TraceTrailer;
        int traceCount = (b.Length - DataStart) / period;
        if (traceCount <= 0)
            throw new InvalidDataException($"GSF has no full traces (len={b.Length}, samples={samples}).");

        double dtNs = timeRangeNs > 0 ? timeRangeNs / samples : 0.0;   // true two-way interval, velocity-independent
        double dzMetres = dtNs * 0.15;                                 // vacuum two-way fallback (caller sets v)
        // Nominal in-line trace spacing. Geoscanners stores a scans/metre field but its offset is not yet
        // confirmed across firmware revisions; the GPR Survey Grid lays lines out by its own Line Spacing,
        // so this only sets the along-line distance axis. 0.05 m is the typical Akula wheel/odometer step.
        double dx = 0.05;

        var traces = new List<GprTrace>(traceCount);
        for (int t = 0; t < traceCount; t++)
        {
            int off = DataStart + t * period;          // samples come first in the record
            var s = new double[samples];
            for (int i = 0; i < samples; i++)
                s[i] = BitConverter.ToInt16(b, off + i * 2);
            traces.Add(new GprTrace(t * dx, 0.0, s, dzMetres, dtNs));
        }

        return new GprRadargram(
            id ?? Path.GetFileNameWithoutExtension(path),
            traces,
            new List<GprReflectorPick>());
    }
}
