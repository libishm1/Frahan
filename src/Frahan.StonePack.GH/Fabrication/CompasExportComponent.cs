#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Frahan.Core.Fabrication;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.Fabrication;

// =============================================================================
// CompasExportComponent (D5F10054, Frahan > Fabricate)
//
// Bridge a Frahan masonry/vault assembly to the COMPAS ecosystem (compas,
// compas_assembly / compas_model, compas_fab). Exports the blocks (as vertex/
// face meshes), placement/robot frames (point + x/y axis), and contact interfaces
// to a small, stable JSON, plus a COMPAS-side Python loader. Interop, not
// compete: a user can then run compas_cra's equilibrium solver or compas_fab's
// robot stack on the same assembly. (COMPAS owns the same Kao-2022 CRA Frahan
// ports; this hands the model over rather than duplicating the framework.)
// =============================================================================

[Algorithm("COMPAS bridge", "Neutral JSON (blocks/frames/interfaces) + compas-side Python loader",
    Note = "Interop with compas_assembly / compas_cra / compas_fab; version-robust neutral schema, not compas internal serialization.")]
[RelatedComponent("Frahan > Vault > Vault Shell CRA", Reason = "Source of the blocks + stability model to hand to compas_cra.")]
[RelatedComponent("Frahan > Fabricate > Planes To Robot Targets", Reason = "Frames this exports as compas Frames for compas_fab.")]
public sealed class CompasExportComponent : FrahanComponentBase
{
    public CompasExportComponent()
        : base("COMPAS Export", "Compas",
            "Bridge a Frahan assembly to COMPAS (compas_assembly / compas_cra / compas_fab). Exports blocks (vertex/" +
            "face meshes) + placement/robot frames to a stable JSON plus a Python loader, so a user can run COMPAS's " +
            "own CRA solver or robot stack. Interop, not compete. Set Write = true to write the file.",
            "Frahan", "Fabricate")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F10054-ED9E-4ED9-A054-ED9EED9E0054");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("CompasExport.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGeometryParameter("Blocks", "B", "Block geometry (mesh or brep) -- the voussoirs / stones.", GH_ParamAccess.list);
        p.AddTextParameter("Ids", "Id", "Per-block id (parallel to Blocks). Auto block_001.. if absent.", GH_ParamAccess.list);
        p[1].Optional = true;
        p.AddPlaneParameter("Frames", "F", "Placement / robot frames (exported as compas Frames).", GH_ParamAccess.list);
        p[2].Optional = true;
        p.AddTextParameter("Units", "U", "Units label (e.g. m, mm).", GH_ParamAccess.item, "m");
        p.AddBooleanParameter("Loader", "L", "Also write the companion frahan_compas_loader.py next to the JSON.", GH_ParamAccess.item, true);
        p.AddTextParameter("File Path", "Fp", "Output .json path.", GH_ParamAccess.item);
        p.AddBooleanParameter("Write", "Wr", "Set true to write. False = dry run (counts + loader text only).", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("File Path", "Fp", "Path written (empty on dry run / failure).", GH_ParamAccess.item);
        p.AddTextParameter("Loader", "Py", "The COMPAS-side Python loader script.", GH_ParamAccess.item);
        p.AddIntegerParameter("Blocks", "Nb", "Blocks exported.", GH_ParamAccess.item);
        p.AddIntegerParameter("Frames", "Nf", "Frames exported.", GH_ParamAccess.item);
        p.AddTextParameter("Report", "Re", "Export summary.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var goos = new List<IGH_GeometricGoo>();
        var haveBlocks = da.GetDataList(0, goos) && goos.Count > 0;
        var ids = new List<string>(); da.GetDataList(1, ids);
        var frames = new List<Plane>(); da.GetDataList(2, frames);
        string units = "m"; da.GetData(3, ref units);
        bool loader = true; da.GetData(4, ref loader);
        string path = null; da.GetData(5, ref path);
        bool write = false; da.GetData(6, ref write);

        if (!haveBlocks && frames.Count == 0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Provide Blocks and/or Frames."); return; }

        var meshes = new List<Mesh>();
        foreach (var g in goos) { var m = ToMesh(g); if (m != null) meshes.Add(m); }

        da.SetData(1, CompasExporter.LoaderPy());

        if (!write)
        {
            string j = CompasExporter.BuildJson(meshes, ids.Count > 0 ? ids : null, frames, null, null, units, out int nb, out int nf, out int ni);
            da.SetData(0, string.Empty);
            da.SetData(2, nb); da.SetData(3, nf);
            da.SetData(4, $"Dry run: {nb} block(s), {nf} frame(s) ready. Set Write = true to write '{path}'.");
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Write = false (dry run). Set Write = true to write the JSON.");
            return;
        }
        if (string.IsNullOrWhiteSpace(path))
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No File Path provided."); return; }

        bool ok = CompasExporter.Write(path, meshes, ids.Count > 0 ? ids : null, frames, null, null, units, loader, out string report);
        if (!ok) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, report); da.SetData(4, report); return; }
        da.SetData(0, path);
        da.SetData(2, meshes.Count);
        da.SetData(3, frames.Count(f => f.IsValid));
        da.SetData(4, report);
    }

    private static Mesh ToMesh(IGH_GeometricGoo goo)
    {
        var g = goo?.ScriptVariable();
        if (g is Mesh m) return m;
        if (g is Brep b)
        {
            var mm = Mesh.CreateFromBrep(b, MeshingParameters.Default);
            if (mm == null || mm.Length == 0) return null;
            var joined = new Mesh(); foreach (var part in mm) joined.Append(part);
            return joined;
        }
        if (g is Rhino.Geometry.Surface s)
        {
            var mm = Mesh.CreateFromSurface(s, MeshingParameters.Default);
            return mm;
        }
        return null;
    }
}
