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
| blocks | 48 (nominal 1.2 m, 0.6 m minimum — no slivers), 9 courses |
| openings | 2 × (1.8 × 1.8 m) |
| lintels | 2 × 3.0 m (1.8 m clear + 0.6 m bearing each side), 2.86 t each |
| stone | 19.44 m³ ≈ 51.5 t |

## Files

- `36_structural_stone_facade.3dm` — the 48 baked blocks, coloured by build
  order, lintels on their own layer. Open this first.
- `36_structural_stone_facade.gh` — the definition, now driven by the
  **`Structural Wall (Generator)`** component: sliders → generator → its
  exact-joint `Assembly` → Masonry Stability Check (CRA) + Block Build Order.
  Fully parametric — drag `Width`, `Course` or the opening sliders and the whole
  chain re-solves.
- `36_structural_stone_facade.jpg` — the elevation as captured in Rhino.

## What it reports

```text
Stable : True
Report : STABLE | CRA-CERTIFIED (residual 0.36e, 1 iter)
         | exact joints (generator adjacency)
         | blocks free 42, interfaces 101, contact vertices 488
         | max compression 24,142 N | weakest: s006 <-> s007
```

```text
structural stone wall (self-supporting single skin) | 7.2 x 5.4 x 0.6 m
courses 9 @ 0.6 | blocks 48 | lintels 2 | running joints 1
                | joints under a jamb / over a bearing 0
stone 19.44 m3 (51.5 t at 2650 kg/m3) | largest unit 3.00 m
```

Build order returns all 48 blocks in 9 layers — a physically valid laying
sequence where no stone is placed before what it rests on. Note the two lintels
abut exactly at mid-span (each is 1.8 m clear + 0.6 m bearing either side, so
they meet at x = 3.6); the generator warns when bearings would actually overlap.

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

```text
collapse tilt        6.40 deg   (out-of-plane overturning)
lateral load factor  0.112 g equivalent
hand check t/h       0.111      -> agreement within 1%
```

That reproduces the classic monolithic-wall overturning rule (`tan θ = t/h`)
from 48 independent blocks with friction cones — an independent check you can do
on paper. It is also why the guide says a self-supporting skin is *"restrained
back to the structural frame"*: unrestrained, this facade takes only ~0.11 g.

Note that **vertical** capacity is unbounded in this model: rigid blocks have no
crushing strength, so pure vertical load never fails it (measured past 40,000 t).
Vertical capacity is a material question this analysis does not answer.

## Baseline validation

The generator and analysis ship with a self-checking baseline suite
(`demos/StructuralStoneFacade`, `dotnet run`), **7/7 passing**:

| case | verdict | note |
| --- | --- | --- |
| B0 unsupported block (control) | UNSTABLE / Infeasible | proves the chain does report collapse |
| B1 solid wall | STABLE | plain gravity stack |
| B2 this facade | STABLE | the example |
| B3 openings with **no** lintels | STABLE | stands by **flat-arch action** — the expectation was wrong, not the solver |
| B4 facade + 12 t roof load | STABLE | tributary load carried |
| B5 zero lintel bearing | STABLE | still finds a thrust line — but see KB-16 |
| A1 architectural elevation | STABLE | door to ground + two windows, 72 blocks |

B3 is the interesting one: a bonded wall spans an opening by arching into the
masonry either side, which the foundation abuts. Masonry really does this, and
the safe theorem certifies exactly that thrust state.

## Bond quality — and a fix to the generator (2026-07-25)

Two distinct bond faults matter here, and an earlier version of this example
conflated them:

| fault | what it is | before | after |
| --- | --- | --- | --- |
| running joint | the same head joint in two vertically adjacent courses | 0 | 1 |
| joint under a jamb / over a bearing | a jamb or a lintel end bearing on a joint rather than on stone | 1 (undetected) | **0** |

The second is the serious one — a jamb standing on a head joint has lost its
bearing — and it was present in this very facade: in course 1 a joint sat at
x = 3.0, exactly window W1's right jamb. It went unseen because the number being
reported was the *running joint* count under a label describing the *jamb*
fault.

The cause was the protection rule. It cleared a bad joint by **merging** the two
stones either side, capped at `MaxLength`; with two protected joints one nominal
apart, clearing the first grows a stone to the cap so the second is silently
refused. The generator now **relocates** the joint instead — moving it to the
nearest position that keeps both stones within `[MinLength, MaxLength]` and, if
possible, off a joint in the course below. Merging remains the fallback, and
anything neither can fix is counted and named rather than dropped.

Where no position satisfies both constraints the generator protects the bearing
and accepts the running joint: a jamb on a joint loses support outright, a
running joint only weakens interlock. That trade is why the left column reads 1.

Both counts are pinned by `verification/Frahan.Verification.Tests`
(`StructuralWallTests`, 46 facts), which asserts zero *relocatable* violations
across a matrix of walls, doors and windows, and checks the reported number
against an independently recomputed one.

## Reproduce

Open the `.3dm`, then the `.gh`. A cold reopen reproduces the result. To
regenerate the geometry and run the baseline suite headlessly:

```sh
dotnet run --project demos/StructuralStoneFacade -c Release
```
