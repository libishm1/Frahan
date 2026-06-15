# Originality Matrix

Sole author: Independent Research. Open data, open source. No university
affiliation.

This is the binding originality ledger for the thesis. Every shipped
component (or component family) across all fifteen chapters is classified
into exactly one originality class, with file-and-line evidence drawn from
its `[Algorithm]` attribute, its Core engine, and the committed benchmark.
The honesty convention of `AGENTS.md` §9 governs: a thing is called original
only with a prior-art sweep behind it, an extension of prior work is named as
such, and vendored third-party code is named by its upstream and licence. A
result is reported, not validated, until visually confirmed on the canvas.

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
the benchmarks is named "the reference physics nester" (or "evolved fork");
no competitor source is copied into this tree. Academic sources are cited by
the `[Algorithm]` attribute model.

A component family that spans more than one chapter is detailed once at its
primary chapter and cross-referenced elsewhere; the whole-repo summary counts
each family once.

---

## Chapter 01 — Two-Dimensional Nesting and Trencadís

| Component (family) | Class | Evidence |
|---|---|---|
| **IrregularSheetFillNfpBlf** / Freeform Sheet Nest (Exact NFP) (FreeNestX) | evolved-fork | `[Algorithm]` `IrregularSheetFillNfpBlfComponent.cs:24-32` (Burke 2006 DOI 10.1287/opre.1060.0293; Bennell-Oliveira 2009 DOI 10.1057/jors.2008.169; Clipper2 BSL-1.0). Feasible-region contract `IrregularSheetFillNfpBlf.cs:18-27`. Clean-room math base; the evolved-fork delta is over V506's overlap-then-trim. Measured 53.9% mean waste-cut vs V506 at zero overlap (`IrregularSheetFillNfpBlf.cs:21-22`; `outputs/2026-06-03/pack2d_nfp_evolution`). |
| **ContactNfpHoleNester** / Sheet Nest (Hole-Aware) (CNH) | evolved-fork | `[Algorithm]` `HoleNestComponent.cs:25-36` (clean-room NFP/BLF/IFP base + "Frahan ContactNfpHoleNester evolution study"). Core engine `ContactNfpHoleNester.cs:10-33` (contract), `:1281-1330` (contact rotations), `:1332-1352` (IFP = intersect over hull vertices), `:1777-1798` (penetration depth), `:1728-1748` (micro-retreat), `:635-738` (rect fast-path), `:243-292` (multi-start). Benchmark `outputs/2026-06-12/hole_packer_evolution`: 60.7 ms valid 12/12 vs reference physics nester (Sparrow) 3255 ms invalid (~54x and valid where the reference fails); fast-path 0.148 ms (146x native shelf). Also the engine consumed by chapters 07 and 14. |
| **IrregularSheetFillV506** / Freeform Sheet Nest (FreeNest) | evolved-fork | `[Algorithm]` `Pack2DIrregularSheetV506Component.cs:23-27` (NFP-assisted BLF; Bennell-Oliveira tutorial). `[Obsolete]` `Exposure=hidden` `:56-57`. Overlap-then-trim documented by design in `examples/10_pack2d/README.md` (KB-6/KB-7). The FreeNestX evolution baseline; now phased out per the 2D-V-solver decision. |
| **IrregularSheetFillComponent** / Frahan Sheet Pack (Unified) (FreeNestU) | facade-over-primitives | `[Algorithm]` `IrregularSheetFillComponent.cs:32-33` ("Variant dispatcher V1/V2/V3/V506; Frahan-original strategy selector") over Burke 2007 + Bennell-Oliveira 2008. Adds no new algorithm; dispatches existing nesting variants behind one box. |
| **NfpPack2DComponent** / 2D NFP Pack | clean-room | `[Algorithm]` `NfpPack2DComponent.cs:11-12` (Burke 2007 DOI 10.1016/j.ejor.2006.03.011; Bennell-Oliveira 2008 DOI 10.1057/jors.2008.169). Citation-only; no upstream nesting source in the tree. |
| **NativeNfpKernel** (`nfp_kernel.dll`) | vendored-library | `NativeNfpKernel.cs:10-22`: "native/nfp_kernel/nfp_kernel.dll, vendored official Clipper2 C++" on the Int64 lane; only the marshalling is ours. Clipper2 BSL-1.0 (no copyleft). Consumed at `ContactNfpHoleNester.cs:924-930`. ~8x batched-NFP wall-time (`NativeNfpKernel.cs:10-22`). |
| **Pack2DTrencadisCatalogComponent** / Trencadis Catalog Pack | facade-over-primitives | `[Algorithm]` `Pack2DTrencadisCatalogComponent.cs:37-38` ("CVD-Lloyd interior seeding" Lloyd 1982; "Slab-partitioned Voronoi catalog; Frahan-original Trencadis extension"; precedent Battiato 2013 `:42`). Composes `CvdLloyd2d` + `HungarianAssignment` primitives. 28/28 placed in 53 ms (`examples/12_trencadis/README.md`). |
| **CvdLloyd2d** (CVD-Lloyd seed generator) | clean-room | `CvdLloyd2d.cs:14-22` (uniform-density CVD; matches `wiki/primitives/cvd_lloyd.md`); Lloyd 1982 relaxation, grid-discretised, stop at half-grid-step move (`:30-108`). Math-only, no upstream code. |
| **HungarianAssignment** (Kuhn-Munkres O(n³)) | clean-room | `HungarianAssignment.cs:11-15` ("classical shortest augmenting path formulation (Bourgeois-Lassalle 1971), standard textbook implementation"); potentials u/v with non-negative reduced costs (`:23-85`). Textbook math (Kuhn 1955 / Munkres 1957 / Bourgeois-Lassalle 1971), no upstream code. Reused by the masonry Lambda engine (Ch. 05) and the Trencadis catalog. |
| **Pack2DTrencadisComponent** / Trencadis Pack (greedy NFP-slide) | facade-over-primitives | `[Algorithm]` `Pack2DTrencadisComponent.cs:37-38` ("Trencadis greedy pack basic" Gaudi Park Guell; "NFP boundary slide" Minkowski-difference sampler). Battiato 2013 sect 4 cut budget `TrencadisFill.cs:13-27`. Standalone box is a skeleton returning empty (`examples/12_trencadis/README.md`) — see Roadmap (ghost component). |
| **Pack2DTrencadisDynamicComponent** / Trencadis Dynamic Settle | facade-over-primitives | `[Algorithm]` `Pack2DTrencadisDynamicComponent.cs:61-62` ("Trencadis dynamic settle" Frahan-original; "Kangaroo 2 goal-based physics" Daniel Piker). 55.1% physics vs 52.7% greedy (`examples/12_trencadis/README.md`). |
| **TrencadisEdgeMatchComponent** / Trencadis EdgeMatch | facade-over-primitives | `[Algorithm]` `TrencadisEdgeMatchComponent.cs:28-29` ("EdgeMatch-powered Trencadis pack"; "Frahan-original alternative to Battiato 2013 CVD+GVF stack"; "Beam-search assembly solver" Frahan-original). Composes the EdgeMatching primitives (Ch. 08). |
| **Pack2DTrencadisPipelineComponent** / Trencadis Pipeline | facade-over-primitives | `[Algorithm]` `Pack2DTrencadisPipelineComponent.cs:59-62` (greedy pack + NFP slide + CVD-Lloyd seeding + Kangaroo 2 settle, all cited to in-repo primitives + Daniel Piker physics). |

---

## Chapter 02 — Three-Dimensional Packing and Settling

