#nullable disable
using System;
using System.Collections.Generic;
using System.Threading;
using Frahan.Core.ScanIngest;
using Frahan.GH.Attributes;
using Frahan.GH.ScanIngest;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Interfaces;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH;

// =============================================================================
// Geogram test components - Grasshopper surfaces that exercise the
// native Geogram shim end-to-end. Mirrors the CGAL/CoACD test-component
// pattern (CgalConvert helpers, IsAvailable / Version probe, same
// "Available + Report" output convention).
//
// Ribbon: "Frahan" / "Geogram" sibling subcategory next to "CGAL" and
// "CoACD".
// =============================================================================

[Algorithm("Vertex-clustering decimation", "Levy, B., INRIA/ALICE. Geogram v1.9.9. BSD-3. https://github.com/BrunoLevy/geogram", WikiPath = "wiki/index/references.md")]
[RelatedComponent("Frahan > Mesh > Mesh Repair", Reason = "Mesh-quality production path; no production decimate component yet.")]
[DesignApplication(
    "Vertex-clustering decimation via Geogram  (GEO::mesh_decimate_vertex_clustering)",
    DesignFlow.Bridges,
    Precedent = "Geogram GEO::mesh_decimate (Levy INRIA)")]
public sealed class GeogramMeshDecimateComponent : FrahanComponentBase
{
    public GeogramMeshDecimateComponent()
        : base("Mesh Decimate (Geogram)", "DecimateGeogram",
            "Vertex-clustering decimation via Geogram " +
            "(GEO::mesh_decimate_vertex_clustering). Voxel-bin algorithm: " +
            "higher Bins = more detail. Different from CGAL's edge-collapse " +
            "decimation - use this for very high-poly scans where you want " +
            "controlled spatial sampling, and CGAL's for precise count " +
            "targeting. " +
            "Wraps Geogram mesh_decimate_vertex_clustering.",
            "Frahan", "Mesh")
    {
    }

    // Fresh GUID series for Geogram components: F2D000C? prefix.
    public override Guid ComponentGuid => new Guid("F2D000C0-6E06-4F2D-A0C0-7E60660C0AC1");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override System.Drawing.Bitmap Icon => IconProvider.Load("Downsample.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M",
            "Input mesh.", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Bins", "B",
            "Voxel grid resolution per bbox dimension. Higher = more " +
            "detail (less aggressive decimation). Typical 50..300; " +
            "default 100. Minimum 2.",
            GH_ParamAccess.item, 100);
        pManager.AddIntegerParameter("Mode", "Mo",
            "Bitwise OR of mode flags:\n" +
            "  0 = FAST (no extra cleanup)\n" +
            "  1 = REMOVE_DUPLICATES\n" +
            "  2 = REMOVE_DEGREE_3\n" +
            "  4 = KEEP_BORDERS\n" +
            "  7 = DEFAULT (1|2|4)",
            GH_ParamAccess.item, 7);
        pManager.AddBooleanParameter("Run", "Run",
            "Set true to compute.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Decimated", "M",
            "Decimated mesh.", GH_ParamAccess.item);
        pManager.AddTextParameter("Backend", "B",
            "Reported version from the loaded shim.",
            GH_ParamAccess.item);
        pManager.AddBooleanParameter("Available", "Av",
            "True iff the Geogram native shim is loadable.",
            GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R",
            "Diagnostic report (V/F counts in/out, runtime).",
            GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh m = null;
        int bins = 100;
        int mode = 7;
        bool run = false;

        if (!da.GetData(0, ref m)) return;
        da.GetData(1, ref bins);
        da.GetData(2, ref mode);
        da.GetData(3, ref run);

        var available = GeogramMesh.IsAvailable;
        da.SetData(1, GeogramMesh.Version);
        da.SetData(2, available);

        if (!run)
        {
            da.SetData(3, available
                ? "Run is false. Geogram shim is loaded and ready."
                : "Run is false. Geogram shim NOT loaded; cannot decimate.");
            return;
        }
        if (!available)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Geogram native shim not loaded - decimation requires Geogram. " +
                "Build from native/geogram_shim/ and place the DLL alongside " +
                "Frahan.StonePack.gha.");
            return;
        }
        if (m == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh is required.");
            return;
        }
        if (bins < 2)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Bins must be >= 2 (typical 50..300).");
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var snap = CgalConvert.ToSnapshot(m);
            var inV = snap.VertexCount;
            var inT = snap.TriangleCount;
            var decimated = GeogramMesh.DecimateMesh(snap, bins, (GeogramDecimateMode)mode);
            sw.Stop();

            var outMesh = CgalConvert.FromSnapshot(decimated);
            da.SetData(0, outMesh);
            da.SetData(3,
                $"Bins       : {bins}\n" +
                $"Mode       : {(GeogramDecimateMode)mode} ({mode})\n" +
                $"Input      : {inV}V / {inT}F\n" +
                $"Output     : {decimated.VertexCount}V / {decimated.TriangleCount}F\n" +
                $"Reduction  : V {(inV > 0 ? (1.0 - decimated.VertexCount/(double)inV) * 100.0 : 0.0):F1}%, " +
                $"F {(inT > 0 ? (1.0 - decimated.TriangleCount/(double)inT) * 100.0 : 0.0):F1}%\n" +
                $"Runtime    : {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Geogram decimate failed: {ex.GetType().Name}: {ex.Message}");
            da.SetData(3, $"FAILED: {ex.Message}");
        }
    }
}

