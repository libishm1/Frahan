#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.DataModel;

namespace Frahan.Masonry.Interfaces;

// =============================================================================
// MeshContactDetector — Cockroach-style proximity-based contact detection
// for masonry assemblies built from real-world meshes (scan data, photo-
// grammetry, hand-authored non-planar geometry). Companion to the simpler
// InterfaceAutoDetector which only handles planar polygonal faces.
//
// Algorithm:
//   For each pair of meshes (A, B):
//     1. AABB pre-filter (with tolerance margin); skip non-overlapping pairs.
//     2. Vertex-proximity sweep:
//          - For each vertex of A, find closest point on any triangle of B.
//          - If distance < tol, record (point on A, point on B, surface
//            normal at point on B).
//          - Symmetric: same for B-vertices vs A-triangles.
//     3. Group contact points by similar normal direction (within angleTol).
//     4. For each group with at least minContactPoints points:
//          - Fit a plane via least-squares (PCA on the contact points).
//          - Project all points to the plane.
//          - 2D convex hull (Andrew monotone chain).
//          - Lift hull back to 3D as the contact polygon.
//          - Build MasonryInterface with normal = A→B direction.
//
// Robust to:
//   - Slight gaps between blocks (as long as gap < tol).
//   - Triangulated meshes (operates on vertices, not face polygons).
//   - Non-axis-aligned contact normals.
//   - Mild non-planarity in the contact region (plane fit smooths it).
//
// KB-9 (2026-06-11, RESOLVED): on EXACT coplanar face-face contacts the pure
// proximity sweep under-covers the true contact polygon (it reconstructs
// centroid/edge-midpoint pentagons biting INTO the real quad), which makes
// statically fine assemblies look infeasible to the equilibrium QP (the
// compas_cra parity arch). The coplanar-coincidence resolver computes the
// true 2D triangle-pair intersections and is therefore ON BY DEFAULT now;
// disable it only for huge scan meshes where the triangle-pair sweep is too
// expensive and contacts are never exactly coplanar anyway.
//
// Reference: IBOIS EPFL Cockroach digital-twin pipeline (point-cloud →
// per-stone mesh → buffered-intersection contact → assembly graph).
// =============================================================================

public static class MeshContactDetector
{
    private const double DefaultDistanceTol = 1e-3;
    private const double DefaultAngleTolDeg = 5.0;
    private const int DefaultMinContactPoints = 3;
    private const double EpsArea = 1e-9;

    // Broad-phase auto-switch threshold. Below this many meshes, sweep-and-
    // prune by min-X is the broad-phase index. At or above, MeshSpatialGrid
    // (uniform 3D hash) becomes the broad-phase, because S&P's O(N log N)
    // sort and the lingering O(K) inner sweep in dense walls become the
    // bottleneck once N · log N ≈ filled-cells × cell-size² stops winning.
    // 1000 is a conservative crossover empirically; the grid is correct at
    // any N, just slightly slower in the small-N regime due to dictionary
    // overhead and per-pair dedup.
    public const int GridSwitchThreshold = 1000;

    /// <summary>
    /// Detect contacts between every pair of meshes. Returns one
    /// MasonryInterface per detected contact (some pairs may produce 0
    /// or multiple if they touch on multiple separated regions).
    ///
    /// <para>
    /// When <paramref name="adaptiveToleranceFactor"/> is &gt; 0, the
    /// per-pair effective tolerance becomes
    /// <c>max(distanceTol, factor · min(medianEdge_A, medianEdge_B))</c>.
    /// This makes the detector self-scale across input units (1m blocks
    /// vs 1cm blocks) without per-mesh tuning. Suggested factor: 0.05
    /// (5% of median edge — generous enough for scan data).
    /// </para>
    /// </summary>
    public static IReadOnlyList<MasonryInterface> Detect(
        IReadOnlyList<MeshSnapshot> meshes,
        IReadOnlyList<string> blockIds,
        double distanceTol = DefaultDistanceTol,
        double angleTolDeg = DefaultAngleTolDeg,
        int minContactPoints = DefaultMinContactPoints,
        double adaptiveToleranceFactor = 0.0,
        bool useCoplanarResolver = true)
    {
        if (meshes == null) throw new ArgumentNullException(nameof(meshes));
        if (blockIds == null) throw new ArgumentNullException(nameof(blockIds));
        if (meshes.Count != blockIds.Count)
            throw new ArgumentException(
                $"meshes.Count ({meshes.Count}) must equal blockIds.Count ({blockIds.Count})",
                nameof(blockIds));
        if (distanceTol < 0.0)
            throw new ArgumentOutOfRangeException(nameof(distanceTol), "must be >= 0");
        if (!(angleTolDeg >= 0.0 && angleTolDeg < 90.0))
            throw new ArgumentOutOfRangeException(nameof(angleTolDeg), "must be in [0, 90)");
        if (minContactPoints < 3)
            throw new ArgumentOutOfRangeException(nameof(minContactPoints), "must be >= 3");
        if (adaptiveToleranceFactor < 0.0)
            throw new ArgumentOutOfRangeException(
                nameof(adaptiveToleranceFactor), "must be >= 0");

        for (int i = 0; i < blockIds.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(blockIds[i]))
                throw new ArgumentException($"blockIds[{i}] is blank", nameof(blockIds));
        }

        int n = meshes.Count;
        var result = new List<MasonryInterface>(n * 2);

