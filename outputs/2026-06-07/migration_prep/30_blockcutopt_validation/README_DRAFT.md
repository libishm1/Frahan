# Example 30 - BlockCutOpt quarry block-yield solve (fracture-aware, validated)

Find the rigid pose of a regular cutting grid that maximises the number of dimension blocks that no
fracture plane crosses. This is the top-down quarry decision: given a fractured rock volume and a target
block size, where do you set the cutting grid so the most blocks come out intact. Units: meters. Style:
short sentences, no em dashes.

![Blocks placed inside the fracture mesh](30_blocks_in_fractures.png)
*Intact blocks (the recovered yield) sitting inside the Loviisa fracture-mesh window, at the winning grid
pose. Blocks a fracture would cross are rejected.*

![Recovered intact blocks, stacked](30_blocks_stacked.png)
*The same recovered blocks, restacked. This is the sellable yield from the solved pose.*

## Design problem and named precedent
A quarry pays to cut a rock mass into rectangular dimension blocks. A block that a natural fracture (joint)
crosses splits during cutting and is lost. The recoverable yield depends on where the cutting grid sits
relative to the fracture network: shift or rotate the grid a few centimetres or degrees and the intact-block
count changes. The job is to search grid pose (yaw, two tilts, two translations) and keep the pose that
maximises non-intersected blocks.

Named precedent: Elkarmoty, Bondua, Bruno 2020, "A combined deterministic and probabilistic approach for
the optimization of dimension stone production" (Resources Policy 68:101761). Frahan implements the
deterministic cutting-grid pose search and adds a BVH-pruned OBB/triangle separating-axis test (SAT,
Akenine-Moller 2001) for block-vs-fracture intersection. See `BlockCutOptSolver.cs`.

## What this example validates
This is the one PASS card from the 2026-06-04 validation pack (the others, A1/A2/A3/A5, carried a 1000x
unit bug and are not migrated). It confirms the deterministic `Parallel.For` pose-grid search (commit
e86e43e) returns the SAME winning pose as the pre-evolution serial solver, faster, with an unchanged
evaluation count. N = 163 non-intersected blocks at the winning pose, deterministic across reruns.

A second, heavier run (`30_tn_heavy_dfn_input.3dm` -> `30_tn_heavy_result.3dm`) exercises the same solver
on a denser Tamil-Nadu-style DFN to show it scales past the small bench window.

## Dataset (real, colocated)
Fracture network: Loviisa rapakivi-granite surface fracture-trace map (Chudasama 2022, DOI
10.5281/zenodo.7077494, CC-BY 4.0), clipped to a 12 m window, RECENTERED from UTM to the origin, and
extruded to a fracture mesh. This is the same dataset the shapefile-ingest example (26) reads. The bench
fracture mesh and the tested-area bbox are baked into `30_bench_input.3dm` (layer `Bench_Mesh`), so the
example runs with no download. Full 8-site source: `../../data/loviisa/`; access: `../../data/DATA_ACCESS.md`.

Recentring matters: the raw Loviisa coordinates are ~4.66e5 E / 6.69e6 N. Solving at those magnitudes loses
precision. Recentring to the origin is the T1 numeric-hygiene fix; the card proves no precision loss versus
the raw UTM run.

## Numeric tolerance
Block size set to the bench: Lx 0.7 m, Ly 0.6 m, Lz 0.4 m. Pose-search tilt step psi = 3 degrees.
Geometric tolerance follows the project budget eps_geo = max(floor, 1e-3 * L): at L ~ 0.7 m that is
~7e-4 m, well below the block dimensions and below any saw kerf. The intersection test is exact OBB/triangle
SAT, not a bbox proxy. See `../../wiki/research/tolerances_dimensions_slm_roses.md`.

## Component
`Frahan BlockCutOpt` (Quarry tab; GUID F2D0BC02-1234-4F2D-A0B0-7E60CADA15A2). Inputs auto-wired from
`30_bench_input.3dm`: Tested Area = the fracture bbox -> A; Fractures (layer `Bench_Mesh`) -> F; block
dimensions Lx/Ly/Lz; psi. Outputs: Non-Intersected Count, Best Psi/Dx/Dy, Evaluations, Elapsed (ms), and
the placed intact blocks. BlockCutOpt previously existed in Frahan only as Core source with no user-facing
example; this is its first worked example.

## Measured (this run)
- N = 163 non-intersected (intact) blocks at the winning pose; identical pose and count to the serial
  baseline; lower Elapsed (ms) on a multi-core box; Evaluations unchanged. Verdict: PASS.
- Heavy DFN run completes on the denser Tamil-Nadu network (`30_tn_heavy_result.3dm`), confirming the
  solver scales beyond the bench window.

## Why this matters
Block yield is the number that decides whether a quarry block is worth cutting. A fracture-aware pose
search turns a fracture map (surface traces from example 26, or a GPR depth survey from example 08) into a
cut plan that maximises intact blocks. The output blocks feed the downstream guillotine and gangsaw cost
examples (24, 25). This closes the gap where BlockCutOpt shipped as Core code with no demonstrator.

## Files
- `30_blockcutopt.gh` - the canvas (BlockCutOpt auto-wired from the bench .3dm).
- `30_blockcutopt_CARD.md` - the original HITL validation card (provenance, PASS record).
- `30_bench_input.3dm` - LIVE input: bench fracture mesh (layer `Bench_Mesh`) + tested-area bbox.
- `30_tn_heavy_dfn_input.3dm` - heavier Tamil-Nadu DFN input.
- `30_blocks_in_fractures.3dm` (LFS) - baked result: intact blocks inside the fracture mesh.
- `30_tn_heavy_result.3dm` (LFS) - baked heavy-DFN result.
- `30_canvas.png`, `30_blocks_in_fractures.png`, `30_blocks_stacked.png`, `30_tn_heavy_result.png`.

## Wiki cross-references
- `../../wiki/research/slm_cards/blockcutopt-quarry.md` - the SLM card (grounded math + source files).
- `../../wiki/research/slm_cards/blockcutopt-pareto-fisher-robust.md` - the Pareto/robust extension.
- `../../wiki/research/tolerances_dimensions_slm_roses.md` - the units/scale/tolerance budget.
