#nullable disable
using System;
using System.Collections.Generic;
using Frahan.EdgeMatching;

namespace Frahan.Masonry.Packing;

// =============================================================================
// StoneCellAssignment — P4 of EVOLUTION_PLAN_MASONRY.md (2026-06-10): the
// imposition <-> negotiation BALANCE ENGINE, made executable.
//
// Given a stone INVENTORY (found / scanned stones — the negotiation side) and
// the TARGET CELLS of a generated wall (the imposition side), assign stones to
// cells minimising the material that must be carved away, and report the
// trade as first-class numbers:
//
//   carve ratio  λ_i = vol(stone_i \ cell_i) / vol(stone_i)   (per stone)
//   imposition index  Λ = Σ carved volume / Σ stone volume     (workflow)
//   gap ratio        g_i = vol(cell_i \ stone_i) / vol(cell_i) (under-fill)
//
// Λ ≈ 1: full imposition (stones are mere stock, cut entirely to the cells —
// sawn ashlar). Λ ≈ 0: true negotiation (stones used as found). The measured
// middle: Clifford & McGee's Cyclopean Cannibalism wall ran at Λ ≈ 0.27
// (73% of scanned stock used) with gap driven to zero by carve-back.
//
// Method:
//   1. Per mesh: volume, centroid, PCA frame + sorted extents.
//   2. Prefilter cost (all pairs, cheap): volume + extent mismatch.
//   3. Refined cost (top-K candidates per cell): VOXEL symmetric-difference
//      fraction after PCA alignment, best of the 4 proper-rotation flips.
//   4. HUNGARIAN assignment (reused from Frahan.EdgeMatching.Core).
//   5. λ / gap for the assigned pairs at a finer voxel resolution.
// Voxel metrics carry a few-percent discretisation error; the EXACT carve-back
// (CGAL boolean, Cyclopean overlap-then-carve) is the downstream fabrication
// step — this engine decides WHO goes WHERE and HOW MUCH it costs.
// Pure managed; no Rhino dependency.
// =============================================================================

/// <summary>Options for <see cref="StoneCellAssignment.Assign"/>.</summary>
public sealed class StoneCellAssignmentOptions
{
    /// <summary>Voxel grid resolution (per axis) for the candidate cost.</summary>
    public int CostVoxels = 12;
    /// <summary>Voxel grid resolution (per axis) for the assigned-pair λ/gap.</summary>
    public int RefineVoxels = 24;
    /// <summary>How many best prefilter candidates per cell get the voxel cost.</summary>
    public int PrefilterTopK = 6;
    /// <summary>Try the 4 extent-preserving proper-rotation flips during alignment.</summary>
    public bool TryFlips = true;
}

/// <summary>One assigned stone-to-cell placement.</summary>
public sealed class StonePlacement
{
    public StonePlacement(int stoneIndex, int cellIndex, double cost,
                          double carveRatio, double gapRatio, double[] transform)
    {
        StoneIndex = stoneIndex; CellIndex = cellIndex; Cost = cost;
        CarveRatio = carveRatio; GapRatio = gapRatio; Transform = transform;
    }
    public int StoneIndex { get; }
    public int CellIndex { get; }
    /// <summary>Voxel symmetric-difference fraction at assignment time (0 = identical shapes).</summary>
    public double Cost { get; }
    /// <summary>λ_i: fraction of the stone's volume that must be carved away to fit the cell.</summary>
    public double CarveRatio { get; }
    /// <summary>Fraction of the cell volume the aligned stone fails to fill.</summary>
    public double GapRatio { get; }
    /// <summary>Row-major 4x4 transform mapping stone coordinates into the cell (world) frame.</summary>
    public double[] Transform { get; }
}

