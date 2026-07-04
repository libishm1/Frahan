#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Kintsugi;

// =============================================================================
// Frahan > Kintsugi > Fragment Shatter (test-bed shard generator).
//
// Takes a solid input mesh (pot, sphere, cube, sculpture) and emits a list
// of broken fragment meshes via Voronoi cell decomposition. The fragments
// are intended to be fed directly into KintsugiAssemblyComponent for round-
// trip testing of the reassembly pipeline.
//
// Algorithm:
//   1. Seed N random points inside the mesh's bounding region (deterministic
//      via the Seed input).
//   2. For each seed, compute its Voronoi cell by repeatedly splitting the
//      input mesh with the bisector plane of (seed, other_seed) and keeping
//      the half that contains `seed`.
//   3. The N resulting meshes are the shards. Each one has the original
//      outer surface plus newly-exposed fracture rims (which are the naked
//      edges that Kintsugi later matches against).
//
// O(N^2) plane splits. For N <= 30 this runs in a few seconds on a typical
// pot mesh. Larger N (40+) is honest about expensive.
//
// Why pure Mesh.Split (not CGAL booleans):
//   - net48 friendly, no native dependency required.
//   - Voronoi cells are convex when measured against the mesh's bounding
//     box; the iterative-clip pattern is robust.
//   - Output meshes inherit the input's outer surface verbatim so the
//     reassembly demo is visually obvious.
//
// CGAL booleans (CgalMeshBoolean.Difference) are available as a fallback
// when the input is non-convex and the Mesh.Split clipper leaves slivers;
// expose via Mode=Cgal when CGAL is installed.
// =============================================================================

[Algorithm("Voronoi shatter for fracture test-beds",
    "Frahan-original: deterministic seed -> pairwise bisector planes -> " +
    "iterative Mesh.Split -> per-seed Voronoi cell. " +
    "Intended as the upstream feeder for KintsugiAssemblyComponent.",
    Note = "O(N^2) plane splits. Convex seeds work best; for highly non-convex inputs, " +
           "small slivers can appear at concave folds. Workaround: feed a convex hull or " +
           "use a larger Min Fragment Volume to drop slivers.")]
[DesignApplication(
    "Voronoi-shatter a solid input mesh into N fragments suitable  for round-trip testing of Frahan Kintsugi",
    DesignFlow.BottomUp,
    Precedent = "Frahan-original Voronoi-shatter; Aurenhammer 1991 Voronoi diagrams")]
