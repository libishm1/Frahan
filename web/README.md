# Frahan web — client-side 2D nest demo

A browser-only nesting demo, in the SVGnest mould, hosted on GitHub Pages with
**no backend**. Import a DXF or SVG, nest with the actual benchmarked Frahan
engine (compiled to WebAssembly), export the packed layout. The CAD file never
leaves the browser.

## Why no backend is needed

The flagship 2D nester (`ContactNfpHoleNester`) is Rhino-free managed code
whose only dependency is Clipper2 (netstandard2.0). `engine/Frahan.Nest2D`
source-links exactly that closure and builds as a net8 library (proven,
0 warnings). Compiled to WebAssembly it runs entirely in the browser: GitHub
Pages serves the WASM + JS + HTML as static assets, the same way it serves the
docs site today. Zero server, zero hosting cost, and (a real SVGnest property)
the user's geometry stays on their machine.

The native NFP acceleration lane is absent in the browser; the engine degrades
to the managed general-NFP path automatically (the one benchmarked in
`docs/results/RESULTS.md`).

## Status

- [x] `engine/Frahan.Nest2D` — net8 extraction of the nester + `Clipper2Adapter`. Builds clean.
- [x] `NestApi.Nest(requestJson) -> responseJson` — the JSON boundary the browser calls. Smoke-tested (6/6 placed, valid).
- [ ] Blazor WebAssembly host exposing `Nest` via `[JSExport]` (needs `dotnet workload install wasm-tools`).
- [ ] DXF import (parse LINE/LWPOLYLINE/POLYLINE/ARC to closed polygons).
- [ ] SVG import (parse `<path>`/`<polygon>`/`<rect>` to polygons; flatten beziers/arcs).
- [ ] Canvas UI (sheet size, spacing, rotations, boundary-mode toggle; live SVG render of placements).
- [ ] DXF/SVG export of the packed layout.
- [ ] Pages tab: build the WASM app in the docs workflow, copy output under the site, link from nav.

## Build plan (next session)

1. **Blazor WASM host.** `dotnet workload install wasm-tools`; `dotnet new blazorwasm -o web/app`;
   reference `engine/Frahan.Nest2D`. Add a `[JSExport] static string Nest(string json)` shim
   forwarding to `NestApi.Nest`. `dotnet publish -c Release` emits static files under
   `web/app/bin/Release/net8.0/publish/wwwroot`.
2. **Import.** In JS: DXF via a small parser (or `dxf-parser` inlined) and SVG via the DOM,
   both producing `[[x0,y0,x1,y1,...], ...]` flat rings. Curves flattened to polylines at a
   tolerance the UI exposes. Build the `NestRequest` JSON and call the exported `Nest`.
3. **Render + export.** Draw sheet + placed parts to an SVG element from the response
   `PlacedOuter` arrays (colour by validity). Export re-emits DXF (LWPOLYLINE per part at its
   transform) and SVG. All in-browser.
4. **Pages integration.** Add a workflow step: build+publish the Blazor app, copy its
   `wwwroot` to `_site_src/nest/`, add a nav entry "Nest (live)". Keep the WASM payload out of
   git (build artifact); it is produced in CI like the MkDocs output.

## Scope guard

Only the 2D nesting path is deployment-ready (risk register: it clears every
gate; 3D/masonry/geology carry open High/Medium items and stay out of the
browser demo until measured and headless-ified — issues #13, #14). This demo
deliberately ships the one proven-safe subset.
