# Human onboarding — Frahan StonePack

For a new human contributor. Style: short sentences, no em dashes.

## What it is
A Rhino 8 / Grasshopper plugin that makes stone fabrication-ready: it turns scans + GPR of rock into
mapped fractures, reconstructed benches, packed/cut blocks, and assembled masonry, then exports for
fabrication. Plus a research library of packing, nesting, block-cutting, and masonry-stability algorithms.

## Set up (Windows)
1. Install Rhino 8 and the .NET SDK (net48 target; the repo pins RhinoCommon via HintPath to the Rhino 8
   System folder). See `docs/INSTALL.md`.
2. Build the plugin: `dotnet build src/Frahan.StonePack.GH/Frahan.StonePack.GH.csproj -c Release`.
3. Close Rhino. Copy `Frahan.StonePack.gha` + `Frahan.StonePack.Core.dll` (+ the E57 worker py + native
   libs listed in docs) into `%APPDATA%/Grasshopper/Libraries/`. There is exactly ONE `.gha`.
4. Open Rhino + Grasshopper. The `Frahan` tab has the components. Open an `examples/` definition.

## Run an example
Each `examples/<workflow>/` has a `.gh` + a `.3dm` + a README pointing at `data/`. Open the `.3dm`, open
the `.gh`, press the per-stage `Run` toggles in order. Heavy nodes are gated so the canvas stays responsive.

## Learn the project
- `README.md` — overview + layout. `AGENTS.md` — the working rules (apply to humans too).
- `wiki/research/` — the studies (why each algorithm is built the way it is, with measured numbers).
- `handoffs/HANDOFF_LATEST.md` — current state. `handoffs/KNOWN_BUGS.md` — traps to avoid.
- `CONTRIBUTING.md` — how to contribute (license GPL-3.0, HITL gates, measure-before-claim, tests).

## The one rule that bites people first
Never internalize a multi-million-vertex scan inside a saved `.gh` (Grasshopper autosave rewrites the whole
file on every edit and crashes). Decimate the scan first or reference it externally. See KNOWN_BUGS KB-1.
