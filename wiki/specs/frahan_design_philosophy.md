# Frahan StonePack — Design Philosophy: Top-Down, Bottom-Up, and Matching

**Location:** `wiki/specs/frahan_design_philosophy.md`
**Status:** canonical · authored by Libish 2026-05-31 (§0-§9) with Frahan
implementation map by Claude (§10). Supersedes the prior Quarra-narrower
draft at `wiki/research/design_sensibility/quarra_design_sensibility.md`
(retained as a focused Quarra companion).
**Date:** 2026-05-29 (authored) · 2026-05-31 (canonicalised)
**Audience:** anyone making architecture or design decisions in Frahan — this is the sensibility the plugin is organized around, not a feature list.
**Companion:** `frahan_quarra_lecture_implications.md`, `frahan_v1_release_feature_report.md`, the architecture-gaps-transition map.

---

## 0. The thesis in one breath

Frahan exists to **make ideal geometry manufacturable from the material of the earth** — and that happens in two opposite directions that meet at a single operation called **matching**.

- **Top-down** is *form-first*. Design the final form, subdivide it into parts, then find or cut stone to fit each part. The form is sovereign; the material is made to serve it.
- **Bottom-up** is *material-first*. Start from what the earth gives — rubble, offcuts, irregular blocks with their own veins and edges — and let the parts find the form. The material is sovereign; the form emerges from fitting.

Both are the same machine pointed in opposite directions. Top-down matches *form-derived templates to available stones*. Bottom-up matches *available stones to each other and to an aesthetic flow*. Whether the parts are 2D (a floor, a mosaic, a nested sheet) or 3D (voussoirs, rubble, a packed wall), the underlying operation is **matching**: finding the valid, non-overlapping, aesthetically and structurally sound correspondence between a piece and its place.

This is why the packing core is not a side feature. It is the engine of the entire philosophy.

---

## 1. Why this matters — the "why" behind everything else

Every other thing the plugin does — scanning quarry faces, mapping fractures, carrying bed/grain metadata, the registration-and-deviation loop, yield estimation — exists to serve one of these two directions, or the matching that joins them.

- We scan stone **because** bottom-up needs to know the real geometry of what it has, and top-down needs to know which block can yield a given part.
- We map fractures and grain **because** matching must respect them: a voussoir cut across a fracture fails; a rubble piece placed against the grain looks wrong and may split.
- We carry provenance and weight metadata **because** both directions end in a real object that must be lifted, placed, and stand.
- We borrow Quarra's registration discipline **because** that is *how top-down survives contact with real stone* — you cannot impose an ideal form on imperfect material without measuring the gap and compensating for it.

So the architecture is not a grab-bag of stone tools. It is a two-directional pipeline whose hinge is matching. Naming this explicitly keeps every future decision honest: a proposed feature either serves top-down, serves bottom-up, or strengthens the matching between them. If it does none of those, it does not belong.

---

## 2. Top-down: form-first, the voussoir logic

### 2.1 The pattern

You begin with the final form — a Platonic solid, a fluidic bench, a freeform vault. The form is the design intent and it is fixed. The work is *subdivision*: slicing the whole into parts that are individually makeable and that reassemble into the whole. Then each part becomes a **template**, and you go to the quarry — physical or digital — and **match a stone to it**: a block big enough to yield the part, with grain in the right direction, with no fracture through the load path.

This is **stereotomy** — the millennial art of subdividing a solid structure into discrete cut stones (*voussoirs*) that work through assembly, where precision in the cuts and correct placement of each part are crucial to structural safety.

### 2.2 The precedents (grounded)

The contemporary digital lineage is exactly this direction:

- **Block Research Group (ETH Zürich), Armadillo Vault** — a discrete stone shell standing in pure compression, where the funicular form was found first (via graphic-statics / Thrust Network Analysis) and *then* each voussoir was conceived by a predetermined structural logic for precise fabrication and assembly. Form first, voussoirs second.
- **Rippmann & Block, "Digital Stereotomy"** — generating voussoir geometry for freeform masonry-like vaults by combining TNA form-finding (compression-only surfaces) with CNC/diamond-wire cutting, in "a smooth digital stream from form finding to fabrication." The voussoirs are *derived from* the form and *constrained by* the fabrication.
- **Quarra's Two Horse Relief and City Jeff** — pure top-down: the artist's form was sovereign and immutable, and the entire heroic effort was making the earth's material conform to it through splitting, flipping, registration, and deviation compensation.

### 2.3 What top-down asks of Frahan

Given a fixed target form, top-down needs the plugin to:

1. **Subdivide** the form into parts (voussoirs / templates) under structural and fabrication logic — fracture lines that follow thrust, parts within machine envelopes, joints that key together.
2. **Generate templates** — the 2D or 3D geometry each part must be cut to.
3. **Match templates to stock** — search available blocks (scanned or catalogued) for the one that yields each part with correct grain and no fracture through the load path. *This is matching in the top-down direction: template → stone.*
4. **Survive the gap** — because real stone never matches the ideal exactly, carry the Quarra registration-and-deviation loop so the cut part can be measured against the template and corrected.

In the current component map this is the Quarry → BlockCutOpt → GeoCut/GeoPack spine plus Slab Cut plus the (future) scan-to-compensated-surface loop. The Flagship Fabricate niche — split a sculpted form into staggered fabricable blocks — is top-down made explicit.

---

## 3. Bottom-up: material-first, the rubble logic

### 3.1 The pattern

You begin with the material — a pile of rubble, a bin of offcuts, a set of irregular blocks, each with its own shape, veins, and edges. There is no predetermined final form. The work is *fitting*: choosing which piece goes where so that the assembly is stable, the joints close, and the aesthetic flows. The form **emerges** from the matching. A dry-stone wall is not designed stone-by-stone in advance; it is built by a mason who, at each course, selects the stone that fits the gap. A Trencadís mosaic or a vein-flow floor is laid by matching edges so the veins continue and the joints work.

The aesthetic *is* the matching. There is no separate "design" step — the design is the sum of good local fits.

### 3.2 The precedents (grounded)

This direction is now as computationally validated as the top-down one:

