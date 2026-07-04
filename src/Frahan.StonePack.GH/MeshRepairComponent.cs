using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using Frahan.GH.Attributes;
using Frahan.Surface;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH;

/// <summary>
/// Run the <see cref="MeshRepair.RepairAll"/> pipeline against one or more
/// input meshes. Returns the repaired copy plus the per-step trace as text.
/// Does not mutate the caller's meshes.
///
/// Spec 11 + runbook section 16.6 component family "Frahan Mesh Repair".
/// </summary>
[Algorithm("Mesh-repair recipe", "Botsch, Kobbelt, Pauly, Alliez, Levy 2010 Polygon Mesh Processing (AK Peters / CRC Press), ISBN 978-1568814261", Note = "Standard weld / cull-degenerate / heal-naked-edges / unify-normals pipeline")]
[DesignApplication(
    "Run the Frahan mesh-repair pipeline (cull degenerate / weld / cull  unused / heal naked edges / unify norma...",
    DesignFlow.Bridges,
    Precedent = "Botsch Kobbelt Pauly Alliez Levy 2010 Polygon Mesh Processing (ISBN 978-1568814261)",
    Tolerance = "watertight + manifold output; Euler characteristic correct; vertex count within 10 % of input",
    CardSet = "wiki/research/hitl_cards/br_mesh_sanitize/")]
public sealed class MeshRepairComponent : FrahanComponentBase
{
    public MeshRepairComponent()
        : base("Mesh Repair", "MeshFix",
            "Run the Frahan mesh-repair pipeline (cull degenerate / weld / cull " +
            "unused / heal naked edges / unify normals / recompute normals) and " +
            "return the repaired mesh plus a per-step trace. [Botsch et al. 2010]",
            "Frahan", "Mesh")
    {
    }

    public override Guid ComponentGuid => new Guid("AB12C00A-1A2B-4C3D-9E4F-5A6B7C8D9E0A");
    protected override Bitmap? Icon => IconProvider.Load("PoissonReconstruct.png");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Meshes", "M",
            "Mesh(es) to repair. Originals are not mutated.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Weld Angle", "Wa",
            "Weld vertices whose face normals fall within this angle (radians). " +
            "Default = pi/8 (~22.5 deg).",
            GH_ParamAccess.item, Math.PI / 8.0);
        pManager.AddNumberParameter("Heal Distance", "Hd",
            "Maximum naked-edge gap to heal (model units). Default = 0.001.",
            GH_ParamAccess.item, 0.001);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Repaired", "R",
            "Repaired mesh per input.", GH_ParamAccess.list);
        pManager.AddTextParameter("Trace", "T",
            "Per-mesh repair trace (one multi-line string per input mesh).",
            GH_ParamAccess.list);
        pManager.AddIntegerParameter("Skipped", "Sk",
            "Number of meshes skipped (null input or pipeline threw).",
            GH_ParamAccess.item);
        pManager.AddTextParameter("Summary", "S", "One-line summary.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var meshes = new List<Mesh>();
        double weldAngle = Math.PI / 8.0;
        double healDist = 0.001;

        if (!da.GetDataList(0, meshes) || meshes.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one mesh required.");
            return;
        }
        da.GetData(1, ref weldAngle);
        da.GetData(2, ref healDist);

        if (weldAngle < 0) weldAngle = 0;
        if (healDist < 0) healDist = 0;

        var repaired = new List<Mesh>(meshes.Count);
        var traces = new List<string>(meshes.Count);
        int skipped = 0;

        for (int i = 0; i < meshes.Count; i++)
        {
            var mesh = meshes[i];
            if (mesh == null)
            {
                skipped++;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Mesh {i}: null, skipped.");
                continue;
            }

            try
            {
                var (fixedMesh, trace) = MeshRepair.RepairAll(mesh, weldAngle, healDist);
                repaired.Add(fixedMesh);

                var sb = new StringBuilder();
                sb.AppendLine($"Mesh {i}:");
                for (int t = 0; t < trace.Count; t++)
                    sb.AppendLine("  " + trace[t]);
                traces.Add(sb.ToString());
            }
            catch (Exception ex)
            {
                skipped++;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Mesh {i}: pipeline threw: {ex.Message}");
            }
        }

        da.SetDataList(0, repaired);
        da.SetDataList(1, traces);
        da.SetData(2, skipped);
        da.SetData(3, $"MeshRepair: {repaired.Count} repaired, {skipped} skipped, " +
            $"weldAngle={weldAngle * 180.0 / Math.PI:0.##} deg, healDist={healDist}");
    }
}
