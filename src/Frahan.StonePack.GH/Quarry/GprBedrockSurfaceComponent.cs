#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;
using Frahan.Core.Earthworks;

namespace Frahan.GH.Quarry;

// =============================================================================
// GprBedrockSurfaceComponent -- GH adapter over Core BedrockSurface + TinMerge
// (cards A9 + A3). Turns GPR fracture/reflector picks into a BEDROCK surface
// mesh sharing a ground TIN's topology, so Overburden To Rock Face can difference
// them. The deepest continuous reflector per column = top of fresh rock
// (Porsani/Isakova/Bondua). Closes the W15 (GPR) -> W16 (overburden strip) chain.
//
// Pick points come from "GPR Fracture Extract" (its Fracture Picks output) or any
// (x, y, depth) source. Depth is read from the pick's -Z (the extract component
// emits picks at z = -depth); pass Depths explicitly to override. The ground mesh
// supplies both the (x,y) sample set and the datum (bedrock z = ground z - depth).
// ComponentGuid: A7E0B0F1-0C0F-4A16-9E3D-0FACE0FACE04.
//
// Frahan > Quarry > GPR bedrock surface.
// =============================================================================

/// <summary>Frahan &gt; Quarry &gt; GPR Bedrock Surface. Build a bedrock surface
/// mesh from GPR reflector picks on a ground TIN (wraps Core BedrockSurface + TinMerge).</summary>
[RelatedComponent("Frahan > Quarry > GPR Fracture Extract", Reason = "Source of the reflector picks (deepest = bedrock).")]
[RelatedComponent("Frahan > Quarry > Overburden To Rock Face", Reason = "This bedrock mesh is its Bedrock input.")]
[RelatedComponent("Frahan > Quarry > Clean Scan Mesh", Reason = "Clean the ground TIN first.")]
[Algorithm("GPR-to-bedrock surface: deepest continuous reflector + k-NN IDW resample onto a ground TIN",
    "Porsani 2006 / Isakova 2021 (top-of-fresh-rock reflector); Shepard 1968 (IDW); scale-relative radius (GeometryNumerics T2)",
    Note = "z_r = z_ground - depth, depth = v*t/2; bedrock z interpolated onto the ground vertices for a common TIN.")]
[DesignApplication(
    "Reconstruct the buried rock-face top from a GPR scan to plan the overburden strip",
    DesignFlow.BottomUp,
    Precedent = "Frahan-original; Porsani Capao Bonito + Bondua Botticino bedrock-from-GPR")]
public sealed class GprBedrockSurfaceComponent : GH_Component
{
    public GprBedrockSurfaceComponent()
        : base("GPR Bedrock Surface", "GprBedrock",
            "Build a bedrock / rock-face-top surface mesh from GPR reflector picks. Takes the " +
            "deepest continuous reflector per column as bedrock, resamples its depth onto a ground " +
            "mesh's vertices (k-NN IDW), and outputs a bedrock mesh (ground topology, z = ground z - " +
            "depth) for Overburden To Rock Face. Wraps Core BedrockSurface + TinMerge (A9 + A3). [Porsani 2006]",
            "Frahan", "Quarry")
    {
    }

