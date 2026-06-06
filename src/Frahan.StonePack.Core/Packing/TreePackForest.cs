#nullable disable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rhino.Geometry;

namespace Frahan.Core.Packing;

// =============================================================================
// TreePackForest — Frahan port of Kim 2025 (Computation 13:211, CC BY 4.0)
// tree/forest guillotine packer. Synthesis in
// wiki/papers/kim2025_tree_packing.md.
//
// Algorithm in one paragraph: f randomised forests, each forest grows one
// tree per container, each tree's leaf nodes are candidate placement
// corners. An element placed at a leaf splits the slab paired with that
// leaf into three axis-aligned sub-slabs (guillotine cut along the
// element's three free faces). The 6 split orderings are chosen
// randomly. The forest's score combines packed-element values with a
// container-price minimisation bonus when all elements fit.
//
// Going beyond Kim 2025:
//   - Deterministic master seed (paper uses random() internally).
//   - Saw kerf width subtracted from each cut.
//   - Per-container Forbidden Boxes (e.g. fracture-intersected cells
//     from HeteroExt) — elements placed into a slab cannot intersect
//     these regions. Closes Kim §8.2 (fracture-aware containers).
//
// Deferred to a future K2 batch: cutting-surface (Jalalian I11) cost
// in the score, parallel-forest orchestration, memory-budget cap,
// async via GH_TaskCapableComponent.
// =============================================================================

