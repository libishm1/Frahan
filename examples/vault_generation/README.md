# Vault generation — compression-only masonry vaults (TNA + whole-shell CRA)

The flagship structural pipeline: form-find a funicular vault, remesh it along the
thrust, turn each cell into a **contact-by-construction voussoir**, and certify the
whole shell in compression-only rigid-block equilibrium (CRA, Kao 2022). Two
geometries run end-to-end: the **Park Güell portico** (barrel + colonnade) and a
**three-prong groin** vault.

## The pipeline (Frahan ▸ Vault)

```text
boundary plan curve
  -> Boundary Net / Catenary Relax / SubD Vault / Catenary Curve   (form-finding, Schek 1974 force density / Gaudi hanging chain)
  -> Thrust Quad Remesh (quadwild-bimdf, our thrust-potential rosy)  (partition follows the thrust)
  -> Quad Cells                                                      (cells / frames / columnness)
  -> Vault Shell CRA  |  Vault Rubble CRA                            (contact-by-construction voussoirs -> compression-only equilibrium)
  -> Trim Shell by Curves -> Fabrication Schedule                   (trim to plan + shop paperwork)
```

`VaultShellAssembly` extrudes each mesh face along the SHARED vertex normals, so
faces meeting at an edge share the extruded rectangle exactly -> contact by
construction (no gap, no contact detection). Interior edges become interfaces;
the lowest-z naked edges (the springing) are fixed as supports. The assembly
feeds `MasonryStabilityChecker` (penalty-RBE QP, friction pyramid K=8, native
OSQP + sparse managed ADMM, out-of-process worker above ~300 blocks).

## The examples

| File | What it shows |
|---|---|
| `guell_formfind_v001` | Güell portico form-finding: plan curve -> force-density hang -> vault surface. |
| `three_prong_formfind_v001` | Three-prong groin form-finding (2nd geometry). |
| `guell_quadstructure_v001` | Güell coarse **structural** thrust-quad shell -> whole-shell CRA. |
| `guell_quadrubble_v002` | Güell thrust-quad -> ETH-stone rubble skin (geometry over the structural shell). |
| `three_prong_quadrubble_v001` | Three-prong thrust-quad -> ETH rubble. |
| `three_prong_staggered_cra_v002` | Whole-shell CRA + **Stagger** (running bond) + Min Block + **Hub Mode** (off / keystone / split). |
| `trim_shell_by_curves_v001` | Trim a shell along plan curves (draw-a-curve-say-cut). |
| `fabrication_schedule_v001` | Voussoirs -> IDs (largest-first), CSV (bbox/volume/weight/centroid), inspection layout. |

## Validated CRA certificates (this repo, 2026-07-02..04)

See `metrics.json` for the machine-readable table; the raw solver verdicts are
preserved in `outputs/2026-07-04/guell_barrel_cra_certificate/`. Highlights:

- **Güell barrel — architecturally trimmed, columns separated off** — STABLE /
  **Optimal** as a **452-block / 841-interface** whole-shell CRA: whole boundary
  max compression **5980 N**, friction util **0.92**, **0 residual tension**
  (`barrel2_full`); springing+wall support **8058 N**, util **1.08**, ~1.6 N
  tension (`barrel2_spring`). This is the authoritative portico certificate — the
  barrel stands once the leaning colonnade is removed. Saved mesh
  `guell_barrel_shell_v002.3dm` (verified: opens as one valid mesh).
- **Three-prong staggered shell CRA** — STABLE / **Optimal** at both hub modes:
  keystone **150 blocks / 393 interfaces** (max comp 542 N), split **171 blocks /
  478 interfaces** (544 N), util 0.92, 0 tension.

### The Güell portico structural finding (settled)

The full portico **with the 7 leaning single-pier columns is infeasible** at every
thickness (0.28 / 0.40 / 0.50), mesh density and trim (8+ runs, all SolverError).
The **barrel VAULT alone is sound** (STABLE / Optimal, above). So the columns —
not the shell — are the infeasibility source; the design lever is the columns
(lean-to-thrust, multi-block piers, or steel cores via Vault Steel Ties), as
Gaudi's real leaning columns + mortar cores resolve.

**Not to be confused with the three-prong supports, which DO certify.** The
three-prong vault's three near-vertical prongs take their thrust into stone
**footer beams / steel seats** (`three_prong_quadcourse_cra`: 32/37 courses
STABLE, full coverage, ~6697 N; `three_prong_staggered_cra`: 150/171 blocks
Optimal). Those supports are tractable; the Güell's 7 *leaning single-pier*
columns are not — as rigid single piers they cannot follow the thrust line.
Splitting each Güell pier into a thrust-following multi-block chain (or coring
it) is the open experiment, not yet certified. (The earlier 2026-06-27 Güell
"columns" work was rubble *geometry* only — CRA was not yet wired.)

### Scale (honest)

The coarse **shell** and the **three-prong** certify in-process / out-of-process.
The **whole-Güell 3159-stone rubble** CRA is the open scale item: the LS-dual step
is still a dense meq^2 Cholesky (fine to ~3k rows), so the 3159-stone assembly
needs a **sparse dual** before it certifies at full skin resolution. The compiled
3159-mould geometry (`guell_rubble_COMPILED_v001.gh`) is the FORM; the STRUCTURE is
certified on the coarse thrust-quad shell.

## Recipes / parameters

Güell dV 0.26 / dC 0.18; three-prong dV 0.15 / dC 0.12 (span/30). shrink 0.92,
seed 18, overfill 1.16, pool AR 2.2, friction mu 0.84, K=8, density 2400 kg/m^3.
Thrust Quad Remesh coarseness: continuous (1 -> 7283 quads, 1.5 -> 3297, 2 ->
1879, 5 -> ~450), alpha 0.05 (even sizing, keeps thin columns).

## Data

Portico `D:\Downloads\guell-vaultarchive.3dm` (Unified_SubD 612 faces + 7 foot
markers). Three-prong `outputs/2026-06-29/three_prong/`. ETH stones bundled subset
`data/eth1100_subset` (16 closed stones). Correct barrel cut
`guell_barrel_shell_v002.3dm` (remove z<1.5 & y>3.8).

See the master handoff `outputs/2026-07-02/thrust_quad_component/HANDOFF_MASTER.md`.

Captured certificates. The authoritative portico result is the **452-block /
841-interface Güell barrel** (`guell_barrel_shell_v002.3dm`) — architecturally
trimmed on the sides and with the leaning colonnade separated off — which
certifies **STABLE / Optimal** (5980 N, util 0.92, 0 tension). The full portico
WITH the 7 columns attached is SolverError/OOM at every tested thickness, density
and trim (541 / 463 / 1336 / 2081-block runs). Raw verdict logs + the colonnade-cut
diagnostics are preserved in `outputs/2026-07-04/guell_barrel_cra_certificate/`.
A separate smaller funicular sanity capture (132 voussoirs, STABLE / Optimal,
359 ms; `figures/whole_shell_cra_barrel.jpg`, blue = supports, green = stable)
shows the in-process path on a clean geometry.
