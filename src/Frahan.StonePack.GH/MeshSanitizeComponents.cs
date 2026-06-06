#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using Frahan.GH.Attributes;
using Frahan.GH.ScanIngest;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Interfaces;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH;

// =============================================================================
// Mesh sanitation / clean-surface components (2026-05-28). Expose the existing
// Core mesh-cleaning plumbing (CgalGeometry.RepairMesh, GeogramMesh.RepairMesh,
// GeogramMesh.FillHoles) as first-class Frahan > Mesh components, because:
//   - Alpha-Shape / scan reconstruction output is rarely watertight or clean.
//   - CGAL ops (booleans, cut-by-fractures) REJECT invalid / non-manifold /
//     self-touching meshes ("please sanitise the mesh").
// The native shims are now FP-exception-guarded, so these run safely inside
// Rhino (which runs with FP exceptions unmasked).
// =============================================================================

// ─── Validity helper (uses Rhino's own checks for the verdict the user cares
//     about: will CGAL accept this?) ───────────────────────────────────────
internal static class MeshValidity
{
    public static string Describe(Mesh m, string label)
    {
        if (m == null) return $"{label}: <null>";
        int naked = 0;
        try { var ne = m.GetNakedEdges(); naked = ne == null ? 0 : ne.Length; } catch { }
        bool manifold = false, oriented = false;
        try { manifold = m.IsManifold(true, out oriented, out _); } catch { }
        return $"{label}: {m.Vertices.Count}V / {m.Faces.Count}F | " +
               $"closed={m.IsClosed} manifold={manifold} oriented={oriented} " +
               $"nakedLoops={naked} disjoint={m.DisjointMeshCount}";
    }

    // CGAL booleans want closed + manifold + oriented.
    public static bool CgalReady(Mesh m)
    {
        if (m == null || m.Faces.Count == 0) return false;
        bool manifold = false;
        try { manifold = m.IsManifold(true, out _, out _); } catch { }
        return m.IsClosed && manifold;
    }
}

// =============================================================================
// Sanitize Mesh — make a mesh valid (CGAL-acceptable): triangulate, stitch
// coincident borders, drop degenerate faces, unify/orient normals, collect
// garbage. Run this upstream of any CGAL boolean / cut component that rejects
// "invalid" meshes, and on Alpha-Shape / reconstruction output.
// =============================================================================
[RelatedComponent("Frahan > Mesh > Close Holes", Reason = "Pair sanitation with hole-closing to reach a watertight surface.")]
[RelatedComponent("Frahan > Cut > Cut By Fractures (CGAL)", Reason = "CGAL boolean cutters require a sanitized, closed, manifold mesh.")]
[RelatedComponent("Frahan > Mesh > Scan Reconstruct", Reason = "Alpha-Shape / reconstruction output usually needs sanitation before use.")]
[Algorithm("CGAL PMP mesh repair",
    "CGAL Polygon Mesh Processing: triangulate_faces + stitch_borders + remove_degenerate_faces + orient_to_bound_a_volume",
    Note = "Makes a mesh CGAL-acceptable. Geogram repair as fallback.")]
[DesignApplication(
    "Make a mesh valid so CGAL ops accept it: triangulate non-tri faces,  stitch coincident borders, remove dege...",
    DesignFlow.Bridges,
    Precedent = "(from [Algorithm] citation) CGAL Polygon Mesh Processing: triangulate_faces + stitch_borders + remove_degenerate_faces + orient_to_bound_a_volume")]
