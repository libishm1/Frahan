#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Interfaces;

namespace Frahan.Masonry.Sequencing;

// =============================================================================
// StructuralWallGenerator — Rhino-free RECTANGULAR coursed structural-stone
// wall generator. Sibling of PolygonalWallGenerator (which produces irregular
// power-diagram stones); this one produces the SELF-SUPPORTING SINGLE SKIN of
// the Stone Federation's "A Guide to Structural Stone" (June 2026), third
// category: "the stacking of large stones to create a deep, single-skin which
// supports its own weight ... ground to roof, thick external masonry skin".
// Big unreinforced blocks, running bond, rectangular openings spanned by
// lintel stones.
//
// Ported verbatim (behaviour-preserving) from the verified demonstrator
// demos/StructuralStoneFacade/Program.cs (6/6 baseline cases, 0 bond faults),
// with ONE additive capability: DOOR openings (see "Doors" below).
//
// Layout rules, in the order the generator applies them per course c
// (z0 = c*CourseHeight, z1 = z0 + CourseHeight):
//
//   1. LINTELS. An opening whose head Z1 coincides with this course's bottom
//      gets one lintel stone spanning [X0 - LintelBearing, X1 + LintelBearing]
//      clamped to the wall, occupying the whole course band.
//   2. BLOCKED BANDS. Any opening overlapping the course band in z removes
//      [X0, X1] from the course. Lintel spans are blocked too (the lintel is
//      already the stone there).
//   3. RUNNING BOND. Each remaining solid span is laid at NominalLength, odd
//      courses starting with a half block (NominalLength/2) so head joints
//      stagger course to course.
//   4. TRAILING-SLIVER MERGE. A trailing stone shorter than MinLength is
//      merged into its neighbour rather than laid as a sliver.
//   5. JOINT PROTECTION. A head joint is a genuine masonry fault when it falls
//      directly UNDER an opening jamb (the jamb must bear on solid stone) or
//      directly OVER a lintel bearing (the lintel end must sit on solid stone,
//      not on a joint). Such joints are removed by merging the two stones into
//      one longer stone, capped at MaxLength. If the merge would exceed
//      MaxLength the joint is kept (a longer stone is the worse fault) and the
//      wall reports it.
//
// DOORS (new, not in the demonstrator). An opening with Z0 <= 0 runs to the
// ground: it is a DOOR, not a window. Two consequences, both handled:
//   * there is no sill course beneath it, so no course can carry its jambs;
//   * therefore the "no head joint under a jamb" protection below the opening
//     does not apply and is skipped explicitly. The jambs bear on the
//     foundation, which is the fixed boundary of the assembly.
// The lintel over a door is generated exactly as for a window, and the door
// reveals are excluded from the bond-fault count exactly as window reveals
// are.
//
// BED-JOINT SNAP (additive, defeatable). A lintel is generated only when an
// opening's head coincides with a bed joint, so a hand-drawn opening a few
// millimetres off the grid silently gets no lintel. SnapOpeningsToCourses
// (default true) pulls the sill and head onto the nearest bed joint when they
// are within a quarter course, and records the snap in Warnings. It is an exact
// no-op for openings already on the grid, so the demonstrator's cases are
// reproduced bit for bit. Set it false for the unmodified demonstrator path.
//
// BOND FAULTS. The counter reports a head joint REPEATED in two vertically
// adjacent courses where both courses are solid at that x (a running joint /
// stack-bond fault). The straight vertical edge of an opening is geometry, not
// a fault, so opening reveals are excluded.
//
// Deterministic: no randomness, no time or culture dependence. Pure managed;
// no Rhino types. Mapping to meshes and to the canvas lives in the GH layer,
// except for the two Rhino-free convenience helpers BoxMesh / BuildAssembly,
// which keep the flat coords+tris buffers and the exact-contact assembly in
// Core where they are testable.
// =============================================================================

/// <summary>
/// One rectangular opening in the wall's XZ plane. An opening with
/// <see cref="Z0"/> &lt;= 0 reaches the ground and is a DOOR
/// (see <see cref="IsDoor"/>).
/// </summary>
public sealed class StructuralWallOpening
{
    /// <summary>Z0 at or below this counts as "reaching the ground".</summary>
    public const double GroundTolerance = 1e-9;

    public StructuralWallOpening() { }

    public StructuralWallOpening(double x0, double x1, double z0, double z1, string name = null)
    {
        X0 = Math.Min(x0, x1); X1 = Math.Max(x0, x1);
        Z0 = Math.Min(z0, z1); Z1 = Math.Max(z0, z1);
        Name = name;
    }

    /// <summary>Left edge (along the wall).</summary>
    public double X0;
    /// <summary>Right edge (along the wall).</summary>
    public double X1;
    /// <summary>Sill height. &lt;= 0 means the opening reaches the ground (a door).</summary>
    public double Z0;
    /// <summary>Head height (the underside of the lintel).</summary>
    public double Z1;
    /// <summary>Optional label, echoed in warnings.</summary>
    public string Name;

