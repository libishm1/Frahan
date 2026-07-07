#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Frahan.GH.Attributes;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Interfaces;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH;

// =============================================================================
// CGAL test components — three minimum-viable Grasshopper surfaces that
// exercise the CGAL native shim end-to-end. Each component probes
// CgalGeometry.IsAvailable + IsObbAvailable, calls into the appropriate
// managed wrapper, and routes the result back to Rhino types.
//
// Once CgalMeshBoolean and CgalGeometry are wired into production
// pipelines (masonry block fracturing etc.), these test components can
// stay on the canvas as diagnostic surfaces or be retired in favour of
// their production siblings.
//
// All three components live on the "Frahan" / "CGAL" ribbon tab.
// =============================================================================

internal static class CgalConvert
{
    public static MeshSnapshot ToSnapshot(Mesh m)
    {
        if (m == null) throw new ArgumentNullException(nameof(m));
        var dup = m.DuplicateMesh();
        dup.Faces.ConvertQuadsToTriangles();
        // Weld coincident vertices and drop unreferenced ones so CGAL sees
        // a closed, manifold mesh. Mesh.CreateFromBox/Sphere from Rhino emit
        // duplicated corner vertices (24 verts for a cube instead of 8); CGAL
        // PMP corefinement then treats every edge as a boundary and returns
        // empty / hole-ridden output. Exact-coordinate weld is safe because
        // both procedural primitives and well-conditioned scan-derived meshes
        // share corner vertices exactly. Tolerance-based welding is the
        // user's job via Mesh Repair (CGAL) for fragile inputs.
        dup.Vertices.CombineIdentical(true, true);
        dup.Vertices.CullUnused();
        dup.Compact();
        var verts = new double[dup.Vertices.Count * 3];
        for (int i = 0; i < dup.Vertices.Count; i++)
        {
            var v = dup.Vertices[i];
            verts[3 * i + 0] = v.X;
            verts[3 * i + 1] = v.Y;
            verts[3 * i + 2] = v.Z;
        }
        var tris = new int[dup.Faces.Count * 3];
        for (int i = 0; i < dup.Faces.Count; i++)
        {
            var f = dup.Faces[i];
            tris[3 * i + 0] = f.A;
            tris[3 * i + 1] = f.B;
            tris[3 * i + 2] = f.C;
        }
        return new MeshSnapshot(verts, tris);
    }

    public static Mesh FromSnapshot(MeshSnapshot s)
    {
        if (s == null) return null;
        var m = new Mesh();
        for (int i = 0; i < s.VertexCount; i++)
        {
            m.Vertices.Add(
                s.VertexCoordsXyz[3 * i + 0],
                s.VertexCoordsXyz[3 * i + 1],
                s.VertexCoordsXyz[3 * i + 2]);
        }
        for (int i = 0; i < s.TriangleCount; i++)
        {
            m.Faces.AddFace(
                s.TriangleIndices[3 * i + 0],
                s.TriangleIndices[3 * i + 1],
                s.TriangleIndices[3 * i + 2]);
        }
        m.Normals.ComputeNormals();
        m.Compact();
        return m;
    }

    public static double[] CurveToFlatVerts2D(Curve c, double tolerance)
    {
        if (c == null) throw new ArgumentNullException(nameof(c));
        Polyline pl;
        if (!c.TryGetPolyline(out pl))
        {
            var asPl = c.ToPolyline(tolerance, Math.PI / 90.0, 0, 0);
            if (asPl == null || !asPl.TryGetPolyline(out pl))
                throw new InvalidOperationException("Could not convert curve to polyline.");
        }
        int n = pl.Count;
        // Drop duplicated seam vertex if the polyline closes on itself.
        if (n > 1 && pl[0].DistanceTo(pl[n - 1]) < Math.Max(tolerance, 1e-9)) n--;
        if (n < 3) throw new InvalidOperationException("Curve has fewer than 3 distinct vertices.");
        var verts = new double[n * 2];
        for (int i = 0; i < n; i++)
        {
            verts[2 * i + 0] = pl[i].X;
            verts[2 * i + 1] = pl[i].Y;
        }
        return verts;
    }
}

// =============================================================================
// Mesh CSG (CGAL) — Union / Intersection / Difference between two Rhino
// meshes via CgalMeshBoolean. Reports the actual back-end used (CGAL or
// ManagedBsp). Useful as both a sanity-check and a side-by-side comparator.
// =============================================================================

[Algorithm("Mesh corefinement boolean", "CGAL Polygon Mesh Processing (corefine_and_compute_boolean_operations)", WikiPath = "wiki/index/references.md")]
[RelatedComponent("Frahan > Masonry > Mesh CSG", Reason = "Production-grade boolean mesh CSG; this CGAL component is the research probe.")]
[RelatedComponent("Frahan > Masonry > Slab Cut By Fractures", Reason = "Production cutting pipeline for slab + fracture inputs.")]
[DesignApplication(
    "Boolean operation between two meshes via the CGAL native  shim",
    DesignFlow.Bridges)]
