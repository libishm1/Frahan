#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Frahan.GH.Attributes;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Interfaces;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH;

// =============================================================================
// CoACD test components - Grasshopper surfaces that exercise the CoACD
// native shim end-to-end. Mirrors the CGAL test-component pattern in
// CgalTestComponents.cs (same Mesh<->MeshSnapshot helpers via CgalConvert,
// same IsAvailable / Version probe pattern, same ribbon-tab approach).
//
// Ribbon: "Frahan" / "CoACD" sibling subcategory next to "CGAL".
//
// Workflow note: when the loaded shim was built with
// FRAHAN_COACD_WITH_3RD_PARTY=OFF, inputs MUST be 2-manifold. Pre-clean
// scanned input through Mesh Repair (CGAL) before feeding it here. The
// "Backend" output reports build-flag status so users can see at a glance.
// =============================================================================

[Algorithm("Collision-aware approximate convex decomposition", "Wei, J., Liu, M., Wang, J. et al. (2022). Approximate Convex Decomposition for 3D Meshes with Collision-Aware Concavity and Tree Search. SIGGRAPH 2022", WikiPath = "wiki/index/references.md")]
[RelatedComponent("Frahan > Masonry > Masonry Assembly", Reason = "Convex decomposition feeds collision-detection for masonry block assembly.")]
[RelatedComponent("Frahan > Masonry > Auto Interfaces", Reason = "Convex parts simplify interface auto-detection between non-convex blocks.")]
[RelatedComponent("Frahan > Mesh > Mesh Diagnostics", Reason = "Diagnose mesh convexity before decomposing.")]
[DesignApplication(
    "Approximate convex decomposition via the CoACD native shim",
    DesignFlow.Bridges)]
public sealed class CoacdMeshDecomposeComponent : GH_Component
{
    public CoacdMeshDecomposeComponent()
        : base("Mesh Decompose (CoACD)", "DecomposeCoacd",
            "Approximate convex decomposition via the CoACD native shim. " +
            "Input must be 2-manifold for the lightweight build (no " +
            "manifold preprocess); pre-clean with Mesh Repair (CGAL) if " +
            "input is non-manifold and the OpenVDB-equipped build is not " +
            "loaded. " +
            "Wraps CoACD (Wei et al. 2022).",
            "Frahan", "Lab")
    {
    }

    // Fresh GUID, distinct from the CGAL series (F2D000A?-CADC-...).
    public override Guid ComponentGuid => new Guid("F2D000B0-C0AC-4F2D-A0B0-7E60C0AC1DB0");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("CoacdDecompose.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        // Primary inputs ----------------------------------------------------
        pManager.AddMeshParameter("Mesh", "M",
            "Input mesh. Must be 2-manifold when running against the " +
            "lightweight (WITH_3RD_PARTY_LIBS=OFF) shim build. The full " +
            "build accepts non-manifold input via OpenVDB preprocessing.",
            GH_ParamAccess.item);
        pManager.AddNumberParameter("Threshold", "T",
            "Concavity threshold. Lower = more pieces, finer fit. " +
            "Default 0.05 (normalized [0..1]) or 0.05 metres if Real " +
            "Metric is true.",
            GH_ParamAccess.item, 0.05);
        pManager.AddIntegerParameter("Max Hulls", "N",
            "Cap on output piece count. -1 = unlimited.",
            GH_ParamAccess.item, -1);
        pManager.AddIntegerParameter("Preprocess", "P",
            "0 = auto, 1 = on, 2 = off. Auto runs OpenVDB-based " +
            "manifold-isation only when input is non-manifold (requires " +
            "WITH_3RD_PARTY_LIBS=ON build).",
            GH_ParamAccess.item, 0);
        pManager.AddBooleanParameter("Real Metric", "RM",
            "When true, Threshold is interpreted as metres rather than " +
            "CoACD's normalized [0..1] units. Use for statue-scale input.",
            GH_ParamAccess.item, false);
        pManager.AddBooleanParameter("Run", "Run",
            "Set true to compute.", GH_ParamAccess.item, false);

        // Advanced (MCTS tuning + repro) ----------------------------------
        pManager.AddIntegerParameter("MCTS Iters", "mi",
            "MCTS iterations per cut (default 150).",
            GH_ParamAccess.item, 150);
        pManager.AddIntegerParameter("MCTS Depth", "md",
            "MCTS tree depth (default 3).",
            GH_ParamAccess.item, 3);
        pManager.AddIntegerParameter("MCTS Nodes", "mn",
            "MCTS nodes per cut (default 20).",
            GH_ParamAccess.item, 20);
        pManager.AddIntegerParameter("Seed", "S",
            "RNG seed for reproducibility (default 0).",
            GH_ParamAccess.item, 0);
        pManager.AddBooleanParameter("PCA", "pca",
            "Align cuts to PCA frame (default false). World-axis cuts " +
            "are usually better for architectural input.",
            GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Convex Hulls", "H",
            "List of convex pieces approximating the input.",
            GH_ParamAccess.list);
        pManager.AddIntegerParameter("Count", "N",
            "Number of hulls produced.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Runtime", "T",
            "Runtime in milliseconds.", GH_ParamAccess.item);
        pManager.AddTextParameter("Backend", "B",
            "Reported version + build-flag status from the loaded shim. " +
            "Use this to confirm whether OpenVDB-based manifold " +
            "preprocessing is available.",
            GH_ParamAccess.item);
        pManager.AddBooleanParameter("Available", "Av",
            "True iff the CoACD native shim is loadable.",
            GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R",
            "Diagnostic report (input/output sizes, runtime, parameters).",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        Mesh m = null;
        double threshold = 0.05;
        int maxHulls = -1;
        int preprocess = 0;
        bool realMetric = false;
        bool run = false;
        int mctsIters = 150;
        int mctsDepth = 3;
        int mctsNodes = 20;
        int seed = 0;
        bool pca = false;

        if (!da.GetData(0, ref m)) return;
        da.GetData(1, ref threshold);
        da.GetData(2, ref maxHulls);
        da.GetData(3, ref preprocess);
        da.GetData(4, ref realMetric);
        da.GetData(5, ref run);
        da.GetData(6, ref mctsIters);
        da.GetData(7, ref mctsDepth);
        da.GetData(8, ref mctsNodes);
        da.GetData(9, ref seed);
        da.GetData(10, ref pca);

        var available = CoacdMeshDecompose.IsAvailable;
        // Backend is index 3 in outputs; Available is index 4.
        da.SetData(3, CoacdMeshDecompose.Version);
        da.SetData(4, available);

        if (!run)
        {
            da.SetData(5, available
                ? "Run is false. CoACD shim is loaded and ready."
                : "Run is false. CoACD shim NOT loaded; cannot decompose.");
            return;
        }
        if (!available)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "CoACD native shim not loaded - decomposition requires CoACD. " +
                "Build from native/coacd_shim/ and place the DLL alongside " +
                "Frahan.StonePack.gha.");
            return;
        }
        if (m == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh is required.");
            return;
        }

