#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using Frahan.GH.Attributes;
using Frahan.GH.ScanIngest;
using Frahan.Masonry.Quarry.Processing;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH;

// =============================================================================
// IsoFractureField -- the CANVAS front for the accepted implicit (level-set)
// kriging core (KrigingField3D -> MarchingCubes.Extract -> Rhino Mesh), with
// delta-method sigma_pos vertex colouring.
//
// Where GprFractureSurface3DComponent kriges a HEIGHT d(x,y) per bed (a graph,
// single-valued, dip-limited), this component kriges the 3D SCALAR field
// F(x,y,z) whose ZERO level-set IS the fracture surface (Lajaunie 1997; Carr
// 2001; Calcagno 2008). F=0 is dip-agnostic: a vertical dyke is as cheap as a
// horizontal bed, and overhangs / stacked reflectors are representable because
// the zero-set is not a graph over (x,y). Per pick i with polarity normal n_i:
//   F = 0 at the pick, F = +-h at pick +- h*n_i (Carr manufactured off-surface
//   polarity; h = h_frac * median pick spacing). Simple kriging of F; the
//   isosurface is extracted by the clean-room MarchingCubes (RhinoCommon ships
//   none). sigma_pos = sigma_F / max(|grad F|, floor), floor = 0.2*median|grad F|
//   (delta method; G13 v3 result), trilinear-sampled at the mesh verts and
//   colour-mapped (green = confident -> red = uncertain).
//
// Heavy (a dense lattice x per-query kriging solve) -> runs on the shared
// AsyncScanComponent background pattern (Run gate; canvas never freezes). No
// live-Rhino geometry touches the background thread: the Task returns plain
// arrays, the Mesh is assembled in EmitResult on the UI thread.
//
// HONESTY: picks from ONE GPR line are collinear in plan; the F=0 output is then
// a SECTION extruded along the unsampled direction, not a measured 3D surface
// (a Warning is emitted). Deep reflectors imaged as one sheet may be MULTIPLE
// sub-parallel structures (G8/G13 declared-width rule); use Layered for the
// stacked-reflector polarity split. ShrinkWrap is OFF by default and is only a
// mesh post-clean -- it is NOT the isosurfacer (G11 note).
// =============================================================================

[Algorithm("Implicit potential-field kriging (level-set)",
    "Lajaunie et al. 1997 (Math. Geol. 29:571); Carr et al. 2001 (RBF implicit surfaces, SIGGRAPH); Calcagno et al. 2008",
    Note = "Krige F(x,y,z); F=0 is the surface. +-h off-surface polarity (Carr 2001). Port of pyfrahan/krige3d (G13 v3).",
    WikiPath = "outputs/2026-07-15/krishnagiri_survey/research/PORT_csharp_implicit_kriging.md")]
[Algorithm("Isosurface extraction (marching cubes)",
    "Lorensen & Cline 1987, ACM SIGGRAPH Comput. Graph. 21(4):163 (METHOD only)",
    Note = "Clean-room MarchingCubes.cs; no Lorensen-Cline / Bourke lookup table reproduced. RhinoCommon ships no MC.")]
[Algorithm("Delta-method position uncertainty",
    "sigma_pos = sigma_F / |grad F| (floor-guarded); G13 v3 result",
    Note = "Grid central-difference |grad F|; trilinear-sampled at the isosurface verts; floor 0.2*median|grad F|.")]
[Algorithm("Gradient cokriging polarity (route i mode)",
    "Lajaunie, Courrioux & Manuel 1997 (Math. Geol. 29:571); Chiles & Delfiner 2012 sec. 2; Calcagno et al. 2008 (GemPy lineage)",
    Note = "Polarity Source = 1: increments F(x_i)-F(x_ref)=0 + gradient data grad F = n_i (no manufactured " +
           "+-h shells); analytic grad F for sigma_pos. Port of pyfrahan/krige3d_grad (G17). Needs per-pick " +
           "normals to beat the signed mode; rejects the exponential covariance.")]
[RelatedComponent("Frahan > Quarry > GPR Fracture Surfaces 3D",
    Reason = "Graph-surface twin: kriges a height d(x,y) per bed + the tolerance ladder. Prefer it for sub-horizontal, single-valued beds; use IsoFractureField for dipping / vertical / overhanging / stacked surfaces.",
    ComponentGuid = "A7E0B0F2-0C0F-4A16-9E3D-0FACE0FACE03")]
[RelatedComponent("Frahan > Quarry > Discontinuity Sets (Async)",
    Reason = "Its per-set poles supply the polarity normal(s) (family normal / dyke trend) this field needs.",
    ComponentGuid = "D5F10048-ED9E-4ED9-A048-ED9EED9E0048")]
[RelatedComponent("Frahan > Block > Fracture Block Pack",
    Reason = "Consumes fracture surfaces as cut constraints for in-situ block yield.",
    ComponentGuid = "A7E0B0F3-0C0F-4A16-9E3D-0FACE0FACE04")]
