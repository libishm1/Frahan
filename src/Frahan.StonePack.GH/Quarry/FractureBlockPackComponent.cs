using System;
using System.Drawing;
using System.Collections.Generic;
using System.Text;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH;

/// <summary>
/// Fracture Block Pack -- multiple-bin packing of fixed-size DIMENSION blocks into the
/// fracture-bounded intact rock zones (each split mesh slab = one BIN). The quarry-yield
/// question: how many marketable rectangular blocks can be cut from each intact zone between
/// the fractures.
///
/// Strategy = the balance verified against the two existing packers: TREE-PACK style COARSE
/// SUBDIVISION (tile the slab AABB on a regular block grid -- fast, like BlockPackTree's
/// guillotine on equal sizes) followed by an IRREGULAR-BOUNDARY FIT (keep only blocks fully
/// inside the slab mesh -- the wavy fracture top/bottom, like Pack3DIrregularContainer). A pure
/// AABB tree-pack over-counts ~3x because the box bin ignores the wavy boundary; the irregular
/// fit is what makes the yield real.
///
/// A FRACTURE CLEARANCE margin keeps blocks away from the (uncertain) fracture surface -- set it
/// to the fracture's position sigma (GPR Fracture Surfaces 3D) for uncertainty-safe blocks.
/// Fully managed -- no native shim.
/// </summary>
public sealed class FractureBlockPackComponent : FrahanComponentBase
{
    public FractureBlockPackComponent()
        : base("Fracture Block Pack", "FracBlockPack",
               "Pack fixed-size dimension blocks into each fracture-bounded slab (bin): tree-pack " +
               "coarse subdivision of the AABB + irregular-boundary fit to the slab mesh. Reports " +
               "per-bin yield. Managed.",
               "Frahan", "Quarry")
    { }

