# 10 - Frahan QuarryCutOpt Spec

**Spec version:** 0.1 (proposed-only; no live source)
**Sources:** `frahan/frahan_geopack_geocut_final_merged_landscape.md`,
runbook §§ 16.5 and 17, the nested
`frahan_quarrycutopt_backend_starter (1).zip`,
`frahan_quarry_blockcutopt_codebase.zip`,
`frahan_quarry_blockcutopt_3d_bvh_codebase.zip`,
`frahan_quarrysuite_integrated_v0_1_20260503.zip` (research bundle,
catalogued not extracted).

## 1. Goal

QuarryCutOpt is the **upstream optimiser** for the entire quarry stack.
Where GeoCut decides cuts inside one block, QuarryCutOpt decides:

- which bench blocks to extract first;
- which blocks to skip because of low yield or fracture risk;
- the cut sequence across multiple blocks under shared saw-bed
  constraints;
- the per-block target product mix (slabs vs billets vs offcut).

## 2. Why a separate module

GeoCut answers: "given a block, what is the best cut plan?"
QuarryCutOpt answers: "given a quarry bench full of blocks, what is
the best extraction order and per-block cut plan that maximises
total yield under shared constraints?"

## 3. Proposed components

| Component | Purpose |
| --- | --- |
| Frahan Quarry Inventory | aggregate every `BenchBlock` in the bench |
| Frahan Quarry Yield Estimator | per-block yield estimate (uses GeoCut as a sub-routine) |
| Frahan Extraction Order Optimizer | order blocks by yield, fracture risk, access cost |
| Frahan Saw-Bed Schedule | schedule blocks onto saw beds over time |
| Frahan Quarry Report | total yield, total waste, schedule, per-block summary |

## 4. Pipeline

```
Quarry inventory (List<BenchBlock>)
  → per-block GeoCut estimate    (calls spec 09 internally)
  → per-block yield + risk score
  → ExtractionOrderOptimizer     (mixed integer or greedy heuristic)
  → SawBedSchedule               (machine-aware scheduling)
  → QuarryReport
```

## 5. Frahan-owned DTOs

```csharp
public sealed class QuarryInventory   { /* benches + blocks */ }
public sealed class BlockYieldEstimate { /* yield, conflicts, est_cost */ }
public sealed class ExtractionPlan    { /* ordered list of blocks */ }
public sealed class SawBedSchedule    { /* per-machine timeline */ }
public sealed class QuarryReport      { /* aggregate metrics */ }
```

## 6. Optimisation strategy

- v1: **greedy** - sort blocks by `yield - risk_penalty`, schedule in
  that order onto saw beds with simple bin-packing in time.
- v2: **mixed-integer programming** - use a managed LP/MIP solver
  (proposed: `Google.OrTools`, license check required) for the
  extraction order + scheduling sub-problem.
- v3: **learning-guided** - covered in spec **12**.

## 7. Acceptance contract

A QuarryCutOpt run produces:

- `QuarryReport.TotalYield` - sum of per-block yield from the chosen
  plan.
- `QuarryReport.TotalWaste` - sum of per-block waste.
- `QuarryReport.ExtractionPlan` - ordered list of `BenchBlock` IDs.
- `QuarryReport.SawBedSchedule` - per-machine, per-day list of
  blocks.

## 8. Validation rules

- Every block in the schedule must appear at most once.
- Saw-bed capacity must not be exceeded on any day.
- Blocks with `Yield < min_yield_threshold` are **skipped** (logged in
  the report).

## 9. Performance targets

- 100 blocks, greedy: ≤ 5 s.
- 100 blocks, MIP: ≤ 5 min (depends on solver).

## 10. Tests required

- Unit: `ExtractionOrderOptimizer` returns the expected order on a
  3-block fixture (hand-checked).
- Unit: `SawBedSchedule` returns a non-overlapping schedule for a
  given block set.
- Integration: end-to-end on a sample 10-block bench produces a
  positive `TotalYield`.

## 11. Out of scope for v1

- Truck / loader logistics outside the quarry.
- Energy / tooling cost models.
- Multi-quarry portfolio optimisation.
