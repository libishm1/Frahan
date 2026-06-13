# 09. Kintsugi & Learned 6-DoF Pose

The `Kintsugi` ribbon tab (7 components) is the repository's fracture
reassembly subsystem: given a set of broken fragments, recover the rigid
6-DoF pose of each so they snap back into the original solid. The tab has two
back ends behind one component. The default is the deterministic geometric
matcher of Chapter 08, which assembles fragments from their naked-edge rim
loops and runs entirely in-process with no learned model. The opt-in second
back end is `Frahan.Kintsugi.Port`, a managed C# port of the learned
PuzzleFusion++ pipeline (Wang, Chen and Furukawa 2025): a PointNet++ encoder,
a VQ-VAE latent, an SE(3) diffusion denoiser, and a learned pairwise
verifier. This is the production 3D reassembly path that Chapter 08
forward-references for fragments whose fracture rims are too smooth for the
geometric matcher to find correspondences.

This chapter derives the learned path's mathematics: the diffusion schedule
and DDPM update the denoiser runs under, the per-fragment normalisation and
its undo, the world-pose composition that lifts a normalised-space SE(3)
prediction back into document coordinates, and the verifier gate that decides
which fragments to trust. The single most important originality and licensing
fact governs the whole chapter and is stated first: the learned path is a
**direct port** of a non-commercial research-licensed upstream, quarantined
in a separate research-only assembly that the default install does not ship.

The truth criterion for this chapter is the live HITL validation of example
14: two Breaking Bad parity fragments reassembled at verifier pair score
0.7068 (STRONG, above the 0.5 gate), zero unplaced, 20 diffusion steps
(`examples/14_kintsugi/README.md`).

---

## 9.1 Why a learned path at all

The geometric matcher of Chapter 08 assembles by matching fracture-rim
geometry: it segments each naked-edge loop, hashes a rotation-invariant
curvature signature, phase-correlates candidate segments, refines with
constrained ICP (Besl and McKay 1992; Kabsch 1976), and beam-searches an
SE(3) assembly. It is deterministic, GPL-free, and best on clean, sharp
fracture rims. It fails on smooth or featureless fracture surfaces, where
there are no distinctive rim segments to hash. Example 14 records the failure
mode directly: a synthetic Voronoi shatter of a sphere gives the geometric
matcher no rim segments to lock onto and only 1 of 6 fragments place
(`examples/14_kintsugi/README.md`).

The learned path replaces hand-crafted rim descriptors with a network trained
on the Breaking Bad fracture dataset (Sellan et al. 2022). It poses fragments
from their full surface point clouds, not just rim loops, so it works where
the rim signal is weak. The cost is a non-commercial research licence, a
~267 MB weight file, and a heavier compute budget (Section 9.6). The two paths
are kept as one component with a `Use Port Mode` toggle; the geometric path
stays the default (`KintsugiAssemblyComponent.cs:68`).

---

## 9.2 The diffusion schedule and the DDPM update

The learned poser is a denoising diffusion model. It starts from a Gaussian
noise pose and walks it down a schedule of timesteps, at each step predicting
the noise to subtract. The repository ports PuzzleFusion++'s **custom**
schedule, not the standard linear-beta DDPM (Ho, Jain and Abbeel 2020). The
cumulative signal retention $\bar\alpha(t)$ is a piecewise quadratic in the
normalised time $\tau = t/(T-1) \in [0,1]$ with $T = 1000$ training steps
(`DiffusionScheduler.cs:59-71`):

$$
\bar\alpha(\tau)=
\begin{cases}
1 - 0.1\,\bigl(\tfrac{\tau}{0.7}\bigr)^2, & \tau \le 0.7,\\[4pt]
0.9\,\Bigl(1 - \bigl(\tfrac{\tau-0.7}{0.3}\bigr)^2\Bigr), & \tau > 0.7.
\end{cases}
$$

The per-step variance follows from the standard DDPM relation
$\beta_t = 1 - \bar\alpha_t/\bar\alpha_{t-1}$, clipped to $[0, 0.999]$ to keep
$\alpha_t = 1-\beta_t$ a valid retention factor (`DiffusionScheduler.cs:48-56`).
Inference subsamples the 1000-step schedule to $S$ descending timesteps with
diffusers' "leading" spacing: step size $\lfloor T/S\rfloor$, timesteps
$t_i = (S-1-i)\lfloor T/S\rfloor$ for $i = 0,\dots,S-1$, so the loop runs from
high noise to low (`DiffusionScheduler.cs:75-82`). The default $S=20$.