    public override Guid ComponentGuid => new Guid("A7E0B0F3-0C0F-4A16-9E3D-0FACE0FACE04");
    protected override Bitmap Icon => Frahan.GH.IconProvider.Load("BlockCutOpt.png");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Container Meshes", "C",
            "Fracture-bounded slab meshes (closed). Each is one BIN. From the split mesh bench / " +
            "fracture surfaces.", GH_ParamAccess.list);
        p.AddNumberParameter("Block Length", "Lx", "Dimension-block length (m).", GH_ParamAccess.item, 0.9);
        p.AddNumberParameter("Block Width", "Ly", "Dimension-block width (m).", GH_ParamAccess.item, 0.7);
        p.AddNumberParameter("Block Height", "Lz", "Dimension-block height (m).", GH_ParamAccess.item, 0.4);
        p.AddNumberParameter("Kerf", "K", "Saw-cut gap between blocks (m).", GH_ParamAccess.item, 0.03);
        p.AddNumberParameter("Fracture Clearance", "Cl",
            "Extra inward margin (m) every block must keep from the fracture boundary. Set it to the " +
            "fracture position sigma (GPR Fracture Surfaces 3D) for uncertainty-safe blocks. Default 0.",
            GH_ParamAccess.item, 0.0);
        p.AddBooleanParameter("Run", "R", "Compute the packing.", GH_ParamAccess.item, true);
        p.AddBooleanParameter("Uncertainty Safe", "US",
            "Toggle the deep-fracture safety allowance. FALSE = geometric yield (clearance ignored, the " +
            "optimistic number). TRUE = enforce the Fracture Clearance (wire it to the fracture sigma " +
            "from GPR Fracture Surfaces 3D) so no block sits within the measured GPR uncertainty of a " +
            "fracture -> uncertainty-safe yield. Default false.",
            GH_ParamAccess.item, false);
        p.AddIntegerParameter("Packer", "Pk",
            "Packing strategy. 0 = fixed axis grid. 1 = best-of (6 orientations x grid phase). 2 = " +
            "combined multi-size on a global grid. 3 = VOXEL-DLBF: per-block deepest-bottom-left-first " +
            "placement on a lattice (each block lands independently, conforming to the wavy boundary -- " +
            "adopted after a head-to-head where a Mosch-style voxel greedy beat the global grid). 4 = " +
            "VOXEL-DLBF + multi-size (max-yield mesh-bench algorithm, DEFAULT): per-block placement plus " +
            "the 1.0/0.66/0.5 marketable fill ladder, strict 8-corner irregular fit + kerf. Tops the " +
            "head-to-head vs Kim forest and the Mosch-style greedy, but is NOT guaranteed saw-separable. " +
            "5 = GUILLOTINE multi-size (MANUFACTURABLE): recursive full-span 3D guillotine so every block " +
            "is separable by edge-to-edge saw cuts; reports cutting-surface-area + cut count (Jalalian " +
            "I11 / saw-path cost). Trades a little yield for full manufacturability.", GH_ParamAccess.item, 4);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Blocks", "B", "Placed dimension-block meshes (closed boxes).", GH_ParamAccess.list);
        p.AddIntegerParameter("Bin Index", "Bi", "Container/bin index of each placed block.", GH_ParamAccess.list);
        p.AddIntegerParameter("Block Count", "N", "Blocks placed per bin.", GH_ParamAccess.list);
        p.AddNumberParameter("Recovered Volume", "V", "Recovered block volume per bin (m^3).", GH_ParamAccess.list);
        p.AddNumberParameter("Yield", "Y", "Recovered / intact volume per bin (0..1).", GH_ParamAccess.list);
        p.AddTextParameter("Report", "Rpt", "Per-bin yield summary.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var bins = new List<Mesh>();
        if (!da.GetDataList(0, bins) || bins.Count == 0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No container meshes."); return; }
        double Lx = 0.9, Ly = 0.7, Lz = 0.4, kerf = 0.03, clr = 0.0; bool run = true, safe = false; int packer = 4;
        da.GetData(1, ref Lx); da.GetData(2, ref Ly); da.GetData(3, ref Lz);
        da.GetData(4, ref kerf); da.GetData(5, ref clr); da.GetData(6, ref run);
        da.GetData(7, ref safe); da.GetData(8, ref packer);
        if (!run) return;
        if (Lx <= 0 || Ly <= 0 || Lz <= 0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Block dimensions must be > 0."); return; }

        // The toggle: geometric yield ignores the clearance; uncertainty-safe enforces it (= fracture sigma).
        double effClr = safe ? clr : 0.0;
        double blockVol = Lx * Ly * Lz;
        var blocks = new List<Mesh>();
        var binIdx = new List<int>();
        var counts = new List<int>();
        var vols = new List<double>();
        var yields = new List<double>();
        var rpt = new StringBuilder();
        string pkName = packer <= 0 ? "0 grid" : packer == 1 ? "1 best-of" : packer == 2 ? "2 combined-grid"
                        : packer == 3 ? "3 voxel-dlbf" : packer == 4 ? "4 voxel-dlbf-multi"
                        : "5 guillotine-separable";
        rpt.AppendLine($"mode: {(safe ? $"UNCERTAINTY-SAFE (clearance {effClr} m = fracture sigma)" : "geometric (clearance ignored)")}" +
                       $" | packer: {pkName}");
        rpt.AppendLine($"dimension block {Lx}x{Ly}x{Lz} m (kerf {kerf}, clearance {effClr}) into {bins.Count} fracture bins:");

        for (int s = 0; s < bins.Count; s++)
        {
            var mesh = bins[s];
            if (mesh == null || !mesh.IsValid) { counts.Add(0); vols.Add(0); yields.Add(0); continue; }
            mesh.Normals.ComputeNormals(); mesh.Compact();
            var placedBoxes = PackSlab(mesh, Lx, Ly, Lz, kerf, effClr, packer, out string layoutDesc);
            double rv = 0.0;
            foreach (var pbx in placedBoxes)
            {
                blocks.Add(Mesh.CreateFromBox(pbx, 1, 1, 1)); binIdx.Add(s);
                rv += (pbx.Max.X - pbx.Min.X) * (pbx.Max.Y - pbx.Min.Y) * (pbx.Max.Z - pbx.Min.Z);
            }
            int placed = placedBoxes.Count;
            double mv = Math.Abs(mesh.Volume());
            counts.Add(placed); vols.Add(rv); yields.Add(mv > 1e-9 ? rv / mv : 0.0);
            double phi = GuillotineSeparableFraction(placedBoxes, 1e-6);
            rpt.AppendLine($"  bin {s}: {layoutDesc} -> {placed} blocks, {rv:0.##} m^3 recovered " +
                           $"of {mv:0.##} m^3 intact ({(mv > 1e-9 ? 100.0 * rv / mv : 0):0.#}% yield)" +
                           $", guillotine-separable {100.0 * phi:0.#}%");
        }
        int totN = 0; double totV = 0, totM = 0;
        for (int s = 0; s < counts.Count; s++) { totN += counts[s]; totV += vols[s]; }
        foreach (var m in bins) if (m != null && m.IsValid) totM += Math.Abs(m.Volume());
        rpt.AppendLine($"TOTAL: {totN} blocks, {totV:0.##} m^3 recovered of {totM:0.##} m^3 " +
                       $"({(totM > 1e-9 ? 100.0 * totV / totM : 0):0.#}% overall yield)");

        da.SetDataList(0, blocks);
        da.SetDataList(1, binIdx);
        da.SetDataList(2, counts);
        da.SetDataList(3, vols);
        da.SetDataList(4, yields);
        da.SetData(5, rpt.ToString().TrimEnd());
    }

    // Pack one fracture-bounded slab. packer<=0 = the baseline fixed grid (orientation as given,
    // origin at the bbox min). packer>=1 = EVOLVED best-of: try all 6 axis orientations of the block
    // x grid phase offsets {0, half-step} per axis, keep the layout with the most blocks. The baseline
    // (orientation 0, offset 0) is one of the candidates, so the evolved result is always >= baseline.
    // This carries Kim 2025's rotation + best-of-N-forest idea into the irregular mesh-bench fit.
    // Size ladder for the COMBINED packer: primary, then smaller marketable cuts to fill boundary gaps.
    private static readonly double[] SizeLadder = { 1.0, 0.66, 0.5 };

    private static List<BoundingBox> PackSlab(Mesh mesh, double Lx, double Ly, double Lz,
        double kerf, double clr, int packer, out string desc)
    {
        var bb = mesh.GetBoundingBox(true);
        if (packer <= 0)
        {
            var g = PackGrid(mesh, bb, Lx, Ly, Lz, kerf, clr, 0, 0, 0, null, out int gnx, out int gny, out int gnz);
            desc = $"grid {gnx}x{gny}x{gnz}";
            return g;
        }
        if (packer == 1)
            return BestOf(mesh, bb, Lx, Ly, Lz, kerf, clr, null, out desc);

        if (packer == 2)
        {
            // COMBINED multi-size on a GLOBAL grid: pack the primary best-of, then fill gaps with
            // smaller marketable cuts (occupancy-checked). Raises yield over single-size best-of.
            var placed = new List<BoundingBox>();
            var sb = new StringBuilder("combined[");
            foreach (double f in SizeLadder)
            {
                var add = BestOf(mesh, bb, Lx * f, Ly * f, Lz * f, kerf, clr, placed, out string _);
                placed.AddRange(add);
                sb.Append($"{f:0.##}x:{add.Count} ");
            }
            desc = sb.ToString().TrimEnd() + "]";
            return placed;
        }

        if (packer == 5)
        {
            // GUILLOTINE (wire-saw manufacturable): best-of a 3-stage staged guillotine (horizontal beds
            // -> vertical strips -> cross cuts, the diamond-wire-saw sequence) and the recursive corner
            // guillotine. Every block is separable by full-span edge-to-edge planar cuts, so the layout
            // is wire-saw cuttable (separable fraction 1.0), unlike the max-yield voxel-DLBF. The staged
            // engine recovers most of the yield (the guillotine penalty is provably small); corner-greedy
            // is kept as a fallback candidate. Reports cutting-surface-area A_cut (Jalalian I11) + cuts.
            var stg = PackStagedGuillotine(mesh, bb, Lx, Ly, Lz, kerf, clr, out string sdesc);
            var cg = PackGuillotine(mesh, bb, Lx, Ly, Lz, kerf, clr, true, 0.05,
                                    out double cArea, out int cCuts, out string cdesc);
            var best = VolumeOf(stg) >= VolumeOf(cg) ? stg : cg;
            string bestDesc = VolumeOf(stg) >= VolumeOf(cg) ? sdesc : cdesc;
            double aCut = CutFaceArea(best, kerf, out int planes);
            double rv = VolumeOf(best);
            desc = $"{bestDesc} | A_cut {aCut:0.#} m^2 ({planes} planes), I11 {(rv > 1e-9 ? aCut / rv : 0):0.##} m^2/m^3, kerf-vol {kerf * aCut:0.##} m^3";
            return best;
        }

        // packer 3 or 4: VOXEL-DLBF per-block placement (max-yield -- each block lands at its own
        // lattice position, conforming to the wavy boundary far better than a global grid). packer >= 4
        // also runs the multi-size fill ladder. This is the max-yield mesh-bench algorithm.
        return VoxelDlbf(mesh, bb, Lx, Ly, Lz, kerf, clr, packer >= 4, out desc);
    }

    // Per-block deepest-bottom-left-first (DLBF) placement on a voxel lattice. An occupancy grid
    // (free = voxel centre inside the slab) lets each block land independently at any lattice cell,
    // not on one global grid -- this conforms to the irregular fracture boundary. Kerf is honoured by
    // reserving ceil((dim+kerf)/vox) voxels per block; every placement is confirmed by the strict
    // 8-corner IsPointInside fit. multiSize runs the 1.0/0.66/0.5 marketable ladder largest-first.
    private static List<BoundingBox> VoxelDlbf(Mesh mesh, BoundingBox bb, double Lx, double Ly, double Lz,
        double kerf, double clr, bool multiSize, out string desc)
    {
        double vox = 0.05;   // placement lattice; fine enough that the kerf-padded cell wastes little
        int nx = Math.Max(1, (int)((bb.Max.X - bb.Min.X) / vox));
        int ny = Math.Max(1, (int)((bb.Max.Y - bb.Min.Y) / vox));
        int nz = Math.Max(1, (int)((bb.Max.Z - bb.Min.Z) / vox));
        var free = new bool[nx, ny, nz];
        for (int ix = 0; ix < nx; ix++)
            for (int iy = 0; iy < ny; iy++)
                for (int iz = 0; iz < nz; iz++)
                    free[ix, iy, iz] = mesh.IsPointInside(
                        new Point3d(bb.Min.X + (ix + 0.5) * vox, bb.Min.Y + (iy + 0.5) * vox, bb.Min.Z + (iz + 0.5) * vox),
                        1e-6, false);
        var sizes = multiSize ? SizeLadder : new[] { 1.0 };
        var outBoxes = new List<BoundingBox>();
        var sb = new StringBuilder(multiSize ? "voxel-dlbf-multi[" : "voxel-dlbf[");
        foreach (double f in sizes)
        {
            var orients = new (double a, double b, double c)[]
            { (Lx*f,Ly*f,Lz*f),(Lx*f,Lz*f,Ly*f),(Ly*f,Lx*f,Lz*f),(Ly*f,Lz*f,Lx*f),(Lz*f,Lx*f,Ly*f),(Lz*f,Ly*f,Lx*f) };
            int placedThis = 0;
            for (int iz = 0; iz < nz; iz++)
                for (int iy = 0; iy < ny; iy++)
                    for (int ix = 0; ix < nx; ix++)
                    {
                        foreach (var o in orients)
                        {
                            int wx = Math.Max(1, (int)Math.Ceiling((o.a + kerf) / vox));
                            int wy = Math.Max(1, (int)Math.Ceiling((o.b + kerf) / vox));
                            int wz = Math.Max(1, (int)Math.Ceiling((o.c + kerf) / vox));
                            if (ix + wx > nx || iy + wy > ny || iz + wz > nz) continue;
                            bool ok = true;
                            for (int jx = ix; jx < ix + wx && ok; jx++)
                                for (int jy = iy; jy < iy + wy && ok; jy++)
                                    for (int jz = iz; jz < iz + wz && ok; jz++)
                                        if (!free[jx, jy, jz]) ok = false;
                            if (!ok) continue;
                            double x0 = bb.Min.X + ix * vox + kerf * 0.5;
                            double y0 = bb.Min.Y + iy * vox + kerf * 0.5;
                            double z0 = bb.Min.Z + iz * vox + kerf * 0.5;
                            var box = new BoundingBox(x0, y0, z0, x0 + o.a, y0 + o.b, z0 + o.c);
                            if (!BlockInside(mesh, box, clr)) continue;
                            for (int jx = ix; jx < ix + wx; jx++)
                                for (int jy = iy; jy < iy + wy; jy++)
                                    for (int jz = iz; jz < iz + wz; jz++)
                                        free[jx, jy, jz] = false;
                            outBoxes.Add(box); placedThis++;
                            break;
                        }
                    }
            sb.Append($"{f:0.##}x:{placedThis} ");
        }
        desc = sb.ToString().TrimEnd() + "]";
        return outBoxes;
    }

    // Best-of search over the 6 axis orientations x grid phase offsets {0, half-step}/axis, keeping
    // the most-blocks layout. `occupied` (may be null) are already-placed blocks to avoid overlapping.
    private static List<BoundingBox> BestOf(Mesh mesh, BoundingBox bb, double Lx, double Ly, double Lz,
        double kerf, double clr, List<BoundingBox> occupied, out string desc)
    {
        var orients = new (double a, double b, double c)[]
        {
            (Lx, Ly, Lz), (Lx, Lz, Ly), (Ly, Lx, Lz), (Ly, Lz, Lx), (Lz, Lx, Ly), (Lz, Ly, Lx),
        };
        List<BoundingBox> best = null; string bestDesc = "none"; int bestN = -1;
        foreach (var o in orients)
        {
            double sx = o.a + kerf, sy = o.b + kerf, sz = o.c + kerf;
            double[] ox = { 0.0, sx * 0.5 }, oy = { 0.0, sy * 0.5 }, oz = { 0.0, sz * 0.5 };
            foreach (var fx in ox) foreach (var fy in oy) foreach (var fz in oz)
            {
                var cand = PackGrid(mesh, bb, o.a, o.b, o.c, kerf, clr, fx, fy, fz, occupied, out int nx, out int ny, out int nz);
                if (cand.Count > bestN)
                {
                    bestN = cand.Count; best = cand;
                    bestDesc = $"best-of {o.a:0.##}x{o.b:0.##}x{o.c:0.##} grid {nx}x{ny}x{nz} phase({fx:0.##},{fy:0.##},{fz:0.##})";
                }
            }
        }
        desc = bestDesc;
        return best ?? new List<BoundingBox>();
    }

    // Lay an axis-aligned grid of (ax,ay,az) blocks (kerf-spaced) from bbox min + (offx,offy,offz),
    // keeping blocks that pass the irregular-boundary fit AND do not overlap any `occupied` block.
    private static List<BoundingBox> PackGrid(Mesh mesh, BoundingBox bb, double ax, double ay, double az,
        double kerf, double clr, double offx, double offy, double offz, List<BoundingBox> occupied,
        out int nx, out int ny, out int nz)
    {
        double sx = ax + kerf, sy = ay + kerf, sz = az + kerf;
        double x00 = bb.Min.X + offx, y00 = bb.Min.Y + offy, z00 = bb.Min.Z + offz;
        nx = Math.Max(0, (int)((bb.Max.X - x00) / sx));
        ny = Math.Max(0, (int)((bb.Max.Y - y00) / sy));
        nz = Math.Max(0, (int)((bb.Max.Z - z00) / sz));
        var outBoxes = new List<BoundingBox>();
        for (int ix = 0; ix < nx; ix++)
            for (int iy = 0; iy < ny; iy++)
                for (int iz = 0; iz < nz; iz++)
                {
                    double x0 = x00 + ix * sx + kerf * 0.5;
                    double y0 = y00 + iy * sy + kerf * 0.5;
                    double z0 = z00 + iz * sz + kerf * 0.5;
                    var box = new BoundingBox(x0, y0, z0, x0 + ax, y0 + ay, z0 + az);
                    if (!BlockInside(mesh, box, clr)) continue;
                    if (occupied != null && OverlapsAny(box, occupied, kerf)) continue;
                    outBoxes.Add(box);
                }
        return outBoxes;
    }

    // AABB overlap (each placed block grown by kerf) -- prevents the fill cuts from colliding.
    private static bool OverlapsAny(BoundingBox b, List<BoundingBox> placed, double kerf)
    {
        double h = kerf * 0.5;
        for (int i = 0; i < placed.Count; i++)
        {
            var p = placed[i];
            if (b.Min.X < p.Max.X + h && b.Max.X > p.Min.X - h
                && b.Min.Y < p.Max.Y + h && b.Max.Y > p.Min.Y - h
                && b.Min.Z < p.Max.Z + h && b.Max.Z > p.Min.Z - h)
                return true;
        }
        return false;
    }

    // A block fits the irregular bin if the block grown by `clr` on all sides lies fully inside
    // the (closed) slab mesh -- tested at the centre + 8 expanded corners (parity ray-cast, so
    // robust to mesh winding/sign).
    private static bool BlockInside(Mesh mesh, BoundingBox box, double clr)
    {
        var pts = new List<Point3d>
        {
            new Point3d((box.Min.X + box.Max.X) / 2, (box.Min.Y + box.Max.Y) / 2, (box.Min.Z + box.Max.Z) / 2)
        };
        double[] xs = { box.Min.X - clr, box.Max.X + clr };
        double[] ys = { box.Min.Y - clr, box.Max.Y + clr };
        double[] zs = { box.Min.Z - clr, box.Max.Z + clr };
        foreach (var px in xs) foreach (var py in ys) foreach (var pz in zs)
            pts.Add(new Point3d(px, py, pz));
        foreach (var p in pts)
            if (!mesh.IsPointInside(p, 1e-6, false)) return false;
        return true;
    }

    // ---- GUILLOTINE-CUTTABLE packer (mode 5): manufacturable mesh-bench packing ----
    // Recursive 3D guillotine on the slab. In each axis-aligned region: place the largest marketable
    // block (best of 6 orientations) flush in the min corner if it passes the strict 8-corner irregular
    // fit, then split the remainder with three full-span cuts (right/top/front -- the standard 3D
    // guillotine recursion) and recurse. If no block fits flush (the wavy fracture boundary clips it),
    // split the region in half along its longest axis with one full-span cut and recurse, so the packing
    // conforms to the boundary while STAYING separable by edge-to-edge saw cuts. Every block ends up in
    // the min corner of a region produced only by full-span cuts, so the layout is guillotine-cuttable by
    // construction (separable fraction = 1.0). Accumulates cutting-surface-area + cut count = the
    // saw-path / Jalalian I11 cost. Tracks A_cut only for splits that leave a non-empty sub-region.
    public static List<BoundingBox> PackGuillotine(Mesh mesh, BoundingBox bb, double Lx, double Ly, double Lz,
        double kerf, double clr, bool multiSize, double vox, out double cutAreaM2, out int cutCount, out string desc)
    {
        var sizes = multiSize ? SizeLadder : new[] { 1.0 };
        double minBlock = Math.Min(Lx, Math.Min(Ly, Lz)) * sizes[sizes.Length - 1];
        var outBoxes = new List<BoundingBox>();
        double area = 0.0; int cuts = 0;
        GuillotineRegion(mesh, bb, Lx, Ly, Lz, kerf, clr, sizes, minBlock, vox, 0, outBoxes, ref area, ref cuts);
        cutAreaM2 = area; cutCount = cuts;
        desc = $"guillotine{(multiSize ? "-multi" : "")} {outBoxes.Count} blk, {cuts} cuts, {area:0.##} m^2 cut-surface";
        return outBoxes;
    }

    private static void GuillotineRegion(Mesh mesh, BoundingBox r, double Lx, double Ly, double Lz,
        double kerf, double clr, double[] sizes, double minBlock, double vox, int depth,
        List<BoundingBox> outBoxes, ref double area, ref int cuts)
    {
        if (depth > 96) return;
        double rdx = r.Max.X - r.Min.X, rdy = r.Max.Y - r.Min.Y, rdz = r.Max.Z - r.Min.Z;
        if (rdx < minBlock + kerf || rdy < minBlock + kerf || rdz < minBlock + kerf) return;
        const double eps = 1e-9;

        // 1. try to corner-place the largest marketable block, best of 6 orientations
        foreach (double f in sizes)
        {
            var orients = new (double a, double b, double c)[]
            { (Lx*f,Ly*f,Lz*f),(Lx*f,Lz*f,Ly*f),(Ly*f,Lx*f,Lz*f),(Ly*f,Lz*f,Lx*f),(Lz*f,Lx*f,Ly*f),(Lz*f,Ly*f,Lx*f) };
            foreach (var o in orients)
            {
                if (o.a + kerf > rdx + eps || o.b + kerf > rdy + eps || o.c + kerf > rdz + eps) continue;
                double x0 = r.Min.X + kerf * 0.5, y0 = r.Min.Y + kerf * 0.5, z0 = r.Min.Z + kerf * 0.5;
                var box = new BoundingBox(x0, y0, z0, x0 + o.a, y0 + o.b, z0 + o.c);
                if (!BlockInside(mesh, box, clr)) continue;
                outBoxes.Add(box);

                // full-span guillotine split of the remainder: right (perp X), top (perp Y), front (perp Z)
                double xCut = r.Min.X + o.a + kerf, yCut = r.Min.Y + o.b + kerf, zCut = r.Min.Z + o.c + kerf;
                if (r.Max.X - xCut > minBlock + kerf)
                {
                    area += rdy * rdz; cuts++;
                    GuillotineRegion(mesh, new BoundingBox(xCut, r.Min.Y, r.Min.Z, r.Max.X, r.Max.Y, r.Max.Z),
                        Lx, Ly, Lz, kerf, clr, sizes, minBlock, vox, depth + 1, outBoxes, ref area, ref cuts);
                }
                if (r.Max.Y - yCut > minBlock + kerf)
                {
                    area += (o.a + kerf) * rdz; cuts++;
                    GuillotineRegion(mesh, new BoundingBox(r.Min.X, yCut, r.Min.Z, xCut, r.Max.Y, r.Max.Z),
                        Lx, Ly, Lz, kerf, clr, sizes, minBlock, vox, depth + 1, outBoxes, ref area, ref cuts);
                }
                if (r.Max.Z - zCut > minBlock + kerf)
                {
                    area += (o.a + kerf) * (o.b + kerf); cuts++;
                    GuillotineRegion(mesh, new BoundingBox(r.Min.X, r.Min.Y, zCut, xCut, yCut, r.Max.Z),
                        Lx, Ly, Lz, kerf, clr, sizes, minBlock, vox, depth + 1, outBoxes, ref area, ref cuts);
                }
                return;
            }
        }

        // 2. no block fit flush -> one full-span half-cut along the longest axis, recurse both halves
        //    (lets the packing shift to where a block fits inside the wavy boundary, still guillotine).
        if (rdx >= rdy && rdx >= rdz)
        {
            double c = r.Min.X + rdx * 0.5; area += rdy * rdz; cuts++;
            GuillotineRegion(mesh, new BoundingBox(r.Min.X, r.Min.Y, r.Min.Z, c, r.Max.Y, r.Max.Z),
                Lx, Ly, Lz, kerf, clr, sizes, minBlock, vox, depth + 1, outBoxes, ref area, ref cuts);
            GuillotineRegion(mesh, new BoundingBox(c, r.Min.Y, r.Min.Z, r.Max.X, r.Max.Y, r.Max.Z),
                Lx, Ly, Lz, kerf, clr, sizes, minBlock, vox, depth + 1, outBoxes, ref area, ref cuts);
        }
        else if (rdy >= rdz)
        {
            double c = r.Min.Y + rdy * 0.5; area += rdx * rdz; cuts++;
            GuillotineRegion(mesh, new BoundingBox(r.Min.X, r.Min.Y, r.Min.Z, r.Max.X, c, r.Max.Z),
                Lx, Ly, Lz, kerf, clr, sizes, minBlock, vox, depth + 1, outBoxes, ref area, ref cuts);
            GuillotineRegion(mesh, new BoundingBox(r.Min.X, c, r.Min.Z, r.Max.X, r.Max.Y, r.Max.Z),
                Lx, Ly, Lz, kerf, clr, sizes, minBlock, vox, depth + 1, outBoxes, ref area, ref cuts);
        }
        else
        {
            double c = r.Min.Z + rdz * 0.5; area += rdx * rdy; cuts++;
            GuillotineRegion(mesh, new BoundingBox(r.Min.X, r.Min.Y, r.Min.Z, r.Max.X, r.Max.Y, c),
                Lx, Ly, Lz, kerf, clr, sizes, minBlock, vox, depth + 1, outBoxes, ref area, ref cuts);
            GuillotineRegion(mesh, new BoundingBox(r.Min.X, r.Min.Y, c, r.Max.X, r.Max.Y, r.Max.Z),
                Lx, Ly, Lz, kerf, clr, sizes, minBlock, vox, depth + 1, outBoxes, ref area, ref cuts);
        }
    }

    // Greedy guillotine-separable fraction of an arbitrary axis-aligned layout (manufacturability score).
    // Repeatedly find a full-span axis-aligned plane (at a block face) that splits the group in two with
    // no box straddling it; recurse. A group that admits no such cut contributes zero. Returns the share
    // of blocks isolable by a full-span (saw-executable) cut sequence -- a lower bound, 1.0 = fully
    // saw-separable. The guillotine packer (mode 5) returns 1.0 by construction.
    public static double GuillotineSeparableFraction(List<BoundingBox> boxes, double eps)
    {
        if (boxes == null || boxes.Count <= 1) return 1.0;
        return (double)CountSeparable(boxes, eps) / boxes.Count;
    }

    private static int CountSeparable(List<BoundingBox> g, double eps)
    {
        int n = g.Count;
        if (n <= 1) return n;
        // axis order x,y,z; first admissible cut wins (greedy lower bound)
        for (int ax = 0; ax < 3; ax++)
        {
            // candidate cut coordinates = distinct box max faces along this axis
            var coords = new List<double>();
            foreach (var b in g) coords.Add(ax == 0 ? b.Max.X : ax == 1 ? b.Max.Y : b.Max.Z);
            coords.Sort();
            foreach (double c in coords)
            {
                var lo = new List<BoundingBox>(); var hi = new List<BoundingBox>(); bool straddle = false;
                foreach (var b in g)
                {
                    double bmin = ax == 0 ? b.Min.X : ax == 1 ? b.Min.Y : b.Min.Z;
                    double bmax = ax == 0 ? b.Max.X : ax == 1 ? b.Max.Y : b.Max.Z;
                    if (bmax <= c + eps) lo.Add(b);
                    else if (bmin >= c - eps) hi.Add(b);
                    else { straddle = true; break; }
                }
                if (straddle || lo.Count == 0 || hi.Count == 0) continue;
                return CountSeparable(lo, eps) + CountSeparable(hi, eps);
            }
        }
        return 0; // stuck cluster -> none of its blocks isolable by full-span cuts
    }

    // ---- STAGED 3-stage guillotine (the diamond-wire-saw sequence) ----
    // Per-dimension marketable size factors (primary, 2/3, 1/2, 1/3): a wire saw cuts rectangular
    // pieces, not only uniformly-scaled blocks, so each axis gets its own size set.
    private static readonly double[] DimFactors = { 1.0, 0.6667, 0.5, 0.3333 };
    private static double[] SizeSet(double d)
    {
        var a = new double[DimFactors.Length];
        for (int i = 0; i < DimFactors.Length; i++) a[i] = d * DimFactors[i];
        return a;
    }

    public static double VolumeOf(List<BoundingBox> bs)
    {
        double v = 0; if (bs == null) return 0;
        foreach (var b in bs) v += (b.Max.X - b.Min.X) * (b.Max.Y - b.Min.Y) * (b.Max.Z - b.Min.Z);
        return v;
    }

    // Best-of (6 axis orientations x phase {0, half}) over a 3-stage guillotine: stage 1 cuts horizontal
    // beds (Z layers), stage 2 cuts vertical strips (Y) within each layer, stage 3 cuts blocks (X) within
    // each strip. Each stage greedily takes the largest marketable size that fits; the block (X) stage
    // tries smaller lengths against the strict 8-corner irregular fit before skipping. Cuts are global per
    // stage, so the layout is guillotine-cuttable by construction (wire-saw realizable).
    public static List<BoundingBox> PackStagedGuillotine(Mesh mesh, BoundingBox bb, double Lx, double Ly,
        double Lz, double kerf, double clr, out string desc)
    {
        var sets = new (double[] X, double[] Y, double[] Z, string nm)[]
        {
            (SizeSet(Lx), SizeSet(Ly), SizeSet(Lz), "XYZ"),
            (SizeSet(Lx), SizeSet(Lz), SizeSet(Ly), "XZY"),
            (SizeSet(Ly), SizeSet(Lx), SizeSet(Lz), "YXZ"),
            (SizeSet(Ly), SizeSet(Lz), SizeSet(Lx), "YZX"),
            (SizeSet(Lz), SizeSet(Lx), SizeSet(Ly), "ZXY"),
            (SizeSet(Lz), SizeSet(Ly), SizeSet(Lx), "ZYX"),
        };
        List<BoundingBox> best = null; double bestV = -1; string bestNm = "none";
        foreach (var st in sets)
        {
            double[] phx = { 0, (st.X[0] + kerf) * 0.5 }, phy = { 0, (st.Y[0] + kerf) * 0.5 }, phz = { 0, (st.Z[0] + kerf) * 0.5 };
            foreach (var fx in phx) foreach (var fy in phy) foreach (var fz in phz)
            {
                var cand = StagedPass(mesh, bb, st.X, st.Y, st.Z, kerf, clr, fx, fy, fz);
                double v = VolumeOf(cand);
                if (v > bestV) { bestV = v; best = cand; bestNm = st.nm; }
            }
        }
        desc = $"staged-guillotine {best?.Count ?? 0} blk (orient {bestNm})";
        return best ?? new List<BoundingBox>();
    }

    private static List<BoundingBox> StagedPass(Mesh mesh, BoundingBox bb, double[] Xs, double[] Ys,
        double[] Zs, double kerf, double clr, double ox, double oy, double oz)
    {
        const double eps = 1e-9;
        var outB = new List<BoundingBox>();
        double zmin = bb.Min.Z + oz, zmax = bb.Max.Z, ymin = bb.Min.Y + oy, ymax = bb.Max.Y, xmin = bb.Min.X + ox, xmax = bb.Max.X;
        double minZ = Zs[Zs.Length - 1], minY = Ys[Ys.Length - 1], minX = Xs[Xs.Length - 1];
        double z = zmin;
        while (z + minZ + kerf <= zmax + eps)
        {
            double hz = 0; foreach (double s in Zs) { if (z + s + kerf <= zmax + eps) { hz = s; break; } }
            if (hz == 0) { z += minZ + kerf; continue; }
            double y = ymin;
            while (y + minY + kerf <= ymax + eps)
            {
                double wy = 0; foreach (double s in Ys) { if (y + s + kerf <= ymax + eps) { wy = s; break; } }
                if (wy == 0) { y += minY + kerf; continue; }
                double x = xmin;
                while (x + minX + kerf <= xmax + eps)
                {
                    bool placed = false;
                    foreach (double lx in Xs)
                    {
                        if (x + lx + kerf > xmax + eps) continue;
                        var box = new BoundingBox(x + kerf * 0.5, y + kerf * 0.5, z + kerf * 0.5,
                                                  x + kerf * 0.5 + lx, y + kerf * 0.5 + wy, z + kerf * 0.5 + hz);
                        if (BlockInside(mesh, box, clr)) { outB.Add(box); x += lx + kerf; placed = true; break; }
                    }
                    if (!placed) x += minX + kerf;
                }
                y += wy + kerf;
            }
            z += hz + kerf;
        }
        return outB;
    }

    // Cutting-surface area A_cut (Jalalian I11 numerator): sum of the distinct planar saw cuts that free
    // the blocks. Each block face contributes half its area; a face that does not abut a neighbour block
    // (it borders waste / the slab boundary) contributes the other half, so a shared cut is charged once
    // and a waste-bordering cut once. Kerf volume V_w = kerf * A_cut stays below the sawn-away volume.
    public static double CutFaceArea(List<BoundingBox> bs, double kerf, out int planes)
    {
        double acut = 0; var planeSet = new HashSet<string>();
        for (int i = 0; i < bs.Count; i++)
        {
            var b = bs[i];
            double dx = b.Max.X - b.Min.X, dy = b.Max.Y - b.Min.Y, dz = b.Max.Z - b.Min.Z;
            var faces = new (int ax, double coord, double area, bool isMax)[]
            {
                (0, b.Min.X, dy*dz, false), (0, b.Max.X, dy*dz, true),
                (1, b.Min.Y, dx*dz, false), (1, b.Max.Y, dx*dz, true),
                (2, b.Min.Z, dx*dy, false), (2, b.Max.Z, dx*dy, true),
            };
            foreach (var fc in faces)
            {
                acut += 0.5 * fc.area;
                bool shared = false;
                for (int j = 0; j < bs.Count && !shared; j++)
                {
                    if (j == i) continue; var o = bs[j];
                    double oc = fc.isMax ? (fc.ax == 0 ? o.Min.X : fc.ax == 1 ? o.Min.Y : o.Min.Z)
                                         : (fc.ax == 0 ? o.Max.X : fc.ax == 1 ? o.Max.Y : o.Max.Z);
                    if (Math.Abs(oc - fc.coord) > kerf + 1e-4) continue;
                    bool ov = true;
                    for (int a = 0; a < 3 && ov; a++)
                    {
                        if (a == fc.ax) continue;
                        double bmin = a == 0 ? b.Min.X : a == 1 ? b.Min.Y : b.Min.Z, bmax = a == 0 ? b.Max.X : a == 1 ? b.Max.Y : b.Max.Z;
                        double omin = a == 0 ? o.Min.X : a == 1 ? o.Min.Y : o.Min.Z, omax = a == 0 ? o.Max.X : a == 1 ? o.Max.Y : o.Max.Z;
                        if (Math.Min(bmax, omax) - Math.Max(bmin, omin) <= 1e-4) ov = false;
                    }
                    if (ov) shared = true;
                }
                if (!shared) acut += 0.5 * fc.area;
                planeSet.Add($"{fc.ax}:{Math.Round(fc.coord, 2)}");
            }
        }
        planes = planeSet.Count;
        return acut;
    }
}
