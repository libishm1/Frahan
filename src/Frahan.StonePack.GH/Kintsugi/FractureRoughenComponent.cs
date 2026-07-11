#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Kintsugi;

// =============================================================================
// Frahan > Kintsugi > Fracture Roughen.
//
// Turns the dead-flat planar cut surfaces of Voronoi shatter fragments into
// irregular, worn fracture surfaces -- while keeping adjacent fragments
// MATING so the pieces still fit back together.
//
// THE KEY IDEA (fit-together worn fracture)
//   Displacement comes from ONE shared 3D fractal noise field evaluated at
//   WORLD position:  p' = p + D(p),  where D(p) is the same vector field for
//   every fragment. Two cells that were cut along the same bisector share the
//   same boundary points in world space; since D depends only on position
//   (not on which mesh the vertex belongs to), both move identically -> they
//   still mate. Coherent fractal noise (vs the old per-vertex Gaussian) gives
//   a worn/eroded look instead of spikes.
//
// Why this matters: Voronoi planar cuts are out-of-distribution for the
// Breaking Bad-trained PuzzleFusion++ model. Worn, curved fracture surfaces
// look closer to its training data AND read as real broken stone.
//
// Algorithm
//   1. (optional) Cap each open cut with FillHoles so the fracture is an
//      actual surface, not a hole. The cap + rim vertices are the cut region.
//   2. REFINE the caps (2026-07-11 fix): FillHoles ear-clips the rim polygon
//      into a few huge triangles with NO interior vertices, so displacing
//      "the cap" used to move only the rim -- the fracture surface stayed
//      dead flat and the Kintsugi Port encoder (trained on Breaking Bad)
//      saw an out-of-distribution planar interface. Interior cap edges are
//      now midpoint-subdivided (conforming; rim edges shared with the outer
//      skin are never split) until they resolve the finest noise octave.
//   3. Displace every cut-region vertex by D(worldPos), a fractal sum of
//      value-noise octaves. Shared field -> adjacent cells stay mated
//      (both sides track the same continuous displaced surface; residual
//      gap is the piecewise-linear interpolation error, O(h^2)).
//      RIM TAPER: at the rim the displacement is projected onto the outer
//      skin's tangent plane and blended to full 3D over the taper width.
//      The crack line stays irregular (Breaking-Bad-like) while the outer
//      skin silhouette is preserved (fracture-modes never modifies the
//      original surface, only introduces new interior surfaces).
//   4. Recompute normals + compact.
//
// Measured targets (outputs/2026-07-11/kintsugi_fracture_generator/):
//   Breaking Bad contact-region deviation-from-plane / extent:
//     p25-p50-p75 = 0.031 - 0.044 - 0.061   (300 everyday/val fractures)
//   Real scanned granite shard facets (D:\granite_shards.ply, 10 shards):
//     p25-p50-p75 = 0.0040 - 0.0045 - 0.0065
//   Amplitude default 0.05 pushes the synthetic surfaces toward the BB band
//   (what the learned model expects). For a real-granite look use ~0.006.
//
// Determinism: the Seed fixes the noise field, so re-solves and ALL fragments
// use the identical field (required for matching).
// =============================================================================

[Algorithm("Fracture surface roughen (shared fractal field)",
    "Displaces cut-region vertices by a single world-position fractal noise " +
    "field so adjacent Voronoi fragments stay mated while gaining worn, " +
    "irregular fracture surfaces. Reduces the Voronoi-vs-BreakingBad " +
    "distribution gap for the Kintsugi Port model.")]
[Algorithm("Cap refinement + rim taper (Breaking Bad alignment)",
    "Interior cap edges midpoint-subdivided to resolve the finest noise " +
    "octave (FillHoles ear-clip alone leaves the cap flat); displacement " +
    "tapers to skin-tangential at the rim so the outer surface is " +
    "preserved, matching fracture-modes output where only interior " +
    "fracture surfaces are new. Targets measured from Breaking Bad " +
    "(deviation/extent median 0.044) and real granite shards (0.0045).",
    Doi = "arXiv:2210.11463",
    Note = "Amplitude 0.04 = BB-band (learned-model-friendly); 0.006 = " +
           "real-granite look. Rim Taper 0 + Cut Resolution < 0 restore " +
           "the legacy 2026-05 behaviour.")]
