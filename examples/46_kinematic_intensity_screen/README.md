# 46 — Kinematic feasibility + fracture intensity (rock-mass screen)

Reviewer-grade discontinuity analysis on top of the existing joint-set tools:
**Terzaghi** sampling-bias correction, **Dershowitz P32** fracture intensity, and
a **Markland / Hoek-Bray kinematic** feasibility screen. These are the depth
metrics a rock-mechanics reader expects that a raw pole plot leaves out.

## What it does

```
survey_discontinuities.csv --> Discontinuity Ingest --> Terzaghi Correction
                                                         (vertical scanline)

tongjiang_sets.csv --------> Discontinuity Ingest --+--> Kinematic Feasibility  (self-drawing stereonet)
                                                    +--> Fracture Intensity     (P32 / P10)
```

- **Terzaghi Correction** (Frahan ▸ Quarry) — corrects the 26-plane survey for
  orientation sampling bias along a vertical scanline. A vertical borehole
  under-samples steep joints; the correction rebalances the set proportions
  (here S5, dip ~79°, rises 23% → **40%**). `w = 1/sin(δ)`, capped at a 15°
  blind-zone half-angle.
- **Kinematic Feasibility** (Frahan ▸ Quarry) — screens the 5 real Tongjiang
  joint sets against a cut face (sliders: slope dip / dip-dir / friction) for
  planar sliding, wedge sliding, and flexural toppling. It draws a lower-
  hemisphere pole stereonet with the friction circle (blue), the slope great
  circle (orange), and each set pole coloured red (a feasible failure) or green.
  With a 60°/323° face at φ=30° it flags **S2 planar sliding**.
- **Fracture Intensity** (Frahan ▸ Quarry) — reports P32 (fracture area per unit
  rock volume, the scale-independent DFN measure), per-set and total. Here total
  **P32 = 2.31 1/m**, which equals the Palmström Jv for persistent joints
  (cross-checks Stereonet + Block Size).

## Data

- `../31_discontinuity_ingest/survey_discontinuities.csv` — 26 measured planes, 5 sets.
- `../../docs/validation/discontinuity_ingest_card/tongjiang_sets.csv` — 5 real
  joint sets discovered by the CSR discontinuity worker on the Tongjiang quarry
  scan (7.86 M points).

## Use

Open the .gh. Drag the **slope dip / dip-dir / friction** sliders to test a cut
orientation; the stereonet recolours live and the Report lists every feasible
failure mode with its governing angles. Spacings feeding Fracture Intensity are
representative ISRM values; swap in a scanline count where you have one.

Validated live 2026-07-03 (real data, exact P32==Jv and P10→P32 round-trip).
See `../../outputs/2026-07-03/geology_trio/`.
