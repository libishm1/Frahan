# 02 - Frahan Architecture Spec

**Spec version:** 0.2
**Last updated:** 2026-05-22 (Day 2 of the 2-day execution window).
**Supersedes:** 0.1 (preserved in git history; see commit before 44871d1).
**Sources:** runbook §§ 4 and 15.3, the live `Frahan.StonePack.*`
source tree, the live `Frahan.StonePack.sln` solution, and the
2026-05-22 architecture-update draft at
`Template-General/outputs/2026-05-22/strategy/architecture_update_2026-05-22.md`.

## 1. Layered architecture (current state)

```
┌──────────────────────────────────────────────────────────────┐
│  Frahan.StonePack.GH               (.gha, net48)             │
│  ~112 Grasshopper components, 11 ribbon subcategories         │
│  including the new Ingest tab + Lab tab (research probes).    │
│  Frahan.GH.Attributes [Algorithm] + [RelatedComponent]        │
├──────────────────────────────────────────────────────────────┤
│  Frahan.StonePack.Core            (net48 - near-Rhino-free)   │
│  Masonry data model, equilibrium (RBE Kao 2022), solvers,     │
│  packing (Ashlar, BestFit, Trencadis), sequencing             │
│  (Kim 2024), edge matching primitives, BlockCutOpt            │
│  (Elkarmoty 2020 + I1..I14 improvements), quarry/CutOpt,      │
│  surface packing (BFF Sawhney+Crane 2017), geometry kernels   │
│  (CGAL/COACD/Geogram/Clipper2/Greiner-Hormann),               │
│  Masonry/Quarry/Ingestion/ (12 files; vector + GPR readers).  │
├──────────────────────────────────────────────────────────────┤
│  Frahan.EdgeMatching.Core         (net48 - sibling lib)       │
│  Deterministic Trencadis / live-edge solver.                  │
│  5-stage pipeline: BoundarySegmenter -> SegmentHashIndex      │
│  -> PhaseCorrelator (FFT) -> ConstrainedIcp{2D,3D}            │
│  -> AssemblySolver. Auto-dispatch by Panel.Mode.              │
├──────────────────────────────────────────────────────────────┤
│  Frahan.NativeBridge              (net48 - probe + fallback)  │
│  IGeometryBackend / IPackingBackend / NativeBackendLoader     │
│  Probes for native DLLs at first use; falls back to managed.  │
├──────────────────────────────────────────────────────────────┤
│  Frahan.Native.{coacd_shim, cgal_shim, geogram_shim}          │
│  Native C++ shims (P/Invoke). Optional at runtime.            │
└──────────────────────────────────────────────────────────────┘
                             │
                             ▼
                  ┌──────────────────────────┐
                  │  Frahan.StonePack.Rhino  │  ← .rhp, net48
                  │  PlugIn class +          │
                  │  5 commands:             │
                  │    _FrahanStonePackAbout │
                  │    _FrahanBlockCutOpt    │
                  │    _FrahanAshlarPack     │
                  │    _FrahanPackTrencadis  │
                  │    _FrahanWhichAlgorithm │
                  └──────────────────────────┘
```

### Target layered architecture (deferred refactor)

The 2026-05-22 strategic report `porting_tradeoffs.md` keeps the
RhinoCommon split deferred (~2 weeks of focused work). When it lands:

```
┌──────────────────────────────────────────────────────────────┐
│  Frahan.StonePack.GH       (.gha, net48)                      │
├──────────────────────────────────────────────────────────────┤
│  Frahan.StonePack.Surface  (net48 - Rhino.Geometry-bound)     │
│  FrahanSurfaceChart, BarycentricMapper2DTo3D,                 │
│  BffCommandLineRunner, MeshObjIO, ChartScaleComputer          │
├──────────────────────────────────────────────────────────────┤
│  Frahan.StonePack.Core     (net48 - pure managed)             │
│  No RhinoCommon HintPath. Everything else from current Core.  │
└──────────────────────────────────────────────────────────────┘
```

The blocker is the surface-packing subsystem; its types currently
reference `Rhino.Geometry.Mesh`, `Curve`, `Surface`. Logged in
`Template-General/outputs/2026-05-22/hitl_cards/NEXT_STEPS.md`.

## 2. Module map (current state, supersedes AGENTS.md §2 snapshot)

