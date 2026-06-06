#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Quarry.Ingestion;

// =============================================================================
// GprRadargram -- POCO for one GPR radargram product.
//
// Layer 1 ingestion type. Carries the raw scan as a regular grid of
// reflection amplitudes plus a sparse pick set of interpreted reflectors.
// World coordinates in metres. SEG-Y / DZT / RD3 are intentionally NOT
// parsed here; conversion happens externally via RGPR per wiki paper 20
// section 3. CSV is the realistic on-disk format for Tinti-zip products and
// in-house TN field acquisitions.
// =============================================================================

public sealed class GprTrace
{
    public GprTrace(
        double x, double y,
        IReadOnlyList<double> sampleAmplitudes,
        double sampleSpacingMetres,
        double sampleIntervalNs = 0.0)
    {
        if (sampleAmplitudes == null) throw new ArgumentNullException(nameof(sampleAmplitudes));
        if (sampleSpacingMetres <= 0) throw new ArgumentOutOfRangeException(nameof(sampleSpacingMetres), "> 0");
        X = x; Y = y;
        SampleAmplitudes = sampleAmplitudes;
        SampleSpacingMetres = sampleSpacingMetres;
        SampleIntervalNs = sampleIntervalNs;
    }

    public double X { get; }
    public double Y { get; }
    public IReadOnlyList<double> SampleAmplitudes { get; }
    public double SampleSpacingMetres { get; }

    /// <summary>True two-way sample time interval in nanoseconds, if the reader knew it
    /// (MALA: 1000/sampling-freq; IDS: Y_TIME_CELL). 0 = unknown. Velocity-INDEPENDENT;
    /// depth = v*(i*SampleIntervalNs)/2 with the stone velocity. Prefer this over deriving
    /// dt from SampleSpacingMetres, whose velocity convention differs per format.</summary>
    public double SampleIntervalNs { get; }

    public int SampleCount => SampleAmplitudes.Count;
    public double TraceDepthMetres => SampleSpacingMetres * SampleAmplitudes.Count;
}

public sealed class GprReflectorPick
{
    public GprReflectorPick(double x, double y, double depthMetres, double confidence, string label)
    {
        if (confidence < 0 || confidence > 1) throw new ArgumentOutOfRangeException(nameof(confidence), "0..1");
        X = x; Y = y;
        DepthMetres = depthMetres;
        Confidence = confidence;
        Label = label ?? string.Empty;
    }

    public double X { get; }
    public double Y { get; }
    public double DepthMetres { get; }
    public double Confidence { get; }
    public string Label { get; }
}

public sealed class GprRadargram
{
    public GprRadargram(
        string id,
        IReadOnlyList<GprTrace> traces,
        IReadOnlyList<GprReflectorPick> picks)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id required", nameof(id));
        Id = id;
        Traces = traces ?? throw new ArgumentNullException(nameof(traces));
        Picks = picks ?? throw new ArgumentNullException(nameof(picks));
    }

    public string Id { get; }
    public IReadOnlyList<GprTrace> Traces { get; }
    public IReadOnlyList<GprReflectorPick> Picks { get; }

    public int TraceCount => Traces.Count;
    public int PickCount => Picks.Count;

    public override string ToString() =>
        $"GprRadargram({Id}, traces={TraceCount}, picks={PickCount})";
}
