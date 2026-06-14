#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using KangarooSolver;
using KangarooSolver.Goals;
using Rhino.Geometry;

namespace Frahan.GH;

/// <summary>
/// Trencadís dynamic settle (F-2D-002.F8).
///
/// Pipeline expected upstream:
///   1. Pack first using a tolerance-based 2D packer (greedy Trencadís
///      Pack with Boundary Mode 2 is the canonical "boundary packing
///      algorithm").
///   2. The packed curves' centroids ARE the answer points / anchor
///      positions for the dynamic stage. Not the part-library frame
///      centroids, not the world-origin centroids — the centroids of
///      the curves AFTER they've been positioned by the packer.
///   3. This component runs Kangaroo 2 to find the residual dynamic
///      tolerance — how much the settled positions deviate from the
///      packer's output once collision and boundary attraction are
///      enforced rigorously.
///
/// Lightweight design: ONE particle per piece (its centroid). Each
/// piece is translated rigidly by (settledCentroid − originalCentroid)
/// after the solve. Shape and edge lengths are preserved EXACTLY (not
/// "approximately by a goal") because rigid translation is shape-
/// invariant by construction. No RigidPointSet, no per-edge Spring, no
/// Collide2d on polygons — those goals scale O(N × verts²) and made the
/// component too slow to be useful next to Kangaroo's native solver
/// component. SphereCollide on centroids is O(N²) over a much smaller
/// set and runs ~50× faster on a typical canvas.
///
/// Goals applied:
///   • Anchor — soft pull on each centroid back to its post-packing
///     position. Prevents drift.
///   • SphereCollide — single goal carrying all centroids with a
///     uniform collision radius (mean of per-piece bounding radii).
///     Pieces whose centroid distance is less than the collision
///     diameter get pushed apart.
///   • OnCurve — pieces whose post-packing centroid is within 1.5×
///     mean radius of any sheet boundary stay attracted to that
///     boundary, preserving the ring layout from Mode 2 phase 1.
///
/// Apply Physics is the master toggle. False = pure pass-through.
/// True = solve and output translated pieces.
///
/// Residual Overlap output is the dynamic tolerance: sum of polygon-
/// pair intersection areas after settle. 0 = clean fit. Raise
/// Iterations or Collide Strength to drive it down.
/// </summary>
[Algorithm("Trencadis dynamic settle", "F-2D-002.F8 Frahan-original", Note = "Kangaroo 2 physics relax after greedy pack")]
[Algorithm("Kangaroo 2 goal-based physics", "Daniel Piker Kangaroo 2 dynamic relaxation solver")]
[DesignApplication(
    "Light Kangaroo 2 settle for trimmed trencadís packing",
    DesignFlow.BottomUp,
    Precedent = "Battiato 2013 CVD+GVF Trencadis synthesis (dynamic-tile variant)")]