**The DDPM step (epsilon parameterisation).** Given the predicted noise
$\epsilon_\theta(x_t,t)$, the model first recovers the clean pose estimate by
inverting the forward process $x_t = \sqrt{\bar\alpha_t}\,x_0 +
\sqrt{1-\bar\alpha_t}\,\epsilon$:

$$
\hat x_0 = \frac{x_t - \sqrt{1-\bar\alpha_t}\;\epsilon_\theta(x_t,t)}{\sqrt{\bar\alpha_t}} .
$$

It then forms the posterior mean of $x_{t-1}$ as the standard DDPM convex
combination of $\hat x_0$ and $x_t$
(`DiffusionScheduler.cs:124-129`):

$$
x_{t-1} = \underbrace{\frac{\sqrt{\bar\alpha_{t-1}}\,\beta_t}{1-\bar\alpha_t}}_{c_{x_0}}\;\hat x_0
\;+\;
\underbrace{\frac{\sqrt{\alpha_t}\,(1-\bar\alpha_{t-1})}{1-\bar\alpha_t}}_{c_{x_t}}\;x_t .
$$

**Original derivation of the two terminal guards.** The posterior coefficients
divide by $1-\bar\alpha_t$. At the last inference step $t=0$ the schedule gives
$\bar\alpha_0 = 1$ (zero noise), so $1-\bar\alpha_t \to 0$ and the posterior
mean is $0/0$. The port branches: at $t=0$ it returns $\hat x_0$ directly, the
clean predicted pose, which is exactly the diffusers' $t=0$ branch
(`DiffusionScheduler.cs:108-111`). A second guard catches the same singularity
on any subsampled step where $1-\bar\alpha_t$ is numerically tiny: if
$1-\bar\alpha_t < 10^{-6}$ it again returns $\hat x_0$ rather than amplify
round-off through the reciprocal (`DiffusionScheduler.cs:120-123`). The port is
no-added-noise DDPM, not DDIM (`DiffusionScheduler.cs:25-26`); the schedule is
deterministic given the seed.

A pose is a 7-vector $[\,\mathbf t\;(3)\mid \mathbf q\;(4)\,]$: translation
first, then a unit quaternion $(w,x,y,z)$. The diffusion runs in this layout
(`KintsugiPortInference.cs:31-34`). Quaternions are re-normalised to the unit
sphere after every scheduler step (`KintsugiPortInference.cs:306-322`); the
model is trained to tolerate the off-manifold drift the linear update
introduces and the normalisation projects back.

> **Originality.** The scheduler is a **direct port** of
> `puzzlefusion_plusplus/.../custom_diffusers.py`, named as such at
> `DiffusionScheduler.cs:7-9`. The piecewise-quadratic $\bar\alpha$ and the
> epsilon-DDPM update are upstream; the two terminal guards
> (`:108-111`, `:120-123`) are the only port-side additions, and they
> reproduce the diffusers reference behaviour rather than change it.

---

## 9.3 The encode-in-the-loop architecture and its cost

The port mirrors the upstream `AutoAgglomerative.test_denoiser_only` schedule
step-for-step, and the orchestrator header flags the one place a naive
re-implementation goes wrong (`KintsugiPortInference.cs:11-43`). The encoder
is **re-run inside the denoising loop**, not once up front. At each timestep
the current noisy quaternion is applied to each fragment's point cloud, the
**rotated** cloud is fed through the PointNet++ encoder (Qi et al. 2017) and
VQ-VAE quantiser (van den Oord, Vinyals and Kavukcuoglu 2017), and the
denoiser conditions on encoder features of the **currently-estimated** pose
(`KintsugiPortInference.cs:200-261`). The denoiser is a 6-block, 8-head, 512-D
AdaLN-conditioned transformer (Vaswani et al. 2017; conditioning after
Peebles and Xie 2023) predicting a 7-D residual per fragment
(`Se3Denoiser.cs:12-22`).

