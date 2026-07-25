using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Interfaces;
using Frahan.Masonry.Solvers;

namespace Frahan.Demos.StructuralStoneFacade;

// =============================================================================
// Structural stone facade demonstrator.
//
// Builds a SELF-SUPPORTING single-skin stone facade - the Stone Federation's
// third category in "A Guide to Structural Stone" (June 2026): "the stacking of
// large stones to create a deep, single-skin which supports its own weight ...
// ground to roof, thick external masonry skin". Big unreinforced blocks,
// running bond, rectangular window openings spanned by lintel stones.
//
// Then it runs the SHIPPING, FORMALLY VERIFIED structural pipeline on it:
//   * MasonryStabilityChecker  - the static (safe) theorem of limit analysis.
//     Proved in Lean as thm:cra (admissibleSet_convex + cra_farkas): the wall
//     stands iff an admissible force state exists, and INFEASIBILITY is itself
//     the certificate of collapse.
//   * BlockBuildOrderer        - Kahn topological build order. Proved as
//     thm:kahn (dag_has_source + kahn_linear_extension): a valid build sequence
//     always exists on an acyclic support graph and the loop never stalls.
//
// Emits facade.json (blocks + course + lintel flag + build order) for baking.
// =============================================================================

internal static class Program
{
    // Granite. Structural stone works in "big stones", so the units are large.
    const double Density = 2650.0;      // kg/m3
    const double Friction = 0.6;        // stone-on-stone, dry

    struct Box
    {
        public double X0, Y0, Z0, X1, Y1, Z1;
        public int Course;
        public bool IsLintel;
        public double Length => X1 - X0;
        public double Volume => (X1 - X0) * (Y1 - Y0) * (Z1 - Z0);
    }

    struct Opening
    {
        public double X0, X1, Z0, Z1;
        public string Name;
    }

    static void Main()
    {
        // ---- facade definition (metres) --------------------------------
        const double wallW = 7.2;        // ground-to-roof external skin
        const double wallH = 5.4;
        const double depth = 0.6;        // "deep, single skin"
        const double course = 0.6;       // course height = block height
        const double nominal = 1.2;      // nominal block length (a big stone)
        const double minLen = 0.6;       // no slivers: half-block minimum
        const double bearing = 0.6;      // lintel bearing each side

        var openings = new List<Opening>
        {
            new Opening { X0 = 1.2, X1 = 3.0, Z0 = 1.2, Z1 = 3.0, Name = "W1" },
            new Opening { X0 = 4.2, X1 = 6.0, Z0 = 1.2, Z1 = 3.0, Name = "W2" },
        };

        var boxes = BuildFacade(wallW, wallH, depth, course, nominal, minLen, bearing, openings);

        Console.WriteLine("=== Structural stone facade (self-supporting single skin) ===");
        Console.WriteLine($"wall        : {wallW} x {wallH} m, skin depth {depth} m");
        Console.WriteLine($"courses     : {(int)Math.Round(wallH / course)} at {course} m");
        Console.WriteLine($"openings    : {openings.Count} rectangular, each " +
                          $"{openings[0].X1 - openings[0].X0} x {openings[0].Z1 - openings[0].Z0} m");
        Console.WriteLine($"blocks      : {boxes.Count}");

        int lintels = 0; double vol = 0, maxLen = 0;
        foreach (var b in boxes) { if (b.IsLintel) lintels++; vol += b.Volume; maxLen = Math.Max(maxLen, b.Length); }
        Console.WriteLine($"lintels     : {lintels} (one per opening, {openings[0].X1 - openings[0].X0 + 2 * bearing} m each)");
        Console.WriteLine($"stone volume: {vol:F2} m3  ({vol * Density / 1000.0:F1} tonnes)");
        Console.WriteLine($"largest unit: {maxLen:F2} m long, {maxLen * depth * course * Density / 1000.0:F2} t");

        // ---- running-bond check (an honest self-check, not a kernel call) ----
        int aligned = CountAlignedHeadJoints(boxes, course);
        Console.WriteLine($"bond        : {aligned} aligned head joints between adjacent courses " +
                          (aligned == 0 ? "(clean running bond)" : "(CHECK)"));

        // ---- assemble + analyse with the shipping kernels --------------------
        var asm = BuildAssembly(boxes, out var fixedCount);
        Console.WriteLine();
        Console.WriteLine($"assembly    : {asm.BlockCount} blocks, {asm.InterfaceCount} interfaces, " +
                          $"{fixedCount} fixed (foundation course), {asm.FreeBlockCount} free");

        // Deterministic managed lane (native OSQP is opt-in; keep this reproducible).
        MasonrySolverRegistry.Default = null;

        Console.WriteLine();
        Console.WriteLine("--- structural verdict (thm:cra, the static/safe theorem) ---");
        var t0 = DateTime.UtcNow;
        var res = MasonryStabilityChecker.CheckDetailed(asm, Friction);
        var ms = (DateTime.UtcNow - t0).TotalMilliseconds;
        var r = res.Result;
        Console.WriteLine($"stable      : {r.IsStable}   status={r.Status}   ({ms:F0} ms)");
        Console.WriteLine($"max compression      : {r.MaxCompression:F1} N");
        Console.WriteLine($"max friction utilisn : {r.MaxFrictionUtilization:P1}   (mu = {Friction})");
        if (!r.IsStable)
            Console.WriteLine("NOTE: infeasibility IS the collapse certificate (cra_farkas), not a solver failure.");

        Console.WriteLine();
        Console.WriteLine("--- build order (thm:kahn, topological) ---");
        var steps = BlockBuildOrderer.Solve(asm);
        Console.WriteLine($"steps       : {steps.Count} (all blocks sequenced: {steps.Count == asm.BlockCount})");
        int maxLayer = 0; foreach (var s in steps) maxLayer = Math.Max(maxLayer, s.Layer);
        Console.WriteLine($"layers      : {maxLayer + 1}");
        var sb0 = new StringBuilder();
        for (int i = 0; i < Math.Min(8, steps.Count); i++) sb0.Append(steps[i].BlockId).Append(' ');
        Console.WriteLine($"first steps : {sb0}...");

        var order = new Dictionary<string, int>();
        foreach (var s in steps) order[s.BlockId] = s.OrderIndex;

        // ---- emit for baking -------------------------------------------------
        string outDir = Path.Combine("D:", "code_ws", "outputs", "2026-07-25", "structural_stone_facade");
        Directory.CreateDirectory(outDir);
        string json = Path.Combine(outDir, "facade.json");
        WriteJson(json, boxes, order, r.IsStable, r.Status.ToString(), r.MaxFrictionUtilization);
        Console.WriteLine();
        Console.WriteLine("wrote " + json);
    }