public sealed class CgalMeshCsgComponent : FrahanComponentBase
{
    public CgalMeshCsgComponent()
        : base("Mesh CSG (CGAL)", "MeshCsgCgal",
            "Boolean operation between two meshes via the CGAL native " +
            "shim. Falls back transparently to in-tree BSP CSG when the " +
            "shim is absent. Reports which back-end actually ran. " +
            "Wraps CGAL corefine_and_compute_boolean_operations.",
            "Frahan", "Mesh")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D000A0-CADC-4F2D-A0A0-7E60CADA15A0");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("CoacdDecompose.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh A", "A", "First operand.", GH_ParamAccess.item);
        pManager.AddMeshParameter("Mesh B", "B", "Second operand.", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Operation", "Op",
            "0 = Union, 1 = Intersection, 2 = Difference (A − B).",
            GH_ParamAccess.item, 0);
        pManager.AddBooleanParameter("Use Hybrid Kernel", "Hybrid",
            "False = EPICK only (default, fast). True = HYBRID — EPICK " +
            "storage + EPECK intersection construction. Use Hybrid when " +
            "inputs may be numerically fragile (multi-cut chains, " +
            "near-tangent contacts).",
            GH_ParamAccess.item, false);
        pManager.AddBooleanParameter("Run", "Run",
            "Set true to compute.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Result", "M", "Result mesh.", GH_ParamAccess.item);
        pManager.AddTextParameter("Backend", "B",
            "Which kernel ran: 'CGAL' or 'ManagedBsp'.",
            GH_ParamAccess.item);
        pManager.AddBooleanParameter("Available", "Av",
            "True iff the CGAL native shim is loadable.",
            GH_ParamAccess.item);
        pManager.AddTextParameter("Version", "V",
            "Reported version string from the shim.",
            GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R",
            "Diagnostic report (timing, kernel, fallback note).",
            GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh a = null, b = null;
        int op = 0;
        bool hybrid = false;
        bool run = false;

        if (!da.GetData(0, ref a)) return;
        if (!da.GetData(1, ref b)) return;
        da.GetData(2, ref op);
        da.GetData(3, ref hybrid);
        da.GetData(4, ref run);

        var available = CgalMeshBoolean.IsAvailable;
        da.SetData(2, available);
        da.SetData(3, CgalMeshBoolean.Version);

        if (!run)
        {
            da.SetData(4, available
                ? "Run is false. CGAL shim is loaded and ready."
                : "Run is false. CGAL shim NOT loaded; will fall back to BSP.");
            return;
        }

        if (a == null || b == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Both meshes are required.");
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var sa = CgalConvert.ToSnapshot(a);
            var sb = CgalConvert.ToSnapshot(b);

            CsgBackend backend;
            MeshSnapshot result;
            var kernel = hybrid ? CsgKernelMode.Hybrid : CsgKernelMode.Inexact;
            switch (op)
            {
                case 1: result = CgalMeshBoolean.Intersection(sa, sb, kernel, out backend); break;
                case 2: result = CgalMeshBoolean.Difference(sa, sb, kernel, out backend); break;
                default: result = CgalMeshBoolean.Union(sa, sb, kernel, out backend); break;
            }
            sw.Stop();

            var rhinoMesh = CgalConvert.FromSnapshot(result);
            da.SetData(0, rhinoMesh);
            da.SetData(1, backend.ToString());

            var opName = op == 1 ? "Intersection" : op == 2 ? "Difference" : "Union";
            var report =
                $"Operation : {opName}\n" +
                $"Backend   : {backend} (kernel: {kernel})\n" +
                $"A in      : {sa.VertexCount}V / {sa.TriangleCount}F\n" +
                $"B in      : {sb.VertexCount}V / {sb.TriangleCount}F\n" +
                $"Result    : {result.VertexCount}V / {result.TriangleCount}F\n" +
                $"Runtime   : {sw.ElapsedMilliseconds} ms\n";
            da.SetData(4, report);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"CGAL CSG failed: {ex.GetType().Name}: {ex.Message}");
            da.SetData(4, $"FAILED: {ex.Message}");
        }
    }
}

// =============================================================================
// Straight Skeleton (CGAL) — interior straight skeleton of a 2D polygon
// (with optional holes). Outputs the skeleton as line segments + per-vertex
// time-of-arrival values (boundary vertices have time 0, interior verts
// carry the offset distance at which they appear).
// =============================================================================

[Algorithm("Straight skeleton", "CGAL Straight_skeleton_2 (Aichholzer-Aurenhammer straight skeleton)", WikiPath = "wiki/index/references.md")]
[RelatedComponent("Frahan > Masonry > Slab Cut By Fractures", Reason = "Skeleton-based cutting paths feed into the production fracture-cutting pipeline.")]
[RelatedComponent("Frahan > Masonry > Fracture Polygon From Curve", Reason = "Polygon ingest sibling for fracture inputs.")]
[DesignApplication(
    "Interior straight skeleton of a 2D polygon (with optional  holes) via CGAL Straight_skeleton_2",
    DesignFlow.Bridges)]
