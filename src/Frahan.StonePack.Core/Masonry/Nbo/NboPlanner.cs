#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Masonry.Nbo;

// =============================================================================
// NboPlanner -- the dry-stone "Next-Best-Object" placement core: given a stone
// shape (StoneShapeAnalyzer) it decides HOW the stone should sit (orientation)
// and WHETHER that pose is admissible (analytic stability gate). This is the
// missing pillar P3 of the bottom-up masonry loop -- everything else (settle,
// CRA, scan-back, inventory selection, rim-path) composes around this decision.
//
// The orientation rule is the validated HYBRID (NBO_3D_DESIGN.md, "Track 4"),
// resolved by a 3-course stacking study on real ETH1100 stones:
//   * WHICH face rests down = the hull STABLE face (CoM-over-face, Goldberg &
//     Mirtich 1999). This is the stability backbone: stacked 3 courses it gave
//     12/12 stable, where a pure PCA-section rest gave only 6/12 (half topple).
//   * The YAW about that resting normal = the PCA LONG axis laid into the wall
//     (ETH/Johns 2020, the #1-broken dry-stone rule "length into the wall").
//   * The PCA *section size* (smaller vs larger cross-section) is ORTHOGONAL to
//     stability and is NOT a placement lever -- both topple equally; only the
//     stable face confers stability. The hybrid composes both: 12/12 stable AND
//     bond 0.97 (long axis into the wall).
//
// The analytic gate is the cheap accept/reject BEFORE any physics (the 3-course
// rig showed ~half of un-gated long-axis placements fall):
//   1. CoM-over-support: the placed CoM projects (along gravity) inside the
//      resting-face contact polygon.
//   2. d/h >= 0.5: depth-into-wall span / height span (ETH stability heuristic).
//   3. seating: the upward contact normal is within ~45 deg of vertical
//      (e_z . n_up >= 0.7) -- the face seats flat, not on a precarious edge.
// Physics settle (BulletSettleService) and the CRA QP are the harder arbiters
// layered on top; this gate is the fast pre-filter that feeds them few, sane
// candidates.
//
// Deterministic, Rhino-bound (consistent with RubbleWallSettle). No RNG.
// =============================================================================

/// <summary>Tunables for the analytic stability gate.</summary>
public sealed class NboGateOptions
{
    /// <summary>Minimum depth-into-wall / height ratio (ETH "d/h &gt; 0.5").</summary>
    public double MinDepthOverHeight { get; set; } = 0.5;
    /// <summary>Minimum upward-contact-normal . vertical (seating flatness, ~45 deg).</summary>
    public double MinSeatingDot { get; set; } = 0.7;
    /// <summary>Minimum normalized CoM-over-support margin to accept (0 = on the edge).</summary>
    public double MinComMargin { get; set; } = 0.0;
}

/// <summary>Verdict of the analytic stability gate for one placed pose.</summary>
public sealed class StabilityVerdict
{
    public bool Stable;
    public bool ComSupported;
    /// <summary>Signed CoM-over-support margin, normalized by sqrt(contact area).</summary>
    public double ComMargin;
    public double DepthOverHeight;
    public double SeatingDot;
    public string Reason = "";
}

public static partial class NboPlanner
{
    /// <summary>
    /// The HYBRID placement transform (Track 4): rest the stone on its chosen
    /// stable face, then yaw its long axis into the wall, then seat it at
    /// <paramref name="bedZ"/>. <paramref name="wallRunDir"/> is the horizontal
    /// direction the wall runs (stones lay their length perpendicular to it);
    /// pass <see cref="Vector3d.XAxis"/> for a wall running along X.
    /// </summary>
    public static Transform HybridPlacement(StoneShape shape, DominantFace restFace, Vector3d wallRunDir, double bedZ)
    {
        if (shape == null) throw new ArgumentNullException(nameof(shape));
        if (restFace == null) throw new ArgumentNullException(nameof(restFace));

        // 1. rest the chosen face flat down (its outward normal -> -Z).
        Transform rest = Transform.Rotation(restFace.Normal, -Vector3d.ZAxis, restFace.Centroid);

        // 2. yaw the long axis into the wall (perpendicular to the wall run).
        Vector3d depth = HorizontalDepthDir(wallRunDir);
        Vector3d eL = shape.AxisLong; eL.Transform(rest);
        var eh = new Vector3d(eL.X, eL.Y, 0.0);
        if (eh.Length < 1e-9) eh = depth;                 // long axis is vertical -> nothing to yaw
        else eh.Unitize();
        if (eh * depth < 0.0) eh = -eh;                   // axis is a line: pick the smaller turn
        Point3d cr = shape.Com; cr.Transform(rest);
        double ang = SignedAngleAboutZ(eh, depth);
        Transform yaw = Transform.Rotation(ang, Vector3d.ZAxis, cr);

        Transform orient = yaw * rest;

        // 3. seat: drop so the lowest hull point lands on bedZ.
        Transform drop = Transform.Translation(0, 0, bedZ - LowestZ(shape.Hull, orient));
        return drop * orient;
    }