[DesignApplication(
    "Give Voronoi shatter fragments worn, irregular fracture surfaces  using a shared world-position fractal fie...",
    DesignFlow.BottomUp,
    Precedent = "Frahan-original Voronoi-shatter post-process")]
public sealed class FractureRoughenComponent : FrahanComponentBase
{
    public FractureRoughenComponent()
        : base("Fracture Roughen", "Roughen",
            "Give Voronoi shatter fragments worn, irregular fracture surfaces " +
            "using a shared world-position fractal field, so the pieces still " +
            "fit together. Wire between Frahan Fragment Shatter and Frahan " +
            "Kintsugi.",
            "Frahan", "Kintsugi")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("F2D00504-2026-4522-B0B0-1ABE15A0CAFE");

    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("DiffusionDenoiser.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        // Original 0-4 order preserved (Fragments, Amplitude, Roughness, Seed,
        // Run) so existing canvases don't get their wiring scrambled; the
        // fractal controls are appended at 5-7.
        p.AddMeshParameter("Fragments", "F",
            "List of fragments to roughen (typically from Frahan Fragment Shatter).",
            GH_ParamAccess.list);
        p.AddNumberParameter("Amplitude", "A",
            "Displacement amplitude as a FRACTION of the bounding box diagonal. " +
            "Default 0.05 approaches the measured Breaking Bad roughness band " +
            "(contact deviation/extent 0.031-0.061, median 0.044) -- what the " +
            "Kintsugi Port model was trained on. Use ~0.006 for the measured " +
            "real-granite-shard look (much flatter).",
            GH_ParamAccess.item, 0.05);
        p.AddNumberParameter("Roughness", "R",
            "Per-octave amplitude falloff (persistence). 0.5 = balanced. " +
            "Higher = rougher/grittier; lower = smoother. Default 0.5.",
            GH_ParamAccess.item, 0.5);
        p.AddIntegerParameter("Seed", "S",
            "RNG seed for the shared noise field. SAME seed = SAME field for " +
            "every fragment (required for mating). Default 42.",
            GH_ParamAccess.item, 42);
        p.AddBooleanParameter("Run", "Run", "Apply.", GH_ParamAccess.item, false);
        p.AddNumberParameter("Frequency", "Fq",
            "Noise frequency as cycles across the bounding box diagonal. " +
            "Lower = broad gentle waves; higher = fine pitting. Default 3.0.",
            GH_ParamAccess.item, 3.0);
        p.AddIntegerParameter("Octaves", "Oc",
            "Fractal octaves summed (each doubles frequency, halves amplitude). " +
            "1 = smooth, 4-5 = rich worn detail. Default 4.",
            GH_ParamAccess.item, 4);
        p.AddBooleanParameter("Cap Cuts", "Cap",
            "TRUE = FillHoles first so the cut becomes a worn SURFACE (closed " +
            "fragment). FALSE = displace only the open rim. Default TRUE. " +
            "The learned Kintsugi path (Mode=Port) REQUIRES TRUE: its " +
            "area-uniform point sampler only sees surfaces that exist.",
            GH_ParamAccess.item, true);
        // Appended 2026-07-11 (Breaking-Bad alignment); appending preserves
        // the wiring of previously saved canvases.
        p.AddNumberParameter("Cut Resolution", "Cr",
            "Target edge length for cap refinement as a FRACTION of the " +
            "bounding box diagonal. 0 (default) = auto: resolve the finest " +
            "noise octave (diag / (2 * Frequency * 2^(Octaves-1))). " +
            "Negative = no refinement (legacy flat ear-clip caps). " +
            "Vertex budget 20k per fragment guards runaway subdivision.",
            GH_ParamAccess.item, 0.0);
        p.AddNumberParameter("Rim Taper", "Rt",
            "Width of the rim blend zone as a FRACTION of the bounding box " +
            "diagonal. Inside the zone the displacement blends from " +
            "skin-tangential (at the rim: crack line wiggles, outer skin " +
            "silhouette preserved) to full 3D (cap interior). 0 = legacy " +
            "full displacement everywhere. Default 0.04.",
            GH_ParamAccess.item, 0.04);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Roughened Fragments", "Fo",
            "Fragments with worn, irregular fracture surfaces (still mating).",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Displaced Count", "Dc",
            "Total cut-region vertices displaced (across all fragments).",
            GH_ParamAccess.item);
        p.AddTextParameter("Report", "Rp", "Per-fragment displacement count.",
            GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var inputs = new List<Mesh>();
        double amplitude = 0.05;
        double frequency = 3.0;
        int octaves = 4;
        double roughness = 0.5;
        bool capCuts = true;
        int seed = 42;
        bool run = false;
        double cutRes = 0.0;
        double rimTaper = 0.04;
        if (!da.GetDataList(0, inputs)) return;
        da.GetData(1, ref amplitude);
        da.GetData(2, ref roughness);
        da.GetData(3, ref seed);
        da.GetData(4, ref run);
        da.GetData(5, ref frequency);
        da.GetData(6, ref octaves);
        da.GetData(7, ref capCuts);
        // Appended inputs (8/9) -- guard for canvases saved before they
        // existed.
        if (Params.Input.Count > 8) da.GetData(8, ref cutRes);
        if (Params.Input.Count > 9) da.GetData(9, ref rimTaper);
        if (!run)
        {
            da.SetData(2, "Run is false. Toggle to apply.");
            return;
        }
        if (octaves < 1) octaves = 1;
        if (octaves > 8) octaves = 8;

        // ONE shared noise field for ALL fragments. The three channels give a
        // 3D displacement vector; large offsets decorrelate the channels.
        var field = new FractalField(seed, Math.Max(1, octaves), roughness);

        // Frequency is expressed per bbox-diagonal; convert to world units
        // using the COMBINED bounding box of all fragments so every fragment
        // samples the same field at the same world frequency.
        var union = BoundingBox.Empty;
        foreach (var m in inputs) if (m != null) union.Union(m.GetBoundingBox(true));
        double diag = union.IsValid ? union.Diagonal.Length : 1.0;
        if (diag < 1e-9) diag = 1.0;
        double freqScale = frequency / diag;     // cycles per world unit
        double ampWorld = amplitude * diag;      // displacement in world units

        // Cap refinement target edge: resolve the finest noise octave.
        // cutRes 0 = auto; cutRes < 0 = no refinement (legacy caps).
        double targetEdge;
        if (cutRes < 0) targetEdge = 0;
        else if (cutRes > 0) targetEdge = cutRes * diag;
        else targetEdge = diag / (2.0 * Math.Max(0.5, frequency) *
                                  Math.Pow(2.0, octaves - 1));
        double taperWorld = Math.Max(0.0, rimTaper) * diag;

        var outputs = new List<Mesh>(inputs.Count);
        int totalDisplaced = 0;
        var report = new System.Text.StringBuilder();

        for (int f = 0; f < inputs.Count; f++)
        {
            var src = inputs[f];
            if (src == null) { outputs.Add(null); continue; }

            if (capCuts)
            {
                var m = RoughenCapped(src, field, freqScale, ampWorld,
                    targetEdge, taperWorld,
                    out int displaced, out double capAreaFrac, out int added);
                outputs.Add(m);
                totalDisplaced += displaced;
                report.AppendLine(
                    $"Fragment {f}: displaced {displaced} cap vertices " +
                    $"(+{added} refinement verts), fracture-surface area " +
                    $"share {capAreaFrac:F2}.");
            }
            else
            {
                // Legacy open-rim path (geometric Kintsugi wants OPEN rims).
                var m = src.DuplicateMesh();
                int preVerts = m.Vertices.Count;
                var cut = new bool[preVerts];
                MarkNakedEdgeVertices(m, cut);
                int displaced = 0;
                for (int v = 0; v < m.Vertices.Count; v++)
                {
                    if (v >= cut.Length || !cut[v]) continue;
                    var p = m.Vertices[v];
                    field.Sample(p.X * freqScale, p.Y * freqScale, p.Z * freqScale,
                                 out double dx, out double dy, out double dz);
                    m.Vertices.SetVertex(v, new Point3f(
                        (float)(p.X + dx * ampWorld),
                        (float)(p.Y + dy * ampWorld),
                        (float)(p.Z + dz * ampWorld)));
                    displaced++;
                }
                m.Normals.ComputeNormals();
                m.Compact();
                outputs.Add(m);
                totalDisplaced += displaced;
                report.AppendLine($"Fragment {f}: displaced {displaced} rim vertices (open rim).");
            }
        }
        report.AppendLine();
        report.AppendLine($"Total displaced: {totalDisplaced}.");
        report.AppendLine($"Shared fractal field: seed={seed}, octaves={octaves}, " +
                          $"freq={frequency:G3}/diag, amp={amplitude:G3}*diag.");
        if (capCuts)
        {
            report.AppendLine($"Cap refinement target edge: {targetEdge:G3} " +
                              $"(diag {diag:G4}); rim taper {taperWorld:G3}.");
            report.AppendLine("Breaking Bad targets: fracture-surface point share " +
                              "0.04-0.27 (median 0.11); deviation/extent 0.031-0.061 " +
                              "(median 0.044). Real granite facets: 0.0045.");
        }
        report.AppendLine("Field is world-position based, so adjacent fragments stay mated.");
        da.SetDataList(0, outputs);
        da.SetData(1, totalDisplaced);
        da.SetData(2, report.ToString());
    }

