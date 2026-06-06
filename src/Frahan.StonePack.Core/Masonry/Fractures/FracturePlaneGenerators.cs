#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;

namespace Frahan.Masonry.Fractures;

// =============================================================================
// FracturePlaneGenerators — DFN (discrete fracture network) authoring helpers.
//
// The cutting kernel (SlabCutter / FractureCutter, Phase E.1 / E.2) takes
// FracturePlane / FracturePolygon inputs and splits Slabs accordingly. These
// generators produce the input planes from coarse parameters: an axis-
// aligned bounding box plus pattern-specific knobs.
//
// All generators are deterministic given a (seed, count) pair so the GH
// canvas re-evaluates produce identical output.
// =============================================================================

/// <summary>
/// Static factory methods for common DFN patterns: orthogonal grids,
/// jittered grids, uniformly-random orientations, and Voronoi bisectors
/// between user-supplied seed points.
/// </summary>
public static class FracturePlaneGenerators
{
    /// <summary>
    /// Three orthogonal families of evenly-spaced planes filling
    /// <paramref name="box"/>: <paramref name="nX"/> planes perpendicular to
    /// X, <paramref name="nY"/> to Y, <paramref name="nZ"/> to Z. Each
    /// family lays out planes at 1/(n+1), 2/(n+1), … N/(n+1) along the box.
    /// </summary>
    public static IReadOnlyList<FracturePlane> Grid(BoundingBox3 box, int nX, int nY, int nZ)
    {
        if (box == null) throw new ArgumentNullException(nameof(box));
        if (nX < 0) throw new ArgumentOutOfRangeException(nameof(nX), "must be >= 0");
        if (nY < 0) throw new ArgumentOutOfRangeException(nameof(nY), "must be >= 0");
        if (nZ < 0) throw new ArgumentOutOfRangeException(nameof(nZ), "must be >= 0");

        var result = new List<FracturePlane>(nX + nY + nZ);
        for (int i = 1; i <= nX; i++)
        {
            double t = (double)i / (nX + 1);
            double x = box.MinX + t * box.SizeX;
            result.Add(new FracturePlane(x, box.CenterY, box.CenterZ, 1, 0, 0));
        }
        for (int i = 1; i <= nY; i++)
        {
            double t = (double)i / (nY + 1);
            double y = box.MinY + t * box.SizeY;
            result.Add(new FracturePlane(box.CenterX, y, box.CenterZ, 0, 1, 0));
        }
        for (int i = 1; i <= nZ; i++)
        {
            double t = (double)i / (nZ + 1);
            double z = box.MinZ + t * box.SizeZ;
            result.Add(new FracturePlane(box.CenterX, box.CenterY, z, 0, 0, 1));
        }
        if (result.Count != nX + nY + nZ)
            throw new InvalidOperationException("grid count mismatch");
        return result;
    }

