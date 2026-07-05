#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Packing.TwoD;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.TwoD;

// =============================================================================
// HoleNestShared — extraction (2026-07-05) of the GH-glue that HoleNestComponent
// (sync, self-triggered progressive async) and SheetNestLiveComponent (Run-gated
// AsyncScanComponent) both need:
//   * curve<->loop conversion (chord-tolerance / uniform-by-length sampling,
//     WorldXY-parallel-plane guard, proxy-deviation measurement)
//   * PIP-first sheet-hole and part-hole routing + Snapshot building (owned,
//     duplicated inputs so a background Task never touches live Rhino data)
//   * result -> output-geometry building (ORIGINAL full-resolution curves,
//     transformed to their placed positions; holes travel with their part)
//
// The packing MATH stays in Frahan.Packing.TwoD.ContactNfpHoleNester (Core,
// unmodified); this file is GH/Rhino glue only. Both components call
// ContactNfpHoleNester.PackSheets(...) directly with the Snapshot fields this
// class produces — the shared surface is the conversion + snapshot + output
// build, not the solver call itself (each component's async shape around that
// call differs: HoleNestComponent's self-triggered progressive re-solve vs
// SheetNestLiveComponent's simple Run-gated AsyncScanComponent).
//
// HoleNestComponent's own input-hash loop-guard, self-trigger re-solve, and
// progressive partial-display state machine are specific to its always-on
// auto-solve behavior and stay PRIVATE to that class — they are not part of
// the shared surface.
// =============================================================================
public static class HoleNestShared
{
    /// <summary>Hard cap for explicit output polylines (drawn as-is).</summary>
    public const int MaxVerts = 200;

    /// <summary>
    /// ACCURACY lane: sheet + sheet-hole sampling resolution. The sheet and its
    /// holes are single loops whose vertex count barely affects solve cost
    /// (only PART verts drive the Minkowski NFPs), so they sample high-res — a
    /// coarse sheet proxy on a large freeform boundary measured 13+ units of
    /// deviation, and the old shared compensation inflated every PART by 2x
    /// that (ProxyDevComp +27 on a benchmark S-sheet = 21/200 fill).
    /// </summary>
    public const int SheetSampleVerts = 192;

    /// <summary>
    /// COST lane clamp bounds for parts + part-holes (Resolution input). 24 is
    /// the recommended default: benchmark showed solve cost scales
    /// ~quadratically with this (48v was ~10-20x slower than 24v) while packing
    /// DENSITY is nearly flat (&lt;2%), because this only sets the COLLISION
    /// proxy — the Placed output is always the exact original curve.
    /// </summary>
    public const int MinSmoothSampleVerts = 16;

    public sealed class Snapshot
    {
        public List<IReadOnlyList<(double X, double Y)>> Sheets;
        public List<IReadOnlyList<IReadOnlyList<(double X, double Y)>>> SheetHolesPerSheet;
        public List<double> SheetZ;          // per sheet (placed parts land at their sheet's elevation)
        public List<double> SheetNetArea;    // per sheet: |outer| - sum|holes| (for the aggregate density)
        public List<HoleNestPart> Parts;
        public double UserSpacing, EngineSpacing, MaxDev;
        public int BaseRotations, ContactRotations, MultiStart;
        public List<int> InputIndexOf;
        public List<double> PartZOf;
        public List<Curve> Originals;           // duplicated on the UI thread (owned)
        public List<List<Curve>> OriginalHoles; // per prepared part, duplicated (may be null per part)
        public ulong Hash;
    }

    public sealed class Outputs
    {
        public List<Curve> Placed;
        public List<int> Source;
        public List<Transform> Transform;
        public List<bool> Nested;
        public List<int> Sheet;
        public GH_Structure<GH_Curve> PlacedHoles;
        public int Unplaced;
    }

