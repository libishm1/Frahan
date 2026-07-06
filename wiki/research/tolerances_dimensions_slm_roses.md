# Tolerances and Dimensions - SLM + ROSES study (Frahan StonePack)

Date: 2026-06-06. Framework: Research V4, T1 SLM (algorithm math) + T2 ROSES (interdisciplinary
synthesis). Scope: every tolerance + dimension parameter across the plugin. Anchoring decisions (user
confirmed): per-application UNITS (meters for site/quarry/masonry; millimeters for shop pieces), STANDARD
industry dimension figures, a PER-APPLICATION tolerance budget tied to characteristic dimension and the
real process. Style: short sentences, no em dashes. Produced by an 8-agent workflow (6 SLM families +
ROSES synthesis + adversarial verify); all load-bearing code facts spot-checked against source.

## 1. The core finding
Tolerance in this plugin is a multi-decade-of-length problem. The characteristic dimension L spans ~1e-3 m
(fracture rim / gold seam) to ~1e1 m (quarry bench). A single absolute tolerance cannot serve it. The
shipped Rhino document tolerance is 0.01 (1 cm in a meters doc); it is correct at exactly ONE rung
(dry-stone fit ~1 m) and is silently 100x too loose for sawn slabs, ~200x too loose for mosaic, and
~100x too loose for a Kintsugi vessel. The GH 2D nesters never read `RhinoDoc.ModelAbsoluteTolerance`
(verified: zero reads in source); they hardcode the literal 0.01. The fix is one master tolerance read off
the document, overridden per application, with everything else derived from it.

## 2. Scale ladder (5 rungs)
- R1 mm - FRACTURE RIM / SEAM. L ~ 1-10 mm. Crack apertures (1.5-2.0 mm observed), Kintsugi gold seams,
  mosaic grout. Resolution ~0.05-0.1 mm. (Kintsugi seams, Trencadis grout sit here.)
- R2 cm - MOSAIC SHARD / DRESSED FACE. L ~ 40-150 mm. Trencadis + monument shards, Kintsugi fragments.
  Resolution ~0.5-1 mm.
- R3 dm - DIMENSION-STONE PRODUCT / SLAB PART. L ~ 0.2-1.2 m. Slab cut parts, quarry sub-elements.
  Resolution ~0.5-1 mm. (Example 10 is the canonical R3.)
- R4 m - QUARRY BLOCK / MONUMENT / SHEET. L ~ 1-3.5 m. Slab sheet, quarry block, monument body, dry-stone.
  Resolution ~1-10 mm. The ONE rung where the shipped 0.01 m is correct (dry-stone joint).
- R5 10 m - BENCH / OUTCROP / SITE. L ~ 5-50 m. Benches, UTM coords. RECENTER mandatory (float64 loses
  7-9 mantissa digits at 1e5-1e6 m before any algorithm runs).

KEY: each example spans TWO adjacent rungs (a body at one rung, joints one rung finer). This is why
scale-relative epsilon and the per-application unit choice matter: the tolerance must resolve the finer
rung while the geometry is sized at the coarser rung.

## 3. Unified tolerance budget (function of characteristic dimension L and process P)
Three independent quantities, each with a different L-dependence. Conflating them is the root bug (three
unreconciled systems run in the nesters today).

1. GEOMETRIC EPSILON `eps_geo(L) = max(eps_floor, k_geo * L)`, k_geo ~ 1e-3 (0.1% of L). Numeric-hygiene
   quantity (degeneracy, near-coincidence, chord error, Clipper snap). Scales LINEARLY with L. Floor =
   `max(RhinoMath.ZeroTolerance, scan_noise)` (~0.1 mm site, ~0.001 mm shop). HARD CONSTRAINT
   `eps_geo < kerf/3` so it never swallows a real saw cut. Implement via
   `GeometryNumerics.ScaleRelativeEpsilon(modelAbsTol, bboxDiag) = modelAbsTol * max(|diag|,1)`, then
   `ToleranceBudget.From` derives Model=eps_geo, Join=10x, Intersection=0.1x, Snap=0.01x.
2. KERF = PROCESS CONSTANT, independent of L (machine-set). Its YIELD impact scales INVERSELY:
   `relative_loss = kerf/L` (5 mm kerf is 0.4% of a 1.2 m part but 12.5% of a 40 mm shard). Keep kerf a
   SEPARATE physical allowance, never inside the numeric budget (folding it in rejects sub-kerf fractures).