    /// <summary>
    /// Same as <see cref="Grid"/> but each plane's point is jittered along
    /// its normal by up to <paramref name="jitter"/> × cell-step. The plane
    /// stays orthogonal; only the offset changes. Deterministic given
    /// <paramref name="seed"/>.
    /// </summary>
    public static IReadOnlyList<FracturePlane> JitteredGrid(
        BoundingBox3 box, int nX, int nY, int nZ, double jitter, int seed)
    {
        if (box == null) throw new ArgumentNullException(nameof(box));
        if (nX < 0) throw new ArgumentOutOfRangeException(nameof(nX));
        if (nY < 0) throw new ArgumentOutOfRangeException(nameof(nY));
        if (nZ < 0) throw new ArgumentOutOfRangeException(nameof(nZ));
        if (!(jitter >= 0.0 && jitter < 0.5))
            throw new ArgumentOutOfRangeException(nameof(jitter), "must be in [0, 0.5)");

        var rng = new Random(seed);
        var result = new List<FracturePlane>(nX + nY + nZ);

        double stepX = (nX > 0) ? box.SizeX / (nX + 1) : 0.0;
        double stepY = (nY > 0) ? box.SizeY / (nY + 1) : 0.0;
        double stepZ = (nZ > 0) ? box.SizeZ / (nZ + 1) : 0.0;

        for (int i = 1; i <= nX; i++)
        {
            double x = box.MinX + i * stepX + (rng.NextDouble() * 2.0 - 1.0) * jitter * stepX;
            result.Add(new FracturePlane(x, box.CenterY, box.CenterZ, 1, 0, 0));
        }
        for (int i = 1; i <= nY; i++)
        {
            double y = box.MinY + i * stepY + (rng.NextDouble() * 2.0 - 1.0) * jitter * stepY;
            result.Add(new FracturePlane(box.CenterX, y, box.CenterZ, 0, 1, 0));
        }
        for (int i = 1; i <= nZ; i++)
        {
            double z = box.MinZ + i * stepZ + (rng.NextDouble() * 2.0 - 1.0) * jitter * stepZ;
            result.Add(new FracturePlane(box.CenterX, box.CenterY, z, 0, 0, 1));
        }
        if (result.Count != nX + nY + nZ)
            throw new InvalidOperationException("jittered grid count mismatch");
        return result;
    }

    /// <summary>
    /// <paramref name="count"/> uniformly-distributed planes whose points
    /// lie inside <paramref name="box"/> and whose normals are uniformly
    /// distributed on the unit sphere. Deterministic given
    /// <paramref name="seed"/>.
    /// </summary>
    public static IReadOnlyList<FracturePlane> Random(BoundingBox3 box, int count, int seed)
    {
        if (box == null) throw new ArgumentNullException(nameof(box));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

        var rng = new System.Random(seed);
        var result = new List<FracturePlane>(count);
        for (int i = 0; i < count; i++)
        {
            double px = box.MinX + rng.NextDouble() * box.SizeX;
            double py = box.MinY + rng.NextDouble() * box.SizeY;
            double pz = box.MinZ + rng.NextDouble() * box.SizeZ;

            // Marsaglia: uniform on the unit sphere via two random angles.
            double u = rng.NextDouble() * 2.0 - 1.0;
            double theta = rng.NextDouble() * 2.0 * Math.PI;
            double s = Math.Sqrt(1.0 - u * u);
            double nx = s * Math.Cos(theta);
            double ny = s * Math.Sin(theta);
            double nz = u;
            result.Add(new FracturePlane(px, py, pz, nx, ny, nz));
        }
        if (result.Count != count)
            throw new InvalidOperationException("random count mismatch");
        return result;
    }

    /// <summary>
    /// Layered (sedimentary) fracture pattern: <paramref name="count"/>
    /// parallel planes equally spaced along <paramref name="axis"/> within
    /// <paramref name="box"/>. Useful for sedimentary rocks where the
    /// natural splitting direction is layered.
    /// </summary>
    /// <param name="axis">0 = X, 1 = Y, 2 = Z.</param>
    public static IReadOnlyList<FracturePlane> Layered(BoundingBox3 box, int axis, int count)
    {
        if (box == null) throw new ArgumentNullException(nameof(box));
        if (axis < 0 || axis > 2)
            throw new ArgumentOutOfRangeException(nameof(axis), "must be 0 (X), 1 (Y), or 2 (Z)");
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

        var result = new List<FracturePlane>(count);
        for (int i = 1; i <= count; i++)
        {
            double t = (double)i / (count + 1);
            switch (axis)
            {
                case 0:
                    result.Add(new FracturePlane(box.MinX + t * box.SizeX,
                        box.CenterY, box.CenterZ, 1, 0, 0));
                    break;
                case 1:
                    result.Add(new FracturePlane(box.CenterX,
                        box.MinY + t * box.SizeY, box.CenterZ, 0, 1, 0));
                    break;
                case 2:
                    result.Add(new FracturePlane(box.CenterX, box.CenterY,
                        box.MinZ + t * box.SizeZ, 0, 0, 1));
                    break;
            }
        }
        if (result.Count != count)
            throw new InvalidOperationException("layered count mismatch");
        return result;
    }

