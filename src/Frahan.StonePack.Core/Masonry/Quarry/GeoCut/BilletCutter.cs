#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;

namespace Frahan.Masonry.Quarry.GeoCut;

// =============================================================================
// BilletCutter -- spec 09 section 2: sub-divide each slab into billets along
// a secondary axis at a target billet width.
//
// Algorithm: given a Slab and (axis, billetWidth, kerf), emit
// FracturePlanes orthogonal to the axis spaced by (billetWidth + kerf), then
// SlabCutter.Cut the slab.
// =============================================================================

public sealed class BilletPlan
{
    public BilletPlan(SlabAxis axis, double billetWidthMetres, double kerfMetres)
    {
        if (billetWidthMetres <= 0) throw new ArgumentOutOfRangeException(nameof(billetWidthMetres), "> 0");
        if (kerfMetres < 0) throw new ArgumentOutOfRangeException(nameof(kerfMetres), ">= 0");
        Axis = axis;
        BilletWidthMetres = billetWidthMetres;
        KerfMetres = kerfMetres;
    }

    public SlabAxis Axis { get; }
    public double BilletWidthMetres { get; }
    public double KerfMetres { get; }

    public override string ToString() =>
        $"BilletPlan(axis={Axis}, w={BilletWidthMetres:0.###} m, kerf={KerfMetres:0.###} m)";
}

public static class BilletCutter
{
    public static SlabCutResult Cut(Slab slab, BilletPlan plan)
    {
        if (slab == null) throw new ArgumentNullException(nameof(slab));
        if (plan == null) throw new ArgumentNullException(nameof(plan));

        ComputeAabb(slab, out double xMin, out double yMin, out double zMin,
                          out double xMax, out double yMax, out double zMax);
        double start, end;
        double nx = 0, ny = 0, nz = 0;
        switch (plan.Axis)
        {
            case SlabAxis.X: start = xMin; end = xMax; nx = 1; break;
            case SlabAxis.Y: start = yMin; end = yMax; ny = 1; break;
            case SlabAxis.Z: start = zMin; end = zMax; nz = 1; break;
            default: throw new NotSupportedException(plan.Axis.ToString());
        }
        double pitch = plan.BilletWidthMetres + plan.KerfMetres;
        var planes = new List<FracturePlane>();
        double offset = start + plan.BilletWidthMetres;
        while (offset + plan.KerfMetres < end)
        {
            double px = (plan.Axis == SlabAxis.X) ? offset : 0.5 * (xMin + xMax);
            double py = (plan.Axis == SlabAxis.Y) ? offset : 0.5 * (yMin + yMax);
            double pz = (plan.Axis == SlabAxis.Z) ? offset : 0.5 * (zMin + zMax);
            planes.Add(new FracturePlane(px, py, pz, nx, ny, nz));
            offset += pitch;
        }
        return SlabCutter.Cut(slab, planes);
    }

    public static IReadOnlyList<Slab> CutAll(IReadOnlyList<Slab> slabs, BilletPlan plan)
    {
        if (slabs == null) throw new ArgumentNullException(nameof(slabs));
        if (plan == null) throw new ArgumentNullException(nameof(plan));

        var output = new List<Slab>(slabs.Count);
        for (int i = 0; i < slabs.Count; i++)
        {
            var result = Cut(slabs[i], plan);
            for (int k = 0; k < result.Slabs.Count; k++) output.Add(result.Slabs[k]);
        }
        return output;
    }

    private static void ComputeAabb(
        Slab s,
        out double xMin, out double yMin, out double zMin,
        out double xMax, out double yMax, out double zMax)
    {
        var v = s.VertexCoordsXyz;
        xMin = double.PositiveInfinity; yMin = double.PositiveInfinity; zMin = double.PositiveInfinity;
        xMax = double.NegativeInfinity; yMax = double.NegativeInfinity; zMax = double.NegativeInfinity;
        int n = s.VertexCount;
        for (int i = 0; i < n; i++)
        {
            double x = v[3 * i + 0], y = v[3 * i + 1], z = v[3 * i + 2];
            if (x < xMin) xMin = x; if (x > xMax) xMax = x;
            if (y < yMin) yMin = y; if (y > yMax) yMax = y;
            if (z < zMin) zMin = z; if (z > zMax) zMax = z;
        }
    }
}