// =============================================================================
// Mesh Repair (Geogram) - BSD-3 parallel to Mesh Repair (CGAL).
// =============================================================================

[Algorithm("Geogram mesh repair", "Levy, B., INRIA/ALICE. Geogram v1.9.9. BSD-3. https://github.com/BrunoLevy/geogram", WikiPath = "wiki/index/references.md")]
[RelatedComponent("Frahan > Mesh > Mesh Repair", Reason = "Production mesh-repair component; this Geogram variant is the research probe.")]
[RelatedComponent("Frahan > Mesh > Mesh Diagnostics", Reason = "Diagnose before repairing.")]
[DesignApplication(
    "Topology-aware mesh repair via GEO::mesh_repair  (colocate + remove duplicate facets + triangulate)",
    DesignFlow.Bridges,
    Precedent = "Geogram GEO::mesh_repair (Levy INRIA)")]
public sealed class GeogramMeshRepairComponent : FrahanComponentBase
{
    public GeogramMeshRepairComponent()
        : base("Mesh Repair (Geogram)", "RepairGeogram",
            "Topology-aware mesh repair via GEO::mesh_repair " +
            "(colocate + remove duplicate facets + triangulate). BSD-3. " +
            "Wraps Geogram mesh_repair.",
            "Frahan", "Mesh") { }
    public override Guid ComponentGuid => new Guid("F2D000C1-6E06-4F2D-A0C1-7E60660C0AC1");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override System.Drawing.Bitmap Icon => IconProvider.Load("PoissonReconstruct.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh.", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Mode", "Mo",
            "Bitwise OR of GeogramRepairMode flags:\n" +
            "  0 = TopologyOnly   (always done; dissociate non-manifold)\n" +
            "  1 = Colocate       (merge identical vertices)\n" +
            "  2 = RemoveDupFacets\n" +
            "  4 = Triangulate    (force triangulation)\n" +
            "  7 = Default = 1|2|4",
            GH_ParamAccess.item, 7);
        pManager.AddNumberParameter("Colocate Eps", "Eps",
            "Tolerance for COLOCATE merge (0 = exact only). Match scene units.",
            GH_ParamAccess.item, 0.0);
        pManager.AddBooleanParameter("Run", "Run", "Set true to compute.", GH_ParamAccess.item, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Repaired", "M", "Repaired mesh.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Available", "Av", "True iff Geogram shim loaded.", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R", "Diagnostic report.", GH_ParamAccess.item);
    }
    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh m = null; int mode = 7; double eps = 0.0; bool run = false;
        if (!da.GetData(0, ref m)) return;
        da.GetData(1, ref mode); da.GetData(2, ref eps); da.GetData(3, ref run);
        var av = GeogramMesh.IsAvailable; da.SetData(1, av);
        if (!run) { da.SetData(2, av ? "Run is false." : "Geogram shim NOT loaded."); return; }
        if (!av) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Geogram shim not loaded."); return; }
        if (m == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh required."); return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var snap = CgalConvert.ToSnapshot(m);
            var inV = snap.VertexCount; var inT = snap.TriangleCount;
            var rep = GeogramMesh.RepairMesh(snap, (GeogramRepairMode)mode, eps);
            sw.Stop();
            da.SetData(0, CgalConvert.FromSnapshot(rep));
            da.SetData(2,
                $"Mode    : {(GeogramRepairMode)mode} ({mode}) eps={eps}\n" +
                $"Input   : {inV}V / {inT}F\n" +
                $"Output  : {rep.VertexCount}V / {rep.TriangleCount}F\n" +
                $"Runtime : {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex) {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Geogram repair failed: {ex.Message}");
            da.SetData(2, $"FAILED: {ex.Message}");
        }
    }
}

// =============================================================================
// OBB (Geogram) - PCA-based OBB, lighter than CGAL OBB (no Eigen).
// =============================================================================

[Algorithm("PCA oriented bounding box", "Frahan-original", Note = "Textbook covariance/eigendecomposition PCA-OBB via Geogram PrincipalAxes3d; no single canonical paper")]
[RelatedComponent("Frahan > Quarry > BlockCutOpt Solve", Reason = "OBB primitive is used inside the BlockCutOpt solver inner loop (I2 BVH pruning).")]
[RelatedComponent("Frahan > Mesh > Bench From Mesh", Reason = "Quarry-bench mesh inputs benefit from OBB analysis.")]
[DesignApplication(
    "Oriented bounding box via PrincipalAxes3d (PCA)",
    DesignFlow.Bridges,
    Precedent = "Geogram PrincipalAxes3d / OBB")]
