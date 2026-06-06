#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Frahan.GH.Attributes;
using Frahan.Kintsugi.Port.Weights;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Kintsugi;

// =============================================================================
// Frahan > Kintsugi > Load Breaking Bad Sample.
//
// Reads a Breaking Bad sample (.bin in FRKINTSU format, produced by
// extract_breaking_bad_sample.py) and outputs the per-fragment point clouds
// PLUS convex-hull Meshes that Kintsugi can consume.
//
// Why this exists
// ---------------
// The PuzzleFusion++ model was trained on the Breaking Bad dataset --
// real fractured ceramics/sculptures with rich curvature + texture on
// fracture surfaces. Synthetic Voronoi shatters (planar cuts) are
// OUT-OF-DISTRIBUTION; even with all port-side fixes applied, the
// model cannot generalize to dead-flat polygonal interfaces.
//
// This component lets the user feed actual training-distribution
// fragments into Frahan Kintsugi to see end-to-end assembly working
// (verifier scores > 0.5, fragments approaching identity poses for
// ground-truth-aligned samples like bb_sample_00697 with 2 fragments).
//
// Workflow
// --------
//   1. Once: extract a sample via
//        python extract_breaking_bad_sample.py
//             --pc-zip D:\path\to\pc_data.zip
//             --sample 00697
//             --out D:\code_ws\...\reference\bb_sample_00697.bin
//   2. Drop "Load BB Sample" component, point Sample File to the .bin
//   3. Wire Fragments output to Frahan Kintsugi -> Mode=Port = True
//   4. Run -- expect verifier score > 0.5 on the 2-fragment sample.
//
// Convex hull is a coarse mesh approximation of the point cloud. It is
// sufficient for Kintsugi because Kintsugi re-samples points from the
// mesh surface anyway. The exact mesh shape doesn't drive the model;
// only the points sampled from it do.
// =============================================================================

[Algorithm("Breaking Bad fragment loader",
    "Loads upstream PuzzleFusion++ training-distribution fragments from " +
    "an .npz that has been pre-converted to FRKINTSU binary via " +
    "extract_breaking_bad_sample.py. Outputs per-fragment point clouds + " +
    "convex-hull meshes for direct feed into Frahan Kintsugi.")]
[DesignApplication(
    "Load a Breaking Bad sample (.bin from extract_breaking_bad_sample.py)  and output per-fragment point clouds...",
    DesignFlow.Bridges,
    Precedent = "Breaking Bad dataset (Sellan et al. 2022, NeurIPS) -- pre-converted to FRKINTSU .bin")]
