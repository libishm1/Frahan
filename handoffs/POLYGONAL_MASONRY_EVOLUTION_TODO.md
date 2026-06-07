# Polygonal masonry - evolution TODO (raised 2026-06-07)

Directive (Libish): the shipped polygonal masonry (example 27, `Polygonal Masonry Sequence` 2D/3D) is
**very basic**. It needs a **much more evolved, aesthetically pleasing algorithm**, must work at
**architectural scale**, and should produce **at least ~10 blocks (small panel) to ~30+ blocks (wall /
facade) depending on the case**, so the workflows are genuinely useful. Style: short sentences, no em dashes.

## Where it is now (the gap)
- The component recovers an INSTALL ORDER from a pre-existing joint pattern (Kim 2024). It does NOT design
  the masonry pattern itself. The pattern quality is whatever the input chains give.
- Fixtures are toy scale: 4x4 to 16x12 model units, 3 to 20 finite stones. Not architectural.
- Aesthetics are flat: spanning chains give plain horizontal bands (cards 01-05); they do not look like real
  cut-stone masonry (no bond, no interlock, no size grading, no corners/openings).
- 2D Voronoi (card 06) is HELD BACK: `Pslg.FromSegments` under-extracts a dense ridge network (~10 of ~26
  cells, KB-8). The only "irregular" 2D case is therefore broken.
- 3D Voronoi (card 07, 50 cells) works and now emits clean closed cells, but it is a generic point-Voronoi,
  not an architecturally-motivated stone layout.

## Goals
1. DESIGN the masonry, not just sequence it. Generate the joint pattern from architectural intent.
2. Aesthetically convincing real-masonry patterns, grounded in precedent.
3. Architectural scale (meters; walls, facades, openings, corners).
4. Controllable block count: target ~10 for a panel, ~30+ for a wall/facade; size grading by position.
5. Compose into end-to-end workflows (design pattern -> cut from quarry stock -> install order -> stability).

## Roadmap (phased, checkboxed)

### Phase A - Architectural pattern GENERATORS (the core of "more evolved")
- [ ] **Polygonal wall pattern generator**: boundary + target block count/size -> an interlocking polygonal
  joint pattern. No continuous through-joints (the running-bond rule); stones interlock like real masonry.
  Precedents: Inca polygonal masonry (Sacsayhuaman, Cusco - tight irregular polygons, no mortar), Roman
  opus incertum / opus reticulatum, Cyclopean masonry, European dry-stone walling.
- [ ] **Ashlar / coursed bond generator**: regular and broken courses, header/stretcher bonds, with grading
  (larger stones at the base, smaller toward the top). Precedent: classical ashlar, Quarra coursed work.
- [ ] **Size-graded Voronoi / Lloyd**: drive cell sizes from a target count + a grading field (base-heavy),
  not uniform random seeds, so the wall reads as designed. Lloyd relaxation for even, masonry-like cells.
- [ ] **Openings + corners**: doors / windows as boundary holes the pattern routes around (reuse the
  `Hole Probes` path); quoins / corner stones; wall returns and buttresses.

### Phase B - Fix and strengthen the substrate algorithm
- [ ] **KB-8: robust 2D planar arrangement** for ridge networks (intersection split + endpoint snapping +
  half-edge face traversal), OR a 2D-cells input path mirroring the 3D component, so irregular 2D
  tessellations extract the correct N cells. Re-enable card 06 once it gives ~26 (or the designed count).
- [ ] **Stone-shape quality controls**: convexity / min-angle / aspect-ratio / min-edge constraints so no
  slivers or ugly spikes; merge tiny cells into neighbours to hit the target count cleanly.
- [ ] **Interlock / bond scoring**: penalise continuous vertical joints; reward overlap (the half-stone
  offset). Make the generator optimise for a real bond, not just a partition.
- [ ] **Structural awareness in sequencing**: bed joints perpendicular to the thrust line; keystone /
  voussoir integration (tie to examples 21/22); gate install order by the Masonry Stability (RBE / Kao 2022).

### Phase C - Architectural scale + block-count targets
- [ ] Parametric examples at building scale (meters): a ~3 x 2 m panel (~10 blocks), a ~6 x 3 m wall with an
  opening (~30+ blocks), a facade (~50+). Block size + count are inputs, not accidents of the fixture.
- [ ] Per-case target-count control: the generator hits a requested block count (within a tolerance) by
  tuning seed density + the merge/grading pass.

### Phase D - End-to-end workflows (the "better workflows")
- [ ] Compose: pattern generator -> cut each polygon from quarry stock (examples 15-17 packers / BlockCutOpt)
  -> install order (this component) -> stability check -> assembly. One quarry-to-wall architectural spine.
- [ ] Couple with the voussoir/arch/vault generators (21/22, D5F10012/13) so walls, arches, and vaults share
  one masonry-design layer.
- [ ] Grain / vein alignment: lay each stone's strongest axis along the bed-joint compression.

## Acceptance criteria
- A wall example reads as real architectural masonry to an architect (named-precedent bond, graded sizes,
  interlocking joints, routed openings), not flat bands.
- Block count is a controllable input: ~10 for a panel, ~30+ for a wall, hit within tolerance.
- All emitted stones are clean closed solids (already enforced in the 3D component; extend to 2D output).
- Live-validated in Rhino (truth criterion c): pattern + install order + stability, with captures.

## Grounding (per feedback_hitl_cards_design_grounded)
Every new pattern needs: a named precedent (Inca / opus incertum / Cyclopean / ashlar / dry-stone / Quarra),
an explicit numeric tolerance (min angle, min edge, target count tolerance), a real or design-grounded
dataset, and a wiki cross-ref. Base algorithm: Kim 2024 (DETC2024-142563). Substrate bug: KB-8.
