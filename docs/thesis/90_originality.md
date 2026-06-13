# Originality Matrix

Sole author: Independent Research. Open data, open source. No university
affiliation.

This is the binding originality ledger for the thesis. Every shipped
component (or component family) is classified into exactly one originality
class, with file-and-line evidence drawn from its `[Algorithm]` attribute,
its Core engine, and the committed benchmark. The honesty convention of
`AGENTS.md` §9 governs: a thing is called original only with a prior-art
sweep behind it, an extension of prior work is named as such, and vendored
third-party code is named by its upstream and licence. A result is reported,
not validated, until visually confirmed on the canvas.

The seven classes are:

- **clean-room** — built from published mathematics, no upstream source in
  the tree.
- **evolved-fork** — extends a documented baseline (in-repo or published)
  with a stated, measured delta.
- **facade-over-primitives** — a monolith composing our own published Core
  primitives; adds orchestration, not a new algorithm.
- **direct-port** — a line-by-line port of an external open-source library.
- **vendored-library** — we ship or link a third-party library unmodified;
  only the marshalling is ours.
- **original-research** — Frahan-novel, A-candidate pending a prior-art
  sweep.
- **wrapper-of-native** — a Grasshopper wrapper over a native exe or DLL.

Clean-room language note. The OpenNest-lineage physics nester referenced in
the benchmarks is named "the reference physics nester"; no competitor source
is copied into this tree. Academic sources are cited by the `[Algorithm]`
attribute model.

---

## Chapter 01 — Two-Dimensional Nesting and Trencadís