The rotation is applied as the explicit quaternion-to-matrix form
$R(\mathbf q)$, then each point is mapped $p \mapsto R(\mathbf q)\,p$
(`KintsugiPortInference.cs:222-233`). The VQ step is essential and was a
documented earlier omission: the SA3 features are compressed by `conv6` from
512-D to 64-D, reshaped to $4L$ rows of 16, and each row is snapped to its
nearest of 1024 codebook entries, because the denoiser was trained on the
quantised latent $z_q$, not the raw encoder output $z_e$
(`KintsugiPortInference.cs:374-412`).

**The anchor convention.** Fragment $0$ (or the chosen `anchorIndex`) is the
reference. Its pose is **pinned to identity** every step ($\mathbf t = 0$,
$\mathbf q = (1,0,0,0)$) and never noised; all other fragments are predicted
relative to it, and the reset after each scheduler step prevents the anchor
from drifting (`KintsugiPortInference.cs:169-171`, `:303-304`). This is what
makes the assembly well-posed: the network predicts relative SE(3), so one
fragment must define the world frame.

The compute cost is linear in fragments $F$ and steps $S$: $F\cdot S$ encoder
forwards plus $S$ denoiser forwards plus one verifier pass per pair. The header
budgets it honestly for the libtorch path: $F{=}10, S{=}5 \approx 165$ s;
$F{=}10, S{=}20 \approx 660$ s (`KintsugiPortInference.cs:36-43`). This is why
the component is async with a default-false `Run` gate (the heavy-node rule of
`AGENTS.md` §6; `examples/14_kintsugi/README.md`).

> **Originality.** The orchestrator and the encoder, denoiser, VQ-VAE and
> verifier modules are a **direct port** of the PuzzleFusion++ Python
> reference. The header states it: "Mirrors upstream's
> `auto_aggl.py::AutoAgglomerative.test_denoiser_only` step-for-step"
> (`KintsugiPortInference.cs:11-13`). Module headers cite the upstream file
> and paper section for each component (e.g. `Se3Denoiser.cs:12`,
> `DiffusionScheduler.cs:7`). A dual TorchSharp/libtorch denoiser path exists
> for paper-exact kernels, with a silent-fallback report flag so the
> component surfaces whether the manual port (with ~3-5% drift) or libtorch
> actually ran (`KintsugiPortInference.cs:57-72`, `:1131-1142`).

---

## 9.4 Per-fragment normalisation and the pose-composition fix

The network operates in **per-fragment normalised space**: before encoding,
each point cloud is centred at its own centroid and scaled so its maximum
absolute coordinate is 1 (`KintsugiAssemblyComponent.cs:1303-1334`). Define for
fragment $f$ the captured centroid $\mathbf c_f$ and scale $m_f = \max_i
\lVert p_i\rVert_\infty$. The normalisation map is

$$
T_{\mathrm{norm}}(f) = \operatorname{scale}\!\bigl(\tfrac{1}{m_f}\bigr)\cdot \operatorname{translate}(-\mathbf c_f),
$$

so a document-space point $p$ maps to $T_{\mathrm{norm}}(f)\,p$ in the unit
cube the encoder expects. The network returns a pose $T_{\mathrm{net}}(f)$ that
is an SE(3) transform **in that normalised frame**, not in document
coordinates.

Applying $T_{\mathrm{net}}(f)$ directly to a document-coordinate mesh is the
2026-05-24 misalignment bug: it rotates the mesh about the world origin and
translates by sub-unit distances, collapsing every fragment onto a blob
(`KintsugiAssemblyComponent.cs:1008-1014`). The fix composes three transforms.
Each non-anchor fragment is brought into **its own** normalised frame, posed by
the network, then lifted into the **anchor's** world frame:

$$
\boxed{\,T_{\mathrm{world}}(f) = T_{\mathrm{unnorm}}(0)\cdot T_{\mathrm{net}}(f)\cdot T_{\mathrm{norm}}(f)\,}
$$

with the anchor's un-normalisation (the inverse of the anchor's own
normalisation)

$$
T_{\mathrm{unnorm}}(0) = \operatorname{translate}(+\mathbf c_0)\cdot \operatorname{scale}(m_0),
$$

assembled verbatim at `KintsugiAssemblyComponent.cs:1098-1102` and
`:1032-1036`.

