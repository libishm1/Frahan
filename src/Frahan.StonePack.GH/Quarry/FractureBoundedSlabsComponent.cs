#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using Frahan.GH.Attributes;

namespace Frahan.GH.Quarry;

// =============================================================================
// FractureBoundedSlabsComponent -- cut a bench into the fracture-bounded SLABS
// (the closed volumes between consecutive kriged bed surfaces), the input the
// staged-guillotine packer (Fracture Block Pack mode 5) needs to reproduce the
// paper's manufacturable block layout.
//
// The GPR/kriged fracture beds are SINGLE-VALUED depth surfaces z = f(x,y), so we
// do NOT need a CGAL solid-boolean: we sample each bed onto a common grid (a
// vertical-ray height field, nearest-point fallback), sort the beds by depth, and
// STITCH consecutive height fields into closed slab meshes (top field + bottom
// field + 4 side walls). Each slab FOLLOWS the wavy beds, so a block packed inside
// it never crosses a fracture. Robust and fast where a boolean of open surfaces is
// fragile. (For arbitrary non-height-field cutters use Slab Cut By Tool Mesh / CGAL.)
//
// Frahan > Quarry > kriged beds -> fracture-bounded slabs -> Fracture Block Pack.
// =============================================================================

/// <summary>
/// Frahan &gt; Quarry &gt; Fracture Bounded Slabs. Cut a bench box into the closed
/// inter-bed slabs that follow the kriged fracture surfaces (height-field stitch),
/// one slab per gap between consecutive beds (and bench top/bottom). Feed the slabs
/// into Fracture Block Pack as the per-slab containers.
/// </summary>
[RelatedComponent("Frahan > Quarry > GPR Fracture Surfaces 3D", Reason = "Source of the kriged bed surfaces this slabs the bench by.")]
[RelatedComponent("Frahan > Quarry > Fracture Block Pack", Reason = "Pack each fracture-bounded slab with the staged guillotine (mode 5).")]
[RelatedComponent("Frahan > Slab > Slab Cut By Tool Mesh (CGAL)", Reason = "The CGAL boolean alternative for arbitrary (non-height-field) curved cutters.")]
[Algorithm("Fracture-bounded slabs by height-field stitch between single-valued kriged bed surfaces",
    "ordinary-kriging bed surfaces (Cressie 1993); guillotine bed-cut sequence (Gilmore and Gomory 1965)",
    Note = "Beds are single-valued z=f(x,y); stitch consecutive sampled height fields into closed slabs. No solid boolean needed.")]
public sealed class FractureBoundedSlabsComponent : FrahanComponentBase
{
    public FractureBoundedSlabsComponent()
        : base("Fracture Bounded Slabs", "BedSlabs",
            "Cut a bench box into the closed inter-bed SLABS that FOLLOW the kriged fracture surfaces. " +
            "The beds are single-valued depth surfaces, so each slab is built by stitching the sampled " +
            "height fields of two consecutive beds (height-field stitch, no CGAL boolean): one slab per " +
            "gap between consecutive beds and the bench top/bottom. Each slab follows the wavy beds, so a " +
            "block packed inside it never crosses a fracture. Feed the slabs into Fracture Block Pack " +
            "(packer 5, staged guillotine) -> the paper's manufacturable bed-following layout.",
            "Frahan", "Quarry")
    { }

