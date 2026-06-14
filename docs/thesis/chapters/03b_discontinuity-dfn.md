# 03b. Point-Cloud Discontinuity Extraction, Joint Sets, and the DFN

## 3b.0 Scope and lineage

Chapter 03 optimises a cutting lattice against a discrete fracture network (DFN) that it takes as given. This chapter builds that network from the upstream evidence: a raw rock-face point cloud. The subsystem turns an unstructured UAV or terrestrial-laser-scan (TLS) cloud into planar facets, clusters their poles into joint sets, reports normal spacing and a Palmstrom block-size estimate, and emits a DFN that feeds the block-cut optimiser of chapter 03. It is the geological front end of the quarry pipeline.

The subsystem is implemented, benchmarked, and live-validated on real clouds. The authoritative sources are the clean-room worker (`native/discontinuity_worker/`), its README, and the validation report (`docs/validation/discontinuity_ingest_card/VALIDATION_REPORT.md`). Where a number in this chapter is uncertain or scale-dependent, it says so.

The lineage is conventional structural geology, specifically the joint-set-to-DFN chain codified by Priest (1993) and the ISRM Suggested Methods [R47], implemented as clean-room code. The pole-clustering follows the mean-shift idea of Riquelme et al. (2014, the DSE method); the facet extraction follows the region-grow idea of Dewez et al. (2016, qFacets); the per-point geometry follows Pauly et al. (2002). The stochastic finite-disc DFN rests on the Baecher / Veneziano / Levy-Lee / Priest joint-generator family [R162]. None of these baselines contributes source code. CloudCompare, CCCoreLib, and qFACETS are GPL-licensed and were treated as black-box benchmark baselines only; no GPL source was read or copied. The math is derived from the published papers and from first principles, so the C++ owes nothing to the expression of any GPL tool (`native/discontinuity_worker/README.md`).

The pipeline has five Grasshopper front ends, all in the `Frahan > Quarry` tab:

- **Discontinuity Sets (Async)**, GUID D5F10048 (`DiscontinuitySetsAsyncComponent.cs`) - runs the worker on a cloud.
- **Discontinuity Ingest**, GUID D5F10049 (`Quarry/DiscontinuityIngestComponent.cs`) - reads measured orientations.
- **Stereonet + Block Size**, GUID D5F1004A (`Quarry/StereonetBlockSizeComponent.cs`) - projects and reports.
- **Joint Sets to DFN**, GUID D5F1004B (`Quarry/JointSetsToDfnComponent.cs`) - deterministic infinite-plane bridge.
- **Stochastic DFN (Baecher)**, GUID D5F1004C (`Quarry/StochasticDfnComponent.cs`) - finite-disc realisations.

## 3b.1 The clean-room worker: CSR grid, PCA normals, surface variation

The worker is an out-of-process native executable (`native/discontinuity_worker/frahan_discontinuity_worker.cpp`). It runs outside Rhino so a crash on a malformed 10 M-point cloud cannot take the canvas down with it. It reads a float32 PLY, writes `discontinuity.json` (the joint sets), `facets.csv` (the facet poles), and an optional segmented PLY, and returns. The async component D5F10048 launches it and surfaces the results, with a synchronous fallback for headless testing.

The cloud is $P = \{p_i \in \mathbb{R}^3\}$, $i = 1..N$. Coordinates are shifted by $p_i \leftarrow p_i - p_{\min}$ to a local origin, so single-precision float carries about $10^{-7}$ of the extent, sub-millimetre on a 50 m face. Unit normals are folded to the lower hemisphere, $n_z \le 0$. Poles are axial, undirected, so $n \equiv -n$ and the lower-hemisphere representative is canonical.

**Per-point normal.** For each point the worker gathers a neighbourhood $N_i$ (the $k$ nearest points, default $k = 24$), forms the centroid $c$, and accumulates the covariance

$$C = \sum_{q \in N_i} (q - c)(q - c)^\top \in \mathbb{R}^{3 \times 3}, \quad \text{symmetric PSD}.$$

With eigenpairs $\lambda_0 \le \lambda_1 \le \lambda_2$, the normal is the direction of least variance, $n = v_0$. This is total least squares: the orthogonal-distance best-fit plane is exactly the smallest-eigenvector problem, so PCA *is* the optimal plane and no separate fit is needed. The eigensolve uses an analytic closed-form 3x3 symmetric solver, not a Jacobi loop.

