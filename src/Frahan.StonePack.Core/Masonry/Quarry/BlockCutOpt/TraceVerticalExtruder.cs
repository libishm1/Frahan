#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Geometry;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// TraceVerticalExtruder -- helper for the photogrammetric ingestion path
// described in `D:\code_ws\wiki\papers\equations_and_diagrams\09_dataset_reproduction_report.md`
// section 6.5 and 6.6.
//
// Given a list of 2D fracture-trace endpoint pairs (x1, y1, x2, y2) digitized
// from an orthophoto or a regional fracture map, emit a PlyMesh of vertical
// rectangles spanning [zMin, zMax]. The output is directly consumable by
// BlockCutOptSolver.
//
// This is the C# equivalent of `csv_to_ply.py` and the same recipe BlockCutOpt
// 2020 used to ingest figure 7 of Sousa et al. 2016 (Mondim de Basto granite).
// =============================================================================

public static class TraceVerticalExtruder
{
    /// <summary>
    /// Vertical-extrude a list of 2D fracture traces into a PLY mesh of
    /// rectangles fan-triangulated as two triangles each.
    /// </summary>
    public static PlyMesh Extrude(
        IReadOnlyList<(double X1, double Y1, double X2, double Y2)> traces,
        double zMin,
        double zMax)
    {
        if (traces == null) throw new ArgumentNullException(nameof(traces));
        if (!(zMax > zMin)) throw new ArgumentException("zMax must exceed zMin");

        var v = new List<double>(traces.Count * 12);
        var t = new List<int>(traces.Count * 6);

        for (int i = 0; i < traces.Count; i++)
        {
            var tr = traces[i];
            int b = v.Count / 3;

            v.Add(tr.X1); v.Add(tr.Y1); v.Add(zMin);
            v.Add(tr.X2); v.Add(tr.Y2); v.Add(zMin);
            v.Add(tr.X2); v.Add(tr.Y2); v.Add(zMax);
            v.Add(tr.X1); v.Add(tr.Y1); v.Add(zMax);

            t.Add(b + 0); t.Add(b + 1); t.Add(b + 2);
            t.Add(b + 0); t.Add(b + 2); t.Add(b + 3);
        }

        return new PlyMesh(v, t, null);
    }
}
