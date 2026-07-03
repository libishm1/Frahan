#nullable disable
using System;
using System.Collections.Generic;
using Frahan.GH;
using Frahan.Masonry.Library;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.StonePack.GH.Masonry
{
    // =========================================================================
    // Stone Library — load a CATALOGUE of real rubble stones (.obj scans) from a
    // folder, tagged by lithology (sub-folder) + shape category + measured volume,
    // and filtered by category/lithology/chunkiness. The output meshes feed
    // straight into Stone-Cell Match to place a SELECTED category of library stones
    // onto a vault's Voronoi/mould cells -> a polygonal rubble vault. This is the
    // "select a rubble stone from a category/library" half of the matching story.
    // =========================================================================
    public sealed class StoneLibraryLoadComponent : FrahanComponentBase
    {
        public StoneLibraryLoadComponent()
            : base("Stone Library", "StoneLib",
                "Load a catalogue of real rubble stones (.obj scans, e.g. ETH1100) from a folder, tagged by " +
                "lithology (sub-folder name) + shape Category (Blocky / Platey / Elongated) + measured volume, " +
                "with a chunky aspect-ratio filter and optional category/lithology selection. The output stone " +
                "meshes feed Stone-Cell Match to place a SELECTED category onto a vault's cells (the polygonal " +
                "rubble vault). Deterministic for a given Seed.",
                "Frahan", "Vault")
        {
        }

        public override Guid ComponentGuid => new Guid("B7A11500-000A-4A11-B500-0000000000AA");
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Folder", "F", "Folder of stone .obj files (the library/catalogue).", GH_ParamAccess.item);
            p.AddIntegerParameter("Seed", "S", "Deterministic shuffle seed (repeatable subset). Default 18 = the validated Park Guell v004 recipe seed.", GH_ParamAccess.item, 18);
            p.AddIntegerParameter("Max Count", "N", "Keep at most this many stones (0 = all).", GH_ParamAccess.item, 0);
            p.AddNumberParameter("AR Max", "Ar", "Chunky filter: drop stones whose long/thin ratio exceeds this (0 = no filter). Default 2.2 = the validated poolAR.", GH_ParamAccess.item, 2.2);
            p.AddBooleanParameter("Recursive", "R", "Recurse into sub-folders (lithology = immediate sub-folder name).", GH_ParamAccess.item, false);
            p.AddTextParameter("Lithology", "L", "Keep only this lithology (sub-folder name); empty = all.", GH_ParamAccess.item, "");
            p.AddTextParameter("Category", "C", "Keep only this shape category: Blocky / Platey / Elongated; empty = all.", GH_ParamAccess.item, "");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Stones", "St", "The library stone meshes (centered), ready to feed Stone-Cell Match.", GH_ParamAccess.list);
            p.AddTextParameter("Names", "Nm", "Source file name per stone (provenance).", GH_ParamAccess.list);
            p.AddTextParameter("Lithology", "Li", "Lithology (sub-folder) per stone.", GH_ParamAccess.list);
            p.AddTextParameter("Category", "Ca", "Shape category per stone (Blocky / Platey / Elongated).", GH_ParamAccess.list);
            p.AddNumberParameter("Volume", "V", "Volume (m^3) per stone.", GH_ParamAccess.list);
            p.AddTextParameter("Report", "Rp", "Summary (counts by category/lithology).", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            string folder = null, lith = "", catStr = "";
            int seed = 18, maxCount = 0;
            double arMax = 2.2;
            bool recursive = false;
            if (!da.GetData(0, ref folder) || string.IsNullOrWhiteSpace(folder))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Provide a folder of .obj stones."); return; }
            da.GetData(1, ref seed); da.GetData(2, ref maxCount); da.GetData(3, ref arMax);
            da.GetData(4, ref recursive); da.GetData(5, ref lith); da.GetData(6, ref catStr);

            StoneCategory? catFilter = null;
            if (!string.IsNullOrWhiteSpace(catStr))
            {
                if (Enum.TryParse(catStr.Trim(), true, out StoneCategory c)) catFilter = c;
                else AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Unknown category '{catStr}' (use Blocky/Platey/Elongated); ignoring.");
            }

            var lib = StoneLibraryLoader.Load(folder, new StoneLibraryOptions
            {
                Seed = seed,
                MaxCount = Math.Max(0, maxCount),
                ArMax = arMax,
                Recursive = recursive,
                LithologyFilter = string.IsNullOrWhiteSpace(lith) ? null : lith.Trim(),
                CategoryFilter = catFilter,
            });

            if (lib.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No stones matched (check folder/filters)."); }

            var meshes = new List<Mesh>(lib.Count);
            var names = new List<string>(lib.Count);
            var liths = new List<string>(lib.Count);
            var cats = new List<string>(lib.Count);
            var vols = new List<double>(lib.Count);
            int nb = 0, np = 0, ne = 0;
            foreach (var s in lib)
            {
                meshes.Add(s.Mesh); names.Add(s.SourceName); liths.Add(s.Lithology);
                cats.Add(s.Category.ToString()); vols.Add(s.Volume);
                if (s.Category == StoneCategory.Blocky) nb++;
                else if (s.Category == StoneCategory.Platey) np++;
                else ne++;
            }

            da.SetDataList(0, meshes);
            da.SetDataList(1, names);
            da.SetDataList(2, liths);
            da.SetDataList(3, cats);
            da.SetDataList(4, vols);
            da.SetData(5, $"{lib.Count} stones loaded ({nb} blocky / {np} platey / {ne} elongated)" +
                          (catFilter.HasValue ? $", category={catFilter}" : "") +
                          (string.IsNullOrWhiteSpace(lith) ? "" : $", lithology={lith}") +
                          $", seed {seed}.");
            Message = $"{lib.Count} stones";
        }
    }
}
