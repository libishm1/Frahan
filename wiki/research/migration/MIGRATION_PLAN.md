# Frahan official repo: migration + packaging plan

Date: 2026-06-06. Style: short sentences, no em dashes. Grounded in `WORKSPACE_MAP.md` (the 6-agent
read-only scan). Goal: package everything into ONE clean, well-structured official Frahan repo (private
now, public/open-source later) under MIT, usable by human contributors AND agents the same way Libish uses
it: source + dev install + sample datasets per master-spine workflow + research/math-derivation context +
condensed orchestration rules + known bugs. Execute SLOW AND STEADY with a checkpoint between every
movement stage; make COPIES so old HILT `.gh` dataset links never break.

## 0. Source-of-truth (from the scan)
- Three workspaces in `code_ws`: `Template-General/` (live, 25.9 GB; the real Frahan plugin is buried at
  `Template-General/outputs/2026-05-01/frahan_stonepack/`), `Template-CSharp-GH/` (empty scaffold template),
  `Agent-orchestration-main/` (immutable orchestration-template source, user-protected -- DO NOT move/edit).
- 5 source modules: `Frahan.StonePack.Core`, `Frahan.StonePack.GH` (.gha), `Frahan.StonePack.Rhino` (.rhp),
  `Frahan.EdgeMatching.Core`, `Frahan.Kintsugi.Port` (GPL-3.0 -- LICENSE TRAP, see section 5).
- Datasets sit untracked under `Template-General/raw/2026-05-27/` (Stanford bunny/dragon/buddha/armadillo,
  GPR, Granite Dells TLS, Tongjiang) + links in `wiki/index/data_assets_inventory.md`. No `data/` dir yet.
- Bloat to strip: `native/` (2.1 GB / 66k files), 47 bin/obj (1.48 GB), 5.6 GB .git LFS history; root-lock
  violations (`3dpacking.*`, stray files).

## 1. Target repo structure (clean)
```
frahan-stonepack/                     (new official repo, MIT)
  README.md                           project overview, quick start, screenshots
  LICENSE                             MIT (see section 5 re Kintsugi.Port GPL split)
  CONTRIBUTING.md                     dev + agent contribution guide
  AGENTS.md                           condensed orchestration rules (from the 1035-line canonical)
  CHANGELOG.md
  .gitignore / .gitattributes        (LFS rules for data/large)
  src/                               the 5 modules (code only, no binaries/media)
    Frahan.StonePack.Core/
    Frahan.StonePack.GH/
    Frahan.StonePack.Rhino/
    Frahan.EdgeMatching.Core/
    Frahan.Kintsugi.Port/            (GPL-3.0 -- isolated, optional build flag)
  tools/                             Harness, GprBench
  tests/                             Frahan.StonePack.Tests
  examples/                          MASTER-SPINE examples (the deliverable)
    01_scan_to_blockplan_engineer/   <name>.gh + <name>.3dm + README + data refs -> ../../data/...
    02_scan_to_carving_artist/
    03_scan_to_fracture_geologist/
    ... (one folder per master-spine workflow)
  data/                             sample datasets per workflow (LFS), with ATTRIBUTION.md + links
    eth1100/  granite_dells_tls/  stanford_scans/  gpr/  tongjiang/  ...
  wiki/                             curated owned memory (research, specs, algorithms, papers)
    research/                       slm_cards, slm_spines, roses_synthesis, PRISMA studies
    algorithms/  specs/  papers/  index/
  research/                         long-form math derivations + research-level coding context
  raw/                              immutable evidence (add-only) -- selectively migrated
  outputs/                          dated generated artifacts (selectively migrated: keep final optimal)
  handoffs/                         human + agent handoff markdowns
    HUMAN_ONBOARDING.md  AGENT_ONBOARDING.md  HANDOFF_LATEST.md  KNOWN_BUGS.md
  docs/                             dev install, build, deploy, architecture
```

## 2. What migrates, what is stripped
- KEEP: src (code only), tools, tests, examples (.gh/.3dm), the FINAL optimal research (wiki/research:
  SLM/PRISMA/ROSES, the packing + GPR + masonry studies), known bugs, the condensed orchestration rules,
  the data-asset inventory + links.
- STRIP / EXCLUDE: `native/` (2.1 GB vendored builds -> reference via download/build script in docs/), all
  bin/obj, the 5.6 GB .git LFS history (start a FRESH git history for the clean repo), build media
  (.zip/.gif/.mp4), the empty `Template-CSharp-GH/` scaffold, and `Agent-orchestration-main/` (protected,
  stays in code_ws; its rules are condensed into AGENTS.md, the tree itself is not copied).
