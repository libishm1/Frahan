#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Frahan.GH.Attributes;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Interfaces;
using Frahan.Masonry.Physics;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH;

/// <summary>
/// Settle 3D (Physics) -- takes an already-placed pack of stone meshes (e.g. from Pack3D
/// Irregular Container) and SETTLES them into real 3D contact with a Bullet rigid-body
/// simulation: convex-decompose each stone (CoACD, with a convex-hull fallback), drop under
/// gravity, let them rotate/slide/nestle to rest, then return the settled meshes + transforms.
/// This is the "complete packing, not bounding boxes" finishing pass; composed after any packer.
///
/// Backend: Frahan.Masonry.Physics.BulletSettleService (Bullet via BulletSharp.x64, zlib).
/// Bullet beats Kangaroo for stone settling (Kangaroo rigid-body has no friction; author-
/// confirmed unsuitable for stacking). Same engine as the pybullet dev harness. See
/// outputs/2026-06-03/pack3d_evolution/BULLET_PHYSICS_BACKEND.md. Heavy + single-threaded ->
/// async (Run-gated background Task). Needs native libbulletc.dll beside the .gha.
/// </summary>
[Algorithm("Rigid-body physics settle of irregular stone piles",
    "Zhuang, Q., Chen, Z., He, K., Cao, J., Wang, W. (2024). \"Dynamics Simulation-Based Packing of Irregular 3D Objects.\" Computers and Graphics 123:103996",
    Doi = "10.1016/j.cag.2024.103996")]
[Algorithm("Bullet rigid-body dynamics + convex-decomposition collision",
    "Bullet physics (Coumans et al.) via BulletSharp (zlib); convex pieces via CoACD (Wei et al. 2022)")]
[Algorithm("COM-over-support stability gate",
    "Heyman, J. (1966) limit-state; contact-support polygon")]
public sealed class PackSettle3DComponent : GH_TaskCapableComponent<PackSettle3DComponent.Result>
{
    public PackSettle3DComponent()
        : base("Settle 3D (Physics)", "Settle3D",
            "Physically settles an already-placed pack of stone meshes into real 3D contact " +
            "with a Bullet rigid-body simulation (convex-decomposition collision, gravity, " +
            "friction). Compose after any 3D packer to turn a heightmap/proxy placement into a " +
            "settled, stable, non-interpenetrating pile of real geometry. Bullet backend " +
            "(better than Kangaroo for stacking); needs libbulletc.dll beside the .gha.",
            "Frahan", "3D Packing")
    {
    }

