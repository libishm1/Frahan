#nullable disable
using System;

namespace Frahan.GH.Attributes;

// =============================================================================
// AlgorithmAttribute -- declarative algorithm + citation tag for Frahan GH
// components. Apply to GH_Component subclasses to record which Core algorithm
// the component wraps and the published paper that algorithm is from.
//
// Multiple [Algorithm(...)] attributes per component are allowed; a component
// that wraps a pipeline of N algorithms gets N tags. The order of application
// is the execution order of the algorithm in the component.
//
// Discoverable via reflection. The companion `_FrahanWhichAlgorithm` Rhino
// command (planned) reads selected canvas component(s), looks up their
// attributes, and prints the citation chain to the command line.
//
// Design choice: attribute over inline Description suffix. Reasons:
//   * structured (Name, Citation, Doi, WikiPath are typed fields)
//   * supports multiple algorithms per component
//   * reflection-aggregatable into reports
//   * does not clutter the user-facing Description hover text
// Trade-off: invisible in the GH UI by default. Mitigation: trailing
// "[refs: see _FrahanWhichAlgorithm]" suffix in Description text where useful.
// =============================================================================

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class AlgorithmAttribute : Attribute
{
    public AlgorithmAttribute(string name, string citation)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Citation = citation ?? throw new ArgumentNullException(nameof(citation));
    }

    /// <summary>Short algorithm name, e.g. "BFF flatten" or "Trencadis greedy pack".</summary>
    public string Name { get; }

    /// <summary>Human-readable citation string, e.g. "Sawhney &amp; Crane 2017, ACM TOG 36(4):109".</summary>
    public string Citation { get; }

    /// <summary>Optional DOI (without the https://doi.org/ prefix).</summary>
    public string Doi { get; set; }

    /// <summary>Optional path inside the wiki/ tree pointing at the deeper writeup.</summary>
    public string WikiPath { get; set; }

    /// <summary>Optional note: "Frahan-original" if no peer-reviewed source, or a short caveat.</summary>
    public string Note { get; set; }
}