| Component (family) | Class | Evidence |
|---|---|---|
| **IrregularSheetFillNfpBlf** / Freeform Sheet Nest (Exact NFP) (FreeNestX) | evolved-fork | `[Algorithm]` `IrregularSheetFillNfpBlfComponent.cs:24-32` (Burke 2006 DOI 10.1287/opre.1060.0293; Bennell-Oliveira 2009 DOI 10.1057/jors.2008.169; Clipper2 BSL-1.0). Feasible-region contract `IrregularSheetFillNfpBlf.cs:18-27`. Clean-room math base; the evolved-fork delta is over V506's overlap-then-trim. Measured 53.9% mean waste-cut vs V506 at zero overlap (`IrregularSheetFillNfpBlf.cs:21-22`; `outputs/2026-06-03/pack2d_nfp_evolution`). |
| **ContactNfpHoleNester** / Sheet Nest (Hole-Aware) (CNH) | evolved-fork | `[Algorithm]` `HoleNestComponent.cs:25-36` (clean-room NFP/BLF/IFP base + "Frahan ContactNfpHoleNester evolution study"). Core engine `ContactNfpHoleNester.cs:10-33` (contract), `:1281-1330` (contact rotations), `:1332-1352` (IFP = intersect over hull vertices), `:1777-1798` (penetration depth), `:1728-1748` (micro-retreat), `:635-738` (rect fast-path), `:243-292` (multi-start). Benchmark `outputs/2026-06-12/hole_packer_evolution`: 60.7 ms valid 12/12 vs Sparrow 3255 ms invalid (~54x and valid where the reference fails); fast-path 0.148 ms (146x native shelf). |
| **IrregularSheetFillV506** / Freeform Sheet Nest (FreeNest) | evolved-fork | `[Algorithm]` `Pack2DIrregularSheetV506Component.cs:23-27` (NFP-assisted BLF; Bennell-Oliveira tutorial). `[Obsolete]` `Exposure=hidden` `:56-57`. Overlap-then-trim documented by design in `examples/10_pack2d/README.md` (KB-6/KB-7). The FreeNestX evolution baseline; now phased out per the 2D-V-solver decision. |
| **IrregularSheetFillComponent** / Frahan Sheet Pack (Unified) (FreeNestU) | facade-over-primitives | `[Algorithm]` `IrregularSheetFillComponent.cs:32-33` ("Variant dispatcher V1/V2/V3/V506; Frahan-original strategy selector") over Burke 2007 + Bennell-Oliveira 2008. Adds no new algorithm; dispatches existing nesting variants behind one box. |
| **NfpPack2DComponent** / 2D NFP Pack | clean-room | `[Algorithm]` `NfpPack2DComponent.cs:11-12` (Burke 2007 DOI 10.1016/j.ejor.2006.03.011; Bennell-Oliveira 2008 DOI 10.1057/jors.2008.169). Citation-only; no upstream nesting source in the tree. |
| **NativeNfpKernel** (`nfp_kernel.dll`) | vendored-library | `NativeNfpKernel.cs:10-22`: "native/nfp_kernel/nfp_kernel.dll, vendored official Clipper2 C++" on the Int64 lane; only the marshalling is ours. Clipper2 BSL-1.0 (no copyleft). Consumed at `ContactNfpHoleNester.cs:924-930`. |
| **Pack2DTrencadisCatalogComponent** / Trencadis Catalog Pack | facade-over-primitives | `[Algorithm]` `Pack2DTrencadisCatalogComponent.cs:37-38` ("CVD-Lloyd interior seeding" Lloyd 1982; "Slab-partitioned Voronoi catalog; Frahan-original Trencadis extension"; precedent Battiato 2013 `:42`). Composes `CvdLloyd2d` + `HungarianAssignment` primitives. 28/28 placed in 53 ms (`examples/12_trencadis/README.md`). |
| **CvdLloyd2d** (CVD-Lloyd seed generator) | clean-room | `CvdLloyd2d.cs:14-22` (uniform-density CVD; matches `wiki/primitives/cvd_lloyd.md`); Lloyd 1982 relaxation, grid-discretised, stop at half-grid-step move (`:30-108`). Math-only, no upstream code. |
| **HungarianAssignment** (Kuhn-Munkres O(n³)) | clean-room | `HungarianAssignment.cs:11-15` ("classical shortest augmenting path formulation (Bourgeois-Lassalle 1971), standard textbook implementation"); potentials u/v with non-negative reduced costs (`:23-85`). Textbook math, no upstream code. |
| **Pack2DTrencadisComponent** / Trencadis Pack (greedy NFP-slide) | facade-over-primitives | `[Algorithm]` `Pack2DTrencadisComponent.cs:37-38` ("Trencadis greedy pack basic" Gaudi Park Guell; "NFP boundary slide" Minkowski-difference sampler). Battiato 2013 sect 4 cut budget `TrencadisFill.cs:13-27`. Standalone box is a skeleton returning empty (`examples/12_trencadis/README.md`) — see Roadmap (ghost component). |
| **Pack2DTrencadisDynamicComponent** / Trencadis Dynamic Settle | facade-over-primitives | `[Algorithm]` `Pack2DTrencadisDynamicComponent.cs:61-62` ("Trencadis dynamic settle" Frahan-original; "Kangaroo 2 goal-based physics" Daniel Piker). 55.1% physics vs 52.7% greedy (`examples/12_trencadis/README.md`). |
| **TrencadisEdgeMatchComponent** / Trencadis EdgeMatch | facade-over-primitives | `[Algorithm]` `TrencadisEdgeMatchComponent.cs:28-29` ("EdgeMatch-powered Trencadis pack"; "Frahan-original alternative to Battiato 2013 CVD+GVF stack"; "Beam-search assembly solver" Frahan-original). Composes the EdgeMatching primitives. |
| **Pack2DTrencadisPipelineComponent** / Trencadis Pipeline | facade-over-primitives | `[Algorithm]` `Pack2DTrencadisPipelineComponent.cs:59-62` (greedy pack + NFP slide + CVD-Lloyd seeding + Kangaroo 2 settle, all cited to in-repo primitives + Daniel Piker physics). |

---

## Chapter 03 — Quarry Block-Cutting Optimization