3. CLEARANCE/GAP = f(L, P), the designed joint. sawn/CNC contact: clearance = kerf. masonry:
   `clamp(0.005*L, 1e-3, 1e-2) m`. dry-stone: `clamp(0.02*L, 1e-2, 5e-2) m`. mosaic grout: designed 5-20 mm
   (aesthetic, not from L). ceramic/Kintsugi: firing-shrinkage allowance ~1-2% L + seam width.

COMPOSITION RULE: choose UNITS so bboxDiag > 1 doc-unit; set `modelAbsTol = max(k_geo*L, scan_floor)`
capped below kerf/3; derive eps_geo + Join/Intersection/Snap; set spacing = kerf (cut-whole) or
clearance(L,P) (fitted); keep kerf separate; set Clipper integer Scale = 1/Snap via SafeIntegerScale on
recentered coords.

## 4. Process constants (real fabrication tolerances)
- Diamond WIRE saw kerf 3-10 mm (bead 5-8 mm). Primary quarry KerfDefaultMetres = 0.05 m (50 mm) is the
  published CHANNEL/material-lost allowance, NOT a single bead pass; keep 50 mm for block-pack yield, 5-8 mm
  for slab-saw spacing.
- BRIDGE / BLADE saw kerf 1-3 mm. WATERJET ~0.8-1.2 mm. CNC router ~0.1 mm (0.05-0.15). Engraving ~0.1 mm.
- DRY-STONE joint fit ~cm (target face < 10 mm) - the ONLY app where shipped 0.01 m is correct.
- MASONRY mortar joint 3-15 mm (bed ~10 mm). MOSAIC grout 5-20 mm (Trencadis), 3-6 mm (monument), AESTHETIC.
- CERAMIC firing shrinkage ~1-2% linear. 3D SCAN noise 0.1-2 mm (sets eps floor). PHOTOGRAMMETRY GSD
  2 cm/px bench UAV, 5 mm/px close-range. OBSERVED fracture aperture 1.5-2.0 mm. Quarry half-kerf 25 mm/axis.

## 5. Cross-cutting numeric-hygiene rules (apply once in the shared GeometryNumerics layer)
- A. SEED ONE MASTER TOLERANCE FROM THE DOC. Read `RhinoDoc.ActiveDoc.ModelAbsoluteTolerance`, override per
  application, derive everything via `ToleranceBudget.From`. Today the nesters never read it (hardcode 0.01).
- B. RECENTER BEFORE COMPUTE. Recenter the WHOLE problem (sheet+parts+holes, or block+elements) to combined
  bbox centre; store offset; add back on emit. The 2D path recenters parts but keeps SHEETS in world coords
  (partial bug). Mandatory for R4/R5; free for R1/R2.
- C. SCALE-RELATIVE EPSILON, NOT ABSOLUTE. The `max(scale,1)` clamp means a sub-unit object (a 0.22 m vessel
  in a meters doc) collapses to a bare floor. THEREFORE keep R1/R2 shop pieces in MILLIMETERS so bboxDiag > 1
  and the relative term engages. This is the technical justification for the per-application unit decision.
- D. CLIPPER2 SCALING. The fixed `const Scale = 1000` is not adaptive; the double PathsD API snaps to int64
  at ~2-decimal precision, so emitted geometry inflates ~3-4% (report yield against TRUE input-part area).
  Replace with `SafeIntegerScale(maxAbsCoordAfterRecenter) ~ 1/Snap` AFTER recentering: ~1e5 for meter
  slab/quarry, 1e4-1e5 for mm shop. Correct scale is INVERSELY proportional to L.
- E. THREE-SYSTEM RECONCILIATION. The nesters run three unreconciled tolerances (ZeroTolerance ~1e-12, user
  _tol 0.01, implicit 1/Scale Clipper snap). Wire ONE ToleranceBudget to all gates. Keep BlockCutOpt kerf
  (0.05 m) separate.
- F. UNIT FLOOR DISCIPLINE. `eps_geo` floored at `max(ZeroTolerance, scan_noise)` and capped at `kerf/3`.

