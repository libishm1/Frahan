#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using Frahan.Core.ScanIngest;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.ScanIngest;

// =============================================================================
// ScanReconstructComponent — Phase H visible wrapper (UX architecture
// report §7.7.E). Picks an Alpha Shape / Poisson / Advancing-Front
// reconstruction primitive via a Mode enum and runs it through the
// native shim (frahan_cgal.dll or frahan_geogram.dll).
//
// Native exports are wired conditionally: when the user's machine has
// the Phase H build of the shims, the component returns reconstructed
// meshes; when it doesn't, the component surfaces a Warning bubble
// asking for a rebuild and returns no mesh. The .gha continues to
// load and other components keep working.
//
// NON-BLOCKING (2026-05-28): the native reconstruction can take many seconds
// on a large cloud, so it runs on a background thread via AsyncScanComponent,
// gated by a "Run" toggle (default false). Opening a definition never
// auto-triggers a reconstruction on the UI thread, and the canvas stays
// responsive while a reconstruction is in flight.
// =============================================================================

[Algorithm("3D Alpha Shapes", "Edelsbrunner & Mücke 1994 (three-dimensional alpha shapes)",
    Note = "CGAL; tight reconstruction (Mode = AlphaShape)")]
[Algorithm("Screened Poisson reconstruction", "Kazhdan & Hoppe 2013 (Screened Poisson Surface Reconstruction)",
    Note = "Geogram bundled PoissonRecon (CGAL fallback); requires oriented normals (Mode = Poisson)")]
[Algorithm("Advancing-front surface reconstruction", "Cohen-Steiner & Da 2004 (advancing-front surface reconstruction)",
    Note = "CGAL; tolerant of unoriented input (Mode = AdvancingFront)")]
