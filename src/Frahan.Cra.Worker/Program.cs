using System;
using System.Collections.Generic;
using System.IO;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Solvers;

namespace Frahan.Cra.Worker
{
    // frahan_cra_worker — out-of-process CRA solve. Runs the Rhino-free
    // MasonryStabilityChecker (+ native OSQP) so a GH component can offload the
    // heavy QP without blocking the canvas.
    //   --selftest            solve a known-stable 2-block stack (no I/O)
    //   --roundtrip           serialize -> deserialize -> solve (validates CraAssemblyIO)
    //   --solve <in> <out>    read a serialized assembly, solve, write the verdict
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 0 || args[0] == "--selftest") return SelfTest();
                if (args[0] == "--roundtrip") return RoundTrip();
                if (args[0] == "--solve" && args.Length >= 3) return SolveFile(args[1], args[2]);
                Console.Error.WriteLine("cra_worker: usage --selftest | --roundtrip | --solve <in> <out>");
                return 2;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("WORKER ERROR: " + e);
                return 3;
            }
        }

        private static int SolveFile(string inPath, string outPath)
        {
            MasonryAssembly asm;
            double mu, ts, gz; int fc; bool ins;
            using (var fin = File.OpenRead(inPath))
                asm = CraAssemblyIO.ReadAssembly(fin, out mu, out fc, out ins, out ts, out gz);
            var r = MasonryStabilityChecker.Check(asm, mu, fc, ins, ts, gz);
            using (var fout = File.Create(outPath))
                CraAssemblyIO.WriteResult(fout, r.IsStable, (int)r.Status, r.MaxCompression, "CRA " + r.Status);
            Console.WriteLine($"solved {asm.Blocks.Count} blocks / {asm.Interfaces.Count} interfaces -> IsStable={r.IsStable} ({r.Status})");
            return 0;
        }

        private static int RoundTrip()
        {
            var asm = BuildTestAssembly();
            var ms = new MemoryStream();
            CraAssemblyIO.WriteAssembly(ms, asm, 0.84, 8, true, 1.0, -9.80665);
            ms.Position = 0;
            var asm2 = CraAssemblyIO.ReadAssembly(ms, out double mu, out int fc, out bool ins, out double ts, out double gz);
            var r = MasonryStabilityChecker.Check(asm2, mu, fc, ins, ts, gz);
            Console.WriteLine($"ROUNDTRIP: in blocks={asm.Blocks.Count} -> out blocks={asm2.Blocks.Count} ifaces={asm2.Interfaces.Count} IsStable={r.IsStable} ({r.Status})");
            return (asm2.Blocks.Count == asm.Blocks.Count && r.IsStable) ? 0 : 1;
        }

        private static int SelfTest()
        {
            var r = MasonryStabilityChecker.Check(BuildTestAssembly(), 0.84, 8, true, 1.0, -9.80665);
            Console.WriteLine($"SELFTEST: IsStable={r.IsStable}  Status={r.Status}  MaxCompression={r.MaxCompression:0.0}N");
            Console.WriteLine(r.IsStable
                ? "OK: the Rhino-free CRA solve runs standalone (native OSQP reachable)."
                : "FAIL: expected the resting block to be stable.");
            return r.IsStable ? 0 : 1;
        }

        // A free block resting on a fixed base, sharing a horizontal contact at z=0
        // -> gravity gives an admissible compression-only state -> STABLE.
        private static MasonryAssembly BuildTestAssembly()
        {
            var baseB = Box("base", 0, 0, -0.5, 1, 1, 0.0, 2400);
            var topB = Box("top", 0, 0, 0.0, 1, 1, 0.5, 2400);
            var poly = new List<ContactVertex>
            {
                new ContactVertex(0, 0, 0), new ContactVertex(1, 0, 0),
                new ContactVertex(1, 1, 0), new ContactVertex(0, 1, 0),
            };
            var iface = new MasonryInterface("base", "top", poly, 0, 0, 1, 1, 0, 0, 0, 1, 0);
            return new MasonryAssembly(
                new List<MasonryBlock> { baseB, topB },
                new List<MasonryInterface> { iface },
                new BoundaryConditions(new List<string> { "base" }));
        }

        private static MasonryBlock Box(string id,
            double x0, double y0, double z0, double x1, double y1, double z1, double density)
        {
            var c = new double[]
            {
                x0,y0,z0, x1,y0,z0, x1,y1,z0, x0,y1,z0,
                x0,y0,z1, x1,y0,z1, x1,y1,z1, x0,y1,z1,
            };
            var t = new int[]
            {
                0,2,1, 0,3,2,   4,5,6, 4,6,7,
                0,1,5, 0,5,4,   1,2,6, 1,6,5,
                2,3,7, 2,7,6,   3,0,4, 3,4,7,
            };
            return new MasonryBlock(id, c, t, density);
        }
    }
}
