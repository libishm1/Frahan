#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Core.Voussoir;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.Voussoir;

// =============================================================================
// ArchVoussoirsComponent (GUID D5F10012)
//
// The geometry FRONT END of the Voussoir pipeline. Generates a stereotomic
// arch as N radial voussoir CELLS (8-vertex wedge solids), then emits a typed
// VoussoirAssembly that wires straight into Voussoir Stone Matcher (D5F10010)
// and the Rubble Evolved Fit + CGAL trim (example 21). Before this component
// the cells only existed inside a one-off generation script; now the whole
// top-down arch flow lives on the canvas.
//
// Radial bed-joint rule (Frezier 1737 Traite de stereotomie; Monge geometrie
// descriptive 1798): bed joints are normal to the intrados and, for a circular
// arch, point at the centre of curvature; each wedge turns the thrust aside.
// Profiles: Semicircular, Segmental, Pointed (equilateral), Catenary -- the
// wiki note "catenary/pointed are a drop-in change of the intrados curve" made
// real (wiki/research/stereotomy_voussoir_from_rubble.md).
// =============================================================================

[Algorithm("RadialVoussoirCells",
    "Frahan-original: intrados curve -> arc-length stations -> 8-vertex wedge solids with radial bed joints (extrados = intrados offset by ring thickness along the outward normal)",
    Note = "Implements the radial bed-joint rule; circular profiles give exact concentric extrados.")]
[Algorithm("Frezier1737 / Monge1798 stereotomy",
    "A.-F. Frezier, Traite de stereotomie 1737-39 (coined 'stereotomie'); G. Monge, Geometrie descriptive 1798 -- bed joints normal to the intrados / along lines of curvature",
    Note = "The geometric law the wedge cells obey.")]
[DesignApplication(
    "Generate a stereotomic voussoir arch (the cut-stone cells) to match and carve from quarry stone or rubble.",
    DesignFlow.TopDown,
    Precedent = "Frezier Traite de stereotomie 1737; Monge geometrie descriptive 1798; Rippmann-Block 2011 Digital Stereotomy; Voussoir-GH (food4rhino); Frahan example 21",
    Tolerance = "faceted cells (straight chords between arc-length stations); circular extrados exact; raise Count to reduce facet error; closed solids (Mesh.IsClosed)",
    CardSet = "wiki/research/stereotomy_voussoir_from_rubble.md")]
public sealed class ArchVoussoirsComponent : GH_Component
{
    public ArchVoussoirsComponent()
        : base("Arch Voussoirs", "ArchVous",
            "Generate a stereotomic arch as N radial voussoir cells (8-vertex " +
            "wedge solids; bed joints normal to the intrados). Profiles: " +
            "Semicircular / Segmental / Pointed / Catenary. Outputs the cut-stone " +
            "cells plus a typed VoussoirAssembly for Voussoir Stone Matcher + the " +
            "rubble match-and-trim (example 21). Grounded in " +
            "wiki/research/stereotomy_voussoir_from_rubble.md.",
            "Frahan", "Voussoir")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("D5F10012-ED9E-4ED9-A012-ED9EED9E0012");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap Icon => IconProvider.Load("StereotomyGenerate.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddIntegerParameter("Profile", "Pf",
            "Intrados profile: 0 = Semicircular, 1 = Segmental, 2 = Pointed " +
            "(equilateral), 3 = Catenary. Default 0.",
            GH_ParamAccess.item, 0);
        p.AddNumberParameter("Intrados Radius", "R",
            "Intrados radius (m). For Catenary this is the span. Default 2.0.",
            GH_ParamAccess.item, 2.0);
        p.AddNumberParameter("Ring Thickness", "t",
            "Radial thickness intrados->extrados (m). Default 0.55.",
            GH_ParamAccess.item, 0.55);
        p.AddNumberParameter("Width", "w",
            "Out-of-plane voussoir width (m). Default 0.6.",
            GH_ParamAccess.item, 0.6);
        p.AddIntegerParameter("Count", "N",
            "Number of voussoirs. Default 11.",
            GH_ParamAccess.item, 11);
        p.AddNumberParameter("Included Angle", "A",
            "Included angle for the Segmental profile (deg, 0..180). Ignored by " +
            "the other profiles. Default 120.",
            GH_ParamAccess.item, 120.0);
        p.AddNumberParameter("Rise", "Ri",
            "Apex rise for the Catenary profile (m). 0 = use Intrados Radius. " +
            "Ignored by the other profiles. Default 0.",
            GH_ParamAccess.item, 0.0);
        p.AddPointParameter("Base Point", "P",
            "Origin to translate the arch to (built in world XZ, width along Y, " +
            "springers on z=0). Default world origin.",
            GH_ParamAccess.item, Point3d.Origin);
        p[7].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Cells", "C",
            "The voussoir cut-stone cells (closed wedge solids), in install order.",
            GH_ParamAccess.list);
        p.AddGenericParameter("Assembly", "VA",
            "Typed VoussoirAssembly. Wire into Voussoir Stone Matcher (D5F10010).",
            GH_ParamAccess.item);
        p.AddPlaneParameter("Bed Planes", "Bp",
            "Per-voussoir lower bed-joint plane (radial). The springer's is the springing plane.",
            GH_ParamAccess.list);
        p.AddPointParameter("Centroids", "Ct",
            "Per-voussoir centroid.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Volumes", "V",
            "Per-voussoir volume (m^3).",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Keystone", "K",
            "Index of the keystone voussoir (nearest the apex).",
            GH_ParamAccess.item);
        p.AddCurveParameter("Intrados", "I",
            "The intrados (soffit) curve.",
            GH_ParamAccess.item);
        p.AddTextParameter("Report", "R",
            "Build summary (profile, span, rise, total volume, closedness).",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        int profile = 0;
        double radius = 2.0, thickness = 0.55, width = 0.6, angle = 120.0, rise = 0.0;
        int count = 11;
        Point3d basePt = Point3d.Origin;

        DA.GetData(0, ref profile);
        DA.GetData(1, ref radius);
        DA.GetData(2, ref thickness);
        DA.GetData(3, ref width);
        DA.GetData(4, ref count);
        DA.GetData(5, ref angle);
        DA.GetData(6, ref rise);
        DA.GetData(7, ref basePt);

        if (radius <= 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Intrados Radius must be positive."); return; }
        if (thickness <= 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Ring Thickness must be positive."); return; }
        if (width <= 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Width must be positive."); return; }
        if (count < 1) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Count must be >= 1."); return; }
        if (profile < 0 || profile > 3)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Profile out of range; using Semicircular (0).");
            profile = 0;
        }

        VoussoirCellResult result;
        try
        {
            result = VoussoirCellFactory.BuildArch(
                (ArchProfile)profile, radius, thickness, width, count, angle, rise, basePt);
        }
        catch (Exception e)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "BuildArch failed: " + e.Message);
            return;
        }

        if (!AllClosed(result.Cells))
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "One or more voussoir cells are not closed solids. Check the inputs.");

        DA.SetDataList(0, result.Cells);
        DA.SetData(1, new GH_ObjectWrapper(result.Assembly));
        DA.SetDataList(2, result.BedPlanes);
        DA.SetDataList(3, result.Centroids);
        DA.SetDataList(4, result.Volumes);
        DA.SetData(5, result.KeystoneIndex);
        DA.SetData(6, result.Intrados);
        DA.SetData(7, result.Report);
    }

    private static bool AllClosed(List<Mesh> meshes)
    {
        if (meshes == null) return false;
        foreach (var m in meshes) if (m == null || !m.IsClosed) return false;
        return true;
    }
}