public sealed class GeogramObbComponent : FrahanComponentBase
{
    public GeogramObbComponent()
        : base("OBB (Geogram)", "ObbGeogram",
            "Oriented bounding box via PrincipalAxes3d (PCA). " +
            "BSD-3 parallel to OBB (CGAL); no Eigen dependency. " +
            "Frahan-original method.",
            "Frahan", "Mesh") { }
    public override Guid ComponentGuid => new Guid("F2D000C2-6E06-4F2D-A0C2-7E60660C0AC1");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override System.Drawing.Bitmap Icon => IconProvider.Load("MeshBvh.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh (triangles ignored; uses vertex point cloud).", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Run", "Run", "Set true to compute.", GH_ParamAccess.item, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddBoxParameter("OBB", "B", "Oriented bounding box.", GH_ParamAccess.item);
        pManager.AddPlaneParameter("Plane", "P", "Box origin frame.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Available", "Av", "True iff Geogram shim loaded.", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R", "Diagnostic report.", GH_ParamAccess.item);
    }
    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh m = null; bool run = false;
        if (!da.GetData(0, ref m)) return;
        da.GetData(1, ref run);
        var av = GeogramMesh.IsAvailable; da.SetData(2, av);
        if (!run) { da.SetData(3, av ? "Run is false." : "Geogram shim NOT loaded."); return; }
        if (!av) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Geogram shim not loaded."); return; }
        if (m == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh required."); return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var snap = CgalConvert.ToSnapshot(m);
            var obb = GeogramMesh.OrientedBoundingBox(snap.VertexCoordsXyz, snap.TriangleIndices);
            sw.Stop();
            var origin = new Point3d(obb.OriginX, obb.OriginY, obb.OriginZ);
            var xAxis = new Vector3d(obb.XAxisX, obb.XAxisY, obb.XAxisZ);
            var yAxis = new Vector3d(obb.YAxisX, obb.YAxisY, obb.YAxisZ);
            var plane = new Plane(origin, xAxis, yAxis);
            var box = new Box(plane,
                new Interval(0, obb.ExtentX),
                new Interval(0, obb.ExtentY),
                new Interval(0, obb.ExtentZ));
            da.SetData(0, box);
            da.SetData(1, plane);
            da.SetData(3,
                $"Origin  : ({obb.OriginX:F3}, {obb.OriginY:F3}, {obb.OriginZ:F3})\n" +
                $"Extents : ({obb.ExtentX:F3}, {obb.ExtentY:F3}, {obb.ExtentZ:F3})\n" +
                $"Volume  : {obb.ExtentX * obb.ExtentY * obb.ExtentZ:F3}\n" +
                $"Runtime : {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex) {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Geogram OBB failed: {ex.Message}");
            da.SetData(3, $"FAILED: {ex.Message}");
        }
    }
}

// =============================================================================
// Mesh Remesh (Geogram) - uniform remeshing via remesh_smooth.
// =============================================================================

