#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Quarry.CutOpt;

namespace Frahan.Masonry.Quarry.GeoPack;

// =============================================================================
// GeoPackBuilders -- manual-input v0 of spec 08.
//
// CrackGraphBuilder.FromPlanes:  List<FracturePlane> + confidence + ids
//                                -> CrackGraph
//
// BlockGraphBuilder.Partition:  Slab bench + CrackGraph
//                               -> BlockGraph (one Slab per resulting cell)
//
// BlockCandidateGenerator.AabbPerCell:  BlockGraph
//                                      -> List<BlockCandidate> (one
//                                          AABB-based BenchBlock per cell)
//
// Partition uses the existing SlabCutter -- each crack plane in turn cuts
// every current piece into two. For N planes the worst-case cell count is
// 2^N, but in practice it's far smaller (cracks don't generally divide every
// cell).
// =============================================================================

public static class CrackGraphBuilder
{
    public static CrackGraph FromPlanes(
        IReadOnlyList<FracturePlane> planes,
        IReadOnlyList<double> confidences = null,
        IReadOnlyList<string> ids = null,
        IReadOnlyList<double> rmsErrors = null)
    {
        if (planes == null) throw new ArgumentNullException(nameof(planes));

        var cracks = new List<CrackSurface>(planes.Count);
        for (int i = 0; i < planes.Count; i++)
        {
            var p = planes[i];
            if (p == null) throw new ArgumentException($"planes[{i}] is null", nameof(planes));
            double conf = (confidences != null && i < confidences.Count) ? confidences[i] : 1.0;
            if (conf < 0) conf = 0; if (conf > 1) conf = 1;
            string id = (ids != null && i < ids.Count && !string.IsNullOrWhiteSpace(ids[i]))
                ? ids[i]
                : $"CRK-{i:D4}";
            double rms = (rmsErrors != null && i < rmsErrors.Count) ? rmsErrors[i] : 0.0;
            if (rms < 0) rms = 0;
            cracks.Add(new CrackSurface(id, CrackSurfaceKind.Plane, p, rms, conf));
        }
        return new CrackGraph(cracks);
    }
}

public static class BlockGraphBuilder
{
    public static BlockGraph Partition(Slab bench, CrackGraph cracks, double minCellVolume = 1e-9)
    {
        if (bench == null) throw new ArgumentNullException(nameof(bench));
        if (cracks == null) throw new ArgumentNullException(nameof(cracks));
        if (minCellVolume < 0) throw new ArgumentOutOfRangeException(nameof(minCellVolume));

        var planes = cracks.ToFracturePlanes();
        var result = SlabCutter.Cut(bench, planes);

        var cells = new List<BlockCell>(result.Count);
        int idCounter = 0;
        for (int i = 0; i < result.Count; i++)
        {
            var s = result.Slabs[i];
            double v = Math.Abs(s.SignedVolume());
            if (v < minCellVolume) continue;
            cells.Add(new BlockCell($"CELL-{idCounter:D4}", s));
            idCounter++;
        }
        return new BlockGraph(cells);
    }
}

public static class BlockCandidateGenerator
{
    /// <summary>
    /// One BlockCandidate per cell, using the cell's axis-aligned bounding
    /// box as the BenchBlock footprint. Useful for downstream Layer 7 input.
    /// </summary>
    public static IReadOnlyList<BlockCandidate> AabbPerCell(
        BlockGraph graph,
        double geologyGrade = 1.0,
        double uncertaintyBuffer = 0.0)
    {
        if (graph == null) throw new ArgumentNullException(nameof(graph));
        if (geologyGrade < 0 || geologyGrade > 1)
            throw new ArgumentOutOfRangeException(nameof(geologyGrade), "0..1");

        var output = new List<BlockCandidate>(graph.Count);
        for (int i = 0; i < graph.Count; i++)
        {
            var c = graph.Cells[i];
            ComputeAabb(c.Geometry,
                out double xMin, out double yMin, out double zMin,
                out double xMax, out double yMax, out double zMax);

            if (!(xMax > xMin && yMax > yMin && zMax > zMin))
                continue; // degenerate cell skipped

            var footprint = new Frahan.Masonry.Fractures.BoundingBox3(xMin, yMin, zMin, xMax, yMax, zMax);
            var bench = new BenchBlock($"BLK-{i:D4}", footprint, geologyGrade);
            output.Add(new BlockCandidate(c, bench, uncertaintyBuffer));
        }
        return output;
    }

    /// <summary>
    /// Materialise the BlockCandidates as a QuarryInventory ready for the
    /// Layer 7 (CutOpt) pipeline.
    /// </summary>
    public static QuarryInventory ToInventory(
        string benchId,
        IReadOnlyList<BlockCandidate> candidates)
    {
        if (candidates == null) throw new ArgumentNullException(nameof(candidates));
        var blocks = new List<BenchBlock>(candidates.Count);
        for (int i = 0; i < candidates.Count; i++) blocks.Add(candidates[i].OrientedBox);
        return new QuarryInventory(benchId, blocks);
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
