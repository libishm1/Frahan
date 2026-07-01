# quadwild-bimdf (bundled third-party workers)

**What:** `bin/quadwild.exe` + `bin/quad_from_patches.exe` — QuadWild quad remeshing
(Pietroni, Nuvoli, Alderighi, Cignoni, Tarini: "Reliable Feature-Line Driven
Quad-Remeshing", SIGGRAPH 2021) extended with the Bi-MDF quantization solver
(Heistermann, Warnett, Campen: Min-Deviation-Flow in Bi-directed Graphs for
T-Mesh Quantization; LEMON/satsuma — **no Gurobi**).

**Source:** https://github.com/cgg-bern/quadwild-bimdf
**Version:** release v0.0.2 (windows-binaries.zip), UNMODIFIED.
**License:** GPL-3.0 (see `LICENSE` in this folder). Complete corresponding
source code is available at the repository above (tag v0.0.2).

**How Frahan uses it:** the "Thrust Quad Remesh (QuadWild)" Grasshopper component
invokes these executables **as separate processes** (command line + OBJ/rosy
files on disk) — the same arm's-length pattern as the other bundled workers.
Frahan StonePack itself is GPL-3.0-licensed, so the combination is
license-compatible in both directions.

`config/` is the upstream configuration tree (the flow configs reference the
nested `config/main_config/*.json` + `config/satsuma/*.json`, so the whole tree
ships). The component copies it to a temp working dir per run.
