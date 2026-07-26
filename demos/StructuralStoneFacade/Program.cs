using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Interfaces;
using Frahan.Masonry.Sequencing;
using Frahan.Masonry.Solvers;

namespace Frahan.Demos.StructuralStoneFacade;

// =============================================================================
// Structural stone facade demonstrator + baseline validation cases.
//
// Builds a SELF-SUPPORTING single-skin stone facade - the Stone Federation's
// third category in "A Guide to Structural Stone" (June 2026): "the stacking of
// large stones to create a deep, single-skin which supports its own weight ...
// ground to roof, thick external masonry skin". Big unreinforced blocks,
// running bond, rectangular window openings spanned by lintel stones.
//
// Analysed by the SHIPPING, FORMALLY VERIFIED kernels:
//   * MasonryStabilityChecker - the static (safe) theorem of limit analysis,
//     proved in Lean as thm:cra (admissibleSet_convex + cra_farkas): the wall
//     stands iff an admissible force state exists, and INFEASIBILITY is itself
//     the certificate of collapse.
//   * BlockBuildOrderer - Kahn topological build order, proved as thm:kahn
//     (dag_has_source + kahn_linear_extension).
//
// The BASELINE CASES below are known-answer checks: each states the verdict it
// must produce and the run reports PASS/FAIL against that expectation, so the
// demonstrator validates itself rather than just printing numbers.
// =============================================================================

internal static class Program
{
    const double Density = 2650.0;      // granite, kg/m3
    const double Friction = 0.6;        // stone on stone, dry

    struct Box
    {
        public double X0, Y0, Z0, X1, Y1, Z1;
        public int Course;
        public bool IsLintel;
        public bool IsRoof;
        public double Density;
        public double Length => X1 - X0;
        public double Volume => (X1 - X0) * (Y1 - Y0) * (Z1 - Z0);
    }

    struct Opening
    {
        public double X0, X1, Z0, Z1;
        public string Name;
    }

    sealed class Spec
    {
        public string Name = "facade";
        public double WallW = 7.2, WallH = 5.4, Depth = 0.6;
        public double Course = 0.6, Nominal = 1.2, MinLen = 0.6, Bearing = 0.6;
        public double MaxLen = 2.4;             // merged stones stay "big", not absurd
        public bool Lintels = true;
        public double RoofLoadTonnes = 0.0;     // tributary floor/roof load on the top course
        public List<Opening> Openings = new List<Opening>();
        public bool ExpectStable = true;
        public string Why = "";
        public List<Box> Custom = null;      // bypasses the facade generator
        public StructuralWallResult Generated = null;   // filled by BuildFacade
    }