    /// <summary>
    /// Radial (fan) fracture pattern: <paramref name="count"/> planes that
    /// share the line passing through <paramref name="centerX/Y/Z"/> in
    /// direction <paramref name="axisX/Y/Z"/>; each plane is rotated by
    /// <c>i * 180° / count</c> around that axis. Useful for log-splitting
    /// patterns or fan cuts on cylindrical stones.
    /// </summary>
    public static IReadOnlyList<FracturePlane> Radial(
        double centerX, double centerY, double centerZ,
        double axisX, double axisY, double axisZ,
        int count)
    {
        double a2 = axisX * axisX + axisY * axisY + axisZ * axisZ;
        if (a2 < 1e-24) throw new ArgumentException("axis must be non-zero");
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

        // Build an orthonormal frame (axis, u, v).
        double inv = 1.0 / Math.Sqrt(a2);
        double ax = axisX * inv, ay = axisY * inv, az = axisZ * inv;
        double sx, sy, sz;
        if (Math.Abs(az) < 0.9) { sx = 0; sy = 0; sz = 1; }
        else                    { sx = 1; sy = 0; sz = 0; }
        // u = axis × seed
        double ux = ay * sz - az * sy;
        double uy = az * sx - ax * sz;
        double uz = ax * sy - ay * sx;
        double um = Math.Sqrt(ux * ux + uy * uy + uz * uz);
        if (um < 1e-20) throw new InvalidOperationException("radial seed parallel to axis");
        ux /= um; uy /= um; uz /= um;
        // v = axis × u
        double vx = ay * uz - az * uy;
        double vy = az * ux - ax * uz;
        double vz = ax * uy - ay * ux;

        var result = new List<FracturePlane>(count);
        for (int i = 0; i < count; i++)
        {
            double theta = i * Math.PI / Math.Max(1, count);
            double cs = Math.Cos(theta), sn = Math.Sin(theta);
            // plane normal = u * cos + v * sin (perpendicular to axis).
            double nx = ux * cs + vx * sn;
            double ny = uy * cs + vy * sn;
            double nz = uz * cs + vz * sn;
            result.Add(new FracturePlane(centerX, centerY, centerZ, nx, ny, nz));
        }
        if (result.Count != count)
            throw new InvalidOperationException("radial count mismatch");
        return result;
    }

    /// <summary>
    /// Brick-pattern fracture set: orthogonal planes that simulate
    /// running-bond brick layout. Emits <paramref name="nZ"/> horizontal
    /// planes (course separators) plus <paramref name="nX"/> vertical
    /// X planes per course; alternate courses are shifted by half a
    /// brick width.
    /// </summary>
    public static IReadOnlyList<FracturePlane> BrickPattern(
        BoundingBox3 box, int nX, int nZ)
    {
        if (box == null) throw new ArgumentNullException(nameof(box));
        if (nX < 0) throw new ArgumentOutOfRangeException(nameof(nX));
        if (nZ < 0) throw new ArgumentOutOfRangeException(nameof(nZ));

        var result = new List<FracturePlane>(nX * (nZ + 1) + nZ);
        // Bed-joint horizontals (Z-orthogonal planes).
        for (int i = 1; i <= nZ; i++)
        {
            double t = (double)i / (nZ + 1);
            double z = box.MinZ + t * box.SizeZ;
            result.Add(new FracturePlane(box.CenterX, box.CenterY, z, 0, 0, 1));
        }
        // Head-joint verticals (X-orthogonal planes), staggered per course.
        // Note: cuts are infinite planes so the stagger only matters
        // visually if the consumer post-processes per-course; the X plane
        // set is the union across all courses.
        double brickWidth = (nX > 0) ? box.SizeX / (nX + 1) : box.SizeX;
        for (int course = 0; course <= nZ; course++)
        {
            double xOffset = (course % 2 == 1) ? 0.5 * brickWidth : 0.0;
            for (int i = 1; i <= nX; i++)
            {
                double x = box.MinX + i * brickWidth + xOffset;
                if (x <= box.MinX + 1e-9 || x >= box.MaxX - 1e-9) continue;
                result.Add(new FracturePlane(x, box.CenterY, box.CenterZ, 1, 0, 0));
            }
        }
        return result;
    }