public sealed class SanitizeMeshComponent : GH_Component
{
    public SanitizeMeshComponent()
        : base("Sanitize Mesh", "Sanitize",
            "Make a mesh valid so CGAL ops accept it: triangulate non-tri faces, " +
            "stitch coincident borders, remove degenerate faces, orient/unify " +
            "normals, drop unused vertices. Use upstream of CGAL boolean / cut " +
            "components and on Alpha-Shape / scan-reconstruction output that " +
            "comes out non-manifold or unwelded.",
            "Frahan", "Mesh")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D05A01-1A2B-4C3D-9E4F-5A6B7C8D9E01");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("PoissonReconstruct.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Mesh", "M", "Mesh to sanitize.", GH_ParamAccess.item);
        p.AddIntegerParameter("Backend", "B",
            "0 = CGAL (strict; what CGAL ops need), 1 = Geogram (robust repair), " +
            "2 = Auto (Geogram then CGAL). Default 0.",
            GH_ParamAccess.item, 0);
        p.AddBooleanParameter("Run", "R", "Set true to sanitize.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Mesh", "M", "Sanitized mesh.", GH_ParamAccess.item);
        p.AddBooleanParameter("CGAL Ready", "Ok",
            "True if the output is closed + manifold (CGAL booleans will accept it).",
            GH_ParamAccess.item);
        p.AddTextParameter("Report", "R", "Before/after validity summary.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        Mesh m = null;
        int backend = 0;
        bool run = false;
        if (!da.GetData(0, ref m)) return;
        da.GetData(1, ref backend);
        da.GetData(2, ref run);

        if (!run) { da.SetData(2, "Run is false. Set Run = true to sanitize."); return; }
        if (m == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh required."); return; }
        if (!CgalGeometry.IsAvailable && !GeogramMesh.IsAvailable)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No mesh-repair shim loaded (frahan_cgal / frahan_geogram).");
            return;
        }

        string before = MeshValidity.Describe(m, "Input ");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var snap = CgalConvert.ToSnapshot(m);
            MeshSnapshot outSnap;
            string used;
            switch (backend)
            {
                case 1:
                    outSnap = GeogramMesh.RepairMesh(snap, GeogramRepairMode.Default, 0.0); used = "Geogram";
                    break;
                case 2:
                    outSnap = MeshOps.Repair(snap, out var be); used = be.ToString();
                    break;
                default: // 0 = CGAL, with Geogram fallback if CGAL refuses
                    try { outSnap = CgalGeometry.RepairMesh(snap); used = "CGAL"; }
                    catch (Exception cex)
                    {
                        if (GeogramMesh.IsAvailable)
                        {
                            outSnap = GeogramMesh.RepairMesh(snap, GeogramRepairMode.Default, 0.0);
                            used = "Geogram (CGAL refused: " + cex.Message + ")";
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                                "CGAL refused the input; used Geogram repair instead.");
                        }
                        else throw;
                    }
                    break;
            }
            sw.Stop();
            var outMesh = CgalConvert.FromSnapshot(outSnap);
            bool ok = MeshValidity.CgalReady(outMesh);
            da.SetData(0, outMesh);
            da.SetData(1, ok);
            da.SetData(2,
                $"Backend : {used}\n{before}\n{MeshValidity.Describe(outMesh, "Output")}\n" +
                $"CGAL-ready: {ok}{(ok ? "" : "  (still not closed+manifold; try Close Holes, or Backend = Auto)")}\n" +
                $"Runtime : {sw.ElapsedMilliseconds} ms");
            if (!ok)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Output is not closed+manifold yet. Run Close Holes downstream, or set Backend = Auto.");
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Sanitize failed: {ex.Message}");
            da.SetData(2, $"FAILED: {ex.Message}\n{before}");
        }
    }
}

// =============================================================================
// Close Holes — fill open boundary loops to make a watertight surface, via
// Geogram's hole filler (GEO::fill_holes). Use on a sanitized Alpha-Shape /
// scan mesh before extruding it into a solid cutter, or before any operation
// that needs a closed volume.
// =============================================================================
[RelatedComponent("Frahan > Mesh > Sanitize Mesh", Reason = "Sanitize first (weld/triangulate), then close holes.")]
[RelatedComponent("Frahan > Mesh > Scan Reconstruct", Reason = "Alpha-Shape output is open; close holes to get a watertight tool mesh.")]
[Algorithm("Geogram hole filling",
    "GEO::fill_holes — triangulate open boundary loops up to an area / edge-count threshold",
    Note = "BSD-3. repair_after cleans duplicate verts/facets left by the triangulator.")]
