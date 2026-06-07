# Voussoir geometry components - build + live validation report

Date: 2026-06-07. Built in `frahan-stonepack` (collaborators repo). Style: short sentences, no em dashes.

## What was built

Two new Grasshopper GEOMETRY GENERATORS, the missing front end of the Voussoir pipeline. Before this, the
arch and vault voussoir cells only existed inside a one-off `run_python` session that was never saved to
disk. That is why examples 21 and 22 could not be reproduced and showed incomplete pieces.

- **Arch Voussoirs** (`D5F10012`, Frahan > Voussoir, primary). Generates an arch as N radial voussoir
  cells: 8-vertex closed wedge solids, bed joints normal to the intrados (the Frezier 1737 / Monge 1798
  radial bed-joint rule). Profiles: 0 Semicircular, 1 Segmental, 2 Pointed (equilateral), 3 Catenary.
  Inputs: Profile, Intrados Radius, Ring Thickness, Width, Count, Included Angle (segmental), Rise
  (catenary), Base Point. Outputs: Cells, typed VoussoirAssembly, Bed Planes, Centroids, Volumes,
  Keystone index, Intrados curve, Report.
- **Pendentive Vault Voussoirs** (`D5F10013`, Frahan > Voussoir, primary). Generates a pendentive (sail)
  dome (sphere over a square) tessellated on a U x V grid into cells along the sphere's lines of curvature
  (Monge), each extruded radially by the shell thickness. Inputs: Sphere Radius, Square Half Width, Shell
  Thickness, Grid U, Grid V, Drop To Ground, Base Point. Outputs: Cells, VoussoirAssembly, Bed Planes,
  Centroids, Volumes, Report.

Both emit a typed `VoussoirAssembly`, so they wire straight into Voussoir Stone Matcher (`D5F10010`) and
the rubble match-and-trim used in examples 21 and 22. New GUIDs only; no shipped GUID was reused.

## Code

- `src/Frahan.StonePack.Core/Voussoir/VoussoirCellFactory.cs` - the geometry math (Rhino.Geometry, no
  Rhino document needed). One shared path: build an intrados Curve per profile, station it by arc length,
  loft 8-vertex wedge cells, offset the extrados along the outward normal. `MakeHexahedron` welds two quad
  rings into a closed solid (UnifyNormals). Catenary solves the chain parameter by bisection.
- `src/Frahan.StonePack.GH/Voussoir/ArchVoussoirsComponent.cs`,
  `src/Frahan.StonePack.GH/Voussoir/PendentiveVaultVoussoirsComponent.cs` - thin GH fronts with
  `[Algorithm]` + `[DesignApplication]` grounding (precedents + tolerance + wiki card set).
- `tests/Frahan.StonePack.Tests/VoussoirCellFactoryTests.cs` - 10 tests (counts, closedness, volume band,
  springers on z=0, X span, keystone index, base-point translate, all four profiles, vault corner-off-
  sphere guard, assembly records ready for the matcher), registered in `Program.cs`.

## Build + deploy

- `dotnet build Frahan.StonePack.GH.csproj -c Release`: 0 errors (warnings pre-existing).
- Deployed file-copy with Rhino closed to `%APPDATA%\Grasshopper\Libraries` (Frahan.StonePack.gha + .dll +
  Core.dll). Backup of the prior assemblies in `deploy_backup/`.
- Headless test harness here cannot init `rhcommon_c.dll` (HRESULT 0x8007045A); the mesh/curve tests SKIP
  offline, same as all native tests. They were validated live instead.

## Live validation (Rhino 8, slot aardvark, truth criterion c)

Component registration (Grasshopper ComponentServer):
- `D5F10012` -> 'Arch Voussoirs' [Frahan > Voussoir] exposure=primary. FOUND.
- `D5F10013` -> 'Pendentive Vault Voussoirs' [Frahan > Voussoir] exposure=primary. FOUND.

Geometry (run_csharp against the live Core):
- ARCH semicircular R=2.0, t=0.55, w=0.6, N=11: cells=11, all closed, keystone index 5, total volume
  **2.3266 m^3** (matches example 21 `arch_metrics.json` exactly), per-cell uniform 0.2115 m^3, bbox
  X=[-2.55,2.55] (span 5.1 = 2(R+t)), Z=[0.00,2.52] (springers on ground), 2 ground anchors.
- VAULT pendentive R=2.5, h=1.6, t=0.4, 6x6: cells=36, all closed, total volume **5.6941 m^3** (example 22
  recorded 5.6793), bbox Z=[0.00,1.84] (drop-to-ground; extrados apex rise 1.84).

Captures (shaded perspective, this folder): `arch_voussoirs.png`, `pendentive_vault_voussoirs.png`,
`voussoir_components_hero.png`. Demo model: `voussoir_components.3dm` (47 cells, 2 layers, m). Every piece
is a clean closed cut-stone voussoir. No raw boulders.

## How this addresses the examples 21/22 complaint

The screenshots showed pieces that were neither a trimmed rubble stone nor a clean cut stone: raw oversized
boulders posed over a cell. Root cause (self-documented in the README + wiki): when a rubble stone does not
fully contain a wedge, the trim fell back to "keep the raw stone surface" and the un-trimmed boulder was
placed as-is. The fix for 21/22: drive the cells from these components and make the fallback emit the CLEAN
CUT cell, never the raw boulder. Every voussoir then is either a CGAL-trimmed rubble OR a clean cut stone.
That regeneration is the next step (NOT YET DONE here).

## Orientation bug found + fixed (correctness)

Live probing the CGAL trim exposed a real bug: `MakeHexahedron` produced INWARD-oriented cells (cell5
signed volume -0.21151). `UnifyNormals` only makes winding consistent, not outward. With an inward solid,
CGAL reads the cell as "everything except the cell", so `Intersection(cell, stone)` returned the stock
minus the cell (coverage 2x to 5x the cell, impossible for a true intersection). Fixed: after
`UnifyNormals`, flip if `Mesh.Volume() < 0`. Test updated to assert positive signed volume. Rebuilt and
redeployed. Live re-check: arch 11/11 + vault 36/36 cells positive signed volume; end-to-end trim
coverage 0.539 (<= 1.0). The shipped component now feeds CGAL correctly.

## Examples 21/22 regenerated (the reported bug, fixed)

Both examples rebuilt live (slot armadillo): cells from the new components, each matched to the best of N
ETH1100 candidate stones, CGAL-intersected to the cell; clean cut-cell fallback if no valid trim. Every
voussoir is bounded by its cell. NO raw boulders.

- Example 21 arch: 11/11 real rubble trims, coverage 94.9% (was 9/11, 74.5%), arch volume 2.3266 m^3.
- Example 22 vault: 36/36 real rubble trims, coverage 98.3% (was 17/36, 85.2%), shell volume 5.6941 m^3.

Staged into `examples/21_stereotomy_rubble_arch/` and `examples/22_pendentive_vault_rubble/` (.3dm + .png
+ metrics.json + README). Originals backed up in `examples_backup/`. Regen captures + .3dm also in
`ex21_regen/` and `ex22_regen/`.

## Status

Components: DONE, live-validated, orientation bug fixed + redeployed. Examples 21/22: DONE, regenerated +
visually validated. Commit to Frahan: PENDING human approval (changes touch > 5 files; HITL gate).
