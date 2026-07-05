#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace Frahan.Masonry.Nbo;

// =============================================================================
// NboFill -- the incremental "Next-Best-Object" fill loop that turns a stone
// inventory + a wall target into an ORDERED placement sequence (the thing a
// robot consumes). This closes the canonical ETH loop (Furrer 2017 / Johns
// 2020) at the planning level:
//
//   front (rim) -> candidate stones -> orient (hybrid) -> drop-to-contact onto
//   the as-built -> analytic stability gate -> multi-objective cost -> pick the
//   best -> commit -> advance the front -> repeat.
//
// "Rim-path" here is specialized to a STRAIGHT wall running along +X: the
// placement front is the running fill position along the course, bumping to the
// next course (running-bond offset) when the course is full. The general
// target-envelope rim (as-built upper surface  intersect  target Brep) is the
// later generalization; the front abstraction is the same.
//
// Drop-to-contact is now in Core (was demo-harness only): each candidate is
// seeded above the wall, then ray-cast straight down so it rests on the stones
// below / the ground -- no floating, real contact for the gate + the sequence.
//
// Deterministic: inventory scanned in index order, deterministic cost, index
// tie-breaks, no RNG.
// =============================================================================

/// <summary>Tunables for the greedy wall-fill loop.</summary>
public sealed class NboFillOptions
{
    /// <summary>Wall length to fill along the run direction (m).</summary>
    public double WallLength { get; set; } = 4.0;
    /// <summary>Stop once the wall top reaches this height (m).</summary>
    public double TargetHeight { get; set; } = 1.6;
    /// <summary>Minimum gap between stones along a course (m).</summary>
    public double Gap { get; set; } = 0.02;
    /// <summary>Running-bond offset applied on alternating courses (m).</summary>
    public double CourseOffset { get; set; } = 0.28;
    /// <summary>Horizontal wall-run direction (stones lay their length across it).</summary>
    public Vector3d WallRunDir { get; set; } = Vector3d.XAxis;
    /// <summary>Optional closed target envelope (Brep). When set, the wall length /
    /// height are taken from its bounds and a placement is rejected if the stone's
    /// CoM falls outside it -- the (axis-aligned, straight-wall) target-envelope
    /// rim. Position the envelope with its base near the origin, running along +X.</summary>
    public Brep Envelope { get; set; } = null;
    /// <summary>Optional PLAN-RIM spine: a horizontal curve the wall follows. When
    /// passed to <see cref="NboPlanner.FillSpine"/>, the placement front advances
    /// along the spine by arc length, the wall-run direction is the spine tangent
    /// (so the long axis lays into the wall along the local normal), and stones are
    /// centred on the spine. A straight line reproduces <see cref="NboPlanner.FillWall"/>.</summary>
    public Curve Spine { get; set; } = null;
    /// <summary>Safety cap on total placements.</summary>
    public int MaxStones { get; set; } = 400;
    /// <summary>Only commit poses that pass the analytic stability gate.</summary>
    public bool RequireStable { get; set; } = true;
    public NboGateOptions Gate { get; set; } = new NboGateOptions();

    // ---- settle-in-the-loop (physical seat check during planning) ----
    /// <summary>When true, each picked candidate is settle-TESTED onto the FIXED already-built wall
    /// (a single-stone Bullet drop) before it is committed; a candidate that translates more than
    /// <see cref="SettleMoveTol"/> m or rotates more than <see cref="SettleRotTolDeg"/> deg is REJECTED
    /// and the next-best stone is tried (up to <see cref="SettleMaxAttempts"/> times). Stones are then
    /// committed at their SETTLED pose, so the wall is built physically seated -- the in-loop
    /// settle-as-placed (Furrer/Johns) check. Off by default (it runs a Bullet settle per candidate;
    /// needs the libbulletc backend), but functional: it keeps the stones that bed firmly and drops
    /// the ones that slip, so the result settles tighter than the un-validated wall (at the cost of
    /// placing fewer stones). Whole-assembly stability is still the job of
    /// <see cref="NboCra.ConfirmSettledCra"/> (incremental settle + CRA).</summary>
    public bool SettleValidate { get; set; } = false;
    /// <summary>Reject a candidate that translates more than this when settled (m). The settle is
    /// accurate now (a thin Bullet collision margin), so a well-seated stone moves only a few cm as it
    /// beds onto the irregular stones below; a slip/topple moves far more.</summary>
    public double SettleMoveTol { get; set; } = 0.12;
    /// <summary>Reject a candidate that rotates more than this when settled (deg). Bedding a stone onto
    /// an irregular course-top rocks it ~15-20 deg into the gaps (a valid seat, committed at that pose);
    /// a true topple is much larger. 20 lets upper courses bed; tighten toward 12 for flatter stock.</summary>
    public double SettleRotTolDeg { get; set; } = 20.0;
    /// <summary>Max alternative stones to try at one front before giving up the position.</summary>
    public int SettleMaxAttempts { get; set; } = 3;
    /// <summary>How many of a stone's stable resting faces to try as candidate orientations (the
    /// analyzer ranks them by area). 1 = the single best face (the old behaviour). Higher gives the
    /// planner the orientation VARIETY to nestle a stone onto a bumpy course-top -- the key to seating
    /// upper courses. Used by both the analytic pick (best by cost) and the settle-validated pick
    /// (best by how tightly it beds).</summary>
    public int OrientationsPerStone { get; set; } = 3;

