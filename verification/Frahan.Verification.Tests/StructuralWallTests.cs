using System;
using System.Collections.Generic;
using Xunit;
using Frahan.Masonry.Sequencing;

namespace Frahan.Verification.Tests
{
    /// <summary>
    /// Verifies <c>StructuralWallGenerator.Generate</c> — the rectangular coursed
    /// structural-stone wall (Stone Federation "A Guide to Structural Stone",
    /// June 2026, self-supporting single skin).
    ///
    /// WHY THIS SUITE EXISTS. The generator's jamb/bearing joint protection was
    /// shipped, documented as "0 bond faults", and was NOT holding: in the
    /// demonstrator's own hero facade a head joint sat directly under a window
    /// jamb. The merge-only rule could not clear two protected joints one nominal
    /// apart (clearing the first grew a stone to MaxLength, so the second was
    /// silently refused), and the metric being reported counted a DIFFERENT fault
    /// (running joints), so nothing looked. T3 below is exactly the missing check.
    ///
    /// Independent oracles only: protected positions are recomputed here from the
    /// opening geometry, and occupancy is measured by direct interval arithmetic
    /// on the returned blocks. The generator's own BondFaults /
    /// ProtectedJointViolations counters are never used as the oracle — T3b
    /// asserts the counter agrees with the independent count, so the reported
    /// number is verified rather than trusted.
    ///
    /// Facts:
    ///   T1 NO OVERLAP  — no two stones share interior volume; each course's
    ///                    stones tile exactly the solid part of that course.
    ///   T2 OPENINGS    — no stone intrudes into any opening void (doors included).
    ///   T3 PROTECTION  — no head joint under an opening jamb or over a lintel
    ///                    bearing; and the reported counter equals the truth.
    ///   T4 UNIT SIZE   — every stone length lies within [MinLength, MaxLength].
    ///   T5 DETERMINISM — identical options ⇒ bit-identical geometry.
    /// All run across a matrix of wall sizes × opening layouts including doors.
    /// </summary>
    public class StructuralWallTests
    {
        const double Tol = 1e-7;

        /// <summary>
        /// Wall cases on the course grid (so bed-joint snapping is an exact
        /// no-op and the cases exercise layout, not the snap). Includes the
        /// shipped hero facade, doors reaching the ground, an opening pair one
        /// nominal apart (the case that defeated merge-only protection), and a
        /// full architectural elevation with a door and two windows.
        /// </summary>
        public static IEnumerable<object[]> Cases()
        {
            yield return Case("solid", 7.2, 5.4);
            yield return Case("hero facade", 7.2, 5.4,
                (1.2, 3.0, 1.2, 3.0), (4.2, 6.0, 1.2, 3.0));
            yield return Case("door only", 6.0, 4.8, (2.4, 3.6, 0.0, 2.4));
            yield return Case("door + 2 windows", 9.0, 6.0,
                (3.6, 4.8, 0.0, 2.4), (1.2, 3.0, 3.0, 4.8), (6.0, 7.8, 3.0, 4.8));
            // A1 — the shipped architectural elevation (example 37). Pinned here
            // so the flagship geometry is covered by every fact in this class.
            yield return Case("A1 architectural elevation", 9.0, 6.0,
                (3.6, 5.4, 0.0, 2.4), (1.2, 2.4, 3.0, 4.8), (6.6, 7.8, 3.0, 4.8));
            yield return Case("jambs one nominal apart", 7.2, 4.8,
                (1.8, 3.0, 1.2, 2.4), (4.2, 5.4, 1.2, 2.4));
            yield return Case("opening at the wall end", 6.0, 4.2, (0.0, 1.8, 1.2, 3.0));
            yield return Case("tall narrow with door", 4.8, 7.2, (1.8, 3.0, 0.0, 3.0));
            yield return Case("three windows one course", 10.8, 4.8,
                (1.2, 2.4, 1.2, 3.0), (4.2, 5.4, 1.2, 3.0), (7.2, 8.4, 1.2, 3.0));
        }

        static object[] Case(string name, double w, double h,
            params (double x0, double x1, double z0, double z1)[] openings)
        {
            var o = new StructuralWallOptions
            {
                Width = w, Height = h, Depth = 0.6, CourseHeight = 0.6,
                NominalLength = 1.2, MinLength = 0.6, MaxLength = 2.4,
                LintelBearing = 0.6, Lintels = true, Density = 2650.0
            };
            foreach (var op in openings)
                o.Openings.Add(new StructuralWallOpening(op.x0, op.x1, op.z0, op.z1));
            return new object[] { name, o };
        }

        // ---------------------------------------------------------------------
        // T1 — no two stones overlap, and each course tiles its solid part
        // ---------------------------------------------------------------------

