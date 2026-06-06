#nullable disable
using System;
using System.Collections.Generic;
using Grasshopper.Kernel;

namespace Frahan.GH.Attributes;

/// <summary>
/// Central Lab-gating allow-list. Per v1_consolidated_plan §0.1 (Preservation):
/// Lab-gating is a visibility flag ONLY. Source / icon / GUID / csproj entry
/// of a Lab-gated component are never deleted. Removing a GUID from this list
/// returns the component to its default ribbon (reversible).
/// </summary>
public static class LabConfig
{
    // GUIDs gated to Lab.
    //
    // Reverted 2026-05-30 per HITL: "keep only truly miscellaneous components in
    // Lab." The 11 GUIDs originally added in step 9 (Advanced Quarry Decompose,
    // BlockCutOpt Ingestion, Quarry Bridge, Quarry Ingestion) all turned out to
    // be real, named algorithms / production paths -- not "miscellaneous." Per
    // the H-series verdict, "they are essentially different algorithms; if we
    // merge them we will get a zombie component" -- the same logic applies to
    // hiding them: distinct algorithms with a clear subcategory home stay on
    // the default ribbon. They were preserved per §0.1 with their original
    // exposure (secondary / tertiary / quarternary / primary) on each class.
    //
    // The Lab subcategory itself stays reserved for genuinely miscellaneous /
    // scratchpad / experimental future components that need an enabling
    // configuration to use. None qualify in v1.0. Add specific GUIDs here with
    // an explicit per-row justification when they appear.
    private static readonly HashSet<Guid> _labGated = new HashSet<Guid>
    {
        // (intentionally empty -- see comment above)
    };

    /// <summary>True if the component GUID is Lab-gated.</summary>
    public static bool IsLabGated(Guid guid) => _labGated.Contains(guid);

    /// <summary>The Lab subcategory string used everywhere.</summary>
    public const string LabSubcategory = "Lab";

    /// <summary>Reads the effective Exposure for a component, honoring Lab gating.</summary>
    public static GH_Exposure EffectiveExposure(Guid guid, GH_Exposure declared) =>
        IsLabGated(guid) ? GH_Exposure.hidden : declared;

    /// <summary>Reads the effective subcategory for a component, honoring Lab gating.</summary>
    public static string EffectiveSubcategory(Guid guid, string declared) =>
        IsLabGated(guid) ? LabSubcategory : declared;

    /// <summary>Enumerate all Lab-gated GUIDs (for the round-trip test).</summary>
    public static IReadOnlyCollection<Guid> LabGatedGuids => _labGated;
}
