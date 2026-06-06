#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Core.Reports;

// =============================================================================
// FrahanReport typed-record base (task #58 orphan-report fix sweep).
//
// Every Frahan Report component (PackingReport, PackingPlanReport, MeshDiagnostics,
// PackDiagnostics, MeshQualityReport, ChartFlatnessReport, FabricationPrepReport,
// BlockCutOptInspector) emits its text-panel diagnostics AND a typed record
// derived from this base so downstream components can chain off it.
//
// Discipline source: wiki/specs/component_decomposition_plan.md §6 +
// `feedback_hitl_cards_design_grounded` memory.
// =============================================================================

/// <summary>Base class for all Frahan typed-record reports.</summary>
public abstract class FrahanReport
{
    /// <summary>Component that produced this report (for traceability).</summary>
    public string ProducerComponent;

    /// <summary>UTC timestamp when the report was generated.</summary>
    public DateTime GeneratedUtc = DateTime.UtcNow;

    /// <summary>Per-line diagnostics (the same list shown in the text panel).</summary>
    public IReadOnlyList<string> Diagnostics;

    /// <summary>Numeric per-field stats (mean, max, distribution).</summary>
    public IReadOnlyDictionary<string, double> Numerics;

    /// <summary>True when the report's invariants all hold (no warnings).</summary>
    public bool AllInvariantsHeld;
}

/// <summary>Packing-result report (output of PackingReportComponent +
/// PackingPlanReportComponent).</summary>
public sealed class PackingReport : FrahanReport
{
    public int PlacedCount;
    public int UnplacedCount;
    public double Coverage;       // fraction in [0,1]
    public double WastedArea;     // model units²
    public double MeanOverlap;    // model units (should be near 0)
}

/// <summary>Mesh-diagnostics report.</summary>
public sealed class MeshDiagnosticsReport : FrahanReport
{
    public int VertexCount;
    public int FaceCount;
    public int NakedEdges;
    public int DuplicateFaces;
    public int NonManifoldEdges;
    public double BoundingBoxDiagonal;
    public bool Watertight;
}

/// <summary>Chart-flatness report (Surface chart UV quality).</summary>
public sealed class ChartFlatnessReport : FrahanReport
{
    public double MeanAngleDistortion;
    public double MaxAngleDistortion;
    public double MeanAreaDistortion;
    public double MaxAreaDistortion;
    public int FaceCount;
}

/// <summary>Fabrication-prep report (BlockCutOpt + Stone-Aware Cut Export pipeline).</summary>
public sealed class FabricationPrepReport : FrahanReport
{
    public int StoneCount;
    public int CutCount;
    public double TotalCutLengthMm;
    public double TotalCutSurfaceMm2;
    public double Yield;          // fraction in [0,1]
    public double WasteVolumeMm3;
}

/// <summary>BlockCutOpt-inspector report.</summary>
public sealed class BlockCutOptReport : FrahanReport
{
    public int CandidateCount;
    public int ParetoFrontSize;
    public double BestYield;
    public double BestCarvingCost;
    public double BestCuttingSurface;
    public double BestBlockValue;
}
