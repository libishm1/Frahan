#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Quarry.BlockCutOpt;
using Frahan.Masonry.Quarry.Ingestion;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Quarry
{
    // =========================================================================
    // Layer 1 ingestion components: GPR + GeoFractNet.
    //
    // GprRadargramReader: reads a Frahan-format CSV traces + picks pair (no
    // SEG-Y / DZT / RD3 here -- those convert externally via RGPR per wiki
    // paper 20 section 3).
    //
    // GeoFractNetInference: reads a CSV of pre-computed fracture-plane
    // predictions (offline). net48 cannot host PyTorch directly, so the live-
    // inference step runs externally; this component picks up the result.
    // =========================================================================

    [RelatedComponent("Frahan > Ingest > GPR File Loader",
        Reason = "SUPERSEDED BY: GPR File Loader — native multi-format ingest (CSV / SEG-Y / RD3 / DT1 / DZT / IDS .dt), no external conversion needed.")]
    [RelatedComponent("Frahan > Quarry > GPR Fracture Extract",
        Reason = "SUPERSEDED BY: GPR Fracture Extract — full processing chain (f-k migration + Hilbert energy + continuity) with stone/frequency presets.")]
    [DesignApplication(
        "Read a Frahan-format GPR radargram (traces CSV + optional  picks CSV)",
        DesignFlow.Bridges)]
    public sealed class FrahanGprRadargramReaderComponent : FrahanComponentBase
    {
        public FrahanGprRadargramReaderComponent()
            : base(
                "GPR Radargram Reader", "GprRead",
                "SUPERSEDED BY: GPR File Loader + GPR Fracture Extract, which read " +
                "vendor formats natively and run the validated processing chain. " +
                "Kept loadable for old canvases. " +
                "Read a Frahan-format GPR radargram (traces CSV + optional " +
                "picks CSV). Coordinates in metres. SEG-Y / DZT / RD3 must " +
                "be converted externally (RGPR).",
                "Frahan", "Quarry")
        { }

        public override Guid ComponentGuid =>
            new Guid("F7A12001-0001-4F2D-A0B0-7E60CADA17B1");

        public override GH_Exposure Exposure => Frahan.GH.Attributes.LabConfig.EffectiveExposure(ComponentGuid, GH_Exposure.hidden);

        protected override Bitmap Icon => IconProvider.Load("GprIngest.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Id", "I", "Radargram identifier.", GH_ParamAccess.item, "scan-1");
            p.AddTextParameter("Traces CSV", "T", "Path to traces CSV (x,y,dz,a0,a1,...).", GH_ParamAccess.item);
            p.AddTextParameter("Picks CSV", "P", "Path to picks CSV (x,y,depth,conf,label). Empty = none.", GH_ParamAccess.item, string.Empty);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Radargram", "R", "GprRadargram object.", GH_ParamAccess.item);
            p.AddPointParameter("Trace XY", "TXY", "One point per trace at (x, y, 0).", GH_ParamAccess.list);
            p.AddPointParameter("Pick Points", "Pk", "One point per pick at (x, y, -depth).", GH_ParamAccess.list);
            p.AddNumberParameter("Pick Confidence", "C", "Confidence per pick (0..1).", GH_ParamAccess.list);
            p.AddIntegerParameter("Trace Count", "Nt", "Number of traces.", GH_ParamAccess.item);
            p.AddIntegerParameter("Pick Count", "Np", "Number of picks.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            string id = "scan-1", tracesPath = null, picksPath = string.Empty;
            da.GetData(0, ref id);
            if (!da.GetData(1, ref tracesPath) || string.IsNullOrWhiteSpace(tracesPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Traces CSV path is required.");
                return;
            }
            da.GetData(2, ref picksPath);

            GprRadargram radargram;
            try
            {
                radargram = GprRadargramReader.Load(id, tracesPath, picksPath);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Load failed: {ex.Message}");
                return;
            }

            var tracePts = new List<Point3d>(radargram.TraceCount);
            for (int i = 0; i < radargram.TraceCount; i++)
            {
                var t = radargram.Traces[i];
                tracePts.Add(new Point3d(t.X, t.Y, 0.0));
            }
            var pickPts = new List<Point3d>(radargram.PickCount);
            var pickConfs = new List<double>(radargram.PickCount);
            for (int i = 0; i < radargram.PickCount; i++)
            {
                var p = radargram.Picks[i];
                pickPts.Add(new Point3d(p.X, p.Y, -p.DepthMetres));
                pickConfs.Add(p.Confidence);
            }

            da.SetData(0, new GH_ObjectWrapper(radargram));
            da.SetDataList(1, tracePts);
            da.SetDataList(2, pickPts);
            da.SetDataList(3, pickConfs);
            da.SetData(4, radargram.TraceCount);
            da.SetData(5, radargram.PickCount);
        }
    }

    [DesignApplication(
        "Load pre-computed GeoFractNet fracture predictions from CSV  and emit a BlockCutOpt-ready fracture Mesh cli...",
        DesignFlow.Bridges)]
    public sealed class FrahanGeoFractNetInferenceComponent : FrahanComponentBase
    {
        public FrahanGeoFractNetInferenceComponent()
            : base(
                "GeoFractNet Inference", "GFNInfer",
                "Load pre-computed GeoFractNet fracture predictions from CSV " +
                "and emit a BlockCutOpt-ready fracture Mesh clipped to a bench " +
                "AABB. Inference itself runs externally (net48 cannot host " +
                "PyTorch).",
                "Frahan", "Quarry")
        { }

        public override Guid ComponentGuid =>
            new Guid("F7A12002-0001-4F2D-A0B0-7E60CADA17B2");

        public override GH_Exposure Exposure => Frahan.GH.Attributes.LabConfig.EffectiveExposure(ComponentGuid, GH_Exposure.primary);

        protected override Bitmap Icon => IconProvider.Load("DefectMap.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("CSV Path", "P", "Path to GeoFractNet predictions CSV.", GH_ParamAccess.item);
            p.AddBoxParameter("Bench AABB", "B", "Bounding box to clip fracture planes to.", GH_ParamAccess.item);
            p.AddNumberParameter("Min Confidence", "C", "Drop predictions below this confidence (0..1).", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Fractures", "F", "Fracture mesh ready for BlockCutOpt.", GH_ParamAccess.item);
            p.AddPlaneParameter("Planes", "Pl", "One Rhino Plane per fracture.", GH_ParamAccess.list);
            p.AddNumberParameter("Confidence", "C", "Per-fracture confidence.", GH_ParamAccess.list);
            p.AddIntegerParameter("Set Id", "S", "Per-fracture set id.", GH_ParamAccess.list);
            p.AddIntegerParameter("Triangle Count", "Nt", "Triangles in the fracture mesh.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            string path = null;
            Box box = Box.Empty;
            double minConf = 0.0;
            if (!da.GetData(0, ref path) || string.IsNullOrWhiteSpace(path))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "CSV path is required.");
                return;
            }
            if (!da.GetData(1, ref box) || !box.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Valid bench AABB is required.");
                return;
            }
            da.GetData(2, ref minConf);
            if (minConf < 0) minConf = 0;
            if (minConf > 1) minConf = 1;

            IReadOnlyList<GeoFractNetFracture> predictions;
            try
            {
                predictions = GeoFractNetMaskReader.Load(path, minConf);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Load failed: {ex.Message}");
                return;
            }

            var planes = new List<FracturePlane>(predictions.Count);
            for (int i = 0; i < predictions.Count; i++) planes.Add(predictions[i].Plane);

            var bench = GhBlockCutOptInterop.BoxToBbox(box);
            var ply = JointSetDfnPlyEmitter.Emit(planes, bench);
            var mesh = GhBlockCutOptInterop.PlyToRhinoMesh(ply);

            var rhPlanes = new List<Plane>(predictions.Count);
            var confs = new List<double>(predictions.Count);
            var sets = new List<int>(predictions.Count);
            foreach (var p in predictions)
            {
                var origin = new Point3d(p.Plane.PointX, p.Plane.PointY, p.Plane.PointZ);
                var normal = new Vector3d(p.Plane.NormalX, p.Plane.NormalY, p.Plane.NormalZ);
                rhPlanes.Add(new Plane(origin, normal));
                confs.Add(p.Confidence);
                sets.Add(p.SetId);
            }

            da.SetData(0, mesh);
            da.SetDataList(1, rhPlanes);
            da.SetDataList(2, confs);
            da.SetDataList(3, sets);
            da.SetData(4, ply.TriangleCount);
        }
    }
}
