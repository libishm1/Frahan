using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Interfaces;

namespace Frahan.Verification.Tests
{
    /// <summary>
    /// Verifies <c>BlockGraphColorer.Color</c> (Welsh-Powell greedy graph
    /// colouring) against the Lean theorem <c>greedy_coloring_exists</c>
    /// (<c>frahan_proofs/FrahanProofs/Coloring.lean</c>): a proper colouring
    /// exists using at most Delta+1 colours (Delta = max contact degree).
    /// Two obligations:
    ///  * PROPER — no two blocks sharing an interface get the same colour;
    ///  * Delta+1 — the colourer never refuses; a Delta+1 palette always
    ///    suffices, so it must not throw (the old build hard-capped the palette
    ///    at 8 and threw on any graph needing more; that bug is fixed).
    /// Independent oracle: adjacency is rebuilt straight from the interface list
    /// handed in, never the colourer's internal graph.
    ///
    /// Origin harness + report: <c>outputs/2026-07-24/coloring_verification/</c>
    /// (found the 8-colour-cap refusal bug on 23.9% of random graphs; fixed to
    /// scale the palette to Delta+1; post-fix refusals 0, properness 0).
    /// </summary>
    public class ColoringTests
    {
        /// <summary>Random Erdos-Renyi contact graphs (N=8..30, p in [0.05,0.95]):
        /// every colouring is proper and the colourer never refuses.</summary>
        [Fact]
        public void RandomGraphs_ProperColouring_AndNeverRefuses()
        {
            var rng = new Random(20260724);
            int trials = 5000;
            long violations = 0, refusals = 0;
            int maxDeltaSeen = 0, maxColoursUsed = 0;

            for (int t = 0; t < trials; t++)
            {
                int n = rng.Next(8, 31);
                double p = 0.05 + 0.9 * rng.NextDouble();
                var (blocks, ifaces, edges, deg) = BuildRandomGraph(n, p, rng, tag: $"g{t}");
                int delta = deg.Count == 0 ? 0 : deg.Values.Max();
                if (delta > maxDeltaSeen) maxDeltaSeen = delta;

                var assembly = new MasonryAssembly(blocks, ifaces, new BoundaryConditions(new List<string>()));
                IReadOnlyDictionary<string, int> color;
                try { color = BlockGraphColorer.Color(assembly); }
                catch (InvalidOperationException) { refusals++; continue; }

                int used = color.Count == 0 ? 0 : color.Values.Distinct().Count();
                if (used > maxColoursUsed) maxColoursUsed = used;

                // independent oracle: every handed-in edge must be bichromatic
                foreach (var (a, b) in edges)
                    if (color[a] == color[b]) violations++;
            }

            Assert.True(violations == 0, $"proper-colouring violated: {violations} adjacent block pairs share a colour (maxDelta={maxDeltaSeen})");
            Assert.True(refusals == 0, $"colourer refused (threw) on {refusals} graphs — the Delta+1 guarantee (greedy_coloring_exists) must hold; maxDelta={maxDeltaSeen}, maxColours={maxColoursUsed}");
        }

        /// <summary>Complete graphs K_n (n=6..14) have chromatic number n and
        /// Delta = n-1. K_9..K_14 need &gt; 8 colours, directly probing the old
        /// 8-colour cap: the colourer must colour every one (no refusal), use
        /// exactly n colours, and stay proper.</summary>
        [Fact]
        public void CompleteGraphs_PaletteScalesPastEight()
        {
            int refusals = 0, violations = 0;
            var wrongCount = new List<string>();

            for (int n = 6; n <= 14; n++)
            {
                var blocks = new List<MasonryBlock>();
                for (int i = 0; i < n; i++) blocks.Add(Box($"k{n}_{i}", i * 2.0, 0.0));
                var ifaces = new List<MasonryInterface>();
                var edges = new List<(string, string)>();
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                    {
                        string a = $"k{n}_{i}", b = $"k{n}_{j}";
                        ifaces.Add(Contact(a, b));
                        edges.Add((a, b));
                    }
                var assembly = new MasonryAssembly(blocks, ifaces, new BoundaryConditions(new List<string>()));
                try
                {
                    var color = BlockGraphColorer.Color(assembly);
                    int used = color.Values.Distinct().Count();
                    foreach (var (a, b) in edges) if (color[a] == color[b]) violations++;
                    // K_n greedy uses exactly n colours (Delta+1 = n).
                    if (used != n) wrongCount.Add($"K{n}: used {used} colours, expected {n}");
                }
                catch (InvalidOperationException) { refusals++; }
            }

            Assert.True(refusals == 0, $"colourer refused on {refusals} complete graphs (the 8-colour cap must be gone)");
            Assert.True(violations == 0, $"complete-graph colouring not proper: {violations} monochromatic edges");
            Assert.True(wrongCount.Count == 0, $"K_n colour count != n: {string.Join("; ", wrongCount)}");
        }

        static (List<MasonryBlock>, List<MasonryInterface>, List<(string, string)>, Dictionary<string, int>)
            BuildRandomGraph(int n, double p, Random rng, string tag)
        {
            var blocks = new List<MasonryBlock>();
            var deg = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < n; i++)
            {
                string id = $"{tag}_b{i}";
                blocks.Add(Box(id, i * 2.0, (i % 3) * 1.0));
                deg[id] = 0;
            }
            var ifaces = new List<MasonryInterface>();
            var edges = new List<(string, string)>();
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    if (rng.NextDouble() < p)
                    {
                        string a = $"{tag}_b{i}", b = $"{tag}_b{j}";
                        ifaces.Add(Contact(a, b));
                        edges.Add((a, b));
                        deg[a]++; deg[b]++;
                    }
                }
            return (blocks, ifaces, edges, deg);
        }

        static MasonryBlock Box(string id, double x, double z)
        {
            double h = 0.4;
            var v = new List<double>();
            foreach (var sx in new[] { -h, h })
            foreach (var sy in new[] { -h, h })
            foreach (var sz in new[] { -h, h })
            { v.Add(x + sx); v.Add(sy); v.Add(z + sz); }
            var tri = new List<int>();
            for (int i = 0; i + 2 < 8; i++) { tri.Add(i); tri.Add(i + 1); tri.Add(i + 2); }
            return new MasonryBlock(id, v, tri, 1.0);
        }

        static MasonryInterface Contact(string a, string b)
        {
            var poly = new List<ContactVertex>
            {
                new ContactVertex(0, 0, 0), new ContactVertex(1, 0, 0), new ContactVertex(0, 1, 0)
            };
            return new MasonryInterface(a, b, poly,
                0, 0, 1,   // normal
                1, 0, 0,   // tangent1
                0, 1, 0);  // tangent2
        }
    }
}