    // ---- multi-objective cost weights (lower cost = better) ----
    /// <summary>Penalty per metre the stone drops into a void (void-under proxy).</summary>
    public double WeightVoid { get; set; } = 1.0;
    /// <summary>Penalty on placed height (prefer low, level coursing).</summary>
    public double WeightHeight { get; set; } = 0.3;
    /// <summary>Reward for CoM-over-support margin (subtracted).</summary>
    public double WeightStability { get; set; } = 0.3;
    /// <summary>Reward for footprint length along the run (covers more front).</summary>
    public double WeightFill { get; set; } = 0.5;
}

/// <summary>One committed placement in the sequence.</summary>
public sealed class NboPlacementStep
{
    /// <summary>Index into the inventory list passed to <see cref="NboPlanner.FillWall"/>.</summary>
    public int StoneIndex;
    /// <summary>Mesh-local -> world placement transform.</summary>
    public Transform Placement;
    public StabilityVerdict Verdict;
    public int Course;
    public double Cost;
    /// <summary>Where on the wall run (min-X of the footprint) the stone was placed.</summary>
    public double FrontX;
    /// <summary>Placed extent along the wall-run direction (for advancing the front).</summary>
    public double AlongRun;
    /// <summary>World bounds of the placed stone.</summary>
    public BoundingBox PlacedBounds;
}

/// <summary>The ordered, gated placement sequence -- the robot job.</summary>
public sealed class PlacementSequence
{
    public List<NboPlacementStep> Steps = new List<NboPlacementStep>();
    public int Placed => Steps.Count;
    public int StableCount;
    public double FilledLength;
    public double TopHeight;
    public int Courses;
}

public static partial class NboPlanner
{
    /// <summary>
    /// Greedy wall fill: place the best stone at the running front, drop it onto
    /// the as-built, gate it, advance the front, course by course, until the
    /// target height is reached or no remaining stone fits. Returns the ordered
    /// <see cref="PlacementSequence"/>. Deterministic.
    /// </summary>
    public static PlacementSequence FillWall(IReadOnlyList<Mesh> inventory, NboFillOptions opt = null)
    {
        if (inventory == null) throw new ArgumentNullException(nameof(inventory));
        opt = opt ?? new NboFillOptions();

        var shapes = new StoneShape[inventory.Count];
        for (int i = 0; i < inventory.Count; i++) shapes[i] = StoneShapeAnalyzer.Analyze(inventory[i]);

        // target-envelope rim: when an envelope is given, its bounds set the wall
        // length / height (and EvaluateCandidate rejects out-of-envelope poses).
        double wallLen = opt.WallLength, targetH = opt.TargetHeight;
        if (opt.Envelope != null)
        {
            var eb = opt.Envelope.GetBoundingBox(true);
            wallLen = eb.Max.X;
            targetH = eb.Max.Z;
        }

        var used = new HashSet<int>();
        var below = new List<Mesh>();
        var seq = new PlacementSequence();
        double top = 0.0;
        int course = 0;

        while (seq.Placed < opt.MaxStones && used.Count < inventory.Count)
        {
            double offset = (course % 2 == 1) ? opt.CourseOffset : 0.0;
            double frontX = 0.0;
            int placedThisCourse = 0;

            while (frontX < wallLen && used.Count < inventory.Count && seq.Placed < opt.MaxStones)
            {
                double seedZ = top + 0.10;                 // seed above the wall, then drop
                NboPlacementStep step;
                Mesh placedMesh;
                if (opt.SettleValidate)
                {
                    // Seating search: try the top-K orientations of every stone, settle each onto the
                    // FIXED wall, and take the one that beds tightest (committed at its settled pose).
                    step = NextPoseValidated(inventory, shapes, used, frontX, seedZ, offset, course, below, opt, out placedMesh);
                }
                else
                {
                    step = NextPose(inventory, shapes, used, frontX, seedZ, offset, course, below, opt);
                    placedMesh = step != null ? Place(inventory[step.StoneIndex], step.Placement) : null;
                }
                if (step == null) break;                   // no stone seats stably here

                used.Add(step.StoneIndex);
                below.Add(placedMesh);
                seq.Steps.Add(step);
                if (step.Verdict.Stable) seq.StableCount++;
                top = Math.Max(top, step.PlacedBounds.Max.Z);
                frontX = step.PlacedBounds.Max.X + opt.Gap;
                placedThisCourse++;
            }

            if (placedThisCourse == 0) break;              // cannot place any remaining stone
            course++;
            if (top >= targetH) break;
        }

        seq.Courses = course;
        seq.TopHeight = top;
        seq.FilledLength = wallLen;
        return seq;
    }