- **Gramazio Kohler Research (ETH Zürich), Autonomous Dry Stone** — a robotic excavator maps the site, localizes and digitizes irregular stones, and a planning algorithm determines the position and orientation of each stone *to align with a designer-indicated goal surface*. The Circularity Park retaining wall is 938 irregular elements (boulders and demolition debris, ~1000+ kg each), dry-stacked, no mortar. Crucially, it maintains an **inventory of only ~20–30 stones at a time**, scanning more as the inventory depletes — material-first, with the form accommodating whatever arrives.
- **"From Rocks to Walls" and the SDF planners** — geometric planning by *constrained registration and signed-distance-field classification* to decide how irregular stones should be positioned toward stable, explicitly-shaped structures. The match is computed against the real scanned geometry, not an idealization.
- **Dry-stacked non-standard limestone (Construction Robotics, Feb 2026)** — assemble spanning structures from irregular limestone *waste*: a generative algorithm evaluates a digitized library of 50 fragments against a structural geometry, determining optimal stone pairings and the minimal machining (planar faces + pin holes) needed. The machining is kept minimal precisely to *preserve the raw volume* — the material leads.
- **Quarra's Parallel Nature** — the hinge case (see §4): they walked the quarry matching triangular *offcuts* to their parent blocks, bottom-up, against a design derived from a scan of Manhattan schist, top-down.
- **Quarra's SARZ boulders and the dry-stone tradition generally** — fitting and finishing irregular material into a coherent whole.

### 3.3 What bottom-up asks of Frahan

Given a stock of irregular material and a loose goal (a wall envelope, a floor area, a surface to clad), bottom-up needs the plugin to:

1. **Digitize the stock** — scan each irregular piece into a mesh with its veins/edges/defects tagged.
2. **Match pieces to gaps and to each other** — for each open position, find the piece whose geometry fits (non-overlapping, stable contact) *and* whose veins/edges continue the flow. *This is matching in the bottom-up direction: stone → place + neighbour.*
3. **Settle contact** — resolve the real contact between irregular faces (the Kintsugi Settle Contact / Rubble Wall Settle logic), optionally with minimal machined registration faces (the Feb 2026 lesson).
4. **Order the assembly** — sequence placement so each piece is stable when set and the structure stands at every stage, not only at completion.

In the current component map this is Random Rubble + Rubble Wall Settle + Kintsugi + Trencadís + EdgeMatch + Surface Packing. The aesthetic-flow requirement (vein direction, edge continuity) is what distinguishes Frahan's bottom-up from a pure structural packer — and it is exactly what EdgeMatch and the Trencadís variants are *for*.

---

## 4. The hinge: matching as the universal engine

### 4.1 Both directions are one operation

Strip away the framing and top-down and bottom-up call the same primitive. **Matching** is: *given a piece and a set of candidate places (or a place and a set of candidate pieces), find the valid correspondence under geometric, structural, and aesthetic constraints.*

- **Top-down** fixes the places (the template slots derived from the form) and searches pieces (which block yields this voussoir).
- **Bottom-up** fixes the pieces (the stones you have) and searches places (where does this rubble fit, which edge continues this vein).

Same machine, opposite input held constant. This is why a single matching engine — 2D and 3D — underlies the whole plugin, and why improving it improves *both* directions at once.

### 4.2 The mathematics Frahan already leans on

The matching primitives are well-established and Frahan already implements versions of them:

- **No-Fit Polygon (NFP) / Minkowski sum** — the fundamental 2D tool: the set of non-overlapping relative placements of one shape against another. NFP answers "where can this piece touch that piece without overlapping," which is *exactly* the matching question in 2D. Frahan's Pack2D / NFP family is this. (For irregular containers, the placed-shape NFPs are clipped against the inner-container NFP — note the known cache-invalidation subtlety, which connects to the NFP cache-key bug already on the radar.)
- **True-shape nesting into holes** — the generalization that lets pieces nest inside the concavities of other pieces and inside irregular containers. This is bottom-up vein-flow floors and top-down offcut utilization in the same breath.
- **3D packing + signed-distance fields** — the 3D analogue: SDF classification (as in the dry-stone planners) decides how an irregular solid fits into a void or against placed neighbours. Frahan's Pack3D / Block Pack Tree + the CGAL/Geogram SDF machinery is this.
- **Constrained registration** — fitting a real scanned piece to a target pose (top-down: part to template; bottom-up: stone to goal surface). This is the same best-fit primitive as Quarra's deviation loop and the dry-stone planners' "align to goal surface."
- **Edge matching** — for the aesthetic-flow requirement: matching piece *edges* so veins, textures, and joint lines continue across neighbours. Frahan's EdgeMatch / Trencadís Edge Match is this; it is what makes bottom-up *beautiful* rather than merely stable.

### 4.3 The aesthetic dimension is not optional

A purely structural matcher would satisfy non-overlap and stability and stop. Frahan's matching must also satisfy **flow** — vein direction, edge continuity, texture rhythm — because the whole point of stone-as-architecture (the Quarra thesis) is that the material's geological character is the design. This is the difference between a gabion cage and a dry-stone wall, between random tile and Trencadís. Matching in Frahan therefore carries an aesthetic objective alongside the geometric and structural ones. Bed/grain direction in the metadata schema is consumed here, not only at fabrication.

---

## 5. The convergence — why both directions need the same substrate

The deepest reason to hold these two philosophies together is that, computationally, **they converge on identical operations**:

| Operation | Top-down use | Bottom-up use |
|---|---|---|
| **Scan + digitize stock** | find which block yields a part | digitize the irregular inventory |
| **Fracture / grain mapping** | avoid fracture through a voussoir's load path | place rubble with grain, continue veins |
| **Matching (NFP / SDF / nesting)** | template → stone | stone → gap + neighbour |
| **Constrained registration** | cut part → template (deviation loop) | stone → designer goal surface |
| **Edge matching** | align cut faces of adjacent voussoirs | continue vein/texture across rubble |
| **Contact settling** | seat a voussoir on its bed joint | resolve irregular rubble contact |
| **Assembly sequencing + stability** | course a vault so it stands while built | stack a wall stable at each stage |
| **Minimal machined registration faces** | key voussoirs together | index irregular fragments (Feb 2026 limestone) |

Every row is one capability serving both directions. This is the engineering justification for the philosophy: building the substrate once serves the whole vision. It is also why the v1 spine — scan, fracture, slab, decompose, pack, settle, sequence — is the right foundation regardless of which direction a given project runs.

---

## 6. The spectrum, not a binary