public sealed class Pack2DTrencadisDynamicComponent : FrahanComponentBase
{
    public Pack2DTrencadisDynamicComponent()
        : base("Frahan Trencadís Dynamic Settle", "TrencadisDyn",
            "Light Kangaroo 2 settle for trimmed trencadís packing. " +
            "Each piece is one centroid particle. SphereCollide pushes " +
            "overlapping centroids apart, Anchor pulls back to post- " +
            "packing centroid, OnCurve sticks boundary-adjacent pieces " +
            "to the sheet edge. Pieces translate rigidly so shape and " +
            "edge lengths are exactly preserved.",
            "Frahan", "Trencadis")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D00008-CADC-4F2D-9008-7E60CADA15A0");
    public override GH_Exposure Exposure => GH_Exposure.tertiary;
    protected override Bitmap Icon => IconProvider.Load("ContactSettle.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Pieces", "C",
            "Trimmed pieces from a trencadís packer. Centroid of each " +
            "input curve becomes the anchor target — i.e. the answer " +
            "point produced by the boundary packing algorithm upstream.",
            GH_ParamAccess.list);
        pManager.AddCurveParameter("Sheet Outlines", "S",
            "Closed planar sheet outlines. OnCurve targets.",
            GH_ParamAccess.list);
        pManager[1].Optional = true;
        pManager.AddCurveParameter("Sheet Holes", "H",
            "Hole curves as a tree. OnCurve targets too.",
            GH_ParamAccess.tree);
        pManager[2].Optional = true;
        pManager.AddBooleanParameter("Apply Physics", "Phys",
            "Master toggle. False = pure pass-through (output = input). " +
            "True = solve.",
            GH_ParamAccess.item, false);
        pManager.AddIntegerParameter("Iterations", "Iter",
            "Maximum solver step count. Solver early-exits when kinetic " +
            "energy drops below 1e-6. Default 100.",
            GH_ParamAccess.item, 100);
        pManager.AddNumberParameter("Anchor Strength", "Anc",
            "Pull strength on each centroid back toward its post-packing " +
            "position. 0 = disabled. Default 0.05.",
            GH_ParamAccess.item, 0.05);
        pManager.AddNumberParameter("Collide Strength", "Col",
            "SphereCollide goal strength. Default 1.0.",
            GH_ParamAccess.item, 1.0);
        pManager.AddNumberParameter("Boundary Pull", "Bp",
            "OnCurve goal strength for centroids within 1.5× mean radius " +
            "of any boundary curve. 0 = disabled. Default 0.5.",
            GH_ParamAccess.item, 0.5);
        pManager.AddNumberParameter("Collide Radius Factor", "RF",
            "Multiplier on the mean per-piece bounding radius used as " +
            "the SphereCollide radius. >1 = more space between pieces. " +
            "<1 = pieces can overlap (centroid distance allowed to be " +
            "less than mean radius). Default 1.0.",
            GH_ParamAccess.item, 1.0);
        pManager.AddBooleanParameter("Strict Containment", "Cont",
            "Hard per-vertex boundary collider. When True, after each " +
            "Kangaroo step the proposed centroid translation is binary-" +
            "searched to find the largest fraction that keeps EVERY " +
            "vertex of the piece inside at least one Sheet Outline and " +
            "outside every Sheet Hole. Pieces whose initial vertices " +
            "are already outside the boundary won't move (fraction = 0). " +
            "When False, only the soft OnCurve boundary pull on centroids " +
            "is applied (legacy behaviour). Default True.",
            GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Settled Pieces", "C",
            "Pieces translated by (settledCentroid − originalCentroid). " +
            "Shape and edge lengths preserved exactly via rigid translation.",
            GH_ParamAccess.list);
        pManager.AddVectorParameter("Translations", "V",
            "Per-piece translation vector applied during settle.",
            GH_ParamAccess.list);
        pManager.AddPointParameter("Final Centroids", "X",
            "Per-piece centroid after settling.",
            GH_ParamAccess.list);
        pManager.AddNumberParameter("Final vSum", "v",
            "Final kinetic-energy sum. < 1e-6 indicates well converged.",
            GH_ParamAccess.item);
        pManager.AddNumberParameter("Residual Overlap", "Res",
            "Sum of polygon-pair intersection areas after settle. This " +
            "is the dynamic tolerance — how much overlap remains in the " +
            "tolerance-based packing once Kangaroo has resolved as much " +
            "as it can. 0 = clean fit.",
            GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R",
            "Settle report.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var pieces = new List<Curve>();
        var sheets = new List<Curve>();
        GH_Structure<GH_Curve> holesTree = null;
        bool applyPhysics = false;
        int iter = 100;
        double anchorS = 0.05;
        double collideS = 1.0;
        double boundaryS = 0.5;
        double radiusFactor = 1.0;
        bool strictContainment = true;

        if (!da.GetDataList(0, pieces)) return;
        da.GetDataList(1, sheets);
        da.GetDataTree(2, out holesTree);
        da.GetData(3, ref applyPhysics);
        da.GetData(4, ref iter);
        da.GetData(5, ref anchorS);
        da.GetData(6, ref collideS);
        da.GetData(7, ref boundaryS);
        da.GetData(8, ref radiusFactor);
        if (Params.Input.Count > 9) da.GetData(9, ref strictContainment);

        // Compute centroids + bounding radii for every piece. Centroid
        // is the answer point from the upstream packer. Used for the
        // anchor target AND the particle's initial position.
        var centroids = new Point3d[pieces.Count];
        var radii = new double[pieces.Count];
        var valid = new bool[pieces.Count];
        var validCount = 0;
        for (int i = 0; i < pieces.Count; i++)
        {
            var c = pieces[i];
            if (c == null || !c.IsValid) continue;
            try
            {
                var ap = AreaMassProperties.Compute(c);
                centroids[i] = ap?.Centroid ?? c.GetBoundingBox(true).Center;
            }
            catch { centroids[i] = c.GetBoundingBox(true).Center; }
            var bb = c.GetBoundingBox(true);
            radii[i] = Math.Max(0.5 * bb.Diagonal.Length, 1e-3);
            valid[i] = true;
            validCount++;
        }

        // Pure pass-through path.
        if (!applyPhysics || validCount == 0)
        {
            var passthrough = new List<Curve>(pieces.Count);
            var zero = new List<Vector3d>(pieces.Count);
            var origCentroids = new List<Point3d>(pieces.Count);
            for (int i = 0; i < pieces.Count; i++)
            {
                passthrough.Add(pieces[i]?.DuplicateCurve());
                zero.Add(Vector3d.Zero);
                origCentroids.Add(valid[i] ? centroids[i] : Point3d.Origin);
            }
            da.SetDataList(0, passthrough);
            da.SetDataList(1, zero);
            da.SetDataList(2, origCentroids);
            da.SetData(3, 0.0);
            da.SetData(4, 0.0);
            da.SetData(5, applyPhysics
                ? "No valid pieces to settle."
                : "Apply Physics is false. Pass-through.");
            return;
        }

        var sw = Stopwatch.StartNew();

        // Mean bounding radius drives SphereCollide. Tradeoff: pieces of
        // varying size share one collision radius. For trencadís (similar-
        // sized pieces) this is acceptable; the radius factor input lets
        // the user dial it up or down without polygon-level math.
        double meanR = 0;
        var validRadii = new List<double>(validCount);
        for (int i = 0; i < pieces.Count; i++)
            if (valid[i]) { meanR += radii[i]; validRadii.Add(radii[i]); }
        meanR /= validCount;
        var collideRadius = meanR * radiusFactor;

        // Build PhysicalSystem. Pre-add particles so we know their
        // indices for readback without relying on FindParticleIndex
        // semantics that vary across Kangaroo versions.
        var ps = new PhysicalSystem();
        var pieceIdx = new int[pieces.Count];
        for (int i = 0; i < pieces.Count; i++)
        {
            if (!valid[i]) { pieceIdx[i] = -1; continue; }
            ps.AddParticle(centroids[i], 1.0);
            pieceIdx[i] = ps.ParticleCount() - 1;
        }

        // Goals.
        var goals = new List<IGoal>(validCount + 8);

        if (anchorS > 0)
        {
            for (int i = 0; i < pieces.Count; i++)
                if (valid[i])
                    goals.Add(new Anchor(pieceIdx[i], centroids[i], anchorS));
        }

        if (collideS > 0 && validCount >= 2)
        {
            var centroidList = new List<Point3d>(validCount);
            for (int i = 0; i < pieces.Count; i++)
                if (valid[i]) centroidList.Add(centroids[i]);
            goals.Add(new SphereCollide(centroidList, collideRadius, collideS));
        }

        // OnCurve for boundary-near pieces.
        var boundaryCurves = new List<Curve>();
        if (boundaryS > 0)
        {
            foreach (var s in sheets)
                if (s != null && s.IsClosed) boundaryCurves.Add(s);
            if (holesTree != null)
            {
                foreach (var path in holesTree.Paths)
                    foreach (var ghc in holesTree.get_Branch(path))
                        if (ghc is GH_Curve gc && gc.Value != null && gc.Value.IsClosed)
                            boundaryCurves.Add(gc.Value);
            }
            if (boundaryCurves.Count > 0)
            {
                var threshold = meanR * 1.5;
                for (int i = 0; i < pieces.Count; i++)
                {
                    if (!valid[i]) continue;
                    foreach (var bc in boundaryCurves)
                    {
                        if (!bc.ClosestPoint(centroids[i], out var t)) continue;
                        var dist = centroids[i].DistanceTo(bc.PointAt(t));
                        if (dist > threshold) continue;
                        goals.Add(new OnCurve(
                            new List<Point3d> { centroids[i] },
                            bc, boundaryS));
                        break;
                    }
                }
            }
        }

        // Resolve Point3d → particle index per goal. Goals constructed
        // with explicit indices (Anchor) skip this; goals with Point3d
        // (SphereCollide, OnCurve) get deduped against pre-added
        // particles using the same tol.
        // 2026-05-22 fix: Anchor goals with an explicit ParticleIndex
        // ctor have PIndex pre-set. Calling AssignPIndex on them in
        // certain Kangaroo 2.5+ builds triggers a NullReferenceException
        // when the goal's internal PIndex array is partially populated.
        // Skip Anchor explicitly; Point3d-based goals (SphereCollide,
        // OnCurve) still get resolved.
        const double Ptol = 1e-4;
        foreach (var g in goals)
        {
            if (g is Anchor) continue;
            ps.AssignPIndex(g, Ptol);
        }

        // Step. Looser convergence threshold than F-2D-002.F8 V1 (1e-6
        // not 1e-9) so the solver early-exits as soon as motion is
        // negligible — relevant for the lightweight problem where final
        // moves are small.
        const double VSumThreshold = 1e-6;
        double finalVSum = 0;
        int itUsed = 0;
        for (int it = 0; it < iter; it++)
        {
            ps.Step(goals, true, VSumThreshold);
            finalVSum = ps.GetvSum();
            itUsed = it + 1;
            if (finalVSum < VSumThreshold) break;
        }

        // 2026-05-22 -- per-vertex boundary collider.
        // After the Kangaroo step proposes a new centroid for each piece,
        // before applying the translation, binary-search the magnitude
        // so EVERY vertex of the piece stays inside at least one outer
        // sheet boundary and outside every hole. Hard constraint; not a
        // Kangaroo soft goal -- the collider is enforced after the solve.
        List<Curve> outerSheets = null;
        List<Curve> holeCurves = null;
        List<Point3d[]> pieceVertexSamples = null;
        if (strictContainment)
        {
            outerSheets = new List<Curve>();
            foreach (var s in sheets)
                if (s != null && s.IsClosed && s.IsValid) outerSheets.Add(s);
            holeCurves = new List<Curve>();
            if (holesTree != null)
            {
                foreach (var path in holesTree.Paths)
                    foreach (var ghc in holesTree.get_Branch(path))
                        if (ghc is GH_Curve gc && gc.Value != null && gc.Value.IsClosed)
                            holeCurves.Add(gc.Value);
            }
            pieceVertexSamples = new List<Point3d[]>(pieces.Count);
            for (int i = 0; i < pieces.Count; i++)
                pieceVertexSamples.Add(valid[i] && pieces[i] != null
                    ? SampleCurveVertices(pieces[i])
                    : null);
        }

        // Translate each piece by (final − original centroid). Shape and
        // edge lengths preserved exactly because the original curve is
        // duplicated and translated, never deformed.
        var settled = new List<Curve>(pieces.Count);
        var translations = new List<Vector3d>(pieces.Count);
        var finalCentroids = new List<Point3d>(pieces.Count);
        int clampedPieceCount = 0;
        int frozenPieceCount = 0;
        for (int i = 0; i < pieces.Count; i++)
        {
            if (!valid[i] || pieces[i] == null)
            {
                settled.Add(pieces[i]?.DuplicateCurve());
                translations.Add(Vector3d.Zero);
                finalCentroids.Add(valid[i] ? centroids[i] : Point3d.Origin);
                continue;
            }
            var newPos = ps.GetPosition(pieceIdx[i]);
            var v = newPos - centroids[i];

            if (strictContainment
                && pieceVertexSamples[i] != null)
            {
                // Boundary + self-overlap clamp combined. Sliding pair-
                // overlap test: piece i's translated curve must not
                // overlap any piece already in `settled` (j < i).
                double safeFraction = ClampTranslationByContainmentAndOverlap(
                    pieces[i], pieceVertexSamples[i], centroids[i], v,
                    outerSheets, holeCurves, settled);
                if (safeFraction <= 0.0) frozenPieceCount++;
                else if (safeFraction < 1.0) clampedPieceCount++;
                v *= safeFraction;
            }

            var c = pieces[i].DuplicateCurve();
            c.Translate(v);
            settled.Add(c);
            translations.Add(v);
            finalCentroids.Add(centroids[i] + v);
        }

        // Residual overlap = sum of pair intersection areas. Bbox
        // prefilter so we only do the expensive boolean for nearby
        // pairs. This is the "dynamic tolerance" output.
        double residualOverlap = ComputeResidualOverlap(settled, 0.001);

        sw.Stop();
        da.SetDataList(0, settled);
        da.SetDataList(1, translations);
        da.SetDataList(2, finalCentroids);
        da.SetData(3, finalVSum);
        da.SetData(4, residualOverlap);
        var report =
            $"Trencadís dynamic settle: {validCount}/{pieces.Count} pieces\n" +
            $"Iterations    : {itUsed}/{iter} (vSum {finalVSum:E2})\n" +
            $"Particles     : {ps.ParticleCount()}\n" +
            $"Goals         : {goals.Count}\n" +
            $"Mean radius   : {meanR:F4}\n" +
            $"Collide R     : {collideRadius:F4} (factor {radiusFactor:F2})\n" +
            $"Anchor/Col/Bp : {anchorS:F2} / {collideS:F2} / {boundaryS:F2}\n" +
            $"Strict cont   : {(strictContainment ? "ON" : "off")}" +
                (strictContainment ? $" (clamped {clampedPieceCount}, frozen {frozenPieceCount})\n" : "\n") +
            $"Residual ovr  : {residualOverlap:F4}\n" +
            $"Runtime       : {sw.ElapsedMilliseconds} ms\n";
        da.SetData(5, report);
    }

