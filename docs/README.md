# Frahan StonePack — website, wiki &amp; deck

The public site for **Frahan StonePack** — a Rhino 8 / Grasshopper plugin for stone-fabrication
readiness. This folder is a self-contained static site, ready to deploy to **GitHub Pages**.

Software repository: https://github.com/libishm1/Frahan

## Pages

| File | Page | What |
|---|---|---|
| `index.html` | **Overview** | Landing page: pipeline, benchmarks, the 18 subcategories, install |
| `wiki.html` | **Component library** | All 187 components — full I/O ports, algorithm citations, source paths, upstream/downstream links. Hash-routed (`#/`, `#/s/<subcategory>`, `#/c/<component>`) |
| `architecture.html` | **Architecture** | Pipeline spine, code modules, and the live 93-edge connection graph (pan / zoom / click) |
| `research.html` | **Research** | Ten research areas, source-cited benchmarks, datasets, how to cite &amp; reproduce |
| `deck.html` | **Intro deck** | 11-slide new-user walkthrough of the whole pipeline |

All five pages share `styles.css` and the `← → top` navigation, and link to each other.

## Data — single source of truth

Everything is driven by **`data.js`**, generated from the plugin's own source bundle
(`components.json` + `connections.json`):

- `window.FRAHAN_COMPONENTS` — 187 components (guid, name, nickname, subcategory, description,
  algorithm citation, inputs, outputs, related edges, icon, source file)
- `window.FRAHAN_SUBCATS` — the 18 subcategories with pipeline-stage accent colours + counts
- `window.FRAHAN_GRAPH` — the 93 upstream→downstream edges for the connection map
- `window.FRAHAN_META` — version, counts, repo

To regenerate after a component change: re-run the plugin's `extract_components.py`, drop the
fresh `components.json` / `connections.json` in, and rebuild `data.js` (the transform lives in the
project history). Icons live in `assets/icons/` (128 PNGs, filename = each component's `icon` field).

## Deploy to GitHub Pages

1. Commit this `docs/` folder to the repository.
2. **Settings → Pages → Source: Deploy from a branch**, branch `main`, folder **`/docs`**.
3. The site publishes at `https://<user>.github.io/<repo>/`. `index.html` is the entry point.

`.nojekyll` is included so GitHub serves every file as-is (no Jekyll processing).

All links are **relative**, so the site also works opened straight from disk (`file://`) and from
any sub-path — no base-URL configuration required.

---
Frahan StonePack · v0.1.0-alpha · GPL-3.0 · Libish Murugesan
