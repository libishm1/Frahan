#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino;
using Rhino.DocObjects;
using Rhino.FileIO;
using Rhino.Geometry;

namespace Frahan.GH.Kintsugi;

// =============================================================================
// Frahan > Kintsugi > Load Scan Fragments (PLY).
//
// Loads a set of scanned fragment .ply files (scattered, real fracture
// surfaces) and emits per-fragment point clouds + meshes ready to feed
// Frahan Kintsugi in Port mode. This is the INFERENCE path for real scans:
// no ground truth, no fine-tuning needed -- scattered fragments are exactly
// the input the model expects. Real scanned fracture surfaces are far closer
// to the Breaking Bad training distribution than synthetic Voronoi/COACD,
// so the pretrained model has a real chance here.
//
// Each .ply = one fragment. PLY may be a mesh (vertices + faces) or a raw
// point cloud (vertices only); both are handled. Files are read into a
// HEADLESS RhinoDoc so they never touch the user's active document.
//
// Wiring:
//   Load Scan Fragments  --Points (tree)-->  Frahan Kintsugi : Point Clouds
//                        --Fragments-------->  Frahan Kintsugi : Fragments
//   then Use Port Mode = True, Vt = 0.5, Diffusion Steps = 100.
// =============================================================================

[Algorithm("Load scanned PLY fragments for Kintsugi inference",
    "Reads scattered scanned fragment .ply files into per-fragment point " +
    "clouds + meshes for Frahan Kintsugi Port mode. Real-scan inference path " +
    "(no GT / fine-tuning needed); real fracture surfaces are closer to the " +
    "Breaking Bad training distribution than synthetic cuts.")]
[DesignApplication(
    "Load scanned fragment .ply files (mesh or point cloud) into  per-fragment point clouds + meshes for Frahan ...",
    DesignFlow.Bridges,
    Precedent = "Breaking Bad dataset (Sellan 2022) + Stanford PLY format (Greenberg Turk 1994)")]