**Surface variation.** Pauly et al. (2002) define the planarity proxy

$$\sigma = \frac{\lambda_0}{\lambda_0 + \lambda_1 + \lambda_2} \in [0, \tfrac{1}{3}].$$

It goes to $0$ on a perfect plane and to $1/3$ when the neighbourhood is isotropic. Because the trace identity gives $\lambda_0 + \lambda_1 + \lambda_2 = \mathrm{trace}(C) = \sum \lVert q - c \rVert^2$, $\sigma$ is scale-free. It rejects non-planar seeds and orders the region-grow seeds, flattest first.

**The neighbour search is the bottleneck, and the lever.** For $k \approx 24$ the covariance is about 150 floating-point operations per point, trivial. The cost is the neighbour gather and its cache behaviour. A first hash-map grid (`unordered_map<cell, vector>`) scattered each cell across the heap, one cache miss per neighbour, and reached only about 3x OpenMP scaling on 16 cores: memory-bandwidth bound. The shipped worker replaces it with a counting-sort CSR uniform grid after Hoetzlein (2014, fixed-radius nearest-neighbour cell lists from the SPH and molecular-dynamics literature):

1. Hash each point to a cell, $\mathrm{cell}(p) = (\lfloor (x-x_0)/r \rfloor, \lfloor (y-y_0)/r \rfloor, \lfloor (z-z_0)/r \rfloor)$, linearised to one index.
2. Counting-sort into CSR: count points per cell, prefix-sum to `cellStart[]`, scatter point ids to `sortedIdx[]`. Build is $O(N)$ with no comparisons.
3. A cell's points are contiguous in `sortedIdx`, so a cell scan is cache-coherent.
4. A query scans the 27 cells around the home cell; each candidate is a sequential read.

The point array is then reordered into cell order, so neighbouring cells are near in RAM, which is the dominant lever. Normals-step time is U-shaped in cell size: too fine and shells traverse empty cells, too coarse and each cell holds too many candidates to partial-sort. The minimum is near `cell` $\approx 1.3$ times the median spacing (`native/discontinuity_worker/README.md`).

## 3b.2 Region-grow facets (FACETS)

Facets are extracted by planar region growing after the FACETS / qFacets method of Dewez et al. (2016). Seeds are ordered by ascending $\sigma$, flattest first. The grow is a breadth-first search over the radius-neighbour graph. A candidate $q$ joins facet $F$ with current plane $(n_F, c_F)$ when both hold:

- axial normal agreement, $\lvert n_q \cdot n_F \rvert \ge \cos\theta_{\max}$, with $\theta_{\max}$ near 10 to 15 degrees;
- plane band, $\lvert (q - c_F) \cdot n_F \rvert \le d_{\text{band}}$, with $d_{\text{band}} \approx 2.5$ times the point spacing.

The plane is refit by PCA as the facet grows. The shipped worker maintains a seed-relative incremental covariance, $\Sigma q$ and $\Sigma q q^\top$, and recomputes the eigenpair in $O(1)$ per accretion rather than re-running a full PCA every 64 points. Adversarial verification replicated this incremental covariance against batch PCA to a relative difference near machine epsilon and confirmed it is translation-invariant to a $10^7$ shift. That $O(1)$ refit cut the facets stage from about 3.2 s to about 2.85 to 3.0 s on the Tongjiang cloud. Facets below a minimum point count are dropped. Each surviving facet carries its PCA normal, centroid, and point count.

## 3b.3 Joint-set clustering (Watson axial mean-shift) and the dip / dip-direction convention

Facet poles live on the projective plane $\mathbb{RP}^2$: they are antipodal, $x \equiv -x$. The DSE method of Riquelme et al. (2014) clusters poles but takes a preset cluster count. The worker avoids that preset by mode-seeking with the antipodal Watson kernel,

$$K(m, x) \propto \exp\!\big(\kappa\, (m \cdot x)^2\big), \quad \kappa = \frac{1}{\sin^2(\text{bw})},$$

which is the natural axial (bipolar) density on the sphere and is invariant to $x \leftrightarrow -x$ because it depends on $(m \cdot x)^2$. The weighted mean-shift fixed point, with weight $w_i$ the facet point count, is