public static class TreePackForest
{
    /// <summary>
    /// Pack <paramref name="elements"/> into <paramref name="containers"/>
    /// using the Kim 2025 tree/forest method. Returns the best-scoring
    /// forest's placements.
    /// </summary>
    /// <param name="elements">Element AABBs (sculpture / final-piece
    /// bounding boxes). All boxes are interpreted as axis-aligned in
    /// their <see cref="Box.Plane"/>; only the X/Y/Z intervals are used
    /// for the fitting test (the plane is preserved into the output
    /// transform).</param>
    /// <param name="elementValues">Per-element value (e.g. piece price).
    /// Must match the element count.</param>
    /// <param name="containers">Container AABBs (stone-block bounding
    /// boxes).</param>
    /// <param name="containerPrices">Per-container price (e.g. stone-
    /// block material cost). Must match the container count.</param>
    /// <param name="options">Knobs: forest count, seed, rotation mode,
    /// kerf width.</param>
    /// <param name="forbiddenBoxesPerContainer">Optional list of
    /// forbidden boxes per container. If supplied, must have one entry
    /// (possibly empty) per container. Placements that intersect any
    /// forbidden box in their container are rejected.</param>
    public static GuillotinePackResult Pack(
        IReadOnlyList<Box> elements,
        IReadOnlyList<double> elementValues,
        IReadOnlyList<Box> containers,
        IReadOnlyList<double> containerPrices,
        GuillotinePackOptions options,
        IReadOnlyList<IReadOnlyList<Box>> forbiddenBoxesPerContainer = null)
    {
        if (elements == null) throw new ArgumentNullException(nameof(elements));
        if (elementValues == null) throw new ArgumentNullException(nameof(elementValues));
        if (containers == null) throw new ArgumentNullException(nameof(containers));
        if (containerPrices == null) throw new ArgumentNullException(nameof(containerPrices));
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (elements.Count == 0)
            throw new ArgumentException("Need at least one element.", nameof(elements));
        if (containers.Count == 0)
            throw new ArgumentException("Need at least one container.", nameof(containers));
        if (elementValues.Count != elements.Count)
            throw new ArgumentException(
                $"elementValues count ({elementValues.Count}) must match elements count ({elements.Count}).",
                nameof(elementValues));
        if (containerPrices.Count != containers.Count)
            throw new ArgumentException(
                $"containerPrices count ({containerPrices.Count}) must match containers count ({containers.Count}).",
                nameof(containerPrices));
        if (forbiddenBoxesPerContainer != null
            && forbiddenBoxesPerContainer.Count != containers.Count)
            throw new ArgumentException(
                "forbiddenBoxesPerContainer must have one entry per container (may be empty).",
                nameof(forbiddenBoxesPerContainer));
        for (int i = 0; i < elementValues.Count; i++)
            if (elementValues[i] < 0.0 || double.IsNaN(elementValues[i]))
                throw new ArgumentException($"elementValues[{i}] must be a non-negative finite number.",
                    nameof(elementValues));
        for (int i = 0; i < containerPrices.Count; i++)
            if (containerPrices[i] < 0.0 || double.IsNaN(containerPrices[i]))
                throw new ArgumentException($"containerPrices[{i}] must be a non-negative finite number.",
                    nameof(containerPrices));

        // Normalise inputs to internal Box3 representation.
        var elementSizes = new Size3[elements.Count];
        for (int i = 0; i < elements.Count; i++) elementSizes[i] = SizeOf(elements[i]);
        var containerSizes = new Size3[containers.Count];
        for (int i = 0; i < containers.Count; i++) containerSizes[i] = SizeOf(containers[i]);
        var forbiddenLocal = new List<Aabb>[containers.Count];
        if (forbiddenBoxesPerContainer != null)
        {
            for (int c = 0; c < containers.Count; c++)
            {
                var fbs = forbiddenBoxesPerContainer[c];
                forbiddenLocal[c] = new List<Aabb>(fbs?.Count ?? 0);
                if (fbs == null) continue;
                var containerOrigin = ContainerOrigin(containers[c]);
                foreach (var fb in fbs)
                {
                    // Forbidden boxes are in world coords; convert to slab-
                    // local coords (i.e. relative to the container origin
                    // since slabs share the container's frame).
                    var fbOrigin = ContainerOrigin(fb);
                    var fbSize = SizeOf(fb);
                    var local = new Aabb(
                        fbOrigin.X - containerOrigin.X,
                        fbOrigin.Y - containerOrigin.Y,
                        fbOrigin.Z - containerOrigin.Z,
                        fbSize.Width, fbSize.Depth, fbSize.Height);
                    forbiddenLocal[c].Add(local);
                }
            }
        }
        else
        {
            for (int c = 0; c < containers.Count; c++)
                forbiddenLocal[c] = new List<Aabb>();
        }

        // K2 memory budget. Rough per-forest estimate:
        //   - per-element placement record ≈ 160 bytes (struct + Box + Transform)
        //   - per-forest queue worst-case ≈ 3n leaf-slab pairs × 80 bytes
        //   - rotation array + visited set ≈ 16n bytes
        // Total ≈ 700 * n bytes per forest. Empirical headroom: ×2.
        int forestsToRun = options.ForestCount;
        if (options.MemoryBudgetBytes > 0)
        {
            long perForestBytes = 1400L * Math.Max(elementSizes.Length, 1);
            long maxByBudget = Math.Max(1L, options.MemoryBudgetBytes / perForestBytes);
            if (maxByBudget < forestsToRun) forestsToRun = (int)Math.Min(int.MaxValue, maxByBudget);
        }

        // K2 parallel forest growth. Each forest is independent; the
        // SplitMix64 RNG is seeded with (Seed + k), so parallel results
        // are bitwise identical to serial results regardless of the
        // degree of parallelism.
        var attempts = new GuillotinePackResult[forestsToRun];
        int maxParallel = options.MaxDegreeOfParallelism > 0
            ? options.MaxDegreeOfParallelism
            : Environment.ProcessorCount;
        if (maxParallel == 1)
        {
            for (int k = 0; k < forestsToRun; k++)
            {
                attempts[k] = GrowOneSerial(k);
            }
        }
        else
        {
            var po = new ParallelOptions { MaxDegreeOfParallelism = maxParallel };
            Parallel.For(0, forestsToRun, po, k => attempts[k] = GrowOneSerial(k));
        }

        GuillotinePackResult best = null;
        foreach (var a in attempts)
            if (best == null || a.Score > best.Score)
                best = a;

        return best;

        GuillotinePackResult GrowOneSerial(int k)
        {
            long forestSeed = unchecked(options.Seed + k);
            var rng = new DeterministicRng(forestSeed);
            return GrowOne(elementSizes, elementValues, containerSizes, containerPrices,
                forbiddenLocal, options, rng, k, containers);
        }
    }

