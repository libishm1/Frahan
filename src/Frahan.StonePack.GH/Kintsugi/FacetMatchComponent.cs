#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Frahan.EdgeMatching;
using Frahan.GH.Attributes;
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
public sealed class FacetMatchComponent : FrahanComponentBase
{
    public FacetMatchComponent()
        : base("Facet Match", "FacetMatch",
            "Reassemble WATERTIGHT fracture fragments by segmenting faces " +
            "into facets (dihedral-angle region growing), matching " +
            "complementary fracture facets across fragments, and refining " +
            "with Soft ICP. The closed-mesh sibling of the rim-matching " +
            "Kintsugi path: use THIS when fragments have no naked rims " +
            "(scanned pieces, Fracture Roughen with Cap Cuts=True).",
            "Frahan", "Kintsugi")
    {
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
        p.AddNumberParameter("Accept Floor", "Af",
            "Accept a facet pairing when its sample RMS is below this " +
            "MULTIPLE of the sampling-density floor (sqrt(area/K)). Two " +
            "independent samplings of the SAME surface sit near 0.7-1.0; " +
            "wrong partners mismatch the fracture relief and land higher. " +
            "Default 1.3. The report's candidate diagnostics show the " +
            "measured multiples; tune from there.",
            GH_ParamAccess.item, 1.3);
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
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var fragments = new List<Mesh>();
        double angleDeg = 30.0, minShare = 0.05, jointWidth = 1.0, penTol = 0.5;
        bool softIcp = true, run = false, penHinge = false;
        if (!da.GetDataList(0, fragments)) return;
        da.GetData(1, ref angleDeg);
        da.GetData(2, ref minShare);
        da.GetData(3, ref jointWidth);
        da.GetData(4, ref penTol);
        da.GetData(5, ref softIcp);
        da.GetData(6, ref run);
        if (Params.Input.Count > 7) da.GetData(7, ref penHinge);
        double acceptFloor = 1.3;
        if (Params.Input.Count > 8) da.GetData(8, ref acceptFloor);
        if (!run)
        {
            da.SetData(5, "Run is false. Toggle to execute.");
            return;
        }
        if (fragments.Count < 2)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Need >= 2 fragments.");
            return;
        }

        var report = new System.Text.StringBuilder();
        var rng = new Random(42);

