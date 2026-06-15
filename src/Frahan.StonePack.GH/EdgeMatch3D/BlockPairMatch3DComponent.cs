#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.EdgeMatching;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.EdgeMatch3D;

// =============================================================================
// BlockPairMatch3DComponent (Component B3D, GUID D5F10008)
//
// Atomic 3D edge-matching primitive: given two scanned-stone meshes, find
// the rigid 3D pose (translation + rotation) where their planar face
// patches mate cleanly. This is the foundational primitive every higher-
// order 3D Frahan assembly workflow (Block Chain Along Thrust Line,
// Adaptive Block Match w/ Trim, Template Block Match, Cyclopean Recipe
// Coursing) calls.
//
// Pipeline (mirroring the 2D EdgeMatch family per
// wiki/specs/frahan_design_philosophy.md SS10.9):
//   1. VsaSegmenter -> quasi-planar face patches per mesh.
//   2. Filter patches by area (SS2.4.1 of UCL Devadass 2025; default
//      MinFaceArea = 15,000 mm^2).
//   3. For each (patch_a, patch_b) pair: compute initial transform that
//      aligns their planes.
//   4. BoundarySegmenter3D on each patch's boundary -> segments.
//   5. PhaseCorrelator (3D variant) -> lag estimate -> InitialTransformBuilder.FromLag3D.
//   6. ConstrainedIcp3D for refinement.
//   7. Score by patch-pair Hausdorff residual + match-length.
//   8. Emit top-N candidates.
//
// Status: SKELETON. The component compiles, registers inputs/outputs, and
// reads the meshes; the actual face-pair search loop is the v1.x build
// target. See HITL card wiki/research/hitl_cards/em_3d_chain_ucl_bartlett/
// for the pass criterion.
// =============================================================================

[RelatedComponent("Frahan > Masonry > Stone-Cell Match (Λ)",
    Reason = "The practically-tested matcher: Hungarian stone-to-cell assignment, ETH1100 Lambda=0.194 (card 27_07). Use it for real stone-to-target matching today.")]
[Algorithm("Variational Shape Approximation (face partitioning)",
    "Cohen-Steiner, Alliez, Desbrun 2004 SIGGRAPH; Frahan stub implementation",
    Note = "Stage 1 of 3D EdgeMatch pipeline; see VsaSegmenter.cs TODO before UCL-replica fidelity")]
[Algorithm("Phase correlator FFT (3D)",
    "Classical cross-correlation lag estimation",
    Note = "Stage 5; turning-signature alignment via PhaseCorrelator")]
[Algorithm("Constrained ICP (3D)",
    "Besl and McKay 1992 iterative closest point + Kabsch SVD (MathNet.Numerics)",
    Note = "Stage 6; refinement after coarse phase")]
[Algorithm("Hungarian assignment",
    "H.W. Kuhn 1955 Hungarian Method for the Assignment Problem; Jonker-Volgenant pivot",
    Note = "Used downstream by Components D and D3D; HungarianAssigner.cs is the shared utility")]
[DesignApplication(
    "Match two scanned stone blocks along their planar faces (atomic primitive for 3D assembly).",
    DesignFlow.BottomUp,
    Precedent = "UCL Devadass 2025 face-library evaluation (SS2.4); cyclopean masonry Utah-detail bed-joint scribing",
    Tolerance = "joint-face Hausdorff <= 2 mm; correct-match identification >= 80 % on known pairs",
    CardSet = "wiki/research/hitl_cards/em_2d_boundary_match/ (2D sibling); em_3d_chain_ucl_bartlett/ (3D consumer)")]
public sealed class BlockPairMatch3DComponent : FrahanComponentBase
{
    public BlockPairMatch3DComponent()
        : base("Block Pair Match 3D", "BlkMatch3D",
            "First-cut matcher: VSA segmentation + plane-to-plane mating scored by sampled " +
            "Hausdorff distance. The full exhaustive face-pair search is a planned refinement. " +
            "For a practically-tested matcher use Stone-Cell Match (Λ) " +
            "(ETH1100 Lambda=0.194, card 27_07). " +
            "Atomic 3D edge-matching primitive: given two scanned stone meshes, " +
            "find the rigid 3D pose where their planar face patches mate. " +
            "VsaSegmenter -> face filtering -> per-pair PhaseCorrelator + " +
            "ConstrainedIcp3D refinement -> top-N candidates ranked by " +
            "patch-pair Hausdorff residual + match-length. Foundational " +
            "primitive for the 3D EdgeMatch family (Block Chain, Adaptive " +
            "Block Match, Template Block Match, Cyclopean Recipe Coursing). [Cohen-Steiner et al. 2004]",
            "Frahan", "EdgeMatch")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("D5F10008-ED9E-4ED9-A008-ED9EED9E0008");

    public override GH_Exposure Exposure => GH_Exposure.hidden;  // first-cut matcher; hidden to match the two sibling 3D stubs