| Component (family) | Class | Evidence |
|---|---|---|
| **BlockCutOptSolver** (pose-sweep max-cover) | clean-room | Core `BlockCutOptSolver.cs:108-135` (pose grid + parallel argmax, bit-identical to serial reference), `:261-268` (kerf film). GH `BlockCutOptSolveComponent` guid `F2D0BC02` `[Algorithm("BlockCutOpt brute-force search","Elkarmoty Bondua Bruno 2020, Resources Policy 68:101761",Doi=10.1016/j.resourpol.2020.101761)]` `BlockCutOptComponents.cs:97`. `README.md:46-50`: upstream is private C++, no source in tree. |
| **CuttingGrid** full-3D rotation (I1 pose tilt) | evolved-fork | `CuttingGrid.cs:84-110` (pre-multiplied U,V,W), `:77-78/127-129` (kerf pitch). `[Algorithm("Full 3D rotation grid","Frahan I1 improvement over Elkarmoty 2020 psi-only")]` `BlockCutOptComponents.cs:98`. `README.md:185`. psi-only back-compat constructor `OrientedBlock.cs:40-50` collapses to BlockCutOpt 2020. |
| **ObbTriangleIntersection + TriangleAabbBvh** (I2/I4 predicate) | clean-room | `ObbTriangleIntersection.cs:10-16` (13-axis SAT), used `BlockCutOptSolver.cs:243-256`. `[Algorithm("Triangle-AABB BVH pruning","Akenine-Moller 2001 fast 3D triangle-box overlap")]` `BlockCutOptComponents.cs:99`. `README.md:186-188`. |
| **BlockCutOptParetoSolver + BlockCutOptOmniSolver** (I6/I11 four-axis) | evolved-fork | `BlockCutOptParetoSolver.cs:82-95`, `ParetoPoint.Dominates` `ParetoPoint.cs:54-71`, `BlockCutOptOmniSolver.cs:115-178`. GH `BlockCutOptOmniSolveComponent` guid `F2D0BC04` `[Algorithm]` pair `BlockCutOptComponents.cs:306-307` (Elkarmoty 2020 + Jalalian 2023 BCSdbBV). `README.md:190,195`. |
| **BlockValueModel** BCSdbBV cost objective (I11) | clean-room | `BlockValueModel.cs:54-58` (SurfaceArea S=2(LxLy+LyLz+LxLz)), `:22-27` (BV). `ParetoPoint.cs:12`. `README.md:195` cites Jalalian et al. 2023 DOI 10.1038/s41598-023-49633-w. Faithful axis from published math. |
| **RecoveryCascade** (multi-scale reject-recover) | evolved-fork | `RecoveryCascade.cs:26-29` (W(R,s) recursion), `:91-119` (kept/cracked partition by !bvh.AnyTriangleIntersects), `:21-24` (reduces to baseline at one scale), `:31-36` (BoEGE / Murugean 2026 cite softening, flag E9). No GH consumer — see Roadmap (high). |
| **AmrrPlanner** (I9 plane sequence) + **SharedEdgeSlicer** (I12) | clean-room | `AmrrPlanner.cs:7-31`, `:132-178` (cut loop), `:85` (AMRR = removed volume / cutting time). GH `BlockCutOptAmrrPlanComponent` guid `F2D0BC03` `[Algorithm("AMRR in-block plane-sequence cutting","Shao, Liu, Gao 2022")]` `BlockCutOptComponents.cs:215`. `README.md:193,196` (Shao 2022 DOI 10.3390/pr10040695; Minetto 2017 DOI 10.1016/j.cad.2017.07.001). |
| **FractureBlockPack** (uncertainty-safe yield, example 09) | facade-over-primitives | `FractureBlockPackComponent.cs:27` (class), `:37` (guid `A7E0B0F3`), `:9-25` header: self-contained recovery engine that does NOT call RecoveryCascade / BlockCutOptSolver / Dlbf3dMixedSizePacker (silent-disagreement risk — Roadmap high). Fully managed, no native shim. |
| **HeteroExt** (heterogeneous quarry extraction) | facade-over-primitives | `[Algorithm("Heterogeneous quarry extraction pipeline","Frahan-original",Note="Composes Elkarmoty 2020 and Chehrazad 2025, both interpreted and reimplemented...")]` `BlockCutOptHeterogeneousComponents.cs:169`; DLBF `[Algorithm]` `:42` (Chehrazad Roose Wauters 2025 DOI 10.1080/00207543.2025.2478434). |
| **FrahanSawBedScheduleComponent** (Saw Bed Schedule) | clean-room | `[Algorithm("Greedy LPT list scheduling","Graham 1969, SIAM J. Appl. Math. 17(2):416-429",Doi=10.1137/0117039)]` `QuarryCutOptComponents.cs:336`. Textbook LPT, no upstream code. |
| **Extraction Order Optimizer** | original-research | `[Algorithm]` note `QuarryCutOptComponents.cs:223` "no published scheduling algorithm matched". A-candidate, prior-art sweep pending (Roadmap low). |

