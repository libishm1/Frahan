#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Quarry.GeoPack;

namespace Frahan.Masonry.Quarry.Monuments;

// =============================================================================
// BenchMonumentPacker -- per-cell SO(3)-aware greedy AABB packer.
//
// Algorithm (one BlockCell):
//   1. Compute the cell's AABB (cellMin, cellMax) and pre-compute the cell's
//      face planes (Slab faces) for the point-in-polyhedron containment test.
//   2. Sort monuments descending by AabbVolume (largest-first is the standard
//      3D bin-packing heuristic).
//   3. For each monument, for each of 24 axis-aligned rotations:
//        a) Compute the rotated AABB extents (dx, dy, dz).
//        b) Skip if any extent exceeds the cell AABB extent.
//        c) Sweep candidate origins on a grid stepping by gridStride along
//           each axis from cellMin to (cellMax - dims). At each origin, check
//           - all 8 corners of (origin, dims) are inside the cell Slab,
//           - no overlap with any already-placed AABB in this cell.
//        d) On the first acceptance, record the MonumentPlacement and skip
//           remaining rotations.
//   4. If no rotation fits anywhere in this cell, monument is left unplaced
//      and may be retried in the next cell by PackBlockGraph.
//
// Across-graph (PackBlockGraph):
//   For each cell in BlockGraph.Cells (in declared order — caller controls
//   sort), call PackInCell with the still-unplaced inventory. Stop when all
//   monuments are placed or all cells exhausted.
//
// Spec: outputs/2026-05-15/connection_map/MONUMENT_PACKING.md.
// =============================================================================

public sealed class BenchMonumentPackerOptions
{
    public BenchMonumentPackerOptions(
        double gridStride = 0.05,
        double containmentEps = 1e-6)
    {
        if (gridStride <= 0) throw new ArgumentOutOfRangeException(nameof(gridStride), "> 0");
        if (containmentEps < 0) throw new ArgumentOutOfRangeException(nameof(containmentEps), ">= 0");
        GridStride = gridStride;
        ContainmentEps = containmentEps;
    }

    /// <summary>Grid step in metres for candidate-origin sweep. Smaller = denser search, slower.</summary>
    public double GridStride { get; }

    /// <summary>Tolerance for the "all 8 corners inside cell" test.</summary>
    public double ContainmentEps { get; }
}

public static class BenchMonumentPacker
{
    public static BenchMonumentPlan PackBlockGraph(
        BlockGraph graph,
        MonumentInventory inventory,
        BenchMonumentPackerOptions options = null)
    {
        if (graph == null) throw new ArgumentNullException(nameof(graph));
        if (inventory == null) throw new ArgumentNullException(nameof(inventory));
        options = options ?? new BenchMonumentPackerOptions();

        // sort inventory descending by AABB volume
        var sorted = new List<Monument>(inventory.Monuments);
        sorted.Sort((a, b) => b.AabbVolume.CompareTo(a.AabbVolume));

        var placements = new List<MonumentPlacement>();
        var remaining = new List<Monument>(sorted);
        double benchAabbVolume = 0.0;

        foreach (var cell in graph.Cells)
        {
            benchAabbVolume += Math.Abs(cell.Geometry.SignedVolume());
            if (remaining.Count == 0) continue;

            var cellPlacements = PackInCellInternal(cell, remaining, options);
            placements.AddRange(cellPlacements);

            // drop placed monuments from the remaining list
            var placedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in cellPlacements) placedIds.Add(p.MonumentId);
            remaining.RemoveAll(m => placedIds.Contains(m.Id));
        }