/// <summary>Result of <see cref="StoneCellAssignment.Assign"/>.</summary>
public sealed class AssignmentResult
{
    public AssignmentResult(IReadOnlyList<StonePlacement> placements, double impositionIndex,
                            double meanGapRatio, IReadOnlyList<int> unassignedCells,
                            IReadOnlyList<int> unusedStones)
    {
        Placements = placements; ImpositionIndex = impositionIndex; MeanGapRatio = meanGapRatio;
        UnassignedCells = unassignedCells; UnusedStones = unusedStones;
    }
    public IReadOnlyList<StonePlacement> Placements { get; }
    /// <summary>Λ: volume-weighted carve fraction over the assigned stones (0 negotiation .. 1 imposition).</summary>
    public double ImpositionIndex { get; }
    public double MeanGapRatio { get; }
    public IReadOnlyList<int> UnassignedCells { get; }
    public IReadOnlyList<int> UnusedStones { get; }
}

/// <summary>
/// Hungarian stone-inventory to wall-cell assignment with carve/gap metrics —
/// the executable form of the imposition index Λ.
/// </summary>
public static class StoneCellAssignment
{
    public static AssignmentResult Assign(
        IReadOnlyList<IReadOnlyList<double>> stoneCoords,
        IReadOnlyList<IReadOnlyList<int>> stoneTris,
        IReadOnlyList<IReadOnlyList<double>> cellCoords,
        IReadOnlyList<IReadOnlyList<int>> cellTris,
        StoneCellAssignmentOptions options = null)
    {
        if (stoneCoords == null) throw new ArgumentNullException(nameof(stoneCoords));
        if (stoneTris == null) throw new ArgumentNullException(nameof(stoneTris));
        if (cellCoords == null) throw new ArgumentNullException(nameof(cellCoords));
        if (cellTris == null) throw new ArgumentNullException(nameof(cellTris));
        if (stoneCoords.Count != stoneTris.Count || cellCoords.Count != cellTris.Count)
            throw new ArgumentException("coords/tris lists must be parallel");
        if (stoneCoords.Count == 0 || cellCoords.Count == 0)
            throw new ArgumentException("need at least one stone and one cell");
        options = options ?? new StoneCellAssignmentOptions();

        int nS = stoneCoords.Count, nC = cellCoords.Count;
        var stones = new Body[nS];
        var cells = new Body[nC];
        for (int i = 0; i < nS; i++) stones[i] = Body.Build(stoneCoords[i], stoneTris[i]);
        for (int i = 0; i < nC; i++) cells[i] = Body.Build(cellCoords[i], cellTris[i]);

        // ---- 2. prefilter cost (volume + sorted-extent mismatch) ----
        var pre = new double[nC, nS];
        for (int c = 0; c < nC; c++)
            for (int s = 0; s < nS; s++)
                pre[c, s] = PrefilterCost(stones[s], cells[c]);

        // ---- 3. refined voxel cost for the top-K stones per cell ----
        var cost = new double[nC, nS];
        var bestFlip = new int[nC, nS];
        for (int c = 0; c < nC; c++)
        {
            var order = TopK(pre, c, nS, options.PrefilterTopK);
            for (int s = 0; s < nS; s++) { cost[c, s] = 1.0 + pre[c, s]; bestFlip[c, s] = 0; } // 1+pre > any voxel frac
            foreach (int s in order)
            {
                cost[c, s] = VoxelSymDiff(stones[s], cells[c], options.CostVoxels,
                                          options.TryFlips, out int flip, out _, out _);
                bestFlip[c, s] = flip;
            }
        }

        // ---- 4. Hungarian (rows = cells, cols = stones) ----
        var flat = new double[nC * nS];
        for (int c = 0; c < nC; c++)
            for (int s = 0; s < nS; s++) flat[c * nS + s] = cost[c, s];
        int[] match = HungarianAssigner.Solve(flat, nC, nS);

        // ---- 5. λ / gap at refine resolution + Λ ----
        var placements = new List<StonePlacement>(nC);
        var unassignedCells = new List<int>();
        var usedStones = new HashSet<int>();
        double carvedVol = 0, stoneVol = 0, gapSum = 0;
        for (int c = 0; c < nC; c++)
        {
            int s = match[c];
            if (s < 0) { unassignedCells.Add(c); continue; }
            usedStones.Add(s);
            VoxelSymDiff(stones[s], cells[c], options.RefineVoxels, options.TryFlips,
                         out int flip, out double carve, out double gap);
            var t = AlignmentTransform(stones[s], cells[c], flip);
            placements.Add(new StonePlacement(s, c, cost[c, s], carve, gap, t));
            carvedVol += carve * stones[s].Volume;
            stoneVol += stones[s].Volume;
            gapSum += gap;
        }
        var unused = new List<int>();
        for (int s = 0; s < nS; s++) if (!usedStones.Contains(s)) unused.Add(s);

        double lambda = stoneVol > 1e-12 ? carvedVol / stoneVol : 0;
        double meanGap = placements.Count > 0 ? gapSum / placements.Count : 0;
        return new AssignmentResult(placements, lambda, meanGap, unassignedCells, unused);
    }

