#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Interfaces;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH;

// =============================================================================
// CgalCutComponents — production CGAL-backed cutters that complement the
// existing plane-based slab + quarry cutters. The plane-based cutters
// (SlabCutByFractures, FractureCutter, QuarryDecomposer) are analytic and
// fast but constrained to convex slabs cut by infinite planes / convex
// finite polygons. These two components extend the family to:
//
//   1. Slab Cut By Tool Mesh (CGAL)        — cut any slab by an arbitrary
//                                              tool mesh (sculpted / scanned
//                                              fracture surfaces, free-form
//                                              CSG cutters).
//   2. Quarry Decompose By Mesh (CGAL)     — split a non-convex quarry
//                                              (e.g. a scanned boulder) into
//                                              blocks by intersecting it with
//                                              a regular grid of box meshes.
//
// Both rely on CgalMeshBoolean (managed front-end over the native
// frahan_cgal shim). When the shim is not available the user gets a
// clear error: there is no analytic fallback for arbitrary mesh-mesh
// CSG, only the BSP fallback that already lives inside CgalMeshBoolean
// — which we still surface so the user can see which back-end ran.
//
// Default kernel is Hybrid (EPICK storage + EPECK constructions). The
// 2–5x speed cost vs Inexact buys numerical robustness on the kind of
// fragile inputs that motivate using CGAL in the first place.
// =============================================================================

// =============================================================================
// Slab Cut By Tool Mesh (CGAL)
//
// Cut an arbitrary slab/block mesh by an arbitrary tool mesh and return
// the outside (slab − tool) and inside (slab ∩ tool) halves. Either or
// both halves can be requested via the Mode input — running both halves
// costs two CGAL calls, but the second call reuses the same input
// snapshots so most of the per-call setup is free.
// =============================================================================

[Algorithm("Exact-predicate mesh-mesh CSG via corefinement",
    "CGAL Polygon Mesh Processing corefine_and_compute_difference/intersection (EPICK+EPECK Hybrid kernel)",
    WikiPath = "wiki/index/references.md#CGAL_PMP")]
[DesignApplication(
    "Cuts a slab/block mesh by an arbitrary tool mesh via CGAL  exact-predicate booleans",
    DesignFlow.Bridges)]