    /// <summary>
    /// Plan-rim fill: like <see cref="FillWall"/>, but the wall follows an arbitrary
    /// horizontal <paramref name="spine"/> curve. The front advances along the spine
    /// by arc length; at each station the wall-run direction is the spine tangent (so
    /// the long axis lays into the wall along the local normal) and the stone is
    /// centred on the spine. Courses stack by height. A straight line reproduces
    /// <see cref="FillWall"/>. Deterministic.
    /// </summary>
    public static PlacementSequence FillSpine(IReadOnlyList<Mesh> inventory, Curve spine, NboFillOptions opt = null)
    {
        if (inventory == null) throw new ArgumentNullException(nameof(inventory));
        if (spine == null) throw new ArgumentNullException(nameof(spine));
        opt = opt ?? new NboFillOptions();

        var shapes = new StoneShape[inventory.Count];
        for (int i = 0; i < inventory.Count; i++) shapes[i] = StoneShapeAnalyzer.Analyze(inventory[i]);

        double spineLen = spine.GetLength();
        if (spineLen < 1e-6) return new PlacementSequence();
        double targetH = opt.TargetHeight;
        if (opt.Envelope != null) targetH = opt.Envelope.GetBoundingBox(true).Max.Z;

        var used = new HashSet<int>();
        // Drop-to-contact targets PREVIOUS courses only -- on a curved spine,
        // consecutive same-course stones have overlapping axis-aligned bboxes, so
        // including them would let each drop onto its lateral neighbour and the wall
        // would staircase upward instead of coursing. A stone rests on the course
        // below, not on its neighbour.
        var belowPrev = new List<Mesh>();
        var curCourse = new List<Mesh>();
        var seq = new PlacementSequence();
        double top = 0.0;
        int course = 0;

        while (seq.Placed < opt.MaxStones && used.Count < inventory.Count)
        {
            double s = (course % 2 == 1) ? opt.CourseOffset : 0.0;
            int placedThisCourse = 0;

            while (s < spineLen && used.Count < inventory.Count && seq.Placed < opt.MaxStones)
            {
                double seedZ = top + 0.10;
                if (!spine.LengthParameter(s, out double prm)) break;
                Point3d p = spine.PointAt(prm);
                Vector3d t = spine.TangentAt(prm); t.Z = 0.0;
                if (!t.Unitize()) t = Vector3d.XAxis;

                var step = NextPoseAt(inventory, shapes, used, new Point3d(p.X, p.Y, 0.0), t, seedZ, course, belowPrev, opt);
                if (step == null) break;

                used.Add(step.StoneIndex);
                var pm = inventory[step.StoneIndex].DuplicateMesh(); pm.Transform(step.Placement);
                curCourse.Add(pm);
                seq.Steps.Add(step);
                if (step.Verdict.Stable) seq.StableCount++;
                top = Math.Max(top, step.PlacedBounds.Max.Z);
                s += step.AlongRun + opt.Gap;
                placedThisCourse++;
            }

            if (placedThisCourse == 0) break;
            belowPrev.AddRange(curCourse);   // commit this course as a drop target
            curCourse.Clear();
            course++;
            if (top >= targetH) break;
        }

        seq.Courses = course;
        seq.TopHeight = top;
        seq.FilledLength = spineLen;
        return seq;
    }

