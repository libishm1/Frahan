#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
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
        // Appended 2026-07-11 (output index 3; appending preserves saved
        // canvases).
        p.AddMeshParameter("Fracture Surfaces", "Fs",
            "TREE of fracture surface submeshes: one BRANCH per fragment, " +
            "one ITEM per cut interface (the displaced caps, no skin; " +
            "separate meshes because position-welded topology would " +
            "reconnect them). Wire into Facet Match's Fracture Regions " +
            "input for exact-correspondence matching: mates carry the " +
            "identical surface by construction. Empty when Cap Cuts=FALSE.",
            GH_ParamAccess.tree);
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
        var capTree = new Grasshopper.Kernel.Data.GH_Structure<GH_Mesh>();
        int totalDisplaced = 0;
        var report = new System.Text.StringBuilder();

        if (capCuts)
        {
            // pair-based: each interface surface is built ONCE and shared by
            // both fragments (see RoughenCappedPaired header)
            var piecesPer = new List<Mesh>[inputs.Count];
            RoughenCappedPaired(inputs, field, freqScale, ampWorld,
                targetEdge, taperWorld, outputs, piecesPer,
                out totalDisplaced, report);
            for (int f = 0; f < inputs.Count; f++)
            {
                var path = new Grasshopper.Kernel.Data.GH_Path(f);
                capTree.EnsurePath(path);
                if (piecesPer[f] == null) continue;
                foreach (var piece in piecesPer[f])
                    capTree.Append(new GH_Mesh(piece), path);
            }
        }
        else for (int f = 0; f < inputs.Count; f++)
        {
            var src = inputs[f];
            if (src == null) { outputs.Add(null); continue; }

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
            capTree.EnsurePath(new Grasshopper.Kernel.Data.GH_Path(f));
            totalDisplaced += displaced;
            report.AppendLine($"Fragment {f}: displaced {displaced} rim vertices (open rim).");
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
        if (Params.Output.Count > 3) da.SetDataTree(3, capTree);
    }

    // =========================================================================
    // PAIR-BASED capping (v4, 2026-07-12). Each interface surface is built
    // ONCE per fragment pair -- from the lower-index fragment's rim sampling
    // -- then the SAME mesh is stitched into both fragments and emitted to
    // both branches of the pieces tree. Mate correspondence is bit-exact by
    // construction. Per-fragment capping (v1-v3) could not achieve this:
    // sequential cell clipping tessellates the shared rim curve differently
    // per fragment (measured 2026-07-12: only ~half the rim points coincide
    // at N>=3), so every fragment-local decomposition diverged from its mate
    // near junctions by 10-27 rms on a diag-165 block.
    // =========================================================================

    private const int PairSurfaceVertexBudget = 8000;

    private sealed class PairSurface
    {
        public Mesh Mesh;             // final displaced surface
        public List<Point3d> Pos;     // local vertex positions (displaced)
        public List<int[]> Tris;      // local triangles
        public int BoundaryCount;     // Pos[0..BoundaryCount-1] = polygon boundary order
    }

    private static void RoughenCappedPaired(
        List<Mesh> inputs, FractalField field, double freqScale, double ampWorld,
        double targetEdge, double taperWorld,
        List<Mesh> outputs, List<Mesh>[] piecesPer, out int displacedTotal,
        System.Text.StringBuilder report)
    {
        int nF = inputs.Count;
        displacedTotal = 0;
        var W = new Mesh[nF];
        var loopsPer = new List<List<int>>[nF];
        for (int f = 0; f < nF; f++)
        {
            piecesPer[f] = new List<Mesh>();
            if (inputs[f] == null) continue;
            W[f] = inputs[f].DuplicateMesh();
            loopsPer[f] = ExtractNakedLoops(W[f]);
        }

        // mean rim edge length -> proximity tolerance
        double sumE = 0; int cntE = 0;
        for (int f = 0; f < nF; f++)
        {
            if (loopsPer[f] == null) continue;
            foreach (var loop in loopsPer[f])
                for (int k = 0; k < loop.Count; k++)
                {
                    sumE += ((Point3d)W[f].Vertices[loop[k]]).DistanceTo(
                            (Point3d)W[f].Vertices[loop[(k + 1) % loop.Count]]);
                    cntE++;
                }
        }
        double tolNear = 3.0 * (cntE > 0 ? sumE / cntE : 1.0);

        // flattened rim clouds per fragment
        var rimPts = new List<Point3d>[nF];
        for (int f = 0; f < nF; f++)
        {
            rimPts[f] = new List<Point3d>();
            if (loopsPer[f] == null) continue;
            foreach (var loop in loopsPer[f])
                foreach (var vi in loop) rimPts[f].Add((Point3d)W[f].Vertices[vi]);
        }
        double DistToRim(Point3d p, int g)
        {
            double best = double.MaxValue;
            var lst = rimPts[g];
            for (int k = 0; k < lst.Count; k++)
            {
                double d = (p - lst[k]).SquareLength;
                if (d < best) best = d;
            }
            return Math.Sqrt(best);
        }

        // EXCLUSIVE nearest-rim assignment of every rim point: p belongs to
        // interface (f, owner) where owner's rim is nearest within tolNear.
        // Exclusivity keeps adjacent pair surfaces from overlapping at
        // junction corners; unassigned corner slop is filled per fragment
        // at the end (pinned, no piece).
        var ownerPer = new int[nF][][];
        for (int f = 0; f < nF; f++)
        {
            if (loopsPer[f] == null) continue;
            ownerPer[f] = new int[loopsPer[f].Count][];
            for (int L = 0; L < loopsPer[f].Count; L++)
            {
                var loop = loopsPer[f][L];
                var own = new int[loop.Count];
                for (int k = 0; k < loop.Count; k++)
                {
                    var p = (Point3d)W[f].Vertices[loop[k]];
                    double bd = double.MaxValue; int bo = -1;
                    for (int g = 0; g < nF; g++)
                    {
                        if (g == f || rimPts[g] == null || rimPts[g].Count == 0) continue;
                        double d = DistToRim(p, g);
                        if (d < bd) { bd = d; bo = g; }
                    }
                    own[k] = bd <= tolNear ? bo : -1;
                }
                // cyclic majority smoothing: kill single-point flickers
                var sm = new int[loop.Count];
                for (int k = 0; k < loop.Count; k++)
                {
                    int lp = own[(k + loop.Count - 1) % loop.Count];
                    int ln = own[(k + 1) % loop.Count];
                    sm[k] = lp == ln ? lp : own[k];
                }
                ownerPer[f][L] = sm;
            }
        }

        // contiguous same-owner runs = the arcs of interface (f, g) as
        // sampled by fragment f
        List<(List<int> chain, bool closed)> ArcsOf(int f, int g)
        {
            var res = new List<(List<int>, bool)>();
            if (loopsPer[f] == null) return res;
            for (int L = 0; L < loopsPer[f].Count; L++)
            {
                var loop = loopsPer[f][L];
                var own = ownerPer[f][L];
                int n = loop.Count;
                int start = -1; bool all = true;
                for (int k = 0; k < n; k++)
                {
                    if (own[k] != g) all = false;
                    if (start < 0 && own[k] == g && own[(k + n - 1) % n] != g) start = k;
                }
                if (all) { res.Add((new List<int>(loop), true)); continue; }
                if (start < 0) continue;
                var chain = new List<int>();
                for (int t = 0; t < n; t++)
                {
                    int k = (start + t) % n;
                    if (own[k] == g) chain.Add(loop[k]);
                    else if (chain.Count > 0)
                    {
                        if (chain.Count >= 4) res.Add((chain, false));
                        chain = new List<int>();
                    }
                }
                if (chain.Count >= 4) res.Add((chain, false));
            }
            return res;
        }

        for (int i = 0; i < nF; i++)
        {
            if (W[i] == null) continue;
            for (int j = i + 1; j < nF; j++)
            {
                if (W[j] == null) continue;
                var arcsI = ArcsOf(i, j);
                if (arcsI.Count == 0) continue;
                var arcsJ = ArcsOf(j, i);
                if (arcsJ.Count == 0) continue;
                int pairSalt = unchecked(31 * (i + 1) + 977 * (j + 1));

                var polys = ClosePolygons(W[i], arcsI);
                int pieceVerts = 0;
                foreach (var poly in polys)
                {
                    var S = BuildInterfaceSurface(W[i], poly, field, freqScale,
                        ampWorld, targetEdge, taperWorld, pairSalt, out int disp);
                    if (S == null) continue;
                    displacedTotal += disp;
                    pieceVerts += S.Mesh.Vertices.Count;

                    AttachToOwner(W[i], poly, S);
                    AttachToPartnerAndZip(W[j], S, arcsJ, tolNear);

                    piecesPer[i].Add(S.Mesh.DuplicateMesh());
                    piecesPer[j].Add(S.Mesh.DuplicateMesh());
                }
                report.AppendLine($"Interface ({i},{j}): {arcsI.Count}/{arcsJ.Count} arcs " +
                                  $"-> {polys.Count} surface(s), {pieceVerts} verts.");
            }
        }

        // Close junction slop + zip leftovers (pinned, fragment-local,
        // never a piece), then orient outward. The zip strip is largely
        // degenerate (most partner rim points coincide bit-exactly with the
        // surface boundary), so weld identical vertices and cull the
        // zero-area faces BEFORE filling the remaining slits.
        for (int f = 0; f < nF; f++)
        {
            if (W[f] == null) { outputs.Add(null); continue; }
            W[f].Vertices.CombineIdentical(true, true);
            W[f].Faces.CullDegenerateFaces();
            FanCapRemainingHoles(W[f]);
            W[f].Faces.CullDegenerateFaces();
            if (!W[f].IsClosed) FanCapRemainingHoles(W[f]);
            W[f].Normals.ComputeNormals();
            W[f].FaceNormals.ComputeFaceNormals();
            W[f].UnifyNormals();
            W[f].FaceNormals.ComputeFaceNormals();
            if (W[f].Volume() < 0) W[f].Flip(true, true, true);
            W[f].Compact();
            outputs.Add(W[f]);
            report.AppendLine($"Fragment {f}: closed={W[f].IsClosed}, " +
                              $"{piecesPer[f].Count} interface piece(s).");
        }

        // Orient every piece OUTWARD from its own fragment. The two copies
        // of one interface surface then carry OPPOSING normals -- what
        // downstream mating logic (Facet Match) expects of mates.
        for (int f = 0; f < nF; f++)
        {
            if (W[f] == null || !W[f].IsClosed) continue;
            foreach (var piece in piecesPer[f])
            {
                piece.Normals.ComputeNormals();
                double eps = Math.Max(1e-6, piece.GetBoundingBox(true).Diagonal.Length * 0.02);
                int inside = 0, total = 0;
                int step = Math.Max(1, piece.Vertices.Count / 7);
                for (int v = 0; v < piece.Vertices.Count && total < 9; v += step)
                {
                    var n = new Vector3d(piece.Normals[v]);
                    if (n.Length < 1e-9) continue;
                    n.Unitize();
                    var q = (Point3d)piece.Vertices[v] + n * eps;
                    if (W[f].IsPointInside(q, 1e-6, true)) inside++;
                    total++;
                }
                if (inside * 2 > total) piece.Flip(true, true, true);
            }
        }
    }

    /// <summary>Naked-edge loops as vertex-index chains (exact float match
    /// back to mesh vertices; unresolvable loops skipped).</summary>
    private static List<List<int>> ExtractNakedLoops(Mesh m)
    {
        var res = new List<List<int>>();
        var naked = m.GetNakedEdges();
        if (naked == null) return res;
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
            if (ok && idx.Count >= 3) res.Add(idx);
        }
        return res;
    }

    /// <summary>Chain open arcs into closed polygons by nearest free
    /// endpoint (closed arcs pass through as-is). Chords between chained
    /// arcs are implicit; a jump limit splits disjoint interface patches
    /// into separate polygons.</summary>
    private static List<List<int>> ClosePolygons(
        Mesh m, List<(List<int> chain, bool closed)> arcs)
    {
        var res = new List<List<int>>();
        var open = new List<List<int>>();
        foreach (var a in arcs)
        {
            if (a.closed) res.Add(new List<int>(a.chain));
            else open.Add(new List<int>(a.chain));
        }
        if (open.Count == 0) return res;
        Point3d P(int vi) => (Point3d)m.Vertices[vi];
        var bb = BoundingBox.Empty;
        foreach (var c in open) foreach (var vi in c) bb.Union(P(vi));
        double jumpMax = Math.Max(1e-9, bb.Diagonal.Length * 0.6);
        var used = new bool[open.Count];
        for (int s = 0; s < open.Count; s++)
        {
            if (used[s]) continue;
            var poly = new List<int>(open[s]);
            used[s] = true;
            while (true)
            {
                var tail = P(poly[poly.Count - 1]);
                double bestD = double.MaxValue; int bestA = -1; bool atStart = true;
                for (int t = 0; t < open.Count; t++)
                {
                    if (used[t]) continue;
                    double dS = tail.DistanceTo(P(open[t][0]));
                    double dE = tail.DistanceTo(P(open[t][open[t].Count - 1]));
                    if (dS < bestD) { bestD = dS; bestA = t; atStart = true; }
                    if (dE < bestD) { bestD = dE; bestA = t; atStart = false; }
                }
                double closeD = tail.DistanceTo(P(poly[0]));
                if (bestA < 0 || bestD >= closeD || bestD > jumpMax) break;
                var next = new List<int>(open[bestA]);
                if (!atStart) next.Reverse();
                poly.AddRange(next);
                used[bestA] = true;
            }
            if (poly.Count >= 3) res.Add(poly);
        }
        return res;
    }

    /// <summary>Fan the polygon from its centroid, refine interior edges to
    /// the target length, then displace the interior with the pair-salted
    /// field (boundary PINNED: the crack line is shared with the skin of
    /// both fragments and must not move).</summary>
    private static PairSurface BuildInterfaceSurface(Mesh mOwner, List<int> poly,
        FractalField field, double freqScale, double ampWorld,
        double targetEdge, double taperWorld, int pairSalt, out int displaced)
    {
        displaced = 0;
        int B = poly.Count;
        if (B < 3) return null;
        var pos = new List<Point3d>(B + 16);
        foreach (var vi in poly) pos.Add((Point3d)mOwner.Vertices[vi]);
        var cen = new Point3d(0, 0, 0);
        foreach (var p in pos) cen += p;
        cen /= B;
        pos.Add(cen);
        var tris = new List<int[]>(B);
        for (int k = 0; k < B; k++) tris.Add(new[] { k, (k + 1) % B, B });

        if (targetEdge > 0)
        {
            for (int round = 0; round < 8 && pos.Count < PairSurfaceVertexBudget; round++)
            {
                var use = CountEdges(tris);
                var mid = new Dictionary<(int, int), int>();
                foreach (var kv in use)
                {
                    if (kv.Value < 2) continue;   // boundary edge: never split
                    var (a, b) = kv.Key;
                    if (pos[a].DistanceTo(pos[b]) <= targetEdge) continue;
                    mid[kv.Key] = pos.Count;
                    pos.Add((pos[a] + pos[b]) * 0.5);
                    if (pos.Count >= PairSurfaceVertexBudget) break;
                }
                if (mid.Count == 0) break;
                tris = SplitTriangles(tris, mid);
            }
        }

        // taper: edge-distance BFS from the pinned boundary
        var adj = BuildAdjacency(CountEdges(tris));
        var dist = new Dictionary<int, double>();
        var queue = new Queue<int>();
        for (int b = 0; b < B; b++) { dist[b] = 0; queue.Enqueue(b); }
        while (queue.Count > 0)
        {
            int v = queue.Dequeue();
            if (!adj.TryGetValue(v, out var nbrs)) continue;
            foreach (var w2 in nbrs)
            {
                if (dist.ContainsKey(w2)) continue;
                dist[w2] = dist[v] + pos[v].DistanceTo(pos[w2]);
                queue.Enqueue(w2);
            }
        }

        for (int v = B; v < pos.Count; v++)
        {
            var p = pos[v];
            field.Sample(p.X * freqScale, p.Y * freqScale, p.Z * freqScale, pairSalt,
                out double dx, out double dy, out double dz);
            var d = new Vector3d(dx * ampWorld, dy * ampWorld, dz * ampWorld);
            double w = taperWorld <= 0 ? 1.0
                : Math.Min(1.0, (dist.TryGetValue(v, out var dv) ? dv : taperWorld) / taperWorld);
            pos[v] = p + d * w;
            displaced++;
        }

        var mesh = new Mesh();
        foreach (var p in pos) mesh.Vertices.Add(p.X, p.Y, p.Z);
        foreach (var t in tris) mesh.Faces.AddFace(t[0], t[1], t[2]);
        mesh.Normals.ComputeNormals();
        return new PairSurface { Mesh = mesh, Pos = pos, Tris = tris, BoundaryCount = B };
    }

    /// <summary>Weld the surface into the fragment whose rim sampled it:
    /// boundary positions ARE that fragment's rim vertices, so the weld is
    /// exact by index.</summary>
    private static void AttachToOwner(Mesh mi, List<int> poly, PairSurface S)
    {
        var map = new int[S.Pos.Count];
        for (int b = 0; b < S.BoundaryCount; b++) map[b] = poly[b];
        for (int v = S.BoundaryCount; v < S.Pos.Count; v++)
            map[v] = mi.Vertices.Add(S.Pos[v].X, S.Pos[v].Y, S.Pos[v].Z);
        foreach (var t in S.Tris) mi.Faces.AddFace(map[t[0]], map[t[1]], map[t[2]]);
    }

    /// <summary>Append the identical surface to the partner fragment, then
    /// stitch the partner's own rim arcs to the surface boundary with thin
    /// rail triangles (both polylines sample the same physical curve, so
    /// the strip is near-degenerate).</summary>
    private static void AttachToPartnerAndZip(Mesh mj, PairSurface S,
        List<(List<int> chain, bool closed)> arcsJ, double tolNear)
    {
        int baseJ = mj.Vertices.Count;
        foreach (var p in S.Pos) mj.Vertices.Add(p.X, p.Y, p.Z);
        foreach (var t in S.Tris)
            mj.Faces.AddFace(baseJ + t[0], baseJ + t[1], baseJ + t[2]);

        int B = S.BoundaryCount;
        foreach (var (chain, closed) in arcsJ)
        {
            // polygon membership gate: the arc must actually run along THIS
            // surface's boundary (a pair can have disjoint patches)
            var midP = (Point3d)mj.Vertices[chain[chain.Count / 2]];
            double bd = double.MaxValue; int bMid = 0;
            for (int b = 0; b < B; b++)
            {
                double d = midP.DistanceTo(S.Pos[b]);
                if (d < bd) { bd = d; bMid = b; }
            }
            if (bd > 2 * tolNear) continue;

            var railA = new List<int>(chain);
            List<int> railB;
            var a0 = (Point3d)mj.Vertices[chain[0]];
            var a1 = (Point3d)mj.Vertices[chain[Math.Min(1, chain.Count - 1)]];
            int b0 = 0; double b0d = double.MaxValue;
            for (int b = 0; b < B; b++)
            {
                double d = a0.DistanceTo(S.Pos[b]);
                if (d < b0d) { b0d = d; b0 = b; }
            }
            if (closed)
            {
                railA.Add(chain[0]);   // close the cycle
                bool fwd = S.Pos[(b0 + 1) % B].DistanceTo(a1)
                        <= S.Pos[(b0 + B - 1) % B].DistanceTo(a1);
                railB = new List<int>(B + 1);
                for (int t = 0; t <= B; t++)
                    railB.Add(baseJ + (fwd ? (b0 + t) % B : (b0 + B - (t % B)) % B));
            }
            else
            {
                var aE = (Point3d)mj.Vertices[chain[chain.Count - 1]];
                int b1 = 0; double b1d = double.MaxValue;
                for (int b = 0; b < B; b++)
                {
                    double d = aE.DistanceTo(S.Pos[b]);
                    if (d < b1d) { b1d = d; b1 = b; }
                }
                // both cyclic directions from b0 to b1; pick the one passing
                // nearest the arc midpoint
                var fwdChain = new List<int>();
                for (int b = b0; ; b = (b + 1) % B) { fwdChain.Add(b); if (b == b1) break; }
                var bwdChain = new List<int>();
                for (int b = b0; ; b = (b + B - 1) % B) { bwdChain.Add(b); if (b == b1) break; }
                double fwdMid = double.MaxValue, bwdMid = double.MaxValue;
                foreach (var b in fwdChain)
                    fwdMid = Math.Min(fwdMid, midP.DistanceTo(S.Pos[b]));
                foreach (var b in bwdChain)
                    bwdMid = Math.Min(bwdMid, midP.DistanceTo(S.Pos[b]));
                var pick = fwdMid <= bwdMid ? fwdChain : bwdChain;
                railB = new List<int>(pick.Count);
                foreach (var b in pick) railB.Add(baseJ + b);
            }
            StitchRails(mj, railA, railB);
        }
    }

    /// <summary>Greedy rail stitch between two same-direction polylines of
    /// mesh vertex indices.</summary>
    private static void StitchRails(Mesh mj, List<int> railA, List<int> railB)
    {
        int ia = 0, ib = 0;
        while (ia < railA.Count - 1 || ib < railB.Count - 1)
        {
            bool advA;
            if (ia >= railA.Count - 1) advA = false;
            else if (ib >= railB.Count - 1) advA = true;
            else advA = ((Point3d)mj.Vertices[railA[ia + 1]]).DistanceTo(
                            (Point3d)mj.Vertices[railB[ib]])
                     <= ((Point3d)mj.Vertices[railB[ib + 1]]).DistanceTo(
                            (Point3d)mj.Vertices[railA[ia]]);
            if (advA) { mj.Faces.AddFace(railA[ia], railA[ia + 1], railB[ib]); ia++; }
            else { mj.Faces.AddFace(railA[ia], railB[ib + 1], railB[ib]); ib++; }
        }
    }

    internal static void FanCapRemainingHoles(Mesh m)
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
            Sample(x, y, z, 0, out dx, out dy, out dz);
        }

        /// <summary>
        /// Salted sampling: interfaceSalt keys the field PER CUT PLANE so
        /// different interfaces carry DECORRELATED relief while both mates
        /// of one interface (same plane, same salt) still move identically.
        /// Without this, one shared field made all interfaces statistically
        /// similar -- the facet matcher could not separate true pairs from
        /// impostors (found 2026-07-11).
        /// </summary>
        public void Sample(double x, double y, double z, int interfaceSalt,
                           out double dx, out double dy, out double dz)
        {
            dx = Fbm(x, y, z, 0 + interfaceSalt * 101);
            dy = Fbm(x + 137.13, y + 71.7, z + 19.3, 1 + interfaceSalt * 101);
            dz = Fbm(x - 53.7, y - 113.1, z + 211.9, 2 + interfaceSalt * 101);
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
