#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Core.Fabrication;

namespace Frahan.Tests;

// Pure-managed tests for the running-bond staggered cell layout (no Rhino).
static class StaggeredBlockLayoutTests
{
    // Box 2 x 1 x 1, course 0.5, block 1.0, stagger 0.5, up = Z.
    private static IReadOnlyList<StaggeredCell> Sample()
        => StaggeredBlockLayout.Build(0, 0, 0, 2, 1, 1, 0.5, 1.0, 0.5, 2);

    public static void Build_ProducesTwoCourses()
    {
        var cells = Sample();
        int maxCourse = -1;
        foreach (var c in cells) if (c.Course > maxCourse) maxCourse = c.Course;
        Assert(maxCourse == 1, $"expected 2 courses (0..1), got max course {maxCourse}");
    }

    public static void Build_OddCourseIsStaggered()
    {
        var cells = Sample();
        // Course 0 first cell is a full block; course 1 first cell is clipped (< block length).
        double c0first = -1, c1first = -1;
        foreach (var c in cells)
        {
            if (c.Course == 0 && c.IndexInCourse == 0) c0first = c.SizeX;
            if (c.Course == 1 && c.IndexInCourse == 0) c1first = c.SizeX;
        }
        Assert(Math.Abs(c0first - 1.0) < 1e-9, $"course0 first cell should be full block 1.0, got {c0first}");
        Assert(c1first > 0 && c1first < 1.0 - 1e-9, $"course1 first cell should be clipped (<1.0) by the stagger, got {c1first}");
    }

    public static void Build_CellsStayWithinBox()
    {
        var cells = Sample();
        foreach (var c in cells)
        {
            Assert(c.MinX >= -1e-9 && c.MaxX <= 2 + 1e-9, "X out of box");
            Assert(c.MinY >= -1e-9 && c.MaxY <= 1 + 1e-9, "Y out of box");
            Assert(c.MinZ >= -1e-9 && c.MaxZ <= 1 + 1e-9, "Z out of box");
            Assert(c.SizeX > 1e-9 && c.SizeY > 1e-9 && c.SizeZ > 1e-9, "degenerate cell");
        }
    }

    public static void Build_DepthAxisSpansFull()
    {
        var cells = Sample();
        // Y is the depth axis (smaller non-up axis) -> every cell spans full Y.
        foreach (var c in cells)
            Assert(Math.Abs(c.MinY) < 1e-9 && Math.Abs(c.MaxY - 1) < 1e-9, "depth (Y) should span full box");
    }

    public static void Build_InvalidCourseHeight_Throws()
    {
        bool threw = false;
        try { StaggeredBlockLayout.Build(0, 0, 0, 1, 1, 1, 0, 1, 0.5, 2); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "course height <= 0 should throw");
    }

    private static void Assert(bool c, string m) { if (!c) throw new InvalidOperationException("StaggeredBlockLayout: " + m); }
}
