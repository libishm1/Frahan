# Example 22 - Pendentive (sail) vault voussoirs carved from rubble boulders

The vault extension of the stereotomy flagship: a pendentive dome (sail vault) tessellated into
voussoirs along its lines of curvature, each carved from a matched ETH1100 rubble boulder by the
evolved matcher + CGAL trim. Units: meters. Style: short sentences, no em dashes.

![Pendentive vault of rubble voussoirs](22_pendentive_vault.png)

## Geometry
A pendentive dome is a sphere over a square plan: sphere R = 2.5 m, square half-width 1.6 m, shell
thickness 0.4 m. The continuous spherical surface springs from the four corners (the pendentives) up
to the apex (the sail/pendentive-dome form). It is tessellated on a 6 x 6 grid into 36 voussoir cells;
the bed joints follow the sphere's lines of curvature (meridians + parallels), which is Monge's rule
for stable mortarless assembly. Each cell is an intrados patch extruded radially by the thickness.

## Match + trim (evolved matcher)
Each voussoir cell is matched to a volume-feasible ETH boulder, posed to envelop the cell (24 rotation
seeds + SE(3) containment of the cell's real vertices), then trimmed by `CgalMeshBoolean.Intersection`
to the voussoir geometry (digital ravalement). A validity guard keeps closed trims within the cell
volume; otherwise a clean voussoir is used.

## Measured (this run)
- Pendentive dome, R = 2.5 m, span 3.2 m, 36 voussoirs.
- **17/36 real rubble trims**, coverage ~85% (recovered / shell volume).
- Honest finding: irregular boulders rarely FULLY contain a curved voussoir patch (0 exact-contained
  here), so the rubble voussoirs are "inside the resource" (boulder surface kept where it falls short)
  and the rest fall back to clean voussoirs. CoACD-convex stock or larger boulders raise the exact
  fraction. Metrics in `22_vault_metrics.json`.

## Files
- `22_pendentive_vault.3dm` - the vault (one mesh per voussoir, coloured).
- `22_pendentive_vault.png` - the assembled vault.
- `22_vault_metrics.json` - voussoirs, rubble-trim count, coverage.

## Lineage + next
Companion to example 21 (arch). Stereotomy research: `../../wiki/research/stereotomy_voussoir_from_rubble.md`.
For form-found doubly-curved vaults (not just the sphere), the reference pipeline is **compas-RV**
(github.com/BlockResearchGroup/compas-RV, Block Research Group RhinoVAULT in COMPAS): pattern ->
form/force diagrams -> compression-only funicular shell -> tessellate into voussoir courses -> the same
match-and-trim from rubble. Equilibrium via Frahan Masonry Stability (RBE / Kao 2022 CRA).