| Path | Responsibility | Owns | Depends-on |
|---|---|---|---|
| `Template-General/outputs/2026-05-01/frahan_stonepack/src/Frahan.StonePack.Core/` | Geometry-runtime-agnostic algorithms (near-Rhino-free; surface subsystem still references Rhino types). | Core .dll, C# code | (none — leaf, modulo the surface debt) |
| `Template-General/outputs/2026-05-01/frahan_stonepack/src/Frahan.EdgeMatching.Core/` | Deterministic Trencadis / live-edge edge-matching solver (5-stage). | EdgeMatching.Core.dll, C# code | RhinoCommon (HintPath), MathNet.Numerics 4.15 |
| `Template-General/outputs/2026-05-01/frahan_stonepack/src/Frahan.StonePack.GH/` | Grasshopper components (.gha). Wraps Core + EdgeMatching.Core for Rhino canvas. ~112 components. | .gha output, C# code | Core, EdgeMatching.Core, RhinoCommon, Grasshopper, GH_IO, KangarooSolver, NetTopologySuite, NetTopologySuite.IO.Esri.Shapefile, NetTopologySuite.IO.GeoJSON |
| `Template-General/outputs/2026-05-01/frahan_stonepack/src/Frahan.StonePack.Rhino/` | Rhino plug-in (.rhp). 5 commands. Wires `MasonrySolverRegistry` on `OnLoad`. | .rhp output, C# code | Core, RhinoCommon |
| `Template-General/outputs/2026-05-01/frahan_stonepack/tests/Frahan.StonePack.Tests/` | Headless test runner (`Program.cs` + `*Tests.cs`). | Test executable, C# code | Core, EdgeMatching.Core, GH, Rhino |

Frozen / not active (do not edit; HITL on touch): `archive/`,
`references/`, `native/coacd_shim/build-with3rd/`,
`Agent-orchestration-main/` (memory-protected).

## 3. Ingestion layer (added 2026-05-22)

`Frahan.StonePack.Core/Masonry/Quarry/Ingestion/` collects every
external-data reader. All readers return Frahan POCO domain types
and have zero RhinoCommon imports.

| File | Domain | Dependencies |
|---|---|---|
| `FractureTrace.cs` | Fracture-trace DTO (FractureTrace + TracePoint2D + FractureTraceCollection) | none |
| `ShapefileFractureReader.cs` | ESRI Shapefile (LineString / MultiLineString) | NetTopologySuite.IO.Esri.Shapefile |
| `GeoJsonFractureReader.cs` | RFC 7946 GeoJSON | NetTopologySuite.IO.GeoJSON |
| `VectorFractureReader.cs` | Dispatcher (.shp / .geojson / .json) | (the above) |
| `GprRadargram.cs` | GPR radargram POCO (GprTrace + GprReflectorPick + GprRadargram) | none |
| `GprRadargramReader.cs` | CSV trace + picks | none |
| `GprSegYReader.cs` | SEG-Y rev 0/1/2 (IBM-float + IEEE-754 BE; formats 1/2/3/5) | none |
| `GprMalaRd3Reader.cs` | MALA RD3 + RAD ASCII header | none |
| `GprDt1Reader.cs` | Sensors & Software DT1 + HD ASCII header | none |
| `GprFileReader.cs` | Dispatcher (.csv / .sgy / .segy / .rd3 / .dt1) | (the above) |
| `GeoFractNetFracture.cs` | GeoFractNet U-Net mask DTO | none |
| `GeoFractNetMaskReader.cs` | GeoFractNet mask reader | none |

Canvas adapters (2 thin GH components in `Frahan.StonePack.GH`):
- `VectorFracturesLoaderComponent` (Frahan > Ingest)
- `GprFileLoaderComponent` (Frahan > Ingest)

## 4. Plug-in commands (added 2026-05-22)

`Frahan.StonePack.Rhino` ships 5 commands:

| Command | Status | Purpose |
|---|---|---|
| `_FrahanStonePackAbout` | shipped earlier | Print version + native-backend probe report |
| `_FrahanBlockCutOpt` | Phase 1 functional 2026-05-22 | Pick tested-area mesh + fracture meshes, prompt block size / kerf / psi step, run `BlockCutOptSolver.Solve`, print scalar result |
| `_FrahanAshlarPack` | Phase 1 scaffold 2026-05-22 | Prints entry-point message + Core API pointer. Phase 2 wiring deferred |
| `_FrahanPackTrencadis` | Phase 1 scaffold 2026-05-22 | Prints entry-point message. Phase 2 wiring deferred |
| `_FrahanWhichAlgorithm` | shipped 2026-05-22 | Reflects on loaded `.gha`, prints `[Algorithm]` citations + `[RelatedComponent]` cross-refs. Supports `_All`, `_Untagged`, `<ClassName>` filters via late-bound reflection (no .rhp -> .gha project reference required) |

