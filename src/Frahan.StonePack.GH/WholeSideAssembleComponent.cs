using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using Frahan.EdgeMatching;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH;

/// <summary>
/// Whole-side, best-first reassembler. Takes an anchor part and a pool of scattered
/// or rotated parts (all coplanar in world XY), decomposes each into whole contour
/// SIDES between min-area-rect corners, scores complementary side pairs by a
/// length-normalized shape distance, and grows the assembly from the anchor by a
/// best-first seam mate with overlap rejection. Unlike <c>EdgeMatch Solve</c> (which
/// matches curvature-broken segment fragments), this compares the WHOLE seam shape,
/// which separates true neighbours from look-alikes on hard pieces. 2D only.
/// </summary>
[RelatedComponent("Frahan > EdgeMatch > EdgeMatch Solve",
    Reason = "ALTERNATIVE SOLVER: segment/ICP/beam pipeline for frame-anchored Trencadís; " +
             "Whole-Side Assemble is the whole-seam + best-first path for free reassembly of jigsaw-like parts.")]
[Algorithm("Whole-side corner/side extraction",
    "Minimum-area bounding rectangle (rotating calipers) corners + flat-border (is_edge) exclusion",
    Note = "robust to wavy seams + rotation")]
[Algorithm("Whole-side shape fit",
    "Charnas ryan-puzzle-solver error_between_polylines: length-normalized index-L1 over endpoint-chord canonical frames")]
[Algorithm("Best-first seam-mate assembly",
    "Frahan-original deterministic best-first grow with 2-point rigid mate + Curve.Contains overlap rejection")]
[DesignApplication(
    "Reassemble scattered/rotated parts by matching whole contour sides, anchored to a seed part.",
    DesignFlow.BottomUp,
    Precedent = "Charnas ryan-puzzle-solver (all-white jigsaw robot, 2022-24)")]