    /// <summary>
    /// Inputs -> owned snapshot (conversion + proxy-deviation measurement +
    /// hash). UI-thread only (duplicates Rhino curves; must run before any
    /// background Task starts). Returns null on validation failure; the error
    /// has already been reported via <paramref name="owner"/>.AddRuntimeMessage.
    /// </summary>
    public static Snapshot BuildSnapshot(
        GH_Component owner,
        List<Curve> sheetCurves,
        GH_Structure<GH_Curve> sheetHolesTree,
        List<Curve> partCurves,
        GH_Structure<GH_Curve> partHolesTree,
        double spacing,
        int baseRotations,
        int contactRotations,
        int resolution,
        int multiStart)
    {
        int smoothSampleVerts = Math.Max(MinSmoothSampleVerts, Math.Min(MaxVerts, resolution));
        spacing = Math.Max(0.0, spacing);
        baseRotations = Math.Max(1, baseRotations);
        contactRotations = Math.Max(0, contactRotations);
        multiStart = Math.Max(1, multiStart); // Core clamps the upper bound to the available order count

        double partDev = 0.0, sheetDev = 0.0;
        var sheets = new List<IReadOnlyList<(double X, double Y)>>();
        var sheetZs = new List<double>();
        var sheetNet = new List<double>();
        foreach (var sc in sheetCurves)
        {
            var loop = CurveToLoop(owner, sc, "Sheet", SheetSampleVerts, out var sz, out var devS);
            if (loop == null) continue;
            sheets.Add(loop); sheetZs.Add(sz);
            sheetNet.Add(Math.Abs(SignedArea((List<(double X, double Y)>)loop)));
            if (devS > sheetDev) sheetDev = devS;
        }
        if (sheets.Count == 0)
        {
            owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "At least one Sheet must be a valid closed curve in a WorldXY-parallel plane.");
            return null;
        }
        if (sheets.Count < sheetCurves.Count)
            owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{sheetCurves.Count - sheets.Count} sheet curve(s) ignored (must be closed and WorldXY-parallel).");

        // per-sheet holes: PIP-FIRST geometric routing (the house SheetHolesUtil
        // pattern, Bug B-2D-001 fix) — each hole goes to whichever sheet
        // geometrically CONTAINS it (tree path {s} is only the fallback); no
        // tree matching or grafting required, sheets without holes need nothing.
        var validSheetCurves = new List<Curve>();
        foreach (var sc in sheetCurves) if (sc != null && sc.IsClosed) validSheetCurves.Add(sc);
        var routedHoles = SheetHolesUtil.BuildHolesBySheet(
            validSheetCurves, sheetHolesTree, sheets.Count,
            Math.Max(0.01, 1e-6 * (validSheetCurves.Count > 0 ? validSheetCurves[0].GetBoundingBox(false).Diagonal.Length : 1.0)));
        var sheetHolesPerSheet = new List<IReadOnlyList<IReadOnlyList<(double X, double Y)>>>();
        for (int si = 0; si < sheets.Count; si++) sheetHolesPerSheet.Add(new List<IReadOnlyList<(double X, double Y)>>());
        var droppedSheetHoles = 0;
        for (int si = 0; si < sheets.Count && si < routedHoles.Count; si++)
        {
            var target = (List<IReadOnlyList<(double X, double Y)>>)sheetHolesPerSheet[si];
            foreach (var hc in routedHoles[si])
            {
                if (hc == null) continue;
                var loop = CurveToLoop(owner, hc, null, SheetSampleVerts, out _, out var devH);
                if (loop != null)
                {
                    target.Add(loop);
                    sheetNet[si] -= Math.Abs(SignedArea((List<(double X, double Y)>)loop));
                    if (devH > sheetDev) sheetDev = devH;
                }
                else droppedSheetHoles++;
            }
        }
        if (droppedSheetHoles > 0)
            owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{droppedSheetHoles} sheet-hole curve(s) ignored (must be closed and WorldXY-parallel).");

