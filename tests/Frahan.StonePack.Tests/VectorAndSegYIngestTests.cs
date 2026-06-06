#nullable disable
using System;
using System.IO;
using System.Text;
using Frahan.Masonry.Quarry.Ingestion;

namespace Frahan.Tests;

// =============================================================================
// VectorAndSegYIngestTests -- Layer 1 ingest tests for the cross-format
// readers added 2026-05-22 in response to the "go all out, multimodel"
// directive.
//
// Covers:
//   * ShapefileFractureReader against the Loviisa Zenodo raw data
//   * GeoJsonFractureReader against synthetic in-memory text
//   * VectorFractureReader dispatcher
//   * GprSegYReader against synthetic SEG-Y written from inside the test
//   * GprSegYReader IBM-float decode on known bit patterns
//
// Loviisa test SKIPs if the raw shapefile isn't present (clean-clone case).
// =============================================================================

static class VectorAndSegYIngestTests
{
    private const string LoviisaShp = @"D:\code_ws\Template-General\raw\2026-05-22\zenodo_loviisa\U-Net_Traces\KB11_2022.shp";

    public static void ShapefileFractureReader_LoadsLoviisaIfPresent()
    {
        if (!File.Exists(LoviisaShp))
        {
            Console.WriteLine($"        info: Loviisa raw data not present at {LoviisaShp} - test is a no-op");
            return;
        }

        var coll = ShapefileFractureReader.Load(LoviisaShp);
        Assert(coll.Count > 0, $"expected > 0 traces, got {coll.Count}");
        Assert(coll.Traces[0].VertexCount >= 2, $"first trace had {coll.Traces[0].VertexCount} verts");
        Assert(coll.CrsWkt.Contains("EUREF_FIN") || coll.CrsWkt.Contains("TM35FIN"),
            $"crs wkt did not look like EUREF_FIN_TM35FIN: {coll.CrsWkt.Substring(0, Math.Min(60, coll.CrsWkt.Length))}");
    }

    public static void GeoJsonFractureReader_ParsesLineString()
    {
        var text = "{\"type\":\"FeatureCollection\",\"features\":[" +
                   "{\"type\":\"Feature\",\"properties\":{\"name\":\"trace_a\",\"conf\":\"0.95\"}," +
                   "\"geometry\":{\"type\":\"LineString\",\"coordinates\":[[0.0,0.0],[1.0,1.0],[2.0,0.0]]}}," +
                   "{\"type\":\"Feature\",\"properties\":{\"name\":\"trace_b\"}," +
                   "\"geometry\":{\"type\":\"LineString\",\"coordinates\":[[5.0,0.0],[5.0,1.0]]}}]}";
        var coll = GeoJsonFractureReader.LoadFromText(text, "test.geojson");
        Assert(coll.Count == 2, $"expected 2 traces, got {coll.Count}");
        Assert(coll.Traces[0].VertexCount == 3, $"first trace had {coll.Traces[0].VertexCount} verts");
        Assert(coll.Traces[0].Attributes["name"] == "trace_a", $"attr name: {coll.Traces[0].Attributes["name"]}");
        AssertNear(coll.Traces[0].TotalLengthMetres(), Math.Sqrt(2.0) + Math.Sqrt(2.0), 1e-9, "total length");
    }

    public static void GeoJsonFractureReader_HandlesMultiLineString()
    {
        var text = "{\"type\":\"FeatureCollection\",\"features\":[" +
                   "{\"type\":\"Feature\",\"properties\":{}," +
                   "\"geometry\":{\"type\":\"MultiLineString\",\"coordinates\":[" +
                   "[[0.0,0.0],[1.0,0.0]],[[2.0,0.0],[3.0,0.0]]]}}]}";
        var coll = GeoJsonFractureReader.LoadFromText(text, "multi.geojson");
        Assert(coll.Count == 2, $"MultiLineString should split into 2 traces, got {coll.Count}");
    }

