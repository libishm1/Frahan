#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// DensityWatershedPartition -- Phase 7; improvement I5.
//
// Adaptive sub-division of the tested area by the fracture-density field
// instead of a forced uniform (mx, my) grid. The sub-zone boundaries snap to
// high-density ridges, aligning the unavoidable boundary-as-synthetic-fracture
// penalty with already-existing fractures. Result: small but real recovery
// improvement over BlockCutOpt 2020 uniform sub-division.
//
// Procedure:
//   1. Build a 2D fracture-density raster over the tested area using a
//      Gaussian-kernel-on-trace-footprint estimator.
//   2. Run a marker-based watershed segmentation. Markers = local minima of
//      the density field (low-density basins).
//   3. For each watershed basin, compute its axis-aligned bounding box --
//      this is the SubZone polygon (rectangular in v1; the polygon-aware
//      solver lands later).
//
// Units: tested area is in metres; bandwidth h is in metres
// (BlockCutOptTolerances default: 10 m for regional-granite, 1 m for
// limestone-bench).
// =============================================================================

public static class DensityWatershedPartition
{
    /// <summary>
    /// Partition <paramref name="area"/> by the 2D fracture-density field
    /// inferred from each FracturePlane's projection onto the X-Y plane.
    /// </summary>
    /// <param name="area">Tested area AABB (metres).</param>
    /// <param name="planes">Fracture planes (only their footprints onto Z=area.CenterZ are used).</param>
    /// <param name="bandwidth">Gaussian KDE bandwidth in metres.</param>
    /// <param name="rasterCellSize">Cell size of the density raster in metres. Default: bandwidth / 4.</param>
    public static IReadOnlyList<SubZone> Partition(
        BoundingBox3 area,
        IReadOnlyList<FracturePlane> planes,
        double bandwidth = 1.0,
        double rasterCellSize = 0.0)
    {
        if (area == null) throw new ArgumentNullException(nameof(area));
        if (planes == null) throw new ArgumentNullException(nameof(planes));
        if (!(bandwidth > 0)) throw new ArgumentOutOfRangeException(nameof(bandwidth));
        if (rasterCellSize <= 0) rasterCellSize = bandwidth * 0.25;

        int nx = Math.Max(8, (int)Math.Ceiling(area.SizeX / rasterCellSize));
        int ny = Math.Max(8, (int)Math.Ceiling(area.SizeY / rasterCellSize));

        // 1. Density raster
        var density = new double[nx, ny];
        double h2 = bandwidth * bandwidth;
        for (int p = 0; p < planes.Count; p++)
        {
            var pl = planes[p];
            // sample point at the plane's "point" -- a coarse but effective
            // approximation since BlockCutOpt's PLY input only uses planes
            // discretely sampled along scanlines
            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                double cx = area.MinX + (i + 0.5) * (area.SizeX / nx);
                double cy = area.MinY + (j + 0.5) * (area.SizeY / ny);
                double dx = cx - pl.PointX;
                double dy = cy - pl.PointY;
                double r2 = dx * dx + dy * dy;
                density[i, j] += Math.Exp(-0.5 * r2 / h2);
            }
        }

        // 2. Find local minima -> seeds
        var seeds = new List<(int I, int J)>();
        for (int j = 1; j < ny - 1; j++)
        for (int i = 1; i < nx - 1; i++)
        {
            double v = density[i, j];
            if (v <= density[i - 1, j] && v <= density[i + 1, j]
                && v <= density[i, j - 1] && v <= density[i, j + 1]
                && v <= density[i - 1, j - 1] && v <= density[i + 1, j + 1]
                && v <= density[i + 1, j - 1] && v <= density[i - 1, j + 1])
            {
                seeds.Add((i, j));
            }
        }
        if (seeds.Count == 0)
        {
            // degenerate case -- one zone
            return new[] { new SubZone(1, 1, area) };
        }

