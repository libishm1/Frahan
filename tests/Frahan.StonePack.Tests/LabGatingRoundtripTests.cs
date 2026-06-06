#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;
using Frahan.GH;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;

namespace Frahan.Tests;

// LabGatingRoundtripTests -- preservation contract verifier for the Lab-gating
// mechanism (v1_consolidated_plan §0.1, §4.3 item 4). Built 2026-05-30 as
// step 6 of the v1.0 execution order.
//
// For every GUID in LabConfig.LabGatedGuids, this test asserts:
//   (a) the GUID resolves to an actual concrete GH_Component class in the
//       Frahan.GH assembly (reflection by GUID);
//   (b) that class still instantiates without throwing (parameterless ctor
//       works -- the component is buildable);
//   (c) the instance carries a non-null icon resource (icon preserved);
//   (d) the resolved component's ComponentGuid equals the GUID in LabConfig
//       (stable GUID -- existing .gh documents still bind to this component).
//
// Together (a)-(d) prove that Lab-gating does not delete source, icon, or
// GUID. The mechanism is a visibility flag only.
//
// Step 6 ships LabConfig with an EMPTY allow-list. The test handles that
// gracefully as a no-op (passes with zero asserts) so the test suite is
// green from the moment the mechanism lands; step 9 populates the list and
// the test starts doing real preservation checks then.
//
// Skip pattern: if the GH/Rhino runtime is unavailable (rhcommon_c.dll,
// Grasshopper.dll, GH_IO.dll), reflection or instantiation will throw a
// native-loader exception. The headless test runner in Program.cs catches
// those via IsNativeRhinoException(...) and reports SKIP rather than FAIL,
// per the existing convention (Program.cs:1221).
static class LabGatingRoundtripTests
{
    public static void EveryLabGatedGuid_PreservesSourceIconAndGuid()
    {
        IReadOnlyCollection<Guid> gated = LabConfig.LabGatedGuids;
        if (gated == null || gated.Count == 0)
        {
            // Step 6 ships empty; step 9 populates. No-op until then.
            return;
        }

        // Build a GUID -> concrete-Type index over the Frahan.GH assembly.
        Assembly asm = typeof(EdgeMatchSolveComponent).Assembly;
        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex)
        {
            var ok = new List<Type>();
            foreach (var t in ex.Types) if (t != null) ok.Add(t);
            types = ok.ToArray();
        }

        var byGuid = new Dictionary<Guid, Type>();
        foreach (var t in types)
        {
            if (t == null || t.IsAbstract || t.IsGenericTypeDefinition) continue;
            if (!typeof(GH_Component).IsAssignableFrom(t)) continue;
            ConstructorInfo ctor = t.GetConstructor(Type.EmptyTypes);
            if (ctor == null) continue;

            GH_Component comp;
            try { comp = (GH_Component)ctor.Invoke(null); }
            catch { continue; } // needs Rhino/GH runtime; the outer SKIP handler covers this

            Guid g;
            try { g = comp.ComponentGuid; }
            catch { continue; }

            if (g == Guid.Empty) continue;
            // First-writer-wins. ComponentGuidUniquenessTests enforces uniqueness
            // separately; here we just need any resolving class per GUID.
            if (!byGuid.ContainsKey(g)) byGuid[g] = t;
        }

        var problems = new List<string>();
        foreach (Guid guid in gated)
        {
            // (a) resolve to a concrete class
            if (!byGuid.TryGetValue(guid, out Type type))
            {
                problems.Add($"Lab-gated GUID {guid} does not resolve to any concrete GH_Component in Frahan.GH (source removed?)");
                continue;
            }

            // (b) instantiate without throwing
            GH_Component instance;
            try
            {
                instance = (GH_Component)type.GetConstructor(Type.EmptyTypes).Invoke(null);
            }
            catch (Exception ex)
            {
                problems.Add($"Lab-gated GUID {guid} ({type.Name}) ctor threw {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            // (c) carries a non-null icon (Icon_24x24 is the GH_Component override
            //     for the 24x24 ribbon icon; it is protected, so reflect on it).
            object icon;
            try
            {
                PropertyInfo iconProp = typeof(GH_Component).GetProperty(
                    "Icon_24x24",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                icon = iconProp != null ? iconProp.GetValue(instance) : null;
            }
            catch (Exception ex)
            {
                problems.Add($"Lab-gated GUID {guid} ({type.Name}) icon read threw {ex.GetType().Name}: {ex.Message}");
                continue;
            }
            if (icon == null)
            {
                problems.Add($"Lab-gated GUID {guid} ({type.Name}) has null Icon_24x24 (icon resource missing?)");
                continue;
            }

            // (d) GUID stable -- same value the LabConfig has
            Guid actual;
            try { actual = instance.ComponentGuid; }
            catch (Exception ex)
            {
                problems.Add($"Lab-gated GUID {guid} ({type.Name}) ComponentGuid read threw {ex.GetType().Name}: {ex.Message}");
                continue;
            }
            if (actual != guid)
            {
                problems.Add($"Lab-gated GUID {guid} ({type.Name}) reports ComponentGuid {actual} (GUID drift)");
            }
        }

        if (problems.Count > 0)
            throw new InvalidOperationException(
                $"Lab-gating preservation failures ({gated.Count} gated):\n  "
                + string.Join("\n  ", problems));
    }
}
