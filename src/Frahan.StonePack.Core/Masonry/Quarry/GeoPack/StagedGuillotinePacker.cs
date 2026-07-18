#nullable disable
using System;
using System.Collections.Generic;
using System.Text;

namespace Frahan.Masonry.Quarry.GeoPack;

// =============================================================================
// StagedGuillotinePacker -- Rhino-free C# port of the FRACTURE-FOLLOWING STAGED
// GUILLOTINE block packer. Axis-aligned (H/V) full-span wire-saw cuts that FOLLOW
// the mapped fractures: a joint becomes a free cut, the bench splits into
// fracture-bounded slabs, then each slab is packed with H/V-only staged cuts.
//
// Ported faithfully from the two Frahan GH quarry components (the C# source of
// truth), and cross-checked against the independent Python verifier pyfrahan
// (outputs/2026-07-15/krishnagiri_survey/scripts/pyfrahan/pack_guillotine.py):
//   Quarry/FractureBoundedSlabsComponent.cs (Sample / StitchSlab / SolveSafe)
//     -> SampleSurfaceZ / StitchSlab / FractureBoundedSlabs.Build
//   Quarry/FractureBlockPackComponent.cs, packer mode 5 (PackStagedGuillotine)
//     -> PackStagedGuillotine / StagedPass  (the 3-stage wire-saw sequence)
//     -> BlockInside                        (the strict 8-corner irregular fit)
//     -> SeparableFraction / CountSeparable (the manufacturability score)
//     -> CutFaceArea                        (A_cut, the Jalalian I11 numerator)
//     -> SawPassLines                       (plan-view cut centre-lines)
//
// WHY A SEPARATE PORT (reuse-vs-new, documented):
//  * The GH components hold the algorithm but reference Rhino.Geometry
//    (BoundingBox / Mesh / Point3d / Intersection.MeshRay / Mesh.IsPointInside),
//    so they cannot build headless. This port replaces those with:
//      - GBox: a min/max axis box mirroring Rhino BoundingBox exactly. The
//        in-repo Rhino-free Box3 (GeometryPrimitives.cs) routes through Size3,
//        which THROWS on non-positive extents and exposes no min/max face
//        arithmetic; the guillotine / separability / A_cut code needs raw min/max
//        faces, so GBox is new (necessary, documented).
//      - TriMeshInside.Contains: a parity vertical-ray point-in-mesh cast (odd
//        crossings above p == inside), the robust equivalent of Rhino
//        Mesh.IsPointInside(p,1e-6,false) for a watertight mesh. This is the SAME
//        substitution the pyfrahan port documents as D-GUI-1.
//  * Deterministic: no RNG, fixed iteration order, so identical inputs give
//    bit-identical placements (the parity harness asserts this vs pyfrahan).
//
// Documented deviations (mirror pyfrahan pack_guillotine.py):
//  D-GUI-1  point-in-mesh -> parity vertical-ray cast (above).
//  D-GUI-2  the GH component packs pre-built slab meshes (Container Meshes input);
//           this port BUILDS the slabs (FractureBoundedSlabs) so it runs on a bare
//           surface end to end. With a pre-closed slab the slab step is identity.
//  D-GUI-3  Factors is exposed (default the C# ladder {1,2/3,1/2,1/3}). Passing a
//           single {1.0} gives the single-size guillotine used for the clean
//           fracture-following-vs-grid-avoidance comparison at one block size.
//  D-GUI-5  separability / A_cut / saw-pass reporting is per-SLAB (per bin),
//           matching the GH per-bin report. phi == 1.0 is a per-slab invariant.
// =============================================================================

/// <summary>Axis-aligned box (min,max) mirroring Rhino BoundingBox. Rhino-free.</summary>
public readonly struct GBox
{
    public readonly double MinX, MinY, MinZ, MaxX, MaxY, MaxZ;

    public GBox(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        MinX = minX; MinY = minY; MinZ = minZ; MaxX = maxX; MaxY = maxY; MaxZ = maxZ;
    }

    public double SizeX => MaxX - MinX;
    public double SizeY => MaxY - MinY;
    public double SizeZ => MaxZ - MinZ;
    public double Volume => SizeX * SizeY * SizeZ;

    /// <summary>Min/max face coordinate along axis 0=X,1=Y,2=Z.</summary>
    public double Min(int axis) => axis == 0 ? MinX : axis == 1 ? MinY : MinZ;
    public double Max(int axis) => axis == 0 ? MaxX : axis == 1 ? MaxY : MaxZ;
}