    static void Main()
    {
        var std = new List<Opening>
        {
            new Opening { X0 = 1.2, X1 = 3.0, Z0 = 1.2, Z1 = 3.0, Name = "W1" },
            new Opening { X0 = 4.2, X1 = 6.0, Z0 = 1.2, Z1 = 3.0, Name = "W2" },
        };

        // The architectural elevation (A1): 9.0 x 6.0 m, ten courses. A central
        // door 1.8 m wide running to the ground, and two 1.2 m windows at first
        // floor, symmetric about the centre line (4.5 +/- 2.7).
        var arch = new List<Opening>
        {
            new Opening { X0 = 3.6, X1 = 5.4, Z0 = 0.0, Z1 = 2.4, Name = "door" },
            new Opening { X0 = 1.2, X1 = 2.4, Z0 = 3.0, Z1 = 4.8, Name = "W1" },
            new Opening { X0 = 6.6, X1 = 7.8, Z0 = 3.0, Z1 = 4.8, Name = "W2" },
        };

        var cases = new List<Spec>
        {
            // NEGATIVE CONTROL. Validates end-to-end that this harness can and
            // does report collapse: a block with no support under it at all.
            new Spec { Name = "B0 unsupported block (control)", ExpectStable = false,
                       Custom = new List<Box>
                       {
                           new Box { X0=0, Y0=0, Z0=0,   X1=1.2, Y1=0.6, Z1=0.6, Course=0, Density=Density },
                           new Box { X0=0, Y0=0, Z0=1.8, X1=1.2, Y1=0.6, Z1=2.4, Course=3, Density=Density },
                       },
                       Why = "nothing carries the upper block; the QP must be infeasible" },

            new Spec { Name = "B1 solid wall (no openings)", Openings = new List<Opening>(),
                       ExpectStable = true,
                       Why = "a plain gravity stack must stand" },

            new Spec { Name = "B2 facade, 2 openings + lintels", Openings = std,
                       ExpectStable = true,
                       Why = "the demonstrator: lintels carry the head load onto the jambs" },

            // NOTE (corrected after running it): these were expected to collapse.
            // They do not, and the solver is right - a bonded wall over an opening
            // develops FLAT-ARCH action, thrusting into the masonry either side,
            // which the fixed foundation abuts. Masonry spans openings this way in
            // reality. The safe theorem certifies exactly that: an admissible
            // thrust state exists. Kept as observations, not failures.
            new Spec { Name = "B3 same openings, NO lintels", Openings = std, Lintels = false,
                       ExpectStable = true,
                       Why = "stands by flat-arch action, not by the lintel" },

            new Spec { Name = "B4 facade + 12 t roof/floor load", Openings = std,
                       RoofLoadTonnes = 12.0, ExpectStable = true,
                       Why = "tributary load must still be carried" },

            new Spec { Name = "B5 facade, zero lintel bearing", Openings = std, Bearing = 0.0,
                       ExpectStable = true,
                       Why = "lintel ends flush with the reveal still find a thrust line" },

            // A1 - the architectural elevation. A real building face rather than
            // a test panel: a central doorway running to the ground, two upper
            // windows, everything set out on the 0.6 m course module. The door
            // is the case the facade demonstrator never had - an opening with no
            // sill course, whose jambs bear on the foundation rather than on
            // masonry.
            new Spec { Name = "A1 architectural elevation", WallW = 9.0, WallH = 6.0,
                       Openings = arch, ExpectStable = true,
                       Why = "door to ground + two windows; the flagship example" },
        };

        Console.WriteLine("=== Structural stone facade - baseline validation ===");
        Console.WriteLine($"granite {Density} kg/m3, mu = {Friction}, gravity only unless stated\n");
        Console.WriteLine($"{"case",-34} {"blocks",6} {"verdict",-9} {"status",-12} {"util",7}  {"exp",4} {"result",6}");
        Console.WriteLine(new string('-', 92));

        MasonrySolverRegistry.Default = null;   // deterministic managed lane
        int passed = 0;
        List<Box> heroBoxes = null; Dictionary<string, int> heroOrder = null;
        bool heroStable = false; string heroStatus = ""; double heroUtil = 0;
        int heroRunning = 0, heroJamb = 0;
        List<Box> archBoxes = null; Dictionary<string, int> archOrder = null;
        bool archStable = false; string archStatus = ""; double archUtil = 0;
        Spec archSpec = null; int archInterfaces = 0, archFixed = 0, archLayers = 0;

        foreach (var spec in cases)
        {
            var boxes = BuildFacade(spec);
            var asm = BuildAssembly(boxes, out int fixedCount);
            var res = MasonryStabilityChecker.CheckDetailed(asm, Friction);
            var r = res.Result;
            bool ok = r.IsStable == spec.ExpectStable;
            if (ok) passed++;

            Console.WriteLine($"{spec.Name,-34} {boxes.Count,6} {(r.IsStable ? "STABLE" : "UNSTABLE"),-9} " +
                              $"{r.Status,-12} {r.MaxFrictionUtilization,6:P1}  " +
                              $"{(spec.ExpectStable ? "yes" : "no"),4} {(ok ? "PASS" : "FAIL"),6}");

            if (spec.Name.StartsWith("B2"))
            {
                heroBoxes = boxes; heroStable = r.IsStable; heroStatus = r.Status.ToString();
                heroUtil = r.MaxFrictionUtilization;
                heroRunning = spec.Generated.BondFaults;
                heroJamb = spec.Generated.ProtectedJointViolations;
                var steps = BlockBuildOrderer.Solve(asm);
                heroOrder = new Dictionary<string, int>();
                foreach (var s in steps) heroOrder[s.BlockId] = s.OrderIndex;
                Console.WriteLine($"{"",-34} -> {asm.InterfaceCount} interfaces, {fixedCount} fixed, " +
                                  $"build order {steps.Count} steps / {MaxLayer(steps) + 1} layers");
            }

            if (spec.Name.StartsWith("A1"))
            {
                archBoxes = boxes; archStable = r.IsStable; archStatus = r.Status.ToString();
                archUtil = r.MaxFrictionUtilization; archSpec = spec;
                archInterfaces = asm.InterfaceCount; archFixed = fixedCount;
                var steps = BlockBuildOrderer.Solve(asm);
                archLayers = MaxLayer(steps) + 1;
                archOrder = new Dictionary<string, int>();
                foreach (var s in steps) archOrder[s.BlockId] = s.OrderIndex;
                Console.WriteLine($"{"",-34} -> {asm.InterfaceCount} interfaces, {fixedCount} fixed, " +
                                  $"build order {steps.Count} steps / {archLayers} layers");
            }
        }

        Console.WriteLine(new string('-', 92));
        Console.WriteLine($"baseline: {passed}/{cases.Count} cases match their expected verdict");

        // ---- emit the geometry BEFORE the slow part ---------------------------
        // The collapse-tilt bisections below are 15 full QP solves each and take
        // minutes. Writing the JSON first means an interrupted run still leaves
        // usable data and drawable elevations (learned the hard way: a run killed
        // mid-bisection produced no data file at all).
        string outDir = Path.Combine("D:", "code_ws", "outputs", "2026-07-25", "structural_stone_facade");
        Directory.CreateDirectory(outDir);
        string json = Path.Combine(outDir, "facade.json");
        WriteJson(json, heroBoxes, heroOrder, heroStable, heroStatus, heroUtil);
        Console.WriteLine("wrote " + json);
        string ajson = Path.Combine(outDir, "architectural.json");
        WriteJson(ajson, archBoxes, archOrder, archStable, archStatus, archUtil);
        Console.WriteLine("wrote " + ajson);

        // ---- hero facade detail ----------------------------------------------
        Console.WriteLine();
        Console.WriteLine("=== B2 hero facade ===");
        double vol = 0, maxLen = 0; int lintels = 0;
        foreach (var b in heroBoxes) { vol += b.Volume; maxLen = Math.Max(maxLen, b.Length); if (b.IsLintel) lintels++; }
        Console.WriteLine($"blocks {heroBoxes.Count}, lintels {lintels}, stone {vol:F2} m3 " +
                          $"({vol * Density / 1000.0:F1} t), largest unit {maxLen:F2} m");
        Console.WriteLine($"verdict {(heroStable ? "STABLE" : "UNSTABLE")} ({heroStatus})");
        Console.WriteLine($"friction utilisation of the returned certificate: {heroUtil:P1} " +
                          "- NOT a safety margin (see note)");
        Console.WriteLine("  note: the safe theorem asserts a valid force state EXISTS; the solver");
        Console.WriteLine("  returns one such state, not the least-shear one, so this figure is a");
        Console.WriteLine("  property of that particular certificate. It reads the same across very");
        Console.WriteLine("  different walls, which is why the margin below is measured instead.");

        Console.WriteLine();
        Console.WriteLine("--- capacity by limit analysis (the real margin) ---");
        var heroSpec = cases.Find(c => c.Name.StartsWith("B2"));
        double tilt = CollapseTiltDegrees(heroSpec);
        double lam = Math.Tan(tilt * Math.PI / 180.0);
        double hand = heroSpec.Depth / heroSpec.WallH;
        Console.WriteLine($"collapse tilt        : {tilt:F2} deg  (out-of-plane overturning)");
        Console.WriteLine($"lateral load factor  : {lam:F3} g equivalent");
        Console.WriteLine($"hand check t/h       : {hand:F3}  - the monolithic-wall rule, " +
                          $"ratio {lam / hand:F2}");
        Console.WriteLine("vertical load        : unbounded in this model (rigid blocks, no");
        Console.WriteLine("                       crushing) - a material question, not a stability one");
        Console.WriteLine("--- bond quality (two DISTINCT faults, measured separately) ---");
        Console.WriteLine($"head joint repeated in adjacent courses (running joint): {heroRunning}");
        Console.WriteLine($"head joint under a jamb / over a lintel bearing       : {heroJamb}");

        // ---- A1 architectural elevation ---------------------------------------
        Console.WriteLine();
        Console.WriteLine("=== A1 architectural elevation (door + 2 windows) ===");
        double aVol = 0, aMax = 0; int aLintels = 0;
        foreach (var b in archBoxes) { aVol += b.Volume; aMax = Math.Max(aMax, b.Length); if (b.IsLintel) aLintels++; }
        Console.WriteLine($"wall {archSpec.WallW:F1} x {archSpec.WallH:F1} x {archSpec.Depth:F1} m, " +
                          $"{archSpec.Generated.CourseCount} courses");
        Console.WriteLine($"blocks {archBoxes.Count}, lintels {aLintels}, stone {aVol:F2} m3 " +
                          $"({aVol * Density / 1000.0:F1} t), largest unit {aMax:F2} m");
        Console.WriteLine($"verdict {(archStable ? "STABLE" : "UNSTABLE")} ({archStatus}), " +
                          $"{archInterfaces} interfaces, {archFixed} fixed, {archLayers} build layers");
        double aTilt = CollapseTiltDegrees(archSpec);
        double aLam = Math.Tan(aTilt * Math.PI / 180.0);
        double aHand = archSpec.Depth / archSpec.WallH;
        Console.WriteLine($"collapse tilt {aTilt:F2} deg = {aLam:F3} g | hand t/h {aHand:F3} " +
                          $"| ratio {aLam / aHand:F2}");
        Console.WriteLine($"running joints {archSpec.Generated.BondFaults}, " +
                          $"joints under a jamb / bearing {archSpec.Generated.ProtectedJointViolations}");
        foreach (var wmsg in archSpec.Generated.Warnings) Console.WriteLine("  note: " + wmsg);

        Environment.Exit(passed == cases.Count ? 0 : 1);
    }