        var unplacedIds = new List<string>(remaining.Count);
        foreach (var m in remaining) unplacedIds.Add(m.Id);
        return new BenchMonumentPlan(placements, unplacedIds, benchAabbVolume);
    }

    public static IReadOnlyList<MonumentPlacement> PackInCell(
        BlockCell cell,
        IReadOnlyList<Monument> monuments,
        BenchMonumentPackerOptions options = null)
    {
        if (cell == null) throw new ArgumentNullException(nameof(cell));
        if (monuments == null) throw new ArgumentNullException(nameof(monuments));
        options = options ?? new BenchMonumentPackerOptions();

        var sorted = new List<Monument>(monuments);
        sorted.Sort((a, b) => b.AabbVolume.CompareTo(a.AabbVolume));
        return PackInCellInternal(cell, sorted, options);
    }

    // ----- internals ---------------------------------------------------------

    private static IReadOnlyList<MonumentPlacement> PackInCellInternal(
        BlockCell cell,
        List<Monument> sortedMonuments,
        BenchMonumentPackerOptions options)
    {
        ComputeAabb(cell.Geometry,
            out double cellMinX, out double cellMinY, out double cellMinZ,
            out double cellMaxX, out double cellMaxY, out double cellMaxZ);

        // pre-compute face planes for point-in-polyhedron tests
        var faces = BuildOutwardFaces(cell.Geometry);

        var placed = new List<MonumentPlacement>();

        foreach (var m in sortedMonuments)
        {
            if (TryPlace(m, cell.Id, cellMinX, cellMinY, cellMinZ, cellMaxX, cellMaxY, cellMaxZ,
                        faces, placed, options, out var placement))
            {
                placed.Add(placement);
            }
        }
        return placed;
    }

    private static bool TryPlace(
        Monument m, string cellId,
        double cellMinX, double cellMinY, double cellMinZ,
        double cellMaxX, double cellMaxY, double cellMaxZ,
        IReadOnlyList<(double nx, double ny, double nz, double d)> faces,
        List<MonumentPlacement> already,
        BenchMonumentPackerOptions options,
        out MonumentPlacement placement)
    {
        placement = null;
        for (int r = 0; r < MonumentOrientationSampler.Count; r++)
        {
            MonumentOrientationSampler.RotatedAabb(
                r,
                m.AabbMinX, m.AabbMinY, m.AabbMinZ,
                m.AabbMaxX, m.AabbMaxY, m.AabbMaxZ,
                out double dx, out double dy, out double dz);

            if (!(dx > 0 && dy > 0 && dz > 0)) continue;
            if (dx > cellMaxX - cellMinX) continue;
            if (dy > cellMaxY - cellMinY) continue;
            if (dz > cellMaxZ - cellMinZ) continue;

            double stride = options.GridStride;
            double xLo = cellMinX, xHi = cellMaxX - dx;
            double yLo = cellMinY, yHi = cellMaxY - dy;
            double zLo = cellMinZ, zHi = cellMaxZ - dz;

            for (double z = zLo; z <= zHi + 1e-12; z += stride)
            {
                for (double y = yLo; y <= yHi + 1e-12; y += stride)
                {
                    for (double x = xLo; x <= xHi + 1e-12; x += stride)
                    {
                        if (!AabbOverlapsAny(x, y, z, dx, dy, dz, already) &&
                            AllCornersInside(x, y, z, dx, dy, dz, faces, options.ContainmentEps))
                        {
                            placement = new MonumentPlacement(m.Id, cellId, r, x, y, z, dx, dy, dz);
                            return true;
                        }
                    }
                }
            }
        }
        return false;
    }

    private static bool AabbOverlapsAny(
        double x, double y, double z, double dx, double dy, double dz,
        List<MonumentPlacement> already)
    {
        double xMax = x + dx, yMax = y + dy, zMax = z + dz;
        for (int i = 0; i < already.Count; i++)
        {
            var p = already[i];
            if (xMax <= p.OriginX || p.MaxX <= x) continue;
            if (yMax <= p.OriginY || p.MaxY <= y) continue;
            if (zMax <= p.OriginZ || p.MaxZ <= z) continue;
            return true;
        }
        return false;
    }

    private static bool AllCornersInside(
        double x, double y, double z, double dx, double dy, double dz,
        IReadOnlyList<(double nx, double ny, double nz, double d)> faces,
        double eps)
    {
        for (int corner = 0; corner < 8; corner++)
        {
            double cx = ((corner & 1) == 0) ? x : x + dx;
            double cy = ((corner & 2) == 0) ? y : y + dy;
            double cz = ((corner & 4) == 0) ? z : z + dz;
            for (int i = 0; i < faces.Count; i++)
            {
                var f = faces[i];
                // outward-oriented half-space test: dot(n, p) - d <= eps means inside
                double signed = f.nx * cx + f.ny * cy + f.nz * cz - f.d;
                if (signed > eps) return false;
            }
        }
        return true;
    }

    private static List<(double nx, double ny, double nz, double d)> BuildOutwardFaces(Slab slab)
    {
        // each face stored as outward unit normal (nx, ny, nz) and offset d s.t.
        // dot(n, p) <= d holds for points inside the convex Slab.
        var output = new List<(double, double, double, double)>(slab.FaceCount);
        var v = slab.VertexCoordsXyz;
        for (int fi = 0; fi < slab.FaceCount; fi++)
        {
            var face = slab.Faces[fi];
            if (face.Count < 3) continue;
            int v0 = face[0], v1 = face[1], v2 = face[2];
            double ax = v[3 * v0], ay = v[3 * v0 + 1], az = v[3 * v0 + 2];
            double bx = v[3 * v1], by = v[3 * v1 + 1], bz = v[3 * v1 + 2];
            double cx = v[3 * v2], cy = v[3 * v2 + 1], cz = v[3 * v2 + 2];
            // outward normal via (b-a) × (c-a). Slab convention: CCW from outside.
            double ux = bx - ax, uy = by - ay, uz = bz - az;
            double wx = cx - ax, wy = cy - ay, wz = cz - az;
            double nx = uy * wz - uz * wy;
            double ny = uz * wx - ux * wz;
            double nz = ux * wy - uy * wx;
            double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (!(len > 0)) continue;
            nx /= len; ny /= len; nz /= len;
            double d = nx * ax + ny * ay + nz * az;
            output.Add((nx, ny, nz, d));
        }
        return output;
    }

    private static void ComputeAabb(
        Slab s,
        out double xMin, out double yMin, out double zMin,
        out double xMax, out double yMax, out double zMax)
    {
        var v = s.VertexCoordsXyz;
        xMin = double.PositiveInfinity; yMin = double.PositiveInfinity; zMin = double.PositiveInfinity;
        xMax = double.NegativeInfinity; yMax = double.NegativeInfinity; zMax = double.NegativeInfinity;
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
