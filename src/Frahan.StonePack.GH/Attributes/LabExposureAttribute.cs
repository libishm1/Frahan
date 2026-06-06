#nullable disable
using System;

namespace Frahan.GH.Attributes;

// =============================================================================
// LabExposureAttribute -- class-level opt-in marker that declares a Grasshopper
// component is *eligible* for Lab gating. Built 2026-05-30 as step 6 of the
// v1.0 execution order (v1_consolidated_plan.md §4.3 item 4).
//
// The attribute itself does NOT hide a component. LabConfig (sibling file) is
// the central allow-list and the source of truth. The attribute lets a
// component self-declare that Lab-gating it is reasonable; whether it is
// actually gated is decided by LabConfig.IsLabGated(guid).
//
// Preservation contract (v1_consolidated_plan §0.1): Lab-gating is a
// visibility flag ONLY. Source / icon / GUID / csproj entry of a Lab-gated
// component are never deleted. Removing a GUID from LabConfig._labGated
// returns the component to its default ribbon. This attribute is reversible
// in the same way -- removing it does not break the component, only its
// self-declaration that it could be Lab-gated.
// =============================================================================

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class LabExposureAttribute : Attribute
{
    public string Reason { get; }

    public LabExposureAttribute(string reason)
    {
        Reason = reason;
    }
}
