#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using Frahan.EdgeMatching;
using Frahan.GH.Attributes;
using Frahan.GH.ScanIngest;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Kintsugi;

// =============================================================================
// Frahan > Kintsugi > Facet Match (closed-mesh fracture assembly).
//
// Geometric fallback for WATERTIGHT fragments (scanned pieces, capped
// synthetic fragments) that have NO naked rims for the rim-matching path.
// Libish's proposal (2026-07-11): segment the mesh faces by dihedral angle,
// match complementary fracture FACETS across fragments, refine with Soft ICP.
//
// Pipeline
//   1. SEGMENT each fragment into facets: region-grow over face adjacency
//      while the dihedral angle stays under the threshold.
//   2. DESCRIBE each facet: area, centroid, area-weighted normal, principal
//      in-plane extents (2x2 covariance eigen), plane-RMS roughness.
//   3. PAIR complementary facets across fragments (similar area + extents;
//      orientation-free) and build candidate rigid poses by mating the
//      facet frames (target normal flipped; both principal-axis signs, plus
//      extra in-plane rotations for near-isotropic facets).
//   4. GATE each candidate: facet-sample RMS residual under Joint Width,
//      opposing normals after placement, no deep penetration into the
//      placed cluster (IsPointInside is reliable here: inputs are closed).
//   5. ASSEMBLE greedily from the largest fragment; repeat until no
//      progress.
//   6. Optional SOFT ICP polish: joint CPD-style refinement of all placed
//      poses (Frahan.EdgeMatching.SoftIcpRefiner) using facet samples as
//      the contact set and the closed meshes for the penetration hinge.
// =============================================================================

[Algorithm("Facet segmentation by dihedral angle",
    "Region-growing over the mesh face-adjacency graph while the dihedral " +
    "angle stays under a threshold; standard mesh segmentation primitive.",
    Note = "Facets below the Min Facet Share of total area are ignored as noise.")]
[Algorithm("Complementary facet matching + oriented frame mating",
    "Frahan-original: mating fracture facets have near-equal area/extents " +
    "and opposite outward normals; candidate poses come from PlaneToPlane " +
    "on principal-axis facet frames with the target normal flipped.",
    Note = "Both principal-axis signs are tested; near-isotropic facets get " +
           "extra in-plane rotations. Residual + normal opposition + " +
           "penetration gates decide.")]
[Algorithm("Soft ICP joint pose refinement",
    "CPD-style soft-correspondence refinement over SE(3)^N with a " +
    "penetration hinge (Frahan.EdgeMatching.SoftIcpRefiner).",
    Note = "Runs after greedy assembly; anchor stays fixed.")]
[DesignApplication(
    "Reassemble WATERTIGHT fracture fragments (scans, capped synthetic pieces) by matching complementary fracture facets",
    DesignFlow.BottomUp,
    Precedent = "Libish's facet-matching proposal 2026-07-11; region-growing segmentation; CPD soft ICP",
    Tolerance = "facet-sample RMS <= Joint Width at acceptance; report prints per-placement residuals")]
