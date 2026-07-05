# Canonical Components Handoff — the most task-capable / most evolved per domain

Written 2026-07-05 (evening), during the 2D-packer consolidation. Purpose: never
lose track of WHICH solver/component is the evolved one, what supersedes what,
and why. Companion to `HANDOFF_2026-07-05.md` (Zenodo release checklist).

Repo rule: `D:\frahan-stonepack` (github libishm1/Frahan) is canonical.

---

## 1. THE 2D-PACKER CONSOLIDATION (in flight right now)

User decision: ONE nester on the ribbon, not three. The three surfaced today
overlap ~80%:

| Component | Core solver | Unique feature | Verdict |
|---|---|---|---|
| **Sheet Nest (Hole-Aware)** `HoleNest` | `ContactNfpHoleNester` (Core) | part-holes + sheet-holes, contact rotations, multi-start | most-evolved CORE; sync only, no preview |
| **Freeform Sheet Nest (Exact NFP)** `FreeNestX` | `IrregularSheetFillNfpBlf` | `GH_TaskCapable` (NOT truly async — only parallelizes data branches; a single big nest still blocks) | superseded by CNH |
| **Sheet Pack (Unified)** `FreeNestU` | V506 legacy dispatcher | **Boundary Mode** (edge-affinity boundary placement) | legacy; its one good idea is being evolved into CNH |

### The consolidation (two agents in flight when this was written)
1. **`Sheet Nest (Live)`** (`NestLive`, new file
   `src/Frahan.StonePack.GH/TwoD/SheetNestLiveComponent.cs`): truly-async +
   live-preview wrapper over the SAME `ContactNfpHoleNester` core.
   - Async base: `AsyncScanComponent<TSnapshot,TPayload>`
     (`src/Frahan.StonePack.GH/ScanIngest/AsyncScanComponent.cs`) — Run gate,
     background Task, `ScheduleSolution` on completion, cancel on Run=false.
     **This is the real async pattern.** `GH_TaskCapableComponent` is NOT
     (branch-parallel only) — that misconception is why FreeNestX looked async.
   - Live preview pattern copied from `FloorTileComponent`
     (`DrawViewportMeshes` + `ClippingBox` + `_previewMeshes`/`_previewMat`).
   - Shared GH glue extracted to `HoleNestShared.cs` (curve<->loop, snapshot,
     result->geometry) so HoleNest + NestLive share one implementation — the
     80% overlap is reused, not duplicated.
2. **Evolved boundary mode in Core** (`ContactNfpHoleNester`): new
   `boundaryMode`/`minBoundaryContact` params (default off = byte-identical).
   Evolution over V506's Boundary Mode:
   - V506: pre-classified part edges via `BoundaryRailMatcher` descriptor
     buckets — **orientation-locked** (world-XY angle buckets; the R6 gap in
     `wiki/research/edge_matching_theory_vs_implementation.md`) — then spread
     by a golden-ratio angular stride (approximate; clusters on concave sheets).
   - Evolved: **measure TRUE contact length between the placed part outline and
     the sheet boundary at verified NFP candidate poses** (rotation-invariant by
     construction, exact), spread by **arc-interval occupancy** (exact, no
     stride heuristic), self-limiting fallback to bottom-left when contact <
     `minBoundaryContact * perimeter`. Native-NFP lane skipped in boundary mode
     (managed lane only).
3. After both land: wire a Boundary input into `Sheet Nest (Live)`, validate
   live on canvas (bake + capture), THEN decide hiding FreeNestX + Unified
   (keep loadable for old canvases; off the ribbon). Update the yak + docs.

### Placement machinery facts (hard-won, do not relearn)
- CNH candidate loop: per rotation -> `InnerFit` (IFP) minus `CachedNfp`
  obstacles -> `OrderedVertices` walked in (y,x) bottom-left order -> first
  `TryVerifiedCandidate` survivor wins (exact compound verify + one
  micro-retreat). Lines ~900-1005.
- `PackSheets` = greedy overflow across sheets; multi-start K orders keeps the
  best VALID layout by (placed, density, diag).
- The V506 lineage (V1/V2/V3/V506 + Unified + Async) is retained HIDDEN
  permanently so old canvases load. Never delete: GUIDs are load-bearing.

---

