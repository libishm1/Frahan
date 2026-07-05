# quadwild_bimdf integration — reliable thrust-aligned quad remeshing

For surfaces where our single-chart Poisson remesher (`frahan_quadremesh`) cannot
produce a valid global chart — a **multiply-connected / high-genus-boundary vault**
like the Park Güell portico (8 boundary loops = 1 outer + 7 openings, χ = −6) — we
delegate the global quad layout to **QuadWild + Bi-MDF** and feed it **our thrust
field**. Our physics (compression flow), their robustness (patch tracing + integer
quantization → one watertight all-quad mesh).

## Why (measured)

Our single-chart pipeline is exact on a clean disk (the `Vault_TNA` Güell surface →
387 quads, **0 flips**). On the actual holed portico it folds no matter the field or
the seam:

| approach | flips | residual |
|---|---|---|
| proxy cross-field | 1835 | 0.35 |
| proxy + SeamCut → disk | 1251 | 0.32 |
| thrust-potential field | 4548 | 0.034 |
| potential + SeamCut disk | 1668 | 0.036 |

The residual proves the field is good; the ~1600 flips are singularities a single
global chart cannot place. That is exactly the job of QuadWild's tracing + **Bi-MDF**
integer quantization.

## Result

| pipeline | quads | tris | boundary loops | notes |
|---|---|---|---|---|
| QuadWild default (curvature field) | 8024 | 0 | 8 | 100% quad, watertight, holes kept |
| **QuadWild + OUR thrust `.rosy`** | 7084 | 0 | 8 | 100% quad, watertight, **thrust-aligned** |

Figures: `outputs/2026-06-30/thrust_remesh/figures/quadwild_curvature_quads.png`,
`quadwild_thrust_quads.png`. Result meshes: `outputs/.../portico_quad_curvature.obj`,
`portico_quad_thrust.obj`.

## How the field injection works

QuadWild's `.rosy` is a **per-face** cross-field: line 1 = nFaces, line 2 = 4, then one
unit direction per face. With `do_remesh 0` in the prep config, QuadWild uses OUR mesh
and OUR field directly (no internal isotropic remesh). `frahan_quadremesh --rosy`
computes the thrust-potential field E1 = ∇φ (Δφ = lumped-area, φ=0 at low-z supports)
and writes it per-face in that format. Then:

```
quadwild        m.obj 2 prep(do_remesh 0) m.rosy   # trace our field into patches
quad_from_patches m_rem_p0.obj 123 flow_noalign_lemon.txt   # Bi-MDF/LEMON quantize
```

`run_quadwild_thrust.sh <mesh.bin> <out.obj> [supportFrac]` automates all of it.

## Dependency / license

QuadWild + Bi-MDF is **GPL-3.0** (`https://github.com/cgg-bern/quadwild-bimdf`, Campen
group, Uni Bern). Bi-MDF's LEMON/satsuma solver **replaces Gurobi**, so it is
license-clean for an open build. The binaries are **NOT vendored here** — they are
called arm's-length (a separate process, like `frahan_instantmesh.exe`), so they do
not affect the licensing of our own natives. Fetch a release and point
`QUADWILD_HOME` at the extracted dir:

```
gh release download v0.0.2 -R cgg-bern/quadwild-bimdf -p windows-binaries.zip
# + download config/ from the repo (the flow config references nested json)
export QUADWILD_HOME=/path/to/quadwild-bimdf
```

## Plugin wiring (next)

Async GH component (like the CRA worker): write the mesh blob, shell
`run_quadwild_thrust.sh`, read the quad OBJ back. The thrust field comes from
`ThrustField` (potential mode); bundle the two exes + `config/` under
`install/plugin/thirdparty/quadwild-bimdf/` with the GPL notice.