    /// <summary>Best stone at an arbitrary (anchor point, run-direction) station.</summary>
    public static NboPlacementStep NextPoseAt(
        IReadOnlyList<Mesh> inventory, IReadOnlyList<StoneShape> shapes, ISet<int> used,
        Point3d anchorXY, Vector3d runDir, double seedZ, int course,
        IReadOnlyList<Mesh> below, NboFillOptions opt)
    {
        opt = opt ?? new NboFillOptions();
        NboPlacementStep best = null;
        for (int i = 0; i < inventory.Count; i++)
        {
            if (used != null && used.Contains(i)) continue;
            var step = EvaluateAtPoint(i, inventory[i], shapes[i], anchorXY, runDir, seedZ, course, below, opt);
            if (step == null) continue;
            if (opt.RequireStable && !step.Verdict.Stable) continue;
            if (best == null || step.Cost < best.Cost) best = step;
        }
        return best;
    }

    // Orient one candidate (hybrid) for a general (anchor, run-direction) station,
    // centre it on the anchor, drop it onto the as-built, gate + cost it.
    private static NboPlacementStep EvaluateAtPoint(
        int stoneIndex, Mesh stone, StoneShape shape,
        Point3d anchorXY, Vector3d runDir, double seedZ, int course,
        IReadOnlyList<Mesh> below, NboFillOptions opt)
    {
        var rest = StoneShapeAnalyzer.BestRestingFace(shape);
        if (rest == null) return null;

        Transform t0 = HybridPlacement(shape, rest, runDir, seedZ);
        var probe = stone.DuplicateMesh(); probe.Transform(t0);
        var bb0 = probe.GetBoundingBox(true);
        Point3d c0 = bb0.Center;
        Transform slide = Transform.Translation(anchorXY.X - c0.X, anchorXY.Y - c0.Y, 0);
        Transform ft = slide * t0;
        var probe2 = stone.DuplicateMesh(); probe2.Transform(ft);

        var candBB = probe2.GetBoundingBox(true);
        var near = new List<Mesh>();
        for (int b = 0; b < below.Count; b++)
        {
            var bb = below[b].GetBoundingBox(true);
            if (candBB.Min.X <= bb.Max.X && candBB.Max.X >= bb.Min.X &&
                candBB.Min.Y <= bb.Max.Y && candBB.Max.Y >= bb.Min.Y)
                near.Add(below[b]);
        }
        double drop = DropToContact(probe2, near, 0.0);
        Transform final = Transform.Translation(0, 0, -drop) * ft;

        var placed = stone.DuplicateMesh(); placed.Transform(final);
        var pbb = placed.GetBoundingBox(true);

        if (opt.Envelope != null)
        {
            Point3d comW = shape.Com; comW.Transform(final);
            if (!opt.Envelope.IsPointInside(comW, 1e-6, false)) return null;
        }

        var verdict = Gate(shape, final, rest, runDir, opt.Gate);
        double alongRun = ProjSpan(placed, runDir);
        double height = pbb.Max.Z - pbb.Min.Z;
        double cost = opt.WeightVoid * drop
                    + opt.WeightHeight * height
                    - opt.WeightStability * Math.Max(0.0, verdict.ComMargin)
                    - opt.WeightFill * alongRun;

        return new NboPlacementStep
        {
            StoneIndex = stoneIndex,
            Placement = final,
            Verdict = verdict,
            Course = course,
            Cost = cost,
            FrontX = anchorXY.X,
            AlongRun = alongRun,
            PlacedBounds = pbb,
        };
    }

