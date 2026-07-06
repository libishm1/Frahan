# The mathematics of the geology, GPR and block-cutting stacks

Code-bound equation layer for the shipping geology/discontinuity, GPR-processing
and block-cutting implementations in this repository. Each equation:
statement, short derivation, code provenance, method-class citation. Derived
from the implementation as read, not copied from paper figures. Renders
natively on GitHub and on the docs site (MathJax).

Shipping implementations covered: `TerzaghiCorrection`, `FractureIntensity`,
`BlockSizeMath`, `InSituBlockSize`, `SetClusterer`, `KinematicAnalysis`,
`JointSetDfnGenerator`, `BaecherDfnGenerator` (Part A);
`RadargramProcessor`, `FractureExtractor`, `Kriging`, `FractureUncertainty`,
`GprDetectionCalibration` (Part B); `BlockCutOptSolver` + `CuttingGrid`,
`SlabYieldOptimizer`, `BlockYieldOptimizer`, `WireSawFeasibility`,
`CutOrientationOptimizer`, `RecoveryCascade`, `FisherRobustSampler`,
`AmrrPlanner`, `BlockYieldEstimator` (Part C). All under
`src/Frahan.StonePack.Core/` unless noted; one GH-layer item
(`FractureBlockPackComponent`) is included because the uncertainty-safe yield
toggle lives there.

Where the code deviates from the textbook formulation, the CODE's version is
stated and the deviation is flagged. Topics requested but absent from code are
declared in one line.

---

# Part A. Geology / discontinuity stack

## A1. Terzaghi orientation-bias correction

### A1.1 Weight

A sampling element (scanline or rock-face window) under-samples discontinuities
sub-parallel to it. Each discontinuity gets the Terzaghi weight

$$
w_i = \frac{1}{\sin \delta_i},
\qquad
\delta_i = \begin{cases}
90^\circ - \angle(\mathbf{p}_i, \mathbf{t}) & \text{scanline direction } \mathbf{t}\\
\angle(\mathbf{p}_i, \mathbf{n}_f) & \text{window face normal } \mathbf{n}_f
\end{cases}
$$

where $\mathbf{p}_i$ is the discontinuity pole and $\angle(\cdot,\cdot)$ is the
acute (axial) angle. $\delta$ is the angle between the discontinuity PLANE and
the sampler; as $\delta \to 0$ (grazing) the weight diverges, so it is capped
by a blind-zone half-angle $\delta_{\min}$ (default $15^\circ$):

$$
w_i = \frac{1}{\sin\big(\max(|\delta_i|,\ \delta_{\min})\big)},
\qquad
w_{\max} = \frac{1}{\sin \delta_{\min}} \approx 3.86 \ \ (\delta_{\min}=15^\circ).
$$

*Derivation.* The expected number of intersections of a plane set with a
scanline is proportional to $|\cos\angle(\mathbf{t},\mathbf{p})| = \sin\delta$;
dividing each observation by its intersection probability de-biases the count.
Features inside the blind zone are counted as clamped
(`TerzaghiResult.Clamped`) and flagged as lower-confidence rather than
extrapolated. Code: `TerzaghiCorrection.Weight`,
`TerzaghiCorrection.ScanlineBiasAngleDeg`, `TerzaghiCorrection.WindowBiasAngleDeg`,
`TerzaghiCorrection.Correct` (`Discontinuity/TerzaghiCorrection.cs`).
Citation ([Algorithm] attribute, `TerzaghiCorrectionComponent`): Terzaghi 1965,
Geotechnique 15(3):287-304; Priest 1993 ch. 5; capped-weight practice (Park & West).

### A1.2 Corrected set proportions and weighted mean pole

$$
\hat f_k = \frac{\sum_{i \in S_k} w_i}{\sum_i w_i},
\qquad
\bar{\mathbf{p}} = \operatorname{normalize}\!\Big(\sum_i w_i\, \sigma_i\, \mathbf{p}_i\Big),
\quad \sigma_i = \operatorname{sign}(\mathbf{p}_i \cdot \mathbf{p}_{\text{seed}}),
$$

a weighted AXIAL mean: each pole is folded to the lower hemisphere and
sign-aligned to the largest-weight pole before summing, so $\mathbf{p}$ and
$-\mathbf{p}$ (the same plane) never cancel. Code:
`TerzaghiCorrection.Correct` (per-set sums), `TerzaghiCorrection.CorrectedMeanPole`.

## A2. Fracture intensity (Dershowitz-Herda P_ij family)

### A2.1 P32 from spacings (persistent-set route)

For persistent parallel sets with normal spacing $s_j$ (scaled to metres by
`unitScale`):

$$
P_{32} = \sum_j \frac{1}{s_j},
$$

each set contributing unit fracture area per $s_j$ of thickness. This equals
the Palmstrom volumetric joint count $J_v$ for persistent joints (cross-checked
against `BlockSizeMath.Jv`). Code: `FractureIntensity.P32FromSpacings`.
Citation ([Algorithm] attribute, `FractureIntensityComponent`):
Dershowitz & Herda 1992, 33rd US Rock Mech. Symp.

### A2.2 Scanline forward and inverse (the Terzaghi geometry factor)

$$
P_{10}(\mathbf{t}) = \sum_j P_{32,j}\, \big|\cos\angle(\mathbf{t}, \mathbf{p}_j)\big|,
\qquad
P_{32} = \frac{P_{10}}{\max\!\big(|\cos\angle(\mathbf{t},\mathbf{p})|,\ \cos(90^\circ - \delta_{\min})\big)} .
$$

*Derivation.* A scanline crosses a set of volumetric intensity $P_{32}$ at
linear frequency $P_{32}|\cos\theta|$ where $\theta$ is the scanline-to-pole
angle; inverting recovers $P_{32}$ from a measured count. **Code deviation:**
the inverse clamps the cosine at
$\cos(90^\circ - \delta_{\min})$ (default $\delta_{\min}=15^\circ$) so a
grazing set does not blow up, mirroring the Terzaghi blind-zone cap; the
textbook inversion has no floor. Code: `FractureIntensity.P10AlongScanline`,
`FractureIntensity.P32FromP10`. Citation: Wang 2005 (stereological P10/P21 to P32).

### A2.3 Direct DFN route and companions

$$
P_{32} = \frac{\sum_i A_i}{V},
\qquad
P_{30} = \frac{N}{V},
\qquad
P_{21} = \frac{\sum_i \ell_i}{A_{\text{window}}} .
$$

Code: `FractureIntensity.P32FromAreas`, `FractureIntensity.P30FromCount`,
`FractureIntensity.P21FromTraces`; assembled by `FractureIntensity.Compute`.

## A3. Deterministic block size (Palmstrom)

