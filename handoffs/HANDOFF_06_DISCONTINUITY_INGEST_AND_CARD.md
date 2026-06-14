# HANDOFF 06 — Discontinuity ingest (Feature A) + Stereonet/Block-size card (Feature B)

Date: 2026-06-14. Branch: `feat/discontinuity-csr-worker`. Authored from a Cowork
session (Linux, no .NET/Rhino) that wrote the C# source and verified the C++ worker.
**This is the build/test/validate handoff for Claude Code on Windows.** Read
`AGENTS.md` first; truth criterion is (c) visual validation in Rhino; HITL on
>5-file commits and ALL pushes.

---

## 0. TL;DR — what to do
1. `git checkout feat/discontinuity-csr-worker` (already current).
2. Build Core → GH → Tests (commands in §3). Fix any compile errors (the source was
   written without a compiler available — see §6 for the few things to watch).
3. Run the test suite; confirm the new **Discontinuity*/Block*/Stereonet** tests pass.
4. Deploy the `.gha`, drop the two new components on a canvas, validate live in Rhino
   (§4). Capture HITL PNGs.
5. Commit (HITL: >5 files → get Libish's OK). Hold the push for review.
6. Optionally retune the worker default bandwidth (§5 finding).

## 1. What this delivers (the HANDOFF_05 plan, implemented)
**Feature A — discontinuity trace/plane INGEST** (the inverse of the worker: read
orientations a geologist/CloudCompare-Compass survey already measured):
- `src/Frahan.StonePack.Core/Discontinuity/Ingest/Discontinuity.cs` — model
  (`Discontinuity`, `DiscontinuityKind`, `DiscontinuityCollection`) + factories
  (`FromPlane`, `FromDipDipDir`, `FromTrace` via PCA/TLS).
- `src/Frahan.StonePack.Core/Discontinuity/Ingest/DiscontinuityReader.cs` —
  `DiscontinuityReader.Load(path)` dispatch + `Csv`/`Dxf`/`GeoJson`/`Shapefile`
  readers. CSV + DXF hand-rolled; GeoJSON/SHP use the already-referenced
  `NetTopologySuite.IO.GeoJSON` 4.0.0 / `.Esri.Shapefile` 1.2.0. Bad rows are
  skipped with a warning, never thrown.
- `OrientationMath.NormalFromDipDipDir(dip, dipdir)` — new inverse of `DipDipDir`.
  **NB:** I did NOT use HANDOFF_05's literal formula `(.., .., +cos dip)` folded —
  that round-trips 180° off in dip-direction. The correct pole is
  `(sin·sin, sin·cos, −cos)` (z sign only). Verified by the round-trip unit test.
- `src/Frahan.StonePack.GH/Quarry/DiscontinuityIngestComponent.cs` — **D5F10049**,
  Frahan > Quarry. In: File, Origin(offset). Out: Planes, Traces, Dip, Dip dir,
  Set id, Report.

**Feature B — Stereonet + Block-size CARD:**
- `src/Frahan.StonePack.Core/Discontinuity/StereonetProjection.cs` — equal-area
  (Schmidt) + Wulff lower-hemisphere projection + great-circle traces.
- `src/Frahan.StonePack.Core/Discontinuity/BlockSizeMath.cs` — Palmström `Jv`,
  `Vb = s1 s2 s3 / (sinγ12 sinγ23 sinγ31)`, `Ib`, `Deq`, `RQD`; guards `s=0`,
  `S<3` (slabs/columns), near-parallel sets; `unitScale` → metres.
- `src/Frahan.StonePack.GH/DiscontinuitySetsAsyncComponent.cs` (**D5F10048**) —
  surfaced the previously-dropped data: new **Share** output, new **Keep facets**
  input (copies `facets.csv` to a stable path) + **Facets path** output. Inputs/
  outputs were *appended* so saved canvases stay valid.
- `src/Frahan.StonePack.GH/Quarry/StereonetBlockSizeComponent.cs` — **D5F1004A**,
  Frahan > Quarry. **Self-presenting**: draws the net + great circles + poles +
  labels + Jv/Vb/RQD readout in `DrawViewportWires` (reopening the .gh cold
  reproduces the figure — no bake script). Also emits the geometry as outputs.

**Tests:** `tests/Frahan.StonePack.Tests/DiscontinuityIngestTests.cs` (16 methods),
registered in `Program.cs` (orientation round-trip; CSV header/coeff/headerless/
bad-row; DXF LWPOLYLINE/3DFACE; GeoJSON point+line; block size Vb=3.0 & Jv≈2.167 &
2-set/near-parallel guards; stereonet range; both components' metadata).

## 2. C++ worker — already verified this session (Cowork/Linux)
The worker is unchanged. I compiled it with native g++ and ran a synthetic
ground-truth test (`native/discontinuity_worker/test/`): it recovers 3 planted
sets to <1.5° dip, is deterministic, and emits facets.csv + segmented.ply. **One
finding:** at the default `--bw 15` it finds 2 of 3 clean sets; at `--bw ≤ 12` it
finds all 3 (documented bandwidth sensitivity). See `test/SYNTHETIC_TEST.md`.

## 3. Build + test (Windows)
```
cd D:\frahan-stonepack
"C:\Program Files\dotnet\dotnet" build src/Frahan.StonePack.Core/Frahan.StonePack.Core.csproj -c Release -v minimal
"C:\Program Files\dotnet\dotnet" build src/Frahan.StonePack.GH/Frahan.StonePack.GH.csproj   -c Release -v minimal
"C:\Program Files\dotnet\dotnet" build tests/Frahan.StonePack.Tests/Frahan.StonePack.Tests.csproj -c Release -v minimal
set FRAHAN_SKIP_NATIVE=1
"C:\Program Files\dotnet\dotnet" run -c Release --project tests/Frahan.StonePack.Tests
```
Expect the JointSetDfn + Discontinuity* tests to pass green.

## 4. Live Rhino validation (the truth criterion)
1. Back up the live `.gha`, copy the new one to
   `%APPDATA%\Grasshopper\Libraries\Frahan.StonePack.gha` (close Rhino first; it's locked while running).
2. **Feature A:** drop **Discontinuity Ingest**; feed a real exported file. Ask
   Libish for a CloudCompare/Compass DXF or a dip/dipdir CSV; otherwise use a
   hand-written `dip,dipdir,x,y,z` CSV. Bake Planes; eyeball orientations.
3. **Feature B:** wire **Discontinuity Sets (Async)** (Run=true, Keep facets=true)
   → **Stereonet + Block Size** (Dip/Dip dir/Spacing/Share/Facets path). Confirm
   the net + poles + great circles + readout draw on the canvas; reopen the .gh
   cold and confirm it reproduces (self-presenting). Capture `3dm` + `png` HITL.
4. End-to-end: a real cloud (e.g. Tongjiang) → sets → card. **Watch UNITS** (§ next).

## 5. Known caveats to verify
- **UNITS (highest risk):** worker spacings are in the cloud's units. A metre-scale
  detail scan with mm spacings makes `Jv` huge and `RQD`=0. The card exposes
  `Unit scale` and labels the numbers a proxy — confirm the label shows and the
  guidance is clear on the live card.
- **Bandwidth:** see §2 — consider lowering the D5F10048 default `Bandwidth` from 15
  (or add a preset). Don't change the worker's deterministic seeding.
- **DXF dialects:** ASCII only; LINE/LWPOLYLINE/POLYLINE/3DFACE; bulge arcs
  linearised with a warning; binary DXF rejected with a warning.

## 6. Things to watch at compile (written without a compiler)
- Core uses `NetTopologySuite` (GeoJSON 4.0.0 / Esri 1.2.0) — both already in the
  csproj. The readers use `GeoJsonReader().Read<FeatureCollection>`,
  `Shapefile.ReadAllFeatures(path)`, `IAttributesTable.GetNames()/GetOptionalValue`.
  If any API name differs in the pinned version, adjust (the parsing logic is
  isolated in `GeoJsonDiscontinuityReader` / `ShapefileDiscontinuityReader`).
- `DiscontinuityReader` calls `CloudMath.Pca` (internal, same assembly — OK).
- The GH components use `Rhino.Display` (`PointStyle`, `Draw2dText`, `Text3d`) —
  available in RhinoCommon/Grasshopper net48.
- Component param counts are asserted by tests (Ingest 2 in/6 out; Stereonet 9 in/9
  out) — if you change the params, update the metadata tests.

## 7. Commit scope (HITL — get Libish's OK; >5 files)
New: `Discontinuity/Ingest/Discontinuity.cs`, `Discontinuity/Ingest/DiscontinuityReader.cs`,
`Discontinuity/BlockSizeMath.cs`, `Discontinuity/StereonetProjection.cs`,
`GH/Quarry/DiscontinuityIngestComponent.cs`, `GH/Quarry/StereonetBlockSizeComponent.cs`,
`tests/.../DiscontinuityIngestTests.cs`, `native/discontinuity_worker/test/*`.
Modified: `Discontinuity/OrientationMath.cs`, `GH/DiscontinuitySetsAsyncComponent.cs`,
`tests/.../Program.cs`. Suggested message:
`feat(quarry): HO5 — discontinuity ingest (CSV/GeoJSON/DXF/SHP) + stereonet & Palmström block-size card`.
Verify `git status` on Windows (LFS present) before staging — the Linux session
could not run LFS, so data/binaries showed spuriously modified.

## 8. Definition of done
- A: `DiscontinuityReader.Load` parses CSV/GeoJSON/DXF/SHP; D5F10049 bakes
  planes+traces; tests pass; live-validated; committed (HITL).
- B: D5F10048 surfaces Share + facets; D5F1004A draws the stereonet+block-size
  card self-presented; orthogonal fixture (Vb=3.0) passes; units guarded;
  live-validated; committed (HITL).
- Update memory `project_discontinuity_worker_csr_clean_room` + its MEMORY.md line.
