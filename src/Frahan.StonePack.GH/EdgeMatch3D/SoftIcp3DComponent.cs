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
// SoftIcp3DComponent (GUID D5F1000E)
//
// Standalone GH wrapper around Frahan.EdgeMatching.SoftIcpRefiner.Refine3D.
//
// Per Libish 2026-05-31 directive: "Soft ICP 3D as a component. I want
// people to be able to use these components across workflows."
//
// This is the FIRST primitive in the new decomposition pattern (per
// wiki/specs/component_decomposition_plan.md SS3.5 Stage 5 / refine).
// Existing monolithic solvers (EdgeMatch Solve, KintsugiAssembly, etc.)
// call SoftIcpRefiner internally when AssemblyOptions.SoftIcpRefine = true.
// This component surfaces that primitive on the canvas so users can chain
// it INTO different workflows: voussoir-stone match refinement,
// cyclopean recipe per-stone refinement, Trencadis post-pack refinement,
// kintsugi reassembly polish, etc.
//
// Soft-ICP uses CPD (Myronenko-Song 2010) soft correspondence + EM
// weighted-Kabsch alternation + smooth non-penetration hinge. See the
// Core type's header for the full algorithm description.
//
// Inputs are fragment meshes + their rim/naked-edge samples at their
// CURRENT pose. The component samples each input mesh's naked edges
// automatically; the Anchored input pins fragment 0 by default (so the
// other fragments move relative to the anchor).
//
// Outputs per fragment: the refined Transform Delta (left-composed onto
// the input pose), the refined mesh, plus the global Report (mean rim
// gap, max penetration, contact-sample count, iterations).
// =============================================================================

[Algorithm("Soft-ICP / CPD weighted-Kabsch alternation",
    "Myronenko and Song 2010 Coherent Point Drift; Frahan EM closed-form M-step",
    Note = "Stage 5 refine in the decomposition plan; opt-in in EdgeMatch Solve via AssemblyOptions.SoftIcpRefine")]
[Algorithm("Penetration hinge (smooth non-penetration)",
    "Mesh.IsPointInside inside-test + smooth quadratic hinge per SettleContactComponent / OverlapResolver2D",
    Note = "Auto-folded into the contact M-step by redirecting penetrating samples' targets to neighbour surface")]
[Algorithm("Weighted Kabsch via 3x3 SVD",
    "Kabsch 1976 / 1978 algorithm; MathNet.Numerics SVD",
    Note = "Closed-form rigid alignment under per-sample confidence weights")]
[DesignApplication(
    "Refine the poses of placed 3D fragments so their rims touch without solids interpenetrating.",
    DesignFlow.Bridges,
    Precedent = "Myronenko-Song 2010 CPD; Frahan EM weighted-Kabsch closed-form per Soft ICP roadmap (Pillar A Phase 2+4)",
    Tolerance = "convergence translation < 1e-4 * objectScale; rotation < 1e-3 deg per fragment per iter; default 40 EM iters",
    CardSet = "wiki/research/hitl_cards/em_2d_trencadis_solve/ + em_3d_chain_ucl_bartlett/ + bu_kintsugi/ (multi-workflow primitive)")]