For the 3 dominant sets with unit poles $\mathbf{n}_1,\mathbf{n}_2,\mathbf{n}_3$,
spacings $s_1,s_2,s_3$ and pairwise acute inter-set angles $\gamma_{ij}$:

$$
V_b = \frac{s_1 s_2 s_3}{\sin\gamma_{12}\,\sin\gamma_{23}\,\sin\gamma_{31}},
\qquad
J_v = \sum_j \frac{1}{s_j},
\qquad
\mathrm{RQD} \approx \operatorname{clamp}\big(110 - 2.5\,J_v,\ 0,\ 100\big),
$$

with the block-size index $I_b = (s_1+s_2+s_3)/3$ and equivalent diameter
$D_{eq} = V_b^{1/3}$. Guards: $V_b$ is declared undefined when any
$\sin\gamma_{ij} < 0.087$ (sets within about $5^\circ$ of parallel) or when
fewer than 3 valid sets exist (slabs / columns, not blocks). **Code note:** the
code uses the Palmstrom 1974 correlation constant pair $(110, 2.5)$, not the
older Palmstrom 1982 $(115, 3.3)$ form. Code: `BlockSizeMath.Compute`,
`BlockSizeMath.ComputeFromDip` (`Discontinuity/BlockSizeMath.cs`). Citation
(file header): Palmstrom 1995/2005; ISRM Suggested Methods (spacing along the
set normal).

## A4. In-situ block-size distribution (Monte-Carlo IBSD)

### A4.1 Per-realization block volume

$$
V_b = \frac{s_1 s_2 s_3}{q},
\qquad
q = \big|\mathbf{n}_1 \cdot (\mathbf{n}_2 \times \mathbf{n}_3)\big| \in (0,1],
$$

the scalar-triple-product (determinant) form of Palmstrom's formula: $q = 1$
for mutually orthogonal sets (right prism), $q \to 0$ as sets go coplanar.
Realizations with $q < 0.02$ (roughly $1^\circ$ from coplanar) are skipped as
ill-conditioned. With more than 3 sets, the 3 smallest-spacing samples bound
the block. Code: `InSituBlockSize.Simulate`, `InSituBlockSize.Det`.
Citation ([Algorithm] attribute, `InSituBlockSizeComponent`): Kalenchuk,
Diederichs & McKinnon 2006; Palmstrom 2005.

### A4.2 Fisher orientation sampling

Each set's pole is drawn from a Fisher (von Mises-Fisher) distribution about
the mean pole $\boldsymbol\mu$ with concentration $\kappa$ by inverse-CDF of the
colatitude:

$$
w = 1 + \frac{1}{\kappa}\ln\!\big(u + (1-u)\,e^{-2\kappa}\big) = \cos\theta,
\qquad
\mathbf{v} = w\,\boldsymbol\mu + \sqrt{1-w^2}\,\big(\cos\phi\ \mathbf{a} + \sin\phi\ \mathbf{b}\big),
$$

with $u,\ \phi/2\pi \sim U(0,1)$ and $(\mathbf a, \mathbf b)$ an orthonormal
basis normal to $\boldsymbol\mu$. The concentration comes from an input angular
scatter via Fisher's circular-s.d. approximation:

$$
\kappa = \left(\frac{81}{\text{scatter}_{\deg}}\right)^{2} .
$$

Code: `InSituBlockSize.FisherSample` (identical sampler in
`BaecherDfnGenerator.FisherSample`). Citation (file header): Fisher, Lewis &
Embleton 1987 (inverse-CDF colatitude sampling).

### A4.3 Spacing sampling

$$
s = -\bar{s}\,\ln U
\quad \text{(negative exponential, Priest 1993 default)},
\qquad
s = \max\!\big(0.05\,\bar s,\ \bar s\,(1 + 0.3\,z)\big),\ z \sim N(0,1)
\quad \text{(clamped normal, CV } 0.3\text{)}.
$$

Box-Muller supplies $z$. Code: `InSituBlockSize.SampleSpacing`.

### A4.4 Outputs

Empirical $P_{10}/P_{50}/P_{90}$ volume percentiles (linear interpolation on
the sorted sample), $D_{eq} = V_{b,50}^{1/3}$, mean non-orthogonality
$\bar q$, right-prism fraction $\Pr[q \ge 0.95]$ (the sawable-to-rectangular
signal), and a shape class from the sorted spacings with elongation
$= s_{\max}/s_{\text{mid}} \ge 2$ and flatness $= s_{\text{mid}}/s_{\min} \ge 2$
splitting blocky / columnar / tabular / columnar+tabular. Code:
`InSituBlockSize.Simulate`, `InSituBlockSize.ShapeClass`,
`InSituBlockSize.Percentile`.

## A5. Joint-set statistics: Watson-kernel mean-shift clustering

Facet poles (lower hemisphere, weighted by facet point count $w_j$) are
clustered by mean-shift on the unit sphere with a Watson AXIAL kernel

$$
K(\mathbf{m}, \mathbf{x}) = \exp\!\big(\kappa\,[(\mathbf{m}\cdot\mathbf{x})^2 - 1]\big),
\qquad
\kappa = \frac{1}{\sin^2(\text{bandwidth})},
$$

which is antipodally symmetric ($\mathbf{x}$ and $-\mathbf{x}$ equal), so a set
never splits across the stereonet equator. The mean-shift update sign-folds
each pole toward the current mode:

$$
\mathbf{m} \leftarrow \operatorname{normalize}\!\Big(
\sum_j w_j\, e^{\kappa[(\mathbf{m}\cdot\mathbf{p}_j)^2 - 1]}\,
\operatorname{sign}(\mathbf{m}\cdot\mathbf{p}_j)\ \mathbf{p}_j \Big),
$$

iterated to convergence ($< 0.02^\circ$ axial move, max 60 iterations);
converged modes within `MergeDeg` (default $8^\circ$ axial) merge; facets are
assigned to the nearest mode by axial angle; the number of sets is discovered,
not preset. Per-set normal spacing = the mean of the positive gaps between
sorted projections of member-facet centroids onto the set pole:

$$
\bar s = \operatorname{mean}\big\{ o_{(i+1)} - o_{(i)} : o_{(i+1)} - o_{(i)} > 0 \big\},
\qquad
o_i = \mathbf{c}_i \cdot \mathbf{p}_{\text{set}} .
$$

**Code note:** the code's own header calls the kernel Watson; the exponent
matches the Watson (axial bipolar) density $\propto e^{\kappa(\mathbf m\cdot\mathbf x)^2}$
up to the constant factor $e^{-\kappa}$. Code: `SetClusterer.Cluster`,
`SetClusterer.NormalSpacing` (`Discontinuity/DiscontinuitySetClusterer.cs`).
Citation (file header): set identification after Riquelme et al. 2014 (DSE).

