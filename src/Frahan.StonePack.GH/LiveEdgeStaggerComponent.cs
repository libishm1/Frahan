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
/// Live Edge Stagger Layup -- lays a pool of classified offcuts into a
/// staggered live-edge floor (Case A: live edges run along the course as
/// continuous wavy "river" seams; the short sawn butt joints are staggered
/// brick-bond). End-to-end Classify -> Match -> Trim -> Stagger. With no
/// Outlines connected it synthesises a deterministic demo pool, so the
/// component is self-presenting on its own.
/// </summary>
[Algorithm("Brick-bond scribe layup", "Frahan-original",
    Note = "Predefined smooth rivers; per-course greedy fill with a stagger penalty; scribe-and-fill trim.")]
[Algorithm("Bipartite assignment", "Kuhn 1955 Hungarian method", Note = "Optional Mode=1: global min-trim assignment over the staggered slots.")]
[RelatedComponent("Frahan > EdgeMatch > Live Edge Classify", Reason = "Produces the live/sawn split each offcut is laid by.")]
[RelatedComponent("Frahan > Voussoir > Template Panel Match", Reason = "Same HungarianAssigner, 3D top-down stone-to-slot assignment.")]
public class LiveEdgeStaggerComponent : FrahanComponentBase
{
    public LiveEdgeStaggerComponent()
        : base("Live Edge Stagger Layup", "LEStagger",
            "Lay a pool of wood offcuts into a staggered live-edge floor: live edges matched along the course as " +
            "continuous wavy seams, short sawn butt joints staggered brick-bond, each board scribe-trimmed to fit. " +
            "Mode 0 = greedy, Mode 1 = Hungarian (global min-trim). No Outlines -> a demo pool is synthesised.",
            "Frahan", "EdgeMatch")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F10046-ED9E-4ED9-A046-ED9EED9E0046");
    protected override Bitmap Icon => IconProvider.Load("LiveEdgeStagger.png");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Outlines", "O", "Offcut outline pool (closed curves). Empty -> demo pool.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Floor width", "W", "Floor width along the courses.", GH_ParamAccess.item, 520.0);
        pManager.AddIntegerParameter("Courses", "C", "Number of courses (rows).", GH_ParamAccess.item, 5);
        pManager.AddNumberParameter("Course height", "H", "Nominal course height.", GH_ParamAccess.item, 60.0);
        pManager.AddIntegerParameter("Seed", "S", "Deterministic seed (river shapes + demo pool).", GH_ParamAccess.item, 313131);
        pManager.AddIntegerParameter("Mode", "M", "0 = Greedy, 1 = Hungarian (global min-trim).", GH_ParamAccess.item, 0);
        pManager.AddBooleanParameter("Run", "R", "Set true to lay the floor.", GH_ParamAccess.item, false);
        pManager[0].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Boards", "B", "Placed boards (vertex-coloured meshes).", GH_ParamAccess.list);
        pManager.AddCurveParameter("Rivers", "Rv", "The live-edge seams between courses.", GH_ParamAccess.list);
        pManager.AddLineParameter("Butt joints", "J", "Staggered sawn butt joints.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Trim slivers", "T", "Scribe-and-fill strips removed from each board.", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "Re", "Layup summary.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
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

        // Build the offcut pool. No input -> synthesise a deterministic demo pool.
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

        var meshes = new List<Mesh>();
        var slivers = new List<Curve>();
        foreach (var pb in result.Boards)
        {
            var sc = pb.Scribe;
            int le = sc.ScribedBottom.Length;
            var m = new Mesh();
            for (int i = 0; i < le; i++) m.Vertices.Add(sc.ScribedBottom[i]);
            for (int i = 0; i < le; i++) m.Vertices.Add(sc.ScribedTop[i]);
            for (int i = 0; i < le - 1; i++) m.Faces.AddFace(i, i + 1, le + i + 1, le + i);
            var col = Color.FromArgb(pb.ColorRgb[0], pb.ColorRgb[1], pb.ColorRgb[2]);
            for (int i = 0; i < m.Vertices.Count; i++) m.VertexColors.Add(col);
            m.Normals.ComputeNormals();
            meshes.Add(m);
            slivers.Add(new PolylineCurve(sc.BottomSliver));
            slivers.Add(new PolylineCurve(sc.TopSliver));
        }

        var rivers = result.Rivers.Select(rv => (Curve)new PolylineCurve(rv)).ToList();
        var joints = result.ButtJoints.Select(j => new Line(j[0], j[1])).ToList();
        string report = $"Live-edge floor: {result.Placed} boards in {opt.Courses} courses, " +
                        $"mode={(opt.Mode == LiveEdgeLayupMode.Hungarian ? "Hungarian" : "Greedy")}. " +
                        $"Scribe trim mean={result.MeanTrim:F2} max={result.MaxTrim:F2} (tol H/30={hc / 30.0:F2}).";

        da.SetDataList(0, meshes);
        da.SetDataList(1, rivers);
        da.SetDataList(2, joints);
        da.SetDataList(3, slivers);
        da.SetData(4, report);
    }
}