    // -------------------------------------------------------------------------
    // Capped-cut path: FillHoles -> refine cap interior -> displace with rim
    // taper. See the header comment for why each stage exists.
    // -------------------------------------------------------------------------

    private const int CapVertexBudget = 20000;

    private static Mesh RoughenCapped(Mesh src, FractalField field,
        double freqScale, double ampWorld, double targetEdge, double taperWorld,
        out int displaced, out double capAreaFrac, out int addedVerts)
    {
        displaced = 0;
        capAreaFrac = 0;
        addedVerts = 0;

        var m = src.DuplicateMesh();
        int preFaces = m.Faces.Count;
        try { m.FillHoles(); } catch { }
        // FillHoles declines the LARGE rim loops that meander across several
        // Voronoi cut planes (verified live 2026-07-11: every shatter
        // fragment kept 1-2 big naked loops, so pieces were never closed and
        // the learned path sampled no fracture surface there). Cap whatever
        // is still open with a centroid fan; refinement + the noise field
        // turn the fan into a normal rough fracture surface.
        try { FanCapRemainingHoles(m); } catch { }
        // FillHoles DUPLICATES the rim vertices for its cap (verified live:
        // +1 cap vertex per rim vertex). The cap is then only position-
        // welded, not index-welded; displacing rim vertices on one side
        // splits the seam into two naked loops. Weld BEFORE collecting cap
        // triangles so skin and cap share rim indices and move together.
        try { m.Vertices.CombineIdentical(true, true); } catch { }
        int postVerts = m.Vertices.Count;
        int postFaces = m.Faces.Count;

        // Cap triangles = faces added by FillHoles (appended at the END, so
        // deleting them later leaves skin face indices untouched).
        var capTris = new List<int[]>();
        for (int fi = preFaces; fi < postFaces; fi++)
        {
            var face = m.Faces[fi];
            if (face.IsQuad)
            {
                capTris.Add(new[] { face.A, face.B, face.C });
                capTris.Add(new[] { face.A, face.C, face.D });
            }
            else capTris.Add(new[] { face.A, face.B, face.C });
        }
        if (capTris.Count == 0)
        {
            // Nothing was capped (already closed, or FillHoles failed).
            m.Normals.ComputeNormals();
            m.Compact();
            return m;
        }

        // Outer-skin normals at cap vertices (accumulated from PRE-cap faces
        // only). Rim vertices sit on both skin and cap; their skin normal
        // defines the tangent plane the taper projects onto.
        var capVertSet = new HashSet<int>();
        foreach (var t in capTris) { capVertSet.Add(t[0]); capVertSet.Add(t[1]); capVertSet.Add(t[2]); }
        m.FaceNormals.ComputeFaceNormals();
        var skinNormal = new Dictionary<int, Vector3d>();
        for (int fi = 0; fi < preFaces; fi++)
        {
            var face = m.Faces[fi];
            var n = (Vector3d)m.FaceNormals[fi];
            int cnt = face.IsQuad ? 4 : 3;
            for (int c = 0; c < cnt; c++)
            {
                int v = face[c];
                if (!capVertSet.Contains(v)) continue;
                skinNormal.TryGetValue(v, out var acc);
                skinNormal[v] = acc + n;
            }
        }

        // New vertex positions appended past the mesh's current count.
        var newPos = new List<Point3d>();
        Point3d PosOf(int idx) => idx < postVerts
            ? (Point3d)m.Vertices[idx]
            : newPos[idx - postVerts];

        // Iterative conforming refinement of cap INTERIOR edges. Rim edges
        // (edge soup count == 1, i.e. shared with the un-split skin) are
        // never split, so no T-junction against the skin can form.
        if (targetEdge > 0)
        {
            for (int round = 0; round < 8 && newPos.Count < CapVertexBudget; round++)
            {
                var edgeUse = CountEdges(capTris);
                var mid = new Dictionary<(int, int), int>();
                foreach (var kv in edgeUse)
                {
                    if (kv.Value < 2) continue; // rim edge: never split
                    var (a, b) = kv.Key;
                    if (PosOf(a).DistanceTo(PosOf(b)) <= targetEdge) continue;
                    var mp = (PosOf(a) + PosOf(b)) * 0.5;
                    mid[kv.Key] = postVerts + newPos.Count;
                    newPos.Add(mp);
                    if (newPos.Count >= CapVertexBudget) break;
                }
                if (mid.Count == 0) break;
                capTris = SplitTriangles(capTris, mid);
            }
        }

        // Rim vertices of the FINAL cap triangulation.
        var finalEdgeUse = CountEdges(capTris);
        var rimVerts = new HashSet<int>();
        foreach (var kv in finalEdgeUse)
        {
            if (kv.Value == 1) { rimVerts.Add(kv.Key.Item1); rimVerts.Add(kv.Key.Item2); }
        }

        // Multi-source BFS from the rim across cap edges: taper distance +
        // propagated skin normal (first visit wins). Edges are near-uniform
        // after refinement, so accumulated edge lengths approximate the
        // geodesic well enough for a blend weight.
        var dist = new Dictionary<int, double>();
        var rimN = new Dictionary<int, Vector3d>();
        var queue = new Queue<int>();
        foreach (var rv in rimVerts)
        {
            dist[rv] = 0;
            skinNormal.TryGetValue(rv, out var n);
            if (n.Length > 1e-12) n.Unitize();
            rimN[rv] = n;
            queue.Enqueue(rv);
        }
        var adj = BuildAdjacency(finalEdgeUse);
        while (queue.Count > 0)
        {
            int v = queue.Dequeue();
            if (!adj.TryGetValue(v, out var nbrs)) continue;
            foreach (var w in nbrs)
            {
                if (dist.ContainsKey(w)) continue;
                dist[w] = dist[v] + PosOf(v).DistanceTo(PosOf(w));
                rimN[w] = rimN[v];
                queue.Enqueue(w);
            }
        }

        // Displace every cap vertex by the SHARED field, tapered at the rim.
        // Rim (weight 0): tangential-only -> outer skin silhouette preserved,
        // crack line still wiggles. Interior (weight 1): full 3D displacement.
        var moved = new Dictionary<int, Point3d>();
        var displaceTargets = new List<int>(capVertSet.Count + newPos.Count);
        foreach (var v in capVertSet) displaceTargets.Add(v);
        for (int k = 0; k < newPos.Count; k++) displaceTargets.Add(postVerts + k);
        foreach (var v in displaceTargets)
        {
            var p = PosOf(v);
            field.Sample(p.X * freqScale, p.Y * freqScale, p.Z * freqScale,
                         out double dx, out double dy, out double dz);
            var d = new Vector3d(dx * ampWorld, dy * ampWorld, dz * ampWorld);
            double w = taperWorld <= 0 ? 1.0
                : Math.Min(1.0, (dist.TryGetValue(v, out var dv) ? dv : taperWorld) / taperWorld);
            if (w < 1.0)
            {
                var n = rimN.TryGetValue(v, out var rn) ? rn : Vector3d.Zero;
                if (n.Length > 1e-12)
                {
                    var tangential = d - n * (d * n);
                    d = tangential * (1.0 - w) + d * w;
                }
            }
            moved[v] = p + d;
            displaced++;
        }

        // Rebuild: drop the coarse cap faces, append refined ones. Cap faces
        // are the mesh's LAST faces, so skin face indices survive the delete.
        var capFaceIdx = new List<int>(postFaces - preFaces);
        for (int fi = preFaces; fi < postFaces; fi++) capFaceIdx.Add(fi);
        // compact:false -- vertex indices must stay stable for `moved` and
        // the refined-triangle index references.
        m.Faces.DeleteFaces(capFaceIdx, false);
        foreach (var p in newPos) m.Vertices.Add(p);
        foreach (var t in capTris) m.Faces.AddFace(t[0], t[1], t[2]);
        addedVerts = newPos.Count;

        foreach (var kv in moved)
            m.Vertices.SetVertex(kv.Key, new Point3f(
                (float)kv.Value.X, (float)kv.Value.Y, (float)kv.Value.Z));

        // Fracture-surface area share (target: BB neighbour-contact point
        // share 0.04-0.27, median 0.11).
        double capArea = 0;
        foreach (var t in capTris)
            capArea += TriArea(PosOfMoved(m, t[0]), PosOfMoved(m, t[1]), PosOfMoved(m, t[2]));
        double totalArea = 0;
        m.FaceNormals.ComputeFaceNormals();
        for (int fi = 0; fi < m.Faces.Count; fi++)
        {
            var face = m.Faces[fi];
            var a = (Point3d)m.Vertices[face.A];
            var b = (Point3d)m.Vertices[face.B];
            var c = (Point3d)m.Vertices[face.C];
            totalArea += TriArea(a, b, c);
            if (face.IsQuad)
                totalArea += TriArea(a, c, (Point3d)m.Vertices[face.D]);
        }
        capAreaFrac = totalArea > 1e-12 ? capArea / totalArea : 0;

        m.Normals.ComputeNormals();
        m.Compact();
        return m;
    }