public sealed class CgalStraightSkeletonComponent : FrahanComponentBase
{
    public CgalStraightSkeletonComponent()
        : base("Straight Skeleton (CGAL)", "SkeletonCgal",
            "Interior straight skeleton of a 2D polygon (with optional " +
            "holes) via CGAL Straight_skeleton_2. Outer ring CCW, holes " +
            "CW; the shim auto-reverses if winding is wrong. " +
            "Wraps CGAL Straight_skeleton_2.",
            "Frahan", "Mesh")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D000A1-CADC-4F2D-A0A1-7E60CADA15A0");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("PolygonSimplify.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Outer", "O",
            "Closed planar polyline / curve. The outer ring of the polygon.",
            GH_ParamAccess.item);
        pManager.AddCurveParameter("Holes", "H",
            "Optional closed planar curves treated as holes.",
            GH_ParamAccess.list);
        pManager[1].Optional = true;
        pManager.AddNumberParameter("Tolerance", "T",
            "Curve-to-polyline tolerance.", GH_ParamAccess.item, 0.01);
        pManager.AddBooleanParameter("Run", "Run",
            "Set true to compute.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddLineParameter("Edges", "E",
            "Skeleton edges as 2D lines (Z = 0).", GH_ParamAccess.list);
        pManager.AddPointParameter("Vertices", "V",
            "Skeleton + boundary vertex positions.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Times", "Time",
            "Time-of-arrival per vertex (boundary = 0; interior > 0).",
            GH_ParamAccess.list);
        pManager.AddBooleanParameter("Available", "Av",
            "True iff the CGAL native shim is loadable.",
            GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R",
            "Diagnostic report.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Curve outer = null;
        var holes = new List<Curve>();
        double tol = 0.01;
        bool run = false;

        if (!da.GetData(0, ref outer)) return;
        da.GetDataList(1, holes);
        da.GetData(2, ref tol);
        da.GetData(3, ref run);

        var available = CgalGeometry.IsAvailable;
        da.SetData(3, available);

        if (!run)
        {
            da.SetData(4, available
                ? "Run is false. CGAL shim is loaded and ready."
                : "Run is false. CGAL shim NOT loaded; cannot compute skeleton.");
            return;
        }
        if (!available)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "CGAL native shim not loaded — straight skeleton requires CGAL.");
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var outerVerts = CgalConvert.CurveToFlatVerts2D(outer, tol);

            // Concatenate holes + per-hole counts.
            var holeBuf = new List<double>();
            var holeCounts = new List<int>();
            foreach (var hc in holes)
            {
                if (hc == null) continue;
                var hv = CgalConvert.CurveToFlatVerts2D(hc, tol);
                holeCounts.Add(hv.Length / 2);
                holeBuf.AddRange(hv);
            }

            var ss = CgalGeometry.StraightSkeleton2D(outerVerts, holeBuf, holeCounts);
            sw.Stop();

            // Vertex points (Z = 0).
            var pts = new List<Point3d>(ss.VertexCount);
            for (int i = 0; i < ss.VertexCount; i++)
                pts.Add(new Point3d(ss.Vertices[2 * i], ss.Vertices[2 * i + 1], 0));

            // Edges → Line list.
            var lines = new List<Line>(ss.EdgeCount);
            for (int i = 0; i < ss.EdgeCount; i++)
            {
                var a = ss.Edges[2 * i];
                var b = ss.Edges[2 * i + 1];
                if (a < 0 || b < 0 || a >= pts.Count || b >= pts.Count) continue;
                lines.Add(new Line(pts[a], pts[b]));
            }

            da.SetDataList(0, lines);
            da.SetDataList(1, pts);
            da.SetDataList(2, ss.Times);
            da.SetData(4,
                $"Outer V    : {outerVerts.Length / 2}\n" +
                $"Holes      : {holeCounts.Count}\n" +
                $"Skeleton V : {ss.VertexCount}\n" +
                $"Skeleton E : {ss.EdgeCount}\n" +
                $"Runtime    : {sw.ElapsedMilliseconds} ms\n");
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"CGAL skeleton failed: {ex.GetType().Name}: {ex.Message}");
            da.SetData(4, $"FAILED: {ex.Message}");
        }
    }
}

// =============================================================================
// Mesh Repair (CGAL) — robust manifold cleanup pipeline. Wraps CGAL's
// PMP repair primitives (triangulate_faces + stitch_borders +
// remove_degenerate_faces + orient_to_bound_a_volume + collect_garbage)
// in one component. Stronger than Rhino's built-in repairs because
// CGAL uses exact-predicate adjacency, not normal-vector heuristics, to
// merge coincident half-edges.
// =============================================================================

[Algorithm("CGAL PMP mesh repair", "CGAL Polygon Mesh Processing (repair package)", WikiPath = "wiki/index/references.md")]
[RelatedComponent("Frahan > Mesh > Mesh Repair", Reason = "Production mesh-repair component; this CGAL variant is the research path.")]
[RelatedComponent("Frahan > Mesh > Mesh Quality Report", Reason = "Diagnose mesh quality before deciding to repair.")]
[DesignApplication(
    "Robust mesh repair via CGAL Polygon Mesh Processing",
    DesignFlow.Bridges)]
