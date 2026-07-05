# Geology trio — Terzaghi + P32 + kinematic feasibility (validated 2026-07-03)

Three reviewer-grade discontinuity metrics added to close the depth gap between
the plugin's joint-set tools and rock-mechanics SOTA. Built on the existing
`Frahan.Core.Discontinuity` model (OrientationMath, StereonetProjection,
BlockSizeMath) — non-duplicative, composes with `Stereonet + Block Size`.

## New code

Core (`src/Frahan.StonePack.Core/Discontinuity/`):
- `TerzaghiCorrection.cs` — orientation sampling-bias correction (Terzaghi 1965).
  `w = 1/sin(δ)`, δ = plane-to-sampler angle, capped by a blind-zone half-angle.
  Scanline and window/face modes; raw-vs-corrected set proportions; weighted mean pole.
- `FractureIntensity.cs` — Dershowitz-Herda (1992) P_ij. P32 = Σ1/spacing
  (persistent), P10 along a scanline = Σ P32·|cosθ| (Wang 2005), P32-from-P10
  inverse, direct DFN P32 = ΣA/V, P30, P21.
- `KinematicAnalysis.cs` — Markland (1972) / Hoek-Bray (1981) / Goodman-Bray
  (1976). Planar sliding + flexural toppling per set, wedge sliding per pair;
  apparent-dip daylight test, friction test, lateral limits; line-of-intersection.

GH (`src/Frahan.StonePack.GH/Quarry/`), ribbon Frahan ▸ Quarry:
- `TerzaghiCorrectionComponent.cs` (D5F1004D)
- `FractureIntensityComponent.cs` (D5F1004E)
- `KinematicFeasibilityComponent.cs` (D5F1004F) — self-drawing pole stereonet.

Example: `examples/46_kinematic_intensity_screen/` (Ingest → trio, sliders).

## Validation — real data, live in Rhino 8 (deployed .gha 11:35)

Dataset: `survey_discontinuities.csv` (26 measured planes, 5 sets) +
`tongjiang_sets.csv` (5 real joint sets from the CSR worker on the 7.86 M-pt
Tongjiang scan).

**Terzaghi** (vertical scanline, blind-zone 15°, max weight 3.86, 6 in blind zone):

| set | dip | raw | corrected |
|-----|-----|-----|-----------|
| S1  | 17.8 | 19.2% | 9.0%  |
| S2  | 49.4 | 23.1% | 15.9% |
| S3  | 73.3 | 15.4% | 23.4% |
| S4  | 43.7 | 19.2% | 11.9% |
| S5  | 77.5 | 23.1% | **39.7%** |

Correct physics: a vertical borehole over-samples shallow joints (S1 down-weighted)
and under-samples steep joints (S5 boosted). Fractions sum to 1.000.

**Fracture intensity**: per-set P32 = 1/spacing; total **P32 = 2.312 1/m**.
- CHECK P32 == Jv (Palmström): 2.3123 == 2.3123 — exact.
- CHECK P10→P32 round-trip (S2): 0.3528 → 0.1662 → 0.3528 — exact.

**Kinematic** (cut face dip 60 / dip-dir 323, friction 30°):
- 1 feasible failure: **S2 planar sliding** (dip 49° daylights a 60° face,
  49° > 30° friction, dip-dir aligned). 0 wedges, 0 toppling.
- Sanity assertion passed (S2 planar must be feasible).

**On canvas**: DiscontinuityIngest → all three components solve; Kinematic self-
draws the lower-hemisphere pole stereonet (friction circle, slope great circle,
poles coloured by feasibility). Figure: `figures/kinematic_stereonet_tongjiang.jpg`.

Build: 0 errors. Deployed + staged. Push pending HITL.