    /// <summary>
    /// Cap every remaining closed naked-edge loop with a centroid fan.
    /// Loop points are mapped back to vertex indices by exact float match
    /// (naked-edge points originate from those vertices, so the roundtrip
    /// is bit-exact). The fan's rim edges stay protected in the refinement
    /// (they appear once in the cap soup); its spokes are splittable.
    /// </summary>
    private static void FanCapRemainingHoles(Mesh m)
    {
        var naked = m.GetNakedEdges();
        if (naked == null || naked.Length == 0) return;
        var vmap = new Dictionary<Point3f, int>(m.Vertices.Count);
        for (int vi = 0; vi < m.Vertices.Count; vi++) vmap[m.Vertices[vi]] = vi;
        foreach (var loop in naked)
        {
            if (loop == null || !loop.IsClosed || loop.Count < 4) continue;
            var idx = new List<int>(loop.Count - 1);
            bool ok = true;
            for (int k = 0; k < loop.Count - 1; k++)
            {
                var pf = new Point3f((float)loop[k].X, (float)loop[k].Y, (float)loop[k].Z);
                if (!vmap.TryGetValue(pf, out int vi)) { ok = false; break; }
                idx.Add(vi);
            }
            if (!ok || idx.Count < 3) continue;
            var cen = new Point3d(0, 0, 0);
            foreach (var k in idx) cen += (Point3d)m.Vertices[k];
            cen /= idx.Count;
            int ci = m.Vertices.Add(cen.X, cen.Y, cen.Z);
            for (int k = 0; k < idx.Count; k++)
                m.Faces.AddFace(idx[k], idx[(k + 1) % idx.Count], ci);
        }
    }

