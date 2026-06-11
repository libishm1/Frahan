# Lambda flagship study — imposition vs assignment strategy (2026-06-11)

60 real ETH1100 stones (30-200 L band, Zenodo 10038881) assigned into generated walls scaled to the mean
stone volume, across the Coursing continuum (0 = Inca polygonal .. 1 = coursed) and two grids. ALL three
strategies are scored by the SAME exact CGAL carve-back metric (engines differ only in the matching), so
the comparison is apples-to-apples on final Lambda (carved/found volume) and mean gap.
Test: tests/Frahan.StonePack.Tests/LambdaStudyBenchmarkTests.cs (REPORTED, not gated).

| coursing | grid | Hungarian Λ / gap | vol-greedy Λ / gap | random Λ / gap | Hungarian ms |
|---|---|---|---|---|---|
| 0.0 | 4x3 | **0.214 / 0.225** | 0.610 / 0.548 | 0.629 / 0.524 | 1501 |
| 0.0 | 6x4 | **0.209 / 0.229** | 0.619 / 0.594 | 0.622 / 0.557 | 3262 |
| 0.5 | 4x3 | **0.184 / 0.251** | 0.590 / 0.559 | 0.603 / 0.551 | 1260 |
| 0.5 | 6x4 | **0.212 / 0.223** | 0.616 / 0.589 | 0.619 / 0.561 | 3532 |
| 1.0 | 4x3 | **0.207 / 0.252** | 0.592 / 0.545 | 0.636 / 0.508 | 1381 |
| 1.0 | 6x4 | **0.233 / 0.242** | 0.619 / 0.589 | 0.628 / 0.549 | 3207 |

## Reading
1. **Shape-aware matching is worth ~3x in imposition**: the Hungarian engine (PCA + flips + voxel
   symmetric-difference cost) lands Lambda 0.18-0.23 where volume-only greedy and random both sit ~0.59-0.64
   — i.e. matching SHAPE, not just size, is what recovers the found-geometry value.
2. **The advantage holds across the whole Coursing continuum** (Inca polygonal -> coursed): Lambda is flat
   in coursing for Hungarian, so the imposition cost of choosing a more regular aesthetic is small at these
   stone counts; the matcher absorbs it.
3. All Hungarian results beat the Cyclopean Cannibalism built-wall datum (0.27) on this stock — consistent
   with the earlier 15-cell benchmark (0.194) and the card 27_07 canvas run (0.087 on generated stock).
4. Baselines are honest-weak: vol-greedy/random place by centroid translation without rotation search;
   their gap numbers are not directly comparable as quality (their stones protrude and get carved more).
5. Cost: O(n^3) Hungarian + voxel costs = 1.3-3.5 s at 60 stones x 12-24 cells — interactive at card scale.