Fisher statistics: present as the sampling distribution (A4.2, A7.2); no
Fisher $\kappa$ ESTIMATION from data is implemented in Core (the
$\kappa = (81/\text{scatter})^2$ map converts a user-supplied scatter).
Bingham statistics: NOT present anywhere in the codebase (grep over `src/`),
one line and moving on.

## A6. Kinematic feasibility (Markland-style screens, as coded)

Convention: dip $\in [0^\circ, 90^\circ]$, dip-direction azimuth clockwise from
North; friction angle $\phi$; lateral limit $\Delta$ (default $20^\circ$).
Signed azimuth difference folded to $(-180^\circ, 180^\circ]$. The apparent dip
of the cut face (dip $\psi_f$, dip-dir $\alpha_f$) along azimuth $\alpha$:

$$
d = \operatorname{az}(\alpha, \alpha_f),
\qquad
\psi_{\text{app}}(\alpha) =
\begin{cases}
\arctan\!\big(\tan\psi_f \cos d\big) & |d| < 90^\circ\\
0 & \text{otherwise (no daylight that way).}
\end{cases}
$$

Code: `KinematicAnalysis.ApparentDip`, `KinematicAnalysis.AzDiff`.

### A6.1 Planar sliding (per set)

Feasible iff all three hold:

$$
\big|\operatorname{az}(\alpha_s, \alpha_f)\big| \le \Delta,
\qquad
0 < \psi_s < \psi_{\text{app}}(\alpha_s),
\qquad
\psi_s > \phi .
$$

**Code deviation from classic Markland:** daylighting is tested against the
face's APPARENT dip evaluated along the SET's dip direction
$\psi_{\text{app}}(\alpha_s)$, not the face's true dip $\psi_f$; this is a
stricter, geometrically exact daylight test for sets oblique to the face.
Code: `KinematicAnalysis.Analyze` (planar block). Citation ([Algorithm]
attribute, `KinematicFeasibilityComponent`): Markland 1972; Hoek & Bray 1981;
Wyllie & Mah 2004 ch. 7.

### A6.2 Wedge sliding (per pair)

The intersection line of sets $i,j$ is
$\mathbf{L} = \mathbf{n}_i \times \mathbf{n}_j$ (pointed downward), with

$$
\text{plunge} = \arctan\!\frac{-L_z}{\sqrt{L_x^2 + L_y^2}},
\qquad
\text{trend} = \operatorname{atan2}(L_x, L_y) \bmod 360^\circ,
$$

feasible iff

$$
0 < \text{plunge} < \psi_{\text{app}}(\text{trend})
\quad \text{and} \quad
\text{plunge} > \phi .
$$

Code: `KinematicAnalysis.Intersection`, `KinematicAnalysis.Analyze` (wedge
block). Citation: Hoek & Bray 1981. **Code note:** no lateral-limit gate on
wedges (daylight along the trend is the direction test), and friction is
screened on the plunge only (the standard Markland wedge screen; no wedge-factor
refinement).

### A6.3 Flexural toppling (per set)

Feasible iff the set dips INTO the face and inter-layer slip is possible:

$$
\Big|\,\big|\operatorname{az}(\alpha_s, \alpha_f)\big| - 180^\circ \Big| \le \Delta
\quad \text{and} \quad
(90^\circ - \psi_s) \le (\psi_f - \phi) .
$$

*Derivation.* $(90^\circ - \psi_s)$ is the inclination of the layer normals;
slip on layer boundaries requires the face to steepen past friction by that
amount (the Goodman-Bray condition). Code: `KinematicAnalysis.Analyze`
(toppling block). Citation: Goodman & Bray 1976. **Code note:** the slip
condition uses the face's TRUE dip $\psi_f$, not an apparent dip.

## A7. DFN generation

### A7.1 Infinite-plane joint-set DFN (spacing walk)

For each set with unit normal $\mathbf{n}$ and mean spacing $\bar s$: project
the 8 box corners onto $\mathbf{n}$ to get $[t_{\min}, t_{\max}]$ (measured
from the box centre $\mathbf{c}$), then emit planes at

$$
t_0 = t_{\min} + U\,\bar s,
\qquad
t_{k+1} = t_k + \begin{cases} -\bar s \ln(1-U) & \text{exponential spacing}\\ \bar s & \text{uniform spacing,}\end{cases}
$$

each plane through $\mathbf{c} + t_k \mathbf{n}$ with normal optionally
perturbed by a tangent-plane Gaussian (small-angle Fisher approximation):

$$
\mathbf{n}' = \operatorname{normalize}\big(\mathbf{n} + a\,\mathbf{u} + b\,\mathbf{v}\big),
\qquad
a, b \sim N(0, \sigma^2),\ \ \sigma = \text{scatter}_{\deg} \cdot \tfrac{\pi}{180},
$$

with $(\mathbf u, \mathbf v)$ an orthonormal in-plane basis. Code:
`JointSetDfnGenerator.Generate`, `JointSetDfnGenerator.PerturbNormal`
(`Masonry/Quarry/JointSetDfnGenerator.cs`). Citation (file header +
[Algorithm] attribute): Priest 1993 ch. 4 (spacing along a scanline); ISRM
Suggested Methods.

### A7.2 Baecher disc DFN (finite persistence)

Each fracture is a finite circular disc: centre uniform in the domain (Poisson
point process), pole Fisher-sampled about the set mean (sampler identical to
A4.2), radius lognormal. The disc count per set follows from the linear
intensity $P_{10} = 1/\bar s$:

$$
N = \operatorname{round}\!\left( \frac{P_{10}\, V}{\pi\, \mathbb{E}[r^2]} \right),
\qquad
\mathbb{E}[r^2] = \bar r^{\,2}\,(1 + \mathrm{CV}^2),\ \ \bar r = \tfrac{1}{2}\overline{D},
$$

*Derivation.* A set of discs of mean squared radius $\mathbb E[r^2]$ crossed
normal to their mean pole produces $N \pi \mathbb E[r^2] / V$ intersections per
unit length; setting that equal to $P_{10}$ gives $N$. The lognormal radius is
sampled with the arithmetic-mean/CV parameterization

$$
\sigma^2 = \ln(1 + \mathrm{CV}^2),
\qquad
\mu = \ln \overline{D} - \tfrac{1}{2}\sigma^2,
\qquad
D = e^{\mu + \sigma z},\ z \sim N(0,1),
$$

and the realized intensity is reported as $P_{32} = \sum_i \pi r_i^2 / V$.
Code: `BaecherDfnGenerator.Generate`, `BaecherDfnGenerator.LogNormal`,
`BaecherDfnGenerator.FisherSample`
(`Masonry/Quarry/BlockCutOpt/BaecherDfnGenerator.cs`). Citation (file header):
Baecher, Lanney & Einstein 1977; Fisher 1953; Dershowitz & Herda 1992.

