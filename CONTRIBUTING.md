# Contributing to Frahan StonePack

Style: short sentences, no em dashes. By contributing you agree your work is licensed GPL-3.0.

## Before you start
1. Read `AGENTS.md` (the working rules, for humans and agents alike) and `handoffs/AGENT_ONBOARDING.md`.
2. Read `handoffs/KNOWN_BUGS.md` to avoid the documented traps.
3. Set up the toolchain per `docs/INSTALL.md` (Rhino 8 + dotnet net48 + RhinoCommon HintPath).

## Workflow
- Branch from `main`. Small, focused commits. Conventional-commit subjects (`feat(pack2d): ...`).
- net48 hygiene: no `Contains(StringComparison)`, no `HashCode.Combine`, `#nullable disable` per file.
- Keep `Frahan.StonePack.Core` Rhino-free where it already is.
- New evolution behind a default-off flag so the legacy path stays byte-identical; add a no-regression test.
- Measure before claiming: a perf/correctness claim needs a `--packbench` / `--pack2dstudy` / test number.
- Truth criterion (c): visually validate the `.3dm`/`.gh` in Rhino before calling a geometric result done.

## HITL gates (ask a maintainer first)
- A change touching > 5 files, or changing/reusing a shipped GH `ComponentGuid`, or deleting/overwriting
  content you did not author, or deviating from a validated approach.
- Hide-not-delete: dominated components get `[Obsolete]` + `Exposure=hidden`, GUIDs preserved.

## Tests + CI
- `tests/Frahan.StonePack.Tests` must stay green (run the xUnit-style runner). Add tests for new behaviour.
- Run `tools/Frahan.StonePack.Harness --packbench` / `--pack2dstudy` for packer changes; attach the numbers.

## Research contributions
- Algorithm work belongs in `wiki/research/` with the three-tier treatment: PRISMA (statistics) + SLM
  (math + code) + ROSES (synthesis). Cite real datasets from `data/` (see `data/ATTRIBUTION.md`).

## Datasets
- Sample data is in `data/` (LFS at the public step). Honor each dataset's upstream license
  (`data/ATTRIBUTION.md`); several are non-commercial or research-only.