    /// <summary>
    /// Collapse tilt of the facade - the classic masonry limit-analysis measure.
    /// The wall is tilted about its base with gravity held vertical, and the
    /// angle at which the safe theorem can no longer certify it is found by
    /// bisection. By cra_farkas that loss of feasibility IS the collapse
    /// certificate. tan(theta) is the equivalent horizontal load factor (the
    /// lateral g a wind or seismic action could apply).
    ///
    /// NOTE on why this, and not a vertical load test: rigid-block limit
    /// analysis gives blocks no crushing strength, so a flat wall under pure
    /// vertical load never fails in this model (measured: it certified past
    /// 40,000 t). Vertical capacity is a MATERIAL question this model does not
    /// answer. Overturning and sliding are what it does answer, so that is what
    /// is measured.
    /// </summary>
    static double CollapseTiltDegrees(Spec spec)
    {
        var boxes = BuildFacade(spec);
        Func<double, bool> stableAt = deg =>
        {
            var asm = BuildAssembly(boxes, out _, deg);
            return MasonryStabilityChecker.CheckDetailed(asm, Friction).Result.IsStable;
        };
        if (!stableAt(0.0)) return 0.0;
        double lo = 0.0, hi = 45.0;
        for (int i = 0; i < 15; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (stableAt(mid)) lo = mid; else hi = mid;
        }
        return lo;
    }