| Component (family) | Class | Evidence |
|---|---|---|
| **Heightmap packers** (`GreedyHeightmapPacker`, `OrientedMeshHeightmap`, `MeshPileHeightmap`, `IrregularMeshContainer`) | clean-room | Deepest-bottom-left / DLBF substrate cited to Chehrazad, Roose, Wauters 2025 (`Pack3DIrregularComponent.cs:20-23`, GUID `E36C3F7D`). Two-surface mesh-pile proxy, per-cell vertical-interval test, six-orientation down-axis search, ray-cast container labelled "Frahan-original mesh-pile heightmap" (`Pack3DMeshHeightmapComponent.cs:20-21`, GUID `A16D6426`). `Heightmap.cs:8-69`, `OrientedMeshHeightmap.cs:8-292`, `MeshPileHeightmap.cs:79-271`, `IrregularMeshContainer.cs:52-176`. **Citation flag:** sibling `Pack3DIrregularContainerComponent.cs:18` credits "Park and Han 2024" (`[R8]`, no DOI, placeholder) for the same method — reconcile (Roadmap medium). |
| **BlockPackTreeComponent** / Block Pack (Tree) (DLBF guillotine forest) | evolved-fork | `[Algorithm]` `BlockPackTreeComponent.cs:30-31` (Kim 2025 Computation 13:211 DOI 10.3390/computation13090211, CC BY 4.0; Jalalian 2023 BCSdbBV cut-area term). GUID `C2D3E4F5`. Frahan deltas: deterministic master seed, saw kerf, forbidden boxes, parallel forest (`:22-28`, `:154-158`). Live 12/12, score 65.11, deterministic (`examples/11_pack3d/README.md`). |
| **Dlbf3dMixedSizePacker** (3D deepest-left-bottom-fill mixed-size) | clean-room | `Dlbf3dMixedSizePacker.cs:8-37`, `:186-220` (lexicographic deepest-left-bottom), `:127-143` (best-of-orientation, default off). `[Algorithm]` `BlockCutOptHeterogeneousComponents.cs:42` (Chehrazad, Roose, Wauters 2025, DOI 10.1080/00207543.2025.2478434). Standalone as Frahan Mixed-Size Block Pack 3D (GUID `F2D0BC18`). |
| **HeteroExt** (`FrahanHeterogeneousExtractionComponent`) | facade-over-primitives | `[Algorithm]` `BlockCutOptHeterogeneousComponents.cs:169` ("Frahan-original ... Composes Elkarmoty 2020 and Chehrazad 2025 ... the composition and the heterogeneity model are the contribution"). GUID `F2D0BC19`. Composes BlockCutOpt + DLBF + monument packer; `[RelatedComponent]` back-pointers to the standalone primitives (`:170-176`). Detailed once here; also in Ch. 03 / Ch. 13 / Ch. 14. |
| **Settle 3D (Physics)** (`PackSettle3DComponent` / `BulletSettleService`) | clean-room (over vendored) | `[Algorithm]` `PackSettle3DComponent.cs:29-35` (Zhuang et al. 2024 DOI 10.1016/j.cag.2024.103996 dynamics packing; Bullet via BulletSharp zlib; CoACD Wei et al. 2022; Heyman 1966 COM-over-support). GUID `134785ac`. Gravity-ramp seeding `BulletSettleService.cs:135-141`, centroid-relative decode `:62-70`. The dynamics framing + gravity-ramp + transform decode are ours; Bullet/BulletSharp/CoACD are vendored. |
| **SlabCutByFractures** / slab half-space cutter | clean-room | `[Algorithm]` `SlabCutByFracturesComponent.cs:33-39` ("Frahan-original" convex half-space clipping, Sutherland-Hodgman family; Goodman-Shi 1985 block theory `SlabCutter.cs:29-33`). GUID `C2B3D4E5`. Exact for convex input; opt-in CGAL backend for non-convex (`:84-92`). |
| **Slab Cut By Tool Mesh (CGAL)** / **Vertical Fracture Planes From Curves** | wrapper-of-native / clean-room | CGAL boolean path is wrapper-of-native over the GPL `frahan_cgal` shim (licensing register row 4); the curve-to-plane lift (GUID `F2D05A09`) is clean-room elementary geometry. |
| Example 15 statue-to-blocks decomposition (bed-bounded real-face grid) | facade-over-primitives | Study-level composition: 0.5 m grid × CGAL boolean over a Geogram-cleaned 2-manifold; recovered-volume ratio 1.0000 by `VolumeMassProperties` (`examples/15_statue_to_blocks/README.md`). Composes Geogram clean-up + CGAL boolean; no Core algorithm class. |

---

## Chapter 03 — Quarry Block-Cutting Optimization

| Component (family) | Class | Evidence |
|---|---|---|
| **BlockCutOptSolver** (pose-sweep max-cover) | clean-room | Core `BlockCutOptSolver.cs:108-135` (pose grid + parallel argmax, bit-identical to serial reference), `:261-268` (kerf film). GH `BlockCutOptSolveComponent` guid `F2D0BC02` `[Algorithm("BlockCutOpt brute-force search","Elkarmoty Bondua Bruno 2020, Resources Policy 68:101761",Doi=10.1016/j.resourpol.2020.101761)]` `BlockCutOptComponents.cs:97`. `README.md:46-50`: upstream is private C++, no source in tree. |
| **CuttingGrid** full-3D rotation (I1 pose tilt) | evolved-fork | `CuttingGrid.cs:84-110` (pre-multiplied U,V,W), `:77-78/127-129` (kerf pitch). `[Algorithm("Full 3D rotation grid","Frahan I1 improvement over Elkarmoty 2020 psi-only")]` `BlockCutOptComponents.cs:98`. `README.md:185`. psi-only back-compat constructor `OrientedBlock.cs:40-50` collapses to BlockCutOpt 2020. |
| **ObbTriangleIntersection + TriangleAabbBvh** (I2/I4 predicate) | clean-room | `ObbTriangleIntersection.cs:10-16` (13-axis SAT), used `BlockCutOptSolver.cs:243-256`. `[Algorithm("Triangle-AABB BVH pruning","Akenine-Moller 2001 fast 3D triangle-box overlap")]` `BlockCutOptComponents.cs:99`. `README.md:186-188`. |
| **BlockCutOptParetoSolver + BlockCutOptOmniSolver** (I6/I11 four-axis) | evolved-fork | `BlockCutOptParetoSolver.cs:82-95`, `ParetoPoint.Dominates` `ParetoPoint.cs:54-71`, `BlockCutOptOmniSolver.cs:115-178`. GH `BlockCutOptOmniSolveComponent` guid `F2D0BC04` `[Algorithm]` pair `BlockCutOptComponents.cs:306-307` (Elkarmoty 2020 + Jalalian 2023 BCSdbBV). `README.md:190,195`. |
| **BlockValueModel** BCSdbBV cost objective (I11) | clean-room | `BlockValueModel.cs:54-58` (SurfaceArea S=2(LxLy+LyLz+LxLz)), `:22-27` (BV). `ParetoPoint.cs:12`. `README.md:195` cites Jalalian, M.H. et al. 2023 DOI 10.1038/s41598-023-49633-w. Faithful axis from published math. |
| **RecoveryCascade** (multi-scale reject-recover) | evolved-fork | `RecoveryCascade.cs:26-29` (W(R,s) recursion), `:91-119` (kept/cracked partition by !bvh.AnyTriangleIntersects), `:21-24` (reduces to baseline at one scale), `:31-36` (BoEGE / Murugean 2026 cite softening, flag E9). No GH consumer — see Roadmap (high). Also detailed in Ch. 04. |
| **AmrrPlanner** (I9 plane sequence) + **SharedEdgeSlicer** (I12) | clean-room | `AmrrPlanner.cs:7-31`, `:132-178` (cut loop), `:85` (AMRR = removed volume / cutting time). GH `BlockCutOptAmrrPlanComponent` guid `F2D0BC03` `[Algorithm("AMRR in-block plane-sequence cutting","Shao, Liu, Gao 2022")]` `BlockCutOptComponents.cs:215`. `README.md:193,196` (Shao 2022 DOI 10.3390/pr10040695; Minetto 2017 DOI 10.1016/j.cad.2017.07.001). |
| **FractureBlockPack** (uncertainty-safe yield, example 09) | facade-over-primitives | `FractureBlockPackComponent.cs:27` (class), `:37` (guid `A7E0B0F3`), `:9-25` header: self-contained recovery engine that does NOT call RecoveryCascade / BlockCutOptSolver / Dlbf3dMixedSizePacker (silent-disagreement risk — Roadmap high). Fully managed, no native shim. |
| **HeteroExt** (heterogeneous quarry extraction) | facade-over-primitives | See Ch. 02 row. `[Algorithm]` `BlockCutOptHeterogeneousComponents.cs:169`; DLBF `[Algorithm]` `:42` (Chehrazad Roose Wauters 2025). |
| **FrahanSawBedScheduleComponent** (Saw Bed Schedule) | clean-room | `[Algorithm("Greedy LPT list scheduling","Graham 1969, SIAM J. Appl. Math. 17(2):416-429",Doi=10.1137/0117039)]` `QuarryCutOptComponents.cs:336`. Textbook LPT, no upstream code. |
| **Extraction Order Optimizer** | original-research | `[Algorithm]` note `QuarryCutOptComponents.cs:223` "no published scheduling algorithm matched". A-candidate, prior-art sweep pending (Roadmap low). |
| Guillotine cut staging (examples 24/25) | facade-over-primitives | Standard staged guillotine cutting (Gilmore-Gomory 1965 lineage); the contribution is the rendered, in-order, fabricable saw plan on real geometry, not an algorithm (`examples/24_guillotine_cut_sequence/README.md`). |
| Bed-bounded hexahedra + flat/oblique frontier (example 08) | facade-over-primitives | Study-level construction in the example generator (bed-plane fitting + packing objective); REPORTED not gated (`08_marble_cost_volume_metrics.json`). |