public sealed class CgalMeshRepairComponent : FrahanComponentBase
{
    public CgalMeshRepairComponent()
        : base("Mesh Repair (CGAL)", "MeshRepairCgal",
            "Robust mesh repair via CGAL Polygon Mesh Processing. " +
            "Triangulates non-triangle faces, stitches coincident " +
            "borders, removes degenerate triangles, and orients faces " +
            "outward when the mesh is closed. " +
            "Wraps CGAL PMP repair routines.",
            "Frahan", "Mesh")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D000A4-CADC-4F2D-A0A4-7E60CADA15A0");
    public override GH_Exposure Exposure => GH_Exposure.secondary;
    protected override Bitmap Icon => IconProvider.Load("PoissonReconstruct.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M",
            "Input mesh to repair.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Run", "Run",
            "Set true to compute.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Repaired", "M",
            "Repaired mesh.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Available", "Av",
            "True iff the CGAL native shim is loadable.",
            GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R",
            "Repair report (vertex/face deltas, runtime).",
            GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh m = null;
        bool run = false;

        if (!da.GetData(0, ref m)) return;
        da.GetData(1, ref run);

        var available = CgalGeometry.IsAvailable;
        da.SetData(1, available);

        if (!run)
        {
            da.SetData(2, available
                ? "Run is false. CGAL shim is loaded and ready."
                : "Run is false. CGAL shim NOT loaded; cannot repair.");
            return;
        }
        if (!available)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "CGAL native shim not loaded — mesh repair requires CGAL.");
            return;
        }
        if (m == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh is required.");
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var snap = CgalConvert.ToSnapshot(m);
            var inputV = snap.VertexCount;
            var inputT = snap.TriangleCount;
            var repaired = CgalGeometry.RepairMesh(snap);
            sw.Stop();
            var outMesh = CgalConvert.FromSnapshot(repaired);
            da.SetData(0, outMesh);
            da.SetData(2,
                $"Input  : {inputV}V / {inputT}F\n" +
                $"Output : {repaired.VertexCount}V / {repaired.TriangleCount}F\n" +
                $"Delta V: {repaired.VertexCount - inputV:+#;-#;0}\n" +
                $"Delta F: {repaired.TriangleCount - inputT:+#;-#;0}\n" +
                $"Runtime: {sw.ElapsedMilliseconds} ms\n" +
                $"Pipeline: triangulate_faces → stitch_borders → " +
                $"remove_degenerate_faces → orient_to_bound_a_volume " +
                $"(if closed) → collect_garbage");
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"CGAL repair failed: {ex.GetType().Name}: {ex.Message}");
            da.SetData(2, $"FAILED: {ex.Message}");
        }
    }
}

// =============================================================================
// Mesh Decimate (CGAL) — Surface_mesh_simplification edge collapse with
// the default Lindstrom-Turk policies. Three stop modes: count ratio,
// target edge count, edge length. Useful before CoACD on high-poly
// statue input (CoACD's MCTS scales with input triangle count) and after
// CoACD on each hull to slim per-piece complexity for fabrication.
// =============================================================================

[Algorithm("Quadric edge-collapse simplification", "CGAL Surface_mesh_simplification (Lindstrom-Turk edge-collapse policies)", WikiPath = "wiki/index/references.md")]
[RelatedComponent("Frahan > Mesh > Mesh Repair", Reason = "Mesh-quality production sibling; no dedicated decimate component on the production side yet.")]
[DesignApplication(
    "Mesh simplification via CGAL Surface_mesh_simplification  (quadric-error edge collapse, Lindstrom-Turk poli...",
    DesignFlow.Bridges)]