Top-down and bottom-up are the poles; most real projects live on the spectrum between them, and **Parallel Nature is the canonical hinge**: a top-down design (divisions derived from a scan of Manhattan schist) realized through bottom-up material reality (matching triangular quarry offcuts to that design, block by block, by hand). The design bent to accommodate the offcuts; the offcuts were chosen to serve the design. Neither pole alone; the productive middle.

Frahan should make this spectrum *navigable*. A project might:

- Start top-down (a fixed Platonic building) but flip to bottom-up at the block level (find real stones that fit the voussoirs, accepting the form must flex slightly to the stock).
- Start bottom-up (a pile of quarry waste) but impose a top-down goal surface (a designer-indicated wall shape the rubble must approximate).
- Run both at once (Parallel Nature): a top-down division and a bottom-up stock, met by matching.

The tools should not force a direction. They should let the designer set what is fixed (form or material) and let matching resolve the rest.

---

## 7. Implications for architecture and design decisions

This philosophy is not decoration; it should steer concrete decisions.

1. **Matching is the core, not a branch.** Invest in one strong, shared matching engine (2D NFP + 3D SDF + registration + edge-matching) because it serves both directions and every output branch. The known NFP cache-key bug and Clipper2 int64 overflow are therefore *core* bugs, not peripheral ones — they sit at the heart of the philosophy.

2. **Carry the metadata that matching needs.** Bed/grain, fracture/GPR zones, vein direction, and provenance must be first-class because matching consumes them in *both* directions (avoid-fracture for top-down, continue-vein for bottom-up). This is the D3 metadata-schema decision in the release plan, justified by the philosophy.

3. **The registration-and-deviation loop is how top-down meets reality.** Prioritize the scan-to-compensated-surface loop (F18) — it is the mechanism that lets ideal forms be cut from imperfect stone, the Quarra lesson and the top-down requirement in one.

4. **Edge-matching and contact-settling are how bottom-up becomes architecture.** Do not treat EdgeMatch, Trencadís, Rubble Settle, and Kintsugi as isolated niches; they are the bottom-up half of the core philosophy. Their consolidation and validation matter as much as the spine's.

5. **Build for the spectrum.** Let the designer declare what is fixed (form or material) and let matching resolve the rest. A "what is sovereign here?" toggle is more useful than two separate toolchains.

6. **Voussoirs and raw-stone matching are the same problem.** When the architectural work begins — designing a 3D building from Platonic geometry and finding the stones to fit it, or designing a fluidic form and extracting quarry stone carved to fit (the Quarra precedents) — it is top-down matching with a structural objective. The same engine that nests a Trencadís floor finds the block for a voussoir. Build it once, aimed both ways.

---

## 8. One-paragraph version, for the project log

> Frahan is organized around two opposite directions of fabrication intent that meet at matching. Top-down is form-first: design the final form (a Platonic-solid building, a fluidic shell), subdivide it into voussoirs or templates, and match quarried stone to fit each part — the stereotomy lineage of Block Research Group's Armadillo Vault and Rippmann's digital stereotomy, and Quarra's Two Horse and City Jeff, where the artist's form is sovereign and real stone is made to conform through registration and deviation compensation. Bottom-up is material-first: start from rubble, offcuts, and irregular stones with their own veins and edges, and let the parts find the form — the dry-stone lineage of Gramazio Kohler's autonomous walls and the 2026 dry-stacked-limestone work, and Quarra's offcut-matched Parallel Nature, where the aesthetic *is* the matching. Both are one operation pointed in opposite directions: top-down matches templates to stones, bottom-up matches stones to each other and to an aesthetic flow, and the NFP/SDF/registration/edge-matching machinery is the shared engine in 2D and 3D. The whole purpose is to make ideal geometry manufacturable from the material of the earth, in both directions — which is why the matching core, the stone-intelligence metadata, and the Quarra-style registration loop are the architectural decisions that matter most.

---

## 9. References

**Top-down / stereotomy:**
- Rippmann, M. & Block, P. "Digital Stereotomy: Voussoir geometry for freeform masonry-like vaults informed by structural and fabrication constraints." IABSE-IASS 2011. (`block.arch.ethz.ch/brg/files/IABSE-IASS2011_Rippmann-Block.pdf`)
- Block Research Group, ETH Zürich — **Armadillo Vault** ("Beyond Bending," Venice Biennale 2016): discrete compression-only stone shell, form-found then voussoir-subdivided. (`link.springer.com/article/10.1007/s00004-018-0407-7`)
- Rippmann, M. "Funicular Shell Design," ETH Zürich, 2016.
- Fallacara, G. (Politecnico di Bari) — contemporary stereotomy with digitally fabricated voussoirs.