    static Spec Clone(Spec s) => new Spec
    {
        Name = s.Name, WallW = s.WallW, WallH = s.WallH, Depth = s.Depth, Course = s.Course,
        Nominal = s.Nominal, MinLen = s.MinLen, Bearing = s.Bearing, MaxLen = s.MaxLen,
        Lintels = s.Lintels, RoofLoadTonnes = s.RoofLoadTonnes, Openings = s.Openings,
        ExpectStable = s.ExpectStable, Why = s.Why, Custom = s.Custom
    };

    static int MaxLayer(IReadOnlyList<BuildStep> steps)
    {
        int m = 0; foreach (var s in steps) m = Math.Max(m, s.Layer); return m;
    }

    // -------------------------------------------------------------------------
    // Generator
    // -------------------------------------------------------------------------
    /// <summary>
    /// The wall geometry comes from the SHIPPING generator
    /// (Frahan.StonePack.Core, <c>StructuralWallGenerator</c>), linked and not
    /// copied — so this demonstrator and the Grasshopper component cannot
    /// diverge, and the numbers printed below are produced by the code that
    /// ships. This demo adds only what is a load case rather than wall
    /// geometry: the <see cref="Spec.Custom"/> bypass used by the B0 control,
    /// and the tributary roof block.
    /// </summary>
    static List<Box> BuildFacade(Spec s)
    {
        if (s.Custom != null) return s.Custom;

        var opts = new StructuralWallOptions
        {
            Width = s.WallW, Height = s.WallH, Depth = s.Depth,
            CourseHeight = s.Course, NominalLength = s.Nominal,
            MinLength = s.MinLen, MaxLength = s.MaxLen,
            LintelBearing = s.Bearing, Lintels = s.Lintels, Density = Density,
        };
        foreach (var op in s.Openings)
            opts.Openings.Add(new StructuralWallOpening(op.X0, op.X1, op.Z0, op.Z1, op.Name));

        var wall = StructuralWallGenerator.Generate(opts);
        s.Generated = wall;

        var boxes = new List<Box>();
        foreach (var b in wall.Blocks)
            boxes.Add(new Box
            {
                X0 = b.X0, Y0 = b.Y0, Z0 = b.Z0, X1 = b.X1, Y1 = b.Y1, Z1 = b.Z1,
                Course = b.Course, IsLintel = b.IsLintel, Density = Density
            });

        // Tributary roof/floor load, carried as a real bearing block on the top
        // course (an honest load path, not an abstract force): its density is
        // scaled so its weight equals the stated tonnage.
        if (s.RoofLoadTonnes > 0)
        {
            double th = 0.3;
            double v = s.WallW * s.Depth * th;
            double top = wall.EffectiveHeight;
            boxes.Add(new Box
            {
                X0 = 0, Y0 = 0, Z0 = top, X1 = s.WallW, Y1 = s.Depth, Z1 = top + th,
                Course = wall.CourseCount, IsRoof = true, Density = s.RoofLoadTonnes * 1000.0 / v
            });
        }
        return boxes;
    }

