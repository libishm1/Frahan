#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Rhino;
using Rhino.Commands;
using Rhino.Input;

namespace Frahan.Rhino;

// =============================================================================
// FrahanWhichAlgorithm -- reflects on the loaded Frahan.StonePack.gha and
// prints the [Algorithm(...)] citations + [RelatedComponent(...)] cross-refs
// for every tagged GH component.
//
// Late-bound reflection: the .rhp project does NOT reference Frahan.StonePack.GH.
// Instead we LoadFrom the .gha at runtime and compare attribute types by
// FullName string. Keeps the .rhp dependency-light; tolerates a missing or
// stale .gha gracefully.
//
// Usage:
//   _FrahanWhichAlgorithm                    -> prompt for component class name
//   _FrahanWhichAlgorithm _All               -> dump every tagged component
//   _FrahanWhichAlgorithm _Untagged          -> list components that lack [Algorithm]
//   _FrahanWhichAlgorithm <ClassName>        -> dump one component by class name
// =============================================================================

public sealed class FrahanWhichAlgorithmCommand : Command
{
    public override string EnglishName => "FrahanWhichAlgorithm";

    private const string AlgorithmAttrFullName = "Frahan.GH.Attributes.AlgorithmAttribute";
    private const string RelatedComponentAttrFullName = "Frahan.GH.Attributes.RelatedComponentAttribute";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var ghPath = FindGhPath();
        if (ghPath == null)
        {
            RhinoApp.WriteLine("FrahanWhichAlgorithm: could not locate Frahan.StonePack.gha in %APPDATA%/Grasshopper/Libraries/.");
            return Result.Failure;
        }