**Bottom-up / dry stone + irregular assembly:**
- Johns, R.L., Wermelinger, M., et al. (Gramazio Kohler Research + Robotic Systems Lab, ETH Zürich). "Autonomous dry stone: On-site planning and assembly of stone walls with a robotic excavator." *Construction Robotics*, 2020. (`link.springer.com/article/10.1007/s41693-020-00037-6`)
- "A framework for robotic excavation and dry stone construction using on-site materials." *Science Robotics*, 2023 — constrained registration + SDF classification; Circularity Park 938-element retaining wall. (`science.org/doi/10.1126/scirobotics.abp9758`)
- **Lu, C.-L., Zhu, Z., Olesti, G.P., Scully, P. & Devadass, P.** "Computational Design and Robotic Fabrication of Dry-Stacked Non-Standard Spanning Limestone Assemblies." *Construction Robotics* / ResearchSquare preprint 2025-11-20, posted under CC-BY 4.0. UCL Bartlett. 50-fragment library, selective machining for registration + shear keys, raw-volume preservation; 18-stone three-legged arch built. Methodology: VSA planar segmentation + face-library evaluation + MOO/Pareto generative aggregation + minimum machined-joint modifications. **This is THE canonical Frahan precedent** — see §10.9 for the component-by-component mapping. Preprint: `doi.org/10.21203/rs.3.rs-8019586/v1`; Journal: `doi.org/10.1007/s41693-026-00180-6`; full PDF: `discovery.ucl.ac.uk/id/eprint/10218513/...` (verified live 2026-05-31).
- **Clifford, B. & McGee, W.** *"Cyclopean Cannibalism: A Method for Recycling Rubble."* ACADIA 2017, pp. 404-413. Matter Design / MIT / U-Michigan / Quarra Stone Co. as realizer. Demolition-rubble scanned + algorithmically virtual-set against a variable-thickness global form; recursive 4-sided-polygon stock extraction; trim-by-overlap-then-carve discipline. **Validates Component C's `Trim Style = 0` (straight planar cut) as the canonical adaptive-match default** — see §10.11. Also see `[[project_filename_mismatch_cyclopean]]` for the filename-mismatch note (the file at `reference/A_Review_of_the_State-of-the-Art_Optimization_Algorithms_for_Dimensional_Stone_Cutting.pdf` actually contains this paper).
- Clifford, B. & McGee, W. *"La Voûte de LeFevre: a Variable-Volume Compression-Only Vault."* In *Fabricate 2014: Negotiating Design & Making*, ed. Gramazio / Kohler / Langenberg, pp. 146-153. Zurich: gta Verlag. **The top-down Matter-Design sibling** to Cyclopean Cannibalism; cited in §1B of `wiki/research/design_sensibility/quarra_design_sensibility.md` as a top-down precedent.
- Clifford, B. *The Cannibal's Cookbook: Mining Myths of Cyclopean Constructions.* Boston: Matter Publishing, 2017. The book-length version of the recipe rule-set Cyclopean Cannibalism encodes; §10.11 references its bed-joint / draft-angle / Utah-detail / coursing-sequence schema.
- McGee, W., Durham, C., Zayas, J., Brugmann, S. & Clifford, B. *"Quarra Cairn,"* ACADIA 2017. Sibling Matter-Design paper from the same year; bottom-up variable-mass stone stack; already cited in `frahan_quarra_lecture_implications.md` §6.
- Wibranek, B. & Tessmann, O. "Digital Rubble: Compression-Only Structures with Irregular Rock and 3D Printed Connectors." IASS 2019.
- Furrer, F., Wermelinger, M., Yoshida, H., Gramazio, F., Kohler, M., Siegwart, R. & Hutter, M. "Autonomous Robotic Stone Stacking with Online next Best Object Target Pose Planning." *IEEE ICRA* 2017, pp. 2350-2356. Singapore. Cited in Cyclopean Cannibalism (Clifford & McGee 2017) as the closed-loop physics-simulation precedent; relevant to `[[project_kintsugi_port_pose_composition]]`.

**Matching mathematics:**
- Bennell, J.A. & Oliveira, J.F. "A tutorial in irregular shape packing problems." *J. Oper. Res. Soc.* 60(1), 2009.
- Burke, E. et al. "Complete and robust no-fit polygon generation for the irregular stock cutting problem." *Eur. J. Oper. Res.*, 2007.
- Jones, D.R. "A fully general, exact algorithm for nesting irregular shapes." *J. Global Optim.*, 2013 (QP-Nest; inscribed-circle relaxation).
- NFP / Minkowski-sum nesting fundamentals (Art, Adamowicz & Albano, originators).

**Frahan internal:**
- `frahan_quarra_lecture_implications.md` (the registration-and-deviation discipline)
- `frahan_v1_release_feature_report.md`, `frahan_release_backlog.md`, the architecture-gaps-transition map.

---

## 10. Frahan implementation map (added 2026-05-31 by Claude)

This section maps the §7 implications to concrete files / GUIDs / backlog
rows in the current Frahan repo. It is the contract between the
philosophy in §0-§9 and the executable codebase. Every entry below was
verified live against the repo on 2026-05-31; no invented paths per
AGENTS.md §9.

### §10.1 Matching is the core (§7 implication 1)

The single shared matching substrate spans four sub-engines. Each lives
in a known module of the source tree:

| Sub-engine | Source location (verified) | Direction served | Current state |
|---|---|---|---|
| 2D NFP / Minkowski | `Frahan.StonePack.GH/TwoD/NfpRhino.cs`, `NfpCache.cs`, `NfpBottomLeftFillRhino.cs`, `NfpPack2DComponent.cs` | Both | Working; the **NFP cache-key bug** flagged in §4.2 lives in `NfpCache.cs`. |
| Clipper2 boolean / offset substrate (NFP support) | `Frahan.StonePack.Core/Masonry/Geometry/Clipper2Adapter.cs` | Both | Working; the **Clipper2 int64 overflow** flagged in §7.1 lives here. |
| 3D packing + SDF | `Frahan.StonePack.Core/Masonry/Quarry/BlockPackTree/` + the CGAL/Geogram backend per `[[feedback_mesh_boolean_backend_cgal_geogram]]` | Both | Working in 1-block mode; multi-block scenarios route through CGAL/Geogram. |
| Edge matching | `Frahan.EdgeMatching.Core/*` (Panel, Segment, BoundarySegmenter, PhaseCorrelator, ICP, AssemblySolver, etc.) | Bottom-up (v1) + top-down (v1.x via Component D, see EM-D in `edge_matching_redesign.md`) | Working; redesigned 2026-05-30 with Components A/B/C/D + Hungarian assigner. |
| Constrained registration | (re-uses 3D packing + ICP primitives) + the proposed F18 scan-to-compensated-surface loop in `frahan_quarra_lecture_implications.md` §4.2 | Both | F18 NOT yet built — the Quarra registration-and-deviation discipline gap. |

**Bugs that are now core, not peripheral** (§7.1):

- **NFP cache-key bug** — in `NfpCache.cs`. Cache invalidation when placed-shape geometry changes mid-pack. Promote to v1.0.x patch priority (not v1.x): every other matching feature depends on this.
- **Clipper2 int64 overflow** — in `Clipper2Adapter.cs`. Coordinate ranges in large quarry-scale operations overflow Clipper2's int64 representation. Promote to v1.0.x patch priority.

Both bug fixes are gated on HITL reproduction; existing HITL cards
(`hitl_cards/h_2d_consolidation/`) are the right surface to anchor them.

### §10.2 Metadata that matching needs (§7 implication 2)

The D3 metadata-schema decision lands in:

- `Frahan.StonePack.Core/Quarry/QuarryBlock.cs` — the canonical Frahan typed record (shipped v1.0-rc1, GUID `F2D0BC20-…`). Carries bounds + frame + dimensions + volume + label.
- **Gap (v1.x):** D3 extension — add `BedGrain` (Vector3d), `FractureZones` (Mesh list), `VeinDirection` (Vector3d), `Provenance` (typed record with georeferenced coords). This is the architecture gap that §7.2 names directly. Backlog rows `F19` (Geo Import) and the algorithm references audit `[Algorithm("StoneCutMetadata-D3")]` are the wiring.