    /// <summary>
    /// True when the opening runs to the ground. A door has no sill course, so
    /// the jamb-protection below the opening does not apply.
    /// </summary>
    public bool IsDoor => Z0 <= GroundTolerance;

    public double Width => X1 - X0;
    public double Height => Z1 - Z0;
}

/// <summary>Options for <see cref="StructuralWallGenerator.Generate"/>.</summary>
public sealed class StructuralWallOptions
{
    /// <summary>Wall width in model units (x extent).</summary>
    public double Width = 7.2;
    /// <summary>Requested wall height. Snapped to a whole number of courses.</summary>
    public double Height = 5.4;
    /// <summary>Single-skin depth (y extent) of every stone.</summary>
    public double Depth = 0.6;
    /// <summary>Bed-joint spacing: the height of one course.</summary>
    public double CourseHeight = 0.6;
    /// <summary>Nominal stone length used to set out the running bond.</summary>
    public double NominalLength = 1.2;
    /// <summary>Shortest acceptable stone. Trailing slivers below this merge into their neighbour.</summary>
    public double MinLength = 0.6;
    /// <summary>Cap on a stone produced by a joint-protection merge.</summary>
    public double MaxLength = 2.4;
    /// <summary>Bearing length of a lintel onto the masonry each side of its opening.</summary>
    public double LintelBearing = 0.6;
    /// <summary>False lays the courses over the openings without a lintel stone (flat-arch action only).</summary>
    public bool Lintels = true;
    /// <summary>Stone density (kg/m3). Granite dry = 2650.</summary>
    public double Density = 2650.0;
    /// <summary>
    /// Pull an opening's sill and head onto the nearest bed joint when they are
    /// within a quarter course of one. A lintel is only generated when the head
    /// coincides with a bed joint, so an unsnapped hand-drawn opening silently
    /// gets no lintel. Snapping is reported in <see cref="StructuralWallResult.Warnings"/>
    /// and is an exact no-op for openings already on the grid. Set false for the
    /// unmodified demonstrator behaviour.
    /// </summary>
    public bool SnapOpeningsToCourses = true;
    /// <summary>Rectangular openings in the wall's XZ plane.</summary>
    public List<StructuralWallOpening> Openings = new List<StructuralWallOpening>();
}

/// <summary>One rectangular stone of the generated wall, as an axis-aligned box.</summary>
public sealed class StructuralWallBlock
{
    public StructuralWallBlock(double x0, double y0, double z0,
                               double x1, double y1, double z1,
                               int course, bool isLintel)
    {
        X0 = x0; Y0 = y0; Z0 = z0; X1 = x1; Y1 = y1; Z1 = z1;
        Course = course; IsLintel = isLintel;
    }

    public double X0 { get; }
    public double Y0 { get; }
    public double Z0 { get; }
    public double X1 { get; }
    public double Y1 { get; }
    public double Z1 { get; }

    /// <summary>Zero-based course index, counting up from the ground.</summary>
    public int Course { get; }
    /// <summary>True when this stone is the lintel spanning an opening.</summary>
    public bool IsLintel { get; }

    public double Length => X1 - X0;
    public double Depth => Y1 - Y0;
    public double Height => Z1 - Z0;
    public double Volume => (X1 - X0) * (Y1 - Y0) * (Z1 - Z0);
}

/// <summary>Result of <see cref="StructuralWallGenerator.Generate"/>.</summary>
public sealed class StructuralWallResult
{
    public StructuralWallResult(IReadOnlyList<StructuralWallBlock> blocks,
                                int courseCount, int lintelCount, int bondFaults,
                                int protectedJointViolations,
                                double stoneVolume, double largestUnitLength,
                                double effectiveHeight, double density,
                                IReadOnlyList<string> warnings, string report)
    {
        Blocks = blocks; CourseCount = courseCount; LintelCount = lintelCount;
        BondFaults = bondFaults; ProtectedJointViolations = protectedJointViolations;
        StoneVolume = stoneVolume;
        LargestUnitLength = largestUnitLength; EffectiveHeight = effectiveHeight;
        Density = density; Warnings = warnings; Report = report;
    }

    /// <summary>Every stone, in laying order (course 0 first).</summary>
    public IReadOnlyList<StructuralWallBlock> Blocks { get; }
    public int BlockCount => Blocks.Count;
    /// <summary>Number of courses actually laid (Height snapped to CourseHeight).</summary>
    public int CourseCount { get; }
    public int LintelCount { get; }
    /// <summary>Head joints repeated in vertically adjacent solid courses. Opening reveals excluded.</summary>
    public int BondFaults { get; }
    /// <summary>
    /// Head joints left sitting under an opening jamb or over a lintel bearing.
    /// Zero for every wall the shipped defaults can lay; a non-zero value names
    /// each offender in <see cref="Warnings"/>.
    /// </summary>
    public int ProtectedJointViolations { get; }
    /// <summary>Total stone volume in model units cubed.</summary>
    public double StoneVolume { get; }
    /// <summary>Length of the longest single stone (the handling / hoisting constraint).</summary>
    public double LargestUnitLength { get; }
    /// <summary>CourseCount * CourseHeight. May differ from the requested Height.</summary>
    public double EffectiveHeight { get; }
    /// <summary>Density carried through from the options, used by <see cref="StructuralWallGenerator.BuildAssembly"/>.</summary>
    public double Density { get; }
    /// <summary>Non-fatal layout observations (openings off the course grid, overlapping lintels, refused merges).</summary>
    public IReadOnlyList<string> Warnings { get; }
    /// <summary>One-line-per-topic human-readable summary.</summary>
    public string Report { get; }
}

