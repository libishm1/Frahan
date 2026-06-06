using System.Reflection;
using Frahan.NativeBridge;
using Rhino;
using Rhino.Commands;

namespace Frahan.Rhino;

public sealed class FrahanStonePackAboutCommand : Command
{
    public override string EnglishName => "FrahanStonePackAbout";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var asm = typeof(FrahanStonePackAboutCommand).Assembly;
        var version = asm.GetName().Version?.ToString() ?? "unknown";

        RhinoApp.WriteLine($"Frahan StonePack {version}: Rhino/Grasshopper environment for deterministic 2D, 3D, and surface packing.");

        var geometry = NativeBackendLoader.ChooseGeometryBackend();
        var packing = NativeBackendLoader.ChoosePackingBackend();
        RhinoApp.WriteLine($"  Geometry backend: {geometry.Name} {geometry.Version}");
        RhinoApp.WriteLine($"  Packing backend:  {packing.Name} {packing.Version}");

        var probe = NativeBackendLoader.LastGeometryProbe;
        if (probe != null && probe.SearchPathsChecked.Count > 0)
        {
            RhinoApp.WriteLine($"  Search paths checked ({probe.SearchPathsChecked.Count}):");
            foreach (var (path, existed) in probe.SearchPathsChecked)
            {
                RhinoApp.WriteLine($"    - {(existed ? "[exists]" : "[missing]")} {path}");
            }
        }

        return Result.Success;
    }
}
