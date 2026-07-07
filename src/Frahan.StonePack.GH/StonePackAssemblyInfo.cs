using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using Grasshopper.Kernel;

[assembly: InternalsVisibleTo("Frahan.StonePack.Tests")]
// Load the .gha via COFF byte-array instead of file-mapping — releases the file lock
// so the build can overwrite Frahan.StonePack.gha while Rhino is running.
// Run GrasshopperReloadAssemblies (or restart GH) to pick up the new build.
[assembly: GH_Loading(GH_LoadingDemand.ForceCOFF)]

namespace Frahan.GH;

public sealed class StonePackAssemblyInfo : GH_AssemblyInfo
{
    public override string Name => "Frahan StonePack";
    public override Bitmap? Icon => IconProvider.Load("QuarryBlock.png");
    public override string Description => "Fabrication-aware 2D and 3D packing components for Rhino and Grasshopper.";
    public override Guid Id => new Guid("4F07C431-1546-4920-A36B-3AC3AEFE3CE1");
    public override string AuthorName => "Frahan";
    public override string AuthorContact => "";
    // Derive from the assembly version (csproj <Version>) so this never drifts from the
    // real build again -- a hardcoded "0.5.5" here outlived several feature releases.
    public override string Version =>
        typeof(StonePackAssemblyInfo).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
}
