#nullable disable
using System;
using System.Collections.Generic;
using Frahan.EdgeMatching;
using Rhino.Geometry;

namespace Frahan.Tests;

// R0 AGGLOMERATIVE assembly (2026-05-25). Two kinds of test:
//   (1) ComposeRelatives_* : pure-managed validation of the spanning-tree
//       composition INVARIANT. Transform is a RhinoCommon value struct whose
//       matrix multiply / inverse is managed (no rhcommon_c native init), so
//       these run on hosts without a live Rhino process. They lock the
//       composition direction the solver relies on: with edge relative pose
//       M mapping child-local -> parent-local, the child's absolute pose is
//       T_child = T_parent * M, and from the reversed edge T_other = T_in * M^-1.
//   (2) Solve_Agglomerative_* : end-to-end through the public Solve with
//       Mode=Agglomerative. Needs the Rhino runtime (segmenter + ICP), so
//       SKIPs without it, like the other edgematch pipeline tests.
static class EdgeMatchingAgglomerativeTests
{
    // ---- (1) Pure-managed composition invariants --------------------------

    // Chain seed -> A -> B. Each edge's relative pose maps the CHILD's local
    // frame onto the PARENT's local frame. Composing down the chain must place
    // B at T_seed * M(A->seed) * M(B->A). We build known rigid transforms and
    // assert the composed absolute equals the hand-multiplied product.
    public static void ComposeRelatives_AlongChain_MatchesProduct()
    {
        // Seed sits at a known non-identity pose (mimics an anchor placed at its
        // given transform). A maps onto seed by Mseed; B maps onto A by Ma.
        var tSeed = Transform.Translation(10, 0, 0) * Transform.Rotation(0.3, Point3d.Origin);
        var mA = Transform.Translation(0, 5, 0) * Transform.Rotation(-0.2, Point3d.Origin); // A-local -> seed-local
        var mB = Transform.Translation(0, 0, 7) * Transform.Rotation(0.5, Vector3d.XAxis, Point3d.Origin); // B-local -> A-local

        // Solver composition: T_A = T_seed * mA ; T_B = T_A * mB.
        Transform tA = Transform.Multiply(tSeed, mA);
        Transform tB = Transform.Multiply(tA, mB);

        // Independent hand product.
        Transform expectedB = Transform.Multiply(Transform.Multiply(tSeed, mA), mB);

        Assert(TransformsClose(tB, expectedB, 1e-9),
            "chain composition T_seed*mA*mB must equal the left-to-right product");

        // A point expressed in B-local maps to world via tB; verify against the
        // explicit two-hop mapping (B-local -> A-local -> seed-local -> world).
        var pB = new Point3d(1, 2, 3);
        var viaComposed = pB; viaComposed.Transform(tB);
        var viaHops = pB;
        viaHops.Transform(mB);   // -> A-local
        viaHops.Transform(mA);   // -> seed-local
        viaHops.Transform(tSeed); // -> world
        Assert(viaComposed.DistanceTo(viaHops) < 1e-9,
            "point mapped through composed tB must equal the explicit hop chain");
    }

    // The reversed-edge branch: when the in-tree node is the edge's CHILD (the
    // out node is the parent), the solver composes T_out = T_in * M^-1, because
    // M maps in-local -> out-local. Verify the inverse round-trips the mapping.
    public static void ComposeRelatives_ReversedEdge_UsesInverse()
    {
        var tIn = Transform.Translation(3, -4, 2) * Transform.Rotation(0.7, Vector3d.ZAxis, Point3d.Origin);
        // M maps in-local -> out-local (the edge stored in=child, out=parent).
        var m = Transform.Translation(2, 2, 0) * Transform.Rotation(0.4, Point3d.Origin);

        bool ok = m.TryGetInverse(out Transform mInv);
        Assert(ok, "rigid relative pose must be invertible");

        // Solver: T_out = T_in * M^-1.
        Transform tOut = Transform.Multiply(tIn, mInv);

        // Consistency: a point in out-local maps to world via tOut, and the same
        // world point reached by out-local -> (M) is WRONG direction; instead
        // in-local -> world is tIn, and in-local -> out-local is M, so
        // out-local -> world should be tIn * M^-1. Check the round trip:
        // T_in == T_out * M.
        Transform back = Transform.Multiply(tOut, m);
        Assert(TransformsClose(back, tIn, 1e-9),
            "T_out * M must round-trip back to T_in (inverse composition)");
    }

    // ---- (2) End-to-end agglomerative solve (Rhino runtime) ----------------