public sealed class CgalMeshDecimateComponent : FrahanComponentBase
{
    public CgalMeshDecimateComponent()
        : base("Mesh Decimate (CGAL)", "DecimateCgal",
            "Mesh simplification via CGAL Surface_mesh_simplification " +
            "(quadric-error edge collapse, Lindstrom-Turk policies). " +
            "Three stop modes: count ratio, target edge count, edge " +
            "length. Run before CoACD to speed up decomposition on " +
            "scanned statue input. " +
            "Wraps CGAL Surface_mesh_simplification (Lindstrom-Turk policies).",
            "Frahan", "Mesh")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D000A5-CADC-4F2D-A0A5-7E60CADA15A0");
    public override GH_Exposure Exposure => GH_Exposure.secondary;
    protected override Bitmap Icon => IconProvider.Load("Downsample.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M",
            "Input mesh. Should be a valid 2-manifold for stable results.",
            GH_ParamAccess.item);
        pManager.AddIntegerParameter("Stop Kind", "K",
            "0 = count ratio (remaining/initial, value in (0, 1)). Most common.\n" +
            "1 = target edge count (>= 1).\n" +
            "2 = minimum edge length (> 0); preserves edges shorter than the " +
            "threshold (good for keeping sharp features).",
            GH_ParamAccess.item, 0);
        pManager.AddNumberParameter("Stop Value", "V",
            "Threshold meaning depends on Stop Kind:\n" +
            "  Kind 0: 0.5 = halve edge count.\n" +
            "  Kind 1: 5000 = stop at 5000 edges.\n" +
            "  Kind 2: 0.05 = stop when next edge to collapse is >= 0.05.",
            GH_ParamAccess.item, 0.5);
        pManager.AddBooleanParameter("Run", "Run",
            "Set true to compute.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Decimated", "M",
            "Decimated mesh.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Available", "Av",
            "True iff the CGAL native shim is loadable.",
            GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R",
            "Diagnostic report (V/F counts in/out, runtime).",
            GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh m = null;
        int stopKind = 0;
        double stopValue = 0.5;
        bool run = false;

        if (!da.GetData(0, ref m)) return;
        da.GetData(1, ref stopKind);
        da.GetData(2, ref stopValue);
        da.GetData(3, ref run);

        var available = CgalGeometry.IsAvailable;
        da.SetData(1, available);

        if (!run)
        {
            da.SetData(2, available
                ? "Run is false. CGAL shim is loaded and ready."
                : "Run is false. CGAL shim NOT loaded; cannot decimate.");
            return;
        }
        if (!available)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "CGAL native shim not loaded — decimation requires CGAL.");
            return;
        }
        if (m == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh is required.");
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var snap = CgalConvert.ToSnapshot(m);
            var inV = snap.VertexCount;
            var inT = snap.TriangleCount;
            var kind = (DecimateStopKind)stopKind;
            var decimated = CgalGeometry.DecimateMesh(snap, kind, stopValue);
            sw.Stop();

            var outMesh = CgalConvert.FromSnapshot(decimated);
            da.SetData(0, outMesh);
            da.SetData(2,
                $"Stop Kind  : {kind} = {stopValue}\n" +
                $"Input      : {inV}V / {inT}F\n" +
                $"Output     : {decimated.VertexCount}V / {decimated.TriangleCount}F\n" +
                $"Reduction  : V {(inV > 0 ? (1.0 - decimated.VertexCount/(double)inV) * 100.0 : 0.0):F1}%, " +
                $"F {(inT > 0 ? (1.0 - decimated.TriangleCount/(double)inT) * 100.0 : 0.0):F1}%\n" +
                $"Runtime    : {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"CGAL decimate failed: {ex.GetType().Name}: {ex.Message}");
            da.SetData(2, $"FAILED: {ex.Message}");
        }
    }
}

// =============================================================================
// Polygon Partition (CGAL) — convex / monotone decomposition of a 2D
// simple polygon (no holes). Returns each sub-polygon as a closed curve
// suitable for downstream visualisation or per-piece processing.
// =============================================================================

[Algorithm("Convex polygon partition", "CGAL Partition_2 (Hertel-Mehlhorn approximate + Greene optimal convex decomposition)", WikiPath = "wiki/index/references.md")]
[RelatedComponent("Frahan > Masonry > Slab Cut By Fracture Polygons", Reason = "Production polygon-based slab cutting; uses partitioned polygons as input.")]
[RelatedComponent("Frahan > Masonry > Polygon Sanitize", Reason = "Pre-clean polygons before partitioning.")]
[DesignApplication(
    "Decompose a 2D simple polygon into convex sub-polygons or  y-monotone pieces via CGAL Partition_2",
    DesignFlow.Bridges)]
public sealed class CgalPolygonPartitionComponent : FrahanComponentBase
{
    public CgalPolygonPartitionComponent()
        : base("Polygon Partition (CGAL)", "PartitionCgal",
            "Decompose a 2D simple polygon into convex sub-polygons or " +
            "y-monotone pieces via CGAL Partition_2. " +
            "Wraps CGAL Partition_2 (Hertel-Mehlhorn and Greene).",
            "Frahan", "Mesh")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D000A2-CADC-4F2D-A0A2-7E60CADA15A0");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("Voronoi.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Polygon", "P",
            "Closed planar simple polygon (no holes).", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Kind", "K",
            "0 = approximate convex (Hertel-Mehlhorn, fast). " +
            "1 = optimal convex (Greene, O(n^4) but minimal pieces). " +
            "2 = y-monotone partition.",
            GH_ParamAccess.item, 0);
        pManager.AddNumberParameter("Tolerance", "T",
            "Curve-to-polyline tolerance.", GH_ParamAccess.item, 0.01);
        pManager.AddBooleanParameter("Run", "Run",
            "Set true to compute.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Pieces", "C",
            "Sub-polygons as closed polylines.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Piece Count", "N",
            "Number of pieces.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Available", "Av",
            "True iff the CGAL native shim is loadable.",
            GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R",
            "Diagnostic report.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Curve poly = null;
        int kind = 0;
        double tol = 0.01;
        bool run = false;

        if (!da.GetData(0, ref poly)) return;
        da.GetData(1, ref kind);
        da.GetData(2, ref tol);
        da.GetData(3, ref run);

        var available = CgalGeometry.IsAvailable;
        da.SetData(2, available);

        if (!run)
        {
            da.SetData(3, available
                ? "Run is false. CGAL shim is loaded and ready."
                : "Run is false. CGAL shim NOT loaded; cannot partition.");
            return;
        }
        if (!available)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "CGAL native shim not loaded — partition requires CGAL.");
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var verts = CgalConvert.CurveToFlatVerts2D(poly, tol);
            var pk = (PartitionKind)kind;
            var result = CgalGeometry.PolygonPartition2D(verts, pk);
            sw.Stop();

            // Reconstruct each sub-polygon as a closed Polyline.
            var pieces = new List<Curve>(result.PolygonCount);
            for (int i = 0; i < result.PolygonCount; i++)
            {
                var coords = result.GetPolygon(i);
                var pl = new Polyline(coords.Length / 2 + 1);
                for (int k = 0; k < coords.Length / 2; k++)
                    pl.Add(coords[2 * k], coords[2 * k + 1], 0);
                if (pl.Count > 0) pl.Add(pl[0]);  // close
                pieces.Add(pl.ToNurbsCurve());
            }

            da.SetDataList(0, pieces);
            da.SetData(1, result.PolygonCount);
            da.SetData(3,
                $"Kind       : {pk}\n" +
                $"Input V    : {verts.Length / 2}\n" +
                $"Pieces     : {result.PolygonCount}\n" +
                $"Output V   : {result.VertexCount}\n" +
                $"Runtime    : {sw.ElapsedMilliseconds} ms\n");
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"CGAL partition failed: {ex.GetType().Name}: {ex.Message}");
            da.SetData(3, $"FAILED: {ex.Message}");
        }
    }
}

