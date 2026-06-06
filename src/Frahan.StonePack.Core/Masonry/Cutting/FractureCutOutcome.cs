#nullable disable

namespace Frahan.Masonry.Cutting;

/// <summary>
/// Result classification of one <see cref="FractureCutter"/> operation.
/// </summary>
public enum FractureCutOutcome
{
    /// <summary>Plane does not intersect the slab — passthrough, one piece.</summary>
    Miss,

    /// <summary>Fracture polygon contains the entire slab cross-section — full cut, two pieces.</summary>
    Spans,

    /// <summary>
    /// Fracture polygon partially overlaps the slab cross-section. The slab
    /// would be cut along a non-convex boundary; Phase E.2 returns the input
    /// slab as a single passthrough piece and tags the outcome as
    /// <c>Partial</c>. Callers can opt in to "extend the partial fracture
    /// to an infinite plane" via <see cref="FractureCutOptions.ExtendPartialToInfinitePlane"/>;
    /// in that case the outcome is reported as <see cref="PartialExtended"/> instead.
    /// </summary>
    Partial,

    /// <summary>
    /// Fracture polygon partially overlapped the slab, and the caller asked
    /// to treat that case as an infinite-plane cut. The output is two
    /// pieces (same as <see cref="Spans"/>) but the original polygon's
    /// finite extent did not actually cover the cross-section.
    /// </summary>
    PartialExtended,
}
