using Frahan.Masonry.Solvers;
using Rhino.PlugIns;

namespace Frahan.Rhino;

public sealed class StonePackPlugin : PlugIn
{
    public StonePackPlugin()
    {
        Instance = this;
    }

    public static StonePackPlugin? Instance { get; private set; }

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        MasonrySolverRegistry.UseOsqpIfAvailable();
        return base.OnLoad(ref errorMessage);
    }
}