public sealed class IsoFractureFieldComponent
    : AsyncScanComponent<IsoFractureFieldComponent.Snapshot, IsoFractureFieldComponent.Payload>
{
    public IsoFractureFieldComponent()
        : base("Iso Fracture Field", "IsoFrac3D",
            "Implicit (level-set) kriging of a 3D fracture SURFACE from picks. Kriges the scalar field " +
            "F(x,y,z) whose zero level-set is the surface (dip-agnostic: vertical / overhanging / stacked " +
            "surfaces are representable, unlike a height field). Polarity Source picks the polarity engine: " +
            "0 = signed +-h off-surface shells (Carr 2001, route ii, default) | 1 = gradient cokriging " +
            "(Lajaunie 1997, route i: normals as honest gradient data, analytic sigma_pos). Clean-room " +
            "marching cubes and sigma_pos = sigma_F/|grad F| vertex colouring (green confident -> red " +
            "uncertain). Async (Run gate). Lajaunie 1997 / Carr 2001; MC Lorensen-Cline 1987 (method).",
            "Frahan", "Quarry")
    {
    }

    public override Guid ComponentGuid => new Guid("9C2AA7E5-B40B-41BC-9544-CF87A2AF95BC");
    protected override Bitmap Icon => IconProvider.Load("Stratigraphy.png");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    private static readonly string[] Families = { "gaussian", "exponential", "matern15", "matern25" };

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddPointParameter("Picks", "P",
            "Fracture / reflector picks in world coordinates (x, y, z). Combine several GPR section lines " +
            "for a true 3D surface; >= 3 required. Units per the doc tolerance conventions (mm or m).",
            GH_ParamAccess.list);
        p.AddVectorParameter("Normal", "N",
            "Polarity normal(s): the family normal or dyke trend along which the +-h off-surface points are " +
            "placed (Carr 2001). Wire ONE vector (broadcast to all picks) or one PER pick. Empty -> +Z " +
            "(horizontal bed) with a warning.",
            GH_ParamAccess.list);
        p[1].Optional = true;
        p.AddNumberParameter("Point Sigma", "Sg",
            "OPTIONAL per-pick sigma (same units as the picks), aligned to Picks. Enters as point_nugget " +
            "(variance sigma_i^2) on the F=0 Gram diagonal -> looser picks pull the surface less and read " +
            "as higher sigma_pos. Omit for a uniform off-surface nugget.",
            GH_ParamAccess.list);
        p[2].Optional = true;
        p.AddIntegerParameter("Covariance", "Cov",
            "Covariance family: 0 = gaussian (smooth, default), 1 = exponential, 2 = matern15, 3 = matern25.",
            GH_ParamAccess.item, 0);
        p.AddNumberParameter("Range", "Rg",
            "Covariance range (world units). <= 0 = auto-fit by NLL over a (0.05..0.9)*extent grid " +
            "(median-NN spacing / var(ddof=1) reproduced from the port).",
            GH_ParamAccess.item, -1.0);
        p.AddNumberParameter("h Frac", "h",
            "Off-surface polarity offset as a fraction of the median pick spacing (Carr +-h). Default 1.5. " +
            "Larger = a stiffer, smoother field; smaller = tighter to the picks.",
            GH_ParamAccess.item, 1.5);
        p.AddIntegerParameter("Lattice Res", "L",
            "Lattice divisions along the LONGEST axis; the other axes are proportional (min 12 nodes). " +
            "Default 40; clamped [12, 80]. Cost grows ~ nodes x picks^2 -> stays async.",
            GH_ParamAccess.item, 40);
        p.AddNumberParameter("Padding", "Pd",
            "Fraction of the pick extent by which the lattice is grown beyond the pick bounding box (also " +
            "floored to 1.5*h so the F sign change is captured on both sides). Default 0.15.",
            GH_ParamAccess.item, 0.15);
        p.AddNumberParameter("Level", "Lv",
            "Iso-level to extract. 0 = the fracture surface (default). Non-zero traces an offset shell of F.",
            GH_ParamAccess.item, 0.0);
        p.AddBooleanParameter("Layered", "Ly",
            "Treat the picks as a STACK of sub-parallel reflectors: order along the Normal, split into depth " +
            "layers, and ALTERNATE the off-surface polarity per layer (phantom-free stacked reflectors; the " +
            "G13 surprise). Default false. When true, Normal is used as the single base normal.",
            GH_ParamAccess.item, false);
        p.AddNumberParameter("Layer Gap", "Lg",
            "Only with Layered: the along-normal gap that starts a new reflector layer. <= 0 = auto " +
            "(2 x median pick spacing).",
            GH_ParamAccess.item, -1.0);
        p.AddBooleanParameter("ShrinkWrap", "Sw",
            "OFF by default. Post-clean the extracted isosurface with Rhino ShrinkWrap (watertight wrap). " +
            "NOTE: ShrinkWrap is NOT the isosurfacer -- the surface is the marching-cubes F=level extraction; " +
            "this only wraps it. Colours are on the un-wrapped surface (G11 honesty note).",
            GH_ParamAccess.item, false);
        // Appended LAST so existing canvases keep their wiring. Default false.
        p.AddBooleanParameter("Run", "R",
            "Set true to build the field + surface on a background thread. False = idle; the canvas never freezes.",
            GH_ParamAccess.item, false);
        // Appended AFTER Run (2026-07-18, route-i port) so existing canvases keep their
        // wiring; same GUID, no new component (PORT_ACCEPTANCE F5).
        p.AddIntegerParameter("Polarity Source", "Ps",
            "Where the field polarity comes from. 0 = SIGNED off-surface (route ii, default): Carr +-h " +
            "manufactured shells along the Normal. 1 = GRADIENT cokriging (route i, Lajaunie 1997): " +
            "increments F(pick)-F(ref)=0 + the Normal(s) as honest gradient data grad F = n (no manufactured " +
            "values; analytic grad F for sigma_pos). Gradient mode beats signed only with per-pick normals " +
            "(G17); it rejects the exponential covariance (not differentiable) and, when Range <= 0, uses the " +
            "fixed default 0.3 * extent (the Python CV auto-fit is not ported). With Layered, layers become " +
            "INTERFACES (per-layer isovalues) instead of alternating polarity.",
            GH_ParamAccess.item, 0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Fracture Mesh", "M",
            "The {F = level} isosurface, vertex-coloured by sigma_pos (green = confident -> red = uncertain).",
            GH_ParamAccess.item);
        p.AddNumberParameter("Sigma_pos", "S",
            "Per-vertex position uncertainty sigma_pos = sigma_F / max(|grad F|, floor) (world units), " +
            "aligned to the mesh vertices.",
            GH_ParamAccess.list);
        p.AddColourParameter("Colours", "C",
            "Per-vertex colours applied to the mesh (same order as the vertices), for re-preview / legend.",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Components", "K",
            "Number of DISJOINT surface pieces in the isosurface -- the honest count of imaged fracture " +
            "structures (may be multiples for a deep stacked reflector).",
            GH_ParamAccess.item);
        p.AddTextParameter("Diagnostics", "D",
            "Field / lattice / surface summary, sigma_pos scale, and any honesty warnings.",
            GH_ParamAccess.item);
    }

    // -------- immutable inputs captured on the UI thread --------
    public sealed class Snapshot
    {
        public double[] Px, Py, Pz;
        public double[][] Normals;   // 1x3 broadcast or Nx3 (already selected / layered)
        public double[] PointSigma;  // null or length N
        public string Family;
        public double Range;         // NaN = auto-fit
        public bool Fit;
        public double HFrac;
        public double H;             // hFrac * spacing (for lattice padding)
        public double Spacing;
        public int LatN;
        public double PadFrac;
        public double Level;
        public bool ShrinkWrap;
        public bool Layered;
        public double LayerGap;
        public int LayerCount;
        public bool NormalDefaulted;
        public bool NormalMismatch;
        public bool Gradient;        // Polarity Source: false = signed (ii), true = gradient (i)
        public int[] InterfaceLabels; // gradient + Layered: depth-layer -> interface labels
    }

    // -------- result produced on the background thread (plain data only) --------
    public sealed class Payload
    {
        public List<double[]> Verts;   // world-space vertices
        public List<int[]> Faces;      // triangles (indices into Verts)
        public int[] Argb;             // per-vertex colour
        public double[] SigmaPos;      // per-vertex sigma_pos
        public int Components;
        public double MinCell;         // min(dx,dy,dz) -> ShrinkWrap target edge
        public string Report;
        public List<string> Warnings = new List<string>();
        public string Failure;
    }

    protected override bool TryRead(IGH_DataAccess da, out bool run, out Snapshot snapshot)
    {
        run = false; snapshot = null;
        da.GetData(12, ref run);
        if (!run) return true;

        var pts = new List<Point3d>();
        if (!da.GetDataList(0, pts) || pts.Count < 3)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Need >= 3 picks.");
            return false;
        }
        int n = pts.Count;
        var px = new double[n]; var py = new double[n]; var pz = new double[n];
        for (int i = 0; i < n; i++) { px[i] = pts[i].X; py[i] = pts[i].Y; pz[i] = pts[i].Z; }

        var normalsIn = new List<Vector3d>();
        da.GetDataList(1, normalsIn);
        var sigIn = new List<double>();
        da.GetDataList(2, sigIn);

        int covIdx = 0; da.GetData(3, ref covIdx);
        double range = -1.0; da.GetData(4, ref range);
        double hFrac = 1.5; da.GetData(5, ref hFrac);
        int latN = 40; da.GetData(6, ref latN);
        double padFrac = 0.15; da.GetData(7, ref padFrac);
        double level = 0.0; da.GetData(8, ref level);
        bool layered = false; da.GetData(9, ref layered);
        double layerGap = -1.0; da.GetData(10, ref layerGap);
        bool shrink = false; da.GetData(11, ref shrink);
        int polaritySource = 0; da.GetData(13, ref polaritySource);
        bool gradient = polaritySource == 1;

        covIdx = Math.Max(0, Math.Min(Families.Length - 1, covIdx));
        if (gradient && Families[covIdx] == "exponential")
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Gradient cokriging (Polarity Source = 1) needs a mean-square differentiable covariance: " +
                "gaussian, matern15, or matern25. The exponential family has no derivative at 0.");
            return false;
        }
        latN = Math.Max(12, Math.Min(80, latN));
        if (!(hFrac > 0)) hFrac = 1.5;
        if (!(padFrac >= 0)) padFrac = 0.15;

        double spacing = MedianNn(px, py, pz);
        double h = hFrac * spacing;

        // per-pick sigma (aligned to picks, else null)
        double[] psig = null;
        if (sigIn != null && sigIn.Count == n) psig = sigIn.ToArray();
        else if (sigIn != null && sigIn.Count > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Point Sigma count ({sigIn.Count}) != pick count ({n}); ignored (uniform nugget used).");

        // polarity normals
        double[][] normals;
        int layerCount = 1;
        bool defaulted = false, mismatch = false;
        double[] baseNormal = (normalsIn != null && normalsIn.Count > 0)
            ? new[] { normalsIn[0].X, normalsIn[0].Y, normalsIn[0].Z }
            : new[] { 0.0, 0.0, 1.0 };

        int[] interfaceLabels = null;
        if (layered)
        {
            if (normalsIn == null || normalsIn.Count == 0) defaulted = true;
            double gap = layerGap > 0 ? layerGap : 2.0 * spacing;
            layerGap = gap;
            var coords = new double[n][];
            for (int i = 0; i < n; i++) coords[i] = new[] { px[i], py[i], pz[i] };
            var labels = PolarityNormals.DepthLayers(coords, baseNormal, gap);
            int mx = 0; for (int i = 0; i < labels.Length; i++) if (labels[i] > mx) mx = labels[i];
            layerCount = mx + 1;
            if (gradient)
            {
                // route i: layers are INTERFACES (per-layer emergent isovalues via the
                // increment labels); the base normal is the gradient direction for all
                // picks -- no alternating polarity sign needed (krige3d_grad semantics).
                normals = new[] { baseNormal };
                interfaceLabels = labels;
            }
            else
            {
                normals = PolarityNormals.LayeredPolarityNormals(coords, baseNormal, gap);
            }
        }
        else
        {
            if (normalsIn == null || normalsIn.Count == 0)
            {
                normals = new[] { new[] { 0.0, 0.0, 1.0 } };  // +Z broadcast
                defaulted = true;
            }
            else if (normalsIn.Count == 1)
            {
                normals = new[] { new[] { normalsIn[0].X, normalsIn[0].Y, normalsIn[0].Z } };  // broadcast
            }
            else if (normalsIn.Count == n)
            {
                normals = new double[n][];
                for (int i = 0; i < n; i++) normals[i] = new[] { normalsIn[i].X, normalsIn[i].Y, normalsIn[i].Z };
            }
            else
            {
                normals = new[] { new[] { normalsIn[0].X, normalsIn[0].Y, normalsIn[0].Z } };  // broadcast first
                mismatch = true;
            }
        }

        snapshot = new Snapshot
        {
            Px = px, Py = py, Pz = pz,
            Normals = normals, PointSigma = psig,
            Family = Families[covIdx],
            Range = range > 0 ? range : double.NaN,
            Fit = !(range > 0),
            HFrac = hFrac, H = h, Spacing = spacing,
            LatN = latN, PadFrac = padFrac, Level = level,
            ShrinkWrap = shrink, Layered = layered, LayerGap = layerGap, LayerCount = layerCount,
            NormalDefaulted = defaulted, NormalMismatch = mismatch,
            Gradient = gradient, InterfaceLabels = interfaceLabels,
        };
        return true;
    }

    protected override Payload Compute(Snapshot s, CancellationToken token, Action<string> progress)
    {
        int n = s.Px.Length;
        progress($"building implicit kriging field ({n} picks)...");
        token.ThrowIfCancellationRequested();

        KrigingField3D kr = null;
        GradientKrigingField gkr = null;
        try
        {
            if (s.Gradient)
            {
                // route i: Lajaunie gradient cokriging (no manufactured +-h shells).
                // Range <= 0 -> the fixed no-fit default 0.3*extent inside the core
                // (the Python CV auto-fit is not ported; see the port doc).
                var coords = new double[n][];
                for (int i = 0; i < n; i++) coords[i] = new[] { s.Px[i], s.Py[i], s.Pz[i] };
                gkr = new GradientKrigingField(coords, s.Normals,
                    interfaceLabels: s.InterfaceLabels, family: s.Family,
                    range: s.Range, pointSigma: s.PointSigma,
                    fit: false, gradFloorFrac: 0.2);
            }
            else
            {
                kr = new KrigingField3D(s.Px, s.Py, s.Pz, s.Normals,
                    family: s.Family, hFrac: s.HFrac, spacing: double.NaN, pointSigma: s.PointSigma,
                    range: s.Range, rangeZ: double.NaN, sill: double.NaN,
                    gradFloorFrac: 0.2, fit: s.Fit);
            }
        }
        catch (Exception kex)
        {
            return new Payload { Failure = "kriging field failed: " + kex.Message };
        }

        // ---- lattice bounds (pick bbox + padding; floor pad to 1.5*h so F straddles 0) ----
        double minx = s.Px.Min(), maxx = s.Px.Max();
        double miny = s.Py.Min(), maxy = s.Py.Max();
        double minz = s.Pz.Min(), maxz = s.Pz.Max();
        double globalExt = Math.Max(maxx - minx, Math.Max(maxy - miny, maxz - minz));
        if (!(globalExt > 0)) globalExt = Math.Max(1.0, 6.0 * s.H);
        double pad = Math.Max(s.PadFrac * globalExt, Math.Max(1.5 * s.H, 0.02 * globalExt));
        double ox = minx - pad, oy = miny - pad, oz = minz - pad;
        double ex = (maxx - minx) + 2 * pad, ey = (maxy - miny) + 2 * pad, ez = (maxz - minz) + 2 * pad;
        double maxExt = Math.Max(ex, Math.Max(ey, ez));
        int Nodes(double e) => Math.Max(12, Math.Min(s.LatN, (int)Math.Round(s.LatN * e / maxExt)));
        int nx = Nodes(ex), ny = Nodes(ey), nz = Nodes(ez);
        double dx = ex / (nx - 1), dy = ey / (ny - 1), dz = ez / (nz - 1);
        var xs = new double[nx]; for (int i = 0; i < nx; i++) xs[i] = ox + i * dx;
        var ys = new double[ny]; for (int j = 0; j < ny; j++) ys[j] = oy + j * dy;
        var zs = new double[nz]; for (int k = 0; k < nz; k++) zs[k] = oz + k * dz;

        progress($"evaluating field on {nx} x {ny} x {nz} = {nx * ny * nz} lattice nodes...");
        token.ThrowIfCancellationRequested();
        var (F, Sig, _, _, _) = s.Gradient
            ? gkr.PredictLattice3d(xs, ys, zs)
            : kr.PredictLattice3d(xs, ys, zs);

        progress("marching cubes...");
        token.ThrowIfCancellationRequested();
        var (verts, faces) = MarchingCubes.Extract(F, nx, ny, nz, s.Level, dx, dy, dz, ox, oy, oz);
        if (verts.Count == 0 || faces.Count == 0)
            return new Payload
            {
                Failure = $"isosurface empty: no F = {s.Level:0.###} crossing in the lattice. Check the " +
                          "polarity Normal, the Level, or increase Padding / Lattice Res."
            };

        progress("gradient + sigma_pos colouring...");
        token.ThrowIfCancellationRequested();
        int V = verts.Count;
        var sigmaPos = new double[V];
        double floor;
        if (s.Gradient)
        {
            // route i: sigma_F + ANALYTIC grad F at the actual vertices (no lattice-
            // spacing floor) -- mirrors krige3d_grad.extract_isosurface.
            var vq = verts.ToArray();
            var sFv = gkr.Predict(vq, withSigma: true).SigmaF;
            var gv = gkr.GradF(vq);
            var gm = new double[V];
            var pos = new List<double>(V);
            for (int v = 0; v < V; v++)
            {
                double g2 = gv[v][0] * gv[v][0] + gv[v][1] * gv[v][1] + gv[v][2] * gv[v][2];
                gm[v] = Math.Sqrt(g2);
                if (gm[v] > 0) pos.Add(gm[v]);
            }
            floor = Math.Max(pos.Count > 0 ? gkr.GradFloorFrac * Median(pos.ToArray()) : 0.0, 1e-9);
            for (int v = 0; v < V; v++) sigmaPos[v] = sFv[v] / Math.Max(gm[v], floor);
        }
        else
        {
            // route ii: |grad F| on the lattice (central differences), median -> floor,
            // trilinear-sampled at the mesh verts (the shipped G13 path).
            double[] gradMag = GradMagLattice(F, nx, ny, nz, dx, dy, dz);
            double medGrad = Median(gradMag);
            floor = kr.GradFloorFrac * medGrad;
            if (!(floor > 0)) floor = 1e-9;
            for (int v = 0; v < V; v++)
            {
                double[] p = verts[v];
                double sF = Trilinear(Sig, nx, ny, nz, ox, oy, oz, dx, dy, dz, p[0], p[1], p[2]);
                double g = Trilinear(gradMag, nx, ny, nz, ox, oy, oz, dx, dy, dz, p[0], p[1], p[2]);
                sigmaPos[v] = sF / Math.Max(g, floor);
            }
        }

        // robust colour scale [p5, p95] of the vertex sigma_pos, green(low)->red(high)
        var sorted = (double[])sigmaPos.Clone(); Array.Sort(sorted);
        double lo = Percentile(sorted, 0.05), hi = Percentile(sorted, 0.95);
        if (!(hi > lo)) hi = lo + 1e-9;
        var argb = new int[V];
        for (int v = 0; v < V; v++) argb[v] = SigmaColour(sigmaPos[v], lo, hi).ToArgb();

        int comp = ComponentCount(faces, V);

        // ---- report + honesty warnings ----
        var warn = new List<string>();
        if (s.NormalDefaulted)
            warn.Add("No polarity Normal wired -> defaulted to +Z (horizontal bed). Wire the family normal / " +
                     "dyke trend for dipping, vertical, or overhanging structures.");
        if (s.NormalMismatch)
            warn.Add($"Normal count != pick count and != 1 -> the first normal was broadcast to all {n} picks.");
        bool singleLine = PlanCollinearity(s.Px, s.Py) < 0.08;
        if (singleLine)
            warn.Add("Picks are collinear in plan (single GPR survey line): the F=0 output is a SECTION " +
                     "extruded along the unsampled direction, NOT a measured 3D surface. Cross more lines " +
                     "for a true 3D fracture surface (G8/G13 declared-width rule).");
        warn.Add("Deep reflectors imaged as one surface may be MULTIPLE sub-parallel structures; the surface " +
                 "is the imaged extent, not a proven single fracture. Use Layered for a stacked-reflector split.");
        if (s.Layered)
            warn.Add(s.Gradient
                ? $"Layered interfaces (gradient mode): {s.LayerCount} reflector layer(s) = {s.LayerCount} " +
                  $"interface(s) along the base normal (gap = {s.LayerGap:0.###}); F=0 is interface 0, other " +
                  "interfaces sit at their emergent isovalues (see Diagnostics; extract via Level)."
                : $"Layered polarity: {s.LayerCount} reflector layer(s) along the base normal " +
                  $"(gap = {s.LayerGap:0.###}).");
        if (s.Gradient && s.Fit)
            warn.Add("Gradient mode with Range unset: fixed default range 0.3 * pick extent (the Python CV " +
                     "auto-fit is not ported, route i). Wire an explicit Range to control smoothness.");
        if (s.Gradient && !s.Layered && s.Normals.Length == 1)
            warn.Add("Gradient mode with ONE broadcast normal: the exact gradient constraint over-planarises " +
                     "rough surfaces (G17 honest limitation). Wire per-pick normals (e.g. Discontinuity Sets " +
                     "local plane normals) for the accuracy win over the signed mode.");
        if (s.ShrinkWrap)
            warn.Add("ShrinkWrap post-clean requested: it wraps the isosurface but is NOT the isosurfacer; the " +
                     "surface is the marching-cubes F=level extraction. Colours index the un-wrapped surface.");

        var sb = new System.Text.StringBuilder();
        if (s.Gradient)
        {
            sb.AppendLine($"IsoFractureField | mode=GRADIENT cokriging (route i, Lajaunie 1997) | picks={n}, " +
                          $"increments={gkr.IncrementCount}, gradient eqs={gkr.GradientCount * 3}, cov={s.Family}, " +
                          $"range={gkr.Ranges[0]:0.###}{(s.Fit ? " (default 0.3*extent)" : " (fixed)")}, " +
                          $"sill={gkr.Sill:0.####} (gauge R^2/|phi''(0)|)" +
                          (s.PointSigma != null ? ", per-pick sigma ON" : ""));
            if (s.InterfaceLabels != null)
            {
                var lv = gkr.InterfaceLevels();
                var parts = new List<string>();
                foreach (var kvp in lv) parts.Add($"{kvp.Key}:{kvp.Value:0.####}");
                sb.AppendLine("interface isovalues (label:F): " + string.Join(", ", parts));
            }
        }
        else
        {
            sb.AppendLine($"IsoFractureField | picks={n}, spacing={s.Spacing:0.###} (median NN), h={kr.H:0.###} " +
                          $"(Carr +-h), cov={s.Family}, range={kr.Range:0.###}{(s.Fit ? " (auto-fit)" : " (fixed)")}" +
                          $", rangeZ={kr.RangeZ:0.###}, sill={kr.Sill:0.####}" +
                          (s.PointSigma != null ? ", per-pick sigma ON" : ""));
        }
        sb.AppendLine($"lattice {nx} x {ny} x {nz} = {nx * ny * nz} nodes, cell (dx,dy,dz)=" +
                      $"({dx:0.###},{dy:0.###},{dz:0.###}), level={s.Level:0.###}");
        sb.AppendLine($"isosurface: {V} verts, {faces.Count} tris, {comp} disjoint structure(s)");
        sb.AppendLine($"sigma_pos = sigma_F/max(|grad F|, floor), floor=0.2*median|grad F|={floor:0.#####}" +
                      (s.Gradient ? " (ANALYTIC grad F at the verts)" : "") +
                      $"; colour scale green->red over [p5,p95]=[{lo:0.####},{hi:0.####}]");
        foreach (var w in warn) sb.AppendLine("WARN: " + w);

        return new Payload
        {
            Verts = verts, Faces = faces, Argb = argb, SigmaPos = sigmaPos,
            Components = comp, MinCell = Math.Min(dx, Math.Min(dy, dz)),
            Report = sb.ToString().TrimEnd(), Warnings = warn,
        };
    }

    protected override void EmitResult(IGH_DataAccess da, Payload r)
    {
        if (r.Failure != null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, r.Failure);
            da.SetData(4, r.Failure);
            return;
        }

        // Build the Rhino Mesh on the UI thread (no RhinoCommon geometry crossed the Task).
        var mesh = new Mesh();
        int V = r.Verts.Count;
        for (int v = 0; v < V; v++)
        {
            var p = r.Verts[v];
            mesh.Vertices.Add(p[0], p[1], p[2]);
            mesh.VertexColors.Add(Color.FromArgb(r.Argb[v]));
        }
        foreach (var t in r.Faces) mesh.Faces.AddFace(t[0], t[1], t[2]);
        mesh.Faces.CullDegenerateFaces();
        mesh.Normals.ComputeNormals();
        mesh.Compact();

        Mesh outMesh = mesh;
        if (r.MinCell > 0 && ShrinkWrapRequested)
        {
            try
            {
                var parms = new ShrinkWrapParameters
                {
                    TargetEdgeLength = Math.Max(r.MinCell, 1e-6),
                    SmoothingIterations = 1,
                    FillHolesInInputObjects = true,
                };
                var wrapped = mesh.ShrinkWrap(parms);
                if (wrapped != null && wrapped.IsValid && wrapped.Faces.Count > 0)
                {
                    wrapped.Normals.ComputeNormals();
                    outMesh = wrapped;   // colours are on `mesh` (un-wrapped); noted in Diagnostics
                }
                else
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        "ShrinkWrap produced no valid mesh; returning the raw isosurface.");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "ShrinkWrap skipped: " + ex.Message);
            }
        }

        foreach (var w in r.Warnings) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, w);

        var colours = new List<Color>(V);
        for (int v = 0; v < V; v++) colours.Add(Color.FromArgb(r.Argb[v]));

        Message = $"{r.Components} surf | {V}v";
        da.SetData(0, outMesh);
        da.SetDataList(1, r.SigmaPos);
        da.SetDataList(2, colours);
        da.SetData(3, r.Components);
        da.SetData(4, r.Report);
    }

    protected override void EmitIdle(IGH_DataAccess da, string message)
    {
        da.SetData(4, message);
    }

    // ShrinkWrap is captured per-solve into the payload's MinCell gate; the toggle itself is
    // re-read cheaply here so EmitResult (UI thread) knows whether to wrap.
    private bool ShrinkWrapRequested;
    protected override bool TryReadRunOnly(IGH_DataAccess da, out bool run)
    {
        // light pass: also latch the ShrinkWrap toggle for EmitResult without the heavy capture.
        run = false;
        bool sw = false;
        try { da.GetData(11, ref sw); da.GetData(12, ref run); }
        catch { return false; }
        ShrinkWrapRequested = sw;
        return true;
    }

    // =====================================================================
    // helpers (all Rhino-free plain math; safe on the background thread)
    // =====================================================================

    /// <summary>Median nearest-neighbour spacing of the picks (brute force, small N).</summary>
    private static double MedianNn(double[] x, double[] y, double[] z)
    {
        int n = x.Length;
        if (n < 2) return 1.0;
        var best = new double[n];
        for (int i = 0; i < n; i++)
        {
            double bd2 = double.PositiveInfinity;
            for (int j = 0; j < n; j++)
            {
                if (j == i) continue;
                double dx = x[i] - x[j], dy = y[i] - y[j], dz = z[i] - z[j];
                double d2 = dx * dx + dy * dy + dz * dz;
                if (d2 < bd2) bd2 = d2;
            }
            best[i] = Math.Sqrt(bd2);
        }
        double m = Median(best);
        return m > 1e-12 ? m : 1.0;
    }

    private static double Median(double[] a)
    {
        int n = a.Length;
        if (n == 0) return 0.0;
        var c = (double[])a.Clone();
        Array.Sort(c);
        return (n & 1) == 1 ? c[n / 2] : 0.5 * (c[n / 2 - 1] + c[n / 2]);
    }

    /// <summary>p in [0,1] percentile of a pre-sorted array (linear interpolation).</summary>
    private static double Percentile(double[] sorted, double p)
    {
        int n = sorted.Length;
        if (n == 0) return 0.0;
        if (n == 1) return sorted[0];
        double idx = p * (n - 1);
        int lo = (int)Math.Floor(idx);
        int hi = Math.Min(lo + 1, n - 1);
        double t = idx - lo;
        return sorted[lo] * (1 - t) + sorted[hi] * t;
    }

    /// <summary>|grad F| per lattice node via central differences (one-sided at faces).
    /// Flat C-order index (i,j,k) = (i*ny + j)*nz + k, matching PredictLattice3d.</summary>
    private static double[] GradMagLattice(double[] f, int nx, int ny, int nz, double dx, double dy, double dz)
    {
        var g = new double[f.Length];
        int Idx(int i, int j, int k) => (i * ny + j) * nz + k;
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
                for (int k = 0; k < nz; k++)
                {
                    double gx, gy, gz;
                    if (i == 0) gx = (f[Idx(1, j, k)] - f[Idx(0, j, k)]) / dx;
                    else if (i == nx - 1) gx = (f[Idx(nx - 1, j, k)] - f[Idx(nx - 2, j, k)]) / dx;
                    else gx = (f[Idx(i + 1, j, k)] - f[Idx(i - 1, j, k)]) / (2 * dx);
                    if (j == 0) gy = (f[Idx(i, 1, k)] - f[Idx(i, 0, k)]) / dy;
                    else if (j == ny - 1) gy = (f[Idx(i, ny - 1, k)] - f[Idx(i, ny - 2, k)]) / dy;
                    else gy = (f[Idx(i, j + 1, k)] - f[Idx(i, j - 1, k)]) / (2 * dy);
                    if (k == 0) gz = (f[Idx(i, j, 1)] - f[Idx(i, j, 0)]) / dz;
                    else if (k == nz - 1) gz = (f[Idx(i, j, nz - 1)] - f[Idx(i, j, nz - 2)]) / dz;
                    else gz = (f[Idx(i, j, k + 1)] - f[Idx(i, j, k - 1)]) / (2 * dz);
                    g[Idx(i, j, k)] = Math.Sqrt(gx * gx + gy * gy + gz * gz);
                }
        return g;
    }

    /// <summary>Trilinear sample of a lattice volume at world (x,y,z).</summary>
    private static double Trilinear(double[] vol, int nx, int ny, int nz,
        double ox, double oy, double oz, double dx, double dy, double dz,
        double x, double y, double z)
    {
        double fi = (x - ox) / dx, fj = (y - oy) / dy, fk = (z - oz) / dz;
        if (fi < 0) fi = 0; else if (fi > nx - 1) fi = nx - 1;
        if (fj < 0) fj = 0; else if (fj > ny - 1) fj = ny - 1;
        if (fk < 0) fk = 0; else if (fk > nz - 1) fk = nz - 1;
        int i0 = Math.Min((int)fi, nx - 2), j0 = Math.Min((int)fj, ny - 2), k0 = Math.Min((int)fk, nz - 2);
        if (i0 < 0) i0 = 0; if (j0 < 0) j0 = 0; if (k0 < 0) k0 = 0;
        double tx = fi - i0, ty = fj - j0, tz = fk - k0;
        int Idx(int i, int j, int k) => (i * ny + j) * nz + k;
        double c000 = vol[Idx(i0, j0, k0)], c100 = vol[Idx(i0 + 1, j0, k0)];
        double c010 = vol[Idx(i0, j0 + 1, k0)], c110 = vol[Idx(i0 + 1, j0 + 1, k0)];
        double c001 = vol[Idx(i0, j0, k0 + 1)], c101 = vol[Idx(i0 + 1, j0, k0 + 1)];
        double c011 = vol[Idx(i0, j0 + 1, k0 + 1)], c111 = vol[Idx(i0 + 1, j0 + 1, k0 + 1)];
        double c00 = c000 * (1 - tx) + c100 * tx, c10 = c010 * (1 - tx) + c110 * tx;
        double c01 = c001 * (1 - tx) + c101 * tx, c11 = c011 * (1 - tx) + c111 * tx;
        double c0 = c00 * (1 - ty) + c10 * ty, c1 = c01 * (1 - ty) + c11 * ty;
        return c0 * (1 - tz) + c1 * tz;
    }

    /// <summary>green (sigma_pos &lt;= lo) -> yellow -> red (&gt;= hi).</summary>
    private static Color SigmaColour(double sigma, double lo, double hi)
    {
        double t = (sigma - lo) / (hi - lo);
        if (t < 0) t = 0; else if (t > 1) t = 1;
        int r = (int)(255 * Math.Min(1.0, 2 * t));
        int g = (int)(255 * Math.Min(1.0, 2 * (1 - t)));
        return Color.FromArgb(r, g, 60);
    }

    /// <summary>Disjoint-piece count of the triangle mesh (union-find over shared vertices).</summary>
    private static int ComponentCount(List<int[]> faces, int vertCount)
    {
        var parent = new int[vertCount];
        for (int i = 0; i < vertCount; i++) parent[i] = i;
        int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
        void Union(int a, int b) { int ra = Find(a), rb = Find(b); if (ra != rb) parent[ra] = rb; }
        var used = new bool[vertCount];
        foreach (var t in faces)
        {
            Union(t[0], t[1]); Union(t[1], t[2]);
            used[t[0]] = used[t[1]] = used[t[2]] = true;
        }
        var roots = new HashSet<int>();
        for (int i = 0; i < vertCount; i++) if (used[i]) roots.Add(Find(i));
        return roots.Count;
    }

    /// <summary>Plan (x,y) collinearity: sqrt(minEig/maxEig) of the 2x2 scatter. ~0 = a single
    /// line; ~1 = an isotropic plan spread. Used for the single-survey-line honesty warning.</summary>
    private static double PlanCollinearity(double[] x, double[] y)
    {
        int n = x.Length;
        if (n < 3) return 0.0;
        double mx = x.Average(), my = y.Average();
        double sxx = 0, syy = 0, sxy = 0;
        for (int i = 0; i < n; i++)
        {
            double dx = x[i] - mx, dy = y[i] - my;
            sxx += dx * dx; syy += dy * dy; sxy += dx * dy;
        }
        double tr = sxx + syy, det = sxx * syy - sxy * sxy;
        double disc = Math.Sqrt(Math.Max(0.0, tr * tr / 4 - det));
        double l1 = tr / 2 + disc, l2 = tr / 2 - disc;
        if (l1 <= 1e-12) return 1.0;
        return Math.Sqrt(Math.Max(0.0, l2) / l1);
    }
}
