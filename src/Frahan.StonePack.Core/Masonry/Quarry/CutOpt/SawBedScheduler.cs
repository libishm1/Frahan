#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Quarry.CutOpt;

// =============================================================================
// SawBedScheduler -- v1 greedy LPT (Longest Processing Time first).
//
// Spec section 6 v1: greedy bin-packing in time. LPT is the standard 4/3 -
// 1 / (3m) approximation for makespan on m identical machines (Graham 1969).
//
// Inputs:
//   * ExtractionPlan -- the order already encodes priority (yield - risk),
//     but for makespan minimisation we additionally sort by descending
//     EstimatedCuttingTimeMin. The extraction priority is preserved by
//     starting the LPT pass from the head of Accepted, breaking ties on
//     descending duration.
//   * bedCount  -- number of saw beds.
// =============================================================================

public sealed class SawBedSchedulerOptions
{
    public SawBedSchedulerOptions(int bedCount, double setupMin = 0.0)
    {
        if (bedCount < 1) throw new ArgumentOutOfRangeException(nameof(bedCount), ">= 1");
        if (setupMin < 0) throw new ArgumentOutOfRangeException(nameof(setupMin));
        BedCount = bedCount;
        SetupMin = setupMin;
    }

    public int BedCount { get; }

    /// <summary>Fixed inter-block setup time on each bed (minutes).</summary>
    public double SetupMin { get; }
}

public static class SawBedScheduler
{
    public static SawBedSchedule Schedule(ExtractionPlan plan, SawBedSchedulerOptions options)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (options == null) throw new ArgumentNullException(nameof(options));

        // LPT-style: sort accepted blocks by descending estimated cutting time,
        // then for each block assign to the currently-least-loaded bed.
        var sorted = new List<ExtractionPlanEntry>(plan.Accepted);
        sorted.Sort((a, b) =>
        {
            int c = b.Estimate.EstimatedCuttingTimeMin.CompareTo(a.Estimate.EstimatedCuttingTimeMin);
            if (c != 0) return c;
            return a.Order.CompareTo(b.Order);
        });

        var bedLoads = new double[options.BedCount];
        var bedSlots = new List<SawBedSlot>[options.BedCount];
        for (int i = 0; i < options.BedCount; i++)
            bedSlots[i] = new List<SawBedSlot>();

        foreach (var e in sorted)
        {
            int bestBed = 0;
            double bestLoad = bedLoads[0];
            for (int b = 1; b < options.BedCount; b++)
            {
                if (bedLoads[b] < bestLoad)
                {
                    bestBed = b;
                    bestLoad = bedLoads[b];
                }
            }

            double start = bedLoads[bestBed];
            double dur = e.Estimate.EstimatedCuttingTimeMin;
            bedSlots[bestBed].Add(new SawBedSlot(e.Block.Id, start, dur));
            bedLoads[bestBed] = start + dur + options.SetupMin;
        }

        var timelines = new SawBedTimeline[options.BedCount];
        for (int b = 0; b < options.BedCount; b++)
            timelines[b] = new SawBedTimeline(b, bedSlots[b]);

        return new SawBedSchedule(plan.BenchId, timelines);
    }
}
