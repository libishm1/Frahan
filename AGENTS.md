# AGENTS.md — Frahan StonePack (condensed orchestration rules)

Condensed from the code_ws canonical AGENTS.md (1035 lines). The rules an agent OR human contributor must
follow. Style: short sentences, no em dashes.

## 1. Truth criterion
A result is true only when VISUALLY VALIDATED (criterion c): the `.3dm`/`.gh` is opened in Rhino and the
output is seen to be correct. Green tests are necessary, not sufficient. Hardware-in-loop is dormant (d).
Numbers from a headless harness are "measured", not "validated", until seen in Rhino.

## 2. Measure before you claim
Never assert a performance or correctness gain without a measured number from the harness or a test. The
2D/3D packing claims live or die by `--packbench` / `--pack2dstudy`. Read `wiki/research/` for the metric
definitions (e.g. 2D yield = stock-utilization = placed-true-area / (sheet - holes); 80% = good).

## 3. Build + deploy
net48 only. No `Contains(StringComparison)`, no `HashCode.Combine`; `#nullable disable` per file. ONE
`Frahan.StonePack.gha`. Deploy is FILE-COPY with Rhino closed (never build into the live Libraries dir).
In-process CGAL/geogram BOOLEAN can crash Rhino: route heavy boolean/recon through the out-of-process worker.

## 4. Memory model
- `raw/` = immutable evidence, add-only, never edit (provenance header required).
- `wiki/` = curated owned memory, edit subject to review.
- `outputs/` = generated, date-stamped `YYYY-MM-DD/<task>/`.
- `data/` = sample datasets (attribution in `data/ATTRIBUTION.md`).
- Checkpoints: write one between every stage of a long or destructive operation.

## 5. HITL gates (mandatory stops)
Get explicit human approval before: a commit touching > 5 files; changing/reusing a shipped GH
`ComponentGuid`; deviating from a validated approach; deleting or overwriting non-self-authored content;
pushing; any outward/destructive action. Hide-not-delete: mark losers `[Obsolete]` + `Exposure=hidden`,
GUIDs preserved, decide deletion later with data.

## 6. Components + canvas
One canonical type per concept; one `Frahan` ribbon tab. No ghost components: every component must produce
a real, valid output (no stubs on the primary ribbon). Heavy nodes: a default-false `Run` gate; keep the
canvas responsive. Never internalize a multi-million-vertex mesh in a saved `.gh` (autosave crash, KB-1):
decimate or reference externally.

## 7. Research framework (V4)
Three tiers: T0 PRISMA (counted statistics + risk-of-bias), T1 SLM (algorithm math + code), T2 ROSES
(interdisciplinary synthesis). Evolve the shipping implementation against real datasets; baseline is the
shipped engine, not a re-implementation. Keep the Core Rhino-free where it already is.

## 8. Subagents / workflows
For broad fan-out (review, audit, migration), use a workflow of parallel agents with adversarial
verification before committing a finding. Every spawned agent must read this file first. Default to
pipeline() over barriers. Gate workflow use on explicit human opt-in.

## 9. Known traps
See `handoffs/KNOWN_BUGS.md` (KB-1 large-mesh autosave, KB-2 Mesh.Reduce, KB-3 masonry RBE sign, KB-4
concave-NFP overlap, KB-5 cov-metric invariance, KB-6 V506 spacing floor). Read it before development.

## 10. Style
Short declarative sentences. No em dashes. No unsupported claims. No hedging. Report failures plainly with
the output.
