#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.DataModel;

namespace Frahan.Masonry.Packing;

// =============================================================================
// BestFitInventoryPacker — inventory-aware ashlar packing inspired by ETH
// Zurich Gramazio Kohler Research's robotic-masonry workflow
// (gramaziokohler/ashlar). Where AshlarLayoutEngine picks the FIRST slab
// that fits a slot, this packer scores EVERY remaining slab against the
// slot and picks the highest-scoring one.
//
// Scoring (default weights, all in [0, 1]):
//   - Width fit:    1 - |w_slab - w_target| / max(w_target, w_slab)
//   - Depth fit:    1 - |d_slab - d_target| / max(d_target, d_slab)
//   - Height fit:   1 - |h_slab - h_target| / max(h_target, h_slab)
//   - Aspect fit:   1 - |aspect(w/h)_slab - aspect(w/h)_target| / max(...)
//
// Combined score = mean of the four. The packer picks the highest-scoring
// candidate that physically fits (slab.W <= remaining_x, slab.D <=
// wallThickness). Falls back to first-fit if no candidate fits.
//
// References: Furrer, F. et al. (2017). "Autonomous robotic stone stacking
// with online next best object target pose planning." IEEE ICRA.
// Johns, R.L. et al. (2020). "Autonomous dry stone." Construction
// Robotics 4. Implementation reference: the gramaziokohler/ashlar
// repository.
// =============================================================================

public static class BestFitInventoryPacker
{
    private const int HardCourseLimit = 10000;
    private const int HardSlotLimit = 10000;
    private const double Eps = 1e-9;

    /// <summary>
    /// Pack <paramref name="slabs"/> into the wall described by
    /// <paramref name="options"/> using best-fit selection. Returns the
    /// same <see cref="AshlarPackResult"/> shape as
    /// <see cref="AshlarLayoutEngine.Pack"/>.
    /// </summary>
    public static AshlarPackResult Pack(
        IReadOnlyList<Slab> slabs,
        AshlarPackOptions options)
    {
        if (slabs == null) throw new ArgumentNullException(nameof(slabs));
        if (options == null) throw new ArgumentNullException(nameof(options));

        var inventory = ComputeInventory(slabs);
        var filtered = FilterByMode(inventory, options);
        var notes = new List<string>(8);
        var consumed = new HashSet<int>();
        var remaining = new List<InventoryItem>(filtered);

        var placed = LayCourses(remaining, options, notes, consumed);
        AssertPlacementsInBounds(placed, options);

        var blocks = BuildBlocks(placed, options);
        var interfaces = new List<MasonryInterface>(placed.Count * 2 + 8);
        EmitHeadJointInterfaces(placed, options, interfaces);
        EmitBedJointInterfaces(placed, options, interfaces);

        var bc = BuildBoundaryConditions(placed);
        var assembly = new MasonryAssembly(blocks, interfaces, bc);
        var leftovers = CollectLeftovers(slabs, consumed, remaining);
        int courseCount = CountCourses(placed);
        double coverage = ComputeCoverage(placed, options);
        return new AshlarPackResult(assembly, blocks, leftovers, courseCount, coverage, notes);
    }

    // ─── Helpers (intentionally mirror AshlarLayoutEngine to share semantics) ─

    private struct InventoryItem
    {
        public Slab Slab;
        public int OriginalIndex;
        public double XMin, YMin, ZMin;
        public double Width, Height, Depth;
    }

    private struct PlacedItem
    {
        public string BlockId;
        public Slab Source;
        public int CourseIndex;
        public int SlotIndex;
        public double OriginX, OriginY, OriginZ;
        public double Width, Height, Depth;
    }

    private static List<InventoryItem> ComputeInventory(IReadOnlyList<Slab> slabs)
    {
        var result = new List<InventoryItem>(slabs.Count);
        for (int i = 0; i < slabs.Count; i++)
        {
            var s = slabs[i] ?? throw new ArgumentException($"slabs[{i}] is null");
            ComputeAabb(s, out double xMin, out double yMin, out double zMin,
                           out double xMax, out double yMax, out double zMax);
            var item = new InventoryItem
            {
                Slab = s,
                OriginalIndex = i,
                XMin = xMin, YMin = yMin, ZMin = zMin,
                Width = xMax - xMin,
                Height = zMax - zMin,
                Depth = yMax - yMin,
            };
            if (!(item.Width > 0 && item.Height > 0 && item.Depth > 0))
                throw new ArgumentException($"slabs[{i}] has degenerate AABB", nameof(slabs));
            result.Add(item);
        }
        return result;
    }