    public override Guid ComponentGuid => new Guid("A7E0B0F4-0C0F-4A16-9E3D-0FACE0FACE05");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("Box2Mesh.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddBoxParameter("Bench", "A", "Bench bounding box (m). XY footprint + the Z range to slab.",
            GH_ParamAccess.item);
        p.AddMeshParameter("Bed Surfaces", "F",
            "Kriged fracture bed surfaces (from GPR Fracture Surfaces 3D). Single-valued depth surfaces; " +
            "one slab is built per gap between consecutive beds.", GH_ParamAccess.list);
        p.AddIntegerParameter("Grid Res", "G",
            "Stitch grid resolution along the longer footprint axis (the other axis scales to keep cells " +
            "near-square). Higher = finer wavy-bed fidelity. Default 26.", GH_ParamAccess.item, 26);
        p.AddNumberParameter("Keep-out", "K",
            "Inward Z margin (m) kept from each bed (the GPR position keep-out). Default 0.", GH_ParamAccess.item, 0.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Slabs", "S",
            "The closed fracture-bounded slab meshes, one per inter-bed layer (shallow -> deep). Feed into " +
            "Fracture Block Pack > Container Meshes.", GH_ParamAccess.list);
        p.AddNumberParameter("Thickness", "T", "Mean thickness (m) of each slab, aligned to Slabs.", GH_ParamAccess.list);
        p.AddTextParameter("Report", "Rpt", "Per-slab summary.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var bench = Box.Unset;
        if (!da.GetData(0, ref bench) || !bench.IsValid)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No valid Bench box."); return; }
        var beds = new List<Mesh>();
        da.GetDataList(1, beds);
        beds = beds.Where(m => m != null && m.Vertices.Count > 2).ToList();
        if (beds.Count == 0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No bed surfaces."); return; }
        int gridRes = 26; double keepout = 0.0;
        da.GetData(2, ref gridRes); da.GetData(3, ref keepout);
        gridRes = Math.Max(6, Math.Min(120, gridRes));

        var bb = bench.BoundingBox;
        double xmin = bb.Min.X, xmax = bb.Max.X, ymin = bb.Min.Y, ymax = bb.Max.Y;
        double zTop = bb.Max.Z, zBot = bb.Min.Z;
        double ex = xmax - xmin, ey = ymax - ymin;
        if (ex < 1e-6 || ey < 1e-6 || (zTop - zBot) < 1e-6)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Degenerate bench."); return; }
        int Nx = ex >= ey ? gridRes : Math.Max(4, (int)Math.Round(gridRes * ex / ey));
        int Ny = ey >= ex ? gridRes : Math.Max(4, (int)Math.Round(gridRes * ey / ex));

        // sample each bed onto the common (Nx x Ny) grid -> height field + mean depth
        double Sample(Mesh m, double gx, double gy)
        {
            var ray = new Ray3d(new Point3d(gx, gy, zTop + 2.0), new Vector3d(0, 0, -1));
            double t = Intersection.MeshRay(m, ray);
            if (t >= 0) return ray.PointAt(t).Z;
            var mp = m.ClosestMeshPoint(new Point3d(gx, gy, (zTop + zBot) * 0.5), 0.0);
            return mp != null ? m.PointAt(mp).Z : (zTop + zBot) * 0.5;
        }
        var fields = new List<(double[,] h, double mean)>();
        foreach (var m in beds)
        {
            var h = new double[Nx, Ny]; double tot = 0;
            for (int i = 0; i < Nx; i++)
                for (int j = 0; j < Ny; j++)
                {
                    double gx = xmin + ex * i / (Nx - 1), gy = ymin + ey * j / (Ny - 1);
                    double z = Sample(m, gx, gy); h[i, j] = z; tot += z;
                }
            fields.Add((h, tot / (Nx * Ny)));
        }
        fields = fields.OrderByDescending(f => f.mean).ToList(); // shallow (z high) -> deep

        // boundary height fields: bench top, each bed (minus keep-out), bench bottom
        var bounds = new List<double[,]>();
        var top = new double[Nx, Ny]; var flo = new double[Nx, Ny];
        for (int i = 0; i < Nx; i++) for (int j = 0; j < Ny; j++) { top[i, j] = zTop; flo[i, j] = zBot; }
        bounds.Add(top);
        foreach (var f in fields)
        {
            var hk = new double[Nx, Ny];
            for (int i = 0; i < Nx; i++) for (int j = 0; j < Ny; j++) hk[i, j] = f.h[i, j];
            bounds.Add(hk);
        }
        bounds.Add(flo);

        var slabs = new List<Mesh>();
        var thick = new List<double>();
        var rpt = new System.Text.StringBuilder();
        rpt.AppendLine($"bench {ex:0.##} x {ey:0.##} x {(zTop - zBot):0.##} m | {beds.Count} beds -> {bounds.Count - 1} slabs | grid {Nx}x{Ny} keepout {keepout:0.###}m");
        for (int k = 0; k < bounds.Count - 1; k++)
        {
            var A = bounds[k]; var B = bounds[k + 1];
            // apply keep-out: pull bed boundaries inward (top down, bottom up), not the bench faces
            var At = (double[,])A.Clone(); var Bt = (double[,])B.Clone();
            if (k > 0) for (int i = 0; i < Nx; i++) for (int j = 0; j < Ny; j++) At[i, j] -= keepout;          // below an upper bed
            if (k < bounds.Count - 2) for (int i = 0; i < Nx; i++) for (int j = 0; j < Ny; j++) Bt[i, j] += keepout; // above a lower bed
            var (slab, meanThk) = StitchSlab(At, Bt, xmin, ymin, ex, ey, Nx, Ny);
            if (slab.Faces.Count == 0) continue;
            slabs.Add(slab); thick.Add(meanThk);
            rpt.AppendLine($"  slab {k}: mean thickness {meanThk:0.##} m, {slab.Faces.Count} faces, closed {slab.IsClosed}");
        }

        da.SetDataList(0, slabs);
        da.SetDataList(1, thick);
        da.SetData(2, rpt.ToString().TrimEnd());
    }

    // Closed mesh between top field A and bottom field B over the common grid: top + bottom + 4 walls.
    private static (Mesh slab, double meanThk) StitchSlab(double[,] A, double[,] B,
        double xmin, double ymin, double ex, double ey, int Nx, int Ny)
    {
        var m = new Mesh();
        var ti = new int[Nx, Ny]; var bi = new int[Nx, Ny];
        double thkSum = 0; int thkN = 0;
        for (int i = 0; i < Nx; i++)
            for (int j = 0; j < Ny; j++)
            {
                double gx = xmin + ex * i / (Nx - 1), gy = ymin + ey * j / (Ny - 1);
                double zt = A[i, j], zb = B[i, j];
                if (zt < zb + 0.03) zt = zb + 0.03;     // never invert
                ti[i, j] = m.Vertices.Add(gx, gy, zt);
                bi[i, j] = m.Vertices.Add(gx, gy, zb);
                thkSum += zt - zb; thkN++;
            }
        for (int i = 0; i < Nx - 1; i++)
            for (int j = 0; j < Ny - 1; j++)
            {
                m.Faces.AddFace(ti[i, j], ti[i + 1, j], ti[i + 1, j + 1], ti[i, j + 1]);     // top
                m.Faces.AddFace(bi[i, j], bi[i, j + 1], bi[i + 1, j + 1], bi[i + 1, j]);     // bottom (flip)
            }
        for (int i = 0; i < Nx - 1; i++)
        {
            m.Faces.AddFace(ti[i, 0], bi[i, 0], bi[i + 1, 0], ti[i + 1, 0]);                 // y=min wall
            m.Faces.AddFace(ti[i, Ny - 1], ti[i + 1, Ny - 1], bi[i + 1, Ny - 1], bi[i, Ny - 1]); // y=max wall
        }
        for (int j = 0; j < Ny - 1; j++)
        {
            m.Faces.AddFace(ti[0, j], ti[0, j + 1], bi[0, j + 1], bi[0, j]);                 // x=min wall
            m.Faces.AddFace(ti[Nx - 1, j], bi[Nx - 1, j], bi[Nx - 1, j + 1], ti[Nx - 1, j + 1]); // x=max wall
        }
        m.Normals.ComputeNormals(); m.Compact();
        return (m, thkN > 0 ? thkSum / thkN : 0.0);
    }
}
