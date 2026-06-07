# Statistical report: GPR-driven dimension-block layout of a Botticino marble bench

Cost-wise vs volume-wise vs balanced block cutting on a **real** GPR fracture grid, with flat
(orthogonal) and oblique (bed-following) guillotine cutting. Units: meters and USD. Style: short
sentences, no em dashes. All numbers are reproducible from the bundled data and `_marble_metrics`.

## 1. Source data and provenance
Real 600 MHz GPR of Botticino marble (Bondua, Tinti et al. 2024, MDPI Data 9(3):42; Mendeley
10.17632/w26n6nftxs.3; CC-BY-NC-ND 4.0, research only). Two parallel grid lines:

| Profile | Traces | Samples | dt (ns) | dx (m) | x span (m) | depth (m) |
|---|---|---|---|---|---|---|
| LA010001.DT | 185 | 512 | 0.156 | 0.026 | 4.79 | 4.00 |
| LA010002.DT | 183 | 512 | 0.156 | 0.026 | 4.74 | 4.00 |

Velocity 0.10 m/ns (relative permittivity ~9 for marble); two-way depth = v t / 2.

## 2. Fracture extraction statistics
Pipeline: `RadargramProcessor` (dewow, background removal, t-power gain, AGC, Stolt f-k migration,
Hilbert instantaneous energy) then `FractureExtractor` (high-energy local maxima above the 0.985
energy quantile, kept only under the dip-aware USGS lateral-continuity rule: >= 12 like picks in a
41-trace window along candidate dips <= 45 deg).

- Picks: 148 (profile 1) + 132 (profile 2) = **280**.
- Clustered into **3 continuous beds** (>= 15 supporting picks), fitted to dipping planes
  `depth = a + bx*x + by*y` by least squares:

| Bed | Depth (m) | Support (picks) | Dip (deg) | Plane-fit RMS (m) | Mean confidence |
|---|---|---|---|---|---|
| 1 | 0.72 | 168 | 6.1 | 0.015 | 0.22 |
| 2 | 2.10 | 33 | 0.9 | 0.005 | 0.13 |
| 3 | 3.70 | 52 | 6.1 | 0.012 | 0.25 |

The sub-centimetre plane-fit RMS (5 to 15 mm) confirms these are coherent planar beds, not clutter.
Bed spacing (the block-height budget): **0.72, 1.38, 1.59, 0.30 m** (mean 1.00, std 0.51). This is the
single most important quarry number: the bed spacing caps how tall a sound block can be.

## 3. Bench and block model
Bench 4.8 x 2.6 x 4.0 m = 49.92 m3. The 3 beds split it into 4 layers; a 50 mm keep-out at each bed
removes the fractured rock (7.8% loss), leaving 46.05 m3 of usable intact stone. Block catalogue
(footprint x depth, height = layer thickness, cut along the beds):

| Block | Footprint (m) | Price ($/m3) |
|---|---|---|
| A | 3.0 x 1.5 | 2200 |
| B | 2.0 x 1.5 | 1800 |
| C | 1.5 x 1.0 | 1400 |
| D | 1.0 x 1.0 | 1100 |

Cut cost $200/m2 of sawn surface. Net per block = price * volume - 200 * surface area. Price falls and
cut-cost-per-m3 rises as blocks shrink, so small blocks in thin beds are loss-making (e.g. D in the
0.30 m bed is net -$323). The packer maximises `net + W * volume`, sweeping the volume credit
`W ($/m3)` from cost (W = 0) through balanced (W = 500) to volume (W -> inf).

## 4. Oblique guillotine (bed-following) results
Full-span straight cuts tilted to follow each dipping bed (3 oblique bed-parallel passes plus vertical
rip cuts). Blocks are bed-bounded hexahedra and recover the full bed spacing. This is the higher-yield
plan but needs scanning + georeferenced marking to execute the sloped passes (Section 6).

| Objective | W | Blocks | Volume | Recovery of usable | NET value | Slabs (20 mm) |
|---|---|---|---|---|---|---|
| Max cost | 0 | 13 | 32.16 m3 | 69.8% | **$28,741** | 647 (1388 m2) |
| Balanced | 500 | 15 | 36.32 m3 | 78.9% | $27,263 | 777 (1569 m2) |
| Max volume | inf | 20 | 38.85 m3 | 84.4% | $25,010 | 1036 (1678 m2) |

Trade-off: cost -> balanced buys +4.16 m3 for only -$1,478 (-5.1%); cost -> volume buys +6.69 m3 for
-$3,731 (-13.0%). Max cost keeps 4 premium A blocks; max volume swaps every A for two B blocks to grab
x-coverage, trading $/m3 for m3. **Balanced is the recommended operating point.**

## 5. Flat (orthogonal) guillotine results: the today-fabricable baseline
Flat axis-aligned full-span cuts placed at the dip-safe envelope (top = deepest point of the upper bed,
bottom = shallowest point of the lower bed, minus keep-out). The triangular wedges between a flat cut
and the dipping bed are waste. Dip-safe layer thicknesses collapse to **0.33, 1.05, 1.04, 0.16 m**.

| Objective | W | Blocks | Volume | NET value |
|---|---|---|---|---|
| Max cost | 0 | 9 | 20.26 m3 | $17,454 |
| Balanced | 500 | 14 | 24.87 m3 | $15,327 |
| Max volume | inf | 20 | 27.06 m3 | $12,317 |

## 6. The georeferencing prize
At the recommended max-cost setting, the oblique guillotine recovers **+11.90 m3 (+59%)** and
**+$11,287** more than flat cuts, on this single ~50 m3 bench, entirely because the beds dip ~6 deg.

That gap is the business case for the last mile: to cut along a 6 deg bed you must scan the extracted
block, georeference it to the GPR-fitted bed planes, and mark the sloped saw lines on the real stone.
Until that marking and georeferencing chain exists, a quarry runs flat guillotine and leaves 59% of the
recoverable premium stone in the wedges. Localised adjustment of each mark stays with the stonemason;
the system supplies the georeferenced plan, the mason fine-tunes at the rock.

## 7. Sensitivity and limitations
- Velocity: depths and bed spacing scale linearly with the 0.10 m/ns assumption (+/-10% v -> +/-10%
  depth). The dip angles and the flat-vs-oblique ratio are velocity-invariant.
- Two 2D profiles interpolated to a 3D grid (profile 1 -> y = 0, profile 2 -> y = 2.6). A dense grid
  would constrain the cross-line dip directly.
- Block economics use each layer's mean spacing; the oblique geometry follows the true per-(x,y) bed
  planes, so no displayed block crosses a bed (verified in the front-view capture).
- Data is CC-BY-NC-ND: research and testing only.

## 8. Files
- `08_gpr_radargram_marble.png` source radargram; `08b_bench_beds.png` extracted 3-bed grid.
- `08c_maxcost.png`, `08d_balanced.png`, `08e_maxvolume.png` oblique-guillotine packings.
- `08f_flat_guillotine.png` flat-guillotine baseline (wasted wedges visible).
- `08_marble_block_layout.3dm`, `08_marble_cost_volume_metrics.json`.