public sealed class LoadScanFragmentsComponent : GH_Component
{
    public LoadScanFragmentsComponent()
        : base("Load Scan Fragments", "ScanFrags",
            "Load scanned fragment .ply files (mesh or point cloud) into " +
            "per-fragment point clouds + meshes for Frahan Kintsugi Port mode. " +
            "Wire Points -> Point Clouds and Fragments -> Fragments.",
            "Frahan", "Kintsugi")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("F2D00505-2026-4522-B0B0-1ABE15A0CAFE");

    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("LoadScanFragments.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("Folder", "D",
            "Optional folder to scan for *.ply (each file = one fragment). " +
            "Used when File Paths is empty.", GH_ParamAccess.item, "");
        p[0].Optional = true;
        p.AddTextParameter("File Paths", "F",
            "Optional explicit list of .ply file paths (one fragment each). " +
            "Takes priority over Folder.", GH_ParamAccess.list);
        p[1].Optional = true;
        p.AddIntegerParameter("Sample Count", "N",
            "Points emitted per fragment for the Point Clouds tree. " +
            "Upstream convention is 1000. Dense scans are subsampled; sparse " +
            "ones are repeated up to N.", GH_ParamAccess.item, 1000);
        p.AddBooleanParameter("Split Disjoint", "Sp",
            "TRUE = a single .ply containing many shards is SPLIT into separate " +
            "fragments (disjoint mesh pieces, or proximity-clustered points). " +
            "Use this for one-file-many-shards scans. FALSE = each .ply is one " +
            "fragment. Default TRUE.", GH_ParamAccess.item, true);
        p.AddNumberParameter("Cluster Tol", "Ct",
            "Point-cloud clustering distance (document units) when Split " +
            "Disjoint is on and the .ply is a raw cloud (no faces). 0 = auto " +
            "(bbox diagonal / 80). Ignored for mesh PLYs (uses disjoint faces).",
            GH_ParamAccess.item, 0.0);
        p.AddBooleanParameter("Remove Floor", "Rf",
            "TRUE = RANSAC-detect the dominant plane (the scan floor/ground) " +
            "and strip points near it BEFORE splitting, so resting shards " +
            "separate into individual fragments. Works at the point level " +
            "(shards become point clusters). Default FALSE.",
            GH_ParamAccess.item, false);
        p.AddNumberParameter("Floor Tol", "Ft",
            "Distance band (document units) around the detected floor plane to " +
            "remove. 0 = auto (bbox diagonal / 150). Raise if the floor isn't " +
            "fully removed; lower if shard bottoms get clipped.",
            GH_ParamAccess.item, 0.0);
        p.AddBooleanParameter("Run", "R", "Load.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddPointParameter("Points", "P",
            "Per-fragment points as a tree: one BRANCH per fragment, N points " +
            "per branch. Wire into Frahan Kintsugi -> Point Clouds.",
            GH_ParamAccess.tree);
        p.AddMeshParameter("Fragments", "Frag",
            "Per-fragment mesh (the PLY mesh if present, else a coarse " +
            "point-pulled mesh). Wire into Frahan Kintsugi -> Fragments.",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Fragment Count", "Nf",
            "Number of fragments loaded.", GH_ParamAccess.item);
        p.AddTextParameter("Report", "Rp", "Per-file load diagnostic.",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        string folder = "";
        var files = new List<string>();
        int sampleCount = 1000;
        bool splitDisjoint = true;
        double clusterTol = 0.0;
        bool removeFloor = false;
        double floorTol = 0.0;
        bool run = false;
        da.GetData(0, ref folder);
        da.GetDataList(1, files);
        da.GetData(2, ref sampleCount);
        da.GetData(3, ref splitDisjoint);
        da.GetData(4, ref clusterTol);
        da.GetData(5, ref removeFloor);
        da.GetData(6, ref floorTol);
        da.GetData(7, ref run);
        if (!run) { da.SetData(3, "Run is false. Toggle to load."); return; }
        if (sampleCount < 100) sampleCount = 100;

        // Resolve the fragment file list: explicit paths win; else glob folder.
        var paths = new List<string>();
        if (files != null && files.Count > 0)
        {
            foreach (var f in files) if (!string.IsNullOrWhiteSpace(f)) paths.Add(f);
        }
        else if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            paths.AddRange(Directory.GetFiles(folder, "*.ply", SearchOption.TopDirectoryOnly));
            paths.Sort(StringComparer.OrdinalIgnoreCase);
        }
        if (paths.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "No .ply files. Wire File Paths or a Folder containing *.ply.");
            return;
        }

        var pointTree = new Grasshopper.DataTree<Point3d>();
        var meshes = new List<Mesh>(paths.Count);
        var rep = new System.Text.StringBuilder();
        int loaded = 0;

        for (int f = 0; f < paths.Count; f++)
        {
            var path = paths[f];
            if (!File.Exists(path))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Missing: {path}");
                rep.AppendLine($"[{f}] MISSING {Path.GetFileName(path)}");
                continue;
            }

            var verts = new List<Point3d>();
            Mesh plyMesh = null;
            try { ReadPly(path, verts, ref plyMesh); }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Failed to read {Path.GetFileName(path)}: {ex.Message}");
                rep.AppendLine($"[{f}] ERROR {Path.GetFileName(path)}: {ex.Message}");
                continue;
            }

            // Split a single multi-shard PLY into separate fragment pieces.
            var pieces = BuildPieces(verts, plyMesh, splitDisjoint, clusterTol,
                                     removeFloor, floorTol, out string floorInfo);
            if (!string.IsNullOrEmpty(floorInfo))
                rep.AppendLine($"    floor: {floorInfo}");
            if (pieces.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{Path.GetFileName(path)} yielded no usable pieces; skipped.");
                rep.AppendLine($"[{f}] EMPTY {Path.GetFileName(path)}");
                continue;
            }

            for (int pieceIdx = 0; pieceIdx < pieces.Count; pieceIdx++)
            {
                var (srcPts, pieceMesh) = pieces[pieceIdx];
                if (srcPts == null || srcPts.Count < 4) continue;

                // Resample to exactly sampleCount: subsample (stride) if dense,
                // repeat (modulo) if sparse. Deterministic.
                var branchPath = new Grasshopper.Kernel.Data.GH_Path(loaded);
                int M = srcPts.Count;
                for (int i = 0; i < sampleCount; i++)
                {
                    int idx = (M >= sampleCount)
                        ? (int)((long)i * M / sampleCount)   // even stride
                        : (i % M);                            // repeat
                    pointTree.Add(srcPts[idx], branchPath);
                }

                var outMesh = pieceMesh ?? BuildCoarseMesh(srcPts);
                if (outMesh != null) outMesh.Normals.ComputeNormals();
                meshes.Add(outMesh);

                var bb = new BoundingBox(srcPts);
                rep.AppendLine($"[{loaded}] {Path.GetFileName(path)}" +
                               (pieces.Count > 1 ? $" piece {pieceIdx}" : "") +
                               $": {M} pts" +
                               (pieceMesh != null ? $", mesh F={pieceMesh.Faces.Count}" : ", cloud"));
                loaded++;
            }
        }

        if (loaded < 2)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "Kintsugi needs >= 2 fragments to assemble.");

        rep.AppendLine();
        rep.AppendLine($"Loaded {loaded} fragment(s), {sampleCount} pts each.");
        rep.AppendLine("Wire Points -> Kintsugi Point Clouds, Fragments -> Kintsugi Fragments.");
        rep.AppendLine("Port mode: Vt=0.5, Diffusion Steps=100, TorchSharp=True.");

        da.SetDataTree(0, pointTree);
        da.SetDataList(1, meshes);
        da.SetData(2, loaded);
        da.SetData(3, rep.ToString());
    }

    /// <summary>
    /// Turn a loaded PLY (cloud points + optional mesh) into fragment pieces.
    /// Split Disjoint ON: a mesh is split by SplitDisjointPieces (real shards);
    /// a raw cloud is clustered by proximity (voxel-connectivity). OFF or
    /// single-piece input: one fragment. Returns (points, mesh) per piece.
    /// </summary>
    private static List<(List<Point3d> pts, Mesh mesh)> BuildPieces(
        List<Point3d> verts, Mesh plyMesh, bool splitDisjoint, double clusterTol,
        bool removeFloor, double floorTol, out string floorInfo)
    {
        floorInfo = null;
        var result = new List<(List<Point3d>, Mesh)>();

        // Floor removal works at the POINT level: gather all points (mesh
        // vertices + cloud), strip the dominant plane, then cluster the rest.
        // This overrides the mesh-disjoint path (shards resting on a floor are
        // all one connected piece until the floor is gone).
        if (removeFloor)
        {
            var all = new List<Point3d>();
            if (verts != null) all.AddRange(verts);
            if (plyMesh != null)
                for (int v = 0; v < plyMesh.Vertices.Count; v++) all.Add(plyMesh.Vertices[v]);
            if (all.Count < 4) { return result; }

            var kept = RemoveDominantPlane(all, floorTol, out int removed, out floorInfo);
            foreach (var cluster in ClusterByProximity(kept, clusterTol))
                if (cluster.Count >= 4) result.Add((cluster, null));
            return result;
        }

        if (plyMesh != null && plyMesh.Faces.Count > 0)
        {
            Mesh[] pieces = splitDisjoint
                ? (plyMesh.SplitDisjointPieces() ?? new[] { plyMesh })
                : new[] { plyMesh };
            if (pieces == null || pieces.Length == 0) pieces = new[] { plyMesh };
            foreach (var pc in pieces)
            {
                if (pc == null || pc.Vertices.Count < 4) continue;
                var pts = new List<Point3d>(pc.Vertices.Count);
                for (int v = 0; v < pc.Vertices.Count; v++) pts.Add(pc.Vertices[v]);
                result.Add((pts, pc));
            }
            return result;
        }

        // Raw point cloud (no faces).
        if (verts == null || verts.Count < 4) return result;
        if (!splitDisjoint)
        {
            result.Add((verts, null));
            return result;
        }
        foreach (var cluster in ClusterByProximity(verts, clusterTol))
            if (cluster.Count >= 4) result.Add((cluster, null));
        if (result.Count == 0) result.Add((verts, null)); // fallback: one piece
        return result;
    }

    /// <summary>
    /// RANSAC dominant-plane detection + removal (the scan floor). Samples
    /// triplets over K iterations, fits a plane, counts inliers within `tol`,
    /// keeps the largest inlier set, and returns the points NOT on that plane.
    /// tol &lt;= 0 auto-picks bbox-diagonal / 150. Deterministic (fixed seed).
    /// </summary>
    private static List<Point3d> RemoveDominantPlane(
        List<Point3d> pts, double tol, out int removed, out string info)
    {
        removed = 0; info = null;
        var bb = new BoundingBox(pts);
        double diag = bb.Diagonal.Length;
        if (tol <= 0) tol = (diag > 1e-9 ? diag : 1.0) / 150.0;
        if (tol <= 1e-9) tol = 1.0;

        int n = pts.Count;
        var rng = new Random(12345);          // deterministic
        int iters = 200;
        int bestInliers = -1;
        Vector3d bestN = Vector3d.ZAxis;
        double bestD = 0;

        for (int it = 0; it < iters; it++)
        {
            var a = pts[rng.Next(n)];
            var b = pts[rng.Next(n)];
            var c = pts[rng.Next(n)];
            var ab = b - a; var ac = c - a;
            var nrm = Vector3d.CrossProduct(ab, ac);
            if (nrm.Length < 1e-9) continue;
            nrm.Unitize();
            double d = -(nrm.X * a.X + nrm.Y * a.Y + nrm.Z * a.Z);

            // Count inliers on a strided subset for speed.
            int step = Math.Max(1, n / 2000);
            int inliers = 0;
            for (int i = 0; i < n; i += step)
            {
                double dist = Math.Abs(nrm.X * pts[i].X + nrm.Y * pts[i].Y + nrm.Z * pts[i].Z + d);
                if (dist < tol) inliers++;
            }
            if (inliers > bestInliers) { bestInliers = inliers; bestN = nrm; bestD = d; }
        }

        // Keep points farther than tol from the best plane.
        var kept = new List<Point3d>(n);
        for (int i = 0; i < n; i++)
        {
            double dist = Math.Abs(bestN.X * pts[i].X + bestN.Y * pts[i].Y + bestN.Z * pts[i].Z + bestD);
            if (dist >= tol) kept.Add(pts[i]); else removed++;
        }
        info = $"plane n=({bestN.X:F2},{bestN.Y:F2},{bestN.Z:F2}) tol={tol:G3}, " +
               $"removed {removed}/{n} pts, {kept.Count} kept.";
        return kept;
    }

    /// <summary>
    /// Voxel-connectivity clustering: bin points into a grid of cell-size
    /// `tol`, union 26-neighbour occupied cells, group points by cluster.
    /// tol &lt;= 0 auto-picks bbox-diagonal / 80. O(N) with a dictionary grid.
    /// </summary>
    private static List<List<Point3d>> ClusterByProximity(List<Point3d> pts, double tol)
    {
        var bb = new BoundingBox(pts);
        double diag = bb.Diagonal.Length;
        if (tol <= 0) tol = (diag > 1e-9 ? diag : 1.0) / 80.0;
        if (tol <= 1e-9) tol = 1.0;
        double inv = 1.0 / tol;

        // cell key -> list of point indices
        var cells = new Dictionary<(int, int, int), List<int>>();
        var keyOf = new (int, int, int)[pts.Count];
        for (int i = 0; i < pts.Count; i++)
        {
            var k = ((int)Math.Floor(pts[i].X * inv),
                     (int)Math.Floor(pts[i].Y * inv),
                     (int)Math.Floor(pts[i].Z * inv));
            keyOf[i] = k;
            if (!cells.TryGetValue(k, out var lst)) { lst = new List<int>(); cells[k] = lst; }
            lst.Add(i);
        }

        // Union-find over occupied cells (26-neighbourhood).
        var cellKeys = new List<(int, int, int)>(cells.Keys);
        var cellIndex = new Dictionary<(int, int, int), int>();
        for (int c = 0; c < cellKeys.Count; c++) cellIndex[cellKeys[c]] = c;
        var parent = new int[cellKeys.Count];
        for (int c = 0; c < parent.Length; c++) parent[c] = c;
        int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
        void Union(int a, int b) { int ra = Find(a), rb = Find(b); if (ra != rb) parent[ra] = rb; }

        for (int c = 0; c < cellKeys.Count; c++)
        {
            var (x, y, z) = cellKeys[c];
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dy == 0 && dz == 0) continue;
                        if (cellIndex.TryGetValue((x + dx, y + dy, z + dz), out int nc))
                            Union(c, nc);
                    }
        }

        // Gather points per root cluster.
        var clusters = new Dictionary<int, List<Point3d>>();
        for (int i = 0; i < pts.Count; i++)
        {
            int root = Find(cellIndex[keyOf[i]]);
            if (!clusters.TryGetValue(root, out var lst)) { lst = new List<Point3d>(); clusters[root] = lst; }
            lst.Add(pts[i]);
        }
        return new List<List<Point3d>>(clusters.Values);
    }

    /// <summary>
    /// Read a .ply via RhinoCommon's FilePly into a HEADLESS doc, then pull
    /// out point-cloud points and/or a mesh. Headless = never touches the
    /// user's active document. Caller gets vertices (always, when present)
    /// and the first mesh (if any).
    /// </summary>
    private static void ReadPly(string path, List<Point3d> verts, ref Mesh mesh)
    {
        RhinoDoc doc = null;
        try
        {
            doc = RhinoDoc.CreateHeadless(null);
            FilePly.Read(path, doc, new FilePlyReadOptions());
            foreach (var obj in doc.Objects)
            {
                var g = obj?.Geometry;
                if (g is PointCloud pc)
                {
                    for (int i = 0; i < pc.Count; i++) verts.Add(pc[i].Location);
                }
                else if (g is Mesh m)
                {
                    if (mesh == null) mesh = m.DuplicateMesh();
                    else mesh.Append(m);
                }
                else if (g is Rhino.Geometry.Point pt)
                {
                    verts.Add(pt.Location);
                }
            }
        }
        finally { try { doc?.Dispose(); } catch { } }
    }

    /// <summary>
    /// Coarse display mesh for point-cloud PLYs (no faces): bbox subdivided
    /// 8^3, each vertex pulled to its nearest input point. Same accuracy-
    /// preferred trick as the BB Loader's style 0. Display only.
    /// </summary>
    private static Mesh BuildCoarseMesh(List<Point3d> pts)
    {
        try
        {
            var bb = new BoundingBox(pts);
            var m = Mesh.CreateFromBox(bb, 8, 8, 8);
            for (int v = 0; v < m.Vertices.Count; v++)
            {
                var mv = (Point3d)m.Vertices[v];
                Point3d nearest = pts[0];
                double best = mv.DistanceToSquared(pts[0]);
                for (int i = 1; i < pts.Count; i++)
                {
                    double d = mv.DistanceToSquared(pts[i]);
                    if (d < best) { best = d; nearest = pts[i]; }
                }
                m.Vertices.SetVertex(v, new Point3f((float)nearest.X, (float)nearest.Y, (float)nearest.Z));
            }
            m.Compact();
            return m;
        }
        catch { return null; }
    }
}
