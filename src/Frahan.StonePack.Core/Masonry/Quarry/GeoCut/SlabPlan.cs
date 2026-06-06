#nullable disable
using System;

namespace Frahan.Masonry.Quarry.GeoCut;

// =============================================================================
// SlabPlan -- one candidate slabbing of a single block.
//
// Spec: wiki/specs/09_frahan_geocut_spec.md section 5.
//
// "Slabbing" here means: take one block (Slab in the Frahan data model), and
// pick a slicing direction (unit normal n) and a slab thickness t. The block
// is partitioned into floor((extent_n - kerf) / (t + kerf)) parallel slabs.
//
// v1 scope: axis-aligned slicing only (one of +X, +Y, +Z) and one thickness
// per plan. The optimiser enumerates a small candidate set; Pareto / oblique
// search is a follow-on.
// =============================================================================

public enum SlabAxis
{
    X = 0,
    Y = 1,
    Z = 2,
}

public sealed class SlabPlan
{
    public SlabPlan(SlabAxis axis, double thicknessMetres, double kerfMetres)
    {
        if (thicknessMetres <= 0) throw new ArgumentOutOfRangeException(nameof(thicknessMetres), "> 0");
        if (kerfMetres < 0) throw new ArgumentOutOfRangeException(nameof(kerfMetres), ">= 0");
        Axis = axis;
        ThicknessMetres = thicknessMetres;
        KerfMetres = kerfMetres;
    }

    public SlabAxis Axis { get; }
    public double ThicknessMetres { get; }
    public double KerfMetres { get; }

    public override string ToString() =>
        $"SlabPlan(axis={Axis}, t={ThicknessMetres:0.###} m, kerf={KerfMetres:0.###} m)";
}
