# Frahan StonePack — Cowork vs Claude Code: who does what

Date: 2026-06-14. A practical division of labour for this repo across the two
Claude surfaces you have, plus the skills and connectors available right now.

## The core constraint that decides everything

| | Cowork (this surface) | Claude Code (Windows) |
|---|---|---|
| Shell | **Linux sandbox** (Ubuntu, g++, python3, no .NET, no Rhino) | Your real Windows box |
| .NET net48 + RhinoCommon build | ✗ cannot | ✓ `dotnet build` |
| Run Grasshopper / live Rhino MCP | ✗ | ✓ (slots, HITL ViewCapture) |
| C/C++ build + run | ✓ native g++ (verified the worker this session) | ✓ mingw static build |
| Edit files in `D:\frahan-stonepack` | ✓ (mounted) | ✓ |
| Web research, docs, figures, planning | ✓ | ✓ (less ergonomic) |
| Connectors (GitHub, Slack, Linear…) | ✓ (once authenticated) | partial |

**Rule of thumb:** anything that must *compile against RhinoCommon* or *render in
Rhino/Grasshopper* belongs to **Claude Code**. Everything that is C++, pure-managed
logic, research, data prep, document generation, or project coordination can be done
in **Cowork** — which is exactly how this session split the discontinuity work.

## What Cowork did well this session (the template)
- Wrote all the C# **source** for Feature A/B (Rhino-light Core + GH components) —
  Claude Code just builds + validates it.
- **Compiled and tested the C++ worker** end-to-end (native g++), with a synthetic
  ground-truth cloud → found the bw=15 sensitivity. C++ needs no Windows.
- Wrote the unit tests, the handoff, and this plan.

## What only Claude Code can finish (queued in HANDOFF_06)
- `dotnet build` Core/GH/Tests; fix any compile nits.
- Run the test runner; confirm the new tests pass.
- Deploy the `.gha`, validate the two components live in Rhino, capture HITL PNGs.
- The self-presenting stereonet card must be eyeballed in a viewport.

---

## Skills available here, mapped to this project

**Engineering skills (most useful for Frahan):**
- `engineering:testing-strategy` — design the regression gate for the worker +
  ingest (pair with `native/.../test/SYNTHETIC_TEST.md`).
- `engineering:code-review` / `security-review` — review the HO5 diff before the
  HITL commit.
- `engineering:debug` — when Claude Code hits a build/runtime error, run this.
- `engineering:architecture` / `system-design` — ADRs for the scan→sets→blockcut
  pipeline boundaries (you already keep `wiki/specs/architectural_decisions_*`).
- `engineering:documentation` — README/runbook for the discontinuity worker.
- `engineering:tech-debt`, `deploy-checklist`, `standup`, `incident-response` —
  project hygiene; `standup` can summarise from GitHub once connected.

**Frahan-specific skills (installed):**
- `academic-writing` — the master paper / thesis chapters (you have an active
  `docs/thesis/`); calibrated to your AEC/robotics voice.
- `robotics-in-architecture` — UR/ROS2/Grasshopper-bridge work (Stage F/G).
- `pdf` / `docx` / `pptx` / `xlsx` — deliverables (benchmark reports, the block-size
  proxy tables, slide decks for Quarra-style reviews).
- `deep-research` — DSE / Palmström / ISRM literature passes (you cite Riquelme,
  Dewez, Palmström already).
- `skill-creator`, `schedule` — author repo-specific skills; schedule nightly tasks.

**Design skills** (`design:*`) — lower priority here; useful only if you build a UI
for the plugin or do a Food4Rhino landing page.

## Connectors available (authenticate before first use)

These are **connecting/authenticating** — run the `…__authenticate` tool once each
in Cowork to enable them. Mapped to plausible Frahan uses:

| Connector | Status | Use for Frahan |
|---|---|---|
| **GitHub** | needs auth | the repo lives at `github.com/libishm1/Frahan` — PRs, issues, releases, CI status, branch `feat/discontinuity-csr-worker`. **Highest-value connector.** |
| **Linear** / **Asana** | needs auth | track HO5/HO6 tasks, the worker bandwidth retune, paper milestones |
| **Notion** | needs auth | mirror the wiki/handoffs; research notes |
| **Slack** | needs auth | post HITL captures / benchmark results to a channel |
| **Atlassian** (Jira/Confluence) | needs auth | if you run Jira/Confluence instead of Linear/Notion |
| **Datadog** / **PagerDuty** | needs auth | not relevant to a desktop plugin (ops/on-call) — skip |
| **Figma** / **Intercom** | needs auth | only if you build plugin UI / user support — skip for now |

## Recommended next moves

1. **Now, in Claude Code (Windows):** execute HANDOFF_06 §3–§4 — build, test,
   deploy, live-validate the two components, capture HITL, then HITL-commit.
2. **In Cowork, in parallel:** (a) authenticate **GitHub**, open a tracking issue +
   draft the PR for `feat/discontinuity-csr-worker`; (b) optionally run
   `engineering:code-review` on the HO5 diff; (c) if you want, retune the worker's
   default bandwidth here (C++ compiles fine) and re-run the synthetic test.
3. **Documentation pass (Cowork):** fold the block-size *proxy/units* caveat and the
   DSE method into `docs/thesis/` via the `academic-writing` skill.
4. **Set a cadence (optional):** `schedule` a weekly `standup` that summarises the
   branch's commits/PRs once GitHub is connected.

## One-line summary
Cowork = source, C++, research, docs, coordination. Claude Code = the RhinoCommon
build and the live-Rhino validation. Hand work across with the `handoffs/` files.