    // -------------------------------------------------------------------------
    // Facade generator: courses of big blocks in running bond, openings cut out,
    // a lintel stone over each opening with bearing onto the jambs either side.
    // -------------------------------------------------------------------------
    static List<Box> BuildFacade(double wallW, double wallH, double depth, double course,
        double nominal, double minLen, double bearing, List<Opening> openings)
    {
        var boxes = new List<Box>();
        int nCourses = (int)Math.Round(wallH / course);

        for (int c = 0; c < nCourses; c++)
        {
            double z0 = c * course, z1 = z0 + course;

            // Lintel course for any opening whose head is this course's bed.
            var lintelSpans = new List<(double x0, double x1)>();
            foreach (var op in openings)
            {
                if (Math.Abs(op.Z1 - z0) < 1e-9)
                {
                    double lx0 = Math.Max(0.0, op.X0 - bearing);
                    double lx1 = Math.Min(wallW, op.X1 + bearing);
                    boxes.Add(new Box { X0 = lx0, Y0 = 0, Z0 = z0, X1 = lx1, Y1 = depth, Z1 = z1, Course = c, IsLintel = true });
                    lintelSpans.Add((lx0, lx1));
                }
            }

            // Solid spans of this course = wall minus openings minus lintels.
            var blocked = new List<(double x0, double x1)>();
            foreach (var op in openings)
                if (op.Z0 < z1 - 1e-9 && op.Z1 > z0 + 1e-9) blocked.Add((op.X0, op.X1));
            blocked.AddRange(lintelSpans);

            foreach (var span in SolidSpans(0.0, wallW, blocked))
                LayCourse(boxes, span.x0, span.x1, z0, z1, depth, course, nominal, minLen, c);
        }
        return boxes;
    }

    /// <summary>[lo,hi] minus the blocked intervals, as the remaining solid runs.</summary>
    static List<(double x0, double x1)> SolidSpans(double lo, double hi, List<(double x0, double x1)> blocked)
    {
        var cuts = new List<(double x0, double x1)>(blocked);
        cuts.Sort((a, b) => a.x0.CompareTo(b.x0));
        var spans = new List<(double x0, double x1)>();
        double cur = lo;
        foreach (var b in cuts)
        {
            if (b.x0 > cur + 1e-9) spans.Add((cur, Math.Min(b.x0, hi)));
            cur = Math.Max(cur, b.x1);
        }
        if (cur < hi - 1e-9) spans.Add((cur, hi));
        return spans;
    }