## 6. Per-application set (as applied to examples 10-14, 2026-06-06)
| Example | Unit | Overall dims | Element dims | Geom tol | Spacing / grout / kerf | Position |
|---|---|---|---|---|---|---|
| 10_pack2d slab | m | sheet 3.2 x 2.0 m | parts 0.3-1.2 m | 1 mm | spacing = saw kerf 5 mm; Trim 0 | flat XY z=0, lower-left at origin |
| 11_pack3d block | m | block 3.0 x 1.5 x 1.5 m | elements 0.2-1.0 m | 1 mm | kerf 8 mm in-block (or 50 mm primary) | base on z=0, centred XY |
| 12_trencadis | mm | panel ~1100 mm | shards 40-120 mm | 0.05 mm | grout 5 mm (<< shard) | flat XY z=0, lower-left at origin |
| 13_surface monument | mm | 1200 x 1200 x 3500 mm | shards 40-200 mm | 0.05 mm | grout 4 mm | base on z=0, centred XY |
| 14_kintsugi vessel | mm | ~100-220 x 280 mm | fragments 50-150 mm | 0.1 mm | seam 1-3 mm; no kerf | base on z=0, centred XY |

## 7. Master parameter table (recommended values per application)
| Parameter | Component | Recommended | Basis |
|---|---|---|---|
| ModelAbsoluteTolerance (master) | should seed every solver; today hardcoded 0.01, never read from doc | slab 5e-4 m; quarry 1e-3 m; dry-stone 1e-2 m; mosaic 0.05 mm; monument 0.05 mm; vessel 0.1 mm | process anchors; T4 flag |
| Clipper integer Scale | IrregularSheetFillNfpBlf const 1000 | SafeIntegerScale ~1/Snap after recenter; ~1e5 (m), 1e4-1e5 (mm) | T6; emission inflation ~3-4% |
| Scale-relative eps | GeometryNumerics.ScaleRelativeEpsilon (exists, unused by nesters) | baseEps 1e-4 (m), 1e-5 (mm), 2e-4 Kintsugi; keep shop pieces in mm | max(scale,1) clamp |
| Recenter | GeometryNumerics.Recenter | recenter whole problem; mandatory R4/R5 | 7-9 mantissa digit loss at UTM |
| ToleranceBudget (Model/Join/Intersection/Snap) | GeometryNumerics.ToleranceBudget.From (unused) | wire Model->gates, Join 10x weld, Intersection 0.1x accept, Snap 0.01x->Scale | T5 three-system flag |
| Tolerance (2D nester) | default 0.01 | slab 1 mm; mosaic 0.05 mm; CNC 0.01-0.05 mm; rule max(L/1000, scan_floor) and < kerf/3 | area gate tol^2 |
| Spacing (2D nester) | default 0.1 | slab 5 mm (wire) / 1-2 mm (waterjet); mosaic = grout 5-20 mm; 0 only for FreeNestX virtual nest | KB-6 spacing floor |
| Trim Tolerance (V506) | default 0.1 | 0 for cut-whole production; 3-10 mm masonry shared-contact; 0 mosaic. V506 overlap = this default, by design | hover even says try 0.1-1.0 |
| Discretization Tolerance | default -1 (inherit) | slab 1-2 mm; mosaic 0.05-0.1 mm; CNC 0.02-0.05 mm; keep verts < 150 (cap 200) | NFP O(nA*nB) |
| Min Boundary Affinity | default 0.5 | 0.3-0.45 dense hug; 0.7-0.8 strict; DIMENSIONLESS [0,1] (the one scale-independent param) | normalized |
| Cell Size (LEGACY heightmap only) | default 10.0 | min-element/6..10 (0.02-0.05 m). NOT in 11_pack3d (BlockPackTree has no grid) | 10 m cell collapses grid |
| Forests (BlockPackTree, the real 11_pack3d) | default 256 | 256 (plateau f~50-1000); cap via Memory Budget | Kim 2025 |
| Kerf Width (BlockPackTree) | default 0.0 | 3-10 mm in-block wire; 50 mm primary-quarry yield; keep separate from numeric budget | over-estimates yield at 0 |
| Rotations (2D) / Rotation Mode (3D) | {0,90,180,270} / 0 | slab {0,90,180,270} or {0,180} to respect vein; quarry Rotation Mode 1 (vein-aligned); mosaic free | grain matters |
| CGAL dihedral Angle (segmentation) | input degrees | 5-15 strict planarity; 30-60 smooth bands (monument used 35); 90+ orthogonal creases | detect_sharp_edges |
| Kintsugi Joint Width / Sample Spacing | normalized | scale to fragment size; vessel eps 0.1 mm = scan-noise floor | scale-invariance |

