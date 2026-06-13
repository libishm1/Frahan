# 06. Voussoir Geometry & Stereotomy

The Voussoir tab (ribbon `Frahan > Voussoir`, 5 components) is the
repository's top-down stereotomy front end. Stereotomy is the art of cutting
solids, classically stone, so that the cut faces alone hold an arch or a vault
in compression without mortar (Frezier 1737; Monge 1798). The tab makes that
art executable. Two generators turn a designed form into cut-stone cells: Arch
Voussoirs builds a planar arch as radial wedge solids, and Pendentive Vault
Voussoirs builds a sail dome tessellated along the sphere's lines of curvature.
Three downstream components (Voussoir Ingest, Voussoir Stone Matcher, Voussoir
Pack Into Block) then assign and pack those cells against found or quarried
stone. This chapter covers the two generators and their shared cell factory;
the matcher and packer are Hungarian and bin-pack facades documented with the
quarry assignment layer.

The design framing is the load-bearing point. The other design tabs let
material be sovereign (rubble walls, Trencadis mosaics, edge-matched
fragments). The Voussoir generators are the opposite pole: the form is
sovereign, and the system cuts stone to realise it. Both `[DesignApplication]`
attributes tag `DesignFlow.TopDown` explicitly
(`ArchVoussoirsComponent.cs:37`, `PendentiveVaultVoussoirsComponent.cs:35`).

The geometry here is pure trigonometry and curve sampling. Where the
repository turned a classical stereotomic rule into an algorithm, or fixed a
real bug, the derivation is shown, not only the formula. Every originality
claim is anchored to a `file:line`, an `[Algorithm]` attribute, or a measured
example.

---

## 6.1 The stereotomy lineage and the front-end gap

The classical literature gives two laws the generators obey. First, the
**radial bed-joint rule**: in an arch the bed joints (the faces between
neighbouring voussoirs) are normal to the intrados, the inner soffit curve;
for a circular arch they point at the centre of curvature. Each wedge then
turns the thrust aside, stone to stone, so the whole ring stands in pure
compression (Frezier 1737, who coined *stereotomie*; the funicular reading is
Hooke's 1675 inverted-catenary insight, made a limit-state theorem by Heyman
1966). Second, **Monge's lines-of-curvature rule** for vaults: orient the bed
joints along the lines of curvature of the doubly-curved surface; for a sphere
these are the meridians and parallels (Monge 1798). Both laws are recorded in
source as `[Algorithm]` citations on the two components
(`ArchVoussoirsComponent.cs:34-36`,
`PendentiveVaultVoussoirsComponent.cs:32-34`).

The repository already shipped a voussoir *back end*: Voussoir Ingest reads
cells produced by an external Grasshopper plugin (Varela and Sousa's Voussoir,
food4rhino), then Voussoir Stone Matcher and Voussoir Pack Into Block assign
and pack them (`VoussoirRecord.cs:11-21`). What was missing was the
*generation* step. Without it the top-down flow began outside Frahan, in a
third-party tool. The `VoussoirCellFactory` closes that gap: it generates the
cut-stone cells from first principles so the whole top-down chain lives inside
the repository (`VoussoirCellFactory.cs:9-20`):

```text
VoussoirCellFactory.BuildArch / BuildPendentiveVault   (this chapter)
  -> VoussoirAssembly (typed)
    -> Voussoir Stone Matcher (Hungarian) / Rubble Evolved Fit
      -> CGAL trim (digital ravalement)
```

The shared engine is a static class, `VoussoirCellFactory`, with two public
entry points (`BuildArch`, `BuildPendentiveVault`) and one shared solid
builder (`MakeHexahedron`). It depends on RhinoCommon for `Mesh`, `Curve`, and
`Plane`, but needs no Rhino document, so it runs in the headless harness
(`VoussoirCellFactory.cs:39-41`).

---

## 6.2 The arch: radial voussoir cells

`ArchVoussoirsComponent` (GUID
`D5F10012-ED9E-4ED9-A012-ED9EED9E0012`, `:57-58`) generates an arch as `N`
radial wedge solids. The construction has three stages: build the intrados
curve for the chosen profile, station it by equal arc length, then loft a
closed wedge between each pair of stations.

### 6.2.1 Stationing the intrados

The intrados is built in the world XZ plane with the springers on $z = 0$ and
the width running along $Y$. For the count $N$ the curve is divided into $N+1$
equal-arc-length stations, giving $N$ wedges (`VoussoirCellFactory.cs:119-121`):