        // Pre-compute median edge length per mesh once, when adaptive
        // tolerance is enabled. ~O(T log T) per mesh; cached in an array
        // so the per-pair lookup is O(1).
        double[] medianEdge = null;
        if (adaptiveToleranceFactor > 0.0)
        {
            medianEdge = new double[n];
            for (int i = 0; i < n; i++)
                medianEdge[i] = ComputeMedianEdgeLength(meshes[i]);
        }

        // ── Broad phase: pick S&P (small N) or spatial grid (large N) ───────
        // Auto-switch at GridSwitchThreshold. The grid is asymptotically
        // better for very large dense assemblies because pair discovery
        // becomes O(N + filled-cells · bucket-size²) ≈ O(N) once cell size
        // matches average block extent, while S&P retains its inner sweep.
        if (n >= GridSwitchThreshold)
        {
            DetectViaGrid(meshes, blockIds,
                distanceTol, angleTolDeg, minContactPoints,
                medianEdge, adaptiveToleranceFactor, useCoplanarResolver, result);
        }
        else
        {
            DetectViaSweepAndPrune(meshes, blockIds,
                distanceTol, angleTolDeg, minContactPoints,
                medianEdge, adaptiveToleranceFactor, useCoplanarResolver, result);
        }
        return result;
    }

    // ─── Adaptive-tolerance helpers ──────────────────────────────────────

    private static double ComputeMedianEdgeLength(MeshSnapshot m)
    {
        if (m == null || m.TriangleCount == 0) return 0.0;
        var v = m.VertexCoordsXyz;
        var t = m.TriangleIndices;
        int tc = m.TriangleCount;
        // 3 edges per triangle, deduped by canonical (min, max).
        var seen = new HashSet<long>();
        var lengths = new List<double>(tc * 3);
        for (int i = 0; i < tc; i++)
        {
            int a = t[3 * i + 0], b = t[3 * i + 1], c = t[3 * i + 2];
            AddEdge(v, seen, lengths, a, b);
            AddEdge(v, seen, lengths, b, c);
            AddEdge(v, seen, lengths, c, a);
        }
        if (lengths.Count == 0) return 0.0;
        lengths.Sort();
        return lengths[lengths.Count / 2];
    }

    private static void AddEdge(
        IReadOnlyList<double> v, HashSet<long> seen, List<double> lengths,
        int a, int b)
    {
        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        if (!seen.Add(key)) return;
        double dx = v[3 * a + 0] - v[3 * b + 0];
        double dy = v[3 * a + 1] - v[3 * b + 1];
        double dz = v[3 * a + 2] - v[3 * b + 2];
        lengths.Add(Math.Sqrt(dx * dx + dy * dy + dz * dz));
    }

    private static double EffectiveTolerance(
        double baseTol, double[] medianEdge, double factor, int i, int j)
    {
        if (medianEdge == null || factor <= 0.0) return baseTol;
        double mi = medianEdge[i], mj = medianEdge[j];
        if (mi <= 0.0 || mj <= 0.0) return baseTol;
        double scale = factor * Math.Min(mi, mj);
        return Math.Max(baseTol, scale);
    }

    // ─── Broad-phase: sweep-and-prune (small N) ─────────────────────────────

    private static void DetectViaSweepAndPrune(
        IReadOnlyList<MeshSnapshot> meshes,
        IReadOnlyList<string> blockIds,
        double distanceTol, double angleTolDeg, int minContactPoints,
        double[] medianEdge, double adaptiveFactor,
        bool useCoplanarResolver,
        List<MasonryInterface> result)
    {
        int n = meshes.Count;
        // Sweep-and-prune by min-X. O(N log N) sort + O(K) sweep, where K
        // is the count of X-overlapping pairs. For typical brick walls, K
        // is O(N) — most pairs are filtered out by the X check alone.
        int[] order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        Array.Sort(order, (a, b) => meshes[a].BBoxMinX.CompareTo(meshes[b].BBoxMinX));

        for (int oi = 0; oi < n; oi++)
        {
            int i = order[oi];
            double iMaxX = meshes[i].BBoxMaxX;
            // Precompute the largest possible per-pair tolerance for the
            // sweep early-exit: if any j could be paired with i, the pair
            // tol is bounded by max(distanceTol, factor · medianEdge[i]).
            double iMaxPairTol = medianEdge != null && adaptiveFactor > 0.0
                ? Math.Max(distanceTol, adaptiveFactor * medianEdge[i])
                : distanceTol;
            for (int oj = oi + 1; oj < n; oj++)
            {
                int j = order[oj];
                if (meshes[j].BBoxMinX - iMaxPairTol > iMaxX) break;
                double pairTol = EffectiveTolerance(distanceTol, medianEdge, adaptiveFactor, i, j);
                if (!meshes[i].BBoxOverlaps(meshes[j], pairTol)) continue;
                DetectPairAdding(meshes[i], meshes[j], blockIds[i], blockIds[j],
                    pairTol, angleTolDeg, minContactPoints,
                    useCoplanarResolver, result);
            }
        }
    }

    // ─── Broad-phase: uniform spatial grid (large N) ────────────────────────

    private static void DetectViaGrid(
        IReadOnlyList<MeshSnapshot> meshes,
        IReadOnlyList<string> blockIds,
        double distanceTol, double angleTolDeg, int minContactPoints,
        double[] medianEdge, double adaptiveFactor,
        bool useCoplanarResolver,
        List<MasonryInterface> result)
    {
        int n = meshes.Count;
        // Cell size = average AABB extent across all axes. This makes a
        // typical mesh occupy O(1) cells; pair-discovery cost per filled
        // cell stays bounded.
        double sumExtent = 0.0;
        int extentSamples = 0;
        for (int i = 0; i < n; i++)
        {
            var m = meshes[i];
            double ex = m.BBoxMaxX - m.BBoxMinX;
            double ey = m.BBoxMaxY - m.BBoxMinY;
            double ez = m.BBoxMaxZ - m.BBoxMinZ;
            if (ex > 0) { sumExtent += ex; extentSamples++; }
            if (ey > 0) { sumExtent += ey; extentSamples++; }
            if (ez > 0) { sumExtent += ez; extentSamples++; }
        }
        double cellSize = extentSamples > 0
            ? sumExtent / extentSamples
            : 1.0;
        // Guard against degenerate (everything coincident) input.
        if (!(cellSize > 0.0)) cellSize = 1.0;

        var grid = new MeshSpatialGrid(meshes, cellSize);
        foreach (var pair in grid.CandidatePairs())
        {
            int i = pair.I, j = pair.J;
            double pairTol = EffectiveTolerance(distanceTol, medianEdge, adaptiveFactor, i, j);
            // Confirm full 3D AABB overlap with tolerance margin (a shared
            // cell does not guarantee AABB overlap — two AABBs may both
            // touch the same cell while still being separated by < cellSize).
            if (!meshes[i].BBoxOverlaps(meshes[j], pairTol)) continue;
            DetectPairAdding(meshes[i], meshes[j], blockIds[i], blockIds[j],
                pairTol, angleTolDeg, minContactPoints,
                useCoplanarResolver, result);
        }
    }

    // ─── Per-pair detection ─────────────────────────────────────────────────

    private static void DetectPairAdding(
        MeshSnapshot a, MeshSnapshot b, string idA, string idB,
        double distanceTol, double angleTolDeg, int minContactPoints,
        bool useCoplanarResolver,
        List<MasonryInterface> sink)
    {
        if (a == null || b == null || sink == null) throw new ArgumentNullException();

        // Build BVHs once per pair; queries below are O(log T) instead of O(T).
        var bvhA = new MeshBvh(a.VertexCoordsXyz, a.TriangleIndices);
        var bvhB = new MeshBvh(b.VertexCoordsXyz, b.TriangleIndices);

        var contacts = new List<ContactPoint>(64);
        // Vertex sweeps catch edge-to-face and corner-to-face contacts.
        SweepVerticesAgainstTriangles(a, b, bvhB, distanceTol, contacts, isAtoB: true);
        SweepVerticesAgainstTriangles(b, a, bvhA, distanceTol, contacts, isAtoB: false);
        // Triangle-centroid sweeps catch face-to-face contacts even when
        // meshes share corners (where vertex sweeps give ambiguous normals
        // because multiple equidistant target triangles exist).
        SweepCentroidsAgainstTriangles(a, b, bvhB, distanceTol, contacts, isAtoB: true);
        SweepCentroidsAgainstTriangles(b, a, bvhA, distanceTol, contacts, isAtoB: false);
        // Edge-midpoint sweeps catch partial-overlap contacts where neither
        // vertices nor centroids of either mesh project onto the other's
        // surface (the contact is along a shared edge).
        SweepEdgeMidpointsAgainstTriangles(a, b, bvhB, distanceTol, contacts, isAtoB: true);
        SweepEdgeMidpointsAgainstTriangles(b, a, bvhA, distanceTol, contacts, isAtoB: false);
        // Coplanar-coincidence resolver: directly compute 2D triangle-pair
        // intersections for anti-parallel coplanar pairs and sample their
        // interior. Catches the pathology where two faces are perfectly
        // coincident — vertex / centroid / edge-midpoint sweeps can give
        // ambiguous normals because the closest point on the target lands
        // on a triangle boundary.
        if (useCoplanarResolver)
            SweepCoplanarTrianglePairs(a, b, distanceTol, angleTolDeg, contacts);
        if (contacts.Count < minContactPoints) return;

        var groups = GroupByNormal(contacts, angleTolDeg);
        for (int g = 0; g < groups.Count; g++)
        {
            var grp = groups[g];
            if (grp.Count < minContactPoints) continue;
            EmitInterface(grp, idA, idB, sink);
        }
    }

    private struct ContactPoint
    {
        public double Pax, Pay, Paz;       // on A's surface
        public double Pbx, Pby, Pbz;       // on B's surface
        public double Nx, Ny, Nz;          // surface normal at contact, **A→B** direction
        public double Distance;
    }

    private static void SweepVerticesAgainstTriangles(
        MeshSnapshot src, MeshSnapshot tgt, MeshBvh tgtBvh, double tol,
        List<ContactPoint> sink, bool isAtoB)
    {
        if (src == null || tgt == null || tgtBvh == null) throw new ArgumentNullException();

        int srcVerts = src.VertexCount;
        var sv = src.VertexCoordsXyz;
        var tv = tgt.VertexCoordsXyz;
        var ti = tgt.TriangleIndices;

        for (int v = 0; v < srcVerts; v++)
        {
            double px = sv[3 * v + 0], py = sv[3 * v + 1], pz = sv[3 * v + 2];
            if (!tgtBvh.ClosestPoint(px, py, pz, tol,
                out double bx, out double by, out double bz,
                out int triIdx, out double dist)) continue;

            int ia = ti[3 * triIdx + 0], ib = ti[3 * triIdx + 1], ic = ti[3 * triIdx + 2];
            TriangleNormal(
                tv[3 * ia + 0], tv[3 * ia + 1], tv[3 * ia + 2],
                tv[3 * ib + 0], tv[3 * ib + 1], tv[3 * ib + 2],
                tv[3 * ic + 0], tv[3 * ic + 1], tv[3 * ic + 2],
                out double bnx, out double bny, out double bnz);

            // Establish the A→B normal direction.
            // - SweepVerticesAgainstTriangles(A, B) gives (a-vertex, b-projection, b-normal);
            //   the b-normal points outward from B at the contact, which is the direction
            //   pointing FROM B TOWARD A (i.e. B→A, the convention we need to NEGATE).
            // - SweepVerticesAgainstTriangles(B, A) gives (b-vertex, a-projection, a-normal);
            //   the a-normal points outward from A, i.e. A→B (already correct).
            double nax, nay, naz, pax, pay, paz, pbx, pby, pbz;
            if (isAtoB)
            {
                // src=A, tgt=B. Normal at the B point is B's outward → points TOWARD A. Flip.
                nax = -bnx; nay = -bny; naz = -bnz;
                pax = px; pay = py; paz = pz;
                pbx = bx; pby = by; pbz = bz;
            }
            else
            {
                // src=B, tgt=A. Normal at the A point is A's outward → points TOWARD B. Already A→B.
                nax = bnx; nay = bny; naz = bnz;
                pax = bx; pay = by; paz = bz;
                pbx = px; pby = py; pbz = pz;
            }
            sink.Add(new ContactPoint
            {
                Pax = pax, Pay = pay, Paz = paz,
                Pbx = pbx, Pby = pby, Pbz = pbz,
                Nx = nax, Ny = nay, Nz = naz,
                Distance = dist,
            });
        }
    }

    private static void SweepCentroidsAgainstTriangles(
        MeshSnapshot src, MeshSnapshot tgt, MeshBvh tgtBvh, double tol,
        List<ContactPoint> sink, bool isAtoB)
    {
        if (src == null || tgt == null || tgtBvh == null) throw new ArgumentNullException();

        int srcTris = src.TriangleCount;
        var sv = src.VertexCoordsXyz;
        var sti = src.TriangleIndices;
        var tv = tgt.VertexCoordsXyz;
        var ti = tgt.TriangleIndices;

        for (int s = 0; s < srcTris; s++)
        {
            int sa = sti[3 * s + 0], sb = sti[3 * s + 1], sc = sti[3 * s + 2];
            double sax = sv[3 * sa + 0], say = sv[3 * sa + 1], saz = sv[3 * sa + 2];
            double sbx = sv[3 * sb + 0], sby = sv[3 * sb + 1], sbz = sv[3 * sb + 2];
            double scx = sv[3 * sc + 0], scy = sv[3 * sc + 1], scz = sv[3 * sc + 2];
            double px = (sax + sbx + scx) / 3.0;
            double py = (say + sby + scy) / 3.0;
            double pz = (saz + sbz + scz) / 3.0;

            if (!tgtBvh.ClosestPoint(px, py, pz, tol,
                out double bx, out double by, out double bz,
                out int triIdx, out double dist)) continue;

            int ia = ti[3 * triIdx + 0], ib = ti[3 * triIdx + 1], ic = ti[3 * triIdx + 2];
            TriangleNormal(
                tv[3 * ia + 0], tv[3 * ia + 1], tv[3 * ia + 2],
                tv[3 * ib + 0], tv[3 * ib + 1], tv[3 * ib + 2],
                tv[3 * ic + 0], tv[3 * ic + 1], tv[3 * ic + 2],
                out double bnx, out double bny, out double bnz);

            double nax, nay, naz, pax, pay, paz, pbx, pby, pbz;
            if (isAtoB)
            {
                nax = -bnx; nay = -bny; naz = -bnz;
                pax = px; pay = py; paz = pz;
                pbx = bx; pby = by; pbz = bz;
            }
            else
            {
                nax = bnx; nay = bny; naz = bnz;
                pax = bx; pay = by; paz = bz;
                pbx = px; pby = py; pbz = pz;
            }
            sink.Add(new ContactPoint
            {
                Pax = pax, Pay = pay, Paz = paz,
                Pbx = pbx, Pby = pby, Pbz = pbz,
                Nx = nax, Ny = nay, Nz = naz,
                Distance = dist,
            });
        }
    }

    private static void SweepEdgeMidpointsAgainstTriangles(
        MeshSnapshot src, MeshSnapshot tgt, MeshBvh tgtBvh, double tol,
        List<ContactPoint> sink, bool isAtoB)
    {
        if (src == null || tgt == null || tgtBvh == null) throw new ArgumentNullException();

        int srcTris = src.TriangleCount;
        var sv = src.VertexCoordsXyz;
        var sti = src.TriangleIndices;
        var tv = tgt.VertexCoordsXyz;
        var ti = tgt.TriangleIndices;

        for (int s = 0; s < srcTris; s++)
        {
            int sa = sti[3 * s + 0], sb = sti[3 * s + 1], sc = sti[3 * s + 2];
            ProcessEdgeMidpoint(sv, sa, sb, tv, ti, tgtBvh, tol, sink, isAtoB);
            ProcessEdgeMidpoint(sv, sb, sc, tv, ti, tgtBvh, tol, sink, isAtoB);
            ProcessEdgeMidpoint(sv, sc, sa, tv, ti, tgtBvh, tol, sink, isAtoB);
        }
    }

    // ─── Coplanar-coincidence resolver ──────────────────────────────────
    //
    // For each anti-parallel coplanar triangle pair, project both into A's
    // plane, compute the 2D intersection (S-H clip on convex triangles),
    // and emit a contact point at the intersection's centroid. This
    // catches the pathology where two block faces are exactly coincident:
    // the per-vertex / centroid / edge-midpoint sweeps either miss the
    // contact or assign ambiguous normals because the closest target
    // point lands on a boundary.
    //
    // Cost: O(T_a · T_b) per pair. Opt-in via the flag on Detect.

    private static void SweepCoplanarTrianglePairs(
        MeshSnapshot a, MeshSnapshot b,
        double distanceTol, double angleTolDeg,
        List<ContactPoint> sink)
    {
        if (a == null || b == null || sink == null) throw new ArgumentNullException();

        double cosAnti = -Math.Cos(angleTolDeg * Math.PI / 180.0);
        var av = a.VertexCoordsXyz;
        var ai = a.TriangleIndices;
        var bv = b.VertexCoordsXyz;
        var bi = b.TriangleIndices;

        for (int ta = 0; ta < a.TriangleCount; ta++)
        {
            int a0 = ai[3 * ta + 0], a1 = ai[3 * ta + 1], a2 = ai[3 * ta + 2];
            double a0x = av[3 * a0 + 0], a0y = av[3 * a0 + 1], a0z = av[3 * a0 + 2];
            double a1x = av[3 * a1 + 0], a1y = av[3 * a1 + 1], a1z = av[3 * a1 + 2];
            double a2x = av[3 * a2 + 0], a2y = av[3 * a2 + 1], a2z = av[3 * a2 + 2];

            TriangleNormal(a0x, a0y, a0z, a1x, a1y, a1z, a2x, a2y, a2z,
                out double anx, out double any, out double anz);
            if (anx == 0 && any == 0 && anz == 0) continue;

            // a-plane equation: normal · p = w.
            double aw = anx * a0x + any * a0y + anz * a0z;

            // Build orthonormal in-plane basis (u, v) for projection.
            BuildPlanarBasis(anx, any, anz,
                out double aux, out double auy, out double auz,
                out double avx, out double avy, out double avz);

            double a0u = (a0x - a0x) * aux + (a0y - a0y) * auy + (a0z - a0z) * auz;
            double a0v = (a0x - a0x) * avx + (a0y - a0y) * avy + (a0z - a0z) * avz;
            double a1u = (a1x - a0x) * aux + (a1y - a0y) * auy + (a1z - a0z) * auz;
            double a1v = (a1x - a0x) * avx + (a1y - a0y) * avy + (a1z - a0z) * avz;
            double a2u = (a2x - a0x) * aux + (a2y - a0y) * auy + (a2z - a0z) * auz;
            double a2v = (a2x - a0x) * avx + (a2y - a0y) * avy + (a2z - a0z) * avz;

            // Ensure A is CCW in (u, v).
            double aSigned = (a1u - a0u) * (a2v - a0v) - (a1v - a0v) * (a2u - a0u);
            var aPoly = aSigned > 0
                ? new List<(double X, double Y)> { (a0u, a0v), (a1u, a1v), (a2u, a2v) }
                : new List<(double X, double Y)> { (a0u, a0v), (a2u, a2v), (a1u, a1v) };

            for (int tb = 0; tb < b.TriangleCount; tb++)
            {
                int b0 = bi[3 * tb + 0], b1 = bi[3 * tb + 1], b2 = bi[3 * tb + 2];
                double b0x = bv[3 * b0 + 0], b0y = bv[3 * b0 + 1], b0z = bv[3 * b0 + 2];
                double b1x = bv[3 * b1 + 0], b1y = bv[3 * b1 + 1], b1z = bv[3 * b1 + 2];
                double b2x = bv[3 * b2 + 0], b2y = bv[3 * b2 + 1], b2z = bv[3 * b2 + 2];

                TriangleNormal(b0x, b0y, b0z, b1x, b1y, b1z, b2x, b2y, b2z,
                    out double bnx, out double bny, out double bnz);
                if (bnx == 0 && bny == 0 && bnz == 0) continue;

                double cosAB = anx * bnx + any * bny + anz * bnz;
                // Anti-parallel test (allow either sign — some inputs may
                // have outward-vs-inward normal conventions).
                if (cosAB > cosAnti && cosAB < -cosAnti) continue;

                // Coplanarity: each B vertex's signed distance from A's
                // plane must be within tol.
                double d0 = anx * b0x + any * b0y + anz * b0z - aw;
                double d1 = anx * b1x + any * b1y + anz * b1z - aw;
                double d2 = anx * b2x + any * b2y + anz * b2z - aw;
                if (Math.Abs(d0) > distanceTol) continue;
                if (Math.Abs(d1) > distanceTol) continue;
                if (Math.Abs(d2) > distanceTol) continue;

                double b0u = (b0x - a0x) * aux + (b0y - a0y) * auy + (b0z - a0z) * auz;
                double b0v = (b0x - a0x) * avx + (b0y - a0y) * avy + (b0z - a0z) * avz;
                double b1u = (b1x - a0x) * aux + (b1y - a0y) * auy + (b1z - a0z) * auz;
                double b1v = (b1x - a0x) * avx + (b1y - a0y) * avy + (b1z - a0z) * avz;
                double b2u = (b2x - a0x) * aux + (b2y - a0y) * auy + (b2z - a0z) * auz;
                double b2v = (b2x - a0x) * avx + (b2y - a0y) * avy + (b2z - a0z) * avz;

                double bSigned = (b1u - b0u) * (b2v - b0v) - (b1v - b0v) * (b2u - b0u);
                var bPoly = bSigned > 0
                    ? new List<(double X, double Y)> { (b0u, b0v), (b1u, b1v), (b2u, b2v) }
                    : new List<(double X, double Y)> { (b0u, b0v), (b2u, b2v), (b1u, b1v) };

                // Both A and B are convex triangles; S-H gives the exact
                // 2D intersection.
                var clip = Geometry.RobustPolygon2D.SutherlandHodgmanClip(bPoly, aPoly);
                if (clip.Count < 3) continue;
                double area = Geometry.RobustPolygon2D.Area(clip);
                if (area < EpsArea) continue;

                // Sample the intersection's centroid as the contact point.
                var (cu, cv) = Geometry.RobustPolygon2D.Centroid(clip);
                double cx = a0x + cu * aux + cv * avx;
                double cy = a0y + cu * auy + cv * avy;
                double cz = a0z + cu * auz + cv * avz;

                // Normal: A→B is the convention. Anti-parallel pair means
                // B's outward normal points TOWARD A; flip for A→B.
                double nx = -bnx, ny = -bny, nz = -bnz;
                if (cosAB > 0)
                {
                    // Same-direction normals (rare but legal under loose
                    // angleTol); use A's normal directly.
                    nx = anx; ny = any; nz = anz;
                }
                sink.Add(new ContactPoint
                {
                    Pax = cx, Pay = cy, Paz = cz,
                    Pbx = cx, Pby = cy, Pbz = cz,
                    Nx = nx, Ny = ny, Nz = nz,
                    Distance = 0.0,
                });
            }
        }
    }

    private static void ProcessEdgeMidpoint(
        IReadOnlyList<double> sv, int sa, int sb,
        IReadOnlyList<double> tv, IReadOnlyList<int> ti, MeshBvh tgtBvh,
        double tol, List<ContactPoint> sink, bool isAtoB)
    {
        double px = 0.5 * (sv[3 * sa + 0] + sv[3 * sb + 0]);
        double py = 0.5 * (sv[3 * sa + 1] + sv[3 * sb + 1]);
        double pz = 0.5 * (sv[3 * sa + 2] + sv[3 * sb + 2]);

        if (!tgtBvh.ClosestPoint(px, py, pz, tol,
            out double bx, out double by, out double bz,
            out int triIdx, out double dist)) return;

        int ia = ti[3 * triIdx + 0], ib = ti[3 * triIdx + 1], ic = ti[3 * triIdx + 2];
        TriangleNormal(
            tv[3 * ia + 0], tv[3 * ia + 1], tv[3 * ia + 2],
            tv[3 * ib + 0], tv[3 * ib + 1], tv[3 * ib + 2],
            tv[3 * ic + 0], tv[3 * ic + 1], tv[3 * ic + 2],
            out double bnx, out double bny, out double bnz);

        double nax, nay, naz, pax, pay, paz, pbx, pby, pbz;
        if (isAtoB)
        {
            nax = -bnx; nay = -bny; naz = -bnz;
            pax = px; pay = py; paz = pz;
            pbx = bx; pby = by; pbz = bz;
        }
        else
        {
            nax = bnx; nay = bny; naz = bnz;
            pax = bx; pay = by; paz = bz;
            pbx = px; pby = py; pbz = pz;
        }
        sink.Add(new ContactPoint
        {
            Pax = pax, Pay = pay, Paz = paz,
            Pbx = pbx, Pby = pby, Pbz = pbz,
            Nx = nax, Ny = nay, Nz = naz,
            Distance = dist,
        });
    }

    // ─── Group by normal ────────────────────────────────────────────────────

    private static List<List<ContactPoint>> GroupByNormal(
        List<ContactPoint> contacts, double angleTolDeg)
    {
        if (contacts == null) throw new ArgumentNullException(nameof(contacts));
        double cosTol = Math.Cos(angleTolDeg * Math.PI / 180.0);

        var groups = new List<List<ContactPoint>>(4);
        for (int i = 0; i < contacts.Count; i++)
        {
            var c = contacts[i];
            int found = -1;
            for (int g = 0; g < groups.Count; g++)
            {
                var first = groups[g][0];
                double dot = c.Nx * first.Nx + c.Ny * first.Ny + c.Nz * first.Nz;
                if (dot >= cosTol) { found = g; break; }
            }
            if (found < 0)
            {
                var nl = new List<ContactPoint>(8);
                nl.Add(c);
                groups.Add(nl);
            }
            else
            {
                groups[found].Add(c);
            }
        }
        return groups;
    }

    // ─── Emit one MasonryInterface for a contact-point group ───────────────

    private static void EmitInterface(
        List<ContactPoint> grp, string idA, string idB,
        List<MasonryInterface> sink)
    {
        if (grp == null) throw new ArgumentNullException(nameof(grp));
        if (sink == null) throw new ArgumentNullException(nameof(sink));

        // Mean normal (A→B).
        double nx = 0, ny = 0, nz = 0;
        for (int i = 0; i < grp.Count; i++)
        {
            nx += grp[i].Nx; ny += grp[i].Ny; nz += grp[i].Nz;
        }
        double nm = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (nm < 1e-12) return;
        nx /= nm; ny /= nm; nz /= nm;

        // Centroid of the contact points (use the midpoints of (A, B) pairs).
        double cx = 0, cy = 0, cz = 0;
        for (int i = 0; i < grp.Count; i++)
        {
            double mx = 0.5 * (grp[i].Pax + grp[i].Pbx);
            double my = 0.5 * (grp[i].Pay + grp[i].Pby);
            double mz = 0.5 * (grp[i].Paz + grp[i].Pbz);
            cx += mx; cy += my; cz += mz;
        }
        cx /= grp.Count; cy /= grp.Count; cz /= grp.Count;

        // Build orthonormal in-plane basis (u, v).
        BuildPlanarBasis(nx, ny, nz, out double ux, out double uy, out double uz,
                                       out double vx, out double vy, out double vz);

        // Project each contact midpoint to 2D (u, v) in-plane coords.
        var pts2d = new List<(double U, double V)>(grp.Count);
        for (int i = 0; i < grp.Count; i++)
        {
            double mx = 0.5 * (grp[i].Pax + grp[i].Pbx) - cx;
            double my = 0.5 * (grp[i].Pay + grp[i].Pby) - cy;
            double mz = 0.5 * (grp[i].Paz + grp[i].Pbz) - cz;
            double u = mx * ux + my * uy + mz * uz;
            double v = mx * vx + my * vy + mz * vz;
            pts2d.Add((u, v));
        }

        var hull = ConvexHull2D(pts2d);
        if (hull.Count < 3) return;
        hull = WeldHull(hull);
        if (hull.Count < 3) return;
        if (PolygonArea2D(hull) < EpsArea) return;

        // Lift hull back to 3D.
        var poly = new ContactVertex[hull.Count];
        for (int i = 0; i < hull.Count; i++)
        {
            double x = cx + hull[i].U * ux + hull[i].V * vx;
            double y = cy + hull[i].U * uy + hull[i].V * vy;
            double z = cz + hull[i].U * uz + hull[i].V * vz;
            poly[i] = new ContactVertex(x, y, z);
        }

        sink.Add(new MasonryInterface(idA, idB, poly,
            nx, ny, nz, ux, uy, uz, vx, vy, vz));
    }

    // ─── Geometry helpers ───────────────────────────────────────────────────

    private static void TriangleNormal(
        double ax, double ay, double az,
        double bx, double by, double bz,
        double cx, double cy, double cz,
        out double nx, out double ny, out double nz)
    {
        double ex = bx - ax, ey = by - ay, ez = bz - az;
        double fx = cx - ax, fy = cy - ay, fz = cz - az;
        nx = ey * fz - ez * fy;
        ny = ez * fx - ex * fz;
        nz = ex * fy - ey * fx;
        double m = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (m < 1e-20) { nx = 0; ny = 0; nz = 0; return; }
        nx /= m; ny /= m; nz /= m;
    }

    /// <summary>
    /// Closest point on triangle (a, b, c) to point p. Reference:
    /// Ericson, "Real-Time Collision Detection" §5.1.5.
    /// </summary>
    private static void ClosestPointOnTriangle(
        double px, double py, double pz,
        double ax, double ay, double az,
        double bx, double by, double bz,
        double cx, double cy, double cz,
        out double qx, out double qy, out double qz)
    {
        double abx = bx - ax, aby = by - ay, abz = bz - az;
        double acx = cx - ax, acy = cy - ay, acz = cz - az;
        double apx = px - ax, apy = py - ay, apz = pz - az;
        double d1 = abx * apx + aby * apy + abz * apz;
        double d2 = acx * apx + acy * apy + acz * apz;
        if (d1 <= 0 && d2 <= 0) { qx = ax; qy = ay; qz = az; return; }

        double bpx = px - bx, bpy = py - by, bpz = pz - bz;
        double d3 = abx * bpx + aby * bpy + abz * bpz;
        double d4 = acx * bpx + acy * bpy + acz * bpz;
        if (d3 >= 0 && d4 <= d3) { qx = bx; qy = by; qz = bz; return; }

        double vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        {
            double t = d1 / (d1 - d3);
            qx = ax + t * abx; qy = ay + t * aby; qz = az + t * abz;
            return;
        }

        double cpx = px - cx, cpy = py - cy, cpz = pz - cz;
        double d5 = abx * cpx + aby * cpy + abz * cpz;
        double d6 = acx * cpx + acy * cpy + acz * cpz;
        if (d6 >= 0 && d5 <= d6) { qx = cx; qy = cy; qz = cz; return; }

        double vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        {
            double t = d2 / (d2 - d6);
            qx = ax + t * acx; qy = ay + t * acy; qz = az + t * acz;
            return;
        }

        double va = d3 * d6 - d5 * d4;
        if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
        {
            double t = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            qx = bx + t * (cx - bx); qy = by + t * (cy - by); qz = bz + t * (cz - bz);
            return;
        }

        double denom = 1.0 / (va + vb + vc);
        double v_ = vb * denom;
        double w_ = vc * denom;
        qx = ax + v_ * abx + w_ * acx;
        qy = ay + v_ * aby + w_ * acy;
        qz = az + v_ * abz + w_ * acz;
    }

    private static void BuildPlanarBasis(
        double nx, double ny, double nz,
        out double ux, out double uy, out double uz,
        out double vx, out double vy, out double vz)
    {
        double sx, sy, sz;
        if (Math.Abs(nz) < 0.9) { sx = 0; sy = 0; sz = 1; }
        else                    { sx = 1; sy = 0; sz = 0; }
        ux = sy * nz - sz * ny;
        uy = sz * nx - sx * nz;
        uz = sx * ny - sy * nx;
        double um = Math.Sqrt(ux * ux + uy * uy + uz * uz);
        if (um < 1e-20)
            throw new InvalidOperationException("planar basis seed parallel to normal");
        ux /= um; uy /= um; uz /= um;
        vx = ny * uz - nz * uy;
        vy = nz * ux - nx * uz;
        vz = nx * uy - ny * ux;
    }

    /// <summary>
    /// Andrew monotone-chain 2D convex hull. O(n log n).
    /// </summary>
    private static List<(double U, double V)> ConvexHull2D(List<(double U, double V)> pts)
    {
        if (pts == null) throw new ArgumentNullException(nameof(pts));
        int n = pts.Count;
        if (n < 3) return new List<(double, double)>(pts);

        var sorted = new List<(double U, double V)>(pts);
        sorted.Sort((a, b) => a.U == b.U ? a.V.CompareTo(b.V) : a.U.CompareTo(b.U));

        var lower = new List<(double, double)>(n);
        for (int i = 0; i < n; i++)
        {
            while (lower.Count >= 2 &&
                   Cross(lower[lower.Count - 2], lower[lower.Count - 1], sorted[i]) <= 0)
                lower.RemoveAt(lower.Count - 1);
            lower.Add(sorted[i]);
        }

        var upper = new List<(double, double)>(n);
        for (int i = n - 1; i >= 0; i--)
        {
            while (upper.Count >= 2 &&
                   Cross(upper[upper.Count - 2], upper[upper.Count - 1], sorted[i]) <= 0)
                upper.RemoveAt(upper.Count - 1);
            upper.Add(sorted[i]);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static double Cross((double U, double V) o, (double U, double V) a, (double U, double V) b) =>
        (a.U - o.U) * (b.V - o.V) - (a.V - o.V) * (b.U - o.U);

    /// <summary>
    /// Hull hygiene (KB-9): the clipped triangle soup reconstructs the same
    /// physical contact corner several times, micrometres apart; the convex
    /// hull then keeps 5-6 vertices where the geometry has 4, and the
    /// near-duplicate force columns ill-condition the equilibrium QP (the
    /// penalty-ADMM diverges on systems the same physics solves instantly
    /// with clean polygons). Weld consecutive vertices closer than 1e-3 of
    /// the hull diameter, then cull near-collinear vertices, both RELATIVE
    /// tolerances so the pass is scale-free.
    /// </summary>
    private static List<(double U, double V)> WeldHull(List<(double U, double V)> hull)
    {
        int n = hull.Count;
        if (n < 3) return hull;
        double minU = double.MaxValue, maxU = double.MinValue;
        double minV = double.MaxValue, maxV = double.MinValue;
        for (int i = 0; i < n; i++)
        {
            if (hull[i].U < minU) minU = hull[i].U;
            if (hull[i].U > maxU) maxU = hull[i].U;
            if (hull[i].V < minV) minV = hull[i].V;
            if (hull[i].V > maxV) maxV = hull[i].V;
        }
        double du = maxU - minU, dv = maxV - minV;
        double diam = Math.Sqrt(du * du + dv * dv);
        if (diam < 1e-12) return hull;
        double weld2 = (1e-3 * diam) * (1e-3 * diam);

        // 1) weld consecutive near-duplicates (including the wrap-around pair)
        var welded = new List<(double U, double V)>(n);
        for (int i = 0; i < n; i++)
        {
            if (welded.Count == 0) { welded.Add(hull[i]); continue; }
            var p = welded[welded.Count - 1];
            double dx = hull[i].U - p.U, dy = hull[i].V - p.V;
            if (dx * dx + dy * dy > weld2) welded.Add(hull[i]);
        }
        while (welded.Count >= 3)
        {
            var first = welded[0];
            var last = welded[welded.Count - 1];
            double dx = first.U - last.U, dy = first.V - last.V;
            if (dx * dx + dy * dy > weld2) break;
            welded.RemoveAt(welded.Count - 1);
        }
        if (welded.Count < 3) return welded;

        // 2) cull near-collinear vertices: a vertex whose deviation from the
        // prev->next chord is below 1e-4 of the hull diameter is clip dirt
        // (e.g. an edge midpoint protruding by micrometres), not geometry.
        double devTol = 1e-4 * diam;
        bool removed = true;
        while (removed && welded.Count > 3)
        {
            removed = false;
            for (int i = 0; i < welded.Count && welded.Count > 3; i++)
            {
                var prev = welded[(i - 1 + welded.Count) % welded.Count];
                var cur = welded[i];
                var next = welded[(i + 1) % welded.Count];
                double chordU = next.U - prev.U, chordV = next.V - prev.V;
                double chordLen = Math.Sqrt(chordU * chordU + chordV * chordV);
                if (chordLen < 1e-12) continue;
                double dev = Math.Abs(Cross(prev, cur, next)) / chordLen;
                if (dev < devTol)
                {
                    welded.RemoveAt(i);
                    removed = true;
                    i--;
                }
            }
        }
        return welded;
    }

    private static double PolygonArea2D(List<(double U, double V)> poly)
    {
        if (poly == null) throw new ArgumentNullException(nameof(poly));
        int n = poly.Count;
        if (n < 3) return 0.0;
        double a = 0.0;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            a += poly[i].U * poly[j].V - poly[j].U * poly[i].V;
        }
        return 0.5 * Math.Abs(a);
    }
}
