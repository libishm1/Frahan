#nullable disable
using Rhino.Geometry;

namespace Frahan.Core.Quarry;

// =============================================================================
// QuarryBlock — typed record carrying a scanned-block's geometry + metadata
// through the Frahan pipeline. Produced by ScanToBlockInventoryComponent
// (Frahan > Quarry > Scan to Block Inventory) and consumed by the downstream
// nesting chain (BlockPackTree, Pack3DIrregularContainer, SlabYieldOptimizer,
// BlockCandidateGenerator) via an optional adapter input.
//
// Mirrors the existing StoneCutMetadata pattern: simple public fields, no
// behaviour. Reference type (Mesh, Plane, etc.) so cheap to flow on GH wires.
//
// Spec: v1_consolidated_plan.md §1.4 (2026-05-30, frahan-v1-plan-2026-05-30).
// =============================================================================

public sealed class QuarryBlock
{
    public Mesh Bounds;          // oriented bounding-box mesh (used by downstream as the "container")
    public Mesh UsableVolume;    // interior mesh after Usable Inset (== Bounds when Inset=0)
    public Plane Frame;          // origin + axes (X = longest, Y = next, Z = thinnest)
    public Vector3d Dimensions;  // longest, next, thinnest (model units)
    public double Volume;        // m³ (model units cubed)
    public string Label;         // provenance string carried through pipeline
    public string Method;        // "OBB" | "InscribedAABB" | "ConvexHull"

    public QuarryBlock() { }

    public QuarryBlock(Mesh bounds, Mesh usableVolume, Plane frame, Vector3d dims,
                       double volume, string label, string method)
    {
        Bounds = bounds;
        UsableVolume = usableVolume;
        Frame = frame;
        Dimensions = dims;
        Volume = volume;
        Label = label ?? "";
        Method = method ?? "";
    }
}