- CONDENSE: the 1035-line AGENTS.md -> a focused repo AGENTS.md (orchestration rules + best practices +
  pointers); 30 CHECKPOINT_*.md + handoffs -> one HANDOFF_LATEST.md + an archive.

## 3. Datasets + the data/ folder (the careful part)
1. HUNT (task #42): search all of D drive for the datasets each master-spine workflow needs (Stanford
   scans, ETH1100, Granite Dells TLS, Tongjiang, GPR grids, Loviisa DFN). Record every source path.
2. STAGE: create `code_ws/Data/` and COPY (not move) each dataset in, organized by workflow. Copying (not
   moving) means old HILT `.gh`/`.3dm` links keep resolving to their current paths -- zero breakage.
3. REPATH: for the NEW example `.gh`/`.3dm`, set their data refs to the new `data/` folder (relative). The
   `.3dm` embeds its own geometry; only external-reference nodes need repathing. Make a COPY of each example
   `.gh` for the new repo so the original HILT files are untouched.
4. LFS + ATTRIBUTION: large data goes to Git LFS with a `data/ATTRIBUTION.md` (source, license, citation,
   download link per dataset). Public-step only: actually push LFS; private-step keeps links + small samples.
5. CHECKPOINT after every dataset moved (a one-line entry in `migration/CHECKPOINT_MIGRATION.jsonl`), so a
   crash never loses more than one move.

## 4. Handoffs (human + agent)
- `handoffs/HUMAN_ONBOARDING.md`: what the project is, how to build/install (Rhino 8, .gha file-copy, the
  single-gha rule), how to run the examples, where the research lives.
- `handoffs/AGENT_ONBOARDING.md`: the condensed orchestration rules, the truth criterion (c) visual
  validation, the HITL gates, the memory model (wiki/raw/outputs), the known traps (KB-1..KB-6), the
  measure-before-claim discipline, the workflow/subagent patterns.
- `handoffs/HANDOFF_LATEST.md` + `KNOWN_BUGS.md`: carried from the nightshift registry.
- Research-level context: `research/` holds the math derivations (NFP/IFP feasible-region, GLS, util_stock,
  volumetric ratios, BlockCutOpt, RBE) + the SLM/PRISMA/ROSES studies, so a contributor develops at the
  same depth as Libish.

## 5. License (decision needed)
- Base repo MIT (the user's choice). TRAP: `Frahan.Kintsugi.Port` is a GPL-3.0 port (linking it makes the
  distributed .gha GPL-3.0). RESOLUTION: isolate Kintsugi.Port behind an OPTIONAL build flag / separate
  package so the core .gha ships MIT; document the GPL obligation for anyone enabling it. CONFIRM with
  Libish before packaging.
- Dataset licenses vary (ETH1100, Stanford, Granite Dells, Loviisa CC-BY etc.); `data/ATTRIBUTION.md`
  records each. The user approved upload-with-attribution.

## 6. Staged execution (slow + steady, checkpoint between each)
- STAGE A (done): workspace scan + WORKSPACE_MAP + this plan.
- STAGE B: dataset hunt across D drive -> inventory (paths + sizes + licenses + which workflow). Checkpoint.
- STAGE C: create `code_ws/Data/`, COPY datasets in workflow-by-workflow. Checkpoint per dataset.
- STAGE D: scaffold the clean `frahan-stonepack/` tree (structure above), copy src (code only) + tools +
  tests, fresh .gitignore/.gitattributes. Checkpoint.
- STAGE E: build the master-spine example folders (.gh COPIES + .3dm + README + data refs -> data/).
  Repath the new .gh; verify they still open. Checkpoint.
- STAGE F: condense AGENTS.md + handoffs + wiki/research migration (final optimal research only). MIT +
  CONTRIBUTING + READMEs. Checkpoint.
- STAGE G: repo HEALTH AUDIT (build green from a clean clone, examples open, tests pass, no broken data
  refs, no bloat) + a FINAL STRUCTURE REPORT. Checkpoint.
- STAGE H (public step, later): init the official GitHub repo, add collaborators, push (fresh history),
  enable LFS, migrate large datasets, make public/open-source.

## 7. Risks / guards
- Do NOT touch `Agent-orchestration-main/` (protected). Do NOT move datasets (COPY) until the new refs are
  proven, so HILT files never break. Fresh git history (do not drag the 5.6 GB LFS past). Kintsugi GPL split
  before any public MIT claim. Every stage ends with a checkpoint + (where it touches the live tree) a
  green build. Nothing is deleted from code_ws during migration; the clean repo is built alongside.
```