$$
\{t_0,\dots,t_N\} = \mathrm{DivideByCount}(N,\ \text{include ends}),\qquad
p_k = \mathbf{c}(t_k),\quad k = 0,\dots,N.
$$

Equal arc length, not equal angle, is the right division: it keeps the
intrados face of every voussoir the same physical width even when the profile
is not circular (a catenary or a pointed arch), which is the fabrication
constraint a mason cares about.

### 6.2.2 The outward normal and the radial bed joint

At each station the bed-joint direction is the **outward normal** to the
intrados. The code takes the in-plane tangent $\mathbf{T}(t_k)$, projects it
into the XZ plane, and rotates it ninety degrees in plane
(`VoussoirCellFactory.cs:136-146`):

$$
\mathbf{T} = (T_x, 0, T_z),\qquad
\mathbf{n} = (T_z,\ 0,\ -T_x).
$$

The rotation $(T_x, T_z) \mapsto (T_z, -T_x)$ is the planar perpendicular. Its
sign is then fixed to point away from the arch interior by testing against the
radial vector $\mathbf{r}_k = p_k - \bar{p}$ from the station to the centroid
of the station cloud, flipping $\mathbf{n}$ when $\mathbf{n} \cdot \mathbf{r}_k
< 0$:

$$
\mathbf{n}_k \leftarrow
\begin{cases}
-\mathbf{n} & \text{if } \mathbf{n}\cdot(p_k-\bar{p}) < 0,\\
\ \ \mathbf{n} & \text{otherwise.}
\end{cases}
$$

**Why this is exactly the radial rule for a circular arch.** For a circle of
centre $O$ the tangent at any point is perpendicular to the radius $O p_k$, so
the in-plane perpendicular of the tangent is collinear with that radius.
Rotating the tangent ninety degrees therefore yields the radial direction, and
the centroid sign-fix orients it outward, away from $O$. The bed joint built on
$\mathbf{n}_k$ is then normal to the intrados and points at the centre of
curvature, which is Frezier's rule verbatim. For a non-circular profile
(catenary, pointed) the centre of curvature varies along the curve, and
$\mathbf{n}_k$ is the local intrados normal, the correct generalisation: the
bed joint stays perpendicular to the soffit at the joint, which is what keeps
the contact face square to the local thrust.

### 6.2.3 The wedge solid

Each voussoir spans stations $i$ and $i+1$. Its four in-plane corners are the
two intrados points and the two extrados points; the extrados is the intrados
offset outward by the ring thickness $t$ along the per-station normal
(`VoussoirCellFactory.cs:152-174`):

$$
\text{in}_A = p_i,\quad \text{in}_B = p_{i+1},\qquad
\text{ex}_A = p_i + t\,\mathbf{n}_i,\quad \text{ex}_B = p_{i+1} + t\,\mathbf{n}_{i+1}.
$$

Because the extrados rides on the **same** outward normals, a circular arch
gives an exactly concentric extrados: each extrados point sits at radius
$R + t$ on the same ray as its intrados point. The in-plane quad is then swept
the half-width $\tfrac{w}{2}$ each way along $Y$ to give the front and back
rings, and `MakeHexahedron` welds them into a closed eight-vertex solid. The
lower bed-joint plane is emitted with origin at the mid-thickness of the lower
face and axes $(\mathbf{n}_i,\ \mathbf{Y})$, so its own normal is the
tangential thrust direction (`:179-182`).

The result is faceted: straight chords between arc-length stations. The
component tolerance note states this plainly and gives the remedy, raise the
count (`ArchVoussoirsComponent.cs:41`). The error is the chord sagitta of the
intrados arc, which falls as $O(1/N^2)$; the circular extrados, by the
concentric construction above, carries the same relative facet error as the
intrados, not a worse one.

### 6.2.4 The keystone

The keystone is the voussoir nearest the apex. The factory finds it by a
combined height-and-symmetry score: among cells with positive $z$, minimise
$|x_c| + |z_{\text{apex}} - z_c|$ over the centroid $(x_c, z_c)$, so the winner
is the highest cell closest to the plane of symmetry $x = 0$
(`NearestToApex`, `:526-538`). Its `JointClass` is then set to `"key"`
(`:189-193`), and the springer course (the lowest 5% height band) is tagged
`"ground"` as the install-DAG anchors (`:509-515`).

