#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Masonry;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Interfaces;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH;

// =============================================================================
// AdvancedQuarryDecomposeComponents — three quarry-cut strategies that
// complete the Frahan Cut > Quarry tab:
//
//   1. Quarry Decompose By CoACD     — concavity-driven nearly-convex split.
//   2. Quarry Decompose By Tet       — Geogram tetrahedralization (one mesh
//                                       per tet). Requires GEOGRAM_WITH_TETGEN
//                                       at build time (off by default for
//                                       license reasons).
//   3. Quarry Decompose By Voronoi   — interior Voronoi cells × quarry,
//                                       seed count + Lloyd relaxation
//                                       controllable; uses CGAL Hybrid for
//                                       the final cell ∩ quarry step.
//
// All three live on the existing 'Frahan Cut' / 'Quarry' ribbon section
// alongside the plane-grid and CGAL grid-of-boxes decomposers.
// =============================================================================

// =============================================================================
// 1. CoACD-driven quarry decompose
// =============================================================================

[Algorithm("Collision-Aware Approximate Convex Decomposition", "Wei, Liu, Wang et al. 2022, Approximate Convex Decomposition for 3D Meshes with Collision-Aware Concavity and Tree Search, SIGGRAPH 2022", WikiPath = "wiki/index/references.md")]
[DesignApplication(
    "Decomposes a quarry mesh into nearly-convex blocks via  CoACD (Wei et al, SIGGRAPH 2022)",
    DesignFlow.TopDown)]
