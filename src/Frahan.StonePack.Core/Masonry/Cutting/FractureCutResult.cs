#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Cutting;

/// <summary>
/// Output of one <see cref="FractureCutter.Cut"/> operation: resulting
/// slab pieces plus the outcome tag that describes whether the fracture
/// polygon spanned, missed, or only partially covered the slab.
/// </summary>
public sealed class FractureCutResult
{
    public FractureCutResult(IReadOnlyList<Slab> slabs, FractureCutOutcome outcome)
    {
        Slabs = slabs ?? throw new ArgumentNullException(nameof(slabs));
        Outcome = outcome;
    }

    public IReadOnlyList<Slab> Slabs { get; }
    public FractureCutOutcome Outcome { get; }

    public int Count => Slabs.Count;

    public override string ToString() =>
        $"FractureCutResult(outcome={Outcome}, n={Count})";
}
