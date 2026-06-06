#nullable disable
using System;
using Frahan.Masonry.Cutting;

namespace Frahan.Masonry.Quarry.Ingestion;

// =============================================================================
// GeoFractNetFracture -- one fracture-plane prediction from a GeoFractNet
// pass (or any equivalent CNN producing mapped fractures from photogrammetry
// orthomosaics + DEMs).
//
// GeoFractNet output is consumed offline: net48 cannot host PyTorch.
// Producers run inference externally (Python) and dump a CSV; this Frahan
// reader picks it up.
// =============================================================================

public sealed class GeoFractNetFracture
{
    public GeoFractNetFracture(FracturePlane plane, double confidence, int setId, string label)
    {
        if (plane == null) throw new ArgumentNullException(nameof(plane));
        if (confidence < 0 || confidence > 1) throw new ArgumentOutOfRangeException(nameof(confidence), "0..1");
        if (setId < 0) throw new ArgumentOutOfRangeException(nameof(setId));
        Plane = plane;
        Confidence = confidence;
        SetId = setId;
        Label = label ?? string.Empty;
    }

    public FracturePlane Plane { get; }
    public double Confidence { get; }
    public int SetId { get; }
    public string Label { get; }
}