public sealed class ScanReconstructComponent
    : AsyncScanComponent<ScanReconstructComponent.Snapshot, ScanReconstructComponent.Payload>
{
    public ScanReconstructComponent()
        : base("Scan Reconstruct", "ScanRecon",
            "Reconstruct a closed mesh from a point cloud. Three backends: " +
            "Alpha Shape (CGAL; tight; preserves edges), Poisson (Geogram-" +
            "bundled PoissonRecon, CGAL fallback; smooth; needs oriented " +
            "normals), and Advancing-Front (CGAL; BPA-equivalent; tolerant of " +
            "unoriented input). Runs on a background thread (Run gate) so the " +
            "canvas never freezes. Requires the Phase H rebuild of " +
            "frahan_cgal.dll / frahan_geogram.dll. [Edelsbrunner & Mücke 1994]",
            "Frahan", "Mesh")
    {
    }

    public override Guid ComponentGuid => new Guid("E4F5A6B7-3101-4F5E-A6B7-C8D9E0F12345");
    protected override Bitmap Icon => IconProvider.Load("PoissonReconstruct.png");
    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddPointParameter("Points", "P",
            "Input point cloud as a point list. Optional if Cloud is wired.", GH_ParamAccess.list);
        p[0].Optional = true;
        p.AddVectorParameter("Normals", "N",
            "Optional per-point oriented normals (required for Poisson; " +
            "ignored by Alpha Shape and Advancing-Front).",
            GH_ParamAccess.list);
        p[1].Optional = true;
        p.AddIntegerParameter("Mode", "M",
            "0 = Auto, 1 = AlphaShape, 2 = Poisson (Geogram), 3 = AdvancingFront, " +
            "4 = Poisson (CGAL). Auto picks AlphaShape with find_optimal_alpha(1) " +
            "and falls back to Advancing-Front. All run in an isolated worker " +
            "process, so a backend crash cannot take down Rhino.",
            GH_ParamAccess.item, 0);
        p.AddNumberParameter("Alpha", "A",
            "Alpha value for AlphaShape mode. <= 0 uses CGAL's " +
            "find_optimal_alpha(1).",
            GH_ParamAccess.item, 0.0);
        p.AddIntegerParameter("Poisson Depth", "D",
            "Octree depth for Poisson mode. Typical 7-9. <= 0 uses 8.",
            GH_ParamAccess.item, 8);
        p.AddNumberParameter("Samples Per Node", "Sn",
            "Poisson samples-per-leaf-node. <= 0 uses 1.5.",
            GH_ParamAccess.item, 1.5);
        p.AddNumberParameter("Radius Ratio", "Rr",
            "Advancing-Front radius ratio. <= 0 uses CGAL default 5.0.",
            GH_ParamAccess.item, 5.0);
        p.AddNumberParameter("AF Beta", "Bt",
            "Advancing-Front sharp-edge parameter. <= 0 uses 0.52.",
            GH_ParamAccess.item, 0.52);
        p.AddGeometryParameter("Cloud", "C",
            "Input as a single native PointCloud (lag-free; preferred for large " +
            "scans). If it carries normals (from Estimate Cloud Normals' Cloud " +
            "output), Poisson uses them. If wired, the Points / Normals lists are ignored.",
            GH_ParamAccess.item);
        p[8].Optional = true;
        // Appended LAST so existing canvases keep their wiring. Default false.
        p.AddBooleanParameter("Run", "R",
            "Set true to reconstruct (on a background thread). False = idle; " +
            "nothing runs, the canvas never freezes.",
            GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Mesh", "M",
            "Reconstructed mesh.", GH_ParamAccess.item);
        p.AddTextParameter("Used Mode", "U",
            "Which backend actually ran (AlphaShape / Poisson / " +
            "AdvancingFront / None).",
            GH_ParamAccess.item);
        p.AddTextParameter("Report", "R",
            "One-line summary: input count, output verts / tris, mode.",
            GH_ParamAccess.item);
    }

    public sealed class Snapshot
    {
        // Flattened on the UI thread (TryRead). The background Task touches only
        // these plain arrays -- NO RhinoCommon geometry crosses the thread
        // boundary (that is what crashed Rhino before).
        public double[] Pts;
        public double[] Nrm;   // may be null
        public int NPts;
        public int Mode;
        public double Alpha;
        public int Depth;
        public double Spn;
        public double Rr;
        public double Bt;
    }

    public sealed class Payload
    {
        // Raw reconstruction output (flat). The Rhino Mesh is built in
        // EmitResult on the UI thread, never on the background thread.
        public double[] Verts;
        public int[] Tris;
        public string UsedMode;
        public string Report;
        public string Failure;
        public int InputCount;
    }

    protected override bool TryRead(IGH_DataAccess da, out bool run, out Snapshot snapshot)
    {
        run = false; snapshot = null;
        da.GetData(9, ref run);
        if (!run) return true;

        int mode = 0;
        double alpha = 0.0;
        int depth = 8;
        double spn = 1.5;
        double rr = 5.0;
        double bt = 0.52;
        PointCloud inCloud = null;
        da.GetData(8, ref inCloud);
        da.GetData(2, ref mode);
        da.GetData(3, ref alpha);
        da.GetData(4, ref depth);
        da.GetData(5, ref spn);
        da.GetData(6, ref rr);
        da.GetData(7, ref bt);

        var snap = new Snapshot
        {
            Mode = mode, Alpha = alpha, Depth = depth, Spn = spn, Rr = rr, Bt = bt,
        };

        // Flatten the input to plain double[] HERE, on the UI thread (reading
        // PointCloud items / GH lists must not happen on the background thread).
        bool hasNormals;
        if (inCloud != null && inCloud.Count >= 4)
        {
            int nPts = inCloud.Count;
            var pts = new double[3 * nPts];
            bool hasN = inCloud.ContainsNormals;
            double[] nrm = hasN ? new double[3 * nPts] : null;
            for (int i = 0; i < nPts; i++)
            {
                var loc = inCloud[i].Location;
                pts[3 * i + 0] = loc.X; pts[3 * i + 1] = loc.Y; pts[3 * i + 2] = loc.Z;
                if (hasN)
                {
                    var nv = inCloud[i].Normal;
                    nrm[3 * i + 0] = nv.X; nrm[3 * i + 1] = nv.Y; nrm[3 * i + 2] = nv.Z;
                }
            }
            snap.Pts = pts; snap.Nrm = nrm; snap.NPts = nPts;
            hasNormals = hasN;
        }
        else
        {
            var points = new List<Point3d>();
            if (!da.GetDataList(0, points) || points.Count < 4)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Need at least 4 points (wire Points or Cloud).");
                return false;
            }
            var normals = new List<Vector3d>();
            da.GetDataList(1, normals);
            int nPts = points.Count;
            var pts = new double[3 * nPts];
            for (int i = 0; i < nPts; i++)
            {
                pts[3 * i + 0] = points[i].X;
                pts[3 * i + 1] = points[i].Y;
                pts[3 * i + 2] = points[i].Z;
            }
            double[] nrm = null;
            if (normals.Count == nPts)
            {
                nrm = new double[3 * nPts];
                for (int i = 0; i < nPts; i++)
                {
                    nrm[3 * i + 0] = normals[i].X;
                    nrm[3 * i + 1] = normals[i].Y;
                    nrm[3 * i + 2] = normals[i].Z;
                }
            }
            else if (normals.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Normals count ({normals.Count}) differs from points ({nPts}); normals ignored.");
            snap.Pts = pts; snap.Nrm = nrm; snap.NPts = nPts;
            hasNormals = nrm != null;
        }

        if ((mode == 2 || mode == 4) && !hasNormals)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Poisson (Mode 2/4) requires oriented normals; wire EstimateCloudNormals " +
                "upstream (its Cloud output carries normals).");
            return false;
        }

        snapshot = snap;
        return true;
    }

    protected override Payload Compute(Snapshot s, CancellationToken token, Action<string> progress)
    {
        // Plain arrays only on this background thread (no RhinoCommon geometry).
        double[] pts = s.Pts;
        double[] nrm = s.Nrm;
        int nPts = s.NPts;

        token.ThrowIfCancellationRequested();

        // V3 ROSES roadmap #1 (T1 numeric hygiene): recenter the cloud to its centroid
        // before the native reconstruction so the Delaunay / alpha predicate runs near
        // the origin (recovers mantissa digits at quarry/UTM scale). Normals are
        // directions, so they are NOT translated. The centroid is added back to the
        // result vertices below. Rhino-free (GeometryNumerics is in Frahan.Core).
        var reconOrigin = Frahan.Masonry.Geometry.GeometryNumerics.Centroid(pts);
        pts = Frahan.Masonry.Geometry.GeometryNumerics.Recenter(pts, out reconOrigin);

        // Reconstruction runs in a SEPARATE PROCESS (frahan_recon_worker.exe). A
        // native abort/crash there takes down only the worker -- Rhino survives
        // and we report a clean error. Worker modes: 1=Alpha 2=Poisson(geogram)
        // 3=AdvancingFront 4=Poisson(CGAL). Falls back in-process if the worker
        // exe isn't deployed.
        double[] verts = null;
        int[] tris = null;
        string usedMode = "None";
        string firstError = null;

        int tried = s.Mode == 0 ? 1 : s.Mode; // Auto starts with AlphaShape
        switch (tried)
        {
            case 1:
                progress($"alpha-shape on {nPts} points (isolated worker)...");
                if (OutOfProcessReconstructor.TryReconstruct(1, pts, nrm, s.Alpha, s.Depth, s.Spn, s.Rr, s.Bt, out verts, out tris, out string e1))
                    usedMode = "AlphaShape";
                else
                {
                    firstError = e1;
                    if (s.Mode == 0)
                    {
                        progress("alpha-shape failed; advancing-front fallback...");
                        if (OutOfProcessReconstructor.TryReconstruct(3, pts, nrm, s.Alpha, s.Depth, s.Spn, s.Rr, s.Bt, out verts, out tris, out string e2))
                            usedMode = "AdvancingFront";
                        else firstError = $"AlphaShape: {e1}; AdvancingFront: {e2}";
                    }
                }
                break;
            case 2:
                progress($"poisson (geogram, isolated worker) on {nPts} oriented points...");
                if (OutOfProcessReconstructor.TryReconstruct(2, pts, nrm, s.Alpha, s.Depth, s.Spn, s.Rr, s.Bt, out verts, out tris, out string e3))
                    usedMode = "Poisson";
                else firstError = e3;
                break;
            case 3:
                progress($"advancing-front (isolated worker) on {nPts} points...");
                if (OutOfProcessReconstructor.TryReconstruct(3, pts, nrm, s.Alpha, s.Depth, s.Spn, s.Rr, s.Bt, out verts, out tris, out string e4))
                    usedMode = "AdvancingFront";
                else firstError = e4;
                break;
            case 4:
                progress($"poisson (CGAL, isolated worker) on {nPts} oriented points...");
                if (OutOfProcessReconstructor.TryReconstruct(4, pts, nrm, s.Alpha, s.Depth, s.Spn, s.Rr, s.Bt, out verts, out tris, out string e5))
                    usedMode = "Poisson(CGAL)";
                else firstError = e5;
                break;
            default:
                return new Payload
                {
                    Failure = $"Mode must be 0 (Auto), 1 (AlphaShape), 2 (Poisson/geogram), 3 (AdvancingFront), or 4 (Poisson/CGAL); got {s.Mode}.",
                };
        }

        if (verts == null || tris == null)
            return new Payload { Failure = firstError ?? "(unknown)", InputCount = nPts };

        // Restore the recenter translation (T1), then clean the soup: drop the
        // dangling/isolated SINGULAR facets the alpha-shape can emit (the "weird
        // mesh"), keeping the largest edge-connected component. Both Rhino-free +
        // headless-tested (ReconstructionCleanup). Works even with an unpatched DLL.
        Frahan.Core.ScanIngest.ReconstructionCleanup.Translate(
            verts, reconOrigin.X, reconOrigin.Y, reconOrigin.Z);
        int rawT = tris.Length / 3;
        Frahan.Core.ScanIngest.ReconstructionCleanup.Clean(ref verts, ref tris);
        int cleanT = tris.Length / 3;

        token.ThrowIfCancellationRequested();
        // Return the RAW reconstruction arrays. The Rhino Mesh is assembled in
        // EmitResult on the UI thread (Mesh ops off-thread can hard-crash Rhino).
        return new Payload
        {
            Verts = verts,
            Tris = tris,
            UsedMode = usedMode,
            InputCount = nPts,
            Report = $"In: {nPts} pts; Out: V={verts.Length / 3} T={cleanT}; Mode={usedMode} (isolated worker; recentered T1; cleaned {rawT - cleanT} stray tris)",
        };
    }

    protected override void EmitResult(IGH_DataAccess da, Payload r)
    {
        if (r.Verts == null || r.Tris == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"Reconstruction unavailable: {r.Failure ?? "(unknown)"}");
            da.SetData(1, "None");
            da.SetData(2, $"Failed: {r.Failure ?? "(unknown)"}");
            return;
        }
        // Build the Rhino Mesh HERE, on the UI thread.
        var mesh = new Mesh();
        int vc = r.Verts.Length / 3;
        for (int i = 0; i < vc; i++)
            mesh.Vertices.Add(r.Verts[3 * i + 0], r.Verts[3 * i + 1], r.Verts[3 * i + 2]);
        int tc = r.Tris.Length / 3;
        for (int i = 0; i < tc; i++)
            mesh.Faces.AddFace(r.Tris[3 * i + 0], r.Tris[3 * i + 1], r.Tris[3 * i + 2]);
        mesh.Normals.ComputeNormals();
        mesh.Compact();

        da.SetData(0, mesh);
        da.SetData(1, r.UsedMode);
        da.SetData(2, r.Report);
    }

    protected override void EmitIdle(IGH_DataAccess da, string message)
    {
        da.SetData(2, message);
    }
}
