# Frahan StonePack — architecture review & decisions (2026-07-03)

Grounded in a study of how comparable open-source Grasshopper plugins manage
source and component surface (OpenNest, Wasp, Ladybug Tools, Pufferfish,
Kangaroo, LunchBox, et al.) versus Frahan's current state.

## 1. Current state — inventory

**248 Grasshopper components** in **19 subcategories**, one tab (`Frahan`),
backed by a Core/GH assembly split.

| Subcategory | Comps | Subcategory | Comps |
|---|---:|---|---:|
| Masonry | 39 | Kintsugi | 7 |
| Block (BlockCutOpt) | 33 | Surface Packing | 5 |
| **Lab (CGAL/CoACD/Geogram tests)** | **26** | Ingest | 5 |
| Mesh | 25 | Trencadis | 5 |
| Quarry (geology) | 24 | Voussoir | 5 |
| Vault | 21 | Slab | 4 |
| EdgeMatch | 15 | Reports | 3 |
| Fabricate | 15 | Analysis | 3 |
| Fracture | 10 | Sculpt | 3 |

**Assemblies (the Core/GH split already exists — a strength):**
- `Frahan.StonePack.Core.dll` — algorithms, geometry, IO. Rhino-light (value
  types), largely headless-testable. This is where the ports live (CRA/RBE, TNA,
  packers, discontinuity/geology, fabrication).
- `Frahan.StonePack.gha` — thin `GH_Component` wrappers.
- `Frahan.EdgeMatching.Core.dll`, `Frahan.Kintsugi.Port.dll` — sibling cores.
- Native workers: `frahan_osqp/cgal/coacd/geogram`, `frahan_cra_worker`,
  `frahan_discontinuity_worker`, `frahan_quadremesh`, quadwild-bimdf (bundled).

## 2. How comparable OSS plugins are organized (study findings)

