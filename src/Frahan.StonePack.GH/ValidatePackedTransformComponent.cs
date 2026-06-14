using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH;

[DesignApplication(
    "Debugs StonePack transforms by comparing source mesh + transform against placed mesh output",
    DesignFlow.BottomUp,
    Precedent = "Frahan-original transform-validation diagnostic")]
public sealed class ValidatePackedTransformComponent : FrahanComponentBase
{
    public ValidatePackedTransformComponent()
        : base("Validate Packed Transform", "PackXformCheck",
            "Debugs StonePack transforms by comparing source mesh + transform against placed mesh output.",
            "Frahan", "3D Packing")
    {
    }

    public override Guid ComponentGuid => new Guid("2AE8987D-83E5-471C-B82F-8A19EC57492A");
    protected override Bitmap? Icon => IconProvider.Load("StabilityCheck.png");
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Source Meshes", "S", "Original source meshes sent into the packer.", GH_ParamAccess.list);
        pManager.AddMeshParameter("Placed Meshes", "P", "Placed meshes output by the packer.", GH_ParamAccess.list);
        pManager.AddTransformParameter("Transforms", "T", "Transforms output by the packer.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Source Indices", "Src", "Source index output by the packer.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Tolerance", "Tol", "Validation tolerance in model units.", GH_ParamAccess.item, 1e-6);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Transformed Sources", "TS", "Source meshes after applying the supplied transforms.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Max Vertex Error", "Err", "Maximum same-index vertex distance between transformed source and placed mesh.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Bounding Box Error", "BBox", "Maximum min/max bounding-box corner distance.", GH_ParamAccess.list);
        pManager.AddBooleanParameter("Valid", "OK", "True when vertex and bounding-box errors are within tolerance.", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "Info", "Validation summary.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var sourceMeshes = new List<Mesh>();
        var placedMeshes = new List<Mesh>();
        var transforms = new List<Transform>();
        var sourceIndices = new List<int>();
        var tolerance = 1e-6;

        if (!da.GetDataList(0, sourceMeshes)) return;
        if (!da.GetDataList(1, placedMeshes)) return;
        if (!da.GetDataList(2, transforms)) return;
        da.GetDataList(3, sourceIndices);
        da.GetData(4, ref tolerance);

        tolerance = Math.Max(0, tolerance);
        var count = Math.Min(placedMeshes.Count, transforms.Count);
        if (sourceIndices.Count > 0)
        {
            count = Math.Min(count, sourceIndices.Count);
        }
        else
        {
            count = Math.Min(count, sourceMeshes.Count);
        }

        var transformedSources = new List<Mesh>();
        var vertexErrors = new List<double>();
        var boxErrors = new List<double>();
        var valid = new List<bool>();
        var passed = 0;

        for (var i = 0; i < count; i++)
        {
            var sourceIndex = sourceIndices.Count > 0 ? sourceIndices[i] : i;
            if (sourceIndex < 0 || sourceIndex >= sourceMeshes.Count || sourceMeshes[sourceIndex] == null || placedMeshes[i] == null)
            {
                transformedSources.Add(new Mesh());
                vertexErrors.Add(double.NaN);
                boxErrors.Add(double.NaN);
                valid.Add(false);
                continue;
            }

            var transformed = sourceMeshes[sourceIndex].DuplicateMesh();
            transformed.Transform(transforms[i]);
            transformedSources.Add(transformed);

            var vertexError = ComputeMaxVertexError(transformed, placedMeshes[i]);
            var boxError = ComputeBoundingBoxError(transformed, placedMeshes[i]);
            var ok = !double.IsNaN(vertexError)
                && !double.IsNaN(boxError)
                && vertexError <= tolerance
                && boxError <= tolerance;

            if (ok)
            {
                passed++;
            }

            vertexErrors.Add(vertexError);
            boxErrors.Add(boxError);
            valid.Add(ok);
        }

        da.SetDataList(0, transformedSources);
        da.SetDataList(1, vertexErrors);
        da.SetDataList(2, boxErrors);
        da.SetDataList(3, valid);
        da.SetData(4, $"Validated {count} transform pairs | Passed: {passed} | Failed: {count - passed}");
    }

    private static double ComputeMaxVertexError(Mesh a, Mesh b)
    {
        if (a.Vertices.Count != b.Vertices.Count)
        {
            return double.NaN;
        }

        var max = 0.0;
        for (var i = 0; i < a.Vertices.Count; i++)
        {
            var pa = a.Vertices[i];
            var pb = b.Vertices[i];
            var dx = pa.X - pb.X;
            var dy = pa.Y - pb.Y;
            var dz = pa.Z - pb.Z;
            max = Math.Max(max, Math.Sqrt(dx * dx + dy * dy + dz * dz));
        }

        return max;
    }

    private static double ComputeBoundingBoxError(Mesh a, Mesh b)
    {
        var boxA = a.GetBoundingBox(true);
        var boxB = b.GetBoundingBox(true);
        if (!boxA.IsValid || !boxB.IsValid)
        {
            return double.NaN;
        }

        return Math.Max(boxA.Min.DistanceTo(boxB.Min), boxA.Max.DistanceTo(boxB.Max));
    }
}
