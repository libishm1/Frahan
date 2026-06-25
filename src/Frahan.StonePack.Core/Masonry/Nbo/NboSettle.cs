#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;
using Frahan.Masonry.Physics;      // BulletSettleService + types
using Frahan.Masonry.Geometry;     // CoacdMeshDecompose (collision-proxy convex pieces)
using Frahan.Masonry.Interfaces;   // MeshSnapshot

namespace Frahan.Masonry.Nbo;

// =============================================================================
// NboSettle -- physics CONFIRMATION of an NBO-produced wall. The analytic gate
// (CoM-over-support + d/h + seating) is a cheap per-stone pre-filter; this is the
// holistic check: drop the whole placed wall into Bullet and measure how far each
// stone moves. Small displacement = the wall holds; large = a stone would topple.
//
// This is the second of the three stability tiers in the design:
//   1. analytic gate   (NboPlanner.Gate)   -- per pose, microseconds, no physics
//   2. Bullet settle    (this)              -- whole wall, real rigid-body physics
//   3. CRA limit analysis (RbeQpFormulation) -- per interface graph, NOT yet wired
//      into the NBO loop (needs a MasonryAssembly built from the settled contacts;
//      flagged as the remaining hardening step).
//
// COLLISION PROXY -- the stones are NOT cut or fractured. Each stone is settled
// as its CoACD convex-piece decomposition glued into ONE compound rigid body, so
// it stays a whole stone while colliding ACCURATELY against its neighbours. A
// single convex hull is too big (it fills the concavities), so touching stones'
// hulls overlap and Bullet shoves them apart (the "explosion"); the CoACD pieces
// hug the real surface, so they touch without overlap and the wall settles into
// seat. Falls back to the single hull only when the CoACD shim is unavailable.
// Offset into a wide box so the container side walls (2 m away) add no support.
// =============================================================================

/// <summary>Per-wall settle-confirmation result.</summary>
public sealed class SettleConfirmResult
{
    /// <summary>False if the Bullet native backend is unavailable (no libbulletc).</summary>
    public bool Available;
    /// <summary>Metres each stone moved when settled (index matches the input list).</summary>
    public double[] Displacement = new double[0];
    /// <summary>Degrees each stone rotated when settled.</summary>
    public double[] RotationDeg = new double[0];
    /// <summary>The SETTLED stone meshes (the raw drop-to-contact wall after Bullet
    /// rocks each stone into seat). Feed these to <see cref="NboCra.ConfirmCra"/> --
    /// the CRA QP needs the settled contact PATCHES, not the un-settled point
    /// contacts. Null entries where a stone was missing from the settle result.</summary>
    public Mesh[] SettledMeshes = new Mesh[0];
    /// <summary>How many stones stayed within the movement / rotation tolerances.</summary>
    public int Held;
    public int Total;
    public double MaxDisplacement;
    public double MeanDisplacement;
    /// <summary>True if the stones used CoACD collision pieces (accurate), false if
    /// they fell back to single convex hulls (the shim was unavailable).</summary>
    public bool UsedCoacd;
}

