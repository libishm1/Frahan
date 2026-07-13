#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Masonry.Nbo
{
    // =========================================================================
    // StoneSlotMatcher — globally-optimal stone-to-slot assignment via the
    // Jonker-Volgenant (LAPJV) successive-shortest-paths algorithm.
    //
    // The greedy NBO loop scores O(N×M) pairs per wall fill, always picking the
    // best stone at each position in sequence. That is locally optimal but not
    // globally: a good stone for slot 3 might be even better for slot 7, leaving
    // slot 3 with a mediocre stone. LAPJV finds the minimum-cost bijective
    // assignment in O(n^3), where n = max(N stones, M slots).
    //
    // Cost model: dimension-only (bounding-box), not full drop-to-contact. This
    // keeps matrix build at O(N×M × O(1)) instead of O(N×M × mesh-ray-casts).
    // The assignment is a pre-pass; the caller still drives the full
    // EvaluateCandidate / drop-to-contact / gate in execution order.
    // =========================================================================

    /// <summary>A pre-defined wall slot (a position where one stone will be placed).</summary>
    public sealed class StoneSlot
    {
        public double FrontX;
        public double SeedZ;
        public double Offset;
        public int    Course;
        /// <summary>Estimated slot width along the wall run (used for fill-cost).</summary>
        public double ExpectedWidth;
        /// <summary>Estimated course height (used for height-cost).</summary>
        public double ExpectedHeight;
    }

    /// <summary>Result of the LAPJV or greedy assignment.</summary>
    public sealed class StoneSlotAssignment
    {
        /// <summary>SlotToStone[j] = inventory index assigned to slot j, or -1 if unfilled.</summary>
        public int[]  SlotToStone;
        /// <summary>StoneToSlot[i] = slot index assigned to stone i, or -1 if unplaced.</summary>
        public int[]  StoneToSlot;
        /// <summary>Total assignment cost (sum of per-pair quick-costs).</summary>
        public double TotalCost;
        /// <summary>True when frahan_lapjv.dll was used; false when greedy fallback ran.</summary>
        public bool   LapjvUsed;

        // diagnostics
        public int StonesConsidered;
        public int SlotsConsidered;
        public int Assigned;
    }

    public static class StoneSlotMatcher
    {
        /// <summary>True when frahan_lapjv.dll is available and LAPJV will be used.</summary>
        public static bool LapjvAvailable => LapjvNative.Available;

        /// <summary>Version string from frahan_lapjv.dll, or "(not found)" when absent.</summary>
        public static string LapjvVersion => LapjvNative.Version() ?? "(not found)";

        // ---- cost model -------------------------------------------------------
        // Cheap dimension-only cost: no mesh ray-casts.
        // Lower cost = better fit for the slot.
        private static double QuickCost(
            StoneShape shape, StoneSlot slot, NboFillOptions opt)
        {
            if (shape == null || shape.StableFaces.Count == 0)
                return 1e9;   // no stable face → strongly discourage

            // Extents[2] = long-axis span (footprint length into the wall when placed flat).
            // Extents[0] = thin-axis span (height when resting on the dominant flat face).
            double stoneLen = shape.Extents[2];
            double stoneH   = shape.Extents[0];

            // Height deviation: penalise if stone is taller than the expected course.
            double heightCost = opt.WeightHeight
                              * Math.Max(0.0, stoneH - slot.ExpectedHeight);

            // Fill contribution: reward stones whose footprint covers the slot width.
            double fillGap  = Math.Max(0.0, slot.ExpectedWidth - stoneLen);
            double fillCost = opt.WeightFill * fillGap;

            return heightCost + fillCost;
        }

        // ---- main entry -------------------------------------------------------
        /// <summary>
        /// Assign available inventory stones to wall slots using LAPJV (with greedy
        /// fallback when frahan_lapjv.dll is absent).
        ///
        /// n = max(available stones, slots). The cost matrix is n×n (dummy rows/cols
        /// carry zero cost so padding never prevents a real assignment).
        /// </summary>
        public static StoneSlotAssignment Match(
            IReadOnlyList<Mesh>       inventory,
            IReadOnlyList<StoneShape> shapes,
            ISet<int>                 usedStones,
            IReadOnlyList<StoneSlot>  slots,
            NboFillOptions            opt)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            if (slots     == null) throw new ArgumentNullException(nameof(slots));
            opt = opt ?? new NboFillOptions();

            // Available (unused) stone indices.
            var avail = new List<int>(inventory.Count);
            for (int i = 0; i < inventory.Count; i++)
                if (usedStones == null || !usedStones.Contains(i)) avail.Add(i);

            int ns    = avail.Count;
            int nSlot = slots.Count;

            var result = new StoneSlotAssignment
            {
                SlotToStone       = new int[nSlot],
                StoneToSlot       = new int[inventory.Count],
                StonesConsidered  = ns,
                SlotsConsidered   = nSlot,
            };
            for (int j = 0; j < nSlot; j++)           result.SlotToStone[j]  = -1;
            for (int i = 0; i < inventory.Count; i++) result.StoneToSlot[i] = -1;

            if (ns == 0 || nSlot == 0) return result;

            int n = Math.Max(ns, nSlot);

            // Build n×n cost matrix (row = stone row index in avail[], col = slot index).
            // Padding rows/cols get 0.0 (dummy, not penalised) so LAPJV is free to use them
            // for the leftovers without inflating the objective.
            double[] cost = new double[n * n];  // zero-initialised → dummy entries = 0

            for (int si = 0; si < ns; si++)
            {
                int stoneIdx = avail[si];
                StoneShape sh = (shapes != null && stoneIdx < shapes.Count)
                                ? shapes[stoneIdx] : null;

                for (int sj = 0; sj < nSlot; sj++)
                    cost[si * n + sj] = QuickCost(sh, slots[sj], opt);

                // Columns beyond nSlot (dummy slots) keep 0.
            }
            // Rows beyond ns (dummy stones) are already 0.

            bool usedLapjv = false;
            if (LapjvNative.Available)
            {
                int[]    rowsol = new int[n];
                int[]    colsol = new int[n];
                double[] u      = new double[n];
                double[] v      = new double[n];

                int code = LapjvNative.Solve(n, cost, rowsol, colsol, u, v, out double obj);
                if (code == 0)
                {
                    usedLapjv = true;
                    result.TotalCost = obj;
                    int assigned = 0;

                    for (int si = 0; si < ns; si++)
                    {
                        int sj = rowsol[si];
                        if (sj < nSlot)          // real slot (not dummy padding)
                        {
                            int stoneIdx = avail[si];
                            result.SlotToStone[sj]     = stoneIdx;
                            result.StoneToSlot[stoneIdx] = sj;
                            assigned++;
                        }
                    }
                    result.Assigned = assigned;
                }
            }

            if (!usedLapjv)
            {
                // Greedy fallback: each slot gets the cheapest unassigned stone.
                var assignedStones = new HashSet<int>();
                double totalCost   = 0.0;
                int    assigned    = 0;

                for (int sj = 0; sj < nSlot; sj++)
                {
                    double bestCost = double.MaxValue;
                    int    bestSi   = -1;

                    for (int si = 0; si < ns; si++)
                    {
                        if (assignedStones.Contains(avail[si])) continue;
                        double c = cost[si * n + sj];
                        if (c < bestCost) { bestCost = c; bestSi = si; }
                    }

                    if (bestSi >= 0)
                    {
                        int stoneIdx = avail[bestSi];
                        result.SlotToStone[sj]       = stoneIdx;
                        result.StoneToSlot[stoneIdx] = sj;
                        assignedStones.Add(stoneIdx);
                        totalCost += bestCost;
                        assigned++;
                    }
                }
                result.TotalCost = totalCost;
                result.Assigned  = assigned;
            }

            result.LapjvUsed = usedLapjv;
            return result;
        }

        // ---- slot-grid generator --------------------------------------------
        /// <summary>
        /// Generate a uniform-grid slot list for a straight wall, estimating slot
        /// parameters from the inventory's median bounding-box dimensions.
        /// </summary>
        public static List<StoneSlot> GenerateWallSlots(
            IReadOnlyList<StoneShape> shapes,
            NboFillOptions            opt)
        {
            opt = opt ?? new NboFillOptions();

            // Estimate median stone footprint (X) and height (Z).
            var lengths  = new List<double>(shapes?.Count ?? 0);
            var heights  = new List<double>(shapes?.Count ?? 0);
            if (shapes != null)
            {
                foreach (var sh in shapes)
                {
                    if (sh == null || sh.Extents == null) continue;
                    lengths.Add(sh.Extents[2]);  // long axis = footprint length
                    heights.Add(sh.Extents[0]);  // thin axis = placed height
                }
            }
            if (lengths.Count == 0) { lengths.Add(0.28); heights.Add(0.18); }

            lengths.Sort();  heights.Sort();
            double medLen = lengths[lengths.Count / 2];
            double medH   = heights[heights.Count / 2];
            if (medLen < 0.05) medLen = 0.28;
            if (medH   < 0.02) medH   = 0.18;

            double wallLen = opt.WallLength;
            double targetH = opt.TargetHeight;

            var slots = new List<StoneSlot>();
            double top = 0.0;
            int course = 0;

            while (top < targetH)
            {
                double offset  = (course % 2 == 1) ? opt.CourseOffset : 0.0;
                double frontX  = 0.0;

                while (frontX < wallLen)
                {
                    slots.Add(new StoneSlot
                    {
                        FrontX         = frontX,
                        SeedZ          = top + 0.10,
                        Offset         = offset,
                        Course         = course,
                        ExpectedWidth  = medLen,
                        ExpectedHeight = medH,
                    });
                    frontX += medLen + opt.Gap;
                }
                top    += medH;
                course++;
            }
            return slots;
        }
    }
}