public sealed class CoacdQuarryDecomposeComponent : FrahanComponentBase
{
    public CoacdQuarryDecomposeComponent()
        : base("Quarry Decompose By CoACD", "QuarryDcCoacd",
            "Decomposes a quarry mesh into nearly-convex blocks via " +
            "CoACD (Wei et al, SIGGRAPH 2022). Concavity-driven — block " +
            "count and shape come from the input geometry, not a user " +
            "grid. Use when the goal is approximate convex pieces for " +
            "downstream packing or collision physics. Implements Collision-Aware Approximate Convex Decomposition (Wei 2022). " +
            "Selection: convex pieces -> By CoACD; plane-bounded cuts -> By Mesh (CGAL); cell partition -> By Voronoi.",
            "Frahan", "Block Cutting")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D000E0-CADC-4F2D-A0E0-7E60CADA15A0");
    public override GH_Exposure Exposure => Frahan.GH.Attributes.LabConfig.EffectiveExposure(ComponentGuid, GH_Exposure.secondary);
    protected override Bitmap Icon => IconProvider.Load("CoacdDecompose.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Quarry", "Q",
            "Quarry mesh. Must be 2-manifold for the lightweight CoACD " +
            "build (no OpenVDB preprocess); pre-clean with Mesh Repair " +
            "(CGAL) if needed.",
            GH_ParamAccess.item);
        p.AddNumberParameter("Threshold", "Th",
            "Concavity threshold. Lower = more pieces, tighter fit. " +
            "Default 0.05.",
            GH_ParamAccess.item, 0.05);
        p.AddBooleanParameter("Real Metric", "RM",
            "True = treat Threshold as metres rather than normalized " +
            "[0..1] units. Recommended for statue-scale input.",
            GH_ParamAccess.item, false);
        p.AddIntegerParameter("Max Pieces", "Mx",
            "Cap on output piece count. -1 = unlimited.",
            GH_ParamAccess.item, -1);
        p[3].Optional = true;
        p.AddIntegerParameter("Seed", "Sd",
            "RNG seed for reproducibility.",
            GH_ParamAccess.item, 0);
        p[4].Optional = true;
        p.AddBooleanParameter("Run", "Run",
            "Set true to compute. Decomposition is heavy.",
            GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Blocks", "B",
            "One nearly-convex mesh per piece.",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Count", "N",
            "Number of pieces.",
            GH_ParamAccess.item);
        p.AddBooleanParameter("Available", "Av",
            "True iff frahan_coacd.dll is loadable.",
            GH_ParamAccess.item);
        p.AddTextParameter("Report", "R",
            "Diagnostic report.",
            GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh quarry = null;
        double threshold = 0.05;
        bool realMetric = false;
        int maxPieces = -1;
        int seed = 0;
        bool run = false;

        if (!da.GetData(0, ref quarry)) return;
        da.GetData(1, ref threshold);
        da.GetData(2, ref realMetric);
        da.GetData(3, ref maxPieces);
        da.GetData(4, ref seed);
        da.GetData(5, ref run);

        var available = CoacdMeshDecompose.IsAvailable;
        da.SetData(2, available);

        if (!run)
        {
            da.SetData(3, available
                ? "Run is false. CoACD shim is loaded and ready."
                : "Run is false. CoACD shim NOT loaded.");
            return;
        }
        if (!available)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "CoACD shim not loaded — drop frahan_coacd.dll alongside the .gha.");
            return;
        }
        if (quarry == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Quarry is required.");
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var snap = CgalConvert.ToSnapshot(quarry);
            CoacdMeshDecompose.SetLogLevel("warn");
            var pieces = CoacdMeshDecompose.Decompose(snap, new CoacdParameters
            {
                Threshold = threshold,
                RealMetric = realMetric,
                MaxConvexHull = maxPieces,
                Seed = (uint)Math.Max(0, seed),
            });
            sw.Stop();

            var blocks = new List<Mesh>(pieces.Count);
            int totalV = 0, totalT = 0;
            for (int i = 0; i < pieces.Count; i++)
            {
                blocks.Add(CgalConvert.FromSnapshot(pieces[i]));
                totalV += pieces[i].VertexCount;
                totalT += pieces[i].TriangleCount;
            }
            da.SetDataList(0, blocks);
            da.SetData(1, pieces.Count);
            da.SetData(3,
                $"Quarry    : {snap.VertexCount}V / {snap.TriangleCount}F\n" +
                $"Threshold : {threshold} ({(realMetric ? "m" : "norm")})\n" +
                $"Pieces    : {pieces.Count}  ({totalV}V / {totalT}F total)\n" +
                $"Runtime   : {sw.ElapsedMilliseconds} ms\n");
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"CoACD quarry decompose failed: {ex.GetType().Name}: {ex.Message}");
            da.SetData(3, $"FAILED: {ex.Message}");
        }
    }
}

// =============================================================================
// 2. Tet (Geogram) quarry decompose
// =============================================================================

[Algorithm("Geogram tetrahedralisation", "Lévy, B. Geogram v1.9.9 (GEO::mesh_tetrahedralize), BSD-3", WikiPath = "wiki/index/references.md")]
[DesignApplication(
    "Decomposes a quarry mesh into tetrahedra via Geogram",
    DesignFlow.TopDown)]
