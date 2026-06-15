### Frahan StonePack: An Applied Thesis in Support of the Software

### Pre-CAM Stone Fabrication-Readiness for Rhino and Grasshopper

Author: Independent Research
Date: 2026-06-13
Open data. Open source. No university affiliation.

---

## Nature of this thesis

This is an applied thesis. Its primary contribution is the software itself, the
Frahan StonePack Grasshopper plug-in, and this document exists to support that
software development: to derive the mathematics each subsystem implements, to
classify every component by originality against the published literature, and to
validate the results on the Rhino canvas. It is not a theoretical treatise that a
tool happens to accompany. It is the written account of an applied research-and-
development effort whose deliverable is the running system. The reader should treat
the code, the benchmarks, and the example definitions as the primary evidence, and
this document as their structured explanation. Every chapter therefore takes one
shipping subsystem and answers three engineering questions about it: what published
method does it implement, how was that method derived and evolved in code, and what
does the validated result look like in practice.

---

## Abstract

Frahan StonePack is a pre-CAM fabrication-readiness bridge for natural
stone, built as one Grasshopper plug-in (`Frahan.StonePack.gha`) over a
Rhino-free algorithmic core. It sits between design intent and machine-ready
fabrication and answers the questions a CAM package assumes are already
solved for stone: which block to take from a quarry, on which planes to cut
it, in what order, oriented how, packed against which neighbours, and stable
under gravity once set. The system carries two design flows as equals.
Top-down, a target form is imposed and the stone is found or cut to realise
it. Bottom-up, the stock is given and the form emerges from it. A single
data-flow spine, ingest to process to segment to pack-or-cut to stabilise to
fabricate, runs through every workflow, and one shared numeric-hygiene layer
(recenter, scale-relative epsilon, one reconciled tolerance budget) keeps a
pipeline that spans seven orders of physical scale numerically honest.

The originality posture is deliberately conservative and evidence-led. Every
component carries an `[Algorithm]` attribute naming its published source, so
each can be classified honestly: clean-room math built from a citation,
evolved fork over a documented baseline, facade over our own primitives,
vendored permissive library, or flagged original research pending a
prior-art sweep. The repository's research contributions are measured against
the shipping implementation as the baseline, never a re-implementation, and
benchmark numbers are reported, not claimed validated, until seen on the
canvas. Licensing is tracked the same way: copyleft and non-commercial
obligations are quarantined behind optional native shims and an isolated
research-only assembly, so the default install links no copyleft code.

---

## Table of Contents

### Front matter

- [Chapter 0 — Repository Overview and Cross-Cutting Foundations](00_overview.md)

### Chapters

- [01. Two-Dimensional Nesting and Trencadís](chapters/01_two-d-nesting.md)
- [02. Three-Dimensional Packing and Settling](chapters/02_three-d-packing.md)
- [03. Quarry Block-Cutting Optimization](chapters/03_quarry-blockcut.md)
- [04. GPR Fracture and Cavity Mapping](chapters/04_gpr-fracture.md)
- [05. Masonry Equilibrium and Cyclopean Reassembly (CRA)](chapters/05_masonry-cra.md)
- [06. Voussoir Geometry and Stereotomy](chapters/06_voussoir-stereotomy.md)
- [07. Surface Packing and Conformal Unwrapping](chapters/07_surface-packing.md)
- [08. Edge-Matching and Fragment Reassembly](chapters/08_edge-matching.md)
- [09. Kintsugi and Learned 6-DoF Pose](chapters/09_kintsugi-pose.md)
- [10. Mesh Processing and Surface Reconstruction](chapters/10_mesh-reconstruction.md)
- [11. Fabrication, Sculpting and Carving](chapters/11_fabrication-sculpt.md)
- [12. Data Ingestion and Format Readers](chapters/12_ingestion.md)
- [13. Lab, Analysis and Reporting](chapters/13_lab-reports.md)
- [14. Workflow Architecture and Data-Flow Connections](chapters/14_workflow-architecture.md)
- [15. Evolution: From Baselines to the Current System](chapters/15_evolution.md)

### Back matter

- [Originality Matrix](90_originality.md)
- [What Is Left: Roadmap](91_roadmap.md)
- [Consolidated Bibliography](99_references.md)

---

## How to read this thesis

Chapter 0 establishes the assembly layering, the ribbon, and the shared
numeric foundations that every later chapter stands on. The numbered chapters
each take one ribbon subsystem, derive its mathematics (including the original
derivations where the repository evolved the math), classify its components by
originality with file-and-line evidence, and embed visually validated example
renders. Chapter 14 is cross-cutting: it maps how the per-subsystem algorithms
connect into the end-to-end workflows of the data-flow spine.

The three back-matter documents are binding. The Originality Matrix
(`90_originality.md`) is the single honest ledger of what is built from
scratch, what extends prior work, and what is vendored, with the per-component
evidence and the full licensing flag register. The Roadmap (`91_roadmap.md`)
is the consolidated, deduplicated and prioritised list of what is left to do,
graded by severity. The Bibliography (`99_references.md`) returns every cited
work, keyed `[Rn]` for stable cross-reference.

A result in this thesis is true only when visually validated in Rhino
(`AGENTS.md` criterion c). Numbers from the headless harness are measured, not
validated, until seen on the canvas. The example renders below are the
visually validated forms.

> **Status (2026-06-13): complete.** All fifteen chapters are written, and the
> three back-matter ledgers are consolidated across all fifteen: the Originality
> Matrix classifies 109 component families (clean-room 59, facade-over-primitives
> 20, evolved-fork 9, original-research 8, vendored-library 5, wrapper-of-native
> 5, direct-port 3) with file-and-line evidence and a thirteen-flag licensing
> register; the Roadmap consolidates every chapter's open items by severity; and
> the Bibliography returns every cited work, citation-normalised.

![Marble bench max-cost block yield from GPR-mapped beds](../../examples/08_gpr_marble/08c_maxcost.png)

![NFP bottom-left-fill 2D nesting result](../../examples/10_pack2d/10_pack2d_result.png)

![Polygonal-masonry castle keep, contact-stable at building scale](../../examples/27_polygonal_masonry/27_10_castle_keep.png)