    private static Point3d PosOfMoved(Mesh m, int idx) => (Point3d)m.Vertices[idx];

    private static double TriArea(Point3d a, Point3d b, Point3d c)
    {
        var ab = b - a;
        var ac = c - a;
        return 0.5 * Vector3d.CrossProduct(ab, ac).Length;
    }

    private static Dictionary<(int, int), int> CountEdges(List<int[]> tris)
    {
        var use = new Dictionary<(int, int), int>();
        foreach (var t in tris)
        {
            for (int e = 0; e < 3; e++)
            {
                int a = t[e], b = t[(e + 1) % 3];
                var key = a < b ? (a, b) : (b, a);
                use.TryGetValue(key, out int c);
                use[key] = c + 1;
            }
        }
        return use;
    }

    private static Dictionary<int, List<int>> BuildAdjacency(
        Dictionary<(int, int), int> edges)
    {
        var adj = new Dictionary<int, List<int>>();
        foreach (var kv in edges)
        {
            var (a, b) = kv.Key;
            if (!adj.TryGetValue(a, out var la)) adj[a] = la = new List<int>();
            la.Add(b);
            if (!adj.TryGetValue(b, out var lb)) adj[b] = lb = new List<int>();
            lb.Add(a);
        }
        return adj;
    }

    /// <summary>
    /// One conforming subdivision pass. `mid` maps a (sorted) edge to its
    /// midpoint vertex index; every triangle splits according to how many of
    /// its edges have midpoints (1-to-2 / 1-to-3 / 1-to-4). Because `mid` is
    /// shared, the pass cannot create T-junctions inside the cap.
    /// </summary>
    private static List<int[]> SplitTriangles(
        List<int[]> tris, Dictionary<(int, int), int> mid)
    {
        int MidOf(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            return mid.TryGetValue(key, out int v) ? v : -1;
        }

        var result = new List<int[]>(tris.Count * 2);
        foreach (var t in tris)
        {
            int a = t[0], b = t[1], c = t[2];
            int mab = MidOf(a, b), mbc = MidOf(b, c), mca = MidOf(c, a);
            int k = (mab >= 0 ? 1 : 0) + (mbc >= 0 ? 1 : 0) + (mca >= 0 ? 1 : 0);
            if (k == 0)
            {
                result.Add(t);
            }
            else if (k == 3)
            {
                result.Add(new[] { a, mab, mca });
                result.Add(new[] { mab, b, mbc });
                result.Add(new[] { mca, mbc, c });
                result.Add(new[] { mab, mbc, mca });
            }
            else if (k == 1)
            {
                // Rotate so the split edge is (a, b).
                if (mbc >= 0) { var tmp = a; a = b; b = c; c = tmp; mab = mbc; }
                else if (mca >= 0) { var tmp = c; c = b; b = a; a = tmp; mab = mca; }
                result.Add(new[] { a, mab, c });
                result.Add(new[] { mab, b, c });
            }
            else // k == 2
            {
                // Rotate so the UNSPLIT edge is (c, a).
                if (mca < 0)
                {
                    // already (ab, bc split; ca unsplit)
                }
                else if (mab < 0)
                {
                    var tmp = a; a = b; b = c; c = tmp;
                    var tm = mbc; mbc = mca; mab = tm;
                }
                else // mbc < 0
                {
                    var tmp = c; c = b; b = a; a = tmp;
                    var tm = mab; mab = mca; mbc = tm;
                }
                result.Add(new[] { a, mab, mbc });
                result.Add(new[] { a, mbc, c });
                result.Add(new[] { mab, b, mbc });
            }
        }
        return result;
    }

