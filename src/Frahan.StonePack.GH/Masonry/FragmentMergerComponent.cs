#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Geometry;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // FragmentMergerComponent — agglomerates small fragments into their
    // largest adjacent host. Output is a merge mapping (per-input host
    // index + accumulated volume), NOT remeshed geometry. Downstream
    // consumers can either drop slivers, lump volume, or run real CSG
    // when a kernel is wired in.
    //
    // Adjacency input: a flat list of (i,j) pairs (one per pair). The
    // user typically wires this from the contact-pair output of an
    // upstream Auto Interfaces / Robust Auto Interfaces, encoded as
    // "i,j" strings or two parallel integer lists.
    //
    // ComponentGuid: F0123456-789A-BCDE-F012-3456789ABCDE
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Fragment Merger.
    /// Smallest-first agglomeration of fragments into their largest
    /// neighbour. Returns a merge mapping; geometry is not remeshed.
    /// </summary>
        [Algorithm("Greedy smallest-first agglomeration over a contact-adjacency graph",
        "Frahan-original",
        Note = "Union-into-largest-neighbour heuristic over upstream contact pairs; no remeshing, no canonical source")]
        [DesignApplication(
        "Agglomerates small fragments into their largest adjacent  host using upstream contact adjacency",
        DesignFlow.BottomUp,
        Precedent = "Frahan-original fragment merger for split-mesh recombination")]
    public sealed class FragmentMergerComponent : FrahanComponentBase
    {
        public FragmentMergerComponent()
            : base(
                "Fragment Merger", "FragMerge",
                "Agglomerates small fragments into their largest adjacent " +
                "host using upstream contact adjacency. Returns a merge " +
                "mapping (HostOf per piece + per-host accumulated volume); " +
                "geometry is NOT remeshed at this stage. Frahan-original method.",
                "Frahan", "Masonry")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.senary;

        public override Guid ComponentGuid =>
            new Guid("F0123456-789A-BCDE-F012-3456789ABCDE");

        protected override Bitmap Icon => IconProvider.Load("KintsugiAssemble.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Slabs", "S",
                "Slab DTOs (the candidate pieces).",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Adjacency I", "Ai",
                "First index of each adjacency pair.",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Adjacency J", "Aj",
                "Second index of each adjacency pair (parallel to Ai).",
                GH_ParamAccess.list);
            p.AddNumberParameter("Threshold Fraction", "Th",
                "Fragments below threshold·meanVolume are merged. Default " +
                "1e-3 (0.1% of mean).",
                GH_ParamAccess.item, 1e-3);
            p[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddIntegerParameter("Host Of", "H",
                "Per-input-piece, the index it ultimately merged into. " +
                "Self if it's a host.",
                GH_ParamAccess.list);
            p.AddNumberParameter("Merged Volume", "Vm",
                "Per-input-piece volume after merge (host accumulates; " +
                "non-host entries are 0).",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Host Indices", "Hi",
                "Indices of pieces that remained hosts.",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Merged Count", "Mc",
                "Number of fragments that got merged into a different host.",
                GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var raw = new List<object>();
            var ai = new List<int>();
            var aj = new List<int>();
            double thr = 1e-3;
            if (!da.GetDataList(0, raw) || raw.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No slabs.");
                return;
            }
            da.GetDataList(1, ai);
            da.GetDataList(2, aj);
            if (ai.Count != aj.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Adjacency I count ({ai.Count}) must match Adjacency J count ({aj.Count}).");
                return;
            }
            da.GetData(3, ref thr);

            var slabs = new List<Slab>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                if (raw[i] is Slab direct) slabs.Add(direct);
                else if (raw[i] is GH_ObjectWrapper w && w.Value is Slab fw) slabs.Add(fw);
            }
            if (slabs.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Inputs did not contain any Slab DTOs.");
                return;
            }

            var pairs = new List<(int I, int J)>(ai.Count);
            for (int k = 0; k < ai.Count; k++) pairs.Add((ai[k], aj[k]));

            FragmentMergeResult res;
            try { res = FragmentMerger.Merge(slabs, pairs, thr); }
            catch (ArgumentException ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            var hostIndices = new List<int>(res.HostCount);
            for (int i = 0; i < res.HostOf.Length; i++)
                if (res.HostOf[i] == i) hostIndices.Add(i);

            da.SetDataList(0, res.HostOf);
            da.SetDataList(1, res.MergedVolume);
            da.SetDataList(2, hostIndices);
            da.SetData(3, res.MergedCount);
        }
    }
}