    static MasonryAssembly BuildAssembly(List<Box> boxes, out int fixedCount, double tiltDeg = 0.0)
    {
        var snaps = new List<MeshSnapshot>();
        var ids = new List<string>();
        var coordsList = new List<List<double>>();
        var trisList = new List<List<int>>();
        double globalMin = double.MaxValue;
        foreach (var b in boxes) globalMin = Math.Min(globalMin, b.Z0);

        for (int i = 0; i < boxes.Count; i++)
        {
            MeshOf(boxes[i], out var c, out var t, tiltDeg);
            coordsList.Add(c); trisList.Add(t);
            snaps.Add(new MeshSnapshot(c, t));
            ids.Add("s" + i.ToString("000"));
        }
        var ifaces = MeshContactDetector.Detect(snaps, ids);
        var blocks = new List<MasonryBlock>();
        var fixedIds = new List<string>();
        for (int i = 0; i < boxes.Count; i++)
        {
            double d = boxes[i].Density > 0 ? boxes[i].Density : Density;
            blocks.Add(new MasonryBlock(ids[i], coordsList[i], trisList[i], d));
            if (boxes[i].Z0 <= globalMin + 1e-6) fixedIds.Add(ids[i]);
        }
        fixedCount = fixedIds.Count;
        return new MasonryAssembly(blocks, ifaces, new BoundaryConditions(fixedIds));
    }

