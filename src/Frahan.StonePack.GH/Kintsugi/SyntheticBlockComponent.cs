#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Kintsugi;

// =============================================================================
// Frahan > Kintsugi > Synthetic Block (training-target generator).
//
// Generates parametric stone-block shapes to serve as TARGETS for synthetic
// fracture-assembly training (PotNet-stone / Kintsugi round-trip). Wire the
// output straight into Frahan Fragment Shatter -> Fracture Roughen -> Kintsugi,
// or bake to .3dm and feed the Python exporter
// (outputs/.../potnet_stone_per_object/export_per_object_dataset.py).
//
// WHY a palette of shapes (the 2026-05-24 training finding)
//   Per-object training on a FEATURELESS CONVEX block overfits: the outer
//   surface patches are ambiguous, so the network memorises training fragments
//   but cannot generalise (test error ~ chance). Accuracy comes from
//   DISTINCTIVE outer-surface geometry. This component spans that axis on
//   purpose -- from ambiguous (Boulder, Slab) to highly distinctive (Fluted
//   Drum, Bossed, Ridged, Sculpted) -- so you can pick targets that actually
//   train well and build varied synthetic datasets.
//
// Every shape is a CLEAN CLOSED mesh built by direct vertex/face construction
// or single-primitive vertex displacement. No mesh booleans (flaky in net48
// headless) and no self-intersecting appends -> the surface is sound for both
// shattering and surface sampling.
//
// Determinism: Seed fixes all randomness; same inputs -> same blocks.
// =============================================================================

[Algorithm("Synthetic stone-block target generator",
    "Builds parametric closed block meshes (10 shapes spanning featureless to " +
    "distinctive surfaces) as targets for synthetic fracture-assembly training. " +
    "Feeds Frahan Fragment Shatter / the PotNet-stone exporter.",
    Note = "For per-object 6-DoF training, detail must be ASYMMETRIC + non-repeating: " +
           "Bossed (6) and Sculpted (8) are best. Periodic/symmetric shapes (Fluted Drum 4, " +
           "Ridged 7, Stepped 9) and featureless ones (Boulder 0, Slab 1, Gem 5) stay " +
           "ambiguous (measured: drum did not beat the featureless baseline). All shapes " +
           "are clean closed meshes (no booleans).")]
[RelatedComponent("Frahan > Kintsugi > Frahan Fragment Shatter",
    Reason = "Shatter the generated block into fragments for training / round-trip")]
[RelatedComponent("Frahan > Kintsugi > Fracture Roughen",
    Reason = "Give the shards worn fracture surfaces before assembly")]
[RelatedComponent("Frahan > Kintsugi > Frahan Kintsugi",
    Reason = "Reassemble the shards (round-trip test of the target)")]
[DesignApplication(
    "Generate parametric closed stone-block meshes (10 shapes) as  targets for synthetic fracture-assembly training",
    DesignFlow.Bridges,
    Precedent = "Frahan-original parametric block generator (10 shapes)")]
public sealed class SyntheticBlockComponent : GH_Component
{
    private static readonly string[] ShapeNames =
    {
        "Irregular Boulder",   // 0  convex-ish, low feature  (ambiguous baseline)
        "Slab",                // 1  thin plate               (ambiguous)
        "Tapered Wedge",       // 2  battered block
        "Pyramidal Frustum",   // 3  truncated pyramid
        "Fluted Drum",         // 4  column drum w/ flutes     (distinctive)
        "Faceted Gem",         // 5  coarse-faceted convex
        "Bossed Block",        // 6  raised bosses on top      (distinctive)
        "Ridged Block",        // 7  tooled parallel ridges    (distinctive)
        "Sculpted Relief",     // 8  fractal-displaced top     (distinctive)
        "Stepped Block",       // 9  terraced / ziggurat top   (distinctive)
    };