---

# Part B. GPR processing stack

## B1. Velocity model and time-to-depth

$$
v = \frac{c}{\sqrt{\varepsilon_r}},
\qquad
z = \frac{v\,t}{2}
\quad \text{(two-way travel time)},
$$

with $c = 0.299792458$ m/ns. Per-stone $(\varepsilon_r, v, f)$ presets live in
`GprPresets` (marble $\varepsilon_r \approx 9$, $v \approx 0.10$ m/ns; granite
$\varepsilon_r \approx 6$, $v \approx 0.12$ m/ns), kept self-consistent via
$\varepsilon_r = (c/v)^2$. Grid extraction returns the true sample interval
$\Delta t$ (ns) when the reader knows it, else recovers it from the
metres-per-sample step at vacuum velocity, $\Delta t = 2\,\Delta z / c$.
Code: `RadargramProcessor.ToGrid`, `FractureExtractor.Extract` (depth per pick),
`GprPresets`. Citation ([Algorithm] attribute, `GprFractureExtractComponent`):
"v=c/sqrt(eps_r); depth=v*t/2".

## B2. B-scan processing chain

Validated order (`RadargramProcessor.Run`): dewow, background removal,
time-zero mute, smooth2d(1,1), t-power gain, [Stolt migration, smooth2d(1,2)],
Hilbert energy, smooth2d(2,2), depth-equalize. **Code note:** AGC is
implemented but NOT in the validated chain (the legacy prototype used it; the
shipping `Run` does not).

### B2.1 Dewow (running-mean high-pass)

$$
y_i = x_i - \frac{1}{W}\sum_{k=i-\lfloor W/2\rfloor}^{i + W - 1 - \lfloor W/2\rfloor} x_k,
$$

per trace, window $W = \max(5, \lfloor n_s/30 \rfloor)$; out-of-range samples
contribute zero but the divisor stays the FULL width $W$ (matches
`numpy.convolve(..., 'same')` exactly; a textbook running mean would divide by
the clipped count). Code: `RadargramProcessor.Dewow`,
`RadargramProcessor.BoxMeanSame`.

### B2.2 Background removal and time-zero mute

$$
y_{i,t} = x_{i,t} - \frac{1}{n_{tr}}\sum_{t'} x_{i,t'},
\qquad
y_{i,t} = 0 \ \text{for} \ i < \lceil 0.05\, n_s \rceil,
$$

removing horizontal banding / the direct wave, then zeroing the air-wave
coupling band. Code: `RadargramProcessor.BackgroundRemoval`,
`RadargramProcessor.TimeZeroMute`.

### B2.3 t-power gain

$$
y_{i,t} = x_{i,t}\,\big(i\,\Delta t + 1\big)^{p},
\qquad p = 1.6 \ \text{(default)},
$$

compensating spherical divergence + absorption. **Code deviation:** the gain
argument is offset by $+1$ (so the first sample is not zeroed) and the default
exponent is the empirically tuned $1.6$, not the textbook $t^1$ (spherical) or
$t^2$ (energy) laws. Code: `RadargramProcessor.TPowerGain`.

### B2.4 AGC (implemented, not in the validated chain)

$$
y_{i,t} = \frac{x_{i,t}}{\sqrt{\langle x^2 \rangle_{W}(i,t) + 10^{-9}}},
$$

sliding-window RMS normalisation with the same 'same'-convention box mean.
Code: `RadargramProcessor.Agc`.

### B2.5 Stolt f-k migration (constant velocity)

Exploding-reflector model with migration velocity $v_m = v/2$. After a 2-D DFT
$P(\omega, k_x)$ on the exact grid (arbitrary-length DFT, no zero-padding),
each output wavenumber pair $(k_z, k_x)$ with $k_z = \omega/v_m$ samples the
source spectrum at the Stolt-mapped frequency with the Stolt Jacobian:

$$
\omega' = v_m\,\operatorname{sign}(k_z)\,\sqrt{k_z^2 + k_x^2},
\qquad
\hat P(k_z, k_x) = \frac{v_m\,|k_z|}{|\omega'|}\ P(\omega', k_x),
$$

by linear interpolation in $\omega$ (out-of-range values are set to 0), then
inverse 2-D DFT. A cosine dip taper suppresses steep-dip aliasing:

$$
\text{taper}(\rho) = \begin{cases}
1 & \rho < 0.85\\
\tfrac{1}{2}\big(1 + \cos\pi\frac{\rho - 0.85}{0.15}\big) & 0.85 \le \rho \le 1\\
0 & \rho > 1
\end{cases}
\qquad
\rho = \frac{|v_m k_x|}{|\omega|} .
$$

Code: `RadargramProcessor.StoltMigration`, `Fft.Dft`. Citation
([Algorithm] attribute): Stolt 1978. **Code deviations:** cosine taper band
$0.85$-$1.0$ and left/right-zero interpolation are implementation choices of
the validated Python prototype, ported bit-compatibly.

### B2.6 Hilbert instantaneous energy

$$
E_{i,t} = \big| s_{i,t} + i\,\mathcal{H}\{s\}_{i,t} \big|^2,
$$

the squared analytic-signal envelope per trace: fractures/cavities reflect
strongly (high $E$), intact stone is the low-$E$ background. Code:
`RadargramProcessor.HilbertEnergy`, `Fft.AnalyticEnvelope`. Citation
([Algorithm] attribute): Taner 1979 (instantaneous attributes); Porsani 2006,
Isakova 2021 (high energy = fracture).

### B2.7 Depth equalization

$$
y_{i,t} = \frac{E_{i,t}}{\tilde m_i + 10^{-9} E_{\max}},
\qquad
\tilde m = \text{box}_{W}\big(\operatorname{median}_t E_{i,t}\big),
$$

per-row median normalisation (box-smoothed, $W = 31$): a locally strong DEEP
reflector reads as a fracture despite absolute decay with depth. Code:
`RadargramProcessor.DepthEqualizeEnergy`.

## B3. Fracture picking with dip-aware lateral continuity

Candidates are per-trace local maxima of the energy section above the
$q = 0.985$ global quantile. A candidate is kept only if it has lateral
support: for each of $n_{sl} = 9$ candidate slopes $\sigma$ (samples/trace) up
to the dip gate

$$
\sigma_{\max} = \frac{\tan(\theta_{\max})\, \Delta x}{v\,\Delta t / 2},
\qquad \theta_{\max} = 45^\circ,
$$