// =============================================================================
// Mesh Segmentation (CGAL SDF) - feature-based shape decomposition via
// the Shape Diameter Function. Cuts at concave features (necks, folds);
// not appropriate for Voronoi-style spatial chopping of convex blocks.
// Reference: https://doc.cgal.org/latest/Surface_mesh_segmentation/
// =============================================================================

[Algorithm("SDF-based mesh segmentation", "CGAL Surface_mesh_segmentation (Shape Diameter Function graph-cut)", WikiPath = "wiki/index/references.md")]
[RelatedComponent("Frahan > Mesh > Mesh Quality Report", Reason = "Production mesh analysis; SDF segmentation is a research probe.")]
[RelatedComponent("Frahan > Quarry > BlockCutOpt Solve", Reason = "Segmented mesh regions can feed the BlockCutOpt sub-zone partition (I10).")]
[DesignApplication(
    "Surface mesh segmentation via Shape Diameter Function",
    DesignFlow.Bridges)]
public sealed class CgalSdfSegmentationComponent : FrahanComponentBase
{
    public CgalSdfSegmentationComponent()
        : base("Mesh Segmentation (CGAL SDF)", "SegmentSdfCgal",
            "Surface mesh segmentation via Shape Diameter Function. " +
            "Cuts at concave features (deep folds, narrow necks); the " +
            "tried-and-tested CGAL Surface_mesh_segmentation pipeline. " +
            "Returns one mesh per segment. NOT a Voronoi-style spatial " +
            "split: convex inputs collapse to one segment. " +
            "Wraps CGAL Surface_mesh_segmentation (SDF).",
            "Frahan", "Mesh") { }
    public override Guid ComponentGuid => new Guid("F2D000A6-CADC-4F2D-A0A6-7E60CADA15A0");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override System.Drawing.Bitmap Icon => IconProvider.Load("CoacdDecompose.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input surface (2-manifold gives best results).", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Clusters", "K",
            "Target number of segments (>= 2). CGAL's example uses 5.",
            GH_ParamAccess.item, 5);
        pManager.AddNumberParameter("Smoothing", "Lam",
            "Graph-cut smoothness penalty in [0, 1]. Higher = more " +
            "spatially coherent / fewer islands. CGAL default 0.26.",
            GH_ParamAccess.item, 0.26);
        pManager.AddNumberParameter("Cone Angle", "Cone",
            "SDF inward cone half-angle (radians). 0 = CGAL default " +
            "(2/3 * pi, ~120 degrees).",
            GH_ParamAccess.item, 0.0);
        pManager.AddIntegerParameter("Rays", "Ry",
            "Rays per facet for SDF estimation. 0 = CGAL default (25). " +
            "More rays = smoother SDF, slower compute.",
            GH_ParamAccess.item, 0);
        pManager.AddBooleanParameter("Postprocess", "Pp",
            "Run CGAL's SDF postprocess (smoothing + connected-component " +
            "cleanup). Recommended.",
            GH_ParamAccess.item, true);
        pManager.AddBooleanParameter("Run", "Run", "Set true to compute.", GH_ParamAccess.item, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Segments", "S", "One mesh per non-empty segment.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Segment Count", "N", "Number of non-empty segments produced.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Available", "Av", "True iff CGAL shim loaded.", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R", "Diagnostic report.", GH_ParamAccess.item);
    }
    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh m = null;
        int nbClusters = 5;
        double smoothing = 0.26;
        double coneAngle = 0.0;
        int nbRays = 0;
        bool postprocess = true;
        bool run = false;
        if (!da.GetData(0, ref m)) return;
        da.GetData(1, ref nbClusters);
        da.GetData(2, ref smoothing);
        da.GetData(3, ref coneAngle);
        da.GetData(4, ref nbRays);
        da.GetData(5, ref postprocess);
        da.GetData(6, ref run);

        var av = CgalGeometry.IsAvailable; da.SetData(2, av);
        if (!run) { da.SetData(3, av ? "Run is false." : "CGAL shim NOT loaded."); return; }
        if (!av) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "CGAL shim not loaded."); return; }
        if (m == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh required."); return; }
        if (nbClusters < 2) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Clusters must be >= 2."); return; }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var snap = CgalConvert.ToSnapshot(m);
            var seg = CgalGeometry.SegmentMeshBySdf(
                snap, nbClusters, smoothing, coneAngle, nbRays, postprocess);
            sw.Stop();
            var meshes = new List<Mesh>(seg.SegmentCount);
            foreach (var s in seg.Segments) meshes.Add(CgalConvert.FromSnapshot(s));
            da.SetDataList(0, meshes);
            da.SetData(1, seg.SegmentCount);
            da.SetData(3,
                $"Input    : {snap.VertexCount}V / {snap.TriangleCount}F\n" +
                $"Requested: {nbClusters} clusters\n" +
                $"Actual   : {seg.ActualClusters} clusters ({seg.SegmentCount} non-empty)\n" +
                $"Lambda   : {smoothing}\n" +
                $"Rays     : {(nbRays <= 0 ? "default 25" : nbRays.ToString())}\n" +
                $"Cone     : {(coneAngle <= 0 ? "default 2pi/3" : coneAngle.ToString("0.###"))}\n" +
                $"Runtime  : {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"CGAL SDF segmentation failed: {ex.GetType().Name}: {ex.Message}");
            da.SetData(3, $"FAILED: {ex.Message}");
        }
    }
}

