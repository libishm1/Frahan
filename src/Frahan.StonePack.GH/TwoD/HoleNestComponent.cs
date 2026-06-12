#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Frahan.Packing.TwoD;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.TwoD;

/// <summary>
/// Sheet Nest (Hole-Aware). Canvas wrapper for the Core ContactNfpHoleNester
/// (Frahan.Packing.TwoD): a deterministic, exact-NFP, hole-aware 2D nester on
/// the Clipper2 primitive layer. Evolves the Freeform Sheet Nest (Exact NFP)
/// sibling with (1) part-in-part-hole nesting via inner-fit regions and
/// (2) contact-adaptive rotations (edge-alignment angles), both validated in
/// outputs/2026-06-12/hole_packer_evolution. Synchronous component: the solver
/// runs in milliseconds on bench instances, per the repo async-vs-sync rule.
/// </summary>
[Algorithm("Exact No-Fit-Polygon Bottom-Left-Fill with part-in-part-hole nesting",
    "Burke, E.K., Hellier, R., Kendall, G., Whitwell, G. (2006). \"A New Bottom-Left-Fill Heuristic Algorithm for the Two-Dimensional Irregular Packing Problem.\" Operations Research 54(3):587-601",
    Doi = "10.1287/opre.1060.0293")]
[Algorithm("No-fit-polygon / inner-fit-polygon via Minkowski sum",
    "Bennell, J.A. & Oliveira, J.F. (2009). \"A tutorial in irregular shape packing problems.\" J. Oper. Res. Soc. 60(S1):S93-S105",
    Doi = "10.1057/jors.2008.169",
    WikiPath = "wiki/index/references.md#BennellOliveira2008")]
[Algorithm("Clipper2 polygon Minkowski sum + Boolean back-end",
    "Johnson, A. Clipper2 (BSL-1.0); Minkowski sum + NonZero Boolean operations")]
[Algorithm("Contact-adaptive rotations (edge-alignment angle set) + holes-first host nesting",
    "Frahan ContactNfpHoleNester evolution study, outputs/2026-06-12/hole_packer_evolution",
    Note = "Frahan-original; head-to-head benchmark protocol and comparators documented in the study")]
[RelatedComponent("Frahan > 2D Packing > Freeform Sheet Nest (Exact NFP)",
    Reason = "Multi-sheet exact NFP-BLF production sibling without part-in-part-hole nesting; use it when parts have no usable holes.",
    ComponentGuid = "2d351646-2cb0-402a-bbd8-3950b5bb1fbc")]
public sealed class HoleNestComponent : GH_Component
{
    private const int MaxVerts = 200;          // hard cap for explicit polylines (drawn as-is)
    private const int SmoothSampleVerts = 48;  // uniform-by-length samples for smooth curves (NFP cost scales with verts)

