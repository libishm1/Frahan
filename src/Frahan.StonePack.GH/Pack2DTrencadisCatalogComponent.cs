#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using Frahan.GH.Attributes;
using Frahan.GH.TwoD;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH;

/// <summary>
/// Trencadís catalog mode (F-2D-002.F7).
///
/// A second component for the case when the user has a fixed catalog of
/// pieces and wants the packer to optimally assign one piece per
/// CVD-Lloyd "superpixel" cell, minimising area mismatch via the
/// Hungarian algorithm (Kuhn-Munkres O(n^3)).
///
/// This produces a "filled" trencadís rather than the "scattered"
/// trencadís that the greedy F2D-002 component produces. It is useful
/// when the catalog is sized to a target coverage.
///
/// Why a separate component (UX preserving):
///   • The greedy Trencadís component already has 17 inputs. Adding a
///     mode-toggle would push it past the readable threshold.
///   • Catalog mode's outputs include cell adjacencies which the greedy
///     component doesn't expose. Easier to give it its own surface.
///   • CVD-cell generation + Hungarian is a different solver — coupling
///     them inside the same SolveInstance would force a single Run flag
///     to switch between fundamentally different algorithms.
/// </summary>
[Algorithm("CVD-Lloyd interior seeding", "Lloyd 1982 centroidal Voronoi diagram relaxation", WikiPath = "wiki/algorithms/surface_mosaicing/primitives/cvd_lloyd.md")]
[Algorithm("Trencadis catalog placement", "Slab-partitioned Voronoi catalog; Frahan-original Trencadis extension")]
[DesignApplication(
    "Trencadís catalog packer: partition each sheet into CVD-Lloyd  cells, then optimally assign catalog parts t...",
    DesignFlow.BottomUp,
    Precedent = "Lloyd 1982 Centroidal Voronoi diagram (CVD); Battiato 2013 Trencadis synthesis")]
