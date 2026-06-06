# 17 - Frahan Roadmap and Timeline

**Spec version:** 0.1
**Status:** indicative; dates are quarterly windows, not commitments.
**Sources:** the runbook, the live release zips under
`dist/` and `share/`, and `Template-General/outputs/2026-05-01/frahan_stonepack/CHECKPOINT_028.md`.

## 1. Released

| Version | Date | Highlights |
| --- | --- | --- |
| 0.1.0 | early 2026 | First Rhino 8 build; basic 3D heightmap packer |
| 0.2.0 | early 2026 | 2D integration |
| 0.4.0 | early 2026 | NFP restore + irregular sheets |
| 0.5.0 | mid 2026 | 2D source alignment + sheet fixes |
| 0.5.1 | mid 2026 | Irregular sheet hole + speed fix |
| 0.5.2 | mid 2026 | NFP speed defaults |
| 0.5.3 | mid 2026 | 2D speed parity + angled sheet candidates |
| 0.5.4 | mid 2026 | Corner modes + irregular hole speed |
| 0.5.5 | mid 2026 | Focused 2D UI + optional holes (last yak release: `frahan_stonepack-0.5.5-rh7_37-win.yak`) |
| **0.5.6** | 2026-05-02 | **Current candidate**: bundled BFF + SuiteSparse + OpenBLAS runtime; surface packing prototype |

The version inventory is derived from
`outputs/2026-05-01/frahan_stonepack/share/Frahan.StonePack_Rhino8_*.zip`
and matching CHECKPOINT files. See `docs/index/frahan_archive_inventory.md`.

## 2. Q2 2026 (now): documentation freeze + small fixes

- **This run:** documentation refactor + small safe fixes (the
  current overnight nightshift).
- Bug B1 single-line fix in both research markdowns (next agent).
- Test namespace addition (next agent).
- `THIRD_PARTY_NOTICES.md` first draft.
- 0.5.6 yak bundle (`frahan_stonepack-0.5.6-rh8-win.yak`) created
  from the existing `dist/frahan_stonepack-0.5.6-rh8-win.zip`.

## 3. Q3 2026: V2/V3/V506 collapse + namespace plan

- Refactor R3 (V2/V3/V506 collapse) implemented behind
  `[GH_ParamObsolete]`-style migration.
- Refactor R1 (Frahan.StonePack.* → Frahan.*) **planned**, not
  implemented; parallel-namespace transition document published as
  `docs/future_work/R1_namespace_transition_plan.md`.
- 2D Trencadis boundary rail index lands (spec **05** § 4) on a
  feature branch.
- Surface packing distortion-report polish.

## 4. Q4 2026: 3D Ashlar descriptors

- 3D stone descriptor + face-match (spec **07** § 4) on a feature
  branch.
- First `Frahan.Mesh` extraction from `Frahan.Core` (refactor R2  - 
  pure-managed `Frahan.Mesh`).
- `Frahan.NativeBridge` interfaces shipped (no native backends
  yet).
- 0.7.0 release candidate.

## 5. Q1 2027: GeoPack alpha

- Crack candidate detection (geometric only).
- Crack surface fit (planar + quadric).
- Block graph construction (managed).
- 0.8.0-alpha release.

## 6. Q2 2027: GeoCut alpha

- Slab candidate generator + crack-slab intersector.
- Slab yield optimiser (greedy v1).
- 0.9.0-alpha release.

## 7. Q3 2027: Native backends opt-in

- `Frahan.Native.Packing` first; license-low-risk (VHACD / CoACD).
- `Frahan.Native.GeometryCore` second; depends on starter zip
  license confirmation.
- CGAL-backed paths remain OPT-IN with a runtime warning.

## 8. Q4 2027: Learning-guided ordering (gated)

- Gate per spec **12** § 1.
- ONNX Runtime integration in `Frahan.Core.Learning`.
- First trained scorer trained outside Frahan (PyTorch Geometric).

## 9. Long term: QuarryCutOpt + CI

- QuarryCutOpt extraction-order optimiser (greedy v1, MIP v2).
- GitHub Actions CI for V1 + V2 verification.
- `Frahan.Benchmarks` dashboard.

## 10. Risk-weighted tracking

| Item | Risk | Mitigation |
| --- | --- | --- |
| BFF licensing | medium | publish LICENSE in `THIRD_PARTY_NOTICES.md` before next release |
| CGAL GPL | high | keep CGAL backend opt-in; isolate behind `Frahan.Native.CGAL` |
| Snapshot tree duplication | medium | one source of truth before R1 |
| `Pack2DIrregularSheetV506Component` GUID stability across the V2/V3/V506 collapse | medium | document `ComponentGuid` per class before any rename |
| GH_TaskCapableComponent regression on cancellation | medium | add a regression test before 0.7.0 |