public static class NboSettle
{
    /// <summary>
    /// Settle the placed wall in Bullet and report per-stone movement. A stone is
    /// "held" if it moved &lt;= <paramref name="moveTol"/> m and rotated
    /// &lt;= <paramref name="rotTolDeg"/> deg. Stones must be the PLACED meshes
    /// (already at their world wall poses).
    /// </summary>
    public static SettleConfirmResult ConfirmSettle(
        IReadOnlyList<Mesh> placedStones, double moveTol = 0.02, double rotTolDeg = 8.0)
    {
        if (placedStones == null) throw new ArgumentNullException(nameof(placedStones));
        var res = new SettleConfirmResult { Total = placedStones.Count };
        if (placedStones.Count == 0) { res.Available = true; return res; }
        if (!BulletSettleService.IsAvailable) { res.Available = false; return res; }
        res.Available = true;

        // world bounds -> a wide box so side walls are far (no artificial support).
        var bb = BoundingBox.Empty;
        for (int i = 0; i < placedStones.Count; i++) bb.Union(placedStones[i].GetBoundingBox(true));
        const double margin = 2.0;
        double ox = -bb.Min.X + margin, oy = -bb.Min.Y + margin;   // offset into [0,W]x[0,D]
        var box = new SettleContainer
        {
            Width = (bb.Max.X - bb.Min.X) + 2 * margin,
            Depth = (bb.Max.Y - bb.Min.Y) + 2 * margin,
            Height = (bb.Max.Z - bb.Min.Z) + 0.5,
        };

        // Collision proxy: single convex hulls + the thin Bullet margin (accurate now) + the volume CoM,
        // so each stone rests true instead of rolling. NOTE: this is the WHOLE-wall settle; it can still
        // shift an un-settled point-contact wall (every stone moves at once). SettleIncremental is the
        // reliable path -- it seats each stone onto the FIXED already-built wall, with no cascade. Hulls
        // (not CoACD) keep the Confirm toggle fast.
        res.UsedCoacd = false;
        var cparams = FastCoacd();

        var stones = new List<SettleStone>(placedStones.Count);
        foreach (var m in placedStones)
        {
            var d = m.GetBoundingBox(true).Diagonal;
            stones.Add(new SettleStone
            {
                ConvexPieces = BuildPieces(m, ox, oy, false, cparams),
                Mass = Math.Max(1e-3, Math.Abs(d.X * d.Y * d.Z)),
                CenterOfMass = MeshCoM(m, ox, oy),
            });
        }

        var opt = new SettleOptions
        {
            Friction = 0.7,
            Lift = 0.004,            // minimal lift: we want to confirm in place, not re-pack
            SettleSteps = 700,
            SolverIterations = 60,
            TampRounds = 0,
        };
        SettleResult settled = BulletSettleService.Settle(stones, box, opt);

        res.Displacement = new double[placedStones.Count];
        res.RotationDeg = new double[placedStones.Count];
        res.SettledMeshes = new Mesh[placedStones.Count];
        double sum = 0.0;
        int n = Math.Min(settled.Stones.Count, placedStones.Count);
        for (int i = 0; i < n; i++)
        {
            var sr = settled.Stones[i];
            // seed centroid = Centroid; settled centroid = Translation (both in offset coords).
            double dx = sr.Translation[0] - sr.Centroid[0];
            double dy = sr.Translation[1] - sr.Centroid[1];
            double dz = sr.Translation[2] - sr.Centroid[2];
            double disp = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double trace = sr.Rotation[0] + sr.Rotation[4] + sr.Rotation[8];
            double ang = Math.Acos(Math.Max(-1.0, Math.Min(1.0, (trace - 1.0) * 0.5))) * 180.0 / Math.PI;

            res.Displacement[i] = disp;
            res.RotationDeg[i] = ang;
            sum += disp;
            if (disp > res.MaxDisplacement) res.MaxDisplacement = disp;
            if (disp <= moveTol && ang <= rotTolDeg) res.Held++;

            // settled mesh in WORLD: v' = R*(v_offset - Centroid) + Translation, then un-offset.
            // (R is row-major 3x3 for column-vector math, per SettleStoneResult.)
            var R = sr.Rotation; var C = sr.Centroid; var T = sr.Translation;
            var rot = Transform.Identity;
            rot.M00 = R[0]; rot.M01 = R[1]; rot.M02 = R[2];
            rot.M10 = R[3]; rot.M11 = R[4]; rot.M12 = R[5];
            rot.M20 = R[6]; rot.M21 = R[7]; rot.M22 = R[8];
            var rigid = Transform.Translation(T[0], T[1], T[2]) * rot * Transform.Translation(-C[0], -C[1], -C[2]);
            var total = Transform.Translation(-ox, -oy, 0) * rigid * Transform.Translation(ox, oy, 0);
            var sm = placedStones[i].DuplicateMesh();
            sm.Transform(total);
            res.SettledMeshes[i] = sm;
        }
        res.MeanDisplacement = n > 0 ? sum / n : 0.0;
        return res;
    }

