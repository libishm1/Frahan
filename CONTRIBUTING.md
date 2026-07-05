# Contributing to Frahan StonePack

Style: short sentences, no em dashes. By contributing you agree your work is licensed GPL-3.0.

Frahan runs on a collaboration model borrowed from large-scale formalized
mathematics (Terence Tao's "blueprint" projects, e.g. Equational Theories):
the work is decomposed into small, self-contained, independently verifiable
nodes; an automated judge checks every contribution; credit is self-reported
per role. You do not need to understand the whole plugin to contribute one
node.

## 1. The automated judge (our "Lean")

A contribution is accepted when the machine checks pass, not when someone
likes it:

- **Test battery**: `tests/Frahan.StonePack.Tests` (~1,050 hand-registered
  tests). Run headless: `FRAHAN_SKIP_NATIVE=1 dotnet run -c Debug` from the
  test project; `FRAHAN_TEST_FILTER=<substring>` narrows it. Green battery is
  the merge gate. Rhino-runtime tests SKIP without Rhino: expected.
- **Validity gates, not opinions**: packing layouts pass the exact boolean
  0-overlap validator; masonry results pass the RBE/CRA equilibrium check;
  matched edges are gated by residuals (plus the discrete Frechet primitive
  for ordered worst-case gaps). If your change breaks a gate, the gate wins.
- **Benchmark protocol**: perf claims are measured on the reporting machine,
  against a named baseline, with the SAME validity checker on both sides.
  Format: `docs/results/RESULTS.md` (the OpenNest head-to-head is the
  reference example). Run `tools/Frahan.StonePack.Harness --packbench` /
  `--pack2dstudy` for packer changes and attach the numbers.
- **Determinism**: solvers are deterministic (seeded where random). Tests pin
  this; keep it true.
- **Truth criterion (c)**: geometric results are additionally validated
  VISUALLY in Rhino (`.3dm`/`.gh` on canvas) before being called done.

## 2. The blueprint (find a node, take a node)

Work is decomposed into nodes with explicit dependencies, tracked as GitHub
Issues labeled `blueprint`. Each node states: dependencies, its done-criterion
(which test or gate proves it), and size. Current tracks:

- **Edge matching** (see
  `wiki/research/edge_matching_theory_vs_implementation.md`, R1-R6): wire the
  Frechet gate into the block matchers' accept step; geometric hashing for
  partial rims; point-to-plane ICP fine stage; wire edge-match output into
  the fabrication export; implement the advertised `MatcherRegistry`
  MILP solver (OrTools).
- **Infrastructure**: convert RhinoCommon `HintPath` references to
  `PackageReference` (unlocks CI on GitHub runners: the single highest-value
  node, it makes the battery a fully automatic PR judge); then a CI workflow
  running the headless battery on every PR.
- **UX**: move the scan/cloud loaders from Mesh into Ingest; demote
  backend-variant duplicates (Repair/Decimate x3-4) to secondary exposure;
  per-component icons for the shared-icon clusters.
- **Validation**: run an example on your own scanner / GPR / quarry data and
  report; add a benchmark instance the suite lacks; reproduce a RESULTS.md
  number and file any deviation.
- **Docs**: the site is <https://libishm1.github.io/Frahan/>; fixes and gaps
  welcome.

Claim a node by commenting on its issue. Missing node? Open an issue that
proposes it AS a node (dependencies + done-criterion + size) before coding.

## 3. Ground rules (invariants the judges assume)

Before you start: read `AGENTS.md` (working rules for humans and agents
alike), `handoffs/KNOWN_BUGS.md` (documented traps), and set up per
`docs/INSTALL.md` (Rhino 8 + dotnet net48).

- net48 hygiene: no `Contains(StringComparison)`, no `HashCode.Combine`,
  `#nullable disable` per file. Keep `Frahan.StonePack.Core` Rhino-free where
  it already is.
- **Component GUIDs are load-bearing**: never change or reuse one (uniqueness
  is test-enforced). Hide-not-delete: dominated components get `[Obsolete]` +
  `Exposure=hidden`, GUIDs preserved, so old canvases always load.
- **Reuse, don't duplicate**: compose from existing Core solvers and shared
  helpers (`HoleNestShared`, `EdgeMatching.Core` primitives). If two paths
  must diverge, say why in a comment.
- New evolution behind a default-off flag so the legacy path stays
  byte-identical; add a no-regression test. Every behavioral change ships
  with a test that fails before and passes after.
- Epsilons/tolerances are scale-relative unless physical (document which).
- Native code goes through the shim pattern (out-of-process workers for crash
  isolation, `[DllImport]` + free-in-finally), and a bundled native dependency
  ships with ALL its transitive DLLs (see the mpfr-6.dll incident).
- Citations ride with code: algorithms carry `[Algorithm]` attributes citing
  the original paper (author, year, DOI); they surface in component hover and
  the generated reference.
- HITL gates (ask a maintainer first): changes touching > 5 files, changing a
  shipped `ComponentGuid`, deleting/overwriting content you did not author,
  or deviating from a validated approach.

## 4. Credit (self-reported, CRediT-style)

Large collaborations deserve better than an alphabetical list. On your first
merged PR, add yourself to `CONTRIBUTORS.md` with the roles you actually
performed, using [CRediT](https://credit.niso.org/) categories
(conceptualization, methodology, software, validation, data curation,
writing, visualization, ...). That table feeds release notes and the author
metadata of archival releases (`CITATION.cff` / Zenodo); sustained
substantial contributions can be proposed for `.zenodo.json` creator listing.

## Workflow, mechanically

1. Fork; branch from `main`. Small, focused commits; conventional-commit
   subjects (`feat(pack2d): ...`).
2. Build: `dotnet build Frahan.StonePack.sln -c Debug` (Windows; a Rhino 8
   install gives the full battery, the headless subset works without it).
3. Change + test; run the battery.
4. PR with: the node/issue it closes, the PASS lines of the relevant tests,
   and benchmark rows for any perf claim.
5. Review checks the invariants; the battery decides correctness.

## Research contributions

Algorithm work belongs in `wiki/research/` with the three-tier treatment:
PRISMA (statistics) + SLM (math + code) + ROSES (synthesis). Cite the
original papers, and real datasets from `data/` (`data/ATTRIBUTION.md`).

## Datasets

Sample data is in `data/` (LFS at the public step). Honor each dataset's
upstream license (`data/ATTRIBUTION.md`); several are non-commercial or
research-only.
