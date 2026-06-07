#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Frahan.Masonry.Sequencing;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Masonry.Sequencing
{
    // =========================================================================
    // PolygonalMasonrySequence3DComponent — 3D extension of the Kim 2024
    // polygonal-masonry install-order DAG (paper sec. 8 sketches z = F(x, y)
    // surfaces as future work). The component takes a list of closed
    // polyhedral Meshes representing stones, auto-detects face-sharing
    // adjacency, and runs reversed Kahn's depth search on the 3D DAG.
    //
    // Adjacency detection: two meshes are adjacent if they share at least
    // one mesh face within the supplied tolerance. The check buckets mesh
    // face centroids into a coarse grid for O(N * avgFacesPerCell) lookup
    // rather than O(N^2 * F^2).
    //
    // The 3D analogue of paper rule (5)-(8) is: for each adjacent pair, the
    // cell with the higher representative-Z is the upper cell. Cells whose
    // centroid Zs differ by less than `Z Threshold` are side neighbours and
    // impose no ordering, matching the 2D rule for purely vertical shared
    // edges.
    //
    // ComponentGuid: C5F18B4D-8A6F-4E72-AC83-1FBD32D8C7B2
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Polygonal Masonry Sequence 3D.
    /// Install-order DAG for a 3D polyhedral wall (Kim 2024 sec. 8 extension).
    /// </summary>
    [Algorithm("3D polygonal masonry install sequence", "Kim 2024 DETC2024-142563 section 8 3D extension", Note = "Wall3D centroid-based dependencies")]
    [Algorithm("Reversed Kahn DAG depth search", "Kim 2024 Code 1 topological sort", Note = "Diamond-graph longest-path variant")]
        [DesignApplication(
        "Install-order DAG for a 3D polyhedral-stone wall",
        DesignFlow.BottomUp,
        Precedent = "Kim 2024 polygonal masonry install order DAG (3D variant)")]
    public sealed class PolygonalMasonrySequence3DComponent : GH_Component
    {
        public PolygonalMasonrySequence3DComponent()
            : base(
                "Polygonal Masonry Sequence 3D", "PolyMasonrySeq3D",
                "Install-order DAG for a 3D polyhedral-stone wall. Each " +
                "input Mesh is one stone; adjacency is detected from " +
                "shared mesh faces. Returns 1-based install order, " +
                "reversed-Kahn depth, and DAG edges as line segments " +
                "between cell centroids (Kim 2024 sec. 8 extension).",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("C5F18B4D-8A6F-4E72-AC83-1FBD32D8C7B2");

        protected override Bitmap Icon => IconProvider.Load("StereotomyGenerate.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Cells", "M",
                "Closed polyhedral meshes; one per stone.",
                GH_ParamAccess.list);
            p.AddPointParameter("Hole Probes", "H",
                "Optional. Each probe point marks the cell whose centroid " +
                "is closest as a hole; that cell is removed before the " +
                "depth search (sec. 5.4 analogue).",
                GH_ParamAccess.list);
            p[1].Optional = true;
            p.AddNumberParameter("Face Tolerance", "Tf",
                "Two cells count as adjacent when at least one of their " +
                "mesh faces matches within this Euclidean tolerance.",
                GH_ParamAccess.item, 1e-3);
            p[2].Optional = true;
            p.AddNumberParameter("Z Threshold", "Tz",
                "Adjacent cells whose representative-Z difference is " +
                "below this value are treated as side neighbours " +
                "(no ordering constraint).",
                GH_ParamAccess.item, 1e-3);
            p[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Stones", "S",
                "Cells in install order, parallel to Order / Depth.",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Install Order", "i",
                "1-based install index per cell, parallel to Stones.",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Depth", "d",
                "Reversed-Kahn depth per cell.",
                GH_ParamAccess.list);
            p.AddLineParameter("DAG Edges", "E",
                "Line segments from lower-Z cell centroid to higher-Z " +
                "cell centroid for every DAG edge.",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Cell Count", "n",
                "Number of stones included in the install plan " +
                "(excludes hole-marked cells).",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var meshes = new List<Mesh>();
            if (!da.GetDataList(0, meshes) || meshes.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "No cells provided.");
                return;
            }

            var holeProbes = new List<Point3d>();
            da.GetDataList(1, holeProbes);

            double faceTol = 1e-3;
            da.GetData(2, ref faceTol);
            if (faceTol <= 0.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Face Tolerance must be positive, got {faceTol}.");
                return;
            }

            double zThreshold = 1e-3;
            da.GetData(3, ref zThreshold);
            if (zThreshold < 0.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Z Threshold must be non-negative, got {zThreshold}.");
                return;
            }

            var cells = new List<Cell3D>(meshes.Count);
            var meshById = new Dictionary<int, Mesh>(meshes.Count);
            for (int i = 0; i < meshes.Count; i++)
            {
                var m = meshes[i];
                if (m == null || m.Faces.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"Cell {i}: null or empty mesh; skipped.");
                    continue;
                }
                var c = MeshCentroid(m);
                cells.Add(new Cell3D(i, (c.X, c.Y, c.Z)));
                meshById[i] = m;
            }
            if (cells.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "No usable cells after extraction.");
                return;
            }

            List<(int A, int B)> adjacency;
            try
            {
                adjacency = DetectAdjacency(meshById, faceTol);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Adjacency detection failed: {ex.Message}");
                return;
            }

            var wall = new Wall3D(cells, adjacency);
            if (holeProbes.Count > 0)
            {
                wall.RemoveCells(ResolveHoleProbes(meshById, holeProbes));
            }

            InstallationPlan3D plan;
            try
            {
                plan = wall.InstallSequence(zThreshold);
            }
            catch (InvalidOperationException ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            var stoneMeshes = new List<Mesh>();
            var orderList = new List<int>();
            var depthList = new List<int>();
            int cellCount = 0;
            // Emit cells in install-order index.
            var byOrder = new SortedDictionary<int, int>();
            foreach (var kvp in plan.Order) byOrder[kvp.Value] = kvp.Key;
            foreach (var kvp in byOrder)
            {
                int cellId = kvp.Value;
                if (!meshById.TryGetValue(cellId, out var m)) continue;
                // Emit a CLEAN closed solid, not the raw input mesh. Voronoi /
                // scan-derived cells often arrive with duplicate vertices,
                // degenerate slivers, and inconsistent (inward) face normals, so
                // they render as see-through "open" shells even when topologically
                // closed. Sanitise here so the component's output is clean by
                // itself and never needs a downstream cleanup pass.
                stoneMeshes.Add(CleanCell(m));
                orderList.Add(kvp.Key);
                depthList.Add(plan.Depth[cellId]);
                cellCount++;
            }

            var edges = new List<Line>();
            foreach (var (low, high) in plan.DagEdges)
            {
                if (!meshById.ContainsKey(low) || !meshById.ContainsKey(high)) continue;
                var a = cells[low].Representative;
                var b = cells[high].Representative;
                edges.Add(new Line(
                    new Point3d(a.X, a.Y, a.Z),
                    new Point3d(b.X, b.Y, b.Z)));
            }

            da.SetDataList(0, stoneMeshes);
            da.SetDataList(1, orderList);
            da.SetDataList(2, depthList);
            da.SetDataList(3, edges);
            da.SetData(4, cellCount);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        // Sanitise a cell mesh into a clean closed solid with outward normals.
        // Combine duplicate vertices, drop degenerate faces, cap any holes, and
        // unify + outward-orient the normals so the cell renders solid (not a
        // see-through shell). Operates on a duplicate; the input is not mutated.
        private static Mesh CleanCell(Mesh src)
        {
            if (src == null) return null;
            var m = src.DuplicateMesh();
            m.Vertices.CombineIdentical(true, true);
            m.Faces.CullDegenerateFaces();
            if (!m.IsClosed) m.FillHoles();
            m.RebuildNormals();
            m.UnifyNormals();
            if (m.IsClosed && m.Volume() < 0) m.Flip(true, true, true);
            m.RebuildNormals();
            m.Compact();
            return m;
        }

        private static Point3d MeshCentroid(Mesh m)
        {
            int n = m.Vertices.Count;
            if (n == 0) return Point3d.Origin;
            double sx = 0, sy = 0, sz = 0;
            for (int i = 0; i < n; i++)
            {
                var v = m.Vertices[i];
                sx += v.X; sy += v.Y; sz += v.Z;
            }
            return new Point3d(sx / n, sy / n, sz / n);
        }

        /// <summary>
        /// Adjacency by shared mesh face. Face identity uses the centroid
        /// of each face quantised by `tol`; two cells are adjacent iff they
        /// share at least one quantised-centroid key.
        ///
        /// This skips proper face-by-face geometric equality (which would
        /// require ordered vertex comparison). For Voronoi-style cells
        /// where neighbours share a single planar face exactly, centroid
        /// matching is sufficient. For coarse stone meshes with multiple
        /// shared subfaces the count may overestimate adjacency.
        /// </summary>
        private static List<(int A, int B)> DetectAdjacency(
            Dictionary<int, Mesh> meshById, double tol)
        {
            double scale = 1.0 / Math.Max(tol, 1e-12);
            var faceOwners = new Dictionary<(long, long, long), List<int>>();
            foreach (var kvp in meshById)
            {
                int cellId = kvp.Key;
                var m = kvp.Value;
                for (int f = 0; f < m.Faces.Count; f++)
                {
                    var face = m.Faces[f];
                    var centroid = FaceCentroid(m, face);
                    var key = (
                        (long)Math.Round(centroid.X * scale),
                        (long)Math.Round(centroid.Y * scale),
                        (long)Math.Round(centroid.Z * scale));
                    if (!faceOwners.TryGetValue(key, out var owners))
                    {
                        owners = new List<int>(2);
                        faceOwners[key] = owners;
                    }
                    if (!owners.Contains(cellId)) owners.Add(cellId);
                }
            }
            var pairs = new HashSet<(int, int)>();
            var result = new List<(int A, int B)>();
            foreach (var kvp in faceOwners)
            {
                var owners = kvp.Value;
                if (owners.Count < 2) continue;
                for (int i = 0; i < owners.Count; i++)
                {
                    for (int j = i + 1; j < owners.Count; j++)
                    {
                        int a = owners[i], b = owners[j];
                        var key = a < b ? (a, b) : (b, a);
                        if (pairs.Add(key))
                        {
                            result.Add((key.Item1, key.Item2));
                        }
                    }
                }
            }
            return result;
        }

        private static Point3d FaceCentroid(Mesh m, MeshFace face)
        {
            var v0 = m.Vertices[face.A];
            var v1 = m.Vertices[face.B];
            var v2 = m.Vertices[face.C];
            if (face.IsQuad)
            {
                var v3 = m.Vertices[face.D];
                return new Point3d(
                    (v0.X + v1.X + v2.X + v3.X) * 0.25,
                    (v0.Y + v1.Y + v2.Y + v3.Y) * 0.25,
                    (v0.Z + v1.Z + v2.Z + v3.Z) * 0.25);
            }
            return new Point3d(
                (v0.X + v1.X + v2.X) / 3.0,
                (v0.Y + v1.Y + v2.Y) / 3.0,
                (v0.Z + v1.Z + v2.Z) / 3.0);
        }

        private static List<int> ResolveHoleProbes(
            Dictionary<int, Mesh> meshById, List<Point3d> probes)
        {
            var holeIds = new List<int>();
            foreach (var p in probes)
            {
                int bestId = -1;
                double bestDist = double.PositiveInfinity;
                foreach (var kvp in meshById)
                {
                    var c = MeshCentroid(kvp.Value);
                    double d = p.DistanceToSquared(c);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestId = kvp.Key;
                    }
                }
                if (bestId >= 0) holeIds.Add(bestId);
            }
            return holeIds;
        }
    }
}
