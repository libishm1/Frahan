# Grasshopper best practices (Frahan StonePack examples)

The conventions every master-spine example `.gh` follows, so a user can read a canvas cold. Style: short
sentences, no em dashes. Derived from the working/tested HILT cards + SAMPLE_GH_SPEC.

## Canvas layout
- One left-to-right flow by STAGE: 0 COVER -> 1 INGEST -> 2 BENCH -> 3 FRACTURE -> 4 PACK/CUT -> 5 ESTIMATE/REPORT.
- One coloured GROUP box per stage, each with a SCRIBBLE title ("1 INGEST", "2 BENCH", ...). Colour the
  groups consistently across all examples (ingest blue, bench green, fracture orange, pack/cut purple,
  report grey).
- Inputs enter on the LEFT of a group, outputs leave on the RIGHT. Boundary params (sliders, value-lists,
  file paths) sit just left of the node they feed, labelled.

## The COVER group (top-left, every definition)
A Panel with: site, scan file, date, CRS/datum (declare before release), units (m), solver + mesh version.
The engineer report REFUSES to release without a CRS (by design).

## Heavy nodes (Reconstruct, Block Pack, Cascade, GPR migration, Carving)
- A default-FALSE `Run` Boolean Toggle gates every heavy node. The user presses the per-stage Run toggles
  in order and sees a result. Never auto-run a heavy graph on file open.
- Carving Stages stays SYNCHRONOUS + cached (do not make it async; do not reorder its inputs - breaks saved
  files). Async only for source/terminal load-once nodes.
- Show progress on the message line. Every null output emits a Warning naming the missing input + the fix.

## Data references (the new Data/ links)
- Reference datasets from the repo `data/` folder by RELATIVE path where the node allows it, or a documented
  absolute path the README states. Examples and their data:
  - GPR fracture: `data/gpr/grimsel/` (granite, MALA .rd3/.rad), `data/gpr/bondua/` (marble .DT),
    `data/gpr/tu1208/` (multi-rock).
  - Scan ingest / bench: `data/granite_dells_tls/`, `data/tongjiang/`, `data/stanford_scans/`.
  - Packing: `data/eth1100/closed/` + the CSVs.
- NEVER internalize a multi-million-vertex scan in the saved `.gh` (autosave crash, KB-1). Decimate first
  (voxel-cluster to ~150k verts) or reference the `.3dm`/file externally.

## Parameters (sane defaults, scale-clamped)
- Value-lists for Method/Mode/Audience enums. Sliders with sane defaults and ranges clamped to the data
  scale (mm fractures, cm-m blocks, m quarry). Pre-set so pressing Run in order yields a result.
- Tie each example to a design-grounded story: named precedent + a numeric tolerance + a real `data/` fixture.

## Report terminal
- One `Frahan Report / Export` node driven by the Audience enum (0 engineer / 1 artist / 2 geologist).
  Wire the algorithm report records + provenance; read Markdown/CSV; write files with Run.

## Validation
- Truth criterion (c): open the `.gh` + `.3dm` in Rhino, press Run in stage order, SEE the result. A green
  build is not validation. Capture a shaded viewport PNG for the example README.
