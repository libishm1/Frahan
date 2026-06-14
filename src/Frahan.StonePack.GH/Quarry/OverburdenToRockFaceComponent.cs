#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using Frahan.GH.Attributes;
using Frahan.Core.Earthworks;

namespace Frahan.GH.Quarry;

// =============================================================================
// OverburdenToRockFaceComponent -- the GH adapter over Frahan.Core.Earthworks.
// OverburdenVolume (the cut-and-fill -> rock-face core, SLM card A5; workflow W16).
//
// Computes the SOIL volume to strip to reach the rock face: the volume between a
// GROUND surface mesh (LiDAR/photogrammetry) and a BEDROCK surface mesh (from
// GPR/ERT/seismic picks, reconstructed). The Core needs both surfaces on ONE
// common TIN; this adapter builds that by using the GROUND mesh triangulation as
// the common TIN and vertically sampling the bedrock mesh z at each ground vertex
// (Intersection.MeshRay straight down). Vertices with no bedrock below are dropped
// (clip to the common area), per the Core contract.
//
// Outputs the overburden (cut) bank volume, the loose/haul volume (swell-adjusted),
// the fill volume (where bedrock rises above ground -- e.g. an exposed knob), the
// signed net, the plan area, and a depth-coloured ground mesh for the visual pass.
//
// 2.5D for the VOLUME only. The exposed rock FACE geometry (steep/overhang) is NOT
// representable here -- reconstruct it in 3D via Scan Reconstruct (W2) for W1 block
// extraction. ComponentGuid: A7E0B0F1-0C0F-4A16-9E3D-0FACE0FACE01 (new, never reuse).
//
// Frahan > Quarry > overburden / earthworks.
// =============================================================================

/// <summary>
/// Frahan &gt; Quarry &gt; Overburden To Rock Face.
/// Soil volume to strip between a ground surface and a bedrock surface (the W16
/// overburden-strip workflow; wraps Core OverburdenVolume).
/// </summary>
[Algorithm("Cut-and-fill volume by TIN prism differencing",
    "Route-surveying prismoidal volume; difference triangulation (geom.at / Fade2D land survey)",
    Note = "V_facet = A_xy*(d1+d2+d3)/3 over a common TIN, exact for piecewise-linear surfaces; " +
           "cut/fill split at the d=0 crossing. 2.5D volume only; reconstruct the 3D face via Scan Reconstruct.")]
[DesignApplication(
    "Strip overburden to expose the bedrock / rock face, then hand the face to block extraction",
    DesignFlow.Bridges,
    Precedent = "Frahan-original; quarry overburden strip feeding BlockCutOpt (W16 -> W1)")]
public sealed class OverburdenToRockFaceComponent : FrahanComponentBase
{
    public OverburdenToRockFaceComponent()
        : base("Overburden To Rock Face", "Overburden",
            "Soil volume to strip to reach the rock face: the volume between a GROUND " +
            "surface mesh and a BEDROCK surface mesh. Bedrock z is sampled vertically " +
            "under each ground vertex (common-TIN bridge); volume by exact TIN-prism " +
            "differencing (Core OverburdenVolume). Cut = soil to remove; Loose = swell-" +
            "adjusted haul volume. 2.5D volume only -- get the 3D exposed face from Scan " +
            "Reconstruct for block extraction.",
            "Frahan", "Quarry")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("A7E0B0F1-0C0F-4A16-9E3D-0FACE0FACE01");