## 2. MOST-EVOLVED COMPONENT MAP (per domain)

The "reach for this one" table. Everything else in the family is a primitive,
a legacy variant, or a stub.

| Domain | Most evolved (Core) | GH component | Legacy/hidden siblings |
|---|---|---|---|
| 2D nesting | `ContactNfpHoleNester` | `Sheet Nest (Hole-Aware)`; `Sheet Nest (Live)` once landed | BL Pack, Sheet Pack, Freeform V2/V3/V506, Unified, (FreeNestX pending) |
| 2D mosaic | `TrencadisFill` (Battiato cut budget + CVD-Lloyd + GVF) | `Trencadís Pack` (+Catalog/Dynamic) | — |
| 3D packing | CoACD collision + drop-settle (beats heightmap 2x); DLBF mixed-size | `Block Pack Tree` family | heightmap packer |
| Wire-saw block yield | staged guillotine (packer 5) | `Fracture Block Pack` (Packer=5) | baseline grid (0), volumetric DLBF (4) kept as comparisons |
| Reconstruction | out-of-proc worker: **Auto = Poisson-when-normals -> density-adaptive AlphaShape -> AdvancingFront** (038b738/27dcab7) | `Scan Reconstruct` | — |
| Masonry stability | `MasonryStabilityChecker` penalty-RBE (K=8 inscribed, LS-first KKT warm start, native OSQP + sparse ADMM) + `CraStabilityChecker` certificate | `Masonry Stability Check` | IPOPT stub |
| Edge matching 2D/3D | `SoftIcpRefiner` (CPD + tau anneal + Lie retraction), `AssemblySolver` (beam/Prim-MST + cycle-consistency + best-buddies), `FrechetDistance` (R1, new) | `Soft ICP 3D`, `Block Pair Match 3D`, `Edge Gap (Fréchet)` | 4 EdgeMatch3D SKELETONs hidden (AdaptiveBlockMatch3D, BlockChain, Cyclopean, TemplateBlockMatch3D) |
| Boundary-affinity nesting | evolved CNH `boundaryMode` (this session) | via `Sheet Nest (Live)` once wired | V506 Boundary Mode + `BoundaryRailMatcher` route (orientation-locked) |
| Block cutting | `BlockCutOpt` Omni v2 (10 improvements) | `Block Cut Multi` | I11 BCSdbBV = 4th Pareto axis |
| Discontinuity | C++ CSR worker (k=24 normals, 7.86M pts ~10s) + FACETS/DSE -> Watson | `Discontinuity Sets` (D5F10047/48) | — |
| Assignment | `HungarianAssigner` (Kuhn/JV O(n^3)) | `Voussoir Stone Matcher` etc. | MatcherRegistry advertises 7 solvers, ships 1 — do not trust the docstring |
| Fabrication handoff | `StoneCutExport` (.3dm + CAM metadata), `PlanesToRobotTargets` (visose/Robots), `WireSawToolpathAdapter`, KUKA|prc | Fabricate tab | NOT yet wired to edge-match output (study R4) |

Known gaps parked (edge-match study R2-R6): geometric hashing for partial rims,
point-to-plane ICP, MILP stock assignment, edge-match->fab wiring, Stack B
rotation invariance (R6 — now addressed for the NESTING use by the evolved
boundary mode; the rail matcher itself is still orientation-locked).

---

## 3. SESSION STATE AT WRITING

- Zenodo: `.yak` REBUILT + verified clean (0.1.0 gha/workers, mpfr-6.dll in,
  stray as_out.bin caught+removed). Version identity 0.1.0-alpha everywhere.
  Security-scanned: history clean. Publish steps (yak push + repo public +
  GitHub release tag -> DOI) pending this evening — see HANDOFF_2026-07-05.md §1.
- Deployed .gha verified live: 277 components, Edge Gap present,
  Unified=secondary, AdaptiveBlockMatch3D=hidden; examples 8/8 load 0-unresolved.
- In flight: (a) SheetNestLiveComponent + HoleNestShared agent,
  (b) CNH boundaryMode agent. Integration + validation + commit after both land.
- Next after consolidation: UX audit P1 items (Mesh->Ingest split, backend-
  variant demotion), 17 dangling [RelatedComponent] links, icon pass.
