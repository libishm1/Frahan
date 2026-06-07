# Example 29 - Edge matching (Trencadis / live-edge reassembly)

> **Scale, units, position:** MILLIMETERS. Synthetic fixture polylines ~4-12 mm across, flat on world XY
> at z=0. Geometric matching is scale-relative (see tolerance below). Style: short sentences, no em dashes.

Match the complementary edges of broken-stone fragments and assemble them into a coherent layout. Given a
fixed frame piece and a pile of loose shards, the solver finds which shard edge mates which frame edge,
aligns the shard, and chains further shards onto the placed ones. This is the reassembly counterpart to the
Trencadis packers (examples 12/13): packing fills a sheet with offcuts in any order, edge matching respects
the original break geometry so adjacent edges actually fit. First user-facing example of
`Frahan.EdgeMatching.Core`.

![Edge-match clean two-piece](29a_two_pieces_clean.png)
*One shard against a frame with one complementary V-notch edge. Placed at near-identity (translation
< 0.05 mm, rotation < 0.5 deg).*

## Named precedent
Antoni Gaudi's Trencadis ("broken-tile") mosaics at Park Guell and Casa Batllo: irregular ceramic and
stone shards laid so their broken edges read as a continuous surface. The same fit-the-break logic drives
kintsugi vessel reassembly (example 14) and live-edge dry-stone work, where a fragment is placed by the
shape of its fracture, not by a regular grid. The algorithm is geometric-hash coarse phase plus beam-search
assembly over edge correspondences (segment -> hash -> coarse phase -> ICP -> beam).

## Design problem
A stonemason or restorer has a frame piece and a heap of shards. Which shard goes where, and in what
orientation, so the broken edges seat against each other. Brute force over every shard-edge pair times
every orientation does not scale. The 5-stage pipeline prunes it: Stage 1 segments each outline at corners,
Stage 2 hashes segments by turning signature into complement buckets, Stage 3 phase-correlates candidate
pairs (gate >= 0.5), Stage 4 refines the fit with ICP, Stage 5 beam-searches the assembly so a shard can
chain onto an already-placed shard, not only onto the frame.

## Numeric tolerance
- Placement accept: residual < 0.05 (translation < 0.05 mm, rotation < 0.5 deg) for a clean match; total
  residual < 0.10 for the 3-piece chain.
- Phase-score gate: `PhaseScoreThreshold` = 0.5 (Stage 3).
- `MinSegmentLength` is ABSOLUTE (default 8.0 mm) and is larger than every edge in these small fixtures.
  The no-match `.gh` sets `MinSegmentLength = 0.3` so the negative test exercises the geometry-mismatch
  logic, not the trivial all-segments-filtered path. This is the documented scale-invariance gap: per the
  scale-invariance constraint (2026-05-25), match params should scale with object size; the solver exposes
  `ResidualThresholdFactor` as a scale-relative gate but `MinSegmentLength` is still absolute. Lower it on
  small fixtures.

## Dataset
Synthetic fixture polylines, internalized in each `.gh` and baked in each `.3dm` (the example-12 pattern;
no external file inputs, no large-data colocation). Three fixtures:
- `29a_two_pieces_clean` - frame with one right-edge V-notch IN, one shard with the mirror left-edge V-bump
  OUT. Minimum-viable single match.
- `29b_two_pieces_no_match` - two plain jittered rectangles with no complementary edges. Negative test: the
  solver must place the frame only and reject the shard (no false positive).
- `29c_three_pieces_chain` - A frame, B mates A on the left and carries a different (bigger) notch on its
  right, C mates B's right only. Forces the beam to expand past one step (B first, then C onto B). The only
  fixture that requires multi-step beam expansion.
For a real job, replace the fixture polylines with your fragment outlines; the frame is the piece you fix
in place, the shards are the loose fragments.

## Components
`Frahan EdgeMatch Solve` (D5F10001), upstream `Frahan EdgeMatch Segments` (D5F10002), optional
`Frahan EdgeMatch Options` (D5F10003). Frahan > EdgeMatch ribbon. Solve outputs: Placed, Transforms, Ids,
Modes, Planarity RMS, Residuals, Total Residual, Report. Ids are `frame` then `s0000`, `s0001`, ... by
input order. Modes are `Planar2D` for these XY fixtures.

## Measured (fill in on the live run)
- 29a: Placed = 2 (frame + shard), Residuals = [< 0.05], Total Residual < 0.05, Report ends `(placed: 1)`.
- 29b: Placed = 1 (frame only), Residuals empty, Total Residual = 0.0, Report ends `(placed: 0)`, no
  false-positive shard placement.
- 29c: Placed = 3, Residuals = 2 numbers both < 0.05, Total Residual < 0.10, Report mentions `(placed: 2)`,
  chain does not break at C.

## Files
- `29a_two_pieces_clean.gh` / `.3dm` / `.png` - clean single match.
- `29b_two_pieces_no_match.gh` / `.3dm` / `.png` - negative test.
- `29c_three_pieces_chain.gh` / `.3dm` / `.png` - beam chain.
- `cards/` - the three detailed HITL diagnostic cards (per-stage expected outputs + failure-signal tables).

## Run
1. Deploy the `install/` `.gha` with Rhino closed (it carries `Frahan.EdgeMatching.Core.dll` +
   `MathNet.Numerics.dll`; missing either throws TypeLoadException on load).
2. Open a `.gh`. Geometry is internalized, no file input to set.
3. Read the `Report` panel and the per-stage diagnostic panels. Compare against the card's expected table.
4. For the negative test confirm Placed = 1 and no false-positive shard transform.

## Why this matters
Edge matching is the bridge from a fracture map or a pile of fragments to a real assembly. It is the
reassembly leg the Trencadis packers (12/13) and kintsugi restoration (14) lean on, and it carries the
scale-invariance question that runs from mm fractures up to m blocks. The Core stayed user-invisible until
now; this example is its first canvas demonstrator.

## Wiki cross-ref
- `../../wiki/research/slm_cards/edge-match-beam-assembly.md` - the beam-assembly algorithm card
  (segment -> hash -> coarse -> ICP -> beam; verdict: evolve toward scale-invariant beam across mm
  fractures to m blocks). Source-of-record for this example's algorithm.
- `../../wiki/research/tolerances_dimensions_slm_roses.md` - the units + scale-relative tolerance basis.
- Note: the deeper code_ws research (`differentiable_edge_matching.md`, the gradient-descent roadmap, the
  projection-bootstrap-3d note) is dev-side in code_ws and not yet migrated to the Frahan wiki. If a Frahan
  reader needs it, migrate that page separately; do not invent a Frahan path for it.
