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
//
// IMPACT-BIASED SEEDING (2026-07-11 fix, Breaking-Bad alignment)
// --------------------------------------------------------------
// The Kintsugi Port model (PuzzleFusion++) was trained on the Breaking Bad
// dataset, generated with fracture modes (Sellan et al. 2022): an impact
// point breaks off a cluster of small local pieces and leaves most of the
// object as one dominant piece. Measured on 300 everyday/val fractures
// (outputs/2026-07-11/kintsugi_fracture_generator/bb_target_stats.json):
//   largest piece volume share : p25-p50-p75 = 0.46 - 0.69 - 0.92
//   piece extent ratio max/min : p25-p50-p75 = 2.5 - 4.0 - 11.5
//   piece count                : p50 = 5, mode = 2, benchmark cap = 20
// A jittered-grid Voronoi produces near-EQUAL cells (largest share ~1/N,
// extent ratio ~1.5) -- far out of distribution. Fix: cluster a fraction
// (Impact Bias) of the Voronoi seeds around an impact point with a
// half-normal radial falloff. Dense seeds near the impact = small local
// shards; sparse seeds elsewhere = one or few dominant pieces. Bias 0
// restores the legacy jittered grid exactly.
//
// For the learned path, wire Fragments through Frahan Fracture Roughen
// (Cap Cuts = TRUE) so the open Voronoi rims become closed, refined,
// realistically rough fracture SURFACES the encoder can sample.
// =============================================================================

[Algorithm("Voronoi shatter for fracture test-beds",
    "Frahan-original: deterministic seed -> pairwise bisector planes -> " +
    "iterative Mesh.Split -> per-seed Voronoi cell. " +
    "Intended as the upstream feeder for KintsugiAssemblyComponent.",
    Note = "O(N^2) plane splits. Convex seeds work best; for highly non-convex inputs, " +
           "small slivers can appear at concave folds. Workaround: feed a convex hull or " +
           "use a larger Min Fragment Volume to drop slivers.")]