public sealed class Pack2DTrencadisCatalogComponent : GH_Component
{
    public Pack2DTrencadisCatalogComponent()
        : base("Frahan Trencadís Catalog Pack", "TrencadisCat",
            "Trencadís catalog packer: partition each sheet into CVD-Lloyd " +
            "cells, then optimally assign catalog parts to cells via the " +
            "Hungarian algorithm. Best when piece count matches target " +
            "coverage and you want each piece placed exactly once.",
            "Frahan", "Trencadis")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D00007-CADC-4F2D-9007-7E60CADA15A0");
    protected override Bitmap Icon => IconProvider.Load("Trencadis.png");
    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Parts", "P",
            "Catalog of irregular shard curves to place. Each piece will be " +
            "placed exactly once.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Sheet Outlines", "S",
            "Closed planar sheet boundary curves.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Sheet Holes", "H",
            "Hole curves as a tree. Branch {0} = sheet 0, etc.",
            GH_ParamAccess.tree);
        pManager[2].Optional = true;
        pManager.AddNumberParameter("Tolerance", "T",
            "Geometric tolerance.", GH_ParamAccess.item, 0.01);
        pManager.AddIntegerParameter("Seed", "Seed",
            "0 = deterministic.", GH_ParamAccess.item, 0);
        pManager.AddBooleanParameter("Run", "Run",
            "Set true to run the catalog assignment.",
            GH_ParamAccess.item, false);
        pManager.AddIntegerParameter("Lloyd Iterations", "Iter",
            "CVD-Lloyd relaxation iterations. Higher = more uniform cells.",
            GH_ParamAccess.item, 20);
        pManager.AddNumberParameter("Grout", "Gr",
            "Inward offset applied to each piece AFTER trim, to leave the " +
            "characteristic trencadís mortar gap. 0 = no grout. Default 0.02.",
            GH_ParamAccess.item, 0.02);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Placed Pieces", "C",
            "Catalog parts placed at their assigned cell centroids.",
            GH_ParamAccess.list);
        pManager.AddPointParameter("Cell Seeds", "X",
            "CVD-Lloyd seed centroids — one per assigned cell.",
            GH_ParamAccess.list);
        pManager.AddIntegerParameter("Source Indices", "Src",
            "Catalog index for each placed piece.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Sheet Indices", "Sh",
            "Sheet index for each placed piece.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Cell Areas", "A",
            "Approximate area of each assigned cell. Useful to spot " +
            "outliers where the piece is much smaller / bigger than the " +
            "cell.", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "R",
            "Catalog packing report.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var parts = new List<Curve>();
        var sheets = new List<Curve>();
        GH_Structure<GH_Curve> holesTree = null;
        double tol = 0.01;
        int seed = 0;
        bool run = false;
        int lloydIter = 20;
        double grout = 0.02;

        if (!da.GetDataList(0, parts)) return;
        if (!da.GetDataList(1, sheets)) return;
        da.GetDataTree(2, out holesTree);
        da.GetData(3, ref tol);
        da.GetData(4, ref seed);
        da.GetData(5, ref run);
        da.GetData(6, ref lloydIter);
        da.GetData(7, ref grout);

        if (!run)
        {
            da.SetData(5, "Run is false. Trencadís Catalog — set Run=true.");
            return;
        }
        if (sheets.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "At least one sheet outline is required.");
            return;
        }
        if (parts.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "At least one catalog part is required.");
            return;
        }

        var sw = Stopwatch.StartNew();
        var holesBySheet = SheetHolesUtil.BuildHolesBySheet(
            sheets, holesTree, sheets.Count, tol);

        // Per sheet: number of seeds proportional to area.
        var sheetAreas = new double[sheets.Count];
        var sheetPlanes = new Plane[sheets.Count];
        var sheetWorkCurves = new Curve[sheets.Count];
        var sheetWorkHoles = new List<Curve>[sheets.Count];
        var sheetBb = new BoundingBox[sheets.Count];
        var ptol = Math.Max(tol, 0.01);
        double totalArea = 0.0;
        for (int s = 0; s < sheets.Count; s++)
        {
            var outer = sheets[s];
            if (outer == null || !outer.IsClosed) continue;
            if (!outer.TryGetPlane(out var plane, ptol))
                if (!outer.TryGetPlane(out plane, ptol * 100)) continue;
            sheetPlanes[s] = plane;
            var toWork = Transform.PlaneToPlane(plane, Plane.WorldXY);
            var ow = outer.DuplicateCurve();
            ow.Transform(toWork);
            sheetWorkCurves[s] = ow;
            var hWorks = new List<Curve>();
            foreach (var h in holesBySheet[s])
            {
                if (h == null || !h.IsClosed) continue;
                var hw = h.DuplicateCurve();
                hw.Transform(toWork);
                hWorks.Add(hw);
            }
            sheetWorkHoles[s] = hWorks;
            var areaProps = AreaMassProperties.Compute(ow);
            var sheetA = areaProps?.Area ?? 0;
            foreach (var hw in hWorks)
                sheetA -= AreaMassProperties.Compute(hw)?.Area ?? 0;
            sheetAreas[s] = Math.Max(0, sheetA);
            sheetBb[s] = ow.GetBoundingBox(true);
            totalArea += sheetAreas[s];
        }

        // Allocate parts to sheets by area share.
        var sheetK = new int[sheets.Count];
        if (totalArea > tol * tol)
        {
            int remaining = parts.Count;
            for (int s = 0; s < sheets.Count - 1; s++)
            {
                if (sheetAreas[s] <= 0) continue;
                sheetK[s] = (int)Math.Round(parts.Count * sheetAreas[s] / totalArea);
                remaining -= sheetK[s];
            }
            sheetK[sheets.Count - 1] = Math.Max(0, remaining);
        }
        else
        {
            sheetK[0] = parts.Count;
        }

        // Build seeds per sheet, then build a global pool of (sheetIdx, seed)
        // that is exactly N entries wide so Hungarian sees a square problem.
        var globalCells = new List<(int sheetIdx, double sx, double sy, double areaApprox)>();
        for (int s = 0; s < sheets.Count; s++)
        {
            if (sheetWorkCurves[s] == null || sheetK[s] <= 0) continue;
            var outerPoly = CurveToVxVy(sheetWorkCurves[s], ptol, 256);
            if (outerPoly.vx == null || outerPoly.vx.Length < 3) continue;
            var holesAsTuples = new List<(double[] vx, double[] vy)>();
            foreach (var hw in sheetWorkHoles[s])
            {
                var hp = CurveToVxVy(hw, ptol, 128);
                if (hp.vx != null && hp.vx.Length >= 3) holesAsTuples.Add(hp);
            }
            var seeds = CvdLloyd2d.GenerateSeeds(
                outerPoly.vx, outerPoly.vy,
                holesAsTuples,
                sheetBb[s].Min.X, sheetBb[s].Min.Y, sheetBb[s].Max.X, sheetBb[s].Max.Y,
                K: sheetK[s], iterations: lloydIter, gridRes: 64,
                seed: seed == 0 ? s + 1 : seed + s);
            // Approximate cell area = sheet area / K.
            var cellArea = sheetK[s] > 0 ? sheetAreas[s] / sheetK[s] : 0;
            foreach (var (sx, sy) in seeds)
                globalCells.Add((s, sx, sy, cellArea));
        }

        if (globalCells.Count == 0)
        {
            da.SetData(5, "No valid CVD cells generated.");
            return;
        }

        // Pad parts/cells to equal length for Hungarian.
        int n = Math.Max(parts.Count, globalCells.Count);
        var partAreas = new double[n];
        var partBboxDiag = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (i >= parts.Count) { partAreas[i] = 0; partBboxDiag[i] = 0; continue; }
            var ap = AreaMassProperties.Compute(parts[i]);
            partAreas[i] = ap?.Area ?? 0;
            var bb = parts[i].GetBoundingBox(true);
            partBboxDiag[i] = bb.Diagonal.Length;
        }

        // Cost matrix: |cell_area - part_area| / max(...) in [0, 1].
        var maxAreaScale = 1.0;
        foreach (var a in partAreas) if (a > maxAreaScale) maxAreaScale = a;
        foreach (var (_, _, _, a) in globalCells) if (a > maxAreaScale) maxAreaScale = a;
        var cost = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (j >= globalCells.Count || i >= parts.Count)
                {
                    cost[i, j] = 1.0;
                    continue;
                }
                var diff = Math.Abs(partAreas[i] - globalCells[j].areaApprox);
                cost[i, j] = diff / maxAreaScale;
            }
        }

        var assign = HungarianAssignment.Solve(cost);

        // Place each assigned part at its cell centroid (in sheet frame).
        var placedCurves = new List<Curve>();
        var cellSeeds = new List<Point3d>();
        var srcIndices = new List<int>();
        var sheetIndices = new List<int>();
        var cellAreas = new List<double>();
        for (int i = 0; i < parts.Count; i++)
        {
            int j = assign[i];
            if (j < 0 || j >= globalCells.Count) continue;
            var (sIdx, sx, sy, cellArea) = globalCells[j];

            // Take the part to WorkXY first, THEN measure the bbox in that
            // frame. Doing it in the original world frame breaks whenever
            // the part isn't on Plane.WorldXY with a near-origin position,
            // because bbox.Min in world has nothing to do with the WorkXY
            // bbox after partToWork is applied.
            if (!parts[i].TryGetPlane(out var partPlane, ptol)) continue;
            var partToWork = Transform.PlaneToPlane(partPlane, Plane.WorldXY);
            var workToSheet = Transform.PlaneToPlane(Plane.WorldXY, sheetPlanes[sIdx]);

            var c = parts[i].DuplicateCurve();
            c.Transform(partToWork);
            var bbWork = c.GetBoundingBox(true);
            var w = bbWork.Max.X - bbWork.Min.X;
            var h = bbWork.Max.Y - bbWork.Min.Y;
            // Normalise to bbox-min at origin, then move bbox CENTRE to
            // (sx, sy). Equivalent to translating by (sx - bbWork.Min.X - w/2,
            // sy - bbWork.Min.Y - h/2) in one go.
            var translateX = sx - bbWork.Min.X - w * 0.5;
            var translateY = sy - bbWork.Min.Y - h * 0.5;
            c.Transform(Transform.Translation(translateX, translateY, 0));
            c.Transform(workToSheet);

            placedCurves.Add(c);
            // Seed point in sheet frame.
            var pt = new Point3d(sx, sy, 0);
            pt.Transform(workToSheet);
            cellSeeds.Add(pt);
            srcIndices.Add(i);
            sheetIndices.Add(sIdx);
            cellAreas.Add(cellArea);
        }

        // F-2D-002.F7 trim post-pass: each piece is boolean-differenced
        // against earlier-placed pieces it overlaps, producing the chip-fit
        // edges that make this look like trencadís rather than stamped tiles.
        // Earlier-index pieces win; later pieces get chipped where they
        // intrude on a cell that's already occupied.
        int trimEvents = ApplyTrimPostPass(placedCurves, tol);

        // Grout: inward offset by half the gap so each chipped piece shrinks
        // by half a grout width on every chipped edge, leaving the mortar
        // band when neighbours are placed.
        if (grout > tol) ApplyGroutOffset(placedCurves, grout, tol);

        sw.Stop();
        da.SetDataList(0, placedCurves);
        da.SetDataList(1, cellSeeds);
        da.SetDataList(2, srcIndices);
        da.SetDataList(3, sheetIndices);
        da.SetDataList(4, cellAreas);
        var report =
            $"Trencadís catalog pack: {placedCurves.Count}/{parts.Count} placed\n" +
            $"Sheets        : {sheets.Count}\n" +
            $"Cells (CVD)   : {globalCells.Count}\n" +
            $"Cost scale    : maxArea={maxAreaScale:F4}\n" +
            $"Lloyd iter    : {lloydIter}\n" +
            $"Trim events   : {trimEvents}\n" +
            $"Grout         : {grout:F4}\n" +
            $"Runtime       : {sw.ElapsedMilliseconds} ms\n";
        da.SetData(5, report);
    }

    // F-2D-002.F7 trim post-pass — mirrors TrencadisFill.ApplyTrimPostPass
    // but without the cumulative-area budget tracking (catalog mode places
    // exactly one piece per cell, no overlap budget needed). Earlier pieces
    // win every overlap.
    private static int ApplyTrimPostPass(List<Curve> placed, double tol)
    {
        var planeTol = Math.Max(tol, 0.001);
        int events = 0;
        int n = placed.Count;
        for (int j = 1; j < n; j++)
        {
            var laterCurve = placed[j];
            if (laterCurve == null || !laterCurve.IsClosed) continue;
            for (int i = 0; i < j; i++)
            {
                var earlierCurve = placed[i];
                if (earlierCurve == null || !earlierCurve.IsClosed) continue;
                var bbI = earlierCurve.GetBoundingBox(false);
                var bbJ = laterCurve.GetBoundingBox(false);
                if (bbI.Max.X < bbJ.Min.X - tol || bbI.Min.X > bbJ.Max.X + tol ||
                    bbI.Max.Y < bbJ.Min.Y - tol || bbI.Min.Y > bbJ.Max.Y + tol) continue;

                Curve[] diff = null;
                try { diff = Curve.CreateBooleanDifference(laterCurve, earlierCurve, planeTol); }
                catch { diff = null; }
                if (diff == null || diff.Length == 0) continue;

                Curve best = null;
                double bestArea = 0;
                foreach (var d in diff)
                {
                    if (d == null || !d.IsClosed) continue;
                    var area = AreaMassProperties.Compute(d)?.Area ?? 0;
                    if (area > bestArea) { bestArea = area; best = d; }
                }
                if (best != null)
                {
                    placed[j] = best;
                    laterCurve = best;
                    events++;
                }
            }
        }
        return events;
    }

    private static void ApplyGroutOffset(List<Curve> placed, double grout, double tol)
    {
        var inset = grout * 0.5;
        var planeTol = Math.Max(tol, 0.001);
        for (int i = 0; i < placed.Count; i++)
        {
            var c = placed[i];
            if (c == null || !c.IsClosed) continue;
            if (!c.TryGetPlane(out var plane, planeTol)) continue;

            Curve[] off;
            try { off = c.Offset(plane, -inset, planeTol, CurveOffsetCornerStyle.Sharp); }
            catch { off = null; }
            if (off == null || off.Length == 0)
            {
                try { off = c.Offset(plane, inset, planeTol, CurveOffsetCornerStyle.Sharp); }
                catch { off = null; }
            }
            if (off == null || off.Length == 0) continue;

            var origArea = AreaMassProperties.Compute(c)?.Area ?? 0;
            Curve picked = null;
            double pickedArea = double.MaxValue;
            foreach (var o in off)
            {
                if (o == null || !o.IsClosed) continue;
                var a = AreaMassProperties.Compute(o)?.Area ?? 0;
                if (a > 0 && a < origArea && a < pickedArea)
                {
                    pickedArea = a;
                    picked = o;
                }
            }
            if (picked != null) placed[i] = picked;
        }
    }

    private static (double[] vx, double[] vy) CurveToVxVy(Curve curve, double tol, int maxVerts)
    {
        if (curve.TryGetPolyline(out var pl))
        {
            var n = pl.Count;
            if (n > 1 && pl[0].DistanceTo(pl[n - 1]) < tol) n--;
            if (n < 3) return (null, null);
            if (n > maxVerts)
            {
                var vx = new double[maxVerts]; var vy = new double[maxVerts];
                var step = (double)n / maxVerts;
                for (int i = 0; i < maxVerts; i++)
                {
                    var idx = Math.Min(n - 1, (int)(i * step));
                    vx[i] = pl[idx].X; vy[i] = pl[idx].Y;
                }
                return (vx, vy);
            }
            var vxn = new double[n]; var vyn = new double[n];
            for (int i = 0; i < n; i++) { vxn[i] = pl[i].X; vyn[i] = pl[i].Y; }
            return (vxn, vyn);
        }
        var divPar = curve.DivideByCount(Math.Min(maxVerts, 128), false);
        if (divPar == null || divPar.Length < 3) return (null, null);
        var vxd = new double[divPar.Length]; var vyd = new double[divPar.Length];
        for (int i = 0; i < divPar.Length; i++)
        {
            var p = curve.PointAt(divPar[i]);
            vxd[i] = p.X; vyd[i] = p.Y;
        }
        return (vxd, vyd);
    }
}