Interop helpers shared across commands: `FrahanCommandInterop.cs`
(Box -> BoundingBox3, Mesh -> PlyMesh).

## 5. Algorithm + RelatedComponent attributes

`Frahan.StonePack.GH/Attributes/` defines two declarative attributes
for canvas-side reflection:

- **`[Algorithm(name, citation)]`** — multi-attribute; properties
  Name + Citation + Doi + WikiPath + Note. Applied to GH components
  to record which Core algorithm the component wraps and the
  published paper that algorithm is from.
- **`[RelatedComponent(ribbonPath)]`** — multi-attribute; properties
  RibbonPath + Reason + ComponentGuid. Applied to research / Lab
  components to point at production siblings.

Coverage as of 2026-05-22:
- 18 GH components carry `[Algorithm]` (top-30 by canvas use).
- All 25 Lab components carry `[RelatedComponent]`.
- The `_FrahanWhichAlgorithm` command surfaces both via reflection.

Schema doc + worked examples: see
`Template-General/outputs/2026-05-22/strategy/hitl_card_authoring_workflow.md`.

## 6. Architectural rules

1. The `.gha` (`Frahan.StonePack.GH`) takes a hard dependency on
   Grasshopper and Rhino. It is the only assembly allowed to import
   `Grasshopper.*`.
2. The surface-packing subsystem **currently** lives inside
   `Frahan.StonePack.Core` and references `Rhino.Geometry`. This is
   a known violation; the split into `Frahan.StonePack.Surface` is
   logged in `porting_tradeoffs.md` as deferred 2-week work.
3. The pure-managed parts of `Frahan.StonePack.Core` (everything
   outside `SurfacePacking/`) must compile with no Rhino reference.
   Verified by `dotnet build -p:RestoreLockedMode=true` on net48.
4. `Frahan.EdgeMatching.Core` is a sibling library. It references
   `RhinoCommon` (HintPath) plus `MathNet.Numerics 4.15`.
5. `Frahan.NativeBridge` defines `IGeometryBackend`,
   `IPackingBackend`, and the `NativeBackendLoader`. Native shims
   (`coacd_shim`, `cgal_shim`, `geogram_shim`) are concrete
   implementations probed at runtime; absence falls back to managed.
6. The `Frahan.StonePack.Rhino` `.rhp` shell is responsible for
   plug-in registration + 5 commands. Reflects on the deployed
   `.gha` via late-bound `Assembly.LoadFrom`; does NOT reference
   `Frahan.StonePack.GH` at compile time.
7. **Ingest layer rule (added 2026-05-22)**: every external-data
   reader lives in `Frahan.StonePack.Core/Masonry/Quarry/Ingestion/`,
   returns Frahan POCO types, and has zero RhinoCommon imports. The
   canvas-side `*LoaderComponent` is a thin adapter; no parsing
   logic in `.gha`.

## 7. Public-API discipline

- No CGAL, Geogram, VHACD, CoACD, geometry3Sharp, libigl, or MeshLib
  type may appear in a Frahan public method signature, return type,
  property, or attribute.
- `Frahan.Core.IBackend`-style interfaces own the boundary. Native
  implementations marshal at the boundary.
- Frahan DTOs (`PackItem`, `PackPlacement`, `PackResult`, `Vec3`,
  `Size3`, `Box3`, `MeshPackItem`, `MeshPackResult`, `FaceCornerKey`,
  `FaceCornerUvTable`, `FrahanSurfaceChart`, `ChartDistortionReport`,
  `FractureTrace`, `FractureTraceCollection`, `GprRadargram`,
  `GprTrace`, `GprReflectorPick`, …) are the carrier types across
  all layers.
- RhinoCommon types (`Mesh`, `Curve`, `Brep`, `Surface`, `Point2d`,
  `Point3d`, `Vector3d`) are allowed inside `Frahan.Surface` (when
  the split lands) and `Frahan.StonePack.GH` only. The .rhp
  command-class implementations use them at the prompt boundary
  only.