    private static double ProjSpan(Mesh m, Vector3d dir)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        for (int i = 0; i < m.Vertices.Count; i++)
        {
            Point3d p = m.Vertices[i];
            double s = p.X * dir.X + p.Y * dir.Y + p.Z * dir.Z;
            if (s < lo) lo = s; if (s > hi) hi = s;
        }
        return hi - lo;
    }

    /// <summary>
    /// The single Next-Best-Object decision at a given front: score every unused
    /// inventory stone (hybrid orient -> drop-to-contact -> gate -> cost) and
    /// return the best admissible placement, or null if none fits.
    /// </summary>
    public static NboPlacementStep NextPose(
        IReadOnlyList<Mesh> inventory, IReadOnlyList<StoneShape> shapes, ISet<int> used,
        double frontX, double seedZ, double offset, int course,
        IReadOnlyList<Mesh> below, NboFillOptions opt)
    {
        if (inventory == null) throw new ArgumentNullException(nameof(inventory));
        opt = opt ?? new NboFillOptions();

        int k = Math.Max(1, opt.OrientationsPerStone);
        NboPlacementStep best = null;
        for (int i = 0; i < inventory.Count; i++)
        {
            if (used != null && used.Contains(i)) continue;
            foreach (var rf in TopRestFaces(shapes[i], k))
            {
                var step = EvaluateCandidate(i, inventory[i], shapes[i], rf, frontX, seedZ, offset, course, below, opt);
                if (step == null) continue;
                if (opt.RequireStable && !step.Verdict.Stable) continue;
                if (best == null || step.Cost < best.Cost) best = step;
            }
        }
        return best;
    }

    /// <summary>
    /// Settle-VALIDATED next pose: try the top-K resting faces of every unused stone, settle each
    /// candidate onto the FIXED already-built wall, and return the one that beds TIGHTEST (smallest
    /// settle movement within tolerance), committed at its SETTLED pose. Returns null (and a null
    /// <paramref name="placedMesh"/>) if nothing seats here. This is the seating search that lets upper
    /// courses bed onto bumpy tops: orientation variety + the physical settle pick together. Needs the
    /// Bullet backend (falls back to the best analytic pose if it is unavailable).
    /// </summary>
    public static NboPlacementStep NextPoseValidated(
        IReadOnlyList<Mesh> inventory, IReadOnlyList<StoneShape> shapes, ISet<int> used,
        double frontX, double seedZ, double offset, int course,
        IReadOnlyList<Mesh> below, NboFillOptions opt, out Mesh placedMesh)
    {
        if (inventory == null) throw new ArgumentNullException(nameof(inventory));
        opt = opt ?? new NboFillOptions();
        placedMesh = null;
        int k = Math.Max(1, opt.OrientationsPerStone);

        // Phase 1 (cheap): gather every admissible analytic candidate (stone x top-K face).
        var cands = new List<NboPlacementStep>();
        for (int i = 0; i < inventory.Count; i++)
        {
            if (used != null && used.Contains(i)) continue;
            foreach (var rf in TopRestFaces(shapes[i], k))
            {
                var cand = EvaluateCandidate(i, inventory[i], shapes[i], rf, frontX, seedZ, offset, course, below, opt);
                if (cand == null) continue;
                if (opt.RequireStable && !cand.Verdict.Stable) continue;
                cands.Add(cand);
            }
        }
        if (cands.Count == 0) return null;
        cands.Sort((a, b) => a.Cost.CompareTo(b.Cost));

        // Phase 2 (settle): physically test the candidates and keep the tightest bed. We do NOT prune by
        // the analytic cost -- it penalizes drop, but a stone that beds into a gap drops MORE, so pruning
        // would discard the very seaters we want. The light + locally-bounded settle keeps each test cheap;
        // a generous cap only guards pathological inventories.
        int probe = Math.Min(cands.Count, 150);
        NboPlacementStep best = null; Mesh bestMesh = null; double bestScore = double.MaxValue;
        for (int j = 0; j < probe; j++)
        {
            var cand = cands[j];
            var pm = inventory[cand.StoneIndex].DuplicateMesh(); pm.Transform(cand.Placement);
            double disp, rotDeg;
            Transform delta = NboSettle.SettleOnto(pm, below, out disp, out rotDeg, useCoacd: false);
            if (disp > opt.SettleMoveTol || rotDeg > opt.SettleRotTolDeg) continue;   // does not bed

            // Prefer the TIGHTEST bed (disp dominates); the analytic cost is only a faint tie-break.
            double score = disp + 0.003 * cand.Cost;
            if (score < bestScore)
            {
                cand.Placement = delta * cand.Placement;     // commit at the SETTLED pose
                pm.Transform(delta);
                cand.PlacedBounds = pm.GetBoundingBox(true);
                best = cand; bestMesh = pm; bestScore = score;
            }
        }
        placedMesh = bestMesh;
        return best;
    }

    private static Mesh Place(Mesh stone, Transform t) { var m = stone.DuplicateMesh(); m.Transform(t); return m; }

    // The top-K stable resting faces (analyzer-ranked by descending area), or the
    // single best face when the stone has nothing flagged stable.
    private static IEnumerable<DominantFace> TopRestFaces(StoneShape shape, int k)
    {
        if (shape != null && shape.StableFaces.Count > 0)
        {
            int n = Math.Min(k, shape.StableFaces.Count);
            for (int i = 0; i < n; i++) yield return shape.StableFaces[i];
        }
        else
        {
            var b = StoneShapeAnalyzer.BestRestingFace(shape);
            if (b != null) yield return b;
        }
    }

    // Orient one candidate on the GIVEN resting face (hybrid), slide it to the
    // front, drop it onto the as-built, gate it, and score it. Returns the would-be
    // step (caller filters by stability + cost).
    private static NboPlacementStep EvaluateCandidate(
        int stoneIndex, Mesh stone, StoneShape shape, DominantFace rest,
        double frontX, double seedZ, double offset, int course,
        IReadOnlyList<Mesh> below, NboFillOptions opt)
    {
        if (rest == null) return null;

        Transform t0 = HybridPlacement(shape, rest, opt.WallRunDir, seedZ);
        var probe = stone.DuplicateMesh(); probe.Transform(t0);
        var bb0 = probe.GetBoundingBox(true);

        // slide so the footprint starts at the front (min-X) and centres on the bed (Y=0).
        double dx = (frontX + offset) - bb0.Min.X;
        double dy = 0.0 - 0.5 * (bb0.Min.Y + bb0.Max.Y);
        Transform slide = Transform.Translation(dx, dy, 0);
        Transform ft = slide * t0;
        var probe2 = stone.DuplicateMesh(); probe2.Transform(ft);

        // drop-to-contact onto the (XY-overlapping subset of the) as-built + ground.
        var candBB = probe2.GetBoundingBox(true);
        var near = new List<Mesh>();
        for (int b = 0; b < below.Count; b++)
        {
            var bb = below[b].GetBoundingBox(true);
            if (candBB.Min.X <= bb.Max.X && candBB.Max.X >= bb.Min.X &&
                candBB.Min.Y <= bb.Max.Y && candBB.Max.Y >= bb.Min.Y)
                near.Add(below[b]);
        }
        double drop = DropToContact(probe2, near, 0.0);
        Transform final = Transform.Translation(0, 0, -drop) * ft;

        var placed = stone.DuplicateMesh(); placed.Transform(final);
        var pbb = placed.GetBoundingBox(true);

        // target-envelope rim: reject if the placed CoM falls outside the envelope.
        if (opt.Envelope != null)
        {
            Point3d comW = shape.Com; comW.Transform(final);
            if (!opt.Envelope.IsPointInside(comW, 1e-6, false)) return null;
        }

        var verdict = Gate(shape, final, rest, opt.WallRunDir, opt.Gate);

        double footprint = pbb.Max.X - pbb.Min.X;
        double height = pbb.Max.Z - pbb.Min.Z;
        double cost = opt.WeightVoid * drop
                    + opt.WeightHeight * height
                    - opt.WeightStability * Math.Max(0.0, verdict.ComMargin)
                    - opt.WeightFill * footprint;

        return new NboPlacementStep
        {
            StoneIndex = stoneIndex,
            Placement = final,
            Verdict = verdict,
            Course = course,
            Cost = cost,
            FrontX = pbb.Min.X,
            PlacedBounds = pbb,
        };
    }

    // Distance a mesh can fall straight down before its first vertex contacts a
    // below-mesh or the ground plane z = groundZ.
    private static double DropToContact(Mesh stone, IReadOnlyList<Mesh> below, double groundZ)
    {
        double drop = double.MaxValue;
        var down = -Vector3d.ZAxis;
        foreach (var pv in stone.Vertices)
        {
            Point3d v = pv;
            double d = v.Z - groundZ;
            var ray = new Ray3d(v, down);
            for (int b = 0; b < below.Count; b++)
            {
                double t = Intersection.MeshRay(below[b], ray);
                if (t >= 0.0 && t < d) d = t;
            }
            if (d < drop) drop = d;
        }
        return drop == double.MaxValue ? 0.0 : Math.Max(0.0, drop);
    }
}
