#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Frahan.Masonry.Quarry.Ingestion;

// =============================================================================
// GprIdsDtReader -- IDS GeoRadar GRED HD ".dt" (+ companion ".hdr_dt") reader.
//
// Record layout (independently VERIFIED against the Bondua/Tinti 2024 Botticino
// marble data, LA010001.DT = 269,336 B = 262 records x 1028 B = 11 header records
// + 251 trace records, 512 samples/trace, trace 0 = clean radar oscillation):
//   byte 0      : 'V' magic
//   bytes 1..3  : file version
//   bytes 4..5  : len_rec  (uint16 LE) = record stride = 4 + 2 * samples_per_trace
//   records     : fixed len_rec-byte blocks. Header records come first (first byte
//                 a letter: V / F / G / A / H ...). The first record whose first
//                 byte is 'R' is the first TRACE; trace count = remaining records.
//   per trace   : marker1 (int16 LE) + marker2 (int16 LE) + samples * int16 LE.
// Physical scaling from the companion ".hdr_dt" (ASCII "<KEY>" then value):
//   X_CELL -> trace spacing dx; Y_TIME_CELL & PROP_VEL -> dz = T_CELL*VEL/2 (two-way);
//   AD_OFFSET -> sample bias (subtracted). Companion optional (defaults dx=dz=1).
//
// The public file structure was understood with reference to RGPR's readIDS.R
// (E. Huber, GPL>=2); this is an INDEPENDENT clean-room implementation (a binary
// file layout is not itself copyrightable). Strict trailing-byte validation; on
// mismatch the reader throws so the caller can fall back to a SEG-Y export.
//
// NOTE: ".dt" here is IDS GRED -- distinct from pulseEKKO ".dt1" (GprDt1Reader).
// =============================================================================

public static class GprIdsDtReader
{
    public static GprRadargram Load(string dtPath, string id = null)
    {
        if (string.IsNullOrWhiteSpace(dtPath)) throw new ArgumentException("dtPath required", nameof(dtPath));
        if (!File.Exists(dtPath)) throw new FileNotFoundException("IDS .dt file not found", dtPath);

        var bytes = File.ReadAllBytes(dtPath);
        if (bytes.Length < 6 || bytes[0] != (byte)'V')
            throw new InvalidDataException("Not an IDS GRED .dt file (missing 'V' magic).");

        int lenRec = bytes[4] | (bytes[5] << 8);
        if (lenRec <= 4 || ((lenRec - 4) % 2) != 0)
            throw new InvalidDataException($"IDS .dt: implausible record length {lenRec}.");
        if (bytes.Length % lenRec != 0)
            throw new InvalidDataException($"IDS .dt: size {bytes.Length} not a multiple of record length {lenRec}.");

        int samples = (lenRec - 4) / 2;
        int nrec = bytes.Length / lenRec;

        int dataRec = -1;
        for (int i = 0; i < nrec; i++)
            if (bytes[i * lenRec] == (byte)'R') { dataRec = i; break; }
        if (dataRec < 0)
            throw new InvalidDataException("IDS .dt: no 'R' (radar) data record found; route to SEG-Y fallback.");

        int dataOffset = dataRec * lenRec;
        int traceCount = nrec - dataRec;
        if ((long)traceCount * lenRec != bytes.Length - dataOffset)
            throw new InvalidDataException("IDS .dt: trailing-byte validation failed; route to SEG-Y fallback.");

        var hdr = TryParseHdr(dtPath);
        double dx = hdr.XCell > 0 ? hdr.XCell : 1.0;
        double dz = (hdr.TimeCell > 0 && hdr.PropVel > 0) ? hdr.TimeCell * hdr.PropVel / 2.0 : 1.0;
        double adOffset = hdr.AdOffset;
        // TRUE two-way sample interval (ns): Y_TIME_CELL is in seconds. Velocity-independent;
        // carry it so depth = v*(i*dt)/2 with the stone velocity (see GprTrace.SampleIntervalNs).
        double dtNs = hdr.TimeCell > 0 ? hdr.TimeCell * 1e9 : 0.0;

        var traces = new List<GprTrace>(traceCount);
        for (int t = 0; t < traceCount; t++)
        {
            int off = dataOffset + t * lenRec + 4; // skip marker1 + marker2
            var s = new double[samples];
            for (int i = 0; i < samples; i++)
            {
                short raw = (short)(bytes[off] | (bytes[off + 1] << 8));
                s[i] = raw - adOffset;
                off += 2;
            }
            traces.Add(new GprTrace(t * dx, 0.0, s, dz, dtNs));
        }

        return new GprRadargram(
            id ?? Path.GetFileNameWithoutExtension(dtPath),
            traces,
            new List<GprReflectorPick>());
    }

    private struct IdsHdr { public double XCell, TimeCell, PropVel, AdOffset; }

    private static IdsHdr TryParseHdr(string dtPath)
    {
        var hdr = default(IdsHdr);
        string p = FindCompanion(dtPath);
        if (p == null) return hdr;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = File.ReadAllLines(p);
        for (int i = 0; i < lines.Length; i++)
        {
            string t = lines[i].Trim();
            if (t.Length > 2 && t[0] == '<' && t[t.Length - 1] == '>')
            {
                string key = t.Substring(1, t.Length - 2);
                for (int j = i + 1; j < lines.Length; j++)
                {
                    string v = lines[j].Trim();
                    if (v.Length == 0) continue;
                    if (v[0] == '<') break;
                    map[key] = v;
                    break;
                }
            }
        }
        hdr.XCell = GetD(map, "X_CELL");
        hdr.TimeCell = GetD(map, "Y_TIME_CELL");
        hdr.PropVel = GetD(map, "PROP_VEL");
        hdr.AdOffset = GetD(map, "AD_OFFSET");
        return hdr;
    }

    private static string FindCompanion(string dtPath)
    {
        foreach (var ext in new[] { ".hdr_dt", ".HDR_DT" })
        {
            var c = Path.ChangeExtension(dtPath, ext);
            if (File.Exists(c)) return c; // File.Exists is case-insensitive on Windows
        }
        return null;
    }

    private static double GetD(Dictionary<string, string> m, string key)
    {
        if (m.TryGetValue(key, out var v))
        {
            var parts = v.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 &&
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                return d;
        }
        return 0.0;
    }
}