    // =========================================================================
    // Body: volume, centroid, PCA frame, sorted extents, local-frame vertices.
    // =========================================================================
    private sealed class Body
    {
        public double Volume;
        public double[] Centroid = new double[3];
        public double[,] Axes = new double[3, 3];   // rows = principal axes (unit, right-handed)
        public double[] Extents = new double[3];    // half-extents along axes, sorted desc
        public double[][] LocalVerts;               // vertices in the PCA frame
        public int[] Tris;
        public double[] LocalMin = new double[3];
        public double[] LocalMax = new double[3];

        public static Body Build(IReadOnlyList<double> coords, IReadOnlyList<int> tris)
        {
            var b = new Body();
            int nv = coords.Count / 3;
            b.Tris = new int[tris.Count];
            for (int i = 0; i < tris.Count; i++) b.Tris[i] = tris[i];

            // signed volume + volume centroid (divergence theorem)
            double v6 = 0; var cx = new double[3];
            for (int t = 0; t < tris.Count; t += 3)
            {
                int a = tris[t] * 3, b2 = tris[t + 1] * 3, c = tris[t + 2] * 3;
                double ax = coords[a], ay = coords[a + 1], az = coords[a + 2];
                double bx = coords[b2], by = coords[b2 + 1], bz = coords[b2 + 2];
                double cx2 = coords[c], cy = coords[c + 1], cz = coords[c + 2];
                double det = ax * (by * cz - bz * cy) - ay * (bx * cz - bz * cx2) + az * (bx * cy - by * cx2);
                v6 += det;
                cx[0] += det * (ax + bx + cx2);
                cx[1] += det * (ay + by + cy);
                cx[2] += det * (az + bz + cz);
            }
            b.Volume = Math.Abs(v6) / 6.0;
            double inv = Math.Abs(v6) > 1e-30 ? 1.0 / (4.0 * v6) : 0;
            if (inv != 0) { b.Centroid[0] = cx[0] * inv; b.Centroid[1] = cx[1] * inv; b.Centroid[2] = cx[2] * inv; }
            else
            {
                for (int i = 0; i < nv; i++)
                { b.Centroid[0] += coords[i * 3]; b.Centroid[1] += coords[i * 3 + 1]; b.Centroid[2] += coords[i * 3 + 2]; }
                b.Centroid[0] /= nv; b.Centroid[1] /= nv; b.Centroid[2] /= nv;
            }

            // PCA of the vertex cloud (alignment frame)
            var cov = new double[3, 3];
            for (int i = 0; i < nv; i++)
            {
                double dx = coords[i * 3] - b.Centroid[0];
                double dy = coords[i * 3 + 1] - b.Centroid[1];
                double dz = coords[i * 3 + 2] - b.Centroid[2];
                cov[0, 0] += dx * dx; cov[0, 1] += dx * dy; cov[0, 2] += dx * dz;
                cov[1, 1] += dy * dy; cov[1, 2] += dy * dz; cov[2, 2] += dz * dz;
            }
            cov[1, 0] = cov[0, 1]; cov[2, 0] = cov[0, 2]; cov[2, 1] = cov[1, 2];
            var eval = new double[3];
            var evec = Jacobi3(cov, eval);
            // sort by eigenvalue desc
            int[] ord = { 0, 1, 2 };
            Array.Sort(ord, (p, q) => eval[q].CompareTo(eval[p]));
            for (int r = 0; r < 3; r++)
                for (int k = 0; k < 3; k++) b.Axes[r, k] = evec[k, ord[r]];
            // right-handed
            double hx = b.Axes[0, 1] * b.Axes[1, 2] - b.Axes[0, 2] * b.Axes[1, 1];
            double hy = b.Axes[0, 2] * b.Axes[1, 0] - b.Axes[0, 0] * b.Axes[1, 2];
            double hz = b.Axes[0, 0] * b.Axes[1, 1] - b.Axes[0, 1] * b.Axes[1, 0];
            if (hx * b.Axes[2, 0] + hy * b.Axes[2, 1] + hz * b.Axes[2, 2] < 0)
            { b.Axes[2, 0] = -b.Axes[2, 0]; b.Axes[2, 1] = -b.Axes[2, 1]; b.Axes[2, 2] = -b.Axes[2, 2]; }

            // local-frame vertices + bounds + half-extents
            b.LocalVerts = new double[nv][];
            for (int k = 0; k < 3; k++) { b.LocalMin[k] = double.MaxValue; b.LocalMax[k] = double.MinValue; }
            for (int i = 0; i < nv; i++)
            {
                double dx = coords[i * 3] - b.Centroid[0];
                double dy = coords[i * 3 + 1] - b.Centroid[1];
                double dz = coords[i * 3 + 2] - b.Centroid[2];
                var lv = new double[3];
                for (int r = 0; r < 3; r++) lv[r] = b.Axes[r, 0] * dx + b.Axes[r, 1] * dy + b.Axes[r, 2] * dz;
                b.LocalVerts[i] = lv;
                for (int k = 0; k < 3; k++)
                { if (lv[k] < b.LocalMin[k]) b.LocalMin[k] = lv[k]; if (lv[k] > b.LocalMax[k]) b.LocalMax[k] = lv[k]; }
            }
            for (int k = 0; k < 3; k++) b.Extents[k] = (b.LocalMax[k] - b.LocalMin[k]) / 2.0;
            return b;
        }
    }

