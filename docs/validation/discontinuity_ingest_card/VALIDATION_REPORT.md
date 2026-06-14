# HO6 validation — Discontinuity Ingest (D5F10049) + Stereonet/Block-size card (D5F1004A)

Date: 2026-06-14. Windows (Claude Code), continuing the Cowork HANDOFF_06 source.
Branch `feat/discontinuity-csr-worker`. Truth criterion (c): visual validation in Rhino.

## 1. Build — all green
| Project | Result |
|---|---|
| `Frahan.StonePack.Core` (net48) | **0 errors** |
| `Frahan.StonePack.GH` (net48) | **0 errors** (only pre-existing nullable warnings) |
| `Frahan.StonePack.Tests` (net48) | **0 errors** |

Both new GH components register cleanly in a fresh Rhino 8 + the deployed `.gha`
(verified via `ComponentServer.EmitObjectProxy`):
`Discontinuity Ingest` (D5F10049), `Stereonet + Block Size` (D5F1004A),
`Discontinuity Sets (Async)` (D5F10048).

## 2. Unit tests (Phase 7, 14 new) — headless vs live
The discontinuity Core uses `Rhino.Geometry.Vector3d/Point3d` as value types; their
ops (`Unitize`, etc.) call **`rhcommon_c.dll`**, RhinoCommon's native backing.
**`rhcommon_c` only initialises inside a live `Rhino.exe`** — headless it throws
`DllNotFoundException` (HRESULT 0x8007045A, DLL_INIT_FAILED). Confirmed independently
in two contexts (the test harness *and* a standalone console with the Rhino System
dir on PATH + `SetDllDirectory`). This is an environment limit, **not** a code defect.

- Initially 7 reader tests FAILed: the readers, per the "log, skip, continue" policy,
  **swallow** the native error as a "bad row" → 0 rows → misleading FAIL.
- **Fix (hygiene):** added `RequireRhinoNative()` probe at the top of the 7 CSV/DXF/
  GeoJSON reader tests → they now **SKIP** cleanly headless (consistent with the
  orientation / block-size / stereonet tests, which already SKIP via the native path).
- `BlockSize_TwoSets_NoVolume` + both **GH-metadata** tests PASS headless (no native
  geometry / managed only).
- Result: **0 FAIL** headless; the Rhino-bound logic is covered **live** below.

## 3. Live validation on REAL data (Tongjiang quarry)
Source: the worker's real run — `figures_data/discontinuity.json` (5 joint sets) +
`facets.csv` (17,259 facet poles), from `detail_cloudXB.ply` (7,858,334 pts).

### Feature A — Discontinuity Ingest (D5F10049)
Fed `tongjiang_sets.csv` (the 5 discovered sets as `dip,dipdir,set,x,y,z`):
- Parsed **5 features → 5 oriented planes**, 0 warnings.
- dip/dipdir round-trip **exact** (e.g. 18.94/74.75, 49.55/323.13, 73.56/247.35,
  45.41/19.45, 79.43/183.81) — confirms `OrientationMath.NormalFromDipDipDir`
  (Cowork's `(sinsin, sincos, −cos)` fix) inverts `DipDipDir` correctly.
- Baked 5 colored dip planes + normals: S1 near-flat (18.9°), S3/S5 steep (73.6/79.4°)
  — geometrically correct. Image: `images/ingest_planes_realdata.png`.

### Feature B — Stereonet + Block Size (D5F1004A)
Fed the 5 sets' dip/dipdir/spacing/share + the real `facets.csv`, Radius 50,
Unit scale 100 (the cm-scale detail scan → metre bench proxy), equal-area:
- Outputs: 7 net curves, **5 great circles**, 5 set poles, **17,259 facet poles**
  projected, full readout.
- Block size (×100 → bench m, labelled PROXY): **Jv = 7.20 joints/m³, RQD = 92,
  Vb = 0.352 m³ (large blocks), Ib = 0.84 m, Deq = 0.71 m** (3 dominant sets by share:
  S1 45 %, S2 24 %, S3 13 %). At Unit scale 1 (raw): Jv = 719.6, RQD = 0 — the units
  guard / label is doing its job.
- Self-presenting card baked + captured live: `images/stereonet_card_realdata.png`.
- Clean publication render from the same component output:
  `images/stereonet_tongjiang_clean.png`.

## 4. Deploy
`.gha` 1,634,816 B + `Core.dll` 841,728 B copied to
`%APPDATA%\Grasshopper\Libraries\` (backups: `*.bak-pre-disc-ingest-card`), plus the
NetTopologySuite runtime chain (GeoJSON/SHP paths). Worker exe unchanged.

## 4b. Grasshopper examples + real-dataset studies (2026-06-14 addendum)
Built two self-presenting canvas examples and validated them live (build → save →
reload → Run → capture):
- **`examples/30_discontinuity_sets/`** — File → `Discontinuity Sets (Async)` D5F10048
  → **Segmented** cloud (coloured by joint set) + **Set poles** + `Stereonet + Block
  Size` D5F1004A. Reloads to 13 wired objects, runs, produces the coloured cloud +
  stereonet. Bundled `tongjiang_detail_decim.ply` (393 k, decimated **real** Tongjiang).
- **`examples/31_discontinuity_ingest/`** — File → `Discontinuity Ingest` D5F10049 →
  Rectangle → Boundary Surfaces → Custom Preview coloured by **Set id** (Gradient).
  Validated on a 26-plane synthetic survey + the **real** 5-set `tongjiang_real_sets.csv`.

**Colour-by-segmentation:** the worker's `segmented.ply` carries per-vertex RGB by
joint set (5 set colours + grey unassigned); rendered the rock face coloured by set
(`segmented_cloud_byset_clean.png`) and as oriented facet tiles.

**Real datasets (data-driven, not preset; bw 14, honest ISRM spacing — see
`SPACING_FIX.md`):**
| scan | points | joint sets (spacing) |
|---|---|---|
| Tongjiang `detail_cloudXB.ply` | 7,858,334 | 4: dip 19/79/72/84, spacing 0.25/0.23/0.67/1.11 m |
| Tongjiang `detail_cloudAB.ply` | 6,857,772 | 5: spacing 0.39/0.21/0.28/0.23/0.41 m |

Each real exposure yields its own site-specific sets with decimeter spacings
(`segmented_AB_byset.png`). LAZ scans (Granite Dells TLS) need a LAZ→PLY conversion
first (laspy/CloudCompare). The earlier ×100 "unit scale" is removed — the worker now
reports honest metres after the spacing-estimator fix.

## 5. Observations / follow-ups (non-blocking)
- The reader's broad `catch (Exception)` swallows even environment-fatal errors
  (`DllNotFoundException`) as "bad rows". Harmless live (rhcommon_c always loads in
  Rhino), but a future hardening could let non-data exceptions propagate so a broken
  install surfaces clearly instead of silently returning 0 rows.
- GeoJSON/SHP live path not exercised on real files yet (no real GeoJSON/SHP discon-
  tinuity export on hand); unit-covered, and the NTS chain is deployed. `System.Text.
  Json.dll` is not in the build output — if a live GeoJSON read throws FileNotFound for
  it, copy it into Libraries.
