# Frahan StonePack — Geology + Stone-CAM SOTA survey (2026-07-03)

Deep-research fan-out (5 angles, 24 primary sources, 118 extracted claims). The
Search + Fetch phases completed fully; the automated triple-verify + synthesis
were cut short by API rate limits, so this digest is synthesized by hand.

**Confidence labels:** `[CONFIRMED]` = passed 3-vote adversarial verification.
`[SOURCED]` = single extraction from the cited primary source, not triple-checked
— treat as a strong lead to confirm on the real case before relying on it.

Purpose: decide, per item, **BUILD** (stone-specific, small, differentiating) vs
**INTERFACE** (mature external tool, don't reimplement) — consistent with the
plugin's pre-CAM-stone-logic positioning.

================================================================================
## PART A — ROCK-MASS / DISCONTINUITY CHARACTERIZATION

### A1. Sampling-bias + fracture intensity — the cheapest high-value adds

- `[CONFIRMED]` **Modified Terzaghi (per-pole).** The current best-practice form
  drops the stereonet-cell meshing and applies `w = 1/sinθ` **per pole**, and is
  executed **separately for dip and dip-direction**. Validated field case
  Wenchuan. → *Our new Terzaghi component already uses the per-pole form; the one
  refinement to add is weighting dip and dip-direction separately.*
  (onlinelibrary.wiley.com/doi/10.1155/2018/1629039)
- `[CONFIRMED]` **Circular-window estimators (Mauldon-Dunne-Rohrbaugh 2001).**
  Orientation-bias-**free**, counts only: intensity `I = n/4r` (n = trace ×
  circular-scanline intersections), density `D = m/2πr²` (m = trace endpoints in
  the window), mean trace length `μ = (πr/2)(n/m)`. → **BUILD** — trivial, removes
  orientation bias without any weighting, complements Terzaghi.
  (pubs.geoscienceworld.org/aapg/aapgbull/article-abstract/86/12/2089; J. Struct.
  Geol. 23:247-258)
- `[CONFIRMED]` **Kulatilake-Wu (1984) censoring correction.** Corrects finite-
  window trace-length bias from only the **counts** in 3 classes (both ends seen /
  one end / both censored) — no measured lengths or length-PDF needed. → **BUILD** —
  cheap, standard, applies to trace maps. (link.springer.com/10.1007/BF01032335)
- `[CONFIRMED]` **Dershowitz-Herda P_ij** (P10 count/length, P21 length/area, P32
  area/volume, all L⁻¹) — matches our Fracture Intensity component. P10/P21/P32
  are the scale/orientation-independent measures preferred for modeling (also van
  Dijk 1998, Zhang-Einstein 2000). (ccgalberta.com/.../2010-103)
- `[CONFIRMED]` **P10→P32 is done by forward Monte-Carlo, not a closed form**
  (CCG FC1DtoP32: generate known DFN, sample with scanlines, regress). The
  conversion depends strongly on the fracture-size / domain-size ratio (worked
  example: 40 intersections on a 10 m borehole needs ~100 fractures of 8 m in a
  10 m cube). → *Our analytic `P32 = P10/|cosθ|` is the single-set special case;
  document it as such and offer the Monte-Carlo route for finite-size fractures.*

### A2. DFN generation / conditioning — INTERFACE, don't reimplement

| Code | Impl / license | Conditioning | Verdict |
|------|----------------|--------------|---------|
| **HatchFrac** `[SOURCED]` | open **C++**, code CC-BY-4.0 (Zhu/Khirevich/Patzek 2022) | inverse-CDF + accept-reject; **+MLP for arbitrary sampling distributions**; Newman-Ziff connectivity | closest to a **wrappable native lib** |
| **DFN.Lab** `[SOURCED]` | **C++** core + Python API, GitLab (Fractory/ITASCA/CNRS) | statistical OR genetic growth rules; flow/mech/transport | heavy (full physics), interface only |
| **ADFNE** `[SOURCED]` | MATLAB, BSD (Fadakar-Alghalandis 2017) | GenFNM2D/3D + connectivity | MATLAB dep — reference only |
| **MoFrac** `[SOURCED]` | commercial, closed (MIRARCO) | **conditions to field CLD/CAD** (length + area distributions) → matches measured P21/P32 | the conditioning model to emulate |
| **FracMan / pyfracman** | commercial / (unverified) | industry standard | export target |

→ **Keep** the in-plugin Baecher + Fisher generator for quick DFNs. For rigorous
**conditioning to measured P32 / trace maps**, the plugin should **export** the
joint-set statistics and hand off to these — HatchFrac (open C++) is the only one
plausibly wrappable natively if we ever want it in-process. MoFrac's CLD/CAD
conditioning is the algorithm to mirror if we build our own conditioner later.

### A3. In-situ block-size distribution (IBSD) — BUILD (small, ties geology→fab)

- `[SOURCED]` Analytic block volume **V = S₁S₂S₃ / q**, shape coefficient
  `q = sin(γ₁₂)cos(δ₃₋₁₂) = …` (q=1 orthogonal). Monte-Carlo over **Fisher**
  orientations (±10° tested) and fitted spacing PDFs (normal / neg-exponential),
  ~1000 samples. Palmström (2001) block-shape classes (equidimensional / bar /
  blade / slab) via edge ratios; new non-orthogonality classes at q = 0.50/0.75/
  0.95 (q≥0.95 ≈ right prism, 5% tol). (link.springer.com/10.1007/s00603-024-04363-x)
- `[SOURCED]` **The reason to build it:** in the Lorgino *Palissandro marble*
  quarry case, diamond-wire cut planes were scored against joint orientation —
  for a 6×3.5×2.5 m bench slice, **only foliation-parallel cuts dipping >~72°
  yield right-prism blocks**. This is exactly the geology→fabrication bridge that
  is the plugin's unique value: block yield/shape as a function of cut orientation
  vs the fabric. → **BUILD** an IBSD Monte-Carlo on top of the existing
  BlockSizeMath (we already have V=S₁S₂S₃/q via Palmström Vb).

### A4. GPR — INTERFACE to open tools (our stack is picking-level; migration is the gap)

- `[SOURCED]` **GPRPy** (Python, MIT per repo) — profile processing, **velocity
  analysis**, 3D interpolation. **RGPR** (R, **GPL**) — migration, deconvolution,
  hyperbola fitting, reads 18+ formats. **gprMax** (Python/Cython, **GPLv3**) —
  FDTD forward modelling (Warren-Giannopoulos-Giannakis 2016).
- `[SOURCED]` Migration SOTA = **Kirchhoff** (diffraction-sum) + **F-K/Stolt**;
  ML picking exists (**GPRNet**, Keras/TF, **no LICENSE file** → reuse unclear).
  A GSSI **70 MHz** survey on a dimension-stone quarry bench correlated GPR with
  lab rock-quality classes (combined NDT + lab workflow).
- → **INTERFACE**: our GPR is amplitude/pick-level; **migration/velocity is the
  depth gap but is best delegated** — ingest GPRPy/RGPR-processed sections rather
  than building a migrator. (seg TLE39050332; github gprMax; emanuelhuber RGPR;
  agupubs 10.1029/2020JB021047)

### A5. Kinematic / block theory
Our Markland/Hoek-Bray/Goodman-Bray screen is standard. Next depth (from the
brief, verification pending): Goodman-Shi block theory (removability), JRC +
Barton-Bandis joint shear strength as friction inputs — no confirmed source landed
this run; flag for a targeted follow-up.

================================================================================
## PART B — STONE FABRICATION / CAM READINESS  (interface strategy CONFIRMED)

### B1. Interchange formats — export neutral, never emit G-code
- `[SOURCED]` **ALPHACAM Stone** imports **DXF, DWG, IGES, STEP** (+ solids);
  emits machine-specific output via **per-machine post-processors** (G-code for
  3-axis routers → **joint positions for robots**); true-shape **xNesting** with
  grain direction. (alphacam.com/alphacam-stone)
- `[SOURCED]` **DDX EasySTONE** imports **DXF, IGES, STL, PNT, and Rhino 3DM** (no
  STEP advertised); auto-CAM toolpaths, 3/4/5-axis incl. continuous 5-axis,
  **Nesting with vein-matching**, collision-control simulation.
  (ddxgroup.com/.../easystone)
- → **Export DXF** (the countertop-template lingua franca) + **STEP/3DM** for
  solids + our **annotated cut plan (CSV/IDs)**. **3DM straight into EasySTONE is
  the smoothest handoff** (native Rhino). The machine post-processor is *their*
  job — do **not** emit G-code.

### B2. Wire-saw cut feasibility — BUILD (stone-specific, nobody else does this)
- `[SOURCED]` Robotic diamond-wire cutting (JCDE 2024): each cut is a
  **developable ruled surface**, enforced by the orthogonality constraint
  **C′(u)·a(u) = 0** (directrix tangent ⟂ ruling). Kerf/tolerance offset
  **Δ = (D+δ)/2** (wire dia + vibration; example 1.75 mm) applied as
  `R*(u,v) = C(u) + ΔN(u) + v·a(u)`; achieved **±2 mm**, no overcut.
  (academic.oup.com/jcde/article/11/6/75/7875232)
- `[SOURCED]` A KUKA-mounted diamond-wire bandsaw does robotic stereotomy with
  ~zero kerf waste; cut faces striate (need a finishing pass); assemblies need
  grout. (thinkmoult.com robotic-stereotomy)
- → **BUILD** a wire-sawability check: verify each designed cut is a developable
  ruled surface (the `C′·a=0` test) and apply the kerf-offset budget. This is the
  exact pre-CAM validator proposed in the release plan and the clearest
  differentiator.

### B3. Robot handoff — INTERFACE (poses/frames, not motion planning)
- `[SOURCED]` **COMPAS FAB** standard 5 frames (WCF/RCF/T0CF/TCF/OCF), ROS
  convention (WCF = ROS `map`, Z up); `Frame` class + to_local/to_world.
  (gramaziokohler.github.io/compas_fab)
- `[SOURCED]` **RoboDK**: per-brand post-processors (RAPID/ABB, KRL·SRC/KUKA,
  INFORM/Motoman, TP·LS/FANUC, URScript/UR); target = **4×4 homogeneous TCP pose**
  or **6×1 joint vector**; 300+ robots, Python API. (robodk.com/blog/off-line-programming)
- → **INTERFACE**: emit cut poses as **compas.geometry.Frame** (ROS/compas_fab)
  and/or **4×4 matrices** (RoboDK). Our PlanesToRobotTargets / NboPoseToRobotFrame
  already produce targets — align them to these two conventions. Don't do motion
  planning / reachability (that's RoboDK/ROS).

================================================================================
## BOTTOM LINE — build vs interface

**BUILD (stone-specific, small, differentiating):**
1. Circular-window intensity (A1) — trivial, orientation-bias-free.
2. Kulatilake-Wu trace-length censoring (A1) — counts-only.
3. IBSD Monte-Carlo (A3) — block yield/shape vs cut orientation; the geology→fab bridge.
4. Wire-saw developability + kerf feasibility check (B2) — the pre-CAM differentiator.
5. Terzaghi refinement: weight dip and dip-direction separately (A1).

**INTERFACE (mature — export to, don't reimplement):**
- DFN conditioning → HatchFrac (open C++) / DFN.Lab / MoFrac (A2).
- GPR migration/velocity → GPRPy (MIT) / RGPR (GPL) / gprMax (GPL) (A4).
- Stone CAM → DXF + STEP + 3DM to ALPHACAM / DDX EasySTONE (B1).
- Robot → compas_fab Frames / RoboDK 4×4 poses (B3).

**Confirms the plugin's thesis:** the new Terzaghi/P32/kinematic primitives are
the right SOTA (modified-Terzaghi per-pole, Dershowitz P_ij, Mauldon windows), and
the *interface-not-reimplement* fabrication strategy is validated by the format /
post-processor landscape (everyone imports DXF/STEP/3DM and posts per-machine).

Next per the plan: verify these on real cases + on canvas before building.