    // ─── one forest ─────────────────────────────────────────────────────

    private static GuillotinePackResult GrowOne(
        Size3[] elementSizes,
        IReadOnlyList<double> elementValues,
        Size3[] containerSizes,
        IReadOnlyList<double> containerPrices,
        List<Aabb>[] forbiddenLocal,
        GuillotinePackOptions options,
        DeterministicRng rng,
        int forestIndex,
        IReadOnlyList<Box> originalContainers)
    {
        int n = elementSizes.Length;
        int m = containerSizes.Length;

        // Shuffle element + container orders for this forest.
        var elementOrder = Range(n);
        Shuffle(elementOrder, rng);
        var containerOrder = Range(m);
        Shuffle(containerOrder, rng);

        // Sticky rotation per element for this forest.
        var rotations = new int[n];
        int rotationOptionCount = options.RotationMode switch
        {
            GuillotineRotationMode.None => 1,
            GuillotineRotationMode.OneAxis => 2,
            GuillotineRotationMode.ThreeAxis => 6,
            _ => 1,
        };
        for (int i = 0; i < n; i++) rotations[i] = rng.NextInt(rotationOptionCount);

        // Initialise leaf-and-slab queue: one root slab per container,
        // in the shuffled container order.
        // Each entry pairs a slab (in container-local coords, origin at the
        // container's min corner) with its corner = origin.
        var Q = new List<LeafSlab>(m + 3 * n);
        for (int q = 0; q < m; q++)
        {
            int c = containerOrder[q];
            Q.Add(new LeafSlab(
                new Aabb(0, 0, 0, containerSizes[c].Width, containerSizes[c].Depth, containerSizes[c].Height),
                c));
        }

        var placements = new List<GuillotinePlacement>(n);
        var packedValueSum = 0.0;
        var packedElementIdx = new HashSet<int>();
        // K2 / Jalalian I11 — accumulate the cut-surface area introduced
        // by each placement. The three element faces opposite to the
        // placement corner abut freshly cut slab boundaries (or, when the
        // slab spans the container, the original block exterior — which
        // is also a cut surface from the quarry's perspective). Summing
        // these areas gives the total "new" cut area produced by the
        // packing layout.
        double cutSurfaceArea = 0.0;

        foreach (int ei in elementOrder)
        {
            for (int j = 0; j < Q.Count; j++)
            {
                var leafSlab = Q[j];
                var rotated = ApplyAxisRotation(elementSizes[ei], rotations[ei]);
                if (rotated.Width > leafSlab.Slab.W
                    || rotated.Depth > leafSlab.Slab.D
                    || rotated.Height > leafSlab.Slab.H)
                    continue;

                // Forbidden-box overlap check (extension beyond Kim 2025).
                if (IntersectsAnyForbidden(leafSlab.Slab, rotated, forbiddenLocal[leafSlab.ContainerIndex]))
                    continue;

                // Place. Choose one of the 6 axis-orderings for the split.
                int orderIdx = rng.NextInt(6);
                var (sA, sB, sC) = SplitSlab(leafSlab.Slab, rotated, options.KerfWidth, orderIdx);

                // Record the world-frame placement (translate by container origin).
                var containerWorldOrigin = ContainerOrigin(originalContainers[leafSlab.ContainerIndex]);
                var placedOrigin = new Point3d(
                    containerWorldOrigin.X + leafSlab.Slab.X,
                    containerWorldOrigin.Y + leafSlab.Slab.Y,
                    containerWorldOrigin.Z + leafSlab.Slab.Z);
                var placedBox = BuildBox(placedOrigin, rotated);
                var transform = BuildTransform(elementSizes[ei], rotations[ei], placedOrigin);
                placements.Add(new GuillotinePlacement(ei, leafSlab.ContainerIndex, placedBox, transform));
                packedValueSum += elementValues[ei];
                packedElementIdx.Add(ei);
                // Three faces opposite the placement corner. Areas:
                //   x-face (perpendicular to X): depth × height
                //   y-face: width × height
                //   z-face: width × depth
                cutSurfaceArea += rotated.Depth * rotated.Height
                               + rotated.Width * rotated.Height
                               + rotated.Width * rotated.Depth;

                // Replace Q[j] with the 3 new (leaf, slab) pairs, shuffled, at the front.
                Q.RemoveAt(j);
                var triple = new[]
                {
                    new LeafSlab(sA, leafSlab.ContainerIndex),
                    new LeafSlab(sB, leafSlab.ContainerIndex),
                    new LeafSlab(sC, leafSlab.ContainerIndex),
                };
                Shuffle(triple, rng);
                // Filter out collapsed slabs (any dimension <= 0 after kerf).
                int insertAt = 0;
                foreach (var t in triple)
                {
                    if (t.Slab.W > 0 && t.Slab.D > 0 && t.Slab.H > 0)
                        Q.Insert(insertAt++, t);
                }
                break; // placed this element; on to the next
            }
        }

        // Score (Kim 2025 §2.4) — optionally augmented by the Jalalian
        // I11 cut-surface-area penalty (K2 extension).
        var used = new SortedSet<int>();
        foreach (var p in placements) used.Add(p.ContainerIndex);
        double score;
        bool allPacked = packedElementIdx.Count == n;
        if (!allPacked)
        {
            score = packedValueSum;
        }
        else
        {
            double containerPrice = 0;
            foreach (int c in used) containerPrice += containerPrices[c];
            // φ(x) = 1 / (1 + |x|), bounded in (0, 1].
            score = packedValueSum + 1.0 / (1.0 + containerPrice);
        }
        if (options.CutSurfaceWeight > 0.0)
        {
            score -= options.CutSurfaceWeight * cutSurfaceArea;
        }

        return new GuillotinePackResult(
            bestForestIndex: forestIndex,
            score: score,
            placements: placements,
            usedContainerIndices: new List<int>(used),
            allElementsPacked: allPacked,
            forestsRun: -1, // patched up by caller
            seedUsed: unchecked(options.Seed));
    }

