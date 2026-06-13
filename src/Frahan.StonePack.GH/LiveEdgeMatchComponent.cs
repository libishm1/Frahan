using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Frahan.EdgeMatching;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH;

/// <summary>
/// Live Edge Match -- assigns a pool of offcuts to the staggered course slots
/// of a live-edge floor and reports the assignment and per-board scribe trim.
/// Mode 0 = greedy (fast, order-dependent); Mode 1 = Hungarian (global minimum
/// total trim over the staggered slots, the same assigner that drives Template
/// Panel Match). The "solver view" of Live Edge Stagger Layup.
/// </summary>
[Algorithm("Scribe-trim cost matcher", "Frahan-original", Note = "Cost = mean scribe trim of an offcut in a slot.")]
[Algorithm("Bipartite assignment", "Kuhn 1955 Hungarian method", Note = "Mode=1: optimal offcut-to-slot assignment.")]
[RelatedComponent("Frahan > EdgeMatch > Live Edge Stagger Layup", Reason = "Builds the floor geometry from this assignment.")]
[RelatedComponent("Frahan > Voussoir > Template Panel Match", Reason = "Same HungarianAssigner, 3D top-down version.")]
public class LiveEdgeMatchComponent : GH_Component
{
    public LiveEdgeMatchComponent()
        : base("Live Edge Match", "LEMatch",
            "Assign a pool of offcuts to the staggered live-edge course slots and report the assignment plus per-board " +
            "scribe trim. Mode 0 = greedy, Mode 1 = Hungarian (global minimum total trim). No Outlines -> a demo pool.",
            "Frahan", "EdgeMatch")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F10044-ED9E-4ED9-A044-ED9EED9E0044");
    protected override Bitmap Icon => IconProvider.Load("LiveEdgeMatch.png");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Outlines", "O", "Offcut outline pool (closed curves). Empty -> demo pool.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Floor width", "W", "Floor width along the courses.", GH_ParamAccess.item, 520.0);
        pManager.AddIntegerParameter("Courses", "C", "Number of courses (rows).", GH_ParamAccess.item, 5);
        pManager.AddNumberParameter("Course height", "H", "Nominal course height.", GH_ParamAccess.item, 60.0);
        pManager.AddIntegerParameter("Seed", "S", "Deterministic seed.", GH_ParamAccess.item, 313131);
        pManager.AddIntegerParameter("Mode", "M", "0 = Greedy, 1 = Hungarian (global min-trim).", GH_ParamAccess.item, 0);
        pManager.AddBooleanParameter("Run", "R", "Set true to solve the assignment.", GH_ParamAccess.item, false);
        pManager[0].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Pool index", "I", "Offcut pool index assigned to each placed slot (placement order).", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Course", "C", "Course (row) of each placed slot.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Trim", "T", "Mean scribe trim per placed board.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Mean trim", "Mt", "Mean scribe trim over the floor.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Max trim", "Mx", "Max scribe deviation over the floor.", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Placed", "P", "Number of boards placed.", GH_ParamAccess.item);
        pManager.AddCurveParameter("Rivers", "Rv", "The live-edge seams between courses.", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var outlines = new List<Curve>();
        da.GetDataList(0, outlines);
        double fw = 520; da.GetData(1, ref fw);
        int courses = 5; da.GetData(2, ref courses);
        double hc = 60; da.GetData(3, ref hc);
        int seed = 313131; da.GetData(4, ref seed);
        int mode = 0; da.GetData(5, ref mode);
        bool run = false; da.GetData(6, ref run);
        if (!run) return;

        var rawOutlines = (outlines != null && outlines.Count > 0)
            ? outlines.Select(c => LiveEdgeGhUtil.CurveToLoop(c)).Where(l => l != null && l.Count >= 8).Select(l => l.ToArray()).ToList()
            : LiveEdgeDemo.SyntheticOutlines(64, seed);

        var pool = new List<LiveEdgeBoard>();
        foreach (var loop in rawOutlines)
        {
            var b = LiveEdgeBoard.Extract(loop);
            if (b != null) pool.Add(b);
        }
        if (pool.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No offcut classified to a usable board.");
            return;
        }

        var opt = new LiveEdgeLayupOptions
        {
            FloorWidth = fw,
            Courses = Math.Max(1, courses),
            CourseHeight = hc,
            Seed = seed,
            Mode = mode == 1 ? LiveEdgeLayupMode.Hungarian : LiveEdgeLayupMode.Greedy
        };
        var result = LiveEdgeLayup.Solve(pool, opt);

        da.SetDataList(0, result.Boards.Select(b => b.PoolIndex).ToList());
        da.SetDataList(1, result.Boards.Select(b => b.Course).ToList());
        da.SetDataList(2, result.Boards.Select(b => b.Scribe.MeanTrim).ToList());
        da.SetData(3, result.MeanTrim);
        da.SetData(4, result.MaxTrim);
        da.SetData(5, result.Placed);
        da.SetDataList(6, result.Rivers.Select(rv => (Curve)new PolylineCurve(rv)).ToList());
    }
}