the pick mask is sheared so a reflector of that dip is horizontal, like-picks
are counted within a $\pm 2$-sample depth band over a 41-trace window, and the
pick survives iff the maximum support over slopes is $\ge 12$. Each surviving
pick carries $z = v\,t/2$ and confidence $=$ energy normalised to $[0,1]$.
**Code deviation:** the USGS criterion is a horizontal $\ge 40$-trace
continuity rule; the dip-aware shear extension (following reflectors up to
$45^\circ$) is a Frahan evolution that keeps dipping shear zones while
enforcing the USGS $< 45^\circ$ gate. Code: `FractureExtractor.Extract`,
`FractureExtractor.ToReflectorPicks`. Citation ([Algorithm] attribute):
USGS Mirror Lake WRIR 99-4018C (continuity); Porsani 2006; Isakova 2021.

## B4. Fracture-surface kriging (Gaussian-process regression)

Simple kriging on mean-centred depths $z_i$ at pick locations $(x_i, y_i)$
with a GAUSSIAN covariance and nugget:

$$
C(h) = \text{sill} \cdot \exp\!\left(-\frac{h^2}{\text{range}^2}\right),
\qquad
K_{ij} = C(\|\mathbf{x}_i - \mathbf{x}_j\|) + \text{nugget}\cdot\delta_{ij},
$$

$$
\hat z(\mathbf{x}_*) = \bar z + \mathbf{k}_*^{\top} K^{-1} (\mathbf{z} - \bar z),
\qquad
\sigma^2(\mathbf{x}_*) = \text{sill} - \mathbf{w}^{\top}\mathbf{w},
\quad \mathbf{w} = L^{-1}\mathbf{k}_*,\ K = LL^{\top}.
$$

$\sigma(\mathbf{x}_*)$ is the posterior standard deviation used as
$\sigma_{\text{interp}}$ in the tolerance ladder (about 0 at picks, growing in
the gaps between scan lines). Hyperparameters $(\text{range}, \text{nugget})$
are fitted by minimising the negative log marginal likelihood over a small grid
(ranges $\{0.03, \ldots, 0.6\}\times$extent, nuggets
$\{0.003, \ldots, 0.08\}\times$sill):

$$
\mathrm{NLML} = \tfrac{1}{2}\,\mathbf{z}_c^{\top} K^{-1} \mathbf{z}_c
+ \tfrac{1}{2}\log|K| + \text{const},
\qquad
\tfrac{1}{2}\log|K| = \sum_i \log L_{ii} .
$$

**Code deviations:** (1) the variogram is not fitted experimentally; the model
is fixed Gaussian and the range/nugget come from the marginal likelihood, which
the header motivates as capturing cross-line correlation that a
nearest-neighbour range heuristic under-estimates. (2) The file-header comment
states $\operatorname{var}(x_*) = (\text{sill} + \text{nugget}) - \mathbf w^\top \mathbf w$,
but `Predict` returns the LATENT variance $\text{sill} - \mathbf w^\top \mathbf w$
(no nugget added back), matching sklearn's `GaussianProcessRegressor`
latent-function std. The code, not the comment, is authoritative here.
(3) Cholesky failures (near-duplicate points) bump the nugget by $\times 10$ up
to 6 attempts. Code: `Kriging..ctor`, `Kriging.Predict`, `Kriging.Sigma`,
`Kriging.FitMarginalLikelihood`
(`Masonry/Quarry/Processing/Kriging.cs`). Citation ([Algorithm] attribute,
`FractureBoundedSlabsComponent`): ordinary-kriging bed surfaces, Cressie 1993;
validated against the sklearn prototype (`fracture_uncertainty.py`).

In the shipping 3-D surface component the kriging is applied to the RESIDUAL
about a least-squares dip plane,
$z_i - (a x_i + b y_i + c)$, so the plane carries dip and position and the
kriged residual is a bounded undulation (clamped to the residual range).
Code: `GprFractureSurface3DComponent.SolveSafe` (GH layer).

## B5. The tolerance ladder (position uncertainty)

Per-location 1-sigma deviation of the reconstructed fracture from the real
fracture, propagated in quadrature:

$$
\sigma_{\text{total}} = \sqrt{\sigma_{\text{recon}}^2 + \sigma_{\text{interp}}^2 + \sigma_{\text{mesh}}^2}
$$

with the three rungs, exactly as coded:

$$
\sigma_{\text{recon}}(z) = \sqrt{\left(z\,\frac{\sigma_v}{v}\right)^2 + \left(\frac{\lambda}{4}\right)^2 + \sigma_{t_0}^2},
\qquad
\frac{\sigma_v}{v} = \frac{1}{2}\,\frac{\sigma_{\varepsilon}}{\varepsilon_r},
\qquad
\frac{\lambda}{4} = \frac{v}{4 f},
$$

$$
\sigma_{t_0} = \frac{v\,\sigma_{t}}{2},
\qquad
\sigma_t = \frac{(t_{\text{apex}} - t_{\text{break}})/2}{\sqrt{3}}
\quad \text{(rectangular pick ambiguity)},
\qquad
\sigma_{\text{mesh}} = \frac{h^2}{8}\,\kappa
\quad \text{(chord sagitta)}.
$$

*Derivation.* $z = v t/2$ gives $\partial z/\partial v = z/v$ (velocity term,
grows with depth) and $\partial z / \partial t_0 = v/2$ (time-zero term,
dominates at shallow depth, Xie, Lai & Derobert 2021 Eq. 3-4); $\lambda/4$ is
the vertical-resolution floor; $\sigma_v / v = \tfrac12 \sigma_\varepsilon/\varepsilon_r$
follows from $v \propto \varepsilon_r^{-1/2}$; the mesh term is the sagitta of
a chord of length $h$ on curvature $\kappa$. The optimisation target is the
confidence that the fracture lies within tolerance $T$:

$$
\text{confidence}(\mathbf{x}) = \operatorname{erf}\!\left(\frac{T}{\sigma_{\text{total}}(\mathbf{x})\,\sqrt{2}}\right),
$$

averaged over the surface ($\operatorname{erf}$ via Abramowitz & Stegun 7.1.26,
error $< 1.5\times10^{-7}$, no MathNet dependency). Code:
`FractureUncertainty.DepthSigma`, `FractureUncertainty.VelocityRelUncertainty`,
`FractureUncertainty.LambdaQuarter`, `FractureUncertainty.TimeZeroSigma`,
`FractureUncertainty.RectTimeZeroSigma`, `FractureUncertainty.MeshSigma`,
`FractureUncertainty.Combine`, `FractureUncertainty.ConfidenceWithin`,
`FractureUncertainty.Erf`. Citations (file header): Xie, Lai & Derobert 2021
(Measurement 168:108330); Porsani 2006 (velocity error at depth).

## B6. The detection rung and uncertainty-safe yield

### B6.1 Minimum detectable area (depth-aware Fresnel floor)

$$
A_{\min}(z) = \max\!\left( \left(3\,\frac{\lambda}{4}\right)^{2},\ \ \frac{\lambda}{4}\cdot\frac{z}{2} \right).
$$

