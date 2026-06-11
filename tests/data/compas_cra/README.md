# compas_cra sample assemblies (test fixtures)

Source: https://github.com/BlockResearchGroup/compas_cra
(main branch, `src/compas_cra/data/samples/`, fetched 2026-06-11).

Licence: MIT. Copyright (c) 2020 - 2022 ETH Zurich, Block Research
Group, Gene Ting-Chun Kao. Full text:
https://github.com/BlockResearchGroup/compas_cra/blob/main/LICENSE

Files (verbatim, unmodified):

| File | Used by their example | Notes |
|---|---|---|
| `type-b.json` | `docs/examples/05_wedge.py` | 3-block friction wedge; the example sets supports `[0, 1]`, rotates the whole assembly 90 deg about +Y through the origin, `mu = 0.84`, `cra_solve(d_bnd=1e-2)` |
| `bridge.json` | `docs/examples/09_bridge.py` | 16-block bridge; supports `[0, 1]`, deck nodes `[11..15]` density 3.51 (others 1), `mu = 0.9`, `cra_penalty_solve(d_bnd=1e-1, eps=0)` |
| `shelf.json` | `docs/examples/07_shelf.py` | 11-block shelf / H-model family; supports `[0]`, `mu = 0.9`, `cra_penalty_solve(d_bnd=1e-1, eps=1e-3)` |

Schema: compas `json_dump` of `compas_assembly` `Assembly` —
`data.graph.data.node.<id>.block.data.{vertex,face}` where `vertex`
is a dict keyed by stringified int (`{x,y,z}` per entry) and `face`
is a dict keyed by stringified int (polygon as a list of vertex
keys, outward winding). `is_support` node attributes are unset in
these files; the example scripts set supports explicitly via
`set_boundary_conditions(...)`.

Parsed by the test helper
`tests/Frahan.StonePack.Tests/CompasAssemblyJson.cs`; consumed by
`CraCompasJsonFixtureTests.cs`. Do not edit these JSON files.