    // ─── geometry helpers ─────────────────────────────────────────────────

    /// <summary>Internal slab representation: origin in container-local
    /// coords + size.</summary>
    private readonly struct Aabb
    {
        public Aabb(double x, double y, double z, double w, double d, double h)
        { X = x; Y = y; Z = z; W = w; D = d; H = h; }
        public double X { get; } public double Y { get; } public double Z { get; }
        public double W { get; } public double D { get; } public double H { get; }
    }

    private readonly struct LeafSlab
    {
        public LeafSlab(Aabb slab, int containerIndex) { Slab = slab; ContainerIndex = containerIndex; }
        public Aabb Slab { get; }
        public int ContainerIndex { get; }
    }

    private static Size3 SizeOf(Box b)
    {
        // Box.X/Y/Z intervals: Length = max - min. Always positive after
        // MakeValid; we don't enforce here, but a negative interval would
        // make the fitting test trivially fail anyway.
        return new Size3(
            Math.Abs(b.X.Length),
            Math.Abs(b.Y.Length),
            Math.Abs(b.Z.Length));
    }

    private static Point3d ContainerOrigin(Box b)
    {
        // The container's world-frame min corner. We treat the Box's
        // X/Y/Z intervals as starting at b.X.Min, b.Y.Min, b.Z.Min in
        // the box's Plane frame. For axis-aligned boxes (WorldXY plane)
        // this is the world-frame min corner.
        var plane = b.Plane;
        return plane.PointAt(b.X.Min, b.Y.Min, b.Z.Min);
    }

