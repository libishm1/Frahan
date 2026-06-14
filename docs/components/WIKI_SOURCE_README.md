# Wiki / website source bundle (auto-generated)

Generated 2026-06-15 for Frahan StonePack v0.1.0-alpha. This is the structured source a
wiki/website generator (e.g. Claude design) consumes to build per-component pages, the
architecture map, and the icon gallery. Everything here is derived from the component
**source code**, so it is always current - regenerate after any component change.

Regenerate: `python extract_components.py` then `python make_icon_sheet.py`.

## Files

| File | What | Use in the wiki/site |
|---|---|---|
| `components.json` | 187 components: GUID, name, nickname, category/subcategory, description, algorithm citation, **inputs**, **outputs**, related-component edges, icon, source path | One page per component (I/O tables, citation, source link) |
| `COMPONENTS.md` | The same, human-readable, grouped by subcategory with input/output tables | Drop-in component reference section |
| `connections.json` | Architecture graph: 187 nodes + 93 directed edges (upstream/downstream from `[RelatedComponent]`) with reasons | Data for an interactive connection map |
| `connection_map.mmd` | The graph as Mermaid (subgraphs per subcategory) | Renders inline on the wiki |
| `connection_map.graphml` | The graph as GraphML | Import into yEd / Gephi / Cytoscape |
| `ICON_LIBRARY.md` | Component->icon table + coverage (0 missing; 50 unused on disk) | Icon reference + cleanup list |
| `icon_library_sheet.png` | Contact sheet of all 128 icons, labelled with usage | Icon gallery image |
| `icons/` | The **128 individual icon PNGs** (the actual icons). Filename matches each component's `icon` field in `components.json` | Per-component icon images for the wiki/site |

## Counts
- 187 components across 18 subcategories (Masonry 35, Quarry 28, Mesh 24, EdgeMatch 14,
  2D Packing 13, Fracture 10, 3D Packing 9, Fabricate 9, Kintsugi 7, Lab 6, Ingest 5,
  Trencadis 5, Voussoir 5, Slab 4, Surface Packing 4, Analysis 3, Reports 3, Sculpt 3).
- 93 connection edges (the upstream->downstream pipeline structure).
- 128 icons on disk; 79 referenced; every component has an icon.

## Suggested wiki structure (for the generator)
1. Landing: the pipeline (GPR/scan -> discontinuity -> DFN -> packing -> masonry -> fabrication),
   rendered from `connection_map.mmd`.
2. One section per subcategory; one page per component from `components.json` (I/O tables +
   algorithm citation + source link + related links as navigation).
3. Icon gallery from `icon_library_sheet.png` / `ICON_LIBRARY.md`.
4. Cross-link via the related-component edges so each page links its upstream/downstream nodes.