// =============================================================================
// Mesh Segmentation by Angle (CGAL) - cluster faces by dihedral-angle
// change. Detects sharp edges with PMP::detect_sharp_edges, then
// connected-components on faces while treating those edges as walls.
// Faces connected through soft edges land in the same segment.
// Reference: CGAL Polygon Mesh Processing - sharp_edges_segmentation.
// =============================================================================

[Algorithm("Sharp-edge dihedral segmentation", "CGAL Polygon Mesh Processing (detect_sharp_edges + connected components)", WikiPath = "wiki/index/references.md")]
[RelatedComponent("Frahan > Mesh > Mesh Quality Report", Reason = "Production mesh analysis; angle-based segmentation is a research probe.")]
[RelatedComponent("Frahan > Masonry > Mesh Planar Polygon Extractor", Reason = "Planar-face extraction shares the angle-clustering primitive.")]
[DesignApplication(
    "Cluster mesh faces by dihedral-angle change",
    DesignFlow.Bridges)]
public sealed class CgalAngleSegmentationComponent : FrahanComponentBase
{
    public CgalAngleSegmentationComponent()
        : base("Mesh Segmentation by Angle (CGAL)", "SegmentAngleCgal",
            "Cluster mesh faces by dihedral-angle change. Detects " +
            "sharp edges (where adjacent face normals deviate by more " +
            "than the threshold) and flood-fills the rest into smooth " +
            "regions. Returns one mesh per region. " +
            "Tuning: 5-15 deg = strict planarity, 30-60 deg = smooth-" +
            "band detection, 90+ = only orthogonal-ish creases. " +
            "Wraps CGAL detect_sharp_edges.",
            "Frahan", "Mesh") { }
    public override Guid ComponentGuid => new Guid("F2D000A7-CADC-4F2D-A0A7-7E60CADA15A0");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override System.Drawing.Bitmap Icon => IconProvider.Load("CoacdDecompose.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input surface (2-manifold gives stable dihedral computation).", GH_ParamAccess.item);
        pManager.AddNumberParameter("Angle", "A",
            "Dihedral angle threshold in DEGREES, in (0, 180). Edges " +
            "whose dihedral angle exceeds this become segment boundaries. " +
            "Try 30-45 for smooth bands on curved forms.",
            GH_ParamAccess.item, 30.0);
        pManager.AddBooleanParameter("Run", "Run", "Set true to compute.", GH_ParamAccess.item, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Segments", "S", "One mesh per smoothly-connected region.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Segment Count", "N", "Number of non-empty segments produced.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Available", "Av", "True iff CGAL shim loaded.", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R", "Diagnostic report.", GH_ParamAccess.item);
    }
    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh m = null;
        double angleDeg = 30.0;
        bool run = false;
        if (!da.GetData(0, ref m)) return;
        da.GetData(1, ref angleDeg);
        da.GetData(2, ref run);

        var av = CgalGeometry.IsAvailable; da.SetData(2, av);
        if (!run) { da.SetData(3, av ? "Run is false." : "CGAL shim NOT loaded."); return; }
        if (!av) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "CGAL shim not loaded."); return; }
        if (m == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh required."); return; }
        if (!(angleDeg > 0.0 && angleDeg < 180.0))
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Angle must be in (0, 180)."); return; }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var snap = CgalConvert.ToSnapshot(m);
            var seg = CgalGeometry.SegmentMeshByAngle(snap, angleDeg);
            sw.Stop();
            var meshes = new List<Mesh>(seg.SegmentCount);
            foreach (var s in seg.Segments) meshes.Add(CgalConvert.FromSnapshot(s));
            da.SetDataList(0, meshes);
            da.SetData(1, seg.SegmentCount);
            da.SetData(3,
                $"Input    : {snap.VertexCount}V / {snap.TriangleCount}F\n" +
                $"Angle    : {angleDeg} deg\n" +
                $"Segments : {seg.SegmentCount} (CGAL CC count: {seg.ActualClusters})\n" +
                $"Runtime  : {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"CGAL angle segmentation failed: {ex.GetType().Name}: {ex.Message}");
            da.SetData(3, $"FAILED: {ex.Message}");
        }
    }
}