$$m \leftarrow \mathrm{normalize}\!\left( \sum_i w_i \, \exp\!\big(\kappa((m \cdot x_i)^2 - 1)\big)\, \mathrm{sgn}(m \cdot x_i)\, x_i \right).$$

Each seed hill-climbs to a mode, stopping below a small angular change. Modes within a merge angle (axial) are one set, and a set's point share is its facet points over the total. The set count is discovered, not preset. The bandwidth is the granularity knob; on Tongjiang, bw 15 degrees gives a stable 4 sets, bw 10 gives about 10, bw 8 gives about 19. Set count is bandwidth, seeding, and noise sensitive, which is inherent to joint-set identification, not a defect of this implementation.

Determinism was established by adversarial review. A non-stable sort on the seed-order key was replaced by a total order, and the mean-shift now seeds from a hybrid set: strided facet poles plus a fixed Fibonacci hemisphere grid, both order-independent. Two runs are now byte-identical. The Fibonacci grid added one mode the strided-only sample had missed, moving the shipped Tongjiang result from 4 to 5 candidate sets at the comprehensive seeding; the dominant 2 to 3 sets are robust and the marginal sets remain count-sensitive.

**Orientation convention.** With the geology frame $x = \text{East}$, $y = \text{North}$, $z = \text{Up}$, and $n$ folded to $n_z \le 0$ (`Discontinuity/OrientationMath.cs`):

- dip, $\delta = \arccos(\lvert n_z \rvert) \in [0\degree, 90\degree]$;
- dip direction, $\alpha = \mathrm{atan2}(n_x, n_y) \bmod 360\degree$, clockwise from North;
- strike $= (\alpha - 90\degree) \bmod 360\degree$, pole trend $= (\alpha + 180\degree) \bmod 360\degree$, plunge $= 90\degree - \delta$.

The forward map reads the down normal; its inverse computes $n = (\sin\delta \sin\alpha,\ \sin\delta \cos\alpha,\ -\cos\delta)$, flipping only the $z$ sign, the correction recorded when the ingest path was first wired (see the validation report).

## 3b.4 ISRM distinct-joint spacing (the original fix)

Spacing is the geotechnically load-bearing number, and it carried two distinct errors that were corrected. Both fixes are original to this work.

**Fix 1: the home-cell neighbour bug.** The first hash-map kNN advanced its search ring before scanning, so it skipped the query's home cell and measured the nearest neighbour in an adjacent coarse cell. That over-estimated point spacing roughly tenfold, about 0.078 m against a true 0.008 m on Tongjiang. The inflated spacing inflated the region-grow plane band ($2.5 \times \text{spacing}$), which over-merged the joint sets, so only 2 sets were found. The CSR kNN starts at ring 0, recovers the correct 0.008 m spacing, tightens the band, and resolves 4 well-separated sets. The evolved worker is therefore both faster and more geologically correct.

**Fix 2: facet-gap spacing versus distinct-joint spacing.** Within a set, the facet centroids project onto the set normal as scalar offsets $s_k = c_k \cdot n_{\text{set}}$. The first method sorted these offsets and reported the mean consecutive gap as the spacing. That measured the sampling density of facets along the normal, not the distance between physical joints, and it returned spacings far too small to be a real fracture spacing. The fix clusters the sorted offsets into distinct joints with a gap threshold (the gap exceeds the larger of several times the median gap and twice the point spacing), then reports the cluster-to-cluster distances as the spacing and the cluster count as the number of joints. This is the ISRM-faithful "spacing along a scanline" of Priest (1993, chapter 4) and the ISRM Suggested Methods [R47]: spacing is the true perpendicular plane-to-plane distance.

The before-and-after is visible in the numbers. The honest, ISRM-fixed Tongjiang spacings are decimetre-scale per set: 0.2478, 0.2344, 0.6735, 1.1084 m. The honest realisation on a small blank yields distinct fracture planes at a dominant spacing of about 0.19 to 0.38 m with no scale fudge factor, where the earlier averaged-facet-gap method had returned spacings one to two orders of magnitude smaller.

## 3b.5 Stereonet and Palmstrom block size

The Stereonet + Block Size component D5F1004A consumes the per-set dip, dip direction, spacing, and share, plus the facet poles, and draws itself on the canvas in `DrawViewportWires`. It is self-presenting: reopening the saved definition cold reproduces the net, great circles, set poles, facet-pole density, and the block-size readout, with no external bake script.