    private static List<InventoryItem> FilterByMode(List<InventoryItem> inv, AshlarPackOptions opts)
    {
        var result = new List<InventoryItem>(inv.Count);
        for (int i = 0; i < inv.Count; i++)
        {
            var it = inv[i];
            bool heightOk = opts.Mode == CourseMode.CoursedRubble
                || Math.Abs(it.Height - opts.TargetCourseHeight) <= opts.HeightTolerance + Eps;
            if (heightOk && it.Depth <= opts.WallThickness + Eps)
                result.Add(it);
        }
        return result;
    }

    private static List<PlacedItem> LayCourses(
        List<InventoryItem> remaining, AshlarPackOptions options,
        List<string> notes, HashSet<int> consumed)
    {
        // 2026-05-14: route CoursedRubble through LayCoursesRubble so heights
        // can vary per course (the existing path baked in
        // options.TargetCourseHeight for every slot, which broke fracture-
        // pattern inventories where heights don't all match the target).
        if (options.Mode == CourseMode.CoursedRubble)
            return LayCoursesRubble(remaining, options, notes, consumed);
        return LayCoursesAshlar(remaining, options, notes, consumed);
    }

    private static List<PlacedItem> LayCoursesAshlar(
        List<InventoryItem> remaining, AshlarPackOptions options,
        List<string> notes, HashSet<int> consumed)
    {
        var placed = new List<PlacedItem>(remaining.Count);
        int maxCourses = Math.Min(HardCourseLimit,
            (int)Math.Ceiling(options.WallHeight / options.TargetCourseHeight) + 8);
        int maxSlots = Math.Min(HardSlotLimit, remaining.Count + 64);

        double avgWidth = ComputeAverageWidth(remaining);
        double y = 0.0;
        int courseIndex = 0;

        while (y + options.TargetCourseHeight <= options.WallHeight + Eps && courseIndex < maxCourses)
        {
            double x = (courseIndex % 2 == 1) ? options.StaggerOffset * avgWidth : 0.0;
            int slot = 0;
            while (x < options.WallWidth - Eps && slot < maxSlots)
            {
                double gap = options.WallWidth - x;
                int pickIdx = SelectBestFit(remaining, gap, options.WallThickness,
                    options.TargetCourseHeight);
                if (pickIdx < 0)
                {
                    notes.Add($"course {courseIndex} gap from x={x:F3} to {options.WallWidth:F3}");
                    break;
                }

                var pick = remaining[pickIdx];
                remaining.RemoveAt(pickIdx);
                if (pick.OriginalIndex >= 0) consumed.Add(pick.OriginalIndex);

                double placedY = (options.WallThickness - pick.Depth) * 0.5;
                string id = $"bestfit_{courseIndex:D3}_{slot:D3}";
                placed.Add(new PlacedItem
                {
                    BlockId = id,
                    Source = pick.Slab,
                    CourseIndex = courseIndex,
                    SlotIndex = slot,
                    OriginX = x, OriginY = placedY, OriginZ = y,
                    Width = pick.Width, Height = options.TargetCourseHeight, Depth = pick.Depth,
                });
                x += pick.Width + options.HeadJoint;
                slot += 1;
            }
            if (slot >= maxSlots)
                throw new InvalidOperationException("loop guard tripped (slot)");
            y += options.TargetCourseHeight + options.BedJoint;
            courseIndex += 1;
        }
        if (courseIndex >= maxCourses && y + options.TargetCourseHeight <= options.WallHeight + Eps)
            throw new InvalidOperationException("loop guard tripped (course)");
        return placed;
    }

