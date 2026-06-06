#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Interfaces;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // SlabCutByFracturesComponent — accepts a list of Slab DTOs plus a list of
    // Rhino Plane objects (treated as oriented infinite fracture planes) and
    // emits the resulting fragmented Slab list with parent provenance.
    //
    // Wraps the multi-list overload SlabCutter.Cut(slabs, planes, eps). Each
    // Rhino Plane is converted to a FracturePlane via Origin + Normal. The
    // optional eps controls vertex-classification tolerance during splitting
    // (forwarded straight to the cutter).
    //
    // ComponentGuid: C2B3D4E5-6F7A-489B-AC1D-2E3F4A5B6C7D
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Slab Cut By Fractures.
    /// Splits a list of <see cref="Slab"/> DTOs by an ordered list of
    /// fracture planes. Output preserves parent-slab provenance.
    /// </summary>
        [Algorithm("Convex polyhedron half-space clipping (managed)",
        "Frahan-original",
        Note = "Default managed path: convex half-space BSP split (Sutherland-Hodgman family); impl is Frahan's")]
        [Algorithm("Corefinement booleans (optional CGAL backend)",
        "CGAL Polygon Mesh Processing corefine_and_compute_difference/intersection",
        WikiPath = "wiki/index/references.md#CGAL_PMP",
        Note = "Opt-in Use CGAL = true path via CgalMeshBoolean; robust on non-convex / large slabs")]
        [DesignApplication(
        "Cuts a list of Slabs by a list of oriented fracture planes",
        DesignFlow.TopDown,
        Precedent = "Frahan-original slab-cut by fracture set")]
    public sealed class SlabCutByFracturesComponent : GH_Component
    {
        public SlabCutByFracturesComponent()
            : base(
                "Slab Cut By Fractures", "SlabCut",
                "Cuts a list of Slabs by a list of oriented fracture planes. " +
                "Each Rhino Plane is interpreted as an infinite plane (Origin, Normal). " +
                "Output Slabs carry the input-list parent index so callers can " +
                "track 'this fragment came from quarry block #N'. " +
                "Managed path is Frahan-original; opt-in CGAL backend uses CGAL PMP booleans (CGAL_PMP).",
                "Frahan", "Slab")
        {
        }

        // GUID literal: C2B3D4E5-6F7A-489B-AC1D-2E3F4A5B6C7D
        public override Guid ComponentGuid =>
            new Guid("C2B3D4E5-6F7A-489B-AC1D-2E3F4A5B6C7D");

        protected override Bitmap Icon => IconProvider.Load("BlockCutOpt.png");

        // ─── Params ─────────────────────────────────────────────────────────

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Mesh", "M",
                "Convex meshes to cut. Standard Rhino mesh wires; the cutter " +
                "converts to its internal Slab DTO automatically. Multi-shell " +
                "meshes should be split with Mesh Shell Split first.",
                GH_ParamAccess.list);
            p.AddGenericParameter("Plane", "P",
                "Oriented infinite fracture planes. Accepts the Frahan " +
                "FracturePlane DTO (from any *Fracture Planes generator) OR a " +
                "Rhino Plane (origin + normal). The two are interchangeable.",
                GH_ParamAccess.list);
            p.AddNumberParameter("Eps", "E",
                "Vertex-classification tolerance. Default 1e-9 works for most " +
                "metric inputs; raise to 1e-6 for non-metric or noisy meshes.",
                GH_ParamAccess.item, 1e-9);
            p[2].Optional = true;
            // Appended last so existing canvases keep their wiring. Default false.
            p.AddBooleanParameter("Use CGAL", "Cg",
                "Backend. False (default) = managed convex SlabCutter (fast, but " +
                "convex-only and explodes combinatorially on large slabs with many " +
                "planes). True = route the cut through the CGAL boolean kernel " +
                "(CgalMeshBoolean): robust on non-convex / large slabs. CGAL path " +
                "returns meshes (the Slab output is empty). Falls back to managed " +
                "if the CGAL shim is not loaded.",
                GH_ParamAccess.item, false);
            p[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Slab", "S",
                "Output Slabs after cutting.",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Parent", "P",
                "Per-output parent index (0-based) into the input Slab list.",
                GH_ParamAccess.list);
            p.AddNumberParameter("TotalVolume", "V",
                "Sum of signed volumes of all output Slabs (sanity check).",
                GH_ParamAccess.item);
            p.AddIntegerParameter("Count", "N",
                "Number of resulting Slabs.",
                GH_ParamAccess.item);
            p.AddMeshParameter("Mesh", "M",
                "Output Slabs as Rhino Meshes (parallel to the Slab list). Wire " +
                "into native components (Move, Bake, Boolean, Volume, etc.).",
                GH_ParamAccess.list);
        }

        // ─── Solve ──────────────────────────────────────────────────────────

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var meshes = new List<Mesh>();
            da.GetDataList(0, meshes);

            var rawPlanes = new List<object>();
            da.GetDataList(1, rawPlanes);

            double eps = 1e-9;
            da.GetData(2, ref eps);
            bool useCgal = false;
            da.GetData(3, ref useCgal);

            if (meshes.Count == 0 || rawPlanes.Count == 0)
            {
                da.SetDataList(0, new List<Slab>());
                da.SetDataList(1, new List<int>());
                da.SetData(2, 0.0);
                da.SetData(3, 0);
                da.SetDataList(4, new List<Mesh>());
                return;
            }

            if (useCgal)
            {
                if (TryCutWithCgal(da, meshes, rawPlanes)) return;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Use CGAL = true but the CGAL shim is not loaded; falling back to the managed convex SlabCutter.");
            }

            var slabs = new List<Slab>(meshes.Count);
            for (int i = 0; i < meshes.Count; i++)
            {
                var s = GhInterop.SlabFromMesh(meshes[i]);
                if (s == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"Mesh[{i}] is invalid (need >= 4 vertices and >= 4 faces).");
                    return;
                }
                slabs.Add(s);
            }

            // ---- Unwrap planes (FracturePlane DTO or Rhino Plane) ---------
            var planes = new List<FracturePlane>(rawPlanes.Count);
            for (int i = 0; i < rawPlanes.Count; i++)
            {
                var fp = GhInterop.UnwrapPlane(rawPlanes[i]);
                if (fp == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"Plane[{i}] is not a FracturePlane or Plane (got {GhInterop.DescribeType(rawPlanes[i])}).");
                    return;
                }
                planes.Add(fp);
            }

            SlabCutResult result;
            try
            {
                result = SlabCutter.Cut(slabs, planes, eps);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"SlabCutter.Cut failed: {ex.Message}");
                return;
            }

            var outSlabs = new List<Slab>(result.Slabs.Count);
            for (int i = 0; i < result.Slabs.Count; i++) outSlabs.Add(result.Slabs[i]);

            var outParents = new List<int>(result.ParentIndices.Count);
            for (int i = 0; i < result.ParentIndices.Count; i++) outParents.Add(result.ParentIndices[i]);

            da.SetDataList(0, outSlabs);
            da.SetDataList(1, outParents);
            da.SetData(2, result.TotalVolume());
            da.SetData(3, result.Count);
            da.SetDataList(4, GhInterop.SlabsToMeshes(outSlabs));
        }

        // ─── CGAL backend (robust on large / non-convex slabs) ───────────────
        // BSP-split each input mesh by each plane via CgalMeshBoolean: for each
        // plane build a half-space tool box on the +normal side, then peel each
        // current piece into its +side (Intersection) and -side (Difference).
        // Returns true if it handled the cut (CGAL available); false to let the
        // caller fall back to the managed convex path.
        private bool TryCutWithCgal(IGH_DataAccess da, List<Mesh> meshes, List<object> rawPlanes)
        {
            if (!CgalMeshBoolean.IsAvailable) return false;

            var planes = new List<FracturePlane>(rawPlanes.Count);
            for (int i = 0; i < rawPlanes.Count; i++)
            {
                var fp = GhInterop.UnwrapPlane(rawPlanes[i]);
                if (fp == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"Plane[{i}] is not a FracturePlane or Plane (got {GhInterop.DescribeType(rawPlanes[i])}).");
                    return true; // handled (with error); do not fall back
                }
                planes.Add(fp);
            }

            const int cellCap = 4096;
            var kernel = CsgKernelMode.Inexact;
            var outMeshes = new List<Mesh>();
            var parents = new List<int>();
            bool hitCap = false;

            for (int i = 0; i < meshes.Count; i++)
            {
                if (meshes[i] == null || !meshes[i].IsValid)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Mesh[{i}] invalid; skipped.");
                    continue;
                }
                BoundingBox bb = meshes[i].GetBoundingBox(true);
                double r = bb.Diagonal.Length * 2.0;
                if (r <= 0) r = 1.0;

                var pieces = new List<MeshSnapshot> { CgalConvert.ToSnapshot(meshes[i]) };
                foreach (var pl in planes)
                {
                    MeshSnapshot tool = HalfSpaceToolSnapshot(pl, r);
                    var next = new List<MeshSnapshot>(pieces.Count * 2);
                    foreach (var piece in pieces)
                    {
                        MeshSnapshot inside = null, outside = null;
                        try { inside = CgalMeshBoolean.Intersection(piece, tool, kernel, out _); } catch { }
                        try { outside = CgalMeshBoolean.Difference(piece, tool, kernel, out _); } catch { }
                        bool added = false;
                        if (NonEmpty(inside)) { next.Add(inside); added = true; }
                        if (NonEmpty(outside)) { next.Add(outside); added = true; }
                        if (!added) next.Add(piece); // plane missed this piece; keep it whole
                    }
                    pieces = next;
                    if (pieces.Count > cellCap) { hitCap = true; break; }
                }

                foreach (var p in pieces)
                {
                    Mesh m = CgalConvert.FromSnapshot(p);
                    if (m != null && m.IsValid) { outMeshes.Add(m); parents.Add(i); }
                }
            }

            if (hitCap)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Piece count exceeded {cellCap}; stopped splitting early (reduce plane count or pre-segment).");

            double vol = 0.0;
            foreach (var m in outMeshes)
            {
                try { var v = VolumeMassProperties.Compute(m); if (v != null) vol += Math.Abs(v.Volume); }
                catch { }
            }

            da.SetDataList(0, new List<Slab>());   // CGAL path returns meshes, not Slab DTOs
            da.SetDataList(1, parents);
            da.SetData(2, vol);
            da.SetData(3, outMeshes.Count);
            da.SetDataList(4, outMeshes);
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"CGAL backend: {outMeshes.Count} piece(s) from {meshes.Count} mesh(es) x {planes.Count} plane(s). " +
                "Slab output is empty; use the Mesh output.");
            return true;
        }

        private static MeshSnapshot HalfSpaceToolSnapshot(FracturePlane pl, double r)
        {
            var origin = new Point3d(pl.PointX, pl.PointY, pl.PointZ);
            var normal = new Vector3d(pl.NormalX, pl.NormalY, pl.NormalZ);
            var frame = new Plane(origin, normal);                 // frame Z = plane normal
            var box = new Box(frame, new Interval(-r, r), new Interval(-r, r), new Interval(0, 2 * r));
            return CgalConvert.ToSnapshot(Mesh.CreateFromBox(box, 1, 1, 1));
        }

        private static bool NonEmpty(MeshSnapshot s) => s != null && s.TriangleCount > 0;
    }
}
