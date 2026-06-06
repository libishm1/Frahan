#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Frahan.Masonry.Quarry.CutOpt;

// =============================================================================
// QuarryReport -- final aggregate of the Layer 7 pipeline.
//
// Spec section 7 acceptance contract:
//   - TotalYield (recoverable volume)
//   - TotalWaste
//   - ExtractionPlan (ordered list of block ids)
//   - SawBedSchedule (per-bed timeline)
// =============================================================================

public sealed class QuarryReport
{
    public QuarryReport(
        QuarryInventory inventory,
        ExtractionPlan plan,
        SawBedSchedule schedule)
    {
        Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        Schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
    }

    public QuarryInventory Inventory { get; }
    public ExtractionPlan Plan { get; }
    public SawBedSchedule Schedule { get; }

    public double TotalYieldVolume => Plan.TotalRecoverableVolume;
    public double TotalWasteVolume => Plan.TotalWasteVolume;
    public double GrossInventoryVolume => Inventory.TotalGrossVolume;
    public double OverallRecoveryPercent =>
        GrossInventoryVolume > 0 ? 100.0 * TotalYieldVolume / GrossInventoryVolume : 0.0;
    public double TotalEstimatedCuttingTimeMin => Plan.TotalEstimatedCuttingTimeMin;
    public double MakespanMin => Schedule.MakespanMin;

    public override string ToString() =>
        $"QuarryReport({Inventory.BenchId}: V_yield={TotalYieldVolume:0.###} m3, " +
        $"V_waste={TotalWasteVolume:0.###} m3, R={OverallRecoveryPercent:0.0}%, " +
        $"accepted={Plan.Accepted.Count}/{Inventory.Count}, makespan={MakespanMin:0.0} min)";
}

public static class QuarryReportBuilder
{
    public static QuarryReport Build(
        QuarryInventory inventory,
        ExtractionPlan plan,
        SawBedSchedule schedule)
    {
        return new QuarryReport(inventory, plan, schedule);
    }

    public static string ToMarkdown(QuarryReport report)
    {
        if (report == null) throw new ArgumentNullException(nameof(report));
        var sb = new StringBuilder();
        sb.AppendLine("# Frahan Quarry Report");
        sb.AppendLine();
        sb.AppendLine($"Bench: {report.Inventory.BenchId}");
        sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}");
        sb.AppendLine();
        sb.AppendLine("## Aggregates");
        sb.AppendLine();
        sb.AppendLine($"- Gross inventory volume: {report.GrossInventoryVolume:0.###} m^3");
        sb.AppendLine($"- Recoverable yield: {report.TotalYieldVolume:0.###} m^3");
        sb.AppendLine($"- Waste: {report.TotalWasteVolume:0.###} m^3");
        sb.AppendLine($"- Overall recovery: {report.OverallRecoveryPercent:0.0} %");
        sb.AppendLine($"- Accepted blocks: {report.Plan.Accepted.Count}");
        sb.AppendLine($"- Skipped blocks: {report.Plan.Skipped.Count}");
        sb.AppendLine($"- Total cutting time: {report.TotalEstimatedCuttingTimeMin:0.0} min");
        sb.AppendLine($"- Schedule makespan: {report.MakespanMin:0.0} min over {report.Schedule.Timelines.Count} bed(s)");
        sb.AppendLine();
        sb.AppendLine("## Extraction order (accepted)");
        sb.AppendLine();
        sb.AppendLine("| # | Block | Score | Yield % | Risk | Recoverable m^3 | Time min |");
        sb.AppendLine("|---:|---|---:|---:|---:|---:|---:|");
        foreach (var e in report.Plan.Accepted)
        {
            sb.AppendLine(
                $"| {e.Order} | {e.Block.Id} | {e.Score:0.000} | {e.Estimate.RecoveryPercent:0.0} | " +
                $"{e.Estimate.FractureRisk:0.00} | {e.Estimate.RecoverableVolume:0.###} | {e.Estimate.EstimatedCuttingTimeMin:0.0} |");
        }
        sb.AppendLine();
        if (report.Plan.Skipped.Count > 0)
        {
            sb.AppendLine("## Skipped (yield below threshold)");
            sb.AppendLine();
            sb.AppendLine("| Block | Yield % | Risk | Score |");
            sb.AppendLine("|---|---:|---:|---:|");
            foreach (var e in report.Plan.Skipped)
            {
                sb.AppendLine(
                    $"| {e.Block.Id} | {e.Estimate.RecoveryPercent:0.0} | " +
                    $"{e.Estimate.FractureRisk:0.00} | {e.Score:0.000} |");
            }
            sb.AppendLine();
        }
        sb.AppendLine("## Saw-bed schedule");
        sb.AppendLine();
        foreach (var t in report.Schedule.Timelines)
        {
            sb.AppendLine($"### Bed #{t.BedIndex}  (load={t.LoadEndMin:0.0} min)");
            sb.AppendLine();
            sb.AppendLine("| Slot | Block | Start min | End min | Duration min |");
            sb.AppendLine("|---:|---|---:|---:|---:|");
            for (int i = 0; i < t.Slots.Count; i++)
            {
                var s = t.Slots[i];
                sb.AppendLine($"| {i} | {s.BlockId} | {s.StartMin:0.0} | {s.EndMin:0.0} | {s.DurationMin:0.0} |");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