    /// <summary>
    /// CoursedRubble path: bin remaining items by height (tolerance from
    /// options), pick the fullest bin per course so the course height is
    /// driven by the inventory (not by options.TargetCourseHeight). Inside
    /// each bin, the slab pick is still best-fit by width / depth / aspect.
    /// </summary>
    private static List<PlacedItem> LayCoursesRubble(
        List<InventoryItem> remaining, AshlarPackOptions options,
        List<string> notes, HashSet<int> consumed)
    {
        var placed = new List<PlacedItem>(remaining.Count);
        var bins = BinByHeight(remaining, options.HeightTolerance);
        int maxCourses = HardCourseLimit;
        int maxSlots = Math.Min(HardSlotLimit, remaining.Count + 64);

        double avgWidth = ComputeAverageWidth(remaining);
        double y = 0.0;
        int courseIndex = 0;

        while (courseIndex < maxCourses)
        {
            int binIdx = PickFullestBin(bins);
            if (binIdx < 0) break;
            var bin = bins[binIdx];
            double courseHeight = bin[0].Height;
            if (y + courseHeight > options.WallHeight + Eps) break;

            double x = (courseIndex % 2 == 1) ? options.StaggerOffset * avgWidth : 0.0;
            int slot = 0;
            int placedThisCourse = 0;
            while (x < options.WallWidth - Eps && slot < maxSlots)
            {
                double gap = options.WallWidth - x;
                int pickIdx = SelectBestFit(bin, gap, options.WallThickness, courseHeight);
                if (pickIdx < 0)
                {
                    notes.Add($"course {courseIndex} gap from x={x:F3} to {options.WallWidth:F3}");
                    break;
                }
                var pick = bin[pickIdx];
                bin.RemoveAt(pickIdx);
                if (pick.OriginalIndex >= 0) consumed.Add(pick.OriginalIndex);

                double placedY = (options.WallThickness - pick.Depth) * 0.5;
                placed.Add(new PlacedItem
                {
                    BlockId = $"bestfit_{courseIndex:D3}_{slot:D3}",
                    Source = pick.Slab,
                    CourseIndex = courseIndex,
                    SlotIndex = slot,
                    OriginX = x, OriginY = placedY, OriginZ = y,
                    Width = pick.Width, Height = courseHeight, Depth = pick.Depth,
                });
                x += pick.Width + options.HeadJoint;
                slot += 1;
                placedThisCourse += 1;
            }
            if (placedThisCourse == 0)
            {
                // bin exhausted without placing -- discard it so we don't loop forever
                bins.RemoveAt(binIdx);
                continue;
            }
            y += courseHeight + options.BedJoint;
            courseIndex += 1;
        }
        if (courseIndex >= maxCourses)
            throw new InvalidOperationException("loop guard tripped (course)");
        return placed;
    }