    public override Guid ComponentGuid => new Guid("134785ac-19cb-4f14-85f8-e2f666bd14f6");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("PackIntoBlock.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Meshes", "M", "Already-placed stone meshes to settle.", GH_ParamAccess.list);
        pManager.AddMeshParameter("Container", "C", "Container mesh; its bounding box is the settle box (floor + walls). Optional; defaults to the meshes' bounds.", GH_ParamAccess.item);
        pManager[1].Optional = true;
        pManager.AddNumberParameter("Friction", "Fr", "Coulomb friction.", GH_ParamAccess.item, 0.85);
        pManager.AddIntegerParameter("Settle Steps", "St", "Physics steps after the gravity ramp.", GH_ParamAccess.item, 1500);
        pManager.AddIntegerParameter("Tamp", "Tp", "Vertical tamp rounds (densify).", GH_ParamAccess.item, 1);
        pManager.AddBooleanParameter("CoACD", "Cx", "Convex-decompose each stone (else convex hull).", GH_ParamAccess.item, true);
        pManager.AddBooleanParameter("Run", "Run", "Run the settle.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Settled Meshes", "S", "Meshes after physics settle.", GH_ParamAccess.list);
        pManager.AddTransformParameter("Transforms", "X", "Settle transform per input mesh.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Source Indices", "Src", "Input mesh index per settled mesh.", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "R", "Settle report.", GH_ParamAccess.item);
    }

    public sealed class Result
    {
        public List<Mesh> Settled = new();
        public List<Transform> Transforms = new();
        public List<int> Src = new();
        public string Report = "";
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (InPreSolve)
        {
            var meshes = new List<Mesh>();
            Mesh container = null;
            double friction = 0.85; int steps = 1500, tamp = 1; bool coacd = true, run = false;
            if (!da.GetDataList(0, meshes)) return;
            da.GetData(1, ref container);
            da.GetData(2, ref friction); da.GetData(3, ref steps); da.GetData(4, ref tamp);
            da.GetData(5, ref coacd); da.GetData(6, ref run);
            if (!run) { da.SetData(3, "Run is false."); return; }
            if (meshes.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No meshes."); return; }

            var captured = meshes.Select(m => m.DuplicateMesh()).ToList();
            var capC = container?.DuplicateMesh();
            var cFr = friction; var cSt = steps; var cTp = tamp; var cCx = coacd;
            TaskList.Add(Task.Run(() => RunSettle(captured, capC, cFr, cSt, cTp, cCx)));
            return;
        }

        if (!GetSolveResults(da, out var result))
        {
            var meshes = new List<Mesh>();
            Mesh container = null;
            double friction = 0.85; int steps = 1500, tamp = 1; bool coacd = true, run = false;
            if (!da.GetDataList(0, meshes)) return;
            da.GetData(1, ref container);
            da.GetData(2, ref friction); da.GetData(3, ref steps); da.GetData(4, ref tamp);
            da.GetData(5, ref coacd); da.GetData(6, ref run);
            if (!run) { da.SetData(3, "Run is false."); return; }
            if (meshes.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No meshes."); return; }
            try { result = RunSettle(meshes.Select(m => m.DuplicateMesh()).ToList(), container?.DuplicateMesh(), friction, steps, tamp, coacd); }
            catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Settle failed: " + ex.Message); return; }
        }

        if (result == null) return;
        da.SetDataList(0, result.Settled);
        da.SetDataList(1, result.Transforms);
        da.SetDataList(2, result.Src);
        da.SetData(3, result.Report);
    }

    private Result RunSettle(List<Mesh> meshes, Mesh container, double friction, int steps, int tamp, bool coacd)
    {
        if (!BulletSettleService.IsAvailable)
            return new Result { Report = "Bullet native (libbulletc.dll) not available beside the .gha." };

        // Settle box from the container bbox (or the union of mesh bboxes), shifted so its
        // min corner is at the origin (BulletSettleService box is [0,W]x[0,D]x[0,H]).
        var bb = container != null ? container.GetBoundingBox(true) : BoundingBox.Empty;
        if (!bb.IsValid) { bb = BoundingBox.Empty; foreach (var m in meshes) bb.Union(m.GetBoundingBox(true)); }
        var min = bb.Min;
        var sizeX = Math.Max(bb.Max.X - bb.Min.X, 1e-3);
        var sizeY = Math.Max(bb.Max.Y - bb.Min.Y, 1e-3);
        var sizeZ = Math.Max((bb.Max.Z - bb.Min.Z) * 1.5, 1e-3);   // headroom above the pack
        var boxC = new SettleContainer { Width = sizeX, Depth = sizeY, Height = sizeZ };

        var stones = new List<SettleStone>(meshes.Count);
        bool useCoacd = coacd && CoacdMeshDecompose.IsAvailable;
        foreach (var m in meshes)
        {
            var pieces = ConvexPieces(m, useCoacd, min);
            double vol = MeshVolume(m);
            stones.Add(new SettleStone { ConvexPieces = pieces, Mass = Math.Max(vol, 1e-3) });
        }

        var opt = new SettleOptions { Friction = friction, SettleSteps = Math.Max(200, steps), TampRounds = Math.Max(0, tamp) };
        var sim = BulletSettleService.Settle(stones, boxC, opt);

        var res = new Result();
        int settledIn = 0;
        for (int k = 0; k < meshes.Count; k++)
        {
            var sr = sim.Stones[k];
            // world: v' = R*(v - Cworld) + Tworld, with Cworld = centroid + min, Tworld = translation + min
            var Cw = new Vector3d(sr.Centroid[0] + min.X, sr.Centroid[1] + min.Y, sr.Centroid[2] + min.Z);
            var Tw = new Vector3d(sr.Translation[0] + min.X, sr.Translation[1] + min.Y, sr.Translation[2] + min.Z);
            var rot = Transform.Identity;
            rot[0, 0] = sr.Rotation[0]; rot[0, 1] = sr.Rotation[1]; rot[0, 2] = sr.Rotation[2];
            rot[1, 0] = sr.Rotation[3]; rot[1, 1] = sr.Rotation[4]; rot[1, 2] = sr.Rotation[5];
            rot[2, 0] = sr.Rotation[6]; rot[2, 1] = sr.Rotation[7]; rot[2, 2] = sr.Rotation[8];
            var full = Transform.Translation(Tw) * rot * Transform.Translation(-Cw);
            var sm = meshes[k].DuplicateMesh();
            sm.Transform(full);
            res.Settled.Add(sm); res.Transforms.Add(full); res.Src.Add(k);
            if (sr.InContainer) settledIn++;
        }
        res.Report = $"Settle 3D (Bullet) | input {meshes.Count} | settled-in {settledIn} | " +
                     $"decomposition {(useCoacd ? "CoACD" : "convex-hull")} | friction {friction} | steps {steps}";
        return res;
    }

    private static List<double[]> ConvexPieces(Mesh m, bool useCoacd, Point3d shift)
    {
        var pieces = new List<double[]>();
        if (useCoacd)
        {
            try
            {
                var snap = ToSnapshot(m, shift);
                var parts = CoacdMeshDecompose.Decompose(snap, new CoacdParameters());
                foreach (var p in parts) pieces.Add(p.VertexCoordsXyz.ToArray());
                if (pieces.Count > 0) return pieces;
            }
            catch { /* fall through to convex hull */ }
        }
        // Fallback: one piece = all mesh vertices (Bullet builds the convex hull).
        var verts = new double[m.Vertices.Count * 3];
        for (int i = 0; i < m.Vertices.Count; i++)
        {
            var v = m.Vertices[i];
            verts[3 * i] = v.X - shift.X; verts[3 * i + 1] = v.Y - shift.Y; verts[3 * i + 2] = v.Z - shift.Z;
        }
        pieces.Add(verts);
        return pieces;
    }

    private static MeshSnapshot ToSnapshot(Mesh m, Point3d shift)
    {
        var verts = new List<double>(m.Vertices.Count * 3);
        for (int i = 0; i < m.Vertices.Count; i++)
        {
            var v = m.Vertices[i];
            verts.Add(v.X - shift.X); verts.Add(v.Y - shift.Y); verts.Add(v.Z - shift.Z);
        }
        var tris = new List<int>(m.Faces.Count * 6);
        for (int i = 0; i < m.Faces.Count; i++)
        {
            var f = m.Faces[i];
            tris.Add(f.A); tris.Add(f.B); tris.Add(f.C);
            if (f.IsQuad) { tris.Add(f.A); tris.Add(f.C); tris.Add(f.D); }
        }
        return new MeshSnapshot(verts, tris);
    }

    private static double MeshVolume(Mesh m)
    {
        try
        {
            if (m.IsClosed)
            {
                var v = VolumeMassProperties.Compute(m);
                if (v != null) return Math.Abs(v.Volume);
            }
        }
        catch { }
        var bb = m.GetBoundingBox(true);
        return Math.Max(bb.Volume, 1e-3);
    }
}