/// <summary>
/// Closed triangle mesh with a fast vertical-ray point-in-mesh test. Rhino-free.
/// A query point p is inside iff a +Z ray from p crosses an odd number of
/// triangles strictly above p (parity rule; the robust equivalent of Rhino
/// Mesh.IsPointInside for a watertight mesh). Port of pyfrahan MeshInside.
/// </summary>
public sealed class TriMeshInside
{
    // Per-triangle precomputed barycentric edge data (parallel arrays, T entries).
    private readonly double[] _ax, _ay, _az;
    private readonly double[] _e1x, _e1y, _e1z;
    private readonly double[] _e2x, _e2y, _e2z;
    private readonly double[] _det;
    // Flattened vertex list for the nearest-vertex fallback (3T entries).
    private readonly double[] _vx, _vy, _vz;
    private readonly int _n;

    public double MinX { get; }
    public double MinY { get; }
    public double MinZ { get; }
    public double MaxX { get; }
    public double MaxY { get; }
    public double MaxZ { get; }

    /// <summary>tri[t] = {ax,ay,az, bx,by,bz, cx,cy,cz} (9 doubles per triangle).</summary>
    public TriMeshInside(IReadOnlyList<double[]> triangles)
    {
        _n = triangles.Count;
        _ax = new double[_n]; _ay = new double[_n]; _az = new double[_n];
        _e1x = new double[_n]; _e1y = new double[_n]; _e1z = new double[_n];
        _e2x = new double[_n]; _e2y = new double[_n]; _e2z = new double[_n];
        _det = new double[_n];
        _vx = new double[_n * 3]; _vy = new double[_n * 3]; _vz = new double[_n * 3];
        double bx0 = double.MaxValue, by0 = double.MaxValue, bz0 = double.MaxValue;
        double bx1 = double.MinValue, by1 = double.MinValue, bz1 = double.MinValue;
        for (int t = 0; t < _n; t++)
        {
            var tv = triangles[t];
            double ax = tv[0], ay = tv[1], az = tv[2];
            double bx = tv[3], by = tv[4], bz = tv[5];
            double cx = tv[6], cy = tv[7], cz = tv[8];
            _ax[t] = ax; _ay[t] = ay; _az[t] = az;
            _e1x[t] = bx - ax; _e1y[t] = by - ay; _e1z[t] = bz - az;
            _e2x[t] = cx - ax; _e2y[t] = cy - ay; _e2z[t] = cz - az;
            _det[t] = _e1x[t] * _e2y[t] - _e1y[t] * _e2x[t];
            int b = t * 3;
            _vx[b] = ax; _vy[b] = ay; _vz[b] = az;
            _vx[b + 1] = bx; _vy[b + 1] = by; _vz[b + 1] = bz;
            _vx[b + 2] = cx; _vy[b + 2] = cy; _vz[b + 2] = cz;
            bx0 = Math.Min(bx0, Math.Min(ax, Math.Min(bx, cx)));
            by0 = Math.Min(by0, Math.Min(ay, Math.Min(by, cy)));
            bz0 = Math.Min(bz0, Math.Min(az, Math.Min(bz, cz)));
            bx1 = Math.Max(bx1, Math.Max(ax, Math.Max(bx, cx)));
            by1 = Math.Max(by1, Math.Max(ay, Math.Max(by, cy)));
            bz1 = Math.Max(bz1, Math.Max(az, Math.Max(bz, cz)));
        }
        MinX = bx0; MinY = by0; MinZ = bz0; MaxX = bx1; MaxY = by1; MaxZ = bz1;
    }

    public int TriangleCount => _n;

    /// <summary>Odd upward crossings == inside. Port of MeshInside.contains.</summary>
    public bool Contains(double px, double py, double pz, double tol = 1e-9)
    {
        int crossings = 0;
        for (int t = 0; t < _n; t++)
        {
            double det = _det[t];
            if (Math.Abs(det) <= 1e-15) continue;
            double rx = px - _ax[t];
            double ry = py - _ay[t];
            double u = (rx * _e2y[t] - ry * _e2x[t]) / det;
            double v = (_e1x[t] * ry - _e1y[t] * rx) / det;
            if (u >= -tol && v >= -tol && u + v <= 1.0 + tol)
            {
                double zHit = _az[t] + u * _e1z[t] + v * _e2z[t];
                if (zHit > pz + tol) crossings++;
            }
        }
        return (crossings & 1) == 1;
    }

