#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// AmrrPlanner -- Shao, Liu, Gao 2022 in-block plane-sequence cutting.
//
// Iteratively cut a starting CPH (e.g. an OBB block) by planes tangent to a
// target convex shape (a bounding sphere in v1), maximising the average
// material-removal-rate (AMRR = total material removed / total cutting time).
//
// Algorithm (Shao 2022 sections 2.4-2.6):
//   1. Initialise Q := blank box (CPH).
//   2. Repeat:
//        a. Find the vertex Pv of Q that is farthest from the target T.
//        b. Cut Q by the plane through Pv tangent to T (oriented so that T
//           stays in the kept half-space).
//        c. Record (cut plane, cutting span W, cutting path length L, time tau).
//        d. Update Q := Q clipped by that plane.
//      until the remaining "outside" volume is below
//      BlockCutOptTolerances.AmrrConvergenceFraction of the initial blank
//      volume, or no vertex of Q lies outside T.
//
// Units: all inputs are in METRES (the Frahan default). The Shao paper uses
// mm; convert on entry via BlockCutOptTolerances.MmToMetres.
//
// Phase 9 of the synthesis roadmap; couples quarry-scale BlockCutOpt to
// in-block secondary cutting.
// =============================================================================

public sealed class AmrrCutStep
{
    public AmrrCutStep(
        int index,
        double planePx, double planePy, double planePz,
        double planeNx, double planeNy, double planeNz,
        double cutAreaMetres2,
        double removalVolumeMetres3,
        double cuttingTimeMin)
    {
        Index = index;
        PlanePx = planePx; PlanePy = planePy; PlanePz = planePz;
        PlaneNx = planeNx; PlaneNy = planeNy; PlaneNz = planeNz;
        CutAreaMetres2 = cutAreaMetres2;
        RemovalVolumeMetres3 = removalVolumeMetres3;
        CuttingTimeMin = cuttingTimeMin;
    }

    public int Index { get; }
    public double PlanePx { get; } public double PlanePy { get; } public double PlanePz { get; }
    public double PlaneNx { get; } public double PlaneNy { get; } public double PlaneNz { get; }
    public double CutAreaMetres2 { get; }
    public double RemovalVolumeMetres3 { get; }
    public double CuttingTimeMin { get; }
    public double InstantMrr => CuttingTimeMin > 0 ? RemovalVolumeMetres3 / CuttingTimeMin : 0.0;

    public override string ToString() =>
        $"AmrrStep #{Index}: V_ri={RemovalVolumeMetres3 * 1e9:0.0} mm3, " +
        $"tau={CuttingTimeMin:0.000} min, MRR={InstantMrr * 1e9:0.0} mm3/min";
}

public sealed class AmrrPlanResult
{
    public AmrrPlanResult(
        IReadOnlyList<AmrrCutStep> steps,
        ConvexPolyhedron finalCph,
        double totalRemovalVolumeMetres3,
        double totalCuttingTimeMin,
        double materialRemovalPercent)
    {
        Steps = steps;
        FinalCph = finalCph;
        TotalRemovalVolumeMetres3 = totalRemovalVolumeMetres3;
        TotalCuttingTimeMin = totalCuttingTimeMin;
        MaterialRemovalPercent = materialRemovalPercent;
    }

    public IReadOnlyList<AmrrCutStep> Steps { get; }
    public ConvexPolyhedron FinalCph { get; }
    public double TotalRemovalVolumeMetres3 { get; }
    public double TotalCuttingTimeMin { get; }
    public double MaterialRemovalPercent { get; }
    public double Amrr => TotalCuttingTimeMin > 0 ? TotalRemovalVolumeMetres3 / TotalCuttingTimeMin : 0.0;

    public override string ToString() =>
        $"AmrrPlanResult(N_cuts={Steps.Count}, MRP={MaterialRemovalPercent:0.0}%, " +
        $"AMRR={Amrr * 1e9:0.0} mm3/min over {TotalCuttingTimeMin:0.00} min)";
}

