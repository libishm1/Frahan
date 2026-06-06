#nullable disable
using System;
using System.Collections.Generic;
using Frahan.EdgeMatching;
using Rhino.Geometry;

namespace Frahan.Tests;

// Mostly pure-managed: builds Segments by hand from synthetic data.
// Polyline construction is managed; the test does not call into
// rhcommon_c. SKIP only happens if RhinoCommon itself fails to load.
static class EdgeMatchingSegmentHashIndexTests
{
    public static void QueryComplement_FindsMirrorSegment()
    {
        var sig = MakeRamp(128);                       // monotone-increasing
        var mirrorSig = new double[sig.Length];
        for (int i = 0; i < sig.Length; i++) mirrorSig[i] = -sig[sig.Length - 1 - i];

        var a = MakeSegment("panelA", 0, 100.0, totalTurning: +Math.PI / 2, sign: +1, sig);
        var b = MakeSegment("panelB", 0, 100.0, totalTurning: -Math.PI / 2, sign: -1, mirrorSig);

        var index = new SegmentHashIndex();
        index.Add(b);
        var hits = index.QueryComplement(a);
        Assert(hits.Count == 1, $"expected exactly one complement hit, got {hits.Count}");
        Assert(hits[0].PanelId == "panelB",
            $"expected complement to be panelB, got {hits[0].PanelId}");
    }

    public static void QueryComplement_DeterministicOrder()
    {
        var sig = MakeRamp(128);
        var mirrorSig = new double[sig.Length];
        for (int i = 0; i < sig.Length; i++) mirrorSig[i] = -sig[sig.Length - 1 - i];

        var index = new SegmentHashIndex();
        // Add segments out of lexical order to confirm the index restores ordering.
        index.Add(MakeSegment("panelZ", 0, 100.0, -Math.PI / 2, -1, mirrorSig));
        index.Add(MakeSegment("panelA", 1, 100.0, -Math.PI / 2, -1, mirrorSig));
        index.Add(MakeSegment("panelA", 0, 100.0, -Math.PI / 2, -1, mirrorSig));
        index.Add(MakeSegment("panelM", 0, 100.0, -Math.PI / 2, -1, mirrorSig));

        var query = MakeSegment("panelQ", 0, 100.0, +Math.PI / 2, +1, sig);
        var hits = index.QueryComplement(query);
        Assert(hits.Count == 4, $"expected 4 hits, got {hits.Count}");
        Assert(
            hits[0].PanelId == "panelA" && hits[0].Index == 0,
            $"expected first hit (panelA, 0), got ({hits[0].PanelId}, {hits[0].Index})");
        Assert(
            hits[1].PanelId == "panelA" && hits[1].Index == 1,
            $"expected second hit (panelA, 1), got ({hits[1].PanelId}, {hits[1].Index})");
        Assert(hits[2].PanelId == "panelM", $"expected third hit panelM, got {hits[2].PanelId}");
        Assert(hits[3].PanelId == "panelZ", $"expected fourth hit panelZ, got {hits[3].PanelId}");
    }

    public static void HashKey_RoundTrips()
    {
        var k1 = new SegmentHashKey(5, -3, 2, 4, +1);
        var k2 = new SegmentHashKey(5, -3, 2, 4, +1);
        var k3 = new SegmentHashKey(5, -3, 2, 4, -1);
        Assert(k1.Equals(k2), "equal keys must compare equal");
        Assert(k1.GetHashCode() == k2.GetHashCode(), "equal keys must hash equal");
        Assert(!k1.Equals(k3), "differing sign must not compare equal");
    }

    // ----- 3D path tests -----

    public static void QueryComplement3D_FindsMirrorSegment()
    {
        var sig = MakeRamp(128);
        var mirrorSig = new double[sig.Length];
        for (int i = 0; i < sig.Length; i++) mirrorSig[i] = -sig[sig.Length - 1 - i];

        // Torsion: A has rising positive torsion, B has the negated reverse.
        // The std-dev (which drives TorsionVarBin) is identical between the
        // two arrays, so the bins match. The sign flip is what disambiguates
        // mirror-image fracture edges in 3D.
        var torsionA = MakeTorsion(128, +0.05);
        var torsionB = MakeTorsion(128, -0.05); // flipped sign, same magnitude profile

        const double panelRms = 2.0; // both panels equally non-planar → same planarity bin

        var a = MakeSegment3D("panelA", 0, 100.0, +Math.PI / 2, +1, sig, torsionA, panelRms);
        var b = MakeSegment3D("panelB", 0, 100.0, -Math.PI / 2, -1, mirrorSig, torsionB, panelRms);

        var index = new SegmentHashIndex();
        index.Add(b);
        var hits = index.QueryComplement(a);
        Assert(hits.Count == 1, $"expected exactly one 3D complement hit, got {hits.Count}");
        Assert(hits[0].PanelId == "panelB",
            $"expected complement to be panelB, got {hits[0].PanelId}");
        Assert(hits[0].TorsionSignature != null,
            "3D-bucket hit must carry a non-null torsion signature");
    }