| Plugin | ~Components | Tabs | Core/GH split | Source management |
|---|---:|---|---|---|
| **Pufferfish** | 330 | 1 tab, 13 panels by geometry type | (C#) | one .gha |
| **LunchBox** | 169 | 1 tab (+ separate ML tab) | C# | one .gha |
| **Kangaroo 2** | 110 | 1 tab, Goals nested into 8 sub-panels | C# core + .gha | — |
| **Wasp** | ~90 | 1 `Wasp` tab, **numbered** subcats `1\|Elements`…`X\|Experimental` | `src/wasp` core vs `src/ghComp` wrappers | GhPython → .ghuser |
| **OpenNest** | ~14 | near-flat, under Params | **C++/C# engine ↔ thin GH** | .gha over C++ lib |
| **Ladybug Tools** | ~500–600 | **5 tabs = 5 plugins** (LB, HB, HB-E, HB-R, DF) | **pure-Python SDK cores** vs thin GH repos | per-component **JSON manifest + export codegen**; lockstep SemVer; unified installer |
| **Frahan** | **248** | **1 tab, 19 subcats** | **.Core.dll vs .gha (already)** | hand-authored C#, GUID-keyed |

**Key lessons:**
1. **One tab scales to ~330** (Pufferfish) if subcategorized rigorously. Multiple
   tabs are reserved for **suites** (Ladybug) — separate audiences/dependencies,
   not one product.
2. **Numbered subcategories** (`1 | …`, `NN :: …` — Wasp, Ladybug) force the
   ribbon into workflow order instead of alphabetical.
3. `GH_Exposure` is the demotion lever: `hidden` (nowhere on ribbon/search),
   `obscure` (drops off when narrow, end of dropdown, hidden unless "Show
   Obscure") — the standard way to keep advanced/test components installed but
   out of the everyday ribbon.
4. **Core/GH separation** (logic in a plain .NET lib, thin `.gha` wrappers) is
   the highest-value structural move — Frahan already has it.
5. **Yak + SemVer** is the packaging/versioning norm; components deserialize by
   **GUID**, so never reuse a GUID for changed behaviour — retire old ones as
   `hidden`.
6. **Split into sibling plugins sharing one Core** only at >~350 components OR
   for a distinct audience OR **heavy optional dependencies** a base user should
   not load.

## 3. Findings — where Frahan stands

- **248 in one tab is fine** by the evidence (Pufferfish 330). Frahan does **not**
  need multiple tabs today. Good.
- **The clutter is real but fixable without splitting.** Two specific problems:
  - **`Lab` (26)** — CGAL/CoACD/Geogram **test** components sit on the everyday
    ribbon. That is 26 dev-only tiles a user should never see.
  - **19 subcategories, several tiny** (Reports 3, Analysis 3, Sculpt 3, Slab 4,
    Voussoir 5, Trencadis 5) fragment the ribbon; and there is no `primary`/
    `obscure` tiering, so a golden-path component and a niche one look equal.
- **No Yak package / SemVer** yet — a release blocker, not a code problem.
- **Heavy native dependency stack** (CGAL, geogram, CoACD, GPR/discontinuity
  workers, OSQP, quadwild) is bundled monolithically. A fabrication-only user
  loads the whole geology/quarry native stack they never use. This is the one
  genuine *future* split trigger.

## 4. Architectural decisions

| # | Decision | Priority | Rationale / trigger |
|---|---|---|---|
| **D1** | **Stay one `Frahan` tab through V1.** | Now | 248 < Pufferfish's 330; multi-tab is for suites. Keeps the one-product identity. |
| **D2** | **Demote `Lab` (26) to `GH_Exposure.hidden`** (or `obscure`). | Now (cheap) | Test/dev components off the everyday ribbon — biggest single decluttering win, ~26 tiles. |
| **D3** | **Exposure tiering:** mark the ~25 golden-path components `primary`; advanced ones `secondary`/`tertiary`; niche ones `obscure`. | Now | Creates separator lines + narrow-ribbon demotion within each panel; users see the common path first. |
| **D4** | **Consolidate 19 → ~12 panels + numbered workflow order** (`1 Ingest`, `2 Geology`, `3 Block`, `4 Masonry`, `5 Vault`, `6 Fabricate`, `7 Reports`, …). Fold Reports+Analysis; consider Slab/Voussoir into Block/Vault. | V1 polish | Wasp/Ladybug pattern; ribbon reads as the workflow. Subcategory string only — GUIDs unchanged, `.gh` files unaffected. |
| **D5** | **Keep and lean into the Core/GH split.** GH components stay thin marshaling shims over `*.Core`. | Ongoing | Already a strength; enables headless/CLI/Compute reuse and testing. |
| **D6** | **Ship a Yak package with SemVer.** `yak spec`→`build`→`push`; per-Rhino-major dist tags; MAJOR on breaking I/O, retire changed components as `hidden` (never reuse a GUID). | Release blocker | The packaging/versioning norm; needed for Food4Rhino/Package Manager + the pending public release + Zenodo DOI. |
| **D7** | **Plan a sibling `Frahan.Geo` plugin sharing `Frahan.StonePack.Core`** — geology/quarry (Quarry 24 + Block 33 + Fracture 10 + Ingest 5 ≈ 70 comps) with the heavy native stack (CGAL/geogram/GPR/discontinuity workers). **NOT due yet.** | Deferred | Ladybug suite model. Trigger is **technical**: the install/load cost of shipping the geology native stack to fabrication-only users. Detectable without user feedback. Pre-release there is one install base to protect (≈0 migration cost), so unblocked-but-not-due. |
| **D8** | **Interop bridges as first-class** (COMPAS export, DXF/3DM cut-plan, robot targets). | Done / extend | Interface-not-reimplement: hand off to compas_cra/compas_fab and stone CAM rather than rebuild them. Shipped this session; extend with RoboDK 4×4 next. |
| **D9** | *(Optional, Ladybug lesson)* per-component **metadata/manifest + generated docs**. | Later | Lower ROI in C# (GUID + SemVer already give identity); revisit if docs drift. |

## 5. The one architectural fork that matters

Everything above except **D7** is housekeeping. **D7 is the real decision:**
whether Frahan stays a single plugin or becomes a **two-plugin suite**
(`Frahan.StonePack` for masonry/vault/fabrication + `Frahan.Geo` for
geology/quarry/GPR) sharing one Core.

- **Now:** one plugin. Pre-release, no install base, no migration cost, and the
  clutter is solvable with D2/D3/D4 (exposure + subcategory hygiene).
- **The trigger to split:** when the bundled geology **native stack** (CGAL,
  geogram, CoACD, GPR + discontinuity workers) makes the base install heavy/slow
  enough that fabrication-only users pay for capability they never load. That is
  a measurable technical cost (package size, load time), not a taste call.
- This is consistent with the standing positioning: geology is **fab-input** for
  the Rhino user, and if that input layer's dependencies dominate the install,
  it earns its own separately-installable sibling.

## 6. Priority sequence

1. **D2** demote Lab → hidden (minutes). 
2. **D3** exposure tiering on the golden path (hours).
3. **D4** subcategory consolidation + numbering (hours; ribbon-only, safe).
4. **D6** Yak + SemVer package (release blocker).
5. Public release + Zenodo DOI (the standing release step).
6. **D7** monitor native-stack install cost; extract `Frahan.Geo` when it bites.

See `architecture_radar_2026-07-03.png` for the current-vs-target scoring.