    /// <summary>Track-1 ablation: rest the stable face down with NO long-axis
    /// yaw (the stability backbone alone). Used to bench bond vs the hybrid.</summary>
    public static Transform StableFaceDown(StoneShape shape, DominantFace restFace, double bedZ)
    {
        if (shape == null) throw new ArgumentNullException(nameof(shape));
        if (restFace == null) throw new ArgumentNullException(nameof(restFace));
        Transform rest = Transform.Rotation(restFace.Normal, -Vector3d.ZAxis, restFace.Centroid);
        Transform drop = Transform.Translation(0, 0, bedZ - LowestZ(shape.Hull, rest));
        return drop * rest;
    }

    /// <summary>
    /// One honest single-step: analyze, orient by the hybrid rule, gate it, and
    /// translate to <paramref name="targetXY"/> on the bed. Returns the placement
    /// transform (mesh-local -> world) and the stability verdict. This is the
    /// pose primitive; inventory selection, rim-path localization and the
    /// multi-objective cost compose around it (NboPlanner.NextPose, next).
    /// </summary>
    public static (Transform placement, StabilityVerdict verdict, StoneShape shape) PlaceOnBed(
        Mesh stone, Point3d targetXY, Vector3d wallRunDir, double bedZ, NboGateOptions opt = null)
    {
        if (stone == null) throw new ArgumentNullException(nameof(stone));
        opt = opt ?? new NboGateOptions();
        var shape = StoneShapeAnalyzer.Analyze(stone);
        var rest = StoneShapeAnalyzer.BestRestingFace(shape);
        Transform orient = HybridPlacement(shape, rest, wallRunDir, bedZ);

        // slide to the target footprint XY (CoM over the target column).
        Point3d com = shape.Com; com.Transform(orient);
        Transform slide = Transform.Translation(targetXY.X - com.X, targetXY.Y - com.Y, 0);
        Transform placement = slide * orient;

        var verdict = Gate(shape, placement, rest, wallRunDir, opt);
        return (placement, verdict, shape);
    }

    /// <summary>The analytic stability gate: CoM-over-support + d/h + seating.</summary>
    public static StabilityVerdict Gate(StoneShape shape, Transform placement, DominantFace restFace, Vector3d wallRunDir, NboGateOptions opt = null)
    {
        if (shape == null) throw new ArgumentNullException(nameof(shape));
        if (restFace == null) throw new ArgumentNullException(nameof(restFace));
        opt = opt ?? new NboGateOptions();
        var v = new StabilityVerdict();

        // 1. CoM over the placed contact polygon, projected along gravity (-Z).
        Point3d com = shape.Com; com.Transform(placement);
        ComOverSupportWorld(shape.Hull, restFace, placement, com, out bool supported, out double margin, out double contactArea);
        v.ComSupported = supported;
        v.ComMargin = (supported ? 1.0 : -1.0) * (margin / Math.Max(1e-9, Math.Sqrt(Math.Max(contactArea, 1e-12))));

        // 2. d/h: placed depth-into-wall span / height span.
        Vector3d depth = HorizontalDepthDir(wallRunDir);
        double dSpan = PlacedSpan(shape.Hull, placement, depth);
        double hSpan = PlacedSpan(shape.Hull, placement, Vector3d.ZAxis);
        v.DepthOverHeight = hSpan > 1e-9 ? dSpan / hSpan : double.PositiveInfinity;

        // 3. seating: upward contact normal vs vertical.
        Vector3d n = restFace.Normal; n.Transform(placement);   // points "down/out" of the resting face
        v.SeatingDot = -n.Z;                                    // upward seating normal = -n; e_z . (-n) = -n.Z

        v.Stable = v.ComSupported
                   && v.ComMargin >= opt.MinComMargin
                   && v.DepthOverHeight >= opt.MinDepthOverHeight
                   && v.SeatingDot >= opt.MinSeatingDot;
        v.Reason = v.Stable ? "stable"
            : (!v.ComSupported ? "CoM outside support"
            : v.DepthOverHeight < opt.MinDepthOverHeight ? $"d/h {v.DepthOverHeight:F2} < {opt.MinDepthOverHeight:F2}"
            : v.SeatingDot < opt.MinSeatingDot ? $"seating {v.SeatingDot:F2} < {opt.MinSeatingDot:F2}"
            : $"CoM margin {v.ComMargin:F2} < {opt.MinComMargin:F2}");
        return v;
    }

    // ─── geometry helpers ────────────────────────────────────────────────────

    /// <summary>Horizontal direction perpendicular to the wall run (the
    /// "into the wall" depth). Defaults to +Y for a degenerate run dir.</summary>
    private static Vector3d HorizontalDepthDir(Vector3d wallRunDir)
    {
        var run = new Vector3d(wallRunDir.X, wallRunDir.Y, 0.0);
        if (run.Length < 1e-9) return Vector3d.YAxis;
        run.Unitize();
        var depth = Vector3d.CrossProduct(Vector3d.ZAxis, run); // 90 deg, horizontal
        depth.Unitize();
        return depth;
    }