## 8. Threading and cancellation

- Solvers run inside `Task.Run(...)` from `GH_TaskCapableComponent`.
- All hot-path data structures use plain `double[]`, `int[]`, struct
  arrays - no Rhino types - so `Parallel.For` is safe.
- `RhinoApp.WriteLine` is only called from the GH solve thread (the
  primary thread). Plug-in commands run on the main thread; safe.
- `RhinoDoc.Objects.Add*` is **never** called from a worker thread.
- The `BffCommandLineRunner` (the only out-of-process call) returns
  before the GH component reads outputs; it captures the child
  process's exit code, stdout, and stderr.
- New ingest readers (Shapefile / GeoJSON / SEG-Y / RD3 / DT1) read
  files synchronously. None spawn threads. Caller decides whether to
  wrap in `Task.Run`.

## 9. Build and packaging

- Solution: `Template-General/outputs/2026-05-01/frahan_stonepack/Frahan.StonePack.sln`.
- Per-project csproj files target net48 / netstandard2.0 / net6.0
  per assembly (see `01_frahan_software_principles.md`).
- Deploy footprint (2026-05-22): 13 artifacts at
  `%APPDATA%/Grasshopper/Libraries/Frahan.StonePack.MeshHeightmap/`:
  - `.gha` + `Frahan.StonePack.Core.dll` + `Frahan.EdgeMatching.Core.dll`
  - `MathNet.Numerics.dll`
  - `NetTopologySuite.dll`, `NetTopologySuite.Features.dll`,
    `NetTopologySuite.IO.Esri.Shapefile.dll`,
    `NetTopologySuite.IO.GeoJSON.dll`
  - `Newtonsoft.Json.dll`
  - `System.Buffers.dll`, `System.Memory.dll`,
    `System.Numerics.Vectors.dll`,
    `System.Runtime.CompilerServices.Unsafe.dll`
- Plus `Frahan.StonePack.Rhino.rhp` (~22 KB after the 4 new commands).
- AGENTS.md §3 deploy block reflects the pre-NTS 4-artifact set; the
  updated 13-artifact list is captured in this spec but the AGENTS.md
  update is HITL-gated for a follow-up commit.

## 10. Compatibility matrix

| Layer | net48 | netstandard2.0 | net6.0 | net7.0 |
| --- | :-: | :-: | :-: | :-: |
| Frahan.StonePack.Core (current) | **yes** | yes | n/a | n/a |
| Frahan.StonePack.Surface (proposed split) | yes | yes | n/a | n/a |
| Frahan.EdgeMatching.Core | **yes** | n/a | n/a | n/a |
| Frahan.StonePack.GH | **yes** (required) | n/a | n/a | n/a |
| Frahan.StonePack.Rhino | **yes** (required) | n/a | n/a | n/a |
| Frahan.StonePack.Tests | yes | n/a | yes | n/a |
| Frahan.NativeBridge | yes | yes | n/a | n/a |

The reference `Gh2DPacking` source under `references/original_gh_2d_packing_plugin/`
also targets net48 + net7.0-windows; reference-only, not part of the
Frahan solution.

## 11. RhinoCommon dual-plugin pattern

The repo carries a minimal pattern under
`outputs/2026-04-30/rhinocommon_dotnet_setup/RhinoCommonDualPlugin/`
(byte-identical copy under both Template-General and
Agent-orchestration-main). Demonstrates a single source tree producing
Rhino 7 (net48) and Rhino 8 (net7.0-windows) outputs from one csproj.
Frahan's current `Frahan.StonePack.Rhino.csproj` targets only net48;
the dual pattern is a future option if a Rhino 7 build path is wanted.

## 12. Architectural debts (for the refactor plan)

1. **Core/Surface split** — `Frahan.StonePack.Core.SurfacePacking`
   references `Rhino.Geometry` (RhinoCommon dependency in a "Core"
   assembly). Split into `Frahan.StonePack.Core` (pure) +
   `Frahan.StonePack.Surface` (Rhino-bound). 2-week scope per
   `porting_tradeoffs.md`. Logged.
2. **AlgorithmAttribute placement** — currently in
   `Frahan.StonePack.GH/Attributes/`. The `_FrahanWhichAlgorithm`
   command uses late-bound reflection so this is not a blocker, but
   for cleaner cross-project use, move to `Frahan.StonePack.Core/Attributes/`
   in a future refactor batch. ~21 file changes (1 move + 18
   component usings + 2 csprojs). Logged.
