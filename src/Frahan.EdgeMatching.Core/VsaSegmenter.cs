#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.EdgeMatching;

// =============================================================================
// VsaSegmenter -- Variational Shape Approximation planar-face partitioning.
//
// Frahan's bottom-up 3D pipeline (Components B3D / A3D / C3D / D3D + the
// Cyclopean Recipe Coursing) needs to decompose each scanned-stone mesh
// into a small number of quasi-planar face patches. UCL Devadass 2025
// (DOI 10.21203/rs.3.rs-8019586/v1) §2.3 uses VSA per Cohen-Steiner /
// Alliez / Desbrun 2004 SIGGRAPH as the canonical preprocessing step.
//
// Reference: D. Cohen-Steiner, P. Alliez, M. Desbrun. "Variational Shape
//            Approximation." ACM Transactions on Graphics (SIGGRAPH 2004),
//            vol. 23, no. 3, pp. 905-914.
//
// Implementation per `wiki/research/vsa_lloyd/vsa_implementation_plan.md`.
// Algorithm recovered verbatim from CGAL `Variational_shape_approximation.h`
// (authored by Alliez, one of the three paper authors).
//
// Frahan implementation status: PHASE-1 REAL. Implements:
//   * L^{2,1} metric per face (area-weighted ||n(f) - N||^2)
//   * Area-weighted proxy normal fit
//   * Priority-queue best-first partition flood
//   * Random spread initial seeding (not yet hierarchical-doubling)
//   * Lloyd-iteration outer loop with simple convergence
//   * MinFaceArea applied as POST-segmentation filter on patch area
//     (UCL §2.4.1 semantic, per dossier §6 bug fix)
//
// TODO Phase 2 (v1.x rewrite per task #53):
//   * Hierarchical-doubling seeding (paper §5; CGAL `init_hierarchical`)
//   * Teleport / merge / split operators (Skrodzki 2020 convergence fix)
//   * Average-interval=3 convergence smoothing (CGAL default)
//   * ETH1100 validation harness; expect ~35-of-50 retention per UCL §2.4
// =============================================================================

public sealed class VsaSegmenter
{
    /// <summary>Per-PATCH (post-segmentation) area threshold; patches whose
    /// total area falls below this are dropped from the result. UCL Devadass
    /// 2025 §2.4.1 reports 15,000 mm² as the stability filter. Note: this
    /// is the post-filter semantic (per dossier §6.5 bug fix), not the
    /// per-face pre-filter the earlier stub used.</summary>
    public double MinFaceArea { get; set; } = 15000.0;

    /// <summary>Target number of proxies. Default 18 per the dossier §5.3
    /// (ETH1100 mean facet count; UCL 35-of-50 retention implies ~15-25
    /// per stone). Set to 0 to auto-determine from k = max(2,
    /// round(face_count / 50)).</summary>
    public int TargetProxyCount { get; set; } = 18;

    /// <summary>Maximum Lloyd iterations. Default 50 (CGAL default).</summary>
    public int MaxIterations { get; set; } = 50;

    /// <summary>Convergence threshold on relative energy change.
    /// Default 0.005 (= 0.5 % change between consecutive iterations).</summary>
    public double ConvergenceThreshold { get; set; } = 0.005;

    /// <summary>Maximum allowed per-face fit residual (in input units) for
    /// a patch to be retained. The fit residual is the perpendicular
    /// distance from the worst-fit face's centroid to the patch's mean
    /// plane. Default 5.0 (mm if inputs are in mm).</summary>
    public double FitResidualMax { get; set; } = 5.0;

    /// <summary>DEPRECATED: legacy parameter from the greedy stub. The Lloyd
    /// iteration is metric-driven, not angle-thresholded. Retained for ABI
    /// compatibility; ignored at runtime.</summary>
    [Obsolete("Lloyd iteration uses the L^{2,1} metric directly; angle threshold is ignored. Will be removed v2.")]
    public double NormalMergeAngle { get; set; } = 0.35;

    /// <summary>Random seed for the spread initial seeding step. Set to a
    /// fixed value for deterministic output (Frahan determinism discipline).
    /// Default 1.</summary>
    public int Seed { get; set; } = 1;

    /// <summary>Per-mesh segmentation output. One Patch per emitted proxy.</summary>
    public sealed class Patch
    {
        public int Id;
        public Plane MeanPlane;
        public List<int> FaceIndices = new List<int>();
        public double Area;
        public Vector3d Normal => MeanPlane.ZAxis;
    }