        Assembly ghAsm;
        try
        {
            ghAsm = Assembly.LoadFrom(ghPath);
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"FrahanWhichAlgorithm: failed to load {ghPath}: {ex.Message}");
            return Result.Failure;
        }

        string filter = null;
        var gs = new global::Rhino.Input.Custom.GetString();
        gs.SetCommandPrompt("Class name (or _All, _Untagged)");
        gs.SetDefaultString("_All");
        gs.AcceptNothing(true);
        gs.AddOption("All");
        gs.AddOption("Untagged");
        var r = gs.Get();
        if (r == global::Rhino.Input.GetResult.Option)
        {
            filter = gs.Option().EnglishName == "Untagged" ? "_Untagged" : "_All";
        }
        else if (r == global::Rhino.Input.GetResult.String)
        {
            filter = gs.StringResult();
        }
        else if (r == global::Rhino.Input.GetResult.Nothing)
        {
            filter = "_All";
        }
        else
        {
            return Result.Cancel;
        }

        var components = LoadGhComponents(ghAsm).ToList();
        if (components.Count == 0)
        {
            RhinoApp.WriteLine("FrahanWhichAlgorithm: no GH_Component types found in the .gha.");
            return Result.Failure;
        }

        if (filter == "_Untagged")
        {
            DumpUntagged(components);
        }
        else if (filter == "_All" || string.IsNullOrWhiteSpace(filter))
        {
            DumpAllTagged(components);
        }
        else
        {
            DumpOne(components, filter);
        }
        return Result.Success;
    }

    private static string FindGhPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData)) return null;
        var path = Path.Combine(appData, "Grasshopper", "Libraries",
            "Frahan.StonePack.MeshHeightmap", "Frahan.StonePack.gha");
        return File.Exists(path) ? path : null;
    }

    private static IEnumerable<Type> LoadGhComponents(Assembly asm)
    {
        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
        foreach (var t in types)
        {
            if (t == null) continue;
            if (t.IsAbstract || !t.IsClass) continue;
            // Walk the inheritance chain looking for GH_Component
            for (var b = t.BaseType; b != null; b = b.BaseType)
            {
                if (b.Name == "GH_Component" || b.Name == "GH_TaskCapableComponent`1")
                {
                    yield return t;
                    break;
                }
            }
        }
    }

    private static List<Attribute> AlgorithmAttrs(Type t) =>
        t.GetCustomAttributes(false)
            .OfType<Attribute>()
            .Where(a => a.GetType().FullName == AlgorithmAttrFullName)
            .ToList();

    private static List<Attribute> RelatedAttrs(Type t) =>
        t.GetCustomAttributes(false)
            .OfType<Attribute>()
            .Where(a => a.GetType().FullName == RelatedComponentAttrFullName)
            .ToList();

    private static string Prop(object obj, string name)
    {
        var p = obj.GetType().GetProperty(name);
        if (p == null) return null;
        var v = p.GetValue(obj);
        return v == null ? null : v.ToString();
    }

    private static void DumpAllTagged(List<Type> components)
    {
        int tagged = 0;
        foreach (var t in components.OrderBy(x => x.Name))
        {
            var algs = AlgorithmAttrs(t);
            var rels = RelatedAttrs(t);
            if (algs.Count == 0 && rels.Count == 0) continue;
            tagged++;
            DumpOneType(t, algs, rels);
        }
        RhinoApp.WriteLine($"FrahanWhichAlgorithm: {tagged} of {components.Count} components carry [Algorithm] or [RelatedComponent].");
    }

    private static void DumpUntagged(List<Type> components)
    {
        var untagged = components
            .Where(t => AlgorithmAttrs(t).Count == 0 && RelatedAttrs(t).Count == 0)
            .OrderBy(t => t.Name)
            .ToList();
        foreach (var t in untagged)
        {
            RhinoApp.WriteLine($"  (untagged) {t.FullName}");
        }
        RhinoApp.WriteLine($"FrahanWhichAlgorithm: {untagged.Count} of {components.Count} components are untagged.");
    }

    private static void DumpOne(List<Type> components, string nameFragment)
    {
        var matches = components
            .Where(t => t.Name.IndexOf(nameFragment, StringComparison.OrdinalIgnoreCase) >= 0
                     || (t.FullName != null && t.FullName.IndexOf(nameFragment, StringComparison.OrdinalIgnoreCase) >= 0))
            .ToList();
        if (matches.Count == 0)
        {
            RhinoApp.WriteLine($"FrahanWhichAlgorithm: no component matches '{nameFragment}'.");
            return;
        }
        foreach (var t in matches)
        {
            var algs = AlgorithmAttrs(t);
            var rels = RelatedAttrs(t);
            DumpOneType(t, algs, rels);
        }
    }

    private static void DumpOneType(Type t, List<Attribute> algs, List<Attribute> rels)
    {
        RhinoApp.WriteLine($"=== {t.FullName} ===");
        if (algs.Count == 0)
        {
            RhinoApp.WriteLine("  (no [Algorithm] attributes)");
        }
        foreach (var a in algs)
        {
            var name = Prop(a, "Name") ?? "?";
            var citation = Prop(a, "Citation") ?? "?";
            var doi = Prop(a, "Doi");
            var wiki = Prop(a, "WikiPath");
            var note = Prop(a, "Note");
            RhinoApp.WriteLine($"  [Algorithm] {name} -- {citation}");
            if (!string.IsNullOrEmpty(doi))  RhinoApp.WriteLine($"      DOI:  {doi}");
            if (!string.IsNullOrEmpty(wiki)) RhinoApp.WriteLine($"      Wiki: {wiki}");
            if (!string.IsNullOrEmpty(note)) RhinoApp.WriteLine($"      Note: {note}");
        }
        if (rels.Count == 0)
        {
            RhinoApp.WriteLine("  (no [RelatedComponent] cross-refs)");
        }
        foreach (var r in rels)
        {
            var path = Prop(r, "RibbonPath") ?? "?";
            var reason = Prop(r, "Reason");
            RhinoApp.WriteLine($"  [RelatedComponent] {path}");
            if (!string.IsNullOrEmpty(reason)) RhinoApp.WriteLine($"      Reason: {reason}");
        }
    }
}