public sealed class CgalSlabCutByToolMeshComponent : FrahanComponentBase
{
    public CgalSlabCutByToolMeshComponent()
        : base("Slab Cut By Tool Mesh (CGAL)", "SlabCutCgal",
            "Cuts a slab/block mesh by an arbitrary tool mesh via CGAL " +
            "exact-predicate booleans. Outputs the outside half " +
            "(slab − tool), the inside half (slab ∩ tool), or both. " +
            "Use this for non-convex slabs or curved/sculpted fracture " +
            "tools where the plane-based cutter does not apply. " +
            "Implements CGAL PMP corefinement booleans (CGAL_PMP).",
            "Frahan", "Slab")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D000C0-CADC-4F2D-A0C0-7E60CADA15A0");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("BlockCutOpt.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Slab", "S",
            "Slab/block mesh to cut. Must be closed and manifold for " +
            "predictable output (run Mesh Repair (CGAL) upstream if in " +
            "doubt).",
            GH_ParamAccess.item);
        p.AddMeshParameter("Tool", "T",
            "Tool mesh used as the cutter. Closed manifold mesh.",
            GH_ParamAccess.item);
        p.AddIntegerParameter("Mode", "M",
            "0 = Outside only (slab − tool). " +
            "1 = Inside only  (slab ∩ tool). " +
            "2 = Both halves (default).",
            GH_ParamAccess.item, 2);
        p.AddBooleanParameter("Hybrid Kernel", "Hy",
            "True (default) = HYBRID — EPICK storage + EPECK intersection " +
            "construction. Robust on near-tangent contacts and multi-cut " +
            "chains at a 2–5x speed cost. " +
            "False = EPICK only — fastest, fine for well-conditioned inputs.",
            GH_ParamAccess.item, true);
        p.AddBooleanParameter("Run", "Run",
            "Set true to compute. Heavy operation on large inputs.",
            GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Outside", "O",
            "Slab − Tool. The portion of the slab outside the tool. " +
            "Empty mesh when the tool fully contains the slab.",
            GH_ParamAccess.item);
        p.AddMeshParameter("Inside", "I",
            "Slab ∩ Tool. The portion of the slab inside the tool. " +
            "Empty mesh when the tool misses the slab entirely.",
            GH_ParamAccess.item);
        p.AddTextParameter("Backend", "B",
            "Which kernel ran: 'CGAL' or 'ManagedBsp' (BSP fallback).",
            GH_ParamAccess.item);
        p.AddBooleanParameter("Available", "Av",
            "True iff the CGAL native shim is loadable.",
            GH_ParamAccess.item);
        p.AddTextParameter("Report", "R",
            "Diagnostic report (mode, kernel, vertex/face counts, runtime).",
            GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh slab = null, tool = null;
        int mode = 2;
        bool hybrid = true;
        bool run = false;

        if (!da.GetData(0, ref slab)) return;
        if (!da.GetData(1, ref tool)) return;
        da.GetData(2, ref mode);
        da.GetData(3, ref hybrid);
        da.GetData(4, ref run);

        var available = CgalMeshBoolean.IsAvailable;
        da.SetData(3, available);

        if (!run)
        {
            da.SetData(4, available
                ? "Run is false. CGAL shim is loaded and ready."
                : "Run is false. CGAL shim NOT loaded; will fall back to BSP.");
            return;
        }

        if (slab == null || tool == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Both Slab and Tool are required.");
            return;
        }

        if (mode < 0 || mode > 2)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Mode must be 0, 1, or 2 (got {mode}).");
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var sa = CgalConvert.ToSnapshot(slab);
            var sb = CgalConvert.ToSnapshot(tool);
            var kernel = hybrid ? CsgKernelMode.Hybrid : CsgKernelMode.Inexact;

            CsgBackend backend = CsgBackend.ManagedBsp;
            MeshSnapshot outsideSnap = null, insideSnap = null;
            long tDiff = -1, tInter = -1;

            if (mode == 0 || mode == 2)
            {
                var t0 = sw.ElapsedMilliseconds;
                outsideSnap = CgalMeshBoolean.Difference(sa, sb, kernel, out backend);
                tDiff = sw.ElapsedMilliseconds - t0;
            }
            if (mode == 1 || mode == 2)
            {
                var t0 = sw.ElapsedMilliseconds;
                insideSnap = CgalMeshBoolean.Intersection(sa, sb, kernel, out backend);
                tInter = sw.ElapsedMilliseconds - t0;
            }
            sw.Stop();

            if (outsideSnap != null)
                da.SetData(0, CgalConvert.FromSnapshot(outsideSnap));
            if (insideSnap != null)
                da.SetData(1, CgalConvert.FromSnapshot(insideSnap));
            da.SetData(2, backend.ToString());

            string modeName = mode switch
            {
                0 => "Outside only",
                1 => "Inside only",
                _ => "Both halves",
            };
            string OutDesc(MeshSnapshot s) => s == null
                ? "—"
                : $"{s.VertexCount}V / {s.TriangleCount}F";
            string OutTime(long ms) => ms < 0 ? "—" : $"{ms} ms";

            da.SetData(4,
                $"Mode      : {modeName}\n" +
                $"Backend   : {backend} (kernel: {kernel})\n" +
                $"Slab in   : {sa.VertexCount}V / {sa.TriangleCount}F\n" +
                $"Tool in   : {sb.VertexCount}V / {sb.TriangleCount}F\n" +
                $"Outside   : {OutDesc(outsideSnap)}  ({OutTime(tDiff)})\n" +
                $"Inside    : {OutDesc(insideSnap)}  ({OutTime(tInter)})\n" +
                $"Total     : {sw.ElapsedMilliseconds} ms\n");
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"CGAL slab cut failed: {ex.GetType().Name}: {ex.Message}");
            da.SetData(4, $"FAILED: {ex.Message}");
        }
    }
}