public sealed class AmrrPlannerOptions
{
    /// <summary>Sawblade radius in metres (Shao uses mm; converted on construction).</summary>
    public double SawBladeRadiusMetres { get; set; } = BlockCutOptTolerances.MmToMetres(BlockCutOptTolerances.SawBladeRadiusMmDefault);

    /// <summary>Feeding speed in metres / minute.</summary>
    public double FeedSpeedMetresPerMin { get; set; } = BlockCutOptTolerances.MmToMetres(BlockCutOptTolerances.FeedSpeedMmPerMinDefault);

    /// <summary>Stop when remaining-volume-outside-target / blank-volume is below this fraction.</summary>
    public double ConvergenceFraction { get; set; } = BlockCutOptTolerances.AmrrConvergenceFraction;

    /// <summary>Safety cap on iteration count.</summary>
    public int MaxCuts { get; set; } = 100;
}

public static class AmrrPlanner
{
    /// <summary>
    /// Plan a sequence of plane cuts that reduces <paramref name="blank"/>
    /// toward an enclosing bounding sphere of radius <paramref name="targetSphereRadius"/>
    /// centred at <paramref name="targetCx, targetCy, targetCz"/>.
    /// </summary>
    public static AmrrPlanResult PlanBoundingSphere(
        ConvexPolyhedron blank,
        double targetCx, double targetCy, double targetCz,
        double targetSphereRadius,
        AmrrPlannerOptions options = null)
    {
        if (blank == null) throw new ArgumentNullException(nameof(blank));
        if (!(targetSphereRadius > 0)) throw new ArgumentOutOfRangeException(nameof(targetSphereRadius));
        options = options ?? new AmrrPlannerOptions();

        double initialVolume = blank.Volume();
        double targetVolume = (4.0 / 3.0) * Math.PI * targetSphereRadius * targetSphereRadius * targetSphereRadius;

        var steps = new List<AmrrCutStep>();
        var current = blank;
        double totalRemoval = 0.0;
        double totalTime = 0.0;

        for (int iter = 0; iter < options.MaxCuts; iter++)
        {
            // 1. Find the vertex farthest outside the sphere.
            int worstIdx = -1;
            double worstDist = 0.0;
            for (int i = 0; i < current.Vertices.Count; i++)
            {
                var p = current.Vertices[i];
                double dx = p.X - targetCx, dy = p.Y - targetCy, dz = p.Z - targetCz;
                double r = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                double outside = r - targetSphereRadius;
                if (outside > worstDist)
                {
                    worstDist = outside;
                    worstIdx = i;
                }
            }
            if (worstIdx < 0) break; // every vertex is inside the sphere

            var pv = current.Vertices[worstIdx];

            // 2. Cutting plane: tangent to the sphere along the line from
            //    centre to Pv. The tangent point is at distance R along that
            //    direction; the plane normal points from the centre through Pv.
            double dirX = pv.X - targetCx, dirY = pv.Y - targetCy, dirZ = pv.Z - targetCz;
            double dirLen = Math.Sqrt(dirX * dirX + dirY * dirY + dirZ * dirZ);
            if (dirLen < BlockCutOptTolerances.GeometricEps) break;
            double nx = dirX / dirLen, ny = dirY / dirLen, nz = dirZ / dirLen;
            double tangentPx = targetCx + nx * targetSphereRadius;
            double tangentPy = targetCy + ny * targetSphereRadius;
            double tangentPz = targetCz + nz * targetSphereRadius;

            // 3. Volume of the slice that would be removed.
            double volBefore = current.Volume();
            var clipped = current.ClipByHalfSpace(tangentPx, tangentPy, tangentPz, nx, ny, nz);
            double volAfter = clipped.Volume();
            double removed = volBefore - volAfter;
            if (removed <= BlockCutOptTolerances.GeometricEps) break;

            // 4. Cutting-time model (Shao 2022): cut the CPH against the
            //    current candidate plane via SharedEdgeSlicer (Minetto 2017
            //    I12) and use the section diameter as the saw path length.
            //    Falls back to the AABB-projection proxy when the section is
            //    degenerate (e.g. tangent grazing a single vertex).
            double pathLen = EstimateSectionDiameterViaSlicer(
                current, tangentPx, tangentPy, tangentPz, nx, ny, nz);
            if (pathLen <= BlockCutOptTolerances.GeometricEps)
            {
                pathLen = EstimateSectionDiameter(current, tangentPx, tangentPy, tangentPz, nx, ny, nz);
            }
            double timeMin = Math.Max(pathLen / Math.Max(options.FeedSpeedMetresPerMin, 1e-9), 1e-9);

            double cutAreaMetres2 = removed / Math.Max(pathLen, 1e-9);

            steps.Add(new AmrrCutStep(
                iter,
                tangentPx, tangentPy, tangentPz,
                nx, ny, nz,
                cutAreaMetres2,
                removed,
                timeMin));

            totalRemoval += removed;
            totalTime += timeMin;
            current = clipped;

            // 5. Convergence check
            double remainingOutside = volAfter - targetVolume;
            if (remainingOutside < initialVolume * options.ConvergenceFraction) break;
        }

        double mrp = (initialVolume > 0) ? 100.0 * totalRemoval / initialVolume : 0.0;
        return new AmrrPlanResult(steps, current, totalRemoval, totalTime, mrp);
    }