        // 3. Marker-based watershed (simple flooding by sorted density)
        var label = new int[nx, ny];
        for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++) label[i, j] = 0;
        for (int s = 0; s < seeds.Count; s++) label[seeds[s].I, seeds[s].J] = s + 1;

        // priority order: ascending density -> grow lowest cells first
        var indices = new List<(double Val, int I, int J)>(nx * ny);
        for (int j = 0; j < ny; j++)
        for (int i = 0; i < nx; i++)
            indices.Add((density[i, j], i, j));
        indices.Sort((a, b) => a.Val.CompareTo(b.Val));

        // iterative neighbour-propagation until labels stabilise
        bool changed = true;
        int iter = 0;
        const int maxIter = 64;
        while (changed && iter < maxIter)
        {
            changed = false;
            iter++;
            for (int k = 0; k < indices.Count; k++)
            {
                int i = indices[k].I, j = indices[k].J;
                if (label[i, j] != 0) continue;
                int lab = 0;
                if (i > 0      && label[i - 1, j] > 0) lab = label[i - 1, j];
                else if (i + 1 < nx && label[i + 1, j] > 0) lab = label[i + 1, j];
                else if (j > 0      && label[i, j - 1] > 0) lab = label[i, j - 1];
                else if (j + 1 < ny && label[i, j + 1] > 0) lab = label[i, j + 1];
                if (lab > 0) { label[i, j] = lab; changed = true; }
            }
        }

        // any unlabelled remainder defaults to the first label
        for (int j = 0; j < ny; j++)
        for (int i = 0; i < nx; i++)
            if (label[i, j] == 0) label[i, j] = 1;

        // 4. Bounding box per label
        int basinCount = seeds.Count;
        var minI = new int[basinCount + 1]; var maxI = new int[basinCount + 1];
        var minJ = new int[basinCount + 1]; var maxJ = new int[basinCount + 1];
        for (int s = 1; s <= basinCount; s++)
        {
            minI[s] = int.MaxValue; minJ[s] = int.MaxValue;
            maxI[s] = int.MinValue; maxJ[s] = int.MinValue;
        }
        for (int j = 0; j < ny; j++)
        for (int i = 0; i < nx; i++)
        {
            int s = label[i, j];
            if (s <= 0 || s > basinCount) continue;
            if (i < minI[s]) minI[s] = i;
            if (i > maxI[s]) maxI[s] = i;
            if (j < minJ[s]) minJ[s] = j;
            if (j > maxJ[s]) maxJ[s] = j;
        }
        double pixelX = area.SizeX / nx, pixelY = area.SizeY / ny;

        var zones = new List<SubZone>(basinCount);
        int writtenIndex = 0;
        for (int s = 1; s <= basinCount; s++)
        {
            if (maxI[s] < minI[s]) continue;
            double xMinR = area.MinX + minI[s] * pixelX;
            double xMaxR = area.MinX + (maxI[s] + 1) * pixelX;
            double yMinR = area.MinY + minJ[s] * pixelY;
            double yMaxR = area.MinY + (maxJ[s] + 1) * pixelY;
            xMinR = Math.Max(xMinR, area.MinX);
            xMaxR = Math.Min(xMaxR, area.MaxX);
            yMinR = Math.Max(yMinR, area.MinY);
            yMaxR = Math.Min(yMaxR, area.MaxY);
            if (xMaxR - xMinR < BlockCutOptTolerances.GeometricEps) continue;
            if (yMaxR - yMinR < BlockCutOptTolerances.GeometricEps) continue;
            writtenIndex++;
            var sub = new BoundingBox3(xMinR, yMinR, area.MinZ, xMaxR, yMaxR, area.MaxZ);
            zones.Add(new SubZone(writtenIndex, 1, sub));
        }

        if (zones.Count == 0)
            zones.Add(new SubZone(1, 1, area));

        return zones;
    }
}
