# 36 — Structural stone facade (self-supporting single skin)

A **self-supporting** stone facade — the Stone Federation's third category in
*A Guide to Structural Stone* (June 2026): *"the stacking of large stones to
create a deep, single-skin which supports its own weight … ground to roof, thick
external masonry skin, with no intermediate loadbearing bracketry."*

Big unreinforced granite blocks, running bond, two rectangular windows, each
spanned by a lintel stone bearing onto the jambs. The definition then runs the
**formally verified** structural chain on it.

| | |
| --- | --- |
| wall | 7.2 × 5.4 m, 0.6 m deep single skin |
| blocks | 46 (nominal 1.2 m, 0.6 m minimum — no slivers), 9 courses |
| openings | 2 × (1.8 × 1.8 m) |
| lintels | 2 × 3.0 m (1.8 m clear + 0.6 m bearing each side), 2.86 t each |
| stone | 19.44 m³ ≈ 51.5 t |

## Files

- `36_structural_stone_facade.3dm` — the 46 baked blocks (lintels on their own
  layer). Open this first.
- `36_structural_stone_facade.gh` — the definition. Referenced meshes → Masonry
  Block → Robust Auto Interfaces → Masonry Assembly → Block Build Order, and the
  meshes → Masonry Stability Check (CRA).

## What it reports

```
Stable : True
Report : STABLE | CRA-CERTIFIED (residual 0.30e, 1 iter) | detected contacts
         | blocks free 40, interfaces 96, contact vertices 457
         | max compression 30,614 N | weakest: stone_039 <-> stone_038
```

Build order returns all 46 blocks in 9 layers — a physically valid laying
sequence where no stone is placed before what it rests on.

## The verified part

- **Stability** is the static (safe) theorem of limit analysis, machine-checked
  in Lean as `thm:cra` (`admissibleSet_convex` + `cra_farkas`): the wall stands
  iff an admissible compression-only, friction-bounded force state exists — and
  infeasibility is itself the **certificate of collapse**, not a solver failure.
- **Build order** is `thm:kahn` (`dag_has_source` + `kahn_linear_extension`): on
  an acyclic support graph a valid sequence always exists and the loop never
  stalls.
- Both kernels are re-tested against the shipping C# in CI
  (`verification/Frahan.Verification.Tests`).

## Reading the numbers honestly

**"Worst friction utilisation ≈ 0.92" is not a safety margin.** The safe theorem
asserts that *some* admissible force state exists; the solver returns one such
state, not the least-shear one, so that figure describes the certificate rather
than the structure. It reads about the same for a solid wall as for this facade.

The margin that *is* a property of the structure comes from limit analysis —
tilting the wall until certification is lost:

```
collapse tilt        6.40 deg   (out-of-plane overturning)
lateral load factor  0.112 g equivalent
hand check t/h       0.111      -> agreement within 1%
```

That reproduces the classic monolithic-wall overturning rule (`tan θ = t/h`)
from 46 independent blocks with friction cones — an independent check you can do
on paper. It is also why the guide says a self-supporting skin is *"restrained
back to the structural frame"*: unrestrained, this facade takes only ~0.11 g.

Note that **vertical** capacity is unbounded in this model: rigid blocks have no
crushing strength, so pure vertical load never fails it (measured past 40,000 t).
Vertical capacity is a material question this analysis does not answer.

## Baseline validation

The generator and analysis ship with a self-checking baseline suite
(`demos/StructuralStoneFacade`, `dotnet run`), **6/6 passing**:

| case | verdict | note |
| --- | --- | --- |
| B0 unsupported block (control) | UNSTABLE / Infeasible | proves the chain does report collapse |
| B1 solid wall | STABLE | plain gravity stack |
| B2 this facade | STABLE | the example |
| B3 openings with **no** lintels | STABLE | stands by **flat-arch action** — the expectation was wrong, not the solver |
| B4 facade + 12 t roof load | STABLE | tributary load carried |
| B5 zero lintel bearing | STABLE | still finds a thrust line |

B3 is the interesting one: a bonded wall spans an opening by arching into the
masonry either side, which the foundation abuts. Masonry really does this, and
the safe theorem certifies exactly that thrust state.

## Reproduce

Open the `.3dm`, then the `.gh`. A cold reopen reproduces the result (0 errors,
0 warnings). To regenerate the geometry and the baseline suite:

```sh
dotnet run --project demos/StructuralStoneFacade -c Release
```