/// <summary>
/// Generates a rectangular coursed structural-stone wall: running bond, big
/// unreinforced blocks, openings cut out of the courses and spanned by lintel
/// stones, head joints protected under jambs and over lintel bearings. Pure
/// managed math; no Rhino dependency.
/// </summary>
public static class StructuralWallGenerator
{
    private const double Eps = 1e-9;
    private const double JointEps = 1e-6;

    // =========================================================================
    // Generate
    // =========================================================================

    /// <summary>Generate the wall for <paramref name="options"/>.</summary>
    public static StructuralWallResult Generate(StructuralWallOptions options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        double w = options.Width, h = options.Height, depth = options.Depth;
        double courseH = options.CourseHeight;
        if (!(w > 0)) throw new ArgumentOutOfRangeException(nameof(options), "Width must be > 0");
        if (!(h > 0)) throw new ArgumentOutOfRangeException(nameof(options), "Height must be > 0");
        if (!(depth > 0)) throw new ArgumentOutOfRangeException(nameof(options), "Depth must be > 0");
        if (!(courseH > 0)) throw new ArgumentOutOfRangeException(nameof(options), "CourseHeight must be > 0");
        if (!(options.NominalLength > 0))
            throw new ArgumentOutOfRangeException(nameof(options), "NominalLength must be > 0");
        if (!(options.Density > 0))
            throw new ArgumentOutOfRangeException(nameof(options), "Density must be > 0");

        double nominal = options.NominalLength;
        double minLen = Math.Max(0.0, options.MinLength);
        double maxLen = Math.Max(nominal, options.MaxLength);
        double bearing = Math.Max(0.0, options.LintelBearing);

        var warnings = new List<string>();
        var openings = SanitizeOpenings(options, w, courseH, warnings, out int nCourses);
        double effectiveHeight = nCourses * courseH;

        var blocks = new List<StructuralWallBlock>();
        var refusedJoints = new List<string>();
        // Head joints of the course below. Relocation prefers a position clear
        // of these, so protecting a jamb does not create a running joint (the
        // same head joint stacked in two courses) one course higher.
        var prevJoints = new List<double>();

        for (int c = 0; c < nCourses; c++)
        {
            double z0 = c * courseH, z1 = z0 + courseH;
            int courseStart = blocks.Count;

            // ---- 1. lintel stones bearing onto the masonry each side ----
            var lintelSpans = new List<Span>();
            if (options.Lintels)
            {
                for (int i = 0; i < openings.Count; i++)
                {
                    var op = openings[i];
                    if (Math.Abs(op.Z1 - z0) >= Eps) continue;
                    double lx0 = Math.Max(0.0, op.X0 - bearing);
                    double lx1 = Math.Min(w, op.X1 + bearing);
                    if (lx1 - lx0 <= Eps) continue;
                    blocks.Add(new StructuralWallBlock(lx0, 0.0, z0, lx1, depth, z1, c, true));
                    lintelSpans.Add(new Span(lx0, lx1));
                }
                WarnOverlappingLintels(lintelSpans, c, warnings);
            }

            // ---- 2. bands this course must not occupy ----
            var blocked = new List<Span>();
            for (int i = 0; i < openings.Count; i++)
            {
                var op = openings[i];
                if (op.Z0 < z1 - Eps && op.Z1 > z0 + Eps) blocked.Add(new Span(op.X0, op.X1));
            }
            blocked.AddRange(lintelSpans);

            // ---- 3-5. lay each solid span with bond + protection ----
            var protect = ProtectedJoints(openings, options.Lintels, z0, z1, w, courseH, bearing);
            var spans = SolidSpans(0.0, w, blocked);
            for (int i = 0; i < spans.Count; i++)
                LayCourse(blocks, spans[i].X0, spans[i].X1, z0, z1, depth, c,
                          nominal, minLen, maxLen, protect, prevJoints, refusedJoints);

            var joints = new List<double>();
            for (int i = courseStart; i < blocks.Count; i++)
                if (blocks[i].X1 < w - Eps) joints.Add(blocks[i].X1);
            prevJoints = joints;
        }

        // ---- metrics ----
        double volume = 0, largest = 0;
        int lintels = 0;
        for (int i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            volume += b.Volume;
            if (b.Length > largest) largest = b.Length;
            if (b.IsLintel) lintels++;
        }
        int faults = CountBondFaults(blocks, openings, w, courseH);
        var forcedJoints = new List<string>();
        int relocatable;
        int jambViolations = CountProtectedJointViolations(
            blocks, openings, options.Lintels, w, courseH, bearing, 0.5 * minLen,
            forcedJoints, out relocatable);

        if (forcedJoints.Count > 0)
            warnings.Add(forcedJoints.Count.ToString(CultureInfo.InvariantCulture) +
                         " head joint(s) sit under a jamb or over a lintel bearing at " +
                         string.Join(", ", forcedJoints.ToArray()) +
                         ", forced by the opening layout (a lintel end or an opening edge lines up " +
                         "with a jamb above). The bond cannot move these: shift the opening by half " +
                         "a stone, or change the lintel bearing.");

        if (refusedJoints.Count > 0)
            warnings.Add(refusedJoints.Count.ToString(CultureInfo.InvariantCulture) +
                         " protected head joint(s) KEPT at " + string.Join(", ", refusedJoints.ToArray()) +
                         ": merging across them would exceed MaxLength " +
                         maxLen.ToString("0.###", CultureInfo.InvariantCulture) +
                         ". Raise MaxLength or lower NominalLength to clear them.");

        string report = BuildReport(options, openings, blocks.Count, nCourses, lintels, faults,
                                    jambViolations, volume, largest, effectiveHeight, warnings);

        return new StructuralWallResult(blocks, nCourses, lintels, faults, jambViolations,
                                        volume, largest, effectiveHeight, options.Density,
                                        warnings, report);
    }

