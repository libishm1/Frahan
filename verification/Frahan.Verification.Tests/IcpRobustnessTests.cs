using System;
using System.Collections.Generic;
using Frahan.Core.ScanIngest;
using Rhino.Geometry;
using Xunit;

namespace Frahan.Verification.Tests;

/// <summary>
/// Cloud-ICP registration robustness (KB-14).
///
/// The pre-alignment step that gives ICP its initial guess must not be
/// steerable by a handful of outliers. It used to translate the source by the
/// difference of ARITHMETIC-MEAN centroids, computed BEFORE any trimming, so a
/// single stray return in the target (a bird, a reflection, a mis-registered
/// scrap - routine in real scans) dragged the start arbitrarily far and the
/// remaining iterations could not recover. `trimFraction` exists precisely for
/// outlier robustness but never got to act on the pre-align.
///
/// Fixed by a per-axis MEDIAN centre plus applying the shift only when the
/// clouds are genuinely far apart (the feature's actual purpose). These are
/// property tests over many random configurations, not a single case - the
/// battery test `PointCloudIcp trim drops outliers` covers the fixed instance.
///
/// Evidence: code_ws handoffs KB-14, outputs/2026-07-25/release_delta_audit.
/// </summary>
public class IcpRobustnessTests
{
    private static List<Point3d> Grid(int n, double spacing, Vector3d offset)
    {
        var pts = new List<Point3d>(n * n * n);
        for (int x = 0; x < n; x++)
            for (int y = 0; y < n; y++)
                for (int z = 0; z < n; z++)
                    pts.Add(new Point3d(x * spacing + offset.X,
                                        y * spacing + offset.Y,
                                        z * spacing + offset.Z));
        return pts;
    }

    /// <summary>
    /// THE KB-14 PROPERTY: a clean source must still converge onto a target that
    /// carries outliers, for any number/placement/magnitude of them. One stray
    /// point must not move the initial guess.
    /// </summary>
    [Fact]
    public void Outliers_InTarget_DoNotPreventConvergence()
    {
        var rng = new Random(20260725);
        int trials = 0, bad = 0;
        double worst = 0.0;

        for (int t = 0; t < 60; t++)
        {
            double spacing = new[] { 0.05, 1.0, 25.0 }[t % 3];   // mm / m / quarry scales
            var src = Grid(4, spacing, Vector3d.Zero);
            var tgt = new List<Point3d>(src);

            int nOut = 1 + rng.Next(3);                            // 1..3 outliers
            for (int k = 0; k < nOut; k++)
            {
                double mag = spacing * (100.0 + rng.NextDouble() * 5000.0);
                tgt.Add(new Point3d(
                    (rng.NextDouble() < 0.5 ? -1 : 1) * mag,
                    (rng.NextDouble() < 0.5 ? -1 : 1) * mag,
                    (rng.NextDouble() < 0.5 ? -1 : 1) * mag));
            }

            var opts = new CloudIcpOptions(voxelScales: new[] { 0.0 },
                maxIterationsPerScale: 10, trimFraction: 0.1);
            var r = PointCloudIcp.Register(src, tgt, Transform.Identity, opts);

            trials++;
            double tol = 1e-3 * Math.Max(1.0, spacing);
            worst = Math.Max(worst, r.FinalRms);
            if (!(r.FinalRms < tol)) bad++;
        }

        Assert.True(bad == 0,
            $"outliers steered the registration in {bad}/{trials} trials (worst RMS {worst:G4}); " +
            "the pre-alignment centre must be outlier robust (KB-14)");
    }

    /// <summary>
    /// The pre-alignment must still do its JOB: clouds that genuinely sit far
    /// apart (a UTM frame against a local frame) must still be rescued from an
    /// identity start. Guards against "fixing" KB-14 by simply deleting the
    /// feature.
    /// </summary>
    [Fact]
    public void FarApartClouds_AreStillRescued()
    {
        var src = Grid(4, 1.0, Vector3d.Zero);
        var far = new Vector3d(500.0, -300.0, 120.0);            // >> the ~3 unit extent
        var tgt = Grid(4, 1.0, far);

        var opts = new CloudIcpOptions(voxelScales: new[] { 0.0 },
            maxIterationsPerScale: 20, trimFraction: 0.0);
        var r = PointCloudIcp.Register(src, tgt, Transform.Identity, opts);

        Assert.True(r.FinalRms < 1e-3,
            $"far-apart clouds must still be pre-aligned and converge, got RMS {r.FinalRms:G4}");
    }

    /// <summary>
    /// Both together: far apart AND outlier contaminated. The rescue must fire
    /// off a robust centre, not one an outlier has moved.
    /// </summary>
    [Fact]
    public void FarApart_WithOutliers_StillConverges()
    {
        var src = Grid(4, 1.0, Vector3d.Zero);
        var tgt = Grid(4, 1.0, new Vector3d(400.0, 250.0, -180.0));
        tgt.Add(new Point3d(90000, -70000, 40000));               // wild stray
        tgt.Add(new Point3d(-50000, 60000, -30000));

        var opts = new CloudIcpOptions(voxelScales: new[] { 0.0 },
            maxIterationsPerScale: 20, trimFraction: 0.1);
        var r = PointCloudIcp.Register(src, tgt, Transform.Identity, opts);

        Assert.True(r.FinalRms < 1e-3,
            $"far-apart + outlier-contaminated clouds must converge, got RMS {r.FinalRms:G4}");
    }
}