public sealed class BreakingBadSampleLoaderComponent : GH_Component
{
    public BreakingBadSampleLoaderComponent()
        : base("Load BB Sample", "BBLoad",
            "Load a Breaking Bad sample (.bin from extract_breaking_bad_sample.py) " +
            "and output per-fragment point clouds + convex-hull meshes ready for " +
            "Frahan Kintsugi.",
            "Frahan", "Kintsugi")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("F2D00503-2026-4522-B0B0-1ABE15A0CAFE");

    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("LoadScanFragments.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("Sample File", "F",
            "Path to a FRKINTSU .bin produced by extract_breaking_bad_sample.py. " +
            "Defaults to the bb_sample_00697.bin generated during the parity work.",
            GH_ParamAccess.item,
            @"D:\code_ws\Template-General\outputs\2026-05-22\reference\bb_sample_00697.bin");
        p.AddIntegerParameter("Mesh Style", "MS",
            "How to convert the loaded point cloud into a Mesh for " +
            "downstream Frahan Kintsugi consumption:\n" +
            "  0 = point-cloud vertices (DEFAULT, accuracy-preferred). " +
            "Builds a 12^3 bbox-subdivided mesh and pulls each vertex " +
            "to its nearest input point. Rhino may flag the mesh as " +
            "invalid (overlapping verts), but Kintsugi's surface sampler " +
            "lands closer to the ORIGINAL point cloud, giving the encoder " +
            "more accurate features (better verifier scores).\n" +
            "  1 = convex hull (display-preferred). Clean manifold via " +
            "FPS-subsample + QuickHull; valid mesh but loses interior " +
            "fragment shape and fragments at curved fracture interfaces " +
            "will visibly interpenetrate when assembled.\n" +
            "  2 = bbox cube. Always valid, simplest, but Kintsugi sees " +
            "a cube and the encoder produces ~unrelated features.\n" +
            "  3 = high-resolution bbox-pulled (24^3). Use when style 0 " +
            "still leaves visible interpenetration between assembled " +
            "fragments at curved fracture surfaces. ~3750 surface " +
            "vertices per fragment; slower to display but tracks the " +
            "actual cloud surface closely.",
            GH_ParamAccess.item, 0);
        p.AddBooleanParameter("Run", "R", "Load.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddPointParameter("Fragment Points", "P",
            "Per-fragment 3D points (1000 per fragment, flattened across fragments). " +
            "Use the Branches output for per-fragment grouping.",
            GH_ParamAccess.tree);
        p.AddMeshParameter("Fragments", "Frag",
            "Per-fragment convex-hull Mesh (coarse approximation suitable for " +
            "Frahan Kintsugi -- it re-samples points from the mesh surface anyway). " +
            "Wire this directly into Frahan Kintsugi's Fragments input.",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Fragment Count", "N",
            "Number of fragments in the sample.", GH_ParamAccess.item);
        p.AddTextParameter("Report", "Rp",
            "Sample loader diagnostic.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        string path = "";
        int meshStyle = 0;
        bool run = false;
        if (!da.GetData(0, ref path)) return;
        da.GetData(1, ref meshStyle);
        // Old canvases without the Mesh Style input fall through with default 0.
        if (Params.Input.Count > 2) da.GetData(2, ref run);
        else da.GetData(1, ref run);
        if (!run)
        {
            da.SetData(3, "Run is false. Toggle to load.");
            return;
        }
        if (!File.Exists(path))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Sample file not found at '{path}'. " +
                "Generate one via extract_breaking_bad_sample.py.");
            return;
        }
        try
        {
            var reader = new WeightReader(path);
            // Required: bb.input.point_clouds [F, N=1000, 3]
            if (!ReaderHas(reader, "bb.input.point_clouds"))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Sample file does not contain bb.input.point_clouds.");
                return;
            }
            var pc = reader.GetFloat32("bb.input.point_clouds");
            var shape = reader.GetShape("bb.input.point_clouds");
            if (shape.Length != 3 || shape[2] != 3)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Expected [F, N, 3] point clouds, got [{string.Join(",", shape)}].");
                return;
            }
            int F = shape[0];
            int N = shape[1];

            var pointTree = new Grasshopper.DataTree<Point3d>();
            var meshes = new List<Mesh>(F);
            var rep = new System.Text.StringBuilder();
            rep.AppendLine($"Loaded {F} fragments, {N} points each from {Path.GetFileName(path)}.");

            for (int f = 0; f < F; f++)
            {
                var pts = new List<Point3d>(N);
                var path_gh = new Grasshopper.Kernel.Data.GH_Path(f);
                for (int i = 0; i < N; i++)
                {
                    double x = pc[(f * N + i) * 3 + 0];
                    double y = pc[(f * N + i) * 3 + 1];
                    double z = pc[(f * N + i) * 3 + 2];
                    var p = new Point3d(x, y, z);
                    pts.Add(p);
                    pointTree.Add(p, path_gh);
                }
                // Build a coarse mesh from the points so Kintsugi can
                // sample its surface. Style controlled by Mesh Style input.
                var hullMesh = BuildPointCloudMesh(pts, meshStyle);
                if (hullMesh == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"Failed to build mesh for fragment {f}; skipping.");
                    continue;
                }
                meshes.Add(hullMesh);
                var bb = hullMesh.GetBoundingBox(true);
                rep.AppendLine($"  Fragment {f}: {pts.Count} pts, " +
                               $"bbox=[{bb.Min.X:F2},{bb.Min.Y:F2},{bb.Min.Z:F2}]..." +
                               $"[{bb.Max.X:F2},{bb.Max.Y:F2},{bb.Max.Z:F2}], " +
                               $"mesh V={hullMesh.Vertices.Count} F={hullMesh.Faces.Count}.");
            }
            rep.AppendLine();
            rep.AppendLine("Wire the Fragments output into Frahan Kintsugi (Use Port Mode=True).");
            rep.AppendLine("On training-distribution data the verifier should score pairs >0.5;");
            rep.AppendLine("this confirms the C# port reproduces the paper's behaviour on the");
            rep.AppendLine("test set the model was actually trained on.");

            da.SetDataTree(0, pointTree);
            da.SetDataList(1, meshes);
            da.SetData(2, F);
            da.SetData(3, rep.ToString());
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Load failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Build a coarse mesh from a point cloud via Rhino's
    /// PointCloud -> Mesh conversion. Uses ball-pivoting if available,
    /// otherwise falls back to projecting + Delaunay-like triangulation.
    /// </summary>
    private static Mesh BuildPointCloudMesh(List<Point3d> pts, int meshStyle)
    {
        if (pts == null || pts.Count < 4) return null;
        // Style 0 = point-cloud vertices (accuracy preferred -- Libish's
        // default; gives Kintsugi's surface sampler points closer to the
        // original cloud).
        //
        // 2026-05-24: subdivision bumped from 4 -> 12 so the bbox mesh
        // has ~6*13*13 = 1014 surface vertices (matching the 1000-point
        // cloud), letting each vertex pull to a distinct cloud point. The
        // 4^3 grid only had 150 vertices, smoothing the fracture surface
        // enough that two assembled fragments visibly interpenetrated
        // even when the pose was correct (Kintsugi HitL 2026-05-24).
        if (meshStyle == 0)
        {
            try
            {
                return BuildBboxPulledMesh(pts, divisions: 12);
            }
            catch { /* fall through */ }
        }

        // Style 3 = high-resolution bbox-pulled mesh (24^3 grid). Use when
        // style 0 still leaves visible interpenetration between assembled
        // fragments. Produces ~6*25*25 = 3750 surface vertices per
        // fragment so the mesh tracks the actual cloud surface (including
        // jagged fracture rims) closely. Slower than style 0 but the
        // assembly looks tight.
        if (meshStyle == 3)
        {
            try
            {
                return BuildBboxPulledMesh(pts, divisions: 24);
            }
            catch { /* fall through */ }
        }

        // Style 1 = convex hull (display preferred; FPS-subsample first to
        // keep QuickHull fast).
        if (meshStyle == 1)
        {
            try
            {
                var hullInput = pts.Count > 64
                    ? FurthestPointSubsample(pts, 64)
                    : pts;
                var hull = QuickHull3D(hullInput);
                if (hull != null && hull.IsValid && hull.Faces.Count >= 4)
                    return hull;
            }
            catch { /* fall through */ }
        }

        // Style 2 (or any fallback) = bbox cube.
        try
        {
            var bbox = new BoundingBox(pts);
            var box = Mesh.CreateFromBox(bbox, 1, 1, 1);
            box.Normals.ComputeNormals();
            return box;
        }
        catch { return null; }
    }

    /// <summary>
    /// Build a bbox surface mesh subdivided into divisions^3 cells, then
    /// pull each surface vertex to its nearest cloud point. Result is a
    /// mesh that follows the point-cloud surface; the higher the
    /// divisions, the tighter the approximation. Cost is O(V * N) where
    /// V = 6*(divisions+1)^2 and N = pts.Count.
    /// </summary>
    private static Mesh BuildBboxPulledMesh(List<Point3d> pts, int divisions)
    {
        var bb = new BoundingBox(pts);
        var m = Mesh.CreateFromBox(bb, divisions, divisions, divisions);
        for (int v = 0; v < m.Vertices.Count; v++)
        {
            var mv = (Point3d)m.Vertices[v];
            Point3d nearest = pts[0];
            double bestD = mv.DistanceToSquared(pts[0]);
            for (int i = 1; i < pts.Count; i++)
            {
                double d = mv.DistanceToSquared(pts[i]);
                if (d < bestD) { bestD = d; nearest = pts[i]; }
            }
            m.Vertices.SetVertex(v, new Point3f((float)nearest.X, (float)nearest.Y, (float)nearest.Z));
        }
        m.Compact();
        m.Normals.ComputeNormals();
        return m;
    }

    /// <summary>
    /// O(K*N) furthest-point sampling: start with point 0, iteratively
    /// add the point with the maximum minimum-distance to the existing
    /// selection. Result is K well-spread points -- ideal candidates
    /// for a convex hull because the hull only "cares" about extreme
    /// points, and FPS naturally returns them.
    /// </summary>
    private static List<Point3d> FurthestPointSubsample(List<Point3d> src, int K)
    {
        int N = src.Count;
        if (K >= N) return new List<Point3d>(src);
        var dists = new double[N];
        for (int i = 0; i < N; i++) dists[i] = double.MaxValue;
        var chosen = new List<Point3d>(K);
        int last = 0;
        chosen.Add(src[last]);
        for (int k = 1; k < K; k++)
        {
            // Update min-distance to chosen set.
            var p = src[last];
            for (int i = 0; i < N; i++)
            {
                double dx = src[i].X - p.X;
                double dy = src[i].Y - p.Y;
                double dz = src[i].Z - p.Z;
                double d2 = dx * dx + dy * dy + dz * dz;
                if (d2 < dists[i]) dists[i] = d2;
            }
            // Pick farthest.
            int farthest = 0;
            double maxD = -1;
            for (int i = 0; i < N; i++)
                if (dists[i] > maxD) { maxD = dists[i]; farthest = i; }
            chosen.Add(src[farthest]);
            last = farthest;
        }
        return chosen;
    }

    /// <summary>
    /// Simple incremental 3D convex hull (QuickHull). Returns a valid
    /// manifold Mesh of the hull. O(N^2) worst case; fine for N~1000.
    /// </summary>
    private static Mesh QuickHull3D(List<Point3d> pts)
    {
        if (pts.Count < 4) return null;
        // 1. Find initial tetrahedron from extreme points.
        int idxMinX = 0, idxMaxX = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            if (pts[i].X < pts[idxMinX].X) idxMinX = i;
            if (pts[i].X > pts[idxMaxX].X) idxMaxX = i;
        }
        if (idxMinX == idxMaxX) return null;
        var lineAB = new Line(pts[idxMinX], pts[idxMaxX]);
        int idxFarLine = -1; double maxLineDist = -1;
        for (int i = 0; i < pts.Count; i++)
        {
            if (i == idxMinX || i == idxMaxX) continue;
            double d = lineAB.DistanceTo(pts[i], true);
            if (d > maxLineDist) { maxLineDist = d; idxFarLine = i; }
        }
        if (idxFarLine < 0 || maxLineDist < 1e-9) return null;
        Plane planeABC;
        try { planeABC = new Plane(pts[idxMinX], pts[idxMaxX], pts[idxFarLine]); }
        catch { return null; }
        int idxFarPlane = -1; double maxPlaneDist = -1;
        for (int i = 0; i < pts.Count; i++)
        {
            if (i == idxMinX || i == idxMaxX || i == idxFarLine) continue;
            double d = Math.Abs(planeABC.DistanceTo(pts[i]));
            if (d > maxPlaneDist) { maxPlaneDist = d; idxFarPlane = i; }
        }
        if (idxFarPlane < 0 || maxPlaneDist < 1e-9) return null;

        // Initial tetrahedron: 4 faces oriented with outward normals.
        int A = idxMinX, B = idxMaxX, C = idxFarLine, D = idxFarPlane;
        var faces = new List<int[]>();
        AddCcw(faces, pts, A, B, C, D);
        AddCcw(faces, pts, A, D, B, C);
        AddCcw(faces, pts, B, D, C, A);
        AddCcw(faces, pts, C, D, A, B);

        var assigned = new HashSet<int> { A, B, C, D };
        int safetyCap = 5000;
        while (safetyCap-- > 0)
        {
            // Find farthest external point across all faces.
            int bestFi = -1, bestPi = -1; double bestDist = 1e-9;
            for (int fi = 0; fi < faces.Count; fi++)
            {
                var face = faces[fi];
                Plane plane;
                try { plane = new Plane(pts[face[0]], pts[face[1]], pts[face[2]]); }
                catch { continue; }
                for (int i = 0; i < pts.Count; i++)
                {
                    if (assigned.Contains(i)) continue;
                    double signed = plane.DistanceTo(pts[i]);
                    if (signed > bestDist) { bestDist = signed; bestFi = fi; bestPi = i; }
                }
            }
            if (bestFi < 0 || bestPi < 0) break;

            // Find visible faces (those whose outward normal sees bestPi).
            var visible = new HashSet<int>();
            for (int fi = 0; fi < faces.Count; fi++)
            {
                var face = faces[fi];
                Plane plane;
                try { plane = new Plane(pts[face[0]], pts[face[1]], pts[face[2]]); }
                catch { continue; }
                if (plane.DistanceTo(pts[bestPi]) > 1e-9) visible.Add(fi);
            }
            if (visible.Count == 0) break;

            // Horizon = edges of visible faces NOT shared with other visible faces.
            var horizon = new List<int[]>();
            foreach (var vfi in visible)
            {
                var f = faces[vfi];
                for (int e = 0; e < 3; e++)
                {
                    int v0 = f[e], v1 = f[(e + 1) % 3];
                    bool sharedWithVisible = false;
                    foreach (var ofi in visible)
                    {
                        if (ofi == vfi) continue;
                        var of = faces[ofi];
                        if ((of[0] == v1 && of[1] == v0)
                         || (of[1] == v1 && of[2] == v0)
                         || (of[2] == v1 && of[0] == v0))
                        { sharedWithVisible = true; break; }
                    }
                    if (!sharedWithVisible) horizon.Add(new[] { v0, v1 });
                }
            }
            // Remove visible faces; add new ones from bestPi to horizon.
            var newFaces = new List<int[]>();
            for (int fi = 0; fi < faces.Count; fi++)
                if (!visible.Contains(fi)) newFaces.Add(faces[fi]);
            foreach (var edge in horizon)
                newFaces.Add(new[] { edge[0], edge[1], bestPi });
            faces = newFaces;
            assigned.Add(bestPi);
        }

        // Build the final Rhino Mesh from used vertices + faces.
        var usedIdx = new HashSet<int>();
        foreach (var f in faces) { usedIdx.Add(f[0]); usedIdx.Add(f[1]); usedIdx.Add(f[2]); }
        var idxMap = new Dictionary<int, int>();
        var mesh = new Mesh();
        foreach (var i in usedIdx) { idxMap[i] = mesh.Vertices.Count; mesh.Vertices.Add(pts[i]); }
        foreach (var f in faces) mesh.Faces.AddFace(idxMap[f[0]], idxMap[f[1]], idxMap[f[2]]);
        mesh.Vertices.CombineIdentical(true, true);
        mesh.Faces.CullDegenerateFaces();
        mesh.Normals.ComputeNormals();
        mesh.UnifyNormals();
        mesh.Compact();
        return mesh;
    }

    /// <summary>Add a triangle (a, b, c) with outward normal pointing away from `oppose`.</summary>
    private static void AddCcw(List<int[]> faces, List<Point3d> pts, int a, int b, int c, int oppose)
    {
        try
        {
            var plane = new Plane(pts[a], pts[b], pts[c]);
            if (plane.DistanceTo(pts[oppose]) > 0) faces.Add(new[] { a, c, b });
            else faces.Add(new[] { a, b, c });
        }
        catch { /* degenerate, skip */ }
    }

    private static bool ReaderHas(WeightReader r, string name)
    {
        foreach (var n in r.Names) if (n == name) return true;
        return false;
    }
}
