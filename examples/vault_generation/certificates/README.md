# CRA certificates (raw solver verdicts)

The exact `MasonryStabilityChecker` / `frahan_cra_worker` verdict logs behind the
numbers in `../metrics.json`. Baked into the repo so the certified results are
durable (backed on GitHub), not living only in a local scratch folder.

| file | assembly | verdict |
|---|---|---|
| `barrel2_full.log` | Güell barrel, trimmed + colonnade separated, whole boundary supported | **452 blocks / 841 interfaces — STABLE (Optimal), 5980 N, util 0.92, 0 tension** |
| `barrel2_spring.log` | same barrel, springing + wall supported | **452 / 841 — STABLE (Optimal), 8058 N, util 1.08, 1.6 N** |
| `three_prong_stag_keystone.log` | three-prong staggered shell, hub keystone | 150 / 393 — STABLE (Optimal), 542 N, util 0.92 |
| `three_prong_stag_split.log` | three-prong staggered shell, hub split wedges | 171 / 478 — STABLE (Optimal), 544 N, util 0.92 |
| `portico_barrel_T28_INFEASIBLE.log` | full portico WITH the 7 leaning columns | 541 / 969 — SolverError (columns are the infeasibility source) |

The certified geometry is `../guell_barrel_shell_v002.3dm` (the trimmed,
columns-separated barrel; the correct cut is remove z < 1.5 & y > 3.8).