    /// <summary>
    /// Highest +Z ray crossing at (gx,gy) (first hit from above), nearest-vertex
    /// fallback. Port of FractureBoundedSlabs.SampleSurfaceZ / GH Sample.
    /// </summary>
    public double SampleZ(double gx, double gy, double zMid)
    {
        double best = double.NegativeInfinity;
        bool anyHit = false;
        for (int t = 0; t < _n; t++)
        {
            double det = _det[t];
            if (Math.Abs(det) <= 1e-15) continue;
            double rx = gx - _ax[t];
            double ry = gy - _ay[t];
            double u = (rx * _e2y[t] - ry * _e2x[t]) / det;
            double v = (_e1x[t] * ry - _e1y[t] * rx) / det;
            if (u >= -1e-9 && v >= -1e-9 && u + v <= 1.0 + 1e-9)
            {
                double zHit = _az[t] + u * _e1z[t] + v * _e2z[t];
                if (zHit > best) { best = zHit; anyHit = true; }
            }
        }
        if (anyHit) return best;
        if (_vx.Length == 0) return zMid;
        int bestIdx = 0; double bestD2 = double.MaxValue;
        for (int k = 0; k < _vx.Length; k++)
        {
            double dx = _vx[k] - gx, dy = _vy[k] - gy;
            double d2 = dx * dx + dy * dy;
            if (d2 < bestD2) { bestD2 = d2; bestIdx = k; }
        }
        return _vz[bestIdx];
    }
}

/// <summary>One fracture-bounded slab: its closed mesh, AABB, mean thickness.</summary>
public sealed class GuillotineSlab
{
    public GuillotineSlab(TriMeshInside mesh, double meanThickness, string label)
    {
        Mesh = mesh;
        MeanThickness = meanThickness;
        Label = label;
        Bounds = new GBox(mesh.MinX, mesh.MinY, mesh.MinZ, mesh.MaxX, mesh.MaxY, mesh.MaxZ);
    }

    public TriMeshInside Mesh { get; }
    public GBox Bounds { get; }
    public double MeanThickness { get; }
    public string Label { get; }
}

/// <summary>
/// Per-slab + aggregate result of the fracture-following staged guillotine pack.
/// Named to avoid colliding with Frahan.Core.Packing.GuillotinePackResult (Kim
/// forest packer). Mirrors pyfrahan GuillotinePackResult.
/// </summary>
public sealed class FractureGuillotineResult
{
    public List<GBox> Blocks = new List<GBox>();          // all placed blocks (across slabs)
    public List<int> BinIndex = new List<int>();          // slab index per block
    public List<List<GBox>> SlabBlocks = new List<List<GBox>>();
    public List<string> SlabDesc = new List<string>();
    public List<double> SeparableFraction = new List<double>(); // per slab (== 1.0)
    public double CutAreaM2;                               // A_cut summed over slabs
    public int CutPlanes;                                 // distinct planes summed over slabs
    public double RecoveredVolumeM3;
    public double TestedVolumeM3;                         // full bench volume
    public double Kerf;
    public double BlockVolumeM3;

    public int BlockCount => Blocks.Count;

    public double RecoveryFraction => TestedVolumeM3 > 1e-12 ? RecoveredVolumeM3 / TestedVolumeM3 : 0.0;

    public double MinSeparableFraction
    {
        get
        {
            if (SeparableFraction.Count == 0) return 1.0;
            double m = SeparableFraction[0];
            for (int i = 1; i < SeparableFraction.Count; i++) m = Math.Min(m, SeparableFraction[i]);
            return m;
        }
    }
}

/// <summary>
/// The fracture-following staged guillotine packer (Rhino-free port). All methods
/// are static and deterministic.
/// </summary>
public static class StagedGuillotinePacker
{
    private const double Eps = 1e-9;

    // Per-dimension marketable size factors (primary, 2/3, 1/2, 1/3) -- DimFactors
    // in FractureBlockPackComponent.cs. A wire saw cuts rectangular pieces, so each
    // axis gets its own size set.
    public static readonly double[] DimFactors = { 1.0, 0.6667, 0.5, 0.3333 };

    /// <summary>Port of SizeSet(d): d * factors, largest first.</summary>
    public static double[] SizeSet(double d, double[] factors = null)
    {
        double[] f = factors ?? DimFactors;
        var a = new double[f.Length];
        for (int i = 0; i < f.Length; i++) a[i] = d * f[i];
        return a;
    }

    /// <summary>Port of VolumeOf.</summary>
    public static double VolumeOf(List<GBox> boxes)
    {
        double v = 0.0;
        if (boxes == null) return 0.0;
        for (int i = 0; i < boxes.Count; i++) v += boxes[i].Volume;
        return v;
    }

