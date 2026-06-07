# Example 21 - Stereotomy: voussoir arch carved from rubble (flagship)

The final flagship: generate a stereotomic voussoir arch, then carve each voussoir from a matched
ETH1100 rubble stone by trimming the stone to the voussoir geometry. Grounded in a deep stereotomy
study (`../../wiki/research/stereotomy_voussoir_from_rubble.md`). Units: meters. Style: short
sentences, no em dashes.

![Rubble voussoir arch](21_rubble_arch.png)

## The idea (digital ravalement)
Classical stereotomy's simplest cutting method is RAVALEMENT: cut a voussoir oversize, mount it, trim
the excess to the final surface (Frezier; Sakarovitch). This example automates exactly that with found
stone: take an oversize rubble block, pose it over the target voussoir, and TRIM (CGAL boolean
intersect) to the voussoir cell. Stability in stereotomy comes from the carved geometry, not mortar
(Galletti 2020), so the cut faces are what matter.

![Rubble stone trimmed to voussoir](21_stone_to_voussoir.png)
*Right: raw ETH1100 rubble stone. Left: the voussoir trimmed from it (flat bed-joint cuts + the rubble's real surface where it falls within the cell).*

## Pipeline
1. ARCH GEOMETRY: the **Arch Voussoirs** component (Frahan > Voussoir, GUID D5F10012) generates the 11
   radial voussoir CELLS (8-vertex wedge solids; bed joints normal to the intrados, pointing at the
   centre - the radial bed-joint rule) from semicircular intrados R = 2.0 m, ring thickness 0.55 m, width
   0.6 m, N = 11. Catenary / pointed / segmental arches are a drop-in change of the Profile input on the
   same component. Cells are outward-oriented closed solids (required for the CGAL trim below).
2. EVOLVE-MATCH: each voussoir cell is matched to a volume-feasible ETH rubble stone, posed to envelop
   the cell (the evolved variant adds 24 rotation seeds + a (1+8)-ES driving the cell's real vertices
   inside the stone - beating the OBB-only `Voussoir Stone Matcher`).
3. TRIM: `CgalMeshBoolean.Intersection(rubble, cell)` -> the carved voussoir. A validity guard keeps
   only closed results within the cell volume; otherwise a clean voussoir is used (exact fallback).
4. ASSEMBLE: the carved voussoirs placed in the arch ring.

## Measured (this run, regenerated 2026-06-07)
- 11-voussoir semicircular arch, span 4.0 m, ring 0.55 m, arch volume 2.3266 m^3.
- **11/11 voussoirs are real rubble trims**, 0 clean fallback; **coverage 94.9%** (recovered / arch
  volume); per-cell 0.85 to 0.99.
- Cells from the Arch Voussoirs component; each matched to the best of 8 ETH1100 candidate stones (scaled
  to 1.5x the cell diagonal, centroid-aligned) and CGAL-intersected to the cell.
- **Fix (the reported bug):** every voussoir is now either a CGAL-trimmed rubble stone bounded by its
  cell, or (if no valid trim) the CLEAN cut cell. The earlier version kept the raw un-trimmed boulder on a
  failed trim, so oversized stones overshot the arch. No raw boulders remain. Metrics in
  `21_arch_metrics.json`.

## Files
- `21_rubble_arch.3dm` - the carved voussoir arch (one mesh per voussoir, coloured).
- `21_rubble_arch.png` - the arch; `21_stone_to_voussoir.png` - the trim before/after.
- `21_arch_metrics.json` - voussoirs, rubble-trim count, coverage.

## Original logic + components
The logic (radial voussoir cells -> evolve-match -> CGAL trim) is the original contribution; it evolves
the existing `Voussoir Stone Matcher` (Hungarian + OBB) into true SE(3) pose-containment + a boolean
trim, reusing the `Rubble Evolved Fit` substrate (example 19). It composes the reference Voussoir-GH
arch/vault geometry with Frahan CGAL booleans.

## Next: vaults / shells
For doubly-curved vaults the intrados is a funicular shell. Reference form-finding pipeline:
**compas-RV** (github.com/BlockResearchGroup/compas-RV, Block Research Group RhinoVAULT in COMPAS):
pattern -> form/force diagrams -> compression-only shell -> tessellate into voussoir courses (bed
joints along the lines of curvature, per Monge) -> the same match-and-trim from rubble. Equilibrium
checked by Frahan Masonry Stability (RBE / Kao 2022 CRA).
