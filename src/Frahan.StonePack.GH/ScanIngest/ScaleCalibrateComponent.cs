#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Core.ScanIngest;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.ScanIngest;

// =============================================================================
// ScaleCalibrateComponent — Phase F3 GH wrapper for ScaleCalibration.
// Subcategory: Mesh (Scan Ingest row).
// =============================================================================

[Algorithm("Known-distance scale calibration", "Frahan-original", Note = "Single-ratio uniform scale = known length / measured length")]
[DesignApplication(
    "Derive a uniform scale Transform from a measured reference  curve in the scan and the curve's real-world le...",
    DesignFlow.Bridges,
    Precedent = "Frahan-original known-distance scale calibration")]
public sealed class ScaleCalibrateComponent : FrahanComponentBase
{
    public ScaleCalibrateComponent()
        : base("Scan Scale Calibrate", "ScaleCal",
            "Derive a uniform scale Transform from a measured reference " +
            "curve in the scan and the curve's real-world length. " +
            "Optionally apply the transform to a list of input meshes. " +
            "Closes the unit-ambiguity gap in photogrammetry / scan workflows. " +
            "Frahan-original method.",
            "Frahan", "Mesh")
    {
    }

    public override Guid ComponentGuid => new Guid("B1C2D3A4-2001-4F5E-A6B7-C8D9E0F12345");
    protected override Bitmap Icon => IconProvider.Load("CalibrationBoard.png");
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddCurveParameter("Measured Curve", "C",
            "A curve in the scan frame whose real-world length is known " +
            "(e.g. picked between two corners of a printed scale bar).",
            GH_ParamAccess.item);
        p.AddNumberParameter("Reference Length", "L",
            "The real-world length the curve should represent, in the " +
            "target unit system (Z output).",
            GH_ParamAccess.item);
        p.AddMeshParameter("Meshes", "M",
            "Optional scan meshes to scale. When wired, the Scaled Meshes " +
            "output carries the transformed copies; otherwise that output " +
            "is empty.",
            GH_ParamAccess.list);
        p[2].Optional = true;
        p.AddTextParameter("Units", "U",
            "Free-form unit label for the report output (\"m\", \"mm\", " +
            "\"ft\", etc.). Math is unit-agnostic; this is display only.",
            GH_ParamAccess.item, "m");
        p[3].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTransformParameter("Scale Transform", "X",
            "Uniform scale transform centred at the world origin. Apply " +
            "to any scan-frame geometry to bring it into the target frame.",
            GH_ParamAccess.item);
        p.AddNumberParameter("Scale Factor", "F",
            "Uniform scale factor = Reference Length / Measured Length.",
            GH_ParamAccess.item);
        p.AddNumberParameter("Measured Length", "Lm",
            "Length of the measured curve in source-frame units.",
            GH_ParamAccess.item);
        p.AddMeshParameter("Scaled Meshes", "Ms",
            "Input meshes after applying the scale transform. Empty when " +
            "no Meshes input is wired.",
            GH_ParamAccess.list);
        p.AddTextParameter("Report", "R",
            "Human-readable summary line.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Curve measured = null;
        double refLength = 0.0;
        var inputMeshes = new List<Mesh>();
        string units = "m";

        if (!da.GetData(0, ref measured) || measured == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Measured Curve is required.");
            return;
        }
        if (!da.GetData(1, ref refLength))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Reference Length is required.");
            return;
        }
        da.GetDataList(2, inputMeshes);
        da.GetData(3, ref units);

        ScaleCalibrationResult result;
        try
        {
            result = ScaleCalibration.SolveFromCurve(measured, refLength, units);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        var scaled = new List<Mesh>(inputMeshes.Count);
        for (int i = 0; i < inputMeshes.Count; i++)
        {
            if (inputMeshes[i] == null) { scaled.Add(null); continue; }
            var copy = inputMeshes[i].DuplicateMesh();
            copy.Transform(result.ScaleTransform);
            scaled.Add(copy);
        }

        da.SetData(0, result.ScaleTransform);
        da.SetData(1, result.ScaleFactor);
        da.SetData(2, result.MeasuredLength);
        da.SetDataList(3, scaled);
        da.SetData(4, result.ToString());

        // Sanity warning: scale factors very far from 1 usually mean
        // wrong reference units or wrong curve picked.
        if (result.ScaleFactor < 1e-4 || result.ScaleFactor > 1e4)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"Scale factor {result.ScaleFactor:0.######} is far from 1 — " +
                "verify the reference length and curve units.");
        }
    }
}
