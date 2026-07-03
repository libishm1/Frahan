# 47 — Fabrication handoff (pre-CAM cut plan → CAM / robot / COMPAS)

The pre-CAM stone-logic layer in one graph: turn the quarry fabric and the
designed cuts into machine-ready handoffs, without being a CAM. Frahan owns the
stone decisions; the machine's own CAM (or COMPAS) does the rest.

## What it does

```
tongjiang_sets.csv --> Discontinuity Ingest --> Cut Orientation --> DXF Cut Plan
                                                (bench saw grid)    (layered cut sheet)

raw quarry block   --> Block Yield --> cut blocks --> COMPAS Export
                       (max yield, waste-min)        (blocks -> compas_cra / compas_fab)

hypar cut surface  --> Wire-Saw Feasibility          (ruled? developable? kerf offset)
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
- **Block Yield** (Frahan ▸ Fabricate) — saws a raw quarry block (2.1×1.3×1.0 m)
  into rectangular product blocks, flexing the size within ±10% tolerance and
  picking the axis assignment that tiles the block with the least off-cut waste:
  **36 blocks @ 0.52×0.43×0.33 m, 97 % yield** (vs 79 % at a fixed 0.5×0.4×0.3).
  Feed the **Cut frame** from Cut Orientation to cut on the fabric-aligned grid,
  and **Fractures** (joint planes inside the block): the grid slides to dodge them
  and blocks straddled by a fracture are flagged **flawed** (here a dodged vertical
  joint + an oblique one → 26 sound / 10 flawed, **70 % sound yield**). Axis-aligned
  joints are dodgeable; oblique ones cost sound yield — the reason to cut on-fabric.
- **COMPAS Export** (Frahan ▸ Fabricate) — hands the yield-optimised cut blocks (+ optional
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