        [Theory]
        [MemberData(nameof(Cases))]
        public void T1_Stones_Do_Not_Overlap(string name, StructuralWallOptions o)
        {
            var r = StructuralWallGenerator.Generate(o);
            Assert.NotEmpty(r.Blocks);

            for (int i = 0; i < r.Blocks.Count; i++)
                for (int j = i + 1; j < r.Blocks.Count; j++)
                {
                    var a = r.Blocks[i];
                    var b = r.Blocks[j];
                    double ox = Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0);
                    double oz = Math.Min(a.Z1, b.Z1) - Math.Max(a.Z0, b.Z0);
                    Assert.False(ox > Tol && oz > Tol,
                        $"{name}: stones {i} and {j} overlap by {ox:0.###} x {oz:0.###}");
                }

            // volume identity: total stone = wall volume minus every opening's
            // volume, computed independently of the generator's own sum.
            double wallVol = o.Width * r.EffectiveHeight * o.Depth;
            double voidVol = 0;
            foreach (var op in o.Openings)
            {
                double x0 = Math.Max(0, op.X0), x1 = Math.Min(o.Width, op.X1);
                double z0 = Math.Max(0, op.Z0), z1 = Math.Min(r.EffectiveHeight, op.Z1);
                if (x1 > x0 && z1 > z0) voidVol += (x1 - x0) * (z1 - z0) * o.Depth;
            }
            double got = 0;
            foreach (var b in r.Blocks) got += b.Volume;
            Assert.True(Math.Abs(got - (wallVol - voidVol)) < 1e-6,
                $"{name}: stone volume {got:0.####} != wall {wallVol:0.####} - voids {voidVol:0.####}");
        }

        // ---------------------------------------------------------------------
        // T2 — nothing intrudes into an opening
        // ---------------------------------------------------------------------

        [Theory]
        [MemberData(nameof(Cases))]
        public void T2_No_Stone_Intrudes_Into_An_Opening(string name, StructuralWallOptions o)
        {
            var r = StructuralWallGenerator.Generate(o);
            foreach (var op in o.Openings)
                foreach (var b in r.Blocks)
                {
                    double ox = Math.Min(b.X1, op.X1) - Math.Max(b.X0, op.X0);
                    double oz = Math.Min(b.Z1, op.Z1) - Math.Max(b.Z0, op.Z0);
                    Assert.False(ox > Tol && oz > Tol,
                        $"{name}: a stone intrudes {ox:0.###} x {oz:0.###} into the " +
                        $"opening [{op.X0},{op.X1}]x[{op.Z0},{op.Z1}]");
                }
        }

        // ---------------------------------------------------------------------
        // T3 — the invariant that was silently violated in the shipped facade
        // ---------------------------------------------------------------------