    private static double PrefilterCost(Body stone, Body cell)
    {
        double vMis = Math.Abs(stone.Volume - cell.Volume) / Math.Max(Math.Max(stone.Volume, cell.Volume), 1e-12);
        double eMis = 0;
        for (int k = 0; k < 3; k++)
        {
            double m = Math.Max(Math.Max(stone.Extents[k], cell.Extents[k]), 1e-12);
            eMis += Math.Abs(stone.Extents[k] - cell.Extents[k]) / m;
        }
        return 0.5 * vMis + 0.5 * (eMis / 3.0);
    }

    private static List<int> TopK(double[,] pre, int c, int nS, int k)
    {
        var idx = new List<int>(nS);
        for (int s = 0; s < nS; s++) idx.Add(s);
        idx.Sort((p, q) => pre[c, p].CompareTo(pre[c, q]));
        if (idx.Count > k) idx.RemoveRange(k, idx.Count - k);
        return idx;
    }

    // proper-rotation flips (preserve handedness): diag(1,1,1), (1,-1,-1), (-1,1,-1), (-1,-1,1)
    private static readonly double[][] Flips =
    {
        new[] { 1.0, 1.0, 1.0 }, new[] { 1.0, -1.0, -1.0 },
        new[] { -1.0, 1.0, -1.0 }, new[] { -1.0, -1.0, 1.0 },
    };