[Algorithm("Centroidal-Voronoi remeshing", "Levy, B., INRIA/ALICE. Geogram v1.9.9. BSD-3. https://github.com/BrunoLevy/geogram", WikiPath = "wiki/index/references.md")]
[RelatedComponent("Frahan > Mesh > Mesh Repair", Reason = "Mesh-quality production path; remesh is the research variant for adaptive density.")]
[RelatedComponent("Frahan > Mesh > Mesh Diagnostics", Reason = "Pre-remesh diagnostic.")]
public sealed class GeogramRemeshComponent
    : AsyncScanComponent<GeogramRemeshComponent.Snapshot, GeogramRemeshComponent.Payload>
{
    public GeogramRemeshComponent()
        : base("Mesh Remesh (Geogram)", "RemeshGeogram",
            "Uniform surface remeshing via centroidal-Voronoi-driven " +
            "Lloyd + Newton optimization (GEO::remesh_smooth). Accepts a direct " +
            "Mesh OR a File Path (.ply / .obj / .stl / .wrl; takes precedence). " +
            "Runs on a background thread (Run gate) so the canvas never freezes. " +
            "Wraps Geogram remesh_smooth.",
            "Frahan", "Mesh") { }
    public override Guid ComponentGuid => new Guid("F2D000C3-6E06-4F2D-A0C3-7E60660C0AC1");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override System.Drawing.Bitmap Icon => IconProvider.Load("SurfaceTile.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh (optional if File Path is given).", GH_ParamAccess.item);
        pManager[0].Optional = true;
        pManager.AddIntegerParameter("Points", "N",
            "Desired vertex count in output (5000..50000 typical).",
            GH_ParamAccess.item, 5000);
        pManager.AddIntegerParameter("Lloyd Iters", "L",
            "Lloyd relaxation iterations (default 5).",
            GH_ParamAccess.item, 5);
        pManager.AddIntegerParameter("Newton Iters", "Nw",
            "Newton iterations after Lloyd (default 30).",
            GH_ParamAccess.item, 30);
        pManager.AddBooleanParameter("Run", "Run", "Set true to compute.", GH_ParamAccess.item, false);
        // Appended last so existing canvases keep their wiring. When given (and
        // the file exists) it takes precedence over the direct Mesh input.
        pManager.AddTextParameter("File Path", "F",
            "Optional mesh file to remesh directly (.ply / .obj / .stl / .wrl). " +
            "Takes precedence over the Mesh input. Empty = use the Mesh input.",
            GH_ParamAccess.item);
        pManager[5].Optional = true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Remeshed", "M", "Remeshed surface.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Available", "Av", "True iff Geogram shim loaded.", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R", "Diagnostic report.", GH_ParamAccess.item);
    }
    public sealed class Snapshot
    {
        public double[] Verts;   // flattened input (UI-thread captured) when from Mesh
        public int[] Tris;
        public string FilePath;  // when from a file (loaded off-thread)
        public int N, Lloyd, Newton;
        public string Source;
    }

    public sealed class Payload
    {
        public double[] Verts;   // remeshed output; Rhino Mesh built in EmitResult
        public int[] Tris;
        public int InV, InT, OutV, OutT, N, Lloyd, Newton;
        public long Ms;
        public string Source;
    }

    protected override bool TryRead(IGH_DataAccess da, out bool run, out Snapshot snapshot)
    {
        run = false; snapshot = null;
        da.GetData(4, ref run);
        if (!run) return true;
        if (!GeogramMesh.IsAvailable)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Geogram shim not loaded."); return false; }

        int n = 5000, lloyd = 5, newton = 30; string path = null;
        da.GetData(1, ref n); da.GetData(2, ref lloyd); da.GetData(3, ref newton);
        da.GetData(5, ref path);
        var s = new Snapshot { N = n, Lloyd = lloyd, Newton = newton };

        if (!string.IsNullOrWhiteSpace(path))
        {
            if (!System.IO.File.Exists(path))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "File not found: " + path); return false; }
            s.FilePath = path; s.Source = "file: " + System.IO.Path.GetFileName(path);
        }
        else
        {
            Mesh m = null;
            if (!da.GetData(0, ref m) || m == null || m.Faces.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Provide a Mesh or a File Path."); return false; }
            // Flatten the Rhino mesh on the UI thread (no Rhino geometry off-thread).
            var work = m.DuplicateMesh();
            work.Faces.ConvertQuadsToTriangles();
            FlattenMesh(work, out s.Verts, out s.Tris);
            s.Source = "Mesh input";
        }
        snapshot = s;
        return true;
    }

    protected override Payload Compute(Snapshot s, CancellationToken token, Action<string> progress)
    {
        token.ThrowIfCancellationRequested();
        double[] verts; int[] tris;
        if (s.FilePath != null)
        {
            progress("reading " + System.IO.Path.GetFileName(s.FilePath) + "...");
            LoadFlattenedFromFile(s.FilePath, out verts, out tris);
        }
        else { verts = s.Verts; tris = s.Tris; }

        if (verts == null || verts.Length < 9 || tris == null || tris.Length < 3)
            throw new InvalidOperationException("No triangles to remesh (empty / invalid input).");
        token.ThrowIfCancellationRequested();

        var inSnap = new MeshSnapshot(verts, tris);
        progress($"remeshing ({inSnap.TriangleCount} tris -> ~{s.N} pts)...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rem = GeogramMesh.RemeshUniform(inSnap, s.N, s.Lloyd, s.Newton);
        sw.Stop();
        token.ThrowIfCancellationRequested();

        return new Payload
        {
            Verts = ToArr(rem.VertexCoordsXyz),
            Tris = ToArrI(rem.TriangleIndices),
            InV = inSnap.VertexCount, InT = inSnap.TriangleCount,
            OutV = rem.VertexCount, OutT = rem.TriangleCount,
            N = s.N, Lloyd = s.Lloyd, Newton = s.Newton,
            Ms = sw.ElapsedMilliseconds, Source = s.Source,
        };
    }

    protected override void EmitResult(IGH_DataAccess da, Payload r)
    {
        Mesh mesh = BuildMesh(r.Verts, r.Tris);   // Rhino geometry built on the UI thread
        da.SetData(0, mesh);
        da.SetData(1, true);
        da.SetData(2,
            $"Source    : {r.Source}\n" +
            $"Target N  : {r.N}\n" +
            $"Iters     : Lloyd={r.Lloyd} Newton={r.Newton}\n" +
            $"Input     : {r.InV}V / {r.InT}F\n" +
            $"Output    : {r.OutV}V / {r.OutT}F\n" +
            $"Runtime   : {r.Ms} ms");
    }

    protected override void EmitIdle(IGH_DataAccess da, string message)
    {
        da.SetData(1, GeogramMesh.IsAvailable);
        da.SetData(2, message);
        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, message);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static void FlattenMesh(Mesh m, out double[] verts, out int[] tris)
    {
        int nv = m.Vertices.Count;
        verts = new double[3 * nv];
        for (int i = 0; i < nv; i++)
        {
            var p = m.Vertices[i];
            verts[3 * i + 0] = p.X; verts[3 * i + 1] = p.Y; verts[3 * i + 2] = p.Z;
        }
        var tl = new List<int>(m.Faces.Count * 3);
        for (int f = 0; f < m.Faces.Count; f++)
        {
            var face = m.Faces[f];
            tl.Add(face.A); tl.Add(face.B); tl.Add(face.C);
            if (face.IsQuad) { tl.Add(face.A); tl.Add(face.C); tl.Add(face.D); }
        }
        tris = tl.ToArray();
    }

    private static void LoadFlattenedFromFile(string path, out double[] verts, out int[] tris)
    {
        IReadOnlyList<ScanMesh> scans = MultiFormatMeshReader.ReadFile(path);
        var vl = new List<double>();
        var tl = new List<int>();
        foreach (var sm in scans)
        {
            if (sm == null || sm.VertexCoordsXyz == null) continue;
            int baseV = vl.Count / 3;
            for (int i = 0; i < sm.VertexCoordsXyz.Count; i++) vl.Add(sm.VertexCoordsXyz[i]);
            if (sm.TriangleIndices != null)
                for (int i = 0; i < sm.TriangleIndices.Count; i++) tl.Add(baseV + sm.TriangleIndices[i]);
        }
        verts = vl.ToArray();
        tris = tl.ToArray();
    }

    private static Mesh BuildMesh(double[] v, int[] t)
    {
        var mesh = new Mesh();
        for (int i = 0; i < v.Length / 3; i++) mesh.Vertices.Add(v[3 * i + 0], v[3 * i + 1], v[3 * i + 2]);
        for (int i = 0; i < t.Length / 3; i++) mesh.Faces.AddFace(t[3 * i + 0], t[3 * i + 1], t[3 * i + 2]);
        mesh.Normals.ComputeNormals();
        mesh.Compact();
        return mesh;
    }

    private static double[] ToArr(IReadOnlyList<double> src)
    {
        var a = new double[src.Count];
        for (int i = 0; i < a.Length; i++) a[i] = src[i];
        return a;
    }

    private static int[] ToArrI(IReadOnlyList<int> src)
    {
        var a = new int[src.Count];
        for (int i = 0; i < a.Length; i++) a[i] = src[i];
        return a;
    }
}

