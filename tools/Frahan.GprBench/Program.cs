#nullable disable
using System;
using System.Diagnostics;
using Frahan.Masonry.Quarry.Ingestion;
using Frahan.Masonry.Quarry.Processing;

namespace Frahan.GprBench
{
    // Headless benchmark for the GPR processing chain on the REAL Grimsel granite
    // (986 traces x 1377 samples, MALA 160 MHz). Times RadargramProcessor.Run +
    // FractureExtractor.Extract; reports median over N measured iterations after a
    // warmup. Rhino-free (Processing/Ingestion only). Deterministic: also prints a
    // checksum of the energy section + pick count so before/after optimisation can
    // be confirmed NUMERICALLY EQUIVALENT, not just faster.
    //
    // Usage: dotnet run -c Release --project tools/Frahan.GprBench -- [rad] [rd3] [iters]
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string rad = args.Length > 0 ? args[0]
                : @"D:\code_ws\Template-General\raw\2026-06-04\grimsel_gpr\GPR_AU_N-to-S.rad";
            string rd3 = args.Length > 1 ? args[1]
                : @"D:\code_ws\Template-General\raw\2026-06-04\grimsel_gpr\GPR_AU_N-to-S.rd3";
            int iters = args.Length > 2 ? int.Parse(args[2]) : 5;

            if (!System.IO.File.Exists(rd3)) { Console.WriteLine("MISSING " + rd3); return 2; }

            var preset = GprPresets.Get("granite_160");
            var rg = GprMalaRd3Reader.Load(rd3, "granite-bench");
            var B = RadargramProcessor.ToGrid(rg, out double dtNs, out double dx);
            int ns = B.GetLength(0), ntr = B.GetLength(1);
            double v = preset.VelocityMNsPerNs;

            var proc = new RadargramProcessor();
            var fx = new FractureExtractor();
            preset.Apply(proc, fx);

            // warmup (JIT + cache)
            var e0 = proc.Run(B, dtNs, dx, v);
            var p0 = fx.Extract(e0, dtNs, dx, v);
            double checksum = Checksum(e0);

            var times = new double[iters];
            for (int k = 0; k < iters; k++)
            {
                var sw = Stopwatch.StartNew();
                var e = proc.Run(B, dtNs, dx, v);
                var p = fx.Extract(e, dtNs, dx, v);
                sw.Stop();
                times[k] = sw.Elapsed.TotalMilliseconds;
                if (Math.Abs(Checksum(e) - checksum) > 1e-6 || p.Count != p0.Count)
                    Console.WriteLine($"WARN non-deterministic at iter {k}");
            }
            Array.Sort(times);
            double median = times[iters / 2];
            Console.WriteLine($"granite {ntr}x{ns}  dt={dtNs:0.####}ns dx={dx:0.####}m v={v}");
            Console.WriteLine($"picks={p0.Count}  energy_checksum={checksum:0.000000}");
            Console.WriteLine($"Run+Extract median {median:0.1} ms over {iters} iters " +
                              $"(min {times[0]:0.1}, max {times[iters - 1]:0.1})");
            return 0;
        }

        private static double Checksum(double[,] e)
        {
            // scale-stable digest: sum of e[i,j]*(i+1) mod, robust to ordering
            int ns = e.GetLength(0), ntr = e.GetLength(1);
            double s = 0;
            for (int i = 0; i < ns; i++)
                for (int j = 0; j < ntr; j++)
                    s += e[i, j] * ((i * 31 + j * 7) % 1009);
            return s / (ns * (double)ntr);
        }
    }
}