    private static Size3 ApplyAxisRotation(Size3 s, int rotationIdx)
    {
        // 0 → identity, 1-5 → distinct 90° axis-aligned rotations giving
        // permutations of (W, D, H).
        return rotationIdx switch
        {
            0 => s,
            1 => new Size3(s.Width, s.Height, s.Depth),  // Rx
            2 => new Size3(s.Height, s.Depth, s.Width),  // Ry
            3 => new Size3(s.Depth, s.Width, s.Height),  // Rz
            4 => new Size3(s.Height, s.Width, s.Depth),  // Rx then Rz
            5 => new Size3(s.Depth, s.Height, s.Width),  // Ry then Rz
            _ => s,
        };
    }

    private static (Aabb, Aabb, Aabb) SplitSlab(Aabb s, Size3 elt, double kerf, int orderIdx)
    {
        // s: outer slab; elt: element placed at s's min corner; kerf:
        // saw kerf width subtracted from each cut. orderIdx selects one
        // of 6 axis-orderings (XYZ, XZY, YXZ, YZX, ZXY, ZYX).
        double w = elt.Width, d = elt.Depth, h = elt.Height;
        double W = s.W,        D = s.D,       H = s.H;
        double x0 = s.X,       y0 = s.Y,      z0 = s.Z;
        double k = kerf;

        switch (orderIdx)
        {
            case 0: // X → Y → Z
                return (
                    new Aabb(x0 + w + k, y0,         z0,         W - w - k, D,         H),
                    new Aabb(x0,         y0 + d + k, z0,         w,         D - d - k, H),
                    new Aabb(x0,         y0,         z0 + h + k, w,         d,         H - h - k));
            case 1: // X → Z → Y
                return (
                    new Aabb(x0 + w + k, y0,         z0,         W - w - k, D,         H),
                    new Aabb(x0,         y0,         z0 + h + k, w,         D,         H - h - k),
                    new Aabb(x0,         y0 + d + k, z0,         w,         D - d - k, h));
            case 2: // Y → X → Z
                return (
                    new Aabb(x0,         y0 + d + k, z0,         W,         D - d - k, H),
                    new Aabb(x0 + w + k, y0,         z0,         W - w - k, d,         H),
                    new Aabb(x0,         y0,         z0 + h + k, w,         d,         H - h - k));
            case 3: // Y → Z → X
                return (
                    new Aabb(x0,         y0 + d + k, z0,         W,         D - d - k, H),
                    new Aabb(x0,         y0,         z0 + h + k, W,         d,         H - h - k),
                    new Aabb(x0 + w + k, y0,         z0,         W - w - k, d,         h));
            case 4: // Z → X → Y
                return (
                    new Aabb(x0,         y0,         z0 + h + k, W,         D,         H - h - k),
                    new Aabb(x0 + w + k, y0,         z0,         W - w - k, D,         h),
                    new Aabb(x0,         y0 + d + k, z0,         w,         D - d - k, h));
            default: // 5: Z → Y → X
                return (
                    new Aabb(x0,         y0,         z0 + h + k, W,         D,         H - h - k),
                    new Aabb(x0,         y0 + d + k, z0,         W,         D - d - k, h),
                    new Aabb(x0 + w + k, y0,         z0,         W - w - k, d,         h));
        }
    }

    private static bool IntersectsAnyForbidden(Aabb slab, Size3 elt, List<Aabb> forbidden)
    {
        if (forbidden == null || forbidden.Count == 0) return false;
        // Element occupies (slab.X, slab.Y, slab.Z) to (slab.X+w, slab.Y+d, slab.Z+h).
        double ex0 = slab.X, ex1 = slab.X + elt.Width;
        double ey0 = slab.Y, ey1 = slab.Y + elt.Depth;
        double ez0 = slab.Z, ez1 = slab.Z + elt.Height;
        for (int i = 0; i < forbidden.Count; i++)
        {
            var f = forbidden[i];
            double fx1 = f.X + f.W, fy1 = f.Y + f.D, fz1 = f.Z + f.H;
            // Overlap test on each axis. Boxes that only touch on a face
            // (zero-overlap interval) are NOT counted as intersecting.
            if (ex0 < fx1 && ex1 > f.X
                && ey0 < fy1 && ey1 > f.Y
                && ez0 < fz1 && ez1 > f.Z)
                return true;
        }
        return false;
    }

