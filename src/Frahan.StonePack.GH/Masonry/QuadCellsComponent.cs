#nullable disable
using System;
using System.Linq;
using Frahan.GH;
using Frahan.Masonry.Vault;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.StonePack.GH.Masonry
{
    // =========================================================================
    // Quad Cells — the quad grid IS the masonry cell decomposition. Converts a
    // (thrust-aligned) quad mesh into Cells/Frames/Columnness for Vault Voussoir
    // Moulds -> Vault Stone Fit & Trim, with the tuning knobs Libish dialled in
    // the 2026-07-02 Güell/three-prong sessions exposed as inputs:
    // joint-gap Shrink, columnness z-band, and the 2x column subdivision.
    // =========================================================================
    public sealed class QuadCellsComponent : FrahanComponentBase
    {
        public QuadCellsComponent()
            : base("Quad Cells", "QuadCells",
                "Convert a (thrust-aligned) quad mesh into masonry cells: one shrunk cell polyline + frame " +
                "per quad, with columnness from a z-band and an optional finer subdivision on column faces " +
                "(validated: columns split 2x, shrink 0.92). Feed Cells/Frames/Columnness into Vault " +
                "Voussoir Moulds then Vault Stone Fit & Trim for a coursed ETH-stone rubble skin that " +
                "follows the thrust grid. Zero remeshing happens here - the quad mesh drives everything.",
                "Frahan", "Vault")
        {
        }

        public override Guid ComponentGuid => new Guid("B7A11500-0012-4A11-B500-0000000000B2");
        protected override System.Drawing.Bitmap Icon => Frahan.GH.IconProvider.Load("QuadCells.png");
        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Quad Mesh", "Q", "All-quad mesh (e.g. from Thrust Quad Remesh).", GH_ParamAccess.item);
            p.AddNumberParameter("Shrink", "S", "Joint gap: cell scale about its centre (v004: 0.92).", GH_ParamAccess.item, 0.92);
            p.AddNumberParameter("Column Z Lo", "Zl", "Below this z the face is fully column (columnness 1).", GH_ParamAccess.item, 0.9);
            p.AddNumberParameter("Column Z Hi", "Zh", "Above this z the face is fully vault (columnness 0).", GH_ParamAccess.item, 1.8);
            p.AddIntegerParameter("Column Split", "Cs", "Subdivision on column faces (columnness > 0.5), applied ONLY around the tube so course height matches the vault. 2 = validated 'columns twice as fine'. 1 = off.", GH_ParamAccess.item, 2);
            p.AddNumberParameter("Tube Angle", "Ta", "Column detector (deg): a low-z face counts as column only if its 1-ring normals spread beyond this angle (curved tube). Flat wall bases stay full-size footers.", GH_ParamAccess.item, 12.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddCurveParameter("Cells", "C", "Shrunk cell polylines (one per quad / sub-quad).", GH_ParamAccess.list);
            p.AddPlaneParameter("Frames", "F", "Cell frames (origin = cell centre, Z = face normal).", GH_ParamAccess.list);
            p.AddNumberParameter("Columnness", "Cc", "0 = vault, 1 = column (drives mould depth dV -> dC).", GH_ParamAccess.list);
            p.AddTextParameter("Report", "Rp", "Summary.", GH_ParamAccess.item);
            p.AddNumberParameter("Inner Limit", "Il", "Per-cell max INWARD mould offset (0 = unlimited). ~0.6 x local tube radius on columns: wire into Vault Voussoir Moulds' Inner Limit so opposite/adjacent column stones never interpenetrate.", GH_ParamAccess.list);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            Mesh mesh = null;
            double shrink = 0.92, zLo = 0.9, zHi = 1.8, tube = 12.0; int split = 2;
            if (!GhGuard.Item(this, da, 0, ref mesh, "Quad Mesh")) return;
            da.GetData(1, ref shrink); da.GetData(2, ref zLo); da.GetData(3, ref zHi); da.GetData(4, ref split);
            da.GetData(5, ref tube);
            if (zHi <= zLo) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Column Z Hi must exceed Column Z Lo."); return; }

            var r = VaultQuadCells.Build(mesh, shrink, zLo, zHi, split, tube);
            if (r.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No quad faces found (triangulated input?)."); return; }
            da.SetDataList(0, r.Cells);
            da.SetDataList(1, r.Frames);
            da.SetDataList(2, r.Columnness);
            da.SetData(3, $"{r.Count} cells from {mesh.Faces.QuadCount} quads ({r.SplitFaces} column faces split, circumference-only); shrink {shrink:0.00}, z-band [{zLo:0.0}..{zHi:0.0}].");
            da.SetDataList(4, r.InnerLimit);
            Message = r.Count + " cells";
        }
    }
}