        /// <summary>
        /// Independent recomputation of the positions where a head joint is a
        /// fault, straight from the opening geometry — deliberately NOT calling
        /// the generator's private ProtectedJoints.
        /// </summary>
        static List<double> ProtectedAt(StructuralWallOptions o, double z0, double z1)
        {
            var p = new List<double>();
            foreach (var op in o.Openings)
            {
                bool isDoor = op.Z0 <= 1e-9;
                // course directly below a window sill: the jambs bear here.
                // A door has no course below it — its jambs bear on the ground.
                if (!isDoor && Math.Abs(op.Z0 - z1) < 1e-9) { p.Add(op.X0); p.Add(op.X1); }
                // course directly above a lintel: the lintel ends bear here.
                if (o.Lintels && Math.Abs(op.Z1 + o.CourseHeight - z0) < 1e-9)
                {
                    p.Add(Math.Max(0.0, op.X0 - o.LintelBearing));
                    p.Add(Math.Min(o.Width, op.X1 + o.LintelBearing));
                }
            }
            return p;
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void T3_No_Head_Joint_Under_A_Jamb_Or_Over_A_Bearing(
            string name, StructuralWallOptions o)
        {
            var r = StructuralWallGenerator.Generate(o);
            double clear = 0.5 * o.MinLength;

            var relocatable = new List<string>();
            int total = 0;
            foreach (var b in r.Blocks)
            {
                if (b.X1 >= o.Width - 1e-9) continue;      // the wall end is not a joint
                double z0 = b.Course * o.CourseHeight;
                bool hit = false;
                foreach (double p in ProtectedAt(o, z0, z0 + o.CourseHeight))
                    if (Math.Abs(b.X1 - p) < clear) { hit = true; break; }
                if (!hit) continue;

                total++;
                if (!IsForced(r, b))
                    relocatable.Add($"course {b.Course} joint x={b.X1:0.###}");
            }

            // The contract: the generator must clear every joint the BOND
            // controls. A joint forced by the opening layout (a lintel end or an
            // opening edge lining up with a jamb above) cannot be moved by
            // choosing stone lengths — it is reported to the designer instead.
            Assert.True(relocatable.Count == 0,
                $"{name}: {relocatable.Count} relocatable head joint(s) left on a protected " +
                "position — " + string.Join("; ", relocatable));

            // T3b — the reported counter must equal the independent count, so
            // the number the component shows is verified, not trusted.
            Assert.Equal(total, r.ProtectedJointViolations);

            // T3c — anything that IS reported must be explained to the user.
            if (total > 0)
                Assert.Contains(r.Warnings, x => x.Contains("forced by the opening layout"));
        }

        /// <summary>
        /// Independent re-derivation of "the layout forced this joint": it abuts
        /// an opening void (nothing in the course adjoins it) or a lintel is on
        /// one side of it. Deliberately not the generator's own classifier.
        /// </summary>
        static bool IsForced(StructuralWallResult r, StructuralWallBlock b)
        {
            if (b.IsLintel) return true;
            foreach (var n in r.Blocks)
            {
                if (n.Course != b.Course) continue;
                if (Math.Abs(n.X0 - b.X1) < 1e-6) return n.IsLintel;
            }
            return true;
        }

        // ---------------------------------------------------------------------
        // T4 — unit sizes stay within the handling window
        // ---------------------------------------------------------------------

        [Theory]
        [MemberData(nameof(Cases))]
        public void T4_Stone_Lengths_Are_Within_Bounds(string name, StructuralWallOptions o)
        {
            var r = StructuralWallGenerator.Generate(o);
            foreach (var b in r.Blocks)
            {
                if (b.IsLintel) continue;      // a lintel is sized by its span, not the bond
                Assert.True(b.Length <= o.MaxLength + Tol,
                    $"{name}: stone at course {b.Course} x=[{b.X0:0.###},{b.X1:0.###}] is " +
                    $"{b.Length:0.###} long, over MaxLength {o.MaxLength}");
                // A solid span shorter than MinLength yields one short stone by
                // design (there is nothing to merge it into); that is the only
                // admissible under-length case.
                bool spanIsShort = b.Length < o.MinLength - Tol;
                if (spanIsShort)
                    Assert.True(IsIsolatedShortSpan(r, b, o),
                        $"{name}: stone at course {b.Course} x=[{b.X0:0.###},{b.X1:0.###}] is " +
                        $"{b.Length:0.###} long, under MinLength {o.MinLength}, and is not an " +
                        "isolated short span");
            }
        }

        /// <summary>A stone is an isolated short span when nothing in its course adjoins it.</summary>
        static bool IsIsolatedShortSpan(StructuralWallResult r, StructuralWallBlock b,
            StructuralWallOptions o)
        {
            foreach (var other in r.Blocks)
            {
                if (ReferenceEquals(other, b) || other.Course != b.Course) continue;
                if (Math.Abs(other.X1 - b.X0) < Tol || Math.Abs(other.X0 - b.X1) < Tol) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------------
        // T5 — determinism
        // ---------------------------------------------------------------------

        [Theory]
        [MemberData(nameof(Cases))]
        public void T5_Generation_Is_Deterministic(string name, StructuralWallOptions o)
        {
            var a = StructuralWallGenerator.Generate(o);
            var b = StructuralWallGenerator.Generate(o);
            Assert.True(a.Blocks.Count == b.Blocks.Count,
                $"{name}: block count differs between identical runs");
            for (int i = 0; i < a.Blocks.Count; i++)
            {
                Assert.Equal(a.Blocks[i].X0, b.Blocks[i].X0, 12);
                Assert.Equal(a.Blocks[i].X1, b.Blocks[i].X1, 12);
                Assert.Equal(a.Blocks[i].Z0, b.Blocks[i].Z0, 12);
                Assert.Equal(a.Blocks[i].Z1, b.Blocks[i].Z1, 12);
                Assert.Equal(a.Blocks[i].IsLintel, b.Blocks[i].IsLintel);
            }
            Assert.Equal(a.Report, b.Report);
        }

        // ---------------------------------------------------------------------
        // T6 — the regression that started this suite, pinned as a named fact
        // ---------------------------------------------------------------------

        /// <summary>
        /// The shipped hero facade, course 1: window W1's right jamb stands at
        /// x=3.0 and the running bond puts a head joint there. Merge-only
        /// protection refused it (the merge needed a 3.6 m stone against a 2.4 m
        /// cap) and the wall shipped with the fault. Relocation must clear it
        /// without exceeding the cap.
        /// </summary>
        [Fact]
        public void T6_Hero_Facade_Course1_Clears_The_W1_Right_Jamb()
        {
            var o = (StructuralWallOptions)Case("hero", 7.2, 5.4,
                (1.2, 3.0, 1.2, 3.0), (4.2, 6.0, 1.2, 3.0))[1];
            var r = StructuralWallGenerator.Generate(o);

            foreach (var b in r.Blocks)
            {
                if (b.Course != 1 || b.IsLintel) continue;
                Assert.True(Math.Abs(b.X1 - 3.0) > 0.5 * o.MinLength,
                    $"head joint at x={b.X1:0.###} sits under W1's right jamb (x=3.0)");
                Assert.True(Math.Abs(b.X1 - 4.2) > 0.5 * o.MinLength,
                    $"head joint at x={b.X1:0.###} sits under W2's left jamb (x=4.2)");
                Assert.True(b.Length <= o.MaxLength + Tol,
                    $"clearing the jamb produced a {b.Length:0.###} m stone, over the cap");
            }
        }
    }
}