*Derivation.* The first Fresnel zone has radius $r_F = \sqrt{\lambda z / 2}$,
area $\pi \lambda z / 2$; the code's calibrated floor
$(\lambda/4)(z/2) = \lambda z / 8$ is $1/(4\pi)$ of that full zone area
(a sub-Fresnel fracture still reflects, weakly), calibrated so the 1-10 m^2
open sub-horizontal population reproduces Molron 2020's ~80% detection.
The depth-free term $(3\lambda/4)^2$ is the shallow resolution floor.
**Code deviation:** the older depth-free-only form under-estimated $A_{\min}$;
the depth term was added (EVOLVED 2026-06-05). Code:
`FractureUncertainty.MinDetectableArea`.

### B6.2 Detection probability

$$
P_{\det} = \eta_0 \cdot p_{\text{dip}} \cdot p_{\text{open}} \cdot p_{\text{size}},
\qquad
p_{\text{size}} = \frac{A}{A + A_{\min}},
$$

$$
p_{\text{dip}} = \begin{cases}
1 & d \le 25^\circ\\
1 - 0.9\,u^2(3 - 2u),\ u = \frac{d - 25}{50} & 25^\circ < d < 75^\circ\\
0.1 & d \ge 75^\circ
\end{cases}
\qquad
p_{\text{open}} = \begin{cases} 1 & \text{open}\\ \approx 0.15 & \text{sealed.} \end{cases}
$$

$\eta_0$ is the per-stone base imaging efficiency from
`GprDetectionCalibration` (granite 0.80 MEASURED, Molron 2020; others
extrapolated with an `IsMeasured` flag). **Minor doc mismatch:** the XML
comment says "default 0.85" for `baseEfficiency`; the signature default is
`0.80`. The detection-adjusted effective confidence caps the position
confidence:

$$
C_{\text{eff}} = P_{\det} \cdot C_{\text{position}} .
$$

Code: `FractureUncertainty.DetectionProbability`,
`FractureUncertainty.EffectiveConfidence`, `FractureUncertainty.Summarise`,
`GprDetectionCalibration`. Citations (file headers): Molron et al. 2020
(Aspo, 10.1016/j.enggeo.2020.105674); Dorn et al. 2012.

### B6.3 Uncertainty-safe yield (how sigma enters the yield)

The fracture position sigma enters the block packer as a HARD inward clearance:
a candidate block passes only if the block GROWN by the clearance
$c = \sigma_{\text{fracture}}$ on all sides lies fully inside the
fracture-bounded slab mesh (centre + 8 expanded corners, parity ray-cast
point-in-mesh):

$$
\text{block accepted} \iff
\big(\text{box} \oplus c\big) \subset \text{slab},
\qquad
c = \begin{cases} \sigma & \text{uncertainty-safe mode}\\ 0 & \text{geometric mode,} \end{cases}
$$

$$
\text{yield}_{\text{bin}} = \frac{\sum_b V_b}{V_{\text{intact}}},
$$

so no block sits within the measured GPR uncertainty of a fracture; the
geometric (clearance-ignored) number is the optimistic bound. There is no
class named `UncertaintySafeYield`; the mechanism is this clearance toggle.
Code: `FractureBlockPackComponent.SolveSafe`,
`FractureBlockPackComponent.BlockInside`
(`src/Frahan.StonePack.GH/Quarry/FractureBlockPackComponent.cs`, GH layer);
sigma supplied by `Kriging.Sigma` via `GprFractureSurface3DComponent`.

---

# Part C. Block-cutting optimizers

## C1. BlockCutOpt (Elkarmoty 2020) pose search

### C1.1 Candidate grid

The tested region (AABB) is tiled with an oriented grid of candidate blocks of
size $(L_x, L_y, L_z)$ at kerf-inflated pitch:

$$
\text{pitch} = (L_x + k,\ L_y + k,\ L_z + k),
\qquad
R = R_z(\psi)\,R_x(\theta)\,R_y(\phi),
$$

centred on the region centroid, rotated by $R$, translated by $(dx, dy)$, and
clipped to blocks whose 8 corners lie inside the tested area. The OBB body
uses the un-inflated half-sizes; kerf only widens the pitch. Code:
`CuttingGrid.GenerateTilted` (`Masonry/Quarry/BlockCutOpt/CuttingGrid.cs`).
Citation ([Algorithm] attributes, `BlockCutOptComponents`): Elkarmoty, Bondua
& Bruno 2020, Resources Policy 68:101761 (DOI 10.1016/j.resourpol.2020.101761),
psi-only; the $(\theta, \phi)$ tilt axes are the Frahan I1 improvement.

### C1.2 Objective and recovery (Eq. 7-1 as coded)

$$
(\psi, \theta, \phi, dx, dy)^\star
= \operatorname*{arg\,max}\ N(\text{pose}),
\qquad
N = \#\{\, b : \text{no fracture triangle intersects OBB}_b \,\},
$$

exhaustive enumeration over the 5-D pose grid, triangle-OBB tests accelerated
by an AABB BVH (Akenine-Moller 2001 overlap test, Frahan I2); the parallel
solver reproduces the serial argmax bit-identically (ties go to the earliest
pose). Recovery:

$$
\text{recovery} = \frac{N \cdot L_x L_y L_z}{V_{\text{tested}} - V_{\text{kerf}}} \times 100,
\qquad
V_{\text{kerf}} \approx A_{\text{footprint}} \cdot \frac{k}{2},
$$

with the kerf volume approximated as a uniform thin film over the footprint
(explicitly marked Phase-1 grade in code). Code: `BlockCutOptSolver.Solve`,
`BlockCutOptSolver.SolveInternal`, `BlockCutOptSolver.SolveInternalSerial`,
`BlockCutOptSolver.ApproximateKerfVolume`, `TriangleAabbBvh.AnyTriangleIntersects`.

**Deviation / absence note:** the requested "guillotine DP recurrence"
(Gilmore & Gomory 1965 staged-guillotine dynamic program) is NOT implemented
anywhere in the BlockCutOpt stack; Gilmore-Gomory is cited as grounding in
`RecoveryCascade` and in [Algorithm] attributes
(`FractureBoundedSlabsComponent`, `BedBlockLayoutComponent`), but the shipping
solvers are (a) this exhaustive pose search, (b) the Kim 2025 randomized
guillotine TREE packer (`TreePackForest.Pack`: each placement splits the slab
into three axis-aligned sub-slabs along the element's free faces, split order
randomized, forest-of-trees restarts; a stochastic constructive method, not a
DP), and (c) a recursive full-span 3-D guillotine packer in
`FractureBlockPackComponent` (packer mode 5) whose recursion is
region $\to$ place flush block $\to$ split remainder by three full-span cuts
$\to$ recurse. No value-function recurrence $V(w, h) = \max(\ldots)$ exists in code.