---

## Chapter 04 — GPR Fracture and Cavity Mapping

| Component (family) | Class | Evidence |
|---|---|---|
| **GprFileReader** + per-format readers (CSV/SEG-Y/MALA/pulseEKKO/IDS/GSSI) | clean-room / vendored-library | Per-format clean-room over open or public-domain specs (SEG-Y = SEG standard; pulseEKKO DT1/HD = USGS OFR 02-166, Lucius-Powers 1999; MALA/DZT/IDS decoded from open RGPR / BSD-3 readgssi). `GprFileReader.cs:23-51`. Dispatcher is a thin switch, no algorithm. Detailed by reader in Ch. 12. |
| **RadargramProcessor** filters (dewow / bg-removal / mute / t-gain / depth-equalize) | clean-room | Standard GPR processing (Annan 2009; Neal 2004), `RadargramProcessor.cs:100-182,310-332`. The contribution is the validated ordering and parameterisation, not the filters. |
| **Fft** (radix-2 Cooley-Tukey + Bluestein) | clean-room | `Fft.cs:16-18,30-149`. Numerical method (Cooley-Tukey 1965), not copyrightable; exact-length forward/inverse to match numpy.fft. |
| **HilbertEnergy** / analytic-signal envelope | clean-room | `Fft.cs:159-185`, `RadargramProcessor.cs:293-308`. Taner, Koehler, Sheriff 1979 complex-trace analysis, cited `GprFractureExtractComponent.cs:44`. |
| **StoltMigration** (f-k migration + cosine dip-taper) | clean-room (+ evolved anti-alias) | `RadargramProcessor.cs:199-291`, half-velocity `:208`, Jacobian `:259-267`, dip-taper `:235-244`. Stolt 1978, cited `GprFractureExtractComponent.cs:44`. The published method is clean-room; the cosine dip-taper is a small evolved anti-alias addition, not a new migration. |
| **FractureExtractor** (high-energy + dip-aware continuity) | evolved-fork | `FractureExtractor.cs:8-159`. Clean-room base = high-energy quantile + USGS Mirror Lake continuity (WRIR 99-4018C; Porsani 2006; Isakova 2021). The dip-aware shear-count continuity (`:76-130`) is the measured delta over the flat-horizon USGS test. GH GUID `A7E0B0F1`. |
| **GprPresets** (stone × frequency catalogue) | clean-room | `GprPresets.cs:7-25`. Calibration data, no algorithm; `IsEmpirical` flag (`:22-24`) distinguishes validated (`marble_600`, `granite_160`) from literature-default presets. |
| **FractureSurface** loft / reconstruct | clean-room / wrapper-of-native | Loft path clean-room elementary construction (`FractureSurface.cs:42-110`). Reconstruct path wrapper-of-native over geogram screened-Poisson (Kazhdan-Hoppe 2013, BSD-3 + bundled MIT PoissonRecon) and CGAL advancing-front (GPL), reached out-of-process (`:112-139`). |
| **FractureUncertainty** (position ladder + detection rung) | original-research | `FractureUncertainty.cs:6-220`. Three-rung position ladder (depth-growing velocity + time-zero + λ/4), detection rung with depth-aware Fresnel floor and `P_det` factorisation are the Frahan contribution; underlying physics cited (Porsani 2006; Xie 2021; Molron 2020; Dorn 2012). A-candidate, prior-art sweep pending. GH `GprFractureSurface3DComponent.cs:30`, GUID `A7E0B0F2`. |
| **Kriging** (simple kriging posterior) | clean-room | `Kriging.cs:8-29`. Ordinary kriging linear algebra (Cressie 1993; Rasmussen-Williams 2006); managed replacement for scikit-learn GPR. |
| **BedrockSurface** | clean-room | `BedrockSurface.cs:7-93`. Pure reduction + datum shift; deepest-reflector top-of-rock (Porsani 2006 / Isakova 2021). GH GUID `A7E0B0F1` (GprBedrockSurface). |
| **TinMerge** (k-NN inverse-distance weighting) | clean-room | `TinMerge.cs:54-122`. Shepard 1968 IDW with a scale-relative radius and recenter; no upstream code. |
| **TinPeelFilter** (border-peel scan cleaner) | clean-room | `TinPeelFilter.cs:7-163`. Border-peel logic (long-edge / vertical-facet / cap predicates) ported from the Fade2D land-survey reference's `peelOffIf`, no upstream code; cited in `CleanScanMeshComponent.cs:29-31`, GUID `A7E0B0F1`. |
| **Vector Fractures Loader** (shapefile fracture map, example 26) | vendored-library | NetTopologySuite.IO.Esri (ESRI Shapefile / OGC Simple Features); strike binning and render clean-room. Detailed in Ch. 12. |

---

## Chapter 05 — Masonry Equilibrium and Cyclopean Reassembly (CRA)

| Component (family) | Class | Evidence |
|---|---|---|
| **Masonry Stability (RBE)** | clean-room | `[Algorithm]` `MasonryStabilityRbeComponent.cs:69-71` (Kao et al. 2022 CAD 146:103216; Whiting et al. 2009 RBE precedent; compas_cra MIT cited, not copied). Equilibrium math `EquilibriumMatrixBuilder.cs:13-30,201-219`; linearised Coulomb cone `FrictionConeBuilder.cs:24-32`; inscribed-pyramid correction `:105-130`. Wires the sign-corrected `BuildPhysicsCorrected` at `:305`, not the legacy `Build`. Convex-QP force+moment balance, compression-only normals. |
| **Masonry Stability Check (CRA)** | original-research | `CraStabilityChecker.cs:17-50` (Kao H-model Eqs 8-11 cited); alternating-convex certificate `:154-186` is NOT in compas_cra (which uses non-convex IPOPT). A-candidate, soundness-certifying direction proven; H-model regression `CraStabilityCheckerTests.cs:83-105`; compas_cra parity `Program.cs:347-356`. Rejects self-stressed states RBE wrongly accepts. GUID `D5F10015`. |
| **AdmmQpSolver** | clean-room | `AdmmQpSolver.cs:6-51` (Stellato et al. 2020 OSQP, Math.Prog.Comp 12:637-672); ADMM iteration `:182-256`; masonry Ruiz equilibration `:108-145`. CSR-sparse, per-row rho. OSQP-style infrastructure with engineering deltas; no upstream OSQP source in tree. |
| **Polygonal Wall (Generator)** | original-research | `PolygonalWallGenerator.cs:7-34` (power diagram `:13-17`), interlock metric J `InterlockScore:310-384`. A-candidate (Kim 2024 does sequencing, not generation; sweep pending — Legakis 2001 closest prior). Hover credits Kim 2024 / Clifford-McGee 2018 / Lloyd 1982 at `PolygonalWallGeneratorComponent.cs:31-33`. GUID `D5F10014`. |
| **PolygonalWallAssembler** (exact-joint) | clean-room | `PolygonalWallAssembler.cs:8-30` exact planar-quad interface per adjacent pair from shared (u,v) edges; avoids mesh-contact-detector splintering of the equilibrium QP. Feeds the equilibrium builder; `Cra_GeneratedWall_Certified` `Program.cs:335`. |
| **Stone Cell Match (Lambda engine)** | original-research | `StoneCellAssignment.cs:8-37` (Lambda / lambda / gap formulas); composes the reused `HungarianAssigner:141-145` and voxel kernel. A-candidate Lambda formalisation (Clifford-McGee measured 0.27, never formalised; assignment published in Bruetting 2019 / Bukauskas 2019); ETH1100 datum `StoneCellAssignmentEthBenchmarkTests.cs:14,95`; reported Lambda = 0.194. GUID `D5F10016`. |
| **StoneCarveBack** (Cyclopean) | facade-over-primitives | `StoneCarveBack.cs:9-29` cites Clifford & McGee 2018 anti-nesting; exact booleans through the in-repo `CgalMeshBoolean:23-28` primitive; volume-validated in the battery. Composes booleans, adds no new algorithm. |
| **Rubble Wall Settle** | clean-room | `[Algorithm]` `RubbleWallSettleComponent.cs:35-36` (Heyman 1966 limit-state); Core `RubbleWallSettle.cs:9-32` from the signed-off Furrer 2017 / Johns 2020 prototype. Deterministic, non-penetrating, PCA flat-bedding + per-cell drop, Heyman COM-over-support. GUID `6514A1BB`. |
| **Ashlar Pack** | clean-room | `[Algorithm]` `AshlarPackComponent.cs:31-32` (Gramazio Kohler Eichenhofer 2017 NCCR running-bond). Tier-C grid stacking, AABB-first, translation-only. GUID `F1A2B3C4`. |
| **Best-Fit Inventory Pack** | clean-room | Core `BestFitInventoryPacker.cs:8-32` carries the correct Furrer 2017 / Johns 2020 lineage. **CITATION FLAG E5:** the GH facade `BestFitPackComponent.cs:30` attributes a likely-fabricated "Gramazio Kohler Eichenhofer 2017 CAD paper" — corrected to Furrer/Johns (see register and Roadmap). |