// =============================================================================
// Tetrahedralize (Geogram) - REQUIRES GEOGRAM_WITH_TETGEN=ON build.
// In default build returns rc=-210 with a clear error.
// =============================================================================

[Algorithm("Constrained Delaunay tetrahedralisation", "Levy, B., INRIA/ALICE. Geogram v1.9.9. BSD-3. https://github.com/BrunoLevy/geogram", WikiPath = "wiki/index/references.md")]
[RelatedComponent("Frahan > Mesh > Mesh Diagnostics", Reason = "Tet-mesh quality reports feed mesh-diagnostics workflows.")]
[RelatedComponent("Frahan > Masonry > Masonry Stability RBE", Reason = "Tetrahedralisation can support FE-volume stability analysis downstream of the RBE solver.")]
[DesignApplication(
    "Volumetric tetrahedral mesh of a closed surface via  GEO::mesh_tetrahedralize",
    DesignFlow.Bridges,
    Precedent = "Geogram GEO::mesh_tetrahedralize (Levy INRIA)")]
public sealed class GeogramTetrahedralizeComponent : FrahanComponentBase
{
    public GeogramTetrahedralizeComponent()
        : base("Tetrahedralize (Geogram)", "TetGeogram",
            "Volumetric tetrahedral mesh of a closed surface via " +
            "GEO::mesh_tetrahedralize. NOTE: requires the shim to be " +
            "built with GEOGRAM_WITH_TETGEN=ON. Default build has it " +
            "OFF for BSD-3 license cleanliness; in that mode this " +
            "component returns a clear error pointing at the rebuild. " +
            "Wraps Geogram mesh_tetrahedralize (TetGen).",
            "Frahan", "Mesh") { }
    public override Guid ComponentGuid => new Guid("F2D000C4-6E06-4F2D-A0C4-7E60660C0AC1");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override System.Drawing.Bitmap Icon => IconProvider.Load("CoacdDecompose.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Closed input surface.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Preprocess", "Pre", "Clean input first.", GH_ParamAccess.item, true);
        pManager.AddBooleanParameter("Refine", "Re", "Insert Steiner points to improve quality.", GH_ParamAccess.item, false);
        pManager.AddNumberParameter("Quality", "Q", "Element quality target [1.0..2.0]; 1.0 = max.", GH_ParamAccess.item, 2.0);
        pManager.AddBooleanParameter("Keep Regions", "Kr", "Keep all internal regions (else outermost only).", GH_ParamAccess.item, false);
        pManager.AddBooleanParameter("Run", "Run", "Set true to compute.", GH_ParamAccess.item, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Tet Cells", "T", "One mesh per tet (4 boundary triangles each).", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Tet Count", "N", "Number of tetrahedra.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Available", "Av", "True iff Geogram shim loaded.", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R", "Diagnostic report.", GH_ParamAccess.item);
    }
    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh m = null; bool pre = true, re = false, kr = false, run = false; double q = 2.0;
        if (!da.GetData(0, ref m)) return;
        da.GetData(1, ref pre); da.GetData(2, ref re); da.GetData(3, ref q);
        da.GetData(4, ref kr); da.GetData(5, ref run);
        var av = GeogramMesh.IsAvailable; da.SetData(2, av);
        if (!run) { da.SetData(3, av ? "Run is false." : "Geogram shim NOT loaded."); return; }
        if (!av) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Geogram shim not loaded."); return; }
        if (m == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh required."); return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var snap = CgalConvert.ToSnapshot(m);
            var tet = GeogramMesh.Tetrahedralize(snap, pre, re, q, kr);
            sw.Stop();
            // Render each tet as a Mesh of its 4 boundary triangles.
            var tetMeshes = new List<Mesh>(tet.TetCount);
            for (int i = 0; i < tet.TetCount; i++)
            {
                int a = tet.TetIndices[4 * i + 0];
                int b = tet.TetIndices[4 * i + 1];
                int c = tet.TetIndices[4 * i + 2];
                int d = tet.TetIndices[4 * i + 3];
                var tm = new Mesh();
                tm.Vertices.Add(tet.VertexCoordsXyz[3 * a + 0], tet.VertexCoordsXyz[3 * a + 1], tet.VertexCoordsXyz[3 * a + 2]);
                tm.Vertices.Add(tet.VertexCoordsXyz[3 * b + 0], tet.VertexCoordsXyz[3 * b + 1], tet.VertexCoordsXyz[3 * b + 2]);
                tm.Vertices.Add(tet.VertexCoordsXyz[3 * c + 0], tet.VertexCoordsXyz[3 * c + 1], tet.VertexCoordsXyz[3 * c + 2]);
                tm.Vertices.Add(tet.VertexCoordsXyz[3 * d + 0], tet.VertexCoordsXyz[3 * d + 1], tet.VertexCoordsXyz[3 * d + 2]);
                tm.Faces.AddFace(0, 1, 2);
                tm.Faces.AddFace(0, 2, 3);
                tm.Faces.AddFace(0, 3, 1);
                tm.Faces.AddFace(1, 3, 2);
                tm.Normals.ComputeNormals();
                tetMeshes.Add(tm);
            }
            da.SetDataList(0, tetMeshes);
            da.SetData(1, tet.TetCount);
            da.SetData(3,
                $"Verts  : {tet.VertexCount}\n" +
                $"Tets   : {tet.TetCount}\n" +
                $"Runtime: {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex) {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Geogram tetrahedralize failed: {ex.Message}");
            da.SetData(3, $"FAILED: {ex.Message}");
        }
    }
}

