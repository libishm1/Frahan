#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Quarry;
using Frahan.Masonry.Quarry.BlockCutOpt;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Quarry
{
    // =========================================================================
    // BlockCutOpt ingestion / dataset components, 2026-05-15.
    //
    // Closes gaps 2, 5, 7 of the 2026-05-15 Quarry-coverage audit:
    //   FrahanPhotoToPlyComponent          gap 2 (Photo2Ply)
    //   FrahanAlgebraicConvexPolyComponent gap 5 (AlgConv)
    //   FrahanSyntheticTnGraniteComponent  gap 7 (TnGran)
    // =========================================================================

    // -------------------------------------------------------------------------
    // 5. Photo → PLY end-to-end. CSV trace source (already-detected traces in
    //    world metres); the GeoFractNet Python path is exposed by GFNInfer.
    //    Wraps PhotogrammetryPipeline.DetectAndExtrude.
    // -------------------------------------------------------------------------
    [DesignApplication(
        "Run a fracture detector on a calibrated image and emit the  vertical-extruded PLY consumable by BlockCutOpt",
        DesignFlow.TopDown)]
    public sealed class FrahanPhotoToPlyComponent : FrahanComponentBase
    {
        public FrahanPhotoToPlyComponent()
            : base(
                "Frahan Photo Detect → PLY", "Photo2Ply",
                "v1 reads pre-detected fracture TRACES from a CSV (x1, y1, x2, y2 " +
                "in world metres) and emits the vertical-extruded PLY consumable by " +
                "BlockCutOpt. The on-image fracture detector is not yet wired (the " +
                "Origin/GSD/Flip-Y inputs are placeholders for it). Pair with GFNInfer " +
                "to write the CSV from a GeoFractNet run, or hand-author the CSV from " +
                "QGIS / AutoCAD digitisation.",
                "Frahan", "Block")
        { }

        public override Guid ComponentGuid =>
            new Guid("F2D0BC14-1234-4F2D-A0B0-7E60CADA15B4");

        public override GH_Exposure Exposure => Frahan.GH.Attributes.LabConfig.EffectiveExposure(ComponentGuid, GH_Exposure.primary);

        protected override Bitmap Icon => IconProvider.Load("PlyReader.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("CSV Path", "Csv", "Trace CSV file (x1, y1, x2, y2 in metres).", GH_ParamAccess.item);
            p.AddNumberParameter("Origin X (m)", "Ox", "World X of pixel (0, 0). Unused by CSV backend.", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Origin Y (m)", "Oy", "World Y of pixel (0, 0). Unused by CSV backend.", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("GSD (m/px)", "Gsd", "Ground sampling distance. Unused by CSV backend.", GH_ParamAccess.item, 0.02);
            p.AddNumberParameter("Z Min (m)", "Zmin", "Bottom of vertical extrusion.", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Z Max (m)", "Zmax", "Top of vertical extrusion.", GH_ParamAccess.item, 1.0);
            p.AddBooleanParameter("Flip Y", "Fy", "Pixel Y points down. Unused by CSV backend.", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Fractures", "F", "Rhino Mesh of vertically-extruded fracture triangles.", GH_ParamAccess.item);
            p.AddIntegerParameter("Trace Count", "Tc", "Number of traces parsed.", GH_ParamAccess.item);
            p.AddIntegerParameter("Triangle Count", "Tri", "Number of triangles emitted.", GH_ParamAccess.item);
            p.AddTextParameter("Backend", "Bk", "Detector backend used.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            string csv = null;
            double ox = 0.0, oy = 0.0, gsd = 0.02;
            double zMin = 0.0, zMax = 1.0;
            bool flipY = true;
            if (!da.GetData(0, ref csv) || string.IsNullOrWhiteSpace(csv))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "CSV path required."); return; }
            da.GetData(1, ref ox); da.GetData(2, ref oy); da.GetData(3, ref gsd);
            da.GetData(4, ref zMin); da.GetData(5, ref zMax); da.GetData(6, ref flipY);
            if (!(gsd > 0)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "GSD must be > 0."); return; }
            if (!(zMax > zMin)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Z Max must exceed Z Min."); return; }

            PlyMesh ply;
            int traceCount;
            try
            {
                var detector = new CsvFractureTraceSource();
                var map = new ImageToWorldMap(ox, oy, gsd, flipY);
                var traces = detector.Detect(csv, map);
                traceCount = traces.Count;
                ply = PhotogrammetryPipeline.DetectAndExtrude(detector, csv, map, zMin, zMax);
            }
            catch (Exception ex)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Detect/extrude failed: {ex.Message}"); return; }

            var mesh = GhBlockCutOptInterop.PlyToRhinoMesh(ply);
            da.SetData(0, mesh);
            da.SetData(1, traceCount);
            da.SetData(2, ply.TriangleCount);
            da.SetData(3, "csv");
        }
    }

    // -------------------------------------------------------------------------
    // 6. Algebraic Convex Polyhedron -- Zhang 2024 cut-code parity (synthesis
    //    I14). Half-space inequality input → ConvexPolyhedron → Rhino Mesh.
    // -------------------------------------------------------------------------
    [Algorithm("Half-space intersection convex polyhedron", "Frahan-original", Note = "Textbook half-space-intersection geometry; Zhang 2024 cut-code parity target, not a faithful re-implementation.")]
    [DesignApplication(
        "Build a convex polyhedron from N half-space inequalities  Nx*x + Ny*y + Nz*z <= b (Zhang 2024 parity, synth...",
        DesignFlow.TopDown)]
    public sealed class FrahanAlgebraicConvexPolyComponent : FrahanComponentBase
    {
        public FrahanAlgebraicConvexPolyComponent()
            : base(
                "Frahan Algebraic Convex Polyhedron", "AlgConv",
                "Build a convex polyhedron from N half-space inequalities " +
                "Nx*x + Ny*y + Nz*z <= b (Zhang 2024 parity, synthesis I14). " +
                "Each parallel-list row defines one face's outward normal " +
                "and offset. Returns a triangulated Rhino Mesh. Frahan-original method.",
                "Frahan", "Block")
        { }

        public override Guid ComponentGuid =>
            new Guid("F2D0BC15-1234-4F2D-A0B0-7E60CADA15B5");

        public override GH_Exposure Exposure => Frahan.GH.Attributes.LabConfig.EffectiveExposure(ComponentGuid, GH_Exposure.primary);

        protected override Bitmap Icon => IconProvider.Load("CoacdDecompose.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddNumberParameter("B", "B", "Right-hand side b per inequality.", GH_ParamAccess.list);
            p.AddNumberParameter("Nx", "Nx", "Outward-normal X per inequality.", GH_ParamAccess.list);
            p.AddNumberParameter("Ny", "Ny", "Outward-normal Y per inequality.", GH_ParamAccess.list);
            p.AddNumberParameter("Nz", "Nz", "Outward-normal Z per inequality.", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Polyhedron", "P", "Triangulated CPH as Rhino Mesh.", GH_ParamAccess.item);
            p.AddIntegerParameter("Vertex Count", "V", "Vertex count.", GH_ParamAccess.item);
            p.AddIntegerParameter("Face Count", "F", "Face count.", GH_ParamAccess.item);
            p.AddNumberParameter("Volume (m^3)", "Vol", "Polyhedron volume.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var bs = new List<double>();
            var nxs = new List<double>();
            var nys = new List<double>();
            var nzs = new List<double>();
            da.GetDataList(0, bs);
            da.GetDataList(1, nxs);
            da.GetDataList(2, nys);
            da.GetDataList(3, nzs);
            int n = bs.Count;
            if (n < 4)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Need at least 4 inequalities to define a bounded polyhedron."); return; }
            if (nxs.Count != n || nys.Count != n || nzs.Count != n)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "All four lists must be equal length."); return; }

            var rows = new List<(double B, double Nx, double Ny, double Nz)>(n);
            for (int i = 0; i < n; i++) rows.Add((bs[i], nxs[i], nys[i], nzs[i]));

            ConvexPolyhedron cph;
            try { cph = ConvexPolyhedron.FromInequalities(rows); }
            catch (Exception ex)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); return; }
            if (cph == null)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Inequalities did not yield a bounded polyhedron."); return; }

            var ply = cph.ToPlyMesh();
            var mesh = GhBlockCutOptInterop.PlyToRhinoMesh(ply);
            da.SetData(0, mesh);
            da.SetData(1, cph.Vertices.Count);
            da.SetData(2, cph.Faces.Count);
            da.SetData(3, cph.Volume());
        }
    }

    // -------------------------------------------------------------------------
    // 7. Synthetic TN Granite -- deterministic synthetic DFN dataset for
    //    Tamil Nadu granite. Wraps SyntheticTnGraniteGenerator.WriteSampleSet.
    //    Also emits the fracture Mesh in-process so downstream BCO components
    //    can run without re-reading the PLY from disk.
    // -------------------------------------------------------------------------
    [Algorithm("Synthetic joint-set DFN generator", "ISRM Suggested Methods + Priest 1993 joint-set DFN", WikiPath = "wiki/index/references.md")]
    [Algorithm("Block theory key-block backtrack", "Goodman & Shi 1985, Block Theory and Its Application to Rock Engineering, Prentice-Hall", WikiPath = "wiki/index/references.md")]
    [DesignApplication(
        "Generate a deterministic synthetic discrete fracture network  for Tamil Nadu granite (three joint sets: NE-...",
        DesignFlow.TopDown)]
    public sealed class FrahanSyntheticTnGraniteComponent : FrahanComponentBase
    {
        public FrahanSyntheticTnGraniteComponent()
            : base(
                "Frahan Synthetic TN Granite", "TnGran",
                "Generate a deterministic synthetic discrete fracture network " +
                "for Tamil Nadu granite (three joint sets: NE-SW, NW-SE, " +
                "sub-horizontal bedding). Outputs a CSV of 2D traces at " +
                "z=midheight + a PLY of 3D fracture polygons + the fracture " +
                "Mesh in-process. Lets you regression-test BlockCutOpt " +
                "without a field dataset. Implements synthetic joint-set DFN generation (ISRM/Priest 1993; Goodman & Shi 1985).",
                "Frahan", "Block")
        { }

        public override Guid ComponentGuid =>
            new Guid("F2D0BC16-1234-4F2D-A0B0-7E60CADA15B6");

        public override GH_Exposure Exposure => Frahan.GH.Attributes.LabConfig.EffectiveExposure(ComponentGuid, GH_Exposure.primary);

        protected override Bitmap Icon => IconProvider.Load("Stratigraphy.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddBoxParameter("Bench", "B", "Bench bounding box (m).", GH_ParamAccess.item);
            p.AddIntegerParameter("Seed", "S", "Reproducibility seed.", GH_ParamAccess.item, 12345);
            p.AddTextParameter("CSV Path", "Csv", "Output trace CSV path.", GH_ParamAccess.item);
            p.AddTextParameter("PLY Path", "Ply", "Output fracture-polygon PLY path.", GH_ParamAccess.item);
            p.AddBooleanParameter("Write Files", "W", "False = compute in memory only.", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Fractures", "F", "In-process fracture Mesh (consumable by BCO components).", GH_ParamAccess.item);
            p.AddIntegerParameter("Plane Count", "Np", "Number of fracture planes generated.", GH_ParamAccess.item);
            p.AddIntegerParameter("Trace Count", "Nt", "Number of 2D traces at z=midheight.", GH_ParamAccess.item);
            p.AddIntegerParameter("Triangle Count", "Ntri", "Number of triangles in the PLY.", GH_ParamAccess.item);
            p.AddTextParameter("CSV Written", "Co", "CSV file path actually written (empty when W=false).", GH_ParamAccess.item);
            p.AddTextParameter("PLY Written", "Po", "PLY file path actually written (empty when W=false).", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var box = Box.Empty;
            int seed = 12345;
            string csv = null, ply = null;
            bool write = true;
            if (!da.GetData(0, ref box) || !box.IsValid)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid bench."); return; }
            da.GetData(1, ref seed);
            da.GetData(2, ref csv); da.GetData(3, ref ply);
            da.GetData(4, ref write);
            if (write && (string.IsNullOrWhiteSpace(csv) || string.IsNullOrWhiteSpace(ply)))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "CSV and PLY paths required when Write=true."); return; }

            var bench = GhBlockCutOptInterop.BoxToBbox(box);
            var jointSets = SyntheticTnGraniteGenerator.TamilNaduGraniteJointSets();
            var planes = JointSetDfnGenerator.Generate(jointSets, bench, seed);
            var mesh = JointSetDfnPlyEmitter.Emit(planes, bench);

            int csvCount = 0;
            string csvOut = string.Empty, plyOut = string.Empty;
            if (write)
            {
                try
                {
                    var stats = SyntheticTnGraniteGenerator.WriteSampleSet(csv, ply, bench, seed, jointSets);
                    csvCount = stats.CsvTraceCount;
                    csvOut = csv; plyOut = ply;
                }
                catch (Exception ex)
                { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Write failed: {ex.Message}"); return; }
            }
            else
            {
                var traces = SyntheticTnGraniteGenerator.ProjectPlanesToMidZ(planes, bench);
                csvCount = traces.Count;
            }

            var rhMesh = GhBlockCutOptInterop.PlyToRhinoMesh(mesh);
            da.SetData(0, rhMesh);
            da.SetData(1, planes.Count);
            da.SetData(2, csvCount);
            da.SetData(3, mesh.TriangleCount);
            da.SetData(4, csvOut);
            da.SetData(5, plyOut);
        }
    }
}