    private static Box BuildBox(Point3d origin, Size3 size)
    {
        var plane = new Plane(origin, Vector3d.XAxis, Vector3d.YAxis);
        return new Box(plane,
            new Interval(0, size.Width),
            new Interval(0, size.Depth),
            new Interval(0, size.Height));
    }

    private static Transform BuildTransform(Size3 sourceSize, int rotationIdx, Point3d placedOrigin)
    {
        // Apply the rotation around the world origin, then translate by
        // (placedOrigin - rotatedMinCorner). The rotated min corner is
        // computed by transforming the source AABB's 8 corners.
        var rot = RotationMatrix(rotationIdx);
        // Source AABB at world origin spans (0,0,0) → (W, D, H).
        // Compute rotated AABB min corner.
        var corners = new Point3d[]
        {
            new Point3d(0, 0, 0),
            new Point3d(sourceSize.Width, 0, 0),
            new Point3d(0, sourceSize.Depth, 0),
            new Point3d(sourceSize.Width, sourceSize.Depth, 0),
            new Point3d(0, 0, sourceSize.Height),
            new Point3d(sourceSize.Width, 0, sourceSize.Height),
            new Point3d(0, sourceSize.Depth, sourceSize.Height),
            new Point3d(sourceSize.Width, sourceSize.Depth, sourceSize.Height),
        };
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        for (int i = 0; i < corners.Length; i++)
        {
            corners[i].Transform(rot);
            if (corners[i].X < minX) minX = corners[i].X;
            if (corners[i].Y < minY) minY = corners[i].Y;
            if (corners[i].Z < minZ) minZ = corners[i].Z;
        }
        var translate = Transform.Translation(
            placedOrigin.X - minX,
            placedOrigin.Y - minY,
            placedOrigin.Z - minZ);
        return translate * rot;
    }

    private static Transform RotationMatrix(int rotationIdx)
    {
        // Identity-aligned 90° rotations. Each gives a permutation of the
        // AABB dimensions matching ApplyAxisRotation.
        const double pi2 = Math.PI / 2.0;
        return rotationIdx switch
        {
            0 => Transform.Identity,
            1 => Transform.Rotation(pi2, Vector3d.XAxis, Point3d.Origin),
            2 => Transform.Rotation(pi2, Vector3d.YAxis, Point3d.Origin),
            3 => Transform.Rotation(pi2, Vector3d.ZAxis, Point3d.Origin),
            4 => Transform.Rotation(pi2, Vector3d.ZAxis, Point3d.Origin)
               * Transform.Rotation(pi2, Vector3d.XAxis, Point3d.Origin),
            5 => Transform.Rotation(pi2, Vector3d.ZAxis, Point3d.Origin)
               * Transform.Rotation(pi2, Vector3d.YAxis, Point3d.Origin),
            _ => Transform.Identity,
        };
    }

    // ─── RNG and helpers ──────────────────────────────────────────────────

    /// <summary>SplitMix64-style PRNG. Deterministic + fast. We don't
    /// need cryptographic quality; we need reproducibility.</summary>
    private sealed class DeterministicRng
    {
        private ulong _state;
        public DeterministicRng(long seed) { _state = unchecked((ulong)seed) | 1UL; }
        public ulong NextUInt64()
        {
            // SplitMix64 step.
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
        public int NextInt(int exclusiveUpperBound)
        {
            if (exclusiveUpperBound <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveUpperBound));
            return (int)(NextUInt64() % (uint)exclusiveUpperBound);
        }
    }

    private static int[] Range(int n)
    {
        var a = new int[n];
        for (int i = 0; i < n; i++) a[i] = i;
        return a;
    }

    private static void Shuffle<T>(IList<T> arr, DeterministicRng rng)
    {
        // Fisher-Yates.
        for (int i = arr.Count - 1; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            var tmp = arr[i]; arr[i] = arr[j]; arr[j] = tmp;
        }
    }
}