### 6.2.5 Four profiles, one path

The profile families are Semicircular, Segmental, Pointed (equilateral
two-centred Gothic), and Catenary (`ArchProfile`, `:45-55`). The
wiki note that "catenary and pointed are a drop-in change of the intrados
curve" is made concrete: each profile builds only its intrados `Curve`, and
the single shared path of 6.2.1 to 6.2.3 stations and lofts it
(`BuildIntrados`, `:315-336`).

- **Semicircular and Segmental** are circular arcs. The centre is placed at
  $z_c = -R\cos(\tfrac{a}{2})$ so the springers land on $z = 0$ for the
  included angle $a$, and the arc passes through left springer, apex, right
  springer (`BuildArcIntrados`, `:338-352`). The span is
  $S = 2R\sin(\tfrac{a}{2})$.
- **Pointed** is the equilateral two-centred arch: span $S = R$, each half an
  arc of radius $S$ swung from the opposite springer, apex at
  $h = \tfrac{\sqrt 3}{2}S$ (`BuildPointedIntrados`, `:354-382`). The two arcs
  are appended into one `PolyCurve`.
- **Catenary** is the inverted hanging chain, the funicular line of a
  pure arch (Hooke 1675; Heyman 1966). A chain of parameter $a$ hangs as
  $y = a\cosh(x/a)$ with sag $a\cosh(\tfrac{S}{2a}) - a$ over the span $[-S/2,
  S/2]$. Given a target rise $H$ the factory solves for $a$ such that the sag
  equals $H$, then inverts the curve so the sag becomes the rise
  (`BuildCatenaryIntrados`, `:390-410`).

**Original derivation: the catenary parameter solve.** The shape parameter
$a$ has no closed form for a prescribed span $S$ and rise $H$; it is the root
of

$$
f(a) = a\cosh\!\Big(\frac{S}{2a}\Big) - a - H = 0.
$$

$f$ is monotone decreasing in $a$ from $+\infty$ (as $a \to 0^{+}$ the chain
sags arbitrarily deep) toward $-H$ (as $a \to \infty$ the chain straightens
and the sag vanishes), so it crosses zero exactly once and bisection is
guaranteed to converge. The factory brackets the root on $[10^{-4}S,\ 10^{4}S]$
and bisects to a residual of $10^{-9}$ or interval collapse, capped at 200
iterations (`SolveCatenaryA`, `:412-432`). The inverted profile is then sampled
($z = H - (a\cosh(x/a) - a)$, floored at zero) and fitted with a cubic
interpolating curve. Building the *true* funicular intrados is what makes the
catenary arch a structural object, not a decorative pointed shape: the bed
joints normal to it are normal to the thrust line, so the ring is in pure
axial compression by construction (the safe theorem, Heyman 1966).

> **Originality.** Arch Voussoirs is **clean-room** stereotomy. The radial
> bed-joint construction is built from the published rule, cited in source by
> two `[Algorithm]` attributes: the Frahan-original cell construction (intrados
> curve to arc-length stations to eight-vertex wedge solids with radial bed
> joints, `ArchVoussoirsComponent.cs:31-33`), and the geometric law it obeys
> credited to Frezier (1737) and Monge (1798)
> (`:34-36`). No external stereotomy source sits in the tree; the upstream
> Voussoir plugin (Varela and Sousa) is a cited *precedent* in the
> `[DesignApplication]` attribute (`:40`), not a dependency, and its cells are
> consumed only through the separate Voussoir Ingest path. The catenary
> parameter solve and the concentric-extrados construction are the small
> clean-room deltas over a naive radial sweep.

![Stereotomic voussoir arch carved from ETH1100 rubble, eleven radial cells](../examples/21_stereotomy_rubble_arch/21_rubble_arch.png)

---

## 6.3 The pendentive vault: lines-of-curvature cells

`PendentiveVaultVoussoirsComponent` (GUID
`D5F10013-ED9E-4ED9-A013-ED9EED9E0013`,
`PendentiveVaultVoussoirsComponent.cs:55-56`) is the doubly-curved
counterpart. A pendentive (sail) dome is a single spherical surface springing
from the four corners of a square plan up to the apex. The factory tessellates
it on a $U \times V$ grid into wedge cells whose bed joints follow the sphere's
lines of curvature, then extrudes each cell radially by the shell thickness
(`BuildPendentiveVault`, `:216-304`).