    // =========================================================================
    // Input sanitising + layout warnings
    // =========================================================================

    private struct Span
    {
        public Span(double x0, double x1) { X0 = x0; X1 = x1; }
        public double X0;
        public double X1;
    }

    private static List<StructuralWallOpening> SanitizeOpenings(
        StructuralWallOptions options, double w, double courseH,
        List<string> warnings, out int nCourses)
    {
        nCourses = (int)Math.Round(options.Height / courseH);
        if (nCourses < 1) nCourses = 1;
        double effectiveHeight = nCourses * courseH;
        if (Math.Abs(effectiveHeight - options.Height) > JointEps)
            warnings.Add($"Height {options.Height.ToString("0.###", CultureInfo.InvariantCulture)} is not a whole " +
                         $"number of {courseH.ToString("0.###", CultureInfo.InvariantCulture)} courses; " +
                         $"built at {effectiveHeight.ToString("0.###", CultureInfo.InvariantCulture)} " +
                         $"({nCourses} courses).");

        var result = new List<StructuralWallOpening>();
        if (options.Openings == null) return result;

        for (int i = 0; i < options.Openings.Count; i++)
        {
            var raw = options.Openings[i];
            if (raw == null) continue;
            string label = string.IsNullOrEmpty(raw.Name) ? "opening " + i.ToString(CultureInfo.InvariantCulture) : raw.Name;

            var op = new StructuralWallOpening(raw.X0, raw.X1, raw.Z0, raw.Z1, label);
            if (op.Width <= Eps || op.Height <= Eps)
            {
                warnings.Add(label + " has zero width or height; skipped.");
                continue;
            }
            if (op.X0 < -JointEps || op.X1 > w + JointEps)
                warnings.Add(label + " extends outside the wall width; the part outside is ignored.");

            // Bed-joint alignment. A lintel is generated only when the head sits
            // exactly on a bed joint, so pull a near-miss onto the grid rather
            // than silently dropping the lintel. Exact no-op when already on it.
            if (options.SnapOpeningsToCourses)
            {
                double snapTol = 0.25 * courseH;
                double z0s = SnapToGrid(op.Z0, courseH, snapTol);
                double z1s = SnapToGrid(op.Z1, courseH, snapTol);
                if (z1s - z0s > Eps &&
                    (Math.Abs(z0s - op.Z0) > JointEps || Math.Abs(z1s - op.Z1) > JointEps))
                {
                    warnings.Add(label + " snapped to bed joints: Z " +
                                 op.Z0.ToString("0.###", CultureInfo.InvariantCulture) + ".." +
                                 op.Z1.ToString("0.###", CultureInfo.InvariantCulture) + " -> " +
                                 z0s.ToString("0.###", CultureInfo.InvariantCulture) + ".." +
                                 z1s.ToString("0.###", CultureInfo.InvariantCulture) + ".");
                    op = new StructuralWallOpening(op.X0, op.X1, z0s, z1s, label);
                }
            }

            if (op.Z1 > effectiveHeight + JointEps)
                warnings.Add(label + " reaches or passes the top of the wall; no lintel course exists above it.");
            if (!OnCourseGrid(op.Z1, courseH))
                warnings.Add(label + " head Z1=" + op.Z1.ToString("0.###", CultureInfo.InvariantCulture) +
                             " is not on a bed joint (multiple of " +
                             courseH.ToString("0.###", CultureInfo.InvariantCulture) +
                             "); it snaps to the whole course and NO lintel is generated.");
            if (!op.IsDoor && !OnCourseGrid(op.Z0, courseH))
                warnings.Add(label + " sill Z0=" + op.Z0.ToString("0.###", CultureInfo.InvariantCulture) +
                             " is not on a bed joint; it snaps to the whole course.");

            result.Add(op);
        }
        return result;
    }

    private static bool OnCourseGrid(double z, double courseH)
    {
        double k = Math.Round(z / courseH);
        return Math.Abs(k * courseH - z) < JointEps;
    }

