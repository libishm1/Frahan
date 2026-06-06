#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Frahan.StonePack.Harness
{
    /// <summary>
    /// Headless out-of-process validation harness for the RhinoCommon-dependent
    /// edge-matching solver (Frahan.EdgeMatching.AssemblySolver + the 2D/3D ICP
    /// + boundary segmenters). Those classes cannot be exercised in the bare
    /// `dotnet test` host because RhinoCommon's native core (rhcommon_c) never
    /// initialises in a process that did not boot Rhino. This harness boots
    /// RhinoCommon out of process via Rhino.Inside (Rhino 8), runs the same
    /// panel-construction + solve sequence the EdgeMatchSolve / Trencadis
    /// EdgeMatch / Kintsugi GH components run, and prints hard assembly-quality
    /// numbers: pairwise interpenetration (overlap) and coverage / packing.
    ///
    /// CLI:
    ///   Frahan.StonePack.Harness &lt;fixture.3dm&gt; [--noncrossing] [--partial]
    ///     [--agglomerative] [--resolve] [--autoscale F] [--mode 2d|3d]
    ///
    /// The Rhino.Inside startup idiom is split across a JIT boundary: Main calls
    /// RhinoInside.Resolver.Initialize() FIRST, then hands off to Run() (a
    /// separate method) which is the first method that touches any RhinoCommon
    /// type. Because the CLR jits Run() only when it is first called -- after
    /// the resolver's AppDomain.AssemblyResolve hook is installed -- RhinoCommon
    /// loads from the Rhino 8 install dir rather than failing to bind. Touching
    /// a RhinoCommon type inside Main would force an early load before the hook
    /// exists. This is the documented Rhino.Inside pattern.
    /// </summary>
    public static class Program
    {
        private const string RhinoSystemDir = @"C:\Program Files\Rhino 8\System";

        // Adds a directory to the process-wide native DLL search path so the OS
        // loader resolves RhinoCore's native deps (RhinoLibrary.dll etc.) from
        // the Rhino 8 System dir. Same idiom KintsugiAssemblyComponent uses to
        // point the loader at the libtorch deploy folder.
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDllDirectory(string lpPathName);

        public static int Main(string[] args)
        {
            var opts = HarnessOptions.Parse(args);
            if (opts == null)
            {
                Console.Error.WriteLine(
                    "usage: Frahan.StonePack.Harness <fixture.3dm|eth-dir> [--noncrossing] [--partial] [--agglomerative] [--resolve] [--softicp] [--project3d] [--nfp] [--rubble] [--pack3d] [--autoscale F] [--mode 2d|3d]");
                return 2;
            }

            // ---- Rhino.Inside bootstrap. MUST run before any RhinoCommon type
            // is touched. Resolver.Initialize installs the AppDomain assembly-
            // resolve hook that redirects RhinoCommon / its native deps to the
            // Rhino 8 install. ----
            try
            {
                if (Directory.Exists(RhinoSystemDir))
                {
                    RhinoInside.Resolver.RhinoSystemDirectory = RhinoSystemDir;
                    // Native-dependency resolution: RhinoCore loads RhinoLibrary.dll
                    // and its native chain from the System dir. The managed
                    // assembly-resolve hook (Initialize, below) does NOT cover
                    // native DLLs, so without putting the System dir on the native
                    // search path the RhinoCore ctor throws DllNotFoundException
                    // for 'RhinoLibrary' (HRESULT 0x8007007E). Add it to both the
                    // Win32 DLL search path and PATH before the first RhinoCommon
                    // call.
                    try { SetDllDirectory(RhinoSystemDir); } catch { /* best effort */ }
                    string existingPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                    if (existingPath.IndexOf(RhinoSystemDir, StringComparison.OrdinalIgnoreCase) < 0)
                        Environment.SetEnvironmentVariable("PATH", RhinoSystemDir + ";" + existingPath);

                    // Belt-and-braces managed fallback. The Rhino.Inside 7.0
                    // resolver targets a Rhino 7 NuGet dependency set
                    // (RhinoWindows / RhinoCommon 7.0); on a Rhino 8 install some
                    // satellite managed assemblies (RhinoWindows.dll, Eto.dll,
                    // Rhino.UI.dll, ...) are not picked up by its hook and the CLR
                    // probes only the exe's bin folder, throwing
                    // FileNotFoundException. Add an AssemblyResolve fallback that
                    // loads any still-missing assembly from the Rhino 8 System
                    // dir by simple-name. Harmless when the primary resolver
                    // already handled it (this only fires after normal probing
                    // fails).
                    AppDomain.CurrentDomain.AssemblyResolve += ResolveFromRhinoSystem;
                }
                RhinoInside.Resolver.Initialize();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    "FATAL: RhinoInside.Resolver.Initialize() failed. Rhino 8 must be " +
                    "installed at '" + RhinoSystemDir + "'.");
                Console.Error.WriteLine("  " + ex.GetType().Name + ": " + ex.Message);
                return 3;
            }

            // Run() is the JIT boundary. Do not inline it into Main: the first
            // RhinoCommon-typed local would otherwise force RhinoCommon to load
            // while Main is being jitted, before the resolver hook above runs.
            return Run(opts);
        }

        // NoInlining guarantees Run is jitted on first call (post-Resolver),
        // not folded into Main's jit pass.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Run(HarnessOptions opts)
        {
            var log = new StringBuilder();
            void Emit(string s) { Console.WriteLine(s); log.AppendLine(s); }

            Emit("Frahan.StonePack edge-matching validation harness");
            Emit("fixture : " + opts.FixturePath);
            Emit("mode    : " + opts.Mode);
            Emit("noncross: " + (opts.NonCrossing ? "ON" : "off"));
            Emit("partial : " + (opts.Partial ? "ON (sub-segment emission)" : "off"));
            Emit("agglom  : " + (opts.Agglomerative ? "ON (pairwise graph + spanning tree)" : "off (frame-anchored beam)"));
            Emit("resolve : " + (opts.Resolve ? "ON (overlap penalty + edge-exclusivity + depenetration polish)" : "off (no non-overlap term)"));
            Emit("softicp : " + (opts.SoftIcp ? "ON (EM Soft-ICP rim-contact + non-penetration refine)" : "off"));
            Emit("project3d: " + (opts.Project3D ? "ON (2.5D per-facet projection bootstrap -> agglomerative + Soft-ICP refine)" : "off"));
            Emit("nfp     : " + (opts.Nfp ? "ON (Frahan polygon-NFP nester on 2D footprints)" : "off"));
            Emit("rubble  : " + (opts.Rubble ? "ON (Frahan masonry inventory packers on 3D stone meshes)" : "off"));
            Emit("pack3d  : " + (opts.Pack3D ? "ON (Frahan PRODUCTION 3D irregular-container packer on ETH stones)" : "off"));
            Emit("autoscale: " + (opts.AutoScale > 0
                ? opts.AutoScale.ToString("G3", CultureInfo.InvariantCulture) + " (scale-relative gates)"
                : "off (absolute gates)"));
            Emit("rhino   : " + RhinoSystemDir);
            Emit(new string('-', 64));

            Rhino.Runtime.InProcess.RhinoCore? core = null;
            string priorCwd = Directory.GetCurrentDirectory();
            try
            {
                // Booting the in-process Rhino core is what actually initialises
                // rhcommon_c. This throws if Rhino 8 is missing, unlicensed, or
                // cannot run headless in this environment.
                //
                // Run the ctor with the current directory set to the Rhino System
                // dir. RhinoCore's startup loads a chain of satellite managed +
                // native DLLs (RhinoWindows.dll, Eto.dll, native plug-in shims)
                // by probing the working directory; from the harness bin folder
                // those resolve to nothing (FileNotFoundException for
                // RhinoWindows "or one of its dependencies"). Switching CWD is the
                // most reliable way to make both the managed and native loaders
                // find them. Restored in finally.
                if (Directory.Exists(RhinoSystemDir))
                    Directory.SetCurrentDirectory(RhinoSystemDir);

                // Pre-load the Rhino satellite managed assemblies from the System
                // dir BEFORE constructing RhinoCore. RhinoCommon 8's startup does
                // an explicit Assembly.LoadFrom(<AppBase>\RhinoWindows.dll); that
                // path does not exist next to the harness exe, and LoadFrom of a
                // missing explicit path throws FileNotFoundException directly
                // (it never raises AppDomain.AssemblyResolve, so the System-dir
                // fallback hook cannot catch it). Loading the assembly identity
                // into the AppDomain first means the later load binds to the
                // already-resolved assembly instead of probing AppBase. The
                // Rhino.Inside 7.0.0 resolver predates Rhino 8's loader change,
                // so it does not do this for us.
                PreloadRhinoSatellites();

                core = new Rhino.Runtime.InProcess.RhinoCore();
                // Restore CWD so the (possibly relative) fixture path the user
                // passed still resolves against where they launched the harness.
                try { Directory.SetCurrentDirectory(priorCwd); } catch { /* ignore */ }
            }
            catch (Exception ex)
            {
                try { Directory.SetCurrentDirectory(priorCwd); } catch { /* ignore */ }
                Emit("FATAL: could not start the in-process Rhino core (RhinoCore).");
                Emit("  " + ex.GetType().FullName + ": " + ex.Message);
                for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
                    Emit("  inner: " + inner.GetType().FullName + ": " + inner.Message);
                if (ex is System.IO.FileNotFoundException fnf && !string.IsNullOrEmpty(fnf.FusionLog))
                    Emit("  fusion: " + fnf.FusionLog);
                if (ex is System.Reflection.ReflectionTypeLoadException rtle && rtle.LoaderExceptions != null)
                    foreach (var le in rtle.LoaderExceptions)
                        Emit("  loader: " + le?.Message);
                Emit("");
                Emit("DIAGNOSIS:");
                Emit("  Rhino.Inside 8.0.7-beta is wired (matches Rhino 8.x) and RhinoCommon");
                Emit("  is CopyLocal=False so the resolver loads it from the Rhino System");
                Emit("  folder (where its satellite RhinoWindows.dll lives). If RhinoCore");
                Emit("  STILL fails to start, the most likely causes are: (a) Rhino 8 is not");
                Emit("  licensed for Rhino.Inside / headless use on this machine, or (b) the");
                Emit("  Rhino System path is wrong (edit RhinoSystemDir). A FileNotFound for");
                Emit("  RhinoWindows.dll under bin means a stray RhinoCommon.dll got copied");
                Emit("  into the output -- the StripRhinoFromOutput target should remove it.");
                WriteReport(log.ToString());
                return 4;
            }

            try
            {
                // OPT-IN production-packer modes (default path unchanged). --nfp
                // runs Frahan's polygon-NFP nester on 2D footprints; --rubble runs
                // the masonry inventory packers on 3D stone meshes. Dispatched
                // before the edge-match 2D/3D paths; either flag wins over --mode.
                int rc = opts.Pack2DStudy ? Validator.RunPack2DStudy(opts, Emit)
                    : opts.PackBench ? Validator.RunPackBench(opts, Emit)
                    : opts.Pack3D ? Validator.RunPack3D(opts, Emit)
                    : opts.Nfp ? Validator.RunNfp(opts, Emit)
                    : opts.Rubble ? Validator.RunRubble(opts, Emit)
                    : opts.Mode == HarnessMode.ThreeD
                    ? Validator.Run3D(opts, Emit)
                    : Validator.Run2D(opts, Emit);
                Emit(new string('-', 64));
                Emit(rc == 0 ? "RESULT: completed" : "RESULT: failed (rc=" + rc + ")");
                WriteReport(log.ToString());
                return rc;
            }
            catch (Exception ex)
            {
                Emit("ERROR during validation: " + ex.GetType().FullName + ": " + ex.Message);
                Emit(ex.StackTrace ?? "");
                WriteReport(log.ToString());
                return 5;
            }
            finally
            {
                try { core?.Dispose(); } catch { /* shutdown best-effort */ }
            }
        }

        private static void PreloadRhinoSatellites()
        {
            // RhinoWindows pulls Eto / Rhino.UI; load the lot if present. Order
            // matters loosely: load leaf-ish UI libs first so RhinoWindows binds
            // to the already-loaded identities. Failures are non-fatal -- the
            // RhinoCore ctor will surface the real error if a critical one is
            // genuinely missing.
            string[] satellites =
            {
                "Eto", "Rhino.UI", "RhinoWindows",
            };
            foreach (var name in satellites)
            {
                try
                {
                    string path = Path.Combine(RhinoSystemDir, name + ".dll");
                    if (File.Exists(path))
                        System.Reflection.Assembly.LoadFrom(path);
                }
                catch { /* non-fatal; ctor will report if truly required */ }
            }
        }

        private static System.Reflection.Assembly? ResolveFromRhinoSystem(object? sender, ResolveEventArgs args)
        {
            try
            {
                // args.Name is the full display name; take the simple name.
                string simple = new System.Reflection.AssemblyName(args.Name).Name;
                if (string.IsNullOrEmpty(simple)) return null;
                string candidate = Path.Combine(RhinoSystemDir, simple + ".dll");
                if (File.Exists(candidate))
                    return System.Reflection.Assembly.LoadFrom(candidate);
            }
            catch { /* let the CLR continue with its normal failure */ }
            return null;
        }

        private static void WriteReport(string contents)
        {
            try
            {
                // Resolve outputs/2026-05-24/harness/edgematch_validation.txt at the
                // workspace root. Walk up from the exe until we find the repo
                // markers; fall back to CWD-relative if not found.
                string outDir = ResolveOutputDir();
                Directory.CreateDirectory(outDir);
                string path = Path.Combine(outDir, "edgematch_validation.txt");
                File.WriteAllText(path, contents);
                Console.WriteLine("report  : " + path);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("WARN: could not write report file: " + ex.Message);
            }
        }

        private static string ResolveOutputDir()
        {
            // Preferred absolute target per the task spec.
            const string preferred = @"D:\code_ws\outputs\2026-05-24\harness";
            string? rootDrive = null;
            try { rootDrive = Path.GetPathRoot(preferred); } catch { /* ignore */ }
            if (rootDrive != null && Directory.Exists(rootDrive))
                return preferred;

            // Fallback: relative to the current working directory.
            return Path.Combine(Directory.GetCurrentDirectory(), "outputs", "2026-05-24", "harness");
        }
    }

    internal enum HarnessMode { TwoD, ThreeD }

    internal sealed class HarnessOptions
    {
        public string FixturePath { get; private set; } = "";
        public bool NonCrossing { get; private set; }
        public HarnessMode Mode { get; private set; } = HarnessMode.TwoD;
        // A1: when > 0, enable scale-relative gates (ResidualThresholdFactor =
        // this value; phase gate loosened to 0.35). 0 = original absolute gates.
        public double AutoScale { get; private set; }
        // R1: when true, the segmenters emit partial sub-windows so a long edge
        // hash-matches the short sub-edge it physically mates with. off = base
        // whole-segment-only candidate generation (identical to before).
        public bool Partial { get; private set; }
        // R0: when true, AssemblyMode = Agglomerative (match all pairs -> pair
        // graph -> spanning-tree global resolve) instead of the frame-anchored
        // beam. Needed for free 3D reassembly where pieces mate pairwise.
        public bool Agglomerative { get; private set; }
        // R2: when true, turn on the global non-overlap resolve: a beam/edge
        // overlap penalty (OverlapPenalty), edge-exclusivity (EdgeExclusivity),
        // and the post-solve 2D rigid depenetration polish (ResolveOverlap). off
        // = no non-overlap term (identical to before).
        public bool Resolve { get; private set; }
        // Pillar A: when true, run SoftIcpRefiner after solve (2D from the placed
        // state) or, in 3D, from a perturbed-ground-truth start (the geometric
        // solver places 0 on the closed-hull fixture so there is nothing to refine
        // from the solver; the refiner is demonstrated against the GT poses).
        public bool SoftIcp { get; private set; }
        // Projection bootstrap (2.5D per-facet): when true, Run3D projects each
        // naked rim into its facet plane, matches the projected rims with the 2D
        // path, lifts each match to a 3D relative pose, feeds those candidate edges
        // to the agglomerative solver, then refines the placed fragments with
        // Soft-ICP and drops high-residual / penetrating pairs. Implies
        // --agglomerative + --softicp on the 3D path.
        public bool Project3D { get; private set; }
        // Item 2 automation: when true, run Frahan's PRODUCTION polygon-NFP
        // nester (NfpBottomLeftFillRhino) on the fixture's 2D footprint curves
        // and report packed count + true union coverage + overlap. Opt-in; the
        // default edge-match path is untouched.
        public bool Nfp { get; private set; }
        // Item 3 automation: when true, run Frahan's PRODUCTION masonry inventory
        // packers (BestFitInventoryPacker + AshlarLayoutEngine) on the fixture's
        // 3D stone meshes (built into Slab box descriptors) and report stones
        // placed + volume/area fill. Opt-in.
        public bool Rubble { get; private set; }
        // 3D-pack automation (added 2026-05-25): when true, run Frahan's PRODUCTION
        // 3D irregular-container mesh-heightmap packer (GreedyMeshHeightmapPacker +
        // IrregularMeshContainer, the exact Core path Pack3DIrregularContainerComponent
        // drives) on the ETH1100 dry-stone .obj meshes and report stones placed +
        // volume fill + used height. Opt-in. The positional arg is the ETH stone
        // DIRECTORY (or omitted to use the default path).
        public bool Pack3D { get; private set; }
        // --packbench: uniform performance benchmark across the packer families
        // (2D sheet V1/V2/V3/V506/NFP/BLF on synthetic parts + 3D TreePackForest +
        // masonry inventory on ETH AABBs). No fixture required (uses synthetic + ETH).
        public bool PackBench { get; private set; }
        // --pack2dstudy: comprehensive 2D packer study (all engines + V506 boundary
        // modes) with timing, containment, overlap, and a CSV metrics dump.
        public bool Pack2DStudy { get; private set; }

        public static HarnessOptions? Parse(string[] args)
        {
            if (args == null || args.Length == 0) return null;
            var o = new HarnessOptions();
            bool modeSet = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--noncrossing")
                {
                    o.NonCrossing = true;
                }
                else if (a == "--partial")
                {
                    o.Partial = true;
                }
                else if (a == "--agglomerative")
                {
                    o.Agglomerative = true;
                }
                else if (a == "--resolve")
                {
                    o.Resolve = true;
                }
                else if (a == "--softicp")
                {
                    o.SoftIcp = true;
                }
                else if (a == "--project3d")
                {
                    o.Project3D = true;
                }
                else if (a == "--nfp")
                {
                    o.Nfp = true;
                }
                else if (a == "--rubble")
                {
                    o.Rubble = true;
                }
                else if (a == "--pack3d")
                {
                    o.Pack3D = true;
                }
                else if (a == "--packbench")
                {
                    o.PackBench = true;
                }
                else if (a == "--pack2dstudy")
                {
                    o.Pack2DStudy = true;
                }
                else if (a == "--autoscale")
                {
                    if (i + 1 >= args.Length) return null;
                    if (!double.TryParse(args[++i], System.Globalization.NumberStyles.Float,
                            CultureInfo.InvariantCulture, out var f)) return null;
                    o.AutoScale = f;
                }
                else if (a == "--mode")
                {
                    if (i + 1 >= args.Length) return null;
                    string m = args[++i].ToLowerInvariant();
                    if (m == "2d") o.Mode = HarnessMode.TwoD;
                    else if (m == "3d") o.Mode = HarnessMode.ThreeD;
                    else return null;
                    modeSet = true;
                }
                else if (a.StartsWith("--", StringComparison.Ordinal))
                {
                    return null; // unknown flag
                }
                else
                {
                    o.FixturePath = a;
                }
            }
            // --pack3d may run with no positional arg (it defaults to the ETH stone
            // directory). Every other mode still requires a fixture path.
            if (string.IsNullOrEmpty(o.FixturePath) && !o.Pack3D && !o.PackBench && !o.Pack2DStudy) return null;

            // Auto-detect mode from the filename when not explicitly set, so a
            // bare `Harness edgematch_3d.3dm` does the right thing.
            if (!modeSet)
            {
                string lower = Path.GetFileName(o.FixturePath).ToLowerInvariant();
                if (lower.IndexOf("3d", StringComparison.Ordinal) >= 0)
                    o.Mode = HarnessMode.ThreeD;
            }
            return o;
        }
    }
}