---

## Chapter 06 — Voussoir Geometry and Stereotomy

| Component (family) | Class | Evidence |
|---|---|---|
| **Arch Voussoirs** (`ArchVoussoirsComponent` / `VoussoirCellFactory.BuildArch`) | clean-room | `[Algorithm]` `ArchVoussoirsComponent.cs:31-36` (Frahan-original radial cell construction; geometric law Frezier 1737 / Monge 1798). GUID `D5F10012`. Intrados stationing `VoussoirCellFactory.cs:119-174`, outward-normal radial bed joint `:136-146`, catenary parameter solve `:412-432`. The upstream Voussoir plugin (Varela-Sousa) is a cited precedent (`:40`), not a dependency. 11/11 carved, 94.9% coverage (example 21). |
| **Pendentive Vault Voussoirs** (`PendentiveVaultVoussoirsComponent` / `BuildPendentiveVault`) | clean-room | `[Algorithm]` `PendentiveVaultVoussoirsComponent.cs:29-34` (Frahan-original square-grid-lifted sphere cell; Monge lines-of-curvature law). GUID `D5F10013`. Square-grid lift `VoussoirCellFactory.cs:244-256`, corner-on-sphere precondition `:230-234`, radial frustum cell `:269-313`. Rippmann-Block 2011 / RhinoVAULT named as design precedents (`:38`), not in-tree deps. 36/36 carved, 98.3% coverage (example 22). |
| **Inward-orientation fix** (`MakeHexahedron` signed-volume flip) | clean-room | `VoussoirCellFactory.cs:452-464`. Signed-volume sign as an orientation oracle; the precondition that makes the downstream CGAL trim return the carved voussoir rather than its complement. Closedness checked + warned (`ArchVoussoirsComponent.cs:163-165`, `PendentiveVaultVoussoirsComponent.cs:150-152`). |
| **CGAL trim** (digital ravalement, examples 21/22) | facade-over-primitives | Downstream trim runs through the in-repo `CgalMeshBoolean` primitive (GPL CGAL kernel in Rhino, managed BSP fallback headless); out of scope for this tab. The generator only produces correctly oriented input. |
| Voussoir Ingest / Stone Matcher / Pack Into Block | facade-over-primitives | Hungarian and bin-pack facades over the quarry assignment layer (`VoussoirRecord.cs:11-21`); documented with the masonry/quarry assignment chapters. Consume external (Varela-Sousa) cells only through the separate Ingest path. |

---

## Chapter 07 — Surface Packing and Conformal Unwrapping