// =============================================================================
// Quarry Decompose By Mesh (CGAL)
//
// Take a (possibly non-convex) quarry mesh and a 3D grid of box cells.
// For every cell, intersect the box against the quarry mesh. Each non-
// empty intersection becomes one output block. Empty intersections (cells
// that miss the quarry entirely) are dropped.
//
// Useful when the quarry is a scanned boulder or otherwise non-convex —
// the existing plane-based QuarryDecomposeComponent assumes convex input
// and slices by infinite planes, so it does not handle this case.
//
// The grid is defined by a Box (oriented bounding box). When the user
// does not supply a box, the world-aligned bounding box of the quarry
// mesh is used. Grid counts (nX, nY, nZ) describe how the box is sliced
// along each of its three axes.
// =============================================================================

[Algorithm("CGAL Polygon Mesh Processing corefinement", "CGAL Polygon Mesh Processing (corefine_and_compute, EPECK/EPICK hybrid kernel)", WikiPath = "wiki/index/references.md")]
[DesignApplication(
    "Decomposes a (possibly non-convex) quarry mesh into blocks  by intersecting it against a 3D grid of box cel...",
    DesignFlow.Bridges)]
public sealed class CgalQuarryDecomposeByMeshComponent : FrahanComponentBase
{
    public CgalQuarryDecomposeByMeshComponent()
        : base("Quarry Decompose By Mesh (CGAL)", "QuarryDcCgal",
            "Decomposes a (possibly non-convex) quarry mesh into blocks " +
            "by intersecting it against a 3D grid of box cells via CGAL. " +
            "Empty cells are dropped automatically. Use this when the " +
            "plane-based Quarry Decompose does not apply because the " +
            "quarry mesh is not convex. Implements CGAL PMP corefinement. " +
            "Selection: convex pieces -> By CoACD; plane-bounded cuts -> By Mesh (CGAL); cell partition -> By Voronoi.",
            "Frahan", "Quarry")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D000C1-CADC-4F2D-A0C1-7E60CADA15A0");
    public override GH_Exposure Exposure => GH_Exposure.secondary;
    protected override Bitmap Icon => IconProvider.Load("CoacdDecompose.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Quarry", "Q",
            "Quarry mesh. Must be closed and manifold (run Mesh Repair " +
            "(CGAL) upstream if in doubt). Need not be convex.",
            GH_ParamAccess.item);
        p.AddBoxParameter("Grid Box", "Gb",
            "Oriented box that defines the grid extent + orientation. " +
            "If empty (Box.Empty / Box.Unset), the world-aligned bounding " +
            "box of the Quarry mesh is used.",
            GH_ParamAccess.item, Box.Empty);
        p[1].Optional = true;
        p.AddIntegerParameter("nX", "nX",
            "Grid divisions along the box's local +X axis (>= 1).",
            GH_ParamAccess.item, 4);
        p.AddIntegerParameter("nY", "nY",
            "Grid divisions along the box's local +Y axis (>= 1).",
            GH_ParamAccess.item, 4);
        p.AddIntegerParameter("nZ", "nZ",
            "Grid divisions along the box's local +Z axis (>= 1).",
            GH_ParamAccess.item, 2);
        p.AddBooleanParameter("Hybrid Kernel", "Hy",
            "True (default) = HYBRID kernel for robustness on every " +
            "cell intersection. False = EPICK only (fastest).",
            GH_ParamAccess.item, true);
        p.AddBooleanParameter("Run", "Run",
            "Set true to compute. Cost scales with nX*nY*nZ CGAL calls.",
            GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Blocks", "B",
            "One mesh per non-empty grid cell intersection (Quarry ∩ cell).",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Cell Index", "Ci",
            "Flat (i + j*nX + k*nX*nY) cell index for each output block, " +
            "parallel to the Blocks list. Lets the caller correlate " +
            "outputs with their originating cell.",
            GH_ParamAccess.list);
        p.AddTextParameter("Backend", "B",
            "Which kernel ran on the most recent cell.",
            GH_ParamAccess.item);
        p.AddBooleanParameter("Available", "Av",
            "True iff the CGAL native shim is loadable.",
            GH_ParamAccess.item);
        p.AddTextParameter("Report", "R",
            "Diagnostic report (cells visited / kept / dropped, runtime).",
            GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh quarry = null;
        Box box = Box.Empty;
        int nX = 4, nY = 4, nZ = 2;
        bool hybrid = true;
        bool run = false;

        if (!da.GetData(0, ref quarry)) return;
        da.GetData(1, ref box);
        da.GetData(2, ref nX);
        da.GetData(3, ref nY);
        da.GetData(4, ref nZ);
        da.GetData(5, ref hybrid);
        da.GetData(6, ref run);

        var available = CgalMeshBoolean.IsAvailable;
        da.SetData(3, available);

        if (!run)
        {
            da.SetData(4, available
                ? "Run is false. CGAL shim is loaded and ready."
                : "Run is false. CGAL shim NOT loaded; will fall back to BSP.");
            return;
        }
        if (quarry == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Quarry is required.");
            return;
        }
        if (nX < 1 || nY < 1 || nZ < 1)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Grid counts must be >= 1 (got {nX},{nY},{nZ}).");
            return;
        }

