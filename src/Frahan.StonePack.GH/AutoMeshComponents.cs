#nullable disable
using System;
using Frahan.GH.Attributes;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Interfaces;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH;

// =============================================================================
// Auto* components - delegate to MeshOps facade which picks Geogram first,
// CGAL second. Backend output reports which one actually ran. Useful when
// you don't want to commit to a specific shim on the canvas; the explicit
// (CGAL) and (Geogram) components stay available for diagnostics.
//
// Ribbon: "Frahan" / "Auto".
// =============================================================================

[RelatedComponent("Frahan > Mesh > Sanitize Mesh", Reason = "SUPERSEDED BY: Sanitize Mesh (Backend = Auto) does the same Geogram->CGAL repair plus a CGAL-Ready verdict.")]
[RelatedComponent("Frahan > Mesh > Mesh Diagnostics", Reason = "Diagnose mesh before repairing.")]
[DesignApplication(
    "Topology-aware mesh repair via the best available backend  (Geogram first, CGAL fallback)",
    DesignFlow.Bridges)]
public sealed class AutoMeshRepairComponent : FrahanComponentBase
{
    public AutoMeshRepairComponent()
        : base("Mesh Repair (Auto)", "RepairAuto",
            "Topology-aware mesh repair via the best available backend " +
            "(Geogram first, CGAL fallback). Backend output reports " +
            "which one ran.",
            "Frahan", "Mesh") { }
    public override Guid ComponentGuid => new Guid("F2D000D0-A070-4F2D-A0D0-7E60A07000D0");
    // 2026-05-28: retired (Obsolete + Hidden). Superseded by "Sanitize Mesh"
    // (Frahan > Mesh, Backend = Auto), which runs the same MeshOps.Repair
    // (Geogram -> CGAL) AND reports a CGAL-Ready (closed+manifold) verdict +
    // before/after validity. GUID unchanged so existing canvases keep loading;
    // just removed from the palette so new canvases see Sanitize Mesh instead.
    public override bool Obsolete => true;
    public override GH_Exposure Exposure => GH_Exposure.hidden;
    protected override System.Drawing.Bitmap Icon => IconProvider.Load("PoissonReconstruct.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Run", "Run", "Set true to compute.", GH_ParamAccess.item, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Repaired", "M", "Repaired mesh.", GH_ParamAccess.item);
        pManager.AddTextParameter("Backend", "B", "Which backend ran: Geogram, Cgal, or None.", GH_ParamAccess.item);
        pManager.AddTextParameter("Diagnostics", "D", "Loaded shim versions.", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R", "Diagnostic report.", GH_ParamAccess.item);
    }
    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh m = null; bool run = false;
        if (!da.GetData(0, ref m)) return;
        da.GetData(1, ref run);
        da.SetData(2, MeshOps.Diagnostics);
        if (!run)
        {
            da.SetData(1, "(not run)");
            da.SetData(3, MeshOps.IsAvailable ? "Run is false. At least one shim is loaded." : "Run is false. NO shim loaded.");
            return;
        }
        if (m == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh required."); return; }
        if (!MeshOps.IsAvailable) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No mesh-op backend loaded."); return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var snap = CgalConvert.ToSnapshot(m);
            var inV = snap.VertexCount; var inT = snap.TriangleCount;
            var rep = MeshOps.Repair(snap, out var backend);
            sw.Stop();
            da.SetData(0, CgalConvert.FromSnapshot(rep));
            da.SetData(1, backend.ToString());
            da.SetData(3,
                $"Backend  : {backend}\n" +
                $"Input    : {inV}V / {inT}F\n" +
                $"Output   : {rep.VertexCount}V / {rep.TriangleCount}F\n" +
                $"Runtime  : {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex) {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Auto repair failed: {ex.Message}");
            da.SetData(3, $"FAILED: {ex.Message}");
        }
    }
}

[RelatedComponent("Frahan > Mesh > Mesh Repair", Reason = "Mesh-quality production path; no production decimate yet.")]
[DesignApplication(
    "Mesh decimation via the best available backend (Geogram  vertex-clustering preferred; CGAL edge-collapse fa...",
    DesignFlow.Bridges)]