### §10.3 Registration-and-deviation loop F18 (§7 implication 3)

Per `frahan_quarra_lecture_implications.md` §4.2 backlog row F18:
*"Scan-to-compensated-surface loop: import post-machining scan → best-
fit to model → deviation map → reverse to compensated surface →
corrective-pass geometry."* This is NOT yet built. The v1 keystone
(`Scan to Block Inventory`) is the data-entry side; F18 closes the loop.

Card to author when F18 lands: `V-COMP` (verify compensated surface from
a real machine-scan reduces measured deviation on a test piece —
Quarra's 62 % reduction is the benchmark).

### §10.4 Bottom-up consolidation (§7 implication 4)

The bottom-up half of the matching philosophy is currently the most
visible (since the Trencadís family and EdgeMatch are the oldest, most
mature parts of the plugin). The pieces:

| Component / module | Source | Card-set |
|---|---|---|
| `EdgeMatch Solve` (renaming → `Trencadis Assembly Solve` per EM04) | `EdgeMatchSolveComponent.cs` | `hitl_cards/edge_matching_v2/` |
| Components A/B/C/D (EdgeMatch family redesign) | `edge_matching_redesign.md` spec; build is EM01-EM03 + EM-D | proposed `panel_match_along_rail/`, `boundary_match/`, `adaptive_panel_match/`, `template_panel_match/` |
| `Random Rubble Pack` | existing | existing |
| `Rubble Drop-Settle` | port from Python in flight; `[[project_rubble_drop_settle]]` | existing |
| `Kintsugi Settle Contact` | `[[project_kintsugi_port_pose_composition]]` | existing |
| `Trencadís` family (Edge Match / Physics / etc.) | existing — validate per `[[feedback_reuse_dont_duplicate_components]]` | existing |
| Surface Packing chain (vein direction hook, v1.x extension per `edge_matching_redesign.md` §10) | existing core + v1.x extension | existing + v1.x |
| Borrowed Earth `Remnant Inventory` + `Stone Brick From Remnant` (P1) | proposed per `borrowed_earth/frahan_implications.md` | proposed |

§7.4 says these matter "as much as the spine's." That overrides any
implicit ranking that treats EdgeMatch as a niche; it is half the
philosophy.

### §10.5 "What is sovereign here?" toggle (§7 implication 5)

The spectrum-navigation feature §6 / §7.5 calls for is NOT yet built.
Concrete proposal:

- A new top-level setting on each matching component: `Sovereignty`
  enum = `Form` / `Material` / `Balanced` (default `Form`).
- `Form` = top-down: the form input is fixed; matching adapts material.
- `Material` = bottom-up: the material input is fixed; matching adapts
  form (e.g. relaxes the rail / template / goal surface within a
  tolerance to accommodate the available pieces).
- `Balanced` = the Parallel Nature mode: both inputs are soft within
  separate tolerances; matching minimises joint residual + form
  deviation + material discard.

Component D (Template Panel Match) is the right first home for this
toggle — its `Strategy` input already mirrors the shape. Add
`Sovereignty` alongside in the v1.x extension.

### §10.6 Voussoirs and raw-stone matching are the same problem (§7 implication 6)

Per `wiki/research/voussoir_stereotomy_integration.md`, the
voussoir → stone matcher uses Hungarian bipartite assignment — the
*same algorithm* Component D uses for template-panel-match. The
`HungarianAssigner.cs` utility in `Frahan.EdgeMatching.Core` (proposed
in `edge_matching_redesign.md` §6) is the shared substrate for:

- Voussoir → quarry-block matching (top-down, structural objective).
- Template-slot → panel-inventory matching (top-down, aesthetic
  objective).
- Future: rubble → goal-surface-cell matching (bottom-up via
  Sovereignty=Material).

One utility, three call sites, three workflows. §7.6 verbatim: "Build
it once, aimed both ways."

### §10.7 The bug-priority recalculation

Combining §10.1 + §10.4: the bugs and gaps that the philosophy
elevates to first-class priority for v1.0.x / v1.1:

| Item | Current ranking | Philosophy-driven ranking | Source location |
|---|---|---|---|
| NFP cache-key bug | "on the radar" (informal) | v1.0.x patch priority | `Frahan.StonePack.GH/TwoD/NfpCache.cs` |
| Clipper2 int64 overflow | informal | v1.0.x patch priority | `Frahan.StonePack.Core/Masonry/Geometry/Clipper2Adapter.cs` |
| F18 scan-to-compensated-surface | v1.1 build (per backlog) | v1.1 priority **lifted** to the same tier as the spine | proposed; `frahan_quarra_lecture_implications.md` §4.2 |
| D3 metadata schema extension (bed/grain/fracture/vein/provenance) | v1.1 spec | v1.0.x schema reservation + v1.1 implementation | `QuarryBlock.cs` + `StoneCutMetadata-D3` |
| EdgeMatch family build (Components A/B/C/D) | v1.1 build | v1.1 priority **at parity with the spine** | per `edge_matching_redesign.md` §8.2 |
| "What is sovereign here?" toggle | not yet on backlog | v1.x — add to backlog | proposed §10.5 above |

This is the §7 implication 1 effect: bugs and gaps in the matching
engine ARE the philosophy's most load-bearing tickets.

### §10.9 The 3D EdgeMatch + Trencadís-with-trim extension (v1.x)

Added 2026-05-31 per Libish: *"we have the necessary backend, we learn
edge matching and Trencadís principles with trimming to 3D blocks of
stone."* This locks the v1.x extension scope and the canonical paper
that grounds it.

**Canonical precedent**: Lu, Zhu, Olesti, Scully, Devadass, *"Computational
Design and Robotic Fabrication of Dry-Stacked Non-Standard Spanning
Limestone Assemblies,"* UCL Bartlett, ResearchSquare preprint
2025-11-20 (DOI `10.21203/rs.3.rs-8019586/v1`; UCL Discovery full text
at `discovery.ucl.ac.uk/id/eprint/10218513/...`; Springer Construction
Robotics journal DOI `10.1007/s41693-026-00180-6`). Eighteen-stone
three-legged limestone arch built from 50 scanned fragments via VSA
planar segmentation + face-library evaluation + MOO/Pareto generative
aggregation + minimum machined joint modifications. This is the
end-to-end Frahan workflow as someone else has actually built it.

**The 3D extension's component scope** — for each 2D Component already
designed in `edge_matching_redesign.md`, build a 3D sibling:

| 2D component | 3D sibling | GUID (proposed) | What changes in 3D |
|---|---|---|---|
| Component B (Boundary Match) | **Component B3D — Block Pair Match** | `D5F10008` | Atomic match between two scanned 3D blocks' planar faces (VSA-segmented). Re-uses `BoundarySegmenter3D` + `ConstrainedIcp3D` + `PhaseCorrelator` already in `Frahan.EdgeMatching.Core`. |
| Component A (Panel Match Along Rail) | **Component A3D — Block Chain Along Thrust Line** | `D5F10009` | Bidirectional walker along a thrust line (UCL §2.1 parabolic curve); per-station place a scanned block; the rail is the thrust curve, not a 2D rail. |
| Component C (Adaptive Panel Match w/ trim) | **Component C3D — Adaptive Block Match w/ minimal-cut trim** | `D5F1000A` | Per UCL §2.7: targeted joint modifications via planar cuts on candidate faces only (no full re-shaping). Trim is a CGAL/Geogram boolean difference of a thin slab from the block, NOT a curve clip. `Trim Style = 0` (planar cut) is the canonical default; `Trim Style = 1` (free-form sculpt) is the v2 stretch. |
| Component D (Template Panel Match) | **Component D3D — Template Block Match** | `D5F1000B` | Designer supplies an N-cell 3D template (voussoir layout from `Voussoir Ingest`); Hungarian assignment of scanned blocks to cells; cost = UCL's three-objective Pareto (angle deviation, Cg deviation from cell-centroid path, endpoint deviation per leg). |

**Shared 3D substrate** — all four 3D components ride on the same
existing Core primitives:

- `Frahan.EdgeMatching.Core/BoundarySegmenter3D.cs` (already exists; 3D
  boundary segmentation).
- `Frahan.EdgeMatching.Core/ConstrainedIcp3D.cs` (already exists; 3D
  ICP with hash-key 3D and `IcpOptions`).
- `Frahan.EdgeMatching.Core/PlanarityTester.cs` (Mode auto-decision
  per `Panel.Mode`).
- `Frahan.EdgeMatching.Core/ProjectionPairFinder.cs` (the 2.5D
  projection bootstrap; v1 surfaces this for 3D Boundary Match).
- CGAL/Geogram boolean backend per
  `[[feedback_mesh_boolean_backend_cgal_geogram]]` — the trim path.
  Frahan's existing routing logic for "slab-cut + staggered decompose"
  is the same flow.

**New utility** needed (single file): a Variational Shape Approximation
implementation (~300 LoC) at `Frahan.EdgeMatching.Core/VsaSegmenter.cs`
to mirror UCL §2.3. Either port from CGAL's `CGAL::Variational_shape_approximation`
or build a minimal C# port. This is the only new domain-side primitive;
everything else is composition.

**MOO/Pareto extension to AssemblySolver**: UCL §2.5-§2.6 use Octopus
(genetic algorithm) for three-objective optimisation. Frahan's
`AssemblySolver` is currently beam-search single-objective; the v1.x
extension adds an optional `Pareto` strategy (alongside `Beam` and
`Agglomerative`) using either Octopus-equivalent NSGA-II or a simpler
weighted-sum reduction. The Pareto framework already exists in
BlockCutOpt v2 (`[[project_blockcutopt_synthesis]]`); lift the shared
multi-objective machinery into `Frahan.Core.Optimization` for reuse.

**HITL card-set** — propose `outputs/.../block_chain_thrust_line/` with
three fixtures:
1. *3-stone open arch* — minimal case; one leg of the UCL precedent.
2. *18-stone three-legged arch* — the UCL build, fully reproduced
   from a Frahan canvas. Pass = stone count + endpoint deviation
   within UCL's reported tolerance.
3. *Mixed inventory with rejects* — start with 60 scanned stones,
   filter via area + angle to ~40, aggregate; pass = MOO converges +
   filtered-out stones reported with reason.

**Backlog rows** — add to v1.x after the 2D family ships (EM01-EM04 +
EM-D from `edge_matching_redesign.md` §8.2):

| Proposed ID | Type | Item | Size | Done when | Depends on |
|---|---|---|---|---|---|
| `EM-3D-B` | BUILD | Component B3D — Block Pair Match | M | 3-stone card-set passes | EM01, VSA utility built |
| `EM-3D-A` | BUILD | Component A3D — Block Chain Along Thrust Line | M | 18-stone arch reproduces | EM-3D-B, EM02 |
| `EM-3D-C` | BUILD | Component C3D — Adaptive Block Match w/ minimal-cut trim | L | UCL §2.7 trim discipline validated | EM-3D-B, EM03, CGAL/Geogram backend |
| `EM-3D-D` | BUILD | Component D3D — Template Block Match | M | Voussoir → block Hungarian assignment passes | EM-3D-C, EM-D, voussoir_stereotomy_integration.md Phase 2 |
| `EM-3D-MOO` | BUILD | Pareto strategy in AssemblySolver (NSGA-II or weighted-sum) | M | Three-objective Pareto Front emitted | shared with BlockCutOpt v2 |
| `EM-3D-VSA` | BUILD | VsaSegmenter.cs utility in `Frahan.EdgeMatching.Core` | M | unit tests pass on UCL's published mesh dataset | none (atom) |

**Cross-reference to bottom-up precedents** (per §10.10 below): the
3D extension simultaneously serves Cyclopean Cannibalism (Clifford &
McGee, ACADIA 2017 — find largest 4-sided polygon in a scanned rubble
mesh + virtual set with overlap-then-carve trim discipline) and the
UCL bridges-both workflow. Both papers' algorithms are subsumed by the
3D component family above; building it once, aimed both ways, per §7.6.

### §10.10 Strategic directive — top-down robust + bottom-up beats industry standard

Added 2026-05-31 per Libish: *"while we make the top down very robust
we will also make the necessary tools form bottom up design that we
already have to be better than the current industry standard."*

This locks the two parallel investment tracks for v1.x:

**Track 1 — Top-down ROBUSTNESS (new build).** The top-down spine is
the newer half of Frahan. v1.x makes it production-grade:
- `Scan to Block Inventory` already in v1.0-rc1 (entry point).
- `Voussoir Ingest` + `Stone Matcher` per `[[voussoir_stereotomy_integration]]`
  Phases 1-2.
- `Template Panel Match` (Component D, D5F10007 proposed).
- F18 scan-to-compensated-surface loop (`[[reference_quarra_lecture]]`).
- Component A3D / D3D from §10.9 above.

**Track 2 — Bottom-up BETTER THAN INDUSTRY STANDARD (improvement of
existing).** Frahan already has a mature bottom-up substrate. The v1.x
investment is making each piece beat the SoA, not adding more:

| Bottom-up component | Industry standard today | Frahan beat-it metric |
|---|---|---|
| `EdgeMatch Solve` / Trencadís Assembly | IAAC MRAC RoboMosaic (2022-23) five-stage pipeline | Same five-stage architecture + 2D Component A/B/C + bidirectional walker + Hungarian top-down sibling + scale-invariant params `[[project_edgematch_scale_invariance]]` |
| `Random Rubble Pack` + `Rubble Drop-Settle` | Gramazio Kohler Autonomous Dry Stone (Johns et al. 2020-2022) on robotic excavator | Same scan-inventory + per-stone-selection + heuristic-planner architecture, but in-Rhino + on-canvas + designer-driven; physics signed off `[[project_rubble_drop_settle]]` |
| `Surface Packing chain` with vein-direction hook | Cyclopean Cannibalism (Clifford & McGee 2017) virtual set with overlap-and-carve | Component C + pattern-matching hook from `edge_matching_redesign.md` §10; vein direction first-class via D3 metadata schema extension §10.2 |
| `Trencadís` family (Edge Match / Physics) | UCL RoboMosaic 2022-23, current state | Composable with surface-packing + vein-flow + scale-invariant + edge-matching kernel sharing primitive substrate per §10.1 |
| `Kintsugi Settle Contact` | Furrer et al. 2017 IROS *Autonomous Robotic Stone Stacking* | Pose composition fix `[[project_kintsugi_port_pose_composition]]`; norm-undo + verifier-gating; threshold-driven |
| `Borrowed Earth Remnant Inventory` + `Stone Brick From Remnant` | Borrowed Earth Collective's own in-house workflow (not generally available) | First open / in-Rhino implementation; v1.x P1 per `borrowed_earth/frahan_implications.md` |

**The beat-the-SoA discipline**: each Track-2 row needs a concrete
benchmark — a paper or built precedent — that Frahan's implementation
demonstrably exceeds on a measurable axis (precision, speed, material
yield, design freedom, or designer-ergonomics). The HITL card-sets are
the validation; cite the SoA paper in the card's pass criterion.

**Bug-priority recalculation** (extends §10.7): Track 2 elevates the
NFP cache-key bug and Clipper2 int64 overflow to *blocker* priority —
they sit in the 2D Trencadís substrate, which is the most-mature
bottom-up component. Industry-standard parity requires those bugs
fixed before any "we beat the SoA" claim is honest.

**Track 1 vs Track 2 cadence**: build Track 1 sequentially (each step
gates the next per `[[edge_matching_redesign]]` §8.1 + voussoir
Phases 1-5); polish Track 2 opportunistically (one card-set + one bug
fix per sprint window). Don't conflate — the top-down build can fail
loudly; the bottom-up polish must be silent and incremental so the
existing user-base on bottom-up workflows doesn't regress.

### §10.11 Cyclopean Cannibalism — explicit Frahan mapping

Added 2026-05-31. Libish supplied the Cyclopean Cannibalism PDF on
2026-05-31; the file at
`reference/A_Review_of_the_State-of-the-Art_Optimization_Algorithms_for_Dimensional_Stone_Cutting.pdf`
is **mislabeled** (filename suggests a stone-cutting-optimization
survey; content is the Clifford-McGee ACADIA 2017 paper). The actual
SoA optimization survey, if it exists, is a separate reference yet to
land. Memory anchor: `[[project_filename_mismatch_cyclopean]]`.

Citation: Clifford, B. & McGee, W. *"Cyclopean Cannibalism: A Method
for Recycling Rubble."* ACADIA 2017, pp. 404-413. Matter Design / MIT /
U-Michigan / Quarra Stone Co. as realizer; exhibited 2017 Seoul
Biennale of Architecture and Urbanism.

**Method-to-Frahan map**:

| Cyclopean Cannibalism step | Frahan analogue |
|---|---|
| 1. Global form: undulating compound-curvature surface, variable thickness | User-supplied geometry input (Track 1 top-down); F18 deviation loop for the variable-thickness gap |
| 2. Demolition rubble scanned to pointcloud | `Load Cloud` / `Load E57 Cloud` (existing) |
| 3. Recursive algorithm finds largest 4-sided polygon in each scan (the **stock polygon**) | NEW utility candidate — *PolygonInscriber* — find largest k-gon in a 2D projection of a mesh face. Similar to NFP inverse problem. |
| 4. Virtual set: orient + place polygon along global geometry per the variable-offset back-plane | Component A3D — Block Chain Along Thrust Line, with `Back-Plane` constraint variant |
| 5. Recipe rule-set: trapezoid → parallelogram → keystone-fill + Utah details + draft redirection | Encodable as Component A's `StrategyHints` enum or as a new component `Cyclopean Recipe Coursing`. RC4-style shape grammar. |
| 6. Stones **overlap each other until no gap is left between them, and the stones are then carved back at these intersections** (verbatim) | **THIS IS Component C's exact discipline** — adaptive trim with minimal cut. The paper validates Component C's `Trim Style = 0` (straight planar cut) as the canonical default. |
| 7. "Doesn't minimize the space between parts, but has to **remove it entirely**" (verbatim, paraphrased) | The semantic distinction between Frahan's NFP-Pack2D (minimise gap, classical nesting) and Component C (carve to fit, no gap). Both are needed; surfacing the distinction is a UX clarity win. |
| 8. Stability check iteratively throughout the virtual-set process | Reuse `[[project_rubble_drop_settle]]` stability model on every Component A station; emit `Stability` output channel. |
| 9. Six-axis robot + rotary table + swarf machining for side faces | Out of Frahan scope (CAM hand-off); but Fabrication Prep Report should carry the swarf-machining metadata. |
| 10. Hand pitching to dress the front face | Out of scope; Fabrication Prep Report flags "allowance for hand-finish" on each part. |

**The quotable line that grounds Component C**: *"This process of
setting is different from nesting algorithms, which operate under the
goal of setting as many geometries into a given bounding condition by
minimizing the residual waste, but maintaining the original geometry
of each set part. The algorithm employed in this process differs in
that it doesn't minimize the space between parts, but has to remove
it entirely, therefore displacing the concept of waste to the amount
of material carved from each part."* (Clifford & McGee, ACADIA 2017,
p. 410.)

