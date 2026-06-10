# 27_07 — Stone-Cell Match (Λ): the imposition↔negotiation balance on canvas

Full chain, all four evolved components, canvas-validated live 2026-06-10:

    Polygonal Wall (Generator) A  --cells-->  Stone-Cell Match (Λ)  <--stones--  Polygonal Wall (Generator) B
                 |                                                                   (a second generator as
                 +--Assembly--> Masonry Stability Check (CRA = true)                  the stone "inventory")

## Measured on this canvas
- Generator A (cells): 15 stones, interlock J = 0.682, coverage 1.000.
- Generator B (inventory): 24 stones (different seed, stronger size grading).
- **Stone-Cell Match: Λ = 0.087** (0 = stones as found … 1 = pure stock; Cyclopean Cannibalism datum ≈ 0.27),
  mean gap 0.213, 15/15 cells filled, 9 stones unused.
- **Masonry Stability Check: STABLE | CRA-CERTIFIED** (residual 0.48ε, 1 iteration) on the generator's
  EXACT-joint assembly — 30 interfaces, 120 contact vertices, max compression 782 N.

## The capture (27_07_stone_match_lambda.png)
Left = the inventory; middle = the target cell wall; right = the inventory stones PLACED into their assigned
cells, coloured by carve ratio (green low → amber high) — gaps show honestly where a stone under-fills its
cell (the α·carve vs β·gap trade). Exact cut geometry comes from the Core StoneCarveBack (CGAL intersection).

## Presentation is canvas-native (no external scripts)
The final form is produced ENTIRELY by the canvas: Move components lay out inventory / cells / placed
side-by-side, Custom Previews colour them (Colour Swatches for inventory + cells; a Gradient — Traffic
preset, domain 0..0.5 — driven by the Carve output colours the placed stones green = cheap carve, red =
expensive). Re-opening the .gh reproduces the capture with zero scripting.

## Notes
- Mortar is wired 0 so cells = dry joints (the structural model and the matching both use full cells).
- Known canvas quirk fixed in code (next .gha load): the stability check's Stones input is now Optional, so
  the Assembly-only path solves without a Stones wire.
- PachydermGH conflicts with component insertion: keep it disabled in BOTH Grasshopper\Libraries AND the
  package-manager copy (packages\8.0\pachyderm_acoustic_simulation\...\PachydermGH.gha.disabled).
