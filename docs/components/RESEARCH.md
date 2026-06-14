# Frahan StonePack - Research

`v0.1.0-alpha` research preview. Repository: [github.com/libishm1/Frahan](https://github.com/libishm1/Frahan). Cite via `CITATION.cff` (Zenodo DOI minted for this release).

This page is the research-grade summary of the project. It is source material for the public wiki and website. Every quantitative claim below names the file it came from. Numbers are machine-measured (test suite, headless harness, or live solves), not estimated. Where a number is uncertain or domain-bounded, it says so.

## 1. Method: research-grade by construction

Frahan StonePack is a Rhino 8 / Grasshopper plugin for stone-fabrication readiness. It is the bridge layer between design intent and machine-ready fabrication for dimension stone, monuments, and dry-stone masonry. It covers one pipeline: GPR / scan to fracture mapping and point-cloud discontinuity / joint-set extraction, then 3D reconstruction, discrete fracture network (DFN), block packing and cutting, masonry equilibrium, and fabrication export.

Every kept algorithm clears four bars. The framework name is **V4: PRISMA + SLM + ROSES**.

- **Measured benchmark.** Each algorithm is a measured delta over a named baseline, and the baseline is a shipping implementation, not a paper abstraction. Each delta is benchmarked on the same instances before and after (`docs/thesis/chapters/15_evolution.md`, lines 5-8). The truth criterion is live visual validation, not a self-reported number (`outputs/2026-06-03/RESEARCH_MATH_WORKFLOW_V2.md`, cited at `15_evolution.md:11`).
- **SLM tier: math derivation.** Each subsystem carries its algebra in the matching thesis chapter, with named external citations and `[Algorithm]` attribute hover text on the Grasshopper component. The SLM cards live in `wiki/research/slm_cards/`.
- **PRISMA tier: statistics review.** Bounded to head-to-head benchmarks and the five-reviewer re-review at submission for the companion paper. PRISMA is scoped to the benchmarked comparisons, not asserted across the whole library.
- **ROSES tier: interdisciplinary synthesis.** The cross-cutting numeric-hygiene finding is one lever: recenter before computing, use a scale-relative epsilon, route booleans through a robust kernel (`ROSES_top10_fabrication_synthesis.md` section 6, cited at `15_evolution.md:30-32`).

Originality is classified, not asserted. The originality matrix (`docs/thesis/90_originality.md`) classifies the component families into seven classes: clean-room (59), facade-over-primitives (20), evolved-fork (9), original-research (8, each an A-candidate pending a prior-art sweep), wrapper-of-native (5), vendored-library (5), and direct-port (3). Nothing in the default-install algorithm path is a line-by-line port of a competitor. The default install ships no native DLL and links no GPL, AGPL, or non-commercial code; the learned Kintsugi module (non-commercial, research-only) is quarantined out of the default install.

## 2. Research areas

### (a) 2D nesting and hole-aware NFP

**Problem.** Place irregular 2D parts into a sheet, which may carry defect holes, with zero overlap and high material utilisation, and nest small parts inside the holes of larger placed parts to cut saw-able stock around defects.

**Method.** Exact No-Fit-Polygon Bottom-Left-Fill on a Clipper2 integer-snapped back end. The no-fit polygon is the Minkowski sum `NFP(A,B) = A (+) (-B)`; the inner-fit polygon is the matching Minkowski erosion; the feasible set is the sheet IFP minus the union of NFPs against placed parts and sheet holes; placement is the bottom-left vertex of that exact feasible region, so the layout is 0-overlap by construction and deterministic. Citations: NFP and IFP from Bennell and Oliveira (2009, JORS, DOI 10.1057/jors.2008.169); Bottom-Left-Fill from Burke, Hellier, Kendall, Whitwell (2006, Operations Research, DOI 10.1287/opre.1060.0293) and Burke et al. (2007, EJOR, DOI 10.1016/j.ejor.2006.03.011); Clipper2 (Johnson, BSL-1.0) for the booleans. The hole-aware evolution (ContactNfpHoleNester / HoleNest) adds contact-adaptive rotations, a part-in-part-hole IFP phase, and an axis-aligned rectangle shelf fast-path. Trencadis mosaicing uses centroidal Voronoi cells with Lloyd relaxation (Lloyd 1982, DOI 10.1109/TIT.1982.1056489) and one-to-one Hungarian assignment (Kuhn 1955, DOI 10.1002/nav.3800020109).

**Key results.**
- Evolved NFP-BLF cuts mean wasted area by 53.9% versus the V506 overlap-then-trim baseline at zero overlap (`docs/thesis/chapters/01_two-d-nesting.md`, citing `IrregularSheetFillNfpBlf.cs:21-22`).
- It is the only 0-overlap packer in the study to cross 80% stock utilisation with holes: 82.0% oversubscribed, 84.7% L-plus-hole, 89.6% hard 3-hole (`wiki/research/packing/PACK2D_STUDY_REPORT.md` section 0a).
- Hole-aware head-to-head, true-hole instance: HoleNest places 12/12 and fills 4 part-holes, valid and deterministic, where the Sparrow / OpenNest reference physics nester packs outlines only, fills 0 holes, and is structurally invalid on the same input (`docs/benchmarks/HOLE_PACKER_MATH_AND_BENCHMARK.md` section 2a). On the regenerated 2026-06-13 head-to-head, HoleNest wins the rect fast-path (5.2 ms vs 71 ms) and the tight density contest (0.74 vs 0.56; 12 vs 9 placed), matches validity everywhere, and trades slower general-irregular NFP time (`PACK2D_STUDY_REPORT.md` section 3a). The 146x figure is the rect-fast-path-vs-native-shelf ratio (0.148 ms vs 21.6 ms) on the specific true-hole instance, not an all-rectangle benchmark (`HOLE_PACKER_MATH_AND_BENCHMARK.md` section 3b).

### (b) 3D block packing

**Problem.** Pack 3D items into containers under varying constraints: heightmap stacking, saw-cuttable guillotine cuts, revenue-weighted mixed sizing, or physical settling into a stable non-interpenetrating pile.

**Method.** A family of packers sharing the heightmap deepest-bottom-left core. The guillotine forest packer uses a binary-tree guillotine partition with seeded forest growth (Kim 2025, Computation, DOI 10.3390/computation13090211) and a cut-surface cost term (Jalalian et al. 2023, Scientific Reports, DOI 10.1038/s41598-023-49633-w). The mixed-size revenue packer is a clean-room deepest-left-bottom-fill (Chehrazad, Roose, Wauters 2025, IJPR, DOI 10.1080/00207543.2025.2478434). Physics settling uses Bullet sequential-impulse rigid-body dynamics (via BulletSharp) with CoACD convex decomposition (Wei et al. 2022, ACM TOG, DOI 10.1145/3528223.3530103) and a centre-of-mass-over-support stability test (Heyman 1966, DOI 10.1016/0020-7683(66)90018-7).

**Key results.**
- DLBF with best-of-orientation reaches 70.4% volumetric fill versus a 66.4% baseline, 28 vs 27 pieces, 0 overlap, deterministic (`wiki/research/packing/PACK3D_STUDY_REPORT.md` section 3).
- Heightmap-to-settle evolution moved compactness from 17.0% (AABB greedy, 19 of 30 stones) to 33.2% (mesh-accurate plus drop-settle, 30 of 30 stones), 80% COM-stable rising to 100% with the stability-seat pass (`02_three-d-packing.md`, citing `outputs/2026-06-03/pack3d_evolution/PLAN.md` section 5).
- Statue-to-blocks decomposition recovers a volume ratio of exactly 1.0000 (5.4009 m3 statue equals 5.4009 m3 of 113 blocks) over 173 CGAL booleans with zero failures (`02_three-d-packing.md`, citing `examples/15_statue_to_blocks/README.md`).

Volume ratios are not comparable across domains. Guillotine yield pays fill for full saw-separability; the mesh-honest container number and the guillotine number measure different things (`PACK3D_STUDY_REPORT.md` section 5).

### (c) BlockCutOpt, recovery cascade, and wire-saw guillotine

**Problem.** Maximise the count and value of marketable fixed-size dimension-stone blocks cuttable from a fractured quarry bench, by optimising the cutting-lattice pose to avoid fractures, recover value from fracture-crossed blocks at finer scales, and render the result as a fabricable straight-saw guillotine plan.

**Method.** A brute-force 5-D pose-grid argmax (yaw, two tilts, two offsets) maximising the non-intersected block count, a clean-room reimplementation of Elkarmoty et al. (2020, Resources Policy, DOI 10.1016/j.resourpol.2020.101761) extended from yaw-only to full-3D tilt. The intersection predicate is a 13-axis Separating Axis Theorem test (Akenine-Moller 2001) with a triangle-AABB BVH. RecoveryCascade is a multi-scale reject-coarse / recover-fine recursion that reduces exactly to BlockCutOpt at scale 1, grounded in Yarahmadi et al. (2018, Engineering Geology, DOI 10.1016/j.enggeo.2017.11.006), Cherri et al. (2009, EJOR, DOI 10.1016/j.ejor.2008.04.039), and Gilmore and Gomory (1965, Operations Research, DOI 10.1287/opre.13.1.94). A Pareto front spans recovery, revenue, kerf-time, and the BCSdbBV cut-cost objective (Jalalian et al. 2023). The staged guillotine plan uses LPT list scheduling (Graham 1969).

**Key results.**
- Mode 5 staged wire-saw guillotine recovery: 49.3% yield at 100% saw-separable, versus mode 4 voxel-DLBF at 53.3% yield but not saw-cuttable (`docs/results/RESULTS.md` hero figure).
- RecoveryCascade recovers +21% over single-scale BlockCutOpt by re-cutting cracked blocks at finer scales (`docs/results/RESULTS.md`); the study table reports RecoveryCascade 15.2% recovered / 11 blocks vs BlockCutOptSolver 12.5% / 4 blocks on its input (`PACK3D_STUDY_REPORT.md` section 2).
- Real Botticino marble bench (example 08): 280 GPR fracture picks cluster into three dipping beds; the oblique bed-following plan recovers +11.90 m3 (+59%) and +$11,287 net per ~50 m3 bench over the flat orthogonal plan (`03_quarry-blockcut.md`, citing `georeferencing_prize_at_max_cost`). Marble GPR data is research-only licensed.

Flag: RecoveryCascade is Core-validated but has no Grasshopper consumer; the shipped FractureBlockPack component runs a self-contained recovery engine instead (silent-disagreement risk, roadmap high priority, `91_roadmap.md`).

### (d) Point-cloud discontinuity, joint sets, and DFN

**Problem.** Turn a raw UAV or TLS point cloud of a rock face into planar facets, cluster their poles into joint sets, and report normal spacing and block size, so a quarry can orient cuts away from dominant joint sets.

**Method.** An out-of-process, clean-room worker: PLY cloud to counting-sort CSR uniform grid, PCA normals plus surface variation (Pauly 2002), FACETS planar region-grow (Dewez, Girardeau-Montaut et al. 2016, qFacets), Watson axial mean-shift joint-set clustering (Riquelme et al. 2014/2015, DSE), then family-constrained normal spacing. The spatial structure follows Hoetzlein 2014 fixed-radius nearest-neighbour cell lists. Joint-set and DFN theory rests on Priest (1993, Discontinuity analysis for rock engineering) and the Baecher / Veneziano / Levy-Lee / Priest joint-generator family. Fisher-distribution joint-orientation scatter drives the Monte-Carlo robustness node. No CloudCompare, CCCoreLib, or qFACETS GPL source is read or copied; those tools are black-box benchmark baselines only.

**Key results.**
- Apples-to-apples k=24 PCA normals on the 7,858,334-point Tongjiang quarry-face cloud: ~10.2 s, about 1.4x faster than Open3D's KD-tree (14.8 s) at the same neighbourhood; whole discontinuity pipeline ~14.7 s (`native/discontinuity_worker/README.md`, lines 20-25). CloudCompare's octree at radius 0.5 m took 2127 s, but that radius spans thousands of neighbours per point on this 8 mm cloud, a much larger neighbourhood than k=24, so it is a scale reference, not a like-for-like speedup. Any earlier "215x / 265x vs CloudCompare" framing is retracted.
- Live validation on Tongjiang `detail_cloudXB.ply` (7,858,334 points): 4 joint sets (dip 19/79/72/84, spacing 0.25/0.23/0.67/1.11 m), Jv = 7.20 joints/m3, RQD = 92, Vb = 0.352 m3, Deq = 0.71 m (`docs/validation/discontinuity_ingest_card/VALIDATION_REPORT.md`). Shipped components: Discontinuity Sets (Async) GUID D5F10048, Discontinuity Ingest D5F10049, Stereonet + Block Size D5F1004A.

This area is implemented, benchmarked, live-validated, and written up in thesis chapter 03b (`docs/thesis/chapters/03b_discontinuity-dfn.md`); the worker README and validation report are the primary sources.

### (e) GPR fracture mapping

**Problem.** Turn a raw GPR B-scan into a fracture and cavity map, then into a quantified, uncertainty-bounded keep-out volume of intact rock, in dependency-light managed code.

**Method.** Constant-velocity time-to-depth conversion, then a processing chain of dewow, background removal, time-zero mute, time gain, Stolt f-k migration, Hilbert instantaneous energy, and depth equalisation. The FFT is radix-2 Cooley-Tukey (1965, DOI 10.1090/S0025-5718-1965-0178586-1) with a Bluestein chirp-z fallback. Instantaneous energy uses the Hilbert envelope (Taner, Koehler, Sheriff 1979, Geophysics, DOI 10.1190/1.1440994). Migration is Stolt (1978, Geophysics, DOI 10.1190/1.1440826) in the exploding-reflector model with an added cosine dip-taper. Fracture extraction combines a high-energy quantile rule with the USGS Mirror Lake lateral-continuity criterion (WRIR 99-4018C; Porsani 2006; Isakova 2021), evolved into a dip-aware shear-count filter. Surface fitting uses screened-Poisson reconstruction (Kazhdan and Hoppe 2013, ACM TOG, DOI 10.1145/2487228.2487237) with a CGAL advancing-front fallback. Uncertainty composes reconstruction, interpolation, and mesh terms, with kriging posteriors (Cressie 1993; Rasmussen and Williams 2006) and a Fresnel-zone detection probability (Molron et al. 2020; Dorn et al. 2012).

**Key results.**
- Granite spine, real Grimsel ISC data (MALA GX160, CC-BY-4.0): the granite_160 preset extracts 1472 picks on the AU tunnel and 1485 on VE, at dt = 0.4464 ns and dx = 0.0498 m (`04_gpr-fracture.md`, citing `examples/03_gpr_fracture_granite/README.md:14`).
- Loviisa surface fractures, real ESRI Shapefile (Chudasama 2022, CC-BY-4.0): the reader returns 708 traces / 6483 vertices / 1593.5 m total length in EPSG:3067, with two conjugate sets peaking near 15 deg (NNE) and 105-120 deg (ESE) (`04_gpr-fracture.md`, citing `examples/26_loviisa_surface_fractures/README.md:26-30`).
- The error-function approximation (Abramowitz-Stegun 7.1.26) holds |error| < 1.5e-7 (`04_gpr-fracture.md`).

### (f) Masonry CRA equilibrium and Lambda / J metrics

**Problem.** Decide whether a dry masonry assembly stands under self-weight, and measure how much an inventory of found stones must be carved to fit a target wall. Force-only equilibrium admits physically unrealisable states, so a kinematics-coupled check is needed.

**Method.** Rigid-Block Equilibrium (RBE) builds the equilibrium matrix over contact-vertex forces with Coulomb friction linearised to a K-face pyramid, solved as a convex QP; the inscribed-pyramid correction sets the effective friction to `mu*cos(pi/K)` so the pyramid is inscribed, not circumscribed. Coupled Rigid-Block Analysis (CRA) couples statics with virtual rigid-body kinematics, a clean-room transcription of Kao et al. (2022, CAD 146:103216) with the MIT compas_cra as the structural model. The repository does not ship IPOPT; it uses an alternating convex certificate search, sound in the certifying direction. The interlock metric J (original) penalises aligned running joints, formalising Clifford and McGee (2018). The Lambda imposition metric (original) is the volume-weighted fraction of each found stone carved away to fit its cell, assigned via the Hungarian solver over a voxel symmetric-difference cost; the Cyclopean Cannibalism wall at Lambda ~ 0.27 is the datum. Voussoir and stereotomy generators (chapter 06) rest on Frezier (1737), Monge (1798), Hooke (1675), Heyman (1966), and Rippmann and Block (2011).

**Key results.**
- CRA versus compas_cra parity: 5/5 on exact ports of their parametric doc examples, including the H-model where RBE accepts and CRA correctly rejects (`docs/benchmarks/CRA_COMPAS_PARITY.md`). The "50x / 470x faster" figures on the cube and stack fixtures are wall-time per call including IPOPT process spawn and NL file I/O, not a solver-algorithm gap; their RBE actually beats ours on the arch (102-123 ms vs our 159 ms). No "faster than" claim is made for RBE.
- Lambda on 60 real ETH1100 stones: the Hungarian shape-aware matcher lands Lambda 0.18-0.23 across the Coursing continuum, versus volume-greedy and random at ~0.59-0.64 (about 2.9x), and beats the Cyclopean datum of 0.27 (`docs/benchmarks/LAMBDA_STUDY.md`). The flagship single-instance figure is Lambda = 0.194 (`05_masonry-cra.md`, citing `StoneCellAssignmentEthBenchmarkTests.cs:14,95`). Baselines are honest-weak; they place by centroid translation without rotation search.
- Voussoir coverage: arch example 21, 11/11 real rubble trims, 94.9% coverage; pendentive vault example 22, 36/36 trims, 98.3% coverage (`06_voussoir-stereotomy.md`, citing `21_arch_metrics.json`, `22_vault_metrics.json`).

The ADMM QP cold-start convergence degrades past about 50 contact interfaces (54-interface wall 5.4 s, 147-interface 86 s; `05_masonry-cra.md`, roadmap high priority).

### (g) Edge-matching

**Problem.** Fracture reassembly: recover the rigid motion that brings every mating boundary of broken pieces back into contact without interpenetration.

**Method.** A five-stage deterministic pipeline: a boundary segmenter on the signed-turning-angle signature (Arkin et al. 1991), a rotation/translation-invariant segment hash index, a coarse phase correlator, a constrained ICP (Besl and McKay 1992) with per-iteration orthogonal-Procrustes / Kabsch SVD fit (Kabsch 1976; reflection guard after Umeyama 1991), and a frame-anchored beam search assembler. Opt-in increments include order-preserving correspondence DP, a Soft-ICP refiner folding Coherent Point Drift (Myronenko and Song 2010) with a non-penetration hinge, a projection bootstrap (per-facet PCA plane fit, match in 2D, lift and verify in 3D), and a Horn quaternion absolute-orientation kernel (Horn 1987). Trencadis uses centroidal Voronoi plus Hungarian assignment.

**Key results.**
- R1 partial matching plus R2 global non-overlap drove measured 2D overlap from 12-25% to 0%, with 8/8 placed and 100% union coverage (`08_edge-matching.md`, roadmap 2026-05-25).
- Projection bootstrap on a 6-shard fixture produced 67 cross-panel 2D matches where the prior path had 0, lifted and 3D-verified 25 pairs, and placed 6/6 fragments via an agglomerative spanning tree, with the honest caveat that only 2 of 5 tree interfaces were in full contact (`08_edge-matching.md`, citing `ProjectionPairFinder.cs:16-22`).
- The tessellation-invariant production 3D path is the learned Kintsugi Port (see below).

### (h) Kintsugi learned 6-DoF reassembly

**Problem.** Recover each fragment's rigid 6-DoF pose so broken fragments snap back into the original solid, including the smooth fracture surfaces where the geometric edge-matcher finds no rim correspondences.

**Method.** `Frahan.Kintsugi.Port` is a managed C# direct port of PuzzleFusion++ (Wang, Chen and Furukawa 2025, ICLR; arXiv:2406.00259): a PointNet++ encoder (Qi et al. 2017), a VQ-VAE latent (van den Oord et al. 2017), an SE(3) diffusion denoiser (DDPM after Ho, Jain, Abbeel 2020, with the PuzzleFusion++ piecewise-quadratic schedule), a 6-block AdaLN-conditioned transformer (Vaswani et al. 2017; DiT conditioning after Peebles and Xie 2023), and a learned pairwise verifier with a 0.5 acceptance gate. Trained on Breaking Bad (Sellan et al. 2022). The pose-composition fix (Frahan-original, "norm-undo") composes unnormalise, network, and normalise transforms with deliberately mixed indices and an identity-pinned anchor.

**Key results.**
- Example 14 live HITL: two Breaking Bad parity fragments reassembled at verifier pair score 0.7068 (STRONG, above the 0.5 gate), zero unplaced, 20 diffusion steps (`09_kintsugi-pose.md`, citing `examples/14_kintsugi/README.md`).
- The geometric-only path places only 1 of 6 fragments on a smooth Voronoi sphere shatter, which is what motivates the learned path (same source).
- The pure-C# denoiser drifts about 3-5% from libtorch kernels (`KintsugiPortInference.cs:74-81`). Compute budget: F=10, S=20 ~ 660 s by design (GPU diffusion).

License-critical: the port and its converted weight `kintsugi.bin` (~255 MB) are non-commercial research-only, not plain GPL-3.0. It is quarantined in a separate assembly and absent from the default install.

### (i) Surface packing and BFF

**Problem.** Flatten a curved 3D stone surface to a planar chart, pack 2D parts into the chart, and lift them back onto the surface so a flat-cut tile field follows the surface and butts edge to edge in 3D.

**Method.** A ten-step pipeline with the load-bearing invariant that flat face i corresponds exactly to surface face i. Conformal flattening is Boundary First Flattening (Sawhney and Crane 2017, ACM TOG 36(4):109, DOI 10.1145/3072959.3056432), shipped as an external static single-exe, not reimplemented. Chart-scale recovery uses a single isotropic length ratio; an edge-stretch distortion metric warns outside [0.85, 1.15]. A seam-correct flat mesh keys UVs on (faceIndex, cornerIndex) and emits an unwelded mesh so seams never bridge. The inverse map is plain triangle barycentric interpolation via Cramer's rule (the `[Algorithm]` attribute cites Floater 2003 Mean Value Coordinates as background, but the SLM card confirms no MVC code is present). The packing engine is the hole-aware ContactNfpHoleNester, with chart inner naked edges as sheet holes.

**Key results.**
- Example 13 live-validated: a twisted monument (130 deg over its height) split by CGAL dihedral segmentation into 6 regions, then a 176-shard Trencadis mosaic mapped onto the curved surface through the BFF chart and barycentric inverse map (`07_surface-packing.md`; `examples/13_surface_mapping/`).
- The static BFF build folds 17 third-party DLLs into one 38 MB self-contained exe with output byte-identical to the upstream dynamic build (`install/tools/BFF-BUILD-STATIC.md`).

The inverse-map scan is linear O(P*F), author-stated acceptable below about 2000 faces (`BarycentricMapper2DTo3D.cs:107`).

### (j) Mesh and reconstruction

**Problem.** Turn raw scan point clouds and dirty meshes into clean, watertight, boolean-ready geometry, and do the heavy native geometry without crashing Rhino.

**Method.** CGAL corefinement booleans (the COMPAS_CGAL pattern; PMP after Botsch et al. 2010), memoryless edge-collapse decimation (Lindstrom and Turk 1998), Shape Diameter Function graph-cut segmentation (Shapira et al. 2008), and three reconstruction modes: alpha shape (Edelsbrunner and Mucke 1994), screened Poisson (Kazhdan and Hoppe 2013, primary backend Geogram's bundled Kazhdan PoissonRecon), and advancing front (Cohen-Steiner and Da). Geogram (Levy, INRIA/ALICE, BSD-3) and CGAL are vendored libraries; the repository contributions are recenter conditioning, out-of-process crash isolation, binary IPC, the async Run-gate, and mesh-soup cleanup. The managed fallback `MeshCsg` is a direct port of csg.js (Evan Wallace, MIT).

**Key results.**
- Advancing-front reconstruction in an out-of-process worker: 59,971 verts / 111,973 tris in 3.9 s (`12_ingestion.md`).
- Statue-to-blocks recovered-volume identity rho = 1.0000 (`14_workflow-architecture.md` section 14.4).

## 3. Benchmarks at a glance

| Area | Headline measured result | Source file |
|---|---|---|
| 2D nesting | 53.9% mean wasted-area cut vs V506 at 0 overlap | `01_two-d-nesting.md` / `IrregularSheetFillNfpBlf.cs:21-22` |
| 2D nesting | Only 0-overlap packer over 80% util with holes (82.0 / 84.7 / 89.6%) | `PACK2D_STUDY_REPORT.md` section 0a |
| Hole-aware NFP | HoleNest 12/12 placed, 4 holes filled, valid + deterministic; Sparrow invalid on same input | `HOLE_PACKER_MATH_AND_BENCHMARK.md` section 2a |
| Hole-aware NFP | Rect fast-path 5.2 ms vs 71 ms; tight density 0.74 vs 0.56 (12 vs 9) | `PACK2D_STUDY_REPORT.md` section 3a |
| 3D packing | DLBF best-of-orientation 70.4% fill vs 66.4% baseline | `PACK3D_STUDY_REPORT.md` section 3 |
| 3D packing | Compactness 17.0% to 33.2%, 19/30 to 30/30 stones | `02_three-d-packing.md` / `pack3d_evolution/PLAN.md` |
| BlockCutOpt | Mode 5 guillotine 49.3% yield at 100% saw-separable | `docs/results/RESULTS.md` |
| Recovery cascade | +21% recovery over single-scale BlockCutOpt | `docs/results/RESULTS.md` |
| Quarry georeference | Oblique bed-following +59% / +$11,287 net per ~50 m3 | `03_quarry-blockcut.md` |
| Discontinuity worker | k=24 normals ~10.2 s, ~1.4x faster than Open3D KD-tree (14.8 s) | `native/discontinuity_worker/README.md:20-25` |
| Discontinuity | Tongjiang 7.86M pts, 4 joint sets, Jv 7.20, Vb 0.352 m3 | `docs/validation/discontinuity_ingest_card/VALIDATION_REPORT.md` |
| GPR fracture | Grimsel granite: 1472 / 1485 picks (AU / VE) | `04_gpr-fracture.md` / `03_gpr_fracture_granite/README.md:14` |
| Masonry CRA | 5/5 parity vs compas_cra incl. H-model rejection | `CRA_COMPAS_PARITY.md` |
| Masonry Lambda | Hungarian 0.18-0.23 vs greedy/random 0.59-0.64 (~2.9x), datum 0.27 | `LAMBDA_STUDY.md` |
| Edge-matching | Overlap 12-25% to 0%, 8/8 placed, 100% coverage | `08_edge-matching.md` |
| Kintsugi | Verifier pair score 0.7068 STRONG, 2/2 placed | `09_kintsugi-pose.md` / `14_kintsugi/README.md` |
| Surface packing | 130 deg twist, 6 regions, 176-shard mosaic, live-validated | `07_surface-packing.md` |
| Reconstruction | 59,971 v / 111,973 t in 3.9 s, out-of-process | `12_ingestion.md` |
| Test battery | 1034 PASS / 0 FAIL / 147 SKIP (2026-06-14) | `14_workflow-architecture.md:401`, `RESULTS.md` |

Ratios are not cross-comparable across domains; guillotine yield, mesh-honest container fill, and recovery percentages measure different quantities. The CRA wall-time multipliers include out-of-process IPOPT spawn and are not solver-algorithm gaps.

## 4. Datasets

Real datasets drive the benchmarks. Full provenance, DOIs, and licences are in `data/ATTRIBUTION.md`; access and mirrors in `data/DATA_ACCESS.md`.

| Dataset | Source / DOI | Licence | Used by |
|---|---|---|---|
| ETH1100 dry-stone (1100 closed meshes + labels) | Zenodo 10038881 (Johns et al., ETH Zurich) | CC-BY-4.0 | 2D/3D packing, masonry Lambda, Mesh Bench |
| Tongjiang limestone quarry UAV clouds | Zenodo 10.5281/zenodo.15614501 | CC-BY-4.0 | discontinuity, reconstruction |
| Granite Dells TLS rock-face | DOI 10.5069/G9Z60KZ8 (OpenTopography OT.122010.26912.1) | Not Provided (attribution bundled) | scan ingest, discontinuity |
| Stanford 3D Scanning Repository (bunny, dragon, buddha, armadillo) | graphics.stanford.edu/data/3Dscanrep | research-only | recon smoke tests, carving |
| Grimsel ISC GPR (MALA GX160, granite) | DOI 10.3929/ethz-b-000420930 | CC-BY-4.0 | granite GPR fracture extraction |
| Bondua et al. Botticino marble GPR grids | DOI 10.17632/w26n6nftxs.3 (MDPI Data 10.3390/data9030042) | CC-BY-4.0 | marble GPR, block-cut |
| TU1208 IFSTTAR multi-rock GPR | Zenodo 10.5281/zenodo.1211173 | CC-BY | GPR reader validation |
| Loviisa rapakivi-granite fracture traces | Zenodo 10.5281/zenodo.7077494 (Chudasama 2022) | CC-BY-4.0 | surface fracture example (in-git) |
| Finestrat gypsum rock slope | Zenodo 7576524 (Riquelme/DSE) | CC-BY-4.0 | complete-face discontinuity demo |
| GeoCrack fracture patches (11 sites, 12158 patches) | Dataverse (GeoFractNet / GeoCrack) | MIT | fracture digitisation |
| Breaking Bad | Sellan et al. 2022 | dataset terms | Kintsugi parity |

The repository code is GPL-3.0. Datasets carry their own upstream licences, independent of the code licence; downstream users must honour each upstream licence. Total bundle is ~6.3 GB, hosted on Google Drive with original public source links; only the small Loviisa shapefiles live in-git.

## 5. How to cite and reproduce

**Cite.** Use `CITATION.cff` (Libish Murugesan, ORCID 0009-0004-3238-4202; GPL-3.0-only; v0.1.0-alpha; a Zenodo DOI is minted for this release). The companion paper is submitted to the Bulletin of Engineering Geology and the Environment (Zenodo DOI 10.5281/zenodo.20608279).

**Reproduce.**
- Build: `dotnet build src/Frahan.StonePack.GH/Frahan.StonePack.GH.csproj -c Release` (net48). See `docs/INSTALL.md`.
- Test: `tests/Frahan.StonePack.Tests` (1034 PASS / 0 FAIL / 147 SKIP as of 2026-06-14, from a clean clone; skips are Rhino-runtime and optional-dataset gates).
- Benchmarks: `tools/Frahan.StonePack.Harness --packbench` and `--pack2dstudy` regenerate the packing study figures. The discontinuity worker builds from `native/discontinuity_worker/build_mingw.sh`.
- Study reports: `wiki/research/packing/` (PACK2D_STUDY_REPORT, PACK3D_STUDY_REPORT, ROSES_2D_PACKER_GUIDE, SYNTHESIS_2D/3D/BEYOND_BLF), `docs/benchmarks/` (HOLE_PACKER_MATH_AND_BENCHMARK, LAMBDA_STUDY, CRA_COMPAS_PARITY), and the spine thesis `docs/STONEPACK_THESIS.md` with per-chapter math in `docs/thesis/chapters/`.
- Originality and licence registers: `docs/thesis/90_originality.md`, `NOTICE.md`, `THIRD_PARTY_NOTICES.md`.

Truth criterion for this project is visual validation in Rhino / Grasshopper. A green test gate verifies code-level invariants; the example workflows under `examples/` are live-built, captured at correct physical scale, and reproduce on cold reopen.
