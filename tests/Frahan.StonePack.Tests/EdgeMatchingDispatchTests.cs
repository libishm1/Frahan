#nullable disable
using System;
using System.Collections.Generic;
using Frahan.EdgeMatching;
using Rhino.Geometry;

namespace Frahan.Tests;

// Verify that the planarity-based 2D / 3D dispatch is intact across
// Panel.Mode → segmenter → hash-index bucketing → AssemblySolver
// pair-mode pick. Needs Rhino runtime.
static class EdgeMatchingDispatchTests
{
    public static void PlanarPanel_Segmenter_StampsNullTorsion()
    {
        var panel = MakePlanarPanel("planar");
        Assert(panel.Mode == PanelMode.Planar2D,
            $"expected Planar2D classification, got {panel.Mode}");

        var segs = BoundarySegmenter.Segment(panel, new SegmenterOptions());
        Assert(segs.Count > 0, "expected at least one segment from planar input");
        foreach (var s in segs)
        {
            Assert(s.TorsionSignature == null,
                "2D segmenter must leave torsion signature null (it is the routing marker)");
            Assert(s.PanelPlanarityRms < 0.5,
                $"planar segment should carry near-zero panel RMS, got {s.PanelPlanarityRms}");
        }
    }

    public static void SpatialPanel_Segmenter3D_PopulatesTorsion()
    {
        var panel = MakeWarpedPanel("warped");
        Assert(panel.Mode == PanelMode.Spatial3D,
            $"expected Spatial3D classification, got {panel.Mode} (rms={panel.PlanarityRms})");

        var segs = BoundarySegmenter3D.Segment(panel, new SegmenterOptions3D());
        Assert(segs.Count > 0, "expected at least one segment from warped input");
        foreach (var s in segs)
        {
            Assert(s.TorsionSignature != null,
                "3D segmenter must populate torsion signature");
            Assert(s.PanelPlanarityRms > 0.5,
                $"3D segment should carry the parent panel's RMS > tol, got {s.PanelPlanarityRms}");
        }
    }

    public static void MixedAssembly_IndexBuckets_SplitByMode()
    {
        // Pre-populate the index the same way the GH component would:
        // pick segmenter per Panel.Mode. Confirm the 2D and 3D bucket
        // counts both populate (i.e. dispatch happened at index-build).
        var frame = MakeFramePanel("frame");
        var planar = MakePlanarPanel("planar");
        var warped = MakeWarpedPanel("warped");

        var index = new SegmentHashIndex();
        AddByMode(frame, index);
        AddByMode(planar, index);
        AddByMode(warped, index);

        Assert(index.Count2D > 0,
            $"expected non-zero Count2D for mixed assembly, got {index.Count2D}");
        Assert(index.Count3D > 0,
            $"expected non-zero Count3D for mixed assembly, got {index.Count3D}");
    }

    public static void AssemblySolver_MixedPanels_RunsWithoutCrash()
    {
        // Mixed-mode panels exercise both ICP branches inside the solver.
        // We do not assert successful placements (synthetic shards do not
        // necessarily match the frame); we assert the solver completes
        // and the result is well-formed.
        var frame = MakeFramePanel("frame");
        var planar = MakePlanarPanel("planar");
        var warped = MakeWarpedPanel("warped");

        var index = new SegmentHashIndex();
        AddByMode(frame, index);
        AddByMode(planar, index);
        AddByMode(warped, index);

        var solver = new AssemblySolver(index, new AssemblyOptions { MaxIterations = 5 });
        var state = solver.Solve(new[] { frame }, new[] { planar, warped });

        Assert(state.PlacedPanels.Count >= 1,
            $"expected at least the frame in the placed set, got {state.PlacedPanels.Count}");
        Assert(state.PlacedPanels[0].Id == "frame",
            $"expected frame anchored first, got {state.PlacedPanels[0].Id}");
    }

    private static void AddByMode(Panel p, SegmentHashIndex index)
    {
        var segs = p.Mode == PanelMode.Spatial3D
            ? BoundarySegmenter3D.Segment(p, new SegmenterOptions3D())
            : BoundarySegmenter.Segment(p, new SegmenterOptions());
        foreach (var s in segs) index.Add(s);
    }

    private static Panel MakeFramePanel(string id)
    {
        var poly = new Polyline
        {
            new Point3d(0, 0, 0),
            new Point3d(400, 0, 0),
            new Point3d(400, 300, 0),
            new Point3d(0, 300, 0),
            new Point3d(0, 0, 0),
        };
        return new Panel(id, poly.ToPolylineCurve(), PanelKind.Frame);
    }

    private static Panel MakePlanarPanel(string id)
    {
        var poly = new Polyline
        {
            new Point3d(20, 20, 0),
            new Point3d(60, 25, 0),
            new Point3d(80, 60, 0),
            new Point3d(40, 80, 0),
            new Point3d(15, 50, 0),
            new Point3d(20, 20, 0),
        };
        return new Panel(id, poly.ToPolylineCurve(), PanelKind.Shard);
    }

    private static Panel MakeWarpedPanel(string id)
    {
        // Boundary lies on a saddle: Z = 5·sin(2θ). RMS > 0.5 mm.
        var poly = new Polyline();
        const int n = 24;
        for (int i = 0; i <= n; i++)
        {
            double t = 2 * Math.PI * i / n;
            poly.Add(new Point3d(
                Math.Cos(t) * 30 + 150,
                Math.Sin(t) * 30 + 150,
                Math.Sin(2 * t) * 5));
        }
        return new Panel(id, poly.ToPolylineCurve(), PanelKind.Shard);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