    private static void MarkNakedEdgeVertices(Mesh m, bool[] naked)
    {
        var topEdges = m.TopologyEdges;
        var topVerts = m.TopologyVertices;
        for (int e = 0; e < topEdges.Count; e++)
        {
            if (topEdges.GetConnectedFaces(e).Length != 1) continue;
            var pair = topEdges.GetTopologyVertices(e);
            foreach (var meshV in topVerts.MeshVertexIndices(pair.I))
                if (meshV < naked.Length) naked[meshV] = true;
            foreach (var meshV in topVerts.MeshVertexIndices(pair.J))
                if (meshV < naked.Length) naked[meshV] = true;
        }
    }

    // -------------------------------------------------------------------------
    // Deterministic 3D fractal value-noise. Seeded hash -> lattice gradients,
    // trilinear interpolation, summed over octaves. Three decorrelated channels
    // (via large coordinate offsets) form the displacement vector. Pure
    // function of position: the same (x,y,z) ALWAYS yields the same vector,
    // which is what keeps adjacent fragments mating.
    // -------------------------------------------------------------------------
    private sealed class FractalField
    {
        private readonly int _seed;
        private readonly int _octaves;
        private readonly double _persistence;

        public FractalField(int seed, int octaves, double persistence)
        {
            _seed = seed;
            _octaves = octaves;
            _persistence = (persistence > 0 && persistence <= 1) ? persistence : 0.5;
        }