## C2. SlabYieldOptimizer (per-block slab plan)

For each candidate plan (axis $a$, thickness $t$, kerf $k$) on a block with
AABB extent $E_a$ and cross-section area $A_\perp$:

$$
n = \left\lfloor \frac{E_a + k}{t + k} \right\rfloor,
\qquad
\text{yield} = \frac{n\, t\, A_\perp}{V_{\text{block}}},
\qquad
\text{score} = \text{yield} - \lambda_c \cdot \#\text{conflicts},
$$

picking the highest-scoring plan. A conflict is a fracture plane aligned with
the slab axis within tolerance whose anchor point lies in the block AABB:

$$
\big|\mathbf{n}_f \cdot \mathbf{e}_a\big| \ge \cos(\text{tol}),
\qquad \mathbf{p}_f \in \text{AABB},
$$

default $\lambda_c = 0.05$, tol $= 0.10$ rad. *Derivation of $n$:* $n$ slabs
consume $n t + (n-1) k \le E_a$, i.e. $n \le (E_a + k)/(t + k)$. Code:
`SlabYieldOptimizer.PickBest`, `SlabYieldOptimizer.CountConflicts`,
`SlabYieldOptimizer.ToFracturePlanes`
(`Masonry/Quarry/GeoCut/SlabYieldOptimizer.cs`). **Code note:** the crack
penalty is a linear score deduction (a heuristic), not a constraint; only
near-axis-parallel planes anchored inside the box count, so oblique fractures
are invisible to this scorer.

## C3. BlockYieldOptimizer (raw block to product blocks, fracture dodging)

### C3.1 Per-axis tiling

For raw length $L$, target size $s$ with tolerance band $[s - \tau,\ s + \tau]$
and kerf $k$, the largest size at count $n$ and the axis choice are

$$
\text{size}_n = \frac{L - (n-1)\,k}{n},
\qquad
n_{\max} = \left\lfloor \frac{L + k}{(s - \tau) + k} \right\rfloor,
$$

$$
(n, \text{size})^\star = \operatorname*{arg\,max}_{1 \le n \le n_{\max}}\
n \cdot \min\!\big(s + \tau,\ \text{size}_n\big)
\quad \text{s.t.}\ \text{size}_n \ge s - \tau,
$$

maximising the used length (the remainder is trim waste); the best of the 6
axis-to-target permutations wins. Geometric yield
$= n_x n_y n_z \cdot V_{\text{block}} / V_{\text{raw}}$. Code:
`BlockYieldOptimizer.Optimize`, `BlockYieldOptimizer.Axis`
(`Fabrication/BlockYieldOptimizer.cs`). Citation ([Algorithm] attribute,
`BlockYieldComponent`): waste-minimising rectangular cutting, size flexes
within tolerance.

### C3.2 Fracture dodging (grid phase search)

With fracture planes given in the block frame, the grid ORIGIN is slid within
the per-axis trim slack (a coarse 3-D phase grid, work-capped at
$\text{steps}^3 \cdot N_{\text{blocks}} \le 5\times10^5$):

$$
\boldsymbol\phi^\star = \operatorname*{arg\,max}_{\boldsymbol\phi \in [0, \text{trim}]^3}
\ \#\{\, b : \text{no fracture plane crosses } b \,\},
\qquad
\text{sound yield} = \frac{N_{\text{sound}}\, V_{\text{block}}}{V_{\text{raw}}},
$$

a plane crossing a box iff the 8 corner signed distances have mixed signs
(tolerance $10^{-7}$). Blocks fall BETWEEN fractures instead of across them.
Code: `BlockYieldOptimizer.OptimizePhase`, `BlockYieldOptimizer.Crosses`.

## C4. Wire-saw feasibility

A tensioned wire is straight at every instant, so the swept cut surface must be
RULED:

$$
S(u, v) = C(u) + v\,\mathbf{a}(u)
\quad \text{(straight lines in one parameter direction)},
$$

and a ruled surface that is also DEVELOPABLE ($K = 0$) is the cleanest
single-pass cut. As coded, on an $n \times n$ sample grid:

$$
\text{ruled} \iff \min\big(\max_i \operatorname{dev}_V(i),\ \max_j \operatorname{dev}_U(j)\big) \le \tau,
\qquad
\tau = 0.005 \cdot \operatorname{diag}(S),
$$

where $\operatorname{dev}$ is the max distance of an isocurve's interior sample
points from its endpoint chord (a degenerate chord, i.e. a closed/periodic
isocurve, returns the loop spread so a cylinder seam cannot read as a ruling);

$$
\text{developable} \iff \max |K| \cdot \operatorname{diag}^2 \le 10^{-2},
\qquad
\text{wire-sawable} \iff \text{planar} \lor \text{ruled},
$$

with the ruling twist reported as the max angle between consecutive rulings.
The kerf-compensated toolpath is the cut surface offset along its normal by

$$
\Delta = \frac{D + \delta}{2}
\qquad (D = \text{wire diameter},\ \delta = \text{vibration/positioning error}).
$$

Code: `WireSawFeasibility.Analyze`, `WireSawFeasibility.ChordDeviation`,
`WireSawFeasibility.BuildRulings` (`Fabrication/WireSawFeasibility.cs`).
Citation ([Algorithm] attributes, `WireSawFeasibilityComponent` +
`WireSawToolpathAdapterComponent`): robotic diamond-wire cutting of natural
stone, J. Comp. Design & Engineering 2024 (Zhang et al., 11(6):75-85,
DOI 10.1093/jcde/qwae094: developable ruled cut, kerf $\Delta = (D+\delta)/2$);
do Carmo (ruled/developable: $K = 0 \iff$ developable). **Code note:** the
feasibility inequality is a sampled chord-deviation test at tolerance
$\tau = 0.005 \times$ diagonal, not an exact ruled-surface certificate; a
swept-plane CLEARANCE check (wire access/collision) is not part of this class.

## C5. Cut-orientation optimizer

Choose an orthonormal cut frame $\{\mathbf c_1, \mathbf c_2, \mathbf c_3\}$
(right prisms by construction, grid $q = |\det| = 1$) maximising alignment
with the joint fabric:

$$
\max_{\{\mathbf{c}_i\}}\ \sum_{i=1}^{3} \big|\mathbf{c}_i \cdot \mathbf{n}_{m(i)}\big|,
\qquad
\text{obliquity}_i = \arccos\big|\mathbf{c}_i \cdot \mathbf{n}_{m(i)}\big|,
$$