public sealed class FragmentShatterComponent : FrahanComponentBase
{
    public FragmentShatterComponent()
        : base("Fragment Shatter",
            "Shatter",
            "Voronoi-shatter a solid input mesh into N fragments suitable " +
            "for round-trip testing of Frahan Kintsugi. " +
            "Outputs each Voronoi cell as a separate mesh with the original " +
            "outer surface plus fresh fracture rims on the cut surfaces.",
            "Frahan", "Kintsugi")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("F2D00502-2026-4522-B0B0-1ABE15A0CAFE");

    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("SyntheticBlock.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Solid", "M",
            "Input mesh to shatter. Pot, sphere, sculpture, etc. Should be " +
            "closed-ish; tiny gaps are tolerated.",
            GH_ParamAccess.item);
        p.AddIntegerParameter("Fragment Count", "N",
            "Number of Voronoi cells (= output fragments). Practical range 2..30. " +
            "Higher = slower (O(N^2) plane clips).",
            GH_ParamAccess.item, 8);
        p.AddIntegerParameter("Seed", "S",
            "Deterministic random seed. Re-running with the same value " +
            "produces the same shatter pattern. Default 42.",
            GH_ParamAccess.item, 42);
        p.AddNumberParameter("Jitter", "J",
            "Voronoi seed positional noise relative to the bbox diagonal. " +
            "0 = grid layout, 1 = full random in the bbox. Default 0.6 " +
            "(mostly random with a touch of regularity for predictable demos).",
            GH_ParamAccess.item, 0.6);
        p.AddNumberParameter("Min Fragment Volume", "Vmin",
            "Drop any cell whose volume is below this fraction of the " +
            "input volume (0 to disable). Default 0.005 = 0.5% drops " +
            "slivers from edge cells.",
            GH_ParamAccess.item, 0.005);
        p.AddBooleanParameter("Run", "R", "Execute the shatter.",
            GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Fragments", "F",
            "Voronoi-shattered fragments. Wire directly into Kintsugi.",
            GH_ParamAccess.list);
        p.AddPointParameter("Seed Points", "Sp",
            "Voronoi seed points used (one per fragment). Diagnostic.",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Drop Count", "Dc",
            "Number of cells dropped under Min Fragment Volume.",
            GH_ParamAccess.item);
        p.AddTextParameter("Report", "Rp",
            "Per-cell volume / face / vertex / naked-edge counts.",
            GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh solid = null;
        int n = 8;
        int seed = 42;
        double jitter = 0.6;
        double vminFrac = 0.005;
        bool run = false;

        if (!da.GetData(0, ref solid) || solid == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Solid mesh input not wired.");
            return;
        }
        da.GetData(1, ref n);
        da.GetData(2, ref seed);
        da.GetData(3, ref jitter);
        da.GetData(4, ref vminFrac);
        da.GetData(5, ref run);

        if (!run)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Set Run=True to shatter.");
            da.SetData(3, "Run is false.");
            return;
        }
        if (n < 2)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Fragment Count must be >= 2 (got {n}).");
            return;
        }

        var bbox = solid.GetBoundingBox(true);
        if (!bbox.IsValid)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input mesh bounding box invalid.");
            return;
        }
        double diag = bbox.Diagonal.Length;
        double inputVolume;
        try { inputVolume = solid.Volume(); } catch { inputVolume = 0.0; }
        if (inputVolume <= 0) inputVolume = bbox.Volume;

        var seeds = SamplePoissonInBbox(bbox, n, seed, jitter);
        var fragments = new List<Mesh>(n);
        int dropCount = 0;
        var report = new System.Text.StringBuilder();

        for (int i = 0; i < seeds.Count; i++)
        {
            var cell = ClipMeshToVoronoiCell(solid, seeds, i, diag);
            if (cell == null || cell.Vertices.Count == 0)
            {
                dropCount++;
                continue;
            }
            // Volume readings need a closed mesh. Our cell is OPEN
            // along every cut plane (that's the whole point -- those
            // open boundaries become fracture rims for Kintsugi). So
            // build a throw-away CLOSED copy just for the volume read,
            // and discard it.
            double cellVol = 0;
            try
            {
                var closed = cell.DuplicateMesh();
                closed.FillHoles();
                cellVol = closed.Volume();
            }
            catch { cellVol = 0; }
            // Fallback: if FillHoles + Volume both fail, use the
            // axis-aligned bbox volume as a coarse stand-in.
            if (cellVol <= 0)
            {
                var pbb = cell.GetBoundingBox(true);
                if (pbb.IsValid) cellVol = pbb.Volume;
            }
            if (vminFrac > 0 && inputVolume > 0 && cellVol < inputVolume * vminFrac)
            {
                dropCount++;
                continue;
            }
            cell.Normals.ComputeNormals();
            cell.Compact();
            fragments.Add(cell);

            int nakedEdges = 0;
            try
            {
                var top = cell.TopologyEdges;
                for (int e = 0; e < top.Count; e++)
                {
                    if (top.GetConnectedFaces(e).Length == 1) nakedEdges++;
                }
            }
            catch { /* topology issues -> diagnostic stays at 0 */ }

            report.AppendLine(
                $"Cell {i:D2}: volume={cellVol:F4} ({cellVol / inputVolume * 100:F1}%), " +
                $"V={cell.Vertices.Count}, F={cell.Faces.Count}, naked-edges={nakedEdges}");
        }

        if (fragments.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "Shatter produced no fragments above the volume threshold. " +
                "Lower Min Fragment Volume or reduce Fragment Count.");
            da.SetDataList(0, new List<Mesh>());
            da.SetDataList(1, seeds);
            da.SetData(2, dropCount);
            da.SetData(3, "no fragments survived volume filter.");
            return;
        }

        da.SetDataList(0, fragments);
        da.SetDataList(1, seeds);
        da.SetData(2, dropCount);
        da.SetData(3, report.ToString());
    }

    // -------------------------------------------------------------------------
    // Seed generation (Poisson-style with grid bias).
    // -------------------------------------------------------------------------

    private static List<Point3d> SamplePoissonInBbox(BoundingBox bbox, int n, int seed, double jitter)
    {
        var rng = new Random(seed);
        // Start with a regular cubic grid then jitter by `jitter * cellSize`.
        int grid = (int)Math.Ceiling(Math.Pow(n, 1.0 / 3.0));
        double dx = (bbox.Max.X - bbox.Min.X) / grid;
        double dy = (bbox.Max.Y - bbox.Min.Y) / grid;
        double dz = (bbox.Max.Z - bbox.Min.Z) / grid;
        var pts = new List<Point3d>(n);
        for (int gx = 0; gx < grid && pts.Count < n; gx++)
            for (int gy = 0; gy < grid && pts.Count < n; gy++)
                for (int gz = 0; gz < grid && pts.Count < n; gz++)
                {
                    double cx = bbox.Min.X + (gx + 0.5) * dx;
                    double cy = bbox.Min.Y + (gy + 0.5) * dy;
                    double cz = bbox.Min.Z + (gz + 0.5) * dz;
                    double jx = (rng.NextDouble() - 0.5) * dx * jitter;
                    double jy = (rng.NextDouble() - 0.5) * dy * jitter;
                    double jz = (rng.NextDouble() - 0.5) * dz * jitter;
                    pts.Add(new Point3d(cx + jx, cy + jy, cz + jz));
                }
        // Top up with fully-random points if grid was undershot.
        while (pts.Count < n)
        {
            pts.Add(new Point3d(
                bbox.Min.X + rng.NextDouble() * (bbox.Max.X - bbox.Min.X),
                bbox.Min.Y + rng.NextDouble() * (bbox.Max.Y - bbox.Min.Y),
                bbox.Min.Z + rng.NextDouble() * (bbox.Max.Z - bbox.Min.Z)));
        }
        return pts;
    }

    // -------------------------------------------------------------------------
    // Voronoi cell extraction.
    // For seed i, clip the mesh by every bisector plane between seed i and
    // every other seed j, keeping the half that contains seed i.
    // -------------------------------------------------------------------------

    private static Mesh ClipMeshToVoronoiCell(Mesh source, List<Point3d> seeds, int i, double diag)
    {
        var cell = source.DuplicateMesh();
        var s = seeds[i];
        for (int j = 0; j < seeds.Count; j++)
        {
            if (j == i) continue;
            var mid = new Point3d(
                (s.X + seeds[j].X) * 0.5,
                (s.Y + seeds[j].Y) * 0.5,
                (s.Z + seeds[j].Z) * 0.5);
            var nrm = s - seeds[j];
            if (nrm.Length < 1e-9) continue;
            nrm.Unitize();
            // Plane whose normal points TOWARD seed i. Mesh.Split with this
            // plane returns pieces on either side; we keep the side that
            // contains s.
            var bisector = new Plane(mid, nrm);

            Mesh[] pieces = null;
            try { pieces = cell.Split(bisector); } catch { pieces = null; }
            if (pieces == null || pieces.Length == 0)
            {
                // No intersection -- the cell is fully on one side. Test
                // whether s is on the +normal side. If yes, keep cell as-is.
                if (DotPlane(s, bisector) >= 0) continue;
                else return null;  // cell fully on the wrong side -> no Voronoi cell
            }
            // Pick the piece whose centroid is on the +normal side of bisector.
            Mesh best = null;
            double bestDot = double.NegativeInfinity;
            foreach (var piece in pieces)
            {
                if (piece == null || piece.Vertices.Count == 0) continue;
                var pbbox = piece.GetBoundingBox(true);
                var centroid = (pbbox.Min + pbbox.Max) * 0.5;
                double d = DotPlane(centroid, bisector);
                if (d > bestDot) { bestDot = d; best = piece; }
            }
            cell = best;
            if (cell == null || cell.Vertices.Count == 0) return null;
            // Intentionally NO FillHoles here. The cut surface stays
            // OPEN -- its boundary becomes a fracture rim that the
            // Kintsugi solver matches against. Capping the cut would
            // close the rim and make rim-matching impossible.
            //
            // Side effect: the next iteration's Mesh.Split sees an
            // open mesh as input. Mesh.Split tolerates this; the
            // returned pieces are still open along ALL cut planes
            // (the old cuts AND the new one).
        }
        CleanOpenCellBoundaries(cell);
        return cell;
    }

    // -------------------------------------------------------------------------
    // Post-clip cleanup. After N-1 plane clips a cell typically has:
    //   - Duplicate vertices at cut-plane intersections (Mesh.Split's
    //     per-triangle slicing emits coincident vertices on adjacent
    //     triangles)
    //   - A scattering of degenerate triangles (zero-area slivers right
    //     at cut planes)
    //   - Naked edges that are SHORT segments rather than long clean
    //     polylines
    // This pass welds coincident vertices and drops degenerates so the
    // downstream BoundarySegmenter3D sees clean, long, well-formed
    // naked-edge loops.
    // -------------------------------------------------------------------------

    private static void CleanOpenCellBoundaries(Mesh cell)
    {
        if (cell == null) return;
        try { cell.Vertices.CombineIdentical(true, true); } catch { }
        try { cell.Faces.CullDegenerateFaces(); } catch { }
        try { cell.Faces.ConvertNonPlanarQuadsToTriangles(1e-6, 0.01, 0); } catch { }
        // HealNakedEdges tolerance: 0.1% of the cell's bounding box
        // diagonal. Bridges the tiny gaps that Mesh.Split sometimes
        // leaves between adjacent triangles on the same cut plane.
        // Note: Mesh.HealNakedEdges welds matching naked-edge pairs
        // within the tolerance -- it will NOT close the deliberate
        // fracture-rim openings (those are far apart on the mesh, not
        // matching pairs).
        try
        {
            var bb = cell.GetBoundingBox(true);
            double tol = Math.Max(1e-6, bb.Diagonal.Length * 1e-3);
            cell.HealNakedEdges(tol);
        }
        catch { }
        try { cell.UnifyNormals(); } catch { }
        try { cell.Compact(); } catch { }
    }

    private static double DotPlane(Point3d p, Plane plane)
    {
        var v = p - plane.Origin;
        return v * plane.Normal;
    }
}
