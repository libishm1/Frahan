#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Kintsugi;

// =============================================================================
// Frahan > Kintsugi > Fracture Roughen.
//
// Turns the dead-flat planar cut surfaces of Voronoi shatter fragments into
// irregular, worn fracture surfaces -- while keeping adjacent fragments
// MATING so the pieces still fit back together.
//
// THE KEY IDEA (fit-together worn fracture)
//   Displacement comes from ONE shared 3D fractal noise field evaluated at
//   WORLD position:  p' = p + D(p),  where D(p) is the same vector field for
//   every fragment. Two cells that were cut along the same bisector share the
//   same boundary points in world space; since D depends only on position
//   (not on which mesh the vertex belongs to), both move identically -> they
//   still mate. Coherent fractal noise (vs the old per-vertex Gaussian) gives
//   a worn/eroded look instead of spikes.
//
// Why this matters: Voronoi planar cuts are out-of-distribution for the
// Breaking Bad-trained PuzzleFusion++ model. Worn, curved fracture surfaces
// look closer to its training data AND read as real broken stone.
//
// Algorithm
//   1. (optional) Cap each open cut with FillHoles so the fracture is an
//      actual surface, not a hole. The cap + rim vertices are the cut region.
//   2. Displace every cut-region vertex by D(worldPos), a fractal sum of
//      value-noise octaves. Shared field -> adjacent cells stay mated.
//   3. Recompute normals + compact.
//
// Determinism: the Seed fixes the noise field, so re-solves and ALL fragments
// use the identical field (required for matching).
// =============================================================================

[Algorithm("Fracture surface roughen (shared fractal field)",
    "Displaces cut-region vertices by a single world-position fractal noise " +
    "field so adjacent Voronoi fragments stay mated while gaining worn, " +
    "irregular fracture surfaces. Reduces the Voronoi-vs-BreakingBad " +
    "distribution gap for the Kintsugi Port model.")]
[DesignApplication(
    "Give Voronoi shatter fragments worn, irregular fracture surfaces  using a shared world-position fractal fie...",
    DesignFlow.BottomUp,
    Precedent = "Frahan-original Voronoi-shatter post-process")]