    public static void Query3D_IgnoresPlanar2DBuckets()
    {
        // A planar shard cannot complement a spatial-3D shard (addendum §5).
        // Add a 2D segment that would match by the 2D rules, then query
        // with its 3D twin: zero hits.
        var sig = MakeRamp(128);
        var mirrorSig = new double[sig.Length];
        for (int i = 0; i < sig.Length; i++) mirrorSig[i] = -sig[sig.Length - 1 - i];

        var planar = MakeSegment("planar", 0, 100.0, -Math.PI / 2, -1, mirrorSig);
        var spatial = MakeSegment3D("spatial", 0, 100.0, +Math.PI / 2, +1, sig,
            MakeTorsion(128, +0.05), panelPlanarityRms: 2.0);

        var index = new SegmentHashIndex();
        index.Add(planar);
        var hits = index.QueryComplement(spatial);
        Assert(hits.Count == 0,
            $"expected 3D query to ignore planar buckets, got {hits.Count} hits");
    }

    public static void Query2D_IgnoresSpatial3DBuckets()
    {
        var sig = MakeRamp(128);
        var mirrorSig = new double[sig.Length];
        for (int i = 0; i < sig.Length; i++) mirrorSig[i] = -sig[sig.Length - 1 - i];

        var spatial = MakeSegment3D("spatial", 0, 100.0, -Math.PI / 2, -1, mirrorSig,
            MakeTorsion(128, -0.05), panelPlanarityRms: 2.0);
        var planar = MakeSegment("planar", 0, 100.0, +Math.PI / 2, +1, sig);

        var index = new SegmentHashIndex();
        index.Add(spatial);
        var hits = index.QueryComplement(planar);
        Assert(hits.Count == 0,
            $"expected 2D query to ignore spatial buckets, got {hits.Count} hits");
    }

    public static void Count2D_Count3D_ReflectAdds()
    {
        var sig = MakeRamp(128);
        var torsion = MakeTorsion(128, +0.05);

        var index = new SegmentHashIndex();
        Assert(index.Count2D == 0 && index.Count3D == 0,
            $"fresh index must report zero counts, got 2D={index.Count2D} 3D={index.Count3D}");

        index.Add(MakeSegment("p1", 0, 100.0, +Math.PI / 2, +1, sig));
        index.Add(MakeSegment("p2", 0, 100.0, +Math.PI / 2, +1, sig));
        index.Add(MakeSegment3D("q1", 0, 100.0, +Math.PI / 2, +1, sig, torsion, 2.0));

        Assert(index.Count2D == 2, $"expected Count2D=2 after two 2D adds, got {index.Count2D}");
        Assert(index.Count3D == 1, $"expected Count3D=1 after one 3D add, got {index.Count3D}");
    }

    private static Segment MakeSegment(
        string panelId, int idx, double chord,
        double totalTurning, int sign, double[] turningSig)
    {
        var poly = new Polyline();
        poly.Add(new Point3d(0, 0, 0));
        poly.Add(new Point3d(chord, 0, 0));
        var curvature = new double[turningSig.Length];
        for (int i = 0; i < turningSig.Length; i++) curvature[i] = Math.Abs(turningSig[i]);
        return new Segment(panelId, idx, poly, chord, totalTurning, sign, turningSig, curvature, null);
    }

    private static Segment MakeSegment3D(
        string panelId, int idx, double chord,
        double totalTurning, int sign,
        double[] turningSig, double[] torsionSig,
        double panelPlanarityRms)
    {
        var poly = new Polyline();
        poly.Add(new Point3d(0, 0, 0));
        poly.Add(new Point3d(chord, 0, 0));
        var curvature = new double[turningSig.Length];
        for (int i = 0; i < turningSig.Length; i++) curvature[i] = Math.Abs(turningSig[i]);
        return new Segment(
            panelId, idx, poly, chord, totalTurning, sign,
            turningSig, curvature, torsionSig,
            panelPlanarityRms);
    }

    private static double[] MakeTorsion(int n, double amplitude)
    {
        // Linearly ramping torsion. The std-dev is amplitude-driven; flipping
        // sign of `amplitude` produces the mirror profile with identical std.
        var x = new double[n];
        for (int i = 0; i < n; i++) x[i] = amplitude * (-1.0 + 2.0 * i / (n - 1));
        return x;
    }

    private static double[] MakeRamp(int n)
    {
        var x = new double[n];
        for (int i = 0; i < n; i++) x[i] = -1.0 + 2.0 * i / (n - 1);
        return x;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
