using System;
using System.Collections.Generic;
using Xunit;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Quarry.CutOpt;

namespace Frahan.Verification.Tests
{
    /// <summary>
    /// Verifies <c>SawBedScheduler.Schedule</c> — the saw-bed load balancer that
    /// assigns accepted quarry blocks to <c>m</c> identical saw beds. The ACTUAL
    /// algorithm (read from <c>SawBedScheduler.cs</c>) is greedy LPT: sort the
    /// accepted blocks by DESCENDING estimated cutting time (ties broken by
    /// extraction order), then place each on the currently LEAST-LOADED bed. This
    /// is Graham's LPT list scheduling for makespan on identical machines
    /// (Graham 1969).
    ///
    /// Checked against <c>frahan_proofs/FrahanProofs/Machines.lean</c> +
    /// <c>LptOptimal.lean</c> (tex <c>thm:lpt</c>):
    ///   T1 VALIDITY      — per bed, slots do not overlap in time and are ordered
    ///                      (each slot starts at the previous slot's end; setup 0),
    ///                      every job is scheduled exactly once, and the reported
    ///                      makespan equals the max bed load.
    ///   T2 LOWER BOUNDS  — makespan ≥ max single-job duration (<c>opt_ge_max_job</c>)
    ///                      AND makespan ≥ totalWork / beds (<c>opt_ge_avg</c>).
    ///   T3 GREEDY UPPER  — the list-schedule certificate bound
    ///                      makespan ≤ total/m + (1 − 1/m)·pmax, i.e. the testable
    ///                      corollary of <c>makespan_greedy_le</c> (the (2 − 1/m)·LB
    ///                      guarantee, expressed in LB quantities so it needs no OPT).
    ///   T4 LPT TIGHT     — against the true optimum makespan C* (brute-forced on
    ///                      small instances), makespan ≤ (4/3 − 1/(3m))·C*
    ///                      (<c>lpt_tight_bound</c>, Graham 1969).
    ///
    /// All oracles (max, total, per-bed sums, brute-force optimum) are independent
    /// re-computations from the job durations; the schedule's own MakespanMin is
    /// only cross-checked, never used to prove itself. SetupMin = 0 throughout so
    /// the classic (setup-free) makespan bounds apply exactly.
    /// </summary>
    public class SawBedScheduleTests
    {
        /// <summary>T1 — validity: non-overlapping ordered slots per bed, every job
        /// placed once, makespan = max bed load.</summary>
        [Fact]
        public void T1_Schedule_IsValid()
        {
            var rnd = new Random(20260725);
            for (int t = 0; t < 4000; t++)
            {
                int n = 1 + rnd.Next(14);
                int m = 1 + rnd.Next(5);
                var dur = RandDurations(rnd, n);
                var sched = SawBedScheduler.Schedule(BuildPlan(dur), new SawBedSchedulerOptions(m, setupMin: 0.0));

                // conservation: every accepted job appears exactly once
                Assert.Equal(n, sched.TotalSlotCount);
                Assert.Equal(m, sched.Timelines.Count);

                double maxBedLoad = 0;
                var seen = new HashSet<string>();
                foreach (var tl in sched.Timelines)
                {
                    double load = 0;
                    for (int k = 0; k < tl.Slots.Count; k++)
                    {
                        var s = tl.Slots[k];
                        Assert.True(s.StartMin >= -1e-12, $"negative start {s.StartMin}");
                        Assert.True(s.DurationMin >= -1e-12, $"negative duration {s.DurationMin}");
                        Assert.True(seen.Add(s.BlockId), $"block {s.BlockId} scheduled twice");
                        // no overlap with the previous slot on the same bed (setup 0 => contiguous)
                        if (k > 0)
                            Assert.True(tl.Slots[k - 1].EndMin <= s.StartMin + 1e-9,
                                $"overlap on bed {tl.BedIndex}: prev end {tl.Slots[k - 1].EndMin} > start {s.StartMin}");
                        load += s.DurationMin;
                    }
                    if (load > maxBedLoad) maxBedLoad = load;
                }
                Assert.Equal(n, seen.Count);
                // reported makespan equals the independently summed max bed load
                Assert.True(Math.Abs(sched.MakespanMin - maxBedLoad) <= 1e-7 * (1 + maxBedLoad),
                    $"makespan {sched.MakespanMin} != max bed load {maxBedLoad}");
            }
        }

        /// <summary>T2 — the two optimum lower bounds hold for the produced schedule:
        /// makespan ≥ max job (opt_ge_max_job) and makespan ≥ total/beds (opt_ge_avg).</summary>
        [Fact]
        public void T2_Makespan_MeetsLowerBounds()
        {
            var rnd = new Random(4242);
            double tightestJob = double.PositiveInfinity, tightestAvg = double.PositiveInfinity;
            for (int t = 0; t < 6000; t++)
            {
                int n = 1 + rnd.Next(16);
                int m = 1 + rnd.Next(6);
                var dur = RandDurations(rnd, n);
                var sched = SawBedScheduler.Schedule(BuildPlan(dur), new SawBedSchedulerOptions(m, setupMin: 0.0));

                double total = 0, pmax = 0;
                foreach (var d in dur) { total += d; if (d > pmax) pmax = d; }
                double avg = total / m;
                double C = sched.MakespanMin;

                Assert.True(C >= pmax - 1e-7 * (1 + pmax), $"opt_ge_max_job violated: makespan {C} < pmax {pmax}");
                Assert.True(C >= avg - 1e-7 * (1 + avg), $"opt_ge_avg violated: makespan {C} < total/m {avg}");
                tightestJob = Math.Min(tightestJob, C - pmax);
                tightestAvg = Math.Min(tightestAvg, C - avg);
            }
            // both bounds are genuinely reached (slack hits ~0), confirming they bite
            Assert.True(tightestJob <= 1e-6, $"max-job bound never tight (min slack {tightestJob:E3})");
            Assert.True(tightestAvg <= 1e-6, $"avg bound never tight (min slack {tightestAvg:E3})");
        }

