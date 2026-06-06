#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;

namespace Frahan.Masonry.Quarry;

// =============================================================================
// JointSetDfnGenerator — builds a Discrete Fracture Network from a list of
// JointSets and a bounding box. The output is a list of FracturePlanes ready
// to feed SlabCutter for quarry block extraction.
//
// Algorithm (Priest 1993, ch. 4 — "Spacing along a scanline"):
//   For each joint set j:
//     1. Project the 8 bounding-box corners onto j's normal axis. Compute
//        [tMin, tMax] = the projection range relative to the box centre.
//     2. Walk this range in steps of meanSpacing (or exponential samples
//        with rate = 1/meanSpacing), starting from a uniformly-random
//        offset in [0, meanSpacing).
//     3. For each step value t, emit a plane at (boxCentre + t * normal),
//        with normal optionally perturbed by Gaussian scatter (Fisher
//        approximation for small scatter angles).
//
// This gives realistic dimension-stone block patterns: blocks bounded by
// 2-3 distinct joint sets at characteristic orientations and spacings.
// Compare to FracturePlaneGenerators.Grid (axis-aligned only) and Random
// (no spacing structure); JointSetDfn is the geomechanically faithful
// version called out by ISRM Suggested Methods.
// =============================================================================

/// <summary>
/// Generates a DFN from joint set descriptions plus a bounding box.
/// </summary>
public static class JointSetDfnGenerator
{
    private const int HardPlanesPerSetLimit = 5000;

    /// <summary>
    /// Generate fracture planes for all joint sets, clipped to the
    /// projection of <paramref name="box"/> on each set's normal axis.
    /// Deterministic given <paramref name="seed"/>.
    /// </summary>
    public static IReadOnlyList<FracturePlane> Generate(
        IReadOnlyList<JointSet> jointSets,
        BoundingBox3 box,
        int seed)
    {
        if (jointSets == null) throw new ArgumentNullException(nameof(jointSets));
        if (box == null) throw new ArgumentNullException(nameof(box));

        var rng = new Random(seed);
        var result = new List<FracturePlane>(jointSets.Count * 32);

        double cx = box.CenterX, cy = box.CenterY, cz = box.CenterZ;

        for (int s = 0; s < jointSets.Count; s++)
        {
            var js = jointSets[s];
            if (js == null)
                throw new ArgumentException($"jointSets[{s}] is null", nameof(jointSets));

            double nx = js.NormalX, ny = js.NormalY, nz = js.NormalZ;

            // Step 1: projection range of the 8 corners onto js.normal,
            // measured from the box centre.
            double tMin = double.PositiveInfinity, tMax = double.NegativeInfinity;
            for (int c = 0; c < 8; c++)
            {
                double x = ((c & 1) != 0 ? box.MaxX : box.MinX) - cx;
                double y = ((c & 2) != 0 ? box.MaxY : box.MinY) - cy;
                double z = ((c & 4) != 0 ? box.MaxZ : box.MinZ) - cz;
                double proj = x * nx + y * ny + z * nz;
                if (proj < tMin) tMin = proj;
                if (proj > tMax) tMax = proj;
            }

            // Step 2: walk the [tMin, tMax] range in spacing increments.
            double t0 = tMin + rng.NextDouble() * js.MeanSpacing;
            double t = t0;
            int emitted = 0;
            while (t <= tMax && emitted < HardPlanesPerSetLimit)
            {
                // Optional orientation scatter — Gaussian about the mean.
                double pnx = nx, pny = ny, pnz = nz;
                if (js.ScatterDeg > 0.0)
                {
                    PerturbNormal(rng, js.ScatterDeg, ref pnx, ref pny, ref pnz);
                }

                double px = cx + t * nx;
                double py = cy + t * ny;
                double pz = cz + t * nz;

                result.Add(new FracturePlane(px, py, pz, pnx, pny, pnz));

                double step = js.ExponentialSpacing
                    ? -Math.Log(1.0 - rng.NextDouble()) * js.MeanSpacing
                    : js.MeanSpacing;
                if (step <= 0.0)
                    throw new InvalidOperationException("non-positive spacing step");
                t += step;
                emitted += 1;
            }
            if (emitted >= HardPlanesPerSetLimit)
                throw new InvalidOperationException(
                    $"joint set {s} hit hard plane limit ({HardPlanesPerSetLimit}); " +
                    "spacing too small for the box.");
        }
        return result;
    }

    /// <summary>
    /// One-shot pipeline: generate the DFN and cut <paramref name="quarry"/>
    /// by it.
    /// </summary>
    public static SlabCutResult DecomposeByJointSets(
        Slab quarry,
        IReadOnlyList<JointSet> jointSets,
        int seed,
        double eps = 1e-9)
    {
        if (quarry == null) throw new ArgumentNullException(nameof(quarry));
        if (jointSets == null) throw new ArgumentNullException(nameof(jointSets));
        var box = BoundingBox3.FromSlab(quarry);
        var planes = Generate(jointSets, box, seed);
        return SlabCutter.Cut(quarry, planes, eps);
    }

    // ─── Orientation scatter (Gaussian in tangent plane; small-angle Fisher) ─

    private static void PerturbNormal(
        Random rng, double scatterDeg,
        ref double nx, ref double ny, ref double nz)
    {
        double sigma = scatterDeg * Math.PI / 180.0;
        // Sample two zero-mean Gaussians via Box-Muller.
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        double r = Math.Sqrt(-2.0 * Math.Log(u1));
        double a = r * Math.Cos(2.0 * Math.PI * u2) * sigma;
        double b = r * Math.Sin(2.0 * Math.PI * u2) * sigma;

        // Build a pair of in-plane axes (u, v) perpendicular to n.
        double sx, sy, sz;
        if (Math.Abs(nz) < 0.9) { sx = 0; sy = 0; sz = 1; }
        else                    { sx = 1; sy = 0; sz = 0; }
        double ux = ny * sz - nz * sy;
        double uy = nz * sx - nx * sz;
        double uz = nx * sy - ny * sx;
        double um = Math.Sqrt(ux * ux + uy * uy + uz * uz);
        if (um < 1e-20) return;
        ux /= um; uy /= um; uz /= um;
        double vx = ny * uz - nz * uy;
        double vy = nz * ux - nx * uz;
        double vz = nx * uy - ny * ux;

        // n' = n + a*u + b*v, then renormalise.
        double pnx = nx + a * ux + b * vx;
        double pny = ny + a * uy + b * vy;
        double pnz = nz + a * uz + b * vz;
        double pm = Math.Sqrt(pnx * pnx + pny * pny + pnz * pnz);
        if (pm < 1e-20) return;
        nx = pnx / pm; ny = pny / pm; nz = pnz / pm;
    }
}