---

## Chapter 05 — Masonry Equilibrium and Cyclopean Reassembly (CRA)

| Component (family) | Class | Evidence |
|---|---|---|
| **Masonry Stability (RBE)** | clean-room | `[Algorithm]` `MasonryStabilityRbeComponent.cs:69-71` (Kao et al. 2022 CAD 146:103216; compas_cra MIT cited, not copied). Equilibrium math `EquilibriumMatrixBuilder.cs:13-30,201-219`; linearised Coulomb cone `FrictionConeBuilder.cs:24-32`. Wires the sign-corrected `BuildPhysicsCorrected` at `:305`, not the legacy `Build`. Convex-QP force+moment balance, compression-only normals. |
| **Masonry Stability Check (CRA)** | original-research | `CraStabilityChecker.cs:17-50` (Kao H-model Eqs 8-11 cited); alternating-convex certificate `:154-186` is NOT in compas_cra (which uses non-convex IPOPT). A-candidate, soundness-certifying direction proven; H-model regression `CraStabilityCheckerTests.cs:83-105`; compas_cra parity `Program.cs:347-356`. Rejects self-stressed states RBE wrongly accepts. |
| **AdmmQpSolver** | clean-room | `AdmmQpSolver.cs:6-51` (Stellato et al. 2020 OSQP, Math.Prog.Comp 12:637-672); ADMM iteration `:182-256`; masonry Ruiz equilibration `:108-145`. CSR-sparse, per-row rho. OSQP-style infrastructure with engineering deltas; no upstream OSQP source in tree. |
| **Polygonal Wall (Generator)** | original-research | `PolygonalWallGenerator.cs:7-34` (power diagram `:13-17`), interlock metric J `InterlockScore:310-384`. A-candidate (Kim 2024 does sequencing, not generation; sweep pending — Legakis 2001 closest prior). Hover credits Kim 2024 / Clifford-McGee 2018 / Lloyd 1982 at `PolygonalWallGeneratorComponent.cs:31-33`. |
| **PolygonalWallAssembler** (exact-joint) | clean-room | `PolygonalWallAssembler.cs:8-30` exact planar-quad interface per adjacent pair from shared (u,v) edges; avoids mesh-contact-detector splintering of the equilibrium QP. Feeds the equilibrium builder; `Cra_GeneratedWall_Certified` `Program.cs:335`. |
| **Stone Cell Match (Lambda engine)** | original-research | `StoneCellAssignment.cs:8-37` (Lambda / lambda / gap formulas); composes the reused `HungarianAssigner:141-145` and voxel kernel. A-candidate Lambda formalisation (Clifford-McGee measured 0.27, never formalised); ETH1100 datum `StoneCellAssignmentEthBenchmarkTests.cs:14,95`; reported Lambda = 0.194. |
| **StoneCarveBack** (Cyclopean) | facade-over-primitives | `StoneCarveBack.cs:9-29` cites Clifford & McGee 2018 anti-nesting; exact booleans through the in-repo `CgalMeshBoolean:23-28` primitive; volume-validated in the battery. Composes booleans, adds no new algorithm. |
| **Rubble Wall Settle** | clean-room | `[Algorithm]` `RubbleWallSettleComponent.cs:35-36` (Heyman 1966 limit-state); Core `RubbleWallSettle.cs:9-32` from the signed-off Furrer 2017 / Johns 2020 prototype. Deterministic, non-penetrating, PCA flat-bedding + per-cell drop, Heyman COM-over-support. |
| **Ashlar Pack** | clean-room | `[Algorithm]` `AshlarPackComponent.cs:31-32` (Gramazio Kohler Eichenhofer 2017 NCCR running-bond). Tier-C grid stacking, AABB-first, translation-only. |
| **Best-Fit Inventory Pack** | clean-room | Core `BestFitInventoryPacker.cs:8-32` carries the correct Furrer 2017 / Johns 2020 lineage. **CITATION FLAG E5:** the GH facade `BestFitPackComponent.cs:30` attributes a likely-fabricated "Gramazio Kohler Eichenhofer 2017 CAD paper" — should be corrected to Furrer/Johns (see register). |

