#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Interfaces;
using Rhino.Geometry;

namespace Frahan.Masonry.Vault
{
    // =========================================================================
    // VaultRubbleAssembly — the ABUTTING-CELLS CRA path. Turn the Voronoi rubble
    // CELLS into a CRA-ready idealized-voussoir assembly:
    //   1. UN-SHRINK each cell (the pipeline shrinks cells by seal = 0.92+0.22*cc
    //      for the hairline rubble joint; dividing by that restores the tangent-
    //      plane-clipped cell that ABUTS its neighbours).
    //   2. Extrude the abutting cell +/- depth/2 along the frame normal -> a closed
    //      voussoir mould (same stereotomy as VaultVoussoirCapper).
    //   3. DETECT the shared contact faces between adjacent moulds
    //      (MeshContactDetector, adaptive tolerance for the near-tiling) -> interfaces.
    //   4. Low-z cells (the springing) -> fixed supports.
    // The raw ETH-fitted rubble stays a display SKIN; CRA runs on these idealized
    // cells -- the order prescribed by frahan_port/HANDOFF_CHECKPOINT2_CRA ("CRA on
    // idealized voussoirs with clean shared interfaces, never on raw rubble").
    //
    // Distinct from VaultShellAssembly (thrust-aligned quad partition, contact by
    // construction): this keeps the ACTUAL rubble cells (authentic, but blue-noise
    // -- not thrust-aligned) and relies on contact DETECTION. Reuse ShellAssemblyResult.
    // Contact detection is ~O(N) with the grid broad-phase; keep cells <= a few
    // hundred (coarsen the Poisson radius) for interactive use.
    // =========================================================================
    public static class VaultRubbleAssembly
    {
        public static ShellAssemblyResult Build(
            IList<PolylineCurve> cells, IList<Plane> frames, IList<double> columnness,
            double dVault, double dCol, double density = 2400.0, double protrude = 0.0,
            double supportBand = 0.08)
        {
            var res = new ShellAssemblyResult();
            int nc = cells == null ? 0 : cells.Count;
            if (nc == 0) return res;

            var blocks = new List<MasonryBlock>(nc);
            var snaps = new List<MeshSnapshot>(nc);
            var ids = new List<string>(nc);
            var centroids = new List<Point3d>(nc);
            double zmin = double.MaxValue, zmax = double.MinValue;

            for (int i = 0; i < nc; i++)
            {
                var cv = cells[i]; if (cv == null) continue;
                if (!cv.TryGetPolyline(out Polyline poly)) continue;
                int nn = poly.Count - 1; if (nn < 3) continue;
                double cc = (columnness != null && i < columnness.Count) ? columnness[i] : 0.0;
                double shk = 0.92 + 0.22 * cc;
                if (shk < 1e-3) shk = 1.0;

                // cell centroid + un-shrink (÷ shk) so the cell abuts its neighbours
                Point3d c = Point3d.Origin;
                for (int k = 0; k < nn; k++) c += poly[k];
                c /= nn;
                var ab = new Point3d[nn];
                double inv = 1.0 / shk;
                for (int k = 0; k < nn; k++) ab[k] = c + (poly[k] - c) * inv;

                // extrude through the depth (Striatus stereotomy)
                Plane fr = frames[i]; Vector3d n = fr.ZAxis; if (n.Length < 1e-9) n = Vector3d.ZAxis; n.Unitize();
                double depth = dVault + (dCol - dVault) * cc;
                double hd = depth * 0.5 + protrude;
                var m = new Mesh();
                for (int k = 0; k < nn; k++) m.Vertices.Add(ab[k] - n * hd);
                for (int k = 0; k < nn; k++) m.Vertices.Add(ab[k] + n * hd);
                for (int k = 1; k < nn - 1; k++) m.Faces.AddFace(0, k + 1, k);
                for (int k = 1; k < nn - 1; k++) m.Faces.AddFace(nn, nn + k, nn + k + 1);
                for (int k = 0; k < nn; k++) { int j = (k + 1) % nn; m.Faces.AddFace(k, j, nn + j); m.Faces.AddFace(k, nn + j, nn + k); }
                m.Normals.ComputeNormals(); m.UnifyNormals(); m.Compact();

                var coords = new List<double>(m.Vertices.Count * 3);
                for (int v = 0; v < m.Vertices.Count; v++) { var p = m.Vertices[v]; coords.Add(p.X); coords.Add(p.Y); coords.Add(p.Z); }
                var tris = new List<int>(m.Faces.Count * 3);
                foreach (var f in m.Faces)
                {
                    tris.Add(f.A); tris.Add(f.B); tris.Add(f.C);
                    if (f.IsQuad) { tris.Add(f.A); tris.Add(f.C); tris.Add(f.D); }
                }
                string id = "c" + i;
                blocks.Add(new MasonryBlock(id, coords, tris, density));
                snaps.Add(new MeshSnapshot(coords, tris));
                ids.Add(id);
                res.Voussoirs.Add(m);
                centroids.Add(c);
                if (c.Z < zmin) zmin = c.Z; if (c.Z > zmax) zmax = c.Z;
            }
            res.BlockCount = blocks.Count;
            if (blocks.Count == 0) return res;

            // shared contact faces between adjacent moulds (adaptive tol for near-tiling)
            var ifaces = MeshContactDetector.Detect(snaps, ids, 1e-3, 6.0, 3, 0.06, true);
            res.InterfaceCount = ifaces.Count;

            // supports: cells whose centroid sits in the lowest z-band (the springing)
            double zsup = zmin + supportBand * Math.Max(1e-9, zmax - zmin);
            var fixedIds = new List<string>();
            for (int i = 0; i < centroids.Count; i++)
                if (centroids[i].Z <= zsup) { fixedIds.Add(ids[i]); res.FixedIndices.Add(i); }
            res.SupportCount = fixedIds.Count;

            res.Assembly = new MasonryAssembly(blocks, new List<MasonryInterface>(ifaces), new BoundaryConditions(fixedIds));
            return res;
        }
    }
}