    private static List<List<InventoryItem>> BinByHeight(List<InventoryItem> items, double tolerance)
    {
        var bins = new List<List<InventoryItem>>(8);
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            int found = -1;
            for (int b = 0; b < bins.Count; b++)
            {
                if (Math.Abs(bins[b][0].Height - it.Height) <= tolerance + Eps)
                {
                    found = b;
                    break;
                }
            }
            if (found < 0)
            {
                var bin = new List<InventoryItem>(4);
                bin.Add(it);
                bins.Add(bin);
            }
            else
            {
                bins[found].Add(it);
            }
        }
        return bins;
    }

    private static int PickFullestBin(List<List<InventoryItem>> bins)
    {
        int bestIdx = -1;
        int bestCount = 0;
        for (int i = 0; i < bins.Count; i++)
        {
            if (bins[i].Count > bestCount)
            {
                bestIdx = i;
                bestCount = bins[i].Count;
            }
        }
        return bestIdx;
    }

    /// <summary>
    /// Score each fitting candidate; pick highest. Returns -1 if none fits.
    /// </summary>
    private static int SelectBestFit(
        List<InventoryItem> remaining, double slotWidth, double wallThickness, double courseHeight)
    {
        int bestIdx = -1;
        double bestScore = double.NegativeInfinity;
        for (int i = 0; i < remaining.Count; i++)
        {
            var it = remaining[i];
            if (it.Width > slotWidth + Eps) continue;
            if (it.Depth > wallThickness + Eps) continue;

            // Width fit: how close does this slab come to filling the gap?
            // Aspect fit: how close is its W:H ratio to the target slot's W:H?
            double targetWH = slotWidth / courseHeight;
            double itemWH = it.Width / Math.Max(it.Height, Eps);
            double widthFit = 1.0 - Math.Abs(it.Width - slotWidth) / Math.Max(slotWidth, it.Width);
            double depthFit = 1.0 - Math.Abs(it.Depth - wallThickness) / Math.Max(wallThickness, it.Depth);
            double heightFit = 1.0 - Math.Abs(it.Height - courseHeight) / Math.Max(courseHeight, it.Height);
            double aspectFit = 1.0 - Math.Abs(itemWH - targetWH) / Math.Max(targetWH, itemWH);
            double score = 0.4 * widthFit + 0.2 * depthFit + 0.2 * heightFit + 0.2 * aspectFit;

            if (score > bestScore)
            {
                bestScore = score;
                bestIdx = i;
            }
        }
        return bestIdx;
    }

    private static double ComputeAverageWidth(List<InventoryItem> items)
    {
        if (items.Count == 0) return 1.0;
        double sum = 0.0;
        for (int i = 0; i < items.Count; i++) sum += items[i].Width;
        return sum / items.Count;
    }

    private static List<MasonryBlock> BuildBlocks(List<PlacedItem> placed, AshlarPackOptions options)
    {
        var result = new List<MasonryBlock>(placed.Count);
        for (int i = 0; i < placed.Count; i++)
        {
            var p = placed[i];
            var src = p.Source;
            int n = src.VertexCount;
            var verts = new double[n * 3];
            ComputeAabb(src, out double xMin, out double yMin, out double zMin, out _, out _, out _);
            double dx = p.OriginX - xMin, dy = p.OriginY - yMin, dz = p.OriginZ - zMin;
            for (int k = 0; k < n; k++)
            {
                verts[3 * k + 0] = src.VertexCoordsXyz[3 * k + 0] + dx;
                verts[3 * k + 1] = src.VertexCoordsXyz[3 * k + 1] + dy;
                verts[3 * k + 2] = src.VertexCoordsXyz[3 * k + 2] + dz;
            }
            var translated = new Slab(verts, src.Faces);
            result.Add(translated.ToMasonryBlock(p.BlockId, options.Density));
        }
        return result;
    }

    private static void EmitHeadJointInterfaces(
        List<PlacedItem> placed, AshlarPackOptions options, List<MasonryInterface> sink)
    {
        for (int i = 0; i < placed.Count; i++)
        {
            for (int j = 0; j < placed.Count; j++)
            {
                if (i == j) continue;
                var left = placed[i];
                var right = placed[j];
                if (right.CourseIndex != left.CourseIndex) continue;
                if (right.SlotIndex != left.SlotIndex + 1) continue;
                double gap = right.OriginX - (left.OriginX + left.Width);
                if (gap > options.HeadJoint + 1e-6) continue;
                double yLo = Math.Max(left.OriginY, right.OriginY);
                double yHi = Math.Min(left.OriginY + left.Depth, right.OriginY + right.Depth);
                double zLo = Math.Max(left.OriginZ, right.OriginZ);
                double zHi = Math.Min(left.OriginZ + left.Height, right.OriginZ + right.Height);
                if (yHi - yLo <= Eps || zHi - zLo <= Eps) continue;
                double x = 0.5 * (left.OriginX + left.Width + right.OriginX);
                AddRectInterface(sink, left.BlockId, right.BlockId,
                    x, yLo, zLo, x, yHi, zLo, x, yHi, zHi, x, yLo, zHi,
                    +1, 0, 0, 0, 1, 0, 0, 0, 1);
            }
        }
    }

    private static void EmitBedJointInterfaces(
        List<PlacedItem> placed, AshlarPackOptions options, List<MasonryInterface> sink)
    {
        for (int i = 0; i < placed.Count; i++)
        {
            for (int j = 0; j < placed.Count; j++)
            {
                if (i == j) continue;
                var lower = placed[i];
                var upper = placed[j];
                if (upper.CourseIndex != lower.CourseIndex + 1) continue;
                double gap = upper.OriginZ - (lower.OriginZ + lower.Height);
                if (gap > options.BedJoint + 1e-6) continue;
                double xLo = Math.Max(lower.OriginX, upper.OriginX);
                double xHi = Math.Min(lower.OriginX + lower.Width, upper.OriginX + upper.Width);
                double yLo = Math.Max(lower.OriginY, upper.OriginY);
                double yHi = Math.Min(lower.OriginY + lower.Depth, upper.OriginY + upper.Depth);
                if (xHi - xLo <= Eps || yHi - yLo <= Eps) continue;
                double z = 0.5 * (lower.OriginZ + lower.Height + upper.OriginZ);
                AddRectInterface(sink, lower.BlockId, upper.BlockId,
                    xLo, yLo, z, xHi, yLo, z, xHi, yHi, z, xLo, yHi, z,
                    0, 0, 1, 1, 0, 0, 0, 1, 0);
            }
        }
    }

    private static void AddRectInterface(
        List<MasonryInterface> sink, string a, string b,
        double v0x, double v0y, double v0z, double v1x, double v1y, double v1z,
        double v2x, double v2y, double v2z, double v3x, double v3y, double v3z,
        double nx, double ny, double nz,
        double t1x, double t1y, double t1z, double t2x, double t2y, double t2z)
    {
        var poly = new ContactVertex[]
        {
            new ContactVertex(v0x, v0y, v0z),
            new ContactVertex(v1x, v1y, v1z),
            new ContactVertex(v2x, v2y, v2z),
            new ContactVertex(v3x, v3y, v3z),
        };
        sink.Add(new MasonryInterface(a, b, poly, nx, ny, nz, t1x, t1y, t1z, t2x, t2y, t2z));
    }

    private static BoundaryConditions BuildBoundaryConditions(List<PlacedItem> placed)
    {
        var fixedIds = new List<string>(8);
        for (int i = 0; i < placed.Count; i++)
            if (placed[i].CourseIndex == 0) fixedIds.Add(placed[i].BlockId);
        return new BoundaryConditions(fixedIds);
    }

    private static List<Slab> CollectLeftovers(
        IReadOnlyList<Slab> originals, HashSet<int> consumed, List<InventoryItem> finalRemaining)
    {
        var leftovers = new List<Slab>(originals.Count);
        for (int i = 0; i < originals.Count; i++)
        {
            if (!consumed.Contains(i)) leftovers.Add(originals[i]);
        }
        for (int i = 0; i < finalRemaining.Count; i++)
        {
            if (finalRemaining[i].OriginalIndex < 0) leftovers.Add(finalRemaining[i].Slab);
        }
        return leftovers;
    }

    private static int CountCourses(List<PlacedItem> placed)
    {
        if (placed.Count == 0) return 0;
        int max = -1;
        for (int i = 0; i < placed.Count; i++) if (placed[i].CourseIndex > max) max = placed[i].CourseIndex;
        return max + 1;
    }

    private static double ComputeCoverage(List<PlacedItem> placed, AshlarPackOptions options)
    {
        double area = 0.0;
        for (int i = 0; i < placed.Count; i++) area += placed[i].Width * placed[i].Height;
        double wallArea = options.WallWidth * options.WallHeight;
        if (wallArea <= 0) return 0.0;
        double cov = area / wallArea;
        return cov < 0 ? 0 : (cov > 1 ? 1 : cov);
    }

    private static void AssertPlacementsInBounds(List<PlacedItem> placed, AshlarPackOptions options)
    {
        double tol = 1e-6;
        for (int i = 0; i < placed.Count; i++)
        {
            var p = placed[i];
            if (p.OriginX < -tol || p.OriginX + p.Width > options.WallWidth + tol)
                throw new InvalidOperationException($"placed[{i}] X out of bounds");
            if (p.OriginZ < -tol || p.OriginZ + p.Height > options.WallHeight + tol)
                throw new InvalidOperationException($"placed[{i}] Z out of bounds");
        }
    }

    private static void ComputeAabb(Slab s,
        out double xMin, out double yMin, out double zMin,
        out double xMax, out double yMax, out double zMax)
    {
        xMin = yMin = zMin = double.PositiveInfinity;
        xMax = yMax = zMax = double.NegativeInfinity;
        var v = s.VertexCoordsXyz;
        int n = s.VertexCount;
        for (int i = 0; i < n; i++)
        {
            double x = v[3 * i + 0], y = v[3 * i + 1], z = v[3 * i + 2];
            if (x < xMin) xMin = x; if (x > xMax) xMax = x;
            if (y < yMin) yMin = y; if (y > yMax) yMax = y;
            if (z < zMin) zMin = z; if (z > zMax) zMax = z;
        }
    }
}