[Algorithm("Impact-biased seeding (Breaking Bad alignment)",
    "Seeds clustered around an impact point with half-normal radial falloff " +
    "emulate the impact-projected fracture-mode statistics of the Breaking " +
    "Bad dataset: one dominant piece + a cluster of small local shards. " +
    "Sellan, Luong et al. 2022, NeurIPS D&B, arXiv:2210.11463; fracture " +
    "modes: Sellan et al. SIGGRAPH 2022, github.com/sgsellan/fracture-modes",
    Doi = "arXiv:2210.11463",
    Note = "Measured targets (300 everyday/val fractures): largest volume share " +
           "0.46-0.92 (median 0.69); extent ratio 2.5-11.5 (median 4.0); piece " +
           "count 2-20. Impact Bias 0 = legacy jittered grid.")]
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
        // Appended 2026-07-11 (Breaking-Bad alignment). Appending keeps the
        // wiring of canvases saved before these inputs existed.
        p.AddNumberParameter("Impact Bias", "Ib",
            "Fraction of Voronoi seeds clustered around the Impact Point " +
            "(half-normal radial falloff). 0 = legacy jittered grid (equal-" +
            "volume cells). 0.9 (default) reproduces the Breaking Bad " +
            "statistics the Kintsugi Port model was trained on: one dominant " +
            "piece (~0.5-0.9 of the volume) plus small shards near the " +
            "impact. Measured BB targets: largest-share median 0.69, extent " +
            "ratio median 4.0. Default 0.9 (tuned live 2026-07-11: largest " +
            "share 0.49 = inside the BB band; 0.75 gave 0.44).",
            GH_ParamAccess.item, 0.9);
        p.AddPointParameter("Impact Point", "Ip",
            "OPTIONAL impact location. Seeds cluster around it. Unwired = " +
            "a deterministic surface point derived from Seed (reproducible). " +
            "Place it on the mesh surface where the 'hit' should break the " +
            "object.",
            GH_ParamAccess.item);
        Params.Input[Params.Input.Count - 1].Optional = true;
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
        double impactBias = 0.9;
        Point3d impactPoint = Point3d.Unset;

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
        // Appended inputs (index 6/7) -- guard for canvases saved before
        // they existed (deserialized component may still carry 6 inputs).
        if (Params.Input.Count > 6) da.GetData(6, ref impactBias);
        if (Params.Input.Count > 7) da.GetData(7, ref impactPoint);
        if (impactBias < 0) impactBias = 0;
        if (impactBias > 1) impactBias = 1;

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
        if (n > 20)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"Fragment Count {n} exceeds 20. The Breaking Bad benchmark " +
                "(and the Kintsugi Port model) uses 2-20 pieces; more is " +
                "out of the trained envelope.");
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

        var seeds = SampleSeeds(solid, bbox, n, seed, jitter, impactBias,
                                impactPoint, out Point3d impactUsed);
        var fragments = new List<Mesh>(n);
        var keptVolumes = new List<double>(n);
        int dropCount = 0;
        var report = new System.Text.StringBuilder();
        if (impactBias > 0)
        {
            report.AppendLine(
                $"Impact-biased seeding: bias={impactBias:F2}, impact=" +
                $"({impactUsed.X:F3},{impactUsed.Y:F3},{impactUsed.Z:F3})" +
                (impactPoint.IsValid ? " (user)" : " (auto from Seed)"));
        }

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
            keptVolumes.Add(cellVol);

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

        // Distribution summary vs the measured Breaking Bad targets
        // (outputs/2026-07-11/kintsugi_fracture_generator/bb_target_stats.json).
        double volSum = 0;
        foreach (var v in keptVolumes) volSum += v;
        if (volSum > 0 && keptVolumes.Count > 0)
        {
            var shares = new List<double>(keptVolumes.Count);
            foreach (var v in keptVolumes) shares.Add(v / volSum);
            shares.Sort((a, b) => b.CompareTo(a));
            var top = new System.Text.StringBuilder();
            for (int i = 0; i < shares.Count && i < 8; i++)
                top.Append($"{shares[i]:F2} ");
            report.AppendLine();
            report.AppendLine($"Volume shares (desc): {top}" +
                              (shares.Count > 8 ? "..." : ""));
            report.AppendLine($"Largest share: {shares[0]:F2}  " +
                              "[Breaking Bad target: 0.46-0.92, median 0.69]");
            if (impactBias > 0 && shares[0] < 0.35)
            {
                report.AppendLine(
                    "  -> below the BB band. Raise Impact Bias, lower " +
                    "Fragment Count, or move the Impact Point closer to " +
                    "the surface.");
            }
        }

        da.SetDataList(0, fragments);
        da.SetDataList(1, seeds);
        da.SetData(2, dropCount);
        da.SetData(3, report.ToString());
    }

    // -------------------------------------------------------------------------
    // Seed generation.
    //
    // Impact mode (bias > 0): round(n*bias) seeds cluster around the impact
    // point with a half-normal radius (sigma = falloff length). Dense seeds
    // near the impact produce SMALL Voronoi cells there; the sparse
    // remainder produces one or few DOMINANT cells covering the rest of the
    // solid -- the Breaking Bad volume distribution. The remaining seeds use
    // the legacy jittered grid so bias = 0 is bit-identical to the old
    // behaviour.
    // -------------------------------------------------------------------------

    private static List<Point3d> SampleSeeds(
        Mesh solid, BoundingBox bbox, int n, int seed, double jitter,
        double bias, Point3d impact, out Point3d impactUsed)
    {
        impactUsed = Point3d.Unset;
        if (bias <= 0)
            return SamplePoissonInBbox(bbox, n, seed, jitter);

        var rng = new Random(seed);
        double diag = bbox.Diagonal.Length;

        // Impact point: user input, or a deterministic mesh surface vertex.
        if (impact.IsValid)
        {
            impactUsed = impact;
        }
        else
        {
            int vi = rng.Next(Math.Max(1, solid.Vertices.Count));
            impactUsed = solid.Vertices.Count > 0
                ? (Point3d)solid.Vertices[vi]
                : bbox.Center;
        }

        // Falloff length: tight at bias 1 (0.07 * diag), loose at bias -> 0.
        // Tuned live 2026-07-11: 0.10 + 0.35 gave largest-share 0.40, just
        // under the BB p25 of 0.46; tightening lands the default inside the
        // band.
        double sigma = diag * (0.07 + 0.28 * (1.0 - bias));

        int nCluster = Math.Max(1, (int)Math.Round(n * bias));
        if (nCluster >= n) nCluster = n - 1;   // keep >= 1 distal seed
        int nUniform = n - nCluster;

        var pts = new List<Point3d>(n);
        // Clustered seeds: half-normal radius, uniform direction. Clamped to
        // the (slightly deflated) bbox so degenerate far-outside seeds don't
        // produce systematically empty cells.
        var inner = bbox;
        inner.Inflate(-diag * 0.01);
        for (int i = 0; i < nCluster; i++)
        {
            double r = Math.Abs(Gaussian(rng)) * sigma;
            var d = RandomUnitVector(rng);
            var p = impactUsed + d * r;
            p.X = Clamp(p.X, inner.Min.X, inner.Max.X);
            p.Y = Clamp(p.Y, inner.Min.Y, inner.Max.Y);
            p.Z = Clamp(p.Z, inner.Min.Z, inner.Max.Z);
            pts.Add(p);
        }
        // Distal seeds: legacy jittered grid over the whole bbox (fresh
        // deterministic stream so counts don't reshuffle the cluster).
        var distal = SamplePoissonInBbox(bbox, nUniform, seed + 7919, jitter);
        pts.AddRange(distal);
        return pts;
    }

    private static double Gaussian(Random rng)
    {
        // Box-Muller; guards the log(0) corner.
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(Math.Max(u1, 1e-12)))
             * Math.Cos(2.0 * Math.PI * u2);
    }

    private static Vector3d RandomUnitVector(Random rng)
    {
        // Marsaglia rejection sampling on the unit sphere.
        for (int k = 0; k < 64; k++)
        {
            double x = rng.NextDouble() * 2 - 1;
            double y = rng.NextDouble() * 2 - 1;
            double z = rng.NextDouble() * 2 - 1;
            double m2 = x * x + y * y + z * z;
            if (m2 > 1e-8 && m2 <= 1.0)
            {
                double inv = 1.0 / Math.Sqrt(m2);
                return new Vector3d(x * inv, y * inv, z * inv);
            }
        }
        return new Vector3d(0, 0, 1);
    }

    private static double Clamp(double v, double lo, double hi)
        => v < lo ? lo : (v > hi ? hi : v);

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
        // HealNakedEdges bridges the tiny gaps Mesh.Split leaves between
        // adjacent triangles on the same cut plane. DANGER (field defect,
        // 2026-07-11): on SMALL impact-clustered cells the two sides of a
        // fracture rim can fall within the tolerance; healing then stitches
        // the deliberate opening shut in a degenerate, half-welded way --
        // GetNakedEdges() returns null and IsClosed misreports, killing the
        // geometric rim-matching path downstream. Guard: heal a duplicate
        // first, keep it ONLY if the heal removed a small share of the
        // naked edges (gap bridging), never a large share (rim eating).
        try
        {
            var bb = cell.GetBoundingBox(true);
            double tol = Math.Max(1e-6, bb.Diagonal.Length * 1e-4);
            int NakedCount(Mesh m)
            {
                int c = 0;
                var te = m.TopologyEdges;
                for (int e = 0; e < te.Count; e++)
                    if (te.GetConnectedFaces(e).Length == 1) c++;
                return c;
            }
            int before = NakedCount(cell);
            var healed = cell.DuplicateMesh();
            healed.HealNakedEdges(tol);
            int after = NakedCount(healed);
            if (after >= before * 0.7 && healed.GetNakedEdges() != null)
            {
                cell.CopyFrom(healed);
            }
            // else: healing ate the rim; keep the honest open mesh.
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