    /// <summary>Nearest multiple of <paramref name="courseH"/> when within <paramref name="tol"/>; otherwise unchanged.</summary>
    private static double SnapToGrid(double z, double courseH, double tol)
    {
        double snapped = Math.Round(z / courseH) * courseH;
        return Math.Abs(snapped - z) <= tol ? snapped : z;
    }

    /// <summary>
    /// Two lintels whose bearings run into each other would be two solids in
    /// the same place. The generator does NOT clamp them (behaviour preserved
    /// from the demonstrator); it reports the condition instead.
    /// </summary>
    private static void WarnOverlappingLintels(List<Span> lintelSpans, int course, List<string> warnings)
    {
        for (int a = 0; a < lintelSpans.Count; a++)
        {
            for (int b = a + 1; b < lintelSpans.Count; b++)
            {
                double lo = Math.Max(lintelSpans[a].X0, lintelSpans[b].X0);
                double hi = Math.Min(lintelSpans[a].X1, lintelSpans[b].X1);
                if (hi - lo > JointEps)
                    warnings.Add($"course {course.ToString(CultureInfo.InvariantCulture)}: two lintels overlap by " +
                                 (hi - lo).ToString("0.###", CultureInfo.InvariantCulture) +
                                 " (their bearings run into each other). Reduce LintelBearing or move the openings apart.");
            }
        }
    }

    // =========================================================================
    // Layout
    // =========================================================================

    /// <summary>
    /// x positions where a head joint would be a genuine masonry fault in this
    /// course, so the layout merges across them instead:
    ///   * directly UNDER an opening jamb - the jamb must bear on solid stone.
    ///     A DOOR has no course beneath it, so this does not apply to doors;
    ///   * directly OVER a lintel bearing - the lintel end must be built over
    ///     solid stone, not a joint.
    /// The opening reveal itself is a straight vertical line by definition and
    /// is not a fault; it is excluded from the fault count too.
    /// </summary>
    private static List<double> ProtectedJoints(
        List<StructuralWallOpening> openings, bool lintels,
        double z0, double z1, double w, double courseH, double bearing)
    {
        var p = new List<double>();
        for (int i = 0; i < openings.Count; i++)
        {
            var op = openings[i];

            // course below the sill. A door runs to the ground: there is no
            // course below it, and its jambs bear on the foundation, so the
            // protection is skipped explicitly.
            if (!op.IsDoor && Math.Abs(op.Z0 - z1) < Eps) { p.Add(op.X0); p.Add(op.X1); }

            // course above the lintel
            if (lintels && Math.Abs(op.Z1 + courseH - z0) < Eps)
            {
                p.Add(Math.Max(0.0, op.X0 - bearing));
                p.Add(Math.Min(w, op.X1 + bearing));
            }
        }
        return p;
    }

    private static List<Span> SolidSpans(double lo, double hi, List<Span> blocked)
    {
        var cuts = new List<Span>(blocked);
        cuts.Sort((a, b) => a.X0.CompareTo(b.X0));
        var spans = new List<Span>();
        double cur = lo;
        for (int i = 0; i < cuts.Count; i++)
        {
            var b = cuts[i];
            if (b.X0 > cur + Eps) spans.Add(new Span(cur, Math.Min(b.X0, hi)));
            cur = Math.Max(cur, b.X1);
        }
        if (cur < hi - Eps) spans.Add(new Span(cur, hi));
        return spans;
    }

    private static void LayCourse(List<StructuralWallBlock> blocks,
        double x0, double x1, double z0, double z1, double depth, int course,
        double nominal, double minLen, double maxLen, List<double> protect,
        List<double> prevJoints, List<string> refusedJoints)
    {
        double runLen = x1 - x0;
        if (runLen < minLen - Eps)
        {
            if (runLen > Eps)
                blocks.Add(new StructuralWallBlock(x0, 0.0, z0, x1, depth, z1, course, false));
            return;
        }

        // running bond: odd courses start with a half block
        var edges = new List<double> { x0 };
        double first = (course % 2 == 1) ? nominal * 0.5 : nominal;
        double cur = x0 + Math.Min(first, runLen);
        while (cur < x1 - Eps) { edges.Add(cur); cur += nominal; }
        edges.Add(x1);

        // trailing sliver -> merge into its neighbour
        if (edges.Count >= 3 && edges[edges.Count - 1] - edges[edges.Count - 2] < minLen - Eps)
            edges.RemoveAt(edges.Count - 2);

        // Joint protection. A head joint on (or hard against) a protected x is a
        // genuine masonry fault, resolved in the least damaging order:
        //
        //   1. MOVE the joint to the nearest clear position that keeps both
        //      neighbouring stones within [MinLength, MaxLength]. This is what a
        //      mason does - adjust the two stone lengths locally - and it keeps
        //      the units uniform.
        //   2. Failing that, MERGE the two stones into one, capped at MaxLength.
        //   3. Failing both, KEEP the joint and report it. A refused joint is a
        //      real fault; it is counted and named, never silently dropped.
        //
        // Merge-only (the demonstrator's original rule) cannot clear two
        // protected joints one nominal apart when MaxLength = 2*NominalLength:
        // clearing the first grows a stone to the cap, so the second is refused.
        // Relocation has no such failure mode at the shipped defaults.
        double clear = 0.5 * minLen;
        for (int i = edges.Count - 2; i >= 1; i--)
        {
            if (!IsProtected(edges[i], protect, clear)) continue;

            double lo = edges[i - 1], hi = edges[i + 1];
            double moved;
            if (TryRelocateJoint(lo, hi, edges[i], minLen, maxLen, protect, prevJoints, clear, out moved))
            {
                edges[i] = moved;
                continue;
            }
            if (hi - lo <= maxLen + Eps) { edges.RemoveAt(i); continue; }
            refusedJoints.Add("course " + course.ToString(CultureInfo.InvariantCulture) +
                              " x=" + edges[i].ToString("0.###", CultureInfo.InvariantCulture));
        }

        for (int i = 0; i + 1 < edges.Count; i++)
            blocks.Add(new StructuralWallBlock(edges[i], 0.0, z0, edges[i + 1], depth, z1, course, false));
    }

