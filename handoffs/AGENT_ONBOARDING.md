# Agent onboarding — Frahan StonePack

For an AI agent (or a power-user) picking up development. Read this, then `../AGENTS.md`, then
`KNOWN_BUGS.md`. Style: short sentences, no em dashes.

## What this project is
A Rhino/Grasshopper plugin + research library for stone-fabrication readiness. Pipeline: GPR/scan ->
fracture mapping -> 3D reconstruction -> block packing + cutting -> masonry assembly -> fabrication export.
Five source modules: Core (Rhino-free where possible), GH (.gha), Rhino (.rhp), EdgeMatching.Core,
Kintsugi.Port (GPL-3.0).

## The working contract (non-negotiable)
- Truth criterion (c): a geometric result is true only when visually validated in Rhino. Tests are
  necessary, not sufficient.
- Measure before you claim. Use `tools/Frahan.StonePack.Harness --packbench` / `--pack2dstudy` and the
  test suite. Read the metric definitions in `../wiki/research/` (e.g. 2D yield = stock-utilization).
- net48; ONE `.gha`; file-copy deploy with Rhino closed; out-of-process worker for heavy boolean/recon.
- HITL gates: > 5-file commits, GUID changes, deletions, pushes, deviations from validated approaches.
- Hide-not-delete. No ghost components on the ribbon. No multi-million-vertex mesh internalized in a .gh.

## Where the knowledge lives
- `../wiki/research/` — the SLM (math+code) / PRISMA (stats) / ROSES (synthesis) studies: the 2D + 3D
  packing evolution, the beyond-BLF GLS review, masonry/quarry decisions, GPR, block-cut.
- `../research/` — long-form math derivations + research-level coding context (NFP/IFP feasible region,
  GLS separation, util_stock, volumetric ratios, BlockCutOpt, RBE).
- `HANDOFF_LATEST.md` — the current project state + decisions. `KNOWN_BUGS.md` — the traps.
- `../wiki/algorithms/` + `../wiki/specs/` — validated approaches + architecture decisions.

## How to make a change safely
1. Read the relevant `wiki/research/` study + `KNOWN_BUGS.md`.
2. Implement behind a default-off flag; keep the legacy path byte-identical; add a no-regression test.
3. Build (`dotnet build ... -c Release`), run the test suite + the relevant harness bench.
4. For geometry: file-copy the `.gha` (Rhino closed), open the example in Rhino, visually validate.
5. Checkpoint + small focused commit; ask before > 5-file changes / GUID changes / pushes.

## Orchestration (for multi-step work)
For broad review/audit/migration, fan out parallel subagents with adversarial verification before
committing a finding (the project was built this way). Default pipeline over barriers. Every spawned agent
reads `../AGENTS.md` first.