**Projection.** The default is the equal-area (Schmidt / Lambert) lower-hemisphere net (`Discontinuity/StereonetProjection.cs`). For a pole at colatitude $\theta = \arccos\lvert n_z \rvert$ and azimuth $\varphi = \mathrm{atan2}(n_x, n_y)$,

$$r = \sqrt{2}\,\sin(\theta/2), \quad p_x = R\,r\,\sin\varphi, \quad p_y = R\,r\,\cos\varphi.$$

A Wulff (equal-angle) toggle swaps to $r = \tan(\theta/2)$. The mapping is monotone: dip 0 maps to the centre, dip 90 maps to the rim.

**Block size (Palmstrom 2005).** Inter-set axial angles are $\gamma_{jk} = \arccos\lvert n_j \cdot n_k \rvert$. With three dominant sets of spacings $s_1, s_2, s_3$ (`Discontinuity/BlockSizeMath.cs`):

- volumetric joint count $J_v = \sum_j 1/s_j$, with single-plane sets ($s_j \le \varepsilon$) skipped to guard the division;
- block volume $V_b = \dfrac{s_1 s_2 s_3}{\sin\gamma_{12}\,\sin\gamma_{23}\,\sin\gamma_{31}}$, reducing to $s_1 s_2 s_3$ for orthogonal sets;
- block-size index $I_b = (s_1 + s_2 + s_3)/3$, equivalent diameter $D_{eq} = V_b^{1/3}$;
- RQD proxy $\mathrm{RQD} \approx 110 - 2.5\,J_v$, clamped to $[0, 100]$.

When fewer than 3 sets are present the blocks are unbounded slabs or columns, and the component emits a descriptor rather than a $V_b$. A synthetic check with 3 orthogonal sets at spacings 1.0, 1.5, 2.0 m returns $V_b = 3.000\,\text{m}^3$, $J_v = 2.1667/\text{m}$, $D_{eq} = 1.442$, matching the closed form.

**Units are the highest risk.** A detail scan reports spacings in metres on a centimetre-scale object. On the raw Tongjiang detail scan ($s$ of 0.003 to 0.012 m) a naive read gives $J_v$ of hundreds per cubic metre and $\mathrm{RQD} = 0$, physically meaningless at that scale. The component therefore displays spacing units, accepts a unit scale, and shows a PROXY label when the user maps the cm-scale detail scan to a metre-scale bench (`docs/validation/discontinuity_ingest_card/VALIDATION_REPORT.md`). With the ISRM distinct-joint fix the metre-scale spacings are honest without any scale fudge, and the unit guard is then a safety net, not a correction.

## 3b.6 Measured-orientation ingest (CSV / GeoJSON / DXF / SHP)

Not every input is a cloud. A field survey or a CloudCompare / Compass session exports orientations directly, so the Discontinuity Ingest component D5F10049 reads measured discontinuities from four formats (`Discontinuity/Ingest/DiscontinuityReader.cs`, `Discontinuity/Ingest/Discontinuity.cs`). The reader dispatches on extension:

- **CSV** (hand-rolled, dependency-free): sniffs the delimiter and a case-insensitive header, and accepts `dip,dipdir[,x,y,z]`, normal columns `nx,ny,nz[,x,y,z]`, or plane coefficients $a,b,c,d$ with $n \cdot p = d$ giving centroid $(d/\lvert n \rvert^2)\,n$.
- **GeoJSON** (NetTopologySuite.IO.GeoJSON): point features with `dip` / `dipdir` properties become planes; LineString and MultiLineString become traces; the `crs` is read when present.
- **DXF** (hand-rolled ASCII group-code reader): `LINE`, `LWPOLYLINE`, `POLYLINE` / `VERTEX`, and `3DFACE` entities; polylines become traces, 3DFACE becomes a plane.
- **Shapefile** (NetTopologySuite.IO.Esri): points and lines as for GeoJSON, carrying the sidecar `.prj` WKT into the collection's CRS field.

A trace is fit to a plane by PCA total least squares, the same smallest-eigenvector idea as the worker. The model is a `Discontinuity` (normal, centroid, optional trace polyline, kind, set id, cached dip / dip-direction, source) gathered into a `DiscontinuityCollection`. Bad rows are skipped with a warning, never thrown, per the project "log, skip, continue" policy. The component outputs oriented planes, traces, dip, dip direction, set id, and a report.