**Original derivation of the anchor collapse.** Read right to left,
$T_{\mathrm{world}}(f)$ first sends fragment $f$'s document points into its
unit-cube frame ($T_{\mathrm{norm}}(f)$), applies the network's
normalised-space placement ($T_{\mathrm{net}}(f)$), then maps the result out of
the **anchor's** unit-cube frame back to document space
($T_{\mathrm{unnorm}}(0)$). The mixed indices, $f$ on the way in and $0$ on the
way out, are deliberate: the network poses every fragment relative to the
anchor's normalised frame, so the inverse must undo the anchor's normalisation,
not fragment $f$'s. For the anchor itself ($f=0$) the orchestrator forces
$T_{\mathrm{net}}(0)=I$, and then

$$
T_{\mathrm{world}}(0) = T_{\mathrm{unnorm}}(0)\cdot I\cdot T_{\mathrm{norm}}(0) = T_{\mathrm{unnorm}}(0)\,T_{\mathrm{norm}}(0) = I,
$$

because un-normalisation is the exact inverse of normalisation. The anchor
therefore stays at its input document position, which is the desired identity
behaviour (`KintsugiAssemblyComponent.cs:1025-1028`, `:1081`, `:1106-1108`).
This is the "norm-undo" the originality ledger names: the network pose is only
meaningful sandwiched between the forward and inverse normalisations.

> **Originality.** The normalisation, its captured-parameter undo, and the
> three-factor world composition are the **Frahan-original pose-composition
> fix** over the port. The `[DesignApplication]` precedent line names it
> explicitly: "Frahan-original pose composition fix"
> (`KintsugiAssemblyComponent.cs:81`). The network it wraps is the direct port;
> the composition that makes the port usable in document coordinates is the
> repository's contribution and is documented inline as the fix for a named
> HITL failure.

---

## 9.5 The verifier and the 0.5 gate

The network produces a pose for every fragment whether or not the prediction is
trustworthy. A weak prediction must not be applied, or it collapses the
fragment onto the anchor through the same composition that works for strong
ones. The learned verifier is the gate. It is a small transformer classifier
that, for each fragment pair, projects the pair's edge feature to the embed
dimension, runs a 1-token transformer stack, and maps the result through a
linear head to a logit that a sigmoid turns into an acceptance probability
$p\in[0,1]$ (`Verifier.cs:58-78`, `VerifierTransformerPort`):

$$
p_{ij} = \sigma\!\bigl(\mathbf w^\top \operatorname{Transformer}(\,\text{proj}(\phi_{ij})\,) + b\bigr),\qquad
\sigma(z)=\frac{1}{1+e^{-z}} .
$$

In the orchestrator the edge feature $\phi_{ij}$ for the upper-triangular pair
$(i,j)$ is fragment $j$'s final 7-D pose, and every pair is scored
(`KintsugiPortInference.cs:326-349`). The logits are passed through the same
sigmoid to produce the reported per-pair scores.

**Per-fragment confidence and the gate.** The component reduces the pairwise
scores to a per-fragment confidence as the **maximum** score over all pairs
containing that fragment (`KintsugiAssemblyComponent.cs:1056-1070`):

$$
\text{conf}(f) = \max_{j\ne f} p_{\{f,j\}} .
$$

A fragment is accepted (its network pose applied) only if it is the anchor or
its confidence clears the threshold:

$$
\text{accept}(f) = (f = \text{anchor}) \;\lor\; \text{conf}(f) \ge V_t,\qquad V_t = 0.5\ \text{(default)} .
$$

A rejected fragment is held at its input world position (identity transform)
and listed as Unplaced, exactly as PuzzleFusion++'s auto-agglomerative schedule
leaves a weak-pair fragment out of the anchor cluster
(`KintsugiAssemblyComponent.cs:1081-1089`). The threshold default of 0.5 is the
same value that tags a pair "STRONG" in the report
(`KintsugiAssemblyComponent.cs:1052-1055`).

**Why the gate exists (the blob).** The header records the failure that
motivated it: the 5-fragment Breaking Bad sample produced only one strong pair
(top $(3,4)=0.549$, the rest below 0.5). Without gating, every weak-prediction
fragment was composed as $T_{\mathrm{unnorm}}(0)\cdot I\cdot T_{\mathrm{norm}}(f)$
and collapsed onto the anchor's centroid, the blob the user saw on 2026-05-24
(`KintsugiAssemblyComponent.cs:1046-1050`). The gate is the diagnostic too:
reading the verifier score distribution before the poses distinguishes a
pose-composition fault from a network-drift or a mesh-style fault
(`examples/14_kintsugi/README.md`, the "read scores first" rule).