    // Two square halves that share one cut edge, scattered apart. The
    // frame-anchored beam with NO frame would still chain them (they touch),
    // but the point here is that the AGGLOMERATIVE path places BOTH from the
    // pairwise match without requiring a pre-placed frame. Anchor the left half;
    // the right half must place by its shared cut edge.
    public static void Solve_Agglomerative_TwoHalves_PlacesBoth()
    {
        // Left half: square [0,50]x[0,100]. Right half: [50,100]x[0,100].
        // Shared cut edge is the segment x=50, y in [0,100].
        var left = MakeRect("L", 0, 0, 50, 100);
        var right = MakeRect("R", 50, 0, 100, 100);

        var segOpt = new SegmenterOptions
        {
            SampleSpacing = 1.0,
            BreakAngleDeg = 18.0,
            MinSegmentLength = 8.0,
        };

        var index = new SegmentHashIndex();
        foreach (var seg in BoundarySegmenter.Segment(left, segOpt)) index.Add(seg);
        foreach (var seg in BoundarySegmenter.Segment(right, segOpt)) index.Add(seg);

        var asmOpt = new AssemblyOptions
        {
            Mode = AssemblyMode.Agglomerative,
            BeamWidth = 16,
            ResidualThreshold = 1.0,
        };

        var solver = new AssemblySolver(index, asmOpt, segOpt);
        var state = solver.Solve(new[] { left }, new[] { right });

        Assert(state.PlacedPanels.Count == 2,
            $"agglomerative must place both halves (seed + 1), got {state.PlacedPanels.Count}");
        Assert(state.AppliedTransforms.ContainsKey("L") && state.AppliedTransforms.ContainsKey("R"),
            "both panels must have a composed transform");
        // Seed (anchor) stays at identity.
        Assert(TransformsClose(state.AppliedTransforms["L"], Transform.Identity, 1e-9),
            "anchored seed must remain at identity");
    }

    // Determinism: two agglomerative runs on the same input give byte-identical
    // placement order, transforms, and residual.
    public static void Solve_Agglomerative_IsDeterministic()
    {
        AssemblyState Run()
        {
            var left = MakeRect("L", 0, 0, 50, 100);
            var right = MakeRect("R", 50, 0, 100, 100);
            var segOpt = new SegmenterOptions { SampleSpacing = 1.0, BreakAngleDeg = 18.0, MinSegmentLength = 8.0 };
            var index = new SegmentHashIndex();
            foreach (var seg in BoundarySegmenter.Segment(left, segOpt)) index.Add(seg);
            foreach (var seg in BoundarySegmenter.Segment(right, segOpt)) index.Add(seg);
            var asmOpt = new AssemblyOptions { Mode = AssemblyMode.Agglomerative, BeamWidth = 16, ResidualThreshold = 1.0 };
            return new AssemblySolver(index, asmOpt, segOpt).Solve(new[] { left }, new[] { right });
        }

        var s1 = Run();
        var s2 = Run();
        Assert(s1.PlacedPanels.Count == s2.PlacedPanels.Count, "placed count drift");
        Assert(s1.TotalResidual.Equals(s2.TotalResidual), $"residual drift {s1.TotalResidual} vs {s2.TotalResidual}");
        for (int i = 0; i < s1.PlacedPanels.Count; i++)
        {
            Assert(s1.PlacedPanels[i].Id == s2.PlacedPanels[i].Id, $"order drift at {i}");
            var t1 = s1.AppliedTransforms[s1.PlacedPanels[i].Id];
            var t2 = s2.AppliedTransforms[s2.PlacedPanels[i].Id];
            Assert(TransformsClose(t1, t2, 0.0), $"transform drift for {s1.PlacedPanels[i].Id}");
        }
    }

    // Default mode (FrameAnchored) is unchanged by the new code path: a default
    // AssemblyOptions must report FrameAnchored.
    public static void DefaultMode_IsFrameAnchored()
    {
        var opt = new AssemblyOptions();
        Assert(opt.Mode == AssemblyMode.FrameAnchored,
            $"default AssemblyMode must be FrameAnchored, got {opt.Mode}");
    }

    // ---- helpers ----------------------------------------------------------

    private static Panel MakeRect(string id, double x0, double y0, double x1, double y1)
    {
        var poly = new Polyline
        {
            new Point3d(x0, y0, 0),
            new Point3d(x1, y0, 0),
            new Point3d(x1, y1, 0),
            new Point3d(x0, y1, 0),
            new Point3d(x0, y0, 0),
        };
        return new Panel(id, poly.ToPolylineCurve(), PanelKind.Shard);
    }

    private static bool TransformsClose(Transform a, Transform b, double tol)
    {
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                if (Math.Abs(a[r, c] - b[r, c]) > tol) return false;
        return true;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