        public void Sample(double x, double y, double z,
                           out double dx, out double dy, out double dz)
        {
            dx = Fbm(x, y, z, 0);
            dy = Fbm(x + 137.13, y + 71.7, z + 19.3, 1);
            dz = Fbm(x - 53.7, y - 113.1, z + 211.9, 2);
        }

        private double Fbm(double x, double y, double z, int channel)
        {
            double sum = 0, amp = 1.0, freq = 1.0, norm = 0;
            for (int o = 0; o < _octaves; o++)
            {
                sum += amp * ValueNoise(x * freq, y * freq, z * freq, channel + o * 31);
                norm += amp;
                amp *= _persistence;
                freq *= 2.0;
            }
            return norm > 0 ? sum / norm : 0.0; // in [-1, 1]
        }

        private double ValueNoise(double x, double y, double z, int salt)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y), zi = (int)Math.Floor(z);
            double xf = x - xi, yf = y - yi, zf = z - zi;
            double u = Fade(xf), v = Fade(yf), w = Fade(zf);
            double c000 = Lattice(xi, yi, zi, salt);
            double c100 = Lattice(xi + 1, yi, zi, salt);
            double c010 = Lattice(xi, yi + 1, zi, salt);
            double c110 = Lattice(xi + 1, yi + 1, zi, salt);
            double c001 = Lattice(xi, yi, zi + 1, salt);
            double c101 = Lattice(xi + 1, yi, zi + 1, salt);
            double c011 = Lattice(xi, yi + 1, zi + 1, salt);
            double c111 = Lattice(xi + 1, yi + 1, zi + 1, salt);
            double x00 = Lerp(c000, c100, u), x10 = Lerp(c010, c110, u);
            double x01 = Lerp(c001, c101, u), x11 = Lerp(c011, c111, u);
            double y0 = Lerp(x00, x10, v), y1 = Lerp(x01, x11, v);
            return Lerp(y0, y1, w); // [-1, 1]
        }

        private double Lattice(int x, int y, int z, int salt)
        {
            unchecked
            {
                uint h = (uint)(_seed * 374761393 + salt * 668265263);
                h ^= (uint)(x * 0x8da6b343);
                h ^= (uint)(y * 0xd8163841);
                h ^= (uint)(z * 0xcb1ab31f);
                h = (h ^ (h >> 15)) * 0x2c1b3c6d;
                h = (h ^ (h >> 12)) * 0x297a2d39;
                h ^= h >> 15;
                return (h / (double)uint.MaxValue) * 2.0 - 1.0; // [-1, 1]
            }
        }

        private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }
}
