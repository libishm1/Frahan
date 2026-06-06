#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// SyntheticTnGraniteGenerator -- produces a realistic synthetic fracture
// dataset for Tamil Nadu granite, deterministic given a seed.
//
// Joint-set parameters tuned to typical hard-rock granite (Goodman-Shi 1985
// + ISRM Suggested Methods + project memory project_quarry_tamilnadu.md):
//
//   Set 1: vertical, strike around 30 deg (NE-SW)
//          dip direction 120 deg (perpendicular to strike, dipping SE)
//          dip 88 deg, mean spacing 5.0 m, Fisher scatter 6 deg
//   Set 2: vertical, strike around 120 deg (orthogonal to set 1, NW-SE)
//          dip direction 30 deg, dip 88 deg, mean spacing 6.0 m, scatter 6 deg
//   Set 3: sub-horizontal bedding, dip direction 0, dip 5 deg
//          mean spacing 1.5 m, scatter 4 deg
//
// Output: two files
//   - {csvPath}  vertical-2D traces (x1, y1, x2, y2 in metres) at z = bench
//                mid-height, clipped to the bench xy AABB.
//   - {plyPath}  ASCII PLY of the full 3D fracture polygons clipped to the
//                bench AABB (via JointSetDfnPlyEmitter).
//
// Use WriteSampleSet to emit both at once; use other static helpers when only
// one format is needed.
// =============================================================================

public static class SyntheticTnGraniteGenerator
{
    /// <summary>
    /// Canonical Tamil Nadu granite joint-set definitions (see file header
    /// for the rationale). Returns three JointSets.
    /// </summary>
    public static IReadOnlyList<JointSet> TamilNaduGraniteJointSets()
    {
        return new[]
        {
            new JointSet(dipDirectionDeg: 120.0, dipDeg: 88.0, meanSpacing: 5.0, scatterDeg: 6.0),
            new JointSet(dipDirectionDeg: 30.0,  dipDeg: 88.0, meanSpacing: 6.0, scatterDeg: 6.0),
            new JointSet(dipDirectionDeg: 0.0,   dipDeg: 5.0,  meanSpacing: 1.5, scatterDeg: 4.0),
        };
    }