        // 1-2. Segment + describe.
        var facets = new List<Facet>();
        var outlines = new List<Curve>();
        var fragArea = new double[fragments.Count];
        for (int f = 0; f < fragments.Count; f++)
        {
            var m = fragments[f];
            if (m == null) continue;
            if (!m.IsClosed)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Fragment {f} is not closed; facet matching expects " +
                    "watertight meshes (results may degrade).");
            var fs = SegmentFacets(m, f, angleDeg, rng);
            fragArea[f] = fs.Sum(x => x.Area);
            // keep at most the 10 largest qualifying facets per fragment --
            // candidate pairing is O(facets^2) and small facets rarely carry
            // a whole interface
            foreach (var fc in fs.Where(x => x.Area >= fragArea[f] * minShare)
                                 .OrderByDescending(x => x.Area).Take(10))
            {
                facets.Add(fc);
                foreach (var b in FacetBoundaries(m, fc)) outlines.Add(b);
            }
        }
        report.AppendLine($"Facets: {facets.Count} kept across {fragments.Count} fragments " +
                          $"(angle {angleDeg:F0} deg, min share {minShare:F2}).");
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
        bool progress = true;
        var swGreedy = System.Diagnostics.Stopwatch.StartNew();
        while (progress)
        {
            progress = false;
            // CHEAP scan first (residual + normal opposition only); the
            // expensive per-vertex inside-mesh penetration test runs only
            // on the ranked survivors (this was the 300s hotspot).
            var ranked = new List<(double resid, int frag, Transform t)>();
            var diag = new List<(double mult, int cand, int host)>();
            foreach (var cand in facets)
            {
                if (placed[cand.FragIdx]) continue;
                foreach (var host in facets)
                {
                    if (!placed[host.FragIdx] || host.FragIdx == cand.FragIdx) continue;
                    double aRatio = Math.Min(cand.Area, host.Area) / Math.Max(cand.Area, host.Area);
                    if (aRatio < 0.55) continue;
                    double e1Ratio = Math.Min(cand.E1, host.E1) / Math.Max(cand.E1, host.E1);
                    if (e1Ratio < 0.6) continue;
                    double e2Ratio = Math.Min(cand.E2, host.E2) / Math.Max(cand.E2, host.E2);
                    if (e2Ratio < 0.5) continue;
                    // mating facets share the same relief -> near-equal RMS
                    double rMax = Math.Max(cand.Rms, host.Rms);
                    if (rMax > 1e-9 &&
                        Math.Abs(cand.Rms - host.Rms) / rMax > 0.5) continue;

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

                    int spins = (cand.E1 / Math.Max(1e-9, cand.E2) < 1.2) ? 4 : 2;
                    for (int s = 0; s < spins; s++)
                    {
                        double ang = s * Math.PI * 2.0 / spins;
                        var tx = hostFrame.XAxis * Math.Cos(ang) + hostFrame.YAxis * Math.Sin(ang);
                        // mating target: candidate's outward normal must OPPOSE
                        // the host facet's, so target Z = -hostZ (Z = X x Y with
                        // Y = (-hostZ) x X gives exactly that)
                        var target = new Plane(hostFrame.Origin, tx,
                            Vector3d.CrossProduct(-hostFrame.ZAxis, tx));
                        var T = Transform.PlaneToPlane(cand.Frame, target);

                        double resid = SampleRms(cand.Samples, T, hostSamples);
                        // rank + accept in FLOOR MULTIPLES: two independent
                        // samplings of the SAME surface sit ~0.7-1.0*spacing
                        // apart; wrong partners mismatch the relief and land
                        // higher. Acceptance threshold = Accept Floor input.
                        double floorMult = resid / Math.Max(1e-9, spacing);
                        diag.Add((floorMult, cand.FragIdx, host.FragIdx));
                        if (floorMult >= acceptFloor && resid >= jointWidth) continue;
                        double dot = MeanNormalDot(cand.Samples, cand.SampleNormals, T,
                                                   hostSamples, hostNormals, spacing * 3);
                        if (dot > -0.3) continue;
                        ranked.Add((floorMult, cand.FragIdx, T));
                    }
                }
            }
            // candidate diagnostics: the measured floor-multiples, best first
            if (!progressReported && diag.Count > 0)
            {
                progressReported = true;
                report.AppendLine("  candidate floor-multiples (best 12 of " + diag.Count + "):");
                foreach (var d2 in diag.OrderBy(x => x.mult).Take(12))
                    report.AppendLine($"    frag {d2.cand} -> {d2.host}: {d2.mult:F2}");
            }
            // verify survivors best-first; accept the first that clears
            // the penetration gate
            foreach (var (resid, frag, T) in ranked.OrderBy(r => r.resid))
            {
                if (placed[frag]) continue;
                if (penTol > 0 && Penetrates(fragments, placedWorld, placed, frag, T, penTol))
                    continue;
                pose[frag] = T;
                placed[frag] = true;
                RefreshPlaced(frag);
                placedCount++;
                progress = true;
                report.AppendLine($"  placed fragment {frag}: facet RMS {resid:F3}");
                break;   // rescan with the grown cluster
            }
        }
        report.AppendLine($"Greedy assembly: {swGreedy.Elapsed.TotalSeconds:F1} s.");

        // 6. Soft ICP polish.
        if (softIcp && placedCount >= 2)
        {
            try
            {
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
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
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

        da.SetDataList(0, assembled);
        da.SetDataList(1, transforms);
        da.SetDataList(2, placedIdx);
        da.SetDataList(3, unplacedIdx);
        da.SetDataList(4, outlines);
        da.SetData(5, report.ToString());
    }

    // -------------------------------------------------------------------------
    // Segmentation.
    // -------------------------------------------------------------------------

    private static List<Facet> SegmentFacets(Mesh m, int fragIdx, double angleDeg, Random rng)
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
            var meanN = normals[seed];
            var queue = new Queue<int>();
            queue.Enqueue(seed);
            facetId[seed] = id;
            while (queue.Count > 0)
            {
                int f = queue.Dequeue();
                meanN += normals[f];
                var mu = meanN; mu.Unitize();
                foreach (var g in adj[f])
                {
                    if (facetId[g] >= 0) continue;
                    if (normals[g] * mu < cosTol) continue;
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
            Describe(m, fct, rng);
            result.Add(fct);
        }
        return result;
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
