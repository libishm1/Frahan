# The mathematics of the packing stack

Code-bound equation layer for the shipping packers. Each equation: statement,
short derivation, code provenance, method-class citation. Derived from the
definitions and the implementation, not copied from paper figures. Renders
natively on GitHub and on the docs site (MathJax).

Shipping implementations: `ContactNfpHoleNester` (2D, exact NFP-BLF,
hole-aware, boundary mode), `Clipper2Adapter` (integer Minkowski/Boolean
back-end), the heightmap/mesh 3D packers, `CoacdMeshDecompose` +
`BulletSettleService` (3D settle). The reference derivations were validated in
the evolution harnesses before the C# ports (see
[`SYNTHESIS_2D`](SYNTHESIS_2D.md), [`SYNTHESIS_3D`](SYNTHESIS_3D.md)).

---

## 1. Exact-NFP Bottom-Left-Fill nesting (2D)

### 1.1 Placement transform

A part is placed by rotating its local polygon about the origin and
translating its reference point to $p$:

$$
B@p = \{\, R_\theta\, v + p : v \in B_0 \,\},
\qquad
R_\theta=\begin{bmatrix}\cos\theta & -\sin\theta\\ \sin\theta & \cos\theta\end{bmatrix},
$$

where $B_0$ is the part in its source frame and $B=R_\theta B_0$ is normalized
so its bounding-box minimum sits at the origin: candidate $p$ values then index
the part's lower-left placement. Method class: rigid planar motions, standard.

### 1.2 No-fit polygon (overlap locus)

For a fixed part $A$ and a moving part $B$ with reference point $p$:

$$
\mathrm{NFP}(A,B) = A \oplus (-B),
\qquad
(B@p)\cap A \neq \emptyset \iff p \in \operatorname{int}\mathrm{NFP}(A,B).
$$

*Derivation.* $B@p$ and $A$ intersect iff there exist $a\in A,\ b\in B$ with
$a=b+p$, i.e. $p=a-b\in A\oplus(-B)$; the interior excludes mere boundary
contact, which is the densest legal touch. Computed as an exact integer
Minkowski sum on the Clipper2 back-end. Citation: Minkowski-sum NFP, Bennell &
Oliveira 2009 (JORS tutorial); Stoyan et al. 2002. **Machine-verified
instance** (Z3, quantified linear real arithmetic, negation unsat): for unit
squares, $\forall p:\ (\exists a\in A, b\in B: a=b+p) \iff p\in[-1,1]^2$.

### 1.3 Inner-fit polygon (containment locus)

Feasible reference points for which $B$ lies inside the sheet $S$ are the
erosion of $S$ by $B$:

$$
\mathrm{IFP}(S,B) = S\ominus B = \{\, p : B@p \subseteq S \,\}
= \bigcap_{b\in B}\,(S - b),
$$

computed conservatively over the convex-hull vertices of $B$:

$$
\widehat{\mathrm{IFP}}(S,B) = \bigcap_{v\in \operatorname{vert}(\operatorname{hull}(B))} (S - v)
\subseteq \mathrm{IFP}(S,B),
$$

exact when $B$ is convex and a safe subset otherwise, so containment is always
guaranteed. **Machine-verified instance** (Z3, quantified linear real
arithmetic, negation unsat):
$\forall p:\ (\forall b\in[0,1]^2:\ b+p\in[0,5]^2) \iff p\in[0,4]^2$.

### 1.4 Feasible region (hard non-overlap constraint)

Holes are obstacles identical to placed parts, so the exact feasible reference
region with clearance $\sigma$ is

$$
F(B) = \big(\widehat{\mathrm{IFP}}(S,B)\ominus_\sigma\big)\ \setminus\
\Big(\oplus_\sigma \big[\textstyle\bigcup_k \mathrm{NFP}(A_k,B)\ \cup\ \bigcup_j \mathrm{NFP}(H_j,B)\big]\Big),
$$

where $\ominus_\sigma$ / $\oplus_\sigma$ shrink the IFP and inflate the blocked
set by $\sigma$. By construction every $p\in F(B)$ gives $B@p\subseteq S$,
disjoint from each hole $H_j$, and non-penetrating with every placed part
$A_k$: **non-overlap is a hard constraint, not a penalty.**

### 1.5 Bottom-left placement rule

$$
p^\star(B) = \operatorname*{arg\,min}_{p\in F(B)}\ (y,\ x)
\quad(\text{lexicographic}),
$$

attained at a vertex of $F(B)$ because a linear objective over a polygon is
minimized at a vertex. Citation: Bottom-Left-Fill, Burke, Hellier, Kendall &
Whitwell 2006 (Operations Research 54(3):587–601).

### 1.6 Boundary mode (rim hug), shipped 0.1.0-alpha

With the sheet outline parameterized by arc length ($P$ = its perimeter),
each *verified* candidate pose $p$ earns a measured rim-contact length
$c(p)$ (total arc length of the placed outline within tolerance of the sheet
boundary, as arc intervals $\mathcal{A}(p)$) and is scored against the
already-occupied intervals $\mathcal{O}$:

$$
\operatorname{score}(p) = c(p) - 2\,\big|\mathcal{A}(p)\cap \mathcal{O}\big|,
\qquad
p^\star = \operatorname*{arg\,max}_{p}\ \operatorname{score}(p)
\ \ \text{if}\ \ c(p^\star)\ \ge\ \kappa\,\min\!\big(\operatorname{per}(B),\,P\big),
$$

else the part falls back to the bottom-left rule; on acceptance
$\mathcal{O}\leftarrow\mathcal{O}\cup\mathcal{A}(p^\star)$, which spreads
placements around the rim instead of clustering. Because contact is measured
at verified NFP poses, the score is rotation-invariant and exact. A rim-full
early-out skips the scoring pass once $P-|\mathcal{O}|$ falls below the
threshold $\kappa\cdot\operatorname{per}(B)$. Frahan-original evolution of the
descriptor-bucket boundary heuristic; benchmarked in
[`docs/results/RESULTS.md`](../../../docs/results/RESULTS.md).

### 1.7 Greedy assembly and objective

Parts are placed in area-descending order (multi-start over $K$ deterministic
orders, keeping the densest valid layout); each part at $p^\star(B)$ for the
rotation minimizing the BLF rule. The reported objective is overlap-free
utilization by exact polygon union:

$$
U = \frac{\operatorname{area}\!\big(\bigcup_k A_k\big)}{\operatorname{area}(S)},
\qquad W = 1-U .
$$

### 1.8 Complexity (from the loop structure)

Each placement builds at most one Minkowski sum per already-placed part after
a bounding-box prefilter: the dominant cost is $O(N^2)$ Minkowski sums for
$N$ parts, each near-linear in vertex counts, plus one polygon union and
difference per placement. Matches measured runtime.

---

## 2. 3D irregular packing: constructive seed + physics settle

### 2.1 Shipping constructive seed: per-column interval heightmap

The Core seed packer (`GreedyMeshHeightmapPacker` over `MeshPileHeightmap` /
`OrientedMeshHeightmap`) is a per-column **interval** model, richer than a
plain 2.5D skyline. Per orientation $o$, the mesh is rasterized at pitch $h$
into column profiles over its occupied footprint cells $m$:

$$
B^o(m)=\min_{s\in\mathrm{samples}(m)} s_z,
\qquad
T^o(m)=\max_{s\in\mathrm{samples}(m)} s_z,
$$

sampling vertices, triangle edge points, and centroids
(`OrientedMeshHeightmap.AddSample`). The rest height at anchor
$a=(a_x,a_y)$ against the pile-top heightmap $H$ is

$$
z(a) = \max_{m\ \mathrm{occupied}}\ \big[\,H(a{+}m) - B^o(m)\,\big],
$$

then $z$ is lifted until the per-column slab $[z{+}B^o(m),\,z{+}T^o(m)]$ fits
the container's allowed intervals, and rejected if it overlaps any stored
interval of the pile (`TryGetLowestZ`; `WouldCollide` checks the per-column
interval lists, which is what lets later stones reuse voids under overhangs).
Candidates are scored by added height above the pile plus a back-left
compactness bias, minimizing

$$
S(a,o) = w_h \sum_{m} \max\!\big(0,\ z{+}T^o(m) - H(a{+}m)\big)
\;+\; w_c\,(a_x + a_y),
$$

over a capped candidate scan with optional stochastic tie-break
(`ScorePlacement`, `MaxCandidatesPerItem`), and the accepted placement updates
$H(a{+}m)\leftarrow\max\big(H(a{+}m),\,z{+}T^o(m)\big)$ and appends the column
intervals (`Add`). Complexity: $O(N\,k\,C\,F)$ for $N$ stones, $k$
orientations, $C$ scanned anchors, footprint $F$ cells. Citation:
heightmap-packing class (Wang & Hauser 2022); deepest-bottom-left ordering
(Chehrazad, Roose & Wauters 2022). **Verified against source 2026-07-06**
(the equations above are read off `TryGetLowestZ` / `ScorePlacement` / `Add`).

### 2.2 Evolution reference: voxel feasibility (true-3D collision-free poses)

> The three equations below (2.2 - 2.4th paragraph) describe the **validated
> evolution algorithm** (measured $\rho=0.358/0.387$ on ETH1100), whose Core
> port is an open blueprint item. The shipping Core seed is §2.1; the shipping
> settle is §2.6.

With container occupancy $O$ (1 where filled) and stone mask $M_i^o$
(stone $i$, orientation $o$), the integer overlap at every anchor is one
cross-correlation; an anchor is feasible iff the overlap is zero:

$$
(O \star M_i^o)[a] = \sum_{u} O[a+u]\,M_i^o[u],
\qquad
\mathcal F_i^o = \{\,a : (O\star M_i^o)[a]=0\,\}.
$$

Zero means no voxel collision, so $S_i$ fits in true 3D — it may sit in a
cavity or under an overhang, unreachable by a 2.5D skyline. Citation:
occupancy-grid packing (Wang & Hauser 2022, IEEE T-RO 38(2)); FFT collision
(standard).

### 2.3 Evolution reference: deepest-bottom-left placement

