#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Masonry.Vault
{
    // =========================================================================
    // VaultVoussoirCapper — lift each Voronoi cell polygon to a CAPPED closed
    // voussoir mould by offsetting +/-(D/2 + protrude) along the cell normal.
    //
    // Ported from the validated Park Güell rubble-vault v004 recipe (Stage 3).
    // depth D = dVault + (dCol - dVault) * columnness (thinner stones on legs).
    // Output lists are compacted and aligned with their source cells/frames.
    // =========================================================================
    public sealed class MouldResult
    {
        public readonly List<Mesh> Moulds = new List<Mesh>();
        public readonly List<PolylineCurve> Cells = new List<PolylineCurve>();
        public readonly List<Plane> Frames = new List<Plane>();
        public readonly List<double> Columnness = new List<double>();
        public int Count { get { return Moulds.Count; } }
    }

    public static class VaultVoussoirCapper
    {
        public static MouldResult Cap(
            IList<PolylineCurve> cells, IList<Plane> frames, IList<double> columnness,
            double dVault, double dCol, double protrude)
        {
            return Cap(cells, frames, columnness, dVault, dCol, protrude, null);
        }

        /// <summary>
        /// innerLimit (optional, per cell): maximum INWARD offset (-ZAxis side).
        /// On thin column tubes this stops opposite/adjacent stones from
        /// interpenetrating through the tube axis (Quad Cells emits ~0.6 x local
        /// tube radius there; vault cells pass a huge value = symmetric as before).
        /// </summary>
        public static MouldResult Cap(
            IList<PolylineCurve> cells, IList<Plane> frames, IList<double> columnness,
            double dVault, double dCol, double protrude, IList<double> innerLimit)
        {
            var res = new MouldResult();
            int nc = cells == null ? 0 : cells.Count;
            for (int i = 0; i < nc; i++)
            {
                PolylineCurve cv = cells[i];
                if (cv == null) continue;
                Plane fr = frames[i];
                double cc = columnness[i];

                double depth = dVault + (dCol - dVault) * cc;
                double hdOut = depth * 0.5 + protrude;
                double hdIn = hdOut;
                if (innerLimit != null && i < innerLimit.Count && innerLimit[i] > 0.0)
                    hdIn = Math.Max(0.02, Math.Min(hdIn, innerLimit[i]));   // clamp inward on tubes
                Vector3d n = fr.ZAxis;

                Polyline poly;
                if (!cv.TryGetPolyline(out poly)) continue;
                int nn = poly.Count - 1;            // drop the closing duplicate point
                if (nn < 3) continue;

                var m = new Mesh();
                for (int k = 0; k < nn; k++) m.Vertices.Add(poly[k] - n * hdIn);    // bottom (inward) 0..nn-1
                for (int k = 0; k < nn; k++) m.Vertices.Add(poly[k] + n * hdOut);   // top (outward)   nn..2nn-1

                for (int k = 1; k < nn - 1; k++) m.Faces.AddFace(0, k + 1, k);            // bottom fan
                for (int k = 1; k < nn - 1; k++) m.Faces.AddFace(nn, nn + k, nn + k + 1); // top fan
                for (int k = 0; k < nn; k++)                                              // side quads
                {
                    int j = (k + 1) % nn;
                    m.Faces.AddFace(k, j, nn + j);
                    m.Faces.AddFace(k, nn + j, nn + k);
                }

                m.Normals.ComputeNormals();
                m.UnifyNormals();
                m.Compact();
                if (!m.IsClosed) continue;

                res.Moulds.Add(m);
                res.Cells.Add(cv);
                res.Frames.Add(fr);
                res.Columnness.Add(cc);
            }
            return res;
        }
    }
}