    /// <summary>
    /// Lay one solid run of a course. Odd courses start with a half block so head
    /// joints stagger against the course below (running bond). A trailing offcut
    /// shorter than minLen is merged into the previous stone rather than left as
    /// a sliver - the guide's "big stones", and slivers are structurally poor.
    /// </summary>
    static void LayCourse(List<Box> boxes, double x0, double x1, double z0, double z1,
        double depth, double course, double nominal, double minLen, int c)
    {
        double runLen = x1 - x0;
        if (runLen < minLen - 1e-9)
        {
            if (runLen > 1e-9)
                boxes.Add(new Box { X0 = x0, Y0 = 0, Z0 = z0, X1 = x1, Y1 = depth, Z1 = z1, Course = c });
            return;
        }

        var edges = new List<double> { x0 };
        // Running bond: on odd courses the first stone is a half, shifting every
        // head joint above/below by half a block.
        double first = (c % 2 == 1) ? nominal * 0.5 : nominal;
        double cur = x0 + Math.Min(first, runLen);
        while (cur < x1 - 1e-9) { edges.Add(cur); cur += nominal; }
        edges.Add(x1);

        // Merge a trailing sliver into its neighbour.
        if (edges.Count >= 3 && edges[edges.Count - 1] - edges[edges.Count - 2] < minLen - 1e-9)
            edges.RemoveAt(edges.Count - 2);

        for (int i = 0; i + 1 < edges.Count; i++)
            boxes.Add(new Box { X0 = edges[i], Y0 = 0, Z0 = z0, X1 = edges[i + 1], Y1 = depth, Z1 = z1, Course = c });
    }

    /// <summary>Head joints that line up between vertically adjacent courses -
    /// the thing running bond exists to avoid. Excludes wall ends and openings.</summary>
    static int CountAlignedHeadJoints(List<Box> boxes, double course)
    {
        var byCourse = new Dictionary<int, List<Box>>();
        foreach (var b in boxes)
        {
            if (!byCourse.TryGetValue(b.Course, out var l)) { l = new List<Box>(); byCourse[b.Course] = l; }
            l.Add(b);
        }
        int aligned = 0;
        foreach (var kv in byCourse)
        {
            if (!byCourse.TryGetValue(kv.Key + 1, out var above)) continue;
            foreach (var b in kv.Value)
            {
                foreach (var a in above)
                {
                    // interior head joint of the lower stone coinciding with one above
                    if (Math.Abs(a.X0 - b.X1) < 1e-9 && a.X0 > 1e-9)
                    {
                        bool bothInterior = b.X1 > 1e-9;
                        if (bothInterior) aligned++;
                    }
                }
            }
        }
        return aligned;
    }

    static MasonryAssembly BuildAssembly(List<Box> boxes, out int fixedCount)
    {
        var snaps = new List<MeshSnapshot>();
        var ids = new List<string>();
        var coordsList = new List<List<double>>();
        var trisList = new List<List<int>>();
        double globalMin = double.MaxValue;
        foreach (var b in boxes) globalMin = Math.Min(globalMin, b.Z0);

        for (int i = 0; i < boxes.Count; i++)
        {
            MeshOf(boxes[i], out var c, out var t);
            coordsList.Add(c); trisList.Add(t);
            snaps.Add(new MeshSnapshot(c, t));
            ids.Add("s" + i.ToString("000"));
        }
        var ifaces = MeshContactDetector.Detect(snaps, ids);
        var blocks = new List<MasonryBlock>();
        var fixedIds = new List<string>();
        for (int i = 0; i < boxes.Count; i++)
        {
            blocks.Add(new MasonryBlock(ids[i], coordsList[i], trisList[i], Density));
            if (boxes[i].Z0 <= globalMin + 1e-6) fixedIds.Add(ids[i]);   // foundation course
        }
        fixedCount = fixedIds.Count;
        return new MasonryAssembly(blocks, ifaces, new BoundaryConditions(fixedIds));
    }

    static void MeshOf(Box b, out List<double> coords, out List<int> tris)
    {
        coords = new List<double>
        {
            b.X0,b.Y0,b.Z0,  b.X1,b.Y0,b.Z0,  b.X1,b.Y1,b.Z0,  b.X0,b.Y1,b.Z0,
            b.X0,b.Y0,b.Z1,  b.X1,b.Y0,b.Z1,  b.X1,b.Y1,b.Z1,  b.X0,b.Y1,b.Z1,
        };
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