    /// <summary>
    /// Voxel symmetric-difference fraction between the PCA-aligned stone and the
    /// cell (in the cell's local frame), best over the proper flips. Also returns
    /// carve = |S\C|/|S| and gap = |C\S|/|C| voxel fractions for the best flip.
    /// </summary>
    private static double VoxelSymDiff(Body stone, Body cell, int res, bool tryFlips,
                                       out int bestFlip, out double carve, out double gap)
    {
        bestFlip = 0; carve = 1; gap = 1;
        double best = double.MaxValue;
        int flipCount = tryFlips ? Flips.Length : 1;

        // grid over the union of local bounds (cell frame; stone occupies its own
        // local bounds after alignment because PCA frames are matched 1:1)
        var lo = new double[3]; var hi = new double[3];
        for (int k = 0; k < 3; k++)
        {
            lo[k] = Math.Min(cell.LocalMin[k], stone.LocalMin[k]) - 1e-9;
            hi[k] = Math.Max(cell.LocalMax[k], stone.LocalMax[k]) + 1e-9;
        }
        var cellMask = RasterizeLocal(cell.LocalVerts, cell.Tris, lo, hi, res, null);

        for (int f = 0; f < flipCount; f++)
        {
            var stoneMask = RasterizeLocal(stone.LocalVerts, stone.Tris, lo, hi, res, Flips[f]);
            int onlyS = 0, onlyC = 0, inter = 0, sTot = 0, cTot = 0;
            for (int i = 0; i < cellMask.Length; i++)
            {
                bool inC = cellMask[i], inS = stoneMask[i];
                if (inS) sTot++;
                if (inC) cTot++;
                if (inS && inC) inter++;
                else if (inS) onlyS++;
                else if (inC) onlyC++;
            }
            if (sTot == 0 || cTot == 0) continue;
            double sym = (onlyS + onlyC) / (double)(sTot + cTot);
            if (sym < best)
            {
                best = sym; bestFlip = f;
                carve = onlyS / (double)sTot;
                gap = onlyC / (double)cTot;
            }
        }
        return best == double.MaxValue ? 1.0 : best;
    }

