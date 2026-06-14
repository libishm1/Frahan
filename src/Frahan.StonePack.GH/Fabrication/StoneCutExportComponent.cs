#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using Frahan.Core.Fabrication;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.DocObjects;
using Rhino.FileIO;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Fabrication;

// =============================================================================
// StoneCutExportComponent — "Stone-Aware Cut Export".
//
// The fabrication-readiness wedge: CAM (EasySTONE imports .3dm natively,
// Alphacam, Breton Maestro, Lantek) consumes geometry but receives it as dumb
// shapes. Frahan owns the upstream stone-intelligence — bed/grain direction,
// finish, weight, kerf, quarry provenance. This component writes a .3dm where
// each cut piece sits on its own named layer with that metadata attached as
// namespaced object user-strings, so it survives the handoff instead of being
// re-keyed by hand on the shop floor.
//
// Writing is a filesystem side effect, so it is gated behind a Write toggle
// (default false), the same discipline as the async Run gates elsewhere.
// =============================================================================

[DesignApplication(
    "Write cut pieces (mesh / brep / curve / surface) to a .3dm with  stone metadata (bed direction, finish, wei...",
    DesignFlow.Bridges,
    Precedent = "Quarra MIT lecture SS1.2 'selective precision' + Borrowed Earth SS11 hand-finishing -- StoneCutMetadata schema",
    CardSet = "wiki/research/hitl_cards/br_stone_aware_export/")]
public sealed class StoneCutExportComponent : FrahanComponentBase
{
    public StoneCutExportComponent()
        : base("Stone-Aware Cut Export", "CutExport",
            "Write cut pieces (mesh / brep / curve / surface) to a .3dm with "
            + "stone metadata (bed direction, finish, weight, kerf, provenance) "
            + "attached per piece as namespaced user-strings + one layer per "
            + "piece, so CAM (EasySTONE, Alphacam, Breton, Lantek) keeps the "
            + "stone intelligence. Set Write = true to write the file.",
            "Frahan", "Fabricate")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D07A01-1A2B-4C3D-9E4F-5A6B7C8D9E01");
    protected override Bitmap Icon => IconProvider.Load("GcodeExport.png");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGeometryParameter("Geometry", "G", "Cut pieces: mesh / brep / curve / surface.", GH_ParamAccess.list);
        p.AddTextParameter("Piece Ids", "Id", "Per-piece id (parallel to Geometry). Auto S001.. if absent.", GH_ParamAccess.list);
        p[1].Optional = true;
        p.AddTextParameter("Stone", "St", "Stone / source applied to all pieces (e.g. 'TN Black Granite').", GH_ParamAccess.item);
        p[2].Optional = true;
        p.AddTextParameter("Finish", "Fi", "Finish applied to all (polished / honed / flamed / sandblasted).", GH_ParamAccess.item);
        p[3].Optional = true;
        p.AddVectorParameter("Bed Direction", "Bd", "Bed / grain direction (unit vector) applied to all.", GH_ParamAccess.item);
        p[4].Optional = true;
        p.AddNumberParameter("Weight kg", "W", "Per-piece weight in kg (parallel to Geometry).", GH_ParamAccess.list);
        p[5].Optional = true;
        p.AddNumberParameter("Kerf mm", "K", "Saw kerf in mm applied to all.", GH_ParamAccess.item);
        p[6].Optional = true;
        p.AddTextParameter("File Path", "Fp", "Output .3dm path.", GH_ParamAccess.item);
        p.AddBooleanParameter("Write", "Wr", "Set true to write the .3dm. False = dry run (reports only).", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("File Path", "Fp", "Path written (empty on dry run / failure).", GH_ParamAccess.item);
        p.AddIntegerParameter("Piece Count", "N", "Number of pieces exported.", GH_ParamAccess.item);
        p.AddTextParameter("Report", "R", "Export summary.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var goos = new List<IGH_GeometricGoo>();
        if (!da.GetDataList(0, goos) || goos.Count == 0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No geometry provided."); return; }

        var ids = new List<string>();
        var weights = new List<double>();
        string stone = null, finish = null, path = null;
        Vector3d bed = Vector3d.Unset; double kerf = double.NaN; bool write = false;
        da.GetDataList(1, ids);
        da.GetData(2, ref stone);
        da.GetData(3, ref finish);
        da.GetData(4, ref bed);
        da.GetDataList(5, weights);
        da.GetData(6, ref kerf);
        da.GetData(7, ref path);
        da.GetData(8, ref write);

        int n = goos.Count;
        if (ids.Count > 0 && ids.Count != n)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Piece Ids count ({ids.Count}) != geometry ({n}); auto-naming the rest.");
        if (weights.Count > 0 && weights.Count != n)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Weight count ({weights.Count}) != geometry ({n}); unmatched weights ignored.");

        double[] bedArr = bed.IsValid && !bed.IsZero ? new[] { bed.X, bed.Y, bed.Z } : null;

        if (!write)
        {
            da.SetData(0, string.Empty);
            da.SetData(1, n);
            da.SetData(2, $"Dry run: {n} piece(s) ready. Set Write = true to write '{path}'.");
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Write = false (dry run). Set Write = true to write the .3dm.");
            return;
        }
        if (string.IsNullOrWhiteSpace(path))
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No File Path provided."); return; }

        var f = new File3dm();
        int written = 0, skipped = 0;
        for (int i = 0; i < n; i++)
        {
            GeometryBase gb = ToGeometry(goos[i]);
            if (gb == null) { skipped++; continue; }

            string id = (ids.Count == n && !string.IsNullOrWhiteSpace(ids[i]))
                ? ids[i]
                : "S" + (i + 1).ToString("D3", CultureInfo.InvariantCulture);

            var meta = new StoneCutMetadata
            {
                PieceId = id,
                Stone = stone,
                Finish = finish,
                BedDirection = bedArr,
                WeightKg = (weights.Count == n) ? weights[i] : double.NaN,
                KerfMm = kerf,
            };

            var layer = new Layer { Name = "piece_" + id };
            int li = f.AllLayers.Count;   // Add appends; new layer's index is the prior count
            f.AllLayers.Add(layer);
            var attr = new ObjectAttributes { Name = id, LayerIndex = li };
            foreach (var kv in meta.ToUserStrings()) attr.SetUserString(kv.Key, kv.Value);

            if (AddGeometry(f, gb, attr)) written++;
            else skipped++;
        }

        try
        {
            var opts = new File3dmWriteOptions { Version = 7 };
            if (!f.Write(path, opts))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "File3dm.Write failed."); return; }
        }
        catch (Exception ex)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Write error: {ex.Message}"); return; }

        string report = $"Wrote {written} piece(s) to {Path.GetFileName(path)}"
            + (skipped > 0 ? $"; skipped {skipped} unsupported." : ".");
        da.SetData(0, path);
        da.SetData(1, written);
        da.SetData(2, report);
    }

    private static GeometryBase ToGeometry(IGH_GeometricGoo goo)
    {
        if (goo == null) return null;
        return goo.ScriptVariable() as GeometryBase;
    }

    private static bool AddGeometry(File3dm f, GeometryBase gb, ObjectAttributes attr)
    {
        switch (gb)
        {
            case Mesh m: f.Objects.AddMesh(m, attr); return true;
            case Brep b: f.Objects.AddBrep(b, attr); return true;
            case Extrusion e: f.Objects.AddExtrusion(e, attr); return true;
            case Curve c: f.Objects.AddCurve(c, attr); return true;
            default: return false;   // surfaces/points: convert upstream (rare for cut pieces)
        }
    }
}