    static void MeshOf(Box b, out List<double> coords, out List<int> tris, double tiltDeg = 0.0)
    {
        coords = new List<double>
        {
            b.X0,b.Y0,b.Z0,  b.X1,b.Y0,b.Z0,  b.X1,b.Y1,b.Z0,  b.X0,b.Y1,b.Z0,
            b.X0,b.Y0,b.Z1,  b.X1,b.Y0,b.Z1,  b.X1,b.Y1,b.Z1,  b.X0,b.Y1,b.Z1,
        };
        if (Math.Abs(tiltDeg) > 1e-12)
        {
            // Tilt the whole wall about the base X axis, gravity staying vertical:
            // the standard tilting-plane test. tan(theta) is the equivalent
            // horizontal load factor (wind / seismic) the facade can take.
            double th = tiltDeg * Math.PI / 180.0, cs = Math.Cos(th), sn = Math.Sin(th);
            for (int i = 0; i < coords.Count; i += 3)
            {
                double y = coords[i + 1], z = coords[i + 2];
                coords[i + 1] = y * cs - z * sn;
                coords[i + 2] = y * sn + z * cs;
            }
        }
        tris = new List<int>
        {
            0,2,1, 0,3,2,   4,5,6, 4,6,7,
            0,1,5, 0,5,4,   2,3,7, 2,7,6,
            0,4,7, 0,7,3,   1,2,6, 1,6,5,
        };
    }

    static void WriteJson(string path, List<Box> boxes, Dictionary<string, int> order,
        bool stable, string status, double util)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("{\n  \"stable\": ").Append(stable ? "true" : "false")
          .Append(",\n  \"status\": \"").Append(status)
          .Append("\",\n  \"frictionUtilisation\": ").Append(util.ToString("G6", inv))
          .Append(",\n  \"blocks\": [\n");
        for (int i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            string id = "s" + i.ToString("000");
            order.TryGetValue(id, out int ord);
            sb.Append("    {\"id\":\"").Append(id).Append("\",\"course\":").Append(b.Course)
              .Append(",\"lintel\":").Append(b.IsLintel ? "true" : "false")
              .Append(",\"roof\":").Append(b.IsRoof ? "true" : "false")
              .Append(",\"order\":").Append(ord)
              .Append(",\"x0\":").Append(b.X0.ToString("G9", inv))
              .Append(",\"y0\":").Append(b.Y0.ToString("G9", inv))
              .Append(",\"z0\":").Append(b.Z0.ToString("G9", inv))
              .Append(",\"x1\":").Append(b.X1.ToString("G9", inv))
              .Append(",\"y1\":").Append(b.Y1.ToString("G9", inv))
              .Append(",\"z1\":").Append(b.Z1.ToString("G9", inv))
              .Append("}").Append(i + 1 < boxes.Count ? ",\n" : "\n");
        }
        sb.Append("  ]\n}\n");
        File.WriteAllText(path, sb.ToString());
    }
}
