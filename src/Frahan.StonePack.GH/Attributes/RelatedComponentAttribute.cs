#nullable disable
using System;

namespace Frahan.GH.Attributes;

// =============================================================================
// RelatedComponentAttribute -- declarative pointer from a research / diagnostic
// component (typically Lab subcategory) to its production sibling(s). Apply to
// GH_Component subclasses to surface "if you want the production path, see X"
// linkage in the canvas info pane + via `_FrahanWhichAlgorithm` reflection.
//
// Use case: the Lab subcategory consolidated 26 research/inspector components
// in the 2026-05-22 Stage 1 ribbon migration. Without explicit cross-refs to
// Quarry / Masonry / Mesh production components, Lab becomes a dead-end. This
// attribute records the inbound on-ramp from research to production.
//
// Multiple [RelatedComponent(...)] on one class are allowed. The order of
// application is the order of preferred-direction (most-related first).
//
// Example:
//   [RelatedComponent("Frahan > Quarry > BlockCutOpt Solve",
//                     Reason = "production solver; this inspector visualises one stage")]
//   public sealed class BlockCutOptInspectorComponent : GH_Component { ... }
// =============================================================================

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RelatedComponentAttribute : Attribute
{
    public RelatedComponentAttribute(string ribbonPath)
    {
        if (string.IsNullOrWhiteSpace(ribbonPath))
            throw new ArgumentException("ribbonPath required", nameof(ribbonPath));
        RibbonPath = ribbonPath;
    }

    /// <summary>Ribbon path of the related component, e.g. "Frahan > Quarry > BlockCutOpt Solve".</summary>
    public string RibbonPath { get; }

    /// <summary>Optional 1-line reason for the relationship. Why use the related instead of this?</summary>
    public string Reason { get; set; }

    /// <summary>Optional component GUID of the related component (for stable lookup if NickNames drift).</summary>
    public string ComponentGuid { get; set; }
}
