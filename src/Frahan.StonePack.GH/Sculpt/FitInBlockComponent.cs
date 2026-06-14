#nullable disable
using System;
using System.Drawing;
using System.Globalization;
using Frahan.Core.Sculpt;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Sculpt;

// =============================================================================
// FitInBlockComponent — "Fit In Block".
//
// Second half of the digital pointing machine: given an enlarged sculpture and
// an available raw block, does the block hold it (allowing a kerf / roughing
// margin)? Reports fit, per-axis clearance, and the largest scale that would
// still fit. v1 compares axis-aligned bounding extents matched largest-to-
// largest (best box-aligned orientation); OBB-exact orientation search is a
// later refinement. Optionally centres the sculpture inside the block.
// =============================================================================

[DesignApplication(
    "Check whether a raw block can hold a (enlarged) sculpture, allowing a  kerf/roughing margin",
    DesignFlow.TopDown,
    Precedent = "Quarra Two Horse Relief (Met) -- designed mesh fitted to a specific quarried block",
    CardSet = "wiki/research/hitl_cards/td_voussoir/")]
[Algorithm("Bounding-extents containment + max-scale fit", "Frahan-original",
    Note = "axis-aligned bounding extents matched largest-to-largest; OBB-exact orientation search deferred; not a published algorithm")]
public sealed class FitInBlockComponent : FrahanComponentBase
{
    public FitInBlockComponent()
        : base("Fit In Block", "FitBlock",
            "Check whether a raw block can hold a (enlarged) sculpture, allowing a "
            + "kerf/roughing margin. Reports fit, per-axis clearance, and the max "
            + "scale that still fits. v1 uses bounding extents matched largest-to-"
            + "largest; optionally centres the piece in the block. Frahan-original method.",
            "Frahan", "Sculpt")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D06A02-1A2B-4C3D-9E4F-5A6B7C8D9E02");
    protected override Bitmap Icon => IconProvider.Load("PackIntoBlock.png");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Sculpture", "S", "Sculpture mesh (e.g. from Enlarge Sculpture).", GH_ParamAccess.item);
        p.AddMeshParameter("Block", "B", "Raw block mesh (available stock).", GH_ParamAccess.item);
        p.AddNumberParameter("Margin", "Mg",
            "Clearance per side subtracted from the block (kerf + roughing allowance + handling).",
            GH_ParamAccess.item, 0.0);
        p.AddBooleanParameter("Place", "P",
            "Centre the sculpture inside the block (translation only in v1).",
            GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddBooleanParameter("Fits", "F", "True if the block holds the sculpture (with margin).", GH_ParamAccess.item);
        p.AddVectorParameter("Clearance", "C",
            "Per sorted-axis slack (block - sculpture), largest axis first. Negative = overflow.",
            GH_ParamAccess.item);
        p.AddNumberParameter("Max Scale To Fit", "Sf",
            "Largest uniform scale of the sculpture that still fits (>=1 means it already fits).",
            GH_ParamAccess.item);
        p.AddMeshParameter("Placed", "M", "Sculpture centred in the block (if Place = true).", GH_ParamAccess.item);
        p.AddTextParameter("Report", "R", "Human-readable fit summary.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh sculpt = null, block = null;
        double margin = 0.0; bool place = true;
        if (!da.GetData(0, ref sculpt) || sculpt == null || !sculpt.IsValid)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid sculpture mesh."); return; }
        if (!da.GetData(1, ref block) || block == null || !block.IsValid)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid block mesh."); return; }
        da.GetData(2, ref margin); da.GetData(3, ref place);

        BoundingBox sbb = sculpt.GetBoundingBox(true);
        BoundingBox bbb = block.GetBoundingBox(true);
        Vector3d se = sbb.Diagonal, be = bbb.Diagonal;

        FitResult fit = SculptureFitter.FitsInBlock(
            new[] { se.X, se.Y, se.Z }, new[] { be.X, be.Y, be.Z }, margin);

        var inv = CultureInfo.InvariantCulture;
        string report =
            $"Fits: {(fit.Fits ? "YES" : "NO")}\n"
            + $"Sculpture size: {se.X.ToString("0.##", inv)} x {se.Y.ToString("0.##", inv)} x {se.Z.ToString("0.##", inv)}\n"
            + $"Block size: {be.X.ToString("0.##", inv)} x {be.Y.ToString("0.##", inv)} x {be.Z.ToString("0.##", inv)} (margin {margin.ToString("0.##", inv)}/side)\n"
            + $"Clearance (sorted axes): {fit.Clearance[0].ToString("0.##", inv)}, {fit.Clearance[1].ToString("0.##", inv)}, {fit.Clearance[2].ToString("0.##", inv)}\n"
            + $"Max uniform scale to fit: {fit.MaxScaleToFit.ToString("0.###", inv)}\n"
            + "Align the sculpture's longest axis with the block's longest axis for this fit.";
        if (!fit.Fits)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"Sculpture does not fit; reduce scale by x{fit.MaxScaleToFit.ToString("0.###", inv)} or use a bigger block.");

        Mesh placed = null;
        if (place)
        {
            placed = sculpt.DuplicateMesh();
            placed.Transform(Transform.Translation(bbb.Center - sbb.Center));
        }

        da.SetData(0, fit.Fits);
        da.SetData(1, new Vector3d(fit.Clearance[0], fit.Clearance[1], fit.Clearance[2]));
        da.SetData(2, fit.MaxScaleToFit);
        da.SetData(3, placed);
        da.SetData(4, report);
    }
}