public sealed class CloseHolesComponent
    : AsyncScanComponent<CloseHolesComponent.Snapshot, CloseHolesComponent.Payload>
{
    public CloseHolesComponent()
        : base("Close Holes", "CloseHoles",
            "Fill open boundary loops to make a watertight mesh. Backend: Managed " +
            "(RhinoCommon Mesh.FillHoles, fast on clean meshes), Geogram " +
            "(GEO::fill_holes, robust on dirty / scan meshes), or Auto (managed " +
            "first, geogram fallback if still open). Max Hole Area / Edges apply " +
            "to the Geogram path. Runs on a background thread (Run gate) so the " +
            "canvas never freezes.",
            "Frahan", "Mesh")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D05A02-1A2B-4C3D-9E4F-5A6B7C8D9E02");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("PoissonReconstruct.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Mesh", "M", "Mesh with holes (open boundary loops).", GH_ParamAccess.item);
        p.AddNumberParameter("Max Hole Area", "A",
            "Geogram path: largest hole AREA to fill (model units squared). 0 fills " +
            "nothing; a very large value (default 1e30) fills every hole.",
            GH_ParamAccess.item, 1e30);
        p.AddIntegerParameter("Max Hole Edges", "E",
            "Geogram path: max boundary edges per hole. 0 = no limit (area governs).",
            GH_ParamAccess.item, 0);
        p.AddBooleanParameter("Repair After", "Rp",
            "Geogram path: run a repair pass after filling. Default true.",
            GH_ParamAccess.item, true);
        p.AddBooleanParameter("Run", "R", "Set true to close holes (background thread).", GH_ParamAccess.item, false);
        // Appended last so existing canvases keep their wiring.
        p.AddIntegerParameter("Backend", "Bk",
            "0 = Auto (managed first, geogram fallback); 1 = Managed (RhinoCommon, " +
            "fast); 2 = Geogram (robust on dirty meshes).",
            GH_ParamAccess.item, 0);
        p[5].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Mesh", "M", "Hole-filled mesh.", GH_ParamAccess.item);
        p.AddBooleanParameter("Closed", "Cl", "True if the output mesh is closed (watertight).", GH_ParamAccess.item);
        p.AddTextParameter("Report", "R", "Before/after validity summary + backend used.", GH_ParamAccess.item);
    }

    public sealed class Snapshot
    {
        public double[] Verts; public int[] Tris;
        public double MaxArea; public int MaxEdges; public bool RepairAfter; public int Backend;
        public int InV, InF; public string Before;
    }

    public sealed class Payload
    {
        public double[] Verts; public int[] Tris;
        public string Source; public long Ms; public int InV, InF; public string Before;
    }

    protected override bool TryRead(IGH_DataAccess da, out bool run, out Snapshot snapshot)
    {
        run = false; snapshot = null;
        da.GetData(4, ref run);
        if (!run) return true;

        Mesh m = null;
        if (!da.GetData(0, ref m) || m == null || m.Faces.Count == 0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh required."); return false; }

        double maxArea = 1e30; int maxEdges = 0; bool repair = true; int backend = 0;
        da.GetData(1, ref maxArea); da.GetData(2, ref maxEdges); da.GetData(3, ref repair); da.GetData(5, ref backend);
        if (backend == 2 && !GeogramMesh.IsAvailable)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Geogram shim not loaded; set Backend = Managed or Auto."); return false; }

        var work = m.DuplicateMesh();
        work.Faces.ConvertQuadsToTriangles();
        var s = new Snapshot
        {
            MaxArea = maxArea, MaxEdges = maxEdges, RepairAfter = repair, Backend = backend,
            InV = work.Vertices.Count, InF = work.Faces.Count,
            Before = MeshValidity.Describe(work, "Input "),
        };
        FlattenMesh(work, out s.Verts, out s.Tris);
        snapshot = s;
        return true;
    }

    protected override Payload Compute(Snapshot s, CancellationToken token, Action<string> progress)
    {
        token.ThrowIfCancellationRequested();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool tryManaged = s.Backend == 0 || s.Backend == 1;

        if (tryManaged)
        {
            progress("filling holes (managed)...");
            var tm = BuildMesh(s.Verts, s.Tris);
            try { tm.FillHoles(); } catch { }
            tm.Compact();
            if (s.Backend == 1 || tm.IsClosed)   // Managed chosen, or Auto already closed it
            {
                FlattenMesh(tm, out double[] mv, out int[] mt);
                sw.Stop();
                return new Payload { Verts = mv, Tris = mt, Ms = sw.ElapsedMilliseconds,
                    Source = s.Backend == 1 ? "Managed (RhinoCommon)" : "Auto -> Managed",
                    InV = s.InV, InF = s.InF, Before = s.Before };
            }
        }

        if (!GeogramMesh.IsAvailable)
            throw new InvalidOperationException("Managed fill left it open and the Geogram shim is not loaded.");
        token.ThrowIfCancellationRequested();
        progress("filling holes (geogram)...");
        var snap = new MeshSnapshot(s.Verts, s.Tris);
        var rem = GeogramMesh.FillHoles(snap, s.MaxArea, s.MaxEdges, s.RepairAfter);
        sw.Stop();
        return new Payload { Verts = ToArr(rem.VertexCoordsXyz), Tris = ToArrI(rem.TriangleIndices),
            Ms = sw.ElapsedMilliseconds, Source = s.Backend == 0 ? "Auto -> Geogram" : "Geogram",
            InV = s.InV, InF = s.InF, Before = s.Before };
    }

    protected override void EmitResult(IGH_DataAccess da, Payload r)
    {
        Mesh outMesh = BuildMesh(r.Verts, r.Tris);
        bool closed = outMesh.IsClosed;
        da.SetData(0, outMesh);
        da.SetData(1, closed);
        da.SetData(2,
            $"{r.Before}\n{MeshValidity.Describe(outMesh, "Output")}\n" +
            $"Backend : {r.Source}\nWatertight: {closed}\nRuntime : {r.Ms} ms");
        if (!closed)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "Still open. Try Backend = Geogram, raise Max Hole Area, clear Max Hole Edges (0), or Sanitize Mesh first.");
    }

    protected override void EmitIdle(IGH_DataAccess da, string message)
    {
        da.SetData(1, false);
        da.SetData(2, message);
        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, message);
    }

    // ── helpers (flatten on UI thread; build output mesh on UI thread) ─────────
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
    { var a = new double[src.Count]; for (int i = 0; i < a.Length; i++) a[i] = src[i]; return a; }

    private static int[] ToArrI(IReadOnlyList<int> src)
    { var a = new int[src.Count]; for (int i = 0; i < a.Length; i++) a[i] = src[i]; return a; }
}
