# Masonry + Quarry: inclusion / exclusion decision (metrics-backed)

Date: 2026-06-06. Style: short sentences, no em dashes. Consolidates the measured evidence
(`Harness --packbench` masonry + fracture families, the W3 FractureBlockPack decision, the W4 masonry RBE
fix) into concrete canvas keep/hide calls. Chart: `figures/masonry_quarry_decision.png`. Decision rule
(same as keep-or-cut): measure before hiding; gate on validity; hide-not-delete with GUIDs preserved.

## 1. Masonry family (coursed wall / inventory)
| Component | Measured | Verdict | Reason |
|---|---|---|---|
| `BestFitInventoryPacker` | 65.2% wall coverage, 20/20, 21-70 ms, deterministic | **KEEP** | best inventory fit for coursed-rubble walls |
| `AshlarLayoutEngine` | 60.8% coverage, 20/20, height-binned courses | **KEEP** | the coursed-ashlar layout engine; distinct course logic |
| `BenchMonumentPacker` | not in --packbench yet (monument-into-bench cell) | **KEEP (instrument)** | distinct domain (monument blanks into a quarry bench cell); add a bench row before any hide |
| `MasonryStabilityRbeComponent` (RBE) | W4: now uses `BuildPhysicsCorrected`; StageB end-to-end PASS | **KEEP (fixed)** | equilibrium-feasibility verdict; label honest until the Dykstra friction path lands (KB-3) |
| `BuildOrderStabilityStreamComponent` | W4: migrated to corrected RBE | **KEEP (fixed)** | build-order stability stream |

All measured masonry packers are valid (no overlap domain) and serve distinct wall/course/monument needs.
None is dominated. KEEP the family.

## 2. Quarry + fracture family (yield / recovery)
| Component | Measured | Verdict | Reason |
|---|---|---|---|
| `FractureBlockPackComponent` mode 5 (staged wire-saw guillotine) | 49.3% yield, 100% saw-separable (W3, grid3 marble) | **KEEP (canvas spine)** | the paper engine; the manufacturable answer; default to mode 5 |
| `FractureBlockPackComponent` mode 4 (voxel-DLBF) | 53.3% yield, 0% saw-separable (W3) | keep as optimistic upper bound | max yield, not wire-saw cuttable |
| `RecoveryCascade` (Core) | 15.2% recovery, +21% over single-scale, 6 ms | **KEEP + EXPOSE** | validated multi-scale; needs a GH component (W3) so the canvas can reach it |
| `BlockCutOptSolver` (Core) | 12.5% recovery, 125-pose search, 31 ms | **KEEP** | the single-scale pose-search inner loop RecoveryCascade reduces to |
| `Dlbf3dMixedSizePacker` (Core) | 70.4% vol-fill (evolved, +orient) | **KEEP** | mixed-size revenue box packing for the quarry block catalogue |

Verdict: no engine in the quarry/fracture space is dropped (the W3 finding stands -- they are complementary
truth domains, not duplicates). The action is to EXPOSE RecoveryCascade on the canvas and flip
FractureBlockPack's default to the saw-separable mode 5.

## 3. What gets HIDDEN (canvas), with reason
The metrics-backed hides from the keep-or-cut sweep (W2/W5/W6/W7) already apply and stand; this decision
adds none beyond them, because every masonry/quarry component with a measured number earns its place. The
broader audit drop candidates (AUDIT_01 quarry, AUDIT_04 masonry) are READING-LEVEL candidates only; per
the keep-or-cut discipline each must get its own measured number before any `[Obsolete]`+hidden. Concretely
deferred-pending-measurement (do NOT hide yet): the duplicate/legacy masonry assembly + cut-validation
helpers flagged in AUDIT_04, and the quarry decompose/DFN variants in AUDIT_01. Instrument them in
--packbench (or a Core test) first.

## 4. Honesty notes
- Masonry coverage (% of a wall frame filled) and quarry recovery (% of a fractured mass recovered as
  intact blocks) are DIFFERENT metrics in DIFFERENT domains; the chart panels are not cross-comparable.
- FractureBlockPack numbers (49.3 / 53.3%) are mesh-voxel (IsPointInside) on marble grids; they do not
  transfer to the Core AABB Dlbf domain (70.4%). Keep the comparison within-domain.
- BenchMonumentPacker has no measured row yet; KEEP-and-instrument rather than KEEP-on-faith.

## 5. Net canvas decision
KEEP the full measured masonry + quarry/fracture set (BestFit, Ashlar, BenchMonument, RBE-fixed,
FractureBlockPack, BlockCutOpt, RecoveryCascade+expose, Dlbf3D). No new hides beyond the already-applied
W2/W5/W6/W7 ones. Two canvas actions carried forward: (a) add a Recovery Cascade GH component; (b) flip
FractureBlockPack default to mode 5. Audit drop candidates stay pending-measurement.
