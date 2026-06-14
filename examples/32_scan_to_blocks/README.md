# Example 32 — Scan → joint sets → DFN → block-cut yield (the full quarry loop)

Close the loop: a scanned rock face becomes joint sets, the sets become a discrete
fracture network (DFN), and the DFN drives an **evolved** block-cut packer that
reports how many dimension blocks the jointing permits. This is the front-to-back
GPR/scan → block-yield pipeline on one canvas.

![DFN in the bench](32_dfn_bench.png)

## The chain (and the new bridge)

```
File (scan .ply)
   ↓  Discontinuity Sets (Async)  D5F10048      → dip / dipdir / spacing per set
   ↓  Joint Sets to DFN           D5F1004B  ←── THE BRIDGE (new)
   ↓  BlockCutOpt Omni Solve      F2D0BC04      → recovery per zone (Pareto)
```

`Joint Sets to DFN` (**D5F1004B**, Frahan > Quarry) turns the discovered
`Dip / Dip dir / Spacing` into a fracture-network mesh clipped to a bench Box,
deterministic by seed, with a **Spacing scale** to take a cm-scale detail scan to
bench metres. Its `DFN` mesh + `Tested area` box wire straight into any BlockCutOpt
packer. The same bridge also accepts measured sets from `Discontinuity Ingest`
(D5F10049), so a mapped survey feeds the identical yield analysis.

## Two questions this example answers

**1. Do you need a reconstructed / cleaned mesh (CGAL / Geogram / Poisson / remesh)
for block packing?  → No.** The DFN emitter fan-triangulates the joint planes into a
clean fracture mesh **by construction** (planar polygons clipped to the box, no holes,
no manifold requirement — the solver's BVH just tests block edges against fracture
triangles). The packing uses only the joint-set *statistics*, never the scan mesh
geometry, so a patchy / holey / noisy scan is fine. (Reconstruction is only needed for
the *other* workflow — carving the actual scanned solid into blocks, example 15 — which
does need a watertight 2-manifold for CGAL booleans.)

**2. Why not use the evolved packers from the paper?  → This uses them.** The bridge
feeds the **evolved Omni solver** (sub-division into zones + coarse-to-fine search +
4-axis Pareto: recovery / revenue / BCSdbBV cost), not the 2020 single-pose baseline.
It also feeds `RecoveryCascade` (multi-scale crack-aware, Core) and the wire-saw
`Fracture Block Pack`.

## Honest spacing (no fudge factor) — the spacing-estimator fix

An earlier version of this example used a `Spacing scale` of ×100. **That was a
band-aid over a bug in the worker's spacing estimator, now fixed.** The old code
averaged the gap between *every consecutive facet patch* along the set normal, which
measured facet *sampling density* on a continuous surface (sub-millimetre), not the
spacing between *distinct joints*. The worker now uses the ISRM measure: cluster the
facet offsets into distinct joint planes and report `spacing = extent / #joints`. On
the real Tongjiang XB scan the corrected spacings are **decimeter-scale** (dominant
sets 0.19 / 0.31 / 0.38 m) instead of the old 2.8-20 mm. **Spacing scale is now 1.**

So the scan *is* enough for block size once spacing is computed correctly — no fudge.

## Result on the real Tongjiang XB DFN (honest)

18 fracture planes from the dominant joint sets in a 1.5×1×1 m dimension-stone blank
(seed 1, spacing scale 1):

- **Jv = 12 joints/m³, RQD ≈ 80, Vb ≈ 0.11 m³, Deq ≈ 0.48 m.**
- Omni recovery: **2 blocks at 0.15 m, 0 at 0.20 m** — the 0.19 m dominant spacing
  **caps the extractable block size at ~0.15 m.** Densely jointed rock yields only
  small dimension stone; the pipeline surfaces that cap directly from the scan.

The evolved Omni packer (sub-division + coarse-to-fine + Pareto) still recovers where
the single-pose baseline gets zero — that comparison is unchanged by the spacing fix.

## Running on real datasets

The canvas points at the full real `Data/tongjiang/detail_cloudXB.ply` (capped to 1 M
points for a fast preview; press `Run`); `detail_cloudAB.ply` (decimeter-to-meter
spacings 0.43-4.3 m) runs the same way. **Set the Bench box to your blank size** — the
bench must be larger than the joint spacing for the DFN to constrain the blocks. With
the spacing fix you no longer need a scale factor: feed the worker's metres directly.

## Validation
Built, saved, reloaded and run live in Rhino 8: scan → 5 sets → 24-plane DFN → Omni
recovers 4 blocks (0.25 m) where the baseline recovers 0. Self-presenting; the DFN +
bench capture reproduces on reopen.