public sealed class FacetMatchComponent
    : AsyncScanComponent<FacetMatchComponent.MatchSnapshot, FacetMatchComponent.MatchPayload>
{
    public FacetMatchComponent()
        : base("Facet Match", "FacetMatch",
            "Reassemble WATERTIGHT fracture fragments by segmenting faces " +
            "into facets (dihedral-angle region growing), matching " +
            "complementary fracture facets across fragments, and refining " +
            "with Soft ICP. The closed-mesh sibling of the rim-matching " +
            "Kintsugi path: use THIS when fragments have no naked rims " +
            "(scanned pieces, Fracture Roughen with Cap Cuts=True). ASYNC: " +
            "runs on a background task; the canvas stays navigable and " +
            "results pop in when ready (Run=false cancels).",
            "Frahan", "Kintsugi")
    {
    }

    /// <summary>Immutable inputs captured on the UI thread.</summary>
    public sealed class MatchSnapshot
    {
        public List<Mesh> Fragments;
        public List<List<Mesh>> Regions;   // null = regions input unwired/mismatched
        public double AngleDeg, MinShare, JointWidth, PenTol, AcceptFloor;
        public bool SoftIcp, PenHinge;
        public bool RoughMode;
        public double RoughThreshold;      // 0 = auto (Otsu)
        // Resolution-escalation (2026-07-12): when true, run a SECOND
        // interlock-rescue pass over the fragments the primary pass left
        // unplaced. Default false = pinned behaviour exactly. Wired in INC-2;
        // INC-1 only carries the flag + the dormant InterlockRefine method.
        public bool InterlockRescue;
    }

    /// <summary>Result produced on the background thread.</summary>
    public sealed class MatchPayload
    {
        public List<Mesh> Assembled;
        public List<Transform> Transforms;
        public List<int> PlacedIdx, UnplacedIdx;
        public List<Curve> Outlines;
        public string Report;
        public List<string> Warnings = new List<string>();
    }

    // F2D00508: verified unique 2026-07-11 (501=Kintsugi, 502=Shatter,
    // 503=BBLoad, 504=Roughen, 505=LoadScan, 506=SyntheticBlock,
    // 507=SettleContact -- the first pick collided with 507 on Libish's
    // canvas; ALWAYS grep the series before assigning).
    public override Guid ComponentGuid =>
        new Guid("F2D00508-2026-4522-B0B0-1ABE15A0CAFE");

    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("KintsugiAssemble.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Fragments", "F",
            "Watertight fragment meshes. The largest fragment anchors at its " +
            "input pose; the others are placed against it.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Angle Threshold", "At",
            "Facet segmentation dihedral angle in DEGREES. Faces join a facet " +
            "while the angle to the seed region stays below this. Default 30. " +
            "Raise for rough fracture surfaces, lower for crisp cuts.",
            GH_ParamAccess.item, 30.0);
        p.AddNumberParameter("Min Facet Share", "Mf",
            "Ignore facets smaller than this FRACTION of their fragment's " +
            "total area. Default 0.05.",
            GH_ParamAccess.item, 0.05);
        p.AddNumberParameter("Joint Width", "J",
            "Accept a placement when the facet-sample RMS residual is below " +
            "this (document units). Default 1.0.",
            GH_ParamAccess.item, 1.0);
        p.AddNumberParameter("Penetration Tol", "Vp",
            "Reject placements whose mesh penetrates the placed cluster " +
            "deeper than this. 0 disables. Default 0.5.",
            GH_ParamAccess.item, 0.5);
        p.AddBooleanParameter("Soft ICP", "Si",
            "Polish the assembled poses jointly with the CPD Soft ICP " +
            "refiner after greedy placement (FAST contact-only mode: no " +
            "penetration hinge). Default TRUE. Coarse-to-fine per Libish's " +
            "workflow: facet frames give the GLOBAL alignment, the facet " +
            "Soft ICP does the LOCAL polish.",
            GH_ParamAccess.item, true);
        p.AddBooleanParameter("Run", "R", "Execute.", GH_ParamAccess.item, false);
        p.AddBooleanParameter("Penetration Hinge", "Ph",
            "Include the (expensive) per-sample inside-mesh penetration " +
            "term in the Soft ICP polish. Default FALSE = fast contact-only " +
            "refinement; enable for final-quality passes on small sets.",
            GH_ParamAccess.item, false);
        p.AddMeshParameter("Fracture Regions", "Fr",
            "OPTIONAL fracture surface TREE (one branch per fragment, one " +
            "item per interface; wire Fracture Roughen's Fracture Surfaces " +
            "output). When provided, facets come directly from these " +
            "regions -- mates then carry the IDENTICAL surface by " +
            "construction and no re-segmentation runs. Leave unwired for " +
            "scans (dihedral segmentation fallback).",
            GH_ParamAccess.tree);
        Params.Input[Params.Input.Count - 1].Optional = true;
        p.AddNumberParameter("Accept Floor", "Af",
            "Accept a facet pairing when its sample RMS is below this " +
            "MULTIPLE of the sampling-density floor (sqrt(area/K)). Two " +
            "independent samplings of the SAME surface sit near 0.7-1.0; " +
            "wrong partners mismatch the fracture relief and land higher. " +
            "Default 1.3. The report's candidate diagnostics show the " +
            "measured multiples; tune from there.",
            GH_ParamAccess.item, 1.3);
        // Appended 2026-07-12 (appending preserves saved canvases).
        p.AddBooleanParameter("Roughness Mode", "Rm",
            "TRUE = segment facets by SURFACE ROUGHNESS instead of dihedral " +
            "angles: faces are classified fracture (rough) vs skin (smooth) " +
            "by local normal dispersion at a scale-relative radius, and " +
            "connected fracture regions become the facets. Use for REAL " +
            "SCANS of smooth objects (ceramics, glazed pottery), where the " +
            "shallow fracture facet never shows up as a dihedral crease. " +
            "Default FALSE (dihedral segmentation).",
            GH_ParamAccess.item, false);
        p.AddNumberParameter("Roughness Threshold", "Rt",
            "Roughness Mode only. Faces whose local normal dispersion " +
            "(0=perfectly smooth, 1=isotropic) exceeds this are FRACTURE. " +
            "0 (default) = automatic Otsu threshold on the roughness " +
            "histogram; the report prints the value used. Tune from there " +
            "when auto misjudges (e.g. all-rough natural stone).",
            GH_ParamAccess.item, 0.0);
        // Appended 2026-07-12 (appending preserves saved canvases).
        p.AddBooleanParameter("Interlock Rescue", "Ir",
            "TRUE = after the primary rough-mode pass, run a SECOND pass that " +
            "tries to place the fragments the first pass rejected, using the " +
            "dense-sampled INTERLOCK REFINE (a line-search along the fracture " +
            "band to the micro-relief minimum). This rescues PARTIAL-RIM " +
            "mates (e.g. a thin sliver) that boundary matching cannot pin, at " +
            "the cost of the full-resolution interlock search. Needs a " +
            "high-resolution host mesh to bite; on a coarse host it simply " +
            "rejects (safe). Default FALSE (single pass, pinned behaviour).",
            GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Assembled", "M",
            "Fragments at their assembled poses (anchor unmoved).",
            GH_ParamAccess.list);
        p.AddTransformParameter("Transforms", "X",
            "Per-fragment SE(3) transform, parallel to Fragments.",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Placed Indices", "Pi",
            "Fragment indices that were placed.", GH_ParamAccess.list);
        p.AddIntegerParameter("Unplaced Indices", "Ui",
            "Fragment indices left at their input pose.", GH_ParamAccess.list);
        p.AddCurveParameter("Facet Outlines", "Fo",
            "Facet boundary polylines at the INPUT poses. Diagnostic for " +
            "tuning Angle Threshold / Min Facet Share.",
            GH_ParamAccess.list);
        p.AddTextParameter("Report", "Rp", "Assembly summary.", GH_ParamAccess.item);
    }

    // -------------------------------------------------------------------------

    private sealed class Facet
    {
        public int FragIdx;
        public List<int> Faces = new List<int>();
        public double Area;
        public Point3d Centroid;
        public Vector3d Normal;        // area-weighted outward
        public Plane Frame;            // origin=centroid, Z=Normal, X=principal axis
        public double E1, E2;          // principal in-plane extents (std-devs)
        public double Rms;             // deviation from the fit plane
        public Point3d[] Samples;      // area-weighted surface samples
        public Vector3d[] SampleNormals;
        // closed boundary loop resampled to fixed count, in FRAME coords
        // (u,v). Mates share the identical boundary curve, so 2D contour
        // registration recovers the true in-plane rotation.
        public Point2d[] Boundary2D;
        // the same resampled loop in 3D (input pose). Corresponded 3D
        // boundary distance is the pose-pinning score: NN sample RMS on
        // dense near-planar sets is blind to in-plane sliding/rotation.
        public Point3d[] Boundary3D;
        public double BoundaryLength;
        // relief signature along the boundary: signed height of the nearby
        // facet surface above the frame plane. Disambiguates symmetric
        // outlines: mates read the same bumps with OPPOSITE sign, so
        // cand[i]+host[matched i] is ~constant only for the true
        // correspondence.
        public double[] BoundaryRelief;
        // RoughMode: crack-line ring (facet boundary bordering TRUE skin),
        // fragment-local coordinates; computed ONCE per kept facet -- the
        // per-pair recomputation rebuilt a 100k-face adjacency per pair and
        // killed the process under ~100 candidate pairs
        public Point3d[] CrackRing;
    }

    protected override bool TryReadRunOnly(IGH_DataAccess da, out bool run)
    {
        run = false;
        da.GetData(6, ref run);
        return true;
    }

    protected override bool TryRead(IGH_DataAccess da, out bool run, out MatchSnapshot snapshot)
    {
        snapshot = null;
        run = false;
        da.GetData(6, ref run);
        if (!run) return true;

        var fragments = new List<Mesh>();
        double angleDeg = 30.0, minShare = 0.05, jointWidth = 1.0, penTol = 0.5;
        bool softIcp = true, penHinge = false;
        if (!da.GetDataList(0, fragments)) return true;
        da.GetData(1, ref angleDeg);
        da.GetData(2, ref minShare);
        da.GetData(3, ref jointWidth);
        da.GetData(4, ref penTol);
        da.GetData(5, ref softIcp);
        if (Params.Input.Count > 7) da.GetData(7, ref penHinge);
        Grasshopper.Kernel.Data.GH_Structure<Grasshopper.Kernel.Types.GH_Mesh> regionTree = null;
        if (Params.Input.Count > 8)
        {
            try { da.GetDataTree(8, out regionTree); } catch { regionTree = null; }
        }
        double acceptFloor = 1.3;
        if (Params.Input.Count > 9) da.GetData(9, ref acceptFloor);
        bool roughMode = false;
        double roughThreshold = 0.0;
        bool interlockRescue = false;
        if (Params.Input.Count > 10) da.GetData(10, ref roughMode);
        if (Params.Input.Count > 11) da.GetData(11, ref roughThreshold);
        if (Params.Input.Count > 12) da.GetData(12, ref interlockRescue);
        if (fragments.Count < 2)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Need >= 2 fragments.");
            return false;
        }

        // owned deep copies: the background task must never touch live GH data
        List<List<Mesh>> regions = null;
        if (regionTree != null && regionTree.PathCount == fragments.Count && !regionTree.IsEmpty)
        {
            regions = new List<List<Mesh>>(fragments.Count);
            for (int f = 0; f < fragments.Count; f++)
            {
                var lst = new List<Mesh>();
                foreach (var gm in regionTree.Branches[f])
                {
                    var piece = gm?.Value;
                    if (piece != null && piece.Faces.Count > 0)
                        lst.Add(piece.DuplicateMesh());
                }
                regions.Add(lst);
            }
        }
        snapshot = new MatchSnapshot
        {
            Fragments = fragments.Select(m => m?.DuplicateMesh()).ToList(),
            Regions = regions,
            AngleDeg = angleDeg,
            MinShare = minShare,
            JointWidth = jointWidth,
            PenTol = penTol,
            AcceptFloor = acceptFloor,
            SoftIcp = softIcp,
            PenHinge = penHinge,
            RoughMode = roughMode,
            RoughThreshold = roughThreshold,
            InterlockRescue = interlockRescue,
        };
        return true;
    }

    protected override void EmitResult(IGH_DataAccess da, MatchPayload payload)
    {
        foreach (var w in payload.Warnings)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, w);
        da.SetDataList(0, payload.Assembled);
        da.SetDataList(1, payload.Transforms);
        da.SetDataList(2, payload.PlacedIdx);
        da.SetDataList(3, payload.UnplacedIdx);
        da.SetDataList(4, payload.Outlines);
        da.SetData(5, payload.Report);
    }

    protected override void EmitIdle(IGH_DataAccess da, string message)
    {
        da.SetData(5, message);
    }

    protected override MatchPayload Compute(MatchSnapshot snap,
        CancellationToken token, Action<string> progress)
    {
        var fragments = snap.Fragments;
        double angleDeg = snap.AngleDeg, minShare = snap.MinShare,
               jointWidth = snap.JointWidth, penTol = snap.PenTol,
               acceptFloor = snap.AcceptFloor;
        bool softIcp = snap.SoftIcp, penHinge = snap.PenHinge;
        var payload = new MatchPayload();

        var report = new System.Text.StringBuilder();
        var rng = new Random(42);
        progress("sanitizing...");

        // KB-12 sanitation pre-pass (2026-07-12): real scan shells arrive
        // with degenerate faces and duplicate vertices; cull and weld before
        // ANY heavier native call. Snapshot meshes are owned copies, safe to
        // mutate. A fragment that resists sanitation is kept as-is with a
        // warning rather than dropped.
        for (int f = 0; f < fragments.Count; f++)
        {
            var mf = fragments[f];
            if (mf == null) continue;
            try
            {
                mf.Faces.CullDegenerateFaces();
                mf.Vertices.CombineIdentical(true, true);
                mf.Compact();
            }
            catch (Exception ex)
            {
                payload.Warnings.Add($"Fragment {f}: sanitation failed ({ex.Message}); using it as-is.");
            }
            if (mf.Faces.Count > 40000)
                payload.Warnings.Add($"Fragment {f} has {mf.Faces.Count} faces (scan-grade). " +
                    "Decimate to ~20k faces for faster, more reliable matching.");
        }
        progress("segmenting...");

        // 1-2. Segment + describe. The dominant-facet guard runs GLOBALLY:
        // per-fragment angle retries segmented the two mates of an
        // interface at DIFFERENT angles, guaranteeing asymmetric facets
        // (found 2026-07-11, N=2 case). One shared angle for everyone.
        var swSeg = System.Diagnostics.Stopwatch.StartNew();
        var facets = new List<Facet>();
        var outlines = new List<Curve>();
        var fragArea = new double[fragments.Count];
        // RoughMode: per-fragment TRUE-SKIN faces (below the second, lower
        // Otsu threshold) -- the crack-line boundary filter needs them
        var trueSkinPer = new Dictionary<int, HashSet<int>>();
        double angleUse = angleDeg;
        for (int attempt = 0; attempt < 2 && angleUse > 12.0 && !snap.RoughMode; attempt++)
        {
            bool dominant = false;
            foreach (var m in fragments)
            {
                if (m == null) continue;
                // lightweight probe: areas only, no descriptors/boundaries
                var probe = SegmentFacets(m, 0, angleUse, new Random(42), describe: false);
                if (probe.Count > 60) { dominant = false; break; }
                double tot = probe.Sum(x => x.Area);
                if (tot > 1e-12 && probe.Max(x => x.Area) > tot * 0.6) { dominant = true; break; }
            }
            if (!dominant) break;
            angleUse = Math.Max(12.0, angleUse * 0.6);
        }
        fragments = fragments.Select(src =>
        {
            // Defensive normal hygiene: scans and generator output arrive
            // with arbitrary winding; the opposition gate needs consistent
            // OUTWARD normals.
            var m2 = src;
            if (m2 == null) return null;
            try
            {
                m2.UnifyNormals();
                if (m2.IsClosed && m2.Volume() < 0) m2.Flip(true, true, true);
            }
            catch { }
            m2.Normals.ComputeNormals();
            return m2;
        }).ToList();
        // INC-3 (2026-07-12): DECIMATE-FOR-SEGMENTATION. The roughness BFS
        // measures a finer physical scale as the mesh densifies, so the
        // fracture region degenerates (and the pass is slow / crash-prone) at
        // high resolution. Segment + boundary-match on a ~100k copy
        // (resolution-invariant, fast, no crash), and keep the FULL-RES mesh
        // for the pass-2 interlock host, where the micro-relief must be full
        // resolution to pin the along-band slide. Only when Interlock Rescue is
        // on and the fragment is dense; otherwise seg == full (pinned).
        var segFragments = fragments;
        if (snap.RoughMode && snap.InterlockRescue)
        {
            int decimated = 0;
            segFragments = fragments.Select(m =>
            {
                if (m == null || m.Faces.Count <= 130000) return m;
                var dec = m.DuplicateMesh();
                try { dec.Reduce(100000, true, 10, false); dec.RebuildNormals(); decimated++; }
                catch { return m; }
                return dec.Faces.Count > 8 ? dec : m;
            }).ToList();
            if (decimated > 0)
                report.AppendLine($"Escalation: segmenting on {decimated} decimated " +
                                  "~100k cop(y/ies); interlock uses full resolution.");
        }
        bool useRegions = snap.Regions != null;
        for (int f = 0; f < fragments.Count; f++)
        {
            token.ThrowIfCancellationRequested();
            var m = segFragments[f];
            if (m == null) continue;
            if (!m.IsClosed)
                payload.Warnings.Add(
                    $"Fragment {f} is not closed; facet matching expects " +
                    "watertight meshes (results may degrade).");
            List<Facet> fs;
            if (useRegions)
            {
                // (regions mode takes precedence over Roughness Mode)
                // EXACT-CORRESPONDENCE mode: one facet per supplied region
                // piece (branch f of the tree = fragment f, one item per
                // interface). Mates carry the identical surface, so
                // descriptors/boundaries correspond by construction -- the
                // re-segmentation asymmetry that broke closed-loop
                // registration (2026-07-11) cannot occur.
                fs = new List<Facet>();
                foreach (var piece in snap.Regions[f])
                {
                    if (piece == null || piece.Faces.Count == 0) continue;
                    piece.Normals.ComputeNormals();
                    var fct = new Facet { FragIdx = f };
                    fct.Faces.AddRange(Enumerable.Range(0, piece.Faces.Count));
                    Describe(piece, fct, rng);
                    BuildBoundary2D(piece, fct);
                    fs.Add(fct);
                }
            }
            else if (snap.RoughMode)
            {
                fs = SegmentFacetsRoughness(m, f, snap.RoughThreshold, angleUse, 0.05,
                    rng, out double thrUsed, out double roughShare,
                    out HashSet<int> skinF);
                trueSkinPer[f] = skinF;
                report.AppendLine($"  fragment {f}: roughness threshold " +
                    $"{thrUsed:F3}{(snap.RoughThreshold <= 0 ? " (auto)" : "")}, " +
                    $"fracture area share {roughShare:F2}, {fs.Count} region(s), " +
                    $"true-skin faces {skinF.Count}.");
            }
            else
            {
                fs = SegmentFacets(m, f, angleUse, rng);
            }
            fragArea[f] = fs.Sum(x => x.Area);
            // keep at most the 10 largest qualifying facets per fragment --
            // candidate pairing is O(facets^2) and small facets rarely carry
            // a whole interface
            foreach (var fc in fs.Where(x => x.Area >= fragArea[f] * minShare)
                                 .OrderByDescending(x => x.Area).Take(10))
            {
                if (snap.RoughMode && trueSkinPer.TryGetValue(f, out var skinSet))
                    fc.CrackRing = BoundaryPointsOf(m, fc, skinSet);
                facets.Add(fc);
                foreach (var b in FacetBoundaries(m, fc)) outlines.Add(b);
            }
        }
        report.AppendLine($"Facets: {facets.Count} kept across {fragments.Count} fragments " +
                          $"(angle {angleDeg:F0} deg, min share {minShare:F2}; " +
                          $"segmentation {swSeg.Elapsed.TotalSeconds:F1} s).");
        report.AppendLine("  per fragment: [" + string.Join(",",
            Enumerable.Range(0, fragments.Count).Select(f => facets.Count(x => x.FragIdx == f))) + "]");

        // 3-5. Greedy assembly from the largest fragment.
        int anchor = Array.IndexOf(fragArea, fragArea.Max());
        var placed = new bool[fragments.Count];
        var pose = new Transform[fragments.Count];
        for (int f = 0; f < fragments.Count; f++) pose[f] = Transform.Identity;
        placed[anchor] = true;
        report.AppendLine($"Anchor: fragment {anchor} (largest area).");

        int placedCount = 1;
        // cache: placed fragments' meshes transformed to their current pose
        // (rebuilt only when a placement happens -- the per-candidate
        // DuplicateMesh in the penetration gate was the 300s hotspot)
        var placedWorld = new Mesh[fragments.Count];
        void RefreshPlaced(int f)
        {
            var w = fragments[f]?.DuplicateMesh();
            if (w != null) { w.Transform(pose[f]); }
            placedWorld[f] = w;
        }
        RefreshPlaced(anchor);
        bool progressReported = false;
        var gateLog = new List<string>();
        bool moved = true;
        var swGreedy = System.Diagnostics.Stopwatch.StartNew();
        // Resolution-escalation (INC-2, 2026-07-12): pass 0 = pinned matching;
        // pass 1 = interlock RESCUE over whatever pass 0 left unplaced (only
        // when Interlock Rescue is on). pose/placed/placedWorld persist across
        // passes, so the rescue mates against the pass-0 placements. Rescue off
        // runs one pass = pinned behaviour exactly.
        bool rescuePass = false;
        int passCount = (snap.RoughMode && snap.InterlockRescue) ? 2 : 1;
        for (int escPass = 0; escPass < passCount; escPass++)
        {
        rescuePass = escPass == 1;
        if (rescuePass && placedCount >= fragments.Count) break;
        moved = true;
        while (moved)
        {
            moved = false;
            token.ThrowIfCancellationRequested();
            progress($"matching... {placedCount}/{fragments.Count} placed");
            // CHEAP scan first (residual + normal opposition only); the
            // expensive per-vertex inside-mesh penetration test runs only
            // on the ranked survivors (this was the 300s hotspot).
            var ranked = new List<(double resid, int frag, int hostFacet, Transform t, double penEff)>();
            var diag = new List<(double mult, int cand, int host)>();
            var prelim = new List<(double coarseMult, Facet cand, int hostIdx, Transform t,
                                   Point3d[] hostSamples, Vector3d[] hostNormals, double spacing,
                                   int bShiftIdx, bool bRev, Point3d[] host3World, double bFloor)>();
            foreach (var cand in facets)
            {
                if (placed[cand.FragIdx]) continue;
                for (int hostIdx = 0; hostIdx < facets.Count; hostIdx++)
                {
                    var host = facets[hostIdx];
                    if (!placed[host.FragIdx] || host.FragIdx == cand.FragIdx) continue;
                    double aRatio = Math.Min(cand.Area, host.Area) / Math.Max(cand.Area, host.Area);
                    // ROUGHNESS MODE relaxes the congruence pre-gates: real
                    // mates are PARTIAL overlaps (measured FB 00002: mug
                    // break band bleeds into its rough interior, 2310 area
                    // vs the chip's 1027) -- extents never agree, so equal-
                    // extent gates would hold out every true pair.
                    // RoughMode floor is nearly open: rough-region EXTENTS
                    // are meaningless when the host side bleeds (measured
                    // 22k-area blob vs 1.3k chip at 100k faces); the
                    // boundary metrics gate instead
                    if (aRatio < (snap.RoughMode ? 0.04 : 0.55)) continue;
                    if (!snap.RoughMode)
                    {
                        double e1Ratio = Math.Min(cand.E1, host.E1) / Math.Max(cand.E1, host.E1);
                        if (e1Ratio < 0.6) continue;
                        double e2Ratio = Math.Min(cand.E2, host.E2) / Math.Max(cand.E2, host.E2);
                        if (e2Ratio < 0.5) continue;
                        // mating facets share the same relief -> near-equal RMS
                        double rMax = Math.Max(cand.Rms, host.Rms);
                        if (rMax > 1e-9 &&
                            Math.Abs(cand.Rms - host.Rms) / rMax > 0.5) continue;
                    }
                    // FLAT-PAIR EXCLUSION (2026-07-11): opposing-normal mating
                    // is only meaningful between FRACTURE facets. Two flat
                    // facets (skin/sawn faces, relief < 0.2% of extent) are
                    // congruent across fragments of one solid and score
                    // BETTER than true pairs while placing the fragment on
                    // the wrong side of the plane. Measured real granite
                    // fracture facets carry 0.45% relief -- safely above.
                    if (cand.Rms < 0.002 * cand.E1 && host.Rms < 0.002 * host.E1)
                        continue;

                    var hostFrame = host.Frame;
                    hostFrame.Transform(pose[host.FragIdx]);
                    var hostSamples = TransformPoints(host.Samples, pose[host.FragIdx]);
                    var hostNormals = TransformVectors(host.SampleNormals, pose[host.FragIdx]);

                    // The residual FLOOR is the facet sampling density: two
                    // independent samplings of the SAME surface sit
                    // ~sqrt(area/K) apart even at a perfect pose. Acceptance
                    // scales with that spacing, not just Joint Width.
                    double spacing = Math.Sqrt(Math.Max(cand.Area, host.Area) /
                                               Math.Max(1, host.Samples.Length));
                    double thr = Math.Max(jointWidth, 1.6 * spacing);

                    if (snap.RoughMode)
                    {
                        // TRIMMED-ICP lane (2026-07-12, partial-overlap real
                        // scans; trimmed-ICP/contact-band direction from the
                        // research pack RQ1/RQ3). Boundary registration
                        // assumes identical loops and cannot pose partial
                        // mates; instead: seed poses by mating the mirrored
                        // candidate frame at 12 in-plane spins, refine the
                        // best seeds with TRIMMED ICP (only the closest 60%
                        // of sample pairs vote), score by trimmed RMS +
                        // coverage + normal opposition on the trimmed band.
                        double candSpacing = Math.Sqrt(cand.Area /
                            Math.Max(1, cand.Samples.Length));
                        var hostWorld = placedWorld[host.FragIdx];
                        if (hostWorld == null) continue;
                        // correspondences go to the FULL host mesh: the host
                        // facet region is unreliable on real scans (patchy
                        // band coverage resolved through the wall to the
                        // interior). The opposition filter inside the ICP
                        // prevents glue-to-skin solutions; the BREAK-CURVE
                        // pairs pin the in-plane pose the surface statistics
                        // cannot (band-slide degeneracy, FB 00002).
                        // the fragment's break face is its DOMINANT fracture
                        // region: a small bogus facet can out-score the true
                        // seat by hugging tightly with tiny support (measured
                        // FB 00002: an impostor pairing won at rms 0.08x /
                        // brms 0.93x via a minor chip facet)
                        double candMaxArea = facets
                            .Where(x => x.FragIdx == cand.FragIdx)
                            .Max(x => x.Area);
                        if (cand.Area < 0.3 * candMaxArea)
                        {
                            gateLog.Add($"frag {cand.FragIdx}->{host.FragIdx}: TRIM reject minor facet ({cand.Area:F0} vs {candMaxArea:F0})");
                            continue;
                        }
                        var candB = cand.CrackRing ?? new Point3d[0];
                        var hostB = host.CrackRing == null
                            ? new Point3d[0]
                            : TransformPoints(host.CrackRing, pose[host.FragIdx]);
                        if (candB.Length < 12 || hostB.Length < 12)
                        {
                            gateLog.Add($"frag {cand.FragIdx}->{host.FragIdx}: TRIM reject no crack ring ({candB.Length}/{hostB.Length} pts)");
                            continue;
                        }
                        var Tt = TrimmedRegister(cand, hostWorld, hostFrame,
                            candSpacing, candB, hostB,
                            out double tRms, out double cov, out double tDot,
                            out double bRms, out double bCov);
                        double tMult = tRms / Math.Max(1e-9, candSpacing);
                        double bMult = bRms / Math.Max(1e-9, candSpacing);
                        diag.Add((bMult, cand.FragIdx, host.FragIdx));
                        // Pass 0 keeps the pinned bCov 0.35 (complete-rim
                        // precision). Pass 1 (rescue) drops it to a near-empty
                        // guard 0.1: a partial rim carries little boundary, and
                        // the interlock residual below is the precision
                        // mechanism there, not boundary coverage.
                        if (cov < 0.45 || bCov < (rescuePass ? 0.1 : 0.35))
                        {
                            gateLog.Add($"frag {cand.FragIdx}->{host.FragIdx}: TRIM reject cov {cov:F2}/bcov {bCov:F2} (rms {tMult:F2}x brms {bMult:F2}x)");
                            continue;
                        }
                        if ((tRms >= acceptFloor * candSpacing ||
                             bRms >= 2.0 * acceptFloor * candSpacing) && tRms >= jointWidth)
                        {
                            gateLog.Add($"frag {cand.FragIdx}->{host.FragIdx}: TRIM reject rms {tMult:F2}x brms {bMult:F2}x (cov {cov:F2})");
                            continue;
                        }
                        // tDot = OPPOSING FRACTION of covered samples (a true
                        // seat mixes band and continuous skin, so ~0.2-0.5)
                        if (tDot < 0.2)
                        {
                            gateLog.Add($"frag {cand.FragIdx}->{host.FragIdx}: TRIM reject oppFrac {tDot:F2} (rms {tMult:F2}x brms {bMult:F2}x)");
                            continue;
                        }
                        if (rescuePass)
                        {
                            // PASS 2 INTERLOCK RESCUE: pin the along-band slide
                            // by the dense signed-distance-std minimum and gate
                            // on that residual (the physical micro-relief fit).
                            // A non-mate has no interlock minimum and stays
                            // high, so debris still rejects. Needs a high-res
                            // host to bite; on a coarse host the std floor is
                            // high and this simply rejects (safe).
                            // candidate facet supplies the dense sample origins
                            // (decimated is fine); the HOST fracture region is
                            // remapped to FULL resolution so the interlock reads
                            // the true micro-relief (INC-3).
                            var candFracSub = SubMesh(segFragments[cand.FragIdx], cand.Faces);
                            var hostDec = SubMesh(segFragments[host.FragIdx], host.Faces);
                            var hostFracSub = MapFacetToFullRes(fragments[host.FragIdx], hostDec);
                            if (hostFracSub != null) hostFracSub.Transform(pose[host.FragIdx]);
                            var Ti = InterlockRefine(candFracSub, hostFracSub, Tt,
                                candSpacing, out double interStd, out double slideOff);
                            double interMult = interStd / Math.Max(1e-9, candSpacing);
                            if (interStd >= 0.9 * candSpacing)
                            {
                                gateLog.Add($"frag {cand.FragIdx}->{host.FragIdx}: RESCUE reject interlock {interMult:F2}x slide {slideOff:F1} (rms {tMult:F2}x)");
                                continue;
                            }
                            gateLog.Add($"frag {cand.FragIdx}->{host.FragIdx}: RESCUE RANKED interlock {interMult:F2}x slide {slideOff:F1} rms {tMult:F2}x bcov {bCov:F2}");
                            // rank by the INTERLOCK residual; generous penetration
                            // tol (the interlock already verified a tight fit).
                            ranked.Add((interMult, cand.FragIdx, hostIdx, Ti,
                                        Math.Max(penTol, 4.0 * candSpacing)));
                            continue;
                        }
                        gateLog.Add($"frag {cand.FragIdx}->{host.FragIdx}: TRIM RANKED brms {bMult:F2}x rms {tMult:F2}x cov {cov:F2} bcov {bCov:F2} oppFrac {tDot:F2}");
                        ranked.Add((tMult * bMult, cand.FragIdx, hostIdx, Tt,
                                    Math.Max(penTol, 3.0 * tRms)));
                        continue;
                    }

                    // BOUNDARY REGISTRATION (2026-07-11): mates carry the
                    // IDENTICAL facet boundary loop, so registering the
                    // candidate's mirrored 2D outline against the host's
                    // yields the true in-plane rotation + offset directly.
                    // The old 2/4-spin guesses started the mini-ICP >45 deg
                    // off and it locked onto self-similar relief at wrong
                    // offsets (true pair placed 57 units off, verifier
                    // rightly rejected it). Boundary misfit also separates
                    // false pairs whose outlines differ.
                    if (cand.Boundary2D == null || host.Boundary2D == null) continue;
                    double bLenRatio = Math.Min(cand.BoundaryLength, host.BoundaryLength) /
                                       Math.Max(cand.BoundaryLength, host.BoundaryLength);
                    if (bLenRatio < 0.7) continue;
                    if (!RegisterBoundaries(cand.Boundary2D, host.Boundary2D,
                                            cand.BoundaryRelief, host.BoundaryRelief,
                                            0.5 * (cand.Rms + host.Rms),
                                            out double bAng, out Vector2d bShift,
                                            out double bMisfit, out int bShiftIdx,
                                            out bool bRev)) continue;
                    // boundary misfit gate: identical loops register to the
                    // resampling floor (~perimeter/48); different outlines
                    // stay far above it
                    double bFloor = host.BoundaryLength / BoundarySamples;
                    if (bMisfit > 2.5 * bFloor) continue;

                    {
                        // build the 3D mating transform from the registered
                        // 2D pose: mirrored candidate frame -> host frame
                        // rotated by bAng, origin offset by bShift.
                        var mirroredCand = new Plane(cand.Frame.Origin,
                            cand.Frame.XAxis, -cand.Frame.YAxis);
                        var tx = hostFrame.XAxis * Math.Cos(bAng) + hostFrame.YAxis * Math.Sin(bAng);
                        var ty = -hostFrame.XAxis * Math.Sin(bAng) + hostFrame.YAxis * Math.Cos(bAng);
                        var origin = hostFrame.Origin + hostFrame.XAxis * bShift.X
                                                      + hostFrame.YAxis * bShift.Y;
                        var target = new Plane(origin, tx, ty);
                        var T = Transform.PlaneToPlane(mirroredCand, target);

                        double coarse = SampleRms(cand.Samples, T, hostSamples);
                        if (coarse >= 5.0 * spacing && coarse >= jointWidth) continue;
                        var host3World = TransformPoints(host.Boundary3D, pose[host.FragIdx]);
                        prelim.Add(((coarse / Math.Max(1e-9, spacing)) + bMisfit / bFloor * 0.1,
                                    cand, hostIdx, T, hostSamples, hostNormals, spacing,
                                    bShiftIdx, bRev, host3World, bFloor));
                    }
                }
            }

            // Phase 2: refine-then-score ONLY the best prelim candidates.
            foreach (var pc in prelim.OrderBy(p => p.coarseMult).Take(24))
            {
                // Refine on the LOCKED boundary correspondence (closed-form
                // Kabsch on fixed pairs). The earlier free-NN mini-ICP slid
                // the registered pose along the interface plane (in-plane
                // slide is invisible to NN RMS) -- corresponded refinement
                // cannot slide by construction.
                var T = BoundaryKabsch(pc.cand.Boundary3D, pc.t,
                                       pc.host3World, pc.bShiftIdx, pc.bRev, 3);
                // pose-pinning score: CORRESPONDED boundary distance
                double bResid = BoundaryResid3D(pc.cand.Boundary3D, T,
                                                pc.host3World, pc.bShiftIdx, pc.bRev);
                double resid = Math.Max(SampleRms(pc.cand.Samples, T, pc.hostSamples),
                                        bResid * 0.5);
                double floorMult = Math.Max(
                    resid / Math.Max(1e-9, pc.spacing),
                    bResid / Math.Max(1e-9, pc.bFloor));
                diag.Add((floorMult, pc.cand.FragIdx, facets[pc.hostIdx].FragIdx));
                if (floorMult >= acceptFloor && resid >= jointWidth)
                {
                    gateLog.Add($"frag {pc.cand.FragIdx}->{facets[pc.hostIdx].FragIdx}: REJECT floor {floorMult:F2}");
                    continue;
                }
                double dot = MeanNormalDot(pc.cand.Samples, pc.cand.SampleNormals, T,
                                           pc.hostSamples, pc.hostNormals, pc.spacing * 3);
                if (dot > -0.3)
                {
                    gateLog.Add($"frag {pc.cand.FragIdx}->{facets[pc.hostIdx].FragIdx}: REJECT normal dot {dot:F2} (floor {floorMult:F2})");
                    continue;
                }
                gateLog.Add($"frag {pc.cand.FragIdx}->{facets[pc.hostIdx].FragIdx}: RANKED floor {floorMult:F2} dot {dot:F2}");
                // Effective penetration tolerance scales with the matched
                // residual: mating rough surfaces legitimately interpenetrate
                // by ~the registration residual (the true pair was rejected
                // at the fixed 0.5 tol, found 2026-07-11). Wrong poses
                // overlap at fragment scale, far above 3x resid.
                double penEff = Math.Max(penTol, 3.0 * resid);
                ranked.Add((floorMult, pc.cand.FragIdx, pc.hostIdx, T, penEff));
            }
            // candidate diagnostics: the measured floor-multiples, best first
            if (!progressReported && diag.Count > 0)
            {
                progressReported = true;
                report.AppendLine("  candidate floor-multiples (best 12 of " + diag.Count + "):");
                foreach (var d2 in diag.OrderBy(x => x.mult).Take(12))
                    report.AppendLine($"    frag {d2.cand} -> {d2.host}: {d2.mult:F2}");
            }
            // CONFLICT-AWARE GLOBAL SELECTION (2026-07-11): choose at most
            // one candidate per fragment such that no two claim the SAME
            // host facet (impostor placements pile onto the same big host
            // plane and eliminate each other), maximizing placements and
            // then minimizing total floor-multiple. Candidate lists are
            // tiny (< ~30), exhaustive DFS is free.
            var chosen = SelectNonConflicting(ranked);
            foreach (var (resid, frag, _, T, penEff) in chosen.OrderBy(r => r.resid))
            {
                if (placed[frag]) continue;
                if (penTol > 0 && Penetrates(fragments, placedWorld, placed, frag, T, penEff))
                {
                    gateLog.Add($"frag {frag}: REJECT penetration (eff tol {penEff:F2})");
                    continue;
                }
                pose[frag] = T;
                placed[frag] = true;
                RefreshPlaced(frag);
                placedCount++;
                moved = true;
                report.AppendLine($"  placed fragment {frag}: facet RMS-multiple {resid:F3}");
            }
        }
        } // end escalation pass loop (INC-2)
        report.AppendLine($"Greedy assembly: {swGreedy.Elapsed.TotalSeconds:F1} s.");
        if (gateLog.Count > 0)
        {
            report.AppendLine("  gate log (first 16):");
            foreach (var g in gateLog.Take(16)) report.AppendLine("    " + g);
        }

        // 6. Soft ICP polish. SKIPPED in regions mode: exact-correspondence
        // placements are corresponded-boundary Kabsch poses (residual well
        // under the sampling floor) and the contact-only polish measurably
        // dragged CORRECT poses 13-27% of diag away (N=3/5 sweep,
        // 2026-07-12). The polish stays available for the scan path, where
        // segmentation noise leaves real slack to recover.
        if (softIcp && useRegions)
            report.AppendLine("Soft ICP skipped: exact-correspondence regions " +
                              "already lock the greedy poses.");
        // INC-2: also skip in interlock-rescue mode. The rescue slid each
        // placement to the fracture-relief minimum; the contact-only polish
        // then dragged a correct FB 00003 seat to 58% error (measured).
        if (softIcp && !useRegions && snap.InterlockRescue)
            report.AppendLine("Soft ICP skipped: interlock rescue already locks the poses.");
        if (softIcp && !useRegions && !snap.InterlockRescue && placedCount >= 2)
        {
            try
            {
                progress("soft ICP polish...");
                var swIcp = System.Diagnostics.Stopwatch.StartNew();
                var refFrags = new List<SoftIcpRefiner.Fragment>();
                for (int f = 0; f < fragments.Count; f++)
                {
                    if (!placed[f]) continue;
                    var samples = facets.Where(x => x.FragIdx == f)
                        .SelectMany(x => TransformPoints(x.Samples, pose[f])).ToArray();
                    // FAST mode (default): no Solid -> pure contact CPD, no
                    // per-sample inside-mesh queries. Coarse pose is already
                    // global-aligned by the facet frames; the polish is local.
                    Mesh solid = null;
                    if (penHinge)
                    {
                        solid = fragments[f].DuplicateMesh();
                        solid.Transform(pose[f]);
                    }
                    refFrags.Add(new SoftIcpRefiner.Fragment($"f{f:D3}", samples, solid,
                        anchored: f == anchor));
                }
                var rep = SoftIcpRefiner.Refine3D(refFrags, new SoftIcpOptions());
                report.AppendLine($"Soft ICP ({(penHinge ? "penetration hinge" : "fast contact-only")}): {swIcp.Elapsed.TotalSeconds:F1} s.");
                int k = 0;
                for (int f = 0; f < fragments.Count; f++)
                {
                    if (!placed[f]) continue;
                    pose[f] = Transform.Multiply(refFrags[k].Delta, pose[f]);
                    k++;
                }
                report.AppendLine($"Soft ICP: mean rim gap {rep.MeanRimGap:F3}, " +
                                  $"max penetration {rep.MaxPenetration:F3}, " +
                                  $"{rep.ContactSamples} contact samples, {rep.Iterations} iterations.");
            }
            catch (Exception ex)
            {
                payload.Warnings.Add(
                    "Soft ICP polish failed: " + ex.Message + " (greedy poses kept).");
            }
        }

        // emit
        var assembled = new List<Mesh>(fragments.Count);
        var transforms = new List<Transform>(fragments.Count);
        var placedIdx = new List<int>();
        var unplacedIdx = new List<int>();
        for (int f = 0; f < fragments.Count; f++)
        {
            var m = fragments[f]?.DuplicateMesh();
            if (m != null && placed[f]) m.Transform(pose[f]);
            assembled.Add(m);
            transforms.Add(placed[f] ? pose[f] : Transform.Identity);
            if (placed[f]) placedIdx.Add(f); else unplacedIdx.Add(f);
        }
        report.AppendLine($"FacetMatch: {placedIdx.Count}/{fragments.Count} placed" +
                          (unplacedIdx.Count > 0 ? $"; unplaced: {string.Join(",", unplacedIdx)}" : "."));

        payload.Assembled = assembled;
        payload.Transforms = transforms;
        payload.PlacedIdx = placedIdx;
        payload.UnplacedIdx = unplacedIdx;
        payload.Outlines = outlines;
        payload.Report = report.ToString();
        return payload;
    }

    // -------------------------------------------------------------------------
    // Segmentation.
    // -------------------------------------------------------------------------

    private static List<Facet> SegmentFacets(Mesh m, int fragIdx, double angleDeg, Random rng,
                                             bool describe = true)
    {
        m.FaceNormals.ComputeFaceNormals();
        int fc = m.Faces.Count;
        double cosTol = Math.Cos(angleDeg * Math.PI / 180.0);
        var te = m.TopologyEdges;

        // face adjacency via topology edges
        var adj = new List<int>[fc];
        for (int i = 0; i < fc; i++) adj[i] = new List<int>(3);
        for (int e = 0; e < te.Count; e++)
        {
            var faces = te.GetConnectedFaces(e);
            for (int a = 0; a < faces.Length; a++)
                for (int b = a + 1; b < faces.Length; b++)
                { adj[faces[a]].Add(faces[b]); adj[faces[b]].Add(faces[a]); }
        }

        // LOW-PASS the face normals before region growing (segmentation-
        // symmetry fix, 2026-07-11): the two sides of a rough interface
        // sample the SAME displaced surface with independent triangulations;
        // raw-normal growing split the sides differently and true facet
        // pairs died at the descriptor gates. Three averaging passes
        // converge both sides to the shared low-frequency geometry.
        var normals = new Vector3d[fc];
        for (int i = 0; i < fc; i++) normals[i] = (Vector3d)m.FaceNormals[i];
        for (int pass = 0; pass < 3; pass++)
        {
            var next = new Vector3d[fc];
            for (int i = 0; i < fc; i++)
            {
                var acc = normals[i];
                foreach (var g in adj[i]) acc += normals[g];
                if (acc.Length > 1e-12) acc.Unitize();
                next[i] = acc;
            }
            normals = next;
        }

        // region-grow on the smoothed normals
        var facetId = new int[fc];
        for (int i = 0; i < fc; i++) facetId[i] = -1;
        int nFacets = 0;
        for (int seed = 0; seed < fc; seed++)
        {
            if (facetId[seed] >= 0) continue;
            int id = nFacets++;
            var queue = new Queue<int>();
            queue.Enqueue(seed);
            facetId[seed] = id;
            while (queue.Count > 0)
            {
                int f = queue.Dequeue();
                foreach (var g in adj[f])
                {
                    if (facetId[g] >= 0) continue;
                    // LOCAL crease criterion (2026-07-11, from Libish's
                    // canvas evidence): comparing against the REGION MEAN
                    // made wavy fracture surfaces drift past the threshold
                    // and shatter into ragged patches (over-segmentation,
                    // asymmetric across mates). The local dihedral between
                    // adjacent smoothed normals is drift-free: wavy surfaces
                    // stay one facet, sharp interface corners still split.
                    if (normals[f] * normals[g] < cosTol) continue;
                    facetId[g] = id;
                    queue.Enqueue(g);
                }
            }
        }

        // CONSOLIDATION: merge adjacent facets whose mean smoothed normals
        // are near-parallel (75% of the grow tolerance). Fixes the residual
        // asymmetry where one side reads as one facet and the other splits.
        var parent = Enumerable.Range(0, nFacets).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        double cosMerge = Math.Cos(angleDeg * 0.75 * Math.PI / 180.0);
        for (int iter = 0; iter < 2; iter++)
        {
            var accN = new Vector3d[nFacets];
            for (int i = 0; i < fc; i++) accN[Find(facetId[i])] += normals[i];
            for (int i = 0; i < nFacets; i++) if (accN[i].Length > 1e-12) accN[i].Unitize();
            bool merged = false;
            for (int e = 0; e < te.Count; e++)
            {
                var faces = te.GetConnectedFaces(e);
                if (faces.Length != 2) continue;
                int ra = Find(facetId[faces[0]]), rb = Find(facetId[faces[1]]);
                if (ra == rb) continue;
                if (accN[ra] * accN[rb] >= cosMerge) { parent[rb] = ra; merged = true; }
            }
            if (!merged) break;
        }

        var byRoot = new Dictionary<int, Facet>();
        for (int i = 0; i < fc; i++)
        {
            int r = Find(facetId[i]);
            if (!byRoot.TryGetValue(r, out var fct))
                byRoot[r] = fct = new Facet { FragIdx = fragIdx };
            fct.Faces.Add(i);
        }
        var result = new List<Facet>();
        foreach (var fct in byRoot.Values)
        {
            // cheap triangle-area sum for EVERY facet (fragment totals and
            // ranking need it; full descriptors do not)
            double area = 0;
            foreach (var fi2 in fct.Faces)
            {
                var face = m.Faces[fi2];
                Point3d a = m.Vertices[face.A], b = m.Vertices[face.B], c = m.Vertices[face.C];
                area += 0.5 * Vector3d.CrossProduct(b - a, c - a).Length;
                if (face.IsQuad)
                {
                    Point3d d2 = m.Vertices[face.D];
                    area += 0.5 * Vector3d.CrossProduct(c - a, d2 - a).Length;
                }
            }
            fct.Area = area;
            result.Add(fct);
        }
        if (describe)
        {
            // KB-12 (2026-07-12): describe ONLY the facets that can matter.
            // Scan shells segment into THOUSANDS of micro-facets; running
            // Describe + BuildBoundary2D on all of them exploded time and
            // memory until the Rhino process died. Downstream keeps at most
            // 10 facets per fragment, so 24 described candidates is ample.
            // Undescribed facets keep Boundary2D == null and the candidate
            // scan skips them.
            foreach (var fct in result.OrderByDescending(x => x.Area).Take(24))
            {
                Describe(m, fct, rng);
                BuildBoundary2D(m, fct);
            }
        }
        return result;
    }

    /// <summary>
    /// ROUGHNESS-BASED fracture/skin segmentation (2026-07-12; GRAVITATE /
    /// ElNaghy-Dorst direction from the one-sided-scan research pack).
    /// Dihedral segmentation fails on REAL scans of smooth objects: the
    /// fracture facet is a shallow rough patch, not a crease, so region
    /// growing lumps it into the skin. Here every face gets a ROUGHNESS
    /// score (normal dispersion within a scale-relative radius), Otsu (or a
    /// user threshold) splits fracture from skin, and connected fracture
    /// components become the facets. Downstream descriptors and gates are
    /// unchanged.
    /// </summary>
    private static List<Facet> SegmentFacetsRoughness(Mesh m, int fragIdx,
        double userThreshold, Random rng, out double thresholdUsed,
        out double fractureAreaShare)
        => SegmentFacetsRoughness(m, fragIdx, userThreshold, 30.0, rng,
            out thresholdUsed, out fractureAreaShare);

    private static List<Facet> SegmentFacetsRoughness(Mesh m, int fragIdx,
        double userThreshold, double angleDeg, Random rng,
        out double thresholdUsed, out double fractureAreaShare)
        // 0.02 is the MEASURED radius factor (FB 00002 sweep: chip fracture
        // region jaccard 0.65 rank#0 at 0.02; 0.05 fattened the host blob)
        => SegmentFacetsRoughness(m, fragIdx, userThreshold, angleDeg, 0.02, rng,
            out thresholdUsed, out fractureAreaShare, out _);

    private static List<Facet> SegmentFacetsRoughness(Mesh m, int fragIdx,
        double userThreshold, double angleDeg, double radiusFactor, Random rng,
        out double thresholdUsed, out double fractureAreaShare)
        => SegmentFacetsRoughness(m, fragIdx, userThreshold, angleDeg, radiusFactor,
            rng, out thresholdUsed, out fractureAreaShare, out _);

    private static List<Facet> SegmentFacetsRoughness(Mesh m, int fragIdx,
        double userThreshold, double angleDeg, double radiusFactor, Random rng,
        out double thresholdUsed, out double fractureAreaShare,
        out HashSet<int> trueSkin)
    {
        m.FaceNormals.ComputeFaceNormals();
        int fc = m.Faces.Count;
        var te = m.TopologyEdges;
        var adj = new List<int>[fc];
        for (int i = 0; i < fc; i++) adj[i] = new List<int>(3);
        for (int e = 0; e < te.Count; e++)
        {
            var faces = te.GetConnectedFaces(e);
            for (int a = 0; a < faces.Length; a++)
                for (int b = a + 1; b < faces.Length; b++)
                { adj[faces[a]].Add(faces[b]); adj[faces[b]].Add(faces[a]); }
        }
        var centroids = new Point3d[fc];
        var normals = new Vector3d[fc];
        var areas = new double[fc];
        for (int i = 0; i < fc; i++)
        {
            var face = m.Faces[i];
            Point3d a = m.Vertices[face.A], b = m.Vertices[face.B], c = m.Vertices[face.C];
            centroids[i] = face.IsQuad
                ? (a + b + c + (Point3d)m.Vertices[face.D]) / 4.0
                : (a + b + c) / 3.0;
            normals[i] = (Vector3d)m.FaceNormals[i];
            areas[i] = 0.5 * Vector3d.CrossProduct(b - a, c - a).Length;
            if (face.IsQuad)
                areas[i] += 0.5 * Vector3d.CrossProduct(
                    c - a, (Point3d)m.Vertices[face.D] - a).Length;
        }

        // per-face roughness = area-weighted normal dispersion within a
        // SCALE-RELATIVE radius, gathered by BFS over face adjacency.
        // 0 = perfectly smooth, 1 = isotropic normals. Radius derives from
        // TOTAL MESH AREA, not the bbox diagonal: the bbox changes with
        // pose, and a scrambled fragment segmented differently from its
        // unscrambled self (measured FB 00002: chip top region 1027 -> 154
        // after a rigid scramble).
        double totalMeshArea = areas.Sum();
        double radius = radiusFactor * Math.Sqrt(Math.Max(1e-12, totalMeshArea));
        double r2 = radius * radius;
        var rough = new double[fc];
        var stamp = new int[fc];
        var bfs = new Queue<int>();
        for (int i = 0; i < fc; i++)
        {
            bfs.Clear();
            bfs.Enqueue(i);
            stamp[i] = i + 1;
            var acc = Vector3d.Zero;
            double wSum = 0;
            int visited = 0;
            while (bfs.Count > 0 && visited < 80)
            {
                int f2 = bfs.Dequeue();
                visited++;
                var n2 = normals[f2];
                if (n2.Length > 1e-12) n2.Unitize();
                acc += n2 * areas[f2];
                wSum += areas[f2];
                foreach (var g in adj[f2])
                {
                    if (stamp[g] == i + 1) continue;
                    if ((centroids[g] - centroids[i]).SquareLength > r2) continue;
                    stamp[g] = i + 1;
                    bfs.Enqueue(g);
                }
            }
            rough[i] = wSum > 1e-12 ? 1.0 - acc.Length / wSum : 0.0;
        }
        // one cyclic-free majority-style smoothing pass (mean over ring 1)
        var sm = new double[fc];
        for (int i = 0; i < fc; i++)
        {
            double s = rough[i]; int n = 1;
            foreach (var g in adj[i]) { s += rough[g]; n++; }
            sm[i] = s / n;
        }
        rough = sm;

        // threshold: user value, or area-weighted Otsu on a 64-bin
        // histogram over an arbitrary face subset (reused below for the
        // second, TRUE-SKIN threshold)
        double OtsuOver(Func<int, bool> include)
        {
            const int Bins = 64;
            double rMax = 1e-9;
            for (int i = 0; i < fc; i++)
                if (include(i) && rough[i] > rMax) rMax = rough[i];
            var hist = new double[Bins];
            for (int i = 0; i < fc; i++)
            {
                if (!include(i)) continue;
                int bin = Math.Min(Bins - 1, (int)(rough[i] / rMax * (Bins - 1)));
                hist[bin] += areas[i];
            }
            double total = hist.Sum(), sumAll = 0;
            for (int b = 0; b < Bins; b++) sumAll += b * hist[b];
            double wB = 0, sumB = 0, bestVar = -1; int bestBin = Bins / 2;
            for (int b = 0; b < Bins; b++)
            {
                wB += hist[b];
                if (wB <= 0) continue;
                double wF = total - wB;
                if (wF <= 0) break;
                sumB += b * hist[b];
                double mB = sumB / wB, mF = (sumAll - sumB) / wF;
                double v = wB * wF * (mB - mF) * (mB - mF);
                if (v > bestVar) { bestVar = v; bestBin = b; }
            }
            return (bestBin + 0.5) / (Bins - 1) * rMax;
        }
        thresholdUsed = userThreshold > 0 ? userThreshold : OtsuOver(i => true);

        // fracture class + components (rebuilt below if escalation fires)
        double totArea = areas.Sum();
        var isFracture = new bool[fc];
        var facetId = new int[fc];
        int nFacets = 0;
        var grow = new Queue<int>();
        double BuildComponents(double thr)   // returns largest component area
        {
            for (int i = 0; i < fc; i++)
            {
                isFracture[i] = rough[i] >= thr;
                facetId[i] = -1;
            }
            nFacets = 0;
            double largest = 0;
            for (int seed = 0; seed < fc; seed++)
            {
                if (!isFracture[seed] || facetId[seed] >= 0) continue;
                int id = nFacets++;
                grow.Clear();
                grow.Enqueue(seed);
                facetId[seed] = id;
                double compArea = areas[seed];
                while (grow.Count > 0)
                {
                    int f2 = grow.Dequeue();
                    foreach (var g in adj[f2])
                    {
                        if (!isFracture[g] || facetId[g] >= 0) continue;
                        facetId[g] = id;
                        compArea += areas[g];
                        grow.Enqueue(g);
                    }
                }
                if (compArea > largest) largest = compArea;
            }
            return largest;
        }
        double largestArea = BuildComponents(thresholdUsed);

        // ESCALATING Otsu (auto mode only): a fracture facet spanning over
        // 30% of the whole fragment is bleed, not a break face (measured FB
        // 00002 mug at 100k: auto 0.135 -> one 22k-area blob = band + matte
        // interior; re-Otsu WITHIN the fracture class lands ~0.20 and the
        // band separates). True fracture faces (the chip: ~10% share) never
        // trigger this.
        if (userThreshold <= 0)
        {
            for (int esc = 0; esc < 3 && largestArea > 0.30 * totArea; esc++)
            {
                double thrCur = thresholdUsed;
                double t2 = OtsuOver(i => rough[i] >= thrCur);
                if (t2 <= thresholdUsed * 1.05) break;
                thresholdUsed = t2;
                largestArea = BuildComponents(thresholdUsed);
            }
        }
        double fracArea = 0;
        for (int i = 0; i < fc; i++) if (isFracture[i]) fracArea += areas[i];
        fractureAreaShare = totArea > 1e-12 ? fracArea / totArea : 0;

        // TRUE SKIN = a second, lower Otsu over the non-fracture faces:
        // separates genuinely smooth skin (glaze, saw cut) from the
        // moderately rough rest (matte interior, weathering). The physical
        // CRACK LINE is the facet boundary bordering TRUE skin; boundaries
        // bordering merely non-fracture faces are bleed edges (label-based
        // filtering is vacuous by construction, measured 2026-07-12).
        double lowThr = thresholdUsed;
        double capped = thresholdUsed;
        {
            double t2 = OtsuOver(i => rough[i] < capped);
            if (t2 > 1e-9 && t2 < thresholdUsed) lowThr = t2;
        }
        trueSkin = new HashSet<int>();
        for (int i = 0; i < fc; i++)
            if (rough[i] < lowThr) trueSkin.Add(i);

        // Components already built by BuildComponents above (pure roughness
        // growth: NO per-edge normal-coherence constraint -- tried twice,
        // removed twice, resolution-dependent shredding).
        var byId = new Dictionary<int, Facet>();
        for (int i = 0; i < fc; i++)
        {
            if (facetId[i] < 0) continue;
            if (!byId.TryGetValue(facetId[i], out var fct))
                byId[facetId[i]] = fct = new Facet { FragIdx = fragIdx };
            fct.Faces.Add(i);
            fct.Area += areas[i];
        }
        var result = byId.Values.ToList();
        // same KB-12 guard as the dihedral path: describe only the facets
        // that can matter
        foreach (var fct in result.OrderByDescending(x => x.Area).Take(24))
        {
            Describe(m, fct, rng);
            BuildBoundary2D(m, fct);
        }
        return result;
    }

    /// <summary>
    /// Longest closed boundary loop of the facet, resampled to 48 points in
    /// the facet-frame (u,v). Mates carry the SAME boundary curve, so 2D
    /// registration of these loops recovers the true mating rotation.
    /// </summary>
    private const int BoundarySamples = 48;

    private static void BuildBoundary2D(Mesh m, Facet facet)
    {
        facet.Boundary2D = null;
        if (facet.Area <= 1e-12) return;
        var lines = FacetBoundaries(m, facet).OfType<LineCurve>()
                    .Select(lc => lc.Line).ToList();
        if (lines.Count < 3) return;
        var joined = Curve.JoinCurves(
            lines.Select(l => (Curve)new LineCurve(l)), 1e-6);
        Curve best = null;
        double bestLen = 0;
        foreach (var c in joined)
        {
            if (!c.IsClosed) continue;
            double len = c.GetLength();
            if (len > bestLen) { bestLen = len; best = c; }
        }
        if (best == null) return;
        var pts = new Point2d[BoundarySamples];
        var pts3 = new Point3d[BoundarySamples];
        var tt = best.DivideByCount(BoundarySamples, true);
        if (tt == null || tt.Length < BoundarySamples) return;
        for (int i = 0; i < BoundarySamples; i++)
        {
            var p = best.PointAt(tt[i]);
            pts3[i] = p;
            double u, v;
            facet.Frame.ClosestParameter(p, out u, out v);
            pts[i] = new Point2d(u, v);
        }
        facet.Boundary2D = pts;
        facet.Boundary3D = pts3;
        facet.BoundaryLength = bestLen;

        // relief signature: nearest interior sample's signed height above
        // the frame plane, per boundary sample
        facet.BoundaryRelief = new double[BoundarySamples];
        if (facet.Samples != null && facet.Samples.Length > 0)
        {
            for (int i = 0; i < BoundarySamples; i++)
            {
                double best2 = double.MaxValue;
                int bj = -1;
                for (int j = 0; j < facet.Samples.Length; j++)
                {
                    double d2 = pts3[i].DistanceToSquared(facet.Samples[j]);
                    if (d2 < best2) { best2 = d2; bj = j; }
                }
                facet.BoundaryRelief[i] = bj >= 0
                    ? facet.Frame.DistanceTo(facet.Samples[bj])
                    : 0.0;
            }
        }
    }

    /// <summary>
    /// Register the candidate's MIRRORED boundary loop (the mating view:
    /// v flips, traversal reverses) against the host's loop over all cyclic
    /// shifts with closed-form 2D Kabsch. Returns the best in-plane angle,
    /// 2D translation (host-frame coords) and the RMS misfit normalised by
    /// loop length -- false pairs with different outlines misfit badly.
    /// </summary>
    private static bool RegisterBoundaries(
        Point2d[] cand, Point2d[] host,
        double[] candRelief, double[] hostRelief, double reliefScale,
        out double angle, out Vector2d shift, out double misfit,
        out int bestShift, out bool reversed)
    {
        angle = 0; shift = new Vector2d(0, 0); misfit = double.MaxValue; bestShift = 0;
        reversed = false;
        if (cand == null || host == null) return false;
        int n = host.Length;
        // mating view of the candidate: mirror v. Traversal direction of
        // each joined loop is ARBITRARY (Curve.JoinCurves), so BOTH
        // directions must be tried; assuming one flipped the correspondence
        // randomly per pair (found 2026-07-11, N=2 case 37 units off).
        for (int rev = 0; rev < 2; rev++)
        {
        var cm = new Point2d[n];
        for (int i = 0; i < n; i++)
        {
            var p = cand[rev == 0 ? (n - 1 - i) : i];
            cm[i] = new Point2d(p.X, -p.Y);
        }
        double cx = 0, cy = 0, hx = 0, hy = 0;
        for (int i = 0; i < n; i++)
        { cx += cm[i].X; cy += cm[i].Y; hx += host[i].X; hy += host[i].Y; }
        cx /= n; cy /= n; hx /= n; hy /= n;

        for (int s = 0; s < n; s++)
        {
            // closed-form 2D rotation for correspondence i <-> (i+s) mod n
            double a = 0, b = 0;
            for (int i = 0; i < n; i++)
            {
                double ux = cm[i].X - cx, uy = cm[i].Y - cy;
                var h = host[(i + s) % n];
                double vx = h.X - hx, vy = h.Y - hy;
                a += ux * vx + uy * vy;
                b += ux * vy - uy * vx;
            }
            double th = Math.Atan2(b, a);
            double cth = Math.Cos(th), sth = Math.Sin(th);
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                double ux = cm[i].X - cx, uy = cm[i].Y - cy;
                double rx = cth * ux - sth * uy, ry = sth * ux + cth * uy;
                var h = host[(i + s) % n];
                double dx = rx - (h.X - hx), dy = ry - (h.Y - hy);
                sum += dx * dx + dy * dy;
            }
            double rms = Math.Sqrt(sum / n);
            // relief-signature disambiguation for symmetric outlines:
            // mates read the same physical bumps with opposite sign, so
            // candRelief + hostRelief is ~constant only for the true
            // correspondence. Score = 2D rms scaled by (1 + reliefStd/scale).
            if (candRelief != null && hostRelief != null && reliefScale > 1e-9)
            {
                double mean = 0;
                for (int i = 0; i < n; i++)
                    mean += candRelief[rev == 0 ? (n - 1 - i) : i] + hostRelief[(i + s) % n];
                mean /= n;
                double var2 = 0;
                for (int i = 0; i < n; i++)
                {
                    double d = candRelief[rev == 0 ? (n - 1 - i) : i] + hostRelief[(i + s) % n] - mean;
                    var2 += d * d;
                }
                double reliefStd = Math.Sqrt(var2 / n);
                rms *= 1.0 + reliefStd / reliefScale;
            }
            if (rms < misfit)
            {
                misfit = rms;
                angle = th;
                bestShift = s;
                reversed = rev == 1;
                double rcx = cth * cx - sth * cy, rcy = sth * cx + cth * cy;
                shift = new Vector2d(hx - rcx, hy - rcy);
            }
        }
        }
        return misfit < double.MaxValue;
    }

    /// <summary>
    /// Rigid refinement on the LOCKED boundary correspondence: iterate a
    /// closed-form Horn/Kabsch solve over the fixed pairs. No nearest-
    /// neighbour step, so the pose cannot slide along the interface.
    /// </summary>
    private static Transform BoundaryKabsch(
        Point3d[] cand3, Transform t0, Point3d[] host3World, int shift,
        bool reversed, int iterations)
    {
        var t = t0;
        int n = host3World.Length;
        for (int it = 0; it < iterations; it++)
        {
            var src = new List<Point3d>(n);
            var dst = new List<Point3d>(n);
            for (int i = 0; i < n; i++)
            {
                var p = cand3[reversed ? i : (n - 1 - i)];
                p.Transform(t);
                src.Add(p);
                dst.Add(host3World[(i + shift) % n]);
            }
            var step = RigidFromPairs(src, dst);
            t = Transform.Multiply(step, t);
        }
        return t;
    }

    /// <summary>Closed-form rigid transform (Horn quaternion) mapping the
    /// paired src points onto dst.</summary>
    /// <summary>
    /// Partial-overlap registration: mate the mirrored candidate frame to
    /// the host frame at 12 in-plane spins, coarse-score each seed by
    /// point-to-MESH RMS, refine the best 3 with trimmed point-to-mesh ICP.
    /// Sample-to-sample NN was tried first and ALIASES the fracture relief
    /// (measured FB 00002: ICP seeded AT ground truth drifted 37% of diag
    /// at 240 samples); the continuous host mesh has no such alias.
    /// rms/coverage/meanDot describe the TRIMMED contact band at the pose.
    /// </summary>
    private static Transform TrimmedRegister(Facet cand, Mesh hostWorld,
        Plane hostFrameWorld, double candSpacing,
        Point3d[] candBoundary, Point3d[] hostBoundaryWorld,
        out double rms, out double coverage, out double meanDot,
        out double bRms, out double bCov)
    {
        // SEED FRAMES from the CRACK-LINE RINGS when both sides have them:
        // plane fit through the boundary cloud, origin at its centroid. The
        // candidate's crack ring belongs exactly ON the host's hole rim, so
        // this anchors the seed where the fragment physically sits -- the
        // host FACET frame is useless when the rough region bleeds (its
        // centroid sits mid-interior, measured FB 00002 at 100k faces).
        // RANSAC CONGRUENT-TRIPLET seeds on the ring clouds. Every guess-
        // based scheme failed measurably (facet frames: origin mid-bleed;
        // ring centroid: 24 units off along a PARTIAL rim; rim stations:
        // half a patch off the ring's true center) -- none started inside
        // the ~20-unit basin. Instead find 3-point correspondences between
        // the candidate ring and the host ring (pairwise-distance congruent
        // triangles): a true triplet gives a POSE-ACCURATE seed directly.
        var seeds = new List<Transform>(256);
        {
            var rngS = new Random(1234);
            int nc = candBoundary.Length;
            // subsample host ring for the pair-distance table
            var hostSub = new List<Point3d>();
            int hStep = Math.Max(1, hostBoundaryWorld.Length / 120);
            for (int i = 0; i < hostBoundaryWorld.Length; i += hStep)
                hostSub.Add(hostBoundaryWorld[i]);
            int nh = hostSub.Count;
            double ringExt = 0;
            var bbC = new BoundingBox(candBoundary);
            ringExt = bbC.Diagonal.Length;
            double tol = Math.Max(1.0, 0.6 * candSpacing);
            // candidate triples with good spread
            var triples = new List<(int a, int b, int c)>();
            for (int tries = 0; tries < 400 && triples.Count < 16; tries++)
            {
                int a = rngS.Next(nc), b = rngS.Next(nc), c = rngS.Next(nc);
                if (a == b || b == c || a == c) continue;
                double dab = candBoundary[a].DistanceTo(candBoundary[b]);
                double dac = candBoundary[a].DistanceTo(candBoundary[c]);
                double dbc = candBoundary[b].DistanceTo(candBoundary[c]);
                if (dab < 0.3 * ringExt || dac < 0.3 * ringExt || dbc < 0.3 * ringExt) continue;
                triples.Add((a, b, c));
            }
            foreach (var (a, b, c) in triples)
            {
                if (seeds.Count >= 240) break;
                var A = candBoundary[a]; var B = candBoundary[b]; var C = candBoundary[c];
                double dab = A.DistanceTo(B), dac = A.DistanceTo(C), dbc = B.DistanceTo(C);
                for (int p = 0; p < nh && seeds.Count < 240; p++)
                    for (int q = 0; q < nh && seeds.Count < 240; q++)
                    {
                        if (p == q) continue;
                        if (Math.Abs(hostSub[p].DistanceTo(hostSub[q]) - dab) > tol) continue;
                        for (int r = 0; r < nh; r++)
                        {
                            if (r == p || r == q) continue;
                            if (Math.Abs(hostSub[p].DistanceTo(hostSub[r]) - dac) > tol) continue;
                            if (Math.Abs(hostSub[q].DistanceTo(hostSub[r]) - dbc) > tol) continue;
                            seeds.Add(RigidFromPairs(
                                new List<Point3d> { A, B, C },
                                new List<Point3d> { hostSub[p], hostSub[q], hostSub[r] }));
                            if (seeds.Count >= 240) break;
                        }
                    }
            }
        }
        // quick ring-NN screen: triplet seeds are pose-accurate when true,
        // so a static score suffices to shortlist
        if (seeds.Count > 24)
        {
            var scored = new List<(double s, Transform t)>(seeds.Count);
            foreach (var T0 in seeds)
            {
                var movedB = TransformPoints(candBoundary, T0);
                double sum = 0; int cnt = 0;
                for (int i = 0; i < movedB.Length; i += 4)
                {
                    double bdd = double.MaxValue;
                    for (int j = 0; j < hostBoundaryWorld.Length; j += 2)
                    {
                        double d2 = (movedB[i] - hostBoundaryWorld[j]).SquareLength;
                        if (d2 < bdd) bdd = d2;
                    }
                    sum += Math.Sqrt(bdd); cnt++;
                }
                scored.Add((sum / Math.Max(1, cnt), T0));
            }
            seeds = scored.OrderBy(x => x.s).Take(24).Select(x => x.t).ToList();
        }
        // classic facet-frame spins ONLY when triplet matching came up dry
        if (seeds.Count < 8)
        {
            var cc2 = new Point3d(0, 0, 0);
            foreach (var p in candBoundary) cc2 += p;
            cc2 /= Math.Max(1, candBoundary.Length);
            var candFrame = new Plane(cc2, cand.Frame.XAxis, cand.Frame.YAxis);
            var mirrored = new Plane(candFrame.Origin, candFrame.XAxis, -candFrame.YAxis);
            for (int s = 0; s < 12; s++)
            {
                double ang = s * (2 * Math.PI / 12);
                var tx = hostFrameWorld.XAxis * Math.Cos(ang) + hostFrameWorld.YAxis * Math.Sin(ang);
                var ty = -hostFrameWorld.XAxis * Math.Sin(ang) + hostFrameWorld.YAxis * Math.Cos(ang);
                seeds.Add(Transform.PlaneToPlane(mirrored, new Plane(hostFrameWorld.Origin, tx, ty)));
            }
        }
        rms = double.MaxValue; coverage = 0; meanDot = 1; bRms = double.MaxValue; bCov = 0;
        var best = Transform.Identity;
        // Full-depth ICP on EVERY seed. A short-ICP screen ranked by
        // boundary rms dropped the true seed: ring impostors (the chip ring
        // hugging another smooth junction, e.g. a foot rim) converge FAST
        // to low boundary rms while the true seed needs the full iteration
        // budget. Rank converged poses by the PRODUCT of surface-rms and
        // boundary-rms multiples: only the true seat wins BOTH (measured
        // FB 00002: GT 0.14x * 1.48x vs impostor 0.49x * 1.88x).
        double bestScore = double.MaxValue;
        foreach (var seed in seeds)
        {
            // triplet seeds are pose-accurate when true; 7 iterations refine
            // them fully and bound the per-pair budget
            var T = TrimmedIcpMesh(cand.Samples, cand.SampleNormals, hostWorld,
                candBoundary, hostBoundaryWorld, seed, candSpacing,
                out double r2, out double cov2, out double dot2,
                out double br2, out double bc2, 7);
            if (r2 >= double.MaxValue || br2 >= double.MaxValue) continue;
            double score = Math.Max(r2, 0.05 * candSpacing) * Math.Max(br2, 0.05 * candSpacing);
            if (score < bestScore)
            {
                bestScore = score;
                rms = r2; coverage = cov2; meanDot = dot2; bRms = br2; bCov = bc2; best = T;
            }
        }
        return best;
    }

    /// <summary>
    /// Trimmed point-to-MESH ICP: correspondences are closest points on the
    /// continuous host mesh; only the closest 60% (min 24) pairs vote in
    /// the Kabsch update, so the pose converges on the OVERLAPPING band and
    /// the non-mated remainder cannot drag it. rms is over the trimmed set;
    /// coverage = fraction of candidate samples within 1.5 candidate
    /// spacings of the host surface; meanDot = mean normal opposition over
    /// the covered samples (host normal from the hit face).
    /// </summary>
    private static Transform TrimmedIcpMesh(Point3d[] candS, Vector3d[] candN,
        Mesh hostWorld, Point3d[] candB, Point3d[] hostB,
        Transform T0, double candSpacing,
        out double rms, out double coverage, out double meanDot,
        out double bRms, out double bCov, int maxIter = 10)
    {
        var T = T0;
        rms = double.MaxValue;
        int n = candS.Length;
        hostWorld.FaceNormals.ComputeFaceNormals();
        var entries = new List<(double d2, Point3d src, Point3d dst)>(n);
        for (int iter = 0; iter < maxIter; iter++)
        {
            var moved = TransformPoints(candS, T);
            var movedNi = TransformVectors(candN, T);
            entries.Clear();
            for (int i = 0; i < n; i++)
            {
                var mp = hostWorld.ClosestMeshPoint(moved[i], 0.0);
                if (mp == null) continue;
                // contact is OPPOSING by physics: reject same-side pairs so
                // the walk cannot settle on a non-mating rest of the facet
                var hn = (Vector3d)hostWorld.FaceNormals[mp.FaceIndex];
                if (hn.Length > 1e-12)
                {
                    hn.Unitize();
                    // FRONT-SIDE filter: the sample must lie on the outward
                    // side of the matched face. Thin shards otherwise pair
                    // through the wall to the far surface and the update
                    // drags the fragment through the material (measured).
                    if ((moved[i] - mp.Point) * hn < -0.2 * candSpacing) continue;
                    var cn = movedNi[i];
                    if (cn.Length > 1e-12) { cn.Unitize(); if (cn * hn > -0.2) continue; }
                }
                entries.Add(((moved[i] - mp.Point).SquareLength, moved[i], mp.Point));
            }
            if (entries.Count < 24) { rms = double.MaxValue; break; }
            entries.Sort((a, b) => a.d2.CompareTo(b.d2));
            // trim relative to the VALID (opposing) set, not the raw sample
            // count: with n-based trimming and ~half the samples rejected by
            // the opposition filter, nothing was ever trimmed and the far
            // bleed tail poisoned the objective (measured: GT scored 1.07x
            // while a false nest scored 0.76x)
            int keep = Math.Min(entries.Count, Math.Max(24, (int)(entries.Count * 0.6)));
            var src = new List<Point3d>(keep);
            var dst = new List<Point3d>(keep);
            double sum = 0;
            for (int k = 0; k < keep; k++)
            {
                src.Add(entries[k].src);
                dst.Add(entries[k].dst);
                sum += entries[k].d2;
            }
            double newRms = Math.Sqrt(sum / keep);
            // BREAK-CURVE pairs: candidate outline midpoints -> nearest host
            // outline midpoints (radius-capped, trimmed to the closest 70%),
            // weighted 2x by duplication. The closed outline pins the
            // in-plane pose the surface statistics cannot.
            if (candB.Length >= 8 && hostB.Length >= 8)
            {
                var movedB = TransformPoints(candB, T);
                var bPairs = new List<(double d2, Point3d src, Point3d dst)>();
                double bCap2 = 4.0 * candSpacing * (4.0 * candSpacing);
                for (int i = 0; i < movedB.Length; i++)
                {
                    double bd = double.MaxValue; int bj = -1;
                    for (int j = 0; j < hostB.Length; j++)
                    {
                        double d2 = (movedB[i] - hostB[j]).SquareLength;
                        if (d2 < bd) { bd = d2; bj = j; }
                    }
                    if (bj >= 0 && bd < bCap2)
                        bPairs.Add((bd, movedB[i], hostB[bj]));
                }
                if (bPairs.Count >= 8)
                {
                    bPairs.Sort((a, b) => a.d2.CompareTo(b.d2));
                    int bKeep = Math.Max(8, (int)(bPairs.Count * 0.7));
                    for (int k = 0; k < bKeep && k < bPairs.Count; k++)
                    {
                        src.Add(bPairs[k].src); dst.Add(bPairs[k].dst);
                        src.Add(bPairs[k].src); dst.Add(bPairs[k].dst);
                    }
                }
            }
            T = Transform.Multiply(RigidFromPairs(src, dst), T);
            if (iter > 2 && Math.Abs(newRms - rms) < 1e-4 * candSpacing) { rms = newRms; break; }
            rms = newRms;
        }
        // final boundary alignment metrics
        bRms = double.MaxValue; bCov = 0;
        if (candB.Length >= 8 && hostB.Length >= 8)
        {
            var movedB = TransformPoints(candB, T);
            double covTolB2 = 1.5 * candSpacing * (1.5 * candSpacing);
            // candidate -> host nearest distances (drive the trimmed brms)
            var bd2 = new List<double>(movedB.Length);
            int candCovered = 0;
            foreach (var p in movedB)
            {
                double bd = double.MaxValue;
                for (int j = 0; j < hostB.Length; j++)
                {
                    double d2 = (p - hostB[j]).SquareLength;
                    if (d2 < bd) bd = d2;
                }
                bd2.Add(bd);
                if (bd < covTolB2) candCovered++;
            }
            bd2.Sort();
            int bKeep = Math.Max(8, (int)(bd2.Count * 0.7));
            double sumB = 0;
            for (int k = 0; k < bKeep && k < bd2.Count; k++) sumB += bd2[k];
            bRms = Math.Sqrt(sumB / Math.Min(bKeep, bd2.Count));
            // NOTE (FB 00003, honest boundary 2026-07-12): a very PARTIAL
            // host rim (mug 138 pts vs chip's complete 551) makes candidate-
            // side coverage cap at ~0.29 even at the TRUE 3%-error pose.
            // Measuring coverage relative to the smaller ring (max of both
            // directions) DID let the true pose pass the gate -- but it also
            // admitted false partial-overlap poses that then WON the rms*brms
            // ranking (measured: 00003 placed at 56% error, a false
            // positive). Kept candidate-side only: precision (zero false
            // placements: debris control + 00003 safe-reject) outranks
            // recall on the most partial rims. Distinguishing true from
            // false partial seats needs a host-side-trimmed boundary score,
            // not a looser coverage gate -- next work.
            bCov = (double)candCovered / movedB.Length;
        }
        // final coverage + OPPOSING FRACTION against the hit faces. The
        // covered set at a TRUE seat mixes the opposing fracture band with
        // same-side continuous skin across the crack, so the MEAN dot sits
        // near 0 (measured +0.04 at the recovered FB 00002 seat) -- gate on
        // the fraction of clearly opposing samples instead.
        hostWorld.FaceNormals.ComputeFaceNormals();
        var movedF = TransformPoints(candS, T);
        var movedN = TransformVectors(candN, T);
        double covTol = 1.5 * candSpacing;
        int close = 0, opp = 0, dotCnt = 0;
        for (int i = 0; i < n; i++)
        {
            var mp = hostWorld.ClosestMeshPoint(movedF[i], covTol * 4);
            if (mp == null) continue;
            double d = movedF[i].DistanceTo(mp.Point);
            if (d < covTol)
            {
                close++;
                var hn = (Vector3d)hostWorld.FaceNormals[mp.FaceIndex];
                if (hn.Length > 1e-12 && movedN[i].Length > 1e-12)
                {
                    hn.Unitize();
                    var cn = movedN[i]; cn.Unitize();
                    if (cn * hn < -0.3) opp++;
                    dotCnt++;
                }
            }
        }
        coverage = (double)close / Math.Max(1, n);
        meanDot = dotCnt > 0 ? (double)opp / dotCnt : 0.0;   // opposing FRACTION
        return T;
    }

    /// <summary>
    /// BREAK-CURVE point cloud of a facet: midpoints of the region's
    /// boundary segments (where fracture meets skin -- the physical crack
    /// outline). Used as extra correspondences in the trimmed ICP: the
    /// outline is a closed curve, so the band-slide degeneracy (chip
    /// sliding along a narrow break band with indistinguishable surface
    /// statistics, measured FB 00002) misaligns it and dies.
    /// </summary>
    private static Point3d[] BoundaryPointsOf(Mesh m, Facet fct)
        => BoundaryPointsOf(m, fct, null);

    /// <summary>
    /// With <paramref name="trueSkin"/> given, only CRACK-LINE edges
    /// qualify: boundary edges whose outside face is TRUE SKIN (below the
    /// second, lower roughness threshold: glaze, saw cut). Filtering by
    /// fracture-region labels was VACUOUS (a region's boundary borders
    /// non-fracture faces by construction); only the roughness VALUES
    /// separate the physical crack line from bleed edges into moderately
    /// rough surfaces (matte interior). Measured FB 00002, 2026-07-12.
    /// </summary>
    private static Point3d[] BoundaryPointsOf(Mesh m, Facet fct, HashSet<int> trueSkin)
    {
        var inFacet = new HashSet<int>(fct.Faces);
        var te = m.TopologyEdges;
        // face adjacency for the halo test below
        Dictionary<int, List<int>> adj = null;
        if (trueSkin != null)
        {
            adj = new Dictionary<int, List<int>>();
            for (int e = 0; e < te.Count; e++)
            {
                var fcs = te.GetConnectedFaces(e);
                for (int a = 0; a < fcs.Length; a++)
                    for (int b = a + 1; b < fcs.Length; b++)
                    {
                        if (!adj.TryGetValue(fcs[a], out var la)) adj[fcs[a]] = la = new List<int>();
                        la.Add(fcs[b]);
                        if (!adj.TryGetValue(fcs[b], out var lb)) adj[fcs[b]] = lb = new List<int>();
                        lb.Add(fcs[a]);
                    }
            }
        }
        // TRUE SKIN within 2 rings of the outside face: the smoothing pass
        // leaves a HALO of intermediate roughness between fracture and
        // glaze, so direct skin adjacency starves the ring (26 points
        // instead of hundreds, measured FB 00002 at 100k)
        bool NearSkin(int face)
        {
            if (trueSkin.Contains(face)) return true;
            if (!adj.TryGetValue(face, out var r1)) return false;
            foreach (var g in r1)
            {
                if (trueSkin.Contains(g)) return true;
                if (adj.TryGetValue(g, out var r2))
                    foreach (var h in r2)
                        if (trueSkin.Contains(h)) return true;
            }
            return false;
        }
        var pts = new List<Point3d>();
        for (int e = 0; e < te.Count; e++)
        {
            var faces = te.GetConnectedFaces(e);
            int inside = 0; bool outsideIsSkin = false;
            for (int k = 0; k < faces.Length; k++)
            {
                if (inFacet.Contains(faces[k])) inside++;
                else if (trueSkin == null || NearSkin(faces[k]))
                    outsideIsSkin = true;
            }
            if (inside != 1 || !outsideIsSkin) continue;
            pts.Add(te.EdgeLine(e).PointAt(0.5));
        }
        return pts.ToArray();
    }

    /// <summary>
    /// Phase 3 INTERLOCK REFINE (2026-07-12, measured on FB 00003 at 400k
    /// faces + dense sampling). The fracture band is self-similar under
    /// translation ALONG its length, so surface rms / coverage / relief
    /// correlation cannot pin the along-band position -- a band-slid pose
    /// scores as well as the true seat (the session-D negative result). But
    /// at sufficient resolution the micro-relief DOES pin it: the std of the
    /// signed candidate-to-host distance over DENSE fracture samples has a
    /// sharp minimum at the true offset (measured 0.63 at truth rising to
    /// 2.1 at +-8). This does a line-search along the band and slides the
    /// pose to that minimum (auto-correcting a band-slide), returning the
    /// residual std for gating. REQUIRES a high-resolution host mesh
    /// (~400k+); at 100k the minimum is broad and mis-placed. Dormant until
    /// INC-2 wires the rescue pass (2026-07-12).
    /// </summary>
    private static Transform InterlockRefine(Mesh candFracSub, Mesh hostFracSubWorld,
        Transform T0, double candSpacing, out double minStd, out double slideOffset)
    {
        minStd = double.MaxValue; slideOffset = 0;
        if (candFracSub == null || hostFracSubWorld == null ||
            candFracSub.Faces.Count < 8 || hostFracSubWorld.Faces.Count < 8)
            return T0;
        // dense area-weighted samples of the candidate fracture surface
        var rng = new Random(7);
        int K = 1200;
        var cum = new double[candFracSub.Faces.Count];
        double acc = 0;
        for (int fi = 0; fi < candFracSub.Faces.Count; fi++)
        {
            var f = candFracSub.Faces[fi];
            Point3d a = candFracSub.Vertices[f.A], b = candFracSub.Vertices[f.B], c = candFracSub.Vertices[f.C];
            acc += 0.5 * Vector3d.CrossProduct(b - a, c - a).Length;
            cum[fi] = acc;
        }
        var samp = new Point3d[K];
        for (int k = 0; k < K; k++)
        {
            double tv = rng.NextDouble() * acc;
            int lo = 0, hi = cum.Length - 1;
            while (lo < hi) { int mid = (lo + hi) / 2; if (cum[mid] < tv) lo = mid + 1; else hi = mid; }
            var f = candFracSub.Faces[lo];
            Point3d a = candFracSub.Vertices[f.A], b = candFracSub.Vertices[f.B], c = candFracSub.Vertices[f.C];
            double r1 = rng.NextDouble(), r2 = rng.NextDouble(), s = Math.Sqrt(r1);
            double w0 = 1 - s, w1 = s * (1 - r2), w2 = s * r2;
            samp[k] = new Point3d(w0 * a.X + w1 * b.X + w2 * c.X,
                                  w0 * a.Y + w1 * b.Y + w2 * c.Y, w0 * a.Z + w1 * b.Z + w2 * c.Z);
        }
        // band direction from a plane fit of the samples (major in-plane PCA)
        var world = TransformPoints(samp, T0);
        Plane pl;
        if (Plane.FitPlaneToPoints(world, out pl) != PlaneFitResult.Success) return T0;
        double su = 0, sv = 0; var uv = new double[world.Length, 2];
        for (int i = 0; i < world.Length; i++)
        { double u, v; pl.ClosestParameter(world[i], out u, out v); uv[i, 0] = u; uv[i, 1] = v; su += u; sv += v; }
        su /= world.Length; sv /= world.Length;
        double suu = 0, svv = 0, suv = 0;
        for (int i = 0; i < world.Length; i++)
        { double du = uv[i, 0] - su, dv = uv[i, 1] - sv; suu += du * du; svv += dv * dv; suv += du * dv; }
        double tr = suu + svv, det = suu * svv - suv * suv, disc = Math.Sqrt(Math.Max(0, tr * tr / 4 - det)), l1 = tr / 2 + disc;
        var bUV = Math.Abs(suv) > 1e-9 ? new Vector2d(l1 - svv, suv)
                                       : new Vector2d(suu >= svv ? 1 : 0, suu >= svv ? 0 : 1);
        bUV.Unitize();
        var bandDir = pl.XAxis * bUV.X + pl.YAxis * bUV.Y; bandDir.Unitize();

        // std of the (unsigned) distance at an offset along the band
        double StdAt(double off)
        {
            var d = bandDir * off;
            double sum = 0, sum2 = 0; int cnt = 0;
            for (int i = 0; i < world.Length; i++)
            {
                var q = world[i] + d;
                var mp = hostFracSubWorld.ClosestMeshPoint(q, 8.0);
                if (mp == null) continue;
                double dist = q.DistanceTo(mp.Point);
                sum += dist; sum2 += dist * dist; cnt++;
            }
            if (cnt < 50) return double.MaxValue;
            double m = sum / cnt;
            return Math.Sqrt(Math.Max(0, sum2 / cnt - m * m));
        }
        // coarse line-search +-20 step 2, then refine +-2 step 0.5
        double bestOff = 0, bestStd = StdAt(0);
        for (double off = -20; off <= 20.0001; off += 2)
        { double s = StdAt(off); if (s < bestStd) { bestStd = s; bestOff = off; } }
        for (double off = bestOff - 2; off <= bestOff + 2.0001; off += 0.5)
        { double s = StdAt(off); if (s < bestStd) { bestStd = s; bestOff = off; } }
        minStd = bestStd; slideOffset = bestOff;
        return Transform.Multiply(Transform.Translation(bandDir * bestOff), T0);
    }

    /// <summary>
    /// INC-3 (2026-07-12): map a facet region found on a DECIMATED mesh to the
    /// corresponding region of the FULL-RES mesh, so the interlock reads the
    /// true micro-relief. Selects full-res faces whose centre lies within a
    /// decimated-edge radius of any decimated-facet vertex (RTree lookup, so a
    /// 400k host costs a fast build + a few hundred sphere searches, and the
    /// result is bounded to the fracture region, not the whole mesh). Returns
    /// the decimated facet unchanged when there is nothing denser to map to.
    /// </summary>
    private static Mesh MapFacetToFullRes(Mesh full, Mesh decFacet)
    {
        if (full == null || decFacet == null || decFacet.Faces.Count < 4) return decFacet;
        if (full.Faces.Count <= decFacet.Faces.Count) return decFacet; // seg == full
        double area = 0;
        for (int i = 0; i < decFacet.Faces.Count; i++)
        {
            var f = decFacet.Faces[i];
            Point3d a = decFacet.Vertices[f.A], b = decFacet.Vertices[f.B], c = decFacet.Vertices[f.C];
            area += 0.5 * Vector3d.CrossProduct(b - a, c - a).Length;
        }
        double dist = 2.5 * Math.Sqrt(area / Math.Max(1, decFacet.Faces.Count));
        if (dist <= 0) return decFacet;
        var centers = new Point3d[full.Faces.Count];
        for (int i = 0; i < full.Faces.Count; i++) centers[i] = full.Faces.GetFaceCenter(i);
        var tree = RTree.CreateFromPointArray(centers);
        var keep = new HashSet<int>();
        foreach (var vtx in decFacet.Vertices)
            tree.Search(new Sphere(new Point3d(vtx.X, vtx.Y, vtx.Z), dist),
                        (s, e) => keep.Add(e.Id));
        if (keep.Count < 8) return decFacet;
        return SubMesh(full, keep.ToList());
    }

    /// <summary>Compact standalone copy of a face subset (facet region).</summary>
    private static Mesh SubMesh(Mesh m, List<int> faces)
    {
        var sub = new Mesh();
        var vmap = new Dictionary<int, int>();
        foreach (var fi in faces)
        {
            if (fi < 0 || fi >= m.Faces.Count) continue;
            var face = m.Faces[fi];
            int Map(int v)
            {
                if (!vmap.TryGetValue(v, out int nv))
                {
                    nv = sub.Vertices.Count;
                    sub.Vertices.Add(m.Vertices[v]);
                    vmap[v] = nv;
                }
                return nv;
            }
            if (face.IsQuad)
                sub.Faces.AddFace(Map(face.A), Map(face.B), Map(face.C), Map(face.D));
            else
                sub.Faces.AddFace(Map(face.A), Map(face.B), Map(face.C));
        }
        if (sub.Faces.Count == 0) return null;
        sub.Normals.ComputeNormals();
        sub.FaceNormals.ComputeFaceNormals();
        return sub;
    }

    private static Transform RigidFromPairs(List<Point3d> src, List<Point3d> dst)
    {
        var cs = new Point3d(0, 0, 0); var cd = new Point3d(0, 0, 0);
        for (int i = 0; i < src.Count; i++) { cs += src[i]; cd += dst[i]; }
        cs /= src.Count; cd /= dst.Count;
        double xx=0, xy=0, xz=0, yx=0, yy=0, yz=0, zx=0, zy=0, zz=0;
        for (int i = 0; i < src.Count; i++)
        {
            var a = src[i] - cs; var b = dst[i] - cd;
            xx += a.X*b.X; xy += a.X*b.Y; xz += a.X*b.Z;
            yx += a.Y*b.X; yy += a.Y*b.Y; yz += a.Y*b.Z;
            zx += a.Z*b.X; zy += a.Z*b.Y; zz += a.Z*b.Z;
        }
        double[,] N = {
            { xx+yy+zz, yz-zy,     zx-xz,     xy-yx     },
            { yz-zy,    xx-yy-zz,  xy+yx,     zx+xz     },
            { zx-xz,    xy+yx,    -xx+yy-zz,  yz+zy     },
            { xy-yx,    zx+xz,     yz+zy,    -xx-yy+zz  } };
        var q = new double[] { 1, 0.1, 0.1, 0.1 };
        for (int pi = 0; pi < 30; pi++)
        {
            var nq = new double[4];
            for (int i2 = 0; i2 < 4; i2++)
                for (int j2 = 0; j2 < 4; j2++) nq[i2] += N[i2, j2] * q[j2];
            for (int i2 = 0; i2 < 4; i2++) nq[i2] += q[i2] * (Math.Abs(xx)+Math.Abs(yy)+Math.Abs(zz)+1);
            double nrm = Math.Sqrt(nq.Sum(v2 => v2 * v2));
            if (nrm < 1e-12) break;
            for (int i2 = 0; i2 < 4; i2++) q[i2] = nq[i2] / nrm;
        }
        double w = q[0], x = q[1], y = q[2], z = q[3];
        var R = Transform.Identity;
        R.M00 = 1-2*(y*y+z*z); R.M01 = 2*(x*y-z*w); R.M02 = 2*(x*z+y*w);
        R.M10 = 2*(x*y+z*w);   R.M11 = 1-2*(x*x+z*z); R.M12 = 2*(y*z-x*w);
        R.M20 = 2*(x*z-y*w);   R.M21 = 2*(y*z+x*w);   R.M22 = 1-2*(x*x+y*y);
        return Transform.Multiply(Transform.Translation(cd - Point3d.Origin),
               Transform.Multiply(R, Transform.Translation(Point3d.Origin - cs)));
    }

    /// <summary>
    /// Pose-pinning score: mean 3D distance between CORRESPONDED boundary
    /// samples (the cyclic correspondence the 2D registration selected:
    /// candidate traversed reversed, offset by shift). Unlike NN sample RMS
    /// this is fully sensitive to in-plane sliding and rotation.
    /// </summary>
    private static double BoundaryResid3D(
        Point3d[] cand3, Transform t, Point3d[] host3World, int shift, bool reversed)
    {
        int n = host3World.Length;
        double s = 0;
        for (int i = 0; i < n; i++)
        {
            var p = cand3[reversed ? i : (n - 1 - i)];
            p.Transform(t);
            s += p.DistanceTo(host3World[(i + shift) % n]);
        }
        return s / n;
    }

    private static void Describe(Mesh m, Facet facet, Random rng)
    {
        // area + centroid + area-weighted normal over the facet triangles
        double area = 0;
        var cen = new Point3d(0, 0, 0);
        var nrm = Vector3d.Zero;
        var tris = new List<(Point3d a, Point3d b, Point3d c, double area)>();
        foreach (var fi in facet.Faces)
        {
            var face = m.Faces[fi];
            var corners = face.IsQuad
                ? new[] { (face.A, face.B, face.C), (face.A, face.C, face.D) }
                : new[] { (face.A, face.B, face.C) };
            foreach (var (ia, ib, ic) in corners)
            {
                Point3d a = m.Vertices[ia], b = m.Vertices[ib], c = m.Vertices[ic];
                var cr = Vector3d.CrossProduct(b - a, c - a);
                double ar = cr.Length * 0.5;
                if (ar <= 0) continue;
                area += ar;
                cen += (a + b + c) * (ar / 3.0);
                nrm += cr * 0.5;
                tris.Add((a, b, c, ar));
            }
        }
        if (area <= 1e-12) { facet.Area = 0; return; }
        cen = new Point3d(cen.X / area, cen.Y / area, cen.Z / area);
        nrm.Unitize();

        // principal in-plane axes via 2x2 covariance of projected tri centroids
        var plane = new Plane(cen, nrm);
        double sxx = 0, sxy = 0, syy = 0;
        double rms = 0;
        foreach (var (a, b, c, ar) in tris)
        {
            var tc = (a + b + c) / 3.0;
            double u, v;
            plane.ClosestParameter(tc, out u, out v);
            sxx += ar * u * u; sxy += ar * u * v; syy += ar * v * v;
            double d = plane.DistanceTo(tc);
            rms += ar * d * d;
        }
        sxx /= area; sxy /= area; syy /= area;
        rms = Math.Sqrt(rms / area);
        double tr = sxx + syy, det = sxx * syy - sxy * sxy;
        double disc = Math.Sqrt(Math.Max(0, tr * tr / 4 - det));
        double l1 = tr / 2 + disc, l2 = Math.Max(0, tr / 2 - disc);
        // principal axis in plane coords
        Vector3d ax;
        if (Math.Abs(sxy) > 1e-12)
        {
            var dir2 = new Vector2d(l1 - syy, sxy);
            ax = plane.XAxis * dir2.X + plane.YAxis * dir2.Y;
        }
        else ax = sxx >= syy ? plane.XAxis : plane.YAxis;
        ax.Unitize();

        facet.Area = area;
        facet.Centroid = cen;
        facet.Normal = nrm;
        facet.E1 = Math.Sqrt(Math.Max(l1, 1e-12));
        facet.E2 = Math.Sqrt(Math.Max(l2, 1e-12));
        facet.Rms = rms;
        facet.Frame = new Plane(cen, ax, Vector3d.CrossProduct(nrm, ax));

        // area-weighted samples with per-sample normals. K sets the
        // discrimination floor: mating facets carry the SAME fractal relief,
        // wrong partners a different one; the point-wise mismatch only shows
        // once sample spacing drops below the relief wavelength.
        int K = Math.Min(240, Math.Max(48, tris.Count * 2));
        var cum = new List<double>(tris.Count);
        double acc = 0;
        foreach (var t in tris) { acc += t.area; cum.Add(acc); }
        var pts = new Point3d[K];
        var ns = new Vector3d[K];
        for (int k = 0; k < K; k++)
        {
            double tv = rng.NextDouble() * acc;
            int lo = 0, hi = cum.Count - 1;
            while (lo < hi) { int mid = (lo + hi) / 2; if (cum[mid] < tv) lo = mid + 1; else hi = mid; }
            var (a, b, c, _) = tris[lo];
            double r1 = rng.NextDouble(), r2 = rng.NextDouble(), s = Math.Sqrt(r1);
            double w0 = 1 - s, w1 = s * (1 - r2), w2 = s * r2;
            pts[k] = new Point3d(
                w0 * a.X + w1 * b.X + w2 * c.X,
                w0 * a.Y + w1 * b.Y + w2 * c.Y,
                w0 * a.Z + w1 * b.Z + w2 * c.Z);
            var nn = Vector3d.CrossProduct(b - a, c - a);
            nn.Unitize();
            ns[k] = nn;
        }
        facet.Samples = pts;
        facet.SampleNormals = ns;
    }

    private static IEnumerable<Curve> FacetBoundaries(Mesh m, Facet facet)
    {
        // facet boundary = edges used once within the facet's face set
        var inFacet = new HashSet<int>(facet.Faces);
        var te = m.TopologyEdges;
        var lines = new List<Line>();
        for (int e = 0; e < te.Count; e++)
        {
            var faces = te.GetConnectedFaces(e);
            int inside = faces.Count(f => inFacet.Contains(f));
            if (inside == 1) lines.Add(te.EdgeLine(e));
        }
        // emit as line segments (cheap diagnostic; joining is display-only)
        foreach (var ln in lines) yield return new LineCurve(ln);
    }

    // -------------------------------------------------------------------------
    // Scoring + gates.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Exhaustive DFS over the (small) candidate list: at most one candidate
    /// per fragment, no two candidates claiming the same HOST facet.
    /// Maximizes the number of placements, then minimizes the total
    /// floor-multiple. Impostor placements (congruent skin planes) tend to
    /// claim the same big host facet and knock each other out here.
    /// </summary>
    private static List<(double resid, int frag, int hostFacet, Transform t, double penEff)>
        SelectNonConflicting(List<(double resid, int frag, int hostFacet, Transform t, double penEff)> ranked)
    {
        var best = new List<(double, int, int, Transform, double)>();
        double bestSum = double.MaxValue;
        var sorted = ranked.OrderBy(r => r.resid).Take(24).ToList();
        var cur = new List<(double, int, int, Transform, double)>();

        void Dfs(int i, HashSet<int> frags, HashSet<int> hosts, double sum)
        {
            if (cur.Count > best.Count ||
                (cur.Count == best.Count && sum < bestSum))
            {
                best = new List<(double, int, int, Transform, double)>(cur);
                bestSum = sum;
            }
            if (i >= sorted.Count) return;
            // prune: even taking every remaining candidate can't beat best
            if (cur.Count + (sorted.Count - i) < best.Count) return;
            var c = sorted[i];
            if (!frags.Contains(c.frag) && !hosts.Contains(c.hostFacet))
            {
                frags.Add(c.frag); hosts.Add(c.hostFacet); cur.Add(c);
                Dfs(i + 1, frags, hosts, sum + c.resid);
                frags.Remove(c.frag); hosts.Remove(c.hostFacet); cur.RemoveAt(cur.Count - 1);
            }
            Dfs(i + 1, frags, hosts, sum);
        }
        Dfs(0, new HashSet<int>(), new HashSet<int>(), 0);
        return best;
    }

    private static Point3d[] TransformPoints(Point3d[] pts, Transform t)
    {
        var r = new Point3d[pts.Length];
        for (int i = 0; i < pts.Length; i++) { r[i] = pts[i]; r[i].Transform(t); }
        return r;
    }

    private static Vector3d[] TransformVectors(Vector3d[] vs, Transform t)
    {
        var r = new Vector3d[vs.Length];
        for (int i = 0; i < vs.Length; i++) { r[i] = vs[i]; r[i].Transform(t); }
        return r;
    }

    /// <summary>
    /// Short point-to-point ICP: nearest-neighbour pairs within
    /// <paramref name="radius"/>, closed-form Kabsch update (quaternion
    /// method, no external SVD), few iterations. Returns the refined
    /// transform (left-composed onto <paramref name="t0"/>).
    /// </summary>
    private static Transform MiniIcp(Point3d[] cand, Transform t0, Point3d[] host,
                                     double radius, int iterations)
    {
        var t = t0;
        double r2 = radius * radius;
        for (int it = 0; it < iterations; it++)
        {
            var src = new List<Point3d>();
            var dst = new List<Point3d>();
            for (int i = 0; i < cand.Length; i++)
            {
                var p = cand[i]; p.Transform(t);
                double best = r2; int bj = -1;
                for (int j = 0; j < host.Length; j++)
                {
                    double d2 = p.DistanceToSquared(host[j]);
                    if (d2 < best) { best = d2; bj = j; }
                }
                if (bj >= 0) { src.Add(p); dst.Add(host[bj]); }
            }
            if (src.Count < 6) return t;
            // centroids
            var cs = new Point3d(0, 0, 0); var cd = new Point3d(0, 0, 0);
            for (int i = 0; i < src.Count; i++) { cs += src[i]; cd += dst[i]; }
            cs /= src.Count; cd /= dst.Count;
            // cross-covariance
            double xx=0, xy=0, xz=0, yx=0, yy=0, yz=0, zx=0, zy=0, zz=0;
            for (int i = 0; i < src.Count; i++)
            {
                var a = src[i] - cs; var b = dst[i] - cd;
                xx += a.X*b.X; xy += a.X*b.Y; xz += a.X*b.Z;
                yx += a.Y*b.X; yy += a.Y*b.Y; yz += a.Y*b.Z;
                zx += a.Z*b.X; zy += a.Z*b.Y; zz += a.Z*b.Z;
            }
            // Horn's quaternion method: max eigenvector of the 4x4 N matrix
            // via power iteration (deterministic start).
            double[,] N = {
                { xx+yy+zz, yz-zy,     zx-xz,     xy-yx     },
                { yz-zy,    xx-yy-zz,  xy+yx,     zx+xz     },
                { zx-xz,    xy+yx,    -xx+yy-zz,  yz+zy     },
                { xy-yx,    zx+xz,     yz+zy,    -xx-yy+zz  } };
            var q = new double[] { 1, 0.1, 0.1, 0.1 };
            for (int pi = 0; pi < 30; pi++)
            {
                var nq = new double[4];
                for (int i2 = 0; i2 < 4; i2++)
                    for (int j2 = 0; j2 < 4; j2++) nq[i2] += N[i2, j2] * q[j2];
                // shift to keep the dominant eigenvalue positive
                for (int i2 = 0; i2 < 4; i2++) nq[i2] += q[i2] * (Math.Abs(xx)+Math.Abs(yy)+Math.Abs(zz)+1);
                double nrm = Math.Sqrt(nq.Sum(v => v * v));
                if (nrm < 1e-12) break;
                for (int i2 = 0; i2 < 4; i2++) q[i2] = nq[i2] / nrm;
            }
            double w = q[0], x = q[1], y = q[2], z = q[3];
            var R = Transform.Identity;
            R.M00 = 1-2*(y*y+z*z); R.M01 = 2*(x*y-z*w); R.M02 = 2*(x*z+y*w);
            R.M10 = 2*(x*y+z*w);   R.M11 = 1-2*(x*x+z*z); R.M12 = 2*(y*z-x*w);
            R.M20 = 2*(x*z-y*w);   R.M21 = 2*(y*z+x*w);   R.M22 = 1-2*(x*x+y*y);
            // update: rotate about source centroid, translate to dest centroid
            var step = Transform.Multiply(Transform.Translation(cd - Point3d.Origin),
                       Transform.Multiply(R, Transform.Translation(Point3d.Origin - cs)));
            t = Transform.Multiply(step, t);
        }
        return t;
    }

    private static double SampleRms(Point3d[] cand, Transform t, Point3d[] host)
    {
        double s = 0; int cnt = 0;
        for (int i = 0; i < cand.Length; i += 2)
        {
            var p = cand[i]; p.Transform(t);
            double best = double.MaxValue;
            for (int j = 0; j < host.Length; j++)
                best = Math.Min(best, p.DistanceToSquared(host[j]));
            s += best; cnt++;
        }
        return cnt > 0 ? Math.Sqrt(s / cnt) : double.MaxValue;
    }

    private static double MeanNormalDot(
        Point3d[] candPts, Vector3d[] candNs, Transform t,
        Point3d[] hostPts, Vector3d[] hostNs, double radius)
    {
        double r2 = radius * radius;
        double dot = 0; int cnt = 0;
        for (int i = 0; i < candPts.Length; i += 2)
        {
            var p = candPts[i]; p.Transform(t);
            var n = candNs[i]; n.Transform(t);
            double best = r2; int bj = -1;
            for (int j = 0; j < hostPts.Length; j++)
            {
                double d2 = p.DistanceToSquared(hostPts[j]);
                if (d2 < best) { best = d2; bj = j; }
            }
            if (bj >= 0) { dot += n * hostNs[bj]; cnt++; }
        }
        return cnt >= 5 ? dot / cnt : 1.0;   // no contact -> fail the opposition test
    }

    private static bool Penetrates(
        List<Mesh> fragments, Mesh[] placedWorld, bool[] placed,
        int candIdx, Transform t, double tol)
    {
        var cand = fragments[candIdx];
        int step = Math.Max(1, cand.Vertices.Count / 48);
        for (int g = 0; g < fragments.Count; g++)
        {
            if (!placed[g] || g == candIdx) continue;
            var other = placedWorld[g];
            if (other == null || !other.IsClosed) continue;
            var bb = other.GetBoundingBox(false);
            for (int v = 0; v < cand.Vertices.Count; v += step)
            {
                var p = (Point3d)cand.Vertices[v];
                p.Transform(t);
                if (!bb.Contains(p)) continue;
                if (!other.IsPointInside(p, 1e-6, true)) continue;
                var mp = other.ClosestMeshPoint(p, 0.0);
                if (mp != null && p.DistanceTo(mp.Point) > tol) return true;
            }
        }
        return false;
    }
}
