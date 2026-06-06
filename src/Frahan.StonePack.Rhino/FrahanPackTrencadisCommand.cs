#nullable disable
using Rhino;
using Rhino.Commands;

namespace Frahan.Rhino;

// Phase 1 scaffold: command class registers and is discoverable from the
// Rhino command line. Full wiring of the Trencadis pipeline requires Curve
// interop helpers from Frahan.StonePack.GH/Pack2DTrencadisPipelineComponent.cs.
// Phase 2 work: duplicate those helpers into FrahanCommandInterop.cs and
// replace this scaffold with a real pick-and-pack flow.

public sealed class FrahanPackTrencadisCommand : Command
{
    public override string EnglishName => "FrahanPackTrencadis";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        RhinoApp.WriteLine("FrahanPackTrencadis: Phase 1 scaffold (entry point registered).");
        RhinoApp.WriteLine("  Core API: Frahan.Packing.Trencadis pipeline (see Pack2DTrencadisPipelineComponent for orchestration).");
        RhinoApp.WriteLine("  Today: use the Pack2DTrencadisPipeline GH component on the canvas.");
        RhinoApp.WriteLine("  Phase 2 (TODO): add Curve interop helpers, prompt for sheet outline +");
        RhinoApp.WriteLine("    part curves + seed; bake packed part transforms.");
        return Result.Success;
    }
}
