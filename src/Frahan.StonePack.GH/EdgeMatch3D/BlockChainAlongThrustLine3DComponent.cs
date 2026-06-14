#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.EdgeMatch3D;

// =============================================================================
// BlockChainAlongThrustLine3DComponent (Component A3D, GUID D5F10009)
//
// 3D sibling of Component A (Panel Match Along Rail). Bidirectional walker
// places scanned stones along a designer-supplied thrust line (parabolic
// curve / catenary / spline), one stone per station, building an arch or
// spanning structure.
//
// This is the centrepiece of the UCL Bartlett 18-stone three-legged arch
// replica (HITL card-set em_3d_chain_ucl_bartlett, fixture 02). The
// Cyclopean Cannibalism rough-coursing recipe (em_3d_cyclopean_cannibalism)
// also calls into this walker.
//
// Status: SKELETON. Calls Component B3D as the atomic primitive per
// station; the walker state machine + bidirectional candidate evaluation
// + Pareto multi-objective scoring (angle / Cg / endpoint deviation) is
// the v1.x build target.
// =============================================================================

[Algorithm("Bidirectional rail walker",
    "Frahan-original sequential placement state machine",
    Note = "Per-station forward+backward fit comparison; OQ1-locked 2026-05-31")]
[Algorithm("NSGA-II Pareto multi-objective optimisation",
    "Deb et al. 2002 'A fast and elitist multiobjective genetic algorithm: NSGA-II', IEEE TEVC 6(2):182-197",
    Note = "Strategy=Pareto path; three objectives per UCL Devadass 2025 SS2.5 (angle / Cg / endpoint deviation)")]
[Algorithm("Block Pair Match 3D",
    "See BlockPairMatch3DComponent for B3D pipeline references",
    Note = "Per-station atomic call")]
[DesignApplication(
    "Place scanned stones along a designer thrust line to build an arch or spanning structure.",
    DesignFlow.Bridges,
    Precedent = "UCL Bartlett Lu/Zhu/Olesti/Scully/Devadass 2025 18-stone three-legged limestone arch (DOI 10.21203/rs.3.rs-8019586/v1)",
    Tolerance = "endpoint deviation <= 50 mm at 2.5 m span; Cg deviation per stone <= 30 mm; cumulative angle <= 5 deg",
    CardSet = "wiki/research/hitl_cards/em_3d_chain_ucl_bartlett/")]
public sealed class BlockChainAlongThrustLine3DComponent : FrahanComponentBase
{
    public BlockChainAlongThrustLine3DComponent()
        : base("Block Chain Along Thrust Line", "BlkChain3D",
            "Bidirectional 3D walker placing scanned stones along a designer-" +
            "supplied thrust line (catenary, parabola, spline). One stone per " +
            "station; Block Pair Match 3D is the per-station atomic call. " +
            "Strategy=Pareto runs NSGA-II on three UCL-paper objectives " +
            "(angle deviation / Cg deviation / endpoint deviation). The " +
            "canonical implementation of the UCL Bartlett 18-stone arch " +
            "workflow (em_3d_chain_ucl_bartlett HITL card-set).",
            "Frahan", "EdgeMatch")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("D5F10009-ED9E-4ED9-A009-ED9EED9E0009");

    // 2026-06-05 (W6, keep-or-cut): hidden from the ribbon. SolveInstance is a
    // pure stub (emits empty lists + a STUB warning); a non-functional component
    // must not sit on the primary ribbon (the "no ghost components" rule). Not
    // marked Obsolete: this is unbuilt future work, not a deprecated/superseded
    // component. Build target = the UCL Bartlett 18-stone arch walker (greedy ->
    // beam -> NSGA-II Pareto) per HITL card em_3d_chain_ucl_bartlett; it depends
    // on the BlockPairMatch3D exact-ICP upgrade (task #54) and is gated by the
    // documented 3D-edge-match tessellation blocker. Flip back to primary when
    // the walker lands. GUID preserved so old canvases still load.
    // See outputs/2026-06-05/keep_or_cut/UNBUILT_COMPONENTS.md.
    public override GH_Exposure Exposure => GH_Exposure.hidden;

    protected override Bitmap Icon => IconProvider.Load("EdgeMatchSolve.png"); // placeholder

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Stone Inventory", "I",
            "Filtered list of scanned-stone meshes. Apply area + internal-angle " +
            "filters upstream (UCL Devadass 2025 SS2.4) before wiring here.",
            GH_ParamAccess.list);
        p.AddCurveParameter("Thrust Curve", "Tc",
            "Designer-supplied thrust line (catenary, parabola, spline). The walker " +
            "places one stone per station evaluated along this curve.",
            GH_ParamAccess.item);
        p.AddIntegerParameter("Strategy", "St",
            "0=Greedy, 1=Beam (default), 2=Pareto (NSGA-II three-objective).",
            GH_ParamAccess.item, 1);
        p.AddIntegerParameter("Direction", "Dr",
            "0=Forward, 1=Backward, 2=Bidirectional (default, OQ1-locked 2026-05-31).",
            GH_ParamAccess.item, 2);
        p.AddNumberParameter("Match Tolerance", "Mt",
            "Per-station Hausdorff match tolerance (mm).",
            GH_ParamAccess.item, 3.0);
        p.AddIntegerParameter("Beam Width", "Bw",
            "Beam width (Strategy=Beam) or population size (Strategy=Pareto).",
            GH_ParamAccess.item, 16);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Placed Stones", "Ps",
            "Per-station placed stone meshes (transformed).", GH_ParamAccess.list);
        p.AddIntegerParameter("Stone Indices", "Si",
            "Per-station inventory index used.", GH_ParamAccess.list);
        p.AddNumberParameter("Per-Station Residual", "Rs",
            "Per-station match residual (mm).", GH_ParamAccess.list);
        p.AddPointParameter("Pareto Front", "Pf",
            "Strategy=Pareto only: 3D points where (x=angle deviation, " +
            "y=Cg deviation, z=endpoint deviation) per solution.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Endpoint Deviation", "Ed",
            "Final placed-chain endpoint distance from designed thrust-line endpoint (mm).",
            GH_ParamAccess.item);
        p.AddTextParameter("Remarks", "Rm",
            "Per-station diagnostic notes + strategy convergence flags.",
            GH_ParamAccess.list);
    }

    protected override void SolveSafe(IGH_DataAccess DA)
    {
        var inventory = new List<Mesh>();
        Curve thrustCurve = null;
        int strategy = 1;
        int direction = 2;
        double matchTolerance = 3.0;
        int beamWidth = 16;

        if (!DA.GetDataList(0, inventory)) return;
        if (!DA.GetData(1, ref thrustCurve)) return;
        DA.GetData(2, ref strategy);
        DA.GetData(3, ref direction);
        DA.GetData(4, ref matchTolerance);
        DA.GetData(5, ref beamWidth);

        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
            "STUB v1.x: walker not yet implemented. See HITL card " +
            "em_3d_chain_ucl_bartlett for the build target. The component " +
            "registers correctly so downstream wiring can be authored ahead.");

        // Emit empty placeholders so the GH canvas remains valid.
        DA.SetDataList(0, new List<Mesh>());
        DA.SetDataList(1, new List<int>());
        DA.SetDataList(2, new List<double>());
        DA.SetDataList(3, new List<Point3d>());
        DA.SetData(4, 0.0);
        DA.SetDataList(5, new List<string>
        {
            "STUB v1.x -- walker not implemented yet. Strategy=" + strategy +
            " (Greedy/Beam/Pareto). Direction=" + direction + " (Forward/Backward/Bidirectional)."
        });
    }
}
