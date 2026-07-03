# 47 — Fabrication handoff (pre-CAM cut plan → CAM / robot / COMPAS)

The pre-CAM stone-logic layer in one graph: turn the quarry fabric and the
designed cuts into machine-ready handoffs, without being a CAM. Frahan owns the
stone decisions; the machine's own CAM (or COMPAS) does the rest.

## What it does

```
tongjiang_sets.csv --> Discontinuity Ingest --> Cut Orientation --> DXF Cut Plan
                                                (bench saw grid)    (layered cut sheet)

hypar cut surface  --> Wire-Saw Feasibility        (ruled? developable? kerf offset)

voussoir blocks    --> COMPAS Export               (blocks + frames -> compas_cra / compas_fab)
```

- **Cut Orientation** (Frahan ▸ Fabricate) — orients a rectangular saw grid to the
  5 real Tongjiang joint sets: bench floor + two vertical cuts at az 81°/171°
  following the steep sets, **fit 95%, max obliquity 21°**. Right-prism blocks
  by construction.
- **DXF Cut Plan** (Frahan ▸ Fabricate) — flattens + shelf-nests the cut profiles
  into a CAM-readable DXF (one layer per piece). Here a **dry run** (Write = false)
  lays out 3 profiles; set Write = true to write the .dxf that Alphacam / DDX
  EasySTONE / Breton import.
- **Wire-Saw Feasibility** (Frahan ▸ Fabricate) — checks a target cut surface is
  wire-sawable (ruled ⇒ a straight wire can sweep it) and emits the kerf-offset
  toolpath. The hypar is **ruled but doubly-curved** → sawable, wire twists.
- **COMPAS Export** (Frahan ▸ Fabricate) — hands the voussoir blocks (+ optional
  placement/robot frames) to the COMPAS ecosystem as a stable JSON + a Python
  loader, so a user can run `compas_cra`'s equilibrium solver or `compas_fab`'s
  robot stack. Interop, not compete.

## Use

Open the .gh. The two exporters default to **Write = false** (dry run — no files
written). Set Write = true on DXF Cut Plan / COMPAS Export to write `cutplan.dxf`
and `assembly_compas.json` into this folder. Drag the surface / blocks / joint
sets to your own geometry.

## Data
- `../../docs/validation/discontinuity_ingest_card/tongjiang_sets.csv` — 5 real
  joint sets (CSR worker on the 7.86 M-pt Tongjiang scan).

Validated live 2026-07-03 (all four components solve on canvas; DXF/COMPAS dry runs).