    // ─── Per-vertex boundary containment (post-Kangaroo hard constraint) ─

    private static Point3d[] SampleCurveVertices(Curve c)
    {
        // Prefer the curve's native polyline vertices for fidelity. Fall
        // back to a 32-point sampling for non-polyline curves.
        if (c.TryGetPolyline(out var poly))
        {
            var pts = new Point3d[poly.Count];
            for (int i = 0; i < poly.Count; i++) pts[i] = poly[i];
            return pts;
        }
        const int sampleCount = 32;
        var ts = c.DivideByCount(sampleCount, true);
        if (ts == null || ts.Length == 0) return new Point3d[0];
        var output = new Point3d[ts.Length];
        for (int i = 0; i < ts.Length; i++) output[i] = c.PointAt(ts[i]);
        return output;
    }

    private static bool AllVerticesInside(
        Point3d[] sampleVerts, Point3d centroidNow, Vector3d offset,
        List<Curve> outerSheets, List<Curve> holeCurves)
    {
        // sampleVerts were captured relative to the ORIGINAL centroid;
        // the proposed new center is (centroidNow + offset). The world
        // position of vertex k is sampleVerts[k] + offset, since
        // sampleVerts already lives in world coords at centroidNow.
        const double tol = 1e-4;
        for (int k = 0; k < sampleVerts.Length; k++)
        {
            var p = sampleVerts[k] + offset;
            // Must lie inside at least one outer sheet.
            if (outerSheets.Count > 0)
            {
                bool insideAny = false;
                for (int s = 0; s < outerSheets.Count; s++)
                {
                    var cont = outerSheets[s].Contains(p, Plane.WorldXY, tol);
                    if (cont == PointContainment.Inside || cont == PointContainment.Coincident)
                    { insideAny = true; break; }
                }
                if (!insideAny) return false;
            }
            // Must NOT lie inside any hole.
            for (int h = 0; h < holeCurves.Count; h++)
            {
                var cont = holeCurves[h].Contains(p, Plane.WorldXY, tol);
                if (cont == PointContainment.Inside) return false;
            }
        }
        return true;
    }

