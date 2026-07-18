using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Frahan.Masonry.Quarry.GeoPack;

namespace Frahan.GuillotineParity
{
    // =========================================================================
    // Console parity harness for the fracture-following staged guillotine packer.
    //
    //   Frahan.GuillotineParity <input.json> <output.json>
    //       Read the shared input cases, run the C# port on each, write results.
    //       The Python side (scripts/csharp_guillotine_parity) reads the SAME
    //       input.json, runs pyfrahan, and compares the two result files.
    //
    //   Frahan.GuillotineParity --selftest
    //       Build the two canonical cases in-process and assert the invariants
    //       (no Python needed): count == analytic grid (no-fracture), phi == 1.0,
    //       0 SAT-overlap straddlers, 2 slabs for the one-bed case. PASS/FAIL.
    //
    // Rhino-free: source-links only StagedGuillotinePacker.cs. No Rhino, no NuGet.
    // =========================================================================
    internal static class Program
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--selftest")
                return SelfTest();
            if (args.Length == 2)
                return RunFile(args[0], args[1]);
            Console.Error.WriteLine("usage: Frahan.GuillotineParity <input.json> <output.json>");
            Console.Error.WriteLine("       Frahan.GuillotineParity --selftest");
            return 2;
        }

        // ---- one packed case, ready to serialize -----------------------------
        private sealed class CaseResult
        {
            public string name;
            public int block_count;
            public double recovered_volume;
            public double tested_volume;
            public double recovery_fraction;
            public double cut_area_m2;
            public int cut_planes;
            public int slab_count;
            public List<int> slab_block_counts = new List<int>();
            public List<double> phi_per_slab = new List<double>();
            public double min_phi;
            public int straddlers;          // SAT-overlap pairs (0 = manufacturable)
            public int analytic_grid_count; // no-fracture anchor
            public List<string> slab_desc = new List<string>();
        }

        private static CaseResult RunCase(string name, GBox bench, double Lx, double Ly, double Lz,
            double kerf, double clearance, double keepout, int gridRes, bool singleSize,
            IReadOnlyList<TriMeshInside> surfaces)
        {
            double[] factors = singleSize ? new[] { 1.0 } : null;
            var r = FractureGuillotinePacker.Pack(bench, surfaces, Lx, Ly, Lz, kerf,
                clearance, keepout, gridRes, factors);
            var cr = new CaseResult
            {
                name = name,
                block_count = r.BlockCount,
                recovered_volume = r.RecoveredVolumeM3,
                tested_volume = r.TestedVolumeM3,
                recovery_fraction = r.RecoveryFraction,
                cut_area_m2 = r.CutAreaM2,
                cut_planes = r.CutPlanes,
                slab_count = r.SlabBlocks.Count,
                min_phi = r.MinSeparableFraction,
                straddlers = StagedGuillotinePacker.SatOverlapPairs(r.Blocks),
                analytic_grid_count = StagedGuillotinePacker.AnalyticGridCount(bench, Lx, Ly, Lz, kerf),
            };
            for (int i = 0; i < r.SlabBlocks.Count; i++) cr.slab_block_counts.Add(r.SlabBlocks[i].Count);
            cr.phi_per_slab.AddRange(r.SeparableFraction);
            cr.slab_desc.AddRange(r.SlabDesc);
            return cr;
        }

        // ---- file-based parity ----------------------------------------------
        private static int RunFile(string inputPath, string outputPath)
        {
            var results = new List<CaseResult>();
            using (var doc = JsonDocument.Parse(File.ReadAllText(inputPath)))
            {
                var cases = doc.RootElement.GetProperty("cases");
                foreach (var c in cases.EnumerateArray())
                {
                    string name = c.GetProperty("name").GetString();
                    var bench = ReadBox(c.GetProperty("bench"));
                    var blk = ReadArray(c.GetProperty("block"));
                    double kerf = c.GetProperty("kerf").GetDouble();
                    double clearance = GetOr(c, "clearance", 0.0);
                    double keepout = GetOr(c, "keepout", 0.0);
                    int gridRes = c.TryGetProperty("grid_res", out var gr) ? gr.GetInt32() : 26;
                    bool singleSize = c.TryGetProperty("single_size", out var ss) && ss.GetBoolean();
                    var surfaces = new List<TriMeshInside>();
                    if (c.TryGetProperty("surfaces", out var surfEl))
                        foreach (var surf in surfEl.EnumerateArray())
                        {
                            var tris = new List<double[]>();
                            foreach (var tri in surf.EnumerateArray()) tris.Add(ReadArray(tri));
                            if (tris.Count > 0) surfaces.Add(new TriMeshInside(tris));
                        }
                    results.Add(RunCase(name, bench, blk[0], blk[1], blk[2], kerf,
                        clearance, keepout, gridRes, singleSize, surfaces));
                }
            }
            File.WriteAllText(outputPath, Serialize(results));
            Console.WriteLine("C# harness: wrote " + results.Count + " case result(s) -> " + outputPath);
            foreach (var r in results)
                Console.WriteLine(string.Format(Inv,
                    "  {0}: {1} blocks, vol {2:0.######} m^3, phi(min) {3:0.###}, straddlers {4}, slabs {5}, analytic {6}",
                    r.name, r.block_count, r.recovered_volume, r.min_phi, r.straddlers, r.slab_count, r.analytic_grid_count));
            return 0;
        }

        // ---- standalone self-test (no Python) -------------------------------
        private static int SelfTest()
        {
            bool ok = true;

            // Case A: no-fracture exact-multiple bench (self-test b anchor).
            var benchA = new GBox(0, 0, 0, 3.3, 2.25, 1.4);
            var rA = RunCase("no_fracture_exact_multiple", benchA, 0.5, 0.4, 0.3, 0.05,
                0.0, 0.0, 26, true, new List<TriMeshInside>());
            bool aCount = rA.block_count == rA.analytic_grid_count && rA.block_count == 120;
            bool aPhi = rA.min_phi == 1.0;
            bool aStrad = rA.straddlers == 0;
            Console.WriteLine(string.Format(Inv, "[A] no-fracture: {0} blocks (analytic {1}) phi {2} straddlers {3} -> {4}",
                rA.block_count, rA.analytic_grid_count, rA.min_phi, rA.straddlers,
                (aCount && aPhi && aStrad) ? "PASS" : "FAIL"));
            ok &= aCount && aPhi && aStrad;

            // Case B: one flat horizontal bed -> two fracture-bounded slabs (self-test c/d).
            var benchB = new GBox(0, 0, 0, 3.3, 2.25, 1.4);
            double bedZ = 0.0 + 3.0 * (0.3 + 0.05); // 3 block-layers up = 1.05
            var bed = FlatBed(benchB, bedZ, 0.5);
            var rB = RunCase("one_flat_bed", benchB, 0.5, 0.4, 0.3, 0.05,
                0.0, 0.0, 26, false, new List<TriMeshInside> { bed });
            bool bSlabs = rB.slab_count == 2;
            bool bPhi = rB.min_phi == 1.0;
            bool bStrad = rB.straddlers == 0;
            bool bBlocks = rB.block_count > 0;
            Console.WriteLine(string.Format(Inv, "[B] one flat bed: {0} slabs, {1} blocks, phi {2} straddlers {3} -> {4}",
                rB.slab_count, rB.block_count, rB.min_phi, rB.straddlers,
                (bSlabs && bPhi && bStrad && bBlocks) ? "PASS" : "FAIL"));
            ok &= bSlabs && bPhi && bStrad && bBlocks;

            Console.WriteLine(ok ? "SELFTEST PASS" : "SELFTEST FAIL");
            return ok ? 0 : 1;
        }

        // A flat 2-triangle bed covering the bench XY (padded) at height z.
        private static TriMeshInside FlatBed(GBox bench, double z, double pad)
        {
            double x0 = bench.MinX - pad, x1 = bench.MaxX + pad;
            double y0 = bench.MinY - pad, y1 = bench.MaxY + pad;
            var tris = new List<double[]>
            {
                new[] { x0, y0, z, x1, y0, z, x1, y1, z },
                new[] { x0, y0, z, x1, y1, z, x0, y1, z },
            };
            return new TriMeshInside(tris);
        }

        // ---- json helpers ----------------------------------------------------
        private static GBox ReadBox(JsonElement e)
        {
            var a = ReadArray(e);
            return new GBox(a[0], a[1], a[2], a[3], a[4], a[5]);
        }

        private static double[] ReadArray(JsonElement e)
        {
            var list = new List<double>();
            foreach (var v in e.EnumerateArray()) list.Add(v.GetDouble());
            return list.ToArray();
        }

        private static double GetOr(JsonElement e, string name, double def)
            => e.TryGetProperty(name, out var v) ? v.GetDouble() : def;

        private static string Serialize(List<CaseResult> results)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"engine\": \"csharp\",\n  \"cases\": [\n");
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                sb.Append("    {\n");
                sb.Append("      \"name\": ").Append(JsonEncode(r.name)).Append(",\n");
                sb.Append("      \"block_count\": ").Append(r.block_count).Append(",\n");
                sb.Append("      \"recovered_volume\": ").Append(D(r.recovered_volume)).Append(",\n");
                sb.Append("      \"tested_volume\": ").Append(D(r.tested_volume)).Append(",\n");
                sb.Append("      \"recovery_fraction\": ").Append(D(r.recovery_fraction)).Append(",\n");
                sb.Append("      \"cut_area_m2\": ").Append(D(r.cut_area_m2)).Append(",\n");
                sb.Append("      \"cut_planes\": ").Append(r.cut_planes).Append(",\n");
                sb.Append("      \"slab_count\": ").Append(r.slab_count).Append(",\n");
                sb.Append("      \"slab_block_counts\": ").Append(IntList(r.slab_block_counts)).Append(",\n");
                sb.Append("      \"phi_per_slab\": ").Append(DblList(r.phi_per_slab)).Append(",\n");
                sb.Append("      \"min_phi\": ").Append(D(r.min_phi)).Append(",\n");
                sb.Append("      \"straddlers\": ").Append(r.straddlers).Append(",\n");
                sb.Append("      \"analytic_grid_count\": ").Append(r.analytic_grid_count).Append("\n");
                sb.Append("    }").Append(i < results.Count - 1 ? "," : "").Append("\n");
            }
            sb.Append("  ]\n}\n");
            return sb.ToString();
        }

        private static string D(double v) => v.ToString("R", Inv);
        private static string JsonEncode(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static string IntList(List<int> xs)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < xs.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(xs[i]); }
            return sb.Append("]").ToString();
        }

        private static string DblList(List<double> xs)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < xs.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(D(xs[i])); }
            return sb.Append("]").ToString();
        }
    }
}