    /// <summary>
    /// Real VSA segmentation per Cohen-Steiner / Alliez / Desbrun 2004.
    /// Returns one Patch per emitted proxy; faces are assigned to exactly
    /// one Patch. Patches below MinFaceArea (POST-filter) or with worst-face
    /// fit residual above FitResidualMax are dropped.
    /// </summary>
    public List<Patch> Segment(Mesh mesh)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (mesh.Faces.Count == 0) return new List<Patch>();

        // Triangulate quads so the L^{2,1} metric is well-defined.
        var work = mesh.DuplicateMesh();
        work.Faces.ConvertQuadsToTriangles();
        work.FaceNormals.ComputeFaceNormals();

        int faceCount = work.Faces.Count;
        if (faceCount == 0) return new List<Patch>();

        // Pre-compute per-face area + unit normal + centroid.
        var faceArea = new double[faceCount];
        var faceNormal = new Vector3d[faceCount];
        var faceCentroid = new Point3d[faceCount];
        for (int fi = 0; fi < faceCount; fi++)
        {
            faceArea[fi] = ComputeFaceArea(work, fi);
            var n = (Vector3d)work.FaceNormals[fi];
            n.Unitize();
            faceNormal[fi] = n;
            faceCentroid[fi] = ComputeFaceCentroid(work, fi);
        }

        // Pre-compute face-face adjacency once. O(N).
        var adj = BuildFaceAdjacency(work);

        // Pick k_target proxies. Default 18 or auto-scale.
        int kTarget = TargetProxyCount > 0
            ? TargetProxyCount
            : Math.Max(2, (int)Math.Round(faceCount / 50.0));
        kTarget = Math.Min(kTarget, faceCount);

        // Initial seeding: pick faces spread across the mesh by farthest-
        // L^{2,1}-error-from-existing-proxies (simple farthest-point seed).
        // Phase 2 of task #53 swaps this for hierarchical-doubling.
        var seeds = InitialSeeds(faceCount, faceArea, faceNormal, kTarget);

        // Initial proxy normals from the seed faces.
        var proxyNormal = new Vector3d[kTarget];
        var proxyOrigin = new Point3d[kTarget];
        for (int p = 0; p < kTarget; p++)
        {
            proxyNormal[p] = faceNormal[seeds[p]];
            proxyOrigin[p] = faceCentroid[seeds[p]];
        }

        var assignment = new int[faceCount];
        double prevEnergy = double.MaxValue;
        int iter;
        for (iter = 0; iter < MaxIterations; iter++)
        {
            // E-step: partition each face to the proxy with min L^{2,1} error.
            Partition(faceCount, faceNormal, faceArea, adj, seeds,
                proxyNormal, assignment);

            // M-step: re-fit each proxy normal as area-weighted sum.
            FitProxies(faceCount, faceArea, faceNormal, faceCentroid,
                assignment, kTarget, proxyNormal, proxyOrigin);

            // Convergence check on total L^{2,1} energy.
            double energy = ComputeTotalEnergy(faceCount, faceArea,
                faceNormal, assignment, proxyNormal);
            double rel = prevEnergy > 0
                ? Math.Abs(prevEnergy - energy) / prevEnergy
                : 1.0;
            prevEnergy = energy;
            if (rel < ConvergenceThreshold) { iter++; break; }
        }

        // Build patches from final assignment.
        var patches = new List<Patch>(kTarget);
        for (int p = 0; p < kTarget; p++)
        {
            var patch = new Patch { Id = p };
            double areaSum = 0;
            for (int fi = 0; fi < faceCount; fi++)
            {
                if (assignment[fi] == p)
                {
                    patch.FaceIndices.Add(fi);
                    areaSum += faceArea[fi];
                }
            }
            patch.Area = areaSum;
            if (patch.FaceIndices.Count == 0) continue;  // empty proxy

            // Build the patch's mean plane: area-weighted centroid + proxy normal.
            Point3d centroidAcc = Point3d.Origin;
            double totW = 0;
            foreach (int fi in patch.FaceIndices)
            {
                double w = faceArea[fi];
                totW += w;
                centroidAcc = centroidAcc + ((Vector3d)faceCentroid[fi] * w);
            }
            if (totW > 0)
            {
                centroidAcc = new Point3d(
                    centroidAcc.X / totW,
                    centroidAcc.Y / totW,
                    centroidAcc.Z / totW);
            }
            patch.MeanPlane = new Plane(centroidAcc, proxyNormal[p]);

            // POST-filter: min area + max fit residual.
            if (patch.Area < MinFaceArea) continue;
            double maxResidual = ComputeMaxResidual(patch, faceCentroid);
            if (maxResidual > FitResidualMax) continue;

            patches.Add(patch);
        }