    /// <summary>
    /// Generate fracture planes + emit both formats. Deterministic given
    /// <paramref name="seed"/> and <paramref name="bench"/>.
    /// </summary>
    public static (int PlaneCount, int CsvTraceCount, int PlyTriangleCount) WriteSampleSet(
        string csvPath,
        string plyPath,
        BoundingBox3 bench,
        int seed,
        IReadOnlyList<JointSet> jointSets = null)
    {
        if (string.IsNullOrWhiteSpace(csvPath)) throw new ArgumentException(nameof(csvPath));
        if (string.IsNullOrWhiteSpace(plyPath)) throw new ArgumentException(nameof(plyPath));
        if (bench == null) throw new ArgumentNullException(nameof(bench));
        jointSets = jointSets ?? TamilNaduGraniteJointSets();

        var planes = JointSetDfnGenerator.Generate(jointSets, bench, seed);

        // PLY: 3D polygons clipped to the bench AABB
        var ply = JointSetDfnPlyEmitter.Emit(planes, bench);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(plyPath)) ?? ".");
        WriteAsciiPly(ply, plyPath, header: "Tamil Nadu granite synthetic DFN -- " +
            $"seed={seed}, bench=({bench.SizeX:0.#}x{bench.SizeY:0.#}x{bench.SizeZ:0.#}m)");

        // CSV: vertical-2D traces at z = bench mid-height
        var traces = ProjectPlanesToMidZ(planes, bench);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(csvPath)) ?? ".");
        WriteTracesCsv(traces, csvPath, bench, seed);

        return (planes.Count, traces.Count, ply.TriangleCount);
    }

    /// <summary>
    /// Project each fracture plane to its intersection line with z = bench
    /// mid-height, clip to the bench xy AABB, and emit one (x1,y1,x2,y2)
    /// tuple per non-degenerate trace. Nearly-horizontal planes (||N x z||
    /// below epsilon) are skipped.
    /// </summary>
    public static IReadOnlyList<(double X1, double Y1, double X2, double Y2)> ProjectPlanesToMidZ(
        IReadOnlyList<FracturePlane> planes,
        BoundingBox3 bench)
    {
        const double horizontalEps = 0.02; // ~1 deg from horizontal
        double zMid = 0.5 * (bench.MinZ + bench.MaxZ);
        double xMin = bench.MinX, xMax = bench.MaxX;
        double yMin = bench.MinY, yMax = bench.MaxY;

        var traces = new List<(double, double, double, double)>(planes.Count);
        for (int i = 0; i < planes.Count; i++)
        {
            var p = planes[i];
            double dx = p.NormalY;     // direction = N x (0,0,1) = (Ny, -Nx, 0)
            double dy = -p.NormalX;
            double dLen = Math.Sqrt(dx * dx + dy * dy);
            if (dLen < horizontalEps) continue;
            dx /= dLen; dy /= dLen;

            // Point on the fracture plane with z = zMid:
            // project (0,0,1) onto the fracture plane: v = z_axis - Nz * N
            // then walk from P along v until z = zMid.
            double vx = -p.NormalX * p.NormalZ;
            double vy = -p.NormalY * p.NormalZ;
            double vz = 1.0 - p.NormalZ * p.NormalZ;
            if (Math.Abs(vz) < 1.0e-9) continue;
            double t = (zMid - p.PointZ) / vz;
            double px = p.PointX + t * vx;
            double py = p.PointY + t * vy;

            // Parametric line: (px + s*dx, py + s*dy). Clip to xy AABB via
            // Liang-Barsky on each axis.
            double sIn = double.NegativeInfinity, sOut = double.PositiveInfinity;
            if (!ClipAxis(px, dx, xMin, xMax, ref sIn, ref sOut)) continue;
            if (!ClipAxis(py, dy, yMin, yMax, ref sIn, ref sOut)) continue;

            double ax = px + sIn * dx;
            double ay = py + sIn * dy;
            double bx = px + sOut * dx;
            double by = py + sOut * dy;
            if (Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)) < 1.0e-6) continue;

            traces.Add((ax, ay, bx, by));
        }
        return traces;
    }

    private static bool ClipAxis(double p, double d, double lo, double hi, ref double tIn, ref double tOut)
    {
        if (Math.Abs(d) < 1.0e-12) return p >= lo - 1.0e-9 && p <= hi + 1.0e-9;
        double t1 = (lo - p) / d;
        double t2 = (hi - p) / d;
        if (t1 > t2) { double tmp = t1; t1 = t2; t2 = tmp; }
        if (t1 > tIn) tIn = t1;
        if (t2 < tOut) tOut = t2;
        return tIn <= tOut + 1.0e-9;
    }

    private static void WriteTracesCsv(
        IReadOnlyList<(double X1, double Y1, double X2, double Y2)> traces,
        string path,
        BoundingBox3 bench,
        int seed)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("# Tamil Nadu granite synthetic DFN -- traces at z = bench mid-height");
        sb.AppendLine($"# Seed: {seed}");
        sb.AppendLine($"# Bench: ({bench.SizeX.ToString("0.#", inv)} x {bench.SizeY.ToString("0.#", inv)} x {bench.SizeZ.ToString("0.#", inv)}) m");
        sb.AppendLine("# Joint sets: 3 (vertical strike 30 deg, vertical strike 120 deg, sub-horizontal bedding)");
        sb.AppendLine("# Generator: Frahan.Masonry.Quarry.BlockCutOpt.SyntheticTnGraniteGenerator");
        sb.AppendLine("x1,y1,x2,y2");
        for (int i = 0; i < traces.Count; i++)
        {
            var t = traces[i];
            sb.Append(t.X1.ToString("0.######", inv)); sb.Append(',');
            sb.Append(t.Y1.ToString("0.######", inv)); sb.Append(',');
            sb.Append(t.X2.ToString("0.######", inv)); sb.Append(',');
            sb.AppendLine(t.Y2.ToString("0.######", inv));
        }
        File.WriteAllText(path, sb.ToString(), Encoding.ASCII);
    }

    private static void WriteAsciiPly(PlyMesh mesh, string path, string header)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("ply");
        sb.AppendLine("format ascii 1.0");
        sb.AppendLine($"comment {header}");
        sb.AppendLine($"element vertex {mesh.VertexCount}");
        sb.AppendLine("property float x");
        sb.AppendLine("property float y");
        sb.AppendLine("property float z");
        sb.AppendLine($"element face {mesh.TriangleCount}");
        sb.AppendLine("property list uchar int vertex_indices");
        sb.AppendLine("end_header");
        var v = mesh.VertexCoordsXyz;
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            sb.Append(v[3 * i + 0].ToString("0.######", inv)); sb.Append(' ');
            sb.Append(v[3 * i + 1].ToString("0.######", inv)); sb.Append(' ');
            sb.AppendLine(v[3 * i + 2].ToString("0.######", inv));
        }
        var t = mesh.TriangleIndices;
        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            sb.Append("3 ");
            sb.Append(t[3 * i + 0]); sb.Append(' ');
            sb.Append(t[3 * i + 1]); sb.Append(' ');
            sb.AppendLine(t[3 * i + 2].ToString(inv));
        }
        File.WriteAllText(path, sb.ToString(), Encoding.ASCII);
    }
}