Live validation fed the discovered Tongjiang sets back through D5F10049 as `dip,dipdir,set,x,y,z`. It parsed the features to oriented planes with 0 warnings, and the dip / dip-direction round-tripped exactly: the shallow set baked near-flat and the steep sets baked steep, geometrically correct (`docs/validation/discontinuity_ingest_card/VALIDATION_REPORT.md`).

## 3b.7 The Joint-Sets-to-DFN bridge (infinite-plane, deterministic)

The simplest DFN treats each joint as an infinite plane spanning the bench, which is the right model when joints are persistent relative to the block size. The Joint Sets to DFN component D5F1004B wraps `JointSetDfnGenerator.Generate`, which builds fracture planes from joint sets and a bounding box. The algorithm is the scanline construction of Priest (1993, chapter 4) [R47]:

1. Project the 8 bounding-box corners onto a set's normal, giving a range $[t_{\min}, t_{\max}]$ relative to the box centre.
2. Walk the range in steps of the mean spacing, starting from a uniformly random offset in $[0, \text{spacing})$.
3. Emit a plane at $(\text{boxCentre} + t\,n)$ for each step.

Spacing is constant by default, or negative-exponential when requested, with the step $-\ln(1-U)\cdot\text{spacing}$ for $U \sim \text{Uniform}(0,1)$. The orientation can carry a small Gaussian scatter about the mean, built from two in-plane Box-Muller samples, a small-angle Fisher approximation. The whole construction is deterministic given the seed, and a hard per-set plane limit guards against a spacing too small for the box. The `JointSet` orientation convention matches section 3b.3: dip direction clockwise from North, dip from horizontal, normal computed from the pair. The emitted planes feed the slab cutter and from there the block-cut optimiser of chapter 03. Source: `Masonry/Quarry/JointSetDfnGenerator.cs`, `Masonry/Quarry/JointSet.cs`.

## 3b.8 The Baecher stochastic finite-disc DFN and Monte-Carlo block yield

Persistent infinite planes overstate connectivity. A real joint terminates, and a finite disc is the standard stochastic model. The Stochastic DFN (Baecher) component D5F1004C wraps `BaecherDfnGenerator` (`Masonry/Quarry/BlockCutOpt/BaecherDfnGenerator.cs`), a clean-room implementation of the Baecher et al. (1977) finite-disc model from the joint-generator family [R162]. Each realisation draws disc centres as a Poisson point process, pole orientations from a Fisher (1953) distribution about the set mean with dispersion $\kappa$, and disc radii from a lognormal distribution. The disc count for a set follows the intensity relation

$$N = \frac{P_{10}\, V}{\pi\, \mathbb{E}[r^2]}, \qquad P_{10} = \frac{1}{\text{spacing}},$$

where $P_{10}$ is the linear fracture frequency along the set normal, $V$ the bench volume, and $\mathbb{E}[r^2]$ the second moment of the radius law. The Fisher dispersion is fit from the per-set pole scatter; the radius law is fit from the in-plane facet traces.

**Block yield is a distribution, not a point estimate.** Because the DFN is stochastic, the optimiser of chapter 03 is run over $M$ independent realisations and the marketable-block count is reported as a distribution. The wrapper runs $M$ seeds, draws one DFN realisation per seed, solves block-cut on each, and reports the $p_{10}$, $p_{50}$, $p_{90}$, mean, and standard deviation of recovery plus the most-common best direction. The $p_{10}$ recovery is the robust score: the yield that survives fracture-mapping uncertainty. On a synthetic 4 x 4 x 4 m bench, $M = 20$ realisations with a mean of about 237 fractures yield, for a 0.5 m target block, a mean of 7.3 blocks (standard deviation about 2.7, range 4 to 13); for a 0.8 m target, a mean of about 0.6 (range 0 to 2); for a 1.2 m target, 0 blocks in all 20 realisations. The spread across seeds is the point: a single deterministic solve would have reported one number and hidden the variance.

### Benchmark and the corrected framing

The shared bottleneck of every discontinuity pipeline is per-point normal estimation, so that is the benchmarked operation. The cloud is the Tongjiang quarry-face `detail_cloudXB.ply` at 7,858,334 points, on a 16-logical-core machine with a static mingw64 build. An apples-to-apples comparison requires the same neighbourhood definition, so kNN and radius are reported separately.