    /// <summary>
    /// INCREMENTAL settle-as-placed -- the RELIABLE wall confirmation. Stones are seated bottom-up:
    /// each stone is dropped (dynamic) onto the already-settled stones held FIXED, so there is no
    /// whole-wall push-apart cascade (the failure mode of <see cref="ConfirmSettle"/> on an
    /// un-settled point-contact wall). Returns the settled wall + per-stone movement.
    /// <para><paramref name="useCoacd"/> defaults to FALSE: a fixed base means single hulls do not
    /// cascade either, and hulls are both faster and -- empirically -- more robust here (a coarse
    /// CoACD piece occasionally produces a degenerate hull that Bullet flings away). Set it true only
    /// for accurate contact patches once CoACD piece-validation is hardened.</para>
    /// </summary>
    public static SettleConfirmResult SettleIncremental(
        IReadOnlyList<Mesh> placedStones, double moveTol = 0.02, double rotTolDeg = 8.0, bool useCoacd = false)
    {
        if (placedStones == null) throw new ArgumentNullException(nameof(placedStones));
        var res = new SettleConfirmResult { Total = placedStones.Count };
        int n = placedStones.Count;
        if (n == 0) { res.Available = true; return res; }
        if (!BulletSettleService.IsAvailable) { res.Available = false; return res; }
        res.Available = true;

        bool coacd = useCoacd && CoacdMeshDecompose.IsAvailable;
        res.UsedCoacd = coacd;
        if (coacd) { try { CoacdMeshDecompose.SetLogLevel("off"); } catch { } }
        var cparams = FastCoacd();

        var bb = BoundingBox.Empty;
        for (int i = 0; i < n; i++) bb.Union(placedStones[i].GetBoundingBox(true));
        const double margin = 2.0;
        double ox = -bb.Min.X + margin, oy = -bb.Min.Y + margin;
        var box = new SettleContainer
        {
            Width = (bb.Max.X - bb.Min.X) + 2 * margin,
            Depth = (bb.Max.Y - bb.Min.Y) + 2 * margin,
            Height = (bb.Max.Z - bb.Min.Z) + 0.5,
        };

        // Decompose each stone ONCE (placed pose, offset coords); the pieces are reused once the
        // stone becomes a fixed support. mass ~ bbox volume.
        var pieces = new double[n][][];
        var mass = new double[n];
        for (int i = 0; i < n; i++)
        {
            pieces[i] = BuildPieces(placedStones[i], ox, oy, coacd, cparams);
            var d = placedStones[i].GetBoundingBox(true).Diagonal;
            mass[i] = Math.Max(1e-3, Math.Abs(d.X * d.Y * d.Z));
        }

        // Bottom-up order: seat the lower stones first, then drop the upper stones onto them.
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        Array.Sort(order, (a, b) => placedStones[a].GetBoundingBox(true).Center.Z
                                   .CompareTo(placedStones[b].GetBoundingBox(true).Center.Z));

        res.Displacement = new double[n];
        res.RotationDeg = new double[n];
        res.SettledMeshes = new Mesh[n];

        var opt = new SettleOptions
        {
            Friction = 0.8,
            Lift = 0.003,            // tiny: just clear initial contact with the fixed base
            SettleSteps = 500,
            SolverIterations = 60,
            TampRounds = 0,
        };

        var committed = new List<SettleStone>(n);   // already-seated stones, held FIXED at settled pose
        double sum = 0.0;
        foreach (int idx in order)
        {
            var dyn = new SettleStone { ConvexPieces = pieces[idx], Mass = mass[idx], Fixed = false };
            var stones = new List<SettleStone>(committed.Count + 1);
            stones.AddRange(committed);
            stones.Add(dyn);                          // the dynamic stone is LAST in the list

            var settled = BulletSettleService.Settle(stones, box, opt);
            if (settled.Stones.Count == 0) continue;
            var sr = settled.Stones[settled.Stones.Count - 1];   // result for the dynamic stone

            double dx = sr.Translation[0] - sr.Centroid[0];
            double dy = sr.Translation[1] - sr.Centroid[1];
            double dz = sr.Translation[2] - sr.Centroid[2];
            double disp = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double trace = sr.Rotation[0] + sr.Rotation[4] + sr.Rotation[8];
            double ang = Math.Acos(Math.Max(-1.0, Math.Min(1.0, (trace - 1.0) * 0.5))) * 180.0 / Math.PI;

            res.Displacement[idx] = disp;
            res.RotationDeg[idx] = ang;
            sum += disp;
            if (disp > res.MaxDisplacement) res.MaxDisplacement = disp;
            if (disp <= moveTol && ang <= rotTolDeg) res.Held++;

            var rigid = RigidOffset(sr);              // offset-coords rigid motion of this stone
            var total = Transform.Translation(-ox, -oy, 0) * rigid * Transform.Translation(ox, oy, 0);
            var sm = placedStones[idx].DuplicateMesh();
            sm.Transform(total);
            res.SettledMeshes[idx] = sm;

            // Commit this stone as a FIXED support at its settled pose (move its pieces by the rigid motion).
            var settledPieces = new double[pieces[idx].Length][];
            for (int p = 0; p < pieces[idx].Length; p++) settledPieces[p] = TransformPiece(pieces[idx][p], rigid);
            committed.Add(new SettleStone { ConvexPieces = settledPieces, Mass = mass[idx], Fixed = true });
        }
        res.MeanDisplacement = n > 0 ? sum / n : 0.0;
        return res;
    }