public sealed class AutoMeshDecimateComponent : FrahanComponentBase
{
    public AutoMeshDecimateComponent()
        : base("Mesh Decimate (Auto)", "DecimateAuto",
            "Mesh decimation via the best available backend (Geogram " +
            "vertex-clustering preferred; CGAL edge-collapse fallback). " +
            "Single ratio in (0,1) is mapped to backend-specific params.",
            "Frahan", "Mesh") { }
    public override Guid ComponentGuid => new Guid("F2D000D1-A070-4F2D-A0D1-7E60A07000D1");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override System.Drawing.Bitmap Icon => IconProvider.Load("Downsample.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Target Ratio", "R",
            "In (0, 1). Higher = more detail kept. Mapped to bin count " +
            "(Geogram) or edge-count ratio (CGAL).",
            GH_ParamAccess.item, 0.5);
        pManager.AddBooleanParameter("Run", "Run", "Set true to compute.", GH_ParamAccess.item, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Decimated", "M", "Decimated mesh.", GH_ParamAccess.item);
        pManager.AddTextParameter("Backend", "B", "Which backend ran.", GH_ParamAccess.item);
        pManager.AddTextParameter("Diagnostics", "D", "Loaded shim versions.", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R", "Diagnostic report.", GH_ParamAccess.item);
    }
    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh m = null; double ratio = 0.5; bool run = false;
        if (!da.GetData(0, ref m)) return;
        da.GetData(1, ref ratio); da.GetData(2, ref run);
        da.SetData(2, MeshOps.Diagnostics);
        if (!run) { da.SetData(1, "(not run)"); da.SetData(3, MeshOps.IsAvailable ? "Run is false." : "No shim loaded."); return; }
        if (m == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh required."); return; }
        if (!MeshOps.IsAvailable) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No mesh-op backend loaded."); return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var snap = CgalConvert.ToSnapshot(m);
            var inV = snap.VertexCount; var inT = snap.TriangleCount;
            var dec = MeshOps.Decimate(snap, ratio, out var backend);
            sw.Stop();
            da.SetData(0, CgalConvert.FromSnapshot(dec));
            da.SetData(1, backend.ToString());
            da.SetData(3,
                $"Backend  : {backend}\n" +
                $"Ratio    : {ratio}\n" +
                $"Input    : {inV}V / {inT}F\n" +
                $"Output   : {dec.VertexCount}V / {dec.TriangleCount}F\n" +
                $"Runtime  : {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex) {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Auto decimate failed: {ex.Message}");
            da.SetData(3, $"FAILED: {ex.Message}");
        }
    }
}

[RelatedComponent("Frahan > Quarry > BlockCutOpt Solve", Reason = "OBB feeds BlockCutOpt I2 BVH pruning; this auto-dispatcher selects CGAL or Geogram.")]
[RelatedComponent("Frahan > Mesh > Bench From Mesh", Reason = "Quarry-bench mesh OBB analysis.")]
[DesignApplication(
    "Oriented bounding box via the best available backend  (Geogram preferred - lighter, no Eigen)",
    DesignFlow.Bridges)]
public sealed class AutoObbComponent : FrahanComponentBase
{
    public AutoObbComponent()
        : base("OBB (Auto)", "ObbAuto",
            "Oriented bounding box via the best available backend " +
            "(Geogram preferred - lighter, no Eigen). CGAL fallback " +
            "requires the shim to be built with Eigen.",
            "Frahan", "Mesh") { }
    public override Guid ComponentGuid => new Guid("F2D000D2-A070-4F2D-A0D2-7E60A07000D2");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override System.Drawing.Bitmap Icon => IconProvider.Load("MeshBvh.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Run", "Run", "Set true to compute.", GH_ParamAccess.item, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddBoxParameter("OBB", "B", "Oriented bounding box.", GH_ParamAccess.item);
        pManager.AddPlaneParameter("Plane", "P", "Box origin frame.", GH_ParamAccess.item);
        pManager.AddTextParameter("Backend", "Bk", "Which backend ran.", GH_ParamAccess.item);
        pManager.AddTextParameter("Diagnostics", "D", "Loaded shim versions.", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R", "Diagnostic report.", GH_ParamAccess.item);
    }
    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh m = null; bool run = false;
        if (!da.GetData(0, ref m)) return;
        da.GetData(1, ref run);
        da.SetData(3, MeshOps.Diagnostics);
        if (!run) { da.SetData(2, "(not run)"); da.SetData(4, MeshOps.IsAvailable ? "Run is false." : "No shim loaded."); return; }
        if (m == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh required."); return; }
        if (!MeshOps.IsAvailable) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No mesh-op backend loaded."); return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var snap = CgalConvert.ToSnapshot(m);
            var obb = MeshOps.OrientedBoundingBox(snap, out var backend);
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
            da.SetData(2, backend.ToString());
            da.SetData(4,
                $"Backend  : {backend}\n" +
                $"Origin   : ({obb.OriginX:F3}, {obb.OriginY:F3}, {obb.OriginZ:F3})\n" +
                $"Extents  : ({obb.ExtentX:F3}, {obb.ExtentY:F3}, {obb.ExtentZ:F3})\n" +
                $"Volume   : {obb.ExtentX * obb.ExtentY * obb.ExtentZ:F3}\n" +
                $"Runtime  : {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex) {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Auto OBB failed: {ex.Message}");
            da.SetData(4, $"FAILED: {ex.Message}");
        }
    }
}