This is the bridge between top-down nesting (NFP/SDF, Frahan
Pack2D/Pack3D) and bottom-up adaptive matching (Component C trim).
Both are matching; they differ in what "valid correspondence" allows.
The philosophy in §4 lands here as a published, peer-reviewed sentence.

### §10.12 `[DesignApplication]` component-source attribute (NEW 2026-05-31)

Per Libish 2026-05-31 clarification: *"components themselves can have
the algorithms references and the small application phrase."* The
design-grounded discipline lives in the COMPONENT SOURCE CODE, not just
in the HITL cards.

Each Frahan component carries two sibling declarative attributes:

```csharp
[Algorithm("Beam-search assembly solver",
           "Frahan-original deterministic beam search...",
           Note = "Stage 5 of 5-stage pipeline")]
[Algorithm("Constrained ICP",
           "Besl and McKay 1992...",
           Note = "Stage 4")]
[DesignApplication(
    "Reassemble Trencadis fragments inside a closed boundary by matching their edges.",
    DesignFlow.BottomUp,
    Precedent = "Gaudi Park Guell Trencadis (1900-1914); IAAC MRAC RoboMosaic (2022-23)",
    Tolerance = "mean joint Hausdorff <= 5 mm, coverage >= 95 %",
    CardSet = "wiki/research/hitl_cards/em_2d_trencadis_solve/")]
public sealed class EdgeMatchSolveComponent : GH_Component
```