    private static double SignedAngleAboutZ(Vector3d from, Vector3d to)
    {
        double dot = from.X * to.X + from.Y * to.Y;
        double crossZ = from.X * to.Y - from.Y * to.X;
        return Math.Atan2(crossZ, dot);
    }

    private static double LowestZ(Mesh hull, Transform t)
    {
        double lo = double.MaxValue;
        for (int i = 0; i < hull.Vertices.Count; i++)
        {
            Point3d p = hull.Vertices[i]; p.Transform(t);
            if (p.Z < lo) lo = p.Z;
        }
        return lo == double.MaxValue ? 0.0 : lo;
    }

    private static double PlacedSpan(Mesh hull, Transform t, Vector3d axis)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        for (int i = 0; i < hull.Vertices.Count; i++)
        {
            Point3d p = hull.Vertices[i]; p.Transform(t);
            double s = p.X * axis.X + p.Y * axis.Y + p.Z * axis.Z;
            if (s < lo) lo = s; if (s > hi) hi = s;
        }
        return hi - lo;
    }

    // CoM over the contact polygon under WORLD gravity (project along -Z onto the
    // placed resting-face triangles, test the CoM's XY inside them). The margin is
    // the XY distance to the nearest contact BOUNDARY edge (edges used by exactly
    // one member triangle) -- internal triangulation diagonals are skipped, else a
    // diagonal through the CoM would falsely read margin 0. Returns contact area
    // too (for margin normalization).
    private static void ComOverSupportWorld(
        Mesh hull, DominantFace restFace, Transform placement, Point3d com,
        out bool inside, out double margin, out double contactArea)
    {
        inside = false; margin = double.MaxValue; contactArea = 0.0;
        double px = com.X, py = com.Y;

        var pv = new Dictionary<int, Point3d>();          // placed vertices by id
        var edgeCount = new Dictionary<long, int>();
        var edgeVerts = new Dictionary<long, (int u, int v)>();

        foreach (int t in restFace.TriangleIds)
        {
            var face = hull.Faces[t];
            Point3d a = Placed(hull, face.A, placement, pv);
            Point3d b = Placed(hull, face.B, placement, pv);
            Point3d c = Placed(hull, face.C, placement, pv);
            contactArea += 0.5 * Math.Abs((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y));
            if (!inside && PointInTriangleXY(px, py, a, b, c)) inside = true;
            TallyEdge(edgeCount, edgeVerts, face.A, face.B);
            TallyEdge(edgeCount, edgeVerts, face.B, face.C);
            TallyEdge(edgeCount, edgeVerts, face.C, face.A);
        }
        foreach (var kv in edgeCount)
        {
            if (kv.Value != 1) continue;                  // boundary edges only
            var (u, v) = edgeVerts[kv.Key];
            margin = Math.Min(margin, DistPointSegXY(px, py, pv[u], pv[v]));
        }
        if (margin == double.MaxValue) margin = 0.0;
    }

    private static Point3d Placed(Mesh hull, int id, Transform t, Dictionary<int, Point3d> cache)
    {
        if (!cache.TryGetValue(id, out var p)) { p = hull.Vertices[id]; p.Transform(t); cache[id] = p; }
        return p;
    }

    private static void TallyEdge(Dictionary<long, int> count, Dictionary<long, (int u, int v)> verts, int u, int v)
    {
        long key = u < v ? ((long)u << 32) | (uint)v : ((long)v << 32) | (uint)u;
        count[key] = count.TryGetValue(key, out int c) ? c + 1 : 1;
        if (!verts.ContainsKey(key)) verts[key] = u < v ? (u, v) : (v, u);
    }

    private static bool PointInTriangleXY(double px, double py, Point3d a, Point3d b, Point3d c)
    {
        double d1 = (px - b.X) * (a.Y - b.Y) - (a.X - b.X) * (py - b.Y);
        double d2 = (px - c.X) * (b.Y - c.Y) - (b.X - c.X) * (py - c.Y);
        double d3 = (px - a.X) * (c.Y - a.Y) - (c.X - a.X) * (py - a.Y);
        bool neg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        bool pos = (d1 > 0) || (d2 > 0) || (d3 > 0);
        return !(neg && pos);
    }

    private static double DistPointSegXY(double px, double py, Point3d a, Point3d b)
    {
        double abx = b.X - a.X, aby = b.Y - a.Y;
        double apx = px - a.X, apy = py - a.Y;
        double len2 = abx * abx + aby * aby;
        double t = len2 > 1e-20 ? (apx * abx + apy * aby) / len2 : 0.0;
        t = Math.Max(0.0, Math.Min(1.0, t));
        double qx = a.X + abx * t, qy = a.Y + aby * t;
        double dx = px - qx, dy = py - qy;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