public sealed class WholeSideAssembleComponent : FrahanComponentBase
{
    public WholeSideAssembleComponent()
        : base("Whole-Side Assemble", "WSAssemble",
            "Reassembles scattered/rotated coplanar parts by matching WHOLE contour " +
            "sides (corner-to-corner) and growing best-first from an anchor part. " +
            "Outputs placed contours and per-part transforms. 2D (world XY) only.",
            "Frahan", "EdgeMatch")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F10021-ED9E-4ED9-A021-ED9EED9E0021");
    protected override Bitmap? Icon => IconProvider.Load("WholeSideAssemble.png");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Anchor", "A",
            "Anchor part (placed first at its current position; the assembly grows from it).",
            GH_ParamAccess.item);
        pManager.AddCurveParameter("Parts", "P",
            "Closed part contours to reassemble (scattered / rotated, coplanar in world XY).",
            GH_ParamAccess.list);
        pManager.AddNumberParameter("Fit Gate", "G",
            "Maximum length-normalized side-fit cost admitted to the search. " +
            "Default 2.5 (must exceed the highest TRUE seam cost; too low orphans far parts).",
            GH_ParamAccess.item, 2.5);
        pManager.AddBooleanParameter("Run", "R",
            "Execute the assembler.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Placed", "Pl",
            "Part contours transformed by their solved placements (anchor included).",
            GH_ParamAccess.list);
        pManager.AddTransformParameter("Transforms", "X",
            "Per-part rigid transform, matching the Placed / Ids order.", GH_ParamAccess.list);
        pManager.AddTextParameter("Ids", "Id",
            "Part ids in placement order.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Total Residual", "Tr",
            "Sum of accepted side-fit costs.", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "Rp",
            "Human-readable solve summary.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Curve? anchorCurve = null;
        var partCurves = new List<Curve>();
        double fitGate = 2.5;
        bool run = false;

        if (!da.GetData(0, ref anchorCurve)) return;
        if (!da.GetDataList(1, partCurves)) return;
        da.GetData(2, ref fitGate);
        da.GetData(3, ref run);

        if (!run)
        {
            da.SetData(4, "Run is false; toggle to execute.");
            return;
        }

        if (!TryToPolylineCurve(anchorCurve, out var anchorPc) || !anchorPc.IsClosed)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Anchor must be a closed polyline (use Curve to Polyline upstream if needed).");
            return;
        }
        // The solver works in world XY (it reads X/Y directly, mates about +Z, and tests
        // overlap against Plane.WorldXY). Panel.Mode == Planar2D only means "flat against
        // its own best-fit plane" -- a flat part TILTED out of XY would pass that test yet
        // project to a distorted shadow. Enforce the actual world-XY precondition here.
        if (!IsCoplanarXY(anchorPc))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Anchor is not flat in the world XY plane (its Z varies). The whole-side solver " +
                "is world-XY only; orient the parts into the XY plane first.");
            return;
        }
        var anchorPanel = new Panel("anchor", anchorPc, PanelKind.Shard);

        var partPanels = new List<Panel>(partCurves.Count);
        int skipped = 0;
        for (int i = 0; i < partCurves.Count; i++)
        {
            if (!TryToPolylineCurve(partCurves[i], out var pc) || !pc.IsClosed)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Part {i} is not a closed polyline; skipping.");
                skipped++;
                continue;
            }
            if (!IsCoplanarXY(pc))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Part {i} is not flat in the world XY plane (its Z varies); the solver is world-XY only. Skipping.");
                skipped++;
                continue;
            }
            var panel = new Panel($"p{i:D4}", pc, PanelKind.Shard);
            if (panel.Mode != PanelMode.Planar2D)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Part {i} is non-planar (Spatial3D); the whole-side solver is 2D only. Skipping.");
                skipped++;
                continue;
            }
            partPanels.Add(panel);
        }

        if (partPanels.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No usable planar part panels.");
            return;
        }

        var opt = new AssemblyOptions { WholeSideFitGate = fitGate };
        var state = new BestFirstAssembler(opt).Solve(new[] { anchorPanel }, partPanels);

        var placed = new List<Curve>();
        var transforms = new List<object>();
        var ids = new List<string>();
        foreach (var panel in state.PlacedPanels)
        {
            var t = state.AppliedTransforms[panel.Id];
            var c = (Curve)panel.SourceContour.DuplicateCurve();
            c.Transform(t);
            placed.Add(c);
            transforms.Add(new GH_Transform(t));
            ids.Add(panel.Id);
        }

        int total = partPanels.Count + 1;
        var report = new StringBuilder();
        report.AppendLine($"Whole-Side Assemble: placed {state.PlacedPanels.Count}/{total} parts (1 anchor + {partPanels.Count} pool).");
        if (skipped > 0) report.AppendLine($"Skipped {skipped} non-closed / non-planar input(s).");
        report.AppendLine($"Fit gate: {fitGate:F2}");
        report.AppendLine($"Total side-fit residual: {state.TotalResidual:F4}");
        if (state.PlacedPanels.Count < total)
            report.AppendLine($"WARNING: {total - state.PlacedPanels.Count} part(s) could not be placed (raise Fit Gate or check part planarity / corners).");

        da.SetDataList(0, placed);
        da.SetDataList(1, transforms);
        da.SetDataList(2, ids);
        da.SetData(3, state.TotalResidual);
        da.SetData(4, report.ToString());
    }

    // True when the closed contour lies flat in (a horizontal offset of) the world XY
    // plane -- i.e. its Z extent is negligible relative to its XY size. A uniform Z
    // offset is fine (overlap projects to XY); a TILT (varying Z) is what we reject.
    private static bool IsCoplanarXY(PolylineCurve pc)
    {
        var bb = pc.GetBoundingBox(false);
        var d = bb.Diagonal;
        double xy = Math.Sqrt(d.X * d.X + d.Y * d.Y);
        double ztol = Math.Max(1e-6, 0.01 * xy);
        return d.Z <= ztol;
    }

    private static bool TryToPolylineCurve(Curve? c, out PolylineCurve pc)
    {
        pc = null!;
        if (c == null) return false;
        if (c is PolylineCurve already) { pc = already; return true; }
        if (c.TryGetPolyline(out Polyline poly))
        {
            pc = poly.ToPolylineCurve();
            return true;
        }
        return false;
    }
}