    private static double ClampTranslationByVertexContainment(
        Point3d[] sampleVerts, Point3d centroidNow, Vector3d proposed,
        List<Curve> outerSheets, List<Curve> holeCurves)
    {
        // Try full translation; if all vertices land inside, accept.
        if (AllVerticesInside(sampleVerts, centroidNow, proposed, outerSheets, holeCurves))
            return 1.0;
        // Otherwise binary-search the largest safe fraction in [0, 1].
        // Edge case: even at fraction 0 the piece may violate (input
        // already partially outside). Return 0 to keep the piece frozen.
        if (!AllVerticesInside(sampleVerts, centroidNow, Vector3d.Zero, outerSheets, holeCurves))
            return 0.0;
        double lo = 0.0, hi = 1.0;
        const int searchSteps = 12; // resolves to ~1/4096 of the move
        for (int s = 0; s < searchSteps; s++)
        {
            double mid = (lo + hi) * 0.5;
            if (AllVerticesInside(sampleVerts, centroidNow, proposed * mid, outerSheets, holeCurves))
                lo = mid;
            else
                hi = mid;
        }
        return lo;
    }

    private static double ClampTranslationByContainmentAndOverlap(
        Curve sourcePiece, Point3d[] sampleVerts, Point3d centroidNow,
        Vector3d proposed,
        List<Curve> outerSheets, List<Curve> holeCurves,
        List<Curve> settledSoFar)
    {
        // Combined constraint: boundary containment (cheap point test
        // on sampled vertices) AND self-overlap with earlier pieces
        // (expensive curve-curve test). Boundary check runs first so the
        // cheap test rejects obviously-bad fractions before the costly
        // overlap test.
        bool FullCheck(double fraction)
        {
            var v = proposed * fraction;
            if (!AllVerticesInside(sampleVerts, centroidNow, v, outerSheets, holeCurves))
                return false;
            if (settledSoFar == null || settledSoFar.Count == 0) return true;
            // Build the candidate curve once at this fraction.
            var candidate = sourcePiece.DuplicateCurve();
            candidate.Translate(v);
            var candidateBb = candidate.GetBoundingBox(false);
            for (int j = 0; j < settledSoFar.Count; j++)
            {
                var prev = settledSoFar[j];
                if (prev == null || !prev.IsClosed) continue;
                var prevBb = prev.GetBoundingBox(false);
                // Bbox prefilter.
                if (candidateBb.Max.X < prevBb.Min.X - 1e-6 ||
                    candidateBb.Min.X > prevBb.Max.X + 1e-6 ||
                    candidateBb.Max.Y < prevBb.Min.Y - 1e-6 ||
                    candidateBb.Min.Y > prevBb.Max.Y + 1e-6)
                    continue;
                // Curve boolean intersection. Non-null + closed = overlap.
                Curve[] inter = null;
                try { inter = Curve.CreateBooleanIntersection(candidate, prev, 0.001); }
                catch { inter = null; }
                if (inter == null) continue;
                for (int k = 0; k < inter.Length; k++)
                {
                    var c = inter[k];
                    if (c == null || !c.IsClosed) continue;
                    var ap = AreaMassProperties.Compute(c);
                    if (ap != null && ap.Area > 1e-6) return false;
                }
            }
            return true;
        }

        if (FullCheck(1.0)) return 1.0;
        if (!FullCheck(0.0)) return 0.0;
        double lo = 0.0, hi = 1.0;
        const int searchSteps = 12;
        for (int s = 0; s < searchSteps; s++)
        {
            double mid = (lo + hi) * 0.5;
            if (FullCheck(mid)) lo = mid;
            else hi = mid;
        }
        return lo;
    }