    public HoleNestComponent()
        : base("Sheet Nest (Hole-Aware)", "HoleNest",
            "Deterministic hole-aware 2D nester: parts are placed on a sheet with defects (holes) by " +
            "exact no-fit-polygon bottom-left-fill, and smaller parts are nested INSIDE the holes of " +
            "larger placed parts via the inner-fit region. No-fit and inner-fit polygons are built " +
            "exactly as Clipper2 Minkowski sums/erosions (Bennell & Oliveira 2009) and placement is " +
            "bottom-left-fill (Burke et al. 2006), so layouts are 0-overlap by construction. " +
            "Rotations are contact-adaptive: the uniform base set is extended with edge-alignment " +
            "angles against the sheet, the latest neighbour, and host holes so parts seat flush. " +
            "Returns valid hole-aware layouts where hole-blind nesters fail; an exact rectangle " +
            "shelf fast-path accelerates all-rectangle instances. Deterministic: the same inputs " +
            "always reproduce the same cut layout.",
            "Frahan", "2D Packing")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F10019-8A3C-4D17-B5E2-6C90F2A47D31");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => Frahan.GH.IconProvider.Load("NoFitPolygon.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Sheet", "S",
            "Closed planar sheet boundary curve.", GH_ParamAccess.item);
        pManager.AddCurveParameter("Sheet Holes", "SH",
            "Closed sheet defect/hole curves inside the sheet boundary. Parts never overlap them.",
            GH_ParamAccess.list);
        pManager[1].Optional = true;
        pManager.AddCurveParameter("Parts", "P",
            "Closed planar part outline curves to nest.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Part Holes", "PH",
            "Part hole curves as a TREE: branch path {i} holds the hole curves of Parts[i] (graft the " +
            "Parts list to author it; branches are matched by PATH index, so pruned/empty branches are " +
            "safe). Parts with holes are placed first as hosts, then smaller parts are nested into " +
            "their holes via the inner-fit region.",
            GH_ParamAccess.tree);
        pManager[3].Optional = true;
        pManager.AddNumberParameter("Spacing", "Gap",
            "Clearance between parts and boundaries.", GH_ParamAccess.item, 0.0);
        pManager.AddIntegerParameter("BaseRotations", "BR",
            "Uniform base rotation count (4 = 0/90/180/270 degrees).", GH_ParamAccess.item, 4);
        pManager.AddIntegerParameter("ContactRotations", "CR",
            "Longest-edge count per polygon used to build contact (edge-alignment) rotation angles.",
            GH_ParamAccess.item, 6);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Placed", "C",
            "Transformed part outers as closed polyline curves (placement order).", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Source", "I",
            "For each placed curve, the index of the source curve in the Parts input (labeling/etching map).",
            GH_ParamAccess.list);
        pManager.AddTransformParameter("Transform", "X",
            "For each placed curve, the rigid placement transform (rotation about the world Z origin, " +
            "then translation). Apply it to the original part curve, its holes, or any decoration.",
            GH_ParamAccess.list);
        pManager.AddBooleanParameter("Nested", "N",
            "True where the corresponding placed part was nested into a host part's hole.",
            GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "R",
            "Placed count, part-holes filled, density, engine note, elapsed ms, valid flag.",
            GH_ParamAccess.item);
        pManager.AddNumberParameter("Density", "D",
            "Placed part material area / net sheet area (sheet minus its holes).", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Valid", "V",
            "True when the final layout passed the independent boolean (path-free) validation.",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        Curve sheetCurve = null;
        var sheetHoleCurves = new List<Curve>();
        var partCurves = new List<Curve>();
        GH_Structure<GH_Curve> partHolesTree = null;
        double spacing = 0.0;
        int baseRotations = 4;
        int contactRotations = 6;

        if (!da.GetData(0, ref sheetCurve)) return;
        da.GetDataList(1, sheetHoleCurves);
        if (!da.GetDataList(2, partCurves)) return;
        da.GetDataTree(3, out partHolesTree);
        da.GetData(4, ref spacing);
        da.GetData(5, ref baseRotations);
        da.GetData(6, ref contactRotations);

        spacing = Math.Max(0.0, spacing);
        baseRotations = Math.Max(1, baseRotations);
        contactRotations = Math.Max(0, contactRotations);

        var sheetOuter = CurveToLoop(sheetCurve, "Sheet", out var sheetZ);
        if (sheetOuter == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Sheet must be a valid closed curve in a WorldXY-parallel plane.");
            return;
        }
        _outZ = sheetZ;   // placed curves are emitted at the sheet's elevation

        var sheetHoles = new List<IReadOnlyList<(double X, double Y)>>();
        var droppedSheetHoles = 0;
        foreach (var hc in sheetHoleCurves)
        {
            var loop = CurveToLoop(hc, null, out _);
            if (loop != null) sheetHoles.Add(loop);
            else droppedSheetHoles++;
        }
        if (droppedSheetHoles > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{droppedSheetHoles} sheet-hole curve(s) ignored (must be closed and WorldXY-parallel).");

        // Map part-hole branches by their PATH's last index (= Parts input
        // index), NOT by branch position: pruned or reordered trees must never
        // shift holes onto the wrong part (the documented tree-path routing
        // failure class behind Bug B-2D-001). Unmatched branches warn loudly.
        Dictionary<int, List<GH_Curve>> holesByPartIndex = null;
        var unmatchedBranches = 0;
        if (partHolesTree != null && !partHolesTree.IsEmpty)
        {
            holesByPartIndex = new Dictionary<int, List<GH_Curve>>();
            for (int b = 0; b < partHolesTree.PathCount; b++)
            {
                var path = partHolesTree.Paths[b];
                var branch = partHolesTree.Branches[b];
                if (branch == null || branch.Count == 0) continue;
                int key = path.Indices.Length > 0 ? path.Indices[path.Indices.Length - 1] : -1;
                if (key < 0 || key >= partCurves.Count) { unmatchedBranches++; continue; }
                if (!holesByPartIndex.TryGetValue(key, out var list))
                { list = new List<GH_Curve>(); holesByPartIndex[key] = list; }
                list.AddRange(branch);
            }
        }
        if (unmatchedBranches > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{unmatchedBranches} Part Holes branch(es) have no matching Parts index (path {{i}} must " +
                "address Parts[i]); they were ignored.");

        // Build the part set. Track the map from the prepared (filtered) CNH
        // index back to the input index so skipped invalid parts never shift
        // another part's holes onto it, and so Source can report input indices.
        var parts = new List<HoleNestPart>();
        var inputIndexOf = new List<int>();
        var partZOf = new List<double>();
        var droppedParts = 0;
        var droppedPartHoles = 0;
        for (int i = 0; i < partCurves.Count; i++)
        {
            var outer = CurveToLoop(partCurves[i], null, out var partZ);
            if (outer == null) { droppedParts++; continue; }

            List<IReadOnlyList<(double X, double Y)>> holes = null;
            if (holesByPartIndex != null && holesByPartIndex.TryGetValue(i, out var branch))
            {
                foreach (var gc in branch)
                {
                    if (gc == null || gc.Value == null) continue;
                    var hl = CurveToLoop(gc.Value, null, out _);
                    if (hl == null) { droppedPartHoles++; continue; }
                    if (holes == null) holes = new List<IReadOnlyList<(double X, double Y)>>();
                    holes.Add(hl);
                }
            }
            parts.Add(new HoleNestPart { Outer = outer, Holes = holes });
            inputIndexOf.Add(i);
            partZOf.Add(partZ);
        }
        if (droppedParts > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{droppedParts} part curve(s) ignored (must be closed and planar).");
        if (droppedPartHoles > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{droppedPartHoles} part-hole curve(s) ignored (must be closed and planar).");
        if (parts.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No valid part curves.");
            return;
        }

        HoleNestResult res;
        try
        {
            res = ContactNfpHoleNester.Pack(
                sheetOuter, sheetHoles, parts, spacing, baseRotations, contactRotations);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Hole-aware nesting failed: " + ex.Message);
            return;
        }

        var placedCurves = new List<Curve>(res.Placements.Count);
        var sourceIndices = new List<int>(res.Placements.Count);
        var transforms = new List<Transform>(res.Placements.Count);
        var nestedFlags = new List<bool>(res.Placements.Count);
        foreach (var pl in res.Placements)
        {
            placedCurves.Add(LoopToCurve(pl.PlacedOuter, _outZ));
            int src = pl.PartIndex >= 0 && pl.PartIndex < inputIndexOf.Count ? inputIndexOf[pl.PartIndex] : -1;
            sourceIndices.Add(src);
            // Core placement = rotate about the world Z origin, then translate.
            // The Z term lifts a part from its own input plane to the sheet's.
            double dz = pl.PartIndex >= 0 && pl.PartIndex < partZOf.Count ? _outZ - partZOf[pl.PartIndex] : 0.0;
            var xf = Transform.Translation(pl.Tx, pl.Ty, dz) *
                     Transform.Rotation(pl.AngleRad, Vector3d.ZAxis, Point3d.Origin);
            transforms.Add(xf);
            nestedFlags.Add(pl.NestedInHost);
        }

        var unplaced = parts.Count - res.PlacedCount;
        if (unplaced > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{unplaced} part(s) could not be placed.");
        if (!res.Valid)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "Layout failed independent boolean validation: " + res.Note);

        var note = string.IsNullOrEmpty(res.Note) ? "ok" : res.Note;
        var report =
            $"Sheet Nest (Hole-Aware) — Placed: {res.PlacedCount}/{parts.Count}, " +
            $"PartHolesFilled: {res.PartHolesFilled}, Density: {res.Density:0.000}, " +
            $"Valid: {res.Valid}, Elapsed: {res.ElapsedMs:0.0} ms, Note: {note}";

        da.SetDataList(0, placedCurves);
        da.SetDataList(1, sourceIndices);
        da.SetDataList(2, transforms);
        da.SetDataList(3, nestedFlags);
        da.SetData(4, report);
        da.SetData(5, res.Density);
        da.SetData(6, res.Valid);
    }

    // ─── Curve <-> loop conversion (mirrors IrregularSheetFillNfpBlf.CurveToLoop) ─
    // TryGetPolyline first, then chord-tolerance sampling, then DivideByCount.
    // Open curves are rejected (warning at the call sites). Loops are emitted
    // CCW because the Core nester expects CCW polygon loops. The nester is 2D:
    // every curve must lie in a WorldXY-parallel plane (tilted curves would
    // silently nest foreshortened projections), and placed output is emitted at
    // the SHEET's elevation (_outZ); the Transform output lifts each part from
    // its own plane.

    private double _outZ;

    private List<(double X, double Y)> CurveToLoop(Curve curve, string label, out double planeZ)
    {
        planeZ = 0.0;
        if (curve == null) return null;
        if (!curve.IsClosed)
        {
            if (label != null)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, label + " curve is open; it was rejected.");
            return null;
        }

        IList<Point3d> pts = null;
        if (curve.TryGetPolyline(out var pl))
        {
            pts = pl;
        }
        else
        {
            // UNIFORM-BY-LENGTH sampling (perf, 2026-06-12, measured): the old
            // absolute 1e-3 chord + 2-degree turn sampled smooth NURBS to ~200
            // curvature-adaptive vertices and the canvas solve took 6.3 s. The
            // engine's cost is dominated by Minkowski NFP builds, which scale
            // with vertex count AND suffer from the tiny edges that
            // curvature-adaptive sampling concentrates at high-curvature spots
            // (measured: 53 adaptive verts 4.2 s vs 48 uniform verts 2.8 s on
            // the same shields). Equidistant points at SmoothSampleVerts per
            // closed curve give the same boundary fidelity budget with none of
            // the degenerate edges. The engine's exact verification gate makes
            // placement VALIDITY independent of sampling density — only
            // boundary fidelity (~0.3% of size at 48 verts) is traded, well
            // inside nesting spacing/kerf budgets.
            var seg = curve.GetLength() / SmoothSampleVerts;
            var div = seg > Rhino.RhinoMath.ZeroTolerance ? curve.DivideEquidistant(seg) : null;
            if (div != null && div.Length >= 3)
            {
                pts = div;
            }
            else
            {
                var divPar = curve.DivideByCount(SmoothSampleVerts, false);
                if (divPar == null || divPar.Length < 3) return null;
                var tmp = new List<Point3d>(divPar.Length);
                foreach (var t in divPar) tmp.Add(curve.PointAt(t));
                pts = tmp;
            }
        }

        var n = pts.Count;
        if (n > 1 && pts[0].DistanceTo(pts[n - 1]) < 1e-9) n--;
        if (n < 3) return null;

        // WorldXY-parallel plane guard: a tilted curve would project
        // foreshortened and nest silently with distorted geometry.
        double zMin = double.MaxValue, zMax = double.MinValue, span = 0.0;
        Point3d pMin = pts[0], pMax = pts[0];
        for (var i = 0; i < n; i++)
        {
            var p = pts[i];
            if (p.Z < zMin) zMin = p.Z;
            if (p.Z > zMax) zMax = p.Z;
            pMin.X = Math.Min(pMin.X, p.X); pMin.Y = Math.Min(pMin.Y, p.Y);
            pMax.X = Math.Max(pMax.X, p.X); pMax.Y = Math.Max(pMax.Y, p.Y);
        }
        span = Math.Max(pMax.X - pMin.X, pMax.Y - pMin.Y);
        if (zMax - zMin > 1e-6 * (1.0 + span))
        {
            if (label != null)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    label + " curve is not in a WorldXY-parallel plane; it was rejected.");
            return null;
        }
        planeZ = 0.5 * (zMin + zMax);

        var loop = new List<(double X, double Y)>(Math.Min(n, MaxVerts));
        if (n > MaxVerts)
        {
            var step = (double)n / MaxVerts;
            for (var i = 0; i < MaxVerts; i++)
            {
                var idx = Math.Min(n - 1, (int)(i * step));
                loop.Add((pts[idx].X, pts[idx].Y));
            }
        }
        else
        {
            for (var i = 0; i < n; i++) loop.Add((pts[i].X, pts[i].Y));
        }

        var area = SignedArea(loop);
        if (Math.Abs(area) < 1e-12) return null;
        if (area < 0) loop.Reverse();   // Core nester expects CCW loops
        return loop;
    }

    private static Curve LoopToCurve(IReadOnlyList<(double X, double Y)> loop, double z)
    {
        var pts = new List<Point3d>(loop.Count + 1);
        foreach (var (x, y) in loop) pts.Add(new Point3d(x, y, z));
        pts.Add(pts[0]);   // close the polyline
        return new PolylineCurve(pts);
    }

    private static double SignedArea(List<(double X, double Y)> loop)
    {
        double a = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            var j = (i + 1) % loop.Count;
            a += loop[i].X * loop[j].Y - loop[j].X * loop[i].Y;
        }
        return 0.5 * a;
    }
}
