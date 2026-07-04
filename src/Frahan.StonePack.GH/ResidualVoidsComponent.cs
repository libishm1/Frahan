using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Core;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH;

/// <summary>
/// Detects residual 2D voids inside a sheet polygon that no placed part covers.
/// Wraps Frahan.Core.ResidualVoidsDetector. Outputs the bounding rectangles of
/// each detected void plus aggregate statistics.
///
/// Spec 5 section 5; runbook section 16.1 component family
/// "Frahan Residual Voids".
/// </summary>
[DesignApplication(
    "Detect 2D residual voids inside a sheet polygon not covered by any  placed part",
    DesignFlow.BottomUp,
    Precedent = "Frahan-original residual-voids analyser post-packing")]
[Algorithm("Grid sampling + connected-component void detection", "Frahan-original",
    Note = "cell-grid sampling plus 4-neighbour connected-component labelling applied as a Frahan-original void metric")]
public sealed class ResidualVoidsComponent : FrahanComponentBase
{
    public ResidualVoidsComponent()
        : base("Residual Voids", "ResVoid",
            "Detect 2D residual voids inside a sheet polygon not covered by any " +
            "placed part. Uses cell-grid sampling + 4-neighbour connected-component " +
            "labelling. Reports each void's bounding rectangle and approximate area; " +
            "small voids below MinArea are filtered. Frahan-original method.",
            "Frahan", "2D Packing")
    {
    }

    public override Guid ComponentGuid => new Guid("AB12C002-1A2B-4C3D-9E4F-5A6B7C8D9E02");
    protected override Bitmap? Icon => IconProvider.Load("PackMetrics.png");
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Sheet", "S",
            "Closed planar curve representing the sheet outline.",
            GH_ParamAccess.item);
        pManager.AddCurveParameter("Placed Parts", "P",
            "Closed planar curves representing already-placed parts.",
            GH_ParamAccess.list);
        pManager.AddNumberParameter("Cell Size", "C",
            "Sampling cell size in model units. Smaller = more accurate, slower.",
            GH_ParamAccess.item, 5.0);
        pManager.AddNumberParameter("Min Void Area", "M",
            "Minimum reportable void area in model-unit-squared. " +
            "Smaller voids are filtered.",
            GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Discretisation Tolerance", "T",
            "Tolerance used when discretising the sheet and parts to polylines.",
            GH_ParamAccess.item, 0.01);
        pManager[1].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddRectangleParameter("Void Bounds", "B",
            "Axis-aligned bounding rectangle of each detected void.",
            GH_ParamAccess.list);
        pManager.AddNumberParameter("Void Areas", "A",
            "Approximate area of each detected void.",
            GH_ParamAccess.list);
        pManager.AddIntegerParameter("Cell Counts", "N",
            "Cells per detected void.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Total Void Area", "Tv",
            "Sum of all reported void areas.", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R",
            "Human-readable summary.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Curve? sheet = null;
        var parts = new List<Curve>();
        double cellSize = 5.0;
        double minArea = 0.0;
        double tol = 0.01;

        if (!da.GetData(0, ref sheet) || sheet == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Sheet curve required.");
            return;
        }
        da.GetDataList(1, parts);
        da.GetData(2, ref cellSize);
        da.GetData(3, ref minArea);
        da.GetData(4, ref tol);

        if (cellSize <= 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Cell Size must be > 0.");
            return;
        }
        if (minArea < 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Min Void Area must be >= 0.");
            return;
        }

        // Convert curves to flat double polygons.
        if (!TryCurveToFlat(sheet, tol, out var sheetFlat))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Sheet curve could not be discretised (must be a closed planar curve).");
            return;
        }

        var partsFlat = new List<IReadOnlyList<double>>(parts.Count);
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            if (p == null) continue;
            if (TryCurveToFlat(p, tol, out var pf))
                partsFlat.Add(pf);
            else
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Part {i} could not be discretised; skipped.");
        }

        ResidualVoidsDetector detector;
        try
        {
            detector = new ResidualVoidsDetector(cellSize, minArea);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        IReadOnlyList<ResidualVoid> voids;
        try
        {
            voids = detector.Detect(sheetFlat, partsFlat);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Detection failed: " + ex.Message);
            return;
        }

        var bounds = new List<Rectangle3d>(voids.Count);
        var areas = new List<double>(voids.Count);
        var cells = new List<int>(voids.Count);
        double totalArea = 0.0;
        var plane = Plane.WorldXY;
        for (int i = 0; i < voids.Count; i++)
        {
            var v = voids[i];
            bounds.Add(new Rectangle3d(plane,
                new Interval(v.MinX, v.MaxX),
                new Interval(v.MinY, v.MaxY)));
            areas.Add(v.ApproximateArea);
            cells.Add(v.CellCount);
            totalArea += v.ApproximateArea;
        }

        da.SetDataList(0, bounds);
        da.SetDataList(1, areas);
        da.SetDataList(2, cells);
        da.SetData(3, totalArea);
        da.SetData(4,
            $"ResidualVoids: {voids.Count} regions, total area {totalArea:0.##}, " +
            $"cell size {cellSize}, min area filter {minArea}.");
    }

    private static bool TryCurveToFlat(Curve curve, double tolerance, out double[] flat)
    {
        flat = Array.Empty<double>();
        if (curve == null) return false;

        // Project to WorldXY plane via the curve's bounding box midpoint Z.
        // (For truly off-plane inputs, callers should project first.)
        // RhinoCommon Curve.ToPolyline overload:
        //   (int mainSegmentCount, int subSegmentCount, double maxAngleRadians,
        //    double maxChordLengthRatio, double maxAspectRatio, double tolerance,
        //    double minEdgeLength, double maxEdgeLength, bool keepStartPoint)
        // 0/0/0.0/0.0/0.0 = "no per-criterion limit; use tolerance only".
        var polylineCurve = curve.ToPolyline(0, 0, 0.0, 0.0, 0.0, tolerance, 0.0, 0.0, true);
        if (polylineCurve == null) return false;
        if (!polylineCurve.TryGetPolyline(out var poly) || poly.Count < 4) return false;

        // Drop the closing duplicate vertex if present.
        int n = poly.Count;
        if (poly[0].EpsilonEquals(poly[n - 1], 1e-9)) n--;
        if (n < 3) return false;

        var arr = new double[n * 2];
        for (int i = 0; i < n; i++)
        {
            arr[2 * i] = poly[i].X;
            arr[2 * i + 1] = poly[i].Y;
        }
        flat = arr;
        return true;
    }
}