- `[Algorithm(...)]` — already-existing; tags the COMPUTATIONAL citation
  (the algorithm + its peer-reviewed paper). Multiple per component
  allowed (one per pipeline stage).
- `[DesignApplication(...)]` — NEW. Tags the ARCHITECTURAL citation
  (the architect-facing application phrase + named precedent +
  tolerance + HITL card-set). Exactly one per component.

Source: `Frahan.StonePack.GH/Attributes/DesignApplicationAttribute.cs`.

The `DesignFlow` enum has three values matching §5.1 of the Quarra
sensibility wiki:

- `DesignFlow.TopDown` — designer's form is sovereign; component matches
  material to form (Voussoir Ingest, Scan to Block Inventory, Template
  Panel Match, BlockCutOpt v2).
- `DesignFlow.BottomUp` — material inventory is sovereign; form emerges
  from matching (EdgeMatch Solve, Panel Match Along Rail, Random Rubble,
  Cyclopean Recipe Coursing).
- `DesignFlow.Bridges` — accepts either flow (Stone-Aware Cut Export,
  Fabrication Prep Report, EdgeMatch Options DTO).

**Apply discipline** (started 2026-05-31, task #51 tracks sweep
completion):

| Tier | Components tagged |
|---|---|
| Proof-of-concept (5) | EdgeMatchSolve / EdgeMatchSegments / EdgeMatchOptions / AshlarPack / ScanToBlockInventory |
| Sweep (179) | All remaining components per task #51 |

