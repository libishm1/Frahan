#nullable disable
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Core.Voussoir;

// =============================================================================
// VoussoirAssembly — the whole-vault-or-arch typed record.
//
// Carries an ordered list of VoussoirRecord plus the assembly's structural
// scaffolding (thrust curve, ground anchors, adjacency graph). Produced by
// VoussoirIngestComponent and consumed by VoussoirStoneMatcherComponent +
// VoussoirPackIntoBlockComponent + downstream Build Order Sequencer.
// =============================================================================

public sealed class VoussoirAssembly
{
    /// <summary>Ordered list of voussoirs. Order may carry assembly
    /// semantics (springer-first; keystone-last) when set by the upstream
    /// designer.</summary>
    public IReadOnlyList<VoussoirRecord> Voussoirs;

    /// <summary>Optional funicular thrust curve (the form-found compression-
    /// only line per Block Research Group TNA / Rippmann-Block 2011 Digital
    /// Stereotomy). When supplied, drives the LoadAxis on each voussoir and
    /// the Build Order Sequencer's stability gate.</summary>
    public Curve ThrustCurve;

    /// <summary>Adjacency pairs: (i, j) means voussoir i and voussoir j
    /// share a joint face (bed-to-head). Detected by shared-face-area in
    /// VoussoirIngestComponent. The graph drives the install DAG (Kim 2024
    /// polygonal masonry sequence).</summary>
    public IReadOnlyList<(int, int)> AdjacencyPairs;

    /// <summary>Optional ground-anchor indices (the springer / abutment
    /// voussoirs that ARE fixed). When wiring into Build Order Sequencer,
    /// these are the starting points of the install DAG.</summary>
    public IReadOnlyList<int> GroundAnchorIndices;

    /// <summary>Provenance string from the upstream tool (e.g. "Voussoir
    /// plugin v2.3 output" or "Frahan Stereotomic Vault Mode").</summary>
    public string Provenance;

    public VoussoirAssembly()
    {
        Voussoirs = new List<VoussoirRecord>();
        AdjacencyPairs = new List<(int, int)>();
        GroundAnchorIndices = new List<int>();
        Provenance = "";
    }
}