    /// <summary>Point-in-mesh voxel mask by +x ray parity, in the shared local grid.
    /// flip == null rasterises as-is; otherwise vertex components are sign-flipped.</summary>
    private static bool[] RasterizeLocal(double[][] verts, int[] tris, double[] lo, double[] hi,
                                         int res, double[] flip)
    {
        var mask = new bool[res * res * res];
        var step = new double[3];
        for (int k = 0; k < 3; k++) step[k] = (hi[k] - lo[k]) / res;

        // flipped vertex buffer
        int nv = verts.Length;
        var fx = new double[nv]; var fy = new double[nv]; var fz = new double[nv];
        for (int i = 0; i < nv; i++)
        {
            fx[i] = flip == null ? verts[i][0] : verts[i][0] * flip[0];
            fy[i] = flip == null ? verts[i][1] : verts[i][1] * flip[1];
            fz[i] = flip == null ? verts[i][2] : verts[i][2] * flip[2];
        }

        for (int iz = 0; iz < res; iz++)
        {
            double pz = lo[2] + (iz + 0.5) * step[2];
            for (int iy = 0; iy < res; iy++)
            {
                double py = lo[1] + (iy + 0.5) * step[1] + 1e-9; // jitter off shared edges
                // collect x-crossings of the ray (y=py, z=pz) once per row
                var xs = new List<double>(8);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    double y0 = fy[a], y1 = fy[b], y2 = fy[c];
                    double z0 = fz[a], z1 = fz[b], z2 = fz[c];
                    // 2D point-in-triangle in (y,z)
                    double d = (y1 - y0) * (z2 - z0) - (z1 - z0) * (y2 - y0);
                    if (Math.Abs(d) < 1e-30) continue;
                    double w1 = ((py - y0) * (z2 - z0) - (pz + 1e-9 - z0) * (y2 - y0)) / d;
                    double w2 = ((y1 - y0) * (pz + 1e-9 - z0) - (z1 - z0) * (py - y0)) / d;
                    if (w1 < 0 || w2 < 0 || w1 + w2 > 1) continue;
                    xs.Add(fx[a] + w1 * (fx[b] - fx[a]) + w2 * (fx[c] - fx[a]));
                }
                if (xs.Count < 2) continue;
                xs.Sort();
                // parity fill along the row
                for (int ix = 0; ix < res; ix++)
                {
                    double px = lo[0] + (ix + 0.5) * step[0];
                    int crossings = 0;
                    for (int q = 0; q < xs.Count; q++) if (xs[q] > px) crossings++;
                    if ((crossings & 1) == 1)
                        mask[(iz * res + iy) * res + ix] = true;
                }
            }
        }
        return mask;
    }

    /// <summary>Row-major 4x4: stone world coords -> cell world frame (PCA-aligned, flip f).</summary>
    private static double[] AlignmentTransform(Body stone, Body cell, int flipIdx)
    {
        // p_cell = R_cellᵀ · F · R_stone · (p − c_stone) + c_cell
        var f = Flips[flipIdx];
        var m = new double[16];
        // R = R_cellᵀ (rows of cell.Axes are axes => R_cellᵀ has axes as columns) times diag(f) times R_stone
        var r = new double[3, 3];
        for (int i = 0; i < 3; i++)        // world row
            for (int j2 = 0; j2 < 3; j2++) // world col
            {
                double sum = 0;
                for (int k = 0; k < 3; k++) sum += cell.Axes[k, i] * f[k] * stone.Axes[k, j2];
                r[i, j2] = sum;
            }
        for (int i = 0; i < 3; i++)
        {
            for (int j2 = 0; j2 < 3; j2++) m[i * 4 + j2] = r[i, j2];
            m[i * 4 + 3] = cell.Centroid[i]
                - (r[i, 0] * stone.Centroid[0] + r[i, 1] * stone.Centroid[1] + r[i, 2] * stone.Centroid[2]);
        }
        m[15] = 1;
        return m;
    }

    /// <summary>Jacobi eigen-decomposition of a symmetric 3x3; returns eigenvectors (columns).</summary>
    private static double[,] Jacobi3(double[,] a, double[] eval)
    {
        var v = new double[3, 3];
        var b = new double[3, 3];
        for (int i = 0; i < 3; i++) { v[i, i] = 1; for (int j2 = 0; j2 < 3; j2++) b[i, j2] = a[i, j2]; }
        for (int sweep = 0; sweep < 50; sweep++)
        {
            double off = Math.Abs(b[0, 1]) + Math.Abs(b[0, 2]) + Math.Abs(b[1, 2]);
            if (off < 1e-14) break;
            for (int p = 0; p < 2; p++)
            {
                for (int q = p + 1; q < 3; q++)
                {
                    if (Math.Abs(b[p, q]) < 1e-300) continue;
                    double theta = (b[q, q] - b[p, p]) / (2 * b[p, q]);
                    double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    if (theta == 0) t = 1;
                    double cos = 1 / Math.Sqrt(t * t + 1), sin = t * cos;
                    for (int k = 0; k < 3; k++)
                    {
                        double bkp = b[k, p], bkq = b[k, q];
                        b[k, p] = cos * bkp - sin * bkq;
                        b[k, q] = sin * bkp + cos * bkq;
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        double bpk = b[p, k], bqk = b[q, k];
                        b[p, k] = cos * bpk - sin * bqk;
                        b[q, k] = sin * bpk + cos * bqk;
                        double vkp = v[k, p], vkq = v[k, q];
                        v[k, p] = cos * vkp - sin * vkq;
                        v[k, q] = sin * vkp + cos * vkq;
                    }
                }
            }
        }
        for (int i = 0; i < 3; i++) eval[i] = b[i, i];
        return v;
    }
}