with $m$ a greedy UNIQUE matching of cut normals to distinct joint poles
(all (axis, pole) pairs sorted by $|\cos|$ descending, strongest first).
Search spaces: bench-constrained mode pins $\mathbf c_1 = \mathbf z$ and sweeps
the vertical grid's strike over $[0, 180^\circ)$ in `azSteps` (1 DOF); free
mode samples $\mathbf c_1$ on a Fibonacci hemisphere (golden angle
$\pi(3 - \sqrt 5)$) times a roll sweep (3 DOF over SO(3)). Reported fit score
$= \tfrac13\sum_i |\cos(\text{obliquity}_i)|$, plus the natural fabric's
$q = |\det(\mathbf n_1, \mathbf n_2, \mathbf n_3)|$ for contrast. Code:
`CutOrientationOptimizer.Optimize`, `CutOrientationOptimizer.Score`
(`Fabrication/CutOrientationOptimizer.cs`). Citation ([Algorithm] attribute,
`CutOrientationOptimizerComponent`): orthogonal saw grid vs joint fabric,
maximise $\sum |\mathbf c \cdot \mathbf p|$; Palmstrom 2005 right-prism criterion.

## C6. RecoveryCascade (multi-scale re-cut recursion)

At each scale $s$ (coarse to fine) BlockCutOpt chooses the winning pose; the
winning grid is partitioned into kept (no fracture intersection) and cracked;
each cracked block's AABB is fed to the next finer scale, until the remnant
falls below the finest marketable volume. The recursion, verbatim from the
code header and matched by `Recurse`:

$$
W(R, s) = \sum_{b \in \text{kept}(R, s)} \mathrm{RMV}_s(b)
\; + \sum_{b \in \text{cracked}(R, s)}
\begin{cases}
W\big(\mathrm{AABB}(b),\ s + 1\big) & s + 1 < S \ \text{and} \ V_{\mathrm{AABB}(b)} \ge V_{\min}^{(s+1)}\\
\text{residual}(b) & \text{otherwise,}
\end{cases}
$$

with kept/cracked decided by the shared BVH predicate
`!bvh.AnyTriangleIntersects(obb)`. Recovery metrics:

$$
\text{RecoveryFraction} = \frac{\sum_s \sum_{b \in \text{kept}_s} V_b}{V_{\text{tested}}},
\qquad
\mathrm{BCSdbBV} = \frac{\sum_s A^{\text{cut}}_s}{\sum_s \text{Value}_s}
\quad \text{(Jalalian I11: cut area per recovered value)},
$$

plus per-tier kerf volume $= (\text{inflated} - \text{block})$ volume per
recovered block. With a single scale the cascade reduces exactly to
`BlockCutOptSolver.Solve` (same winning pose, same predicate). Depth is capped
at the number of scales, so recursion is bounded. Code: `RecoveryCascade.Run`,
`RecoveryCascade.Recurse`, `CascadeResult.RecoveryFraction`,
`CascadeResult.Bcsdbbv` (`Masonry/Quarry/BlockCutOpt/RecoveryCascade.cs`,
`CascadeResult.cs`). Citations (file header): Yarahmadi 2018 (conditional
two-scale); Cherri 2009 (usable-leftover threshold); Gilmore & Gomory 1965
(staged guillotine, grounding only, see C1.2 note); Hahn 1968 / Afsharian 2014
(defect-aware rough-mill cut-up); Jalalian 2023 (BCSdbBV); unified 3-D cascade
introduced in Murugean 2026 (submitted; deposit DOI 10.5281/zenodo.20608279).

## C7. Robustness and downstream estimates

### C7.1 Fisher-robust recovery (sensitivity to fracture-mapping error)

$M$ Monte-Carlo DFN realizations (JointSetDfnGenerator with seeds
$\text{base} + m$) are each solved by BlockCutOpt; the report is the empirical
recovery distribution and the robust direction:

$$
\{R_m\}_{m=1}^{M} \to (P_{10}, P_{50}, P_{90}, \bar R, \sigma_R),
\qquad
\psi_{\text{robust}} = \operatorname{median}_m\ \psi^\star_m .
$$

Code: `FisherRobustSampler.Solve`
(`Masonry/Quarry/BlockCutOpt/FisherRobustSampler.cs`). Citation ([Algorithm]
attribute, `BlockCutOptInspectorComponents`): Fisher-distribution joint-scatter
robustness sampling, Azarafza et al. 2016.

### C7.2 Per-bench yield estimate (proxies)

$$
V_{\text{recoverable}} = \min\!\big(N \cdot V_{\text{block}} \cdot G,\ V_{\text{gross}}\big),
\qquad
\text{risk} = \min\!\left(\frac{\#\{\text{overlapping fracture-tri AABBs}\}}{N_{\text{ref}}},\ 1\right),
$$

$$
t_{\text{cut}} \approx \frac{2\,(S_x + S_y)\cdot N}{v_{\text{feed}}}
\quad \text{(footprint-perimeter proxy at the Shao 2022 feed speed),}
$$

with $G$ the geology grade. All three are declared deterministic proxies in
the code header. Code: `BlockYieldEstimator.EstimateOne`,
`BlockYieldEstimator.ComputeFractureRisk`,
`BlockYieldEstimator.EstimateCuttingTimeMin`
(`Masonry/Quarry/CutOpt/BlockYieldEstimator.cs`).

### C7.3 AMRR in-block plane-sequence cutting (staged cuts that DO exist)

Iteratively cut a convex blank $Q$ toward a convex target $T$: at each step
find the vertex of $Q$ farthest from $T$, cut with the plane through it tangent
to $T$ (keeping $T$), until the outside volume is below a convergence fraction.
The objective is the average material-removal rate:

$$
\mathrm{AMRR} = \frac{\sum_i V_{r,i}}{\sum_i \tau_i}
\qquad \text{(total removed volume / total cutting time)},
$$

per-step instantaneous $\mathrm{MRR}_i = V_{r,i}/\tau_i$. Code:
`AmrrPlanner` (`Masonry/Quarry/BlockCutOpt/AmrrPlanner.cs`). Citation
([Algorithm] attribute, `BlockCutOptComponents`): Shao, Liu & Gao 2022,
Processes (MDPI), sections 2.4-2.6.

---

## Absent topics (declared, not fabricated)

- Bingham orientation statistics: not present anywhere in `src/`.
- Fisher $\kappa$ maximum-likelihood ESTIMATION from data: not present; only
  the $\kappa = (81/\text{scatter})^2$ conversion and Fisher SAMPLING exist.
- Gilmore-Gomory guillotine DP value recurrence: cited, never implemented
  (see C1.2); the shipping guillotine machinery is stochastic tree splitting
  (Kim 2025 port) and a recursive full-span splitter.
- Experimental variogram fitting (spherical/exponential model selection): not
  present; the kriging covariance is fixed Gaussian with NLML-fitted
  range/nugget (B4).
- A class named `UncertaintySafeYield`: does not exist; the uncertainty-safe
  yield is the sigma-clearance toggle in `FractureBlockPackComponent` (B6.3).