        // CoACD's BVH + MCTS pipeline has implicit minimum-size requirements:
        // sampling 2000 points across a 12-triangle Box exposes degeneracies
        // that surface as access violations rather than clean errors. Soft
        // warning at < 100 triangles; the shim's SEH translator will still
        // catch a crash and report it via Report rather than letting it
        // bubble up as SEHException.
        int triCountIn = m.Faces.Count;
        if (triCountIn < 100)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"Low-poly input ({triCountIn} faces). CoACD may fail on " +
                "meshes below ~100 triangles. Consider subdividing first " +
                "(Mesh > Subdivide) or use a Rhino primitive with higher " +
                "polygon count.");
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var snap = CgalConvert.ToSnapshot(m);
            var parameters = new CoacdParameters
            {
                Threshold      = threshold,
                MaxConvexHull  = maxHulls,
                PreprocessMode = preprocess,
                RealMetric     = realMetric,
                MctsIterations = mctsIters,
                MctsMaxDepth   = mctsDepth,
                MctsNodes      = mctsNodes,
                Seed           = (uint)Math.Max(0, seed),
                Pca            = pca,
            };
            var hulls = CoacdMeshDecompose.Decompose(snap, parameters);
            sw.Stop();

            var rhinoMeshes = new List<Mesh>(hulls.Count);
            foreach (var h in hulls) rhinoMeshes.Add(CgalConvert.FromSnapshot(h));

            da.SetDataList(0, rhinoMeshes);
            da.SetData(1, hulls.Count);
            da.SetData(2, (double)sw.ElapsedMilliseconds);

            string preprocessName;
            switch (preprocess)
            {
                case 1:  preprocessName = "on";   break;
                case 2:  preprocessName = "off";  break;
                default: preprocessName = "auto"; break;
            }
            int totalV = 0, totalT = 0;
            for (int i = 0; i < hulls.Count; i++)
            {
                totalV += hulls[i].VertexCount;
                totalT += hulls[i].TriangleCount;
            }
            da.SetData(5,
                $"Input      : {snap.VertexCount}V / {snap.TriangleCount}F\n" +
                $"Hulls      : {hulls.Count}\n" +
                $"Threshold  : {threshold} ({(realMetric ? "metres" : "normalized")})\n" +
                $"Preprocess : {preprocessName}\n" +
                $"MCTS       : iters={mctsIters} depth={mctsDepth} nodes={mctsNodes} seed={seed} pca={pca}\n" +
                $"Total V    : {totalV}\n" +
                $"Total F    : {totalT}\n" +
                $"Runtime    : {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"CoACD decompose failed: {ex.GetType().Name}: {ex.Message}");
            da.SetData(5, $"FAILED: {ex.Message}");
        }
    }
}