    /// <summary>
    /// Settle ONE candidate stone (given at its placed world pose) onto the FIXED already-built
    /// stones, and return the WORLD rigid motion that takes it to its settled rest -- compose this
    /// onto the candidate's placement to commit the settled pose. <paramref name="disp"/> /
    /// <paramref name="rotDeg"/> report how far it moved: a small move means it found a real seat; a
    /// large move (or a flyaway from a placed overlap) means the caller should REJECT this candidate
    /// and try another. Hull collision (fast + robust); the fixed base means no push-apart cascade.
    /// Returns identity (disp 0) when the Bullet backend is unavailable, so callers degrade to the
    /// analytic gate.
    /// </summary>
    public static Transform SettleOnto(
        Mesh placedCandidate, IReadOnlyList<Mesh> fixedBelow, out double disp, out double rotDeg,
        bool useCoacd = true)
    {
        disp = 0.0; rotDeg = 0.0;
        if (placedCandidate == null) throw new ArgumentNullException(nameof(placedCandidate));
        if (!BulletSettleService.IsAvailable) return Transform.Identity;
        if (useCoacd && CoacdMeshDecompose.IsAvailable) { try { CoacdMeshDecompose.SetLogLevel("off"); } catch { } }
        var cp = FastCoacd();

        // Only stones whose XY-bbox is within `reach` of the candidate can interact with it; far stones
        // are skipped so the settle stays O(local), not O(whole wall) -- the key speed-up for long/tall walls.
        var cbb = placedCandidate.GetBoundingBox(true);
        const double reach = 0.35;
        var near = new List<Mesh>();
        if (fixedBelow != null)
            foreach (var m in fixedBelow)
                if (m != null)
                {
                    var mb = m.GetBoundingBox(true);
                    if (mb.Max.X < cbb.Min.X - reach || mb.Min.X > cbb.Max.X + reach ||
                        mb.Max.Y < cbb.Min.Y - reach || mb.Min.Y > cbb.Max.Y + reach) continue;
                    near.Add(m);
                }

        var bb = cbb;
        foreach (var m in near) bb.Union(m.GetBoundingBox(true));
        const double margin = 2.0;
        double ox = -bb.Min.X + margin, oy = -bb.Min.Y + margin;
        var box = new SettleContainer
        {
            Width = (bb.Max.X - bb.Min.X) + 2 * margin,
            Depth = (bb.Max.Y - bb.Min.Y) + 2 * margin,
            Height = (bb.Max.Z - bb.Min.Z) + 0.5,
        };

        var stones = new List<SettleStone>();
        foreach (var m in near)
        {
            var dd = m.GetBoundingBox(true).Diagonal;
            stones.Add(new SettleStone
            {
                ConvexPieces = BuildPieces(m, ox, oy, useCoacd, cp),
                Mass = Math.Max(1e-3, Math.Abs(dd.X * dd.Y * dd.Z)),
                CenterOfMass = MeshCoM(m, ox, oy),
                Fixed = true,
            });
        }
        var cd = placedCandidate.GetBoundingBox(true).Diagonal;
        stones.Add(new SettleStone   // the candidate is the single DYNAMIC body, last in the list
        {
            ConvexPieces = BuildPieces(placedCandidate, ox, oy, useCoacd, cp),
            Mass = Math.Max(1e-3, Math.Abs(cd.X * cd.Y * cd.Z)),
            CenterOfMass = MeshCoM(placedCandidate, ox, oy),
            Fixed = false,
        });

        // Light settle: a single body onto a FIXED base needs little gentle ramping, so a short ramp +
        // fewer settle steps run several x faster (this is called many times by the in-loop seating search).
        var opt = new SettleOptions { Friction = 0.8, Lift = 0.003, SettleSteps = 220, SolverIterations = 60, TampRounds = 0, RampStepsPerPhase = 60 };
        var settled = BulletSettleService.Settle(stones, box, opt);
        if (settled.Stones.Count == 0) return Transform.Identity;
        var sr = settled.Stones[settled.Stones.Count - 1];

        double dx = sr.Translation[0] - sr.Centroid[0];
        double dy = sr.Translation[1] - sr.Centroid[1];
        double dz = sr.Translation[2] - sr.Centroid[2];
        disp = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        double trace = sr.Rotation[0] + sr.Rotation[4] + sr.Rotation[8];
        rotDeg = Math.Acos(Math.Max(-1.0, Math.Min(1.0, (trace - 1.0) * 0.5))) * 180.0 / Math.PI;

        var rigid = RigidOffset(sr);
        return Transform.Translation(-ox, -oy, 0) * rigid * Transform.Translation(ox, oy, 0);
    }

