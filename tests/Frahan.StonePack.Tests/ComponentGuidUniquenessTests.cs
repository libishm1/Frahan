#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;
using Frahan.GH;
using Frahan.GH.Sculpt;
using Frahan.GH.Fabrication;
using Grasshopper.Kernel;

namespace Frahan.Tests;

// Repo-wide regression guard. On 2026-05-29 a new component (Load E57 Cloud)
// reused an existing ComponentGuid (Read LAS Cloud); two GH_Components sharing a
// GUID collide on the ribbon and break .gh deserialization, and the C# build
// does NOT catch it. This test reflects over every concrete GH_Component in the
// Frahan.GH assembly, reads each ComponentGuid, and asserts global uniqueness.
//
// Components whose ctor needs the Rhino/GH native runtime (or has no
// parameterless ctor) are skipped per-type rather than failing the whole test;
// the ones that instantiate pure-managed (the vast majority) are enough to catch
// a duplicate GUID like the one above.
static class ComponentGuidUniquenessTests
{
    public static void AllGhComponents_HaveUniqueGuids()
    {
        Assembly asm = typeof(EdgeMatchSolveComponent).Assembly;
        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex)
        {
            var ok = new List<Type>();
            foreach (var t in ex.Types) if (t != null) ok.Add(t);
            types = ok.ToArray();
        }

        var byGuid = new Dictionary<Guid, string>();
        var problems = new List<string>();
        int scanned = 0;
        foreach (var t in types)
        {
            if (t == null || t.IsAbstract || t.IsGenericTypeDefinition) continue;
            if (!typeof(GH_Component).IsAssignableFrom(t)) continue;
            ConstructorInfo ctor = t.GetConstructor(Type.EmptyTypes);
            if (ctor == null) continue;

            GH_Component comp;
            try { comp = (GH_Component)ctor.Invoke(null); }
            catch { continue; } // needs Rhino/GH runtime; skip this one

            Guid g;
            try { g = comp.ComponentGuid; }
            catch (Exception ex) { problems.Add($"{t.Name}: ComponentGuid threw {ex.GetType().Name}: {ex.Message}"); continue; }

            if (g == Guid.Empty) { problems.Add($"{t.Name}: empty GUID"); continue; }
            scanned++;
            if (byGuid.TryGetValue(g, out string other))
                problems.Add($"GUID {g} shared by {other} and {t.Name}");
            else
                byGuid[g] = t.Name;
        }

        if (problems.Count > 0)
            throw new InvalidOperationException(
                $"Component GUID problems ({scanned} scanned, {byGuid.Count} unique):\n  "
                + string.Join("\n  ", problems));
    }

    public static void NewOutputBranchComponents_GuidsParseAndAreUnique()
    {
        Guid enlarge = new EnlargeSculptureComponent().ComponentGuid;
        Guid fit = new FitInBlockComponent().ComponentGuid;
        Guid export = new StoneCutExportComponent().ComponentGuid;
        if (enlarge == Guid.Empty || fit == Guid.Empty || export == Guid.Empty)
            throw new InvalidOperationException("Output-branch component GUID parsed to Empty");
        if (enlarge == fit || fit == export || enlarge == export)
            throw new InvalidOperationException("Output-branch components share a GUID");
    }
}