At a matched $k = 24$ neighbourhood, the shipped double-precision worker computes the normals in about 10.2 s against Open3D 0.19's KD-tree at 14.78 s, about 1.4x faster. A single-precision structure-of-arrays bench variant reaches about 8.0 s, about 1.85x. The speedup comes from the counting-sort CSR build (about 0.7 s, $O(N)$), the cache-coherent point reorder, the analytic eigensolver, and the float coordinate layout.

The earlier "215x / 265x faster than CloudCompare" claim is retracted. CloudCompare's octree took about 2127 s, but it ran at radius 0.5 m, which spans thousands of neighbours per point on this 8 mm-spacing cloud, a far larger neighbourhood than $k = 24$. That is a scale reference, not a like-for-like speedup. The defensible win is the about 1.4x over Open3D at matched $k = 24$, plus a Rhino-free, single-executable, deterministic worker. The whole discontinuity pipeline, normals plus facets plus Watson sets, runs end-to-end in about 13.6 to 14.7 s, with normals the dominant stage (`native/discontinuity_worker/README.md`).

### Live validation, and a validity gate

The first acceptance rule is geological, not numerical: confirm the scan is a clean in-situ exposure before trusting the joint sets. A muck pile, a vegetated slope, or a registration crop will produce confident but meaningless sets, because the surfaces are loose-block faces, not joints (`docs/rockfaces_dataset.md`). The validity gate checks the assigned point share and an edge-on view.

**Granite Dells, Arizona (valid in-situ case).** A clean TLS granite outcrop, OpenTopography OT.122010.26912.1, 4,977,725 points at about 0.0141 m spacing. The worker found 3 sets in about 9.1 s: a near-horizontal sheeting joint at dip 3.13 degrees and two near-vertical sets at dip 88.66 and 89.19 degrees, with spacings 0.2814, 0.3361, 0.3954 m and point shares 0.63, 0.19, 0.18. The sheeting joint plus two orthogonal vertical sets is the textbook granite block structure (`docs/validation/discontinuity_ingest_card/CLEAN_GRANITE_DELLS.md`).

**Finestrat, Alicante (valid complete-face case).** A gypsum rock slope, the Riquelme / DSE reference dataset (Zenodo 7576524, CC-BY-4.0), 1,738,184 points, downsampled to 869,092. The worker found 3 sets in about 1.36 s at dips 85.1, 37.8, 86.8 degrees and spacings 1.68, 1.95, 1.83 m. This is the cleanest complete-face demonstration, because the scan is a full exposed slope rather than a detail crop (`docs/rockfaces_dataset.md`).

**Tongjiang, Sichuan (cautionary case).** The detail-scan cloud at 7,858,334 points runs the full pipeline in about 14.6 s and yields 4 sets at dips 19.3, 78.75, 72.27, 83.47 degrees and ISRM-fixed spacings 0.2478, 0.2344, 0.6735, 1.1084 m. The comprehensive Fibonacci seeding surfaces a 5th marginal set. The card run at unit scale 100, the cm-detail-to-bench proxy, reported $J_v = 7.20$, $\mathrm{RQD} = 92$, $V_b = 0.352\,\text{m}^3$, $D_{eq} = 0.71$ m and is labelled PROXY in the component. The cautionary flag stands: the originals are a loose-rock muck-pile scan, not an in-situ exposure, so Tongjiang exercises the software end to end but is not a valid dimension-stone deposit (`docs/validation/discontinuity_ingest_card/VALIDATION_REPORT.md`, `docs/rockfaces_dataset.md`).

Both new components built clean against net48, and all three (D5F10048, D5F10049, D5F1004A) register in a fresh Rhino 8 with the deployed `.gha`. The Core uses `Rhino.Geometry` types whose native operations call `rhcommon_c.dll`, which only initialises inside a live Rhino. The reader tests therefore SKIP cleanly headless and are covered live; the block-size, orientation, and stereonet math tests run headless and PASS, for 0 failures (`docs/validation/discontinuity_ingest_card/VALIDATION_REPORT.md`).

## 3b.9 Status and what is left

Established and live-validated:

- Clean-room CSR worker: $k = 24$ PCA normals on the 7.86 M-point Tongjiang cloud in about 10.2 s, about 1.4x faster than Open3D's KD-tree at matched $k = 24$; full pipeline about 14.7 s, deterministic.
- FACETS region-grow with $O(1)$ incremental covariance, and Watson axial mean-shift joint-set clustering with a discovered set count.
- ISRM distinct-joint spacing, the two-part original fix, giving honest decimetre spacings.
- Stereonet plus Palmstrom block size, self-presenting on canvas, units-guarded.
- Four-format measured-orientation ingest (CSV, GeoJSON, DXF, SHP).
- The deterministic infinite-plane DFN bridge to chapter 03, and the Baecher finite-disc DFN with Monte-Carlo block yield.
- Three valid in-situ validations: Granite Dells (3 sets), Finestrat (3 sets), plus Tongjiang as the worker exercise and muck-pile cautionary case.

Honest limitations:

- **Persistence is censored.** A surface scan sees the trace, not the buried extent of a joint, so the persistent fraction and the radius law are partly extrapolated. The DFN disc sizes inherit that censoring.
- **The lognormal radius coefficient of variation is assumed**, not measured from independent data; it is fit from in-plane facet traces, which are themselves censored.
- **Yield is a distribution, not a point estimate.** The Monte-Carlo wrapper reports $p_{10}$ / $p_{50}$ / $p_{90}$ over seeds for exactly this reason; a single deterministic solve would hide the variance.
- **Set count is bandwidth, seeding, and noise sensitive.** The dominant 2 to 3 sets are robust; the marginal sets and the exact count are inherently sensitive.
- **The validity gate is advisory, not automatic.** The user must confirm a clean in-situ exposure; the software cannot yet reject a muck-pile scan on its own.

Next:

- A point-share floor and a stability-based bandwidth pick, to make the set count robust across clouds without manual bandwidth tuning.
- A matched radius-0.5 control (worker and Open3D both at radius 0.5) to fully decompose the algorithm gain from the neighbourhood gain.
- Per-physical-plane density clustering as the v2 refinement of the distinct-joint spacing, replacing the sorted-offset gap clustering.

## References (this chapter)

The two project-global references reused here:

- **[R47]** ISRM Suggested Methods (1978) and Priest, S. D. (1993), *Discontinuity Analysis for Rock Engineering* - joint-set definition, scanline spacing, and the infinite-plane DFN basis.
- **[R162]** Tian (2025) and the Baecher / Veneziano / Levy-Lee / Priest joint-generator family - stochastic finite-disc DFN models.

New sources cited in this chapter (listed here, not assigned global R-numbers):

- Baecher, G. B., Lanney, N. A., and Einstein, H. H. (1977). Statistical description of rock properties and sampling. *18th U.S. Symposium on Rock Mechanics*. Finite-disc DFN model: Poisson centres, Fisher poles, lognormal radii.
- Dewez, T. J. B., Girardeau-Montaut, D., Allanic, C., and Rohmer, J. (2016). FACETS: a CloudCompare plugin to extract geological planes from unstructured 3D point clouds. *ISPRS Archives* XLI-B5. Planar region-growing facet extraction (qFacets); used as a black-box baseline, not as source.
- Fisher, R. A. (1953). Dispersion on a sphere. *Proceedings of the Royal Society A* 217(1130):295-305. Orientation scatter via the dispersion parameter $\kappa$.
- Hoetzlein, R. (2014). Fast fixed-radius nearest neighbors: interactive million-particle fluids. *GPU Technology Conference*. Counting-sort uniform-grid cell lists.
- Palmstrom, A. (2005). Measurements of and correlations between block size and rock quality designation (RQD). *Tunnelling and Underground Space Technology* 20(4):362-377. Block volume, $J_v$, and RQD relations.
- Pauly, M., Gross, M., and Kobbelt, L. P. (2002). Efficient simplification of point-sampled surfaces. *IEEE Visualization 2002*. Surface variation $\sigma = \lambda_0 / (\lambda_0 + \lambda_1 + \lambda_2)$.
- Riquelme, A. J., Abellan, A., Tomas, R., and Jaboyedoff, M. (2014). A new approach for semi-automatic rock mass joints recognition from 3D point clouds. *Computers and Geosciences* 68:38-52. The DSE method; mean-shift pole clustering, used as a black-box baseline.
