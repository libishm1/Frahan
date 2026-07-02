#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Rhino.Geometry;

namespace Frahan.Masonry.Vault
{
    // =========================================================================
    // FabricationSchedule — turn a voussoir/stone list into shop paperwork
    // (P2 fabrication outputs, 2026-07-02): stable IDs, per-block dimensions
    // (world bbox for saw envelopes), volume + weight, a CSV schedule, and an
    // inspection LAYOUT (blocks re-arranged on a flat grid in ID order, largest
    // first, so the yard can check pieces against the sheet). Pure geometry +
    // strings — the 2D cut-sheet nesting of planar faces stays with the
    // existing Sheet Nest (Hole-Aware) component.
    // =========================================================================
    public sealed class FabScheduleResult
    {
        public readonly List<string> Ids = new List<string>();
        public readonly List<Point3d> TagPoints = new List<Point3d>();
        public readonly List<Mesh> Layout = new List<Mesh>();
        public string Csv = "";
        public double TotalVolume;
        public double TotalWeight;
        public int Count { get { return Ids.Count; } }
    }

    public static class FabricationSchedule
    {
        public static FabScheduleResult Build(IList<Mesh> blocks, double density,
                                              string prefix, double layoutSpacing)
        {
            var res = new FabScheduleResult();
            if (blocks == null || blocks.Count == 0) return res;
            if (string.IsNullOrEmpty(prefix)) prefix = "V";
            var ci = CultureInfo.InvariantCulture;

            // measure
            var rows = new List<(int Src, double Vol, BoundingBox Bb)>();
            for (int i = 0; i < blocks.Count; i++)
            {
                var m = blocks[i];
                if (m == null || m.Faces.Count == 0) continue;
                double vol = 0;
                try { var vp = VolumeMassProperties.Compute(m); if (vp != null) vol = Math.Abs(vp.Volume); }
                catch { }
                rows.Add((i, vol, m.GetBoundingBox(true)));
            }
            // fabrication order: largest volume first (saw the big pieces early)
            rows.Sort((a, b) => b.Vol.CompareTo(a.Vol));

            var sb = new StringBuilder();
            sb.AppendLine("id,source_index,dim_x_m,dim_y_m,dim_z_m,volume_m3,weight_kg,centroid_x,centroid_y,centroid_z");
            int digits = Math.Max(3, rows.Count.ToString(ci).Length);

            // layout grid sizing
            int perRow = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(rows.Count)));
            double cell = 0;
            foreach (var r in rows) { var d = r.Bb.Diagonal; cell = Math.Max(cell, Math.Max(d.X, d.Y)); }
            cell += Math.Max(0, layoutSpacing);

            for (int k = 0; k < rows.Count; k++)
            {
                var (src, vol, bb) = rows[k];
                string id = prefix + (k + 1).ToString(ci).PadLeft(digits, '0');
                var d = bb.Diagonal;
                double w = vol * density;
                var c = bb.Center;
                res.Ids.Add(id);
                res.TagPoints.Add(c);
                res.TotalVolume += vol;
                res.TotalWeight += w;
                sb.AppendLine(string.Join(",",
                    id, src.ToString(ci),
                    d.X.ToString("0.000", ci), d.Y.ToString("0.000", ci), d.Z.ToString("0.000", ci),
                    vol.ToString("0.0000", ci), w.ToString("0.0", ci),
                    c.X.ToString("0.000", ci), c.Y.ToString("0.000", ci), c.Z.ToString("0.000", ci)));

                if (layoutSpacing > 0)
                {
                    int gr = k / perRow, gc = k % perRow;
                    var lay = blocks[src].DuplicateMesh();
                    // rest each block on the ground at its grid cell, bbox-min aligned
                    var target = new Point3d(gc * cell, -(gr + 2) * cell, 0);
                    lay.Translate(target - bb.Min);
                    res.Layout.Add(lay);
                }
            }
            res.Csv = sb.ToString();
            return res;
        }
    }
}
