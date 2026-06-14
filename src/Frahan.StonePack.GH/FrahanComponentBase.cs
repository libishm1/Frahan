#nullable disable
using System;
using System.Collections.Generic;
using Grasshopper.Kernel;

namespace Frahan.GH
{
    // =============================================================================
    // FrahanComponentBase - resilient base for synchronous Frahan components.
    //
    // Goal: a null, empty, or wrong-typed input must never turn a component RED with
    // a stack-trace balloon. It should degrade to a clean orange Warning and produce
    // no output, so an incomplete or mis-wired canvas reads as "waiting", not "broken".
    //
    // Components implement SolveSafe(da) instead of SolveInstance(da). The base seals
    // SolveInstance and runs SolveSafe inside a guard: any uncaught exception (null
    // dereference, invalid cast, bad geometry) becomes a Warning carrying the message.
    // Deliberate hard errors still work: call AddRuntimeMessage(Error, ...) inside
    // SolveSafe for conditions the user genuinely must fix.
    //
    // Async components (GH_TaskCapableComponent, AsyncScanComponent) keep their own
    // base and are intentionally NOT migrated to this one.
    // =============================================================================
    public abstract class FrahanComponentBase : GH_Component
    {
        protected FrahanComponentBase(string name, string nickname, string description,
            string category, string subCategory)
            : base(name, nickname, description, category, subCategory)
        {
        }

        protected sealed override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                SolveSafe(da);
            }
            catch (Exception ex)
            {
                // Recoverable: never crash the component red on a data/input problem.
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Skipped - input or data issue: " + Trim(ex));
            }
        }

        // Implement the component logic here instead of SolveInstance.
        protected abstract void SolveSafe(IGH_DataAccess da);

        private static string Trim(Exception ex)
        {
            var m = (ex == null ? null : ex.Message) ?? "unexpected error";
            return m.Length > 240 ? m.Substring(0, 240) + "..." : m;
        }
    }

    // Optional guard helpers. Use inside SolveSafe to fail soft on missing data:
    //   if (!GhGuard.Item(this, da, 0, ref mesh, "Mesh")) return;   // warns + bails on no data
    // These return false (and add a Warning) instead of letting a null propagate.
    public static class GhGuard
    {
        public static bool Item<T>(GH_Component c, IGH_DataAccess da, int index, ref T value, string label = null)
        {
            T tmp = default;
            bool ok;
            try { ok = da.GetData(index, ref tmp); }
            catch { ok = false; }
            if (!ok || tmp == null)
            {
                c.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "No data for input " + (label ?? ("#" + index)) + ".");
                return false;
            }
            value = tmp;
            return true;
        }

        public static bool List<T>(GH_Component c, IGH_DataAccess da, int index, List<T> value, string label = null)
        {
            bool ok;
            try { ok = da.GetDataList(index, value); }
            catch { ok = false; }
            if (!ok || value == null || value.Count == 0)
            {
                c.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "No data for input " + (label ?? ("#" + index)) + ".");
                return false;
            }
            return true;
        }
    }
}