    private static CoacdParameters FastCoacd() => new CoacdParameters
    {
        // FAST collision-proxy quality: a coarse decomposition (a handful of pieces) is enough to stop
        // hull-overlap; we do not need a tight visual decomposition. Capped + low MCTS, ~1-3 s/stone.
        Threshold = 0.12, Merge = true, MaxConvexHull = 8,
        MctsNodes = 12, MctsIterations = 40, SampleResolution = 800,
    };

    // CoACD decomposition (or single-hull fallback) of a placed stone, in offset world coords.
    private static double[][] BuildPieces(Mesh m, double ox, double oy, bool useCoacd, CoacdParameters cp)
    {
        var pieces = new List<double[]>();
        if (useCoacd && CoacdMeshDecompose.IsAvailable)
        {
            try
            {
                // Guard: accept the CoACD pieces only if EVERY piece is sane and stays inside the
                // stone's own bbox (+10% pad). A degenerate piece with a vertex flung far outside makes
                // a ConvexHullShape with a huge AABB that Bullet resolves as a violent impulse (the 6 m
                // "flyaway"). If any piece is bad, discard them all and fall back to the single hull.
                var bbx = m.GetBoundingBox(true);
                double pad = 0.1 * bbx.Diagonal.Length + 1e-6;
                double minx = bbx.Min.X + ox - pad, maxx = bbx.Max.X + ox + pad;
                double miny = bbx.Min.Y + oy - pad, maxy = bbx.Max.Y + oy + pad;
                double minz = bbx.Min.Z - pad,      maxz = bbx.Max.Z + pad;
                var tmp = new List<double[]>();
                bool ok = true;
                foreach (var part in CoacdMeshDecompose.Decompose(ToSnapshot(m), cp))
                {
                    var pv = OffsetVerts(part.VertexCoordsXyz, ox, oy);
                    if (pv.Length < 12) { ok = false; break; }   // < 4 vertices = degenerate hull
                    for (int i = 0; i + 2 < pv.Length; i += 3)
                        if (pv[i] < minx || pv[i] > maxx || pv[i + 1] < miny || pv[i + 1] > maxy ||
                            pv[i + 2] < minz || pv[i + 2] > maxz) { ok = false; break; }
                    if (!ok) break;
                    tmp.Add(pv);
                }
                if (ok) pieces.AddRange(tmp);
            }
            catch { pieces.Clear(); }
        }
        if (pieces.Count == 0)   // fallback: the whole-stone single hull
        {
            var pts = new double[m.Vertices.Count * 3];
            for (int v = 0; v < m.Vertices.Count; v++)
            {
                Point3d p = m.Vertices[v];
                pts[3 * v + 0] = p.X + ox; pts[3 * v + 1] = p.Y + oy; pts[3 * v + 2] = p.Z;
            }
            pieces.Add(pts);
        }
        return pieces.ToArray();
    }

