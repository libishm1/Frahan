#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;

namespace Frahan.Masonry.Quarry.GeoPack;

// =============================================================================
// GeoPack v0 DTOs -- spec 08 section 4.
//
// "v0" means: manual / authored crack input only. Live crack detection from
// point clouds (spec 08 § 3 row "Frahan Detect Crack Candidates") is out of
// scope for this slice -- it requires native fits and ML and is large.
//
// Useful slice: take a user-drawn list of FracturePlanes (e.g. from
// FrahanMeshFacesToFracturePlanes) and wrap them in CrackSurface +
// CrackGraph for the spec-08 pipeline, then partition a bench volume into
// BlockCells via SlabCutter for the BlockGraph.
// =============================================================================

public enum CrackSurfaceKind
{
    Plane = 0,
    Quadric = 1,
    Mesh = 2,
}

public sealed class CrackCandidate
{
    public CrackCandidate(IReadOnlyList<double> samplePointsXyz, double confidence, string source)
    {
        SamplePointsXyz = samplePointsXyz ?? throw new ArgumentNullException(nameof(samplePointsXyz));
        if (samplePointsXyz.Count == 0 || samplePointsXyz.Count % 3 != 0)
            throw new ArgumentException("samplePointsXyz must be a non-empty multiple of 3", nameof(samplePointsXyz));
        if (confidence < 0 || confidence > 1) throw new ArgumentOutOfRangeException(nameof(confidence), "0..1");
        Confidence = confidence;
        Source = string.IsNullOrWhiteSpace(source) ? "manual" : source;
    }

    public IReadOnlyList<double> SamplePointsXyz { get; }
    public double Confidence { get; }
    public string Source { get; }

    public int SampleCount => SamplePointsXyz.Count / 3;
}

public sealed class CrackSurface
{
    public CrackSurface(string id, CrackSurfaceKind kind, FracturePlane fitPlane, double rmsError, double confidence)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id required", nameof(id));
        if (rmsError < 0) throw new ArgumentOutOfRangeException(nameof(rmsError));
        if (confidence < 0 || confidence > 1) throw new ArgumentOutOfRangeException(nameof(confidence), "0..1");
        Id = id;
        Kind = kind;
        FitPlane = fitPlane ?? throw new ArgumentNullException(nameof(fitPlane));
        RmsError = rmsError;
        Confidence = confidence;
    }

    public string Id { get; }
    public CrackSurfaceKind Kind { get; }
    public FracturePlane FitPlane { get; }
    public double RmsError { get; }
    public double Confidence { get; }
}

public sealed class CrackGraph
{
    public CrackGraph(IReadOnlyList<CrackSurface> cracks)
    {
        Cracks = cracks ?? throw new ArgumentNullException(nameof(cracks));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < cracks.Count; i++)
        {
            if (cracks[i] == null) throw new ArgumentException($"cracks[{i}] is null", nameof(cracks));
            if (!seen.Add(cracks[i].Id))
                throw new ArgumentException($"duplicate crack id '{cracks[i].Id}'", nameof(cracks));
        }
    }

    public IReadOnlyList<CrackSurface> Cracks { get; }
    public int Count => Cracks.Count;

    /// <summary>Project the graph back to a flat list of FracturePlanes.</summary>
    public IReadOnlyList<FracturePlane> ToFracturePlanes()
    {
        var output = new List<FracturePlane>(Cracks.Count);
        for (int i = 0; i < Cracks.Count; i++) output.Add(Cracks[i].FitPlane);
        return output;
    }

    public override string ToString() => $"CrackGraph(N={Count})";
}