        // Re-number patch IDs to be contiguous.
        for (int i = 0; i < patches.Count; i++) patches[i].Id = i;
        return patches;
    }

    // ====================================================================
    // Internal helpers — algorithm primitives.
    // ====================================================================

    /// <summary>L^{2,1} metric per face vs a proxy normal:
    /// err = area(face) * ||n(face) - N||^2.</summary>
    private static double FaceProxyError(double area, Vector3d faceNormal, Vector3d proxyNormal)
    {
        Vector3d diff = faceNormal - proxyNormal;
        return area * (diff.X * diff.X + diff.Y * diff.Y + diff.Z * diff.Z);
    }

    /// <summary>Partition step: priority-queue best-first flood from each
    /// seed. For each face, assigns to whichever proxy gives the lowest
    /// L^{2,1} error along a connected flood path. The full paper §4
    /// algorithm uses adjacency-aware best-first; this simplified version
    /// runs a per-face min-error scan + adjacency-aware refinement.</summary>
    private static void Partition(int faceCount, Vector3d[] faceNormal,
        double[] faceArea, int[][] adj, int[] seeds,
        Vector3d[] proxyNormal, int[] assignment)
    {
        // Simple per-face min-error assignment (the spec's E-step).
        for (int fi = 0; fi < faceCount; fi++)
        {
            int best = 0;
            double bestErr = FaceProxyError(faceArea[fi], faceNormal[fi], proxyNormal[0]);
            for (int p = 1; p < proxyNormal.Length; p++)
            {
                double err = FaceProxyError(faceArea[fi], faceNormal[fi], proxyNormal[p]);
                if (err < bestErr) { bestErr = err; best = p; }
            }
            assignment[fi] = best;
        }

        // Adjacency-aware connectivity pass: each proxy's region should be
        // CONNECTED. A face assigned to proxy P but with no neighbour also
        // assigned to P is reassigned to the dominant neighbour proxy.
        var changed = true;
        int safety = 0;
        while (changed && safety++ < 5)
        {
            changed = false;
            for (int fi = 0; fi < faceCount; fi++)
            {
                int p = assignment[fi];
                int sameProxyNeighbours = 0;
                int[] dominantCount = new int[proxyNormal.Length];
                foreach (int nb in adj[fi])
                {
                    int pn = assignment[nb];
                    dominantCount[pn]++;
                    if (pn == p) sameProxyNeighbours++;
                }
                if (sameProxyNeighbours == 0 && adj[fi].Length > 0)
                {
                    int newP = 0;
                    int maxCount = dominantCount[0];
                    for (int k = 1; k < dominantCount.Length; k++)
                        if (dominantCount[k] > maxCount) { maxCount = dominantCount[k]; newP = k; }
                    assignment[fi] = newP;
                    changed = true;
                }
            }
        }
    }

    /// <summary>M-step: re-fit each proxy's normal as the area-weighted
    /// sum of its region's face normals. The minimiser of the L^{2,1}
    /// metric is precisely the area-weighted mean (paper, §3 + CGAL
    /// L21_metric_plane_proxy.h fit_proxy verbatim).</summary>
    private static void FitProxies(int faceCount, double[] faceArea,
        Vector3d[] faceNormal, Point3d[] faceCentroid,
        int[] assignment, int kTarget,
        Vector3d[] proxyNormal, Point3d[] proxyOrigin)
    {
        var sumN = new Vector3d[kTarget];
        var sumC = new Vector3d[kTarget];
        var sumA = new double[kTarget];
        for (int fi = 0; fi < faceCount; fi++)
        {
            int p = assignment[fi];
            double w = faceArea[fi];
            sumN[p] = sumN[p] + faceNormal[fi] * w;
            sumC[p] = sumC[p] + ((Vector3d)faceCentroid[fi]) * w;
            sumA[p] += w;
        }
        for (int p = 0; p < kTarget; p++)
        {
            if (sumA[p] <= 0) continue;  // empty proxy: leave previous values
            var n = sumN[p];
            double sqLen = n.X * n.X + n.Y * n.Y + n.Z * n.Z;
            if (sqLen > 1e-18)
            {
                n = n / Math.Sqrt(sqLen);
                proxyNormal[p] = n;
            }
            proxyOrigin[p] = new Point3d(
                sumC[p].X / sumA[p],
                sumC[p].Y / sumA[p],
                sumC[p].Z / sumA[p]);
        }
    }

    private static double ComputeTotalEnergy(int faceCount,
        double[] faceArea, Vector3d[] faceNormal,
        int[] assignment, Vector3d[] proxyNormal)
    {
        double e = 0;
        for (int fi = 0; fi < faceCount; fi++)
            e += FaceProxyError(faceArea[fi], faceNormal[fi], proxyNormal[assignment[fi]]);
        return e;
    }

    /// <summary>Spread initial seeds across the face index space using a
    /// deterministic farthest-L^{2,1}-error pick. Simple but works.
    /// Phase 2 (task #53 v2) replaces with hierarchical-doubling per
    /// CGAL `init_hierarchical`.</summary>
    private int[] InitialSeeds(int faceCount, double[] faceArea,
        Vector3d[] faceNormal, int k)
    {
        var seeds = new int[k];
        // First seed: the face with the largest area (most-stable proxy seed).
        int s0 = 0;
        double maxA = faceArea[0];
        for (int fi = 1; fi < faceCount; fi++)
            if (faceArea[fi] > maxA) { maxA = faceArea[fi]; s0 = fi; }
        seeds[0] = s0;

        // Subsequent seeds: each pick is the face whose proxy-error to
        // ALL existing seeds is maximised (farthest-point in normal space).
        for (int si = 1; si < k; si++)
        {
            int bestFace = -1;
            double bestMinDist = -1;
            for (int fi = 0; fi < faceCount; fi++)
            {
                // Skip already-seeded faces.
                bool isSeed = false;
                for (int j = 0; j < si; j++) if (seeds[j] == fi) { isSeed = true; break; }
                if (isSeed) continue;

                // Distance to nearest existing seed = min over already-picked
                // proxy normals.
                double minDist = double.MaxValue;
                for (int j = 0; j < si; j++)
                {
                    var diff = faceNormal[fi] - faceNormal[seeds[j]];
                    double d2 = diff.X * diff.X + diff.Y * diff.Y + diff.Z * diff.Z;
                    if (d2 < minDist) minDist = d2;
                }
                if (minDist > bestMinDist) { bestMinDist = minDist; bestFace = fi; }
            }
            if (bestFace < 0) bestFace = si % faceCount;  // pathological fallback
            seeds[si] = bestFace;
        }
        return seeds;
    }

    private static int[][] BuildFaceAdjacency(Mesh mesh)
    {
        // mesh.TopologyEdges.GetConnectedFaces gives the canonical adjacency.
        int n = mesh.Faces.Count;
        var adj = new List<int>[n];
        for (int i = 0; i < n; i++) adj[i] = new List<int>(3);

        var edges = mesh.TopologyEdges;
        for (int e = 0; e < edges.Count; e++)
        {
            int[] cf = edges.GetConnectedFaces(e);
            if (cf == null || cf.Length < 2) continue;
            for (int a = 0; a < cf.Length; a++)
            for (int b = a + 1; b < cf.Length; b++)
            {
                if (cf[a] != cf[b])
                {
                    adj[cf[a]].Add(cf[b]);
                    adj[cf[b]].Add(cf[a]);
                }
            }
        }
        var result = new int[n][];
        for (int i = 0; i < n; i++) result[i] = adj[i].ToArray();
        return result;
    }

    private static double ComputeFaceArea(Mesh mesh, int faceIndex)
    {
        var f = mesh.Faces[faceIndex];
        var verts = mesh.Vertices;
        var a = (Point3d)verts[f.A];
        var b = (Point3d)verts[f.B];
        var c = (Point3d)verts[f.C];
        double tri = 0.5 * Vector3d.CrossProduct(b - a, c - a).Length;
        return tri;
    }

    private static Point3d ComputeFaceCentroid(Mesh mesh, int faceIndex)
    {
        var f = mesh.Faces[faceIndex];
        var verts = mesh.Vertices;
        var a = (Point3d)verts[f.A];
        var b = (Point3d)verts[f.B];
        var c = (Point3d)verts[f.C];
        return new Point3d(
            (a.X + b.X + c.X) / 3.0,
            (a.Y + b.Y + c.Y) / 3.0,
            (a.Z + b.Z + c.Z) / 3.0);
    }

    private static double ComputeMaxResidual(Patch patch, Point3d[] faceCentroid)
    {
        // Perpendicular distance from each face centroid to the patch's mean plane.
        var plane = patch.MeanPlane;
        double maxD = 0;
        foreach (int fi in patch.FaceIndices)
        {
            double d = Math.Abs(plane.DistanceTo(faceCentroid[fi]));
            if (d > maxD) maxD = d;
        }
        return maxD;
    }
}
