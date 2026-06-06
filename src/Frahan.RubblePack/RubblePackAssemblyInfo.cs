#nullable disable
using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace Frahan.GH.RubblePack
{
    /// <summary>
    /// Grasshopper plugin metadata for the Frahan RubblePack sibling assembly.
    /// Components register under Category "Frahan" so they share the ribbon
    /// tab with the main Frahan.StonePack plugin.
    /// </summary>
    public sealed class RubblePackAssemblyInfo : GH_AssemblyInfo
    {
        public override string Name => "Frahan RubblePack";
        public override Bitmap Icon => null;
        public override string Description =>
            "Carve decomposed statue blocks out of a rough rubble lot with TRUE geometric " +
            "enclosure. Two methods: an evolved single-block fit (one block per stone, pose " +
            "optimised) and a multi-bin pack (many blocks per stone). Companion to example 15.";
        public override Guid Id => new Guid("b1c2d3e4-aa01-4f5e-9c10-7e60cada15ff");
        public override string AuthorName => "Frahan StonePack";
        public override string AuthorContact => "github.com/libishm1/Frahan";
        public override string Version => "0.1.0";
    }
}