    // -----------------------------------------------------------------------
    // 8-corner irregular fit -- port of FractureBlockPackComponent.BlockInside
    // -----------------------------------------------------------------------
    /// <summary>The block grown by clr on all sides lies fully inside the (closed)
    /// slab mesh -- centre + 8 expanded corners.</summary>
    public static bool BlockInside(TriMeshInside mesh, GBox box, double clr)
    {
        double cx = 0.5 * (box.MinX + box.MaxX);
        double cy = 0.5 * (box.MinY + box.MaxY);
        double cz = 0.5 * (box.MinZ + box.MaxZ);
        if (!mesh.Contains(cx, cy, cz)) return false;
        double x0 = box.MinX - clr, x1 = box.MaxX + clr;
        double y0 = box.MinY - clr, y1 = box.MaxY + clr;
        double z0 = box.MinZ - clr, z1 = box.MaxZ + clr;
        // 8 corners in the SAME order as the C# / Python (px outer, py, pz inner).
        double[] xs = { x0, x1 }, ys = { y0, y1 }, zs = { z0, z1 };
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 2; j++)
                for (int k = 0; k < 2; k++)
                    if (!mesh.Contains(xs[i], ys[j], zs[k])) return false;
        return true;
    }

    // -----------------------------------------------------------------------
    // staged 3-stage guillotine -- port of PackStagedGuillotine / StagedPass
    // -----------------------------------------------------------------------
    private static List<GBox> StagedPass(TriMeshInside mesh, GBox bb, double[] Xs, double[] Ys,
        double[] Zs, double kerf, double clr, double ox, double oy, double oz)
    {
        var outB = new List<GBox>();
        double zmin = bb.MinZ + oz, zmax = bb.MaxZ;
        double ymin = bb.MinY + oy, ymax = bb.MaxY;
        double xmin = bb.MinX + ox, xmax = bb.MaxX;
        double minZ = Zs[Zs.Length - 1], minY = Ys[Ys.Length - 1], minX = Xs[Xs.Length - 1];
        double z = zmin;
        while (z + minZ + kerf <= zmax + Eps)
        {
            double hz = 0.0;
            for (int s = 0; s < Zs.Length; s++) { if (z + Zs[s] + kerf <= zmax + Eps) { hz = Zs[s]; break; } }
            if (hz == 0.0) { z += minZ + kerf; continue; }
            double y = ymin;
            while (y + minY + kerf <= ymax + Eps)
            {
                double wy = 0.0;
                for (int s = 0; s < Ys.Length; s++) { if (y + Ys[s] + kerf <= ymax + Eps) { wy = Ys[s]; break; } }
                if (wy == 0.0) { y += minY + kerf; continue; }
                double x = xmin;
                while (x + minX + kerf <= xmax + Eps)
                {
                    bool placed = false;
                    for (int s = 0; s < Xs.Length; s++)
                    {
                        double lx = Xs[s];
                        if (x + lx + kerf > xmax + Eps) continue;
                        var box = new GBox(x + kerf * 0.5, y + kerf * 0.5, z + kerf * 0.5,
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

    /// <summary>Port of PackStagedGuillotine: best-of (6 axis-role orientations x
    /// phase {0,half} per axis) over the 3-stage guillotine, keep max-volume pass.</summary>
    public static List<GBox> PackStagedGuillotine(TriMeshInside mesh, GBox bb,
        double Lx, double Ly, double Lz, double kerf, double clr, out string desc,
        double[] factors = null)
    {
        double[] SS(double d) => SizeSet(d, factors);
        var sets = new (double[] X, double[] Y, double[] Z, string nm)[]
        {
            (SS(Lx), SS(Ly), SS(Lz), "XYZ"),
            (SS(Lx), SS(Lz), SS(Ly), "XZY"),
            (SS(Ly), SS(Lx), SS(Lz), "YXZ"),
            (SS(Ly), SS(Lz), SS(Lx), "YZX"),
            (SS(Lz), SS(Lx), SS(Ly), "ZXY"),
            (SS(Lz), SS(Ly), SS(Lx), "ZYX"),
        };
        List<GBox> best = null;
        double bestV = -1.0;
        string bestNm = "none";
        foreach (var st in sets)
        {
            double[] phx = { 0.0, (st.X[0] + kerf) * 0.5 };
            double[] phy = { 0.0, (st.Y[0] + kerf) * 0.5 };
            double[] phz = { 0.0, (st.Z[0] + kerf) * 0.5 };
            foreach (var fx in phx)
                foreach (var fy in phy)
                    foreach (var fz in phz)
                    {
                        var cand = StagedPass(mesh, bb, st.X, st.Y, st.Z, kerf, clr, fx, fy, fz);
                        double v = VolumeOf(cand);
                        if (v > bestV) { bestV = v; best = cand; bestNm = st.nm; }
                    }
        }
        if (best == null) best = new List<GBox>();
        desc = string.Format("staged-guillotine {0} blk (orient {1})", best.Count, bestNm);
        return best;
    }

    // -----------------------------------------------------------------------
    // guillotine-separability -- port of GuillotineSeparableFraction / CountSeparable
    // -----------------------------------------------------------------------
    private static int CountSeparable(List<GBox> g, double eps)
    {
        int n = g.Count;
        if (n <= 1) return n;
        for (int ax = 0; ax < 3; ax++)
        {
            var coords = new List<double>(n);
            for (int i = 0; i < n; i++) coords.Add(g[i].Max(ax));
            coords.Sort();
            for (int ci = 0; ci < coords.Count; ci++)
            {
                double c = coords[ci];
                var lo = new List<GBox>();
                var hi = new List<GBox>();
                bool straddle = false;
                for (int i = 0; i < n; i++)
                {
                    double bmin = g[i].Min(ax);
                    double bmax = g[i].Max(ax);
                    if (bmax <= c + eps) lo.Add(g[i]);
                    else if (bmin >= c - eps) hi.Add(g[i]);
                    else { straddle = true; break; }
                }
                if (straddle || lo.Count == 0 || hi.Count == 0) continue;
                return CountSeparable(lo, eps) + CountSeparable(hi, eps);
            }
        }
        return 0; // stuck cluster
    }

    /// <summary>Port of GuillotineSeparableFraction. Staged guillotine returns 1.0.</summary>
    public static double SeparableFraction(List<GBox> boxes, double eps = 1e-6)
    {
        if (boxes == null || boxes.Count <= 1) return 1.0;
        return (double)CountSeparable(boxes, eps) / boxes.Count;
    }

    /// <summary>
    /// SAT-overlap straddler count: number of placed block PAIRS whose interiors
    /// overlap by more than eps on all three axes. For axis-aligned boxes the
    /// 13-axis SAT collapses to this 3-axis face-overlap test (the 9 edge-cross
    /// axes are degenerate), so this IS the SAT result. Staged guillotine returns 0
    /// (neighbours share exactly one kerf gap, never interpenetrate).
    /// </summary>
    public static int SatOverlapPairs(List<GBox> boxes, double eps = 1e-9)
    {
        int count = 0;
        if (boxes == null) return 0;
        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
            {
                var a = boxes[i]; var b = boxes[j];
                double ox = Math.Min(a.MaxX, b.MaxX) - Math.Max(a.MinX, b.MinX);
                double oy = Math.Min(a.MaxY, b.MaxY) - Math.Max(a.MinY, b.MinY);
                double oz = Math.Min(a.MaxZ, b.MaxZ) - Math.Max(a.MinZ, b.MinZ);
                if (ox > eps && oy > eps && oz > eps) count++;
            }
        return count;
    }

    // -----------------------------------------------------------------------
    // cutting-surface area A_cut -- port of CutFaceArea (Jalalian I11 numerator)
    // -----------------------------------------------------------------------
    /// <summary>Port of CutFaceArea. Each face contributes half its area; a face
    /// that does not abut a neighbour block (borders waste) contributes the other
    /// half too. Returns A_cut; outputs the distinct-plane count.</summary>
    public static double CutFaceArea(List<GBox> bs, double kerf, out int planes)
    {
        double acut = 0.0;
        var planeSet = new HashSet<string>();
        int n = bs.Count;
        for (int i = 0; i < n; i++)
        {
            var b = bs[i];
            double dx = b.SizeX, dy = b.SizeY, dz = b.SizeZ;
            // (axis, coord, area, isMax)
            var faces = new (int ax, double coord, double area, bool isMax)[]
            {
                (0, b.MinX, dy * dz, false), (0, b.MaxX, dy * dz, true),
                (1, b.MinY, dx * dz, false), (1, b.MaxY, dx * dz, true),
                (2, b.MinZ, dx * dy, false), (2, b.MaxZ, dx * dy, true),
            };
            foreach (var fc in faces)
            {
                acut += 0.5 * fc.area;
                bool shared = false;
                for (int j = 0; j < n && !shared; j++)
                {
                    if (j == i) continue;
                    var o = bs[j];
                    double oc = fc.isMax ? o.Min(fc.ax) : o.Max(fc.ax);
                    if (Math.Abs(oc - fc.coord) > kerf + 1e-4) continue;
                    bool ov = true;
                    for (int a = 0; a < 3 && ov; a++)
                    {
                        if (a == fc.ax) continue;
                        double bmin = b.Min(a), bmax = b.Max(a);
                        double omin = o.Min(a), omax = o.Max(a);
                        if (Math.Min(bmax, omax) - Math.Max(bmin, omin) <= 1e-4) ov = false;
                    }
                    if (ov) shared = true;
                }
                if (!shared) acut += 0.5 * fc.area;
                planeSet.Add(fc.ax + ":" + Math.Round(fc.coord, 2).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        planes = planeSet.Count;
        return acut;
    }

    // -----------------------------------------------------------------------
    // saw-pass centre-lines -- port of SawPassLines (plan-view cut plan)
    // -----------------------------------------------------------------------
    /// <summary>Port of SawPassLines. Returns full-span vertical cut centre-lines
    /// as (coord, lo, hi) at face +- kerf/2. xCuts (X-rip planes) then yCuts.</summary>
    public static (List<(double coord, double lo, double hi)> xCuts,
                   List<(double coord, double lo, double hi)> yCuts)
        SawPassLines(List<GBox> boxes, double kerf)
    {
        var xd = new SortedDictionary<double, (double lo, double hi)>();
        var yd = new SortedDictionary<double, (double lo, double hi)>();
        void Add(SortedDictionary<double, (double lo, double hi)> d, double coord, double lo, double hi)
        {
            double c = Math.Round(coord, 3);
            if (d.TryGetValue(c, out var span)) d[c] = (Math.Min(span.lo, lo), Math.Max(span.hi, hi));
            else d[c] = (lo, hi);
        }
        if (boxes != null)
            foreach (var b in boxes)
            {
                Add(xd, b.MinX - kerf * 0.5, b.MinY - kerf * 0.5, b.MaxY + kerf * 0.5);
                Add(xd, b.MaxX + kerf * 0.5, b.MinY - kerf * 0.5, b.MaxY + kerf * 0.5);
                Add(yd, b.MinY - kerf * 0.5, b.MinX - kerf * 0.5, b.MaxX + kerf * 0.5);
                Add(yd, b.MaxY + kerf * 0.5, b.MinX - kerf * 0.5, b.MaxX + kerf * 0.5);
            }
        var xs = new List<(double, double, double)>();
        foreach (var kv in xd) xs.Add((kv.Key, kv.Value.lo, kv.Value.hi));
        var ys = new List<(double, double, double)>();
        foreach (var kv in yd) ys.Add((kv.Key, kv.Value.lo, kv.Value.hi));
        return (xs, ys);
    }

    // -----------------------------------------------------------------------
    // analytic no-fracture grid count (self-test anchor)
    // -----------------------------------------------------------------------
    /// <summary>The primary-block grid count of the staged guillotine on a full box
    /// with NO fracture: max over the 6 axis-role orientations of
    /// floor(dim/(blockdim+kerf)) per axis. Port of pyfrahan analytic_grid_count.</summary>
    public static int AnalyticGridCount(GBox bench, double Lx, double Ly, double Lz, double kerf)
    {
        double W = bench.SizeX, H = bench.SizeY, D = bench.SizeZ;
        int NFit(double span, double blk) => (int)Math.Floor((span + Eps) / (blk + kerf));
        int best = 0;
        var orients = new (double a, double b, double c)[]
        { (Lx, Ly, Lz), (Lx, Lz, Ly), (Ly, Lx, Lz), (Ly, Lz, Lx), (Lz, Lx, Ly), (Lz, Ly, Lx) };
        foreach (var o in orients)
            best = Math.Max(best, NFit(W, o.a) * NFit(H, o.b) * NFit(D, o.c));
        return best;
    }
}

/// <summary>
/// Fracture-bounded slab builder (Rhino-free port of FractureBoundedSlabsComponent):
/// cut a bench box into the closed inter-bed slabs that FOLLOW the kriged fracture
/// surfaces (height-field stitch), one slab per gap between consecutive beds.
/// </summary>
public static class FractureBoundedSlabs
{
    /// <summary>Port of StitchSlab: closed mesh between top field A[Nx,Ny] and
    /// bottom field B[Nx,Ny] over the common grid (top + bottom + 4 walls). Returns
    /// the triangle list (each {ax..cz}) and the mean thickness. Clamp zt >= zb + 0.03.</summary>
    public static (List<double[]> tris, double meanThickness) StitchSlab(
        double[,] A, double[,] B, double xmin, double ymin, double ex, double ey, int Nx, int Ny)
    {
        var verts = new List<double[]>();
        var ti = new int[Nx, Ny];
        var bi = new int[Nx, Ny];
        double thkSum = 0.0; int thkN = 0;
        for (int i = 0; i < Nx; i++)
            for (int j = 0; j < Ny; j++)
            {
                double gx = xmin + ex * i / (Nx - 1);
                double gy = ymin + ey * j / (Ny - 1);
                double zt = A[i, j];
                double zb = B[i, j];
                if (zt < zb + 0.03) zt = zb + 0.03;   // never invert
                ti[i, j] = verts.Count; verts.Add(new[] { gx, gy, zt });
                bi[i, j] = verts.Count; verts.Add(new[] { gx, gy, zb });
                thkSum += zt - zb; thkN++;
            }
        // quads (i0,i1,i2,i3) -> two triangles, SAME winding order as the port
        var quads = new List<int[]>();
        for (int i = 0; i < Nx - 1; i++)
            for (int j = 0; j < Ny - 1; j++)
            {
                quads.Add(new[] { ti[i, j], ti[i + 1, j], ti[i + 1, j + 1], ti[i, j + 1] });     // top
                quads.Add(new[] { bi[i, j], bi[i, j + 1], bi[i + 1, j + 1], bi[i + 1, j] });     // bottom
            }
        for (int i = 0; i < Nx - 1; i++)
        {
            quads.Add(new[] { ti[i, 0], bi[i, 0], bi[i + 1, 0], ti[i + 1, 0] });                 // y=min wall
            quads.Add(new[] { ti[i, Ny - 1], ti[i + 1, Ny - 1], bi[i + 1, Ny - 1], bi[i, Ny - 1] }); // y=max wall
        }
        for (int j = 0; j < Ny - 1; j++)
        {
            quads.Add(new[] { ti[0, j], ti[0, j + 1], bi[0, j + 1], bi[0, j] });                 // x=min wall
            quads.Add(new[] { ti[Nx - 1, j], bi[Nx - 1, j], bi[Nx - 1, j + 1], ti[Nx - 1, j + 1] }); // x=max wall
        }
        var tris = new List<double[]>(quads.Count * 2);
        foreach (var q in quads)
        {
            var va = verts[q[0]]; var vb = verts[q[1]]; var vc = verts[q[2]]; var vd = verts[q[3]];
            tris.Add(new[] { va[0], va[1], va[2], vb[0], vb[1], vb[2], vc[0], vc[1], vc[2] });
            tris.Add(new[] { va[0], va[1], va[2], vc[0], vc[1], vc[2], vd[0], vd[1], vd[2] });
        }
        return (tris, thkN > 0 ? thkSum / thkN : 0.0);
    }

    /// <summary>
    /// Port of FractureBoundedSlabsComponent.SolveSafe. Cut the bench into the closed
    /// inter-bed slabs that FOLLOW the kriged fracture surfaces. `surfaces` is a list
    /// of single-valued depth surfaces (each a TriMeshInside). Empty/null -> one slab
    /// = the whole bench box (the no-fracture reduction).
    /// </summary>
    public static List<GuillotineSlab> Build(GBox bench, IReadOnlyList<TriMeshInside> surfaces,
        int gridRes = 26, double keepout = 0.0)
    {
        double xmin = bench.MinX, ymin = bench.MinY, zbot = bench.MinZ;
        double xmax = bench.MaxX, ymax = bench.MaxY, ztop = bench.MaxZ;
        double ex = xmax - xmin, ey = ymax - ymin;
        var slabs = new List<GuillotineSlab>();
        if (ex < 1e-6 || ey < 1e-6 || (ztop - zbot) < 1e-6) return slabs;
        gridRes = Math.Max(6, Math.Min(120, gridRes));
        int Nx = ex >= ey ? gridRes : Math.Max(4, (int)Math.Round(gridRes * ex / ey));
        int Ny = ey >= ex ? gridRes : Math.Max(4, (int)Math.Round(gridRes * ey / ex));
        double zmid = 0.5 * (ztop + zbot);

        // sample each bed onto the common grid -> height field + mean depth
        var fields = new List<(double[,] h, double mean)>();
        if (surfaces != null)
            foreach (var surf in surfaces)
            {
                var h = new double[Nx, Ny];
                double tot = 0.0;
                for (int i = 0; i < Nx; i++)
                    for (int j = 0; j < Ny; j++)
                    {
                        double gx = xmin + ex * i / (Nx - 1);
                        double gy = ymin + ey * j / (Ny - 1);
                        double z = surf.SampleZ(gx, gy, zmid);
                        h[i, j] = z; tot += z;
                    }
                fields.Add((h, tot / (Nx * Ny)));
            }
        // sort shallow (z high) -> deep. Stable to match Python's list.sort (stable).
        StableSortByMeanDescending(fields);

        // boundary height fields: bench top, each bed, bench bottom
        var bounds = new List<double[,]>();
        var top = new double[Nx, Ny];
        var flo = new double[Nx, Ny];
        for (int i = 0; i < Nx; i++)
            for (int j = 0; j < Ny; j++) { top[i, j] = ztop; flo[i, j] = zbot; }
        bounds.Add(top);
        foreach (var f in fields)
        {
            var hk = new double[Nx, Ny];
            for (int i = 0; i < Nx; i++)
                for (int j = 0; j < Ny; j++) hk[i, j] = f.h[i, j];
            bounds.Add(hk);
        }
        bounds.Add(flo);

        for (int k = 0; k < bounds.Count - 1; k++)
        {
            var A = (double[,])bounds[k].Clone();
            var B = (double[,])bounds[k + 1].Clone();
            if (k > 0)                       // below an upper bed
                for (int i = 0; i < Nx; i++) for (int j = 0; j < Ny; j++) A[i, j] -= keepout;
            if (k < bounds.Count - 2)        // above a lower bed
                for (int i = 0; i < Nx; i++) for (int j = 0; j < Ny; j++) B[i, j] += keepout;
            var (tris, meanThk) = StitchSlab(A, B, xmin, ymin, ex, ey, Nx, Ny);
            var mesh = new TriMeshInside(tris);
            slabs.Add(new GuillotineSlab(mesh, meanThk, "slab" + k));
        }
        return slabs;
    }

    // Stable sort by mean descending (matches Python fields.sort(key=..., reverse=True),
    // which is stable). List.Sort in .NET is NOT stable, so do an index-tagged sort.
    private static void StableSortByMeanDescending(List<(double[,] h, double mean)> fields)
    {
        int n = fields.Count;
        var idx = new int[n];
        for (int i = 0; i < n; i++) idx[i] = i;
        var snapshot = fields.ToArray();
        Array.Sort(idx, (a, b) =>
        {
            int c = snapshot[b].mean.CompareTo(snapshot[a].mean); // descending
            return c != 0 ? c : a.CompareTo(b);                   // stable tie-break
        });
        for (int i = 0; i < n; i++) fields[i] = snapshot[idx[i]];
    }
}

/// <summary>Top-level fracture-following staged-guillotine driver. Port of
/// pyfrahan pack_guillotine.</summary>
public static class FractureGuillotinePacker
{
    /// <summary>
    /// Build the fracture-bounded slabs then staged-guillotine each slab.
    /// `surfaces` = kriged fracture surfaces (null/empty -> the whole bench).
    /// `prebuiltSlabs` bypasses the slab step (the GH component's actual input).
    /// </summary>
    public static FractureGuillotineResult Pack(GBox bench, IReadOnlyList<TriMeshInside> surfaces,
        double Lx, double Ly, double Lz, double kerf,
        double clearance = 0.0, double keepout = 0.0, int slabGridRes = 26,
        double[] factors = null, IReadOnlyList<TriMeshInside> prebuiltSlabs = null)
    {
        List<GuillotineSlab> slabs;
        if (prebuiltSlabs != null)
        {
            slabs = new List<GuillotineSlab>();
            for (int i = 0; i < prebuiltSlabs.Count; i++)
                slabs.Add(new GuillotineSlab(prebuiltSlabs[i], 0.0, "slab" + i));
        }
        else
        {
            slabs = FractureBoundedSlabs.Build(bench, surfaces, slabGridRes, keepout);
        }

        var res = new FractureGuillotineResult { Kerf = kerf, BlockVolumeM3 = Lx * Ly * Lz };
        for (int si = 0; si < slabs.Count; si++)
        {
            var boxes = StagedGuillotinePacker.PackStagedGuillotine(
                slabs[si].Mesh, slabs[si].Bounds, Lx, Ly, Lz, kerf, clearance, out string desc, factors);
            double a = StagedGuillotinePacker.CutFaceArea(boxes, kerf, out int pl);
            res.CutAreaM2 += a;
            res.CutPlanes += pl;
            res.SeparableFraction.Add(StagedGuillotinePacker.SeparableFraction(boxes, 1e-6));
            for (int b = 0; b < boxes.Count; b++) { res.Blocks.Add(boxes[b]); res.BinIndex.Add(si); }
            res.SlabBlocks.Add(boxes);
            res.SlabDesc.Add(desc);
        }
        res.TestedVolumeM3 = bench.SizeX * bench.SizeY * bench.SizeZ;
        res.RecoveredVolumeM3 = StagedGuillotinePacker.VolumeOf(res.Blocks);
        return res;
    }
}