// =============================================================================
// CVT Seeds (Geogram) - centroidal-Voronoi-optimized seed positions.
// =============================================================================

[Algorithm("Centroidal Voronoi tessellation (Lloyd relaxation)", "Lloyd, S. (1982). Least squares quantization in PCM. IEEE Trans. Inf. Theory IT-28:129-137", WikiPath = "wiki/index/references.md")]
[Algorithm("Geogram CVT backend", "Levy, B., INRIA/ALICE. Geogram v1.9.9. BSD-3. https://github.com/BrunoLevy/geogram", WikiPath = "wiki/index/references.md")]
[RelatedComponent("Frahan > Quarry > Quarry DFN", Reason = "Production DFN generator consumes CVT seeds.")]
[RelatedComponent("Frahan > Quarry > Joint Set", Reason = "Joint-set parameters drive CVT seed distribution.")]
[RelatedComponent("Frahan > 2D > Pack 2D Trencadis Catalog", Reason = "CVD-Lloyd seeds are also used for Trencadis catalog placement.")]
[DesignApplication(
    "Compute optimized seed positions on a surface via  centroidal Voronoi tessellation (Lloyd + Newton-Lloyd)",
    DesignFlow.Bridges,
    Precedent = "Geogram Centroidal Voronoi Tessellation (Levy INRIA)")]
