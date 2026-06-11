# Persona map

Where to start, by who you are. Each row names the ribbon entry points
and the shipped examples (under `../examples/`) that demonstrate them
end to end. Use only the evolved components listed here; the legacy
forms they replaced are catalogued in `SUPERSESSION_MAP.md`.

| Persona | Ribbon entry points | Examples | Typical chain |
|---|---|---|---|
| Geologists | Ingest GPR row | 03 / 08 / 09 | GPR File Loader -> Fracture Extract -> Surfaces 3D -> Joint Set/DFN |
| Surveyors | cloud row | 04 / 07 / 26 | Load Cloud/E57/LAS -> ICP -> Georeference CRS -> Bench From Mesh |
| Computational designers / architects | Masonry | 16 / 17 / 27 | Polygonal Wall Generator -> Sequence -> Stability+CRA -> IFC Export; Voussoirs for 21/22 |
| Stone masons | Pack 3D + Fabricate | 11 / 24 / 25 | block subdivision, cut sequencing, gangsaw costing |
| Artists | Sculpt + Match | 05 / 12 / 14 / 15 | Carving Stages, Trencadis Catalog, Kintsugi |
| OSS developers | Lab | (component-level) | Geogram / CGAL / CoACD primitives |

## Notes per persona

- **Geologists.** Start at example 03 (granite radargrams), then 08
  (real Botticino marble GPR to dipping beds), then 09 (uncertainty and
  safe yield). The chain is GPR File Loader -> GPR Fracture Extract ->
  fracture Surfaces 3D -> Joint Set/DFN.
- **Surveyors.** Start at example 07 (the full scan-ingest front end),
  then 04 (scan to bench) and 26 (Loviisa surface fractures). The chain
  is Load Cloud/E57/LAS -> ICP -> Georeference CRS -> Bench From Mesh.
- **Computational designers / architects.** Start at example 27
  (polygonal masonry), with 16 (rubble) and 17 (ashlar) as the wall
  baselines. The chain is Polygonal Wall Generator -> Sequence ->
  Stability+CRA -> IFC Export. For arches and vaults use the Voussoir
  generators, demonstrated in examples 21/22.
- **Stone masons.** Start at example 11 (saw-cuttable block subdivision
  with Block Pack Tree), then 24 (guillotine cut sequence) and 25
  (gangsaw cost on real marble).
- **Artists.** Start at example 05 (carving stages / pointing machine),
  then 12 (Trencadis Catalog mosaic), 14 (Kintsugi reassembly), and 15
  (statue to blocks).
- **OSS developers.** The Lab tab exposes the Geogram / CGAL / CoACD
  primitives that the higher-level components compose. Every monolith
  is a facade over these published primitives; read the Lab component
  hovers for the algorithm citations and `[RelatedComponent]` links.

## Related docs

- `SUPERSESSION_MAP.md` — legacy -> evolved component mapping with the
  benchmarks.
- `INSTALL.md` — deploy the `.gha` before opening any example.
- `../examples/README.md` — the full example table and run order.
