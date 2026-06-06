#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Frahan.GH.TwoD;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH;

/// <summary>
/// Exact No-Fit-Polygon Bottom-Left-Fill nester. Sibling of the V506 solver; V506
/// is untouched. Builds the complete feasible region per part and rotation
/// (IFP minus the union of no-fit polygons) and places at its bottom-left vertex,
/// so non-overlap is a hard constraint by construction, not a post-hoc trim.
/// Validated against V506 at outputs/2026-06-03/pack2d_nfp_evolution/ (mean
/// wasted-area cut 53.9%, zero overlap, Python reference).
/// </summary>
[Algorithm("Exact No-Fit-Polygon Bottom-Left-Fill (hard non-overlap by construction)",
    "Burke, E.K., Hellier, R., Kendall, G., Whitwell, G. (2006). \"A New Bottom-Left-Fill Heuristic Algorithm for the Two-Dimensional Irregular Packing Problem.\" Operations Research 54(3):587-601",
    Doi = "10.1287/opre.1060.0293")]
[Algorithm("No-fit-polygon / inner-fit-polygon via Minkowski sum",
    "Bennell, J.A. & Oliveira, J.F. (2009). \"A tutorial in irregular shape packing problems.\" J. Oper. Res. Soc. 60(S1):S93-S105",
    Doi = "10.1057/jors.2008.169",
    WikiPath = "wiki/index/references.md#BennellOliveira2008")]
[Algorithm("Clipper2 polygon Minkowski sum + Boolean back-end",
    "Johnson, A. Clipper2 (BSL-1.0); Minkowski sum + NonZero Boolean operations")]
public sealed class IrregularSheetFillNfpBlfComponent : GH_TaskCapableComponent<PackingResult>
{
    public IrregularSheetFillNfpBlfComponent()
        : base("Freeform Sheet Nest (Exact NFP)", "FreeNestX",
            "Packs closed planar parts into freeform sheets using an exact No-Fit-Polygon " +
            "Bottom-Left-Fill solver. The feasible region for each part is the inner-fit polygon " +
            "minus the union of no-fit polygons of placed parts and holes, so parts never overlap " +
            "by construction (a hard constraint, not a trim). Implements bottom-left-fill " +
            "(Burke et al. 2006) over Minkowski-sum NFP/IFP (Bennell & Oliveira 2009) on a Clipper2 " +
            "back-end. Sibling of the V506 nester; V506 is unchanged.",
            "Frahan", "2D Packing")
    {
    }