## 8. Verification corrections (adversarial pass)
- The Cell-Size-10.0 critique applies to the LEGACY heightmap packers (Pack3DMeshHeightmap /
  Pack3DIrregular / Pack3DIrregularContainer, all default 10.0), NOT the flagship 11_pack3d, which uses
  Block Pack (Tree) / DLBF (no cell size; levers are Forests + Kerf + Memory Budget).
- V506 part-to-part overlap is BY DESIGN: the default Trim Tolerance 0.1 permits overlap-then-trim; the
  clean geometry is the Trimmed Curves output (or set Trim Tolerance = 0). FreeNestX is strict 0-overlap by
  construction and is what example 10 now uses (verified 0.0 overlap, 16/16 placed, in the rebuilt .gha).
- Source line numbers in older SLM cards drifted after the 2026-06-06 evolution edits; GeometryNumerics,
  Clipper2Adapter, component, and BlockCutOptTolerances line numbers are current.
- Two parallel source trees exist (this repository's `src/` and the older Template-General mirror),
  identical constants, different namespaces.

## 9. Open questions for Libish (source-code policy, beyond the example-file fixes)
1. Change the shipped Rhino doc tolerance default 0.01 -> 0.001 in the meters template, or override
   per-component from the budget? (Per-component is safer; affects existing files either way.)
2. Reconcile the kerf model: 50 mm primary-quarry channel vs 5-8 mm slab-saw bead. Which ships per example?
3. Monument (13): confirmed mm for the shard/grout work (applied). Body modelling in mm too (applied) vs a
   split m-body/mm-shard workflow?
4. Flip the V506 Trim Tolerance default 0.1 -> 0 so cut-whole production is out-of-box? (Changes behaviour.)
5. Pack3D legacy Cell Size: keep absolute default (unit-fragile) or auto-derive from min-element/8?
6. Kintsugi firing shrinkage: fragments are scanned POST-firing (shrinkage already happened), so the only
   fit allowance is scan noise + seam width. Model shrinkage at all, or drop it?

## 10. Source UX fixes APPLIED (2026-06-06, user-approved, rebuilt + redeployed + tested live)
All six decided; users now get correct behaviour out of the box (no manual per-scale tuning):
- Q1 + Q6 (auto tolerance): the 2D nesters (Frahan Sheet Pack Unified + Freeform Sheet Nest Exact NFP)
  default Tolerance = 0 = AUTO. When 0, the component computes a SCALE-RELATIVE epsilon =
  1e-4 x sheet-bbox-diagonal, tightened to the doc tolerance when the user set a finer doc. (The raw doc
  tolerance alone, 0.01 m in a metre model, is too loose and overlapped exact-NFP parts; the first auto
  attempt used it and FAILED the 0-overlap test, so it was updated to scale-relative.) VERIFIED LIVE:
  FreeNestX with Tolerance=0 packs 14/14 at overlap 0.0.
- Q3 (V506 Trim default): Trim Tolerance default flipped 0.1 -> 0.0 (strict no-overlap out of the box).
  VERIFIED: default reads 0.
- Q4 (Pack3D Cell Size): legacy heightmap packers (Pack3D Mesh Heightmap / Irregular / Irregular
  Container) default Cell Size = 0 = AUTO, deriving the grid from the smallest element (min bbox edge / 8).
  The shipped 10.0 packed nothing in a metre model. VERIFIED: auto packs 8/8 with a 3200-cell grid.
- Q2 (kerf): kept as a SEPARATE physical allowance, set per example (50 mm primary-quarry yield for
  11_pack3d; 5-8 mm slab-saw spacing for 10_pack2d). No risky unit-ambiguous source default.
- Q5 (firing shrinkage): dropped. No shrinkage parameter exists in the Kintsugi source; fragments are
  scanned post-firing, so the only fit allowance is scan noise + seam width (documented, not a knob).
Rebuilt Frahan.StonePack.gha (0 errors), redeployed to the Grasshopper Libraries folder, refreshed
install/plugin/. Open: whether to also lower the shipped Rhino doc-template tolerance (left at 0.01;
per-component auto override is the safer path already taken).
