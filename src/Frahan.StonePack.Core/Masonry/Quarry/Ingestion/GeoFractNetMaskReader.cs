#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Frahan.Masonry.Cutting;

namespace Frahan.Masonry.Quarry.Ingestion;

// =============================================================================
// GeoFractNetMaskReader -- CSV ingestion of GeoFractNet fracture predictions.
//
// Expected CSV columns (header optional):
//     point_x_m, point_y_m, point_z_m,
//     normal_x, normal_y, normal_z,
//     confidence_01, set_id, label
//
// Lines starting with '#' are comments. Confidence clamped to [0, 1].
//
// Conversion to a BlockCutOpt-consumable PlyMesh is left to the existing
// JointSetDfnPlyEmitter (one triangulated infinite plane intersected with the
// bench AABB per fracture); this reader stays pure (no geometry expansion).
// =============================================================================

public static class GeoFractNetMaskReader
{
    public static IReadOnlyList<GeoFractNetFracture> Load(string csvPath, double minConfidence = 0.0)
    {
        if (string.IsNullOrWhiteSpace(csvPath)) throw new ArgumentException("csvPath required", nameof(csvPath));
        if (!File.Exists(csvPath)) throw new FileNotFoundException("CSV not found", csvPath);
        if (minConfidence < 0 || minConfidence > 1)
            throw new ArgumentOutOfRangeException(nameof(minConfidence), "0..1");

        var output = new List<GeoFractNetFracture>();
        using (var reader = new StreamReader(csvPath))
        {
            string line;
            int lineNo = 0;
            while ((line = reader.ReadLine()) != null)
            {
                lineNo++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.TrimStart().StartsWith("#")) continue;

                var parts = line.Split(',');
                if (parts.Length < 8) continue;

                if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var px))
                    continue; // header
                if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var py))
                    throw new FormatException($"line {lineNo}: point_y_m");
                if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var pz))
                    throw new FormatException($"line {lineNo}: point_z_m");
                if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var nx))
                    throw new FormatException($"line {lineNo}: normal_x");
                if (!double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var ny))
                    throw new FormatException($"line {lineNo}: normal_y");
                if (!double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var nz))
                    throw new FormatException($"line {lineNo}: normal_z");
                if (!double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var conf))
                    throw new FormatException($"line {lineNo}: confidence_01");
                if (conf < 0) conf = 0;
                if (conf > 1) conf = 1;
                if (conf < minConfidence) continue;

                int setId = 0;
                if (parts.Length > 7 && !int.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out setId))
                {
                    setId = 0;
                }
                if (setId < 0) setId = 0;
                string label = parts.Length > 8 ? parts[8].Trim() : string.Empty;

                FracturePlane plane;
                try
                {
                    plane = new FracturePlane(px, py, pz, nx, ny, nz);
                }
                catch (ArgumentException)
                {
                    // zero-magnitude normal -- skip the row, do not throw on a single bad line
                    continue;
                }
                output.Add(new GeoFractNetFracture(plane, conf, setId, label));
            }
        }
        return output;
    }
}
