#nullable disable
using System;
using Grasshopper.Kernel;

namespace Frahan.GH
{
    // =========================================================================
    // AsyncResolve — delivery kick for background workers (P1 hardening,
    // 2026-07-02). The async components store the finished payload BEFORE
    // kicking a re-solve, so a swallowed ScheduleSolution exception could
    // strand a completed result behind a frozen progress label. Kick tries
    // the normal schedule first and falls back to a UI-thread expire; only if
    // BOTH fail (document actually gone) is there nothing left to deliver to.
    // =========================================================================
    internal static class AsyncResolve
    {
        public static void Kick(GH_Document doc, Guid instanceGuid)
        {
            if (doc == null) return;
            try
            {
                doc.ScheduleSolution(10, d =>
                {
                    if (d?.FindComponent(instanceGuid) is GH_Component c) c.ExpireSolution(true);
                });
                return;
            }
            catch { /* fall through to the UI-thread route */ }
            try
            {
                Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
                {
                    try { if (doc.FindComponent(instanceGuid) is GH_Component c) c.ExpireSolution(true); }
                    catch { }
                }));
            }
            catch { /* document disposed - no component left to deliver to */ }
        }
    }
}
