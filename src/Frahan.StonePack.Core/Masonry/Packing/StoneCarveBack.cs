#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Interfaces;

namespace Frahan.Masonry.Packing;

// =============================================================================
// StoneCarveBack — P4c of EVOLUTION_PLAN_MASONRY.md (2026-06-10): the EXACT
// fabrication step behind the Lambda assignment.
//
// Cyclopean anti-nesting (Clifford & McGee 2018): the placed stone OVERLAPS
// its target cell, then everything outside the cell is CARVED BACK — "the
// algorithm doesn't minimise the space between parts, it removes it entirely,
// displacing the concept of waste to the amount of material carved from each
// part." Operationally per assigned placement:
//
//     carved stone  =  stone(placed)  ∩  cell        (boolean intersection)
//     exact λ_i     =  1 − vol(∩) / vol(stone)        (carved-away fraction)
//     exact gap_i   =  1 − vol(∩) / vol(cell)         (unfilled cell volume)
//     exact Λ       =  Σ λ_i·vol(stone_i) / Σ vol(stone_i)
//
// Booleans run through CgalMeshBoolean: the native CGAL kernel when the shim
// is loaded (inside Rhino), the managed BSP fallback headless — both are
// volume-validated in the battery. These EXACT numbers replace the voxel
// estimates of StoneCellAssignment; the carved meshes are the cut geometry
// the wire saw / mill receives. Pure managed entry; no Rhino dependency.
// =============================================================================

/// <summary>Result of <see cref="StoneCarveBack.Carve"/>.</summary>
public sealed class CarveBackResult
{
    public CarveBackResult(IReadOnlyList<MeshSnapshot> carvedStones,
                           IReadOnlyList<double> carveRatios, IReadOnlyList<double> gapRatios,
                           double impositionIndex, double meanGapRatio, string backend)
    {
        CarvedStones = carvedStones; CarveRatios = carveRatios; GapRatios = gapRatios;
        ImpositionIndex = impositionIndex; MeanGapRatio = meanGapRatio; Backend = backend;
    }
    /// <summary>stone ∩ cell per placement, in placement order (null where the boolean failed).</summary>
    public IReadOnlyList<MeshSnapshot> CarvedStones { get; }
    /// <summary>Exact λ_i = 1 − vol(∩)/vol(stone).</summary>
    public IReadOnlyList<double> CarveRatios { get; }
    /// <summary>Exact gap_i = 1 − vol(∩)/vol(cell).</summary>
    public IReadOnlyList<double> GapRatios { get; }
    /// <summary>Exact Λ (volume-weighted carve fraction).</summary>
    public double ImpositionIndex { get; }
    public double MeanGapRatio { get; }
    /// <summary>"cgal" (native) or "managed-bsp" (fallback), from the first boolean.</summary>
    public string Backend { get; }
}

/// <summary>
/// Exact Cyclopean carve-back: boolean-intersect each placed stone with its
/// cell, returning the cut geometry and the exact λ / Λ / gap numbers.
/// </summary>
public static class StoneCarveBack
{
    public static CarveBackResult Carve(
        IReadOnlyList<IReadOnlyList<double>> stoneCoords,
        IReadOnlyList<IReadOnlyList<int>> stoneTris,
        IReadOnlyList<IReadOnlyList<double>> cellCoords,
        IReadOnlyList<IReadOnlyList<int>> cellTris,
        IReadOnlyList<StonePlacement> placements)
    {
        if (stoneCoords == null) throw new ArgumentNullException(nameof(stoneCoords));
        if (stoneTris == null) throw new ArgumentNullException(nameof(stoneTris));
        if (cellCoords == null) throw new ArgumentNullException(nameof(cellCoords));
        if (cellTris == null) throw new ArgumentNullException(nameof(cellTris));
        if (placements == null) throw new ArgumentNullException(nameof(placements));

        var carved = new List<MeshSnapshot>(placements.Count);
        var carveRatios = new List<double>(placements.Count);
        var gapRatios = new List<double>(placements.Count);
        string backend = null;
        double carvedVol = 0, stoneVolSum = 0, gapSum = 0;
        int gaps = 0;

        foreach (var pl in placements)
        {
            var sc = stoneCoords[pl.StoneIndex];
            var st = stoneTris[pl.StoneIndex];
            var placedCoords = ApplyTransform(sc, pl.Transform);
            var stoneSnap = new MeshSnapshot(placedCoords, st);
            var cellSnap = new MeshSnapshot(cellCoords[pl.CellIndex], cellTris[pl.CellIndex]);

            double vStone = Volume(placedCoords, st);
            double vCell = Volume(cellCoords[pl.CellIndex], cellTris[pl.CellIndex]);

            MeshSnapshot inter = null;
            try
            {
                inter = CgalMeshBoolean.Intersection(stoneSnap, cellSnap, out var be);
                if (backend == null) backend = be.ToString();
            }
            catch
            {
                // boolean failure: record worst-case metrics, keep going
            }

            double vInter = inter != null ? Volume(inter.VertexCoordsXyz, inter.TriangleIndices) : 0;
            vInter = Math.Min(vInter, Math.Min(vStone, vCell)); // numerical guard
            double lam = vStone > 1e-12 ? 1.0 - vInter / vStone : 1.0;
            double gap = vCell > 1e-12 ? 1.0 - vInter / vCell : 1.0;

            carved.Add(inter);
            carveRatios.Add(lam);
            gapRatios.Add(gap);
            carvedVol += lam * vStone;
            stoneVolSum += vStone;
            gapSum += gap; gaps++;
        }

        double lambda = stoneVolSum > 1e-12 ? carvedVol / stoneVolSum : 0;
        double meanGap = gaps > 0 ? gapSum / gaps : 0;
        return new CarveBackResult(carved, carveRatios, gapRatios, lambda, meanGap,
                                   backend ?? "none");
    }

    private static IReadOnlyList<double> ApplyTransform(IReadOnlyList<double> coords, double[] t)
    {
        int nv = coords.Count / 3;
        var outC = new double[coords.Count];
        for (int i = 0; i < nv; i++)
        {
            double x = coords[i * 3], y = coords[i * 3 + 1], z = coords[i * 3 + 2];
            outC[i * 3] = t[0] * x + t[1] * y + t[2] * z + t[3];
            outC[i * 3 + 1] = t[4] * x + t[5] * y + t[6] * z + t[7];
            outC[i * 3 + 2] = t[8] * x + t[9] * y + t[10] * z + t[11];
        }
        return outC;
    }

    private static double Volume(IReadOnlyList<double> coords, IReadOnlyList<int> tris)
    {
        double v6 = 0;
        for (int t = 0; t < tris.Count; t += 3)
        {
            int a = tris[t] * 3, b = tris[t + 1] * 3, c = tris[t + 2] * 3;
            v6 += coords[a] * (coords[b + 1] * coords[c + 2] - coords[b + 2] * coords[c + 1])
                - coords[a + 1] * (coords[b] * coords[c + 2] - coords[b + 2] * coords[c])
                + coords[a + 2] * (coords[b] * coords[c + 1] - coords[b + 1] * coords[c]);
        }
        return Math.Abs(v6) / 6.0;
    }
}