public sealed class GeogramTetQuarryDecomposeComponent : FrahanComponentBase
{
    public GeogramTetQuarryDecomposeComponent()
        : base("Quarry Decompose By Tet", "QuarryDcTet",
            "Decomposes a quarry mesh into tetrahedra via Geogram. " +
            "Fine-grained, fracture-pattern style. Requires the " +
            "Geogram shim to be built with GEOGRAM_WITH_TETGEN=ON " +
            "(off by default — TetGen is non-commercial-use). When " +
            "off, the component surfaces a clear error and produces no " +
            "blocks; use Quarry Decompose By CoACD instead. Implements Geogram tetrahedralisation (Lévy, Geogram v1.9.9).",
            "Frahan", "Block Cutting")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D000E1-CADC-4F2D-A0E1-7E60CADA15A0");
    public override GH_Exposure Exposure => Frahan.GH.Attributes.LabConfig.EffectiveExposure(ComponentGuid, GH_Exposure.secondary);
    protected override Bitmap Icon => IconProvider.Load("CoacdDecompose.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Quarry", "Q",
            "Closed manifold quarry mesh.",
            GH_ParamAccess.item);
        p.AddBooleanParameter("Preprocess", "Pp",
            "Run mesh preprocess (manifold-isation, hole fill) inside " +
            "Geogram before tetrahedralizing.",
            GH_ParamAccess.item, true);
        p.AddBooleanParameter("Refine", "Rf",
            "Refine the tet mesh via Delaunay refinement after the " +
            "initial tetrahedralization. Increases tet count.",
            GH_ParamAccess.item, false);
        p[2].Optional = true;
        p.AddNumberParameter("Quality", "Qu",
            "Tet quality bound for refinement (radius-edge ratio). " +
            "Default 1.4. Lower is stricter / more tets.",
            GH_ParamAccess.item, 1.4);
        p[3].Optional = true;
        p.AddBooleanParameter("Run", "Run",
            "Set true to compute.",
            GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Tets", "T",
            "One closed tetrahedron mesh per output cell.",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Count", "N",
            "Number of tets.",
            GH_ParamAccess.item);
        p.AddBooleanParameter("Available", "Av",
            "True iff frahan_geogram.dll is loadable.",
            GH_ParamAccess.item);
        p.AddTextParameter("Report", "R",
            "Diagnostic report. Reports the TetGen-disabled state when " +
            "applicable.",
            GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh quarry = null;
        bool preprocess = true, refine = false;
        double quality = 1.4;
        bool run = false;

        if (!da.GetData(0, ref quarry)) return;
        da.GetData(1, ref preprocess);
        da.GetData(2, ref refine);
        da.GetData(3, ref quality);
        da.GetData(4, ref run);

        var available = GeogramMesh.IsAvailable;
        da.SetData(2, available);

        if (!run)
        {
            da.SetData(3, available
                ? "Run is false. Geogram shim is loaded and ready."
                : "Run is false. Geogram shim NOT loaded.");
            return;
        }
        if (!available)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Geogram shim not loaded — drop frahan_geogram.dll alongside the .gha.");
            return;
        }
        if (quarry == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Quarry is required.");
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var snap = CgalConvert.ToSnapshot(quarry);
            var tetMesh = GeogramMesh.Tetrahedralize(
                snap, preprocess, refine, quality, keepRegions: false);
            sw.Stop();

            // Lift each tet (4 verts) into its own closed mesh: 4 triangle
            // faces (one opposite each vertex). Caller can downstream-compose
            // or move per tet.
            var blocks = new List<Mesh>(tetMesh.TetCount);
            for (int t = 0; t < tetMesh.TetCount; t++)
            {
                int i0 = tetMesh.TetIndices[4 * t + 0];
                int i1 = tetMesh.TetIndices[4 * t + 1];
                int i2 = tetMesh.TetIndices[4 * t + 2];
                int i3 = tetMesh.TetIndices[4 * t + 3];
                var p0 = TetVert(tetMesh, i0);
                var p1 = TetVert(tetMesh, i1);
                var p2 = TetVert(tetMesh, i2);
                var p3 = TetVert(tetMesh, i3);

                var m = new Mesh();
                m.Vertices.Add(p0); m.Vertices.Add(p1);
                m.Vertices.Add(p2); m.Vertices.Add(p3);
                // Outward winding for a CCW Geogram tet (i0,i1,i2,i3):
                m.Faces.AddFace(0, 2, 1);
                m.Faces.AddFace(0, 1, 3);
                m.Faces.AddFace(0, 3, 2);
                m.Faces.AddFace(1, 2, 3);
                m.Normals.ComputeNormals();
                blocks.Add(m);
            }

            da.SetDataList(0, blocks);
            da.SetData(1, tetMesh.TetCount);
            da.SetData(3,
                $"Quarry    : {snap.VertexCount}V / {snap.TriangleCount}F\n" +
                $"Tets      : {tetMesh.TetCount}  ({tetMesh.VertexCount} unique verts)\n" +
                $"Preprocess: {preprocess}\n" +
                $"Refine    : {refine}  (quality={quality})\n" +
                $"Runtime   : {sw.ElapsedMilliseconds} ms\n");
        }
        catch (Exception ex)
        {
            // The default Geogram shim ships with GEOGRAM_WITH_TETGEN=OFF, so
            // Tetrahedralize returns rc=-210. Surface that as actionable guidance
            // (a default-throwing ribbon node is worse than a clear redirect).
            string m = ex.Message ?? "";
            bool backendOff = m.IndexOf("210", StringComparison.Ordinal) >= 0
                || m.IndexOf("tetgen", StringComparison.OrdinalIgnoreCase) >= 0;
            string msg = backendOff
                ? "Tetrahedralisation backend not built (the Geogram shim needs GEOGRAM_WITH_TETGEN=ON). " +
                  "Use 'Quarry Decompose By CoACD' (nearly-convex) or 'By Voronoi' (cell partition) instead."
                : $"Tet decompose: {ex.GetType().Name}: {ex.Message}";
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, msg);
            da.SetData(3, $"UNAVAILABLE: {msg}");
        }
    }

    private static Point3d TetVert(TetMeshSnapshot t, int i)
        => new Point3d(t.VertexCoordsXyz[3 * i + 0],
                       t.VertexCoordsXyz[3 * i + 1],
                       t.VertexCoordsXyz[3 * i + 2]);
}