        // Default the box to the quarry's world-aligned bounding box.
        if (!box.IsValid || box.Volume <= 0.0)
        {
            var bb = quarry.GetBoundingBox(true);
            if (!bb.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Quarry has no valid bounding box.");
                return;
            }
            box = new Box(Plane.WorldXY, bb);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var quarrySnap = CgalConvert.ToSnapshot(quarry);
            var kernel = hybrid ? CsgKernelMode.Hybrid : CsgKernelMode.Inexact;

            // Box dimensions along its local axes.
            var plane = box.Plane;
            double xLen = box.X.Length, yLen = box.Y.Length, zLen = box.Z.Length;
            double dx = xLen / nX, dy = yLen / nY, dz = zLen / nZ;
            double x0 = box.X.Min, y0 = box.Y.Min, z0 = box.Z.Min;

            int totalCells = nX * nY * nZ;
            var blocks = new List<Mesh>(totalCells);
            var cellIndices = new List<int>(totalCells);
            int kept = 0, dropped = 0;
            CsgBackend backend = CsgBackend.ManagedBsp;

            for (int kk = 0; kk < nZ; kk++)
            for (int jj = 0; jj < nY; jj++)
            for (int ii = 0; ii < nX; ii++)
            {
                var cellInterval = new Interval(x0 + ii * dx, x0 + (ii + 1) * dx);
                var cellIntervalY = new Interval(y0 + jj * dy, y0 + (jj + 1) * dy);
                var cellIntervalZ = new Interval(z0 + kk * dz, z0 + (kk + 1) * dz);
                var cellBox = new Box(plane, cellInterval, cellIntervalY, cellIntervalZ);
                var cellMesh = Mesh.CreateFromBox(cellBox, 1, 1, 1);
                if (cellMesh == null || cellMesh.Faces.Count == 0)
                {
                    dropped++;
                    continue;
                }

                var cellSnap = CgalConvert.ToSnapshot(cellMesh);
                MeshSnapshot intersected;
                try
                {
                    intersected = CgalMeshBoolean.Intersection(
                        quarrySnap, cellSnap, kernel, out backend);
                }
                catch
                {
                    // A single fragile cell should not abort the whole grid;
                    // skip it and continue. CgalMeshBoolean already wrapped
                    // the native error message into the exception.
                    dropped++;
                    continue;
                }

                if (intersected == null
                    || intersected.VertexCount == 0
                    || intersected.TriangleCount == 0)
                {
                    dropped++;
                    continue;
                }

                blocks.Add(CgalConvert.FromSnapshot(intersected));
                cellIndices.Add(ii + jj * nX + kk * nX * nY);
                kept++;
            }
            sw.Stop();

            da.SetDataList(0, blocks);
            da.SetDataList(1, cellIndices);
            da.SetData(2, backend.ToString());
            da.SetData(4,
                $"Kernel    : {kernel}\n" +
                $"Backend   : {backend} (last cell)\n" +
                $"Quarry    : {quarrySnap.VertexCount}V / {quarrySnap.TriangleCount}F\n" +
                $"Grid      : {nX}x{nY}x{nZ} = {totalCells} cells\n" +
                $"Kept      : {kept}\n" +
                $"Dropped   : {dropped}\n" +
                $"Runtime   : {sw.ElapsedMilliseconds} ms\n" +
                $"Per cell  : {(totalCells > 0 ? sw.ElapsedMilliseconds / (double)totalCells : 0.0):F1} ms avg\n");
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"CGAL quarry decompose failed: {ex.GetType().Name}: {ex.Message}");
            da.SetData(4, $"FAILED: {ex.Message}");
        }
    }
}
