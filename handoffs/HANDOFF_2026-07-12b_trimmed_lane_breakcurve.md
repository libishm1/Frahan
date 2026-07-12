# Handoff — trimmed partial-overlap lane + break-curve alignment (2026-07-12, session B)

Fresh-session onboarding: read `AGENTS.md` first (truth criterion (c)
visual validation; HITL gates mandatory). Style: short sentences, no em
dashes. Previous handoff: `HANDOFF_2026-07-12_kintsugi_n3_async_realscan.md`.
Branch `fix/kintsugi-fracture-generator` pushed through `6807cf1`.

## State of the real-scan (Roughness Mode) lane

Shipped and verified this session, in commit order:
- `48c7989` KB-12 fix: describe-cap top-24 + sanitation pre-pass; debris
  negative control PASSES (8 shards, anchor only, ~1-2 s).
- `c80214b` example `14_kintsugi_fantastic_breaks_bench.gh` (no data in
  repo; dataset has NO license, Libish authorized personal download:
  D:\Data\fracture_datasets\FantasticBreaks_v1, 5 objects extracted).
- `6a67416` Roughness Mode (inputs Rm/Rt): fracture/skin by local normal
  dispersion, area-weighted Otsu, components = facets.
- `ba155d3` README status.
- `6807cf1` TRIMMED point-to-mesh ICP lane (this session's core).

## The trimmed lane (Facet Match, RoughMode branch of the pair loop)

Candidates skip boundary registration; pose comes from TrimmedRegister:
mirrored cand frame -> host frame at 12 in-plane spins, coarse
point-to-mesh prefilter, top-3 refined by TrimmedIcpMesh against the
FULL host fragment mesh. Inside the ICP, correspondences require:
1. FRONT-SIDE: (sample - hit) . hostFaceNormal > -0.2*candSpacing.
   Thin shards otherwise pair THROUGH the wall and the Kabsch update
   drags the fragment through material (measured, FB 00002).
2. OPPOSITION: candNormal . hostFaceNormal < -0.2.
3. Trim to 60% of the VALID set (n-based trim never trimmed once the
   filters rejected half the samples; the bleed tail poisoned the
   objective).
Pre-gates in this lane: aRatio >= 0.12 only (extent/relief-similarity
gates are congruence assumptions and stay OFF here).

Roughness segmentation hard lessons (do NOT retry):
- Radius must be AREA-based. Bbox-diagonal radius made segmentation
  POSE-DEPENDENT (rigid scramble changed the chip's top region
  1027 -> 154 area).
- NO per-edge normal-coherence constraint during growth. Tried twice,
  removed twice: per-edge dihedrals are resolution-dependent and shred
  the finer-meshed side into slivers.

## MEASURED recall status (FB 00002, mug + chip)

GT harness (reflection into the private statics + cached
`D:\code_ws\outputs\2026-07-12\kintsugi_n3plus\fb00002_pair_scaled.3dm`;
X = known scramble; measure pose error CHIP-LOCAL -- far-probe metrics
amplify small rotations into fake 100% drift):
- Chip fracture region: found, jaccard 0.65, rank#0 (radius factor 0.02,
  growth unconstrained).
- GT-seeded ICP converges 13 units from truth = 26% of chip extent: the
  chip SLIDES ALONG the narrow break band. Physically near-degenerate at
  this decimation (wall thickness ~ sample spacing ~ 2; the 100:1
  decimation of the 2.48M-face scan destroyed most relief).
- Explained-coverage / opposition-fraction / penetration statistics do
  NOT separate the slide from truth (E 0.67 GT vs 0.70 false nest).
- Precision intact with the lane LIVE: debris control places nothing;
  synthetic N=3 regions mode 3/3 CORRECT.

## NEXT (Libish: "implement that"): BREAK-CURVE alignment

The missing constraint is the crack outline (pottery/fresco cue, RQ2 in
the research pack). Design:
1. Boundary point set per facet: FacetBoundaries(m, facet) already
   yields the region's boundary line segments; take segment midpoints as
   a boundary point CLOUD (no curve joining needed).
2. In the trimmed lane, pass candBoundary (fragment coords) and
   hostBoundary (world pose) point sets into TrimmedIcpMesh.
3. Each ICP iteration adds boundary-to-boundary NN pairs (radius-capped
   like the surface pairs, front-side/opposition not applicable) into
   the SAME Kabsch solve, weighted (duplicate boundary pairs ~2x). The
   outline is a closed curve: sliding along the band misaligns it, so
   the band-slide degeneracy dies.
4. Scoring/gates: add boundary rms + boundary coverage; rank by a
   combined multiple; keep the existing penetration gate.
5. Watch out: the HOST region boundary includes interior-bleed edges,
   not only the hole rim. Trimming handles partial overlap; if not, keep
   only host boundary segments whose adjacent skin face is SMOOTH (low
   roughness) -- those are the physical crack line on the skin.
6. Second lever if needed: gentler decimation (~100k faces for the host;
   Reduce needs normalizeSize:true on the ~0.001-unit FB scans; the
   2.48M->25k pass took ~4 min, budget accordingly).

Acceptance for the pair: chip-local error < ~3 units (1.5x spacing) from
GT on FB 00002, E2E through the component (async, poll by pumping
NewSolution(false) -- do NOT force-expire every poll, that races the
completion schedule and can kill Rhino). Then: repeat on objects
00003/00005/00006/00008 (extracted), debris negative control, synthetic
N=3 regression. When recall works, ship the dedicated example canvas in
examples/14_kintsugi/ per Libish ("make the unique cases examples in the
same folder").

## Harness quirks (save your session)

- run_csharp scripts: no `return x;` at top level; Console.WriteLine.
- GH_Document async poll: NewSolution(false) pumps; never force-expire.
- PLY import: scripted `_-Import "path" _Enter` (dialog hangs otherwise).
- Reflection into FacetMatchComponent private statics works and is the
  fast iteration loop (no rebuild): SegmentFacetsRoughness (8 args),
  TrimmedIcpMesh, TrimmedRegister, RigidFromPairs, SubMesh.
- Deploy: build Release, copy Frahan.StonePack.gha to
  %APPDATA%\Grasshopper\Libraries, close+respawn the slot.