public sealed class SoftIcp3DComponent : FrahanComponentBase
{
    public SoftIcp3DComponent()
        : base("Soft ICP 3D", "SoftICP3D",
            "Refine the poses of 3D fragment meshes so their rims come into " +
            "CONTACT while their solids do not interpenetrate. EM weighted-" +
            "Kabsch over CPD soft correspondence + smooth penetration hinge. " +
            "The standalone primitive that EdgeMatch Solve / Kintsugi / Trencadis " +
            "/ Cyclopean Recipe / Voussoir Match all invoke internally; surface " +
            "on the canvas to chain into any custom workflow. [Myronenko & Song 2010]",
            "Frahan", "EdgeMatch")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("D5F1000E-ED9E-4ED9-A00E-ED9EED9E000E");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap Icon => IconProvider.Load("SoftIcp.png"); // placeholder

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Fragments", "F",
            "Placed fragment meshes at their CURRENT pose. Rim samples are taken " +
            "from each mesh's naked-edge / boundary loops automatically.",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Anchor Index", "Ai",
            "Index of the fragment to PIN (its pose stays fixed; others move " +
            "relative to it). Default 0. Set -1 to anchor none (free-floating refine).",
            GH_ParamAccess.item, 0);
        p.AddNumberParameter("Tau0 Factor", "T0",
            "Initial CPD temperature factor (tau0 = T0 * (median rim spacing)^2). " +
            "Larger = softer / wider correspondence. Default 4.0.",
            GH_ParamAccess.item, 4.0);
        p.AddNumberParameter("Tau Anneal", "Ta",
            "Geometric anneal factor (0,1) applied to tau each outer iter. Default 0.8.",
            GH_ParamAccess.item, 0.8);
        p.AddNumberParameter("Outlier Weight", "Ow",
            "Uniform-outlier pseudo-weight in the softmax denominator. " +
            "Default 0.01.",
            GH_ParamAccess.item, 0.01);
        p.AddIntegerParameter("Max Iterations", "Mi",
            "Maximum EM outer iterations. Default 40.",
            GH_ParamAccess.item, 40);
        p.AddNumberParameter("Sample Spacing", "Ss",
            "Arc-length sample spacing along naked edges (mm). Default 1.0 mm; " +
            "0 = auto from the assembly bbox.",
            GH_ParamAccess.item, 1.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTransformParameter("Delta", "D",
            "Per-fragment pose increment (left-composed onto the input pose). " +
            "Anchored fragments emit Transform.Identity.",
            GH_ParamAccess.list);
        p.AddMeshParameter("Refined Fragments", "RF",
            "Per-fragment refined mesh (input mesh with Delta applied).",
            GH_ParamAccess.list);
        p.AddNumberParameter("Mean Rim Gap", "MRG",
            "Mean nearest-neighbour rim gap across all matched neighbour rims " +
            "(global Report metric).",
            GH_ParamAccess.item);
        p.AddNumberParameter("Max Penetration", "MP",
            "Maximum penetration depth across all fragment pairs " +
            "(should be <= 0 or very small after a successful refine).",
            GH_ParamAccess.item);
        p.AddIntegerParameter("Contact Samples", "CS",
            "Count of rim samples on mating interfaces (drives the contact term).",
            GH_ParamAccess.item);
        p.AddIntegerParameter("Iterations", "I",
            "Outer EM iterations actually run.",
            GH_ParamAccess.item);
        p.AddTextParameter("Remarks", "R",
            "Per-fragment diagnostic notes + convergence flags.",
            GH_ParamAccess.list);
    }

    protected override void SolveSafe(IGH_DataAccess DA)
    {
        var meshes = new List<Mesh>();
        int anchorIndex = 0;
        double tau0Factor = 4.0;
        double tauAnneal = 0.8;
        double outlierWeight = 0.01;
        int maxIters = 40;
        double sampleSpacing = 1.0;

        if (!DA.GetDataList(0, meshes)) return;
        DA.GetData(1, ref anchorIndex);
        DA.GetData(2, ref tau0Factor);
        DA.GetData(3, ref tauAnneal);
        DA.GetData(4, ref outlierWeight);
        DA.GetData(5, ref maxIters);
        DA.GetData(6, ref sampleSpacing);

        if (meshes == null || meshes.Count < 2)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Soft ICP 3D needs at least 2 fragment meshes (otherwise nothing to refine against).");
            return;
        }

        // Build SoftIcpOptions
        var opt = new SoftIcpOptions
        {
            Tau0Factor = tau0Factor,
            TauAnneal = tauAnneal,
            OutlierWeight = outlierWeight,
            MaxIterations = maxIters,
        };

        // Build Fragments: sample each mesh's naked edges, keep mesh as Solid for penetration test.
        var fragments = new List<SoftIcpRefiner.Fragment>();
        var remarks = new List<string>();
        for (int i = 0; i < meshes.Count; i++)
        {
            var m = meshes[i];
            if (m == null || m.Vertices.Count == 0)
            {
                remarks.Add($"Fragment[{i}]: null/empty mesh, skipped.");
                fragments.Add(null);
                continue;
            }

            // Sample rim points from naked edges.
            var rimPts = SampleNakedEdges(m, sampleSpacing);
            if (rimPts.Length == 0)
            {
                remarks.Add(
                    $"Fragment[{i}]: no naked edges (closed mesh); using surface " +
                    $"vertex sample (n={Math.Min(m.Vertices.Count, 200)}).");
                rimPts = SampleSurfaceVertices(m, 200);
            }

            // Ensure mesh is closed for the inside-test; if not, fall through and let
            // the refiner's solid=null branch operate without the penetration term.
            Mesh solid = m.IsClosed ? m.DuplicateMesh() : null;

            bool anchored = (i == anchorIndex);
            var frag = new SoftIcpRefiner.Fragment(
                id: i.ToString(),
                rimPoints: rimPts,
                solid: solid,
                contour2D: null,
                anchored: anchored);
            fragments.Add(frag);
            remarks.Add(
                $"Fragment[{i}]: {rimPts.Length} rim samples, " +
                $"{(solid != null ? "closed (penetration enabled)" : "open (rim-only)")}, " +
                $"anchored={anchored}.");
        }

        // Filter out nulls.
        var liveFragments = new List<SoftIcpRefiner.Fragment>();
        foreach (var f in fragments) if (f != null) liveFragments.Add(f);

        if (liveFragments.Count < 2)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "After filtering null/empty meshes, fewer than 2 fragments remain. Cannot refine.");
            return;
        }

        // Run the refine.
        SoftIcpRefiner.Report report;
        try
        {
            report = SoftIcpRefiner.Refine3D(liveFragments, opt);
        }
        catch (Exception e)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Soft ICP 3D refine failed: " + e.Message);
            return;
        }

        // Build outputs.
        var deltas = new List<Transform>();
        var refined = new List<Mesh>();
        int liveIdx = 0;
        for (int i = 0; i < fragments.Count; i++)
        {
            if (fragments[i] == null)
            {
                deltas.Add(Transform.Identity);
                refined.Add(meshes[i]);
                continue;
            }
            var delta = liveFragments[liveIdx].Delta;
            deltas.Add(delta);
            var rm = meshes[i].DuplicateMesh();
            rm.Transform(delta);
            refined.Add(rm);
            liveIdx++;
        }

        DA.SetDataList(0, deltas);
        DA.SetDataList(1, refined);
        DA.SetData(2, report.MeanRimGap);
        DA.SetData(3, report.MaxPenetration);
        DA.SetData(4, report.ContactSamples);
        DA.SetData(5, report.Iterations);
        DA.SetDataList(6, remarks);
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static Point3d[] SampleNakedEdges(Mesh m, double spacing)
    {
        var pts = new List<Point3d>();
        var nakedPolylines = m.GetNakedEdges();
        if (nakedPolylines == null) return pts.ToArray();
        foreach (var pl in nakedPolylines)
        {
            if (pl == null || pl.Count < 2) continue;
            double total = pl.Length;
            if (total <= 0 || spacing <= 0)
            {
                foreach (var v in pl) pts.Add(v);
                continue;
            }
            int n = Math.Max(2, (int)Math.Round(total / spacing));
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / n;
                pts.Add(pl.PointAt(t * (pl.Count - 1)));
            }
        }
        return pts.ToArray();
    }

    private static Point3d[] SampleSurfaceVertices(Mesh m, int cap)
    {
        int n = Math.Min(m.Vertices.Count, cap);
        var pts = new Point3d[n];
        int step = Math.Max(1, m.Vertices.Count / n);
        for (int i = 0; i < n; i++)
        {
            int idx = Math.Min(m.Vertices.Count - 1, i * step);
            pts[i] = m.Vertices[idx];
        }
        return pts;
    }
}
