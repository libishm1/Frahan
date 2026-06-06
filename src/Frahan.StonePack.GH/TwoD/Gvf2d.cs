#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.GH.TwoD;

/// <summary>
/// Gradient Vector Flow (GVF) field over a 2D domain.
///
/// Xu/Prince 1998 IEEE TIP 7(3) 359–369. Pure-managed; no Rhino
/// dependency. Used by the Trencadís solver (F-2D-002.3 wiki
/// recommendation) to give pieces an orientation that follows the
/// boundary tangent in interior regions, propagated smoothly via
/// Euler-Lagrange diffusion.
///
/// Algorithm:
///   1. Build edge magnitude `f(i,j)` = 1 / (1 + d(i,j)²) where
///      d is the distance to the nearest boundary segment. This
///      matches Battiato 2013 §3.1's edge map (high near boundary,
///      low far away).
///   2. Compute fx = ∂f/∂x, fy = ∂f/∂y via central differences.
///   3. Initialize v = (u, w) = (fx, fy).
///   4. Iterate diffusion (Xu/Prince 1998 eq. 12):
///        u_t = μ ∇²u - (u - fx) (fx² + fy²)
///        w_t = μ ∇²w - (w - fy) (fx² + fy²)
///      Discretised with explicit time step.
///   5. Cells outside the domain (outside outer or inside holes)
///      are masked — their (u, w) is not updated and stays 0.
///
/// At candidate position (x, y), <see cref="Sample"/> returns (u, w)
/// via bilinear interpolation. The interesting quantity for tile
/// orientation is `θ = atan2(w, u)` — the GVF vector points along
/// the local edge gradient, so tiles aligned PERPENDICULAR to the
/// vector hug the boundary.
/// </summary>
public sealed class Gvf2d
{
    public readonly double BboxMinX, BboxMinY, BboxMaxX, BboxMaxY;
    public readonly int GridX, GridY;
    public readonly double[,] U;
    public readonly double[,] W;
    public readonly bool[,] Inside;

    private Gvf2d(double minX, double minY, double maxX, double maxY,
        int gx, int gy, double[,] u, double[,] w, bool[,] inside)
    {
        BboxMinX = minX; BboxMinY = minY; BboxMaxX = maxX; BboxMaxY = maxY;
        GridX = gx; GridY = gy;
        U = u; W = w; Inside = inside;
    }

    /// <summary>
    /// Compute GVF field over the domain bounded by `outer` minus `holes`.
    /// </summary>
    public static Gvf2d Compute(
        double[] outerVx, double[] outerVy,
        IList<(double[] vx, double[] vy)> holes,
        double bboxMinX, double bboxMinY, double bboxMaxX, double bboxMaxY,
        int gridRes = 48, double mu = 0.2, int iterations = 80)
    {
        if (outerVx == null || outerVy == null || outerVx.Length < 3
            || gridRes < 4 || bboxMaxX <= bboxMinX || bboxMaxY <= bboxMinY)
        {
            return new Gvf2d(bboxMinX, bboxMinY, bboxMaxX, bboxMaxY, 0, 0, null, null, null);
        }

        holes ??= Array.Empty<(double[], double[])>();
        var nOuter = outerVx.Length;
        var gx = gridRes;
        var gy = gridRes;
        var dx = (bboxMaxX - bboxMinX) / (gx - 1);
        var dy = (bboxMaxY - bboxMinY) / (gy - 1);

        // 1. Edge map: high near boundary, low in interior.
        var f = new double[gx, gy];
        var inside = new bool[gx, gy];
        for (int i = 0; i < gx; i++)
        {
            var x = bboxMinX + i * dx;
            for (int j = 0; j < gy; j++)
            {
                var y = bboxMinY + j * dy;
                var inOuter = PointInPoly(x, y, outerVx, outerVy, nOuter);
                var inHole = PointInAnyHole(x, y, holes);
                inside[i, j] = inOuter && !inHole;

                // Distance to nearest boundary segment (outer + each hole).
                var minDist = MinDistToPolyBoundary(x, y, outerVx, outerVy, nOuter);
                foreach (var h in holes)
                {
                    var dh = MinDistToPolyBoundary(x, y, h.vx, h.vy, h.vx.Length);
                    if (dh < minDist) minDist = dh;
                }
                f[i, j] = 1.0 / (1.0 + minDist * minDist);
            }
        }

        // 2. Edge gradient.
        var fx = new double[gx, gy];
        var fy = new double[gx, gy];
        for (int i = 0; i < gx; i++)
            for (int j = 0; j < gy; j++)
            {
                var ip = Math.Min(i + 1, gx - 1);
                var im = Math.Max(i - 1, 0);
                var jp = Math.Min(j + 1, gy - 1);
                var jm = Math.Max(j - 1, 0);
                fx[i, j] = (f[ip, j] - f[im, j]) / (2 * dx);
                fy[i, j] = (f[i, jp] - f[i, jm]) / (2 * dy);
            }

        // 3. Initialize v = ∇f.
        var u = new double[gx, gy];
        var w = new double[gx, gy];
        for (int i = 0; i < gx; i++)
            for (int j = 0; j < gy; j++)
            {
                u[i, j] = fx[i, j];
                w[i, j] = fy[i, j];
            }

        // 4. GVF iteration. Time-step bound for stability:
        //    Δt ≤ (dx · dy) / (4 · μ). Take 0.9× for safety.
        var dt = 0.9 * dx * dy / Math.Max(4 * mu, 1e-9);
        var uNew = new double[gx, gy];
        var wNew = new double[gx, gy];
        for (int it = 0; it < iterations; it++)
        {
            for (int i = 0; i < gx; i++)
                for (int j = 0; j < gy; j++)
                {
                    if (!inside[i, j]) { uNew[i, j] = 0; wNew[i, j] = 0; continue; }
                    var ip = Math.Min(i + 1, gx - 1);
                    var im = Math.Max(i - 1, 0);
                    var jp = Math.Min(j + 1, gy - 1);
                    var jm = Math.Max(j - 1, 0);
                    var lapU = (u[ip, j] + u[im, j] + u[i, jp] + u[i, jm] - 4 * u[i, j]) / (dx * dy);
                    var lapW = (w[ip, j] + w[im, j] + w[i, jp] + w[i, jm] - 4 * w[i, j]) / (dx * dy);
                    var fxij = fx[i, j];
                    var fyij = fy[i, j];
                    var b = fxij * fxij + fyij * fyij;
                    uNew[i, j] = u[i, j] + dt * (mu * lapU - (u[i, j] - fxij) * b);
                    wNew[i, j] = w[i, j] + dt * (mu * lapW - (w[i, j] - fyij) * b);
                }
            // Swap.
            var tu = u; u = uNew; uNew = tu;
            var tw = w; w = wNew; wNew = tw;
        }

        return new Gvf2d(bboxMinX, bboxMinY, bboxMaxX, bboxMaxY, gx, gy, u, w, inside);
    }