### 6.3.1 Lifting the square grid onto the sphere

The square plan of half-width $h$ is gridded uniformly in $(x, y)$, and each
node is lifted onto the sphere of radius $R$ centred at the origin
(`:244-256`):

$$
x_i = -h + \frac{2h\,i}{U},\quad
y_j = -h + \frac{2h\,j}{V},\qquad
z_{ij} = \sqrt{R^2 - x_i^2 - y_j^2}.
$$

**The corner-on-sphere constraint.** The plan corners $(\pm h, \pm h)$ must lie
on the sphere, which requires $z$ real at the corner:

$$
R^2 - h^2 - h^2 \ge 0 \;\;\Longleftrightarrow\;\; 2h^2 < R^2.
$$

The factory enforces $2h^2 < R^2$ as a hard precondition and throws a
descriptive error otherwise (`:230-234`); the component repeats the same guard
before calling Core and surfaces it as a canvas error
(`PendentiveVaultVoussoirsComponent.cs:130-136`). The springing height (the $z$
of the four corners) is $z_{\text{corner}} = \sqrt{R^2 - 2h^2}$, and an
optional drop-to-ground shift translates the whole vault so the springers rest
on $z = 0$ (`:236-238`).

### 6.3.2 The radial frustum cell

Each grid cell is the patch between four lifted intrados points
$(c_{00}, c_{10}, c_{11}, c_{01})$ and their **radial** projections to the
outer sphere of radius $R + t$. The extrados corner of an intrados point $p$ is
the scaling of $p$ about the sphere centre by the radius ratio
(`Radial`, `:308-313`; cell `:269-277`):

$$
\rho = \frac{R + t}{R},\qquad
p_{\text{ex}} = O_{\text{sph}} + \rho\,(p - O_{\text{sph}}).
$$

Because both faces are radial scalings about the same centre, the side walls of
the cell run along sphere radii, which are the surface normals. The bed joint
between two neighbouring cells therefore sits in a plane containing the sphere
radius, exactly Monge's lines-of-curvature rule for a sphere: the cell edges
run along meridians and parallels, and the joints are radial. The bed plane is
emitted with origin at the intrados patch centre and normal along the outward
radial $\widehat{(\text{mid} - O_{\text{sph}})}$ (`:280-289`). The vault has no
single keystone, so `KeystoneIndex` is left at $-1$ (`:294`).

> **Originality.** Pendentive Vault Voussoirs is **clean-room**. The
> sphere-over-square construction and the radial-frustum cell are built from
> the lines-of-curvature rule, cited in source by the Frahan-original cell
> `[Algorithm]` (square grid lifted by $z = \sqrt{R^2 - x^2 - y^2}$ then
> radially extruded, `PendentiveVaultVoussoirsComponent.cs:29-31`) and the
> Monge tessellation law it obeys (`:32-34`). The cited design precedents are
> Rippmann and Block (2011) Digital Stereotomy and the Block Research Group
> RhinoVAULT pipeline (`:38`); these are named precedents in the
> `[DesignApplication]` attribute, not in-tree dependencies. The sphere is the
> closed-form special case; a general form-found funicular shell would arrive
> through the compas-RV reference pipeline noted in 6.5.

![Pendentive sail vault, thirty-six lines-of-curvature cells carved from rubble boulders](../examples/22_pendentive_vault_rubble/22_pendentive_vault.png)

---

## 6.4 The inward-orientation fix (original derivation)

Both generators feed their cells into a CGAL Boolean trim downstream (the
digital ravalement of examples 21 and 22, section 6.5). A mesh-mesh Boolean is
sensitive to face orientation: the kernel reads a closed mesh's *inside* from
its face winding. If a cell's faces wind inward, the kernel reads the solid as
"all of space except the cell", and the intersection of a stone with that
inverted cell returns the stone minus the cell rather than the carved
voussoir. The failure is silent: the trim returns a closed mesh, just the
wrong one.

The fix lives in the shared solid builder. After `MakeHexahedron` welds the two
rings, unifies the winding, and rebuilds normals, it tests the **signed
volume** of the mesh and flips every face if the volume is negative
(`MakeHexahedron`, `:452-464`):

$$
V_{\text{signed}}(M) < 0 \;\Longrightarrow\; M \leftarrow \mathrm{Flip}(M).
$$

**Why signed volume is the correct orientation test.** For a closed
triangulated mesh the signed volume is the sum over faces of the signed
tetrahedron each face spans with the origin,

