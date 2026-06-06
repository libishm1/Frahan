#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using Frahan.Masonry.Geometry;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// FractureInputReader -- one entrypoint for every fracture-input format the
// Frahan.BlockCutOpt pipeline supports. Auto-dispatches by file extension:
//
//   *.ply      -> PlyMeshReader (full 3D polygon mesh; preserves all dips)
//   *.csv      -> CsvFractureTraceSource + TraceVerticalExtruder
//                 (2D fracture traces, vertical-extruded between zMin/zMax)
//   *.lines    -> plain ASCII "x1 y1 x2 y2" per line + vertical extrusion
//                 (lightweight format for manual digitisation)
//   *.txt      -> same as .lines (auto-detected by content sniff)
//
// All output is a single PlyMesh consumable by BlockCutOptSolver.
//
// World units: METRES throughout (per BlockCutOptTolerances). For non-metre
// Rhino models call BlockCutOptTolerances.ToRhinoUnit on the consumer side.
// =============================================================================

public static class FractureInputReader
{
    /// <summary>
    /// Load fractures from disk, auto-detecting the format by file extension.
    /// Provide zMin / zMax for the 2D-trace formats (CSV / .lines / .txt).
    /// PLY files use their own Z range and ignore the parameters.
    /// </summary>
    public static PlyMesh Load(string path, double zMin = 0.0, double zMax = 1.0)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException(path);

        string ext = Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".ply":
                return PlyMeshReader.ReadFile(path);
            case ".csv":
                return LoadCsv(path, zMin, zMax);
            case ".lines":
            case ".txt":
                return LoadLines(path, zMin, zMax);
            default:
                // sniff: if first non-comment line starts with "ply", treat as PLY
                using (var sr = new StreamReader(path))
                {
                    string first;
                    while ((first = sr.ReadLine()) != null)
                    {
                        var t = first.Trim();
                        if (t.Length == 0 || t.StartsWith("#")) continue;
                        if (t.StartsWith("ply", StringComparison.OrdinalIgnoreCase))
                            return PlyMeshReader.ReadFile(path);
                        break;
                    }
                }
                // fallback: try lines
                return LoadLines(path, zMin, zMax);
        }
    }

    private static PlyMesh LoadCsv(string path, double zMin, double zMax)
    {
        var traces = CsvFractureTraceSource.ReadCsv(path);
        var asTuples = new List<(double X1, double Y1, double X2, double Y2)>(traces.Count);
        for (int i = 0; i < traces.Count; i++)
        {
            var t = traces[i];
            asTuples.Add((t.X1, t.Y1, t.X2, t.Y2));
        }
        return TraceVerticalExtruder.Extrude(asTuples, zMin, zMax);
    }

    private static PlyMesh LoadLines(string path, double zMin, double zMax)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var traces = new List<(double X1, double Y1, double X2, double Y2)>();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw == null ? "" : raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            var parts = line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) continue;
            if (double.TryParse(parts[0], System.Globalization.NumberStyles.Any, inv, out double x1)
                && double.TryParse(parts[1], System.Globalization.NumberStyles.Any, inv, out double y1)
                && double.TryParse(parts[2], System.Globalization.NumberStyles.Any, inv, out double x2)
                && double.TryParse(parts[3], System.Globalization.NumberStyles.Any, inv, out double y2))
            {
                traces.Add((x1, y1, x2, y2));
            }
        }
        return TraceVerticalExtruder.Extrude(traces, zMin, zMax);
    }
}
