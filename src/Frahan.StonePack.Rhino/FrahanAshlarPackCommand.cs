#nullable disable
using Rhino;
using Rhino.Commands;

namespace Frahan.Rhino;

// Phase 1 scaffold: command class registers and is discoverable from the
// Rhino command line. Full wiring of AshlarLayoutEngine.Pack (slabs ->
// MasonryAssembly) requires the Slab interop helpers from
// Frahan.StonePack.GH/AshlarPackComponent.cs. Phase 2 work: duplicate those
// helpers into FrahanCommandInterop.cs and replace this scaffold with a
// real pick-and-pack flow.

public sealed class FrahanAshlarPackCommand : Command
{
    public override string EnglishName => "FrahanAshlarPack";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        RhinoApp.WriteLine("FrahanAshlarPack: Phase 1 scaffold (entry point registered).");
        RhinoApp.WriteLine("  Core API: Frahan.Masonry.Packing.AshlarLayoutEngine.Pack(slabs, options)");
        RhinoApp.WriteLine("  Today: use the AshlarPack GH component on the canvas.");
        RhinoApp.WriteLine("  Phase 2 (TODO): add Slab interop helpers, prompt for slab meshes,");
        RhinoApp.WriteLine("    course height + bond pattern; bake resulting block meshes.");
        return Result.Success;
    }
}