// =============================================================================
// 3. Voronoi quarry decompose (interior seeds + bisector clipping + CGAL)
//
// The Geogram CVT entry point operates on a SURFACE mesh and produces
// surface-distributed seeds, which is wrong for a volumetric Voronoi
// decomposition. This component sidesteps that limitation entirely:
//
//   a. Rejection-sample N seeds from the quarry's AABB, keeping only
//      points strictly inside the quarry mesh.
//   b. Run L Lloyd relaxation iterations in C#: for each seed compute
//      its Voronoi cell (clipped to the AABB) by bisector half-space
//      clipping via the in-tree SlabCutter; replace the seed with the
//      cell centroid.
//   c. With the relaxed seeds, build each Voronoi cell as a convex
//      polytope mesh.
//   d. Intersect each cell against the quarry via CGAL Hybrid. Drop
//      empty intersections.
//
// Output: one solid block per surviving seed, plus the seed point list
// for visualisation / replay.
// =============================================================================

[Algorithm("Restricted Voronoi diagram", "Lévy, B. Geogram v1.9.9 restricted Voronoi, BSD-3", WikiPath = "wiki/index/references.md")]
[Algorithm("Lloyd relaxation (CVD)", "Lloyd, S. 1982, Least squares quantization in PCM, IEEE Trans. Inf. Theory IT-28:129-137", WikiPath = "wiki/index/references.md")]
[DesignApplication(
    "Decomposes a (possibly non-convex) quarry mesh into solid  Voronoi blocks",
    DesignFlow.TopDown)]