---

## Chapter 07 — Surface Packing and Conformal Unwrapping

| Component (family) | Class | Evidence |
|---|---|---|
| **PackOnSurfaceComponent** (BFF flatten + pack + lift) | facade-over-primitives | Orchestrates BFF flatten, 2D pack, and barycentric lift. `[Algorithm]` `PackOnSurfaceComponent.cs:41` mis-credits "Floater 2003 mean value coordinates" (DOI 10.1016/S0167-8396(03)00002-5) — the shipped lift is plain barycentric, not MVC. Attribution defect, math correct (Roadmap medium, flag M-Floater). |
| **BFF runtime** (Boundary First Flattening) | vendored-library | Conformal flatten bundled in dist; Sawhney & Crane 2017 DOI 10.1145/3072959.3056432. Permissive-as-published; owes a THIRD_PARTY_NOTICES row (licensing register). |
| **ChartScaleComputer** (chart-scale recovery) | original-research | `ChartScaleComputer.cs:14-58` recovers one isotropic scalar s as the perimeter-weighted average of e^u. Frahan-original scale recovery over the conformal flatten; known global-scale limitation (Roadmap medium). |
| **BarycentricMapper2DTo3D** (inverse lift) | clean-room | `BarycentricMapper2DTo3D.cs:141-179` plain triangle barycentric interpolation (Cramer's rule on the 2D Gram system). SLM card `bff-surface-flatten.md:6` confirms "NO Floater-2003 MVC code present". O(P·F) linear scan, author ceiling ~2000 faces `:107` (Roadmap medium). |
| **ChartDistortionAnalyzer** (edge-stretch metric) | clean-room | `ChartDistortionAnalyzer.cs:96-98` scalar edge-length stretch. Cannot detect signed-area foldovers (Roadmap medium, flag M3). |
| **MeshObjIO** (OBJ chart I/O) | clean-room | Writes OBJ at raw world coords G10; no recenter, so far-from-origin (UTM) charts lose mantissa bits (Roadmap low, flag T1). |

The Surface Packing back end (BFF, geogram, CGAL shims) is reached
out-of-process and is absent from the default install; see the licensing
register for the copyleft routing.

---

## Chapter 08 — Edge-Matching and Fragment Reassembly

| Component (family) | Class | Evidence |
|---|---|---|
| **Boundary segmenter** (signed-turning descriptor) | clean-room | `[Algorithm]` `EdgeMatchSolveComponent.cs:23` ("Frahan-original arc-length curvature/torsion signature"); core `BoundarySegmenter.cs:151-189`. Rotation-invariant signed-turning signature per segment from the standard turning-function representation; no upstream code. |
| **Segment hash index** | clean-room (Frahan-original) | `[Algorithm]` `EdgeMatchSolveComponent.cs:24`; `SegmentHashKey.cs`. Quantised invariant-hash bucketing of segment signatures for candidate pruning. |
| **Phase correlator** | clean-room | `[Algorithm]` `EdgeMatchSolveComponent.cs:25`; `PhaseCorrelator.cs:13-44`. Direct O(n²) circular L1 correlation lag estimate. **Attribute wording flag:** named "Phase correlator FFT" but `:29-34` is direct correlation, not a frequency-domain transform (see register / Roadmap). |
| **Constrained ICP (2D/3D) + SVD Kabsch** | clean-room (algo) over vendored MathNet | `EdgeMatchSolveComponent.cs:26`; `ConstrainedIcp3D.cs:147-189`. Besl-McKay 1992 ICP loop with a Kabsch 1976 SVD rigid fit; the SVD itself is the vendored `MathNet.Numerics` kernel, the loop and constraints are ours. |
| **Order-preserving correspondence DP** | clean-room | `OrderedBoundaryMatcher.cs:24-37,100-167`. Dynamic-programming monotone boundary correspondence; textbook DP, no upstream code. |
| **Soft-ICP refiner** (CPD + hinge) | clean-room / evolved-fork | `SoftIcp3DComponent.cs:43-51`; `SoftIcpRefiner.cs:299-527`. Soft correspondence is a CPD-style (Myronenko-Song 2010) E-step with a weighted-Kabsch M-step (Kabsch 1976/1978, vendored MathNet SVD `:49-51`). Evolved-fork delta: the unified contact-plus-non-penetration hinge. |
| **Projection bootstrap** | original-research | `ProjectionPairFinder.cs:16-48,481-678`. Per-facet projection bootstrap, antiparallel SE(3) lift composition, 3D-disposes verification gate. A-candidate; prior-art sweep pending (`AGENTS.md` §9). The geometric 3D path assembles only via this bootstrap (independent tessellation yields zero cross-panel hash hits). |
| **Horn registration kernel** | clean-room | `RigidTransformRecovery.cs:6-30`; consumed `GeoreferenceComponent.cs:36`. Closed-form Horn 1987 unit-quaternion absolute orientation over a vendored MathNet eigensolve. One of three duplicate absolute-orientation routes (Roadmap low, refactor). |
| **Block Pair Match 3D** (VSA face partitioner) | facade-over-primitives (stub partitioner) | `[Algorithm(... Frahan stub implementation)]` `BlockPairMatch3DComponent.cs:43-45`. The Variational Shape Approximation (Cohen-Steiner 2004) face partitioner is a declared stub; `[RelatedComponent]` `:41` redirects users to the practically-tested Hungarian Stone-Cell Match. |
| **Frahan.Kintsugi.Port** (learned reassembly) | direct-port (research-only) | C# port of PuzzleFusion++ (Wang, Chen, Furukawa 2025, ICLR). Production 3D path; isolated **non-commercial research-only** assembly with converted weights `kintsugi.bin` (~255 MB). See licensing register flags E1/jigsaw. Norm-undo + verifier-gated pose composition `T_world(f)=T_unnorm(0)·T_net·T_norm(f)`. |

---

## Chapter 14 — Workflow Architecture and Data-Flow Connections

Chapter 14 is cross-cutting and introduces no new solver. It documents how
the per-subsystem algorithms above connect along the ingest to process to
segment to pack-or-cut to stabilise to fabricate spine. Its components are
the ingest readers, mesh-hygiene nodes, and report emitters, each of which is
either a vendored reader (Ingest tab, classed vendored-library by its format
library) or a facade-over-primitives orchestrator. No component in this
chapter is original-research; its contribution is the connection topology,
documented in-place, not a new algorithm.

---

## Summary: counts per class

Counted over the audited shipped components and component families above
(excluding the cross-cutting Chapter 14 connectors, which add no algorithm).

| Class | Count | Components |
|---|---|---|
| clean-room | 23 | NfpPack2D, CvdLloyd2d, HungarianAssignment, BlockCutOptSolver, ObbTriangleIntersection+BVH, BlockValueModel, AmrrPlanner+SharedEdgeSlicer, FrahanSawBedSchedule, BarycentricMapper2DTo3D, ChartDistortionAnalyzer, MeshObjIO; Masonry RBE, AdmmQpSolver, PolygonalWallAssembler, Rubble Wall Settle, Ashlar Pack, Best-Fit Inventory Pack; Boundary segmenter, Segment hash index, Phase correlator, Constrained ICP+Kabsch, Order-preserving DP, Horn kernel |
| evolved-fork | 7 | IrregularSheetFillNfpBlf (FreeNestX), ContactNfpHoleNester (CNH), IrregularSheetFillV506, CuttingGrid (I1), BlockCutOptPareto/Omni (I6/I11), RecoveryCascade; Soft-ICP refiner |
| facade-over-primitives | 10 | Sheet Pack (Unified), Trencadis Catalog, Trencadis Pack, Trencadis Dynamic, Trencadis EdgeMatch, Trencadis Pipeline, FractureBlockPack, HeteroExt, PackOnSurface; StoneCarveBack, Block Pair Match 3D |
| vendored-library | 3 | NativeNfpKernel (Clipper2), BFF runtime, (Ingest format readers, Ch. 14) |
| original-research | 7 | ChartScaleComputer, Extraction Order Optimizer, RecoveryCascade header E9; CRA Stability Check, Polygonal Wall Generator (J metric), Stone Cell Match (Lambda); Projection bootstrap |
| direct-port | 1 | Frahan.Kintsugi.Port (PuzzleFusion++ port; isolated non-commercial research-only assembly — see register) |
| wrapper-of-native | 0 | native reached via lazy P/Invoke inside facades, not as standalone wrappers |

Posture, in one line. Across the five algorithmic chapters the repository is
dominated by clean-room math (23 citation-only implementations of published
algorithms) and facades over our own primitives (10); the genuinely forked
work is small and bounded (7), each carrying a measured delta over a named
baseline; seven components claim originality, and each is an A-candidate
pending a prior-art sweep, not an asserted novelty. The single direct port,
the learned Kintsugi reassembly engine, is quarantined in a separate
non-commercial research-only assembly and is absent from the default install,
so nothing in the default-install algorithm path is a line-by-line port of a
competitor.

---

## Licensing register and mitigations

Every flag raised in the audit, with its current mitigation. The governing
rule: the default install must link no copyleft or non-commercial code; such
obligations are quarantined behind optional native shims, an isolated
research-only assembly, or a data download step.

| # | Flag | Risk | Mitigation / status |
|---|---|---|---|
| 1 | **Root LICENSE GPL-3.0 (placeholder)** | The distribution links `Frahan.Kintsugi.Port`; under GPL the combined work is GPL-3.0. Root LICENSE is a header, not full GPL text. | Replace root LICENSE with canonical `gpl-3.0.txt` before public release. If Kintsugi.Port is isolated behind a separate build, the rest may be relicensed. |
| 2 | **Kintsugi / PuzzleFusion++ NON-COMMERCIAL (CRITICAL, E1)** | Upstream LICENSE is research-use-only / non-commercial, NOT plain GPL-3.0. Covers ported C# code AND converted weights `kintsugi.bin` (~255 MB). | Keep the separately-distributed, optional, non-commercial research-package split. Verify root LICENSE, port README, and any repo-root statement all say research-only non-commercial, not plain GPL-3.0. |
| 3 | **jigsaw_matching subtree unaudited** | Vendored inside PuzzleFusion++ (Jigsaw, Lu et al.); ships its own MIT LICENSE but NOTICE.md states the MIT grant is unaudited against the original repo. | Treated conservatively under the parent non-commercial terms; nothing from it is compiled into or shipped with StonePack. |
| 4 | **CGAL GPL (E3)** | CGAL PMP / Surface_mesh / simplification / reconstruction are GPL; permanently block any MIT relicense of the geometry path. Depends on GMP (`gmp-10.dll`). | Shim source vendored in `native/cgal_shim/` (corresponding-source gap resolved). Native shims optional at runtime with BSP/MeshCsg fallback; absent from default install. A commercial release would buy the CGAL packages. |
| 5 | **CoACD transitive CGAL-GPL** | CoACD itself MIT (pin 1.0.11) but internally vendors CGAL (GPL) plus boost/openvdb/spdlog/zlib; GPL re-enters via CoACD. | Same out-of-process quarantine as CGAL; optional at runtime. |
| 6 | **TetGen AGPL (geogram_shim)** | TetGen (needed for volumetric-Voronoi-blocks) is AGPL and ON by default in the geogram CMakeLists. | `-DFRAHAN_WITH_TETGEN=OFF` documented for an AGPL-free build; volumetric blocks unavailable in that config. Triangle kept OFF. |
| 7 | **Clipper2 BSL-1.0 (low)** | `nfp_kernel` vendors official Clipper2 at tag `Clipper2_2.0.1` unmodified. | Boost Software License 1.0 is permissive, no copyleft. Compatible. Attribution preserved. |
| 8 | **Geogram BSD-3 + bundled PoissonRecon MIT (low)** | Permissive; attribution required. | Kazhdan PoissonRecon bundled as `GEO::PoissonReconstruction` is MIT. Attribution required, no copyleft. |
| 9 | **xBIM CDDL-1.0 vs GPL-3.0 (E2)** | CDDL-1.0 and GPL-3.0 are distribution-incompatible. | HITL ruling: GeometryGymIFC is the long-term licence-clean path; xBIM stays for now. Mitigation options: out-of-process writer or swap to GeometryGymIFC. |
| 10 | **BFF + numeric stack (notices owed)** | BFF runtime (Sawhney & Crane 2017) bundled in dist requires a THIRD_PARTY_NOTICES.md with BFF upstream LICENSE plus SuiteSparse / OpenBLAS / GFortran notices. No THIRD_PARTY_NOTICES.md found at audit time. | Add `THIRD_PARTY_NOTICES.md` at repo root before ship (licensing policy spec 16 requires it per dependency). |
| 11 | **Datasets carry their own licences** | ETH1100, Tongjiang, Grimsel GPR, Bondua Botticino GPR, TU1208 GPR are CC-BY; GeoCrack/Open3D Marbles MIT. NON-COMMERCIAL/UNKNOWN: Granite Dells TLS ("Not Provided"), Stanford 3D Scanning (research-only). Example 08 marble GPR is CC-BY-NC-ND (no commercial demo). | Bundled with attribution by maintainer decision (`data/ATTRIBUTION.md`); downstream users must honour each upstream licence. At the public step, large blobs move to Git LFS and a download script fetches non-redistributable sets. |
| 12 | **Reference-register + per-file attribution gap (spec 16)** | Policy requires `docs/index/frahan_reference_register.md` (not found), a THIRD_PARTY_NOTICES row per dependency, and per-file SPDX headers on any copied source (incl. `references/original_gh_2d_packing_plugin`). | Create the register and per-file attribution before external review per `AGENTS.md` §9. |
| 13 | **Fabricated/stale citations (provenance, not copyleft)** | `BestFitInventoryPacker.cs:26-29` and the Masonry facade `BestFitPackComponent.cs:30` cite a non-existent "Gramazio/Kohler/Eichenhofer 2017 CAD paper" (E5; the Core lineage is correctly Furrer 2017 / Johns 2020); RecoveryCascade self-labelled "novel" without the BoEGE cite (E9); the EdgeMatch `[Algorithm]` at `EdgeMatchSolveComponent.cs:25` names "Phase correlator FFT" while `PhaseCorrelator.cs:29-34` is direct O(n²) correlation, not an FFT. | Fix all three before any external/academic review under `AGENTS.md` §9. RecoveryCascade header softened to Murugean 2026; E5 and the FFT wording still open. |

The architectural quarantine is the load-bearing mitigation. The default
install ships no native DLL and links no GPL, AGPL, or non-commercial code.
CGAL, CoACD, TetGen, and geogram are reached only through optional
out-of-process shims with managed fallbacks. The Kintsugi non-commercial
research package is a separately distributed, isolated assembly. Permissive
dependencies (Clipper2 BSL-1.0, Geogram BSD-3, Kazhdan PoissonRecon MIT)
remain in the default path and require only attribution.