        // Part holes: PIP-FIRST geometric routing (mirrors the sheet-hole
        // pattern) — each hole curve is assigned to the SMALLEST part outline
        // that geometrically contains its centroid; the GH tree path
        // (branch {i} -> Parts[i]) is only the fallback when no part contains
        // it. Flat lists, grafted trees and sparse trees all route correctly;
        // parts without holes need nothing.
        Dictionary<int, List<GH_Curve>> holesByPartIndex = null;
        var unroutedHoles = 0;
        if (partHolesTree != null && !partHolesTree.IsEmpty)
        {
            holesByPartIndex = new Dictionary<int, List<GH_Curve>>();
            for (int b = 0; b < partHolesTree.PathCount; b++)
            {
                var path = partHolesTree.Paths[b];
                var branch = partHolesTree.Branches[b];
                if (branch == null || branch.Count == 0) continue;
                int pathKey = path.Indices.Length > 0 ? path.Indices[path.Indices.Length - 1] : -1;
                foreach (var gc in branch)
                {
                    if (gc == null || gc.Value == null) continue;
                    var bb = gc.Value.GetBoundingBox(false);
                    var centroid = bb.Center;
                    int bestPart = -1; double bestArea = double.MaxValue;
                    for (int pi2 = 0; pi2 < partCurves.Count; pi2++)
                    {
                        var pc = partCurves[pi2];
                        if (pc == null || !pc.IsClosed) continue;
                        var plane = new Plane(new Point3d(0, 0, centroid.Z), Vector3d.ZAxis);
                        if (pc.Contains(centroid, plane, 1e-6) != PointContainment.Inside) continue;
                        var amp = AreaMassProperties.Compute(pc);
                        double area = amp != null ? amp.Area : double.MaxValue;
                        if (area < bestArea) { bestArea = area; bestPart = pi2; }
                    }
                    int key = bestPart >= 0 ? bestPart
                        : (pathKey >= 0 && pathKey < partCurves.Count ? pathKey : -1);
                    if (key < 0) { unroutedHoles++; continue; }
                    if (!holesByPartIndex.TryGetValue(key, out var list))
                    { list = new List<GH_Curve>(); holesByPartIndex[key] = list; }
                    list.Add(gc);
                }
            }
        }
        if (unroutedHoles > 0)
            owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{unroutedHoles} part-hole curve(s) sit inside NO part outline and have no usable tree " +
                "path; they were ignored. Draw holes inside their parts (or graft branch {i} -> Parts[i]).");

        var parts = new List<HoleNestPart>();
        var inputIndexOf = new List<int>();
        var partZOf = new List<double>();
        var originals = new List<Curve>();
        var originalHoles = new List<List<Curve>>();
        var droppedParts = 0;
        var droppedPartHoles = 0;
        for (int i = 0; i < partCurves.Count; i++)
        {
            var outer = CurveToLoop(owner, partCurves[i], null, smoothSampleVerts, out var partZ, out var devP);
            if (outer == null) { droppedParts++; continue; }
            if (devP > partDev) partDev = devP;

            List<IReadOnlyList<(double X, double Y)>> holes = null;
            List<Curve> holeCurves = null;
            if (holesByPartIndex != null && holesByPartIndex.TryGetValue(i, out var branch))
            {
                foreach (var gc in branch)
                {
                    if (gc == null || gc.Value == null) continue;
                    var hl = CurveToLoop(owner, gc.Value, null, smoothSampleVerts, out _, out var devHl);
                    if (hl == null) { droppedPartHoles++; continue; }
                    if (devHl > partDev) partDev = devHl;
                    if (holes == null) holes = new List<IReadOnlyList<(double X, double Y)>>();
                    holes.Add(hl);
                    if (holeCurves == null) holeCurves = new List<Curve>();
                    holeCurves.Add(gc.Value.DuplicateCurve());
                }
            }
            parts.Add(new HoleNestPart { Outer = outer, Holes = holes });
            inputIndexOf.Add(i);
            partZOf.Add(partZ);
            originals.Add(partCurves[i] != null ? partCurves[i].DuplicateCurve() : null);
            originalHoles.Add(holeCurves);
        }
        if (droppedParts > 0)
            owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{droppedParts} part curve(s) ignored (must be closed and planar).");
        if (droppedPartHoles > 0)
            owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{droppedPartHoles} part-hole curve(s) ignored (must be closed and planar).");
        if (parts.Count == 0)
        {
            owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No valid part curves.");
            return null;
        }

        // PROXY-DEVIATION COMPENSATION (overlap fix, 2026-06-12): the solver
        // sees sampled proxies whose chords cut INSIDE the true curve, so
        // touching proxies let the true full-resolution curves cross by up to
        // the sampling deviation on each side. Part-pair clearance needs 2x
        // the worst PART deviation; the sheet term enters ONCE (a part can
        // poke past the true boundary by at most the sheet proxy's own
        // deviation, which the high-res sheet lane keeps tiny).
        double maxDev = Math.Max(partDev, sheetDev); // reported for transparency
        double engineSpacing = spacing + 2.0 * partDev + sheetDev;

        // STABLE input fingerprint for HoleNestComponent's async loop-guard.
        // Sampling-free signals — input counts + each RAW input curve's
        // bounding box (deterministic control-point hull) — quantized. Detects
        // add/remove, resize, and translate of any sheet/part/hole; immune to
        // sampling noise (see HoleNestComponent for the fuller rationale).
        // SheetNestLiveComponent does not use this field (AsyncScanComponent's
        // Run gate does not need input-change detection).
        ulong h = 1469598103934665603UL;
        void HD(double v) { unchecked { h ^= (ulong)BitConverter.DoubleToInt64Bits(v); h *= 1099511628211UL; } }
        void HQ(double v) { HD(Math.Round(v * 1e4) / 1e4); }       // quantize to 1e-4
        void HBox(Curve c)
        {
            if (c == null) { HD(-7.0); return; }
            var b = c.GetBoundingBox(false);
            HQ(b.Min.X); HQ(b.Min.Y); HQ(b.Min.Z);
            HQ(b.Max.X); HQ(b.Max.Y); HQ(b.Max.Z);
            HD(c.SpanCount);   // a stable, sampling-free shape signal (segment count)
        }
        HQ(spacing); HD(baseRotations); HD(contactRotations); HD(smoothSampleVerts); HD(multiStart);
        HD(sheetCurves.Count);
        foreach (var c in sheetCurves) HBox(c);
        if (sheetHolesTree != null)
            foreach (var br in sheetHolesTree.Branches)
                if (br != null) foreach (var gc in br) HBox(gc?.Value);
        HD(partCurves.Count);
        foreach (var c in partCurves) HBox(c);
        if (partHolesTree != null)
            foreach (var br in partHolesTree.Branches)
                if (br != null) foreach (var gc in br) HBox(gc?.Value);

        return new Snapshot
        {
            Sheets = sheets, SheetHolesPerSheet = sheetHolesPerSheet,
            SheetZ = sheetZs, SheetNetArea = sheetNet, Parts = parts,
            UserSpacing = spacing, EngineSpacing = engineSpacing, MaxDev = maxDev,
            BaseRotations = baseRotations, ContactRotations = contactRotations, MultiStart = multiStart,
            InputIndexOf = inputIndexOf, PartZOf = partZOf, Originals = originals,
            OriginalHoles = originalHoles,
            Hash = h,
        };
    }

    /// <summary>
    /// Result + snapshot -> output geometry. The Placed curves are the
    /// ORIGINAL part curves at full resolution, moved to their placed
    /// positions (the solver's collision proxies never leave this method).
    /// UI-thread only (builds RhinoCommon geometry).
    /// </summary>
    public static Outputs BuildOutputs(HoleNestResult res, Snapshot snap)
    {
        var placedCurves = new List<Curve>(res.Placements.Count);
        var sourceIndices = new List<int>(res.Placements.Count);
        var transforms = new List<Transform>(res.Placements.Count);
        var nestedFlags = new List<bool>(res.Placements.Count);
        var sheetIndices = new List<int>(res.Placements.Count);
        var placedHoles = new GH_Structure<GH_Curve>();
        int placedIdx = 0;
        foreach (var pl in res.Placements)
        {
            int src = pl.PartIndex >= 0 && pl.PartIndex < snap.InputIndexOf.Count ? snap.InputIndexOf[pl.PartIndex] : -1;
            sourceIndices.Add(src);
            // Core placement = rotate about the world Z origin, then translate.
            // The Z term lifts a part from its own input plane to ITS sheet's.
            double sheetZ = pl.SheetIndex >= 0 && pl.SheetIndex < snap.SheetZ.Count ? snap.SheetZ[pl.SheetIndex] : snap.SheetZ[0];
            double dz = pl.PartIndex >= 0 && pl.PartIndex < snap.PartZOf.Count ? sheetZ - snap.PartZOf[pl.PartIndex] : 0.0;
            var xf = Transform.Translation(pl.Tx, pl.Ty, dz) *
                     Transform.Rotation(pl.AngleRad, Vector3d.ZAxis, Point3d.Origin);
            transforms.Add(xf);
            // Placed output = the ORIGINAL curve transformed (full resolution).
            // The sampled loop is only the solver's collision proxy; the
            // deviation-compensated spacing guarantees the true curves never
            // overlap. Fall back to the proxy loop if the duplicate fails.
            Curve placedCurve = null;
            int prep = pl.PartIndex;
            if (prep >= 0 && prep < snap.Originals.Count && snap.Originals[prep] != null)
            {
                placedCurve = snap.Originals[prep].DuplicateCurve();
                if (placedCurve != null && !placedCurve.Transform(xf)) placedCurve = null;
            }
            placedCurves.Add(placedCurve != null ? placedCurve : LoopToCurve(pl.PlacedOuter, sheetZ));
            // the part's own holes travel with it (full resolution, same xf)
            var holePath = new GH_Path(placedIdx);
            placedHoles.EnsurePath(holePath);
            if (prep >= 0 && snap.OriginalHoles != null && prep < snap.OriginalHoles.Count &&
                snap.OriginalHoles[prep] != null)
            {
                foreach (var hc in snap.OriginalHoles[prep])
                {
                    if (hc == null) continue;
                    var dup = hc.DuplicateCurve();
                    if (dup != null && dup.Transform(xf)) placedHoles.Append(new GH_Curve(dup), holePath);
                }
            }
            placedIdx++;
            nestedFlags.Add(pl.NestedInHost);
            sheetIndices.Add(pl.SheetIndex);
        }

        return new Outputs
        {
            Placed = placedCurves, Source = sourceIndices, Transform = transforms,
            Nested = nestedFlags, Sheet = sheetIndices, PlacedHoles = placedHoles,
            Unplaced = snap.Parts.Count - res.PlacedCount,
        };
    }

    // ─── Curve <-> loop conversion (mirrors the historical IrregularSheetFillNfpBlf.CurveToLoop) ─
    // TryGetPolyline first, then chord-tolerance sampling, then DivideByCount.
    // Open curves are rejected (warning at the call sites). Loops are emitted
    // CCW because the Core nester expects CCW polygon loops. The nester is 2D:
    // every curve must lie in a WorldXY-parallel plane (tilted curves would
    // silently nest foreshortened projections); the caller places output at
    // its own chosen elevation (the returned planeZ, or the sheet's).

    public static List<(double X, double Y)> CurveToLoop(
        GH_Component owner, Curve curve, string label, int sampleVerts,
        out double planeZ, out double maxDev)
    {
        planeZ = 0.0;
        maxDev = 0.0;
        if (curve == null) return null;
        if (!curve.IsClosed)
        {
            if (label != null)
                owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, label + " curve is open; it was rejected.");
            return null;
        }

        IList<Point3d> pts = null;
        bool measureDeviation = false;
        if (curve.TryGetPolyline(out var pl))
        {
            pts = pl;
        }
        else
        {
            // UNIFORM-BY-LENGTH sampling (perf, 2026-06-12, measured): equidistant
            // points at sampleVerts per closed curve give the same boundary
            // fidelity budget as curvature-adaptive sampling with none of the
            // degenerate tiny edges that curvature-adaptive sampling
            // concentrates at high-curvature spots. The engine's exact
            // verification gate makes placement VALIDITY independent of
            // sampling density — only boundary fidelity is traded, well inside
            // nesting spacing/kerf budgets.
            measureDeviation = true;
            var seg = curve.GetLength() / sampleVerts;
            var div = seg > Rhino.RhinoMath.ZeroTolerance ? curve.DivideEquidistant(seg) : null;
            if (div != null && div.Length >= 3)
            {
                pts = div;
            }
            else
            {
                var divPar = curve.DivideByCount(sampleVerts, false);
                if (divPar == null || divPar.Length < 3) return null;
                var tmp = new List<Point3d>(divPar.Length);
                foreach (var t in divPar) tmp.Add(curve.PointAt(t));
                pts = tmp;
            }
        }

        var n = pts.Count;
        if (n > 1 && pts[0].DistanceTo(pts[n - 1]) < 1e-9) n--;
        if (n < 3) return null;

        // measured proxy deviation (sampled smooth curves only): max distance
        // from each chord midpoint back to the true curve — feeds the
        // deviation-compensated engine spacing so full-resolution outputs
        // can never overlap even though the solver sees coarse proxies
        if (measureDeviation)
        {
            for (var i = 0; i < n; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % n];
                var mid = new Point3d((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, (a.Z + b.Z) * 0.5);
                if (curve.ClosestPoint(mid, out var tcp))
                {
                    var d = curve.PointAt(tcp).DistanceTo(mid);
                    if (d > maxDev) maxDev = d;
                }
            }
        }

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
                owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
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

    public static Curve LoopToCurve(IReadOnlyList<(double X, double Y)> loop, double z)
    {
        var pts = new List<Point3d>(loop.Count + 1);
        foreach (var (x, y) in loop) pts.Add(new Point3d(x, y, z));
        pts.Add(pts[0]);   // close the polyline
        return new PolylineCurve(pts);
    }

    public static double SignedArea(List<(double X, double Y)> loop)
    {
        double a = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            var j = (i + 1) % loop.Count;
            a += loop[i].X * loop[j].Y - loop[j].X * loop[i].Y;
        }
        return 0.5 * a;
    }

    /// <summary>
    /// Fan-triangulate a raw XY loop (engine PlacedOuter) into a single-face-ring
    /// mesh at elevation <paramref name="z"/> — the progressive live-preview fast
    /// path shared by the nesting components.
    /// </summary>
    public static Mesh FanMeshFromLoop(IReadOnlyList<(double X, double Y)> loop, double z = 0.0)
    {
        if (loop == null) return null;
        int n = loop.Count;
        if (n > 1 && Math.Abs(loop[0].X - loop[n - 1].X) < 1e-12 && Math.Abs(loop[0].Y - loop[n - 1].Y) < 1e-12) n--;
        if (n < 3) return null;
        var mesh = new Mesh();
        double cx = 0, cy = 0;
        for (int i = 0; i < n; i++) { cx += loop[i].X; cy += loop[i].Y; }
        cx /= n; cy /= n;
        for (int i = 0; i < n; i++) mesh.Vertices.Add(loop[i].X, loop[i].Y, z);
        int centerIdx = mesh.Vertices.Add(cx, cy, z);
        for (int i = 0; i < n; i++) mesh.Faces.AddFace(i, (i + 1) % n, centerIdx);
        mesh.Normals.ComputeNormals();
        mesh.Compact();
        return mesh;
    }
}
