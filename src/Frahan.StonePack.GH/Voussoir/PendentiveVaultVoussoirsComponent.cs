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
// PendentiveVaultVoussoirsComponent (GUID D5F10013)
//
// The vault counterpart of Arch Voussoirs. Generates a pendentive (sail) dome
// -- a sphere over a square plan -- tessellated on a grid into voussoir cells
// along the sphere's lines of curvature (Monge), each extruded radially by the
// shell thickness. Emits a typed VoussoirAssembly for the same match-and-trim
// flow (example 22).
//
// A pendentive dome springs from the four corners of the square (the
// pendentives) up to the apex on one continuous spherical surface. Monge's
// rule for stable mortarless assembly: bed joints follow the lines of
// curvature (meridians and parallels). Reference: Rippmann-Block 2011 Digital
// Stereotomy; Block Research Group RhinoVAULT; example 22.
// =============================================================================

[Algorithm("PendentiveDomeCells",
    "Frahan-original: square plan grid lifted onto a sphere (z=sqrt(R^2-x^2-y^2)) then extruded radially by the shell thickness -> 8-vertex cells along lines of curvature",
    Note = "Sphere-over-square pendentive (sail) dome; cells are radial frusta between two concentric sphere patches.")]
[Algorithm("Monge lines-of-curvature tessellation",
    "G. Monge: orient vault joints along the lines of curvature of the surface; for a sphere these are meridians and parallels",
    Note = "The geometric law the vault cells obey.")]
[DesignApplication(
    "Generate a pendentive (sail) vault of voussoir cells to match and carve from quarry stone or rubble.",
    DesignFlow.TopDown,
    Precedent = "Monge geometrie descriptive 1798; Rippmann-Block 2011 Digital Stereotomy; Block Research Group RhinoVAULT; Frahan example 22",
    Tolerance = "faceted cells (flat patches between grid stations); raise grid U/V to reduce facet error; requires 2*halfWidth^2 < R^2 so corners lie on the sphere; closed solids (Mesh.IsClosed)",
    CardSet = "wiki/research/stereotomy_voussoir_from_rubble.md")]
public sealed class PendentiveVaultVoussoirsComponent : GH_Component
{
    public PendentiveVaultVoussoirsComponent()
        : base("Pendentive Vault Voussoirs", "VaultVous",
            "Generate a pendentive (sail) dome (sphere over a square) tessellated " +
            "on a grid into voussoir cells along the sphere's lines of curvature, " +
            "each extruded radially by the shell thickness. Outputs the cut-stone " +
            "cells plus a typed VoussoirAssembly for Voussoir Stone Matcher + the " +
            "rubble match-and-trim (example 22). Grounded in " +
            "wiki/research/stereotomy_voussoir_from_rubble.md.",
            "Frahan", "Voussoir")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("D5F10013-ED9E-4ED9-A013-ED9EED9E0013");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap Icon => IconProvider.Load("Voussoir.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddNumberParameter("Sphere Radius", "R",
            "Sphere radius (m). Default 2.5.",
            GH_ParamAccess.item, 2.5);
        p.AddNumberParameter("Square Half Width", "h",
            "Half the side of the square plan (m). Must satisfy 2*h^2 < R^2 so the " +
            "corners lie on the sphere. Default 1.6.",
            GH_ParamAccess.item, 1.6);
        p.AddNumberParameter("Shell Thickness", "t",
            "Radial shell thickness intrados->extrados (m). Default 0.4.",
            GH_ParamAccess.item, 0.4);
        p.AddIntegerParameter("Grid U", "U",
            "Cells across the plan in U. Default 6.",
            GH_ParamAccess.item, 6);
        p.AddIntegerParameter("Grid V", "V",
            "Cells across the plan in V. Default 6.",
            GH_ParamAccess.item, 6);
        p.AddBooleanParameter("Drop To Ground", "D",
            "Translate so the springing corners rest on z=0. Default true.",
            GH_ParamAccess.item, true);
        p.AddPointParameter("Base Point", "P",
            "Origin to translate the vault to. Default world origin.",
            GH_ParamAccess.item, Point3d.Origin);
        p[6].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Cells", "C",
            "The voussoir cut-stone cells (closed solids), one per grid cell.",
            GH_ParamAccess.list);
        p.AddGenericParameter("Assembly", "VA",
            "Typed VoussoirAssembly. Wire into Voussoir Stone Matcher (D5F10010).",
            GH_ParamAccess.item);
        p.AddPlaneParameter("Bed Planes", "Bp",
            "Per-cell intrados (bed) plane: centre + outward sphere radial.",
            GH_ParamAccess.list);
        p.AddPointParameter("Centroids", "Ct",
            "Per-cell centroid.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Volumes", "V",
            "Per-cell volume (m^3).",
            GH_ParamAccess.list);
        p.AddTextParameter("Report", "R",
            "Build summary (R, span, thickness, grid, springing/apex, total volume).",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        double radius = 2.5, half = 1.6, thickness = 0.4;
        int gridU = 6, gridV = 6;
        bool drop = true;
        Point3d basePt = Point3d.Origin;

        DA.GetData(0, ref radius);
        DA.GetData(1, ref half);
        DA.GetData(2, ref thickness);
        DA.GetData(3, ref gridU);
        DA.GetData(4, ref gridV);
        DA.GetData(5, ref drop);
        DA.GetData(6, ref basePt);

        if (radius <= 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Sphere Radius must be positive."); return; }
        if (half <= 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Square Half Width must be positive."); return; }
        if (thickness <= 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Shell Thickness must be positive."); return; }
        if (gridU < 1 || gridV < 1) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Grid U and V must be >= 1."); return; }
        if (2.0 * half * half >= radius * radius)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Square corners fall off the sphere (2*h^2 >= R^2). Increase Sphere " +
                "Radius or decrease Square Half Width.");
            return;
        }

        VoussoirCellResult result;
        try
        {
            result = VoussoirCellFactory.BuildPendentiveVault(
                radius, half, thickness, gridU, gridV, drop, basePt);
        }
        catch (Exception e)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "BuildPendentiveVault failed: " + e.Message);
            return;
        }

        if (!AllClosed(result.Cells))
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "One or more vault cells are not closed solids. Check the inputs.");

        DA.SetDataList(0, result.Cells);
        DA.SetData(1, new GH_ObjectWrapper(result.Assembly));
        DA.SetDataList(2, result.BedPlanes);
        DA.SetDataList(3, result.Centroids);
        DA.SetDataList(4, result.Volumes);
        DA.SetData(5, result.Report);
    }

    private static bool AllClosed(List<Mesh> meshes)
    {
        if (meshes == null) return false;
        foreach (var m in meshes) if (m == null || !m.IsClosed) return false;
        return true;
    }
}
