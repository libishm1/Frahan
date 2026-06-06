#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Frahan.Core;

namespace Frahan.Tests;

// Unit tests for Frahan.Core.ResidualVoidsDetector + ResidualVoid DTO.
// Pure managed; no Rhino runtime required.

static class ResidualVoidsDetectorTests
{
    // -- Constructor guards --------------------------------------------------

    public static void Ctor_NonPositiveCellSize_Throws()
    {
        bool threw = false;
        try { _ = new ResidualVoidsDetector(0.0); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "cellSize <= 0 should throw");
    }

    public static void Ctor_NegativeMinArea_Throws()
    {
        bool threw = false;
        try { _ = new ResidualVoidsDetector(1.0, -1.0); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "minVoidArea < 0 should throw");
    }

    // -- Empty sheet (no parts) ---------------------------------------------

    public static void Detect_EmptySheet_ReturnsOneFullVoid()
    {
        // 100x100 square sheet, no placed parts -> one void = the whole sheet.
        var detector = new ResidualVoidsDetector(cellSize: 10.0);
        var sheet = Square(0, 0, 100, 100);
        var voids = detector.Detect(sheet, Array.Empty<IReadOnlyList<double>>());

        Assert(voids.Count == 1, $"empty sheet should produce 1 void, got {voids.Count}");
        Assert(voids[0].CellCount == 100, $"expected 10x10 = 100 cells, got {voids[0].CellCount}");
        Assert(Math.Abs(voids[0].ApproximateArea - 10000.0) < 1e-6,
            $"expected approx area 10000, got {voids[0].ApproximateArea}");
    }

    // -- Fully covered sheet ------------------------------------------------

    public static void Detect_FullyCoveredSheet_ReturnsZeroVoids()
    {
        var detector = new ResidualVoidsDetector(cellSize: 10.0);
        var sheet = Square(0, 0, 100, 100);
        var part = Square(0, 0, 100, 100); // exact same polygon covers the sheet
        var voids = detector.Detect(sheet, new[] { (IReadOnlyList<double>)part });
        Assert(voids.Count == 0, $"fully covered sheet should produce 0 voids, got {voids.Count}");
    }

    // -- Single hole --------------------------------------------------------

    public static void Detect_SingleCornerHole_ReturnsOneVoid()
    {
        // 100x100 sheet; place a single 80x100 part at the left -> one void on
        // the right of width 20.
        var detector = new ResidualVoidsDetector(cellSize: 10.0);
        var sheet = Square(0, 0, 100, 100);
        var part = Square(0, 0, 80, 100);
        var voids = detector.Detect(sheet, new[] { (IReadOnlyList<double>)part });

        Assert(voids.Count == 1, $"expected 1 void on the right, got {voids.Count}");
        var v = voids[0];
        Assert(v.MinX >= 79 - 1e-6 && v.MinX <= 81 + 1e-6, $"void MinX should be ~80, got {v.MinX}");
        Assert(v.MaxX >= 99 - 1e-6, $"void MaxX should be ~100, got {v.MaxX}");
        Assert(v.CellCount == 20, $"expected 20 cells (2 cols x 10 rows), got {v.CellCount}");
    }

    // -- Two separated voids ------------------------------------------------

    public static void Detect_TwoSeparatedVoids_ReturnsTwo()
    {
        // 100x100 sheet; place a vertical strip across the middle (x=40..60) so
        // there are two voids, one on each side.
        var detector = new ResidualVoidsDetector(cellSize: 10.0);
        var sheet = Square(0, 0, 100, 100);
        var part = Square(40, 0, 60, 100);
        var voids = detector.Detect(sheet, new[] { (IReadOnlyList<double>)part });
        Assert(voids.Count == 2, $"expected 2 voids on each side, got {voids.Count}");

        var areas = voids.Select(x => x.ApproximateArea).OrderBy(a => a).ToList();
        // Each void is 4 cells x 10 cells = 40 cells x 100 area = 4000.
        Assert(Math.Abs(areas[0] - 4000.0) < 1e-6, $"smaller void area should be ~4000, got {areas[0]}");
        Assert(Math.Abs(areas[1] - 4000.0) < 1e-6, $"larger void area should be ~4000, got {areas[1]}");
    }

    // -- MinVoidArea filter -------------------------------------------------

    public static void Detect_FiltersOutVoidsBelowMinArea()
    {
        // Same two-void scenario, but with minVoidArea = 5000 -> both voids
        // are below the threshold and get filtered.
        var detector = new ResidualVoidsDetector(cellSize: 10.0, minVoidArea: 5000.0);
        var sheet = Square(0, 0, 100, 100);
        var part = Square(40, 0, 60, 100);
        var voids = detector.Detect(sheet, new[] { (IReadOnlyList<double>)part });
        Assert(voids.Count == 0, $"voids below minVoidArea should be filtered, got {voids.Count}");
    }

    // -- L-shape sheet ------------------------------------------------------

    public static void Detect_LShapeSheet_RespectsConcaveOutline()
    {
        // L-shaped sheet (100x100 minus the upper-right 50x50 corner).
        // No parts placed. The detected void should not include cells in the
        // missing corner.
        var detector = new ResidualVoidsDetector(cellSize: 10.0);
        var lshape = new double[]
        {
            0, 0,
            100, 0,
            100, 50,
            50, 50,
            50, 100,
            0, 100,
        };
        var voids = detector.Detect(lshape, Array.Empty<IReadOnlyList<double>>());
        Assert(voids.Count == 1, $"L-shape with no parts should be one connected void, got {voids.Count}");
        // L-shape area = 100*50 + 50*50 = 7500. Cells = 75.
        Assert(voids[0].CellCount == 75, $"expected 75 cells in L-shape, got {voids[0].CellCount}");
    }

    // -- Argument guards ----------------------------------------------------

    public static void Detect_NullSheet_Throws()
    {
        var detector = new ResidualVoidsDetector(cellSize: 10.0);
        bool threw = false;
        try { _ = detector.Detect(null, Array.Empty<IReadOnlyList<double>>()); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null sheet should throw ArgumentNullException");
    }

    public static void Detect_NullPlacedPartsList_Throws()
    {
        var detector = new ResidualVoidsDetector(cellSize: 10.0);
        var sheet = Square(0, 0, 10, 10);
        bool threw = false;
        try { _ = detector.Detect(sheet, null); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null placed-parts list should throw ArgumentNullException");
    }

    public static void Detect_DegenerateSheet_Throws()
    {
        var detector = new ResidualVoidsDetector(cellSize: 10.0);
        bool threw = false;
        try { _ = detector.Detect(new double[] { 0, 0, 1, 1 }, Array.Empty<IReadOnlyList<double>>()); }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "sheet with only 2 vertices should throw ArgumentException");
    }

    // -- Helpers ------------------------------------------------------------

    private static double[] Square(double x0, double y0, double x1, double y1) =>
        new[] { x0, y0, x1, y0, x1, y1, x0, y1 };

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