| Component (family) | Class | Evidence |
|---|---|---|
| **PackOnSurfaceComponent** (BFF flatten + pack + lift) | facade-over-primitives | Orchestrates BFF flatten, 2D pack (`ContactNfpHoleNester`, Ch. 01), and barycentric lift. GUID `B7E4D9C1`. `[Algorithm]` `PackOnSurfaceComponent.cs:41` mis-credits "Floater 2003 mean value coordinates" (DOI 10.1016/S0167-8396(03)00002-5) — the shipped lift is plain barycentric, attributable to the mean-value-coordinate family (after Floater 2003), not MVC. Attribution defect, math correct (Roadmap medium, flag M-Floater). |
| **PackSurfacesComponent** (multi-chart, fabrication frames) | facade-over-primitives | Composes BFF chart + `SurfaceHoleNestBridge` + `ContactNfpHoleNester` + `BarycentricMapper2DTo3D`; emits rigid placement frame, full transform, max deviation (`PackSurfacesComponent.cs:56,425,613,655`). GUID `C4A8D2E1`. No new algorithm; self-trigger async, V506-only inputs inert. |
| **BFF runtime** (Boundary First Flattening) | vendored-library | External static `bff-command-line.exe`; Sawhney & Crane 2017 DOI 10.1145/3072959.3056432 (`SurfaceChartComponent.cs:43`). Permissive-as-published; owes a THIRD_PARTY_NOTICES row (licensing register). Static single-exe rebuild is a packaging change (commit `d1b5c5b`). |
| **ChartScaleComputer** (chart-scale recovery) | original-research | `ChartScaleComputer.cs:14-58` recovers one isotropic scalar s as the perimeter-weighted average of e^u. Frahan-original scale recovery over the conformal flatten; known global-scale limitation (Roadmap medium). |
| **BarycentricMapper2DTo3D** (inverse lift) | clean-room | `BarycentricMapper2DTo3D.cs:141-179` plain triangle barycentric interpolation (Cramer's rule on the 2D Gram system). SLM card `bff-surface-flatten.md:6` confirms "NO Floater-2003 MVC code present". O(P·F) linear scan, author ceiling ~2000 faces `:107` (Roadmap medium). |
| **ChartDistortionAnalyzer** (edge-stretch metric) | clean-room | `ChartDistortionAnalyzer.cs:96-98` scalar edge-length stretch. Cannot detect signed-area foldovers (Roadmap medium, flag M3). |
| **ChartFlatnessReport** (area-ratio symmetrised classifier) | clean-room | `ChartFlatnessReport.cs:90-101` per-face max(r,1/r); Frahan-original, not the BFF algorithm (`ChartFlatnessReportComponent.cs:23-24`, GUID `AB12C006`, filed on Surface Packing). One of the five `FrahanReport` types the audience terminal consumes (Ch. 13). |
| **FaceCornerUvTable** (seam-correct unwelded flat mesh) | clean-room | `FaceCornerUvTable.cs:37-56` keys UVs on (face, corner), three fresh vertices per triangle so seams never bridge; throws on a missing UV. Non-trivial seam engineering; no upstream code. |
| **SurfaceHoleNestBridge** (curve-to-loop) | clean-room | `SurfaceHoleNestBridge.cs:21` shared curve-to-loop: uniform sampling, proxy-deviation measure, CCW enforcement, WorldXY guard. No new algorithm; the single curve-to-loop seam. |
| **MeshObjIO** (OBJ chart I/O) | clean-room | Writes OBJ at raw world coords G10; no recenter, so far-from-origin (UTM) charts lose mantissa bits (Roadmap low, flag T1). |

The Surface Packing back end (BFF, geogram, CGAL shims) is reached
out-of-process and is absent from the default install; see the licensing
register for the copyleft routing.

---

## Chapter 08 — Edge-Matching and Fragment Reassembly

| Component (family) | Class | Evidence |
|---|---|---|
| **Boundary segmenter** (signed-turning descriptor) | clean-room | `[Algorithm]` `EdgeMatchSolveComponent.cs:23` ("Frahan-original arc-length curvature/torsion signature"); core `BoundarySegmenter.cs:151-189`. Rotation-invariant signed-turning signature per segment from the standard turning-function representation (Arkin 1991); no upstream code. |
| **Segment hash index** | clean-room (Frahan-original) | `[Algorithm]` `EdgeMatchSolveComponent.cs:24`; `SegmentHashKey.cs`. Quantised invariant-hash bucketing of segment signatures for candidate pruning; planarity-aware 2D/3D split. |
| **Phase correlator** (coarse lag) | clean-room | `[Algorithm]` `EdgeMatchSolveComponent.cs:25`; `PhaseCorrelator.cs:13-44`. Direct O(n²) circular L1 cross-correlation lag estimate. **Attribute wording flag:** named "Phase correlator FFT" but `:29-34` is direct cross-correlation, not a frequency-domain transform — reword to direct cross-correlation (register / Roadmap). |
| **Constrained ICP (2D/3D) + SVD Kabsch** | clean-room (algo) over vendored MathNet | `EdgeMatchSolveComponent.cs:26`; `ConstrainedIcp3D.cs:147-189`. Besl-McKay 1992 ICP loop with a Kabsch 1976 / Umeyama 1991 SVD rigid fit and reflection guard; the SVD itself is the vendored `MathNet.Numerics` kernel, the loop and constraints are ours. |
| **Order-preserving correspondence DP** | clean-room | `OrderedBoundaryMatcher.cs:24-37,100-167`. Dynamic-programming monotone non-crossing boundary correspondence (DTW substrate; Marcotte-Suri 1991 cited as the non-crossing idea, not ported); textbook DP, no upstream code. |
| **Initial transform builder** | clean-room | `InitialTransformBuilder.cs:16-65`. Plane-to-plane complement-orientation seed for ICP (Frenet frames in 3D); plumbing, no `[Algorithm]` attribute. Tier-D scaffolding. |
| **Soft-ICP refiner** (CPD + hinge) | clean-room / evolved-fork | `SoftIcp3DComponent.cs:43-51`; `SoftIcpRefiner.cs:299-527`. Soft correspondence is a CPD-style (Myronenko-Song 2010) E-step with a weighted-Kabsch M-step (Kabsch 1976/1978, vendored MathNet SVD `:49-51`). Evolved-fork delta: the unified contact-plus-non-penetration target redirection (+97% clearance). GUID `D5F1000E`. |
| **Projection bootstrap** | original-research | `ProjectionPairFinder.cs:16-48,481-678`. Per-facet projection bootstrap, antiparallel SE(3) lift composition, 3D-disposes verification gate. A-candidate; prior-art sweep pending (`AGENTS.md` §9). The geometric 3D path assembles only via this bootstrap (independent tessellation yields zero cross-panel hash hits). |
| **Horn registration kernel** | clean-room | `RigidTransformRecovery.cs:6-30`; consumed `GeoreferenceComponent.cs:36` (Ch. 11). Closed-form Horn 1987 unit-quaternion absolute orientation. One of three duplicate absolute-orientation routes (Roadmap low, refactor). |
| **Block Pair Match 3D** (VSA face partitioner) | facade-over-primitives (stub partitioner) | `[Algorithm(... Frahan stub implementation)]` `BlockPairMatch3DComponent.cs:43-45`. The Variational Shape Approximation (Cohen-Steiner 2004) face partitioner is a declared stub; `[RelatedComponent]` `:41` redirects users to the practically-tested Hungarian Stone-Cell Match. GUID `D5F10008`. |
| **Frahan.Kintsugi.Port** (learned reassembly) | direct-port (research-only) | C# port of PuzzleFusion++ (Wang, Chen, Furukawa 2025, ICLR). Production 3D path; isolated **non-commercial research-only** assembly with converted weights `kintsugi.bin` (~255 MB). See licensing register flags E1/jigsaw. Norm-undo + verifier-gated pose composition. Detailed in Ch. 09. |

---

## Chapter 09 — Kintsugi and Learned 6-DoF Pose

| Component (family) | Class | Evidence |
|---|---|---|
| **DiffusionScheduler** (PuzzleFusion++ custom schedule + DDPM step) | direct-port | `DiffusionScheduler.cs:7-9` ("direct port of `custom_diffusers.py`"). Piecewise-quadratic ᾱ `:59-71`, ε-DDPM posterior `:124-129`. The two terminal guards (`:108-111`, `:120-123`) reproduce the diffusers reference, not a change. |
| **KintsugiPortInference** (encode-in-loop orchestrator + PointNet++ / VQ-VAE / Se3Denoiser) | direct-port | `KintsugiPortInference.cs:11-43` ("Mirrors upstream `auto_aggl.py::AutoAgglomerative.test_denoiser_only` step-for-step"). Encoder (Qi 2017), VQ-VAE (van den Oord 2017), 6-block AdaLN transformer `Se3Denoiser.cs:12-22` (Vaswani 2017; Peebles-Xie 2023). Dual TorchSharp/libtorch path with silent-fallback report (`:57-72`). |
| **Pose-composition fix** (norm-undo + three-factor world composition) | original-research (Frahan-original wrapper) | `[DesignApplication]` "Frahan-original pose composition fix" `KintsugiAssemblyComponent.cs:81`. `T_world(f)=T_unnorm(0)·T_net·T_norm(f)` assembled `:1098-1102,1032-1036`; anchor identity `:1025-1028,1081`. The composition that makes the port usable in document coordinates is the repository's contribution; A-candidate. |
| **Verifier** (learned pair classifier + 0.5 gate) | direct-port | `Verifier.cs:7-22` (`VerifierTransformerPort`); sigmoid head + transformer stack upstream. The per-fragment confidence reduction and 0.5 accept/reject gate (`KintsugiAssemblyComponent.cs:1056-1089`) are the port-side integration that keeps weak predictions from collapsing the assembly. |
| **Geometric penetration verifier** (default Mode=Geometric) | clean-room | Frahan-original penetration-based verifier rejecting interpenetrating placements (`KintsugiAssemblyComponent.cs:75-77`); not the learned verifier, runs only in geometric mode (the clean-room edge-matching assembler of Ch. 08). |
| **Frahan.Kintsugi.Port** (whole assembly) | direct-port (research-only) | C# port of PuzzleFusion++ (Wang, Chen, Furukawa 2025, arXiv:2406.00259). Module headers cite the upstream Python file per module. Sole `direct-port` in the thesis; quarantined in a separate **non-commercial research-only** assembly, absent from the default install (register rows 1-3, flag E1 CRITICAL). |

---

## Chapter 10 — Mesh Processing and Surface Reconstruction

| Component (family) | Class | Evidence |
|---|---|---|
| **CgalMeshBoolean / CgalGeometry** (corefinement, repair, decimation, SDF/angle seg, skeleton, partition, heat geodesics) | wrapper-of-native | P/Invoke surfaces over `frahan_cgal.dll`; algorithms execute in vendored CGAL PMP (Botsch 2010; Shapira 2008 SDF; Lindstrom-Turk 1998; Aichholzer-Aurenhammer 1996; Crane 2013 heat). `CgalMeshBoolean.cs:8-23,66-86`, `CgalGeometry.cs:257-659`. Only marshalling, probe, buffer lifetime are ours. CGAL is GPL (`CgalGeometry.cs:23-28`). |
| **GeogramMesh** (decimation, repair, fill-holes, remesh, OBB, CVT/RVD, Voronoi blocks) | wrapper-of-native | P/Invoke over `frahan_geogram.dll`; vendored Geogram BSD-3 (Levy, `GeogramMesh.cs:9-20`). `[Algorithm]` credit Geogram by name/version (`GeogramTestComponents.cs:25,163,311,531`); Lloyd 1982 inside CVT/RVD. TetGen path (Voronoi blocks) AGPL, OFF by default (`:354-361`). |
| **Scan Reconstruct** (`ReconstructionNative`, modes 0-4) | wrapper-of-native | GUID `E4F5A6B7`; three `[Algorithm]` (Edelsbrunner-Mucke 1994 alpha; Kazhdan-Hoppe 2013 screened Poisson; Cohen-Steiner-Da advancing-front), `ScanReconstructComponent.cs:32-37`. CGAL (GPL) + Geogram-bundled Kazhdan PoissonRecon (MIT). Repository deltas: recenter conditioning, out-of-process crash isolation, binary IPC, async Run gate, soup cleanup — no algorithm. |
| **MeshCsg** (managed BSP CSG fallback) | direct-port | `MeshCsg.cs:9-10` ("port of Evan Wallace's csg.js (MIT)"). Silent fallback under `CgalMeshBoolean` when the shim is absent. Permissive MIT; owes a THIRD_PARTY_NOTICES row, no copyleft. |
| **MeshRepair / Mesh Diagnostics** | facade-over-primitives | RhinoCommon weld / cull / heal / unify-normals pipeline + read-only inspector over the standard PMP recipe (Botsch 2010, `MeshRepairComponent.cs:19`, GUID `AB12C00A`; `MeshDiagnosticsComponent.cs:18`, GUID `AB12C005`). Orchestration + readout only. |

---

## Chapter 11 — Fabrication, Sculpting and Carving

| Component (family) | Class | Evidence |
|---|---|---|
| **GCodeParser** (ISO 6983-1 modal state machine) | clean-room | `[Algorithm]` `GCodeParserComponent.cs:53-58` ("ISO 6983-1 G-code tokenizer + modal state machine"; ISO 6983-1:2009 + RhinoCAM/VisualMill dialect). GUID `D5F10030`. Single-pass tokenizer + modal switch `:169-266`; no upstream parser source. |
| **GCodeToPlanes** (tool-axis frame construction) | clean-room | `[Algorithm]` `GCodeToPlanesComponent.cs:39-44` (milling-frame convention, chord-step arc discretisation; Frahan-original glue, CGAL arc primitives deliberately not used). GUID `D5F10031`. Gram-Schmidt frame `:197-211`, arc sweep `:240-270`. |
| **Robot adapters** (`PlanesToKukaPrcCommands`, `PlanesToRobotTargets`) | facade-over-primitives / wrapper | `[Algorithm]` `PlanesToKukaPrcCommandsComponent.cs:40-42`, `PlanesToRobotTargetsComponent.cs:40-42` (thin wrappers credited to KUKAprc Brell-Cokcan/Braumann and visose/Robots Soler MIT; only the CutSegmentKind-to-motion mapping is Frahan-original). GUIDs `D5F10032`/`D5F10033`. Neither links `Robots.dll` — no licence ingress. |
| **WireSawToolpathAdapter** (kerf-compensated wire-saw path) | clean-room (glue over cited precedent) | `[Algorithm]` `WireSawToolpathAdapterComponent.cs:54-62` (Zhang et al. 2024 J.CDE 11(6) DOI 10.1093/jcde/qwae094 + Moult 2018 robot-mounted diamond-wire precedents; kerf-compensated offset is Frahan-original glue over RhinoCommon `Curve.Offset`). GUID `D5F10034`. Half-kerf offset `:174-192`. |
| **StaggeredBlockDecompose** (running-bond cell layout, "Fabricate" flagship) | facade-over-primitives | `[Algorithm]` `StaggeredBlockDecomposeComponent.cs:33-34` (cell layout Frahan-original; running bond a masonry convention, not a citable algorithm). GUID `F2D07A02`. Composes pure-managed `StaggeredBlockLayout.cs:62-90` + the CGAL/geogram boolean back end it routes to. |
| **StoneCutExport / StoneCutMetadata** (CAM-handoff carriage) | clean-room (glue) | `StoneCutMetadata.cs:10-75` namespaced user-strings + schema tag; writer is RhinoCommon `File3dm` + `SetUserString`. GUID `F2D07A01`. Structured carriage contract, no algorithm. |
| **FabricationReport / FabricationPrepReport** (weight + lift class) | clean-room | `FabricationReport.cs:6-40` (W=ρV + lift-class ladder); RhinoCommon `VolumeMassProperties`. GUID `F2D07A04`. Elementary mass-properties arithmetic; handling convention, not an algorithm. |
| **EnlargeSculpture / FitInBlock** (Sculpt tab) | clean-room | `[Algorithm]` `EnlargeSculptureComponent.cs:25-26`, `FitInBlockComponent.cs:28-29` (Frahan-original digital pointing-machine affine scale; axis-aligned extent matching). GUIDs `F2D06A01`/`F2D06A02`. Core `SculptureFitter.cs:47-114`. |
| **CarvingStages** (staged offset-shell roughing) | clean-room | `[Algorithm]` `CarvingStagesComponent.cs:53-54` ("Staged offset-shell roughing", Frahan-original; no published roughing-strategy paper). GUID `F2D06A03`. Core linear ladder `CarvingStages.cs:25-39`; GH fold-fix (smoothed normals + neighbour cap) `:342-403`. Synchronous + cached, decimate-first (KB-1/KB-2). |
| **GeoreferenceMath / Georeference** (geodesy + Horn rigid fit) | clean-room | `GeoreferenceMath.cs:6-283` (WGS84, Bowring 1976 LLH-to-ECEF, ENU rotation, Karney 2011 UTM, Snyder 1987; zero third-party deps). Rigid fit reuses the `RigidTransformRecovery` Horn 1987 kernel (Ch. 08) via `RegistrationApi`; `[Algorithm]` `GeoreferenceComponent.cs:36`. GUID `B1C2D3A4`. |

---

## Chapter 12 — Data Ingestion and Format Readers

| Component (family) | Class | Evidence |
|---|---|---|
| **Vector fracture readers** (Shapefile / GeoJSON) | vendored-library | Thin adapters over NetTopologySuite.IO.Esri / .GeoJSON; only the geometry-to-`FractureTrace` mapping and `.prj` carry-through are ours (`ShapefileFractureReader.cs:5-105`, `GeoJsonFractureReader.cs:5-19`). `[Algorithm]` `VectorFracturesLoaderComponent.cs:38-39` (ESRI Shapefile / OGC Simple Features). GUID `F2D00BEC`. NTS permissive (BSD-3-style), notices owed. |
| **StreamingCloudReader + VoxelGridSink** (PLY/XYZ streaming voxel downsample) | clean-room | `StreamingCloudReader.cs:10-32`, voxel hash-grid centroid accumulator; peak memory bounded by occupied voxels, not input count. Elementary spatial-hash quantisation; PLY per Turk 1994. |
| **LazCloudReader** (LAS/LAZ stream) | vendored-library | Wraps Unofficial.laszip.net (LASzip, Isenburg 2013; LGPL-style, net48); streams into the same `VoxelGridSink`. `LazCloudReader.cs:9-55`. Only the stream-into-sink wiring is ours. ASPRS LAS 1.4. |
| **Load E57 Cloud** (out-of-process Python worker) | wrapper-of-native | `[Algorithm]` `LoadE57CloudComponent.cs:33-35` (Frahan-original; subprocess isolates the E57 parse, coords shifted to origin). GUID `E4F5A6B7`. E57 decode is `pye57`/libE57Format driven out-of-process; the voxel sort-reduce is a clean-room numpy kernel (`frahan_e57_worker.py:37-131`); subprocess orchestration + coordinate-shift precision scheme are ours. E57 per ASTM E2807-11. |
| **GPR readers** (SEG-Y / MALA RD3 / pulseEKKO DT1 / IDS DT / GSSI DZT / CSV) | clean-room | Per-format clean-room over open or public-domain specs (`GprSegYReader.cs`, `GprMalaRd3Reader.cs`, `GprDt1Reader.cs`, `GprIdsDtReader.cs`, `GprDztReader.cs`); a binary layout is not itself copyrightable. Depth-axis derivation dz=v·dt/2 with preserved velocity-independent dt. |
| **GPR Radargram Mesh / GPR Picks From Points / GPR File Loader** | facade-over-primitives | Frahan-original visualisation and pick-conversion over the same readers (`GprRadargramMeshComponent.cs:23-25`, `GprPicksFromPointsComponent.cs:25-27`; `GprFileLoaderComponent.cs:73-122`). GUIDs `F2D05A04`/`F2D05A07`/`F2D00BEC`. |
| **GprFileReader dispatcher** + `.gsf` dead-stop | clean-room (thin) | Extension-dispatch switch with a deliberate `NotSupportedException` on the proprietary Geoscanners `.gsf` (`GprFileReader.cs:23-55`); bridge-not-guess, no algorithm. |

---

## Chapter 13 — Lab, Analysis and Reporting

| Component (family) | Class | Evidence |
|---|---|---|
| **Native-shim exercisers** (CGAL / Geogram / CoACD / Auto families, 20 of 26 Lab nodes) | wrapper-of-native (clean-room marshalling) | Grasshopper surfaces driving the out-of-process kernels end-to-end; the weld/drop-unreferenced marshalling is the only in-tree work (`CgalConvert.ToSnapshot`, `CgalTestComponents.cs:29-64`; `AutoMeshComponents.cs:55-88`). Cited kernels: CGAL PMP (GPL), Geogram (BSD-3), CoACD (Wei 2022, transitively GPL). Each carries a `[RelatedComponent]` redirect (lab-not-an-island). |
| **BCOPareto** (four-axis front inspector) | clean-room | `[Algorithm]` `BlockCutOptInspectorComponents.cs:38` (Jalalian 2023 BCSdbBV). GUID `F2D0BC10`. Surfaces the `Front.BestBcsdbBv()` extremum the production node hides; no new algorithm. |
| **BCORobust** (Fisher-robust Monte-Carlo) | clean-room | `BlockCutOptInspectorComponents.cs:175,270-289` over the cited Azarafza 2016 Fisher reading; reports R_p10/R_p50/R_p90 + median direction. GUID `F2D0BC11`. Monte-Carlo robustness sampling, no new algorithm. |
| **BCOWatershed** (density-watershed zones) | clean-room | Fronts the in-tree Frahan-original `DensityWatershedPartition` (`BlockCutOptInspectorComponents.cs:298,340-381`). GUID `F2D0BC12`. |
| **VtuOut** (ParaView export) | facade-over-primitives | Composes solver + cutting grid + BVH, writes `.vtu`; `Write` gate (`BlockCutOptInspectorComponents.cs:463-499`). GUID `F2D0BC13`. |
| **BCOMixedPack** (`DlbfMixedSizePacker`, 2D) | clean-room | `[Algorithm]` `BlockCutOptInspectorComponents.cs:509` (Chehrazad, Roose, Wauters 2025). GUID `F2D0BC17`. The 2D DLBF primitive seam of the monster-vs-primitive pairing with HeteroExt. |
| **GetData** (distribution helper) | clean-room (Frahan-original utility) | `[Algorithm]` `DownloadFrahanDataComponent.cs:36-38`. GUID `F2D05A08`. Engineering utility, not research; the mechanism that keeps the non-commercial Kintsugi weights out of the default install. |
| **Reports tab** (`PackRpt`, `PackPlanRpt`, `Report`) | facade-over-primitives | Frahan-original report generators over pure-data Core DTOs (`PackingReportComponent.cs:25` GUID `AB12C004`; `PackingPlanReportComponent.cs:24` GUID `AB12C008`; `AudienceReportComponent.cs:30` GUID `AB12C010`, with the CRS-refusal guard). No new algorithm. |
| **Analysis tab** (`RailIdx`, `FragDesc`, `FragMatch`) | clean-room | Frahan-original diagnostics: descriptor schema + arc-length affinity-bucket index (`FragmentDescriptorsComponent.cs:29-31` GUID `AB12C007`; `BoundaryRailIndexComponent.cs:33-35` GUID `AB12C001`; `FragmentEdgeMatchComponent.cs` GUID `AB12C003`). Turning-function precedent Arkin 1991. |

---

## Chapter 14 — Workflow Architecture and Data-Flow Connections

Chapter 14 is cross-cutting and introduces no new solver. It documents how
the per-subsystem algorithms above connect along the ingest → process →
segment → pack-or-cut → stabilise → fabricate spine. Its components are the
ingest readers, mesh-hygiene nodes, and report emitters, each of which is
either a vendored reader (Ingest tab, classed vendored-library by its format
library) or a facade-over-primitives orchestrator, all detailed at their
primary chapters above. No component introduced in this chapter is
original-research; its contribution is the connection topology, documented
in-place, not a new algorithm. The chapter's own originality call-outs
(`ContactNfpHoleNester`, Block Pack Tree, HeteroExt, RBE/CRA, Polygonal
Masonry Sequence, GPR Fracture Extract, Rubble Wall Settle, Trencadis
Catalog, Arch/Pendentive Voussoirs, Kintsugi Port, Vector Fractures Loader,
Pack On Surface, Ashlar Pack) restate the per-chapter classes verbatim.

---

## Chapter 15 — Evolution: From Baselines to the Current System

Chapter 15 is cross-cutting and introduces no new component. It narrates the
six measured-delta threads (2D nesting V506→FreeNestX→CNH; 3D heightmap→
mesh-accurate+settle; BlockCutOpt 2020→v2; RBE→CRA-coupled+Lambda; GPR
RecoveryCascade + staged guillotine; surface packers + BFF onto the hardened
engine). Every verdict it carries (`evolved-fork` for the nesters and the
quarry pose increments, `original-research` for the CRA certificate / J / Λ,
`facade-over-primitives` for the surface recompose and FractureBlockPack,
`vendored-library` for BFF) restates the per-chapter classification with
commit evidence; no class is introduced here that is not already counted at
its primary chapter.

---

## Whole-repo summary: counts per class

Counted over the audited shipped components and component families across all
fifteen chapters. A family spanning multiple chapters (CNH, HeteroExt,
RecoveryCascade, Horn kernel, Kintsugi Port, Vector readers, ChartFlatness,
Hungarian) is counted once at its primary chapter. The two cross-cutting
chapters (14, 15) add connection topology and evolution narrative, not new
components, so they contribute no new rows.

The total is **109 classified component families** across the fifteen
chapters (matching the per-class column below). Counting is by family: a
compound entry such as `ObbTriangleIntersection+BVH` or
`EnlargeSculpture/FitInBlock` is one family, and a family that recurs in a
later chapter (CNH, HeteroExt, Horn kernel, Kintsugi Port) is counted once at
its primary chapter, so the 109 families is smaller than the 121
matrix rows above (which restate cross-chapter families in each chapter they
appear).

| Class | Count | Components (primary chapter) |
|---|---|---|
| **clean-room** | 59 | NfpPack2D, CvdLloyd2d, HungarianAssignment (Ch.01); Heightmap packers, Dlbf3dMixedSizePacker, SlabCutByFractures (Ch.02); BlockCutOptSolver, ObbTriangleIntersection+BVH, BlockValueModel, AmrrPlanner+SharedEdgeSlicer, FrahanSawBedSchedule (Ch.03); RadargramProcessor filters, Fft, HilbertEnergy, StoltMigration, GprPresets, Kriging, BedrockSurface, TinMerge, TinPeelFilter (Ch.04); Masonry RBE, AdmmQpSolver, PolygonalWallAssembler, Rubble Wall Settle, Ashlar Pack, Best-Fit Inventory Pack (Ch.05); Arch Voussoirs, Pendentive Vault Voussoirs, Inward-orientation fix (Ch.06); BarycentricMapper2DTo3D, ChartDistortionAnalyzer, ChartFlatnessReport, FaceCornerUvTable, SurfaceHoleNestBridge, MeshObjIO (Ch.07); Boundary segmenter, Segment hash index, Phase correlator, Constrained ICP+Kabsch, Order-preserving DP, Initial transform builder, Horn kernel (Ch.08); Geometric penetration verifier (Ch.09); GCodeParser, GCodeToPlanes, WireSawToolpathAdapter, StoneCutExport, FabricationReport, EnlargeSculpture/FitInBlock, CarvingStages, GeoreferenceMath (Ch.11); StreamingCloudReader+VoxelGridSink, GPR readers, GprFileReader dispatcher (Ch.12); BCOPareto, BCORobust, BCOWatershed, GetData, Analysis tab (Ch.13) |
| **evolved-fork** | 9 | IrregularSheetFillNfpBlf (FreeNestX), ContactNfpHoleNester (CNH), IrregularSheetFillV506 (Ch.01); BlockPackTree (Ch.02); CuttingGrid (I1), BlockCutOptPareto/Omni (I6/I11), RecoveryCascade (Ch.03); FractureExtractor (Ch.04); Soft-ICP refiner (Ch.08) |
| **facade-over-primitives** | 20 | Sheet Pack (Unified), Trencadis Catalog, Trencadis Pack, Trencadis Dynamic, Trencadis EdgeMatch, Trencadis Pipeline (Ch.01); HeteroExt, statue-to-blocks study (Ch.02); FractureBlockPack, guillotine staging, bed-bounded hexahedra frontier (Ch.03); CGAL trim, Voussoir Ingest/Matcher/Pack (Ch.06); PackOnSurface, PackSurfaces (Ch.07); Block Pair Match 3D (Ch.08); MeshRepair/Diagnostics (Ch.10); GPR mesh/picks/loader (Ch.12); VtuOut, Reports tab (Ch.13) |
| **vendored-library** | 5 | NativeNfpKernel (Clipper2) (Ch.01); BFF runtime (Ch.07); Vector fracture readers (NTS), LazCloudReader (laszip.net) (Ch.12) — plus the GPR per-format reader set classed vendored where it delegates to a library |
| **original-research** | 8 | Extraction Order Optimizer (Ch.03); FractureUncertainty (Ch.04); CRA Stability Check, Polygonal Wall Generator (J metric), Stone Cell Match (Λ) (Ch.05); ChartScaleComputer (Ch.07); Projection bootstrap (Ch.08); Kintsugi pose-composition fix (Ch.09) — counted as the original-research wrapper around the direct port |
| **direct-port** | 3 | MeshCsg (csg.js, MIT) (Ch.10); DiffusionScheduler, KintsugiPortInference+modules, Verifier rolled up as Frahan.Kintsugi.Port (PuzzleFusion++, non-commercial research-only) (Ch.08/09) |
| **wrapper-of-native** | 5 | CgalMeshBoolean/CgalGeometry, GeogramMesh, Scan Reconstruct (Ch.10); Load E57 Cloud (Ch.12); Lab native-shim exercisers (Ch.13) — plus Slab CGAL backend (Ch.02) reached the same way |

Note: the original-research count lists the Kintsugi pose-composition fix
(the Frahan-original wrapper) separately from the direct-port Kintsugi network
it sandwiches; the network itself is counted under direct-port. Several
families have a primary class and a secondary character (e.g. the GPR readers
are clean-room per-format but vendored where they delegate to a library, and
Settle 3D is clean-room orchestration over a vendored engine); each is counted
once under the class that carries its principal contribution.

Posture, in one line. Across the fifteen chapters the repository is dominated
by clean-room implementations of published mathematics (59) and facades over
its own primitives (20); the genuinely forked work is small and bounded (9),
each carrying a measured delta over a named baseline; the heavy native
geometry is honestly wrapped, not reimplemented (5 wrapper-of-native, 5
vendored-library); eight components claim originality, each an A-candidate
pending a prior-art sweep, not an asserted novelty; and the three direct ports
are two permissive (the MIT csg.js fallback) and one quarantined non-commercial
research-only assembly (Kintsugi) absent from the default install, so nothing
in the default-install algorithm path is a line-by-line port of a competitor.

---

## Licensing register and mitigations

Every flag raised in the audit, with its current mitigation. The governing
rule: the default install must link no copyleft or non-commercial code; such
obligations are quarantined behind optional native shims, an isolated
research-only assembly, or a data download step.

| # | Flag | Risk | Mitigation / status |
|---|---|---|---|
| 1 | **Root LICENSE GPL-3.0** (RESOLVED 2026-06-15) | The distribution links `Frahan.Kintsugi.Port`; under GPL the combined work is GPL-3.0. | DONE: root LICENSE is now the full canonical GPL-3.0 text. |
| 2 | **Kintsugi / PuzzleFusion++ NON-COMMERCIAL (CRITICAL, E1)** | Upstream LICENSE is research-use-only / non-commercial, NOT plain GPL-3.0. Covers ported C# code AND converted weights `kintsugi.bin` (~255 MB). The whole port (DiffusionScheduler, KintsugiPortInference, encoder/denoiser/VQ-VAE, Verifier) is the sole direct-port in the thesis (Ch. 08/09). | Keep the separately-distributed, optional, non-commercial research-package split outside the default install. Verify root LICENSE, port README, and any repo-root statement all say research-only non-commercial, not plain GPL-3.0. |
| 3 | **jigsaw_matching subtree unaudited (E1/jigsaw)** | Vendored inside PuzzleFusion++ (Jigsaw, Lu et al.); ships its own MIT LICENSE but NOTICE.md states the MIT grant is unaudited against the original repo. | Treated conservatively under the parent non-commercial terms; nothing from it is compiled into or shipped with StonePack. |
| 4 | **CGAL GPL (E3)** | CGAL PMP / Surface_mesh / simplification / reconstruction / straight-skeleton / partition / SDF segmentation / heat geodesics (Ch. 02, 06, 10) are GPL; permanently block any MIT relicense of the geometry path. Depends on GMP (`gmp-10.dll`). | Shim source vendored in `native/cgal_shim/` (corresponding-source gap resolved). Native shims optional at runtime with BSP/MeshCsg fallback; absent from default install. A commercial release would buy the CGAL packages. |
| 5 | **CoACD transitive CGAL-GPL (E5-shim)** | CoACD itself MIT (pin 1.0.11) but internally vendors CGAL (GPL) plus boost/openvdb/spdlog/zlib; GPL re-enters via CoACD (the Bullet settle convex pieces, Ch. 02; the Lab CoACD exerciser, Ch. 13). | Same out-of-process quarantine as CGAL; optional at runtime, reached only through the optional shim with a convex-hull fallback. |
| 6 | **TetGen AGPL (geogram_shim, E6)** | TetGen (needed for volumetric-Voronoi-blocks, Ch. 10/13) is AGPL and ON by default in the geogram CMakeLists. | `-DFRAHAN_WITH_TETGEN=OFF` documented for an AGPL-free build; volumetric blocks unavailable in that config. Triangle kept OFF. |
| 7 | **Clipper2 BSL-1.0 (low)** | `nfp_kernel` vendors official Clipper2 at tag `Clipper2_2.0.1` unmodified (Ch. 01). | Boost Software License 1.0 is permissive, no copyleft. Compatible. Attribution preserved. |
| 8 | **Geogram BSD-3 + bundled PoissonRecon MIT (low)** | Permissive; attribution required (Ch. 04/10). | Kazhdan PoissonRecon bundled as `GEO::PoissonReconstruction` is MIT. Attribution required, no copyleft. |
| 9 | **Bullet / BulletSharp zlib (low)** | Settle 3D (Ch. 02) links Bullet via BulletSharp.x64. | zlib licence is permissive, no copyleft. Attribution preserved; native `libbulletc.dll` ships in `install/plugin/`. |
| 10 | **csg.js MIT direct-port (low)** | `MeshCsg` (Ch. 10) is a line-by-line port of Evan Wallace's csg.js. | MIT permissive; owes a THIRD_PARTY_NOTICES attribution row, no copyleft. |
| 11 | **xBIM CDDL-1.0 vs GPL-3.0 (E2)** | CDDL-1.0 and GPL-3.0 are distribution-incompatible (IFC terminal). | HITL ruling: GeometryGymIFC is the long-term licence-clean path; xBIM stays for now. Mitigation options: out-of-process writer or swap to GeometryGymIFC. |
| 12 | **BFF + numeric stack** (PARTIALLY RESOLVED 2026-06-15) | BFF runtime (Sawhney & Crane 2017, Ch. 07) bundled in dist. | DONE: `THIRD_PARTY_NOTICES.md` exists (BFF, CGAL, GMP, Geogram, CoACD, Bullet, Clipper2, NTS, PuzzleFusion++, csg.js). STILL OWED: SuiteSparse / OpenBLAS / libgfortran rows for the statically-linked BFF exe. |
| 13 | **Vendored readers (NTS, laszip.net, pye57 chain) notices owed** | NetTopologySuite, laszip.net, and the pye57/libE57Format worker chain (Ch. 12) each owe a notices row. | Add per-dependency rows to `THIRD_PARTY_NOTICES.md`; all permissive (BSD-3-style / LGPL-style), no copyleft in the default managed path; the pye57 chain is an external runtime dep absent from the default install. |
| 14 | **Datasets carry their own licences** | ETH1100, Tongjiang, Grimsel GPR, Bondua Botticino GPR, TU1208 GPR are CC-BY; GeoCrack/Open3D Marbles MIT. NON-COMMERCIAL/UNKNOWN: Granite Dells TLS ("Not Provided"), Stanford 3D Scanning (research-only). Example 08 marble GPR is CC-BY-NC-ND (no commercial demo). | Bundled with attribution by maintainer decision (`data/ATTRIBUTION.md`); downstream users must honour each upstream licence. At the public step, large blobs move to Git LFS and a download script fetches non-redistributable sets. |
| 15 | **Reference-register + per-file attribution gap (spec 16)** | Policy requires `docs/index/frahan_reference_register.md` (not found), a THIRD_PARTY_NOTICES row per dependency, and per-file SPDX headers on any copied source (incl. `references/original_gh_2d_packing_plugin` and the csg.js port). | Create the register and per-file attribution before external review per `AGENTS.md` §9. |
| 16 | **Fabricated/stale citations (provenance, not copyleft)** | `BestFitInventoryPacker.cs:26-29` and the Masonry facade `BestFitPackComponent.cs:30` cite a non-existent "Gramazio/Kohler/Eichenhofer 2017 CAD paper" (E5; the Core lineage is correctly Furrer 2017 / Johns 2020); RecoveryCascade self-labelled "novel" without the BoEGE cite (E9); the EdgeMatch `[Algorithm]` at `EdgeMatchSolveComponent.cs:25` names "Phase correlator FFT" while `PhaseCorrelator.cs:29-34` is direct cross-correlation, not an FFT; the Kintsugi GH `[Algorithm]` still reads "Full GPL-3.0 honest port ... underway / NO learned model" (`KintsugiAssemblyComponent.cs:62-68`), describing a pre-port state. | Fix all before any external/academic review under `AGENTS.md` §9. RecoveryCascade header softened to Murugean 2026; E5 (correct to Furrer/Johns), the FFT wording (reword to direct cross-correlation), and the stale Kintsugi attribute still open. |

The architectural quarantine is the load-bearing mitigation. The default
install ships no native DLL and links no GPL, AGPL, or non-commercial code.
CGAL, CoACD, TetGen, and geogram are reached only through optional
out-of-process shims with managed fallbacks. The Kintsugi non-commercial
research package is a separately distributed, isolated assembly absent from
the default install. Permissive dependencies (Clipper2 BSL-1.0, Geogram
BSD-3, Kazhdan PoissonRecon MIT, Bullet zlib, csg.js MIT, NTS BSD-3,
laszip.net) remain in the default path and require only attribution.