![Two Breaking Bad fragments reassembled by the learned Kintsugi port: the gold neck cloud and the blue body cloud meet at the fracture interface, pair score 0.71 STRONG, zero unplaced.](../examples/14_kintsugi/14_kintsugi_result.png)

> **Originality.** The verifier is a **direct port** of the PuzzleFusion++
> learned binary classifier (`Verifier.cs:7-22`,
> `VerifierTransformerPort`); the sigmoid head and transformer stack are
> upstream. The confidence-reduction and 0.5 gate that turn the per-pair
> probabilities into a per-fragment accept/reject are the port-side
> integration that keeps weak predictions from collapsing the assembly. The
> geometric mode ships a separate **Frahan-original** penetration-based
> verifier that rejects placements whose transformed mesh interpenetrates an
> already-placed mesh (`KintsugiAssemblyComponent.cs:75-77`); the two
> verifiers are not the same code and the learned one runs only in Port mode.

---

## 9.6 Originality and the licence-critical quarantine

The learned path is classified **direct-port**, and this is the single
licensing risk that governs the whole repository's distribution posture.

The upstream is PuzzleFusion++ (Wang, Chen and Furukawa 2025, ICLR;
arXiv:2406.00259). Its licence is **research-use-only / non-commercial**, not
plain GPL-3.0, and that obligation covers both the ported C# code **and** the
converted weight file `kintsugi.bin` (~255-267 MB) derived from the upstream
checkpoint (`docs/thesis/90_originality.md`, register row 2, flag E1
CRITICAL). The port also transitively carries the upstream's vendored
`jigsaw_matching` subtree (Lu et al.), whose own MIT grant is unaudited against
its original repository (register row 3, flag E1/jigsaw).

The load-bearing mitigation is architectural quarantine. `Frahan.Kintsugi.Port`
is a **separate assembly**, isolated from the default `Frahan.StonePack.gha`,
and the weights are gitignored and absent from the default install: example 14
warns and falls back to the geometric path when `kintsugi.bin` is missing
(`examples/14_kintsugi/README.md`, "REQUIRED: kintsugi.bin"). Because the
default install links no part of the port and ships no converted weights,
nothing in the default-install algorithm path is a line-by-line port of a
competitor (`90_originality.md`, posture summary). The mitigation is only valid
while the split holds: the register requires the root LICENSE, the port README,
and any repo-root statement to all say research-only / non-commercial, not
plain GPL-3.0, before any public release (register rows 1-2).

> **Originality.** `Frahan.Kintsugi.Port` is **direct-port (research-only)**.
> Evidence: it is a C# port of PuzzleFusion++, headers cite the upstream Python
> file per module (`KintsugiPortInference.cs:11-13`, `Se3Denoiser.cs:12`,
> `DiffusionScheduler.cs:7`, `Verifier.cs:11`), and the ledger lists it as the
> sole `direct-port` in the thesis, quarantined in a non-commercial
> research-only assembly (`90_originality.md`, Chapter 08 row;
> register rows 1-3). The norm-undo and the verifier-gated world-pose
> composition $T_{\mathrm{world}}(f)=T_{\mathrm{unnorm}}(0)\cdot T_{\mathrm{net}}(f)\cdot T_{\mathrm{norm}}(f)$
> are the **Frahan-original** wrapper around the port, not part of the ported
> network. The default `Mode=Geometric` path is the clean-room edge-matching
> assembler of Chapter 08 and links no GPL or non-commercial code.

The honest boundary on capability is held in source too. Port mode reproduces
the paper's behaviour **only** on the Breaking Bad test distribution it was
trained on; a synthetic Voronoi shatter does not reassemble, and the example
deliberately loads a real Breaking Bad parity sample where the verifier clears
the gate at 0.71 (`examples/14_kintsugi/README.md`). The manual C# denoiser
carries a stated ~3-5% drift versus the libtorch kernels, which is why the
TorchSharp path exists and why the component reports which path actually ran
(`KintsugiPortInference.cs:74-81`, `:1131-1142`).

---

## 9.7 Status and what is left