    /// <summary>
    /// Sample the GVF field at world-space (x, y) via bilinear
    /// interpolation. Returns (0, 0) if (x, y) is outside the domain.
    /// </summary>
    public (double u, double w) Sample(double x, double y)
    {
        if (U == null || GridX < 2 || GridY < 2) return (0, 0);
        if (x < BboxMinX || x > BboxMaxX || y < BboxMinY || y > BboxMaxY) return (0, 0);

        var dx = (BboxMaxX - BboxMinX) / (GridX - 1);
        var dy = (BboxMaxY - BboxMinY) / (GridY - 1);
        var fi = (x - BboxMinX) / dx;
        var fj = (y - BboxMinY) / dy;
        var i = (int)Math.Floor(fi);
        var j = (int)Math.Floor(fj);
        if (i < 0) i = 0; if (i >= GridX - 1) i = GridX - 2;
        if (j < 0) j = 0; if (j >= GridY - 1) j = GridY - 2;
        var ax = fi - i;
        var ay = fj - j;

        if (!Inside[i, j] && !Inside[i + 1, j] && !Inside[i, j + 1] && !Inside[i + 1, j + 1])
            return (0, 0);

        var u00 = U[i, j]; var u10 = U[i + 1, j];
        var u01 = U[i, j + 1]; var u11 = U[i + 1, j + 1];
        var w00 = W[i, j]; var w10 = W[i + 1, j];
        var w01 = W[i, j + 1]; var w11 = W[i + 1, j + 1];

        var u = (1 - ax) * (1 - ay) * u00 + ax * (1 - ay) * u10
              + (1 - ax) * ay * u01 + ax * ay * u11;
        var ww = (1 - ax) * (1 - ay) * w00 + ax * (1 - ay) * w10
              + (1 - ax) * ay * w01 + ax * ay * w11;
        return (u, ww);
    }

    /// <summary>
    /// Preferred tile orientation in degrees [0, 180), from the GVF
    /// field at (x, y). Returns null if the field is empty or the
    /// vector magnitude is too small to be meaningful.
    /// </summary>
    public double? OrientationDeg(double x, double y, double minMag = 1e-4)
    {
        var (u, w) = Sample(x, y);
        var mag = Math.Sqrt(u * u + w * w);
        if (mag < minMag) return null;
        // GVF vector points along the gradient (perpendicular to the
        // boundary contour). Tile-rotation orientation is the TANGENT,
        // i.e. the perpendicular: rotate by +90°. Reduce mod 180°
        // because tile orientation is rotation-by-π invariant for
        // square-ish pieces.
        var tan = Math.Atan2(u, -w); // perp(u, w) = (-w, u)/(u, -w)
        var deg = tan * 180.0 / Math.PI;
        deg = ((deg % 180.0) + 180.0) % 180.0;
        return deg;
    }

    private static bool PointInPoly(double px, double py, double[] vx, double[] vy, int n)
    {
        var inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
            if ((vy[i] > py) != (vy[j] > py) &&
                px < (vx[j] - vx[i]) * (py - vy[i]) / (vy[j] - vy[i]) + vx[i])
                inside = !inside;
        return inside;
    }

    private static bool PointInAnyHole(double x, double y, IList<(double[] vx, double[] vy)> holes)
    {
        for (int h = 0; h < holes.Count; h++)
        {
            var hv = holes[h];
            if (PointInPoly(x, y, hv.vx, hv.vy, hv.vx.Length)) return true;
        }
        return false;
    }

    private static double MinDistToPolyBoundary(double px, double py, double[] vx, double[] vy, int n)
    {
        var min = double.MaxValue;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            var d = PointSegDist(px, py, vx[i], vy[i], vx[j], vy[j]);
            if (d < min) min = d;
        }
        return min;
    }

    private static double PointSegDist(double px, double py, double ax, double ay, double bx, double by)
    {
        var dx = bx - ax; var dy = by - ay;
        var lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-20) return Math.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));
        var t = Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / lenSq));
        var nx = ax + t * dx; var ny = ay + t * dy;
        return Math.Sqrt((px - nx) * (px - nx) + (py - ny) * (py - ny));
    }
}