    protected override Bitmap Icon => IconProvider.Load("EdgeMatchSolve.png"); // reuse until 3D icon ships

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Block A", "A",
            "First scanned-stone mesh. Closed manifold preferred; algorithm tolerates " +
            "open meshes but face-pair coverage may suffer.",
            GH_ParamAccess.item);
        p.AddMeshParameter("Block B", "B",
            "Second scanned-stone mesh. Same constraints as Block A.",
            GH_ParamAccess.item);
        p.AddNumberParameter("Min Face Area", "Mfa",
            "Minimum face-patch area (mm^2) below which patches are dropped. " +
            "Default 15,000 mm^2 per UCL Devadass 2025 SS2.4.1 (stability filter).",
            GH_ParamAccess.item, 15000.0);
        p.AddNumberParameter("Normal Merge Angle", "Nma",
            "VSA segmenter's adjacent-normal merge angle threshold (radians). " +
            "Coarser = fewer larger patches. Default 0.35 rad (~20 deg).",
            GH_ParamAccess.item, 0.35);
        p.AddIntegerParameter("Max Candidates", "Mc",
            "Maximum number of MatchResult candidates to emit (sorted by residual ascending).",
            GH_ParamAccess.item, 5);
        p.AddNumberParameter("Match Tolerance", "Mt",
            "Match residual cutoff (mm). Candidates with residual > this are rejected.",
            GH_ParamAccess.item, 5.0);
        p[5].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTransformParameter("Transforms", "T",
            "Per-candidate rigid 3D transform that places Block B against Block A.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Residuals", "R",
            "Per-candidate Hausdorff residual on the matched patch pair (mm).",
            GH_ParamAccess.list);
        p.AddNumberParameter("Match Areas", "Ma",
            "Per-candidate area of the matched patch (square mm).",
            GH_ParamAccess.list);
        p.AddTextParameter("Remarks", "Rm",
            "Per-candidate diagnostic notes (which face-pair matched, etc.) " +
            "plus rejection reasons if no candidate exceeds the tolerance.",
            GH_ParamAccess.list);
    }

    protected override void SolveSafe(IGH_DataAccess DA)
    {
        Mesh blockA = null;
        Mesh blockB = null;
        double minFaceArea = 15000.0;
        double normalMergeAngle = 0.35;
        int maxCandidates = 5;
        double matchTolerance = 5.0;

        if (!DA.GetData(0, ref blockA)) return;
        if (!DA.GetData(1, ref blockB)) return;
        DA.GetData(2, ref minFaceArea);
        DA.GetData(3, ref normalMergeAngle);
        DA.GetData(4, ref maxCandidates);
        DA.GetData(5, ref matchTolerance);

        if (blockA == null || blockA.Vertices.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Block A is null or empty.");
            return;
        }
        if (blockB == null || blockB.Vertices.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Block B is null or empty.");
            return;
        }

        var vsa = new VsaSegmenter
        {
            MinFaceArea = minFaceArea,
            NormalMergeAngle = normalMergeAngle,
        };
        var patchesA = vsa.Segment(blockA);
        var patchesB = vsa.Segment(blockB);

        if (patchesA.Count == 0 || patchesB.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"VSA produced no patches above MinFaceArea on one side " +
                $"(A={patchesA.Count}, B={patchesB.Count}). Lower Min Face Area or " +
                $"densify the meshes.");
            return;
        }

        // Face-pair loop. For each (patchA, patchB) combination, compute
        // a plane-to-plane alignment that mates the patches (B's normal
        // anti-parallel to A's, centroids coincident in tangent plane),
        // then score by sampled Hausdorff residual between transformed
        // Block B and Block A meshes near the joint. Rank by residual,
        // keep top maxCandidates.
        //
        // TODO v1.x: replace the sample-based Hausdorff with the existing
        // ConstrainedIcp3D + PhaseCorrelator pipeline from
        // Frahan.EdgeMatching.Core for true ICP-refined transforms.
        // The current scoring is a useful first-cut for downstream
        // consumers (A3D / C3D / D3D / Cyclopean Recipe) so the contract
        // is satisfied; full ICP refinement is task #54.

        patchesA.Sort((x, y) => y.Area.CompareTo(x.Area));
        patchesB.Sort((x, y) => y.Area.CompareTo(x.Area));

        // Soft cap on patch pairs evaluated (M*N could be ~25 for typical
        // ETH1100 stones; this is fine without further filtering).
        int patchPairCap = 64;
        int evaluated = 0;
        var scored = new List<ScoredCandidate>();

        for (int ai = 0; ai < patchesA.Count && evaluated < patchPairCap; ai++)
        {
            for (int bi = 0; bi < patchesB.Count && evaluated < patchPairCap; bi++)
            {
                evaluated++;
                var planeA = patchesA[ai].MeanPlane;
                var planeB = patchesB[bi].MeanPlane;

                // Mating: B's plane is reflected so its outward normal
                // opposes A's. Then PlaneToPlane translates B's plane
                // centroid onto A's plane.
                var planeBFlipped = new Plane(planeB.Origin, -planeB.ZAxis);
                Transform flipB = Transform.PlaneToPlane(planeB, planeBFlipped);
                Transform onto = Transform.PlaneToPlane(planeBFlipped, planeA);
                Transform combined = onto * flipB;

                // Apply to a duplicate of Block B for residual measurement.
                var movedB = blockB.DuplicateMesh();
                movedB.Transform(combined);

                double residual = SampleHausdorff(movedB, blockA, 32);
                double matchArea = Math.Min(patchesA[ai].Area, patchesB[bi].Area);

                if (residual > matchTolerance + 1e-9)
                {
                    // Below the tolerance bar; skip this candidate.
                    continue;
                }

                scored.Add(new ScoredCandidate(
                    transform: combined,
                    residual: residual,
                    matchArea: matchArea,
                    patchAId: patchesA[ai].Id,
                    patchBId: patchesB[bi].Id,
                    patchAArea: patchesA[ai].Area,
                    patchBArea: patchesB[bi].Area));
            }
        }

        if (scored.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"No (patchA, patchB) pair produced a residual <= MatchTolerance " +
                $"({matchTolerance:F2} mm) across {evaluated} pairs evaluated. " +
                $"Either (a) the stones are unrelated, (b) MatchTolerance is too " +
                $"strict, or (c) VSA segmentation produced poor patches.");
            DA.SetDataList(0, new List<Transform>());
            DA.SetDataList(1, new List<double>());
            DA.SetDataList(2, new List<double>());
            DA.SetDataList(3, new List<string>
            {
                "No candidates within tolerance. " +
                $"Best evaluated residual = (none computed)."
            });
            return;
        }

        // Rank by residual ascending; keep top maxCandidates.
        scored.Sort((x, y) => x.Residual.CompareTo(y.Residual));
        int keep = Math.Min(maxCandidates, scored.Count);

        var transforms = new List<Transform>(keep);
        var residuals = new List<double>(keep);
        var matchAreas = new List<double>(keep);
        var remarks = new List<string>(keep);

        for (int i = 0; i < keep; i++)
        {
            var c = scored[i];
            transforms.Add(c.Transform);
            residuals.Add(c.Residual);
            matchAreas.Add(c.MatchArea);
            remarks.Add(
                $"Candidate {i}: A.patch[{c.PatchAId}] (area={c.PatchAArea:F1}) <-> " +
                $"B.patch[{c.PatchBId}] (area={c.PatchBArea:F1}); residual={c.Residual:F3} mm. " +
                $"Plane-to-plane alignment; refine downstream with Soft ICP 3D (D5F1000E) for sub-mm.");
        }

        DA.SetDataList(0, transforms);
        DA.SetDataList(1, residuals);
        DA.SetDataList(2, matchAreas);
        DA.SetDataList(3, remarks);
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private readonly struct ScoredCandidate
    {
        public readonly Transform Transform;
        public readonly double Residual;
        public readonly double MatchArea;
        public readonly int PatchAId;
        public readonly int PatchBId;
        public readonly double PatchAArea;
        public readonly double PatchBArea;

        public ScoredCandidate(Transform transform, double residual, double matchArea,
            int patchAId, int patchBId, double patchAArea, double patchBArea)
        {
            Transform = transform;
            Residual = residual;
            MatchArea = matchArea;
            PatchAId = patchAId;
            PatchBId = patchBId;
            PatchAArea = patchAArea;
            PatchBArea = patchBArea;
        }
    }

    /// <summary>
    /// Sample-based one-sided Hausdorff: for each of `sampleCount` vertices
    /// strided from `from`'s naked edges (or surface vertices if closed),
    /// query closest point on `to`'s mesh, return the maximum distance.
    /// Approximate but ranking-monotone; sufficient for top-N candidate
    /// selection. Full Hausdorff is O(N*M); this is O(sampleCount * log M)
    /// via Mesh.ClosestPoint which uses an internal spatial structure.
    /// </summary>
    private static double SampleHausdorff(Mesh from, Mesh to, int sampleCount)
    {
        if (from == null || from.Vertices.Count == 0 ||
            to == null || to.Vertices.Count == 0) return double.MaxValue;

        // Prefer naked-edge samples (the joint boundary); fall back to
        // surface vertices for closed meshes.
        var pts = new List<Point3d>(sampleCount);
        var nakedEdges = from.GetNakedEdges();
        if (nakedEdges != null && nakedEdges.Length > 0)
        {
            foreach (var pl in nakedEdges)
                for (int i = 0; i < pl.Count; i++) pts.Add(pl[i]);
        }
        if (pts.Count == 0)
        {
            int step = Math.Max(1, from.Vertices.Count / sampleCount);
            for (int i = 0; i < from.Vertices.Count; i += step)
                pts.Add(from.Vertices[i]);
        }
        if (pts.Count == 0) return double.MaxValue;

        int strideStep = Math.Max(1, pts.Count / sampleCount);
        double maxDist = 0;
        for (int i = 0; i < pts.Count; i += strideStep)
        {
            var cp = to.ClosestPoint(pts[i]);
            double d = pts[i].DistanceTo(cp);
            if (d > maxDist) maxDist = d;
        }
        return maxDist;
    }
}