public sealed class GeogramCvtSeedsComponent : FrahanComponentBase
{
    public GeogramCvtSeedsComponent()
        : base("CVT Seeds (Geogram)", "CvtGeogram",
            "Compute optimized seed positions on a surface via " +
            "centroidal Voronoi tessellation (Lloyd + Newton-Lloyd). " +
            "Output feeds directly into Voronoi Block Partition. " +
            "Implements CVT (Lloyd 1982) via Geogram.",
            "Frahan", "Lab") { }
    public override Guid ComponentGuid => new Guid("F2D000C5-6E06-4F2D-A0C5-7E60660C0AC1");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override System.Drawing.Bitmap Icon => IconProvider.Load("Voronoi.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input surface.", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Points", "N", "Seed count (50..500 typical for masonry blocks).", GH_ParamAccess.item, 100);
        pManager.AddIntegerParameter("Lloyd Iters", "L", "Lloyd relaxation iterations.", GH_ParamAccess.item, 5);
        pManager.AddIntegerParameter("Newton Iters", "Nw", "Newton iterations after Lloyd.", GH_ParamAccess.item, 30);
        pManager.AddBooleanParameter("Run", "Run", "Set true to compute.", GH_ParamAccess.item, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddPointParameter("Seeds", "S", "CVT seed positions.", GH_ParamAccess.list);
        pManager.AddBooleanParameter("Available", "Av", "True iff Geogram shim loaded.", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R", "Diagnostic report.", GH_ParamAccess.item);
    }
    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh m = null; int n = 100, lloyd = 5, newton = 30; bool run = false;
        if (!da.GetData(0, ref m)) return;
        da.GetData(1, ref n); da.GetData(2, ref lloyd); da.GetData(3, ref newton); da.GetData(4, ref run);
        var av = GeogramMesh.IsAvailable; da.SetData(1, av);
        if (!run) { da.SetData(2, av ? "Run is false." : "Geogram shim NOT loaded."); return; }
        if (!av) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Geogram shim not loaded."); return; }
        if (m == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh required."); return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var snap = CgalConvert.ToSnapshot(m);
            var pts = GeogramMesh.CvtSeeds(snap, n, lloyd, newton);
            sw.Stop();
            var pp = new List<Point3d>(pts.Length / 3);
            for (int i = 0; i < pts.Length / 3; i++)
                pp.Add(new Point3d(pts[3 * i + 0], pts[3 * i + 1], pts[3 * i + 2]));
            da.SetDataList(0, pp);
            da.SetData(2,
                $"Target N : {n}\n" +
                $"Got      : {pp.Count}\n" +
                $"Iters    : Lloyd={lloyd} Newton={newton}\n" +
                $"Runtime  : {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex) {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Geogram CVT failed: {ex.Message}");
            da.SetData(2, $"FAILED: {ex.Message}");
        }
    }
}

// =============================================================================
// Voronoi Block Partition (Geogram RVD) - the headliner. Splits a surface
// into N Voronoi cells given seed points (use CVT Seeds upstream for
// uniform-area cells). Per the wiki, this becomes the geometric input
// for BlockGraph (spec 08) and downstream GeoCut / QuarryCutOpt.
// =============================================================================

[Algorithm("Restricted Voronoi diagram partition", "Levy, B., INRIA/ALICE. Geogram v1.9.9. BSD-3. https://github.com/BrunoLevy/geogram", WikiPath = "wiki/index/references.md")]
[Algorithm("Centroidal Voronoi tessellation (Lloyd relaxation)", "Lloyd, S. (1982). Least squares quantization in PCM. IEEE Trans. Inf. Theory IT-28:129-137", WikiPath = "wiki/index/references.md")]
[RelatedComponent("Frahan > Quarry > Quarry DFN", Reason = "Production DFN partitioner; this Geogram Voronoi is the research-grade alternative.")]
[RelatedComponent("Frahan > Quarry > Joint Set", Reason = "Joint-set fracture statistics feed Voronoi partitioning.")]
[DesignApplication(
    "Partition a surface mesh into N Voronoi cells given seed  points (use CVT Seeds upstream for uniform-area c...",
    DesignFlow.Bridges,
    Precedent = "Geogram CVT + Voronoi (Levy INRIA)")]
public sealed class GeogramVoronoiPartitionComponent : FrahanComponentBase
{
    public GeogramVoronoiPartitionComponent()
        : base("Voronoi Block Partition (Geogram)", "RvdGeogram",
            "Partition a surface mesh into N Voronoi cells given seed " +
            "points (use CVT Seeds upstream for uniform-area cells). " +
            "Output is one Mesh per cell. Quarry-pipeline use: " +
            "Statue → Decimate → Repair → Remesh → CVT → this " +
            "→ BlockGraph → GeoCut → QuarryCutOpt. " +
            "Wraps Geogram restricted Voronoi diagram.",
            "Frahan", "Block") { }
    public override Guid ComponentGuid => new Guid("F2D000C6-6E06-4F2D-A0C6-7E60660C0AC1");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override System.Drawing.Bitmap Icon => IconProvider.Load("Voronoi.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input surface.", GH_ParamAccess.item);
        pManager.AddPointParameter("Seeds", "S", "Seed points (use CVT Seeds for optimized seeds).", GH_ParamAccess.list);
        pManager.AddBooleanParameter("Run", "Run", "Set true to compute.", GH_ParamAccess.item, false);
        // Anti-sawtooth pre-RVD remesh: if > 0, the input is uniformly
        // remeshed to ~SP vertices before partitioning. Smooths the
        // visible cell-boundary stair-step that comes from coarse input
        // triangulations. 0 = skip.
        pManager.AddIntegerParameter("Smooth Pts", "SP",
            "Pre-RVD uniform remesh target vertex count (0 = off, " +
            "5_000..50_000 typical). Smooths cell-boundary sawtooth.",
            GH_ParamAccess.item, 0);
        // Volumetric mode: if true, returns CLOSED polyhedral cells
        // (the input solid sliced into Voronoi blocks) instead of
        // surface patches. Requires native shim built with
        // FRAHAN_WITH_TETGEN=ON (default since v0.2). Smooth Pts is
        // ignored in this mode.
        pManager.AddBooleanParameter("Closed", "Cl",
            "If true, return CLOSED Voronoi blocks (volumetric mode, " +
            "input must be a closed solid). If false, surface partition.",
            GH_ParamAccess.item, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Cells", "C", "One mesh per Voronoi cell.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Cell Count", "N", "Number of non-empty cells produced.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Available", "Av", "True iff Geogram shim loaded.", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R", "Diagnostic report.", GH_ParamAccess.item);
    }
    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh m = null; var seeds = new List<Point3d>(); bool run = false;
        int smoothPts = 0; bool closed = false;
        if (!da.GetData(0, ref m)) return;
        da.GetDataList(1, seeds);
        da.GetData(2, ref run);
        da.GetData(3, ref smoothPts);
        da.GetData(4, ref closed);
        var av = GeogramMesh.IsAvailable; da.SetData(2, av);
        if (!run) { da.SetData(3, av ? "Run is false." : "Geogram shim NOT loaded."); return; }
        if (!av) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Geogram shim not loaded."); return; }
        if (m == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh required."); return; }
        if (seeds.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least 1 seed point required."); return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var snap = CgalConvert.ToSnapshot(m);
            var seedFlat = new double[seeds.Count * 3];
            for (int i = 0; i < seeds.Count; i++)
            {
                seedFlat[3 * i + 0] = seeds[i].X;
                seedFlat[3 * i + 1] = seeds[i].Y;
                seedFlat[3 * i + 2] = seeds[i].Z;
            }
            string mode;
            RvdResult rvd;
            if (closed)
            {
                mode = "volumetric (closed blocks)";
                rvd = GeogramMesh.VoronoiBlocks(snap, seedFlat);
            }
            else if (smoothPts > 0)
            {
                mode = $"surface + pre-remesh ({smoothPts} pts)";
                rvd = GeogramMesh.VoronoiPartitionSmooth(snap, seedFlat, smoothPts);
            }
            else
            {
                mode = "surface (raw)";
                rvd = GeogramMesh.VoronoiPartition(snap, seedFlat);
            }
            sw.Stop();
            var cells = new List<Mesh>(rvd.CellCount);
            foreach (var c in rvd.Cells) cells.Add(CgalConvert.FromSnapshot(c));
            da.SetDataList(0, cells);
            da.SetData(1, rvd.CellCount);
            da.SetData(3,
                $"Mode     : {mode}\n" +
                $"Input    : {snap.VertexCount}V / {snap.TriangleCount}F\n" +
                $"Seeds    : {seeds.Count}\n" +
                $"Cells    : {rvd.CellCount} (non-empty)\n" +
                $"Runtime  : {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex) {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Geogram RVD failed: {ex.Message}");
            da.SetData(3, $"FAILED: {ex.Message}");
        }
    }
}