$$
a_i^\star = \operatorname*{arg\,min}_{a\in\mathcal F_i^o}\ (a_z,\ a_x,\ a_y),
\qquad
o_i^\star = \operatorname*{arg\,min}_{o}\ \big(a_z^\star(o)+h_z^o\big),
$$

choosing the orientation giving the lowest resulting top. Citation:
deepest-bottom-left fill (Chehrazad, Roose & Wauters 2022, EJOR 300(3));
lowest-gravitational-centre placement (Liu et al. 2015, HAPE3D).

### 2.4 Evolution reference: void-fill relaxation

Each placed stone is lifted and re-settled to its current deepest feasible
pose, cascading into voids opened as the pile evolves:

$$
a_i \leftarrow \operatorname*{arg\,min}_{a\in\mathcal F_i^o(O\setminus M_i)}\ a_z,
\qquad \text{iterate until } \max_i \Delta a_{z,i}=0 .
$$

### 2.5 Convex-decomposition collision proxy (ships: CoacdMeshDecompose)

Each concave stone is decomposed into convex pieces (CoACD), reducing the
pair test to exact convex-convex tests (GJK/EPA):

$$
S_i \approx \bigcup_{k=1}^{K_i} C_{ik}\ \ (C_{ik}\ \text{convex}),
\qquad
(T_i S_i)\cap(T_j S_j)\neq\emptyset \iff \exists\,k,l:\ (T_i C_{ik})\cap(T_j C_{jl})\neq\emptyset .
$$

Citation: CoACD (Wei et al. 2022); GJK (Gilbert–Johnson–Keerthi).

### 2.6 Physics settle (ships: BulletSettleService)

Constructive poses seed a rigid-body world; each stone obeys Newton–Euler
dynamics with gravity and contact impulses, integrated to rest:

$$
m_i \dot v_i = m_i g + \sum_{c} J_{n,c}^{\top}\lambda_{n,c} + J_{t,c}^{\top}\lambda_{t,c},
\qquad
I_i \dot\omega_i + \omega_i\times(I_i\omega_i) = \sum_{c} r_{c}\times f_{c},
$$

with each contact $c$ enforcing non-penetration as a complementarity condition
on the signed gap $\phi_c$, inside a Coulomb friction cone:

$$
0 \le \phi_c \ \perp\ \lambda_{n,c} \ge 0,
\qquad
\lVert\lambda_{t,c}\rVert \le \mu\,\lambda_{n,c} .
$$

The idealized termination is total kinetic energy
$\sum_i(\tfrac12 m_i\lVert v_i\rVert^2+\tfrac12\omega_i^{\top}I_i\omega_i)\to 0$
(static equilibrium). **The shipping code deviates**: `BulletSettleService`
integrates a *fixed step budget* (gravity ramp + vertical tamp), with
per-body sleeping when velocities fall below Bullet's thresholds — a bounded
approximation of the energy criterion, not a convergence test (verified
against source 2026-07-06). Citation: Zhuang et al. 2024 (Computers &
Graphics 123:103996); Bullet sequential-impulse solver; Coulomb friction.

### 2.7 Stability (the result must stand)

At rest each stone satisfies static equilibrium, additionally gated on the
centre of mass projecting inside the contact-support polygon:

$$
\sum F = 0,\quad \sum \tau = 0;
\qquad
\pi_{xy}(c_i) \in \operatorname{conv}(\mathrm{supp}_i).
$$

Citation: limit-state COM-over-support (Heyman 1966).

### 2.8 Objective (honest density)

$$
\rho = \frac{\sum_i \operatorname{vol}(S_i)}{A\,H},
\qquad
H=\max_i\ \max_{v\in T_i S_i} v_z,
$$

the **true mesh volume** (divergence / signed-tetra, never the bounding box)
packed per floor-area times used-height, counting only non-interpenetrating
($\phi_{ij}\ge -\varepsilon$) settled stones. Measured: $\rho=0.358$ at
$N{=}30$, $0.387$ at $N{=}60$ on ETH1100 (vs $0.338$ shipping skyline) at zero
interpenetration; a literal $2\times$ ($0.66$) is above the irregular-stone
physical ceiling and is not claimed. The Core now computes the honest
numerator without Rhino: `MeshPackItem.MeshVolume` is the signed-tetra sum
$V=\tfrac16\lvert\sum_t a_t\cdot(b_t\times c_t)\rvert$ over triangles, and
`MeshPackResult.FillRatioMeshVolume` divides it by container volume (the
bbox-based `VolumeEstimate` / `FillRatioEstimate` remain for the fast,
over-reporting path). Added 2026-07-06, test `mesh signed-tetra volume is
honest vs bbox`; machine-verified value on a right-triangular prism (exactly
half its bbox).

### 2.9 Complexity (evolution reference)

Evolution-reference constructive seed: one FFT correlation per (stone,
orientation) over the grid, $O(N\,k\,G\log G)$ for $N$ stones, $k$
orientations, grid size $G$ (the shipping heightmap seed's complexity is in
§2.1). Settle: rigid-body steps dominated by the contact solver over the
convex pieces, near-linear per step in active contacts.