    public SyntheticBlockComponent()
        : base("Synthetic Block", "SynBlock",
            "Generate parametric closed stone-block meshes (10 shapes) as " +
            "targets for synthetic fracture-assembly training. Wire into " +
            "Frahan Fragment Shatter or bake to .3dm for the PotNet-stone " +
            "exporter. Distinctive shapes train better than featureless ones.",
            "Frahan", "Kintsugi")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("F2D00506-2026-4522-B0B0-1ABE15A0CAFE");

    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("SyntheticBlock.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddIntegerParameter("Shape", "S",
            "Block shape index 0..9: 0 Irregular Boulder, 1 Slab, 2 Tapered " +
            "Wedge, 3 Pyramidal Frustum, 4 Fluted Drum, 5 Faceted Gem, " +
            "6 Bossed Block, 7 Ridged Block, 8 Sculpted Relief, 9 Stepped " +
            "Block. For per-object 6-DoF training prefer 6/8 (asymmetric " +
            "features); 4/7/9 are periodic/symmetric and 0/1/5 featureless -> " +
            "ambiguous. All are fine for shatter / round-trip testing.",
            GH_ParamAccess.item, 8);
        p.AddNumberParameter("Size", "Sz",
            "Longest block dimension in model units. Default 100.",
            GH_ParamAccess.item, 100.0);
        p.AddIntegerParameter("Seed", "Sd",
            "Deterministic seed (jitter / sculpt / boss placement). Default 42.",
            GH_ParamAccess.item, 42);
        p.AddNumberParameter("Feature", "Fa",
            "Feature strength 0..1: flute depth, boss height, ridge depth, " +
            "sculpt amplitude, taper, step height. 0 = near-flat, 1 = bold. " +
            "Default 0.5.",
            GH_ParamAccess.item, 0.5);
        p.AddIntegerParameter("Resolution", "Rs",
            "Tessellation density for featured shapes (grid / around count). " +
            "Higher = finer surface detail, more faces. Default 24.",
            GH_ParamAccess.item, 24);
        p.AddIntegerParameter("Variations", "V",
            "Number of seed-varied copies, laid out along +X (1.6*Size apart). " +
            "Use to compare shapes by eye or build a multi-object set. Default 1.",
            GH_ParamAccess.item, 1);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Blocks", "B",
            "Closed block meshes. Wire into Frahan Fragment Shatter or bake " +
            "to .3dm for the PotNet-stone exporter.",
            GH_ParamAccess.list);
        p.AddTextParameter("Shape Name", "Sn",
            "Name of the selected shape.", GH_ParamAccess.item);
        p.AddTextParameter("Report", "Rp",
            "Per-block vertex / face / closed / volume diagnostics.",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        int shape = 4;
        double size = 100.0;
        int seed = 42;
        double feature = 0.5;
        int res = 24;
        int variations = 1;
        da.GetData(0, ref shape);
        da.GetData(1, ref size);
        da.GetData(2, ref seed);
        da.GetData(3, ref feature);
        da.GetData(4, ref res);
        da.GetData(5, ref variations);

        if (shape < 0 || shape >= ShapeNames.Length)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Shape must be 0..{ShapeNames.Length - 1} (got {shape}).");
            return;
        }
        if (size <= 0) { size = 100.0; }
        if (feature < 0) feature = 0;
        if (feature > 1) feature = 1;
        if (res < 4) res = 4;
        if (res > 200) res = 200;
        if (variations < 1) variations = 1;
        if (variations > 50) variations = 50;

        var blocks = new List<Mesh>(variations);
        var report = new System.Text.StringBuilder();
        report.AppendLine($"Shape {shape} = {ShapeNames[shape]}, size={size:G4}, " +
                          $"feature={feature:G3}, res={res}, variations={variations}.");

        for (int v = 0; v < variations; v++)
        {
            var m = BuildShape(shape, size, seed + v, feature, res);
            if (m == null) { report.AppendLine($"Variation {v}: build failed."); continue; }
            if (variations > 1)
            {
                var xf = Transform.Translation(v * size * 1.6, 0, 0);
                m.Transform(xf);
            }
            m.Normals.ComputeNormals();
            m.Compact();

            bool closed = m.IsClosed;
            double vol = 0;
            try { if (closed) vol = m.Volume(); } catch { vol = 0; }
            blocks.Add(m);
            report.AppendLine(
                $"Var {v} (seed {seed + v}): V={m.Vertices.Count}, F={m.Faces.Count}, " +
                $"closed={closed}, vol={vol:G4}");
        }

        if (blocks.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No blocks built.");
        }
        da.SetDataList(0, blocks);
        da.SetData(1, ShapeNames[shape]);
        da.SetData(2, report.ToString());
    }

    // =========================================================================
    // Shape dispatch.
    // =========================================================================
    private static Mesh BuildShape(int shape, double size, int seed, double feature, int res)
    {
        var rng = new Random(seed);
        switch (shape)
        {
            case 0: return Boulder(size, rng, feature, Math.Max(8, res / 2));
            case 1: return CornerBox(size, size, size * 0.18, 1.0, 0, 0);          // Slab
            case 2: return CornerBox(size, size * 0.7, size * 0.7,                  // Tapered Wedge
                                     1.0 - 0.45 * feature, 0.25 * feature, 0);
            case 3: return CornerBox(size, size, size,                              // Pyramidal Frustum
                                     0.25 + 0.5 * (1 - feature), 0, 0);
            case 4: return FlutedDrum(size, feature, res, Math.Max(6, res / 3));    // Fluted Drum
            case 5: return Mesh.CreateFromSphere(                                   // Faceted Gem
                                     new Sphere(Point3d.Origin, size * 0.5),
                                     Math.Max(5, res / 4), Math.Max(4, res / 5));
            case 6: return HeightfieldBox(size, size, size * 0.5, res,              // Bossed Block
                        (u, w) => BossHeight(u, w, feature, seed));
            case 7: return HeightfieldBox(size, size, size * 0.5, res,              // Ridged Block
                        (u, w) => RidgeHeight(u, feature));
            case 8: return HeightfieldBox(size, size, size * 0.5, res,              // Sculpted Relief
                        (u, w) => SculptHeight(u, w, feature, seed));
            case 9: return HeightfieldBox(size, size, size * 0.5, res,              // Stepped Block
                        (u, w) => StepHeight(u, w, feature));
            default: return CornerBox(size, size, size, 1.0, 0, 0);
        }
    }

    // -------------------------------------------------------------------------
    // Irregular boulder: a sphere with each vertex pushed radially by smooth
    // 3D value-noise. Convex-ish, organic, low distinctive detail (baseline).
    // -------------------------------------------------------------------------
    private static Mesh Boulder(double size, Random rng, double feature, int res)
    {
        var m = Mesh.CreateFromSphere(new Sphere(Point3d.Origin, size * 0.5),
                                      Math.Max(12, res), Math.Max(8, res * 2 / 3));
        if (m == null) return null;
        int salt = rng.Next();
        double amp = 0.10 + 0.30 * feature;          // 10%..40% radial wobble
        for (int i = 0; i < m.Vertices.Count; i++)
        {
            var p = m.Vertices[i];
            var d = new Vector3d(p.X, p.Y, p.Z);
            double len = d.Length;
            if (len < 1e-9) continue;
            d /= len;
            double n = Fbm3(d.X * 1.7 + salt, d.Y * 1.7, d.Z * 1.7, 4); // [-1,1]
            double r = len * (1.0 + amp * n);
            m.Vertices.SetVertex(i, (float)(d.X * r), (float)(d.Y * r), (float)(d.Z * r));
        }
        return m;
    }

    // -------------------------------------------------------------------------
    // Corner box: bottom rectangle (full sizeX x sizeY at z=0), top rectangle
    // at z=height scaled by topScale about centre and shifted by skew*sizeX in
    // X. Covers Slab, Tapered Wedge, Pyramidal Frustum. 8 corners, 6 quads.
    // -------------------------------------------------------------------------
    private static Mesh CornerBox(double sx, double sy, double height,
                                  double topScale, double skew, double unused)
    {
        double hx = sx * 0.5, hy = sy * 0.5;
        double tx = hx * topScale, ty = hy * topScale;
        double sh = skew * sx;
        var m = new Mesh();
        // bottom 0..3 (CCW from -,-)
        m.Vertices.Add(-hx, -hy, 0);
        m.Vertices.Add(hx, -hy, 0);
        m.Vertices.Add(hx, hy, 0);
        m.Vertices.Add(-hx, hy, 0);
        // top 4..7
        m.Vertices.Add(-tx + sh, -ty, height);
        m.Vertices.Add(tx + sh, -ty, height);
        m.Vertices.Add(tx + sh, ty, height);
        m.Vertices.Add(-tx + sh, ty, height);
        m.Faces.AddFace(0, 3, 2, 1); // bottom (down)
        m.Faces.AddFace(4, 5, 6, 7); // top (up)
        m.Faces.AddFace(0, 1, 5, 4); // -Y
        m.Faces.AddFace(1, 2, 6, 5); // +X
        m.Faces.AddFace(2, 3, 7, 6); // +Y
        m.Faces.AddFace(3, 0, 4, 7); // -X
        return m;
    }

    // -------------------------------------------------------------------------
    // Fluted drum: a vertical cylinder whose around-radius is modulated by
    // cos(flutes*theta) to carve vertical flutes (a classical column drum).
    // Closed: side quads + top/bottom fans to centre vertices.
    // -------------------------------------------------------------------------
    private static Mesh FlutedDrum(double size, double feature, int around, int flutes)
    {
        around = Math.Max(24, around);
        flutes = Math.Max(6, flutes);
        double R = size * 0.5;
        double H = size * 0.9;
        double depth = (0.04 + 0.16 * feature);      // 4%..20% radius flute depth
        var m = new Mesh();
        // ring vertices: bottom 0..around-1, top around..2*around-1
        for (int ring = 0; ring < 2; ring++)
        {
            double z = ring == 0 ? 0 : H;
            for (int i = 0; i < around; i++)
            {
                double th = 2.0 * Math.PI * i / around;
                double r = R * (1.0 - depth * 0.5 * (1.0 + Math.Cos(flutes * th)));
                m.Vertices.Add(r * Math.Cos(th), r * Math.Sin(th), z);
            }
        }
        int bc = m.Vertices.Add(0, 0, 0);            // bottom centre
        int tc = m.Vertices.Add(0, 0, H);            // top centre
        for (int i = 0; i < around; i++)
        {
            int j = (i + 1) % around;
            int b0 = i, b1 = j, t0 = around + i, t1 = around + j;
            m.Faces.AddFace(b0, b1, t1, t0);          // side
            m.Faces.AddFace(bc, b1, b0);              // bottom fan (down)
            m.Faces.AddFace(tc, t0, t1);              // top fan (up)
        }
        return m;
    }

    // -------------------------------------------------------------------------
    // Heightfield box: a (res x res) grid top surface at z = baseH + topZ(u,w)
    // over [-1,1]^2, a flat grid bottom at z=0, and four stitched side walls.
    // One builder serves Bossed / Ridged / Sculpted / Stepped via topZ.
    // u,w are normalised face coords in [0,1].
    // -------------------------------------------------------------------------
    private static Mesh HeightfieldBox(double sx, double sy, double baseH, int res,
                                       Func<double, double, double> topZ)
    {
        res = Math.Max(4, res);
        int n = res + 1;
        double hx = sx * 0.5, hy = sy * 0.5;
        var m = new Mesh();
        // top grid 0 .. n*n-1
        for (int iy = 0; iy < n; iy++)
            for (int ix = 0; ix < n; ix++)
            {
                double u = (double)ix / res, w = (double)iy / res;
                double x = -hx + u * sx, y = -hy + w * sy;
                double z = baseH + topZ(u, w) * baseH;     // relief scaled by baseH
                m.Vertices.Add(x, y, z);
            }
        int botBase = m.Vertices.Count;
        // bottom grid (z=0) same xy
        for (int iy = 0; iy < n; iy++)
            for (int ix = 0; ix < n; ix++)
            {
                double u = (double)ix / res, w = (double)iy / res;
                m.Vertices.Add(-hx + u * sx, -hy + w * sy, 0);
            }
        Func<int, int, int> T = (ix, iy) => iy * n + ix;
        Func<int, int, int> B = (ix, iy) => botBase + iy * n + ix;
        // top faces (up)
        for (int iy = 0; iy < res; iy++)
            for (int ix = 0; ix < res; ix++)
                m.Faces.AddFace(T(ix, iy), T(ix + 1, iy), T(ix + 1, iy + 1), T(ix, iy + 1));
        // bottom faces (down)
        for (int iy = 0; iy < res; iy++)
            for (int ix = 0; ix < res; ix++)
                m.Faces.AddFace(B(ix, iy), B(ix, iy + 1), B(ix + 1, iy + 1), B(ix + 1, iy));
        // walls: stitch top boundary to bottom boundary along all 4 edges
        for (int ix = 0; ix < res; ix++) // y=0 edge (-Y)
            m.Faces.AddFace(T(ix, 0), B(ix, 0), B(ix + 1, 0), T(ix + 1, 0));
        for (int ix = 0; ix < res; ix++) // y=res edge (+Y)
            m.Faces.AddFace(T(ix + 1, res), B(ix + 1, res), B(ix, res), T(ix, res));
        for (int iy = 0; iy < res; iy++) // x=0 edge (-X)
            m.Faces.AddFace(T(0, iy + 1), B(0, iy + 1), B(0, iy), T(0, iy));
        for (int iy = 0; iy < res; iy++) // x=res edge (+X)
            m.Faces.AddFace(T(res, iy), B(res, iy), B(res, iy + 1), T(res, iy + 1));
        try { m.Vertices.CombineIdentical(true, true); } catch { }
        return m;
    }

    // ---- height functions (return relief in [-1,1], scaled by baseH) --------

    private static double BossHeight(double u, double w, double feature, int seed)
    {
        var rng = new Random(seed * 31 + 7);
        int bosses = 3 + (int)(4 * feature);
        double h = -0.15;                              // slightly recessed field
        for (int b = 0; b < bosses; b++)
        {
            double cu = 0.15 + 0.7 * rng.NextDouble();
            double cw = 0.15 + 0.7 * rng.NextDouble();
            double rad = 0.08 + 0.10 * rng.NextDouble();
            double du = u - cu, dw = w - cw;
            double d2 = (du * du + dw * dw) / (rad * rad);
            if (d2 < 1.0) h += (0.6 + 0.6 * feature) * Math.Cos(Math.Sqrt(d2) * Math.PI * 0.5);
        }
        return Clamp(h, -1, 1);
    }

    private static double RidgeHeight(double u, double feature)
    {
        int ridges = 5;
        return (0.2 + 0.7 * feature) * Math.Sin(u * Math.PI * 2 * ridges) * 0.5;
    }

    private static double SculptHeight(double u, double w, double feature, int seed)
    {
        double n = Fbm3(u * 3.0 + seed * 0.13, w * 3.0, 0.5, 5); // [-1,1]
        return (0.3 + 0.7 * feature) * n;
    }

    private static double StepHeight(double u, double w, double feature)
    {
        // terraced ziggurat: distance to centre -> discrete steps
        double du = u - 0.5, dw = w - 0.5;
        double r = Math.Max(Math.Abs(du), Math.Abs(dw)) * 2.0; // 0 centre .. 1 edge
        int steps = 4;
        double level = Math.Floor((1.0 - r) * steps) / steps;  // 0..1
        return (0.2 + 0.8 * feature) * (level - 0.5);
    }

    private static double Clamp(double v, double lo, double hi)
        => v < lo ? lo : (v > hi ? hi : v);

    // -------------------------------------------------------------------------
    // Deterministic 3D fractal value-noise (self-contained; same idea as
    // FractureRoughenComponent.FractalField). Returns [-1,1].
    // -------------------------------------------------------------------------
    private static double Fbm3(double x, double y, double z, int octaves)
    {
        double sum = 0, amp = 1, freq = 1, norm = 0;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * ValueNoise3(x * freq, y * freq, z * freq, o * 31 + 1);
            norm += amp; amp *= 0.5; freq *= 2.0;
        }
        return norm > 0 ? sum / norm : 0;
    }

    private static double ValueNoise3(double x, double y, double z, int salt)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y), zi = (int)Math.Floor(z);
        double xf = x - xi, yf = y - yi, zf = z - zi;
        double u = Fade(xf), v = Fade(yf), w = Fade(zf);
        double c000 = Lat(xi, yi, zi, salt), c100 = Lat(xi + 1, yi, zi, salt);
        double c010 = Lat(xi, yi + 1, zi, salt), c110 = Lat(xi + 1, yi + 1, zi, salt);
        double c001 = Lat(xi, yi, zi + 1, salt), c101 = Lat(xi + 1, yi, zi + 1, salt);
        double c011 = Lat(xi, yi + 1, zi + 1, salt), c111 = Lat(xi + 1, yi + 1, zi + 1, salt);
        double x00 = Lerp(c000, c100, u), x10 = Lerp(c010, c110, u);
        double x01 = Lerp(c001, c101, u), x11 = Lerp(c011, c111, u);
        return Lerp(Lerp(x00, x10, v), Lerp(x01, x11, v), w);
    }

    private static double Lat(int x, int y, int z, int salt)
    {
        unchecked
        {
            uint h = (uint)(salt * 374761393 + 668265263);
            h ^= (uint)(x * 0x8da6b343); h ^= (uint)(y * 0xd8163841); h ^= (uint)(z * 0xcb1ab31f);
            h = (h ^ (h >> 15)) * 0x2c1b3c6d; h = (h ^ (h >> 12)) * 0x297a2d39; h ^= h >> 15;
            return (h / (double)uint.MaxValue) * 2.0 - 1.0;
        }
    }

    private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
