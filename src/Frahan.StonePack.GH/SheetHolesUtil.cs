using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH;

/// <summary>
/// Item F (2026-05-04). Shared sheet-holes helpers for the unified
/// 2D irregular-sheet GH components (sync + async).
///
/// 2026-05-05: PIP-first routing folded in (Bug B-2D-001 fix). Each
/// hole curve is routed to whichever sheet geometrically contains its
/// centroid (point-in-polygon test). The GH tree path is the fallback
/// when no sheet contains the centroid — preserving the original
/// behavior for explicit per-sheet wiring while also making flat-list
/// hole inputs route correctly across multiple sheets. This brings
/// the unified components to parity with V506's private router and
/// closes the divergence that left sheet 1+ ignoring holes.
/// </summary>
internal static class SheetHolesUtil
{
    /// <summary>
    /// Group sheet-hole curves by sheet index. Each hole is routed
    /// PIP-first (sheet whose outline geometrically contains the hole's
    /// centroid wins), falling back to the GH tree path index
    /// (branch <c>{i}</c> → sheet <c>i</c>) when no sheet contains the
    /// centroid. Out-of-range path indices are dropped, except when
    /// there is exactly one sheet, in which case any branch falls
    /// through to that sheet.
    /// </summary>
    /// <param name="sheets">Sheet outline curves. Inspected for
    /// point-in-polygon routing.</param>
    /// <param name="holesTree">DataTree of hole curves. May be null.</param>
    /// <param name="sheetCount">Number of sheets, used to pre-size the
    /// result list and validate the path index. Should equal
    /// <paramref name="sheets"/>.Count for any caller; kept as a
    /// separate parameter to preserve signature parity with the
    /// previous inline implementation.</param>
    /// <param name="tolerance">Geometric tolerance used by the
    /// point-in-polygon centroid test. Floored at 0.01 internally.</param>
    public static List<List<Curve>> BuildHolesBySheet(
        IReadOnlyList<Curve> sheets, GH_Structure<GH_Curve>? holesTree,
        int sheetCount, double tolerance)
    {
        var holes = new List<List<Curve>>(sheetCount);
        for (int i = 0; i < sheetCount; i++) holes.Add(new List<Curve>());
        if (holesTree == null || sheetCount == 0) return holes;

        foreach (var path in holesTree.Paths)
        {
            int siPath = path.Length > 0 ? path[0] : 0;
            if (siPath < 0 || siPath >= sheetCount) siPath = sheetCount == 1 ? 0 : -1;
            foreach (var item in holesTree.get_Branch(path))
            {
                if (!(item is GH_Curve gc) || gc.Value == null) continue;
                var hole = gc.Value.DuplicateCurve();
                var siPip = FindContainingSheet(sheets, hole, tolerance);
                var si = siPip >= 0 ? siPip : siPath;
                if (si < 0) continue;
                holes[si].Add(hole);
            }
        }
        return holes;
    }

    private static int FindContainingSheet(
        IReadOnlyList<Curve> sheets, Curve hole, double tolerance)
    {
        var pt = AreaMassProperties.Compute(hole)?.Centroid
                 ?? hole.GetBoundingBox(true).Center;
        var tol = System.Math.Max(tolerance, 0.01);
        for (int i = 0; i < sheets.Count; i++)
        {
            if (sheets[i] == null) continue;
            if (sheets[i].TryGetPlane(out var pl, tol))
            {
                var c = sheets[i].Contains(pt, pl, tol);
                if (c == PointContainment.Inside || c == PointContainment.Coincident) return i;
            }
            var c2 = sheets[i].Contains(pt, Plane.WorldXY, tol);
            if (c2 == PointContainment.Inside || c2 == PointContainment.Coincident) return i;
        }
        return -1;
    }

    /// <summary>
    /// Build the "Sheet Preview" output: every sheet outline followed
    /// by every hole assigned to that sheet, all duplicated so the
    /// caller can hand them straight to Grasshopper without aliasing
    /// upstream geometry.
    /// </summary>
    public static List<Curve> BuildPreview(
        IReadOnlyList<Curve> sheets, GH_Structure<GH_Curve>? holesTree,
        int sheetCount, double tolerance)
    {
        var holes = BuildHolesBySheet(sheets, holesTree, sheetCount, tolerance);
        var preview = new List<Curve>();
        for (int i = 0; i < sheets.Count; i++)
        {
            preview.Add(sheets[i].DuplicateCurve());
            if (i < holes.Count) preview.AddRange(holes[i].Select(h => h.DuplicateCurve()));
        }
        return preview;
    }
}
