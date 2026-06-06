using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.EdgeMatching
{
    /// <summary>
    /// Snapshot of the assembly during beam search. Cloned cheaply on
    /// each placement attempt; the search throws failed clones away
    /// rather than rolling back panel state, which keeps Panel objects
    /// pure for the duration of Solve.
    /// </summary>
    public sealed class AssemblyState
    {
        public List<Panel> PlacedPanels { get; } = new List<Panel>();
        public Dictionary<string, Transform> AppliedTransforms { get; } = new Dictionary<string, Transform>();
        public double TotalResidual { get; set; }
        public List<MatchResult> History { get; } = new List<MatchResult>();

        /// <summary>
        /// R2 (B6 edge-exclusivity). Identities "PanelId#Index" of placed-panel
        /// boundary segments already CONSUMED by an accepted placement. Only
        /// populated when <see cref="AssemblyOptions.EdgeExclusivity"/> is on; a
        /// later candidate cannot match a consumed segment, so two pieces cannot
        /// snap to the same placed edge. Empty (and never consulted) by default,
        /// so the default beam path is unchanged.
        /// </summary>
        public HashSet<string> ConsumedSegments { get; } = new HashSet<string>(StringComparer.Ordinal);

        public AssemblyState Clone()
        {
            var s = new AssemblyState { TotalResidual = TotalResidual };
            s.PlacedPanels.AddRange(PlacedPanels);
            foreach (var kv in AppliedTransforms) s.AppliedTransforms[kv.Key] = kv.Value;
            s.History.AddRange(History);
            foreach (var c in ConsumedSegments) s.ConsumedSegments.Add(c);
            return s;
        }
    }
}