$$
V_{\text{signed}}(M) = \frac{1}{6}\sum_{f=(a,b,c)} \big(a \times b\big)\cdot c,
$$

and its sign is determined entirely by the global face winding: outward-wound
faces give $V_{\text{signed}} > 0$, inward-wound give $V_{\text{signed}} < 0$.
The magnitude is the true enclosed volume regardless of sign. So the single
test $V_{\text{signed}} < 0$ detects an inverted solid exactly, and one global
flip corrects it. This is necessary because `Mesh.UnifyNormals` only makes the
winding *consistent* across faces, not necessarily *outward*: a fully
consistent mesh can still be uniformly inside-out, which is the case the flip
catches (`:456-461`). The factory then stores the absolute mesh volume on each
record (`VoussoirRecord.cs:37-39`; `Math.Abs(cell.Volume())` at
`VoussoirCellFactory.cs:476`), so the volume metric is sign-independent and a
flipped or unflipped cell reports the same number.

This bug and fix were a live-validated correctness gate. The memory note for
the voussoir generators records that the inward-orientation bug "silently broke
CGAL booleans" and that the flip-if-signed-volume-under-zero guard is what made
examples 21 and 22 regenerate from raw rubble with no raw boulders surviving
the trim. The closedness of every emitted cell is checked (`Mesh.IsClosed`) and
surfaced as a component warning if any cell is open
(`ArchVoussoirsComponent.cs:163-165`,
`PendentiveVaultVoussoirsComponent.cs:150-152`).

> **Originality.** The orientation fix is a **clean-room** correctness guard on
> the shared `MakeHexahedron` builder (`VoussoirCellFactory.cs:452-464`). It is
> elementary geometry (signed volume sign as an orientation oracle), and its
> contribution is robustness: it is the precondition that makes the downstream
> CGAL trim of section 6.5 return the carved voussoir rather than its
> complement. The downstream trim itself runs through the in-repo
> `CgalMeshBoolean` primitive (the GPL CGAL kernel in Rhino, a managed BSP
> fallback headless), which is **facade-over-primitives** and out of scope for
> this tab; this chapter owns only the generator that produces a correctly
> oriented input to it.

![ETH1100 rubble stone (right) trimmed to a radial voussoir cell (left) by CGAL intersect](../examples/21_stereotomy_rubble_arch/21_stone_to_voussoir.png)

---

## 6.5 The top-down flow end to end: examples 21 and 22

The two generators are the entry point of a top-down stereotomy chain that
ends in carved stone. Examples 21 (arch) and 22 (pendentive vault) run it
whole, with a measured coverage metric.