    private static double EstimateSectionDiameterViaSlicer(
        ConvexPolyhedron cph,
        double pX, double pY, double pZ,
        double nX, double nY, double nZ)
    {
        // signed offset of the plane along its normal
        double off = pX * nX + pY * nY + pZ * nZ;
        var mesh = cph.ToPlyMesh();
        var slices = SharedEdgeSlicer.Slice(mesh, nX, nY, nZ, new[] { off });
        if (slices.Count == 0) return 0.0;
        var c = slices[0];
        if (c.SegmentCount == 0) return 0.0;
        // diameter = max pairwise distance between segment endpoints
        double maxR2 = 0.0;
        var pts = c.SegmentEndpoints;
        for (int i = 0; i < pts.Count; i++)
        {
            for (int j = i + 1; j < pts.Count; j++)
            {
                double dx = pts[i].X - pts[j].X;
                double dy = pts[i].Y - pts[j].Y;
                double dz = pts[i].Z - pts[j].Z;
                double r2 = dx * dx + dy * dy + dz * dz;
                if (r2 > maxR2) maxR2 = r2;
            }
        }
        return Math.Sqrt(maxR2);
    }

    private static double EstimateSectionDiameter(
        ConvexPolyhedron cph,
        double pX, double pY, double pZ,
        double nX, double nY, double nZ)
    {
        // project every vertex onto the plane and return the diameter of the
        // projected point set
        double minProjU = double.PositiveInfinity, maxProjU = double.NegativeInfinity;
        double minProjV = double.PositiveInfinity, maxProjV = double.NegativeInfinity;

        // build an in-plane basis (e, f) from n
        double eX, eY, eZ;
        if (Math.Abs(nZ) < 0.9) { eX = -nY; eY = nX; eZ = 0.0; }
        else                    { eX = 1.0; eY = 0.0; eZ = 0.0; }
        double el = Math.Sqrt(eX * eX + eY * eY + eZ * eZ);
        if (el < BlockCutOptTolerances.GeometricEps) { eX = 1; eY = 0; eZ = 0; el = 1; }
        eX /= el; eY /= el; eZ /= el;
        double fX = nY * eZ - nZ * eY;
        double fY = nZ * eX - nX * eZ;
        double fZ = nX * eY - nY * eX;

        for (int i = 0; i < cph.Vertices.Count; i++)
        {
            var v = cph.Vertices[i];
            double rx = v.X - pX, ry = v.Y - pY, rz = v.Z - pZ;
            double u = rx * eX + ry * eY + rz * eZ;
            double w = rx * fX + ry * fY + rz * fZ;
            if (u < minProjU) minProjU = u;
            if (u > maxProjU) maxProjU = u;
            if (w < minProjV) minProjV = w;
            if (w > maxProjV) maxProjV = w;
        }
        double du = maxProjU - minProjU, dv = maxProjV - minProjV;
        return Math.Sqrt(du * du + dv * dv);
    }
}