public sealed class FractureRoughenComponent : GH_Component
{
    public FractureRoughenComponent()
        : base("Fracture Roughen", "Roughen",
            "Give Voronoi shatter fragments worn, irregular fracture surfaces " +
            "using a shared world-position fractal field, so the pieces still " +
            "fit together. Wire between Frahan Fragment Shatter and Frahan " +
            "Kintsugi.",
            "Frahan", "Kintsugi")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("F2D00504-2026-4522-B0B0-1ABE15A0CAFE");

    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("DiffusionDenoiser.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        // Original 0-4 order preserved (Fragments, Amplitude, Roughness, Seed,
        // Run) so existing canvases don't get their wiring scrambled; the
        // fractal controls are appended at 5-7.
        p.AddMeshParameter("Fragments", "F",
            "List of fragments to roughen (typically from Frahan Fragment Shatter).",
            GH_ParamAccess.list);
        p.AddNumberParameter("Amplitude", "A",
            "Displacement amplitude as a FRACTION of the bounding box diagonal. " +
            "Default 0.02 (2% of bbox). Larger = deeper worn relief.",
            GH_ParamAccess.item, 0.02);
        p.AddNumberParameter("Roughness", "R",
            "Per-octave amplitude falloff (persistence). 0.5 = balanced. " +
            "Higher = rougher/grittier; lower = smoother. Default 0.5.",
            GH_ParamAccess.item, 0.5);
        p.AddIntegerParameter("Seed", "S",
            "RNG seed for the shared noise field. SAME seed = SAME field for " +
            "every fragment (required for mating). Default 42.",
            GH_ParamAccess.item, 42);
        p.AddBooleanParameter("Run", "Run", "Apply.", GH_ParamAccess.item, false);
        p.AddNumberParameter("Frequency", "Fq",
            "Noise frequency as cycles across the bounding box diagonal. " +
            "Lower = broad gentle waves; higher = fine pitting. Default 3.0.",
            GH_ParamAccess.item, 3.0);
        p.AddIntegerParameter("Octaves", "Oc",
            "Fractal octaves summed (each doubles frequency, halves amplitude). " +
            "1 = smooth, 4-5 = rich worn detail. Default 4.",
            GH_ParamAccess.item, 4);
        p.AddBooleanParameter("Cap Cuts", "Cap",
            "TRUE = FillHoles first so the cut becomes a worn SURFACE (closed " +
            "fragment). FALSE = displace only the open rim. Default TRUE.",
            GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Roughened Fragments", "Fo",
            "Fragments with worn, irregular fracture surfaces (still mating).",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Displaced Count", "Dc",
            "Total cut-region vertices displaced (across all fragments).",
            GH_ParamAccess.item);
        p.AddTextParameter("Report", "Rp", "Per-fragment displacement count.",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var inputs = new List<Mesh>();
        double amplitude = 0.02;
        double frequency = 3.0;
        int octaves = 4;
        double roughness = 0.5;
        bool capCuts = true;
        int seed = 42;
        bool run = false;
        if (!da.GetDataList(0, inputs)) return;
        da.GetData(1, ref amplitude);
        da.GetData(2, ref roughness);
        da.GetData(3, ref seed);
        da.GetData(4, ref run);
        da.GetData(5, ref frequency);
        da.GetData(6, ref octaves);
        da.GetData(7, ref capCuts);
        if (!run)
        {
            da.SetData(2, "Run is false. Toggle to apply.");
            return;
        }
        if (octaves < 1) octaves = 1;
        if (octaves > 8) octaves = 8;

        // ONE shared noise field for ALL fragments. The three channels give a
        // 3D displacement vector; large offsets decorrelate the channels.
        var field = new FractalField(seed, Math.Max(1, octaves), roughness);

        // Frequency is expressed per bbox-diagonal; convert to world units
        // using the COMBINED bounding box of all fragments so every fragment
        // samples the same field at the same world frequency.
        var union = BoundingBox.Empty;
        foreach (var m in inputs) if (m != null) union.Union(m.GetBoundingBox(true));
        double diag = union.IsValid ? union.Diagonal.Length : 1.0;
        if (diag < 1e-9) diag = 1.0;
        double freqScale = frequency / diag;     // cycles per world unit
        double ampWorld = amplitude * diag;      // displacement in world units

        var outputs = new List<Mesh>(inputs.Count);
        int totalDisplaced = 0;
        var report = new System.Text.StringBuilder();

        for (int f = 0; f < inputs.Count; f++)
        {
            var src = inputs[f];
            if (src == null) { outputs.Add(null); continue; }
            var m = src.DuplicateMesh();

            // Mark the cut region BEFORE capping (the naked-edge vertices are
            // the open-cut boundary). After capping, the new fill faces extend
            // the cut surface; their vertices are also cut-region.
            int preVerts = m.Vertices.Count;
            var cut = new bool[preVerts];
            MarkNakedEdgeVertices(m, cut);

            if (capCuts)
            {
                try { m.FillHoles(); } catch { }
                // Vertices added by FillHoles (index >= preVerts) sit on the
                // cut plane -> treat as cut-region too.
                if (m.Vertices.Count > preVerts)
                {
                    var grown = new bool[m.Vertices.Count];
                    Array.Copy(cut, grown, preVerts);
                    for (int v = preVerts; v < m.Vertices.Count; v++) grown[v] = true;
                    cut = grown;
                }
            }

            // Displace cut-region vertices by the SHARED world-position field.
            int displaced = 0;
            for (int v = 0; v < m.Vertices.Count; v++)
            {
                if (v >= cut.Length || !cut[v]) continue;
                var p = m.Vertices[v];
                double wx = p.X * freqScale, wy = p.Y * freqScale, wz = p.Z * freqScale;
                field.Sample(wx, wy, wz, out double dx, out double dy, out double dz);
                m.Vertices.SetVertex(v, new Point3f(
                    (float)(p.X + dx * ampWorld),
                    (float)(p.Y + dy * ampWorld),
                    (float)(p.Z + dz * ampWorld)));
                displaced++;
            }

            m.Normals.ComputeNormals();
            m.Compact();
            outputs.Add(m);
            totalDisplaced += displaced;
            report.AppendLine($"Fragment {f}: displaced {displaced} cut-region vertices" +
                              (capCuts ? " (capped)." : " (open rim)."));
        }
        report.AppendLine();
        report.AppendLine($"Total displaced: {totalDisplaced}.");
        report.AppendLine($"Shared fractal field: seed={seed}, octaves={octaves}, " +
                          $"freq={frequency:G3}/diag, amp={amplitude:G3}*diag.");
        report.AppendLine("Field is world-position based, so adjacent fragments stay mated.");
        da.SetDataList(0, outputs);
        da.SetData(1, totalDisplaced);
        da.SetData(2, report.ToString());
    }

    private static void MarkNakedEdgeVertices(Mesh m, bool[] naked)
    {
        var topEdges = m.TopologyEdges;
        var topVerts = m.TopologyVertices;
        for (int e = 0; e < topEdges.Count; e++)
        {
            if (topEdges.GetConnectedFaces(e).Length != 1) continue;
            var pair = topEdges.GetTopologyVertices(e);
            foreach (var meshV in topVerts.MeshVertexIndices(pair.I))
                if (meshV < naked.Length) naked[meshV] = true;
            foreach (var meshV in topVerts.MeshVertexIndices(pair.J))
                if (meshV < naked.Length) naked[meshV] = true;
        }
    }

    // -------------------------------------------------------------------------
    // Deterministic 3D fractal value-noise. Seeded hash -> lattice gradients,
    // trilinear interpolation, summed over octaves. Three decorrelated channels
    // (via large coordinate offsets) form the displacement vector. Pure
    // function of position: the same (x,y,z) ALWAYS yields the same vector,
    // which is what keeps adjacent fragments mating.
    // -------------------------------------------------------------------------
    private sealed class FractalField
    {
        private readonly int _seed;
        private readonly int _octaves;
        private readonly double _persistence;

        public FractalField(int seed, int octaves, double persistence)
        {
            _seed = seed;
            _octaves = octaves;
            _persistence = (persistence > 0 && persistence <= 1) ? persistence : 0.5;
        }

        public void Sample(double x, double y, double z,
                           out double dx, out double dy, out double dz)
        {
            dx = Fbm(x, y, z, 0);
            dy = Fbm(x + 137.13, y + 71.7, z + 19.3, 1);
            dz = Fbm(x - 53.7, y - 113.1, z + 211.9, 2);
        }

        private double Fbm(double x, double y, double z, int channel)
        {
            double sum = 0, amp = 1.0, freq = 1.0, norm = 0;
            for (int o = 0; o < _octaves; o++)
            {
                sum += amp * ValueNoise(x * freq, y * freq, z * freq, channel + o * 31);
                norm += amp;
                amp *= _persistence;
                freq *= 2.0;
            }
            return norm > 0 ? sum / norm : 0.0; // in [-1, 1]
        }

        private double ValueNoise(double x, double y, double z, int salt)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y), zi = (int)Math.Floor(z);
            double xf = x - xi, yf = y - yi, zf = z - zi;
            double u = Fade(xf), v = Fade(yf), w = Fade(zf);
            double c000 = Lattice(xi, yi, zi, salt);
            double c100 = Lattice(xi + 1, yi, zi, salt);
            double c010 = Lattice(xi, yi + 1, zi, salt);
            double c110 = Lattice(xi + 1, yi + 1, zi, salt);
            double c001 = Lattice(xi, yi, zi + 1, salt);
            double c101 = Lattice(xi + 1, yi, zi + 1, salt);
            double c011 = Lattice(xi, yi + 1, zi + 1, salt);
            double c111 = Lattice(xi + 1, yi + 1, zi + 1, salt);
            double x00 = Lerp(c000, c100, u), x10 = Lerp(c010, c110, u);
            double x01 = Lerp(c001, c101, u), x11 = Lerp(c011, c111, u);
            double y0 = Lerp(x00, x10, v), y1 = Lerp(x01, x11, v);
            return Lerp(y0, y1, w); // [-1, 1]
        }

        private double Lattice(int x, int y, int z, int salt)
        {
            unchecked
            {
                uint h = (uint)(_seed * 374761393 + salt * 668265263);
                h ^= (uint)(x * 0x8da6b343);
                h ^= (uint)(y * 0xd8163841);
                h ^= (uint)(z * 0xcb1ab31f);
                h = (h ^ (h >> 15)) * 0x2c1b3c6d;
                h = (h ^ (h >> 12)) * 0x297a2d39;
                h ^= h >> 15;
                return (h / (double)uint.MaxValue) * 2.0 - 1.0; // [-1, 1]
            }
        }

        private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }
}
