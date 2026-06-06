# ROSES synthesis: which 2D packer for which application

Date: 2026-06-06. Style: short sentences, no em dashes. ROSES = the interdisciplinary synthesis tier
(Research Framework V4: T0 PRISMA statistics, T1 SLM algorithm-math+code, T2 ROSES synthesis). This guide
maps each kept 2D packer to the fabrication application it is best for, grounded in the measured study
(`pack2d_study_metrics.csv`, `--pack2dstudy`) and the SLM/PRISMA reviews (`SYNTHESIS_2D.md`,
`SYNTHESIS_BEYOND_BLF.md`). The selection rule is always: choose by the job, gate on overlap == 0, and read
yield as `util_stock = placed true-area / (sheet - holes)` (80% = good irregular-nesting result).

## Decision matrix (measured)
| Application | Recommended packer | util_stock (measured) | Speed | Why |
|---|---|---|---|---|
| Fast interactive nesting, simple sheet, design-time feedback | **V2** (the V506 default delegation) | 60-65% | ~0.1-0.5 s | fastest 0-overlap engine; instant canvas feedback |
| Maximum material yield, valid (production cut sheets) | **NFP-BLF evolved** (FreeNestX, spacing 0) | **82-90%** (crosses 80% incl. 3 holes) | ~2-8 s | exact NFP feasible region + multi-start + reinsertion + concave verify; the only 0-overlap engine over 80% |
| Sheet WITH holes / defect avoidance, max fill | **NFP-BLF evolved** (holes path) | 84.7% (L+hole), 89.6% (3 holes) | ~3-8 s | holes honored exactly as NFP obstacles; fully contained |
| Rectangle / strip / roll stock | **NfpRect mode0** (`NfpPack2DComponent`) | high covUsed on strip | ~3 s | rectangle-strip nester; keep mode2 OFF (it overlaps on dense input + 15-78 s) |
| Aligned ring / frame / coursing layout (parts hug the boundary) | **V506 bmode2 (ring)** or **bmode3 (curve-division)** | 40-55% (alignment, not fill) | ~0.1-0.4 s | the only modes that align a part edge to the boundary tangent; valid, contained, 0 overlap |
| Curve-driven placement along a designer edge | **V506 bmode3** (winding-fixed) | aligned coursing | ~0.2 s | one part per arc-length station, longest edge tangent to the curve |
| Artistic mosaic (grout / intentional contact, overlap by design) | **Trencadis** (CVD-Lloyd + GVF) | overlap-accept | ~0.3 s | a different class (physics-field mosaic), not a cut-nesting packer |
| Chasing the last ~5-10% toward the ceiling | **NFP-BLF evolved + full multi-part GLS** (staged) | +bounded over 89.6% | slow (needs NFP cache) | the SOTA lever; the single-part GLS scaffold ships but adds nothing alone (see below) |

## Per-application notes
- **Production cut sheets (the headline):** use FreeNestX (the evolved exact NFP-BLF) at spacing 0. It is the
  only valid packer crossing 80% util_stock on every oversubscribed/holed fixture (82.0% / 84.7% / 89.6%).
  Do NOT route this through V506-quality for max density: V506 clamps spacing to 0.1 (KB-6), which drops it
  to 61-64%. V506-quality is for when you want V506's hole/boundary features WITH the spacing floor.
- **Interactive design:** V2 at ~0.1 s gives instant feedback; switch to FreeNestX for the final cut sheet.
- **Aligned/architectural coursing:** the V506 boundary modes (1 bias, 2 ring, 3 curve-division) are the
  only ones that produce tangent-aligned, boundary-hugging layouts BLF cannot. They trade fill (40-55%) for
  alignment. bmode2 places the most; bmode3 is the cleanest curve-driven coursing (now winding-fixed so
  Rhino-default CCW holes no longer drop parts). For max fill of the same holed sheet, fall back to FreeNestX.
- **Strip stock:** NfpRect mode0 is the rectangle-strip specialist. mode2 (multi-start) reads higher but
  overlaps on dense input and is 15-78 s; keep it off in production.
- **Mosaic:** Trencadis is an overlap-accept physics-field class, not a substitute for a cut nester.

## What to AVOID (measured, invalid or dominated)
- **V1 (SheetFillRhino):** produces overlapping (invalid) packs (9-31 overlap pairs). Never use.
- **Standalone BottomLeftFill:** overlaps + slow. Use the NFP nester instead.
- **V3:** strictly dominated by V2. No application.
- Any packer selected purely on placedCount without checking overlap: the highest counts (V1 53, NFP-greedy
  57, NfpRect2 55) are all INVALID (overlapping). Always gate on overlap == 0.

## The "is there a better packer than BLF" verdict (carried from SYNTHESIS_BEYOND_BLF)
BLF is the right base. The evolved NFP-BLF already meets the 80% bar with holes. The genuinely-SOTA lever is
overlap-minimization Guided Local Search (sparrow/jagua-rs class), net48-reimplementable with our Clipper2
primitives, to be added as an opt-in Core lever (not a base swap). The single-part GLS separation insert is
shipped (sound, default-off) but measured 0 extra parts on these fixtures: it cannot open gaps that require
rearranging placed parts. The full multi-part GLS (move all parts + outer compress, with an NFP cache for
tractability) is the remaining lever; bounded expected gain since the bar is already crossed, and no
SOTA-parity claim is valid until an ESICUP instance is run as bbox-density.

## Canvas exposure (aligns with W2/W7 + the study)
KEEP on the ribbon: `IrregularSheetFillComponent` (unified, V2 default; add the Quality=FreeNestX toggle),
`IrregularSheetFillNfpBlfComponent` (FreeNestX, the density leader), `NfpPack2DComponent` (strip, mode0),
the Trencadis family, and the V506 boundary modes. HIDE: V1, V3, standalone BottomLeftFill (invalid or
dominated; already Obsolete+hidden where they are ribbon components).
