#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.DataModel;

namespace Frahan.Masonry.Packing;

// =============================================================================
// AshlarLayoutEngine — lay convex Slabs into a coursed-ashlar wall, emit
// MasonryInterfaces from the layout, and compose a MasonryAssembly that the
// downstream RBE solver can consume directly.
//
// Power-of-10 hardened (Holzmann, NASA/JPL). The rules adapted to managed C#:
//   1. simple control flow                — no goto, no recursion
//   2. fixed loop bounds                  — every while carries a guard
//   3. no surprise allocation             — Lists pre-sized; no LINQ in hot path
//   4. function size <= 60 lines          — orchestrator + small helpers
//   5. >= 2 assertions per function       — entry checks + invariant checks
//   6. smallest possible scope            — static class, static helpers
//   7. validate parameters at entry       — every helper guards inputs
//   8. no #define / #if                   — none here
//   9. no pointers / unsafe               — managed C# only
//  10. compile clean                      — zero new warnings
//
// Frame:  +X = wall width, +Y = wall thickness (depth), +Z = wall height (up).
// Slabs are placed AABB-first; their natural Z-extent is the course height.
// Translation only; rotation is deferred (see plan, "deferral list").
// =============================================================================

/// <summary>
/// Static layout engine. Each <see cref="Pack"/> call is independent; no
/// hidden state.
/// </summary>
public static class AshlarLayoutEngine
{
    // Hard caps for the loop guards. Power-of-10 rule 2.
    private const int HardCourseLimit = 10000;
    private const int HardSlotLimit = 10000;
    private const double Eps = 1e-9;

    /// <summary>
    /// Lay <paramref name="slabs"/> into the wall described by
    /// <paramref name="options"/>. Returns the assembly + diagnostics.
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
        var consumedOriginals = new HashSet<int>();

        var placed = LayCourses(filtered, options, notes, consumedOriginals,
                                out List<InventoryItem> finalRemaining);
        AssertPlacementsInBounds(placed, options);

        var blocks = BuildBlocks(placed, options);
        var interfaces = new List<MasonryInterface>(EstimateInterfaceCount(placed));
        EmitHeadJointInterfaces(placed, options, interfaces);
        EmitBedJointInterfaces(placed, options, interfaces);

        var bc = BuildBoundaryConditions(placed);
        var assembly = new MasonryAssembly(blocks, interfaces, bc);

