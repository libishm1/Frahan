#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Nbo;
using Rhino.Geometry;

namespace Frahan.Tests;

// Regression tests for the Next-Best-Object dry-stone planner (Frahan.Masonry.Nbo).
// Encodes the validated behaviour: hybrid orientation (rest the stable face, yaw the long
// axis into the wall), the analytic stability gate, deterministic fill, and the grasp ->
// robot-frame bridge. Needs the Rhino runtime (VolumeMassProperties etc.); SKIPs standalone,
// runs in-process. Uses deterministic box inventory so it needs no external dataset.
static class NboPlannerTests
{
    static List<Mesh> BoxInventory(int n)
    {
        var inv = new List<Mesh>();
        var rng = new Random(11);
        for (int i = 0; i < n; i++)
        {
            double sx = 0.45 + rng.NextDouble() * 0.45; // long-ish
            double sy = 0.25 + rng.NextDouble() * 0.25;
            double sz = 0.18 + rng.NextDouble() * 0.18; // flat-ish
            inv.Add(Mesh.CreateFromBox(new BoundingBox(new Point3d(0, 0, 0), new Point3d(sx, sy, sz)), 1, 1, 1));
        }
        return inv;
    }

    // A 3 x 1.5 x 1.5 box rests on its broad (3x1.5) face, lays its long axis (3) into the
    // wall (Y = depth) with the thin axis (1.5) vertical, and passes the gate (d/h ~ 2, seat 1).
    public static void Nbo_BoxSanity()
    {
        var box = Mesh.CreateFromBox(new BoundingBox(new Point3d(0, 0, 0), new Point3d(3.0, 1.5, 1.5)), 1, 1, 1);
        var s = StoneShapeAnalyzer.Analyze(box);
        Assert(s.DominantFaces.Count == 6, $"box dominant faces {s.DominantFaces.Count} != 6");
        var rest = StoneShapeAnalyzer.BestRestingFace(s);
        Assert(Math.Abs(rest.Area - 4.5) < 1e-6, $"rest face area {rest.Area} != 4.5 (broad face)");

        var t = NboPlanner.HybridPlacement(s, rest, Vector3d.XAxis, 0.0);
        var placed = box.DuplicateMesh(); placed.Transform(t);
        var bb = placed.GetBoundingBox(true);
        Assert(Math.Abs((bb.Max.Y - bb.Min.Y) - 3.0) < 1e-3, "long axis (3.0) should lie in Y (depth into wall)");
        Assert(Math.Abs((bb.Max.Z - bb.Min.Z) - 1.5) < 1e-3, "thin axis (1.5) should be vertical (Z)");
        Assert(Math.Abs(bb.Min.Z) < 1e-6, "placed box should seat on z = 0");

        var v = NboPlanner.Gate(s, t, rest, Vector3d.XAxis, null);
        Assert(v.Stable, "box hybrid placement should pass the gate");
        Assert(v.DepthOverHeight > 1.5, $"box d/h {v.DepthOverHeight:F2} should be ~2");
        Assert(v.SeatingDot > 0.99, $"box seating {v.SeatingDot:F2} should be ~1 (flat broad-face rest)");
    }

    // FillWall is deterministic: two runs -> identical placement order, courses, counts.
    public static void Nbo_FillDeterministic()
    {
        var inv = BoxInventory(16);
        var opt = new NboFillOptions { WallLength = 2.5, TargetHeight = 1.0 };
        var a = NboPlanner.FillWall(inv, opt);
        var b = NboPlanner.FillWall(inv, opt);
        Assert(a.Placed == b.Placed, $"placed differs {a.Placed} vs {b.Placed}");
        Assert(a.StableCount == b.StableCount, $"stable differs {a.StableCount} vs {b.StableCount}");
        Assert(a.Courses == b.Courses, $"courses differ {a.Courses} vs {b.Courses}");
        for (int i = 0; i < a.Placed; i++)
            Assert(a.Steps[i].StoneIndex == b.Steps[i].StoneIndex,
                $"placement order differs at {i}: {a.Steps[i].StoneIndex} vs {b.Steps[i].StoneIndex}");
    }

    // The hybrid orientation + the analytic gate keep EVERY committed placement stable
    // (the 3-course finding: stable-face resting propagates stability up the wall).
    public static void Nbo_FillAllStable()
    {
        var seq = NboPlanner.FillWall(BoxInventory(20), new NboFillOptions { WallLength = 2.5, TargetHeight = 1.0 });
        Assert(seq.Placed > 0, "nothing placed");
        Assert(seq.StableCount == seq.Placed, $"only {seq.StableCount}/{seq.Placed} stable (gate must keep all committed)");
    }

    // The place TCP frame points DOWN into the stone (top-pick tool approach), and the UR
    // pose is the ~180-degree (|rvec| ~ pi) tool-down flip.
    public static void Nbo_GraspTcpDown()
    {
        var box = Mesh.CreateFromBox(new BoundingBox(new Point3d(0, 0, 0), new Point3d(1.0, 0.5, 0.4)), 1, 1, 1);
        var s = StoneShapeAnalyzer.Analyze(box);
        var rest = StoneShapeAnalyzer.BestRestingFace(s);
        var grasp = NboGrasp.TopPick(s, rest);
        var t = NboPlanner.HybridPlacement(s, rest, Vector3d.XAxis, 0.0);
        var placeW = NboGrasp.PlaceFrame(grasp, t);
        Assert(placeW.ZAxis.Z < -0.9, $"place TCP Z should point down (z = {placeW.ZAxis.Z:F2})");

        var pose = NboGrasp.ToUrPose(placeW);
        double rmag = Math.Sqrt(pose.Rx * pose.Rx + pose.Ry * pose.Ry + pose.Rz * pose.Rz);
        Assert(rmag > 2.5 && rmag < 3.6, $"UR rvec magnitude {rmag:F2} should be ~pi (tool-down flip)");
    }

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