    public static void VectorFractureReader_DispatchesByExtension()
    {
        var geojsonPath = Path.Combine(Path.GetTempPath(), "frahan_dispatch_test.geojson");
        try
        {
            File.WriteAllText(geojsonPath, "{\"type\":\"FeatureCollection\",\"features\":[" +
                "{\"type\":\"Feature\",\"properties\":{},\"geometry\":{\"type\":\"LineString\"," +
                "\"coordinates\":[[0.0,0.0],[1.0,0.0]]}}]}");
            var coll = VectorFractureReader.Load(geojsonPath);
            Assert(coll.Count == 1, $"dispatcher delivered {coll.Count} traces");
        }
        finally
        {
            if (File.Exists(geojsonPath)) File.Delete(geojsonPath);
        }
    }

    public static void VectorFractureReader_RejectsUnknownExtension()
    {
        var path = Path.Combine(Path.GetTempPath(), "frahan_dispatch_unknown.xyz");
        File.WriteAllText(path, "not a vector file");
        try
        {
            bool threw = false;
            try { VectorFractureReader.Load(path); }
            catch (NotSupportedException) { threw = true; }
            Assert(threw, "expected NotSupportedException for .xyz");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    public static void GprSegYReader_RoundTripIeeeFloat32()
    {
        var path = Path.Combine(Path.GetTempPath(), "frahan_segy_ieee32.sgy");
        WriteSyntheticSegY(path, formatCode: 5, samplesPerTrace: 4, sampleIntervalUs: 100, numTraces: 2,
            sourceXs: new double[] { 0.0, 1.0 }, sourceYs: new double[] { 0.0, 0.0 },
            sampleValues: new double[] { 0.25, -0.5, 1.5, 2.25 });
        try
        {
            var rg = GprSegYReader.Load(path, "test-segy");
            Assert(rg.TraceCount == 2, $"traceCount {rg.TraceCount}");
            Assert(rg.Traces[0].SampleCount == 4, $"sampleCount {rg.Traces[0].SampleCount}");
            AssertNear(rg.Traces[0].SampleAmplitudes[0], 0.25, 1e-6, "sample[0][0]");
            AssertNear(rg.Traces[0].SampleAmplitudes[1], -0.5, 1e-6, "sample[0][1]");
            AssertNear(rg.Traces[0].SampleAmplitudes[3], 2.25, 1e-6, "sample[0][3]");
            AssertNear(rg.Traces[1].X, 1.0, 1e-9, "trace[1].X");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    public static void GprMalaRd3Reader_RoundTrip()
    {
        var rd3Path = Path.Combine(Path.GetTempPath(), "frahan_test.rd3");
        var radPath = Path.Combine(Path.GetTempPath(), "frahan_test.rad");
        File.WriteAllText(radPath,
            "SAMPLES: 4\n" +
            "LAST TRACE: 3\n" +
            "FREQUENCY: 100\n" +
            "DISTANCE INTERVAL: 0.05\n" +
            "ANTENNAS: 100 MHz HF\n");
        using (var stream = File.Create(rd3Path))
        using (var writer = new BinaryWriter(stream))
        {
            // 3 traces x 4 samples, int16 little-endian
            short[] payload = { 100, 200, 300, 400,  -100, -200, -300, -400,  0, 50, -50, 25 };
            foreach (var v in payload) writer.Write(v);
        }
        try
        {
            var rg = GprMalaRd3Reader.Load(rd3Path, "rd3-test");
            Assert(rg.TraceCount == 3, $"traceCount {rg.TraceCount}");
            Assert(rg.Traces[0].SampleCount == 4, $"sampleCount {rg.Traces[0].SampleCount}");
            AssertNear(rg.Traces[0].SampleAmplitudes[0], 100.0, 1e-9, "t0s0");
            AssertNear(rg.Traces[1].SampleAmplitudes[3], -400.0, 1e-9, "t1s3");
            AssertNear(rg.Traces[2].X, 0.10, 1e-9, "t2.X = 2 * 0.05");
        }
        finally
        {
            if (File.Exists(rd3Path)) File.Delete(rd3Path);
            if (File.Exists(radPath)) File.Delete(radPath);
        }
    }

    public static void GprMalaRd3Reader_RejectsMissingRad()
    {
        var rd3Path = Path.Combine(Path.GetTempPath(), "frahan_no_rad.rd3");
        File.WriteAllBytes(rd3Path, new byte[] { 0, 0, 0, 0 });
        try
        {
            bool threw = false;
            try { GprMalaRd3Reader.Load(rd3Path); }
            catch (FileNotFoundException) { threw = true; }
            Assert(threw, "expected FileNotFoundException when .rad missing");
        }
        finally
        {
            if (File.Exists(rd3Path)) File.Delete(rd3Path);
        }
    }

    public static void GprDt1Reader_RoundTrip()
    {
        var dt1Path = Path.Combine(Path.GetTempPath(), "frahan_test.dt1");
        var hdPath = Path.Combine(Path.GetTempPath(), "frahan_test.HD");
        File.WriteAllText(hdPath,
            "NUMBER OF TRACES = 2\n" +
            "NUMBER OF PTS/TRACE = 3\n" +
            "TOTAL TIME WINDOW = 60 ns\n");
        using (var stream = File.Create(dt1Path))
        using (var writer = new BinaryWriter(stream))
        {
            // Trace 1: 25-float header then 3 int16 samples
            WriteTrace(writer, traceNumber: 1, position: 0.0f, nPoints: 3, samples: new short[] { 10, 20, 30 });
            WriteTrace(writer, traceNumber: 2, position: 0.5f, nPoints: 3, samples: new short[] { 40, 50, 60 });
        }
        try
        {
            var rg = GprDt1Reader.Load(dt1Path, "dt1-test");
            Assert(rg.TraceCount == 2, $"traceCount {rg.TraceCount}");
            Assert(rg.Traces[0].SampleCount == 3, $"sampleCount {rg.Traces[0].SampleCount}");
            AssertNear(rg.Traces[0].SampleAmplitudes[0], 10.0, 1e-9, "t0s0");
            AssertNear(rg.Traces[1].X, 0.5, 1e-6, "t1.X");
            AssertNear(rg.Traces[0].SampleSpacingMetres, (60.0 / 3.0) * 0.15, 1e-9, "dz from HD");
        }
        finally
        {
            if (File.Exists(dt1Path)) File.Delete(dt1Path);
            if (File.Exists(hdPath)) File.Delete(hdPath);
        }
    }

    public static void GprFileReader_DispatchesByExtension()
    {
        var csvPath = Path.Combine(Path.GetTempPath(), "frahan_gpr_disp.csv");
        File.WriteAllText(csvPath, "0.0,0.0,0.05,0.1,0.2\n0.5,0.0,0.05,0.3,0.4\n");
        try
        {
            var rg = GprFileReader.Load(csvPath, "csv-disp");
            Assert(rg.TraceCount == 2, $"csv dispatch traceCount {rg.TraceCount}");
        }
        finally
        {
            if (File.Exists(csvPath)) File.Delete(csvPath);
        }
    }

    private static void WriteTrace(BinaryWriter writer, int traceNumber, float position, int nPoints, short[] samples)
    {
        var hdr = new float[25];
        hdr[0] = traceNumber;
        hdr[1] = position;
        hdr[2] = nPoints;
        hdr[4] = 2; // bytes per point
        for (int i = 0; i < 25; i++) writer.Write(hdr[i]);
        foreach (var s in samples) writer.Write(s);
    }

    public static void GprSegYReader_DecodesIbmFloat32()
    {
        // IBM float 0x41100000 == 1.0 (sign=0, exp=65-64=1, fraction=0x100000)
        // Verify via roundtrip of one trace in format 1.
        var path = Path.Combine(Path.GetTempPath(), "frahan_segy_ibm32.sgy");
        WriteSyntheticSegYIbm(path, ibmEncoded: new uint[] { 0x41100000u, 0xC1100000u });
        try
        {
            var rg = GprSegYReader.Load(path, "test-ibm");
            Assert(rg.TraceCount == 1, $"traceCount {rg.TraceCount}");
            Assert(rg.Traces[0].SampleCount == 2, $"sampleCount {rg.Traces[0].SampleCount}");
            AssertNear(rg.Traces[0].SampleAmplitudes[0], 1.0, 1e-6, "IBM 0x41100000 -> 1.0");
            AssertNear(rg.Traces[0].SampleAmplitudes[1], -1.0, 1e-6, "IBM 0xC1100000 -> -1.0");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // -------------------------------------------------------------------------
    // SEG-Y writers (test helpers only). Big-endian, minimum-viable file.
    // -------------------------------------------------------------------------

    private static void WriteSyntheticSegY(
        string path, int formatCode, int samplesPerTrace, int sampleIntervalUs, int numTraces,
        double[] sourceXs, double[] sourceYs, double[] sampleValues)
    {
        using (var stream = File.Create(path))
        {
            stream.Write(new byte[3200], 0, 3200); // textual header (zeros are fine for round-trip)
            var bin = new byte[400];
            WriteInt16BE(bin, 16, (short)sampleIntervalUs);
            WriteInt16BE(bin, 20, (short)samplesPerTrace);
            WriteInt16BE(bin, 24, (short)formatCode);
            stream.Write(bin, 0, 400);

            for (int t = 0; t < numTraces; t++)
            {
                var hdr = new byte[240];
                WriteInt16BE(hdr, 70, 1);                        // coordScalar
                WriteInt32BE(hdr, 72, (int)sourceXs[t]);
                WriteInt32BE(hdr, 76, (int)sourceYs[t]);
                WriteInt16BE(hdr, 114, (short)samplesPerTrace);
                stream.Write(hdr, 0, 240);

                for (int i = 0; i < samplesPerTrace; i++)
                {
                    var v = (float)sampleValues[i];
                    var leBytes = BitConverter.GetBytes(v);
                    var beBytes = new byte[] { leBytes[3], leBytes[2], leBytes[1], leBytes[0] };
                    stream.Write(beBytes, 0, 4);
                }
            }
        }
    }

    private static void WriteSyntheticSegYIbm(string path, uint[] ibmEncoded)
    {
        using (var stream = File.Create(path))
        {
            stream.Write(new byte[3200], 0, 3200);
            var bin = new byte[400];
            WriteInt16BE(bin, 16, 100);                   // sampleIntervalUs
            WriteInt16BE(bin, 20, (short)ibmEncoded.Length); // samplesPerTrace
            WriteInt16BE(bin, 24, 1);                     // formatCode = IBM float
            stream.Write(bin, 0, 400);

            var hdr = new byte[240];
            WriteInt16BE(hdr, 70, 1);
            WriteInt16BE(hdr, 114, (short)ibmEncoded.Length);
            stream.Write(hdr, 0, 240);
            foreach (var w in ibmEncoded)
            {
                var b = new byte[4]
                {
                    (byte)((w >> 24) & 0xFF),
                    (byte)((w >> 16) & 0xFF),
                    (byte)((w >> 8) & 0xFF),
                    (byte)(w & 0xFF),
                };
                stream.Write(b, 0, 4);
            }
        }
    }

    private static void WriteInt16BE(byte[] dst, int offset, short v)
    {
        dst[offset] = (byte)((v >> 8) & 0xFF);
        dst[offset + 1] = (byte)(v & 0xFF);
    }

    private static void WriteInt32BE(byte[] dst, int offset, int v)
    {
        dst[offset] = (byte)((v >> 24) & 0xFF);
        dst[offset + 1] = (byte)((v >> 16) & 0xFF);
        dst[offset + 2] = (byte)((v >> 8) & 0xFF);
        dst[offset + 3] = (byte)(v & 0xFF);
    }

    private static void Assert(bool ok, string msg)
    {
        if (!ok) throw new Exception(msg);
    }

    private static void AssertNear(double actual, double expected, double tol, string label)
    {
        if (Math.Abs(actual - expected) > tol)
            throw new Exception($"{label}: expected {expected} +- {tol}, got {actual}");
    }
}