public sealed class VoronoiQuarryDecomposeComponent : FrahanComponentBase
{
    public VoronoiQuarryDecomposeComponent()
        : base("Quarry Decompose By Voronoi", "QuarryDcVoro",
            "Decomposes a (possibly non-convex) quarry mesh into solid " +
            "Voronoi blocks. Seeds are sampled inside the quarry and " +
            "Lloyd-relaxed for a more uniform cell-area distribution. " +
            "Each cell is then CGAL-intersected against the quarry " +
            "for the final block geometry. Realistic stone-fracturing " +
            "look; seed count + relaxation iterations are user dials. Implements restricted Voronoi + Lloyd relaxation (Geogram; Lloyd 1982). " +
            "Selection: convex pieces -> By CoACD; plane-bounded cuts -> By Mesh (CGAL); cell partition -> By Voronoi.",
            "Frahan", "Block Cutting")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D000E2-CADC-4F2D-A0E2-7E60CADA15A0");
    public override GH_Exposure Exposure => Frahan.GH.Attributes.LabConfig.EffectiveExposure(ComponentGuid, GH_Exposure.secondary);
    protected override Bitmap Icon => IconProvider.Load("Voronoi.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Quarry", "Q",
            "Closed manifold quarry mesh.",
            GH_ParamAccess.item);
        p.AddIntegerParameter("Seed Count", "Ns",
            "Number of Voronoi seeds = number of output blocks. " +
            "Default 30. Typical 20–200 for masonry-scale work.",
            GH_ParamAccess.item, 30);
        p.AddIntegerParameter("Lloyd Iters", "Li",
            "Lloyd-relaxation iterations on the interior seeds. 0 = " +
            "raw rejection-sampled seeds; 5–10 = visibly more uniform.",
            GH_ParamAccess.item, 5);
        p[2].Optional = true;
        p.AddIntegerParameter("Seed", "Sd",
            "RNG seed for reproducibility. Default 1.",
            GH_ParamAccess.item, 1);
        p[3].Optional = true;
        p.AddBooleanParameter("Hybrid Kernel", "Hy",
            "True (default) = CGAL HYBRID kernel for the cell × " +
            "quarry intersection. False = EPICK only (faster, less robust).",
            GH_ParamAccess.item, true);
        p.AddBooleanParameter("Run", "Run",
            "Set true to compute.",
            GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Blocks", "B",
            "One mesh per non-empty Voronoi cell ∩ quarry intersection.",
            GH_ParamAccess.list);
        p.AddPointParameter("Seeds", "S",
            "The relaxed seed positions actually used (parallel to Blocks).",
            GH_ParamAccess.list);
        p.AddBooleanParameter("Available", "Av",
            "True iff CGAL shim is loadable.",
            GH_ParamAccess.item);
        p.AddTextParameter("Report", "R",
            "Diagnostic report.",
            GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh quarry = null;
        int nSeeds = 30, lloydIters = 5, seedRng = 1;
        bool hybrid = true;
        bool run = false;

        if (!da.GetData(0, ref quarry)) return;
        da.GetData(1, ref nSeeds);
        da.GetData(2, ref lloydIters);
        da.GetData(3, ref seedRng);
        da.GetData(4, ref hybrid);
        da.GetData(5, ref run);

        var available = CgalMeshBoolean.IsAvailable;
        da.SetData(2, available);

        if (!run)
        {
            da.SetData(3, available
                ? "Run is false. CGAL shim is loaded and ready."
                : "Run is false. CGAL shim NOT loaded — Voronoi decompose " +
                  "needs CGAL for the cell × quarry intersection.");
            return;
        }
        if (quarry == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Quarry is required.");
            return;
        }
        if (nSeeds < 2)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Seed Count must be >= 2 (got {nSeeds}).");
            return;
        }
        if (lloydIters < 0) lloydIters = 0;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // ---- a. Rejection-sample interior seeds -----------------
            var bbox = quarry.GetBoundingBox(true);
            if (!bbox.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Quarry has no valid bounding box.");
                return;
            }
            var seeds = SampleInterior(quarry, bbox, nSeeds, seedRng,
                                        out int rejectionAttempts);
            if (seeds.Count < nSeeds)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Only {seeds.Count}/{nSeeds} seeds fit inside the " +
                    $"quarry after {rejectionAttempts} attempts. Inputs " +
                    $"with very thin volume vs AABB benefit from a higher " +
                    $"Seed Count or pre-decimation.");
            }
            if (seeds.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "No interior seeds — the quarry is empty or closed-but-inverted.");
                return;
            }

            // ---- b. Lloyd relaxation on seeds -----------------------
            var aabbSlab = AabbToSlab(bbox);
            for (int it = 0; it < lloydIters; it++)
            {
                var newSeeds = new List<Point3d>(seeds.Count);
                for (int i = 0; i < seeds.Count; i++)
                {
                    var cell = ClipVoronoiCell(aabbSlab, seeds, i);
                    newSeeds.Add(cell == null ? seeds[i] : SlabCentroid(cell));
                }
                seeds = newSeeds;
            }
            long tSeeds = sw.ElapsedMilliseconds;

            // ---- c + d. Per-seed cell, intersect against quarry ----
            var quarrySnap = CgalConvert.ToSnapshot(quarry);
            var kernel = hybrid ? CsgKernelMode.Hybrid : CsgKernelMode.Inexact;

            var blocks = new List<Mesh>(seeds.Count);
            var keptSeeds = new List<Point3d>(seeds.Count);
            int empties = 0, fails = 0;

            for (int i = 0; i < seeds.Count; i++)
            {
                var cellSlab = ClipVoronoiCell(aabbSlab, seeds, i);
                if (cellSlab == null) { empties++; continue; }
                var cellMesh = GhInterop.SlabToMesh(cellSlab);
                if (cellMesh == null || cellMesh.Faces.Count == 0)
                { empties++; continue; }

                MeshSnapshot intersected;
                try
                {
                    var cellSnap = CgalConvert.ToSnapshot(cellMesh);
                    intersected = CgalMeshBoolean.Intersection(
                        quarrySnap, cellSnap, kernel, out _);
                }
                catch
                {
                    fails++;
                    continue;
                }
                if (intersected == null
                    || intersected.VertexCount == 0
                    || intersected.TriangleCount == 0)
                { empties++; continue; }

                blocks.Add(CgalConvert.FromSnapshot(intersected));
                keptSeeds.Add(seeds[i]);
            }
            sw.Stop();

            da.SetDataList(0, blocks);
            da.SetDataList(1, keptSeeds);
            da.SetData(3,
                $"Quarry      : {quarrySnap.VertexCount}V / {quarrySnap.TriangleCount}F\n" +
                $"Seed Count  : {nSeeds} requested, {seeds.Count} interior\n" +
                $"Lloyd       : {lloydIters} iter ({tSeeds} ms)\n" +
                $"Kept blocks : {blocks.Count}\n" +
                $"Dropped     : {empties} empty + {fails} CGAL failures\n" +
                $"Total       : {sw.ElapsedMilliseconds} ms\n" +
                $"Per cell    : {(seeds.Count > 0 ? sw.ElapsedMilliseconds / (double)seeds.Count : 0.0):F1} ms avg\n" +
                $"Kernel      : {kernel}\n");
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Voronoi quarry decompose failed: {ex.GetType().Name}: {ex.Message}");
            da.SetData(3, $"FAILED: {ex.Message}");
        }
    }

    // -- Interior rejection sampling -----------------------------------------

    private static List<Point3d> SampleInterior(
        Mesh quarry, BoundingBox bb, int target, int seedRng,
        out int attempts)
    {
        var rng = new Random(seedRng);
        var pts = new List<Point3d>(target);
        attempts = 0;
        // Hard cap so a thin volume in a fat AABB does not loop forever.
        int cap = Math.Max(target * 50, 5000);
        while (pts.Count < target && attempts < cap)
        {
            attempts++;
            double x = bb.Min.X + rng.NextDouble() * (bb.Max.X - bb.Min.X);
            double y = bb.Min.Y + rng.NextDouble() * (bb.Max.Y - bb.Min.Y);
            double z = bb.Min.Z + rng.NextDouble() * (bb.Max.Z - bb.Min.Z);
            var p = new Point3d(x, y, z);
            if (quarry.IsPointInside(p, 1e-9, true)) pts.Add(p);
        }
        return pts;
    }

    // -- AABB → convex Slab (8 verts, 6 faces) --------------------------------

    private static Slab AabbToSlab(BoundingBox bb)
    {
        var p = new[]
        {
            bb.Min.X, bb.Min.Y, bb.Min.Z,  // 0
            bb.Max.X, bb.Min.Y, bb.Min.Z,  // 1
            bb.Max.X, bb.Max.Y, bb.Min.Z,  // 2
            bb.Min.X, bb.Max.Y, bb.Min.Z,  // 3
            bb.Min.X, bb.Min.Y, bb.Max.Z,  // 4
            bb.Max.X, bb.Min.Y, bb.Max.Z,  // 5
            bb.Max.X, bb.Max.Y, bb.Max.Z,  // 6
            bb.Min.X, bb.Max.Y, bb.Max.Z,  // 7
        };
        // Faces oriented CCW outward (Slab.SignedVolume() positive).
        var faces = new int[][]
        {
            new[] { 0, 3, 2, 1 }, // -Z (bottom)
            new[] { 4, 5, 6, 7 }, // +Z (top)
            new[] { 0, 1, 5, 4 }, // -Y
            new[] { 2, 3, 7, 6 }, // +Y
            new[] { 1, 2, 6, 5 }, // +X
            new[] { 0, 4, 7, 3 }, // -X
        };
        return new Slab(p, faces);
    }

    // -- Voronoi cell of seed `i` clipped to the start slab -------------------
    //
    // For each j != i, build the perpendicular bisector plane between
    // seeds[i] and seeds[j] (normal points from i toward j). The seed_i
    // half is on the negative side of that plane. Cut the running slab
    // by the plane and keep only pieces whose centroid is on the negative
    // side. Convex polytope clipped by half-spaces stays convex; empty
    // result means the cell is collapsed (rare; signals overlapping seeds).

    private static Slab ClipVoronoiCell(
        Slab startSlab, IReadOnlyList<Point3d> seeds, int i)
    {
        Slab current = startSlab;
        var si = seeds[i];
        for (int j = 0; j < seeds.Count; j++)
        {
            if (j == i) continue;
            if (current == null) return null;
            var sj = seeds[j];
            double dx = sj.X - si.X, dy = sj.Y - si.Y, dz = sj.Z - si.Z;
            double mag = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (mag < 1e-12) continue;  // coincident seeds: skip
            double nx = dx / mag, ny = dy / mag, nz = dz / mag;
            double ox = (si.X + sj.X) * 0.5;
            double oy = (si.Y + sj.Y) * 0.5;
            double oz = (si.Z + sj.Z) * 0.5;
            var plane = new FracturePlane(ox, oy, oz, nx, ny, nz);

            SlabCutResult result;
            try { result = SlabCutter.Cut(current, plane, 1e-9); }
            catch { return null; }

            // Keep the piece on the s_i (negative) side of the plane.
            Slab keeper = null;
            for (int k = 0; k < result.Slabs.Count; k++)
            {
                var c = SlabCentroid(result.Slabs[k]);
                double sd = (c.X - ox) * nx + (c.Y - oy) * ny + (c.Z - oz) * nz;
                if (sd < 0)
                {
                    if (keeper == null) keeper = result.Slabs[k];
                    else if (Math.Abs(result.Slabs[k].SignedVolume())
                           > Math.Abs(keeper.SignedVolume()))
                        keeper = result.Slabs[k];
                }
            }
            current = keeper;
        }
        return current;
    }

    private static Point3d SlabCentroid(Slab s)
    {
        int n = s.VertexCount;
        if (n == 0) return Point3d.Origin;
        double cx = 0, cy = 0, cz = 0;
        for (int i = 0; i < n; i++)
        {
            cx += s.VertexCoordsXyz[3 * i + 0];
            cy += s.VertexCoordsXyz[3 * i + 1];
            cz += s.VertexCoordsXyz[3 * i + 2];
        }
        return new Point3d(cx / n, cy / n, cz / n);
    }
}