The flow is the classical *ravalement* method made digital: cut a voussoir
oversize, mount it, trim the excess to the final surface (Frezier; the method
is recorded in the example READMEs). Operationally: the generator emits the
target cell, a volume-feasible ETH1100 rubble stone is matched and posed to
envelop it (the evolved matcher adds rotation seeds and an SE(3) containment
search over the cell's real vertices), and `CgalMeshBoolean.Intersection`
trims the stone to the cell. A validity guard keeps only closed trims within
the cell volume, else falls back to the clean cut cell, never the raw boulder.
Stability in stereotomy comes from the carved geometry, not mortar, so the cut
faces are what matter.

The measured results (regenerated 2026-06-07, `21_arch_metrics.json`,
`22_vault_metrics.json`):

| Example | Form | Cells | Source | Real rubble trims | Coverage |
|---|---|---|---|---|---|
| 21 | Semicircular arch, $R = 2.0$ m, ring $0.55$ m, span $4.0$ m | 11 | Arch Voussoirs (D5F10012) | **11 / 11**, 0 clean fallback | **94.9%** |
| 22 | Pendentive dome, $R = 2.5$ m, $2h = 3.2$ m, $t = 0.4$ m | 36 | Pendentive Vault Voussoirs (D5F10013) | **36 / 36**, 0 clean fallback | **98.3%** |

Coverage is recovered carved volume over the target ring or shell volume:
$2.2079 / 2.3266\ \text{m}^3 = 94.9\%$ for the arch (per-cell 0.85 to 0.99),
$5.5996 / 5.6941\ \text{m}^3 = 98.3\%$ for the vault. Both runs are visually
validated in Rhino (criterion c) with one coloured mesh per voussoir. The
reported bug the regeneration fixed is exactly the orientation-and-fallback
issue of section 6.4: the earlier version kept a raw un-trimmed boulder on a
failed trim, so oversized stones overshot the ring; no raw boulders remain in
either example.

For a general form-found doubly-curved vault (not the closed-form sphere), the
documented reference pipeline is compas-RV (Block Research Group RhinoVAULT in
COMPAS): pattern to form-and-force diagrams to a compression-only funicular
shell, tessellated into voussoir courses with bed joints along the lines of
curvature, then the same match-and-trim. Equilibrium of the assembled ring or
shell is then checked by Frahan Masonry Stability (RBE and CRA, chapter 5),
closing the top-down stereotomy loop onto the contact-equilibrium chapter.

---

## 6.6 Status and what is left

- **Funicular form-finding is external.** The arch supports a true catenary
  intrados, but the pendentive generator is the closed-form *sphere* only. A
  general form-found shell must arrive through the compas-RV reference pipeline
  named in 6.5; there is no in-repo form-finder. The funicular `ThrustCurve`
  field on `VoussoirAssembly` is defined but not populated by either generator
  (`VoussoirAssembly.cs:23-27`). Severity: medium (scope boundary, documented).
- **Faceted cells by construction.** Both generators emit straight-chord
  facets; curvature accuracy is bought only by raising the count or the grid,
  and the tolerance note states this (`ArchVoussoirsComponent.cs:41`,
  `PendentiveVaultVoussoirsComponent.cs:39`). The intrados facet error falls as
  $O(1/N^2)$, but a true NURBS-faced voussoir would need a different cell
  builder. Severity: low (stated, count-controllable).
- **No equilibrium check inside the tab.** The generators produce geometry and
  a typed assembly with ground anchors and `LoadAxis` per cell, but do not run
  a stability verdict; that requires wiring the assembly into the Masonry
  Stability components (chapter 5). The `[DesignApplication]` precedent names
  the link, but the canvas does not enforce it. Severity: low (cross-tab by
  design).
- **Adjacency graph not auto-built by the factory.** `FinalizeResult` leaves
  `AdjacencyPairs` empty (`VoussoirCellFactory.cs:520`); the install-DAG
  adjacency is detected downstream in Voussoir Ingest by shared-face area
  (`VoussoirAssembly.cs:29-33`). A generator that already knows its
  station-to-station and grid neighbour topology could emit the adjacency
  losslessly, as the polygonal-wall assembler does for masonry (chapter 5.5).
  Severity: low (refactor opportunity).
- **Citation hygiene.** The example READMEs cite Sakarovitch (stereotomy
  history) and Galletti (2020, mortarless stability) and Hooke (1675), which
  are not yet keyed in `99_references.md`; the in-source `[Algorithm]`
  citations (Frezier 1737, Monge 1798, Rippmann-Block 2011) are present and
  keyed. Add the missing keys before external review per `AGENTS.md` §9.
  Severity: low (provenance, not copyleft).

---

## References (this chapter)

- Frezier, A.-F. (1737-1739). *La theorie et la pratique de la coupe des
  pierres et des bois* (the stereotomy treatise that coined *stereotomie*; the
  radial bed-joint rule). [R148]
- Monge, G. (1798). *Geometrie descriptive*. Baudouin, Paris (lines of
  curvature for vault tessellation). [R149]
- Hooke, R. (1675). *A description of helioscopes* (the inverted-catenary
  anagram: the funicular line of a pure arch). Cited in source for the catenary
  profile; not yet keyed in `99_references.md`.
- Heyman, J. (1966). The stone skeleton (limit-state / safe theorem of masonry;
  thrust line within the section). *International Journal of Solids and
  Structures* 2(2):249-279. DOI 10.1016/0020-7683(66)90018-7. [R57]
- Rippmann, M., Block, P. (2011). Digital stereotomy: voussoir geometry for
  freeform masonry-like vaults informed by structural and fabrication
  constraints. *Proceedings of IABSE-IASS 2011*, London. [R64]
- Varela, P.A.A., Sousa, J.P. Voussoir: stereotomy plug-in for Grasshopper.
  FAUP Porto Digital Fabrication Laboratory.
  https://www.food4rhino.com/en/app/voussoir (cited precedent for the
  voussoir-ingest back end; not a dependency of the generators). [R132]
- Block Research Group. RhinoVAULT / compas-RV funicular form-finding (the
  reference pipeline for general form-found shells). [R64, R65]
