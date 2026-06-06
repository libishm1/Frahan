#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry;
using Rhino.Geometry;

namespace Frahan.Tests;

// =============================================================================
// RubbleWallSettleTests — unit tests for the rubble-wall settle Core port
// (Frahan.Masonry.RubbleWallSettle), the C# translation of the user-signed-off
// Python prototype outputs/2026-05-25/eth1100_pack/eth1100_rubble_settle.py +
// stability.py.
//
// These build RhinoCommon meshes, so they require rhcommon_c.dll (Rhino
// runtime). When Rhino is absent the harness reports SKIP (same path as every
// other Rhino-runtime test); when present they execute and assert:
//   * all stones placed (one transform per input mesh, order preserved),
//   * no two placed stones interpenetrate per (x,y)-cell (the prototype's
//     pair_penetration check, recomputed independently here),
//   * stability flags + clearances are computed and self-consistent,
//   * determinism (two runs are bit-identical).
// =============================================================================

static class RubbleWallSettleTests
{
    // ─── Fixtures ────────────────────────────────────────────────────────────

    // Deterministic axis-aligned box mesh, dimensions (sx, sy, sz), min corner at origin.
    private static Mesh Box(double sx, double sy, double sz)
    {
        var m = new Mesh();
        m.Vertices.Add(0, 0, 0);
        m.Vertices.Add(sx, 0, 0);
        m.Vertices.Add(sx, sy, 0);
        m.Vertices.Add(0, sy, 0);
        m.Vertices.Add(0, 0, sz);
        m.Vertices.Add(sx, 0, sz);
        m.Vertices.Add(sx, sy, sz);
        m.Vertices.Add(0, sy, sz);
        m.Faces.AddFace(0, 1, 2, 3);
        m.Faces.AddFace(4, 5, 6, 7);
        m.Faces.AddFace(0, 1, 5, 4);
        m.Faces.AddFace(1, 2, 6, 5);
        m.Faces.AddFace(2, 3, 7, 6);
        m.Faces.AddFace(3, 0, 4, 7);
        m.Normals.ComputeNormals();
        return m;
    }

    // A deterministic inventory of flat-ish rubble-like stones with varied sizes.
    private static List<Mesh> Inventory(int n)
    {
        var stones = new List<Mesh>(n);
        for (int i = 0; i < n; i++)
        {
            // Largest extent ~ length, mid ~ thickness, smallest ~ vertical.
            // Deterministic pseudo-variation, no RNG.
            double length = 0.30 + 0.04 * ((i * 37) % 5);   // 0.30..0.46
            double thick = 0.16 + 0.02 * ((i * 11) % 4);    // 0.16..0.22
            double tall = 0.08 + 0.01 * ((i * 7) % 3);      // 0.08..0.10
            stones.Add(Box(length, thick, tall));
        }
        return stones;
    }

    // ─── Tests ────────────────────────────────────────────────────────────────

    public static void Settle_AllStonesPlaced_OrderPreserved()
    {
        var stones = Inventory(12);
        var placements = RubbleWallSettle.Settle(stones, widthMult: 7.0, stabilityAware: true);

        Assert(placements.Count == stones.Count,
            $"expected one placement per stone ({stones.Count}), got {placements.Count}");
        for (int i = 0; i < placements.Count; i++)
        {
            Assert(placements[i] != null, $"placement {i} is null");
            Assert(placements[i].Transform.IsValid, $"placement {i} transform is invalid");
        }
    }

    public static void Settle_NoTwoPlacedStonesInterpenetrate()
    {
        var stones = Inventory(12);
        const double cellsPerWidth = 20.0;
        var placements = RubbleWallSettle.Settle(stones, widthMult: 7.0,
            stabilityAware: true, cellsPerWidth: (int)cellsPerWidth);

        // Apply each transform, build a per-(x,y)-cell [zmin,zmax] profile from
        // the placed VERTICES (same cell size the engine uses), and check the
        // max vertical overlap across shared cells — the prototype's
        // pair_penetration, recomputed here from the actual placed geometry.
        double cell = MeanX(stones) / cellsPerWidth;
        var profiles = new List<Dictionary<long, double[]>>(stones.Count);
        for (int i = 0; i < stones.Count; i++)
        {
            var placed = stones[i].DuplicateMesh();
            placed.Transform(placements[i].Transform);
            profiles.Add(CellProfile(placed, cell));
        }

        double maxPen = 0.0;
        for (int i = 0; i < profiles.Count; i++)
            for (int j = i + 1; j < profiles.Count; j++)
                maxPen = Math.Max(maxPen, PairPenetration(profiles[i], profiles[j]));

        // Per-cell contact settle: residual overlap must be ~0 (one cell tolerance
        // for floating-point binning at shared cell edges).
        Assert(maxPen <= 1.05 * cell,
            $"expected near-zero residual cell penetration, got {maxPen:0.0000} (cell {cell:0.0000})");
    }

