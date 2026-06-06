#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Core.Fabrication;

// =============================================================================
// StaggeredBlockLayout — running-bond ("staggered masonry") cell layout for the
// Fabricate flagship: split a sculpted / freeform stone form into staggered,
// brick-bond-like blocks, each sized to be fabricated by wire saw + 3-axis /
// robotic milling.
//
// Why a dedicated layout (not the existing BrickPattern generator): that
// generator emits INFINITE planes, so a single cut pass yields a regular grid,
// not a true running bond (its own comment notes the stagger "only matters if
// the consumer post-processes per-course"). This class produces explicit
// per-course CELL boxes with the half-block stagger applied, which is exactly
// the per-course post-processing that turns a grid into running bond. The GH
// layer intersects each cell with the form (RhinoCommon boolean) or feeds the
// cells straight into the existing split / pack components.
//
// Pure-managed (no Rhino types): operates on a min/max box and returns cells as
// plain doubles + a course index.
// =============================================================================

public sealed class StaggeredCell
{
    public int Course;            // 0-based, bottom course first (build order)
    public int IndexInCourse;     // 0-based along the bond direction
    public double MinX, MinY, MinZ;
    public double MaxX, MaxY, MaxZ;

    public double SizeX => MaxX - MinX;
    public double SizeY => MaxY - MinY;
    public double SizeZ => MaxZ - MinZ;
}

public static class StaggeredBlockLayout
{
    /// <summary>
    /// Build running-bond cells over the box [min..max]. Courses stack along
    /// <paramref name="upAxis"/> (0=X,1=Y,2=Z; default Z) at <paramref
    /// name="courseHeight"/>; within each course the bond axis (the larger of
    /// the two remaining axes) is tiled at <paramref name="blockLength"/>, with
    /// odd courses shifted by <paramref name="stagger"/>·blockLength so the
    /// joints break. The third axis spans the full depth (single wythe).
    /// </summary>
    public static IReadOnlyList<StaggeredCell> Build(
        double minX, double minY, double minZ,
        double maxX, double maxY, double maxZ,
        double courseHeight, double blockLength, double stagger = 0.5, int upAxis = 2)
    {
        if (courseHeight <= 0) throw new ArgumentOutOfRangeException(nameof(courseHeight));
        if (blockLength <= 0) throw new ArgumentOutOfRangeException(nameof(blockLength));
        if (upAxis < 0 || upAxis > 2) throw new ArgumentOutOfRangeException(nameof(upAxis));
        if (stagger < 0) stagger = 0;

        double[] lo = { minX, minY, minZ };
        double[] hi = { maxX, maxY, maxZ };
        for (int a = 0; a < 3; a++)
            if (hi[a] < lo[a]) { var t = hi[a]; hi[a] = lo[a]; lo[a] = t; }

        // bond axis = the larger of the two non-up axes; depth axis = the other.
        int ax0 = (upAxis + 1) % 3, ax1 = (upAxis + 2) % 3;
        double s0 = hi[ax0] - lo[ax0], s1 = hi[ax1] - lo[ax1];
        int bondAxis = s0 >= s1 ? ax0 : ax1;
        int depthAxis = s0 >= s1 ? ax1 : ax0;

        const double eps = 1e-9;
        double upLo = lo[upAxis], upHi = hi[upAxis];
        double bLo = lo[bondAxis], bHi = hi[bondAxis];

        var cells = new List<StaggeredCell>();
        int nCourses = (int)Math.Ceiling((upHi - upLo) / courseHeight - eps);
        if (nCourses < 1) nCourses = 1;

        for (int c = 0; c < nCourses; c++)
        {
            double u0 = upLo + c * courseHeight;
            double u1 = Math.Min(upHi, u0 + courseHeight);
            if (u1 - u0 <= eps) continue;

            double offset = (c % 2 == 1) ? stagger * blockLength : 0.0;
            double start = bLo - offset;
            int idx = 0;
            for (double x = start; x < bHi - eps; x += blockLength)
            {
                double cx0 = Math.Max(bLo, x);
                double cx1 = Math.Min(bHi, x + blockLength);
                if (cx1 - cx0 <= eps) continue;

                var cell = new StaggeredCell { Course = c, IndexInCourse = idx++ };
                SetAxis(cell, upAxis, u0, u1);
                SetAxis(cell, bondAxis, cx0, cx1);
                SetAxis(cell, depthAxis, lo[depthAxis], hi[depthAxis]);
                cells.Add(cell);
            }
        }
        return cells;
    }

    private static void SetAxis(StaggeredCell cell, int axis, double lo, double hi)
    {
        switch (axis)
        {
            case 0: cell.MinX = lo; cell.MaxX = hi; break;
            case 1: cell.MinY = lo; cell.MaxY = hi; break;
            default: cell.MinZ = lo; cell.MaxZ = hi; break;
        }
    }
}