    public override Guid ComponentGuid => new Guid("A7E0B0F1-0C0F-4A16-9E3D-0FACE0FACE04");
    public override GH_Exposure Exposure => GH_Exposure.secondary;
    protected override Bitmap Icon => IconProvider.Load("GprIngest.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Ground", "G", "Ground / topographic TIN (supplies the (x,y) sample set + datum).",
            GH_ParamAccess.item);
        p.AddPointParameter("Picks", "P",
            "GPR reflector pick points. Depth is taken from -Z unless Depths is supplied.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Depths", "D",
            "Optional explicit depth (m) per pick (overrides -Z). Empty = use -Z.", GH_ParamAccess.list);
        p[2].Optional = true;
        p.AddNumberParameter("Min Depth", "Dmin",
            "Ignore picks shallower than this (m) -- skip the weathered cover. Default 0.", GH_ParamAccess.item, 0.0);
        p.AddNumberParameter("Column Cell", "Cc",
            "Bin (x,y) to this cell (m) when reducing to one bedrock pick per column. 0 = exact. Default 0.25.",
            GH_ParamAccess.item, 0.25);
        p.AddIntegerParameter("Neighbors", "k", "k nearest picks for the IDW resample. Default 6.",
            GH_ParamAccess.item, 6);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Bedrock Mesh", "B",
            "Bedrock surface mesh (ground topology, z = ground z - interpolated depth) for Overburden To Rock Face.",
            GH_ParamAccess.item);
        p.AddPointParameter("Bedrock Points", "Bp", "Scattered bedrock points (deepest reflector per column).",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Unresolved", "U", "Ground vertices with no pick within range (clipped).",
            GH_ParamAccess.item);
        p.AddTextParameter("Report", "Rpt", "Summary.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        Mesh ground = null;
        var picks = new List<Point3d>();
        var depths = new List<double>();
        double minDepth = 0.0, colCell = 0.25;
        int neighbors = 6;
        if (!da.GetData(0, ref ground) || ground == null)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No Ground mesh."); return; }
        if (!da.GetDataList(1, picks) || picks.Count == 0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No Picks."); return; }
        da.GetDataList(2, depths);
        da.GetData(3, ref minDepth); da.GetData(4, ref colCell); da.GetData(5, ref neighbors);
        if (!ground.IsValid || ground.Vertices.Count < 3)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Ground mesh invalid."); return; }

        bool useExplicit = depths.Count == picks.Count;
        var bps = new List<BedrockPick>(picks.Count);
        for (int i = 0; i < picks.Count; i++)
        {
            double d = useExplicit ? depths[i] : -picks[i].Z;   // extract component emits z = -depth
            bps.Add(new BedrockPick(picks[i].X, picks[i].Y, d, 1.0));
        }

        // ground vertices as the target (x,y)
        int n = ground.Vertices.Count;
        var groundXyz = new double[3 * n];
        for (int i = 0; i < n; i++)
        {
            var v = ground.Vertices[i];
            groundXyz[3 * i] = v.X; groundXyz[3 * i + 1] = v.Y; groundXyz[3 * i + 2] = v.Z;
        }

        TinMergeResult merged;
        double[] scattered;
        try
        {
            var bopt = new BedrockSurfaceOptions { MinDepth = minDepth, ColumnCell = colCell };
            scattered = BedrockSurface.DeepestReflectorPoints(bps, (x, y) => 0.0, bopt); // points carry z = -depth (datum 0)
            merged = BedrockSurface.ToCommonTin(bps, groundXyz, bopt,
                new TinMergeOptions { Neighbors = Math.Max(1, neighbors) });
        }
        catch (Exception ex)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Bedrock build failed: " + ex.Message); return; }

        // bedrock mesh = ground topology, z = ground z - interpolated depth (NaN -> drop face later)
        var bmesh = ground.DuplicateMesh();
        for (int i = 0; i < n; i++)
        {
            var v = ground.Vertices[i];
            double z = merged.Valid[i] ? v.Z - merged.Z[i] : v.Z;  // unresolved -> sit at ground (d=0, clipped downstream)
            bmesh.Vertices.SetVertex(i, v.X, v.Y, (float)z);
        }
        bmesh.Normals.ComputeNormals();

        // scattered bedrock points for display (z carried as -depth -> show at -depth)
        var bpts = new List<Point3d>(scattered.Length / 3);
        for (int i = 0; i < scattered.Length; i += 3)
            bpts.Add(new Point3d(scattered[i], scattered[i + 1], scattered[i + 2]));

        if (merged.Unresolved > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{merged.Unresolved}/{n} ground vertices had no pick within range (clipped).");

        string rpt = $"{picks.Count} picks -> {bpts.Count} bedrock columns; resampled onto {n} ground " +
                     $"vertices ({merged.Unresolved} unresolved); median pick spacing {merged.MedianSourceSpacing:0.###} m.";
        da.SetData(0, bmesh);
        da.SetDataList(1, bpts);
        da.SetData(2, merged.Unresolved);
        da.SetData(3, rpt);
    }
}