    /// <summary>
    /// Filter: drop planes that lie entirely outside <paramref name="box"/>
    /// (signed distance has the same sign at all 8 corners). This is a
    /// cheap pre-filter; <see cref="SlabCutter"/> handles missing planes
    /// gracefully but pre-filtering reduces wasted work on large fracture
    /// sets.
    /// </summary>
    public static IReadOnlyList<FracturePlane> FilterToBox(
        IReadOnlyList<FracturePlane> planes, BoundingBox3 box)
    {
        if (planes == null) throw new ArgumentNullException(nameof(planes));
        if (box == null) throw new ArgumentNullException(nameof(box));

        var result = new List<FracturePlane>(planes.Count);
        for (int i = 0; i < planes.Count; i++)
        {
            var p = planes[i];
            int positives = 0, negatives = 0;
            for (int c = 0; c < 8; c++)
            {
                double cx = ((c & 1) != 0) ? box.MaxX : box.MinX;
                double cy = ((c & 2) != 0) ? box.MaxY : box.MinY;
                double cz = ((c & 4) != 0) ? box.MaxZ : box.MinZ;
                double d = p.SignedDistance(cx, cy, cz);
                if (d > 1e-9) positives++;
                else if (d < -1e-9) negatives++;
            }
            // Plane intersects the box if at least one corner is on each side
            // (or any corner is on the plane).
            if (positives > 0 && negatives > 0) result.Add(p);
            else if (positives + negatives < 8) result.Add(p);  // some on plane
        }
        return result;
    }

    /// <summary>
    /// Voronoi bisector planes: for every distinct pair of seed points,
    /// emits the perpendicular bisector plane that lies between them. Cuts
    /// of a slab along these bisectors approximate the Voronoi cells of
    /// the seed set. Caller is responsible for filtering bisectors that
    /// don't intersect the slab (the cutter handles missing planes
    /// gracefully).
    /// </summary>
    /// <param name="seeds">Flat seed coordinates [x0,y0,z0,x1,y1,z1,...]; length must be a positive multiple of 3.</param>
    public static IReadOnlyList<FracturePlane> VoronoiBisectors(IReadOnlyList<double> seeds)
    {
        if (seeds == null) throw new ArgumentNullException(nameof(seeds));
        if (seeds.Count < 6 || seeds.Count % 3 != 0)
            throw new ArgumentException(
                $"need at least 2 seed points; seeds length must be a multiple of 3 (got {seeds.Count})",
                nameof(seeds));

        int n = seeds.Count / 3;
        var result = new List<FracturePlane>(n * (n - 1) / 2);
        for (int i = 0; i < n; i++)
        {
            double ax = seeds[3 * i + 0];
            double ay = seeds[3 * i + 1];
            double az = seeds[3 * i + 2];
            for (int j = i + 1; j < n; j++)
            {
                double bx = seeds[3 * j + 0];
                double by = seeds[3 * j + 1];
                double bz = seeds[3 * j + 2];
                double mx = 0.5 * (ax + bx);
                double my = 0.5 * (ay + by);
                double mz = 0.5 * (az + bz);
                double dx = bx - ax, dy = by - ay, dz = bz - az;
                double len2 = dx * dx + dy * dy + dz * dz;
                if (len2 < 1e-24) continue;  // coincident seeds: skip
                result.Add(new FracturePlane(mx, my, mz, dx, dy, dz));
            }
        }
        if (result.Count > n * (n - 1) / 2)
            throw new InvalidOperationException("Voronoi bisector count overflow");
        return result;
    }
}
