#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Frahan.Masonry.Quarry.CutOpt;

// =============================================================================
// SawBedSchedule -- per-bed timeline of (BenchBlock, duration) slots.
//
// Spec section 5: QuarryReport.SawBedSchedule = per-machine, per-day list.
// We keep it agnostic to calendar days: a slot is a (start, duration) pair
// in minutes, and a caller can wrap it into a working-day calendar at the
// report layer.
// =============================================================================

public sealed class SawBedSlot
{
    public SawBedSlot(string blockId, double startMin, double durationMin)
    {
        if (string.IsNullOrWhiteSpace(blockId)) throw new ArgumentException("blockId required", nameof(blockId));
        if (startMin < 0) throw new ArgumentOutOfRangeException(nameof(startMin));
        if (durationMin < 0) throw new ArgumentOutOfRangeException(nameof(durationMin));
        BlockId = blockId;
        StartMin = startMin;
        DurationMin = durationMin;
    }

    public string BlockId { get; }
    public double StartMin { get; }
    public double DurationMin { get; }
    public double EndMin => StartMin + DurationMin;

    public override string ToString() => $"{BlockId} @ [{StartMin:0.0}, {EndMin:0.0}] min";
}

public sealed class SawBedTimeline
{
    public SawBedTimeline(int bedIndex, IReadOnlyList<SawBedSlot> slots)
    {
        if (bedIndex < 0) throw new ArgumentOutOfRangeException(nameof(bedIndex));
        BedIndex = bedIndex;
        Slots = slots ?? throw new ArgumentNullException(nameof(slots));
    }

    public int BedIndex { get; }
    public IReadOnlyList<SawBedSlot> Slots { get; }

    public double TotalDurationMin => Slots.Sum(s => s.DurationMin);
    public double LoadEndMin => Slots.Count == 0 ? 0.0 : Slots[Slots.Count - 1].EndMin;

    public override string ToString() => $"Bed#{BedIndex} (N={Slots.Count}, end={LoadEndMin:0.0} min)";
}

public sealed class SawBedSchedule
{
    public SawBedSchedule(string benchId, IReadOnlyList<SawBedTimeline> timelines)
    {
        if (string.IsNullOrWhiteSpace(benchId)) throw new ArgumentException("benchId required", nameof(benchId));
        BenchId = benchId;
        Timelines = timelines ?? throw new ArgumentNullException(nameof(timelines));
    }

    public string BenchId { get; }
    public IReadOnlyList<SawBedTimeline> Timelines { get; }

    public double MakespanMin => Timelines.Count == 0 ? 0.0 : Timelines.Max(t => t.LoadEndMin);
    public int TotalSlotCount => Timelines.Sum(t => t.Slots.Count);

    public override string ToString() =>
        $"SawBedSchedule({BenchId}, beds={Timelines.Count}, N={TotalSlotCount}, makespan={MakespanMin:0.0} min)";
}