    /// <summary>
    /// True when x is on, or within <paramref name="clear"/> of, a protected
    /// position. A joint a few millimetres off a jamb is the same fault as one
    /// exactly under it, so proximity counts, not just coincidence.
    /// </summary>
    private static bool IsProtected(double x, List<double> protect, double clear)
    {
        double tol = Math.Max(JointEps, clear);
        for (int k = 0; k < protect.Count; k++)
            if (Math.Abs(x - protect[k]) < tol) return true;
        return false;
    }

    /// <summary>
    /// Nearest position to <paramref name="orig"/> at which the joint between
    /// stones [lo,x] and [x,hi] is clear of every protected position, both
    /// stones stay within [minLen, maxLen], and — where possible — the joint
    /// does not stack on one in the course below.
    /// </summary>
    /// <remarks>
    /// The admissible set is the closed interval
    /// [max(lo+minLen, hi-maxLen), min(hi-minLen, lo+maxLen)] minus an open
    /// ball of radius <paramref name="clear"/> around each protected position.
    /// The closest point of such a set is attained either at an interval end or
    /// at a ball boundary, so testing that finite candidate list is exact — no
    /// sampling, no tolerance sweep. <paramref name="prevJoints"/> adds more
    /// candidate boundaries and is a SOFT preference: candidates clear of the
    /// course below win, and only if none exists does a stacking position get
    /// used. Clearing a jamb is the harder requirement of the two, since a
    /// running joint weakens the bond while a joint under a jamb loses the
    /// bearing outright.
    /// </remarks>
    private static bool TryRelocateJoint(double lo, double hi, double orig,
        double minLen, double maxLen, List<double> protect, List<double> prevJoints,
        double clear, out double moved)
    {
        moved = orig;
        double a = Math.Max(lo + minLen, hi - maxLen);
        double b = Math.Min(hi - minLen, lo + maxLen);
        if (a > b + Eps) return false;

        var candidates = new List<double>(2 * (protect.Count + prevJoints.Count) + 2) { a, b };
        AddBoundaryCandidates(candidates, protect, clear, a, b);
        AddBoundaryCandidates(candidates, prevJoints, clear, a, b);

        bool found = false, bestStacks = true;
        double best = 0, bestDist = double.MaxValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            double x = Math.Min(Math.Max(candidates[i], a), b);
            if (IsProtected(x, protect, clear)) continue;           // hard
            bool stacks = IsProtected(x, prevJoints, clear);        // soft
            double d = Math.Abs(x - orig);
            // lexicographic: avoid stacking first, then stay closest to the
            // set-out position so stone lengths remain as uniform as possible.
            if (!found || (bestStacks && !stacks) ||
                (bestStacks == stacks && d < bestDist))
            {
                bestStacks = stacks; bestDist = d; best = x; found = true;
            }
        }
        if (found) moved = best;
        return found;
    }

    private static void AddBoundaryCandidates(List<double> candidates,
        List<double> centres, double clear, double a, double b)
    {
        for (int k = 0; k < centres.Count; k++)
        {
            double c0 = centres[k] - clear, c1 = centres[k] + clear;
            if (c0 >= a - Eps && c0 <= b + Eps) candidates.Add(c0);
            if (c1 >= a - Eps && c1 <= b + Eps) candidates.Add(c1);
        }
    }

    // =========================================================================
    // Bond faults
    // =========================================================================

    /// <summary>
    /// Head joints landing on a protected position (under an opening jamb, or
    /// over a lintel bearing) despite the protection pass. This measures the
    /// thing <see cref="ProtectedJoints"/> exists to prevent; the running-joint
    /// counter in <see cref="CountBondFaults"/> does NOT look for it, which is
    /// how such a joint survived undetected in the demonstrator's own facade.
    ///
    /// Two kinds come out of this, and only one is the generator's to fix:
    ///
    ///   RELOCATABLE — an ordinary bond joint between two laid stones. The
    ///     layout controls where it falls, so it must always be cleared. A
    ///     non-zero count here is a generator bug.
    ///   FORCED — a joint the opening layout itself dictates: the end of a
    ///     lintel, or a course boundary against an opening void, that happens
    ///     to line up with a jamb above. No bond choice can move it; only
    ///     moving the opening or changing LintelBearing can. These are reported
    ///     by name so the designer can act, never silently absorbed.
    /// </summary>
    private static int CountProtectedJointViolations(List<StructuralWallBlock> blocks,
        List<StructuralWallOpening> openings, bool lintels, double w, double courseH,
        double bearing, double clear, List<string> forced, out int relocatable)
    {
        relocatable = 0;
        int bad = 0;
        for (int i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            if (b.X1 >= w - Eps) continue;   // the wall end is not a joint
            double z0 = b.Course * courseH, z1 = z0 + courseH;
            var protect = ProtectedJoints(openings, lintels, z0, z1, w, courseH, bearing);
            if (!IsProtected(b.X1, protect, clear)) continue;

            bad++;
            if (IsForcedJoint(blocks, b))
                forced.Add("course " + b.Course.ToString(CultureInfo.InvariantCulture) +
                           " x=" + b.X1.ToString("0.###", CultureInfo.InvariantCulture));
            else
                relocatable++;
        }
        return bad;
    }

    /// <summary>
    /// True when the joint at <paramref name="b"/>'s right edge is dictated by
    /// the opening layout rather than by the bond: it abuts an opening void, or
    /// either side of it is a lintel. Such a joint cannot be moved by choosing
    /// stone lengths.
    /// </summary>
    private static bool IsForcedJoint(List<StructuralWallBlock> blocks, StructuralWallBlock b)
    {
        if (b.IsLintel) return true;
        for (int i = 0; i < blocks.Count; i++)
        {
            var n = blocks[i];
            if (n.Course != b.Course) continue;
            if (Math.Abs(n.X0 - b.X1) < JointEps) return n.IsLintel;   // neighbour found
        }
        return true;   // nothing adjoins: the joint abuts an opening void
    }

    /// <summary>
    /// Genuine bond faults: a head joint repeated in vertically adjacent courses
    /// where BOTH courses are solid there. Opening reveals (the straight
    /// vertical edge of a window or door) are excluded - those are geometry,
    /// not a bond fault.
    /// </summary>
    private static int CountBondFaults(List<StructuralWallBlock> blocks,
        List<StructuralWallOpening> openings, double w, double courseH)
    {
        var byCourse = new Dictionary<int, List<StructuralWallBlock>>();
        for (int i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            List<StructuralWallBlock> l;
            if (!byCourse.TryGetValue(b.Course, out l)) { l = new List<StructuralWallBlock>(); byCourse[b.Course] = l; }
            l.Add(b);
        }

        int faults = 0;
        foreach (var kv in byCourse)
        {
            List<StructuralWallBlock> above;
            if (!byCourse.TryGetValue(kv.Key + 1, out above)) continue;

            var lowJoints = new HashSet<double>();
            for (int i = 0; i < kv.Value.Count; i++)
            {
                var b = kv.Value[i];
                if (b.X1 < w - Eps) lowJoints.Add(Math.Round(b.X1, 6));
            }
            for (int i = 0; i < above.Count; i++)
            {
                var a = above[i];
                if (a.X1 >= w - Eps) continue;
                double x = Math.Round(a.X1, 6);
                if (!lowJoints.Contains(x)) continue;
                double zLow = (kv.Key + 0.5) * courseH, zUp = (kv.Key + 1.5) * courseH;
                if (IsReveal(openings, x, zLow) || IsReveal(openings, x, zUp)) continue;
                faults++;
            }
        }
        return faults;
    }

    private static bool IsReveal(List<StructuralWallOpening> openings, double x, double zMid)
    {
        for (int i = 0; i < openings.Count; i++)
        {
            var op = openings[i];
            if ((Math.Abs(op.X0 - x) < JointEps || Math.Abs(op.X1 - x) < JointEps)
                && op.Z0 < zMid && op.Z1 > zMid) return true;
        }
        return false;
    }

    // =========================================================================
    // Report
    // =========================================================================

    private static string BuildReport(StructuralWallOptions o, List<StructuralWallOpening> openings,
        int blockCount, int courses, int lintels, int faults, int jambViolations,
        double volume, double largest,
        double effectiveHeight, List<string> warnings)
    {
        var inv = CultureInfo.InvariantCulture;
        int doors = 0;
        for (int i = 0; i < openings.Count; i++) if (openings[i].IsDoor) doors++;

        var sb = new StringBuilder();
        sb.Append("structural stone wall (self-supporting single skin) | ")
          .Append(o.Width.ToString("0.###", inv)).Append(" x ")
          .Append(effectiveHeight.ToString("0.###", inv)).Append(" x ")
          .Append(o.Depth.ToString("0.###", inv)).Append(" m\n");
        sb.Append("courses ").Append(courses.ToString(inv))
          .Append(" @ ").Append(o.CourseHeight.ToString("0.###", inv))
          .Append(" | blocks ").Append(blockCount.ToString(inv))
          .Append(" | lintels ").Append(lintels.ToString(inv))
          .Append(" | running joints ").Append(faults.ToString(inv))
          .Append(" | joints under a jamb / over a bearing ").Append(jambViolations.ToString(inv)).Append('\n');
        sb.Append("stone ").Append(volume.ToString("0.00", inv)).Append(" m3 (")
          .Append((volume * o.Density / 1000.0).ToString("0.0", inv)).Append(" t at ")
          .Append(o.Density.ToString("0", inv)).Append(" kg/m3)")
          .Append(" | largest unit ").Append(largest.ToString("0.00", inv)).Append(" m\n");
        sb.Append("openings ").Append(openings.Count.ToString(inv))
          .Append(" (doors ").Append(doors.ToString(inv)).Append(')')
          .Append(" | nominal ").Append(o.NominalLength.ToString("0.###", inv))
          .Append(" | min ").Append(o.MinLength.ToString("0.###", inv))
          .Append(" | max ").Append(o.MaxLength.ToString("0.###", inv))
          .Append(" | lintel bearing ").Append(o.LintelBearing.ToString("0.###", inv));

        if (warnings.Count > 0)
        {
            sb.Append('\n');
            for (int i = 0; i < warnings.Count; i++)
                sb.Append("note: ").Append(warnings[i]).Append(i + 1 < warnings.Count ? "\n" : "");
        }
        return sb.ToString();
    }

    // =========================================================================
    // Rhino-free geometry / assembly helpers
    // =========================================================================

    /// <summary>
    /// Flat triangle mesh of one block: 8 vertices, 12 outward-facing
    /// triangles, closed and manifold. Same buffer convention as
    /// <see cref="MasonryBlock"/> and <see cref="MeshSnapshot"/>.
    /// </summary>
    public static void BoxMesh(StructuralWallBlock b, out List<double> coords, out List<int> triangles)
    {
        if (b == null) throw new ArgumentNullException(nameof(b));
        coords = new List<double>
        {
            b.X0, b.Y0, b.Z0,  b.X1, b.Y0, b.Z0,  b.X1, b.Y1, b.Z0,  b.X0, b.Y1, b.Z0,
            b.X0, b.Y0, b.Z1,  b.X1, b.Y0, b.Z1,  b.X1, b.Y1, b.Z1,  b.X0, b.Y1, b.Z1,
        };
        triangles = new List<int>
        {
            0, 2, 1,  0, 3, 2,    4, 5, 6,  4, 6, 7,
            0, 1, 5,  0, 5, 4,    2, 3, 7,  2, 7, 6,
            0, 4, 7,  0, 7, 3,    1, 2, 6,  1, 6, 5,
        };
    }

    /// <summary>
    /// Build the exact-joint structural assembly for a generated wall:
    /// contacts from <see cref="MeshContactDetector"/> (its coplanar-coincidence
    /// resolver recovers the true shared rectangle on exact face-to-face
    /// contacts, KB-9) and boundary conditions fixing every block of the lowest
    /// course. Feeds Masonry Stability Check and Block Build Order directly.
    /// </summary>
    /// <param name="result">A wall from <see cref="Generate"/>.</param>
    /// <param name="fixedBlockCount">Number of blocks pinned as the foundation.</param>
    /// <param name="idPrefix">Block-id prefix; ids are prefix + zero-padded index.</param>
    public static MasonryAssembly BuildAssembly(StructuralWallResult result,
        out int fixedBlockCount, string idPrefix = "s")
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        if (string.IsNullOrEmpty(idPrefix)) idPrefix = "s";
        fixedBlockCount = 0;
        if (result.Blocks.Count == 0)
            return new MasonryAssembly(new List<MasonryBlock>(),
                                       new List<MasonryInterface>(),
                                       new BoundaryConditions(new List<string>()));

        int n = result.Blocks.Count;
        var snapshots = new List<MeshSnapshot>(n);
        var ids = new List<string>(n);
        var coordsList = new List<List<double>>(n);
        var trisList = new List<List<int>>(n);

        double globalMinZ = double.MaxValue;
        for (int i = 0; i < n; i++)
            if (result.Blocks[i].Z0 < globalMinZ) globalMinZ = result.Blocks[i].Z0;

        for (int i = 0; i < n; i++)
        {
            List<double> coords;
            List<int> tris;
            BoxMesh(result.Blocks[i], out coords, out tris);
            coordsList.Add(coords);
            trisList.Add(tris);
            snapshots.Add(new MeshSnapshot(coords, tris));
            ids.Add(idPrefix + i.ToString("000", CultureInfo.InvariantCulture));
        }

        var interfaces = MeshContactDetector.Detect(snapshots, ids);

        var blocks = new List<MasonryBlock>(n);
        var fixedIds = new List<string>();
        for (int i = 0; i < n; i++)
        {
            blocks.Add(new MasonryBlock(ids[i], coordsList[i], trisList[i], result.Density));
            if (result.Blocks[i].Z0 <= globalMinZ + JointEps) fixedIds.Add(ids[i]);
        }
        fixedBlockCount = fixedIds.Count;
        return new MasonryAssembly(blocks, interfaces, new BoundaryConditions(fixedIds));
    }
}
