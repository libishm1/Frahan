# Joint-spacing estimator fix (ISRM distinct-joint count)

Date: 2026-06-14. A review of the DFN density ("too dense") traced back to a bug in
the worker's per-set spacing estimator. This note records the bug, the fix, and the
corrected spacings on the two real Tongjiang clouds.

## The bug

`native/discontinuity_worker/frahan_discontinuity_worker.cpp` computed per-set
spacing as the **mean gap between every consecutive facet centroid** projected onto
the set normal:

```cpp
for facet i in set: offs.push_back(dot(facetCentroid_i, pole));
sort(offs);
for i in 1..: gap = offs[i]-offs[i-1]; if(gap>1e-9){ sp_mean += gap; gaps++; }
spacing = sp_mean / gaps;
```

A continuous rock face has thousands of nearly-coplanar facets a few millimetres
apart, so that average collapses to **facet sampling density**, not the spacing
between distinct joints. On the Tongjiang XB scan it reported 2.8-20 mm — **below the
~8 mm point spacing**, i.e. physically impossible as a joint spacing. This made the
generated DFN ~10-60x too dense and forced a bogus ×100 "spacing scale" downstream.

## The fix (ISRM set spacing)

Cluster the facet offsets along the normal into **distinct joints** (a gap ≫ the
within-joint facet scatter marks a new joint plane; threshold = 8× the median gap,
floored at 2× the point spacing), then `spacing = scanline extent / number of joints`.

## Corrected spacings (real data)

Tongjiang **XB** (7.86 M pts, bw 12), dominant sets:

| set | share | old (facet-gap) | new (ISRM distinct-joint) |
|-----|-------|-----------------|---------------------------|
| 1   | 28 %  | 2.8 mm          | **0.38 m** |
| 2   | 25 %  | 6.2 mm          | **0.19 m** |
| 3   | 20 %  | 16 mm           | **0.31 m** |

Tongjiang **AB** (6.86 M pts, bw 12): 1.62 / 0.43 / 0.63 / 0.73 / 2.09 / 4.32 m
(was 5-15 mm). All decimeter-to-meter — real quarry jointing.

## Downstream impact

- The block-size card and the DFN bridge now use the worker's metres directly;
  **the ×100 scale is removed** from Example 32 (spacing scale = 1).
- Honest block size on XB (1.5×1×1 m blank, 18-plane DFN): Jv ≈ 12, RQD ≈ 80,
  Vb ≈ 0.11 m³, Deq ≈ 0.48 m; Omni recovers 2 blocks at 0.15 m, 0 at 0.20 m — the
  0.19 m dominant spacing caps the block size, which is the correct geological read.
- The scan IS sufficient for block size once spacing is computed correctly; the
  earlier "scan isn't enough" framing applied only to the deterministic 3D cut layout
  (which still wants GPR/borehole subsurface data, not surface extrapolation).
