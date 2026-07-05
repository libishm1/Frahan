#nullable disable
using System;
using Grasshopper.Kernel;

namespace HotReloadLab
{
    // Standalone hot-reload test bed. No Frahan base class on purpose:
    // this project only exists to prove edit-save-resolve works without
    // restarting Rhino.
    public class HotLabComponent : GH_Component
    {
        public HotLabComponent()
            : base(
                "Hot Lab",
                "HotLab",
                "Hot-reload test bed. Attach VS Code to Rhino, edit SolveInstance, save, re-solve - the change appears WITHOUT restarting Rhino.",
                "Dev",
                "HotReload")
        {
        }

        public override Guid ComponentGuid => new Guid("B0B5C0DE-1AB5-4DEF-9E57-C0DE0DE0F00D");

        protected override System.Drawing.Bitmap Icon => null;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("A", "A", "First number", GH_ParamAccess.item, 2.0);
            pManager.AddNumberParameter("B", "B", "Second number", GH_ParamAccess.item, 3.0);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Result", "R", "A + B", GH_ParamAccess.item);
            pManager.AddTextParameter("Note", "N", "Note describing what ran", GH_ParamAccess.item);
        }

        // Hot-reload demo target: edit this method, save, re-solve on the
        // canvas. Try changing A + B to A * B and updating the note string.
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double a = 0.0;
            double b = 0.0;
            if (!DA.GetData(0, ref a)) return;
            if (!DA.GetData(1, ref b)) return;

            double result = a + b;
            string note = "HotLab v1: A+B";

            DA.SetData(0, result);
            DA.SetData(1, note);
        }
    }
}