Once swept, a reflection-based `_FrahanWhichAlgorithm` Rhino command
can dump the full citation chain (algorithm + design) for any selected
component. The Yak package metadata + the plugin discovery UI can read
the `Flow` tag to filter components by design flow.

### §10.13 Document cross-references

- `wiki/research/design_sensibility/quarra_design_sensibility.md` —
  the focused Quarra companion. Read for the "imposition vs negotiation"
  framing and the Quarra-specific precedents.
- `wiki/research/design_sensibility/external_precedents.md`
  — survey of UCL Bartlett / IAAC MRAC / ETH Gramazio Kohler / EPFL CRCL /
  Princeton ECL / MIT Self-Assembly Lab work (14 entries, 4 honest gaps).
  HITL-gated for `wiki/research/design_sensibility/` promotion.
- `wiki/research/voussoir_stereotomy_integration.md` — concrete
  top-down voussoir → Frahan integration plan; the §10.6 entry point.
- `Template-General/outputs/2026-05-30/design/edge_matching_redesign.md`
  — the EdgeMatch family redesign with Components A/B/C/D (bottom-up
  A/B/C + top-down D) and the Hungarian assigner.
- `wiki/specs/release_plan/frahan_quarra_lecture_implications.md`
  — the F18 / F19 / F1A / F1B / F1C backlog and Quarra discipline.
- Memory anchors: `[[feedback_top_down_bottom_up_design]]`,
  `[[project_design_sensibility_quarra_bottom_up]]`,
  `[[project_blockcutopt_synthesis]]`, `[[project_rubble_drop_settle]]`,
  `[[project_edgematching]]`, `[[project_kintsugi_port_pose_composition]]`,
  `[[feedback_reuse_dont_duplicate_components]]`,
  `[[feedback_mesh_boolean_backend_cgal_geogram]]`.

---

## 11. Last updated

- 2026-05-29 — initial authorship (Libish), §0-§9.
- 2026-05-31 — canonicalised at `wiki/specs/`; §10 Frahan implementation
  map added by Claude with verified file paths, GUIDs, and bug
  locations. Supersedes the earlier Quarra-narrower draft (kept as
  focused companion).

---

*Provenance: synthesises the user's stated design sensibility (top-down vs bottom-up, matching as the engine, making Platonic geometry manufacturable from the earth) with deep research into the stereotomy lineage (Block Research Group, Rippmann), the computational dry-stone lineage (Gramazio Kohler, 2026 limestone work), and the NFP/SDF/registration matching mathematics, read against the Quarra precedent and the Frahan component map. §10 implementation map verified live against the repo on 2026-05-31. Promote to `wiki/specs/` after HITL — this draft is already at that location with explicit user approval ("refer this" 2026-05-31).*