    public override Guid ComponentGuid => new Guid("2d351646-2cb0-402a-bbd8-3950b5bb1fbc");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("Pack2D.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Parts", "P", "Closed planar part curves to pack.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Sheet Outlines", "S", "Closed planar sheet boundary curves.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Sheet Holes", "H", "Hole curves as a tree. Branch {i} = sheet i.", GH_ParamAccess.tree);
        pManager[2].Optional = true;
        pManager.AddNumberParameter("Spacing", "Gap", "Clearance between parts and boundaries.", GH_ParamAccess.item, 0.1);
        pManager.AddNumberParameter("Rotations", "R", "Allowed rotation angles in degrees.", GH_ParamAccess.list, 0.0);
        pManager.AddIntegerParameter("Sort Mode", "M", "0 UserOrder, 1 Area↓, 2 Width↓, 3 Height↓, 4 MaxDim↓.", GH_ParamAccess.item, 1);
        pManager.AddNumberParameter("Tolerance", "T", "Geometric tolerance. 0 (default) = AUTO: use the active document's absolute tolerance (mm doc -> mm tol, m doc -> m tol). Set a positive value to override.", GH_ParamAccess.item, 0.0);
        pManager.AddIntegerParameter("Seed", "Seed", "0 = deterministic. Non-zero changes tie-break.", GH_ParamAccess.item, 0);
        pManager.AddBooleanParameter("Run", "Run", "Set true to execute packing.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Packed Curves", "C", "Placed part curves.", GH_ParamAccess.list);
        pManager.AddGenericParameter("Transforms", "X", "Placement transforms per source curve.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Source Indices", "Src", "Original input index for each packed curve.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Sheet Indices", "Sh", "Sheet index for each packed curve.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Unplaced", "U", "Curves that could not be placed.", GH_ParamAccess.list);
        pManager.AddTextParameter("Failure Reasons", "Why", "Reason for each unplaced curve.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Sheet Preview", "B", "Outer sheet and hole preview curves.", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "R", "Packing report.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (InPreSolve)
        {
            var parts        = new List<Curve>();
            var sheets       = new List<Curve>();
            GH_Structure<GH_Curve> holesTree = null;
            var spacing      = 0.1;
            var rotationsDeg = new List<double>();
            var sortModeVal  = 1;
            var tolerance    = 0.0;
            var seed         = 0;
            var run          = false;

            if (!da.GetDataList(0, parts))  return;
            if (!da.GetDataList(1, sheets)) return;
            da.GetDataTree(2, out holesTree);
            da.GetData(3, ref spacing);
            da.GetDataList(4, rotationsDeg);
            da.GetData(5, ref sortModeVal);
            da.GetData(6, ref tolerance);
            // AUTO tolerance (input left at 0): scale-relative epsilon = 1e-4 of the sheet diagonal,
            // tightened to the document tolerance when finer. Raw doc tolerance alone (0.01 m in a
            // metre model) is too loose and lets exact-NFP parts overlap; scale-relative stays 0-overlap.
            if (tolerance <= 0.0)
            {
                var autoBox = Rhino.Geometry.BoundingBox.Empty;
                foreach (var sc in sheets) if (sc != null) autoBox.Union(sc.GetBoundingBox(true));
                double scaleRel = autoBox.IsValid ? autoBox.Diagonal.Length * 1e-4 : 0.0;
                double docT = 0.001;
                var activeDoc = Rhino.RhinoDoc.ActiveDoc;
                if (activeDoc != null && activeDoc.ModelAbsoluteTolerance > 0.0) docT = activeDoc.ModelAbsoluteTolerance;
                tolerance = scaleRel > 0.0 ? System.Math.Max(1e-6, System.Math.Min(docT, scaleRel)) : docT;
            }
            da.GetData(7, ref seed);
            da.GetData(8, ref run);

            if (!run)
            {
                da.SetDataList(6, SheetHolesUtil.BuildPreview(sheets, holesTree, sheets.Count, tolerance));
                da.SetData(7, "Run is false.");
                return;
            }
            if (sheets.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one sheet outline is required.");
                return;
            }

            var capturedParts  = parts.Select(c => c.DuplicateCurve()).ToList();
            var capturedSheets = sheets.Select(c => c.DuplicateCurve()).ToList();
            var holesBySheet   = SheetHolesUtil.BuildHolesBySheet(sheets, holesTree, sheets.Count, tolerance);
            var sortMode       = ToSortMode(sortModeVal);
            if (rotationsDeg.Count == 0) rotationsDeg.AddRange(new[] { 0.0, 90.0, 180.0, 270.0 });
            var capturedRots   = rotationsDeg.ToList();
            var capturedHoles  = holesBySheet
                .Select(l => (IReadOnlyList<Curve>)l.Select(c => c.DuplicateCurve()).ToList())
                .ToList();
            var capSpacing = spacing; var capTol = tolerance; var capSeed = seed;

            TaskList.Add(Task.Run(() =>
            {
                var solver = new IrregularSheetFillNfpBlf(
                    capturedSheets, capturedHoles, capSpacing, capturedRots, capTol, sortMode, capSeed);
                return solver.Pack(capturedParts);
            }));
            return;
        }

        PackingResult result;
        if (!GetSolveResults(da, out result))
        {
            // Synchronous fallback.
            var parts        = new List<Curve>();
            var sheets       = new List<Curve>();
            GH_Structure<GH_Curve> holesTree = null;
            var spacing      = 0.1;
            var rotationsDeg = new List<double>();
            var sortModeVal  = 1;
            var tolerance    = 0.0;
            var seed         = 0;
            var run          = false;

            if (!da.GetDataList(0, parts))  return;
            if (!da.GetDataList(1, sheets)) return;
            da.GetDataTree(2, out holesTree);
            da.GetData(3, ref spacing);
            da.GetDataList(4, rotationsDeg);
            da.GetData(5, ref sortModeVal);
            da.GetData(6, ref tolerance);
            // AUTO tolerance (input left at 0): scale-relative epsilon = 1e-4 of the sheet diagonal,
            // tightened to the document tolerance when finer. Raw doc tolerance alone (0.01 m in a
            // metre model) is too loose and lets exact-NFP parts overlap; scale-relative stays 0-overlap.
            if (tolerance <= 0.0)
            {
                var autoBox = Rhino.Geometry.BoundingBox.Empty;
                foreach (var sc in sheets) if (sc != null) autoBox.Union(sc.GetBoundingBox(true));
                double scaleRel = autoBox.IsValid ? autoBox.Diagonal.Length * 1e-4 : 0.0;
                double docT = 0.001;
                var activeDoc = Rhino.RhinoDoc.ActiveDoc;
                if (activeDoc != null && activeDoc.ModelAbsoluteTolerance > 0.0) docT = activeDoc.ModelAbsoluteTolerance;
                tolerance = scaleRel > 0.0 ? System.Math.Max(1e-6, System.Math.Min(docT, scaleRel)) : docT;
            }
            da.GetData(7, ref seed);
            da.GetData(8, ref run);

            if (!run)
            {
                da.SetDataList(6, SheetHolesUtil.BuildPreview(sheets, holesTree, sheets.Count, tolerance));
                da.SetData(7, "Run is false.");
                return;
            }
            if (sheets.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one sheet outline is required.");
                return;
            }

            var holesBySheet = SheetHolesUtil.BuildHolesBySheet(sheets, holesTree, sheets.Count, tolerance);
            if (rotationsDeg.Count == 0) rotationsDeg.AddRange(new[] { 0.0, 90.0, 180.0, 270.0 });
            try
            {
                var solver = new IrregularSheetFillNfpBlf(
                    sheets, holesBySheet.Select(l => (IReadOnlyList<Curve>)l).ToList(),
                    spacing, rotationsDeg, tolerance, ToSortMode(sortModeVal), seed);
                result = solver.Pack(parts);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Exact NFP packing failed: " + ex.Message);
                return;
            }
        }

        if (result.UnplacedCurves.Count > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{result.UnplacedCurves.Count} part(s) could not be placed.");
        if (result.InvalidCount > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{result.InvalidCount} input curve(s) ignored (must be closed and planar).");

        da.SetDataList(0, result.PackedCurves);
        da.SetDataList(1, result.Transforms);
        da.SetDataList(2, result.SourceIndices);
        da.SetDataList(3, result.SheetIndices);
        da.SetDataList(4, result.UnplacedCurves);
        da.SetDataList(5, result.FailureReasons);
        da.SetDataList(6, result.SheetPreviewCurves);
        da.SetData(7, result.Report);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────
    // Hole routing + preview reuse the shared SheetHolesUtil (PIP-first routing,
    // Bug B-2D-001 fix) so this component matches V506 / the unified components:
    // each hole goes to the sheet that geometrically contains it, with the GH
    // tree path as fallback. Earlier this component routed by tree path only,
    // which dropped holes off any sheet not branch-matched to its index.

    private PackingSortMode ToSortMode(int v)
    {
        if (Enum.IsDefined(typeof(PackingSortMode), v)) return (PackingSortMode)v;
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid sort mode. Using AreaDescending.");
        return PackingSortMode.AreaDescending;
    }
}