        /// <summary>T3 — the greedy list-schedule certificate upper bound:
        /// makespan ≤ total/m + (1 − 1/m)·pmax. This is the LB-only, OPT-free
        /// corollary of makespan_greedy_le / greedy_makespan_bound.</summary>
        [Fact]
        public void T3_Makespan_GreedyCertificateBound()
        {
            var rnd = new Random(1009);
            double worstExcess = 0; string worstMsg = "(none)";
            for (int t = 0; t < 8000; t++)
            {
                int n = 1 + rnd.Next(18);
                int m = 1 + rnd.Next(6);
                var dur = RandDurations(rnd, n);
                var sched = SawBedScheduler.Schedule(BuildPlan(dur), new SawBedSchedulerOptions(m, setupMin: 0.0));

                double total = 0, pmax = 0;
                foreach (var d in dur) { total += d; if (d > pmax) pmax = d; }
                double bound = total / m + (1.0 - 1.0 / m) * pmax;
                double C = sched.MakespanMin;
                double excess = C - bound;
                if (excess > worstExcess) { worstExcess = excess; worstMsg = $"n={n} m={m} C={C:F4} bound={bound:F4} excess={excess:E3}"; }
                Assert.True(excess <= 1e-7 * (1 + bound),
                    $"greedy certificate bound violated: {worstMsg}");
            }
        }

        /// <summary>T4 — LPT tight bound against the true optimum makespan C*
        /// (brute-forced over all m^n assignments on small instances):
        /// makespan_LPT ≤ (4/3 − 1/(3m))·C*.</summary>
        [Fact]
        public void T4_Lpt_TightBound_vs_BruteForceOptimum()
        {
            var rnd = new Random(2718);
            double worstRatio = 0; string worstMsg = "(none)";
            for (int t = 0; t < 3000; t++)
            {
                int n = 1 + rnd.Next(8);       // 1..8 jobs
                int m = 2 + rnd.Next(2);       // 2..3 beds (brute force m^n <= 3^8)
                var dur = RandDurations(rnd, n);
                var sched = SawBedScheduler.Schedule(BuildPlan(dur), new SawBedSchedulerOptions(m, setupMin: 0.0));
                double C = sched.MakespanMin;

                double opt = BruteForceOptimum(dur, m);
                double ratioBound = (4.0 / 3.0 - 1.0 / (3.0 * m)) * opt;
                double ratio = opt > 1e-12 ? C / opt : 1.0;
                if (ratio > worstRatio) { worstRatio = ratio; worstMsg = $"n={n} m={m} C={C:F4} opt={opt:F4} ratio={ratio:F4}"; }

                Assert.True(C >= opt - 1e-7 * (1 + opt), $"LPT beat the optimum?! {worstMsg}");
                Assert.True(C <= ratioBound + 1e-7 * (1 + ratioBound),
                    $"lpt_tight_bound violated: {worstMsg} bound={ratioBound:F4}");
            }
            // Graham's ratio 4/3 - 1/(3m) is <= 1.1667 for m>=2; LPT stays well under.
            Assert.True(worstRatio <= 4.0 / 3.0, $"worst observed LPT/OPT ratio {worstMsg}");
        }

        // ---- independent oracles + builders ---------------------------------

        static double[] RandDurations(Random r, int n)
        {
            var d = new double[n];
            for (int i = 0; i < n; i++) d[i] = 0.1 + r.NextDouble() * 100.0;
            return d;
        }

        // Brute-force minimum makespan over all m^n assignments (small n only).
        static double BruteForceOptimum(double[] dur, int m)
        {
            int n = dur.Length;
            var assign = new int[n];
            double best = double.PositiveInfinity;
            var loads = new double[m];
            // iterate all m^n assignments
            long total = 1; for (int i = 0; i < n; i++) total *= m;
            for (long code = 0; code < total; code++)
            {
                long c = code;
                for (int i = 0; i < n; i++) { assign[i] = (int)(c % m); c /= m; }
                for (int k = 0; k < m; k++) loads[k] = 0;
                for (int i = 0; i < n; i++) loads[assign[i]] += dur[i];
                double mk = 0; for (int k = 0; k < m; k++) if (loads[k] > mk) mk = loads[k];
                if (mk < best) best = mk;
            }
            return best;
        }

        // Build an ExtractionPlan whose Accepted entries carry the given cutting
        // times (the only field the scheduler reads besides Block.Id / Order).
        static ExtractionPlan BuildPlan(double[] cuttingTimes)
        {
            var accepted = new List<ExtractionPlanEntry>(cuttingTimes.Length);
            for (int i = 0; i < cuttingTimes.Length; i++)
            {
                var bbox = new BoundingBox3(0, 0, 0, 1, 1, 1);
                var block = new BenchBlock($"B{i}", bbox);
                var est = new BlockYieldEstimate(
                    blockId: $"B{i}",
                    nonIntersectedCount: 1,
                    recoveryPercent: 50.0,
                    fractureRisk: 0.2,
                    estimatedCuttingTimeMin: cuttingTimes[i],
                    recoverableVolume: 0.5,
                    wasteVolume: 0.5,
                    bestPsiDeg: 0.0);
                accepted.Add(new ExtractionPlanEntry(i, block, est, score: 1.0));
            }
            return new ExtractionPlan("BENCH", accepted, new List<ExtractionPlanEntry>());
        }
    }
}