3. **IrregularSheetFill versions** — `Frahan.StonePack.GH.TwoD`
   carries four versioned solver classes (`IrregularSheetFillRhino`,
   `…V2`, `…V3`, `…V506`). Collapse to one class with a `Variant`
   strategy. V506 is the production line; V1/V2/V3 are legacy.
4. **Frahan.StonePack.Tests namespace** — has no explicit namespace.
5. **References / third_party** — the reference Gh2DPacking tree
   sits inside `references/`; move to `third_party/` or document
   attribution per file.
6. **Snapshot duplication** — the same `frahan_stonepack/` tree
   exists under `Template-General/` (live) and
   `Agent-orchestration-main/Agent-orchestration-main/project_context_template/`
   (memory-protected snapshot). Pick one source of truth. The
   memory-protected dir is the immutable reference; live edits go to
   `Template-General/`.
7. **Phase 2 plug-in commands** — `_FrahanAshlarPack` and
   `_FrahanPackTrencadis` ship as scaffolds. Full pick-and-bake
   wiring requires Slab + Curve interop helpers duplicated from
   `Frahan.StonePack.GH` into `Frahan.StonePack.Rhino/Interop/`.
   ~5-7 files. Logged.
8. **Tagging coverage** — 18 of ~112 GH components carry
   `[Algorithm]`. The remaining ~94 are listed in the subagent
   audit at `Template-General/outputs/2026-05-22/strategy/` (and
   slated for promotion to
   `wiki/research/algorithms_papers_audit_2026-05-22.md`).
9. **AGENTS.md §3 deploy block** — out of date. Now needs 13
   artifacts deployed, not 4. Single-file HITL edit deferred.

## 13. Test gate (current state)

- Headless: **769 PASS / 91 SKIP / 0 FAIL** at 2026-05-22 end of day.
- The 91 SKIPs are all `rhcommon_c.dll` HRESULT 0x8007045A — Rhino-
  runtime-only paths. Resolving them is gated on the Core/Surface
  split (debt #1).
- Visual / canvas validation is Libish's job per truth criterion
  §4(c). 15 new HitL cards across 5 phases shipped 2026-05-22 cover
  ingest, plug-in commands, ribbon migration, algorithm tagging.

## 14. Open architectural questions

1. **Core/Surface split** — when does it land? Logged as
   2-week work in `porting_tradeoffs.md`.
2. **Rhino Compute / Hops endpoint** — free with the existing Rhino
   licence. Wire one example + HitL card. Logged as 1-week work.
3. **Stage 2/3 ribbon migration** — Stage 1 (Lab subcat) landed
   2026-05-22 with zero existing-canvas regression. Stages 2/3
   consolidate Mesh subcategory; deferred pending Stage 1 HitL
   bake-in.
4. **Algorithm-paper tagging schema** — decided 2026-05-22: custom
   `[Algorithm(...)]` C# attribute, applied via reflection by
   `_FrahanWhichAlgorithm`. Schema is stable; remaining work is
   coverage extension.
5. **GPR dataset acquisition** — gprMax synthetic + Krietsch 2020
   Grimsel + USGS Mirror Lake are the realistic sources. Tamil
   Nadu charnockite GPR has zero open data (see
   `project_gpr_dataset_gap_tamilnadu` memory).

## 15. Related docs

- `01_frahan_software_principles.md` — Power-of-10 hardening rules,
  TargetFramework policy.
- `03_frahan_module_map.md` — module ownership matrix.
- `04_frahan_grasshopper_component_spec.md` — GH component naming +
  GUID conventions.
- `Template-General/outputs/2026-05-22/strategy/porting_tradeoffs.md`
  — Rhino + GH vs standalone trade-off analysis.
- `Template-General/outputs/2026-05-22/strategy/two_day_plan_2026-05-22.md`
  — execution plan for the current window.
- `Template-General/outputs/2026-05-22/strategy/hitl_card_authoring_workflow.md`
  — how to author HitL test cards (4 fixture levels).
- `wiki/algorithms/hitl_cards/pattern.md` — HitL card methodology.
- `Template-General/outputs/2026-05-22/hitl_cards/CROSS_REFS.md` —
  16-phase cross-reference map.