    // SettleStoneResult -> rigid motion in offset coords: p' = R*(p - Centroid) + Translation.
    private static Transform RigidOffset(SettleStoneResult sr)
    {
        var R = sr.Rotation; var C = sr.Centroid; var T = sr.Translation;
        var rot = Transform.Identity;
        rot.M00 = R[0]; rot.M01 = R[1]; rot.M02 = R[2];
        rot.M10 = R[3]; rot.M11 = R[4]; rot.M12 = R[5];
        rot.M20 = R[6]; rot.M21 = R[7]; rot.M22 = R[8];
        return Transform.Translation(T[0], T[1], T[2]) * rot * Transform.Translation(-C[0], -C[1], -C[2]);
    }

    private static double[] TransformPiece(double[] flat, Transform t)
    {
        var a = new double[flat.Length];
        for (int i = 0; i + 2 < flat.Length; i += 3)
        {
            var p = new Point3d(flat[i], flat[i + 1], flat[i + 2]);
            p.Transform(t);
            a[i] = p.X; a[i + 1] = p.Y; a[i + 2] = p.Z;
        }
        return a;
    }

    // RhinoCommon Mesh -> MeshSnapshot (flat verts + triangulated faces) for CoACD.
    private static MeshSnapshot ToSnapshot(Mesh m)
    {
        var verts = new double[m.Vertices.Count * 3];
        for (int i = 0; i < m.Vertices.Count; i++)
        {
            Point3d p = m.Vertices[i];
            verts[3 * i + 0] = p.X; verts[3 * i + 1] = p.Y; verts[3 * i + 2] = p.Z;
        }
        var tl = new List<int>(m.Faces.Count * 3);
        for (int f = 0; f < m.Faces.Count; f++)
        {
            var fc = m.Faces[f];
            tl.Add(fc.A); tl.Add(fc.B); tl.Add(fc.C);
            if (fc.IsQuad) { tl.Add(fc.A); tl.Add(fc.C); tl.Add(fc.D); }
        }
        return new MeshSnapshot(verts, tl.ToArray());
    }

    private static double[] OffsetVerts(IReadOnlyList<double> v, double ox, double oy)
    {
        var a = new double[v.Count];
        for (int i = 0; i + 2 < v.Count; i += 3) { a[i] = v[i] + ox; a[i + 1] = v[i + 1] + oy; a[i + 2] = v[i + 2]; }
        return a;
    }

    // Volume centroid (true CoM) of a closed mesh, offset into settle coords. Falls back to the bbox
    // centre if the mass-properties solve fails (open/degenerate mesh).
    private static double[] MeshCoM(Mesh m, double ox, double oy)
    {
        try
        {
            var vmp = VolumeMassProperties.Compute(m);
            if (vmp != null) { var c = vmp.Centroid; return new[] { c.X + ox, c.Y + oy, c.Z }; }
        }
        catch { }
        var cc = m.GetBoundingBox(true).Center;
        return new[] { cc.X + ox, cc.Y + oy, cc.Z };
    }
}