// =============================================================================
// Geodesic Voronoi (CGAL Heat Method) - on-surface Voronoi partition
// driven by user-supplied seed points. Cell boundaries follow geodesic
// equidistance, so cuts respect mesh curvature instead of slicing
// through the surface (the failure mode of Euclidean Geogram RVD).
// Reference: https://www.cgal.org/2019/01/23/Heat_Method/
// =============================================================================

[Algorithm("Heat-method geodesic distance", "CGAL Heat_method_3 (Crane-Weischedel-Wardetzky heat method)", WikiPath = "wiki/index/references.md")]
[RelatedComponent("Frahan > Quarry > Quarry DFN", Reason = "Production Voronoi-based DFN generator; this geodesic variant is the research path.")]
[RelatedComponent("Frahan > Quarry > Joint Set", Reason = "Joint-set seeds for DFN generation.")]
[DesignApplication(
    "Split a mesh surface into Voronoi cells driven by  geodesic distance from user-supplied seed points (Crane ...",
    DesignFlow.Bridges)]
public sealed class CgalGeodesicVoronoiComponent : FrahanComponentBase
{
    public CgalGeodesicVoronoiComponent()
        : base("Geodesic Voronoi (CGAL)", "GeodesicVoronoiCgal",
            "Split a mesh surface into Voronoi cells driven by " +
            "geodesic distance from user-supplied seed points (Crane " +
            "et al. Heat Method 2013). Each seed snaps to its nearest " +
            "vertex; each face joins the cell of the seed with the " +
            "shortest on-surface distance. Cuts follow surface " +
            "curvature - neat boundaries on curved meshes where " +
            "Euclidean Voronoi would slice through the form. " +
            "Wraps CGAL Heat_method_3.",
            "Frahan", "Mesh") { }
    public override Guid ComponentGuid => new Guid("F2D000A8-CADC-4F2D-A0A8-7E60CADA15A0");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override System.Drawing.Bitmap Icon => IconProvider.Load("GeodesicPath.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input surface (2-manifold gives a stable cotangent Laplacian).", GH_ParamAccess.item);
        pManager.AddPointParameter("Seeds", "S",
            "Seed points - each is snapped to the nearest mesh vertex. " +
            "Place 5-50 for a useful tessellation.",
            GH_ParamAccess.list);
        pManager.AddBooleanParameter("Run", "Run", "Set true to compute.", GH_ParamAccess.item, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Cells", "C", "One mesh per geodesic Voronoi cell.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Cell Count", "N", "Number of non-empty cells (== seed count when input is a single connected component).", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Available", "Av", "True iff CGAL shim loaded.", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R", "Diagnostic report.", GH_ParamAccess.item);
    }
    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh m = null;
        var seeds = new List<Point3d>();
        bool run = false;
        if (!da.GetData(0, ref m)) return;
        da.GetDataList(1, seeds);
        da.GetData(2, ref run);

        var av = CgalGeometry.IsAvailable; da.SetData(2, av);
        if (!run) { da.SetData(3, av ? "Run is false." : "CGAL shim NOT loaded."); return; }
        if (!av) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "CGAL shim not loaded."); return; }
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
            var seg = CgalGeometry.SegmentMeshByGeodesicVoronoi(snap, seedFlat);
            sw.Stop();
            var meshes = new List<Mesh>(seg.SegmentCount);
            foreach (var s in seg.Segments) meshes.Add(CgalConvert.FromSnapshot(s));
            da.SetDataList(0, meshes);
            da.SetData(1, seg.SegmentCount);
            da.SetData(3,
                $"Input    : {snap.VertexCount}V / {snap.TriangleCount}F\n" +
                $"Seeds    : {seeds.Count}\n" +
                $"Cells    : {seg.SegmentCount} (non-empty)\n" +
                $"Runtime  : {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"CGAL geodesic Voronoi failed: {ex.GetType().Name}: {ex.Message}");
            da.SetData(3, $"FAILED: {ex.Message}");
        }
    }
}