    protected override Bitmap Icon => IconProvider.Load("QuarryBlock.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Ground", "G",
            "Ground / topographic surface mesh (e.g. from Scan Reconstruct on a LiDAR / " +
            "photogrammetry cloud). Its triangulation is used as the common TIN.",
            GH_ParamAccess.item);
        p.AddMeshParameter("Bedrock", "R",
            "Bedrock / rock-face surface mesh (e.g. reconstructed from GPR / ERT / seismic " +
            "depth picks). Sampled vertically under each ground vertex.",
            GH_ParamAccess.item);
        p.AddNumberParameter("Swell", "Sw",
            "Swell fraction for the loose/haul volume (e.g. 0.25 = +25%). 0 = report bank volume only.",
            GH_ParamAccess.item, 0.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddNumberParameter("Overburden (bank)", "V",
            "Cut volume = soil above the bedrock surface, in model units^3 (bank / in-situ).",
            GH_ParamAccess.item);
        p.AddNumberParameter("Loose (haul)", "L",
            "Swell-adjusted volume to haul = V*(1+Swell).", GH_ParamAccess.item);
        p.AddNumberParameter("Fill", "F",
            "Volume where bedrock is ABOVE ground (rock already exposed / above the surface).",
            GH_ParamAccess.item);
        p.AddNumberParameter("Net", "N", "Cut - Fill (signed).", GH_ParamAccess.item);
        p.AddNumberParameter("Plan Area", "A",
            "Total projected (x,y) area covered by the common TIN.", GH_ParamAccess.item);
        p.AddMeshParameter("Depth Mesh", "D",
            "Ground mesh vertex-coloured by overburden depth (blue=thin -> red=deep) for the visual pass.",
            GH_ParamAccess.item);
        p.AddTextParameter("Report", "Rpt", "Human-readable summary.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh ground = null, bedrock = null;
        double swell = 0.0;
        if (!da.GetData(0, ref ground) || ground == null)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No Ground mesh provided."); return; }
        if (!da.GetData(1, ref bedrock) || bedrock == null)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No Bedrock mesh provided."); return; }
        da.GetData(2, ref swell);
        if (!ground.IsValid || ground.Vertices.Count < 3 || ground.Faces.Count < 1)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Ground mesh is invalid or empty."); return; }
        if (!bedrock.IsValid || bedrock.Faces.Count < 1)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Bedrock mesh is invalid or empty."); return; }
        if (swell < 0.0) swell = 0.0;

        int n = ground.Vertices.Count;

        // Vertical ray origin must sit above BOTH surfaces.
        var gb = ground.GetBoundingBox(true);
        var rb = bedrock.GetBoundingBox(true);
        double zTop = Math.Max(gb.Max.Z, rb.Max.Z) + Math.Max(1.0, (gb.Diagonal.Length + 1.0) * 0.01);

        var groundXyz = new double[3 * n];
        var bedrockZ = new double[n];
        var valid = new bool[n];
        var depth = new double[n];
        int missed = 0;
        var down = new Vector3d(0, 0, -1);

        for (int i = 0; i < n; i++)
        {
            Point3f v = ground.Vertices[i];
            groundXyz[3 * i + 0] = v.X;
            groundXyz[3 * i + 1] = v.Y;
            groundXyz[3 * i + 2] = v.Z;
            var ray = new Ray3d(new Point3d(v.X, v.Y, zTop), down);
            double t = Intersection.MeshRay(bedrock, ray);
            if (t >= 0.0)
            {
                double zr = ray.PointAt(t).Z;
                bedrockZ[i] = zr;
                valid[i] = true;
                depth[i] = v.Z - zr;
            }
            else
            {
                bedrockZ[i] = v.Z;           // d = 0; excluded via the triangle filter below
                valid[i] = false;
                missed++;
            }
        }

        // Triangles from the ground mesh; include only faces whose 3/4 verts all sampled bedrock.
        var tris = new List<int>(ground.Faces.Count * 3);
        for (int f = 0; f < ground.Faces.Count; f++)
        {
            MeshFace mf = ground.Faces[f];
            if (valid[mf.A] && valid[mf.B] && valid[mf.C]) { tris.Add(mf.A); tris.Add(mf.B); tris.Add(mf.C); }
            if (mf.IsQuad && valid[mf.A] && valid[mf.C] && valid[mf.D]) { tris.Add(mf.A); tris.Add(mf.C); tris.Add(mf.D); }
        }
        if (tris.Count < 3)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "No ground triangle has bedrock beneath it. Check the surfaces overlap in (x,y) and the bedrock is below the ground.");
            return;
        }

        OverburdenResult r;
        try
        {
            r = OverburdenVolume.Compute(groundXyz, bedrockZ, tris);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "OverburdenVolume failed: " + ex.Message);
            return;
        }

        double loose = r.CutVolume * (1.0 + swell);

        // Depth-coloured ground mesh (visual pass).
        Mesh dmesh = ground.DuplicateMesh();
        dmesh.VertexColors.Clear();
        double dmax = 0.0;
        for (int i = 0; i < n; i++) if (valid[i] && depth[i] > dmax) dmax = depth[i];
        if (dmax <= 0.0) dmax = 1.0;
        for (int i = 0; i < n; i++)
        {
            double f = valid[i] ? Math.Max(0.0, Math.Min(1.0, depth[i] / dmax)) : 0.0;
            int rr = (int)(255 * f), bb = (int)(255 * (1.0 - f));
            dmesh.VertexColors.Add(Color.FromArgb(rr, 40, bb));
        }

        if (missed > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{missed}/{n} ground vertices had no bedrock beneath (clipped to the common area).");

        string rpt =
            $"Overburden (bank): {r.CutVolume:0.###}  | Loose(+{swell:P0}): {loose:0.###}  | " +
            $"Fill: {r.FillVolume:0.###}  | Net: {r.NetVolume:0.###}  | Plan area: {r.PlanArea:0.###}  | " +
            $"tris: {tris.Count / 3}  | clipped verts: {missed}/{n}";

        da.SetData(0, r.CutVolume);
        da.SetData(1, loose);
        da.SetData(2, r.FillVolume);
        da.SetData(3, r.NetVolume);
        da.SetData(4, r.PlanArea);
        da.SetData(5, dmesh);
        da.SetData(6, rpt);
    }
}