        var leftovers = CollectLeftovers(slabs, consumedOriginals, finalRemaining);
        int courseCount = CountCourses(placed);
        double coverage = ComputeCoverage(placed, options);
        return new AshlarPackResult(assembly, blocks, leftovers, courseCount, coverage, notes);
    }

    // ─── Helper structures ───────────────────────────────────────────────────

    private struct InventoryItem
    {
        public Slab Slab;
        public int OriginalIndex;
        public double XMin, YMin, ZMin;
        public double Width, Height, Depth;
    }

    // ─── ComputeInventory ────────────────────────────────────────────────────

    private static List<InventoryItem> ComputeInventory(IReadOnlyList<Slab> slabs)
    {
        if (slabs == null) throw new ArgumentNullException(nameof(slabs));

        var result = new List<InventoryItem>(slabs.Count);
        for (int i = 0; i < slabs.Count; i++)
        {
            Slab s = slabs[i];
            if (s == null)
                throw new ArgumentException($"slabs[{i}] is null", nameof(slabs));

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
            if (!(item.Width > 0.0 && item.Height > 0.0 && item.Depth > 0.0))
                throw new ArgumentException(
                    $"slabs[{i}] has degenerate AABB ({item.Width}, {item.Depth}, {item.Height})",
                    nameof(slabs));

            result.Add(item);
        }
        if (result.Count != slabs.Count)
            throw new InvalidOperationException("inventory size mismatch");
        return result;
    }

    private static void ComputeAabb(
        Slab s,
        out double xMin, out double yMin, out double zMin,
        out double xMax, out double yMax, out double zMax)
    {
        if (s == null) throw new ArgumentNullException(nameof(s));
        if (s.VertexCount < 1)
            throw new ArgumentException("slab has no vertices", nameof(s));

        var v = s.VertexCoordsXyz;
        xMin = double.PositiveInfinity; yMin = double.PositiveInfinity; zMin = double.PositiveInfinity;
        xMax = double.NegativeInfinity; yMax = double.NegativeInfinity; zMax = double.NegativeInfinity;
        int n = s.VertexCount;
        for (int i = 0; i < n; i++)
        {
            double x = v[3 * i + 0];
            double y = v[3 * i + 1];
            double z = v[3 * i + 2];
            if (x < xMin) xMin = x; if (x > xMax) xMax = x;
            if (y < yMin) yMin = y; if (y > yMax) yMax = y;
            if (z < zMin) zMin = z; if (z > zMax) zMax = z;
        }
        if (!(xMax >= xMin && yMax >= yMin && zMax >= zMin))
            throw new InvalidOperationException("slab AABB inverted");
    }

    // ─── FilterByMode ────────────────────────────────────────────────────────

    private static List<InventoryItem> FilterByMode(
        List<InventoryItem> inventory,
        AshlarPackOptions options)
    {
        if (inventory == null) throw new ArgumentNullException(nameof(inventory));
        if (options == null) throw new ArgumentNullException(nameof(options));

        var result = new List<InventoryItem>(inventory.Count);
        switch (options.Mode)
        {
            case CourseMode.CoursedAshlar:
                for (int i = 0; i < inventory.Count; i++)
                {
                    var it = inventory[i];
                    if (Math.Abs(it.Height - options.TargetCourseHeight) <= options.HeightTolerance + Eps
                        && it.Depth <= options.WallThickness + Eps)
                    {
                        result.Add(it);
                    }
                }
                break;
            case CourseMode.CoursedRubble:
                for (int i = 0; i < inventory.Count; i++)
                {
                    var it = inventory[i];
                    if (it.Depth <= options.WallThickness + Eps)
                        result.Add(it);
                }
                break;
            default:
                throw new NotSupportedException($"unknown CourseMode: {options.Mode}");
        }
        if (result.Count > inventory.Count)
            throw new InvalidOperationException("filter produced more items than input");
        return result;
    }

    // ─── LayCourses ──────────────────────────────────────────────────────────

    private static List<PlacedBlock> LayCourses(
        List<InventoryItem> filtered,
        AshlarPackOptions options,
        List<string> notes,
        HashSet<int> consumedOriginals,
        out List<InventoryItem> finalRemaining)
    {
        if (filtered == null) throw new ArgumentNullException(nameof(filtered));
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (notes == null) throw new ArgumentNullException(nameof(notes));
        if (consumedOriginals == null) throw new ArgumentNullException(nameof(consumedOriginals));

        var placed = new List<PlacedBlock>(filtered.Count);
        var remaining = new List<InventoryItem>(filtered);
        double avgWidth = ComputeAverageWidth(filtered);

        int maxCourses = ComputeMaxCourses(options);
        int maxSlots = Math.Min(HardSlotLimit, filtered.Count + 8 + 64);

        double y = 0.0;
        int courseIndex = 0;

        if (options.Mode == CourseMode.CoursedRubble)
        {
            var rubblePlaced = LayCoursesRubble(filtered, options, notes, maxCourses, maxSlots);
            finalRemaining = remaining;
            // Treat all rubble-mode picks as consuming their originals.
            for (int i = 0; i < rubblePlaced.Count; i++)
            {
                // Find the original by reference and add its index.
                for (int j = 0; j < filtered.Count; j++)
                {
                    if (ReferenceEquals(filtered[j].Slab, rubblePlaced[i].Source))
                    {
                        consumedOriginals.Add(filtered[j].OriginalIndex);
                        break;
                    }
                }
            }
            return rubblePlaced;
        }

        while (y + options.TargetCourseHeight <= options.WallHeight + Eps && courseIndex < maxCourses)
        {
            double startX = (courseIndex % 2 == 1) ? options.StaggerOffset * avgWidth : 0.0;
            int slotsBefore = placed.Count;
            LayOneCourse(remaining, options, courseIndex, options.TargetCourseHeight,
                         y, startX, maxSlots, placed, notes, consumedOriginals);
            y += options.TargetCourseHeight + options.BedJoint;
            courseIndex += 1;
            if (placed.Count - slotsBefore < 0)
                throw new InvalidOperationException("course slot count went negative");
        }

        if (courseIndex >= maxCourses && y + options.TargetCourseHeight <= options.WallHeight + Eps)
            throw new InvalidOperationException("loop guard tripped");

        finalRemaining = remaining;
        return placed;
    }

    private static int ComputeMaxCourses(AshlarPackOptions options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (!(options.TargetCourseHeight > 0.0))
            throw new ArgumentException("TargetCourseHeight must be > 0", nameof(options));

        double raw = Math.Ceiling(options.WallHeight / options.TargetCourseHeight) + 8.0;
        int capped = raw > HardCourseLimit ? HardCourseLimit : (int)raw;
        if (capped < 1)
            throw new InvalidOperationException("maxCourses computed < 1");
        return capped;
    }

    private static double ComputeAverageWidth(List<InventoryItem> filtered)
    {
        if (filtered == null) throw new ArgumentNullException(nameof(filtered));
        if (filtered.Count == 0) return 1.0;

        double sum = 0.0;
        for (int i = 0; i < filtered.Count; i++) sum += filtered[i].Width;
        double avg = sum / filtered.Count;
        if (!(avg > 0.0))
            throw new InvalidOperationException("average width <= 0");
        return avg;
    }

    private static void LayOneCourse(
        List<InventoryItem> remaining,
        AshlarPackOptions options,
        int courseIndex,
        double courseHeight,
        double y,
        double startX,
        int maxSlots,
        List<PlacedBlock> placed,
        List<string> notes,
        HashSet<int> consumedOriginals)
    {
        if (remaining == null) throw new ArgumentNullException(nameof(remaining));
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (placed == null) throw new ArgumentNullException(nameof(placed));
        if (notes == null) throw new ArgumentNullException(nameof(notes));
        if (consumedOriginals == null) throw new ArgumentNullException(nameof(consumedOriginals));
        if (courseIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(courseIndex));

        double x = startX;
        int slot = 0;

        while (x < options.WallWidth - Eps && slot < maxSlots)
        {
            double gap = options.WallWidth - x;
            // Try natural-orientation fit first.
            int pickIdx = FindFirstFitting(remaining, gap, options.WallThickness);
            bool rotated = false;
            // Fall back to a 90° yaw if allowed.
            if (pickIdx < 0 && options.AllowYaw)
            {
                pickIdx = FindFirstFittingYawed(remaining, gap, options.WallThickness);
                if (pickIdx >= 0) rotated = true;
            }
            if (pickIdx < 0)
            {
                if (options.AllowTrim &&
                    TryTrimToFit(remaining, gap, courseHeight, options.WallThickness,
                                 options.HeightTolerance, consumedOriginals,
                                 out var trimmedItem, out var offcutItem))
                {
                    PlaceItem(options, trimmedItem, x, y,
                              courseIndex, slot, courseHeight, false, placed);
                    if (offcutItem.HasValue) remaining.Add(offcutItem.Value);
                    x += trimmedItem.Width + options.HeadJoint;
                    slot += 1;
                    continue;
                }

                notes.Add($"course {courseIndex} gap from x={x:F3} to {options.WallWidth:F3}");
                break;
            }
            var pick = remaining[pickIdx];
            remaining.RemoveAt(pickIdx);
            if (pick.OriginalIndex >= 0) consumedOriginals.Add(pick.OriginalIndex);

            double effW = rotated ? pick.Depth : pick.Width;
            double effD = rotated ? pick.Width : pick.Depth;
            double placedY = (options.WallThickness - effD) * 0.5;
            string id = $"ashlar_{courseIndex:D3}_{slot:D3}";
            placed.Add(new PlacedBlock(id, pick.Slab, courseIndex, slot,
                                        x, placedY, y,
                                        effW, courseHeight, effD,
                                        rotated));
            x += effW + options.HeadJoint;
            slot += 1;
        }
        if (slot >= maxSlots)
            throw new InvalidOperationException("loop guard tripped");
    }

    private static int FindFirstFittingYawed(
        List<InventoryItem> remaining,
        double maxWidth,
        double wallThickness)
    {
        if (remaining == null) throw new ArgumentNullException(nameof(remaining));
        if (!(maxWidth >= 0.0))
            throw new ArgumentOutOfRangeException(nameof(maxWidth));

        // After yaw 90° around +Z, the X-extent becomes the original Y-extent
        // and the Y-extent becomes the original X-extent.
        for (int i = 0; i < remaining.Count; i++)
        {
            var it = remaining[i];
            if (it.Depth <= maxWidth + Eps && it.Width <= wallThickness + Eps)
                return i;
        }
        return -1;
    }

    private static bool TryTrimToFit(
        List<InventoryItem> remaining,
        double gap,
        double courseHeight,
        double wallThickness,
        double heightTolerance,
        HashSet<int> consumedOriginals,
        out InventoryItem trimmed,
        out InventoryItem? offcut)
    {
        if (remaining == null) throw new ArgumentNullException(nameof(remaining));
        if (consumedOriginals == null) throw new ArgumentNullException(nameof(consumedOriginals));
        if (!(gap > 0.0))
            throw new ArgumentOutOfRangeException(nameof(gap));

        trimmed = default;
        offcut = null;

        // Find the first slab whose width strictly exceeds the gap and whose
        // depth fits the wall thickness; height already matches via the
        // ashlar-mode filter, so we don't re-check it here.
        for (int i = 0; i < remaining.Count; i++)
        {
            var it = remaining[i];
            if (it.Width > gap + Eps && it.Depth <= wallThickness + Eps)
            {
                if (TrimSlab(it, gap, out var pieceItem, out var offcutItem))
                {
                    if (it.OriginalIndex >= 0) consumedOriginals.Add(it.OriginalIndex);
                    remaining.RemoveAt(i);
                    trimmed = pieceItem;
                    offcut = offcutItem;
                    return true;
                }
            }
        }
        return false;
    }

    private static bool TrimSlab(
        InventoryItem source,
        double gap,
        out InventoryItem piece,
        out InventoryItem offcut)
    {
        if (!(gap > 0.0))
            throw new ArgumentOutOfRangeException(nameof(gap));

        piece = default;
        offcut = default;

        // Cut along the X axis of the slab's local frame at `xMin + gap`.
        double cutX = source.XMin + gap;
        var plane = new FracturePlane(cutX, source.YMin, source.ZMin, 1.0, 0.0, 0.0);
        SlabCutResult cut;
        try { cut = SlabCutter.Cut(source.Slab, plane); }
        catch { return false; }
        if (cut.Count != 2) return false;

        // Both pieces are synthesized; mark them with OriginalIndex = -1 so the
        // leftover collector can distinguish them from un-touched originals.
        var pieceCandidates = new InventoryItem[2];
        for (int k = 0; k < 2; k++)
        {
            pieceCandidates[k] = MakeInventoryItem(cut.Slabs[k], originalIndex: -1);
        }

        int leftIdx = (Math.Abs(pieceCandidates[0].XMin + pieceCandidates[0].Width - cutX)
                       < Math.Abs(pieceCandidates[1].XMin + pieceCandidates[1].Width - cutX))
                      ? 0 : 1;
        int rightIdx = 1 - leftIdx;

        // Reject if widths look wrong.
        if (Math.Abs(pieceCandidates[leftIdx].Width - gap) > 1e-6) return false;

        piece = pieceCandidates[leftIdx];
        offcut = pieceCandidates[rightIdx];
        return true;
    }

    private static InventoryItem MakeInventoryItem(Slab s, int originalIndex)
    {
        if (s == null) throw new ArgumentNullException(nameof(s));
        ComputeAabb(s, out double xMin, out double yMin, out double zMin,
                       out double xMax, out double yMax, out double zMax);
        var item = new InventoryItem
        {
            Slab = s,
            OriginalIndex = originalIndex,
            XMin = xMin, YMin = yMin, ZMin = zMin,
            Width = xMax - xMin,
            Height = zMax - zMin,
            Depth = yMax - yMin,
        };
        if (!(item.Width > 0.0 && item.Height > 0.0 && item.Depth > 0.0))
            throw new InvalidOperationException("synthesized inventory item has degenerate AABB");
        return item;
    }

    private static void PlaceItem(
        AshlarPackOptions options,
        InventoryItem item,
        double x, double y,
        int courseIndex, int slot,
        double courseHeight,
        bool rotated,
        List<PlacedBlock> placed)
    {
        if (placed == null) throw new ArgumentNullException(nameof(placed));
        if (options == null) throw new ArgumentNullException(nameof(options));

        double effW = rotated ? item.Depth : item.Width;
        double effD = rotated ? item.Width : item.Depth;
        double placedY = (options.WallThickness - effD) * 0.5;
        string id = $"ashlar_{courseIndex:D3}_{slot:D3}";
        placed.Add(new PlacedBlock(id, item.Slab, courseIndex, slot,
                                    x, placedY, y,
                                    effW, courseHeight, effD,
                                    rotated));
    }

    private static int FindFirstFitting(
        List<InventoryItem> remaining,
        double maxWidth,
        double wallThickness)
    {
        if (remaining == null) throw new ArgumentNullException(nameof(remaining));
        if (!(maxWidth >= 0.0))
            throw new ArgumentOutOfRangeException(nameof(maxWidth));

        for (int i = 0; i < remaining.Count; i++)
        {
            var it = remaining[i];
            if (it.Width <= maxWidth + Eps && it.Depth <= wallThickness + Eps)
                return i;
        }
        return -1;
    }

    // ─── LayCoursesRubble (Stage 2) ──────────────────────────────────────────

    private static List<PlacedBlock> LayCoursesRubble(
        List<InventoryItem> filtered,
        AshlarPackOptions options,
        List<string> notes,
        int maxCourses,
        int maxSlots)
    {
        if (filtered == null) throw new ArgumentNullException(nameof(filtered));
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (notes == null) throw new ArgumentNullException(nameof(notes));

        var bins = BinByHeight(filtered, options.HeightTolerance);
        var placed = new List<PlacedBlock>(filtered.Count);
        // Rubble mode tracks consumed originals locally; the caller in
        // LayCourses replays them onto the outer set after the call.
        var localConsumed = new HashSet<int>();

        double y = 0.0;
        int courseIndex = 0;
        double avgWidth = ComputeAverageWidth(filtered);

        while (courseIndex < maxCourses)
        {
            int binIdx = PickFullestBin(bins);
            if (binIdx < 0) break;

            var bin = bins[binIdx];
            double courseHeight = bin.Count > 0 ? bin[0].Height : options.TargetCourseHeight;
            if (y + courseHeight > options.WallHeight + Eps) break;

            double startX = (courseIndex % 2 == 1) ? options.StaggerOffset * avgWidth : 0.0;
            int slotsBefore = placed.Count;
            LayOneCourse(bin, options, courseIndex, courseHeight, y, startX, maxSlots, placed, notes, localConsumed);

            if (placed.Count == slotsBefore)
            {
                bins.RemoveAt(binIdx);
                continue;
            }

            y += courseHeight + options.BedJoint;
            courseIndex += 1;
        }

        if (courseIndex >= maxCourses)
            throw new InvalidOperationException("loop guard tripped");
        return placed;
    }

    private static List<List<InventoryItem>> BinByHeight(
        List<InventoryItem> items,
        double tolerance)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));
        if (tolerance < 0.0)
            throw new ArgumentOutOfRangeException(nameof(tolerance));

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
        if (bins == null) throw new ArgumentNullException(nameof(bins));
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
        if (bestIdx >= bins.Count)
            throw new InvalidOperationException("PickFullestBin returned out-of-range index");
        return bestIdx;
    }

    // ─── BuildBlocks ─────────────────────────────────────────────────────────

    private static List<MasonryBlock> BuildBlocks(
        List<PlacedBlock> placed,
        AshlarPackOptions options)
    {
        if (placed == null) throw new ArgumentNullException(nameof(placed));
        if (options == null) throw new ArgumentNullException(nameof(options));

        var result = new List<MasonryBlock>(placed.Count);
        for (int i = 0; i < placed.Count; i++)
        {
            var p = placed[i];
            var src = p.Source;
            int n = src.VertexCount;
            var verts = new double[n * 3];
            ComputeAabb(src, out double xMin, out double yMin, out double zMin,
                            out _, out double yMax, out _);
            double srcDepth = yMax - yMin;

            if (!p.Rotated)
            {
                double dx = p.OriginX - xMin;
                double dy = p.OriginY - yMin;
                double dz = p.OriginZ - zMin;
                for (int k = 0; k < n; k++)
                {
                    verts[3 * k + 0] = src.VertexCoordsXyz[3 * k + 0] + dx;
                    verts[3 * k + 1] = src.VertexCoordsXyz[3 * k + 1] + dy;
                    verts[3 * k + 2] = src.VertexCoordsXyz[3 * k + 2] + dz;
                }
            }
            else
            {
                // Yaw 90° CCW around +Z, then translate to placed origin.
                // Step 1: shift to origin so AABB starts at (0, 0, 0).
                // Step 2: (x, y, z) -> (-y, x, z); AABB becomes [-D, 0] x [0, W] x [0, H].
                // Step 3: shift +D in X so AABB is [0, D] x [0, W] x [0, H].
                // Step 4: translate by (OriginX, OriginY, OriginZ).
                for (int k = 0; k < n; k++)
                {
                    double xo = src.VertexCoordsXyz[3 * k + 0] - xMin;
                    double yo = src.VertexCoordsXyz[3 * k + 1] - yMin;
                    double zo = src.VertexCoordsXyz[3 * k + 2] - zMin;
                    double xr = -yo + srcDepth;
                    double yr = xo;
                    verts[3 * k + 0] = xr + p.OriginX;
                    verts[3 * k + 1] = yr + p.OriginY;
                    verts[3 * k + 2] = zo + p.OriginZ;
                }
            }

            var translated = new Slab(verts, src.Faces);
            result.Add(translated.ToMasonryBlock(p.BlockId, options.Density));
        }
        if (result.Count != placed.Count)
            throw new InvalidOperationException("block count mismatch");
        return result;
    }

    private static void AssertPlacementsInBounds(
        List<PlacedBlock> placed,
        AshlarPackOptions options)
    {
        if (placed == null) throw new ArgumentNullException(nameof(placed));
        if (options == null) throw new ArgumentNullException(nameof(options));

        double tol = 1e-6;
        for (int i = 0; i < placed.Count; i++)
        {
            var p = placed[i];
            if (p.OriginX < -tol || p.MaxX > options.WallWidth + tol)
                throw new InvalidOperationException($"placed[{i}] X out of bounds: [{p.OriginX}, {p.MaxX}]");
            if (p.OriginY < -tol || p.MaxY > options.WallThickness + tol)
                throw new InvalidOperationException($"placed[{i}] Y out of bounds: [{p.OriginY}, {p.MaxY}]");
            if (p.OriginZ < -tol || p.MaxZ > options.WallHeight + tol)
                throw new InvalidOperationException($"placed[{i}] Z out of bounds: [{p.OriginZ}, {p.MaxZ}]");
        }
    }

    // ─── EmitHeadJointInterfaces ─────────────────────────────────────────────

    private static void EmitHeadJointInterfaces(
        List<PlacedBlock> placed,
        AshlarPackOptions options,
        List<MasonryInterface> interfaces)
    {
        if (placed == null) throw new ArgumentNullException(nameof(placed));
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (interfaces == null) throw new ArgumentNullException(nameof(interfaces));

        for (int i = 0; i < placed.Count; i++)
        {
            var left = placed[i];
            for (int j = 0; j < placed.Count; j++)
            {
                if (i == j) continue;
                var right = placed[j];
                if (right.CourseIndex != left.CourseIndex) continue;
                if (right.SlotIndex != left.SlotIndex + 1) continue;

                double gap = right.OriginX - left.MaxX;
                if (gap > options.HeadJoint + 1e-6) continue;

                double yLo = Math.Max(left.OriginY, right.OriginY);
                double yHi = Math.Min(left.MaxY, right.MaxY);
                double zLo = Math.Max(left.OriginZ, right.OriginZ);
                double zHi = Math.Min(left.MaxZ, right.MaxZ);
                if (yHi - yLo <= Eps || zHi - zLo <= Eps) continue;

                double x = 0.5 * (left.MaxX + right.OriginX);
                AddRectInterface(interfaces, left.BlockId, right.BlockId,
                                  x, yLo, zLo, x, yHi, zLo, x, yHi, zHi, x, yLo, zHi,
                                  +1, 0, 0,  0, 1, 0,  0, 0, 1);
            }
        }
        if (interfaces.Count < 0)
            throw new InvalidOperationException("interface count went negative");
    }

    // ─── EmitBedJointInterfaces ──────────────────────────────────────────────

    private static void EmitBedJointInterfaces(
        List<PlacedBlock> placed,
        AshlarPackOptions options,
        List<MasonryInterface> interfaces)
    {
        if (placed == null) throw new ArgumentNullException(nameof(placed));
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (interfaces == null) throw new ArgumentNullException(nameof(interfaces));

        for (int i = 0; i < placed.Count; i++)
        {
            var lower = placed[i];
            for (int j = 0; j < placed.Count; j++)
            {
                if (i == j) continue;
                var upper = placed[j];
                if (upper.CourseIndex != lower.CourseIndex + 1) continue;

                double gap = upper.OriginZ - lower.MaxZ;
                if (gap > options.BedJoint + 1e-6) continue;

                double xLo = Math.Max(lower.OriginX, upper.OriginX);
                double xHi = Math.Min(lower.MaxX, upper.MaxX);
                double yLo = Math.Max(lower.OriginY, upper.OriginY);
                double yHi = Math.Min(lower.MaxY, upper.MaxY);
                if (xHi - xLo <= Eps || yHi - yLo <= Eps) continue;

                double z = 0.5 * (lower.MaxZ + upper.OriginZ);
                AddRectInterface(interfaces, lower.BlockId, upper.BlockId,
                                  xLo, yLo, z, xHi, yLo, z, xHi, yHi, z, xLo, yHi, z,
                                  0, 0, 1,  1, 0, 0,  0, 1, 0);
            }
        }
        if (interfaces.Count < 0)
            throw new InvalidOperationException("interface count went negative");
    }

    private static void AddRectInterface(
        List<MasonryInterface> sink,
        string a, string b,
        double v0x, double v0y, double v0z,
        double v1x, double v1y, double v1z,
        double v2x, double v2y, double v2z,
        double v3x, double v3y, double v3z,
        double nx, double ny, double nz,
        double t1x, double t1y, double t1z,
        double t2x, double t2y, double t2z)
    {
        if (sink == null) throw new ArgumentNullException(nameof(sink));
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            throw new ArgumentException("block ids must be non-blank");

        var poly = new ContactVertex[]
        {
            new ContactVertex(v0x, v0y, v0z),
            new ContactVertex(v1x, v1y, v1z),
            new ContactVertex(v2x, v2y, v2z),
            new ContactVertex(v3x, v3y, v3z),
        };
        sink.Add(new MasonryInterface(a, b, poly, nx, ny, nz, t1x, t1y, t1z, t2x, t2y, t2z));
    }

    // ─── BuildBoundaryConditions ─────────────────────────────────────────────

    private static BoundaryConditions BuildBoundaryConditions(List<PlacedBlock> placed)
    {
        if (placed == null) throw new ArgumentNullException(nameof(placed));

        var fixedIds = new List<string>(8);
        for (int i = 0; i < placed.Count; i++)
        {
            if (placed[i].CourseIndex == 0)
                fixedIds.Add(placed[i].BlockId);
        }
        if (fixedIds.Count > placed.Count)
            throw new InvalidOperationException("boundary count exceeds placed count");
        return new BoundaryConditions(fixedIds);
    }

    // ─── CollectLeftovers ────────────────────────────────────────────────────

    private static List<Slab> CollectLeftovers(
        IReadOnlyList<Slab> originals,
        HashSet<int> consumedOriginals,
        List<InventoryItem> finalRemaining)
    {
        if (originals == null) throw new ArgumentNullException(nameof(originals));
        if (consumedOriginals == null) throw new ArgumentNullException(nameof(consumedOriginals));
        if (finalRemaining == null) throw new ArgumentNullException(nameof(finalRemaining));

        var leftovers = new List<Slab>(originals.Count + finalRemaining.Count);
        // Untouched originals (never picked, never trimmed; includes ones
        // filtered out at the start by mode/depth).
        for (int i = 0; i < originals.Count; i++)
        {
            if (!consumedOriginals.Contains(i)) leftovers.Add(originals[i]);
        }
        // Synthesized offcuts that didn't get re-placed.
        for (int i = 0; i < finalRemaining.Count; i++)
        {
            if (finalRemaining[i].OriginalIndex < 0)
                leftovers.Add(finalRemaining[i].Slab);
        }
        if (leftovers.Count < 0)
            throw new InvalidOperationException("leftover count went negative");
        return leftovers;
    }

    // ─── Diagnostics ─────────────────────────────────────────────────────────

    private static int CountCourses(List<PlacedBlock> placed)
    {
        if (placed == null) throw new ArgumentNullException(nameof(placed));
        if (placed.Count == 0) return 0;

        int max = -1;
        for (int i = 0; i < placed.Count; i++)
        {
            int c = placed[i].CourseIndex;
            if (c > max) max = c;
        }
        if (max < 0) throw new InvalidOperationException("course index never set");
        return max + 1;
    }

    private static double ComputeCoverage(List<PlacedBlock> placed, AshlarPackOptions options)
    {
        if (placed == null) throw new ArgumentNullException(nameof(placed));
        if (options == null) throw new ArgumentNullException(nameof(options));

        double area = 0.0;
        for (int i = 0; i < placed.Count; i++)
        {
            area += placed[i].BBoxWidth * placed[i].BBoxHeight;
        }
        double wallArea = options.WallWidth * options.WallHeight;
        if (!(wallArea > 0.0))
            throw new InvalidOperationException("wall area <= 0");
        double cov = area / wallArea;
        if (cov < 0.0) cov = 0.0;
        if (cov > 1.0) cov = 1.0;
        return cov;
    }

    private static int EstimateInterfaceCount(List<PlacedBlock> placed)
    {
        if (placed == null) throw new ArgumentNullException(nameof(placed));
        return placed.Count * 2 + 8;
    }
}
