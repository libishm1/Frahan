#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// RecoveryCascade -- recursive multi-scale crack-aware recovery.
//
// At each scale (coarse -> fine) BlockCutOpt is run on the tested region; the
// non-intersected blocks are RECOVERED (the kept set), and every block a
// fracture crosses (the cracked set) is fed back into the SAME engine at the
// next finer scale, cutting AROUND the fracture, until the remnant falls below
// the smallest marketable size (residual waste). This is the unified
// reject-coarse / recover-fine cascade: it recovers value from blocks the
// single-scale packer would discard, and reduces to BlockCutOpt 2020 exactly
// when the scale list has length 1.
//
// Reduces to BlockCutOpt 2020: with a single ScaleSpec, Run recovers exactly
// the non-intersected blocks BlockCutOptSolver.Solve finds (same winning pose,
// same intersection predicate), so the cascade is a faithful superset.
//
// Recursion equation (value recovered from a tested region R at scale s):
//   W(R, s) = sum_{b in kept(R, s)}   RMV_s(b)
//           + sum_{b in cracked(R, s)} W(AABB(b), s+1)     [if s+1 < S and Vol >= Vmin]
//                                      residual(b)          [otherwise]
// with kept/cracked partitioning the winning grid by !bvh.AnyTriangleIntersects.
//
// SLM/ROSES grounding: Yarahmadi 2018 (conditional two-scale), Cherri 2009 CSUL
// usable-leftover threshold, Gilmore-Gomory 1965 staged guillotine, wood
// rough-mill defect-aware cut-up (Hahn 1968 / Afsharian 2014). The unified
// 3D recursive reject-recover cascade is the novel composition.
// =============================================================================

public static class RecoveryCascade
{
    /// <summary>
    /// Run the multi-scale recovery cascade over a tested region given a fracture
    /// mesh and an ordered (coarse -> fine) list of scales.
    /// </summary>
    public static CascadeResult Run(
        BoundingBox3 testedArea,
        PlyMesh fractures,
        IReadOnlyList<ScaleSpec> scales)
    {
        if (testedArea == null) throw new ArgumentNullException(nameof(testedArea));
        if (fractures == null) throw new ArgumentNullException(nameof(fractures));
        if (scales == null || scales.Count == 0) throw new ArgumentException("scales must be non-empty", nameof(scales));

        // one shared immutable BVH for the crack predicate (the OBB test ignores triangles outside R)
        var bvh = TriangleAabbBvh.Build(fractures);

        var tiers = new CascadeTier[scales.Count];
        for (int i = 0; i < scales.Count; i++) tiers[i] = new CascadeTier(i, scales[i].Label);

        var acc = new Acc();
        Recurse(testedArea, 0, fractures, bvh, scales, tiers, acc);

        double testedVol = testedArea.SizeX * testedArea.SizeY * testedArea.SizeZ;
        return new CascadeResult(tiers, acc.CrackedRouted, acc.ResidualCount, acc.ResidualVol, testedVol);
    }

    // mutable accumulator threaded through the recursion (avoids many ref params)
    private sealed class Acc { public int CrackedRouted; public int ResidualCount; public double ResidualVol; }

    private static void Recurse(
        BoundingBox3 region, int s, PlyMesh fractures, TriangleAabbBvh bvh,
        IReadOnlyList<ScaleSpec> scales, CascadeTier[] tiers, Acc acc)
    {
        if (s >= scales.Count) return; // depth cap = number of scales (no unbounded recursion)
        var spec = scales[s];
        var opts = spec.Options;

        // choose the winning pose for this scale (own internal BVH; deterministic strict-greater argmax)
        var result = BlockCutOptSolver.Solve(region, fractures, opts, parallel: true);

        // regenerate the winning grid and partition it into kept / cracked with the shared BVH
        var grid = CuttingGrid.GenerateTilted(
            region, opts.BlockSizeX, opts.BlockSizeY, opts.BlockSizeZ, opts.Kerf,
            result.BestPsiRad, result.BestThetaRad, result.BestPhiRad, result.BestDx, result.BestDy);

        double blkVol = spec.BlockVolume;
        double kerfPerBlock = Math.Max(0.0, spec.InflatedVolume - spec.BlockVolume);
        bool finerExists = s + 1 < scales.Count;
        double finerVmin = finerExists ? scales[s + 1].MinMarketableVolumeM3 : 0.0;

        for (int i = 0; i < grid.Count; i++)
        {
            var obb = grid[i];
            if (!bvh.AnyTriangleIntersects(in obb))
            {
                // RECOVER: a clean marketable block at this scale
                tiers[s].RecoveredCount++;
                tiers[s].RecoveredVolumeM3 += blkVol;
                tiers[s].RecoveredValue += spec.Value.RmvPerBlock;
                tiers[s].CutSurfaceAreaM2 += BlockValueModel.SurfaceArea(in obb);
                tiers[s].KerfVolumeM3 += kerfPerBlock;
            }
            else
            {
                // CRACKED: route to the finer scale (cut around the fracture) or scrap
                var childAabb = AabbOf(in obb);
                double obbVol = childAabb.SizeX * childAabb.SizeY * childAabb.SizeZ;
                if (finerExists && obbVol >= finerVmin)
                {
                    acc.CrackedRouted++;
                    Recurse(childAabb, s + 1, fractures, bvh, scales, tiers, acc);
                }
                else
                {
                    acc.ResidualCount++;
                    acc.ResidualVol += blkVol;
                }
            }
        }
    }

    // Axis-aligned bound of an oriented block (its 8 corners). Exact for psi-only (axis-aligned) blocks.
    private static BoundingBox3 AabbOf(in OrientedBlock o)
    {
        double minx = double.PositiveInfinity, miny = double.PositiveInfinity, minz = double.PositiveInfinity;
        double maxx = double.NegativeInfinity, maxy = double.NegativeInfinity, maxz = double.NegativeInfinity;
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    double x = o.CenterX + sx * o.HalfX * o.UX + sy * o.HalfY * o.VX + sz * o.HalfZ * o.WX;
                    double y = o.CenterY + sx * o.HalfX * o.UY + sy * o.HalfY * o.VY + sz * o.HalfZ * o.WY;
                    double z = o.CenterZ + sx * o.HalfX * o.UZ + sy * o.HalfY * o.VZ + sz * o.HalfZ * o.WZ;
                    if (x < minx) minx = x; if (x > maxx) maxx = x;
                    if (y < miny) miny = y; if (y > maxy) maxy = y;
                    if (z < minz) minz = z; if (z > maxz) maxz = z;
                }
        // guard against a degenerate flat axis (keep a strictly-positive extent for BoundingBox3)
        const double tiny = 1e-6;
        if (maxx - minx < tiny) maxx = minx + tiny;
        if (maxy - miny < tiny) maxy = miny + tiny;
        if (maxz - minz < tiny) maxz = minz + tiny;
        return new BoundingBox3(minx, miny, minz, maxx, maxy, maxz);
    }
}
