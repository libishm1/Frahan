#nullable disable
using System;
using System.Collections.Generic;
using Frahan.GH;
using Frahan.Masonry.Vault;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.StonePack.GH.Masonry
{
    // =========================================================================
    // Trim Shell by Curves — Libish's cut_curves workflow as a component
    // (2026-07-02): draw plan curves where the shell should END (one per end/
    // side), feed the shell + curves, get the architecturally trimmed shell.
    // Validated live on the Güell portico: the curve-cut shell fed the CRA
    // geometry experiments (guell_curvecut_shell_v001). Sides auto-detected
    // from each curve's position; the Removed output shows exactly what was
    // cut (bake it red to check before committing).
    // =========================================================================
    public sealed class TrimShellByCurvesComponent : FrahanComponentBase
    {
        public TrimShellByCurvesComponent()
            : base("Trim Shell by Curves", "CurveTrim",
                "Trim a vault shell along user-drawn PLAN curves (top view): each curve is read as a " +
                "(y -> x) boundary and the shell faces beyond it are removed — the 'draw a curve, say " +
                "cut' workflow. Curves near the low-x end cut the low side, near the high-x end the " +
                "high side (auto-detected). Outputs the kept shell, the removed piece (bake to verify), " +
                "and a report. Feed the kept shell to Thrust Quad Remesh -> Vault Shell CRA to test " +
                "whether the trim improves stability.",
                "Frahan", "Vault")
        {
        }

        public override Guid ComponentGuid => new Guid("B7A11500-0013-4A11-B500-0000000000B3");
        protected override System.Drawing.Bitmap Icon => Frahan.GH.IconProvider.Load("TrimShellByCurves.png");
        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Shell", "M", "Vault shell mesh to trim.", GH_ParamAccess.item);
            p.AddCurveParameter("Cut Curves", "C", "Plan cut curves (e.g. drawn on a 'cut_curves' layer, viewed from Top).", GH_ParamAccess.list);
            p.AddIntegerParameter("Samples", "S", "Samples per curve for the plan boundary table.", GH_ParamAccess.item, 200);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Trimmed", "T", "The kept shell (feed to Thrust Quad Remesh / Shell CRA).", GH_ParamAccess.item);
            p.AddMeshParameter("Removed", "X", "What was cut away (bake red to verify the cut).", GH_ParamAccess.item);
            p.AddTextParameter("Report", "Rp", "Summary.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            Mesh shell = null;
            var curves = new List<Curve>();
            int samples = 200;
            if (!GhGuard.Item(this, da, 0, ref shell, "Shell")) return;
            if (!da.GetDataList(1, curves) || curves.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No cut curves supplied; passing the shell through untrimmed.");
                da.SetData(0, shell);
                da.SetData(2, "No cut curves; shell unchanged.");
                return;
            }
            da.GetData(2, ref samples);

            var r = ShellCurveTrimmer.Trim(shell, curves, Math.Max(8, samples));
            if (r.Kept == null || r.Kept.Faces.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Trim removed everything (check curve positions/orientation): " + r.Note);
                return;
            }
            da.SetData(0, r.Kept);
            da.SetData(1, r.Removed);
            da.SetData(2, r.Note);
            Message = r.RemovedFaces + " faces cut";
        }
    }
}
