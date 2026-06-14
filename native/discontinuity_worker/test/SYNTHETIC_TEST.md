# Discontinuity worker — synthetic ground-truth test

A self-checking test for `frahan_discontinuity_worker`: build a point cloud
sampled on **three known joint sets**, run the worker, and confirm it recovers
the planted dip / dip-direction / spacing. Use this as the numeric regression
gate whenever the worker's facet or mean-shift code changes.

## Ground truth (see `gen_synth_cloud.py`)

| set | dip° | dip-dir° | spacing (m) | pole (lower-hemi)        |
|-----|------|----------|-------------|--------------------------|
| S1  | 8    | 0        | 0.6         | ~ (0, +0.14, −0.99) down |
| S2  | 88   | 90       | 0.5         | ~ (+1, 0, −0.03) East    |
| S3  | 85   | 0        | 0.7         | ~ (0, +1, −0.09) North   |

Block ±4 m in X/Y, ±3 m in Z; 2 mm normal noise; ~62 k points.

## Run

```bash
# Linux (native g++) — used for the Cowork-side verification
g++ -O3 -fopenmp -std=c++17 -D_USE_MATH_DEFINES -o /tmp/disc_worker frahan_discontinuity_worker.cpp -lgomp
python3 test/gen_synth_cloud.py                      # writes synth.ply
mkdir -p out && /tmp/disc_worker --in synth.ply --out out --bw 10 --minshare 0.0 --segply
cat out/discontinuity.json

# Windows (shipped mingw build)
bash build_mingw.sh
python  test\gen_synth_cloud.py
frahan_discontinuity_worker.exe --in synth.ply --out out --bw 10 --minshare 0.0 --segply
```

## Verified result (2026-06-14, Linux g++ 11.4, native build)

Compiles clean-room with **no external libraries**; runs in **0.79 s** on 62 k pts.

At bandwidth ≤ 12 the worker recovers **all three** sets:

| recovered dip° | dip-dir° | spacing | matches |
|----------------|----------|---------|---------|
| 88.04          | 89.99    | 0.501   | S2 ✓    |
| 7.12           | 0.07     | —       | S1 ✓    |
| 84.51          | 359.94   | —       | S3 ✓ (≈85/0) |

Dip recovered to **< 1.5°**, dip-direction to **< 0.1°**, S2 spacing to **0.001 m**.

**Determinism (HO4):** two runs produce byte-identical `discontinuity.json`
*result* content and byte-identical `segmented.ply`; only the `ms_*` timing
fields differ. `facets.csv` (HO3) is emitted with the `cx,cy,cz,nx,ny,nz,set,npts`
header.

**Bandwidth sensitivity (documented, not a bug):** at the **default `--bw 15`**
the worker finds **2 of 3** sets (it merges/skips the marginal S3); at `--bw ≤ 12`
it finds all 3. This is the inherent joint-set-ID sensitivity noted in HANDOFF_04.
Consider lowering the GH default bandwidth or exposing a "sensitivity" preset.

## Pass criteria for a regression run

1. Exit code 0; `discontinuity.json` written.
2. At `--bw 10`: ≥ 3 sets; each planted set matched by some recovered set within
   **2° dip and 3° dip-direction** (axial, mod 180).
3. Two consecutive runs: identical `sets[]` block and identical `segmented.ply`.