- **Licence quarantine must be verified before any public release.** The root
  LICENSE is a header, not the full text, and must state research-only /
  non-commercial (not plain GPL-3.0) across the root LICENSE, the port README,
  and the converted weights. The combined work cannot ship commercially while
  the port is linked (`90_originality.md`, register rows 1-2, flag E1). *Blocker
  for external/commercial distribution.*
- **`jigsaw_matching` subtree unaudited.** Its MIT grant is unverified against
  the original repository; treated conservatively under the parent
  non-commercial terms and not compiled into StonePack (register row 3).
  *High.*
- **Manual-port drift.** The pure-C# denoiser drifts ~3-5% from the libtorch
  kernels; the TorchSharp path removes it but needs LibTorchSharp.dll and a
  working CUDA/CPU libtorch, with a documented silent-fallback to the manual
  port (`KintsugiPortInference.cs:74-114`). *Medium.*
- **Distribution-only generalisation.** Port mode reassembles reliably only on
  Breaking Bad-like fractured-scan fragments; synthetic primitives and smooth
  rims under-place. For arbitrary data the geometric path is the safer default
  on clean rims (`examples/14_kintsugi/README.md`). *Medium (honesty bound, not
  a code fault).*
- **`AutoAgglomerate` outer loop is a skeleton.** The full auto-agglomerative
  multi-round merge with point-match deletion and FPS resampling is wired but
  the per-round merge body is stubbed (`AutoAgglomerate.cs:120-147`,
  `BuildPairFeatures` `:177-192`); the shipped path is the single-round
  `KintsugiPortInference` denoise-then-verify, not the iterative paper
  schedule. *Medium.*
- **Stale `[Algorithm]` wording.** The GH component attribute still reads
  "Full GPL-3.0 honest port ... underway" and "Phase 0 (current): ... NO
  learned model" (`KintsugiAssemblyComponent.cs:62-68`), describing a
  pre-port state. The learned port has landed and the licence is
  non-commercial, not plain GPL-3.0; the attribute should be corrected to match
  the ledger before academic review (`AGENTS.md` §9). *Low.*
- **Compute budget.** $F\cdot S$ encoder forwards make large-$F$ assemblies
  slow (10 fragments at 20 steps ~660 s on GPU); the async gate keeps the
  canvas responsive but the wall-clock is real (`KintsugiPortInference.cs:36-43`).
  *Low.*

---

## References (this chapter)

- Wang, Z., Chen, B., Furukawa, Y. (2025). PuzzleFusion++: auto-agglomerative
  3D fracture assembly by denoising and verification. ICLR 2025.
  arXiv:2406.00259. (Upstream of the direct port; non-commercial research
  licence.) [R112]
- Sellan, S., Chen, Y.-C., Wu, Z., Garg, A., Jacobson, A. (2022). Breaking Bad:
  a dataset for geometric fracture and reassembly. NeurIPS 2022 Datasets and
  Benchmarks. [R113]
- Ho, J., Jain, A., Abbeel, P. (2020). Denoising diffusion probabilistic
  models. Advances in Neural Information Processing Systems 33:6840-6851.
  arXiv:2006.11239.
- Qi, C.R., Yi, L., Su, H., Guibas, L.J. (2017). PointNet++: deep hierarchical
  feature learning on point sets in a metric space. Advances in Neural
  Information Processing Systems 30. arXiv:1706.02413.
- van den Oord, A., Vinyals, O., Kavukcuoglu, K. (2017). Neural discrete
  representation learning (VQ-VAE). Advances in Neural Information Processing
  Systems 30. arXiv:1711.00937.
- Vaswani, A., Shazeer, N., Parmar, N., Uszkoreit, J., Jones, L., Gomez, A.N.,
  Kaiser, L., Polosukhin, I. (2017). Attention is all you need. Advances in
  Neural Information Processing Systems 30. arXiv:1706.03762.
- Peebles, W., Xie, S. (2023). Scalable diffusion models with transformers
  (DiT, adaptive layer norm conditioning). IEEE/CVF ICCV 2023:4195-4205.
  arXiv:2212.09748.
- Besl, P.J., McKay, N.D. (1992). A method for registration of 3-D shapes. IEEE
  Transactions on Pattern Analysis and Machine Intelligence 14(2):239-256. DOI
  10.1109/34.121791. [R101]
- Kabsch, W. (1976). A solution for the best rotation to relate two sets of
  vectors. Acta Crystallographica A32:922-923. DOI
  10.1107/S0567739476001873. [R102]
