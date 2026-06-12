#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Frahan.Packing.TwoD
{
    // =========================================================================
    // NativeNfpKernel — managed front-end for the native batched No-Fit-Polygon
    // kernel (native/nfp_kernel/nfp_kernel.dll, vendored official Clipper2 C++,
    // BSL-1.0). One P/Invoke per part computes NFP(obstacle, part@angle) for
    // every (rotation angle x obstacle) pair on Clipper2's exact Int64 lane.
    //
    // Mirrors the CgalMeshBoolean shim pattern: probe once on first use, cache
    // IsAvailable, never throw out of the probe. Callers treat a null return
    // from BatchNfp as "native unavailable / failed" and fall back to the
    // managed path verbatim.
    //
    // DEPLOY: nfp_kernel.dll (x64) must sit beside the consuming executable's
    // managed assemblies — beside Frahan.StonePack.Tests.exe for the headless
    // tests AND beside the .gha for Grasshopper canvas use. When the dll is
    // absent every caller silently keeps the managed Clipper2 lane.
    // =========================================================================
    public static class NativeNfpKernel
    {
        private const string Dll = "nfp_kernel";

        private static bool? _isAvailable;
        private static readonly object _lock = new object();

        /// <summary>One NFP loop tagged with the (angle, obstacle) pair that produced it.</summary>
        public sealed class NfpLoop
        {
            public int AngleIdx;
            public int ObstIdx;
            public List<(double X, double Y)> Loop;
        }

        /// <summary>
        /// True iff nfp_kernel.dll loads and answers a trivial probe batch.
        /// First call probes; subsequent calls return the cached answer.
        /// </summary>
        public static bool IsAvailable
        {
            get
            {
                if (_isAvailable.HasValue) return _isAvailable.Value;
                lock (_lock)
                {
                    if (_isAvailable.HasValue) return _isAvailable.Value;
                    // explicit kill switch (benchmark A/B + emergency opt-out):
                    // FRAHAN_NFP_NATIVE=0 forces the managed lane even when the
                    // dll is present. Read once per process (cached like the probe).
                    if (Environment.GetEnvironmentVariable("FRAHAN_NFP_NATIVE") == "0")
                    {
                        _isAvailable = false;
                        return false;
                    }
                    try
                    {
                        // Rhino's process does NOT search the plugin folder for
                        // P/Invoke modules, so a dll sitting beside the .gha /
                        // Core.dll is invisible to DllImport's default search.
                        // Preload it by explicit path (same trick as the other
                        // native shims); once loaded by name, DllImport binds.
                        try
                        {
                            var here = System.IO.Path.GetDirectoryName(typeof(NativeNfpKernel).Assembly.Location);
                            if (!string.IsNullOrEmpty(here))
                            {
                                var cand = System.IO.Path.Combine(here, "nfp_kernel.dll");
                                if (System.IO.File.Exists(cand)) LoadLibrary(cand);
                            }
                        }
                        catch { }

                        // unit square vs unit square at angle 0 -> 1 loop, area 4
                        var partXY = new double[] { 0, 0, 1, 0, 1, 1, 0, 1 };
                        var angles = new double[] { 0.0 };
                        var obstVerts = new int[] { 4 };
                        var outXY = new double[64];
                        var lv = new int[8];
                        var la = new int[8];
                        var lo = new int[8];
                        int rc = nfp_batch(partXY, 4, angles, 1, partXY, obstVerts, 1,
                            100.0, 0.0, outXY, 64, lv, la, lo, 8, out int loopCount);
                        _isAvailable = rc == 0 && loopCount == 1;
                    }
                    catch (DllNotFoundException) { _isAvailable = false; }
                    catch (BadImageFormatException) { _isAvailable = false; }
                    catch (EntryPointNotFoundException) { _isAvailable = false; }
                    catch (Exception) { _isAvailable = false; }
                    return _isAvailable.Value;
                }
            }
        }

        /// <summary>
        /// Batched NFP build: for every angle a and obstacle o computes
        /// NFP(o, part@a) = MinkowskiSum(o, -rotate(part, a)) NonZero-unioned,
        /// exactly on the Int64 lane at the given scale. simplifyTol &lt; 0 is
        /// relative (|tol| x bbox-diagonal per shape), &gt; 0 absolute, 0 off.
        /// Returns the loops in deterministic (angleIdx, obstIdx) order, or
        /// NULL on any failure (caller falls back to the managed lane).
        /// </summary>
        public static List<NfpLoop> BatchNfp(
            IReadOnlyList<(double X, double Y)> part,
            IReadOnlyList<double> anglesRad,
            IReadOnlyList<IReadOnlyList<(double X, double Y)>> obstacles,
            double scale,
            double simplifyTol)
        {
            if (!IsAvailable) return null;
            if (part == null || part.Count < 3) return null;
            if (anglesRad == null || anglesRad.Count == 0) return null;
            obstacles = obstacles ?? Array.Empty<IReadOnlyList<(double X, double Y)>>();
            if (obstacles.Count == 0) return new List<NfpLoop>();

            // ── flatten inputs ────────────────────────────────────────────
            var partXY = new double[2 * part.Count];
            for (int i = 0; i < part.Count; i++)
            {
                partXY[2 * i] = part[i].X;
                partXY[2 * i + 1] = part[i].Y;
            }
            var angles = new double[anglesRad.Count];
            for (int i = 0; i < anglesRad.Count; i++) angles[i] = anglesRad[i];

            int totalObstVerts = 0;
            var obstVerts = new int[obstacles.Count];
            for (int i = 0; i < obstacles.Count; i++)
            {
                obstVerts[i] = obstacles[i] != null ? obstacles[i].Count : 0;
                totalObstVerts += obstVerts[i];
            }
            var obstXY = new double[2 * Math.Max(1, totalObstVerts)];
            int w = 0;
            for (int i = 0; i < obstacles.Count; i++)
            {
                var o = obstacles[i];
                for (int k = 0; k < obstVerts[i]; k++)
                {
                    obstXY[w++] = o[k].X;
                    obstXY[w++] = o[k].Y;
                }
            }

            // ── capacity heuristic + retry-on-capacity (max 3 attempts) ───
            // NFP loop size is O(|obstacle| + |part|) for typical inputs; the
            // kernel reports exact requirements on rc == 1, so the retry is a
            // single precise re-allocation.
            int capDoubles = 2 * anglesRad.Count * (totalObstVerts + obstacles.Count * (part.Count + 8)) * 2;
            if (capDoubles < 4096) capDoubles = 4096;
            int capLoops = anglesRad.Count * obstacles.Count * 4 + 64;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                var outXY = new double[capDoubles];
                var loopVerts = new int[capLoops];
                var loopAngle = new int[capLoops];
                var loopObst = new int[capLoops];
                int rc;
                int loopCount;
                try
                {
                    rc = nfp_batch(partXY, part.Count, angles, angles.Length,
                        obstXY, obstVerts, obstacles.Count, scale, simplifyTol,
                        outXY, capDoubles, loopVerts, loopAngle, loopObst,
                        capLoops, out loopCount);
                }
                catch (Exception)
                {
                    return null; // SEH / marshal failure: managed fallback
                }

                if (rc == 1) // capacity: kernel reported exact requirements
                {
                    capLoops = Math.Max(capLoops, loopCount);
                    capDoubles = Math.Max(capDoubles, loopVerts[0]);
                    continue;
                }
                if (rc != 0) return null;

                var result = new List<NfpLoop>(loopCount);
                int off = 0;
                for (int li = 0; li < loopCount; li++)
                {
                    int n = loopVerts[li];
                    var loop = new List<(double X, double Y)>(n);
                    for (int k = 0; k < n; k++)
                        loop.Add((outXY[off + 2 * k], outXY[off + 2 * k + 1]));
                    off += 2 * n;
                    result.Add(new NfpLoop { AngleIdx = loopAngle[li], ObstIdx = loopObst[li], Loop = loop });
                }
                return result;
            }
            return null; // capacity never satisfied (should not happen)
        }

        // ── raw import (see native/nfp_kernel/nfp_kernel_capi.h) ──────────
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string path);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int nfp_batch(
            double[] partXY, int partVerts,
            double[] anglesRad, int angleCount,
            double[] obstXY, int[] obstVerts, int obstCount,
            double scale, double simplifyTol,
            double[] outXY, int outCapacityDoubles,
            int[] outLoopVerts, int[] outLoopAngleIdx, int[] outLoopObstIdx,
            int loopCapacity, out int outLoopCount);
    }
}