// =============================================================================
// Mesh Fill Holes (Geogram) - close sliver / spurious holes in an open
// surface patch while preserving the main outer boundary loop. Use it
// between a per-cell Voronoi sub-mesh and BFF to give BFF a clean
// disk-topology input (the dominant cause of BFF self-overlap is
// open boundary slivers in the cell mesh).
// =============================================================================

[Algorithm("Boundary-loop hole filling", "Levy, B., INRIA/ALICE. Geogram v1.9.9. BSD-3. https://github.com/BrunoLevy/geogram", WikiPath = "wiki/index/references.md")]
[RelatedComponent("Frahan > Mesh > Mesh Repair", Reason = "Production mesh-repair production path; hole-fill is a research sub-operation.")]
[DesignApplication(
    "Triangulate open boundary loops smaller than a size threshold",
    DesignFlow.Bridges,
    Precedent = "Geogram (Levy INRIA/ALICE v1.9.9, BSD-3)")]
public sealed class GeogramMeshFillHolesComponent : FrahanComponentBase
{
    public GeogramMeshFillHolesComponent()
        : base("Mesh Fill Holes (Geogram)", "FillHolesGeogram",
            "Triangulate open boundary loops smaller than a size threshold. " +
            "Use it to close sliver-holes in a Voronoi cell sub-mesh while " +
            "keeping the main outer boundary open - exactly what BFF needs " +
            "to flatten without self-overlap. BSD-3 (GEO::fill_holes). " +
            "Wraps Geogram fill_holes.",
            "Frahan", "Mesh") { }
    public override Guid ComponentGuid => new Guid("F2D000C7-6E06-4F2D-A0C7-7E60660C0AC1");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override System.Drawing.Bitmap Icon => IconProvider.Load("PoissonReconstruct.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh (open patch).", GH_ParamAccess.item);
        pManager.AddNumberParameter("Max Area", "A",
            "Maximum hole AREA (input units squared) to fill. 0 = fill " +
            "nothing. A very large value (1e30) fills every hole.",
            GH_ParamAccess.item, 0.0);
        pManager.AddIntegerParameter("Max Edges", "E",
            "Maximum boundary edges per hole. 0 = no edge limit (size " +
            "governed by area alone). Set to ~30 to target only " +
            "sliver-style holes that have few edges.",
            GH_ParamAccess.item, 0);
        pManager.AddBooleanParameter("Repair After", "R",
            "Run mesh_repair (DEFAULT mode) after filling to clean up " +
            "duplicate vertices / facets the hole triangulator may " +
            "leave behind.",
            GH_ParamAccess.item, true);
        pManager.AddBooleanParameter("Run", "Run", "Set true to compute.", GH_ParamAccess.item, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Filled", "M", "Mesh with small holes triangulated.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Available", "Av", "True iff Geogram shim loaded.", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R", "Diagnostic report.", GH_ParamAccess.item);
    }
    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh m = null; double maxArea = 0.0; int maxEdges = 0; bool repairAfter = true; bool run = false;
        if (!da.GetData(0, ref m)) return;
        da.GetData(1, ref maxArea);
        da.GetData(2, ref maxEdges);
        da.GetData(3, ref repairAfter);
        da.GetData(4, ref run);
        var av = GeogramMesh.IsAvailable; da.SetData(1, av);
        if (!run) { da.SetData(2, av ? "Run is false." : "Geogram shim NOT loaded."); return; }
        if (!av) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Geogram shim not loaded."); return; }
        if (m == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh required."); return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var snap = CgalConvert.ToSnapshot(m);
            var inV = snap.VertexCount; var inT = snap.TriangleCount;
            var filled = GeogramMesh.FillHoles(snap, maxArea, maxEdges, repairAfter);
            sw.Stop();
            da.SetData(0, CgalConvert.FromSnapshot(filled));
            da.SetData(2,
                $"Max Area  : {maxArea}\n" +
                $"Max Edges : {(maxEdges <= 0 ? "no limit" : maxEdges.ToString())}\n" +
                $"Repair    : {(repairAfter ? "on" : "off")}\n" +
                $"Input     : {inV}V / {inT}F\n" +
                $"Output    : {filled.VertexCount}V / {filled.TriangleCount}F\n" +
                $"Added     : {filled.TriangleCount - inT}F\n" +
                $"Runtime   : {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex) {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Geogram fill_holes failed: {ex.Message}");
            da.SetData(2, $"FAILED: {ex.Message}");
        }
    }
}