    public static void Settle_StabilityFlagsComputed_MatchClearance()
    {
        var stones = Inventory(10);
        const double margin = 0.0;
        var placements = RubbleWallSettle.Settle(stones, widthMult: 7.0,
            stabilityAware: true, margin: margin);

        for (int i = 0; i < placements.Count; i++)
        {
            // Stable flag must equal (clearance >= margin) by construction.
            bool expected = placements[i].Clearance >= margin;
            Assert(placements[i].Stable == expected,
                $"stone {i}: Stable={placements[i].Stable} but Clearance={placements[i].Clearance} vs margin {margin}");
        }
    }

    public static void Settle_Deterministic_TwoRunsIdentical()
    {
        var a = RubbleWallSettle.Settle(Inventory(10), widthMult: 7.0, stabilityAware: true);
        var b = RubbleWallSettle.Settle(Inventory(10), widthMult: 7.0, stabilityAware: true);

        Assert(a.Count == b.Count, "run sizes differ");
        for (int i = 0; i < a.Count; i++)
        {
            Assert(a[i].Stable == b[i].Stable, $"stone {i}: Stable differs between runs");
            Assert(Math.Abs(a[i].Clearance - b[i].Clearance) < 1e-12,
                $"stone {i}: Clearance differs between runs");
            var ta = a[i].Transform;
            var tb = b[i].Transform;
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    Assert(Math.Abs(ta[r, c] - tb[r, c]) < 1e-12,
                        $"stone {i}: transform[{r},{c}] differs between runs");
        }
    }

    public static void Settle_StabilityAware_PlacesAllAndIsSafer()
    {
        // Stability-aware run should place all stones and be no less stable
        // than the densest (non-aware) run on the same inventory.
        var inv = Inventory(14);
        var dense = RubbleWallSettle.Settle(inv, widthMult: 7.0, stabilityAware: false);
        var aware = RubbleWallSettle.Settle(Inventory(14), widthMult: 7.0, stabilityAware: true);

        Assert(dense.Count == inv.Count && aware.Count == inv.Count, "not all stones placed");

        int denseStable = 0, awareStable = 0;
        for (int i = 0; i < inv.Count; i++)
        {
            if (dense[i].Stable) denseStable++;
            if (aware[i].Stable) awareStable++;
        }
        Assert(awareStable >= denseStable,
            $"stability-aware ({awareStable}) should be >= densest ({denseStable}) stable count");
    }

    public static void Settle_EmptyInput_ReturnsEmpty()
    {
        var placements = RubbleWallSettle.Settle(new List<Mesh>());
        Assert(placements.Count == 0, "empty input should return empty placements");
    }

    // ─── Independent recomputation helpers (mirror the prototype) ──────────────

    private static double MeanX(List<Mesh> stones)
    {
        // Mean of the largest principal extent ~ the longest bbox edge here
        // (boxes are axis-aligned at construction so bbox == PCA extent).
        double sum = 0; int cnt = 0;
        foreach (var m in stones)
        {
            var bb = m.GetBoundingBox(true);
            double mx = Math.Max(bb.Max.X - bb.Min.X,
                        Math.Max(bb.Max.Y - bb.Min.Y, bb.Max.Z - bb.Min.Z));
            sum += mx; cnt++;
        }
        return cnt > 0 ? sum / cnt : 1.0;
    }

    private static Dictionary<long, double[]> CellProfile(Mesh m, double cell)
    {
        var prof = new Dictionary<long, double[]>();
        int vc = m.Vertices.Count;
        for (int k = 0; k < vc; k++)
        {
            var v = m.Vertices[k];
            int ix = (int)Math.Floor(v.X / cell);
            int iy = (int)Math.Floor(v.Y / cell);
            long key = ((long)(ix + (1 << 30)) << 32) | (uint)(iy + (1 << 30));
            double z = v.Z;
            if (prof.TryGetValue(key, out double[] lohi))
            {
                if (z < lohi[0]) lohi[0] = z;
                if (z > lohi[1]) lohi[1] = z;
            }
            else prof[key] = new[] { z, z };
        }
        return prof;
    }

    // Max vertical overlap over shared (x,y) cells. Profiles are already in world Z
    // (the transform is baked into the placed mesh), so no extra offset is needed.
    private static double PairPenetration(Dictionary<long, double[]> pa, Dictionary<long, double[]> pb)
    {
        if (pa.Count > pb.Count) { var t = pa; pa = pb; pb = t; }
        double best = 0.0;
        foreach (var kv in pa)
        {
            if (!pb.TryGetValue(kv.Key, out double[] ob)) continue;
            double ov = Math.Min(kv.Value[1], ob[1]) - Math.Max(kv.Value[0], ob[0]);
            if (ov > best) best = ov;
        }
        return best;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