    private static double ComputeResidualOverlap(IList<Curve> curves, double tol)
    {
        double total = 0;
        var bboxes = new BoundingBox[curves.Count];
        for (int i = 0; i < curves.Count; i++)
            bboxes[i] = curves[i] == null
                ? BoundingBox.Empty
                : curves[i].GetBoundingBox(false);

        for (int i = 0; i < curves.Count; i++)
        {
            var ci = curves[i];
            if (ci == null || !ci.IsClosed) continue;
            var bbI = bboxes[i];
            for (int j = i + 1; j < curves.Count; j++)
            {
                var cj = curves[j];
                if (cj == null || !cj.IsClosed) continue;
                var bbJ = bboxes[j];
                if (bbI.Max.X < bbJ.Min.X - tol || bbI.Min.X > bbJ.Max.X + tol ||
                    bbI.Max.Y < bbJ.Min.Y - tol || bbI.Min.Y > bbJ.Max.Y + tol) continue;

                Curve[] inter = null;
                try { inter = Curve.CreateBooleanIntersection(ci, cj, tol); }
                catch { inter = null; }
                if (inter == null) continue;
                foreach (var c in inter)
                {
                    if (c == null || !c.IsClosed) continue;
                    var ap = AreaMassProperties.Compute(c);
                    if (ap != null) total += ap.Area;
                }
            }
        }
        return total;
    }
}
