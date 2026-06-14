#nullable disable
using System;
using System.Drawing;
using System.IO;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;

namespace Frahan.GH.ScanIngest;

// =============================================================================
// LoadMetashapeDenseCloudComponent (MRAC Proposal 4)
//
// Recognise-and-guide stub for Agisoft Metashape's proprietary `.oc3` dense-cloud
// format. Mirrors the existing E57 "bridge" pattern but does NOT yet attempt a
// real parse: the binary format is undocumented, and the Frahan team's superior
// reference is the existing E57 out-of-process Python-worker pattern
// (`project_e57_ingest_component` memory). v1 ships the stub; v2 elevates to a
// real reader via `metashape.exe -r script.py` invocation when a customer
// needs it.
//
// What this component does:
//   - Recognises `.oc3` by extension.
//   - Verifies the file exists + reports its size.
//   - Emits guidance text directing the user to open the `.psx` in Metashape
//     and `File > Export > Export Dense Cloud` to PLY, then load with the
//     existing `Load Cloud` component.
//
// Per `feedback_example_files_over_reimplementation`: do NOT compete with
// Metashape's own export; ingest after the user converts.
// =============================================================================

[Algorithm("File-extension recognition + conversion-guidance emission",
    "Frahan-original; stub pattern mirrors E57 / GSF bridge components",
    Note = "v1 recognise-and-guide only; v2 will add Metashape Python worker per project_e57_ingest_component pattern")]
[DesignApplication(
    "Recognise Agisoft Metashape .oc3 dense-cloud files and emit conversion guidance pointing the user to export the .oc3 to PLY via Metashape, then load via the existing Load Cloud component.",
    DesignFlow.Bridges,
    Precedent = "Frahan E57 stub bridge pattern (project_e57_ingest_component memory); Agisoft Metashape File > Export > Export Dense Cloud workflow",
    Tolerance = "exact file-existence + extension match; no numerical computation",
    CardSet = "Template-General/outputs/2026-05-31/hitl_cards/scan_to_cut_pipeline/cards/01_load_quarry_scan_ply.md (related)")]
public sealed class LoadMetashapeDenseCloudComponent : FrahanComponentBase
{
    public LoadMetashapeDenseCloudComponent()
        : base("Load Metashape Dense Cloud", "LoadOc3",
            "Recognise a Metashape .oc3 dense-cloud file and emit conversion " +
            "guidance. v1 does NOT parse the binary format; the user must " +
            "export the .oc3 to PLY in Metashape first, then load with the " +
            "Load Cloud component. v2 will add a Metashape Python worker " +
            "following the E57 out-of-process pattern.",
            "Frahan", "Mesh")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("D5F10040-ED9E-4ED9-A040-ED9EED9E0040");

    public override GH_Exposure Exposure => GH_Exposure.quarternary;

    protected override Bitmap Icon => IconProvider.Load("Downsample.png"); // placeholder

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("Oc3 File", "F",
            "Path to an Agisoft Metashape .oc3 dense-cloud file. v1 recognises " +
            "the format and emits conversion guidance; no point data is " +
            "extracted.",
            GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddBooleanParameter("Recognised", "R",
            "True when the file exists and has a .oc3 extension.",
            GH_ParamAccess.item);
        p.AddNumberParameter("File Size MB", "Sz",
            "Size of the .oc3 in megabytes (informational).",
            GH_ParamAccess.item);
        p.AddTextParameter("Guidance", "G",
            "Conversion guidance: open the .psx in Metashape, File > Export > " +
            "Export Dense Cloud to PLY, then load with the Load Cloud component.",
            GH_ParamAccess.list);
    }

    protected override void SolveSafe(IGH_DataAccess DA)
    {
        string path = null;
        if (!DA.GetData(0, ref path)) return;

        if (string.IsNullOrWhiteSpace(path))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Oc3 File path is empty.");
            return;
        }

        bool recognised = false;
        double sizeMb = 0;

        if (!File.Exists(path))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "File not found: " + path);
        }
        else if (!path.EndsWith(".oc3", StringComparison.OrdinalIgnoreCase))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "File does not have a .oc3 extension. Expected an Agisoft " +
                "Metashape dense-cloud file.");
        }
        else
        {
            recognised = true;
            var info = new FileInfo(path);
            sizeMb = info.Length / (1024.0 * 1024.0);
        }

        var guidance = new System.Collections.Generic.List<string>
        {
            "Frahan v1 does NOT parse .oc3 directly (Agisoft proprietary " +
                "binary format, undocumented).",
            "To use this dense cloud:",
            "  1. Open the associated .psx in Agisoft Metashape.",
            "  2. File > Export > Export Dense Cloud > save as .ply (binary).",
            "  3. Load the .ply via Frahan's Load Cloud or Load PLY Mesh component.",
            "Reference dossier: wiki/research/mrac_workshop_2023/exercise_dossier.md " +
                "Proposal 4 (Frahan e57 worker pattern is the superior reference for v2)."
        };

        DA.SetData(0, recognised);
        DA.SetData(1, sizeMb);
        DA.SetDataList(2, guidance);
    }
}
