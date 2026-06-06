# Stereotomy: voussoirs carved from rubble (research + method)

Deep-research notes grounding the Frahan stereotomy-from-rubble workflow (example 21). Style: short
sentences, no em dashes. Sources are cited inline; this is a working synthesis, not a paper.

## What stereotomy is
Stereotomy is the art of cutting stones into shapes for vaulted construction. The defining trait
(Galletti 2020): voussoirs are individually cut to fit each other precisely and assembled like a 3D
jigsaw; stability comes from the carved geometry of the stones, NOT from mortar wedging. That is the
exact conceptual model for a rubble-match-and-trim component: pieces interlock by cut geometry, not by
fill.

## The geometric problem (the trait)
A vault's three orthographic views do not fully describe a voussoir: its faces are inclined to all
reference planes, so only one of the six faces usually appears in true size. The art du trait
geometrique recovered the true shapes by double orthogonal projection + plane rotation (rabattement) +
surface development, drawn full-scale (epures) and handed to cutters as templates (panneaux). Monge
abstracted this into descriptive geometry (Sakarovitch, Epures d'architecture, 1998).

Treatise lineage: Philibert de l'Orme (1567), Mathurin Jousse (1642), Francois Derand (1643), Guarino
Guarini (Architettura Civile), Amedee-Francois Frezier (Traite de stereotomie, 1737-39, coined the
word), Gaspard Monge (geometrie descriptive, 1798).

## Three cutting methods (Sakarovitch / Frezier) and which one we automate
1. RAVALEMENT (reduction): cut the voussoir oversize as for a regular vault, mount, then trim the
   excess in situ to the real cutting surface. Simple, no projection, but wastes ~20%+ and moves
   oversized blocks. **This is exactly our digital algorithm: take an oversize rubble stock block,
   pose it over the target voussoir, and TRIM (boolean intersect) to the final voussoir cell.**
2. SQUARING (equarrissement): inscribe the voussoir in a rectangular parallelepiped, strip it by
   intermediate cuts. Halves the waste vs ravalement.
3. TEMPLATES (par panneaux): apply true-size face templates to a near-net block. Most efficient,
   hardest (needs the full trait).

## Voussoir anatomy + the load-path primitive
A voussoir is a 6-face wedge: tete (head/front), douelle (intrados/soffit), extrados, lit / joint de
lit (bed joint, the compression-bearing contact), and two lateral joints. RADIAL BED-JOINT RULE: in an
arch the bed joints are normal to the intrados and, for a circular arch, point at the centre of
curvature; each wedge turns the thrust aside, transferring it stone to stone. Structural
generalisation (Hooke 1675 inverted catenary; Heyman safe theorem; Block/Ochsendorf): the bed joints
should be perpendicular to the thrust line, which must stay within the section (middle-third rule).
Monge: orient joints along the lines of curvature of the vault surface.

## Computational stereotomy (for the vault/shell extension)
Thrust Network Analysis (Block + Ochsendorf 2007) and RhinoVAULT / RhinoVAULT2 (Rippmann, Block
Research Group, ETH) form-find compression-only funicular shells; equilibrium is checked by Rigid
Block Equilibrium / CRA (Kao 2022, already ported in Frahan Masonry). For the vault/shell stage,
**compas-RV (github.com/BlockResearchGroup/compas-RV)** is the reference form-finding pipeline:
pattern -> form/force diagrams -> funicular shell -> tessellate into voussoir courses (bed joints along
lines of curvature) -> match-and-trim each from rubble.

## The Frahan method (example 21): evolve-match + CGAL trim
1. ARCH GEOMETRY: from an intrados curve (semicircular here; catenary/pointed are drop-in), ring
   thickness t, voussoir count N, width w -> N radial voussoir CELLS (8-vertex wedge solids, bed
   joints normal to the intrados). Reference: Voussoir-GH (BarrelVault/Catenary curves, VoussoirCreate)
   + Frahan VoussoirAssembly/VoussoirRecord.
2. EVOLVE-MATCH: for each voussoir cell, pick a rubble stone (volume-feasible), pose it to envelop the
   cell (centroid align; the evolved variant adds 24 rotation seeds + a (1+8)-ES driving the cell's
   real vertices inside the stone, beating the existing OBB-only VoussoirStoneMatcher). This is the
   digital RAVALEMENT: oversize stock posed over the target.
3. TRIM: CGAL boolean INTERSECT(rubble, voussoir cell) -> the voussoir carved from rubble. Exact where
   the stone fully contains the cell; partial ("inside the resource") where it falls short, keeping the
   rubble's real surface. A validity guard keeps only closed results with volume <= cell (else a clean
   voussoir fallback).
4. METRICS: voussoirs, real-rubble-trim count, coverage = recovered / arch volume, exact vs partial,
   (next) thrust-line-in-middle-third check via Masonry Stability (RBE).

Example 21 result: 11-voussoir semicircular arch (span 4 m, ring 0.55 m), 9/11 real rubble trims,
coverage ~75%. Honest finding: irregular rubble rarely FULLY contains a chunky wedge, so most voussoirs
are "inside the resource" (the user's accepted case), not exact; larger or convex-decomposed stock
raises the exact fraction.

## Evolved matcher vs the existing VoussoirStoneMatcher
Existing (D5F10010): Hungarian assignment, OBB-containment feasibility (sorted-extent test), yield +
carving cost. Evolved: replace OBB-containment with true SE(3) pose-containment of the voussoir's real
vertices (24 seeds + ES, the Rubble Evolved Fit substrate), and add the CGAL TRIM so the output is the
carved voussoir, not the raw stone. Future: grain/vein alignment (lay the rubble's strongest axis along
the bed-joint compression), and thrust-perpendicular bed joints for non-circular arches.
